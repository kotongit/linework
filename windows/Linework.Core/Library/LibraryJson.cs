using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Linework.Core.Library;

/// <summary>The JSON both apps read: what Swift's encoder writes with prettyPrinted, sortedKeys and withoutEscapingSlashes.</summary>
public static class LibraryJson
{
    public static JsonSerializerOptions Options { get; } = MakeOptions();

    static JsonSerializerOptions MakeOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
        };
        options.Converters.Add(new DateConverter());
        options.Converters.Add(new UuidConverter());
        options.Converters.Add(new FiniteDoubleConverter());
        return options;
    }

    /// <summary>A number the way Swift's encoder writes it: shortest round-trip form, no ".0", lowercase exponent.</summary>
    public static string FormatNumber(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 9007199254740992d)
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e", StringComparison.Ordinal);
    }

    /// <summary>Throws if the text doesn't decode to T the way Swift's synthesized decoders would: a missing or null required
    /// value, or a null inside a list, is a failure.</summary>
    public static T Decode<T>(byte[] data)
    {
        using var document = JsonDocument.Parse(data);
        if (HasNullInArray(document.RootElement)) throw new JsonException("A list holds a null.");
        return document.RootElement.Deserialize<T>(Options) ?? throw new JsonException("Nothing there.");
    }

    static bool HasNullInArray(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Null || HasNullInArray(x)),
        JsonValueKind.Object => e.EnumerateObject().Any(p => HasNullInArray(p.Value)),
        _ => false,
    };

    public static byte[] Encode<T>(T value)
    {
        var sb = new StringBuilder();
        Write(sb, JsonSerializer.SerializeToElement(value, Options), 0);
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    static void Write(StringBuilder sb, JsonElement e, int depth)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                sb.Append("{\n");
                for (var i = 0; i < properties.Count; i++)
                {
                    Indent(sb, depth + 1);
                    WriteString(sb, properties[i].Name);
                    sb.Append(" : ");
                    Write(sb, properties[i].Value, depth + 1);
                    if (i < properties.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                if (properties.Count == 0) sb.Append('\n');
                Indent(sb, depth);
                sb.Append('}');
                break;
            case JsonValueKind.Array:
                var items = e.EnumerateArray().ToList();
                sb.Append("[\n");
                for (var i = 0; i < items.Count; i++)
                {
                    Indent(sb, depth + 1);
                    Write(sb, items[i], depth + 1);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                if (items.Count == 0) sb.Append('\n');
                Indent(sb, depth);
                sb.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(sb, e.GetString()!);
                break;
            case JsonValueKind.Number:
                var raw = e.GetRawText();
                sb.Append(raw.AsSpan().IndexOfAny('.', 'e', 'E') >= 0 ? FormatNumber(e.GetDouble()) : raw);
                break;
            default:
                sb.Append(e.GetRawText());
                break;
        }
    }

    static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

    static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        sb.Append('"');
    }
}

/// <summary>Exactly yyyy-MM-ddTHH:mm:ssZ when written. What Swift's .iso8601 reads when read: no fractional seconds.</summary>
public sealed class DateConverter : JsonConverter<DateTimeOffset>
{
    static readonly string[] Formats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:sszzz"];

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : throw new JsonException();
        return DateTimeOffset.TryParseExact(text, Formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : throw new JsonException($"Bad date: {text}");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions o) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Formats[0], CultureInfo.InvariantCulture));
}

/// <summary>Uppercase, as Swift writes them.</summary>
public sealed class UuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) =>
        reader.TokenType == JsonTokenType.String && Guid.TryParseExact(reader.GetString(), "D", out var value) ? value : throw new JsonException("Bad UUID");

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions o) =>
        writer.WriteStringValue(value.ToString("D").ToUpperInvariant());
}

/// <summary>Swift's decoder refuses numbers too big to hold, such as 1e999, so this does too.</summary>
public sealed class FiniteDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var value = reader.GetDouble();
        return double.IsFinite(value) ? value : throw new JsonException("Number out of range");
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions o) => writer.WriteNumberValue(value);
}
