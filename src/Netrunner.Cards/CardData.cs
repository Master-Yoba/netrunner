using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// The deliberately small, normalized card-data contract consumed by the
/// client and future rules work.  It contains metadata only: no artwork,
/// flavor text, illustrator credits, or executable interpretation of card
/// text is included.
/// </summary>
public sealed class NormalizedCardDefinition
{
    public NormalizedCardDefinition(
        string code,
        string title,
        string side,
        string faction,
        string type,
        IReadOnlyList<string> subtypes,
        string text,
        string setCode,
        int quantity,
        int deckLimit,
        bool uniqueness,
        int? cost,
        int? strength,
        int? memoryCost,
        int? trashCost,
        int? agendaPoints,
        int? advancementRequirement,
        int? influenceCost,
        int? minimumDeckSize,
        int? influenceLimit,
        int? baseLink)
    {
        Code = RequireText(code, nameof(code));
        Title = RequireText(title, nameof(title));
        Side = RequireText(side, nameof(side));
        Faction = RequireText(faction, nameof(faction));
        Type = RequireText(type, nameof(type));
        ArgumentNullException.ThrowIfNull(subtypes);
        Subtypes = new ReadOnlyCollection<string>(subtypes.ToArray());
        // Upstream v1 explicitly permits an empty rules-text string for a
        // metadata-only card.  Presence is still required by the JSON
        // contract; only null/omitted text is invalid.
        Text = RequireText(text, nameof(text), allowEmpty: true);
        SetCode = RequireText(setCode, nameof(setCode));
        Quantity = quantity;
        DeckLimit = deckLimit;
        Uniqueness = uniqueness;
        Cost = cost;
        Strength = strength;
        MemoryCost = memoryCost;
        TrashCost = trashCost;
        AgendaPoints = agendaPoints;
        AdvancementRequirement = advancementRequirement;
        InfluenceCost = influenceCost;
        MinimumDeckSize = minimumDeckSize;
        InfluenceLimit = influenceLimit;
        BaseLink = baseLink;
    }

    public string Code { get; }

    public string Title { get; }

    public string Side { get; }

    public string Faction { get; }

    public string Type { get; }

    public IReadOnlyList<string> Subtypes { get; }

    public string Text { get; }

    public string SetCode { get; }

    public int Quantity { get; }

    public int DeckLimit { get; }

    public bool Uniqueness { get; }

    public int? Cost { get; }

    public int? Strength { get; }

    public int? MemoryCost { get; }

    public int? TrashCost { get; }

    public int? AgendaPoints { get; }

    public int? AdvancementRequirement { get; }

    public int? InfluenceCost { get; }

    public int? MinimumDeckSize { get; }

    public int? InfluenceLimit { get; }

    public int? BaseLink { get; }

    /// <summary>Maps the normalized record to the legacy compact card model.</summary>
    public CardDefinition ToCardDefinition() => new(
        Code,
        Title,
        Side,
        Faction,
        Type,
        Subtypes.Count == 0 ? null : string.Join(" - ", Subtypes),
        Cost,
        Strength,
        AgendaPoints,
        AdvancementRequirement,
        Text,
        SetCode,
        Quantity);

    private static string RequireText(string value, string parameterName, bool allowEmpty = false) =>
        value is null
            || (string.IsNullOrWhiteSpace(value) && !(allowEmpty && value.Length == 0))
            ? throw new ArgumentException("Card metadata text is required.", parameterName)
            : value;
}

/// <summary>Provenance and checksum metadata for one normalized card file.</summary>
public sealed record CardDataManifest(
    int ManifestSchemaVersion,
    string SourceUrl,
    string CommitSha,
    string RetrievalDate,
    string UpstreamSchemaVersion,
    int NormalizedSchemaVersion,
    string OutputSha256,
    string SourceLicensePath,
    string SourceLicenseIdentity,
    string SourcePackPath,
    string PackCode,
    string PackName,
    int CardCount);

/// <summary>Detached card data plus the manifest that authenticated its bytes.</summary>
public sealed class CardDataSet
{
    internal CardDataSet(
        IReadOnlyList<NormalizedCardDefinition> cards,
        CardDataManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(cards);
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Cards = new ReadOnlyCollection<NormalizedCardDefinition>(cards.ToArray());
        CardDefinitions = new ReadOnlyCollection<CardDefinition>(
            Cards.Select(static card => card.ToCardDefinition()).ToArray());
        Packs = new ReadOnlyCollection<CardDataSet>([this]);
    }

    private CardDataSet(IReadOnlyList<CardDataSet> packs)
    {
        Manifest = packs[0].Manifest;
        Cards = new ReadOnlyCollection<NormalizedCardDefinition>(
            packs.SelectMany(static pack => pack.Cards).ToArray());
        CardDefinitions = new ReadOnlyCollection<CardDefinition>(
            Cards.Select(static card => card.ToCardDefinition()).ToArray());
        Packs = new ReadOnlyCollection<CardDataSet>(packs.ToArray());
    }

    /// <summary>Every card in the set, pack by pack in composition order.</summary>
    public IReadOnlyList<NormalizedCardDefinition> Cards { get; }

    public IReadOnlyList<CardDefinition> CardDefinitions { get; }

    /// <summary>
    /// The first pack's manifest. Every pack in a combined set shares one
    /// upstream commit, so <see cref="CardDataManifest.CommitSha"/> is the
    /// set's card-data version.
    /// </summary>
    public CardDataManifest Manifest { get; }

    /// <summary>
    /// The authenticated single-pack sets this set is made of, each with its
    /// own manifest, count and checksum. A single-pack set lists itself.
    /// </summary>
    public IReadOnlyList<CardDataSet> Packs { get; }

    /// <summary>The pack codes of <see cref="Packs"/>, in composition order.</summary>
    public IReadOnlyList<string> PackCodes => Packs.Select(static pack => pack.Manifest.PackCode).ToArray();

    /// <summary>Returns the pack code of the pack that holds a card code, if any.</summary>
    public string? PackCodeOf(string cardCode)
    {
        foreach (CardDataSet pack in Packs)
        {
            if (pack.Cards.Any(card => string.Equals(card.Code, cardCode, StringComparison.Ordinal)))
            {
                return pack.Manifest.PackCode;
            }
        }

        return null;
    }

    /// <summary>
    /// Combines authenticated single-pack sets into one pool. The packs must
    /// share a commit and must not repeat a pack or card code.
    /// </summary>
    public static CardDataSet Combine(IReadOnlyList<CardDataSet> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        if (packs.Count == 0)
        {
            throw new CardDataValidationException("A card pool must contain at least one pack.");
        }

        if (packs.Count == 1)
        {
            return packs[0];
        }

        CardDataSet[] singles = packs.SelectMany(static pack => pack.Packs).ToArray();
        string commit = singles[0].Manifest.CommitSha;
        var packCodes = new HashSet<string>(StringComparer.Ordinal);
        var cardCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (CardDataSet pack in singles)
        {
            if (!string.Equals(pack.Manifest.CommitSha, commit, StringComparison.Ordinal))
            {
                throw new CardDataValidationException(
                    $"Pack '{pack.Manifest.PackCode}' comes from commit '{pack.Manifest.CommitSha}', not '{commit}'.");
            }

            if (!packCodes.Add(pack.Manifest.PackCode))
            {
                throw new CardDataValidationException($"Pack '{pack.Manifest.PackCode}' appears twice in a card pool.");
            }

            foreach (NormalizedCardDefinition card in pack.Cards)
            {
                if (!cardCodes.Add(card.Code))
                {
                    throw new CardDataValidationException($"Card code '{card.Code}' appears in more than one pack.");
                }
            }
        }

        return new CardDataSet(singles);
    }
}

/// <summary>
/// One pinned official pack: its upstream path, expected identity and size,
/// local-only directory and sentinel titles. Each pack is authenticated by its
/// own manifest; a card pool is a combination of packs.
/// </summary>
public sealed record OfficialCardPack(
    string PackCode,
    string PackName,
    string SourcePackPath,
    int CardCount,
    string RelativeDirectory,
    IReadOnlyDictionary<string, string> SentinelTitles);

/// <summary>The official packs this build knows how to authenticate.</summary>
public static class OfficialCardPacks
{
    public const string SystemGatewayCode = "sg";

    public const string ElevationCode = "elev";

    public static OfficialCardPack SystemGateway { get; } = new(
        SystemGatewayCode,
        "System Gateway – Remastered Edition",
        "pack/sg.json",
        77,
        "data/vendor/system-gateway",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["30001"] = "René “Loup” Arcemont: Party Animal",
            ["30036"] = "Luminal Transubstantiation",
            ["30077"] = "The Syndicate: Profit over Principle",
        });

    public static OfficialCardPack Elevation { get; } = new(
        ElevationCode,
        "Elevation",
        "pack/elev.json",
        82,
        "data/vendor/elevation",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["35023"] = "Dewi Subrotoputri: Pedagogical Dhalang",
            ["35057"] = "Nebula Talent Management: Making Stars",
            ["35082"] = "Mahkota Langit Grid",
        });

    /// <summary>Every official pack, in pool order.</summary>
    public static IReadOnlyList<OfficialCardPack> All { get; } = [SystemGateway, Elevation];

    public static OfficialCardPack? Find(string? packCode) =>
        All.FirstOrDefault(pack => string.Equals(pack.PackCode, packCode, StringComparison.Ordinal));

    public static OfficialCardPack Get(string packCode) =>
        Find(packCode) ?? throw new CardDataValidationException($"Unknown official pack '{packCode}'.");
}

/// <summary>Actionable error raised for malformed or incompatible card data.</summary>
public sealed class CardDataValidationException : Exception
{
    public CardDataValidationException(string message)
        : base(message)
    {
    }

    public CardDataValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Symbol normalization is intentionally lexical.  A source token such as
/// <c>[credit]</c> becomes <c>&lt;symbol:credit&gt;</c>; no rule meaning is
/// inferred.  The vocabulary is explicit so a newly introduced icon fails
/// validation instead of silently becoming arbitrary text.
/// </summary>
public static class CardTextNormalizer
{
    private static readonly Regex SourceSymbolPattern = new(
        @"\[(?<name>[A-Za-z][A-Za-z0-9-]*)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NormalizedSymbolPattern = new(
        @"<symbol:(?<name>[A-Za-z][A-Za-z0-9-]*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SymbolVocabulary = new(
        new[]
        {
            "advancement-counter",
            "agenda-point",
            "bad-pub",
            "brain-damage",
            "break",
            "card",
            "click",
            "click-cost",
            "credit",
            "credit-cost",
            "influence",
            "interrupt",
            "link",
            "meat-damage",
            "memory",
            "mu",
            "net-damage",
            "power-counter",
            "recurring-credit",
            "rez",
            "strength",
            "subroutine",
            "tag",
            "trace",
            "trash",
            "trash-cost",
            "virus-counter",
        },
        StringComparer.Ordinal);

    public static IReadOnlySet<string> SupportedSymbols => SymbolVocabulary;

    public static string NormalizeSymbols(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalizedLineEndings = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return SourceSymbolPattern.Replace(
            normalizedLineEndings,
            static match =>
            {
                string name = match.Groups["name"].Value.ToLowerInvariant();
                if (!SymbolVocabulary.Contains(name))
                {
                    throw new CardDataValidationException(
                        $"Unsupported card text symbol '[{name}]'. Add it to the explicit vocabulary first.");
                }

                return $"<symbol:{name}>";
            });
    }

    public static void ValidateNormalized(
        string value,
        string fieldName = "text",
        bool allowEmpty = false)
    {
        if (value is null
            || (string.IsNullOrWhiteSpace(value) && !(allowEmpty && value.Length == 0)))
        {
            throw new CardDataValidationException(
                allowEmpty
                    ? $"Card {fieldName} must be an explicitly present string."
                    : $"Card {fieldName} must be nonblank.");
        }

        if (value.Contains('\r'))
        {
            throw new CardDataValidationException($"Card {fieldName} must use LF line endings.");
        }

        foreach (Match match in NormalizedSymbolPattern.Matches(value))
        {
            string name = match.Groups["name"].Value;
            if (!string.Equals(name, name.ToLowerInvariant(), StringComparison.Ordinal)
                || !SymbolVocabulary.Contains(name))
            {
                throw new CardDataValidationException(
                    $"Card {fieldName} contains unsupported normalized symbol '<symbol:{name}>'.");
            }
        }

        if (SourceSymbolPattern.IsMatch(value))
        {
            throw new CardDataValidationException(
                $"Card {fieldName} contains unnormalized bracket symbol markup.");
        }
    }
}

/// <summary>Strict validation for normalized records and provenance.</summary>
public static class CardDataValidator
{
    public const string OfficialSourceUrl =
        "https://github.com/Null-Signal-Games/netrunner-cards-json";

    public const string OfficialCommitSha =
        "236140cf84491c23995b028bc791281837520b97";

    public const string OfficialPackCode = "sg";

    public const string OfficialPackName = "System Gateway – Remastered Edition";

    public const int OfficialCardCount = 77;

    public const int CurrentManifestSchemaVersion = 1;

    public const int CurrentNormalizedSchemaVersion = 1;

    private static readonly IReadOnlySet<string> CorpFactions = new HashSet<string>(
        new[] { "haas-bioroid", "jinteki", "nbn", "neutral-corp", "weyland-consortium" },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> RunnerFactions = new HashSet<string>(
        new[] { "adam", "anarch", "apex", "criminal", "neutral-runner", "shaper", "sunny-lebeau" },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> CorpTypes = new HashSet<string>(
        new[] { "agenda", "asset", "ice", "identity", "operation", "upgrade" },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> RunnerTypes = new HashSet<string>(
        new[] { "event", "hardware", "identity", "program", "resource" },
        StringComparer.Ordinal);

    public static void Validate(
        IReadOnlyList<NormalizedCardDefinition> cards,
        CardDataManifest manifest,
        bool requireOfficialGateway = false)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateManifest(manifest, requireOfficialGateway);

        if (cards.Count != manifest.CardCount)
        {
            throw new CardDataValidationException(
                $"Manifest card count {manifest.CardCount} does not match normalized data count {cards.Count}.");
        }

        ValidateRecords(cards, manifest.PackCode, requireOfficialGateway);

        if (requireOfficialGateway)
        {
            ValidateOfficialCardsUnbound(cards);
            ValidateOfficialSentinels(cards, OfficialCardPacks.Get(manifest.PackCode));
        }
    }

    public static void ValidateRecords(
        IReadOnlyList<NormalizedCardDefinition> cards,
        string expectedPackCode,
        bool requireOfficialCodes = false)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (string.IsNullOrWhiteSpace(expectedPackCode))
        {
            throw new CardDataValidationException("A normalized pack code is required.");
        }

        if (cards.Count == 0)
        {
            throw new CardDataValidationException("Normalized card data must contain at least one record.");
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        NormalizedCardDefinition? previous = null;
        foreach (NormalizedCardDefinition? card in cards)
        {
            if (card is null)
            {
                throw new CardDataValidationException("Normalized card data must not contain null records.");
            }

            ValidateRecord(card, expectedPackCode, requireOfficialCodes);
            if (!codes.Add(card.Code))
            {
                throw new CardDataValidationException($"Duplicate normalized card code '{card.Code}'.");
            }

            if (previous is not null
                && CompareTitleThenCode(previous, card) > 0)
            {
                throw new CardDataValidationException(
                    $"Normalized cards must be ordered by title then code; '{previous.Code}' precedes '{card.Code}'.");
            }

            previous = card;
        }
    }

    public static void ValidateManifest(CardDataManifest manifest, bool requireOfficialGateway = false)
    {
        if (manifest.ManifestSchemaVersion != CurrentManifestSchemaVersion)
        {
            throw new CardDataValidationException(
                $"Unsupported card-data manifest schema version {manifest.ManifestSchemaVersion}; "
                + $"expected {CurrentManifestSchemaVersion}.");
        }

        if (manifest.NormalizedSchemaVersion != CurrentNormalizedSchemaVersion)
        {
            throw new CardDataValidationException(
                $"Unsupported normalized card-data schema version {manifest.NormalizedSchemaVersion}; "
                + $"expected {CurrentNormalizedSchemaVersion}.");
        }

        RequireNonblank(manifest.SourceUrl, "manifest sourceUrl");
        RequireNonblank(manifest.CommitSha, "manifest commitSha");
        if (!Regex.IsMatch(manifest.CommitSha, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
        {
            throw new CardDataValidationException("Manifest commitSha must be a lowercase full 40-hex commit SHA.");
        }

        if (!DateOnly.TryParseExact(
                manifest.RetrievalDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _)
            || manifest.RetrievalDate.Length != 10)
        {
            throw new CardDataValidationException("Manifest retrievalDate must be an ISO date (YYYY-MM-DD).");
        }

        RequireNonblank(manifest.UpstreamSchemaVersion, "manifest upstreamSchemaVersion");
        RequireNonblank(manifest.OutputSha256, "manifest outputSha256");
        if (!Regex.IsMatch(manifest.OutputSha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
        {
            throw new CardDataValidationException("Manifest outputSha256 must be lowercase SHA-256 hex.");
        }

        RequireNonblank(manifest.SourceLicensePath, "manifest sourceLicensePath");
        RequireNonblank(manifest.SourceLicenseIdentity, "manifest sourceLicenseIdentity");
        RequireNonblank(manifest.SourcePackPath, "manifest sourcePackPath");
        RequireNonblank(manifest.PackCode, "manifest packCode");
        RequireNonblank(manifest.PackName, "manifest packName");
        if (manifest.CardCount <= 0)
        {
            throw new CardDataValidationException("Manifest cardCount must be positive.");
        }

        if (!requireOfficialGateway)
        {
            return;
        }

        OfficialCardPack pack = OfficialCardPacks.Find(manifest.PackCode) ?? OfficialCardPacks.SystemGateway;
        if (!string.Equals(manifest.SourceUrl, OfficialSourceUrl, StringComparison.Ordinal)
            || !string.Equals(manifest.CommitSha, OfficialCommitSha, StringComparison.Ordinal)
            || !string.Equals(manifest.SourcePackPath, pack.SourcePackPath, StringComparison.Ordinal)
            || !string.Equals(manifest.SourceLicensePath, "COPYRIGHT.md", StringComparison.Ordinal)
            || !string.Equals(manifest.UpstreamSchemaVersion, "v1", StringComparison.Ordinal)
            || !string.Equals(manifest.PackCode, pack.PackCode, StringComparison.Ordinal)
            || !string.Equals(manifest.PackName, pack.PackName, StringComparison.Ordinal)
            || !string.Equals(manifest.SourceLicenseIdentity, "NOASSERTION", StringComparison.Ordinal)
            || manifest.CardCount != pack.CardCount)
        {
            throw new CardDataValidationException(
                pack == OfficialCardPacks.SystemGateway
                    ? "The manifest does not describe the pinned System Gateway local-only snapshot."
                    : $"The manifest does not describe the pinned {pack.PackName} local-only snapshot.");
        }
    }

    public static void ValidateOfficialCardsUnbound(
        IReadOnlyList<NormalizedCardDefinition> cards)
    {
        foreach (NormalizedCardDefinition card in cards)
        {
            if (ToyCardBehaviorCatalog.TryGet(new CardDefinitionId(card.Code), out _))
            {
                throw new CardDataValidationException(
                    $"Official card '{card.Code}' is unexpectedly bound to fixture behavior. "
                    + "Official card metadata is data-only in M1.");
            }
        }
    }

    private static void ValidateOfficialSentinels(
        IReadOnlyList<NormalizedCardDefinition> cards,
        OfficialCardPack pack)
    {
        var byCode = cards.ToDictionary(static card => card.Code, StringComparer.Ordinal);
        foreach ((string code, string title) in pack.SentinelTitles)
        {
            if (!byCode.TryGetValue(code, out NormalizedCardDefinition? card))
            {
                throw new CardDataValidationException(
                    pack == OfficialCardPacks.SystemGateway
                        ? $"Official System Gateway data is missing sentinel card '{code}'."
                        : $"Official {pack.PackName} data is missing sentinel card '{code}'.");
            }

            if (!string.Equals(card.Title, title, StringComparison.Ordinal))
            {
                throw new CardDataValidationException(
                    $"Official sentinel '{code}' has title '{card.Title}', expected '{title}'.");
            }
        }
    }

    internal static int CompareTitleThenCode(
        NormalizedCardDefinition left,
        NormalizedCardDefinition right)
    {
        int titleComparison = string.Compare(left.Title, right.Title, StringComparison.Ordinal);
        return titleComparison != 0
            ? titleComparison
            : string.Compare(left.Code, right.Code, StringComparison.Ordinal);
    }

    private static void ValidateRecord(
        NormalizedCardDefinition card,
        string expectedPackCode,
        bool requireOfficialCode)
    {
        RequireStableCode(card.Code, "card code");
        if (requireOfficialCode && !Regex.IsMatch(card.Code, "^[0-9]{5}$", RegexOptions.CultureInvariant))
        {
            throw new CardDataValidationException($"Official card code '{card.Code}' must be exactly five digits.");
        }

        RequireNonblank(card.Title, $"card '{card.Code}' title");
        CardTextNormalizer.ValidateNormalized(card.Text, $"'{card.Code}' text", allowEmpty: true);
        if (!string.Equals(card.SetCode, expectedPackCode, StringComparison.Ordinal))
        {
            throw new CardDataValidationException(
                $"Card '{card.Code}' set/pack code '{card.SetCode}' does not match '{expectedPackCode}'.");
        }

        if (card.Quantity <= 0 || card.DeckLimit <= 0)
        {
            throw new CardDataValidationException(
                $"Card '{card.Code}' quantity and deckLimit must both be positive.");
        }

        if (!string.Equals(card.Side, "corp", StringComparison.Ordinal)
            && !string.Equals(card.Side, "runner", StringComparison.Ordinal))
        {
            throw new CardDataValidationException($"Card '{card.Code}' has invalid side '{card.Side}'.");
        }

        IReadOnlySet<string> factions = card.Side == "corp" ? CorpFactions : RunnerFactions;
        IReadOnlySet<string> types = card.Side == "corp" ? CorpTypes : RunnerTypes;
        if (!factions.Contains(card.Faction))
        {
            throw new CardDataValidationException(
                $"Card '{card.Code}' faction '{card.Faction}' is invalid for side '{card.Side}'.");
        }

        if (!types.Contains(card.Type))
        {
            throw new CardDataValidationException(
                $"Card '{card.Code}' type '{card.Type}' is invalid for side '{card.Side}'.");
        }

        var subtypeSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (string subtype in card.Subtypes)
        {
            RequireNonblank(subtype, $"card '{card.Code}' subtype");
            if (!subtypeSet.Add(subtype))
            {
                throw new CardDataValidationException(
                    $"Card '{card.Code}' contains duplicate subtype '{subtype}'.");
            }
        }

        ValidateNonnegative(card.Code, "cost", card.Cost);
        ValidateNonnegative(card.Code, "strength", card.Strength);
        ValidateNonnegative(card.Code, "memoryCost", card.MemoryCost);
        ValidateNonnegative(card.Code, "trashCost", card.TrashCost);
        ValidateNonnegative(card.Code, "agendaPoints", card.AgendaPoints);
        ValidateNonnegative(card.Code, "advancementRequirement", card.AdvancementRequirement);
        ValidateNonnegative(card.Code, "influenceCost", card.InfluenceCost);
        ValidateNonnegative(card.Code, "minimumDeckSize", card.MinimumDeckSize);
        ValidateNonnegative(card.Code, "influenceLimit", card.InfluenceLimit);
        ValidateNonnegative(card.Code, "baseLink", card.BaseLink);

        if (card.Type == "agenda")
        {
            RequireValue(card.Code, "agendaPoints", card.AgendaPoints);
            RequireValue(card.Code, "advancementRequirement", card.AdvancementRequirement);
        }
        else if (card.AgendaPoints is not null || card.AdvancementRequirement is not null)
        {
            throw new CardDataValidationException(
                $"Non-agenda card '{card.Code}' must not carry agenda-only numeric fields.");
        }

        // CR 2.6.1, 3.4.5 and 3.5.3: assets and upgrades have trash costs, as
        // do some ice and operations (Elevation's gray and black ops).
        if (card.Type is not ("asset" or "upgrade" or "ice" or "operation") && card.TrashCost is not null)
        {
            throw new CardDataValidationException(
                $"Card '{card.Code}' type '{card.Type}' must not carry trashCost.");
        }

        if (card.Type != "program" && card.MemoryCost is not null)
        {
            throw new CardDataValidationException(
                $"Card '{card.Code}' type '{card.Type}' must not carry memoryCost.");
        }

        if (card.Type != "identity"
            && (card.MinimumDeckSize is not null || card.InfluenceLimit is not null || card.BaseLink is not null))
        {
            throw new CardDataValidationException(
                $"Non-identity card '{card.Code}' must not carry identity-only numeric fields.");
        }

        if (card.Type == "identity" && card.MinimumDeckSize is null)
        {
            throw new CardDataValidationException(
                $"Identity card '{card.Code}' must carry minimumDeckSize, explicitly or as a mapped value.");
        }

        if (card.Side == "corp" && card.BaseLink is not null)
        {
            throw new CardDataValidationException($"Corp identity '{card.Code}' must not carry baseLink.");
        }
    }

    private static void RequireStableCode(string value, string fieldName)
    {
        RequireNonblank(value, fieldName);
        if (value.Length > 128 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
        {
            throw new CardDataValidationException($"{fieldName} must be a short stable non-whitespace identifier.");
        }
    }

    private static void RequireNonblank(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CardDataValidationException($"{fieldName} must be nonblank.");
        }
    }

    private static void RequireValue(string code, string fieldName, int? value)
    {
        if (value is null)
        {
            throw new CardDataValidationException($"Card '{code}' requires {fieldName}.");
        }
    }

    private static void ValidateNonnegative(string code, string fieldName, int? value)
    {
        if (value is < 0)
        {
            throw new CardDataValidationException($"Card '{code}' {fieldName} must not be negative.");
        }
    }
}

/// <summary>Deterministic conversion from upstream v1 card JSON to normalized metadata.</summary>
public static class CardDataNormalizer
{
    private static readonly IReadOnlySet<string> UpstreamProperties = new HashSet<string>(
        new[]
        {
            "advancement_cost",
            "agenda_points",
            "base_link",
            "code",
            "cost",
            "deck_limit",
            "faction_code",
            "faction_cost",
            "flavor",
            "illustrator",
            "influence_limit",
            "keywords",
            "memory_cost",
            "minimum_deck_size",
            "narrative_text",
            "pack_code",
            "position",
            "quantity",
            "side_code",
            "strength",
            "stripped_text",
            "stripped_title",
            "text",
            "title",
            "trash_cost",
            "type_code",
            "uniqueness",
        },
        StringComparer.Ordinal);

    public static IReadOnlyList<NormalizedCardDefinition> Normalize(
        string upstreamJson,
        string expectedPackCode = CardDataValidator.OfficialPackCode)
    {
        ArgumentNullException.ThrowIfNull(upstreamJson);
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                Encoding.UTF8.GetBytes(upstreamJson),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
            return Normalize(document.RootElement, expectedPackCode);
        }
        catch (CardDataValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new CardDataValidationException("Upstream card JSON is malformed.", exception);
        }
    }

    public static IReadOnlyList<NormalizedCardDefinition> Normalize(
        ReadOnlySpan<byte> upstreamUtf8Json,
        string expectedPackCode = CardDataValidator.OfficialPackCode)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                upstreamUtf8Json.ToArray(),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
            return Normalize(document.RootElement, expectedPackCode);
        }
        catch (CardDataValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new CardDataValidationException("Upstream card JSON is malformed.", exception);
        }
    }

    private static ReadOnlyCollection<NormalizedCardDefinition> Normalize(
        JsonElement root,
        string expectedPackCode)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new CardDataValidationException("Upstream card JSON must contain a top-level array.");
        }

        if (string.IsNullOrWhiteSpace(expectedPackCode))
        {
            throw new CardDataValidationException("An expected upstream pack code is required.");
        }

        var cards = new List<NormalizedCardDefinition>();
        int index = 0;
        foreach (JsonElement element in root.EnumerateArray())
        {
            string path = $"cards[{index}]";
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new CardDataValidationException($"{path} must be an object.");
            }

            EnsureObjectShape(element, UpstreamProperties, path);
            string packCode = RequiredString(element, "pack_code", path);
            if (!string.Equals(packCode, expectedPackCode, StringComparison.Ordinal))
            {
                throw new CardDataValidationException(
                    $"{path}.pack_code '{packCode}' does not match expected '{expectedPackCode}'.");
            }

            string keywords = OptionalString(element, "keywords", path) ?? string.Empty;
            IReadOnlyList<string> subtypes = keywords.Length == 0
                ? Array.Empty<string>()
                : keywords.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            cards.Add(new NormalizedCardDefinition(
                RequiredString(element, "code", path),
                RequiredString(element, "title", path),
                RequiredString(element, "side_code", path),
                RequiredString(element, "faction_code", path),
                RequiredString(element, "type_code", path),
                subtypes,
                CardTextNormalizer.NormalizeSymbols(RequiredString(element, "text", path, allowEmpty: true)),
                packCode,
                RequiredInt(element, "quantity", path),
                RequiredInt(element, "deck_limit", path),
                RequiredBool(element, "uniqueness", path),
                OptionalNullableInt(element, "cost", path),
                OptionalNullableInt(element, "strength", path),
                OptionalNullableInt(element, "memory_cost", path),
                OptionalNullableInt(element, "trash_cost", path),
                OptionalNullableInt(element, "agenda_points", path),
                OptionalNullableInt(element, "advancement_cost", path),
                OptionalNullableInt(element, "faction_cost", path),
                OptionalNullableInt(element, "minimum_deck_size", path),
                OptionalNullableInt(element, "influence_limit", path),
                OptionalNullableInt(element, "base_link", path)));
            index++;
        }

        cards.Sort(CardDataValidator.CompareTitleThenCode);
        CardDataValidator.ValidateRecords(cards, expectedPackCode, OfficialCardPacks.Find(expectedPackCode) is not null);
        return new ReadOnlyCollection<NormalizedCardDefinition>(cards);
    }

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
                throw new CardDataValidationException($"Unknown upstream field '{path}.{property.Name}'.");
            }

            if (!seen.Add(property.Name))
            {
                throw new CardDataValidationException($"Duplicate upstream field '{path}.{property.Name}'.");
            }
        }
    }

    private static string RequiredString(
        JsonElement element,
        string name,
        string path,
        bool allowEmpty = false)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is null
            || (string.IsNullOrWhiteSpace(value.GetString())
                && !(allowEmpty && value.GetString()!.Length == 0)))
        {
            throw new CardDataValidationException(
                allowEmpty
                    ? $"{path}.{name} must be an explicitly present string."
                    : $"{path}.{name} must be a nonblank string.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new CardDataValidationException($"{path}.{name} must be a string or null.");
        }

        return value.GetString();
    }

    private static int RequiredInt(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new CardDataValidationException($"{path}.{name} must be an integer.");
        }

        return result;
    }

    private static int? OptionalNullableInt(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new CardDataValidationException($"{path}.{name} must be an integer or null.");
        }

        return result;
    }

    private static bool RequiredBool(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new CardDataValidationException($"{path}.{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static JsonElement RequiredProperty(JsonElement element, string name, string path) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new CardDataValidationException($"Missing required upstream field '{path}.{name}'.");
}

/// <summary>Strict JSON parser and deterministic UTF-8 writer for normalized cards/manifests.</summary>
public static class CardDataJson
{
    private static readonly IReadOnlySet<string> NormalizedCardProperties = new HashSet<string>(
        new[]
        {
            "code",
            "title",
            "side",
            "faction",
            "type",
            "subtypes",
            "text",
            "setCode",
            "quantity",
            "deckLimit",
            "uniqueness",
            "cost",
            "strength",
            "memoryCost",
            "trashCost",
            "agendaPoints",
            "advancementRequirement",
            "influenceCost",
            "minimumDeckSize",
            "influenceLimit",
            "baseLink",
        },
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> ManifestProperties = new HashSet<string>(
        new[]
        {
            "manifestSchemaVersion",
            "sourceUrl",
            "commitSha",
            "retrievalDate",
            "upstreamSchemaVersion",
            "normalizedSchemaVersion",
            "outputSha256",
            "sourceLicensePath",
            "sourceLicenseIdentity",
            "sourcePackPath",
            "packCode",
            "packName",
            "cardCount",
        },
        StringComparer.Ordinal);

    public static byte[] SerializeCards(IReadOnlyList<NormalizedCardDefinition> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cards.Count == 0)
        {
            throw new CardDataValidationException("Cannot serialize an empty normalized card set.");
        }

        string packCode = cards[0].SetCode;
        CardDataValidator.ValidateRecords(cards, packCode);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (NormalizedCardDefinition card in cards)
            {
                WriteCard(writer, card);
            }

            writer.WriteEndArray();
        }

        stream.WriteByte((byte)'\n');

        return stream.ToArray();
    }

    public static string SerializeCardsString(IReadOnlyList<NormalizedCardDefinition> cards) =>
        Encoding.UTF8.GetString(SerializeCards(cards));

    public static byte[] SerializeManifest(CardDataManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        CardDataValidator.ValidateManifest(manifest);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteManifest(writer, manifest);
        }

        stream.WriteByte((byte)'\n');

        return stream.ToArray();
    }

    public static string SerializeManifestString(CardDataManifest manifest) =>
        Encoding.UTF8.GetString(SerializeManifest(manifest));

    public static string ComputeSha256Hex(ReadOnlySpan<byte> utf8Json) =>
        Convert.ToHexString(SHA256.HashData(utf8Json)).ToLowerInvariant();

    public static IReadOnlyList<NormalizedCardDefinition> ParseCards(ReadOnlySpan<byte> utf8Json)
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
            return ParseCards(document.RootElement);
        }
        catch (CardDataValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new CardDataValidationException("Normalized card JSON is malformed.", exception);
        }
    }

    public static CardDataManifest ParseManifest(ReadOnlySpan<byte> utf8Json)
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
            return ParseManifest(document.RootElement);
        }
        catch (CardDataValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new CardDataValidationException("Card-data manifest JSON is malformed.", exception);
        }
    }

    private static ReadOnlyCollection<NormalizedCardDefinition> ParseCards(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new CardDataValidationException("Normalized card JSON must contain a top-level array.");
        }

        var cards = new List<NormalizedCardDefinition>();
        int index = 0;
        foreach (JsonElement element in root.EnumerateArray())
        {
            string path = $"cards[{index}]";
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new CardDataValidationException($"{path} must be an object.");
            }

            EnsureObjectShape(element, NormalizedCardProperties, path);
            cards.Add(new NormalizedCardDefinition(
                RequiredString(element, "code", path),
                RequiredString(element, "title", path),
                RequiredString(element, "side", path),
                RequiredString(element, "faction", path),
                RequiredString(element, "type", path),
                RequiredStringArray(element, "subtypes", path),
                RequiredString(element, "text", path, allowEmpty: true),
                RequiredString(element, "setCode", path),
                RequiredInt(element, "quantity", path),
                RequiredInt(element, "deckLimit", path),
                RequiredBool(element, "uniqueness", path),
                RequiredNullableInt(element, "cost", path),
                RequiredNullableInt(element, "strength", path),
                RequiredNullableInt(element, "memoryCost", path),
                RequiredNullableInt(element, "trashCost", path),
                RequiredNullableInt(element, "agendaPoints", path),
                RequiredNullableInt(element, "advancementRequirement", path),
                RequiredNullableInt(element, "influenceCost", path),
                RequiredNullableInt(element, "minimumDeckSize", path),
                RequiredNullableInt(element, "influenceLimit", path),
                RequiredNullableInt(element, "baseLink", path)));
            index++;
        }

        return new ReadOnlyCollection<NormalizedCardDefinition>(cards);
    }

    private static CardDataManifest ParseManifest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new CardDataValidationException("Card-data manifest JSON must contain an object.");
        }

        EnsureObjectShape(root, ManifestProperties, "manifest");
        return new CardDataManifest(
            RequiredInt(root, "manifestSchemaVersion", "manifest"),
            RequiredString(root, "sourceUrl", "manifest"),
            RequiredString(root, "commitSha", "manifest"),
            RequiredString(root, "retrievalDate", "manifest"),
            RequiredString(root, "upstreamSchemaVersion", "manifest"),
            RequiredInt(root, "normalizedSchemaVersion", "manifest"),
            RequiredString(root, "outputSha256", "manifest"),
            RequiredString(root, "sourceLicensePath", "manifest"),
            RequiredString(root, "sourceLicenseIdentity", "manifest"),
            RequiredString(root, "sourcePackPath", "manifest"),
            RequiredString(root, "packCode", "manifest"),
            RequiredString(root, "packName", "manifest"),
            RequiredInt(root, "cardCount", "manifest"));
    }

    private static void WriteCard(Utf8JsonWriter writer, NormalizedCardDefinition card)
    {
        writer.WriteStartObject();
        writer.WriteString("code", card.Code);
        writer.WriteString("title", card.Title);
        writer.WriteString("side", card.Side);
        writer.WriteString("faction", card.Faction);
        writer.WriteString("type", card.Type);
        writer.WritePropertyName("subtypes");
        writer.WriteStartArray();
        foreach (string subtype in card.Subtypes)
        {
            writer.WriteStringValue(subtype);
        }

        writer.WriteEndArray();
        writer.WriteString("text", card.Text);
        writer.WriteString("setCode", card.SetCode);
        writer.WriteNumber("quantity", card.Quantity);
        writer.WriteNumber("deckLimit", card.DeckLimit);
        writer.WriteBoolean("uniqueness", card.Uniqueness);
        WriteNullableInt(writer, "cost", card.Cost);
        WriteNullableInt(writer, "strength", card.Strength);
        WriteNullableInt(writer, "memoryCost", card.MemoryCost);
        WriteNullableInt(writer, "trashCost", card.TrashCost);
        WriteNullableInt(writer, "agendaPoints", card.AgendaPoints);
        WriteNullableInt(writer, "advancementRequirement", card.AdvancementRequirement);
        WriteNullableInt(writer, "influenceCost", card.InfluenceCost);
        WriteNullableInt(writer, "minimumDeckSize", card.MinimumDeckSize);
        WriteNullableInt(writer, "influenceLimit", card.InfluenceLimit);
        WriteNullableInt(writer, "baseLink", card.BaseLink);
        writer.WriteEndObject();
    }

    private static void WriteManifest(Utf8JsonWriter writer, CardDataManifest manifest)
    {
        writer.WriteStartObject();
        writer.WriteNumber("manifestSchemaVersion", manifest.ManifestSchemaVersion);
        writer.WriteString("sourceUrl", manifest.SourceUrl);
        writer.WriteString("commitSha", manifest.CommitSha);
        writer.WriteString("retrievalDate", manifest.RetrievalDate);
        writer.WriteString("upstreamSchemaVersion", manifest.UpstreamSchemaVersion);
        writer.WriteNumber("normalizedSchemaVersion", manifest.NormalizedSchemaVersion);
        writer.WriteString("outputSha256", manifest.OutputSha256);
        writer.WriteString("sourceLicensePath", manifest.SourceLicensePath);
        writer.WriteString("sourceLicenseIdentity", manifest.SourceLicenseIdentity);
        writer.WriteString("sourcePackPath", manifest.SourcePackPath);
        writer.WriteString("packCode", manifest.PackCode);
        writer.WriteString("packName", manifest.PackName);
        writer.WriteNumber("cardCount", manifest.CardCount);
        writer.WriteEndObject();
    }

    private static void WriteNullableInt(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is int number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

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
                throw new CardDataValidationException($"Unknown JSON field '{path}.{property.Name}'.");
            }

            if (!seen.Add(property.Name))
            {
                throw new CardDataValidationException($"Duplicate JSON field '{path}.{property.Name}'.");
            }
        }
    }

    private static string RequiredString(
        JsonElement element,
        string name,
        string path,
        bool allowEmpty = false)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is null
            || (string.IsNullOrWhiteSpace(value.GetString())
                && !(allowEmpty && value.GetString()!.Length == 0)))
        {
            throw new CardDataValidationException(
                allowEmpty
                    ? $"{path}.{name} must be an explicitly present string."
                    : $"{path}.{name} must be a nonblank string.");
        }

        return value.GetString()!;
    }

    private static int RequiredInt(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new CardDataValidationException($"{path}.{name} must be an integer.");
        }

        return result;
    }

    private static int? RequiredNullableInt(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new CardDataValidationException($"{path}.{name} must be an integer or null.");
        }

        return result;
    }

    private static ReadOnlyCollection<string> RequiredStringArray(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new CardDataValidationException($"{path}.{name} must be an array of strings.");
        }

        var values = new List<string>();
        int index = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new CardDataValidationException($"{path}.{name}[{index}] must be a nonblank string.");
            }

            values.Add(item.GetString()!);
            index++;
        }

        return new ReadOnlyCollection<string>(values);
    }

    private static bool RequiredBool(JsonElement element, string name, string path)
    {
        JsonElement value = RequiredProperty(element, name, path);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new CardDataValidationException($"{path}.{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    private static JsonElement RequiredProperty(JsonElement element, string name, string path) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new CardDataValidationException($"Missing required JSON field '{path}.{name}'.");
}

/// <summary>Offline file loader that authenticates normalized bytes before exposing them.</summary>
public static class CardDataLoader
{
    public const string DefaultRelativeDirectory = "data/vendor/system-gateway";

    public static CardDataSet Load(
        string cardsPath,
        string manifestPath,
        bool requireOfficialGateway = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        byte[] cardBytes = ReadFile(cardsPath, "normalized card data");
        byte[] manifestBytes = ReadFile(manifestPath, "card-data manifest");
        CardDataManifest manifest = CardDataJson.ParseManifest(manifestBytes);
        CardDataValidator.ValidateManifest(manifest, requireOfficialGateway);

        string actualSha = CardDataJson.ComputeSha256Hex(cardBytes);
        if (!string.Equals(actualSha, manifest.OutputSha256, StringComparison.Ordinal))
        {
            throw new CardDataValidationException(
                $"Normalized card checksum mismatch: manifest has '{manifest.OutputSha256}', actual bytes have '{actualSha}'.");
        }

        IReadOnlyList<NormalizedCardDefinition> cards = CardDataJson.ParseCards(cardBytes);
        CardDataValidator.Validate(cards, manifest, requireOfficialGateway);
        return new CardDataSet(cards, manifest);
    }

    public static CardDataSet LoadSystemGateway(string? rootDirectory = null) =>
        LoadOfficialPack(OfficialCardPacks.SystemGateway, rootDirectory);

    /// <summary>
    /// Loads and authenticates every official pack from its local-only
    /// directory, then combines them into one pool.
    /// </summary>
    public static CardDataSet LoadOfficialPool(string? rootDirectory = null) =>
        CardDataSet.Combine(OfficialCardPacks.All
            .Select(pack => LoadOfficialPack(pack, rootDirectory))
            .ToArray());

    /// <summary>Loads and authenticates one official pack from its local-only directory.</summary>
    public static CardDataSet LoadOfficialPack(OfficialCardPack pack, string? rootDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        string root = rootDirectory ?? Directory.GetCurrentDirectory();
        string directory = Path.Combine(root, pack.RelativeDirectory);
        CardDataSet data = Load(
            Path.Combine(directory, "cards.json"),
            Path.Combine(directory, "manifest.json"),
            requireOfficialGateway: true);
        string copyrightPath = Path.Combine(directory, "SOURCE_COPYRIGHT.md");
        byte[] copyright = ReadFile(copyrightPath, "source copyright notice");
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(copyright);
        }
        catch (DecoderFallbackException exception)
        {
            throw new CardDataValidationException(
                $"Source copyright notice at '{copyrightPath}' is not valid UTF-8.",
                exception);
        }

        if (copyright.Length == 0)
        {
            throw new CardDataValidationException(
                $"Source copyright notice at '{copyrightPath}' must not be empty.");
        }

        if (!string.Equals(data.Manifest.PackCode, pack.PackCode, StringComparison.Ordinal))
        {
            throw new CardDataValidationException(
                $"The snapshot at '{directory}' is pack '{data.Manifest.PackCode}', not '{pack.PackCode}'.");
        }

        return data;
    }

    public static void ValidateSystemGateway(string? rootDirectory = null) =>
        _ = LoadSystemGateway(rootDirectory);

    private static byte[] ReadFile(string path, string description)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CardDataValidationException($"Unable to read {description} at '{path}'.", exception);
        }
    }
}
