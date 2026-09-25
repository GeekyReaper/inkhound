namespace Foundation.Core.Chatbot.Features;

/// <summary>
/// Clôture d'une demande conversationnelle : un message dédié « 🏁 Demande terminée » est envoyé
/// (en message additionnel) après le message substantiel de fin, quelle qu'en soit l'issue
/// (succès, déjà connu, annulation, erreur…), pour marquer clairement la fin de l'échange.
/// </summary>
public static class RequestClosure
{
    public static readonly FeatureMessage ClosingMessage = new(
        "🏁 Demande terminée",
        "<p>🏁 <b>Demande terminée</b></p>");

    /// <summary>Résultat terminal : message substantiel suivi du message dédié de clôture.</summary>
    public static FeatureExecutionResult Closed(string plain, string? html = null, bool success = true) =>
        new(success, plain, html, [ClosingMessage]);
}
