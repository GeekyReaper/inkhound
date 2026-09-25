using System.Net.Http.Json;
using System.Text.Json;
using Foundation.Core.Chatbot.Vision.Providers.Internal;

namespace Foundation.Core.Chatbot.Vision.Providers;

/// <inheritdoc cref="AnthropicVisionProvider"/>
public sealed class GoogleVisionProvider : IVisionProvider
{
    public const string ProviderName = "Google";

    private readonly UsageStatisticsTracker _usageTracker = new();

    private HttpClient? _http;
    private GoogleVisionSettings _settings = new();

    public string Name => ProviderName;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey) && _http is not null;

    public string Model => _settings.Model;

    public void Reconfigure(HttpClient http, GoogleVisionSettings settings)
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

        var url = $"{GoogleVisionSettings.BaseUrl}/{_settings.Model}:generateContent";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(BuildRequestBody(request)),
        };
        httpRequest.Headers.Add("x-goog-api-key", _settings.ApiKey);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _http.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new VisionAnalysisException("Échec de la requête HTTP vers l'API Google.", ex);
        }

        using (httpResponse)
        {
            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new VisionAnalysisException($"L'API Google a retourné {(int)httpResponse.StatusCode} : {responseBody}");
            }

            var responseDto = JsonSerializer.Deserialize<GoogleGenerateContentResponseDto>(responseBody)
                ?? throw new VisionAnalysisException("Réponse Google vide ou invalide.");

            // Google n'expose pas d'en-têtes de quota restant : RateLimit reste null ici, seul
            // UsageStatisticsTracker (fenêtre locale) peut fournir une estimation.
            var usage = responseDto.UsageMetadata is { } usageDto
                ? new TokenUsage(usageDto.PromptTokenCount, usageDto.CandidatesTokenCount)
                : null;
            if (usage is not null)
            {
                _usageTracker.Record(usage);
            }

            var rawText = responseDto.Candidates?
                .FirstOrDefault()?.Content?.Parts?
                .FirstOrDefault(p => p.Text is not null)?.Text ?? string.Empty;
            JsonExtractionHelper.TryExtractJson(rawText, out var extractedJson);

            return new VisionResult(
                Success: true,
                ProviderName: ProviderName,
                RawResponseText: rawText,
                ExtractedJson: extractedJson,
                ErrorMessage: null,
                Usage: usage,
                RateLimit: null);
        }
    }

    public UsageStatisticsSnapshot GetUsageSnapshot() => _usageTracker.GetSnapshot();

    private GoogleGenerateContentRequestDto BuildRequestBody(VisionRequest request)
    {
        var base64Image = Convert.ToBase64String(request.Content);
        return new GoogleGenerateContentRequestDto
        {
            Contents =
            [
                new GoogleContentDto
                {
                    Parts =
                    [
                        new GooglePartDto { InlineData = new GoogleInlineDataDto { MimeType = request.MediaType, Data = base64Image } },
                        new GooglePartDto { Text = request.Prompt },
                    ],
                },
            ],
            GenerationConfig = new GoogleGenerationConfigDto { MaxOutputTokens = _settings.MaxOutputTokens },
        };
    }
}
