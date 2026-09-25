using System.Text.Json.Serialization;

namespace Foundation.Core.Chatbot.Vision.Providers.Internal;

internal sealed class GoogleGenerateContentRequestDto
{
    [JsonPropertyName("contents")]
    public List<GoogleContentDto> Contents { get; init; } = [];

    [JsonPropertyName("generationConfig")]
    public GoogleGenerationConfigDto GenerationConfig { get; init; } = new();
}

internal sealed class GoogleContentDto
{
    [JsonPropertyName("parts")]
    public List<GooglePartDto> Parts { get; init; } = [];
}

internal sealed class GooglePartDto
{
    [JsonPropertyName("inline_data")]
    public GoogleInlineDataDto? InlineData { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class GoogleInlineDataDto
{
    [JsonPropertyName("mime_type")]
    public string MimeType { get; init; } = "";

    [JsonPropertyName("data")]
    public string Data { get; init; } = "";
}

internal sealed class GoogleGenerationConfigDto
{
    [JsonPropertyName("maxOutputTokens")]
    public int MaxOutputTokens { get; init; }
}

internal sealed class GoogleGenerateContentResponseDto
{
    [JsonPropertyName("candidates")]
    public List<GoogleCandidateDto>? Candidates { get; init; }

    [JsonPropertyName("usageMetadata")]
    public GoogleUsageMetadataDto? UsageMetadata { get; init; }
}

internal sealed class GoogleCandidateDto
{
    [JsonPropertyName("content")]
    public GoogleContentDto? Content { get; init; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; init; }
}

internal sealed class GoogleUsageMetadataDto
{
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; init; }

    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; init; }
}
