using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class ProwlarrVolumeSearchJobParameters : IJobParameters
{
    public Guid VolumeId { get; set; }

    /// <summary>null = utiliser les indexers sélectionnés persistés</summary>
    public int[]? IndexerIds { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (VolumeId == Guid.Empty)
            errors.Add("VolumeId is required.");
        return errors.Count == 0;
    }
}
