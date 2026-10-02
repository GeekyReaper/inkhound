using System.IO.Compression;
using System.Security.Cryptography;
using Foundation.Core;
using Foundation.Core.Model;
using Inkhound.Core.ComicArchiveGenerator;
using Inkhound.Core.DbStorage;
using Inkhound.Core.Export;
using Inkhound.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core;

/// <summary>
/// Module Export : génération d'un PDF/CBZ pour une issue ou d'un ZIP pour un volume (jobs), liste
/// des fichiers produits, suppression, et tickets de téléchargement à usage unique. Le nettoyage à
/// l'expiration est une tâche du planificateur (« Clean export », voir partial Scheduler).
/// </summary>
public partial class InkhoundManager
{
    /// <summary>Vue d'un fichier d'export pour l'API.</summary>
    /// <param name="ExpiresAt">Date de suppression prévue, ou <c>null</c> si le nettoyage planifié est désactivé.</param>
    public record ExportFileInfo(
        Guid Id, ExportTargetType TargetType, Guid TargetId, ExportFormat Format, string FileName,
        long SizeBytes, DateTime CreatedAt, DateTime? ExpiresAt, ExportStatus Status, Guid? JobId);

    // Un ticket ne sert qu'à lancer UN téléchargement par lien natif (le navigateur n'envoie pas le
    // JWT) : valable une minute, consommé à la première utilisation.
    private static readonly TimeSpan ExportTicketLifetime = TimeSpan.FromSeconds(60);

    // Un export « Pending » plus vieux que ça est considéré orphelin (redémarrage pendant le job).
    private static readonly TimeSpan StalePendingExport = TimeSpan.FromHours(2);

    private readonly ExpiringCache<string, Guid> _exportTickets =
        new("Export download tickets", ExportTicketLifetime, 64);

    #region Lancement

    /// <summary>
    /// Lance l'export d'une issue (PDF/CBZ) ou d'un volume (ZIP). Remplace l'export existant de la
    /// même cible et du même format. Le résultat est lu via <see cref="GetExportsAsync"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Cible introuvable.</exception>
    /// <exception cref="InvalidOperationException">Rien à exporter, ou export déjà en cours.</exception>
    public async Task<JobContext> LaunchJobExport(ExportJobParameters parameters)
    {
        var ctx = GetDb();
        string label;
        string fileName;
        var exportExtension = ExportService.GetExtension(parameters.Format);

        if (parameters.TargetType == ExportTargetType.Issue)
        {
            var issue = await ctx.Issues.FindAsync(parameters.TargetId)
                ?? throw new KeyNotFoundException("Issue not found.");
            if (string.IsNullOrEmpty(issue.CbzFilename))
                throw new InvalidOperationException("This issue has no file to export.");
            var volume = await ctx.Volumes.FindAsync(issue.VolumeId)
                ?? throw new KeyNotFoundException("Volume not found.");

            label = $"{volume.Title} #{issue.IssueNumber}";
            fileName = Path.GetFileNameWithoutExtension(ArchiveService.GetPath(issue, volume)) + exportExtension;
        }
        else
        {
            var volume = await ctx.Volumes.FindAsync(parameters.TargetId)
                ?? throw new KeyNotFoundException("Volume not found.");
            if (!await ctx.Issues.AnyAsync(i => i.VolumeId == volume.Id && i.CbzFilename != null && i.CbzFilename != ""))
                throw new InvalidOperationException("This volume has no issue file to export.");

            label = volume.Title;
            fileName = ArchiveService.GetPath(volume) + ".zip";
        }

        // Une seule entrée par (cible, format) : on remplace l'existante, sauf si elle se génère encore.
        var existing = await ctx.ExportFiles
            .Where(e => e.TargetType == parameters.TargetType && e.TargetId == parameters.TargetId && e.Format == parameters.Format)
            .ToListAsync();
        if (existing.Any(e => e.Status == ExportStatus.Pending && e.CreatedAt > DateTime.UtcNow - StalePendingExport))
            throw new InvalidOperationException("An export of this item is already in progress.");
        foreach (var old in existing)
            DeleteExportFilesOnDisk(old);
        ctx.ExportFiles.RemoveRange(existing);

        var export = new ExportFile
        {
            Id = Guid.NewGuid(),
            TargetType = parameters.TargetType,
            TargetId = parameters.TargetId,
            Format = parameters.Format,
            FileName = fileName,
            CreatedAt = DateTime.UtcNow,
            Status = ExportStatus.Pending
        };
        ctx.ExportFiles.Add(export);
        await ctx.SaveChangesAsync();

        var kind = parameters.TargetType == ExportTargetType.Volume ? "ZIP of " : string.Empty;
        var job = StartJob($"Export {kind}{parameters.Format.ToString().ToUpperInvariant()} — {label}", parameters);
        if (job.State == JobState.ERROR)
        {
            ctx.ExportFiles.Remove(export);
            await ctx.SaveChangesAsync();
            return job;
        }

        export.JobId = job.JobId;
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<ExportFile>(export.Id));

        job.SetState(JobState.RUNNING);
        _ = RunExportJobAsync(job, parameters, export.Id);
        return job;
    }

    private async Task RunExportJobAsync(JobContext job, ExportJobParameters parameters, Guid exportId)
    {
        var exportService = GetService<ExportService, ExportOptions>();
        string? finalPath = null;
        string? tmpPath = null;
        string? workDir = null;
        try
        {
            var ctx = GetDb();
            var export = await ctx.ExportFiles.FindAsync(exportId)
                ?? throw new InvalidOperationException("Export entry no longer exists.");

            Directory.CreateDirectory(exportService.ExportPath);
            var extension = parameters.TargetType == ExportTargetType.Volume ? ".zip" : ExportService.GetExtension(parameters.Format);
            finalPath = Path.Combine(exportService.ExportPath, exportId.ToString("N") + extension);
            tmpPath = finalPath + ".tmp";

            if (parameters.TargetType == ExportTargetType.Issue)
            {
                await ExportIssueAsync(job, ctx, exportService, parameters, tmpPath);
            }
            else
            {
                workDir = Path.Combine(exportService.ExportPath, $".work-{exportId:N}");
                await ExportVolumeAsync(job, ctx, exportService, parameters, tmpPath, workDir);
            }

            File.Move(tmpPath, finalPath, overwrite: true);
            export.SizeBytes = new FileInfo(finalPath).Length;
            export.Status = ExportStatus.Ready;
            await ctx.SaveChangesAsync();

            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<ExportFile>(exportId));
            JobSendTrace($"[Export] {export.FileName} ready ({export.SizeBytes / 1024 / 1024} MB)");
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Export] Failed: {ex.Message}", ETraceLevel.ERROR);
            try
            {
                if (tmpPath is not null && File.Exists(tmpPath)) File.Delete(tmpPath);
                if (finalPath is not null && File.Exists(finalPath)) File.Delete(finalPath);
                var ctx = GetDb();
                await ctx.ExportFiles.Where(e => e.Id == exportId).ExecuteDeleteAsync();
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<ExportFile>(exportId));
            }
            catch (Exception cleanupEx)
            {
                JobSendTrace($"[Export] Cleanup after failure failed: {cleanupEx.Message}", ETraceLevel.WARNING);
            }
            EndJob(false);
        }
        finally
        {
            if (workDir is not null && Directory.Exists(workDir))
            {
                try { Directory.Delete(workDir, recursive: true); } catch (IOException) { }
            }
        }
    }

    private async Task ExportIssueAsync(
        JobContext job, DbStorageContext ctx, ExportService exportService, ExportJobParameters parameters, string destPath)
    {
        var issue = await ctx.Issues.FindAsync(parameters.TargetId)
            ?? throw new KeyNotFoundException("Issue not found.");
        var volume = await ctx.Volumes.FindAsync(issue.VolumeId)
            ?? throw new KeyNotFoundException("Volume not found.");
        var library = await ctx.Libraries.FindAsync(volume.LibraryId)
            ?? throw new KeyNotFoundException("Library not found.");

        var source = ResolveIssueCbzPath(issue, volume, library)
            ?? throw new FileNotFoundException("The issue file is missing on disk.");

        JobSendTrace($"[Export] {Path.GetFileName(source)} → {parameters.Format.ToString().ToUpperInvariant()}");
        var title = string.IsNullOrWhiteSpace(issue.Title)
            ? $"{volume.Title} #{issue.IssueNumber}"
            : $"{volume.Title} #{issue.IssueNumber} — {issue.Title}";

        await exportService.BuildIssueFileAsync(source, destPath, parameters.Format, title,
            (total, done) => ReportExportProgress(job, total, done));
    }

    private async Task ExportVolumeAsync(
        JobContext job, DbStorageContext ctx, ExportService exportService, ExportJobParameters parameters,
        string destPath, string workDir)
    {
        var volume = await ctx.Volumes.FindAsync(parameters.TargetId)
            ?? throw new KeyNotFoundException("Volume not found.");
        var library = await ctx.Libraries.FindAsync(volume.LibraryId)
            ?? throw new KeyNotFoundException("Library not found.");

        var issues = await ctx.Issues
            .Where(i => i.VolumeId == volume.Id && i.CbzFilename != null && i.CbzFilename != "")
            .OrderBy(i => i.Category).ThenBy(i => i.IssueNumber)
            .ToListAsync();

        var entries = new List<(Issue Issue, string Source)>();
        foreach (var issue in issues)
        {
            var source = ResolveIssueCbzPath(issue, volume, library);
            if (source is null)
            {
                JobSendTrace($"[Export] Issue #{issue.IssueNumber}: file missing on disk — skipped", ETraceLevel.WARNING);
                continue;
            }
            entries.Add((issue, source));
        }
        if (entries.Count == 0)
            throw new InvalidOperationException("No issue file of this volume could be found on disk.");

        var extension = ExportService.GetExtension(parameters.Format);
        var passthrough = parameters.Format == ExportFormat.Cbz && exportService.IsPassthroughCbz;
        if (!passthrough) Directory.CreateDirectory(workDir);

        // Une étape de progression par issue : la progression fine par page n'a pas de sens ici.
        job.AddTotal(entries.Count);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);

        for (var i = 0; i < entries.Count; i++)
        {
            var (issue, source) = entries[i];
            var baseName = Path.GetFileNameWithoutExtension(source);
            var entryName = baseName + extension;
            for (var n = 2; !usedNames.Add(entryName); n++)
                entryName = $"{baseName} ({n}){extension}";

            var entrySource = source;
            if (!passthrough)
            {
                entrySource = Path.Combine(workDir, $"{i:D4}{extension}");
                JobSendTrace($"[Export] Issue #{issue.IssueNumber} → {parameters.Format.ToString().ToUpperInvariant()}");
                await exportService.BuildIssueFileAsync(source, entrySource, parameters.Format,
                    $"{volume.Title} #{issue.IssueNumber}");
            }

            // Les pages sont déjà compressées : stocker sans recompresser.
            zip.CreateEntryFromFile(entrySource, entryName, CompressionLevel.NoCompression);
            if (!passthrough) File.Delete(entrySource);

            job.SetProgress(new Progression { Completed = i + 1 });
        }
    }

    private static void ReportExportProgress(JobContext job, int total, int done)
    {
        if (job.Progress.Total != total) job.AddTotal(total);
        job.SetProgress(new Progression { Completed = done });
    }

    // Le CBZ peut porter le nom calculé depuis les métadonnées actuelles OU celui enregistré à
    // l'import (dérive possible) : on sonde les deux, comme DeleteIssueFileAsync.
    private static string? ResolveIssueCbzPath(Issue issue, Volume volume, Library library)
    {
        if (string.IsNullOrEmpty(issue.CbzFilename)) return null;
        var candidates = new[]
        {
            Path.Combine(ArchiveService.GetPath(volume, library), issue.CbzFilename),
            ArchiveService.GetPath(issue, volume, library)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    #endregion

    #region Lecture, suppression, téléchargement

    /// <summary>Exports d'une cible, du plus récent au plus ancien. Purge au passage les entrées dont le fichier a disparu.</summary>
    public async Task<List<ExportFileInfo>> GetExportsAsync(ExportTargetType targetType, Guid targetId)
    {
        var ctx = GetDb();
        var exportService = GetService<ExportService, ExportOptions>();
        var scheduler = GetService<SchedulerService, SchedulerOptions>();

        var rows = await ctx.ExportFiles
            .Where(e => e.TargetType == targetType && e.TargetId == targetId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        var missing = rows.Where(e => e.Status == ExportStatus.Ready && !File.Exists(GetExportDiskPath(exportService, e))).ToList();
        if (missing.Count > 0)
        {
            ctx.ExportFiles.RemoveRange(missing);
            await ctx.SaveChangesAsync();
            rows = rows.Except(missing).ToList();
        }

        // SQLite relit les dates en Kind=Unspecified : sans « Z » dans le JSON, le navigateur les
        // interpréterait en heure locale.
        return rows.Select(e =>
        {
            var created = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc);
            return new ExportFileInfo(
                e.Id, e.TargetType, e.TargetId, e.Format, e.FileName, e.SizeBytes, created,
                scheduler.CleanExportsEnabled ? created.AddDays(Math.Max(1, scheduler.CleanExportsMaxAgeDays)) : null,
                e.Status, e.JobId);
        }).ToList();
    }

    /// <summary>Supprime un export (fichier + entrée). Un export en cours de génération ne peut pas l'être.</summary>
    /// <exception cref="KeyNotFoundException">Export introuvable.</exception>
    /// <exception cref="InvalidOperationException">Export en cours de génération.</exception>
    public async Task DeleteExportAsync(Guid exportId)
    {
        var ctx = GetDb();
        var export = await ctx.ExportFiles.FindAsync(exportId)
            ?? throw new KeyNotFoundException("Export not found.");
        if (export.Status == ExportStatus.Pending && export.CreatedAt > DateTime.UtcNow - StalePendingExport)
            throw new InvalidOperationException("This export is still being generated.");

        DeleteExportFilesOnDisk(export);
        ctx.ExportFiles.Remove(export);
        await ctx.SaveChangesAsync();
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<ExportFile>(exportId));
    }

    /// <summary>Crée un ticket de téléchargement à usage unique pour un export prêt.</summary>
    /// <exception cref="KeyNotFoundException">Export introuvable ou fichier disparu.</exception>
    /// <exception cref="InvalidOperationException">Export pas encore prêt.</exception>
    public async Task<string> CreateExportTicketAsync(Guid exportId)
    {
        var export = await GetDb().ExportFiles.FindAsync(exportId)
            ?? throw new KeyNotFoundException("Export not found.");
        if (export.Status != ExportStatus.Ready)
            throw new InvalidOperationException("This export is not ready yet.");
        if (!File.Exists(GetExportDiskPath(GetService<ExportService, ExportOptions>(), export)))
            throw new KeyNotFoundException("The export file no longer exists.");

        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _exportTickets.Set(ticket, exportId);
        return ticket;
    }

    /// <summary>
    /// Consomme un ticket et retourne le fichier à servir, ou <c>null</c> si le ticket est inconnu,
    /// expiré ou déjà utilisé (ou si le fichier a disparu).
    /// </summary>
    public async Task<(string Path, string FileName, string ContentType)?> OpenExportByTicketAsync(string ticket)
    {
        if (!_exportTickets.TryGet(ticket, out var exportId)) return null;
        _exportTickets.Remove(ticket);

        var export = await GetDb().ExportFiles.FindAsync(exportId);
        if (export is null || export.Status != ExportStatus.Ready) return null;

        var path = GetExportDiskPath(GetService<ExportService, ExportOptions>(), export);
        return File.Exists(path)
            ? (path, export.FileName, ExportService.GetContentType(export.TargetType, export.Format))
            : null;
    }

    private static string GetExportDiskPath(ExportService exportService, ExportFile export)
    {
        var extension = export.TargetType == ExportTargetType.Volume ? ".zip" : ExportService.GetExtension(export.Format);
        return Path.Combine(exportService.ExportPath, export.Id.ToString("N") + extension);
    }

    private void DeleteExportFilesOnDisk(ExportFile export)
    {
        var path = GetExportDiskPath(GetService<ExportService, ExportOptions>(), export);
        foreach (var candidate in new[] { path, path + ".tmp" })
        {
            try
            {
                if (File.Exists(candidate)) File.Delete(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                JobSendTrace($"[Export] Could not delete {Path.GetFileName(candidate)}: {ex.Message}", ETraceLevel.WARNING);
            }
        }
    }

    #endregion

    #region Nettoyage planifié

    // Tâche « Clean export » : supprime tous les exports plus vieux que la durée de vie configurée
    // (CleanExportsMaxAgeDays), puis les fichiers du dossier qui n'appartiennent à aucune entrée.
    private async Task RunScheduledCleanExportsAsync()
    {
        var maxAgeDays = Math.Max(1, GetService<SchedulerService, SchedulerOptions>().CleanExportsMaxAgeDays);
        var job = StartJob("Export — clean old files");
        if (job.State == JobState.ERROR) return;
        job.SetState(JobState.RUNNING);

        try
        {
            var threshold = DateTime.UtcNow.AddDays(-maxAgeDays);
            var (entries, orphans) = await CleanExportsAsync(threshold);
            JobSendTrace($"[Export] Clean: {entries} export(s) older than {maxAgeDays} day(s) and {orphans} orphan file(s) removed");
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Export] Clean failed: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    private async Task<(int Entries, int Orphans)> CleanExportsAsync(DateTime thresholdUtc)
    {
        var ctx = GetDb();
        var exportService = GetService<ExportService, ExportOptions>();

        var expired = await ctx.ExportFiles.Where(e => e.CreatedAt < thresholdUtc).ToListAsync();
        foreach (var export in expired)
            DeleteExportFilesOnDisk(export);
        if (expired.Count > 0)
        {
            ctx.ExportFiles.RemoveRange(expired);
            await ctx.SaveChangesAsync();
            foreach (var export in expired)
                OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<ExportFile>(export.Id));
        }

        // Orphelins : fichiers (ou dossiers .work-*) sans entrée en base, laissés par un crash ou un
        // redémarrage en cours de job. Délai de grâce d'une heure pour ne pas toucher un job en cours.
        var orphans = 0;
        var dir = exportService.ExportPath;
        if (Directory.Exists(dir))
        {
            var known = (await ctx.ExportFiles.Select(e => e.Id).ToListAsync()).Select(id => id.ToString("N")).ToHashSet();
            var grace = DateTime.UtcNow.AddHours(-1);

            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
            {
                var name = Path.GetFileName(path);
                if (name == ".write-test") continue;
                var idPart = name.TrimStart('.').Replace("work-", string.Empty);
                idPart = idPart.Split('.')[0];
                if (known.Contains(idPart)) continue;
                if (Directory.GetLastWriteTimeUtc(path) > grace && File.GetLastWriteTimeUtc(path) > grace) continue;

                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else File.Delete(path);
                    orphans++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    JobSendTrace($"[Export] Could not delete orphan {name}: {ex.Message}", ETraceLevel.WARNING);
                }
            }
        }

        return (expired.Count, orphans);
    }

    #endregion
}
