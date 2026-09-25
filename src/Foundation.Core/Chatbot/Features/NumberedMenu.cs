using System.Net;

namespace Foundation.Core.Chatbot.Features;

/// <summary>
/// Construit un menu numéroté (texte + HTML) partagé par les assistants « tape un nombre ».
/// Rendu en &lt;ol&gt;/&lt;li&gt; — jamais &lt;table&gt;, qu'Element X iOS ne rend pas du tout — et pied
/// de menu uniforme. Chaque item peut fournir son propre rendu HTML (pour des liens cliquables).
/// </summary>
public static class NumberedMenu
{
    public static (string Plain, string Html) Build(string header, IReadOnlyList<string> labels) =>
        Build(header, $"<p>{Enc(header)}</p>", Wrap(labels));

    public static (string Plain, string Html) Build(string header, IReadOnlyList<(string Plain, string Html)> items) =>
        Build(header, $"<p>{Enc(header)}</p>", items);

    public static (string Plain, string Html) Build(string headerPlain, string headerHtml, IReadOnlyList<string> labels) =>
        Build(headerPlain, headerHtml, Wrap(labels));

    public static (string Plain, string Html) Build(
        string headerPlain, string headerHtml, IReadOnlyList<(string Plain, string Html)> items)
    {
        var plainLines = new List<string> { headerPlain };
        var htmlItems = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            plainLines.Add($"{i + 1}. {items[i].Plain}");
            htmlItems.Add($"<li>{items[i].Html}</li>");
        }

        var footer = $"Répondez avec le numéro correspondant (1-{items.Count}).";
        plainLines.Add(footer);

        var plain = string.Join('\n', plainLines);
        var html = $"{headerHtml}<ol>{string.Join("", htmlItems)}</ol><p>{Enc(footer)}</p>";
        return (plain, html);
    }

    private static IReadOnlyList<(string, string)> Wrap(IReadOnlyList<string> labels) =>
        [.. labels.Select(l => (l, Enc(l)))];

    public static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
