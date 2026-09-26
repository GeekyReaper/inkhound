using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.News;

/// <summary>
/// Options du module News. La planification (cron, taille des lots d'enrichissement) vit dans
/// <c>SchedulerOptions</c>, comme pour les autres tâches récurrentes.
/// </summary>
public class NewsOptions : IOptionList
{
    /// <summary>Délai minimum entre deux relectures des pages liste (top ventes, nouveautés).</summary>
    public int ListRefreshIntervalHours { get; set; } = 6;

    /// <summary>Nombre d'échecs au-delà duquel un album n'est plus retenté par l'enrichissement planifié.</summary>
    public int MaxEnrichAttempts { get; set; } = 3;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (ListRefreshIntervalHours < 1)
            errors.Add($"{nameof(ListRefreshIntervalHours)} must be at least 1.");
        if (MaxEnrichAttempts < 1)
            errors.Add($"{nameof(MaxEnrichAttempts)} must be at least 1.");
        return errors.Count == 0;
    }

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new() { Name = nameof(ListRefreshIntervalHours), Section = "Feeds", SortOrder = 0, Value = ListRefreshIntervalHours.ToString(), ValueType = EValueType.INT, DefaultValue = "6", Description = "Minimum delay (hours) between two scrapes of the top sales / new releases list pages. Scheduled runs in between only enrich pending albums.", Mandatory = false },
            new() { Name = nameof(MaxEnrichAttempts), Section = "Feeds", SortOrder = 10, Value = MaxEnrichAttempts.ToString(), ValueType = EValueType.INT, DefaultValue = "3", Description = "Number of failed enrichment attempts after which an album is no longer retried by the scheduled job.", Mandatory = false }
        };
    }

    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = new List<string>();
        foreach (var option in options)
        {
            switch (option.Name)
            {
                case nameof(ListRefreshIntervalHours): ListRefreshIntervalHours = option.GetInt(); break;
                case nameof(MaxEnrichAttempts): MaxEnrichAttempts = option.GetInt(); break;
            }
        }
        return IsValid(out errors);
    }
}
