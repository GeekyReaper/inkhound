using System.Net;
using System.Text;
using Inkhound.Core.Chatbot.Services;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Chatbot.Features;

/// <summary>
/// Construit la réponse lisible (texte brut + HTML) d'une série résolue. Découpée en deux sections
/// indépendantes — fiche série et liste des albums — pour que l'appelant les envoie comme deux
/// messages de room distincts.
/// </summary>
/// <remarks>
/// Le rendu n'utilise jamais &lt;table&gt; : Element X iOS ne les rend pas du tout.
/// </remarks>
public static class BdSeriesHtmlFormatter
{
    public const int DefaultMaxAlbumsShown = 30;
    private const int MaxDescriptionLength = 400;

    public static (string Plain, string Html) BuildInfoSection(SeriesDetail detail)
    {
        var series = detail.Series;
        var year = series.StartYear?.ToString() ?? "?";

        var plain = new StringBuilder()
            .AppendLine($"[{series.Source}] {series.Name} ({year})");
        var html = new StringBuilder()
            .Append($"<p><b>[{Enc(series.Source)}] {Enc(series.Name)}</b> ({Enc(year)})</p><ul>");

        if (!string.IsNullOrWhiteSpace(series.Publisher))
        {
            plain.AppendLine($"Éditeur : {series.Publisher}");
            html.Append($"<li>Éditeur : {Enc(series.Publisher)}</li>");
        }

        plain.AppendLine($"Nombre de tomes : {series.CountOfIssues}");
        html.Append($"<li>Nombre de tomes : {series.CountOfIssues}</li>");

        if (!string.IsNullOrWhiteSpace(series.Description))
        {
            var description = Truncate(series.Description, MaxDescriptionLength);
            plain.AppendLine($"Description : {description}");
            html.Append($"<li>Description : {Enc(description)}</li>");
        }

        html.Append("</ul>");

        if (!string.IsNullOrWhiteSpace(series.SiteUrl))
        {
            plain.Append(series.SiteUrl);
            html.Append($"<p><a href=\"{Enc(series.SiteUrl)}\">Voir la série sur {Enc(series.Source)}</a></p>");
        }

        return (plain.ToString().TrimEnd(), html.ToString());
    }

    /// <summary>Retourne null quand la série n'a aucun album à montrer (rien à envoyer en second message).</summary>
    public static (string Plain, string Html)? BuildAlbumsListSection(
        IReadOnlyList<SourceIssue> issues, int totalCount, int maxAlbumsShown = DefaultMaxAlbumsShown)
    {
        if (issues.Count == 0) return null;

        var shown = issues.Take(Math.Max(1, maxAlbumsShown)).ToList();

        var plain = new StringBuilder().AppendLine("Albums :");
        var html = new StringBuilder("<ul>");

        foreach (var issue in shown)
        {
            var year = issue.CoverDate?.Year.ToString() ?? "?";
            var number = string.IsNullOrWhiteSpace(issue.IssueNumber) ? "?" : issue.IssueNumber;
            var name = string.IsNullOrWhiteSpace(issue.Name) ? "(titre inconnu)" : issue.Name;
            plain.AppendLine($"- #{number} {name} ({year})");

            var titleHtml = string.IsNullOrWhiteSpace(issue.SiteUrl)
                ? Enc(name)
                : $"<a href=\"{Enc(issue.SiteUrl)}\">{Enc(name)}</a>";
            html.Append($"<li>#{Enc(number)} {titleHtml} ({Enc(year)})</li>");
        }

        html.Append("</ul>");

        if (totalCount > shown.Count)
        {
            var remaining = totalCount - shown.Count;
            plain.Append($"(+{remaining} autres albums non affichés)");
            html.Append($"<p>(+{remaining} autres albums non affichés)</p>");
        }

        return (plain.ToString().TrimEnd(), html.ToString());
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
