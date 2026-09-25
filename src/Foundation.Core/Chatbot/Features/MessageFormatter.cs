namespace Foundation.Core.Chatbot.Features;

public static class MessageFormatter
{
    public static (string Plain, string? Html) ForUnknownCommand(string name) =>
        ($"Commande inconnue : !{name}. Envoyez !help pour la liste des commandes disponibles.", null);

    public static (string Plain, string? Html) ForResult(FeatureExecutionResult result) =>
        (result.PlainText, result.HtmlBody);
}
