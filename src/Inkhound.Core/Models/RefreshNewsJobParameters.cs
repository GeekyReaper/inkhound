using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

/// <summary>
/// Paramètres du job News : relecture des pages liste (forcée ou selon
/// <c>NewsOptions.ListRefreshIntervalHours</c>) puis enrichissement d'un lot d'albums par flux.
/// </summary>
public class RefreshNewsJobParameters : IJobParameters
{
    /// <summary>Relit les pages liste même si le dernier passage est récent.</summary>
    public bool ForceListRefresh { get; set; }

    /// <summary>Nombre d'albums enrichis par flux (top ventes / nouveautés), 0 = aucun.</summary>
    public int EnrichBatchSize { get; set; } = 5;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (EnrichBatchSize < 0)
            errors.Add("EnrichBatchSize must be at least 0.");
        return errors.Count == 0;
    }
}
