namespace Foundation.Core.Chatbot.Vision;

public sealed class VisionAnalysisException : Exception
{
    public VisionAnalysisException(string message) : base(message) { }
    public VisionAnalysisException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class ProviderNotConfiguredException(string providerName)
    : Exception($"Le provider \"{providerName}\" est connu mais aucune clé API n'est configurée.")
{
    public string ProviderName { get; } = providerName;
}

public sealed class UnknownProviderException(string providerName)
    : Exception($"Provider inconnu : \"{providerName}\".")
{
    public string ProviderName { get; } = providerName;
}
