using Foundation.Core.Model;
using Inkhound.Core.Bedetheque;
using Inkhound.Core.Bedetheque.Catalog;
using Inkhound.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core;

/// <summary>
/// Catalogue local des séries Bedetheque : chargement de l'index mémoire au démarrage, état par
/// lettre, et job de rafraîchissement (scraping des pages d'index alphabétique du site, une lettre
/// à la fois, persisté dans la table <c>BedethequeCatalogSeries</c>). Déclenché à la main (page
/// <c>/settings/bedetheque</c>) ou par la tâche <c>BedethequeCatalog</c> du scheduler.
/// </summary>
public partial class InkhoundManager
{
    /// <summary>Les 27 lettres de l'index alphabétique du site : <c>0</c> (chiffres/symboles) puis <c>A</c>…<c>Z</c>.</summary>
    public static readonly string[] BedethequeCatalogLetters =
        ["0", .. Enumerable.Range('A', 26).Select(c => ((char)c).ToString())];

    // Un seul rafraîchissement à la fois (manuel ou planifié) : deux jobs concurrents scraperaient
    // le site en double et se marcheraient dessus sur les mêmes lettres.
    private int _catalogRefreshRunning;

    private BedethequeSourceService Bedetheque => GetService<BedethequeSourceService, BedethequeOptions>();

    /// <summary>
    /// Recharge l'index mémoire de <see cref="BedethequeSourceService"/> depuis la base. Appelé
    /// en fin de <see cref="AutomaticLoadServices"/> et après chaque job de rafraîchissement.
    /// </summary>
    public async Task LoadBedethequeCatalogAsync()
    {
        var entries = await GetDb().BedethequeCatalog.AsNoTracking().ToListAsync();
        Bedetheque.LoadCatalog(entries);
        JobSendTrace($"[Bedetheque] Catalog loaded — {entries.Count} series");
    }

    /// <summary>État du catalogue : totaux et détail des 27 lettres (jamais chargée = 0 / null).</summary>
    public async Task<BedethequeCatalogStatus> GetBedethequeCatalogStatusAsync()
    {
        var perLetter = await GetDb().BedethequeCatalog
            .GroupBy(e => e.Letter)
            .Select(g => new { Letter = g.Key, Count = g.Count(), FetchedAtUtc = g.Max(e => e.FetchedAtUtc) })
            .ToListAsync();

        var letters = BedethequeCatalogLetters
            .Select(l =>
            {
                var found = perLetter.FirstOrDefault(p => p.Letter == l);
                return new BedethequeCatalogLetterStatus(l, found?.Count ?? 0, found?.FetchedAtUtc);
            })
            .ToList();

        var fetched = perLetter.Select(p => p.FetchedAtUtc).ToList();
        return new BedethequeCatalogStatus(
            Bedetheque.IsCatalogLoaded,
            perLetter.Sum(p => p.Count),
            fetched.Count > 0 ? fetched.Min() : null,
            fetched.Count > 0 ? fetched.Max() : null,
            _catalogRefreshRunning == 1,
            letters);
    }

    /// <summary>
    /// Lance le job de rafraîchissement du catalogue et retourne immédiatement son
    /// <see cref="JobContext"/> (le contrôleur renvoie le <c>jobId</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">Un rafraîchissement est déjà en cours.</exception>
    public async Task<JobContext> LaunchJobRefreshBedethequeCatalog(RefreshBedethequeCatalogJobParameters parameters)
    {
        var letters = await AcquireCatalogRefreshAsync(parameters);

        // StartJob doit être appelé ici (pas dans un helper async) : le job courant est un
        // AsyncLocal, qui ne remonte pas d'une méthode awaitée vers son appelant.
        var job = StartJob(BuildCatalogJobTitle(letters), parameters);
        if (job.State == JobState.ERROR)
        {
            Interlocked.Exchange(ref _catalogRefreshRunning, 0);
            return job;
        }

        job.SetState(JobState.RUNNING);
        _ = RunRefreshBedethequeCatalogJobAsync(job, letters);
        return job;
    }

    // Variante planifiée : même job, mais awaité pour que la garde _schedulerBusy couvre toute
    // l'exécution (sinon la tâche serait considérée terminée dès le lancement).
    private async Task RunScheduledBedethequeCatalogAsync()
    {
        var letterCount = GetService<SchedulerService, SchedulerOptions>().BedethequeCatalogLetterCount;
        if (letterCount < 1)
        {
            JobSendTrace("[Scheduler] Bedetheque catalog letter count < 1 — nothing to do", ETraceLevel.WARNING);
            return;
        }

        var parameters = new RefreshBedethequeCatalogJobParameters { LetterCount = letterCount };
        List<string> letters;
        try
        {
            letters = await AcquireCatalogRefreshAsync(parameters);
        }
        catch (InvalidOperationException ex)
        {
            JobSendTrace($"[Scheduler] Bedetheque catalog — {ex.Message}", ETraceLevel.WARNING);
            return;
        }

        var job = StartJob(BuildCatalogJobTitle(letters), parameters);
        if (job.State == JobState.ERROR)
        {
            Interlocked.Exchange(ref _catalogRefreshRunning, 0);
            return;
        }

        job.SetState(JobState.RUNNING);
        await RunRefreshBedethequeCatalogJobAsync(job, letters);
    }

    private static string BuildCatalogJobTitle(List<string> letters)
        => $"Bedetheque catalog — {letters.Count} letter(s): {string.Join(", ", letters)}";

    // Prend le verrou de rafraîchissement puis résout les lettres à traiter. Le verrou est libéré
    // par RunRefreshBedethequeCatalogJobAsync (ou par l'appelant si StartJob échoue).
    private async Task<List<string>> AcquireCatalogRefreshAsync(RefreshBedethequeCatalogJobParameters parameters)
    {
        if (Interlocked.CompareExchange(ref _catalogRefreshRunning, 1, 0) != 0)
            throw new InvalidOperationException("A Bedetheque catalog refresh is already running.");

        try
        {
            return await ResolveCatalogLettersAsync(parameters);
        }
        catch
        {
            Interlocked.Exchange(ref _catalogRefreshRunning, 0);
            throw;
        }
    }

    // Lettres explicites (normalisées en majuscules, dans l'ordre de l'index), sinon rotation :
    // jamais chargées d'abord, puis les plus anciennes.
    private async Task<List<string>> ResolveCatalogLettersAsync(RefreshBedethequeCatalogJobParameters parameters)
    {
        if (parameters.Letters is { Count: > 0 })
        {
            var wanted = parameters.Letters.Select(l => l.ToUpperInvariant()).ToHashSet();
            return BedethequeCatalogLetters.Where(wanted.Contains).ToList();
        }

        var status = await GetBedethequeCatalogStatusAsync();
        return status.Letters
            .OrderBy(l => l.FetchedAtUtc ?? DateTime.MinValue)
            .ThenBy(l => Array.IndexOf(BedethequeCatalogLetters, l.Letter))
            .Take(parameters.LetterCount ?? BedethequeCatalogLetters.Length)
            .Select(l => l.Letter)
            .ToList();
    }

    private async Task RunRefreshBedethequeCatalogJobAsync(JobContext job, List<string> letters)
    {
        try
        {
            var bedetheque = Bedetheque;
            if ((await bedetheque.GetState()).State != EState.OK)
            {
                JobSendTrace("[Bedetheque] Service unavailable — catalog refresh aborted", ETraceLevel.ERROR);
                EndJob(false);
                return;
            }

            job.CallbackHandler.UpdateTotal(letters.Count);
            var errors = 0;

            foreach (var letter in letters)
            {
                try
                {
                    JobSendTrace($"[Bedetheque] Fetching catalog page '{letter}'…");
                    var entries = await bedetheque.FetchCatalogLetterAsync(letter);

                    if (entries.Count == 0)
                    {
                        // Page vide = très probablement un parsing raté (le site a toujours des
                        // séries pour chaque lettre) : on garde le contenu précédent.
                        JobSendTrace($"[Bedetheque] Letter '{letter}' returned no series — previous content kept", ETraceLevel.WARNING);
                        errors++;
                        job.Progress.Increment(false);
                    }
                    else
                    {
                        await ReplaceCatalogLetterAsync(letter, entries);
                        JobSendTrace($"[Bedetheque] Letter '{letter}' — {entries.Count} series stored");
                        job.Progress.Increment(true);
                    }
                }
                catch (BedethequeBlockedException ex)
                {
                    // Bloqué par le site : inutile d'insister sur les lettres suivantes.
                    JobSendTrace($"[Bedetheque] Blocked while fetching letter '{letter}': {ex.Message} — refresh stopped", ETraceLevel.ERROR);
                    errors++;
                    job.Progress.Increment(false);
                    job.CallbackHandler.Callback(job.Progress);
                    break;
                }
                catch (Exception ex)
                {
                    JobSendTrace($"[Bedetheque] Letter '{letter}' failed: {ex.Message}", ETraceLevel.WARNING);
                    errors++;
                    job.Progress.Increment(false);
                }
                job.CallbackHandler.Callback(job.Progress);
            }

            await LoadBedethequeCatalogAsync();
            EndJob(errors == 0);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Bedetheque] Unexpected error: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
        finally
        {
            Interlocked.Exchange(ref _catalogRefreshRunning, 0);
        }
    }

    // Remplace atomiquement le contenu d'une lettre : une série ayant changé de lettre d'index
    // (titre renommé) est aussi purgée par Id pour ne pas violer la clé primaire.
    private async Task ReplaceCatalogLetterAsync(string letter, List<BedethequeCatalogEntry> entries)
    {
        var ctx = GetDb();
        await using var tx = await ctx.Database.BeginTransactionAsync();

        await ctx.BedethequeCatalog.Where(e => e.Letter == letter).ExecuteDeleteAsync();
        var ids = entries.Select(e => e.Id).ToList();
        await ctx.BedethequeCatalog.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync();

        ctx.BedethequeCatalog.AddRange(entries);
        await ctx.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
