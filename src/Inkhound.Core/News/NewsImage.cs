namespace Inkhound.Core.News;

/// <summary>
/// Visuel d'un album : <c>Kind</c> = "Cover", "Plate" (planche d'extrait) ou "Back" (verso),
/// miniature pour les grilles et grand format pour l'affichage plein écran.
/// </summary>
public record NewsImage(string Kind, string ThumbUrl, string Url);
