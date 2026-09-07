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
}
