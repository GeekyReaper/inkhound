using System.Net;
using System.Text;

namespace Foundation.Core.Chatbot.Features.Implementations;

/// <summary>
/// Liste les commandes disponibles. Le catalogue est résolu paresseusement : HelpFeature fait
/// elle-même partie de ce catalogue, qui n'existe donc pas encore au moment de sa construction.
/// </summary>
public sealed class HelpFeature(Func<FeatureCatalog> catalogAccessor) : IChatFeature
{
    public string Name => "help";
    public string Description => "Liste toutes les commandes disponibles.";

    public Task<FeatureExecutionResult> ExecuteAsync(FeatureCommand command, CancellationToken ct)
    {
        var plain = new StringBuilder();
        var html = new StringBuilder("<p>Commandes disponibles :</p><ul>");

        foreach (var feature in catalogAccessor().All.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            plain.Append('!').Append(feature.Name).Append(" — ").Append(feature.Description).Append('\n');
            html.Append("<li><code>!").Append(Enc(feature.Name)).Append("</code> — ")
                .Append(Enc(feature.Description)).Append("</li>");
        }

        html.Append("</ul>");
        return Task.FromResult(FeatureExecutionResult.Ok(plain.ToString().TrimEnd(), html.ToString()));
    }

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
