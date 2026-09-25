using System.Text.Json.Serialization;

namespace Foundation.Core.Chatbot.Vision.Providers.Internal;

internal sealed class AnthropicMessageRequestDto
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; init; }

    [JsonPropertyName("messages")]
    public required List<AnthropicMessageDto> Messages { get; init; }
}

internal sealed class AnthropicMessageDto
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required List<AnthropicContentBlockDto> Content { get; init; }
}

internal sealed class AnthropicContentBlockDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("source")]
    public AnthropicImageSourceDto? Source { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class AnthropicImageSourceDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("media_type")]
    public required string MediaType { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

internal sealed class AnthropicMessageResponseDto
{
    [JsonPropertyName("content")]
    public List<AnthropicResponseContentBlockDto>? Content { get; init; }

    [JsonPropertyName("usage")]
    public AnthropicUsageDto? Usage { get; init; }
}

internal sealed class AnthropicResponseContentBlockDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class AnthropicUsageDto
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}
