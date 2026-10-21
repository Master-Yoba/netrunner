using System.Text;
using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Strict deterministic JSON parser/writer for SIM-401 documents.</summary>
public static class SimulationFixtureJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly IReadOnlySet<string> RootProperties = new HashSet<string>(
        new[]
        {
            "schemaVersion",
            "fixtureId",
            "rulesVersion",
            "cardDataVersion",
            "behaviorRegistryVersion",
            "behaviorCatalogFingerprint",
            "seed",
            "actionCap",
            "corpDeck",
            "runnerDeck",
            "policy",
            "expectedOutcome",
            "citations",
            "crossCheckNote",
        },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> DeckProperties = new HashSet<string>(
        new[]
        {
            "schemaVersion",
            "side",
            "identityCode",
            "cards",
            "validationProfile",
            "presetId",
            "cardDataVersion",
        },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> CardProperties = new HashSet<string>(
        new[] { "code", "quantity" },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> PolicyProperties = new HashSet<string>(
        new[] { "corpActionOrder", "runnerActionOrder" },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> OutcomeProperties = new HashSet<string>(
        new[] { "kind", "winner", "reason" },
        StringComparer.Ordinal);

    public static SimulationFixtureDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                StrictUtf8.GetBytes(json),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
            return Parse(document.RootElement);
        }
        catch (SimulationFixtureValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Invalid($"simulation fixture: malformed JSON ({exception.Message})");
        }
    }

    public static SimulationFixtureDocument Parse(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return Parse(StrictUtf8.GetString(utf8Json));
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid($"simulation fixture: invalid UTF-8 ({exception.Message})");
        }
    }

    public static string Serialize(SimulationFixtureDocument fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            Write(writer, fixture);
        }

        stream.WriteByte((byte)'\n');
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static SimulationFixtureDocument Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("simulation fixture: expected an object.");
        }

        EnsureObjectShape(root, RootProperties, "simulation fixture");
        DeckRequest corpDeck = ParseDeck(RequiredProperty(root, "corpDeck", "simulation fixture"), "corpDeck");
        DeckRequest runnerDeck = ParseDeck(RequiredProperty(root, "runnerDeck", "simulation fixture"), "runnerDeck");
        SimulationPolicyOverrides? policy = ParsePolicy(root, "simulation fixture");
        SimulationOutcomeExpectation outcome = ParseOutcome(
            RequiredProperty(root, "expectedOutcome", "simulation fixture"),
            "expectedOutcome");

        return new SimulationFixtureDocument(
            RequiredInt(root, "schemaVersion", "simulation fixture"),
            RequiredString(root, "fixtureId", "simulation fixture"),
            RequiredString(root, "rulesVersion", "simulation fixture"),
            RequiredString(root, "cardDataVersion", "simulation fixture"),
            RequiredString(root, "behaviorRegistryVersion", "simulation fixture"),
            RequiredString(root, "behaviorCatalogFingerprint", "simulation fixture"),
            RequiredUInt64(root, "seed", "simulation fixture"),
            RequiredInt(root, "actionCap", "simulation fixture"),
            corpDeck,
            runnerDeck,
            outcome,
            policy,
            OptionalStringArray(root, "citations", "simulation fixture"),
            OptionalString(root, "crossCheckNote", "simulation fixture"));
    }

    private static DeckRequest ParseDeck(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path}: expected an object.");
        }

        EnsureObjectShape(root, DeckProperties, path);
        JsonElement cardsRoot = RequiredProperty(root, "cards", path);
        if (cardsRoot.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{path}.cards: expected an array.");
        }

        var cards = new List<DeckCardEntry>();
        int index = 0;
        foreach (JsonElement card in cardsRoot.EnumerateArray())
        {
            string cardPath = $"{path}.cards[{index}]";
            if (card.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"{cardPath}: expected an object.");
            }

            EnsureObjectShape(card, CardProperties, cardPath);
            cards.Add(new DeckCardEntry(
                RequiredString(card, "code", cardPath),
                RequiredInt(card, "quantity", cardPath)));
            index++;
        }

        return new DeckRequest(
            RequiredInt(root, "schemaVersion", path),
            ParseSide(RequiredString(root, "side", path), $"{path}.side"),
            RequiredString(root, "identityCode", path),
            cards,
            ParseProfile(RequiredString(root, "validationProfile", path), $"{path}.validationProfile"),
            OptionalString(root, "presetId", path),
            OptionalString(root, "cardDataVersion", path));
    }

    private static SimulationPolicyOverrides? ParsePolicy(JsonElement root, string path)
    {
        if (!root.TryGetProperty("policy", out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path}.policy: expected an object or null.");
        }

        EnsureObjectShape(value, PolicyProperties, $"{path}.policy");
        return new SimulationPolicyOverrides(
            OptionalStringArray(value, "corpActionOrder", $"{path}.policy"),
            OptionalStringArray(value, "runnerActionOrder", $"{path}.policy"));
    }

    private static SimulationOutcomeExpectation ParseOutcome(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path}: expected an object.");
        }

        EnsureObjectShape(root, OutcomeProperties, path);
        string? winnerText = OptionalString(root, "winner", path);
        PlayerSide? winner = winnerText is null ? null : ParseSide(winnerText, $"{path}.winner");
        return new SimulationOutcomeExpectation(
            RequiredString(root, "kind", path),
            winner,
            OptionalString(root, "reason", path));
    }

    private static void Write(Utf8JsonWriter writer, SimulationFixtureDocument fixture)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", fixture.SchemaVersion);
        writer.WriteString("fixtureId", fixture.FixtureId);
        writer.WriteString("rulesVersion", fixture.RulesVersion);
        writer.WriteString("cardDataVersion", fixture.CardDataVersion);
        writer.WriteString("behaviorRegistryVersion", fixture.BehaviorRegistryVersion);
        writer.WriteString("behaviorCatalogFingerprint", fixture.BehaviorCatalogFingerprint);
        writer.WriteNumber("seed", fixture.Seed);
        writer.WriteNumber("actionCap", fixture.ActionCap);
        writer.WritePropertyName("corpDeck");
        WriteDeck(writer, fixture.CorpDeck);
        writer.WritePropertyName("runnerDeck");
        WriteDeck(writer, fixture.RunnerDeck);
        if (fixture.Policy is not null)
        {
            writer.WritePropertyName("policy");
            WritePolicy(writer, fixture.Policy);
        }

        writer.WritePropertyName("expectedOutcome");
        WriteOutcome(writer, fixture.ExpectedOutcome);
        writer.WritePropertyName("citations");
        writer.WriteStartArray();
        foreach (string citation in fixture.Citations)
        {
            writer.WriteStringValue(citation);
        }

        writer.WriteEndArray();
        if (fixture.CrossCheckNote is not null)
        {
            writer.WriteString("crossCheckNote", fixture.CrossCheckNote);
        }

        writer.WriteEndObject();
    }

    private static void WriteDeck(Utf8JsonWriter writer, DeckRequest deck)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", deck.SchemaVersion);
        writer.WriteString("side", SideCode(deck.Side));
        writer.WriteString("identityCode", deck.IdentityCode);
        writer.WritePropertyName("cards");
        writer.WriteStartArray();
        foreach (DeckCardEntry card in deck.Cards)
        {
            writer.WriteStartObject();
            writer.WriteString("code", card.Code);
            writer.WriteNumber("quantity", card.Quantity);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("validationProfile", ProfileCode(deck.ValidationProfile));
        if (deck.PresetId is not null)
        {
            writer.WriteString("presetId", deck.PresetId);
        }

        if (deck.CardDataVersion is not null)
        {
            writer.WriteString("cardDataVersion", deck.CardDataVersion);
        }

        writer.WriteEndObject();
    }

    private static void WritePolicy(Utf8JsonWriter writer, SimulationPolicyOverrides policy)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("corpActionOrder");
        WriteStringArray(writer, policy.CorpActionOrder);
        writer.WritePropertyName("runnerActionOrder");
        WriteStringArray(writer, policy.RunnerActionOrder);
        writer.WriteEndObject();
    }

    private static void WriteOutcome(Utf8JsonWriter writer, SimulationOutcomeExpectation outcome)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", outcome.Kind);
        if (outcome.Winner is { } winner)
        {
            writer.WriteString("winner", SideCode(winner));
        }

        if (outcome.Reason is not null)
        {
            writer.WriteString("reason", outcome.Reason);
        }

        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, IReadOnlyList<string> values)
    {
        writer.WriteStartArray();
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void EnsureObjectShape(JsonElement element, IReadOnlySet<string> allowed, string path)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw Invalid($"{path}.{property.Name}: unknown field.");
            }

            if (!seen.Add(property.Name))
            {
                throw Invalid($"{path}.{property.Name}: duplicate field.");
            }
        }
    }

    private static JsonElement RequiredProperty(JsonElement element, string name, string path) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value
            : throw Invalid($"{path}.{name}: required field is missing.");

    private static string RequiredString(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.String || value.GetString() is null)
        {
            throw Invalid($"{path}.{name}: expected a string.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{path}.{name}: expected a string or null.");
        }

        return value.GetString();
    }

    private static List<string> OptionalStringArray(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{path}.{name}: expected an array of strings or null.");
        }

        var result = new List<string>();
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is null)
            {
                throw Invalid($"{path}.{name}[{index}]: expected a string.");
            }

            result.Add(item.GetString()!);
            index++;
        }

        return result;
    }

    private static int RequiredInt(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw Invalid($"{path}.{name}: expected an integer.");
        }

        return result;
    }

    private static ulong RequiredUInt64(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong result))
        {
            throw Invalid($"{path}.{name}: expected an unsigned integer.");
        }

        return result;
    }

    private static PlayerSide ParseSide(string value, string path) => value switch
    {
        "corp" => PlayerSide.Corp,
        "runner" => PlayerSide.Runner,
        _ => throw Invalid($"{path}: '{value}' must be 'corp' or 'runner'."),
    };

    private static DeckValidationProfile ParseProfile(string value, string path) => value switch
    {
        "userFacing" => DeckValidationProfile.UserFacing,
        "developerReduced" => DeckValidationProfile.DeveloperReduced,
        _ => throw Invalid($"{path}: '{value}' must be 'userFacing' or 'developerReduced'."),
    };

    private static string SideCode(PlayerSide side) => side switch
    {
        PlayerSide.Corp => "corp",
        PlayerSide.Runner => "runner",
        _ => side.ToString().ToLowerInvariant(),
    };

    private static string ProfileCode(DeckValidationProfile profile) =>
        profile == DeckValidationProfile.DeveloperReduced ? "developerReduced" : "userFacing";

    private static SimulationFixtureValidationException Invalid(string message)
    {
        // Parser callers format errors as "<exact path>: <diagnostic>". Keep
        // the path separate so consumers can select a field without parsing
        // a human-readable exception message.
        int separator = message.IndexOf(": ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return new([new SimulationFixtureDiagnostic("parse", "simulation fixture", message)]);
        }

        return new([new SimulationFixtureDiagnostic(
            "parse",
            message[..separator],
            message[(separator + 2)..])]);
    }
}

/// <summary>Explicit-path loader for checked-in SIM-401 fixtures.</summary>
public static class SimulationFixtureProvider
{
    public static SimulationFixtureDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return SimulationFixtureJson.Parse(File.ReadAllText(path));
    }

    public static SimulationFixtureCatalog LoadDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string[] paths = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        return new SimulationFixtureCatalog(paths.Select(Load));
    }
}
