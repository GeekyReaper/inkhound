using Foundation.Core;
using Inkhound.Core.Models;

namespace Inkhound.Core;

/// <summary>
/// Service exposant la configuration du planificateur de jobs récurrents. La boucle de
/// planification elle-même vit dans <see cref="InkhoundManager"/> (région Scheduler) et lit ces
/// propriétés à chaque tick — ce service ne fait que porter les options persistées.
/// </summary>
public class SchedulerService : BaseService<SchedulerOptions>
{
    /// <inheritdoc />
    public override string GetServiceName() => "Scheduler";

    /// <summary>Import automatique des downloads activé.</summary>
    public bool ProcessDownloadsEnabled => Options.ProcessDownloadsEnabled;

    /// <summary>Expression cron (heure serveur) de l'import des downloads.</summary>
    public string ProcessDownloadsCron => Options.ProcessDownloadsCron;

    /// <summary>Rolling refresh des volumes activé.</summary>
    public bool RollingRefreshEnabled => Options.RollingRefreshEnabled;

    /// <summary>Expression cron (heure serveur) du rolling refresh.</summary>
    public string RollingRefreshCron => Options.RollingRefreshCron;

    /// <summary>Nombre de volumes traités à chaque exécution du rolling refresh.</summary>
    public int RollingRefreshBatchSize => Options.RollingRefreshBatchSize;

    /// <summary>Auto search (acquisition automatique via Prowlarr) activé.</summary>
    public bool AutoSearchEnabled => Options.AutoSearchEnabled;

    /// <summary>Expression cron (heure serveur) de l'auto search.</summary>
    public string AutoSearchCron => Options.AutoSearchCron;

    /// <summary>Nombre de volumes traités à chaque exécution de l'auto search.</summary>
    public int AutoSearchBatchSize => Options.AutoSearchBatchSize;

    /// <summary>Score minimum (0-100) qu'un torrent doit atteindre pour être acquis automatiquement.</summary>
    public int AutoSearchMinScore => Options.AutoSearchMinScore;

    /// <summary>Rafraîchissement automatique du catalogue local Bedetheque activé.</summary>
    public bool BedethequeCatalogEnabled => Options.BedethequeCatalogEnabled;

    /// <summary>Expression cron (heure serveur) du rafraîchissement du catalogue Bedetheque.</summary>
    public string BedethequeCatalogCron => Options.BedethequeCatalogCron;

    /// <summary>Nombre de lettres d'index rafraîchies à chaque exécution.</summary>
    public int BedethequeCatalogLetterCount => Options.BedethequeCatalogLetterCount;
}
