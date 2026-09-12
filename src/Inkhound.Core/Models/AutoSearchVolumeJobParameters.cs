using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

/// <summary>
/// Paramètres du job « Auto search » d'un volume : recherche Prowlarr des issues Standard
/// manquantes et envoi automatique à qBittorrent des torrents dont le score atteint
/// <see cref="MinScore"/>. Lancé par la tâche <c>AutoSearch</c> du scheduler.
/// </summary>
public class AutoSearchVolumeJobParameters : IJobParameters
{
    public Guid VolumeId { get; set; }

    /// <summary>Score minimum (0-100) qu'un candidat doit atteindre pour être acquis.</summary>
    public int MinScore { get; set; } = 70;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (VolumeId == Guid.Empty)
            errors.Add("VolumeId cannot be empty.");
        if (MinScore is < 0 or > 100)
            errors.Add("MinScore must be between 0 and 100.");
        return errors.Count == 0;
    }
}
