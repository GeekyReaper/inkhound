namespace Inkhound.Core.Models;

/// <summary>
/// Demande d'ajout d'un volume à une library, émise par un compte Guest (qui n'a pas le droit
/// d'ajouter directement). L'admin peut en modifier la library cible et la classification d'âge,
/// puis l'approuver (le volume est alors créé via <c>AddVolumeFromSourceAsync</c>) ou la refuser.
/// </summary>
public class VolumeRequest
{
    public Guid Id { get; set; }

    public Guid RequestedByUserId { get; set; }

    /// <summary>Login du demandeur, dénormalisé pour l'affichage admin (le compte peut disparaître).</summary>
    public string RequestedByLogin { get; set; } = string.Empty;

    /// <summary>Clé de source en minuscules (<c>comicvine</c> | <c>bedetheque</c>).</summary>
    public string Source { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    /// <summary>URL de la couverture, pour l'aperçu dans la liste.</summary>
    public string? Image { get; set; }

    public Guid LibraryId { get; set; }

    public AgeRating AgeRating { get; set; } = AgeRating.Unknown;

    public VolumeRequestStatus Status { get; set; } = VolumeRequestStatus.Pending;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Date d'approbation ou de refus ; <c>null</c> tant que la demande est en attente.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Volume créé à l'approbation.</summary>
    public Guid? VolumeId { get; set; }
}
