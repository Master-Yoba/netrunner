using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Netrunner.Core;

/// <summary>
/// Produces deterministic UTF-8 JSON bytes for engine artifacts. Engine values
/// are first serialized through the strict EngineJson contract; canonicalizing
/// then recursively sorts object properties while preserving array order. JSON
/// numbers are normalized as exact base-10 decimals (never through a binary
/// floating-point conversion), so equivalent spellings such as 1, 1.0, 1e0,
/// and -0 have one representation. Extremely large exponents are rejected as
/// unsupported rather than causing an unbounded allocation.
/// </summary>
public static class CanonicalJson
{
    private const long MaximumCanonicalNumberLength = 1_000_000;

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    public static byte[] SerializeUtf8<T>(T value)
    {
        byte[] json = EngineJson.SerializeUtf8(value);
        return Canonicalize(json);
    }

    public static string Serialize<T>(T value) =>
        Encoding.UTF8.GetString(SerializeUtf8(value));

    public static byte[] Canonicalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Canonicalize(Encoding.UTF8.GetBytes(json));
    }

    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        using JsonDocument document = JsonDocument.Parse(utf8Json.ToArray(), ParseOptions);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
        }))
        {
            WriteElement(writer, document.RootElement);
        }

        return buffer.ToArray();
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var propertyNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject()
                    .OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    if (!propertyNames.Add(property.Name))
                    {
                        throw new JsonException($"Duplicate JSON object property '{property.Name}'.");
                    }

                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(NormalizeNumber(element.GetRawText()), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unsupported JSON value kind '{element.ValueKind}'.");
        }
    }

    private static string NormalizeNumber(string raw)
    {
        ReadOnlySpan<char> text = raw.AsSpan();
        if (text.IsEmpty)
        {
            throw new JsonException("A JSON number must not be empty.");
        }

        int index = 0;
        bool negative = text[index] == '-';
        if (negative)
        {
            index++;
        }

        int integerStart = index;
        if (index >= text.Length || text[index] < '0' || text[index] > '9')
        {
            throw new JsonException($"Unsupported JSON number '{raw}'.");
        }

        if (text[index] == '0')
        {
            index++;
            if (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                throw new JsonException($"Ambiguous JSON number '{raw}' has a leading zero.");
            }
        }
        else
        {
            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                index++;
            }
        }

        int integerEnd = index;
        int fractionDigits = 0;
        if (index < text.Length && text[index] == '.')
        {
            index++;
            int fractionStart = index;
            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                index++;
            }

            fractionDigits = index - fractionStart;
            if (fractionDigits == 0)
            {
                throw new JsonException($"Unsupported JSON number '{raw}' has no fractional digits.");
            }
        }

        long exponent = 0;
        if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
        {
            index++;
            bool negativeExponent = false;
            if (index < text.Length && (text[index] == '+' || text[index] == '-'))
            {
                negativeExponent = text[index] == '-';
                index++;
            }

            int exponentStart = index;
            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                int digit = text[index] - '0';
                if (exponent > MaximumCanonicalNumberLength
                    || (exponent == MaximumCanonicalNumberLength && digit > 0))
                {
                    throw new JsonException($"Unsupported JSON number '{raw}' has an exponent outside the canonical range.");
                }

                exponent = checked((exponent * 10) + digit);
                index++;
            }

            if (index == exponentStart)
            {
                throw new JsonException($"Unsupported JSON number '{raw}' has no exponent digits.");
            }

            if (negativeExponent)
            {
                exponent = -exponent;
            }
        }

        if (index != text.Length)
        {
            throw new JsonException($"Unsupported JSON number '{raw}'.");
        }

        string digits = string.Concat(text[integerStart..integerEnd], text[(integerEnd + (fractionDigits > 0 ? 1 : 0))..(integerEnd + (fractionDigits > 0 ? 1 : 0) + fractionDigits)]);
        int firstNonzero = 0;
        while (firstNonzero < digits.Length - 1 && digits[firstNonzero] == '0')
        {
            firstNonzero++;
        }

        if (digits[firstNonzero..].All(static digit => digit == '0'))
        {
            return "0";
        }

        digits = digits[firstNonzero..];
        long scale = exponent - fractionDigits;
        int lastNonzero = digits.Length - 1;
        while (lastNonzero > 0 && digits[lastNonzero] == '0')
        {
            lastNonzero--;
            scale++;
        }

        digits = digits[..(lastNonzero + 1)];
        long outputLength = scale >= 0
            ? checked(digits.Length + scale)
            : Math.Max(digits.Length, checked(2 - scale));
        if (outputLength > MaximumCanonicalNumberLength)
        {
            throw new JsonException($"Unsupported JSON number '{raw}' exceeds the canonical length limit.");
        }

        var normalized = new StringBuilder(checked((int)outputLength + (negative ? 1 : 0)));
        if (negative)
        {
            normalized.Append('-');
        }

        if (scale >= 0)
        {
            normalized.Append(digits);
            normalized.Append('0', checked((int)scale));
        }
        else
        {
            long decimalPosition = digits.Length + scale;
            if (decimalPosition > 0)
            {
                normalized.Append(digits.AsSpan(0, checked((int)decimalPosition)));
                normalized.Append('.');
                normalized.Append(digits.AsSpan(checked((int)decimalPosition)));
            }
            else
            {
                normalized.Append("0.");
                normalized.Append('0', checked((int)-decimalPosition));
                normalized.Append(digits);
            }
        }

        return normalized.ToString();
    }
}

/// <summary>Lowercase SHA-256 helpers for deterministic engine artifacts.</summary>
public static class DeterministicHash
{
    public static byte[] ComputeBytes(ReadOnlySpan<byte> canonicalUtf8Json) =>
        SHA256.HashData(canonicalUtf8Json);

    public static string ComputeHex(ReadOnlySpan<byte> canonicalUtf8Json) =>
        Convert.ToHexString(ComputeBytes(canonicalUtf8Json)).ToLowerInvariant();

    public static string ComputeHex(byte[] canonicalUtf8Json)
    {
        ArgumentNullException.ThrowIfNull(canonicalUtf8Json);
        return ComputeHex(canonicalUtf8Json.AsSpan());
    }

    public static string ComputeHex<T>(T value) =>
        ComputeHex(CanonicalJson.SerializeUtf8(value).AsSpan());

    public static string Compute<T>(T value) => ComputeHex(value);

    public static string ForState(GameState state) => ComputeHex(state);

    public static string ForEvents(IReadOnlyList<EngineEvent> events) => ComputeHex(events);

    public static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        return value.All(static character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f');
    }
}
