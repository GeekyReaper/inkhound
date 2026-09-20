using Foundation.Core.Model;
using Inkhound.Core.Analysis;
using Inkhound.Core.Models;
using Inkhound.Core.Prowlarr;
using Inkhound.Core.QBittorrent;
using Inkhound.Core.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core;

/// <summary>
/// Job « Auto search » : acquisition automatique, via Prowlarr puis qBittorrent, des issues
/// <see cref="IssueCategory.Standard"/> encore <see cref="IssueStatus.MISSING"/> d'un volume.
/// Lancé séquentiellement par la tâche <c>AutoSearch</c> du scheduler (voir
/// <c>inkhoundManager.Scheduler.cs</c>). Seuls les résultats torrent dont le score atteint
/// <see cref="AutoSearchVolumeJobParameters.MinScore"/> sont considérés, du plus haut au plus bas.
/// </summary>
public partial class InkhoundManager
{
    // Attente maximale des métadonnées d'un PACK (liste des fichiers) au-delà des 10 s déjà
    // consenties par GrabPackSelectiveAsync, et intervalle de polling.
    private static readonly TimeSpan AutoSearchPackMetadataTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AutoSearchPackMetadataPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Lance et <b>attend</b> le job d'auto search d'un volume. Contrairement aux autres
    /// <c>LaunchJobXxx</c>, pas de fire-and-forget : l'appelant (scheduler) veut traiter ses volumes
    /// un par un pour ne pas marteler Prowlarr / qBittorrent.
    /// </summary>
    public async Task LaunchJobAutoSearchVolume(AutoSearchVolumeJobParameters parameters)
    {
        var volume = await GetDb().Volumes.FindAsync(parameters.VolumeId);
        var jobTitle = volume is not null
            ? $"Auto search — {volume.Title}"
            : $"Auto search — {parameters.VolumeId}";

        var job = StartJob(jobTitle, parameters);
        if (job.State == JobState.ERROR) return;

        job.SetState(JobState.RUNNING);
        await RunAutoSearchVolumeJobAsync(job, parameters);
    }

    // État partagé d'une exécution : issues encore manquantes, URLs de torrents déjà suivis (jamais
    // re-grabés) ou rejetés dans ce job (PACK sans issue utile), cache des réponses Prowlarr.
    private sealed class AutoSearchRun(Volume volume, List<Issue> missing, HashSet<string> knownUrls, int minScore)
    {
        public Volume Volume { get; } = volume;
        public List<Issue> Missing { get; } = missing;
        public HashSet<string> KnownUrls { get; } = knownUrls;
        public HashSet<string> RejectedUrls { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ProwlarrSearchResult>> QueryCache { get; } = new();
        public int MinScore { get; } = minScore;
        public int Acquired { get; set; }

        // `banned` est exclu explicitement : un ban ramène déjà le score à 0, donc sous le MinScore
        // par défaut (70) — mais un seuil abaissé à 0 ne doit pas rouvrir la porte à un torrent que
        // l'utilisateur a écarté à la main.
        public bool IsEligible(ProwlarrSearchResult r, float score, bool banned)
            => !banned
               && score >= MinScore
               && string.Equals(r.Protocol, "torrent", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(r.DownloadUrl)
               && !KnownUrls.Contains(r.DownloadUrl)
               && !RejectedUrls.Contains(r.DownloadUrl);
    }

    private async Task RunAutoSearchVolumeJobAsync(JobContext job, AutoSearchVolumeJobParameters parameters)
    {
        var ctx = GetDb();
        try
        {
            var volume = await ctx.Volumes.FindAsync(parameters.VolumeId);
            if (volume is null)
            {
                JobSendTrace("[AutoSearch] Volume not found", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var missing = await ctx.Issues
                .Where(i => i.VolumeId == volume.Id
                    && i.Category == IssueCategory.Standard
                    && i.Status == IssueStatus.MISSING)
                .OrderBy(i => i.IssueNumber)
                .ToListAsync();

            if (missing.Count == 0)
            {
                JobSendTrace("[AutoSearch] No missing Standard issue — nothing to do");
                EndJob(true);
                return;
            }

            var prowlarr = GetService<ProwlarrService, ProwlarrOptions>();
            var qb = GetService<QBittorrentService, QBittorrentOptions>();
            if (prowlarr.CurrentState.State != EState.OK || qb.CurrentState.State != EState.OK)
            {
                JobSendTrace("[AutoSearch] Prowlarr or QBittorrent service unavailable", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            var knownUrls = (await ctx.IssueDownloads
                    .Where(d => d.DownloadUrl != "")
                    .Select(d => d.DownloadUrl)
                    .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Torrents bannis pour une issue de ce volume (suppression manuelle d'un download) —
            // chargés une fois, servent aux deux phases : la phase A raisonne sur tout le volume,
            // la phase B se restreint à l'issue courante.
            var volumeBans = await LoadBanIndexForVolumeAsync(volume.Id);

            var run = new AutoSearchRun(volume, missing, knownUrls, parameters.MinScore);
            var (indexerIds, saved) = await ResolveIndexersAsync(ctx, volume, null);

            // Progression : chaque requête Prowlarr compte pour 1 (cascade volume + une cascade par
            // issue manquante). Les issues couvertes en chemin sautent leur cascade : le total est
            // ajusté à la baisse au fil de l'eau pour garder une barre cohérente.
            var volumeQueries = BuildSearchQueries(volume);
            job.CallbackHandler.UpdateTotal(volumeQueries.Count
                + missing.Sum(i => BuildSearchQueries(volume, i).Count));

            JobSendTrace($"[AutoSearch] \"{volume.Title}\" — {missing.Count} missing Standard issue(s): "
                + string.Join(", ", missing.Select(i => $"#{i.IssueNumber}"))
                + $" — minimum score {parameters.MinScore}");

            // ── Phase A : recherche au niveau volume (capte les PACK et les SINGLE en une cascade) ──
            var merged = await SearchProwlarrCascadeAsync(job, prowlarr, volumeQueries, indexerIds, saved, run.QueryCache);
            var volumeCandidates = ScoringVolumePack.ScoreAndSort(volume, missing, merged, volumeBans)
                .Where(c => run.IsEligible(c.Result, c.Score, c.Banned))
                .ToList();

            JobSendTrace($"[AutoSearch] Volume search: {merged.Count} result(s), {volumeCandidates.Count} eligible candidate(s)");

            foreach (var candidate in volumeCandidates)
            {
                if (run.Missing.Count == 0) break;
                await TryAcquireAsync(run, candidate.Result, candidate.Analysis, candidate.Score);
            }

            // ── Phase B : une cascade par issue encore manquante ──
            foreach (var issue in run.Missing.ToList())
            {
                if (!run.Missing.Contains(issue))
                {
                    // Couverte entre-temps par un PACK : sa cascade n'est plus nécessaire.
                    job.CallbackHandler.UpdateTotal(job.Progress.Total - BuildSearchQueries(volume, issue).Count);
                    continue;
                }

                var issueQueries = BuildSearchQueries(volume, issue);
                var issueMerged = await SearchProwlarrCascadeAsync(job, prowlarr, issueQueries, indexerIds, saved, run.QueryCache);
                var issueBans = await LoadBanIndexForIssueAsync(issue.Id);
                var issueCandidates = ScoringTorrent.ScoreAndSort(volume, issue, issueMerged, issueBans)
                    .Where(c => run.IsEligible(c.Result, c.Score, c.Banned))
                    .ToList();

                JobSendTrace($"[AutoSearch] Issue #{issue.IssueNumber}: {issueMerged.Count} result(s), {issueCandidates.Count} eligible candidate(s)");

                foreach (var candidate in issueCandidates)
                {
                    await TryAcquireAsync(run, candidate.Result, candidate.Analysis, candidate.Score);
                    if (!run.Missing.Contains(issue)) break;
                }

                if (run.Missing.Contains(issue))
                    JobSendTrace($"[AutoSearch] Issue #{issue.IssueNumber}: no acceptable torrent found", ETraceLevel.WARNING);
            }

            JobSendTrace($"[AutoSearch] Done — {run.Acquired} issue(s) sent to download, {run.Missing.Count} still missing");
            OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<Volume>(volume.Id));
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[AutoSearch] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }

    // Tente d'acquérir un candidat. SINGLE "#n" : grab direct si n est manquant. PACK : ajout en
    // pause, lecture des fichiers, appariement par numéro ; sans fichier utile le torrent est
    // supprimé et le candidat suivant est tenté. Toute exception est absorbée (tracée en WARNING)
    // pour qu'un torrent cassé n'interrompe pas le job. Retourne true si ≥1 issue a été retirée de
    // la liste des manquantes.
    private async Task<bool> TryAcquireAsync(AutoSearchRun run, ProwlarrSearchResult result, TorrentAnalysis analysis, float score)
    {
        try
        {
            return analysis.Type switch
            {
                "SINGLE" => await TryAcquireSingleAsync(run, result, analysis, score),
                "PACK" => await TryAcquirePackAsync(run, result, score),
                _ => false
            };
        }
        catch (Exception ex)
        {
            JobSendTrace($"[AutoSearch] Skipping \"{result.Title}\": {ex.Message}", ETraceLevel.WARNING);
            return false;
        }
    }

    private async Task<bool> TryAcquireSingleAsync(AutoSearchRun run, ProwlarrSearchResult result, TorrentAnalysis analysis, float score)
    {
        // Label "#n" — un SINGLE au numéro inconnu ("?") n'est jamais grabé à l'aveugle.
        if (!analysis.Label.StartsWith('#') || !int.TryParse(analysis.Label[1..], out var number))
            return false;

        var issue = run.Missing.FirstOrDefault(i => i.IssueNumber == number);
        if (issue is null) return false;

        JobSendTrace($"[AutoSearch] SINGLE #{number} — grabbing \"{result.Title}\" (score {score:0})");
        var (success, _) = await GrabToQBittorrentAsync(result.DownloadUrl!, result.Title, result.Indexer, issue.Id);
        if (!success)
        {
            JobSendTrace($"[AutoSearch] Failed to add \"{result.Title}\" to QBittorrent", ETraceLevel.WARNING);
            return false;
        }

        run.Missing.Remove(issue);
        run.KnownUrls.Add(result.DownloadUrl!);
        run.Acquired++;
        return true;
    }

    private async Task<bool> TryAcquirePackAsync(AutoSearchRun run, ProwlarrSearchResult result, float score)
    {
        var url = result.DownloadUrl!;
        JobSendTrace($"[AutoSearch] PACK — inspecting \"{result.Title}\" (score {score:0})");

        var (success, hash, files) = await GrabPackSelectiveAsync(url);
        if (!success || hash is null)
        {
            JobSendTrace($"[AutoSearch] Failed to add \"{result.Title}\" to QBittorrent", ETraceLevel.WARNING);
            run.RejectedUrls.Add(url);
            return false;
        }

        // Métadonnées pas encore chargées après les 10 s de GrabPackSelectiveAsync : on patiente
        // encore un peu, puis on abandonne le torrent plutôt que de bloquer le lot.
        if (files is null)
        {
            var deadline = DateTime.UtcNow + AutoSearchPackMetadataTimeout;
            while (files is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(AutoSearchPackMetadataPollInterval);
                var status = await GetPackFetchStatusAsync(hash);
                if (status is { MetadataReady: true })
                    files = [.. status.Files];
            }
        }

        if (files is null)
        {
            JobSendTrace($"[AutoSearch] PACK \"{result.Title}\" — metadata never became available, cancelled", ETraceLevel.WARNING);
            await CancelPackAsync(hash);
            run.RejectedUrls.Add(url);
            return false;
        }

        var matches = MatchPackFilesToMissingIssues(files, run.Missing);
        if (matches.Count == 0)
        {
            JobSendTrace($"[AutoSearch] PACK \"{result.Title}\" contains none of the missing issues — cancelled", ETraceLevel.WARNING);
            await CancelPackAsync(hash);
            run.RejectedUrls.Add(url);
            return false;
        }

        JobSendTrace($"[AutoSearch] PACK \"{result.Title}\" — {matches.Count} file(s) matched: "
            + string.Join(", ", matches.Select(m => $"#{m.Issue.IssueNumber} ← {m.File.Name}")));

        // ApplyPackSelectionAsync refait lui-même l'appariement (issues Standard MISSING uniquement)
        // et crée les IssueDownload ; les issues déjà grabées en SINGLE sont DOWNLOADING, donc
        // jamais ré-appariées.
        var applied = await ApplyPackSelectionAsync(
            hash, url, result.Title, result.Indexer,
            issueId: null, volumeId: run.Volume.Id,
            selectedFileIndices: [.. matches.Select(m => m.File.Index)],
            fileIssueOverrides: null);

        if (!applied)
        {
            JobSendTrace($"[AutoSearch] Failed to apply file selection on \"{result.Title}\" — cancelled", ETraceLevel.WARNING);
            await CancelPackAsync(hash);
            run.RejectedUrls.Add(url);
            return false;
        }

        foreach (var (_, issue) in matches)
            run.Missing.Remove(issue);
        run.KnownUrls.Add(url);
        run.Acquired += matches.Count;
        return true;
    }

    private async Task CancelPackAsync(string hash)
    {
        var (ok, error) = await AbortPackSelectionAsync(hash);
        if (!ok)
            JobSendTrace($"[AutoSearch] Could not remove torrent {hash} from QBittorrent: {error}", ETraceLevel.WARNING);
    }

    /// <summary>
    /// Apparie les fichiers d'archive d'un torrent aux issues manquantes par numéro de tome
    /// (<see cref="TorrentTypeAnalyzer.ExtractIssueNumber"/>). Un seul fichier par issue (le premier
    /// rencontré) ; les fichiers non-archive ou au numéro absent de la liste sont ignorés. Pur, testable.
    /// </summary>
    internal static List<(QBittorrentTorrentFile File, Issue Issue)> MatchPackFilesToMissingIssues(
        IEnumerable<QBittorrentTorrentFile> files, IReadOnlyCollection<Issue> missingIssues)
    {
        var byNumber = missingIssues
            .GroupBy(i => i.IssueNumber)
            .ToDictionary(g => g.Key, g => g.First());
        var matched = new HashSet<Guid>();
        List<(QBittorrentTorrentFile, Issue)> result = [];

        foreach (var file in files)
        {
            if (!IsArchiveFile(file.Name)) continue;
            if (TorrentTypeAnalyzer.ExtractIssueNumber(file.Name) is not { } number) continue;
            if (!byNumber.TryGetValue(number, out var issue) || !matched.Add(issue.Id)) continue;
            result.Add((file, issue));
        }

        return result;
    }
}
