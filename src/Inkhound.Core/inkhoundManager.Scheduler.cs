using System.Collections.Concurrent;
using Cronos;
using Foundation.Core.Model;
using Inkhound.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core;

/// <summary>
/// Planificateur de jobs récurrents. Une boucle <see cref="PeriodicTimer"/> (60 s) démarrée au
/// chargement des services évalue, à chaque tick, les expressions cron des tâches activées et
/// lance le job correspondant en tâche de fond — mêmes jobs, mêmes traces SignalR qu'un
/// déclenchement manuel. Purement backend : aucun client connecté n'est requis.
/// </summary>
public partial class InkhoundManager
{
    /// <summary>État d'une tâche planifiée.</summary>
    /// <param name="Enabled">La tâche est activée.</param>
    /// <param name="Cron">Expression cron configurée (heure serveur).</param>
    /// <param name="LastRunUtc">Dernier déclenchement effectif depuis le démarrage, ou <c>null</c>.</param>
    /// <param name="NextRunUtc">Prochain déclenchement prévu, ou <c>null</c> si désactivée / cron invalide.</param>
    /// <param name="Running">Une occurrence est en cours d'exécution.</param>
    public record SchedulerTaskStatus(
        bool Enabled, string Cron, DateTime? LastRunUtc, DateTime? NextRunUtc, bool Running);

    /// <summary>État complet du planificateur.</summary>
    /// <param name="ProcessDownloads">Tâche d'import des downloads.</param>
    /// <param name="RollingRefresh">Tâche de rolling refresh des volumes.</param>
    /// <param name="RollingRefreshBatchSize">Nombre de volumes traités par exécution du rolling refresh.</param>
    public record SchedulerStatus(
        SchedulerTaskStatus ProcessDownloads, SchedulerTaskStatus RollingRefresh, int RollingRefreshBatchSize);

    /// <summary>Clé de la tâche « import des downloads ».</summary>
    public const string SchedulerTaskProcessDownloads = "ProcessDownloads";

    /// <summary>Clé de la tâche « rolling refresh des volumes ».</summary>
    public const string SchedulerTaskRollingRefresh = "RollingRefresh";

    /// <summary>Indique si <paramref name="cron"/> est une expression cron 5 champs valide (parsing Cronos).</summary>
    public static bool IsValidCronExpression(string? cron)
        => !string.IsNullOrWhiteSpace(cron) && CronExpression.TryParse(cron, out _);

    // Empêche qu'une même tâche soit relancée alors que son occurrence précédente tourne encore.
    private readonly ConcurrentDictionary<string, bool> _schedulerBusy = new();

    // Dernier lancement effectif par tâche (UTC) — en mémoire, repart à vide après un redémarrage.
    private readonly ConcurrentDictionary<string, DateTime> _schedulerLastRun = new();

    private readonly object _schedulerStartLock = new();
    private bool _schedulerStarted;

    /// <summary>
    /// Démarre la boucle de planification si elle ne tourne pas déjà. Idempotent — appelé en fin de
    /// <see cref="AutomaticLoadServices"/>.
    /// </summary>
    public void StartScheduler()
    {
        lock (_schedulerStartLock)
        {
            if (_schedulerStarted) return;
            _schedulerStarted = true;
        }

        _ = Task.Run(SchedulerLoopAsync);
    }

    private async Task SchedulerLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        // Point de départ = maintenant : un créneau manqué pendant que l'app était éteinte n'est
        // pas rejoué au démarrage.
        var lastCheckUtc = DateTime.UtcNow;

        while (await timer.WaitForNextTickAsync())
        {
            var nowUtc = DateTime.UtcNow;
            try
            {
                var scheduler = GetService<SchedulerService, SchedulerOptions>();

                EvaluateScheduledTask(
                    SchedulerTaskProcessDownloads,
                    scheduler.ProcessDownloadsEnabled, scheduler.ProcessDownloadsCron,
                    lastCheckUtc, nowUtc, RunScheduledProcessDownloadsAsync);

                EvaluateScheduledTask(
                    SchedulerTaskRollingRefresh,
                    scheduler.RollingRefreshEnabled, scheduler.RollingRefreshCron,
                    lastCheckUtc, nowUtc, RunScheduledRollingRefreshAsync);
            }
            catch (Exception ex)
            {
                JobSendTrace($"[Scheduler] Loop error: {ex.Message}", ETraceLevel.ERROR);
            }
            finally
            {
                lastCheckUtc = nowUtc;
            }
        }
    }

    // Lance la tâche si une occurrence cron tombe dans la fenêtre (fromUtc, nowUtc].
    private void EvaluateScheduledTask(
        string key, bool enabled, string cron, DateTime fromUtc, DateTime nowUtc, Func<Task> action)
    {
        if (!enabled) return;
        if (!CronExpression.TryParse(cron, out var expr) || expr is null) return;

        var occurrence = expr.GetNextOccurrence(fromUtc, TimeZoneInfo.Local);
        if (occurrence is null || occurrence.Value > nowUtc) return;

        FireScheduledTask(key, action);
    }

    // Exécution détachée avec garde de ré-entrance — la boucle 60 s n'est jamais bloquée.
    private void FireScheduledTask(string key, Func<Task> action)
    {
        if (!_schedulerBusy.TryAdd(key, true))
        {
            JobSendTrace($"[Scheduler] {key} still running — occurrence skipped", ETraceLevel.WARNING);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                _schedulerLastRun[key] = DateTime.UtcNow;
                JobSendTrace($"[Scheduler] Triggering {key}");
                await action();
            }
            catch (Exception ex)
            {
                JobSendTrace($"[Scheduler] {key} failed: {ex.Message}", ETraceLevel.ERROR);
            }
            finally
            {
                _schedulerBusy.TryRemove(key, out _);
            }
        });
    }

    private Task RunScheduledProcessDownloadsAsync()
        => LaunchJobProcessDownloads(new ProcessDownloadsJobParameters());

    // Rolling refresh : synchronise « NEW only » un lot des volumes les moins récemment mis à jour
    // depuis leur source (LastRefreshedAt asc, NULL en premier). Les volumes manuels sont exclus.
    // Chaque volume reçoit un job "Refresh — {titre}" indépendant (fire-and-forget, comme
    // LaunchJobsRefreshLibrary) ; LastRefreshedAt est estampillé AVANT le lancement pour garantir
    // la rotation même si un refresh échoue ou si le process redémarre en cours de lot.
    private async Task RunScheduledRollingRefreshAsync()
    {
        var batchSize = GetService<SchedulerService, SchedulerOptions>().RollingRefreshBatchSize;
        if (batchSize < 1)
        {
            JobSendTrace("[Scheduler] Rolling refresh batch size < 1 — nothing to do", ETraceLevel.WARNING);
            return;
        }

        var ctx = GetDb();
        var volumes = await ctx.Volumes
            .Where(v => v.SourceType != "manual")
            .OrderBy(v => v.LastRefreshedAt)
            .ThenBy(v => v.CreatedAt)
            .Take(batchSize)
            .ToListAsync();

        if (volumes.Count == 0)
        {
            JobSendTrace("[Scheduler] Rolling refresh — no eligible volume");
            return;
        }

        // Estampille AVANT de lancer (commit immédiat) : garantit la rotation même si un refresh
        // échoue ou si le process redémarre en cours de lot. ExecuteUpdate → un seul UPDATE ciblé
        // sur LastRefreshedAt, aucun risque de réécrire les autres colonnes.
        var now = DateTime.UtcNow;
        var volumeIds = volumes.Select(v => v.Id).ToList();
        await ctx.Volumes
            .Where(v => volumeIds.Contains(v.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.LastRefreshedAt, now));

        JobSendTrace($"[Scheduler] Rolling refresh — {volumes.Count} volume(s): "
            + string.Join(", ", volumes.Select(v => v.Title)));

        foreach (var volume in volumes)
        {
            try
            {
                await LaunchJobRefreshVolume(
                    volume.Id,
                    syncFromSource: true, recalculateStatistics: true, regenerateComicInfo: true,
                    scanKavita: true, syncNewIssuesOnly: true, regenerateComicInfoNewOnly: true,
                    checkFiles: false);
            }
            catch (InvalidOperationException)
            {
                // Volume manuel qui aurait échappé au filtre — ignoré silencieusement.
            }
        }
    }

    /// <summary>
    /// Estampille <see cref="Volume.LastRefreshedAt"/> à l'instant courant via un <c>UPDATE</c> ciblé
    /// (aucune matérialisation d'entité) : ne touche que cette colonne, sans risque de réécrire des
    /// métadonnées synchronisées en parallèle sur un autre contexte. Appelé après une synchro source
    /// réussie dans <c>RunRematchVolumeJobAsync</c>.
    /// </summary>
    private async Task StampVolumeLastRefreshedAsync(Guid volumeId)
    {
        var now = DateTime.UtcNow;
        await GetDb().Volumes
            .Where(v => v.Id == volumeId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.LastRefreshedAt, now));
    }

    /// <summary>
    /// Déclenche immédiatement une tâche planifiée (bouton « Run now »). <paramref name="key"/> doit
    /// valoir <see cref="SchedulerTaskProcessDownloads"/> ou <see cref="SchedulerTaskRollingRefresh"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Clé de tâche inconnue.</exception>
    public void RunSchedulerTaskNow(string key)
    {
        Func<Task> action = key switch
        {
            SchedulerTaskProcessDownloads => RunScheduledProcessDownloadsAsync,
            SchedulerTaskRollingRefresh => RunScheduledRollingRefreshAsync,
            _ => throw new ArgumentException($"Unknown scheduler task '{key}'.", nameof(key))
        };

        FireScheduledTask(key, action);
    }

    /// <summary>État courant des deux tâches planifiées (config + dernier / prochain déclenchement).</summary>
    public SchedulerStatus GetSchedulerStatus()
    {
        var scheduler = GetService<SchedulerService, SchedulerOptions>();
        return new SchedulerStatus(
            BuildTaskStatus(SchedulerTaskProcessDownloads, scheduler.ProcessDownloadsEnabled, scheduler.ProcessDownloadsCron),
            BuildTaskStatus(SchedulerTaskRollingRefresh, scheduler.RollingRefreshEnabled, scheduler.RollingRefreshCron),
            scheduler.RollingRefreshBatchSize);
    }

    private SchedulerTaskStatus BuildTaskStatus(string key, bool enabled, string cron)
    {
        DateTime? nextRunUtc = null;
        if (enabled && CronExpression.TryParse(cron, out var expr) && expr is not null)
            nextRunUtc = expr.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Local);

        var lastRunUtc = _schedulerLastRun.TryGetValue(key, out var last) ? last : (DateTime?)null;
        return new SchedulerTaskStatus(enabled, cron, lastRunUtc, nextRunUtc, _schedulerBusy.ContainsKey(key));
    }
}
