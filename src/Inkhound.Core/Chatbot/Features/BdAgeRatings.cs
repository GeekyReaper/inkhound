using Inkhound.Core.Models;

namespace Inkhound.Core.Chatbot.Features;

/// <summary>
/// Classifications d'âge proposées lors de l'ajout d'une série depuis le chatbot, ordonnées du plus
/// jeune au plus mature. <c>Label</c> est l'affichage FR du menu, <c>Value</c> la valeur de l'enum
/// du domaine.
/// </summary>
/// <remarks>
/// Le bot d'origine envoyait ces valeurs en chaînes de caractères à l'API REST, dont
/// <c>"AdultsOnly"</c> — qui ne correspond à aucun membre de <see cref="AgeRating"/> (le membre réel
/// est <see cref="AgeRating.AdultsOnly18Plus"/>). En passant par l'enum, le compilateur garantit
/// désormais que chaque entrée est une valeur valide.
/// </remarks>
public static class BdAgeRatings
{
    public sealed record BdAgeRating(string Label, AgeRating Value);

    public static readonly IReadOnlyList<BdAgeRating> All =
    [
        new("Tout public (Everyone)", AgeRating.Everyone),
        new("Dès 10 ans (Everyone 10+)", AgeRating.Everyone10Plus),
        new("Pré-ados (Kids to Adults)", AgeRating.KidsToAdults),
        new("Ados (Teen)", AgeRating.Teen),
        new("Ados 15+ (MA15+)", AgeRating.MA15Plus),
        new("Adultes 17+ (Mature 17+)", AgeRating.Mature17Plus),
        new("Adultes (Adults Only 18+)", AgeRating.AdultsOnly18Plus),
        new("Explicite 18+ (R18+)", AgeRating.R18Plus),
    ];
}
