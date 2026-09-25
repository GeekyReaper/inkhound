using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Foundation.Core.Chatbot.Vision.Providers.Internal;

namespace Foundation.Core.Chatbot.Vision.Providers;

/// <summary>
/// Provider vision Anthropic. Instancié une seule fois par le socle chatbot : le
/// <see cref="UsageStatisticsTracker"/> qu'il porte doit survivre aux rechargements d'options,
/// d'où <see cref="Reconfigure"/> plutôt qu'une reconstruction.
/// </summary>
public sealed class AnthropicVisionProvider : IVisionProvider
{
    public const string ProviderName = "Anthropic";

    private readonly UsageStatisticsTracker _usageTracker = new();

    private HttpClient? _http;
    private AnthropicVisionSettings _settings = new();

    public string Name => ProviderName;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey) && _http is not null;

    public string Model => _settings.Model;

    public void Reconfigure(HttpClient http, AnthropicVisionSettings settings)
    {
        _http = http;
        _settings = settings;
        _usageTracker.SetLimits(settings.Limits);
    }

    public async Task<VisionResult> AnalyzeAsync(VisionRequest request, CancellationToken cancellationToken = default)
    {
        if (_http is null || string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new ProviderNotConfiguredException(ProviderName);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, AnthropicVisionSettings.BaseUrl)
        {
            Content = JsonContent.Create(BuildRequestBody(request)),
        };
        httpRequest.Headers.Add("x-api-key", _settings.ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVisionSettings.ApiVersion);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _http.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new VisionAnalysisException("Échec de la requête HTTP vers l'API Anthropic.", ex);
        }

        using (httpResponse)
        {
            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new VisionAnalysisException($"L'API Anthropic a retourné {(int)httpResponse.StatusCode} : {responseBody}");
            }

            var rateLimit = TryParseRateLimit(httpResponse.Headers);

            var responseDto = JsonSerializer.Deserialize<AnthropicMessageResponseDto>(responseBody)
                ?? throw new VisionAnalysisException("Réponse Anthropic vide ou invalide.");

            var usage = responseDto.Usage is { } usageDto
                ? new TokenUsage(usageDto.InputTokens, usageDto.OutputTokens)
                : null;
            if (usage is not null)
            {
                _usageTracker.Record(usage);
            }

            var rawText = responseDto.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;
            JsonExtractionHelper.TryExtractJson(rawText, out var extractedJson);

            return new VisionResult(
                Success: true,
                ProviderName: ProviderName,
                RawResponseText: rawText,
                ExtractedJson: extractedJson,
                ErrorMessage: null,
                Usage: usage,
                RateLimit: rateLimit);
        }
    }

    public UsageStatisticsSnapshot GetUsageSnapshot() => _usageTracker.GetSnapshot();

    private AnthropicMessageRequestDto BuildRequestBody(VisionRequest request)
    {
        var base64Image = Convert.ToBase64String(request.Content);
        return new AnthropicMessageRequestDto
        {
            Model = _settings.Model,
            MaxTokens = _settings.MaxTokens,
            Messages =
            [
                new AnthropicMessageDto
                {
                    Role = "user",
                    Content =
                    [
                        new AnthropicContentBlockDto
                        {
                            Type = "image",
                            Source = new AnthropicImageSourceDto
                            {
                                Type = "base64",
                                MediaType = request.MediaType,
                                Data = base64Image,
                            },
                        },
                        new AnthropicContentBlockDto { Type = "text", Text = request.Prompt },
                    ],
                },
            ],
        };
    }

    private static RateLimitSnapshot? TryParseRateLimit(HttpResponseHeaders headers)
    {
        var requestsLimit = ParseIntHeader(headers, "anthropic-ratelimit-requests-limit");
        var requestsRemaining = ParseIntHeader(headers, "anthropic-ratelimit-requests-remaining");
        var requestsReset = ParseDateHeader(headers, "anthropic-ratelimit-requests-reset");
        var tokensLimit = ParseIntHeader(headers, "anthropic-ratelimit-tokens-limit");
        var tokensRemaining = ParseIntHeader(headers, "anthropic-ratelimit-tokens-remaining");
        var tokensReset = ParseDateHeader(headers, "anthropic-ratelimit-tokens-reset");

        if (requestsLimit is null && tokensLimit is null)
        {
            return null;
        }

        return new RateLimitSnapshot(requestsLimit, requestsRemaining, requestsReset, tokensLimit, tokensRemaining, tokensReset);
    }

    private static int? ParseIntHeader(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseDateHeader(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) && DateTimeOffset.TryParse(values.FirstOrDefault(), out var value)
            ? value
            : null;
}
