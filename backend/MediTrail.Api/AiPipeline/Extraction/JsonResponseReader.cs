using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediTrail.Api.AiPipeline.Extraction;

/// <summary>
/// Pulls a JSON object out of a model response.
///
/// The prompt forbids code fences and prose, and mostly that holds — but wrappers are a *formatting*
/// slip, not a content error, and failing the document over one would throw away a good extraction.
/// Anything beyond locating the object is left alone: §11.5 requires malformed output to be retried
/// once and then failed, never silently repaired into something the model did not say.
/// </summary>
public static partial class JsonResponseReader
{
    public static bool TryRead<T>(string response, out T? value, out string? error)
    {
        value = default;
        error = null;

        var json = Extract(response);

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The response contained no JSON object.";
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializerOptions);

            if (value is null)
            {
                error = "The response deserialized to null.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Models occasionally emit "500" where a number is expected. Accepting that is a formatting
        // tolerance, not a content guess — the digits are still the model's own.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    // Reasoning models emit their working before the answer. Qwen and DeepSeek use <think>,
    // and that working is full of illustrative JSON fragments — so it has to be removed before
    // looking for the object, not merely skipped over.
    [GeneratedRegex(@"<(think|thinking|reasoning)>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"<(think|thinking|reasoning)>.*$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex UnclosedThinkBlock();

    private static string Extract(string response)
    {
        var text = ThinkBlock().Replace(response, string.Empty);

        // A truncated response can leave <think> unclosed, which would otherwise swallow nothing
        // and leave the whole transcript in place.
        if (text.Contains("<think", StringComparison.OrdinalIgnoreCase))
        {
            text = UnclosedThinkBlock().Replace(text, string.Empty);
        }

        text = text.Trim();

        // Scan for a brace-balanced object, ignoring braces inside string literals. Taking
        // first '{' to last '}' would break on any stray brace in trailing commentary.
        var start = text.IndexOf('{');
        if (start < 0) return string.Empty;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return text[start..(i + 1)];
        }

        // Unbalanced — the response was cut off. Return it anyway so the parser reports the
        // truncation, which is more useful to the retry than "no JSON found".
        return text[start..];
    }
}
