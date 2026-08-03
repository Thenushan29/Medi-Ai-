using System.Text.Json;

namespace MediTrail.Api.AiPipeline.Extraction;

/// <summary>
/// Pulls a JSON object out of a model response.
///
/// The prompt forbids code fences and prose, and mostly that holds — but a wrapper is a *formatting*
/// slip, not a content error, and failing the document over it would throw away a good extraction.
/// Anything beyond unwrapping is left alone: §11.5 requires malformed output to be retried once and
/// then failed, never silently repaired into something the model did not say.
/// </summary>
public static class JsonResponseReader
{
    public static bool TryRead<T>(string response, out T? value, out string? error)
    {
        value = default;
        error = null;

        var json = Unwrap(response);

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

    /// <summary>Strips a markdown fence and any text either side of the outermost JSON object.</summary>
    private static string Unwrap(string response)
    {
        var text = response.Trim();

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start ? text[start..(end + 1)] : string.Empty;
    }
}
