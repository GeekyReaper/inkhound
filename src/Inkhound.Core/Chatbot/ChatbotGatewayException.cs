namespace Inkhound.Core.Chatbot;

/// <summary>
/// Erreur remontée par <see cref="IInkhoundChatbotGateway"/>. Remplace l'<c>InkhoundApiException</c>
/// du bot d'origine : en appel direct, le domaine lève des exceptions variées
/// (<see cref="InvalidOperationException"/> pour une source inconnue, <see cref="FormatException"/>
/// sur un sourceId non numérique…), que la passerelle uniformise pour que les commandes gardent un
/// seul type d'erreur à intercepter.
/// </summary>
public sealed class ChatbotGatewayException : Exception
{
    public ChatbotGatewayException(string message) : base(message) { }
    public ChatbotGatewayException(string message, Exception innerException) : base(message, innerException) { }
}
