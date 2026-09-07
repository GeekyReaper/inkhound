using Cronos;
using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.Models;

/// <summary>
/// Options du planificateur de jobs récurrents (service <c>Scheduler</c>). Deux tâches
/// indépendantes, chacune activable et pilotée par une expression cron 5 champs (heure serveur) :
/// l'import des downloads terminés et le « rolling refresh » qui synchronise, à chaque exécution,
/// un lot des volumes les moins récemment mis à jour depuis leur source.
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
            new() { Name = nameof(RollingRefreshBatchSize), Section = "Rolling refresh", SortOrder = 40, Value = RollingRefreshBatchSize.ToString(), ValueType = EValueType.INT, DefaultValue = "10", Description = "Number of volumes refreshed per run (least-recently-refreshed first).", Mandatory = false }
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
            }
        }

        return true;
    }
}
