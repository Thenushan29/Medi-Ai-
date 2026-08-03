using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediTrail.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediTrail.Api.AiPipeline.Providers;

/// <summary>
/// Any provider speaking the OpenAI chat-completions API — OpenRouter, Groq, or another endpoint.
/// The provider is configuration, not a code dependency (§14.2), which is also the mitigation for
/// an outage or rate limit during judging (§21).
///
/// Cost and determinism controls per §11.2 and §11.6: temperature 0, capped max_tokens,
/// capped attempts, and reasoning tokens disabled where the provider supports the switch.
/// </summary>
public sealed class OpenAiCompatibleClient(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<OpenAiCompatibleClient> logger) : IAiClient
{
    private readonly AiOptions _options = options.Value;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<AiCompletion> CompleteWithImageAsync(
        string systemPrompt, byte[] image, string imageContentType,
        string? model = null, CancellationToken ct = default)
    {
        // Images travel as a data URI in the message body — OpenRouter has no separate upload step.
        var dataUri = $"data:{Normalize(imageContentType)};base64,{Convert.ToBase64String(image)}";

        var request = new ChatRequest
        {
            Model = model ?? _options.ExtractionModel,
            Temperature = _options.Temperature,
            MaxTokens = _options.MaxTokens,
            // Groq rejects unknown top-level fields with a 400, so the switch is only sent to
            // providers that define it.
            Reasoning = _options.Provider == AiProvider.OpenRouter
                ? new ReasoningConfig { Enabled = false }
                : null,
            Messages =
            [
                new ChatMessage
                {
                    Role = "user",
                    Content =
                    [
                        new ContentPart { Type = "text", Text = systemPrompt },
                        new ContentPart { Type = "image_url", ImageUrl = new ImageUrl { Url = dataUri } }
                    ]
                }
            ]
        };

        return SendAsync(request, ct);
    }

    public Task<AiCompletion> CompleteAsync(
        string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = model ?? _options.ReasoningModel,
            Temperature = _options.Temperature,
            MaxTokens = _options.MaxTokens,
            // Groq rejects unknown top-level fields with a 400, so the switch is only sent to
            // providers that define it.
            Reasoning = _options.Provider == AiProvider.OpenRouter
                ? new ReasoningConfig { Enabled = false }
                : null,
            Messages =
            [
                new ChatMessage { Role = "system", Content = [new ContentPart { Type = "text", Text = systemPrompt }] },
                new ChatMessage { Role = "user",   Content = [new ContentPart { Type = "text", Text = userMessage }] }
            ]
        };

        return SendAsync(request, ct);
    }

    private async Task<AiCompletion> SendAsync(ChatRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try
            {
                var response = await http.PostAsJsonAsync("chat/completions", request, JsonOptions, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);

                    if (!IsRetryable(response.StatusCode) || attempt == _options.MaxAttempts)
                    {
                        throw new AiClientException(
                            $"{_options.Provider} returned {(int)response.StatusCode}: {Truncate(body)}");
                    }

                    logger.LogWarning("{Provider} {Status} on attempt {Attempt}/{Max}; retrying",
                        _options.Provider, (int)response.StatusCode, attempt, _options.MaxAttempts);

                    await BackoffAsync(attempt, ct);
                    continue;
                }

                var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct)
                    ?? throw new AiClientException($"{_options.Provider} returned an empty body.");

                if (payload.Error is not null)
                {
                    throw new AiClientException($"{_options.Provider} error: {payload.Error.Message}");
                }

                var content = payload.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(content))
                {
                    // A blank completion is usually a truncated response or a refusal. Retrying the
                    // identical request rarely helps, so surface it.
                    throw new AiClientException(
                        $"{_options.Provider} returned no content (finish reason: {payload.Choices?.FirstOrDefault()?.FinishReason ?? "unknown"}).");
                }

                stopwatch.Stop();

                logger.LogInformation(
                    "{Model} ok in {Ms}ms (prompt {PromptTokens}, completion {CompletionTokens})",
                    payload.Model ?? request.Model, stopwatch.ElapsedMilliseconds,
                    payload.Usage?.PromptTokens, payload.Usage?.CompletionTokens);

                return new AiCompletion
                {
                    Content = content,
                    Model = payload.Model ?? request.Model,
                    PromptTokens = payload.Usage?.PromptTokens,
                    CompletionTokens = payload.Usage?.CompletionTokens,
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException && attempt < _options.MaxAttempts)
            {
                lastError = ex;
                logger.LogWarning(ex, "OpenRouter transport failure on attempt {Attempt}/{Max}; retrying",
                    attempt, _options.MaxAttempts);
                await BackoffAsync(attempt, ct);
            }
        }

        throw new AiClientException(
            $"{_options.Provider} did not respond after {_options.MaxAttempts} attempts.", lastError);
    }

    // 429 and 5xx are worth another attempt; 400/401/403 mean the request or key is wrong and
    // retrying just burns budget.
    private static bool IsRetryable(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
               or HttpStatusCode.InternalServerError
               or HttpStatusCode.BadGateway
               or HttpStatusCode.ServiceUnavailable
               or HttpStatusCode.GatewayTimeout;

    private static Task BackoffAsync(int attempt, CancellationToken ct) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);

    private static string Normalize(string contentType) =>
        contentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : contentType;

    private static string Truncate(string text) =>
        text.Length <= 500 ? text : text[..500] + "…";

    // ---- Wire shapes ----

    private sealed record ChatRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("messages")] public required IReadOnlyList<ChatMessage> Messages { get; init; }
        [JsonPropertyName("temperature")] public double Temperature { get; init; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; init; }
        [JsonPropertyName("reasoning")] public ReasoningConfig? Reasoning { get; init; }
    }

    private sealed record ReasoningConfig
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    }

    private sealed record ChatMessage
    {
        [JsonPropertyName("role")] public required string Role { get; init; }
        [JsonPropertyName("content")] public required IReadOnlyList<ContentPart> Content { get; init; }
    }

    private sealed record ContentPart
    {
        [JsonPropertyName("type")] public required string Type { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
        [JsonPropertyName("image_url")] public ImageUrl? ImageUrl { get; init; }
    }

    private sealed record ImageUrl
    {
        [JsonPropertyName("url")] public required string Url { get; init; }
    }

    private sealed record ChatResponse
    {
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("choices")] public IReadOnlyList<Choice>? Choices { get; init; }
        [JsonPropertyName("usage")] public Usage? Usage { get; init; }
        [JsonPropertyName("error")] public ErrorPayload? Error { get; init; }
    }

    private sealed record Choice
    {
        [JsonPropertyName("message")] public ResponseMessage? Message { get; init; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
    }

    private sealed record ResponseMessage
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }

    private sealed record Usage
    {
        [JsonPropertyName("prompt_tokens")] public int? PromptTokens { get; init; }
        [JsonPropertyName("completion_tokens")] public int? CompletionTokens { get; init; }
    }

    private sealed record ErrorPayload
    {
        [JsonPropertyName("message")] public string? Message { get; init; }
    }
}
