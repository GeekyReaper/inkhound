namespace Foundation.Core.Chatbot.Features;

/// <summary>Message de suite, envoyé après la réponse principale (voir <see cref="FeatureExecutionResult.AdditionalMessages"/>).</summary>
public sealed record FeatureMessage(string Plain, string? Html = null);

public sealed record FeatureExecutionResult(
    bool Success, string PlainText, string? HtmlBody = null, IReadOnlyList<FeatureMessage>? AdditionalMessages = null)
{
    /// <summary>
    /// Une commande répond en général par un seul message. Quand le résultat se lit mieux réparti sur
    /// plusieurs messages de room (une fiche puis une liste, par exemple), passer additionalMessages :
    /// le socle envoie d'abord le message principal, puis chaque message additionnel dans l'ordre.
    /// </summary>
    public static FeatureExecutionResult Ok(
        string plainText, string? htmlBody = null, IReadOnlyList<FeatureMessage>? additionalMessages = null) =>
        new(true, plainText, htmlBody, additionalMessages);

    public static FeatureExecutionResult Fail(string plainText) => new(false, plainText);
}
