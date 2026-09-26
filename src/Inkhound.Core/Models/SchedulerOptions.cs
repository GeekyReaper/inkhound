using Cronos;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.Models;

/// <summary>
/// Options du planificateur de jobs récurrents (service <c>Scheduler</c>). Cinq tâches
/// indépendantes, chacune activable et pilotée par une expression cron 5 champs (heure serveur) :
/// l'import des downloads terminés, le « rolling refresh » qui synchronise, à chaque exécution,
/// un lot des volumes les moins récemment mis à jour depuis leur source, l'« auto search » qui
/// recherche et envoie en téléchargement les issues Standard manquantes d'un lot de volumes, et
/// le rafraîchissement par lot de lettres du catalogue local Bedetheque, et le rafraîchissement des
/// flux News (top ventes, nouveautés) avec enrichissement d'un lot d'albums par flux.
/// </summary>
public class SchedulerOptions : IOptionList
{
    /// <summary>Active le déclenchement automatique de l'import des downloads.</summary>
    public bool ProcessDownloadsEnabled { get; set; } = false;

    /// <summary>Expression cron (5 champs, heure serveur) pilotant l'import des downloads.</summary>
    public string ProcessDownloadsCron { get; set; } = "*/15 * * * *";

    /// <summary>Active le « rolling refresh » des volumes.</summary>
    public bool RollingRefreshEnabled { get; set; } = false;

    /// <summary>Expression cron (5 champs, heure serveur) pilotant le rolling refresh.</summary>
    public string RollingRefreshCron { get; set; } = "0 3 * * *";

    /// <summary>Nombre de volumes traités à chaque exécution du rolling refresh.</summary>
    public int RollingRefreshBatchSize { get; set; } = 10;

    /// <summary>Active l'« auto search » (acquisition automatique des issues manquantes via Prowlarr).</summary>
    public bool AutoSearchEnabled { get; set; } = false;

    /// <summary>Expression cron (5 champs, heure serveur) pilotant l'auto search.</summary>
    public string AutoSearchCron { get; set; } = "0 4 * * *";

    /// <summary>Nombre de volumes traités à chaque exécution de l'auto search.</summary>
    public int AutoSearchBatchSize { get; set; } = 5;

    /// <summary>Score minimum (0-100) qu'un torrent doit atteindre pour être acquis automatiquement.</summary>
    public int AutoSearchMinScore { get; set; } = 70;

    /// <summary>Active le rafraîchissement automatique du catalogue local Bedetheque.</summary>
    public bool BedethequeCatalogEnabled { get; set; } = false;

    /// <summary>Expression cron (5 champs, heure serveur) pilotant le rafraîchissement du catalogue.</summary>
    public string BedethequeCatalogCron { get; set; } = "0 2 * * *";

    /// <summary>Nombre de lettres d'index rafraîchies à chaque exécution (les plus anciennes d'abord).</summary>
    public int BedethequeCatalogLetterCount { get; set; } = 3;

    /// <summary>Active le rafraîchissement automatique des flux News (top ventes, nouveautés).</summary>
    public bool NewsEnabled { get; set; } = false;

    /// <summary>Expression cron (5 champs, heure serveur) pilotant le rafraîchissement des flux News.</summary>
    public string NewsCron { get; set; } = "0 * * * *";

    /// <summary>Nombre d'albums enrichis par flux à chaque exécution.</summary>
    public int NewsEnrichBatchSize { get; set; } = 5;

    /// <summary>
    /// Valide les expressions cron des tâches activées. Une tâche désactivée n'est pas contrôlée :
    /// une valeur cron invalide n'a alors aucun effet et ne doit pas passer le service en INVALID.
    /// </summary>
    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (ProcessDownloadsEnabled && !CronExpression.TryParse(ProcessDownloadsCron, out _))
            errors.Add($"{nameof(ProcessDownloadsCron)} is not a valid 5-field cron expression.");

        if (RollingRefreshEnabled && !CronExpression.TryParse(RollingRefreshCron, out _))
            errors.Add($"{nameof(RollingRefreshCron)} is not a valid 5-field cron expression.");

        if (RollingRefreshEnabled && RollingRefreshBatchSize < 1)
            errors.Add($"{nameof(RollingRefreshBatchSize)} must be at least 1.");

        if (AutoSearchEnabled && !CronExpression.TryParse(AutoSearchCron, out _))
            errors.Add($"{nameof(AutoSearchCron)} is not a valid 5-field cron expression.");

        if (AutoSearchEnabled && AutoSearchBatchSize < 1)
            errors.Add($"{nameof(AutoSearchBatchSize)} must be at least 1.");

        if (AutoSearchEnabled && AutoSearchMinScore is < 0 or > 100)
            errors.Add($"{nameof(AutoSearchMinScore)} must be between 0 and 100.");

        if (BedethequeCatalogEnabled && !CronExpression.TryParse(BedethequeCatalogCron, out _))
            errors.Add($"{nameof(BedethequeCatalogCron)} is not a valid 5-field cron expression.");

        if (BedethequeCatalogEnabled && BedethequeCatalogLetterCount < 1)
            errors.Add($"{nameof(BedethequeCatalogLetterCount)} must be at least 1.");

        if (NewsEnabled && !CronExpression.TryParse(NewsCron, out _))
            errors.Add($"{nameof(NewsCron)} is not a valid 5-field cron expression.");

        if (NewsEnabled && NewsEnrichBatchSize < 0)
            errors.Add($"{nameof(NewsEnrichBatchSize)} must be at least 0.");

        return errors.Count == 0;
    }

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new() { Name = nameof(ProcessDownloadsEnabled), Section = "Import downloads", SortOrder = 0, Value = ProcessDownloadsEnabled.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Automatically run the download import job on a schedule.", Mandatory = false },
            new() { Name = nameof(ProcessDownloadsCron), Section = "Import downloads", SortOrder = 10, Value = ProcessDownloadsCron, ValueType = EValueType.STRING, DefaultValue = "*/15 * * * *", Description = "5-field cron expression (server time) — e.g. \"*/15 * * * *\" every 15 minutes.", Mandatory = false },
            new() { Name = nameof(RollingRefreshEnabled), Section = "Rolling refresh", SortOrder = 20, Value = RollingRefreshEnabled.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Automatically run a \"NEW issues only\" refresh on a batch of the least-recently-refreshed volumes on a schedule.", Mandatory = false },
            new() { Name = nameof(RollingRefreshCron), Section = "Rolling refresh", SortOrder = 30, Value = RollingRefreshCron, ValueType = EValueType.STRING, DefaultValue = "0 3 * * *", Description = "5-field cron expression (server time) — e.g. \"0 3 * * *\" every day at 03:00.", Mandatory = false },
            new() { Name = nameof(RollingRefreshBatchSize), Section = "Rolling refresh", SortOrder = 40, Value = RollingRefreshBatchSize.ToString(), ValueType = EValueType.INT, DefaultValue = "10", Description = "Number of volumes refreshed per run (least-recently-refreshed first).", Mandatory = false },
            new() { Name = nameof(AutoSearchEnabled), Section = "Auto search", SortOrder = 50, Value = AutoSearchEnabled.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Automatically search Prowlarr and send the best torrents to qBittorrent for the missing Standard issues of a batch of monitored volumes.", Mandatory = false },
            new() { Name = nameof(AutoSearchCron), Section = "Auto search", SortOrder = 60, Value = AutoSearchCron, ValueType = EValueType.STRING, DefaultValue = "0 4 * * *", Description = "5-field cron expression (server time) — e.g. \"0 4 * * *\" every day at 04:00.", Mandatory = false },
            new() { Name = nameof(AutoSearchBatchSize), Section = "Auto search", SortOrder = 70, Value = AutoSearchBatchSize.ToString(), ValueType = EValueType.INT, DefaultValue = "5", Description = "Number of volumes searched per run (least-recently-searched first).", Mandatory = false },
            new() { Name = nameof(AutoSearchMinScore), Section = "Auto search", SortOrder = 80, Value = AutoSearchMinScore.ToString(), ValueType = EValueType.INT, DefaultValue = "70", Description = "Minimum score (0-100) a torrent must reach to be grabbed automatically.", Mandatory = false },
            new() { Name = nameof(BedethequeCatalogEnabled), Section = "Bedetheque catalog", SortOrder = 90, Value = BedethequeCatalogEnabled.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Automatically refresh a batch of the least-recently-fetched letters of the local Bedetheque series catalog on a schedule.", Mandatory = false },
            new() { Name = nameof(BedethequeCatalogCron), Section = "Bedetheque catalog", SortOrder = 100, Value = BedethequeCatalogCron, ValueType = EValueType.STRING, DefaultValue = "0 2 * * *", Description = "5-field cron expression (server time) — e.g. \"0 2 * * *\" every day at 02:00.", Mandatory = false },
            new() { Name = nameof(BedethequeCatalogLetterCount), Section = "Bedetheque catalog", SortOrder = 110, Value = BedethequeCatalogLetterCount.ToString(), ValueType = EValueType.INT, DefaultValue = "3", Description = "Number of index letters (0, A-Z) refreshed per run (least-recently-fetched first).", Mandatory = false },
            new() { Name = nameof(NewsEnabled), Section = "News", SortOrder = 120, Value = NewsEnabled.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Automatically refresh the News feeds (top sales, new releases) and enrich a batch of pending albums on a schedule.", Mandatory = false },
            new() { Name = nameof(NewsCron), Section = "News", SortOrder = 130, Value = NewsCron, ValueType = EValueType.STRING, DefaultValue = "0 * * * *", Description = "5-field cron expression (server time) — e.g. \"0 * * * *\" every hour.", Mandatory = false },
            new() { Name = nameof(NewsEnrichBatchSize), Section = "News", SortOrder = 140, Value = NewsEnrichBatchSize.ToString(), ValueType = EValueType.INT, DefaultValue = "5", Description = "Number of albums enriched per feed and per run (series, authors, preview pages) — one source request each.", Mandatory = false }
        };
    }

    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = new List<string>();

        foreach (var option in options)
        {
            switch (option.Name)
            {
                case nameof(ProcessDownloadsEnabled): ProcessDownloadsEnabled = option.GetBool(); break;
                case nameof(ProcessDownloadsCron): ProcessDownloadsCron = option.Value; break;
                case nameof(RollingRefreshEnabled): RollingRefreshEnabled = option.GetBool(); break;
                case nameof(RollingRefreshCron): RollingRefreshCron = option.Value; break;
                case nameof(RollingRefreshBatchSize): RollingRefreshBatchSize = option.GetInt(); break;
                case nameof(AutoSearchEnabled): AutoSearchEnabled = option.GetBool(); break;
                case nameof(AutoSearchCron): AutoSearchCron = option.Value; break;
                case nameof(AutoSearchBatchSize): AutoSearchBatchSize = option.GetInt(); break;
                case nameof(AutoSearchMinScore): AutoSearchMinScore = option.GetInt(); break;
                case nameof(BedethequeCatalogEnabled): BedethequeCatalogEnabled = option.GetBool(); break;
                case nameof(BedethequeCatalogCron): BedethequeCatalogCron = option.Value; break;
                case nameof(BedethequeCatalogLetterCount): BedethequeCatalogLetterCount = option.GetInt(); break;
                case nameof(NewsEnabled): NewsEnabled = option.GetBool(); break;
                case nameof(NewsCron): NewsCron = option.Value; break;
                case nameof(NewsEnrichBatchSize): NewsEnrichBatchSize = option.GetInt(); break;
            }
        }

        return true;
    }
}
