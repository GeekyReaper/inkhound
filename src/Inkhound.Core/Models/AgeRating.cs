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
