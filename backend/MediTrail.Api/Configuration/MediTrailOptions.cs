using System.ComponentModel.DataAnnotations;

namespace MediTrail.Api.Configuration;

/// <summary>Which OpenAI-compatible provider to talk to. Differences are small but not zero.</summary>
public enum AiProvider
{
    /// <summary>Accepts a `reasoning` block and attribution headers.</summary>
    OpenRouter,
    /// <summary>Free tier, very fast. Rejects unknown top-level fields, so `reasoning` is omitted.</summary>
    Groq,
    /// <summary>Anything else speaking the OpenAI chat-completions API. Nothing provider-specific is sent.</summary>
    OpenAiCompatible
}

/// <summary>
/// AI provider access. Provider, endpoint and model are all configuration, never hard-coded, so
/// they can be swapped without a code change if one degrades mid-competition (PRD §11.2, §21).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.OpenRouter;

    /// <summary>Server-side only. Never sent to the browser (§17.2).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Chat-completions base URL. Leave empty to use the provider's default:
    /// OpenRouter <c>https://openrouter.ai/api/v1</c>, Groq <c>https://api.groq.com/openai/v1</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Vision-capable model used for extraction.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ExtractionModel { get; set; } = "google/gemini-2.5-flash";

    /// <summary>Text model for cross-check, trends and chat. May be the same as the extraction model.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ReasoningModel { get; set; } = "google/gemini-2.5-flash";

    public string ResolveBaseUrl() => !string.IsNullOrWhiteSpace(BaseUrl)
        ? BaseUrl
        : Provider switch
        {
            AiProvider.Groq => "https://api.groq.com/openai/v1",
            _ => "https://openrouter.ai/api/v1"
        };

    /// <summary>Temperature 0 for every call — extraction and cross-checking must be reproducible (§11.2).</summary>
    public double Temperature { get; set; }

    /// <summary>Hard cap per call, to prevent a runaway response from consuming the budget (§11.6).</summary>
    public int MaxTokens { get; set; } = 8000;

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Attempts per call before the document is marked failed (§9.3).</summary>
    public int MaxAttempts { get; set; } = 3;

    // Sent as OpenRouter attribution headers; harmless if unset.
    public string? SiteUrl { get; set; }
    public string? SiteName { get; set; }
}

/// <summary>Supabase Postgres connection plus object storage for the original files.</summary>
public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    /// <summary>Project URL, e.g. https://xxxx.supabase.co</summary>
    [Required(AllowEmptyStrings = false)]
    public string Url { get; set; } = string.Empty;

    /// <summary>Service-role key. Server-side only — this key bypasses row-level security.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ServiceKey { get; set; } = string.Empty;

    /// <summary>Round 1 uses a public bucket; production uses a private bucket with signed URLs (§16.3).</summary>
    public string Bucket { get; set; } = "documents";

    public bool BucketIsPublic { get; set; } = true;
}

/// <summary>Upload limits and pipeline behaviour.</summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>10 MB per file (FR-2.3). Enforced server-side; the client also downscales above 2000px.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>PNG, JPG, JPEG, PDF (FR-2.2). Anything else is rejected with a readable reason.</summary>
    public string[] AllowedContentTypes { get; set; } =
    [
        "image/png",
        "image/jpeg",
        "image/jpg",
        "application/pdf"
    ];

    public string[] AllowedExtensions { get; set; } = [".png", ".jpg", ".jpeg", ".pdf"];

    /// <summary>Documents processed concurrently by the background worker.</summary>
    public int WorkerConcurrency { get; set; } = 3;

    /// <summary>Reuse a prior extraction when an identical file hash is uploaded again (FR-2.6).</summary>
    public bool EnableExtractionCache { get; set; } = true;
}

/// <summary>openFDA verification. Optional enhancement — never a hard dependency (§14.4).</summary>
public sealed class OpenFdaOptions
{
    public const string SectionName = "OpenFda";

    public string BaseUrl { get; set; } = "https://api.fda.gov";

    /// <summary>Optional free key; raises the daily rate limit. Works without one.</summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Each generic name is fetched once and cached for this long (§11.6).</summary>
    public int CacheHours { get; set; } = 24;
}
