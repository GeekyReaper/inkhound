using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foundation.Core.Chatbot.Vision;

/// <summary>
/// Tentative « best effort » d'extraction d'un objet/tableau JSON dans une réponse texte de LLM :
/// bloc de code markdown, texte entier, ou scan par profondeur d'accolades/crochets. Aucune
/// contrainte de format n'étant imposée au modèle, la réponse peut arriver sous n'importe laquelle
/// de ces formes.
/// </summary>
internal static partial class JsonExtractionHelper
{
    [GeneratedRegex("```(?:json)?\\s*(.*?)```", RegexOptions.Singleline)]
    private static partial Regex MarkdownFenceRegex();

    public static bool TryExtractJson(string? rawText, out string? extractedJson)
    {
        extractedJson = null;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        var fenceMatch = MarkdownFenceRegex().Match(rawText);
        if (fenceMatch.Success && IsValidJson(fenceMatch.Groups[1].Value.Trim(), out extractedJson))
        {
            return true;
        }

        if (IsValidJson(rawText.Trim(), out extractedJson))
        {
            return true;
        }

        var scanned = ScanForJson(rawText);
        return scanned is not null && IsValidJson(scanned, out extractedJson);
    }

    private static bool IsValidJson(string candidate, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(candidate);
            normalized = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ScanForJson(string text)
    {
        var startIndex = text.IndexOfAny(['{', '[']);
        if (startIndex == -1)
        {
            return null;
        }

        var openChar = text[startIndex];
        var closeChar = openChar == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == openChar)
            {
                depth++;
            }
            else if (c == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(startIndex, i - startIndex + 1);
                }
            }
        }

        return null;
    }
}
