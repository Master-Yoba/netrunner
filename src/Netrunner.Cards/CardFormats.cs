using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>A format's ban list, as card codes.</summary>
public sealed record CardFormatRestriction(
    string Id,
    IReadOnlyList<string> Banned,
    IReadOnlyList<string> Restricted);

/// <summary>
/// The Startup-style limit on high-value agendas: at most
/// <see cref="MaximumCards"/> agendas worth <see cref="MinimumPoints"/> or more.
/// </summary>
public sealed record HighValueAgendaLimit(int MinimumPoints, int MaximumCards);

/// <summary>A format's deck rules beyond the identity's own size and influence.</summary>
public sealed record CardFormatDeckRules(int MaxCopiesByName, HighValueAgendaLimit? HighValueAgendaLimit);

/// <summary>
/// One versioned, data-only format snapshot (C2). It names the card pool that
/// plays it, the legal packs, the ban list and the deck rules. An incomplete
/// format validates decks but is not offered for play.
/// </summary>
public sealed record CardFormat(
    string Id,
    string Name,
    string EffectiveDate,
    bool Complete,
    string CardPool,
    IReadOnlyList<string> LegalPacks,
    IReadOnlyList<string> MissingPacks,
    CardFormatRestriction? Restriction,
    CardFormatDeckRules DeckRules,
    string SourceNote)
{
    /// <summary>Whether a card code is banned in this format.</summary>
    public bool IsBanned(string code) =>
        Restriction?.Banned.Contains(code, StringComparer.Ordinal) == true;

    /// <summary>Whether this build can play the format: it is complete.</summary>
    public bool Playable => Complete;
}

/// <summary>Stable format identifiers.</summary>
public static class CardFormatIds
{
    public const string Gateway = "gateway";

    public const string GatewayElevation = "gateway-elevation";

    public const string Startup2025 = "startup-2025";

    public const string Startup2026 = "startup-2026";
}

/// <summary>
/// FORMAT-401: the embedded format snapshots under <c>data/formats</c>, parsed
/// strictly and validated against the card pools they name.
/// </summary>
public sealed class CardFormatCatalog
{
    private const string ResourcePrefix = "Netrunner.Cards.Data.formats.";

    private static readonly Lazy<CardFormatCatalog> EmbeddedCatalog = new(
        LoadEmbeddedCore,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly HashSet<string> FormatProperties = new(
        [
            "schemaVersion", "id", "name", "effectiveDate", "complete", "cardPool", "legalPacks",
            "missingPacks", "restriction", "deckRules", "source",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> KnownPools = new(
        [CardPoolIds.Gateway, CardPoolIds.GatewayElevation],
        StringComparer.Ordinal);

    private readonly Dictionary<string, CardFormat> _formats;

    private CardFormatCatalog(IReadOnlyList<CardFormat> formats)
    {
        Formats = new ReadOnlyCollection<CardFormat>(formats.OrderBy(static format => format.Id, StringComparer.Ordinal).ToArray());
        _formats = Formats.ToDictionary(static format => format.Id, StringComparer.Ordinal);
    }

    /// <summary>Every format, ordered by id.</summary>
    public IReadOnlyList<CardFormat> Formats { get; }

    /// <summary>The formats this build can play.</summary>
    public IReadOnlyList<CardFormat> PlayableFormats => Formats.Where(static format => format.Playable).ToArray();

    public static CardFormatCatalog Embedded() => EmbeddedCatalog.Value;

    public CardFormat Get(string id) =>
        _formats.TryGetValue(id, out CardFormat? format)
            ? format
            : throw new CardDataValidationException($"FORMAT-401: unknown format '{id}'.");

    public bool TryGet(string id, out CardFormat format) => _formats.TryGetValue(id, out format!);

    /// <summary>Parses and validates a set of format documents.</summary>
    public static CardFormatCatalog Parse(IReadOnlyList<(string Name, byte[] Utf8Json)> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            throw new CardDataValidationException("FORMAT-401: at least one format snapshot is required.");
        }

        var formats = new List<CardFormat>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, byte[] bytes) in documents)
        {
            CardFormat format = ParseFormat(bytes, name);
            if (!ids.Add(format.Id))
            {
                throw new CardDataValidationException($"FORMAT-401: format '{format.Id}' is declared twice.");
            }

            formats.Add(format);
        }

        return new CardFormatCatalog(formats);
    }

    /// <summary>
    /// Checks every format against an authenticated pool: legal packs must be
    /// present, and every ban must name a card in the pool or one from a
    /// pack the format lists as missing.
    /// </summary>
    public void ValidateAgainst(CardDataSet pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        IReadOnlyList<string> packs = pool.PackCodes;
        foreach (CardFormat format in Formats)
        {
            foreach (string pack in format.LegalPacks)
            {
                if (!packs.Contains(pack, StringComparer.Ordinal))
                {
                    throw new CardDataValidationException(
                        $"FORMAT-401: format '{format.Id}' names pack '{pack}', which the pool does not have.");
                }
            }

            foreach (string code in format.Restriction?.Banned ?? [])
            {
                string? pack = pool.PackCodeOf(code);
                if (pack is null ? format.MissingPacks.Count == 0 : !format.LegalPacks.Contains(pack, StringComparer.Ordinal))
                {
                    throw new CardDataValidationException(
                        $"FORMAT-401: format '{format.Id}' bans card '{code}', which is not in its legal or missing packs.");
                }
            }
        }
    }

    private static CardFormat ParseFormat(byte[] bytes, string name)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            JsonElement root = document.RootElement;
            Require(root.ValueKind == JsonValueKind.Object, name, "the document must be an object");
            foreach (JsonProperty property in root.EnumerateObject())
            {
                Require(FormatProperties.Contains(property.Name), name, $"unknown field '{property.Name}'");
            }

            Require(Int(root, "schemaVersion", name) == 1, name, "schemaVersion must be 1");
            string id = String(root, "id", name);
            Require(Regex.IsMatch(id, "^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant), name, "id must be kebab-case");
            string effectiveDate = String(root, "effectiveDate", name);
            Require(
                DateOnly.TryParseExact(effectiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                name,
                "effectiveDate must be an ISO date");
            string pool = String(root, "cardPool", name);
            Require(KnownPools.Contains(pool), name, $"unknown cardPool '{pool}'");
            bool complete = Bool(root, "complete", name);
            ReadOnlyCollection<string> legalPacks = Strings(root, "legalPacks", name);
            Require(legalPacks.Count > 0, name, "legalPacks must not be empty");
            ReadOnlyCollection<string> missingPacks = Strings(root, "missingPacks", name);
            Require(complete == (missingPacks.Count == 0), name, "a format is complete exactly when it misses no packs");
            CardFormatRestriction? restriction = ParseRestriction(Property(root, "restriction", name), name);
            CardFormatDeckRules rules = ParseRules(Property(root, "deckRules", name), name);
            JsonElement source = Property(root, "source", name);
            Require(source.ValueKind == JsonValueKind.Object, name, "source must be an object");
            foreach (JsonProperty property in source.EnumerateObject())
            {
                Require(property.Name is "snapshotId" or "cardPoolId" or "note", name, $"unknown source field '{property.Name}'");
            }

            return new CardFormat(
                id,
                String(root, "name", name),
                effectiveDate,
                complete,
                pool,
                legalPacks,
                missingPacks,
                restriction,
                rules,
                String(source, "note", name));
        }
        catch (JsonException exception)
        {
            throw new CardDataValidationException($"FORMAT-401: format snapshot '{name}' is malformed JSON.", exception);
        }
    }

    private static CardFormatRestriction? ParseRestriction(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        Require(element.ValueKind == JsonValueKind.Object, name, "restriction must be an object or null");
        foreach (JsonProperty property in element.EnumerateObject())
        {
            Require(property.Name is "id" or "banned" or "restricted", name, $"unknown restriction field '{property.Name}'");
        }

        ReadOnlyCollection<string> banned = Strings(element, "banned", name);
        ReadOnlyCollection<string> restricted = Strings(element, "restricted", name);
        foreach (string code in banned.Concat(restricted))
        {
            Require(Regex.IsMatch(code, "^[0-9]{5}$", RegexOptions.CultureInvariant), name, $"'{code}' is not a card code");
        }

        return new CardFormatRestriction(String(element, "id", name), banned, restricted);
    }

    private static CardFormatDeckRules ParseRules(JsonElement element, string name)
    {
        Require(element.ValueKind == JsonValueKind.Object, name, "deckRules must be an object");
        foreach (JsonProperty property in element.EnumerateObject())
        {
            Require(property.Name is "maxCopiesByName" or "highValueAgendaLimit", name, $"unknown deckRules field '{property.Name}'");
        }

        int copies = Int(element, "maxCopiesByName", name);
        Require(copies >= 1, name, "maxCopiesByName must be positive");
        JsonElement limit = Property(element, "highValueAgendaLimit", name);
        HighValueAgendaLimit? agendaLimit = null;
        if (limit.ValueKind != JsonValueKind.Null)
        {
            Require(limit.ValueKind == JsonValueKind.Object, name, "highValueAgendaLimit must be an object or null");
            foreach (JsonProperty property in limit.EnumerateObject())
            {
                Require(property.Name is "minimumPoints" or "maximumCards", name, $"unknown highValueAgendaLimit field '{property.Name}'");
            }

            agendaLimit = new HighValueAgendaLimit(Int(limit, "minimumPoints", name), Int(limit, "maximumCards", name));
            Require(agendaLimit.MinimumPoints >= 1 && agendaLimit.MaximumCards >= 0, name, "highValueAgendaLimit values are out of range");
        }

        return new CardFormatDeckRules(copies, agendaLimit);
    }

    private static JsonElement Property(JsonElement element, string property, string name) =>
        element.TryGetProperty(property, out JsonElement value)
            ? value
            : throw new CardDataValidationException($"FORMAT-401: format snapshot '{name}' is missing '{property}'.");

    private static string String(JsonElement element, string property, string name)
    {
        JsonElement value = Property(element, property, name);
        Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()), name, $"'{property}' must be a nonblank string");
        return value.GetString()!;
    }

    private static int Int(JsonElement element, string property, string name)
    {
        JsonElement value = Property(element, property, name);
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _), name, $"'{property}' must be an integer");
        return value.GetInt32();
    }

    private static bool Bool(JsonElement element, string property, string name)
    {
        JsonElement value = Property(element, property, name);
        Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, name, $"'{property}' must be a boolean");
        return value.GetBoolean();
    }

    private static ReadOnlyCollection<string> Strings(JsonElement element, string property, string name)
    {
        JsonElement value = Property(element, property, name);
        Require(value.ValueKind == JsonValueKind.Array, name, $"'{property}' must be an array");
        var values = new List<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            Require(item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()), name, $"'{property}' must hold nonblank strings");
            values.Add(item.GetString()!);
        }

        Require(values.Distinct(StringComparer.Ordinal).Count() == values.Count, name, $"'{property}' must not repeat a value");
        return values.AsReadOnly();
    }

    private static void Require(bool condition, string name, string message)
    {
        if (!condition)
        {
            throw new CardDataValidationException($"FORMAT-401: format snapshot '{name}': {message}.");
        }
    }

    private static CardFormatCatalog LoadEmbeddedCore()
    {
        Assembly assembly = typeof(CardFormatCatalog).Assembly;
        var documents = new List<(string Name, byte[] Utf8Json)>();
        foreach (string resource in assembly.GetManifestResourceNames()
            .Where(static resource => resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new CardDataValidationException($"FORMAT-401: embedded format '{resource}' is unreadable.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            documents.Add((resource[ResourcePrefix.Length..], buffer.ToArray()));
        }

        return Parse(documents);
    }
}

/// <summary>
/// Deck validation under a format: the identity's own rules
/// (<see cref="DeckValidator"/>), then the format's legal packs, ban list and
/// deck rules.
/// </summary>
public static class FormatDeckValidator
{
    public static DeckValidationResult Validate(
        DeckRequest request,
        CardFormat format,
        CardDataSet pool,
        OfficialPresetCatalog? officialPresets = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(pool);
        DeckValidationResult result = DeckValidator.Validate(request, pool, officialPresets);
        var diagnostics = result.Diagnostics.ToList();
        var metadata = new CardMetadataCatalog(pool.Cards);

        void CheckCard(string code, string path)
        {
            if (!metadata.ContainsKey(code))
            {
                return;
            }

            string? pack = pool.PackCodeOf(code);
            if (pack is null || !format.LegalPacks.Contains(pack, StringComparer.Ordinal))
            {
                diagnostics.Add(new DeckValidationDiagnostic(
                    "format-pack",
                    path,
                    $"card '{code}' is from pack '{pack}', which is not legal in {format.Name}."));
            }

            if (format.IsBanned(code))
            {
                diagnostics.Add(new DeckValidationDiagnostic(
                    "format-banned",
                    path,
                    $"card '{code}' is banned in {format.Name}."));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.IdentityCode))
        {
            CheckCard(request.IdentityCode, "identityCode");
        }

        int highValueAgendas = 0;
        var copiesByName = new Dictionary<string, (int Quantity, int FirstIndex)>(StringComparer.Ordinal);
        for (int index = 0; index < request.Cards.Count; index++)
        {
            DeckCardEntry? entry = request.Cards[index];
            if (entry is null || string.IsNullOrWhiteSpace(entry.Code))
            {
                continue;
            }

            CheckCard(entry.Code, $"cards[{index}].code");
            if (!metadata.TryGetValue(entry.Code, out NormalizedCardDefinition? card))
            {
                continue;
            }

            copiesByName[card.Title] = copiesByName.TryGetValue(card.Title, out (int Quantity, int FirstIndex) existing)
                ? (existing.Quantity + entry.Quantity, existing.FirstIndex)
                : (entry.Quantity, index);
            if (format.DeckRules.HighValueAgendaLimit is { } limit
                && string.Equals(card.Type, "agenda", StringComparison.Ordinal)
                && card.AgendaPoints >= limit.MinimumPoints)
            {
                highValueAgendas += entry.Quantity;
            }
        }

        foreach ((string title, (int quantity, int firstIndex)) in copiesByName)
        {
            if (quantity > format.DeckRules.MaxCopiesByName)
            {
                diagnostics.Add(new DeckValidationDiagnostic(
                    "format-copy-limit",
                    $"cards[{firstIndex}].quantity",
                    $"card name '{title}' has {quantity} copies; {format.Name} allows {format.DeckRules.MaxCopiesByName}."));
            }
        }

        if (format.DeckRules.HighValueAgendaLimit is { } agendaLimit && highValueAgendas > agendaLimit.MaximumCards)
        {
            diagnostics.Add(new DeckValidationDiagnostic(
                "format-agenda-limit",
                "cards",
                $"the deck has {highValueAgendas} agendas worth {agendaLimit.MinimumPoints} or more points; "
                + $"{format.Name} allows {agendaLimit.MaximumCards}."));
        }

        return new DeckValidationResult(
            diagnostics,
            result.CardCount,
            result.InfluenceUsed,
            result.AgendaPoints,
            result.AgendaPointRange,
            result.ExactTutorialPreset,
            result.CardDataVersion);
    }
}
