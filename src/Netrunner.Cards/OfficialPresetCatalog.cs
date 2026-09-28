using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>Published and authenticated provenance for the official presets.</summary>
public sealed record OfficialPresetProvenance(
    int SchemaVersion,
    string MetadataCommitSha,
    string MetadataSourceUrl,
    string PublishedSourceUrl,
    string RulesSourceUrl,
    string RetrievedDate,
    string ProvenanceNote);

/// <summary>
/// One official System Gateway teaching deck.  The record contains only
/// stable card codes and quantities; card names, text, and artwork remain in
/// the authenticated metadata pipeline.
/// </summary>
public sealed class OfficialPreset
{
    internal OfficialPreset(
        string presetId,
        PlayerSide side,
        string identityCode,
        string kind,
        bool playable,
        IReadOnlyList<DeckCardEntry> cards,
        string? name = null,
        string? summary = null,
        string? sourceUrl = null,
        IReadOnlyList<string>? archetypes = null)
    {
        PresetId = presetId;
        Side = side;
        IdentityCode = identityCode;
        Kind = kind;
        Playable = playable;
        Cards = new ReadOnlyCollection<DeckCardEntry>(cards.ToArray());
        Name = name;
        Summary = summary;
        SourceUrl = sourceUrl;
        Archetypes = new ReadOnlyCollection<string>((archetypes ?? []).ToArray());
    }

    /// <summary>
    /// The deck's declared archetypes (AI plan A8), primary first: how the bot
    /// playing this deck plans to win. Empty when the bot should infer them.
    /// </summary>
    public IReadOnlyList<string> Archetypes { get; }

    /// <summary>The archetype keys a Runner deck may declare (filled in with the Runner archetypes).</summary>
    public static IReadOnlySet<string> RunnerArchetypeKeys { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The archetype keys a Corp deck may declare.</summary>
    public static IReadOnlySet<string> CorpArchetypeKeys { get; } = new HashSet<string>(
        ["glacier", "rush", "fast-advance", "shell-game", "kill", "tag-punish", "horizontal"], StringComparer.Ordinal);

    /// <summary>The published deck name (Gateway sample decks); null for the teaching decks.</summary>
    public string? Name { get; }

    /// <summary>A one-line description of how the deck plays (Gateway sample decks).</summary>
    public string? Summary { get; }

    /// <summary>Where the list was published (Gateway sample decks).</summary>
    public string? SourceUrl { get; }

    public string PresetId { get; }

    /// <summary>Alias retained for callers that use a generic preset ID name.</summary>
    public string Id => PresetId;

    public PlayerSide Side { get; }

    public string IdentityCode { get; }

    /// <summary><c>starter</c> or <c>expanded</c> (the teaching decks), or <c>gateway</c> (the sample decks).</summary>
    public string Kind { get; }

    /// <summary>
    /// Indicates whether the final behavior gate has enabled this preset for
    /// user-facing composition. The parser still accepts a false value so a
    /// staged DATA-401A catalog can be validated before its cards are ready.
    /// </summary>
    public bool Playable { get; }

    public IReadOnlyList<DeckCardEntry> Cards { get; }

    public int CardCount
    {
        get
        {
            int total = 0;
            foreach (DeckCardEntry card in Cards)
            {
                try
                {
                    total = checked(total + card.Quantity);
                }
                catch (OverflowException)
                {
                    return int.MaxValue;
                }
            }

            return total;
        }
    }

    public DeckRequest ToDeckRequest(
        DeckValidationProfile validationProfile = DeckValidationProfile.UserFacing,
        string? cardDataVersion = null) =>
        new(
            DeckRequest.CurrentSchemaVersion,
            Side,
            IdentityCode,
            Cards,
            validationProfile,
            PresetId,
            cardDataVersion);
}

/// <summary>Raised when the checked-in preset document is malformed or stale.</summary>
public sealed class PresetDataValidationException : InvalidOperationException
{
    public PresetDataValidationException(IReadOnlyList<string> problems)
        : base("The official preset document is invalid:\n" + string.Join("\n", problems))
    {
        Problems = new ReadOnlyCollection<string>(problems.ToArray());
    }

    public IReadOnlyList<string> Problems { get; }
}

/// <summary>
/// Immutable parser/catalog for DATA-401A's code/quantity-only teaching
/// presets.  It is a Cards concern because it validates codes, sides, card
/// types, quantities, and authenticated metadata; Match composes selections
/// into user-facing match requests.
/// </summary>
public sealed class OfficialPresetCatalog
{
    public const int CurrentSchemaVersion = 1;

    private const string ResourceName = "Netrunner.Cards.Data.m4.presets.json";
    private static readonly Lazy<OfficialPresetCatalog> EmbeddedCatalog = new(
        LoadEmbeddedCore,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly IReadOnlySet<string> RootProperties = new HashSet<string>(
        new[]
        {
            "schemaVersion",
            "metadataCommitSha",
            "metadataSourceUrl",
            "publishedSourceUrl",
            "rulesSourceUrl",
            "retrievedDate",
            "provenanceNote",
            "presets",
        },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> PresetProperties = new HashSet<string>(
        new[]
        {
            "presetId",
            "side",
            "identityCode",
            "kind",
            "playable",
            "cards",
        },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> CardProperties = new HashSet<string>(
        new[] { "code", "quantity" },
        StringComparer.Ordinal);

    private static readonly Dictionary<string, (PlayerSide Side, string IdentityCode, string Kind, int Count)> ExpectedPresets =
        new Dictionary<string, (PlayerSide, string, string, int)>(StringComparer.Ordinal)
        {
            ["system-gateway-basic-runner"] = (PlayerSide.Runner, "30076", "starter", 30),
            ["system-gateway-expanded-runner"] = (PlayerSide.Runner, "30076", "expanded", 40),
            ["system-gateway-basic-corp"] = (PlayerSide.Corp, "30077", "starter", 34),
            ["system-gateway-expanded-corp"] = (PlayerSide.Corp, "30077", "expanded", 44),
        };

    public const string GatewayKind = "gateway";

    private OfficialPresetCatalog(
        OfficialPresetProvenance provenance,
        IReadOnlyList<OfficialPreset> presets,
        IReadOnlyList<OfficialPreset>? gateway = null)
    {
        Provenance = provenance;
        Teaching = new ReadOnlyCollection<OfficialPreset>(presets.ToArray());
        Gateway = new ReadOnlyCollection<OfficialPreset>((gateway ?? []).ToArray());
        Presets = new ReadOnlyCollection<OfficialPreset>(Teaching.Concat(Gateway).ToArray());
    }

    /// <summary>The System Gateway teaching decks from presets.json (starter and expanded).</summary>
    public IReadOnlyList<OfficialPreset> Teaching { get; }

    /// <summary>The System Gateway-only sample decks (Wave 4 C1).</summary>
    public IReadOnlyList<OfficialPreset> Gateway { get; }

    public OfficialPresetProvenance Provenance { get; }

    public IReadOnlyList<OfficialPreset> Presets { get; }

    /// <summary>Alias for callers that use catalog terminology.</summary>
    public IReadOnlyList<OfficialPreset> All => Presets;

    public static OfficialPresetCatalog Embedded() => EmbeddedCatalog.Value;

    /// <summary>Explicitly named alias for the embedded DATA-401A payload.</summary>
    public static OfficialPresetCatalog LoadEmbedded() => Embedded();

    public bool TryGet(string presetId, out OfficialPreset preset)
    {
        ArgumentNullException.ThrowIfNull(presetId);
        preset = Presets.FirstOrDefault(value =>
            string.Equals(value.PresetId, presetId, StringComparison.Ordinal))!;
        return preset is not null;
    }

    public bool TryGetById(string presetId, out OfficialPreset preset) => TryGet(presetId, out preset);

    public OfficialPreset Get(string presetId) =>
        TryGet(presetId, out OfficialPreset preset)
            ? preset
            : throw new KeyNotFoundException($"Official preset '{presetId}' was not found.");

    public IReadOnlyList<OfficialPreset> For(PlayerSide side) =>
        new ReadOnlyCollection<OfficialPreset>(Presets.Where(value => value.Side == side).ToArray());

    /// <summary>
    /// Validates the structure and official identities/counts without needing
    /// card metadata. This is used by clean CI, where official source records
    /// are intentionally absent.
    /// </summary>
    public IReadOnlyList<string> CollectProblems() => CollectProblemsInternal(data: null);

    /// <summary>
    /// Validates both the preset structure and every code/side/type/quantity
    /// against an authenticated local metadata snapshot.
    /// </summary>
    public IReadOnlyList<string> CollectProblems(CardDataSet data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return CollectProblemsInternal(data);
    }

    public void Validate() => ThrowIfInvalid(CollectProblems());

    public void Validate(CardDataSet data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ThrowIfInvalid(CollectProblems(data));
    }

    private ReadOnlyCollection<string> CollectProblemsInternal(CardDataSet? data)
    {
        var problems = new List<string>();
        AddStructureProblems(problems);
        if (data is not null)
        {
            AddMetadataProblems(problems, data);
            AddGatewayLegalityProblems(problems, data);
        }

        return new ReadOnlyCollection<string>(problems);
    }

    private void AddStructureProblems(List<string> problems)
    {
        if (Provenance.SchemaVersion != CurrentSchemaVersion)
        {
            problems.Add(
                $"schemaVersion: expected {CurrentSchemaVersion}, found {Provenance.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(Provenance.MetadataCommitSha))
        {
            problems.Add("metadataCommitSha: must be nonblank.");
        }
        else if (!IsSha256OrCommit(Provenance.MetadataCommitSha))
        {
            problems.Add("metadataCommitSha: must be a lowercase 40-hex commit SHA.");
        }

        RequireNonblank(problems, Provenance.MetadataSourceUrl, "metadataSourceUrl");
        RequireNonblank(problems, Provenance.PublishedSourceUrl, "publishedSourceUrl");
        RequireNonblank(problems, Provenance.RulesSourceUrl, "rulesSourceUrl");
        RequireNonblank(problems, Provenance.RetrievedDate, "retrievedDate");
        if (!DateOnly.TryParseExact(
                Provenance.RetrievedDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _)
            || Provenance.RetrievedDate.Length != 10)
        {
            problems.Add("retrievedDate: must be an ISO date (YYYY-MM-DD).");
        }

        RequireNonblank(problems, Provenance.ProvenanceNote, "provenanceNote");

        var seenPresetIds = new HashSet<string>(StringComparer.Ordinal);
        for (int presetIndex = 0; presetIndex < Teaching.Count; presetIndex++)
        {
            OfficialPreset? preset = Teaching[presetIndex];
            string path = $"presets[{presetIndex}]";
            if (preset is null)
            {
                problems.Add($"{path}: preset entries must not be null.");
                continue;
            }

            if (!IsStableIdentifier(preset.PresetId))
            {
                problems.Add($"{path}.presetId: must be a nonblank stable identifier.");
            }
            else if (!seenPresetIds.Add(preset.PresetId))
            {
                problems.Add($"{path}.presetId: duplicate preset ID '{preset.PresetId}'.");
            }

            if (!Enum.IsDefined(preset.Side))
            {
                problems.Add($"{path}.side: '{preset.Side}' is not a valid player side.");
            }

            if (string.IsNullOrWhiteSpace(preset.IdentityCode))
            {
                problems.Add($"{path}.identityCode: must be nonblank.");
            }

            if (preset.Kind is not ("starter" or "expanded"))
            {
                problems.Add($"{path}.kind: '{preset.Kind}' is not supported; expected 'starter' or 'expanded'.");
            }

            if (!ExpectedPresets.TryGetValue(preset.PresetId, out var expected))
            {
                problems.Add($"{path}.presetId: unknown official preset '{preset.PresetId}'.");
            }
            else
            {
                if (preset.Side != expected.Side)
                {
                    problems.Add(
                        $"{path}.side: '{preset.Side}' does not match official side '{expected.Side}'.");
                }

                if (!string.Equals(preset.IdentityCode, expected.IdentityCode, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{path}.identityCode: '{preset.IdentityCode}' does not match official identity '{expected.IdentityCode}'.");
                }

                if (!string.Equals(preset.Kind, expected.Kind, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{path}.kind: '{preset.Kind}' does not match official kind '{expected.Kind}'.");
                }

                if (preset.CardCount != expected.Count)
                {
                    problems.Add(
                        $"{path}.cards: contains {preset.CardCount} cards; official count is {expected.Count}.");
                }
            }

            var seenCodes = new HashSet<string>(StringComparer.Ordinal);
            for (int cardIndex = 0; cardIndex < preset.Cards.Count; cardIndex++)
            {
                DeckCardEntry? card = preset.Cards[cardIndex];
                string cardPath = $"{path}.cards[{cardIndex}]";
                if (card is null)
                {
                    problems.Add($"{cardPath}: card entries must not be null.");
                    continue;
                }

                if (!IsStableCardCode(card.Code))
                {
                    problems.Add($"{cardPath}.code: '{card.Code}' must be exactly five digits.");
                }
                else if (!seenCodes.Add(card.Code))
                {
                    problems.Add($"{cardPath}.code: card code '{card.Code}' appears more than once.");
                }

                if (card.Quantity <= 0)
                {
                    problems.Add($"{cardPath}.quantity: must be positive, found {card.Quantity}.");
                }
            }
        }

        if (Teaching.Count != ExpectedPresets.Count)
        {
            problems.Add(
                $"presets: expected {ExpectedPresets.Count} official presets, found {Teaching.Count}.");
        }

        AddExpandedSubsetProblems(problems);
        AddGatewayStructureProblems(problems);
    }

    private void AddExpandedSubsetProblems(List<string> problems)
    {
        foreach ((string side, string starterId, string expandedId) in new[]
        {
            ("runner", "system-gateway-basic-runner", "system-gateway-expanded-runner"),
            ("corp", "system-gateway-basic-corp", "system-gateway-expanded-corp"),
        })
        {
            if (!TryGet(starterId, out OfficialPreset starter)
                || !TryGet(expandedId, out OfficialPreset expanded))
            {
                continue;
            }

            var expandedByCode = new Dictionary<string, DeckCardEntry>(StringComparer.Ordinal);
            foreach (DeckCardEntry card in expanded.Cards)
            {
                // AddStructureProblems reports duplicates with their exact
                // paths. Keep the first entry here so malformed input still
                // produces deterministic validation diagnostics instead of a
                // raw Dictionary exception.
                expandedByCode.TryAdd(card.Code, card);
            }
            for (int index = 0; index < starter.Cards.Count; index++)
            {
                DeckCardEntry card = starter.Cards[index];
                if (!expandedByCode.TryGetValue(card.Code, out DeckCardEntry? expandedCard)
                    || expandedCard.Quantity < card.Quantity)
                {
                    problems.Add(
                        $"presets[{IndexOf(expanded)}].cards: {side} expanded preset must include starter quantity for code '{card.Code}'.");
                }
            }
        }
    }

    private void AddMetadataProblems(List<string> problems, CardDataSet data)
    {
        if (!string.Equals(data.Manifest.CommitSha, Provenance.MetadataCommitSha, StringComparison.Ordinal))
        {
            problems.Add(
                $"metadataCommitSha: '{Provenance.MetadataCommitSha}' does not match authenticated catalog commit '{data.Manifest.CommitSha}'.");
        }

        var byCode = data.Cards.ToDictionary(static card => card.Code, StringComparer.Ordinal);
        for (int presetIndex = 0; presetIndex < Presets.Count; presetIndex++)
        {
            OfficialPreset preset = Presets[presetIndex];
            string presetPath = $"presets[{presetIndex}]";
            if (!byCode.TryGetValue(preset.IdentityCode, out NormalizedCardDefinition? identity))
            {
                problems.Add($"{presetPath}.identityCode: code '{preset.IdentityCode}' is missing from authenticated metadata.");
            }
            else
            {
                if (!string.Equals(identity.Side, ToSideCode(preset.Side), StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{presetPath}.identityCode: identity '{preset.IdentityCode}' has side '{identity.Side}', expected '{ToSideCode(preset.Side)}'.");
                }

                if (!string.Equals(identity.Type, "identity", StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{presetPath}.identityCode: code '{preset.IdentityCode}' is type '{identity.Type}', expected 'identity'.");
                }
            }

            for (int cardIndex = 0; cardIndex < preset.Cards.Count; cardIndex++)
            {
                DeckCardEntry card = preset.Cards[cardIndex];
                string path = $"{presetPath}.cards[{cardIndex}].code";
                if (!byCode.TryGetValue(card.Code, out NormalizedCardDefinition? definition))
                {
                    problems.Add($"{path}: code '{card.Code}' is missing from authenticated metadata.");
                    continue;
                }

                if (!string.Equals(definition.Side, ToSideCode(preset.Side), StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{path}: code '{card.Code}' has side '{definition.Side}', expected '{ToSideCode(preset.Side)}'.");
                }

                if (string.Equals(definition.Type, "identity", StringComparison.Ordinal))
                {
                    problems.Add($"{path}: identity cards cannot appear in an official deck preset.");
                }

                if (card.Quantity > definition.Quantity)
                {
                    problems.Add(
                        $"{path.Replace(".code", ".quantity", StringComparison.Ordinal)}: quantity {card.Quantity} exceeds metadata quantity {definition.Quantity}.");
                }

                if (card.Quantity > definition.DeckLimit)
                {
                    problems.Add(
                        $"{path.Replace(".code", ".quantity", StringComparison.Ordinal)}: quantity {card.Quantity} exceeds printed deck limit {definition.DeckLimit}.");
                }
            }
        }
    }

    private int IndexOf(OfficialPreset preset)
    {
        for (int index = 0; index < Presets.Count; index++)
        {
            if (ReferenceEquals(Presets[index], preset))
            {
                return index;
            }
        }

        return -1;
    }

    private static string ToSideCode(PlayerSide side) => side == PlayerSide.Corp ? "corp" : "runner";

    private static void ThrowIfInvalid(IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new PresetDataValidationException(problems);
        }
    }

    private void AddGatewayStructureProblems(List<string> problems)
    {
        var seen = new HashSet<string>(Teaching.Select(preset => preset.PresetId), StringComparer.Ordinal);
        for (int index = 0; index < Gateway.Count; index++)
        {
            OfficialPreset preset = Gateway[index];
            string path = $"gateway.decks[{index}]";
            if (!IsStableIdentifier(preset.PresetId) || !seen.Add(preset.PresetId))
                problems.Add($"{path}.presetId: '{preset.PresetId}' must be a unique stable identifier.");
            if (preset.Kind != GatewayKind) problems.Add($"{path}.kind: expected '{GatewayKind}'.");
            if (string.IsNullOrWhiteSpace(preset.Name) || string.IsNullOrWhiteSpace(preset.Summary) || string.IsNullOrWhiteSpace(preset.SourceUrl))
                problems.Add($"{path}: a sample deck needs a name, a summary and a source URL.");
            if (preset.CardCount != (preset.Side == PlayerSide.Corp ? 44 : 40))
                problems.Add($"{path}.cards: contains {preset.CardCount} cards; a Gateway sample deck has {(preset.Side == PlayerSide.Corp ? 44 : 40)}.");
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (DeckCardEntry card in preset.Cards)
            {
                if (!IsStableCardCode(card.Code) || !codes.Add(card.Code)) problems.Add($"{path}.cards: code '{card.Code}' is invalid or repeated.");
                if (card.Quantity <= 0) problems.Add($"{path}.cards: code '{card.Code}' has quantity {card.Quantity}.");
            }
        }
    }

    /// <summary>
    /// Deck-building rules for the sample decks, against authenticated metadata:
    /// the identity's minimum deck size and influence limit, no out-of-faction
    /// agendas, and a Corp deck's agenda points (2 per 5 cards plus 2, or one more).
    /// </summary>
    private void AddGatewayLegalityProblems(List<string> problems, CardDataSet data)
    {
        var byCode = data.Cards.ToDictionary(static card => card.Code, StringComparer.Ordinal);
        for (int index = 0; index < Gateway.Count; index++)
        {
            OfficialPreset preset = Gateway[index];
            string path = $"gateway.decks[{index}]";
            if (!byCode.TryGetValue(preset.IdentityCode, out NormalizedCardDefinition? identity)) continue;
            int size = preset.CardCount;
            if (identity.MinimumDeckSize is { } minimum && size < minimum)
                problems.Add($"{path}: {size} cards is below the identity's minimum of {minimum}.");
            int influence = 0, points = 0;
            foreach (DeckCardEntry card in preset.Cards)
            {
                if (!byCode.TryGetValue(card.Code, out NormalizedCardDefinition? definition)) continue;
                bool outOfFaction = definition.Faction != identity.Faction && !definition.Faction.StartsWith("neutral", StringComparison.Ordinal);
                if (outOfFaction) influence += (definition.InfluenceCost ?? 0) * card.Quantity;
                if (outOfFaction && definition.Type == "agenda") problems.Add($"{path}: agenda '{card.Code}' is out of faction.");
                points += (definition.AgendaPoints ?? 0) * card.Quantity;
            }
            if (identity.InfluenceLimit is { } limit && influence > limit)
                problems.Add($"{path}: {influence} influence exceeds the identity's limit of {limit}.");
            if (preset.Side == PlayerSide.Corp)
            {
                int required = size / 5 * 2 + 2;
                if (points != required && points != required + 1)
                    problems.Add($"{path}: {points} agenda points; a {size}-card deck needs {required} or {required + 1}.");
            }
        }
    }

    private const string GatewayResourceName = "Netrunner.Cards.Data.decks.gateway-sample-decks.json";

    private static readonly IReadOnlySet<string> GatewayRootProperties = new HashSet<string>(
        ["schemaVersion", "retrievedDate", "publishedSourceUrl", "metadataCommitSha", "provenanceNote", "decks"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> GatewayDeckProperties = new HashSet<string>(
        ["presetId", "name", "summary", "side", "identityCode", "sourceUrl", "cards", "archetypes"], StringComparer.Ordinal);

    /// <summary>Parses the Gateway sample-deck document (schema 1) into presets of kind <see cref="GatewayKind"/>.</summary>
    public static IReadOnlyList<OfficialPreset> ParseGatewayDecks(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        EnsureObjectShape(root, GatewayRootProperties, "gateway document");
        if (RequiredInt(root, "schemaVersion", "gateway document") != 1) throw Invalid("gateway document.schemaVersion: expected 1.");
        var decks = new List<OfficialPreset>();
        int index = 0;
        foreach (JsonElement element in RequiredProperty(root, "decks", "gateway document").EnumerateArray())
        {
            string path = $"gateway.decks[{index++}]";
            EnsureObjectShape(element, GatewayDeckProperties, path);
            PlayerSide side = RequiredString(element, "side", path) switch
            {
                "corp" => PlayerSide.Corp,
                "runner" => PlayerSide.Runner,
                string other => throw Invalid($"{path}.side: '{other}' must be 'corp' or 'runner'."),
            };
            var cards = new List<DeckCardEntry>();
            foreach (JsonElement card in RequiredProperty(element, "cards", path).EnumerateArray())
            {
                EnsureObjectShape(card, CardProperties, $"{path}.cards");
                cards.Add(new DeckCardEntry(RequiredString(card, "code", path), RequiredInt(card, "quantity", path)));
            }
            var archetypes = new List<string>();
            if (element.TryGetProperty("archetypes", out JsonElement declared))
            {
                IReadOnlySet<string> known = side == PlayerSide.Corp ? OfficialPreset.CorpArchetypeKeys : OfficialPreset.RunnerArchetypeKeys;
                foreach (JsonElement value in declared.EnumerateArray())
                {
                    string key = value.GetString() ?? throw Invalid($"{path}.archetypes: expected strings.");
                    if (!known.Contains(key)) throw Invalid($"{path}.archetypes: '{key}' isn't a {side} archetype.");
                    archetypes.Add(key);
                }
                if (archetypes.Count is 0 or > 2 || archetypes.Distinct().Count() != archetypes.Count)
                    throw Invalid($"{path}.archetypes: expected one or two distinct archetypes.");
            }
            decks.Add(new OfficialPreset(RequiredString(element, "presetId", path), side, RequiredString(element, "identityCode", path),
                GatewayKind, playable: true, cards, RequiredString(element, "name", path), RequiredString(element, "summary", path),
                RequiredString(element, "sourceUrl", path), archetypes));
        }
        return decks;
    }

    private static OfficialPresetCatalog LoadEmbeddedCore()
    {
        Assembly assembly = typeof(OfficialPresetCatalog).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new PresetDataValidationException(
                [$"preset resource: embedded resource '{ResourceName}' was not found."]);
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        OfficialPresetCatalog teaching = Parse(memory.ToArray());
        using Stream? gatewayStream = assembly.GetManifestResourceStream(GatewayResourceName);
        if (gatewayStream is null)
            throw new PresetDataValidationException([$"preset resource: embedded resource '{GatewayResourceName}' was not found."]);
        using var reader = new StreamReader(gatewayStream, Encoding.UTF8);
        var catalog = new OfficialPresetCatalog(teaching.Provenance, teaching.Teaching, ParseGatewayDecks(reader.ReadToEnd()));
        catalog.Validate();
        return catalog;
    }

    public static OfficialPresetCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    public static OfficialPresetCatalog Parse(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                utf8Json.ToArray(),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
            return Parse(document.RootElement);
        }
        catch (PresetDataValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new PresetDataValidationException(
                [$"preset document: malformed JSON ({exception.Message})"]);
        }
    }

    private static OfficialPresetCatalog Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("preset document: expected an object.");
        }

        EnsureObjectShape(root, RootProperties, "preset document");
        OfficialPresetProvenance provenance = new(
            RequiredInt(root, "schemaVersion", "preset document"),
            RequiredString(root, "metadataCommitSha", "preset document"),
            RequiredString(root, "metadataSourceUrl", "preset document"),
            RequiredString(root, "publishedSourceUrl", "preset document"),
            RequiredString(root, "rulesSourceUrl", "preset document"),
            RequiredString(root, "retrievedDate", "preset document"),
            RequiredString(root, "provenanceNote", "preset document"));

        JsonElement presetsElement = RequiredProperty(root, "presets", "preset document");
        if (presetsElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("preset document.presets: expected an array.");
        }

        var presets = new List<OfficialPreset>();
        int presetIndex = 0;
        foreach (JsonElement element in presetsElement.EnumerateArray())
        {
            string path = $"presets[{presetIndex}]";
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"{path}: expected an object.");
            }

            EnsureObjectShape(element, PresetProperties, path);
            string sideCode = RequiredString(element, "side", path);
            PlayerSide side = sideCode switch
            {
                "corp" => PlayerSide.Corp,
                "runner" => PlayerSide.Runner,
                _ => throw Invalid($"{path}.side: '{sideCode}' must be 'corp' or 'runner'."),
            };
            JsonElement cardsElement = RequiredProperty(element, "cards", path);
            if (cardsElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid($"{path}.cards: expected an array.");
            }

            var cards = new List<DeckCardEntry>();
            int cardIndex = 0;
            foreach (JsonElement cardElement in cardsElement.EnumerateArray())
            {
                string cardPath = $"{path}.cards[{cardIndex}]";
                if (cardElement.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid($"{cardPath}: expected an object.");
                }

                EnsureObjectShape(cardElement, CardProperties, cardPath);
                cards.Add(new DeckCardEntry(
                    RequiredString(cardElement, "code", cardPath),
                    RequiredInt(cardElement, "quantity", cardPath)));
                cardIndex++;
            }

            presets.Add(new OfficialPreset(
                RequiredString(element, "presetId", path),
                side,
                RequiredString(element, "identityCode", path),
                RequiredString(element, "kind", path),
                RequiredBool(element, "playable", path),
                cards));
            presetIndex++;
        }

        var catalog = new OfficialPresetCatalog(provenance, presets);
        catalog.Validate();
        return catalog;
    }

    private static PresetDataValidationException Invalid(string message) =>
        new([message]);

    private static void EnsureObjectShape(
        JsonElement element,
        IReadOnlySet<string> allowed,
        string path)
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

    private static int RequiredInt(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw Invalid($"{path}.{name}: expected an integer.");
        }

        return result;
    }

    private static bool RequiredBool(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Invalid($"{path}.{name}: expected a boolean.");
        }

        return value.GetBoolean();
    }

    private static bool IsSha256OrCommit(string value) =>
        value.Length == 40
        && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsStableIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(static character => !char.IsWhiteSpace(character) && !char.IsControl(character));

    private static bool IsStableCardCode(string value) =>
        value.Length == 5 && value.All(static character => character is >= '0' and <= '9');

    private static void RequireNonblank(List<string> problems, string? value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{path}: must be nonblank.");
        }
    }
}
