namespace Inkhound.Core.Models;

public enum AgeRating
{
    Unknown,
    RatingPending,
    EarlyChildhood,
    Everyone,
    G,
    Everyone10Plus,
    PG,
    KidsToAdults,
    Teen,
    MA15Plus,
    Mature17Plus,
    M,
    R18Plus,
    AdultsOnly18Plus,
    X18Plus
}

public static class AgeRatingExtensions
{
    /// <summary>
    /// Vrai si <paramref name="rating"/> est accessible sous le plafond <paramref name="max"/> : l'ordre de
    /// l'enum fait foi (du plus tout public au plus restreint), une classification strictement supérieure
    /// au plafond est refusée. Sous un plafond, un volume non classé (<c>Unknown</c>, <c>RatingPending</c>)
    /// est lui aussi masqué : rien ne prouve qu'il respecte la limite. <c>null</c> = aucun plafond.
    /// </summary>
    public static bool IsAllowedUnder(this AgeRating rating, AgeRating? max)
        => max is null || (rating > AgeRating.RatingPending && rating <= max);

    /// <summary>Classifications accessibles sous un plafond — pour un filtre EF (<c>Contains</c>), l'enum étant stocké en texte.</summary>
    public static AgeRating[] AllowedUpTo(AgeRating max)
        => Enum.GetValues<AgeRating>().Where(r => r.IsAllowedUnder(max)).ToArray();

    public static string ToKavitaString(this AgeRating rating) => rating switch
    {
        AgeRating.Unknown          => "Unknown",
        AgeRating.RatingPending    => "Rating Pending",
        AgeRating.EarlyChildhood   => "Early Childhood",
        AgeRating.Everyone         => "Everyone",
        AgeRating.G                => "G",
        AgeRating.Everyone10Plus   => "Everyone 10+",
        AgeRating.PG               => "PG",
        AgeRating.KidsToAdults     => "Kids to Adults",
        AgeRating.Teen             => "Teen",
        AgeRating.MA15Plus         => "MA15+",
        AgeRating.Mature17Plus     => "Mature 17+",
        AgeRating.M                => "M",
        AgeRating.R18Plus          => "R18+",
        AgeRating.AdultsOnly18Plus => "Adults Only 18+",
        AgeRating.X18Plus          => "X18+",
        _                          => "Unknown"
    };
}
