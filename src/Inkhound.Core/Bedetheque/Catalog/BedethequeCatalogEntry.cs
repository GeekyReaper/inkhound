namespace Inkhound.Core.Bedetheque.Catalog;

/// <summary>
/// Une série de l'index alphabétique de bedetheque.com, persistée dans la table
/// <c>BedethequeCatalogSeries</c>. Le catalogue complet (~77 000 séries) alimente la recherche
/// locale de <see cref="BedethequeSourceService"/> — aucune URL n'est stockée, elle se reconstruit
/// depuis <see cref="Id"/>.
/// </summary>
public class BedethequeCatalogEntry
{
    /// <summary>Identifiant de la série sur Bedetheque (celui de <c>/serie-{id}-BD-x.html</c>).</summary>
    public int Id { get; set; }

    /// <summary>Titre tel qu'affiché par le site, article en suffixe (« Schtroumpfs (Les) »).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Langue déduite du drapeau (« Français », « Anglais »…), <c>null</c> si absent.</summary>
    public string? Language { get; set; }

    /// <summary>Origine déduite du drapeau (« Europe », « USA », « Asie », « Autre »).</summary>
    public string? Origin { get; set; }

    /// <summary>Lettre de la page d'index d'où provient l'entrée (<c>0</c>, <c>A</c>…<c>Z</c>).</summary>
    public string Letter { get; set; } = string.Empty;

    /// <summary>Horodatage UTC du scraping de la page d'index correspondante.</summary>
    public DateTime FetchedAtUtc { get; set; }
}
