namespace Inkhound.Core.Models;

// Rôle canonique d'un contributeur, indépendant de la source (ComicVine envoie déjà l'anglais,
// Bedetheque scrape le français). Sert à filtrer les auteurs pour les tags ComicInfo.xml
// (<Writer>, <Penciller>, ...) — et, potentiellement, à un affichage front normalisé plus tard.
public enum AuthorRole
{
    Writer,
    Penciller,
    Artist,
    Inker,
    Colorist,
    Letterer,
    CoverArtist,
    Editor,
    Translator
}

public static class AuthorRoleExtensions
{
    // Reconnaît le vocabulaire ComicVine (anglais, déjà en minuscules dans l'API) ET les libellés
    // scrapés sur Bedetheque (français, span.metier — "Scénario", "Dessin", "Encrage", "Couleurs",
    // "Lettrage", "Couverture", "Traduction"). Rôle non reconnu → null, auteur simplement ignoré
    // pour cette catégorie de tag — jamais d'exception.
    // Le HTML Bedetheque encadre le rôle de parenthèses ("(Scénario)") — BedethequeSourceService
    // les retire déjà au scraping, mais on les retire aussi ici en défense : ça permet à ce
    // correctif de s'appliquer immédiatement aux données déjà en base (stockées avec les
    // parenthèses avant ce correctif), sans attendre qu'un Rematch/Refresh les récupère à nouveau.
    public static AuthorRole? ParseAuthorRole(string role) => role.Trim().Trim('(', ')').Trim().ToLowerInvariant() switch
    {
        "writer" or "scénario" or "scenario" => AuthorRole.Writer,
        "penciler" or "penciller" or "dessin" => AuthorRole.Penciller,
        "artist" => AuthorRole.Artist,
        "inker" or "encrage" => AuthorRole.Inker,
        "colorist" or "couleurs" => AuthorRole.Colorist,
        "letterer" or "lettrage" => AuthorRole.Letterer,
        "cover" or "covers" or "couverture" => AuthorRole.CoverArtist,
        "editor" or "édition" or "edition" => AuthorRole.Editor,
        "translator" or "traduction" => AuthorRole.Translator,
        _ => null
    };
}
