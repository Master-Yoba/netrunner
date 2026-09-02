using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Raised when a user-facing match request is malformed or illegal.</summary>
public sealed class MatchRequestValidationException : InvalidOperationException
{
    public MatchRequestValidationException(IReadOnlyList<string> problems)
        : base("The match request is invalid:\n" + string.Join("\n", problems))
    {
        Problems = new ReadOnlyCollection<string>(problems.ToArray());
    }

    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// Versioned match composition input. It is deliberately a request contract,
/// not a setup reducer: constructing or parsing one never shuffles, deals,
/// creates card instances, or changes GameState.
/// </summary>
public sealed record MatchRequest
{
    public const int CurrentSchemaVersion = 1;

    [JsonConstructor]
    public MatchRequest(
        int schemaVersion,
        string rulesVersion,
        string cardDataVersion,
        ulong seed,
        DeckRequest corpDeck,
        DeckRequest runnerDeck,
        string? presetGroup = null)
    {
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(cardDataVersion);
        ArgumentNullException.ThrowIfNull(corpDeck);
        ArgumentNullException.ThrowIfNull(runnerDeck);
        SchemaVersion = schemaVersion;
        RulesVersion = rulesVersion;
        CardDataVersion = cardDataVersion;
        Seed = seed;
        CorpDeck = corpDeck;
        RunnerDeck = runnerDeck;
        PresetGroup = presetGroup;
    }

    public int SchemaVersion { get; }

    public string RulesVersion { get; }

    /// <summary>Authenticated metadata commit/hash used by both deck requests.</summary>
    public string CardDataVersion { get; }

    public ulong Seed { get; }

    public DeckRequest CorpDeck { get; }

    public DeckRequest RunnerDeck { get; }

    /// <summary>
    /// Optional Match-owned grouping label (for example <c>system-gateway</c>).
    /// It is composition metadata and has no setup semantics.
    /// </summary>
    public string? PresetGroup { get; }
}

/// <summary>
/// Match-layer composition for user-facing decks and official presets. Cards
/// validates metadata legality; Match selects sides/presets and blocks
/// reduced-deck profiles and incomplete official coverage from user setup.
/// </summary>
public static class MatchRequestComposer
{
    public static MatchRequest ComposeUserFacing(
        DeckRequest corpDeck,
        DeckRequest runnerDeck,
        CardDataSet data,
        OfficialPresetCatalog? officialPresets = null,
        string? presetGroup = null,
        ulong seed = 0,
        IBehaviorCatalog? behaviorCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(corpDeck);
        ArgumentNullException.ThrowIfNull(runnerDeck);
        ArgumentNullException.ThrowIfNull(data);
        OfficialPresetCatalog presets = officialPresets ?? OfficialPresetCatalog.Embedded();
        var problems = new List<string>();
        if (behaviorCatalog is null)
        {
            problems.Add(
                "behaviorCatalog: a compiled behavior catalog is required for user-facing match composition.");
        }

        AddPrefixed(problems, presets.CollectProblems(), "presets");

        // A custom standard deck must be usable with a partial/synthetic
        // metadata snapshot. Cross-check official codes against authenticated
        // metadata only when this request actually names an official preset;
        // tutorial identities are checked by DeckValidator's exact-preset
        // comparison and their selected request is still data-pinned by the
        // Match contract.
        if (corpDeck.PresetId is not null || runnerDeck.PresetId is not null)
        {
            AddPrefixed(problems, presets.CollectProblems(data), "presets");
        }

        RejectReducedProfile(corpDeck, "corpDeck", problems);
        RejectReducedProfile(runnerDeck, "runnerDeck", problems);
        DeckRequest pinnedCorpDeck = PinCardDataVersion(corpDeck, data.Manifest.CommitSha);
        DeckRequest pinnedRunnerDeck = PinCardDataVersion(runnerDeck, data.Manifest.CommitSha);
        DeckValidationResult corp = ValidateDeck(pinnedCorpDeck, data, presets, behaviorCatalog);
        DeckValidationResult runner = ValidateDeck(pinnedRunnerDeck, data, presets, behaviorCatalog);
        AddDeckProblems(problems, "corpDeck", corp);
        AddDeckProblems(problems, "runnerDeck", runner);

        if (problems.Count > 0)
        {
            throw new MatchRequestValidationException(problems);
        }

        return new MatchRequest(
            MatchRequest.CurrentSchemaVersion,
            EngineCompatibility.RulesVersion,
            data.Manifest.CommitSha,
            seed,
            pinnedCorpDeck,
            pinnedRunnerDeck,
            presetGroup);
    }

    /// <summary>
    /// Selects the official basic or expanded preset for each side. Presets
    /// remain parsed/validated while <see cref="OfficialPreset.Playable"/> is
    /// false; this gate is the user-facing coverage boundary for DATA-401A.
    /// </summary>
    public static MatchRequest ComposeOfficial(
        string corpPresetId,
        string runnerPresetId,
        CardDataSet data,
        ulong seed = 0,
        OfficialPresetCatalog? officialPresets = null,
        IBehaviorCatalog? behaviorCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(corpPresetId);
        ArgumentNullException.ThrowIfNull(runnerPresetId);
        ArgumentNullException.ThrowIfNull(data);
        OfficialPresetCatalog presets = officialPresets ?? OfficialPresetCatalog.Embedded();
        var problems = new List<string>();
        if (behaviorCatalog is null)
        {
            problems.Add(
                "behaviorCatalog: a compiled behavior catalog is required for user-facing match composition.");
        }

        AddPrefixed(problems, presets.CollectProblems(data), "presets");

        OfficialPreset? corp = SelectPreset(presets, corpPresetId, PlayerSide.Corp, problems, "corpPresetId");
        OfficialPreset? runner = SelectPreset(presets, runnerPresetId, PlayerSide.Runner, problems, "runnerPresetId");

        if (corp is not null && !corp.Playable)
        {
            problems.Add(
                $"corpPresetId: official preset '{corp.PresetId}' is parsed and validated but not user-playable until all cards reach complete behavior coverage.");
        }

        if (runner is not null && !runner.Playable)
        {
            problems.Add(
                $"runnerPresetId: official preset '{runner.PresetId}' is parsed and validated but not user-playable until all cards reach complete behavior coverage.");
        }

        if (problems.Count > 0)
        {
            throw new MatchRequestValidationException(problems);
        }

        DeckRequest corpDeck = corp!.ToDeckRequest(
            DeckValidationProfile.UserFacing,
            data.Manifest.CommitSha);
        DeckRequest runnerDeck = runner!.ToDeckRequest(
            DeckValidationProfile.UserFacing,
            data.Manifest.CommitSha);
        return ComposeUserFacing(
            corpDeck,
            runnerDeck,
            data,
            presets,
            presetGroup: "system-gateway",
            seed: seed,
            behaviorCatalog: behaviorCatalog);
    }

    /// <summary>
    /// Validates a parsed user-facing request without running setup. This
    /// method is intentionally separate from the Core setup transition seam.
    /// </summary>
    public static void ValidateUserFacing(
        MatchRequest request,
        CardDataSet data,
        OfficialPresetCatalog? officialPresets = null,
        IBehaviorCatalog? behaviorCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(data);
        OfficialPresetCatalog presets = officialPresets ?? OfficialPresetCatalog.Embedded();
        var problems = new List<string>();
        if (behaviorCatalog is null)
        {
            problems.Add(
                "behaviorCatalog: a compiled behavior catalog is required for user-facing match validation.");
        }

        AddPrefixed(problems, presets.CollectProblems(), "presets");
        if (request.CorpDeck.PresetId is not null || request.RunnerDeck.PresetId is not null)
        {
            AddPrefixed(problems, presets.CollectProblems(data), "presets");
        }
        if (request.SchemaVersion != MatchRequest.CurrentSchemaVersion)
        {
            problems.Add(
                $"schemaVersion: expected {MatchRequest.CurrentSchemaVersion}, found {request.SchemaVersion}.");
        }

        if (!string.Equals(request.RulesVersion, EngineCompatibility.RulesVersion, StringComparison.Ordinal))
        {
            problems.Add(
                $"rulesVersion: '{request.RulesVersion}' is not supported; expected '{EngineCompatibility.RulesVersion}'.");
        }

        if (!string.Equals(request.CardDataVersion, data.Manifest.CommitSha, StringComparison.Ordinal))
        {
            problems.Add(
                $"cardDataVersion: '{request.CardDataVersion}' does not match authenticated metadata version '{data.Manifest.CommitSha}'.");
        }

        RejectReducedProfile(request.CorpDeck, "corpDeck", problems);
        RejectReducedProfile(request.RunnerDeck, "runnerDeck", problems);
        RequireCardDataVersion(request.CorpDeck, request.CardDataVersion, "corpDeck", problems);
        RequireCardDataVersion(request.RunnerDeck, request.CardDataVersion, "runnerDeck", problems);
        DeckValidationResult corp = ValidateDeck(request.CorpDeck, data, presets, behaviorCatalog);
        DeckValidationResult runner = ValidateDeck(request.RunnerDeck, data, presets, behaviorCatalog);
        AddDeckProblems(problems, "corpDeck", corp);
        AddDeckProblems(problems, "runnerDeck", runner);

        if (problems.Count > 0)
        {
            throw new MatchRequestValidationException(problems);
        }
    }

    private static OfficialPreset? SelectPreset(
        OfficialPresetCatalog presets,
        string presetId,
        PlayerSide expectedSide,
        List<string> problems,
        string path)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            problems.Add($"{path}: must be nonblank.");
            return null;
        }

        if (!presets.TryGet(presetId, out OfficialPreset preset))
        {
            problems.Add($"{path}: official preset '{presetId}' was not found.");
            return null;
        }

        if (preset.Side != expectedSide)
        {
            problems.Add(
                $"{path}: preset '{presetId}' belongs to side '{preset.Side}', expected '{expectedSide}'.");
            return null;
        }

        return preset;
    }

    private static void RejectReducedProfile(
        DeckRequest request,
        string path,
        List<string> problems)
    {
        if (request.ValidationProfile == DeckValidationProfile.DeveloperReduced)
        {
            problems.Add(
                $"{path}.validationProfile: DeveloperReduced is developer-only and is rejected by user-facing match composition.");
        }
    }

    private static DeckRequest PinCardDataVersion(DeckRequest request, string version) =>
        request.CardDataVersion is null
            ? new DeckRequest(
                request.SchemaVersion,
                request.Side,
                request.IdentityCode,
                request.Cards,
                request.ValidationProfile,
                request.PresetId,
                version)
            : request;

    private static void RequireCardDataVersion(
        DeckRequest request,
        string expectedVersion,
        string path,
        List<string> problems)
    {
        if (request.CardDataVersion is null)
        {
            problems.Add($"{path}.cardDataVersion: user-facing requests must carry the authenticated metadata version.");
        }
        else if (!string.Equals(request.CardDataVersion, expectedVersion, StringComparison.Ordinal))
        {
            problems.Add(
                $"{path}.cardDataVersion: '{request.CardDataVersion}' does not match match cardDataVersion '{expectedVersion}'.");
        }
    }

    private static void AddDeckProblems(
        List<string> problems,
        string prefix,
        DeckValidationResult result)
    {
        foreach (DeckValidationDiagnostic diagnostic in result.Diagnostics)
        {
            problems.Add($"{prefix}.{diagnostic}");
        }
    }

    private static DeckValidationResult ValidateDeck(
        DeckRequest deck,
        CardDataSet data,
        OfficialPresetCatalog presets,
        IBehaviorCatalog? behaviorCatalog) =>
        behaviorCatalog is null
            ? DeckValidator.Validate(deck, data, presets)
            : DeckValidator.ValidateWithBehaviorCatalog(deck, data, behaviorCatalog, presets);

    private static void AddPrefixed(
        List<string> problems,
        IReadOnlyList<string> source,
        string prefix)
    {
        foreach (string problem in source)
        {
            problems.Add($"{prefix}.{problem}");
        }
    }
}

/// <summary>Strict JSON parser/writer for versioned match requests.</summary>
public static class MatchRequestJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly IReadOnlySet<string> RootProperties = new HashSet<string>(
        new[] { "schemaVersion", "rulesVersion", "cardDataVersion", "seed", "corpDeck", "runnerDeck", "presetGroup" },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> DeckProperties = new HashSet<string>(
        new[] { "schemaVersion", "side", "identityCode", "cards", "validationProfile", "presetId", "cardDataVersion" },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> CardProperties = new HashSet<string>(
        new[] { "code", "quantity" },
        StringComparer.Ordinal);

    public static MatchRequest Parse(string json)
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
        catch (MatchRequestValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Invalid($"match request: malformed JSON ({exception.Message})");
        }
    }

    public static MatchRequest Parse(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return Parse(StrictUtf8.GetString(utf8Json));
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid($"match request: invalid UTF-8 ({exception.Message})");
        }
    }

    public static string Serialize(MatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            Write(writer, request);
        }

        stream.WriteByte((byte)'\n');
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static MatchRequest Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("match request: expected an object.");
        }

        EnsureObjectShape(root, RootProperties, "match request");
        DeckRequest corp = ParseDeck(RequiredProperty(root, "corpDeck", "match request"), "corpDeck");
        DeckRequest runner = ParseDeck(RequiredProperty(root, "runnerDeck", "match request"), "runnerDeck");
        string? presetGroup = OptionalString(root, "presetGroup", "match request");
        return new MatchRequest(
            RequiredInt(root, "schemaVersion", "match request"),
            RequiredString(root, "rulesVersion", "match request"),
            RequiredString(root, "cardDataVersion", "match request"),
            RequiredUInt64(root, "seed", "match request"),
            corp,
            runner,
            presetGroup);
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

    private static void Write(Utf8JsonWriter writer, MatchRequest request)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", request.SchemaVersion);
        writer.WriteString("rulesVersion", request.RulesVersion);
        writer.WriteString("cardDataVersion", request.CardDataVersion);
        writer.WriteNumber("seed", request.Seed);
        writer.WritePropertyName("corpDeck");
        WriteDeck(writer, request.CorpDeck);
        writer.WritePropertyName("runnerDeck");
        WriteDeck(writer, request.RunnerDeck);
        if (request.PresetGroup is not null)
        {
            writer.WriteString("presetGroup", request.PresetGroup);
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
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{path}.{name}: expected a string or null.");
        }

        return value.GetString();
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

    private static MatchRequestValidationException Invalid(string message) =>
        new([message]);
}
