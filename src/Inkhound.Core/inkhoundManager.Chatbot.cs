using Foundation.Core.Chatbot;
using Inkhound.Core.Chatbot;

namespace Inkhound.Core;

/// <summary>
/// Accès au module Chatbot depuis la couche Web. Le service vit dans le registre de
/// <c>BaseServiceManager</c> comme tout autre module ; ce partial ne fait qu'exposer son état et
/// son pilotage manuel.
/// </summary>
public partial class InkhoundManager
{
    private ChatbotService Chatbot => GetService<ChatbotService, ChatbotOptions>();

    public ChatbotRuntimeStatus GetChatbotStatus() => Chatbot.GetStatus();

    /// <summary>
    /// Démarre le bot sans modifier l'option <c>Enabled</c> : c'est un contrôle ponctuel, et un
    /// rechargement d'options ultérieur réappliquera la valeur persistée.
    /// </summary>
    public Task StartChatbotAsync() => Chatbot.StartAsync();

    /// <inheritdoc cref="StartChatbotAsync"/>
    public Task StopChatbotAsync() => Chatbot.StopAsync();
}
