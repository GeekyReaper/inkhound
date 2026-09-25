using Inkhound.Core.Sources;

namespace Inkhound.Core.Chatbot.Services;

/// <summary>Une série résolue et la liste de ses albums chez la source.</summary>
public sealed record SeriesDetail(SourceVolume Series, IReadOnlyList<SourceIssue> Issues);

public abstract record BdLookupOutcome
{
    /// <summary>
    /// Une série a été résolue. <see cref="Resolved.Candidates"/> porte tous les résultats de la
    /// recherche (dont le survivant), pour permettre à l'appelant de proposer d'autres
    /// correspondances ; vaut un seul élément quand la recherche n'a renvoyé qu'un résultat.
    /// </summary>
    public sealed record Resolved(SeriesDetail Detail, IReadOnlyList<SourceVolume> Candidates) : BdLookupOutcome;

    public sealed record Ambiguous(IReadOnlyList<SourceVolume> Candidates) : BdLookupOutcome;

    public sealed record NotFound(string Message) : BdLookupOutcome;

    public sealed record Failed(string Message) : BdLookupOutcome;

    private BdLookupOutcome() { }
}
