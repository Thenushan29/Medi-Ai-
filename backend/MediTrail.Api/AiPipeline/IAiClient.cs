namespace MediTrail.Api.AiPipeline;

/// <summary>
/// The one place the application talks to a language model. Behind an interface so the provider is
/// a configuration choice, not a code dependency (§14.2) — which is also the mitigation for an AI
/// provider outage during judging (§21).
/// </summary>
public interface IAiClient
{
    /// <summary>
    /// Sends a prompt with one or more images and returns the model's text.
    ///
    /// Several images means several pages of **one** document, sent in a single call — a two-page
    /// prescription is one prescribing event, and extracting each page separately would split the
    /// medication list from the advice that qualifies it.
    /// </summary>
    Task<AiCompletion> CompleteWithImagesAsync(
        string systemPrompt,
        IReadOnlyList<byte[]> images,
        string imageContentType,
        string? model = null,
        CancellationToken ct = default);

    /// <summary>Text-only completion, for cross-check, trend explanation and chat.</summary>
    Task<AiCompletion> CompleteAsync(
        string systemPrompt,
        string userMessage,
        string? model = null,
        CancellationToken ct = default);
}

public sealed record AiCompletion
{
    public required string Content { get; init; }
    public required string Model { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public required int LatencyMs { get; init; }
}

/// <summary>Raised when the provider cannot be reached or refuses after all retries are spent.</summary>
public sealed class AiClientException(string message, Exception? inner = null)
    : Exception(message, inner);
