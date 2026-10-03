using System.Text;
using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The faster canonicalizer (no parse copy, array sort, integer fast path, in-buffer hashing) must write exactly the bytes
/// the original wrote: every saved hash, replay pin and benchmark manifest depends on them.
/// </summary>
public sealed class CanonicalJsonEquivalenceTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("-0.0")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("1E+2")]
    [InlineData("1.50")]
    [InlineData("-12.5e-3")]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    [InlineData("9223372036854775808")]
    [InlineData("123456789012345678901234567890")]
    [InlineData("0.000001")]
    [InlineData("{\"b\":1,\"a\":[3,2,{\"z\":\"\\u00e9\\n\\\"q\",\"y\":null}],\"\\u0041\":true,\"c\":false}")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"<tag>&'\"")]
    public void HandWrittenDocumentsCanonicalizeToTheSameBytes(string json)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        Assert.Equal(CanonicalJsonReference.Canonicalize(utf8), CanonicalJson.Canonicalize(utf8));
    }

    [Fact]
    public void DuplicatePropertiesAreStillRejected()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("{\"a\":1,\"b\":2,\"a\":3}");
        Assert.Throws<JsonException>(() => CanonicalJsonReference.Canonicalize(utf8));
        Assert.Throws<JsonException>(() => CanonicalJson.Canonicalize(utf8));
    }

    [Theory]
    [InlineData("developer", 7UL)]
    [InlineData("nsg-core-gimbatul~nsg-core-sabbatical", 11UL)]
    public void EveryStateAndEventBatchOfAGameCanonicalizesToTheSameBytes(string contentId, ulong seed)
    {
        if (contentId != "developer" && !PlayFormats.IsAvailable(CardFormatIds.NsgCoreSets)) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(contentId, seed);
        GameState state = GameEngine.CreateMatch(definition.Request.Seed, definition.Request.CorpDeck, definition.Request.RunnerDeck,
            definition.Request.CardDataVersion, definition.Behavior, definition.Runtime).State;
        var random = new Random(unchecked((int)seed));
        for (int step = 0; step < 400 && state.PendingDecision is { } decision; step++)
        {
            byte[] stateJson = EngineJson.SerializeUtf8(state);
            Assert.Equal(CanonicalJsonReference.Canonicalize(stateJson), CanonicalJson.Canonicalize(stateJson));
            Assert.Equal(DeterministicHash.ComputeHex(CanonicalJsonReference.Canonicalize(stateJson)), DeterministicHash.ForState(state));
            LegalAction action = decision.LegalActions[random.Next(decision.LegalActions.Count)];
            ApplyResult applied = GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
                definition.Behavior, definition.Runtime);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            byte[] eventsJson = EngineJson.SerializeUtf8(applied.Events);
            Assert.Equal(CanonicalJsonReference.Canonicalize(eventsJson), CanonicalJson.Canonicalize(eventsJson));
            state = applied.State;
        }
    }
}

internal static class CanonicalJsonReference
{
    private const long MaximumCanonicalNumberLength = 1_000_000;

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

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

