
using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

public class RematchVolumeJobParameters : IJobParameters
{
    public Guid VolumeId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;

    // Étapes optionnelles — toutes activées par défaut. Utilisé par "Refresh" (popup à cases à
    // cocher, cf. VolumeController.Refresh) pour n'exécuter qu'un sous-ensemble des étapes ;
    // "Rematch" (changement de série via recherche libre) laisse toujours les 4 valeurs par défaut.
    public bool SyncFromSource { get; set; } = true;
    public bool RecalculateStatistics { get; set; } = true;
    public bool RegenerateComicInfo { get; set; } = true;
    public bool ScanKavita { get; set; } = true;

    // true si lancé depuis "Refresh" (LaunchJobRefreshVolume, même source déjà associée) — pilote
    // uniquement le libellé du Job ("Refresh — {titre}" au lieu de "Rematch — {titre}"), aucune
    // différence de comportement.
    public bool IsRefresh { get; set; } = false;

    // true = mode "NEW issues only" de la popup Refresh : on synchronise la metadata Volume/Serie
    // puis on ne récupère (endpoint détail par issue/album) QUE les ids source encore inconnus en
    // base, insérés en MISSING ; les issues déjà connues ne sont pas touchées (pas de maj metadata,
    // pas de renumérotation, pas de suppression d'orphelins). Les stats du volume sont recalculées
    // à la fin. false (défaut, seul cas pour le Rematch changement de série) = comportement complet
    // historique. N'a d'effet que si SyncFromSource == true.
    public bool SyncNewIssuesOnly { get; set; } = false;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (VolumeId == Guid.Empty)
            errors.Add("VolumeId cannot be empty.");
        if (SyncFromSource && string.IsNullOrWhiteSpace(Source))
            errors.Add("Source cannot be empty.");
        if (SyncFromSource && string.IsNullOrWhiteSpace(SourceId))
            errors.Add("SourceId cannot be empty.");
        return errors.Count == 0;
    }
}
