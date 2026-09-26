namespace Inkhound.Core.Bedetheque;

public record BdAuteur(string Nom, string? Role, string? Url);

public record BdAlbumSummary(
    int Id,
    string Titre,
    string? NumeroAlbum,
    string? Annee,
    string? Editeur,
    string? CoverUrl,
    string Url,
    // Catégorie ("Standard"/"Special"/"SpecialEdition"/"Omnibus"/"Roman"/"BestOf") et numéro
    // résolus par BedethequeAlbumClassifier à partir de NumeroAlbum + Titre — voir ParseAlbumList.
    string Category = "Standard",
    int Idx = 0);

public record BdSerie(
    int Id,
    string Titre,
    string? Genre,
    string? Parution,
    int? NombreAlbums,
    string? Origine,
    string? Langue,
    string? AnneeDebut,
    string? AnneeFin,
    string? Description,
    string? CoverUrl,
    string Url,
    List<BdAlbumSummary> Albums,
    string? Editeur,
    string? SiteWeb);

public record BdAlbum(
    int Id,
    string Titre,
    string? NumeroAlbum,
    int SerieId,
    string SerieTitre,
    string SerieUrl,
    List<BdAuteur> Auteurs,
    string? Editeur,
    string? Collection,
    string? Annee,
    string? Ean,
    string? Description,
    string? CoverUrl,
    string? DepotLegal,
    int? Planches,
    string? Genre,
    double? Note,
    int? NombreVotes,
    string Url,
    // Repris depuis le BdAlbumSummary correspondant (GetAllAlbumsForSerieAsync) — l'extraction du
    // numéro sur la page détail (alternativeheadline "Tome X") ne couvre que les tomes classiques,
    // la classification fiable vient toujours de la page liste.
    string Category = "Standard",
    int Idx = 0,
    // Visuels de l'édition (couverture, planche d'extrait, verso) en miniature + grand format —
    // bloc "sous-couv" de la page album, voir ParseAlbumImages.
    IReadOnlyList<BdImage>? Images = null);

/// <summary>Visuel d'un album : <c>Kind</c> = "Cover", "Plate" (planche d'extrait) ou "Back" (verso).</summary>
public record BdImage(string Kind, string ThumbUrl, string Url);
