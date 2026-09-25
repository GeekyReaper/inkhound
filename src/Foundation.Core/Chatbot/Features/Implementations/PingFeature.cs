namespace Foundation.Core.Chatbot.Features.Implementations;

public sealed class PingFeature : IChatFeature
{
    public string Name => "ping";
    public string Description => "Répond pong avec la latence écoulée depuis l'envoi de la commande.";

    public Task<FeatureExecutionResult> ExecuteAsync(FeatureCommand command, CancellationToken ct)
    {
        var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(command.TriggerEvent.OriginServerTs);
        var latency = DateTimeOffset.UtcNow - sentAt;
        return Task.FromResult(FeatureExecutionResult.Ok($"pong ({latency.TotalMilliseconds:F0} ms)"));
    }
}
