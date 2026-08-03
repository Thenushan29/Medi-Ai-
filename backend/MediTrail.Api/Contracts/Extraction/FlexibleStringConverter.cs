using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediTrail.Api.Contracts.Extraction;

/// <summary>
/// Reads a JSON string, number or boolean into a <see cref="string"/>.
///
/// Free-text fields legitimately receive both shapes: an age prints as "58 years" on one document
/// and `58` on another, a dose as "1 tablet" or `2`. Rejecting the numeric form would fail the whole
/// document over formatting, discarding a correct reading.
///
/// This is the same class of tolerance as <c>JsonNumberHandling.AllowReadingFromString</c> — the
/// digits are still exactly what the model reported. It never invents or reinterprets a value.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var whole)
                ? whole.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => throw new JsonException(
                $"Expected a string, number or boolean but found {reader.TokenType}.")
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
