namespace Inkhound.Core.News;

/// <summary>Auteur d'un album d'actualité, rôle tel qu'affiché par la source (« Scénario », « Dessin »…).</summary>
public record NewsAuthor(string Name, string? Role);
