namespace Foundation.Core.Chatbot;

/// <summary>
/// Canal de trace du socle chatbot. Remplace <c>ILogger&lt;T&gt;</c> du projet d'origine : Foundation.Core
/// ne prend aucune dépendance NuGet, et les traces doivent de toute façon partir vers le mécanisme
/// interne (SendTrace → GlobalTraceHandler → SignalR) plutôt que vers un logger externe.
/// </summary>
public interface IChatTrace
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
