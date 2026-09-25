namespace Foundation.Core.Chatbot.Features.Implementations;

public sealed class EchoFeature : IChatFeature
{
    public string Name => "echo";
    public string Description => "Répète le texte passé en argument. Usage : !echo <texte>";

    public Task<FeatureExecutionResult> ExecuteAsync(FeatureCommand command, CancellationToken ct)
    {
        var text = string.Join(' ', command.Args);
        return Task.FromResult(FeatureExecutionResult.Ok(
            string.IsNullOrEmpty(text) ? "(rien à répéter)" : text));
    }
}
