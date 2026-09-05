using System.Security.Cryptography;
using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Explicit developer-only reduced decks. Gateway values come from the generated
/// payload; the two no-ability identities are synthetic and never official presets.
/// </summary>
public sealed class DeveloperMatchCards
{
    public const string BehaviorVersion = "developer-match-v1";
    public const string CorpIdentityCode = "developer:corp";
    public const string RunnerIdentityCode = "developer:runner";
    private const string Pack = "developer-match";
    private static readonly string[] Codes = ["30037", "30072", "30005", "30006", "30029"];

    private DeveloperMatchCards(CardDataSet data, BehaviorCatalog behavior, RuntimeCardCatalog runtime)
    {
        Data = data;
        Behavior = behavior;
        Runtime = runtime;
    }

    public CardDataSet Data { get; }
    public BehaviorCatalog Behavior { get; }
    public RuntimeCardCatalog Runtime { get; }

    public static DeveloperMatchCards Create(CardDataSet? source = null)
    {
        source ??= CardDataResource.LoadEmbedded();
        CardMetadataCatalog metadata = new(source.Cards);
        var definitions = Codes.Select(code => Repack(metadata[code])).ToList();
        definitions.Add(Identity(CorpIdentityCode, "Developer Corp (no ability)", "corp"));
        definitions.Add(Identity(RunnerIdentityCode, "Developer Runner (no ability)", "runner"));
        definitions = definitions.OrderBy(card => card.Title, StringComparer.Ordinal).ThenBy(card => card.Code, StringComparer.Ordinal).ToList();
        string hash = Convert.ToHexString(SHA256.HashData(CardDataJson.SerializeCards(definitions))).ToLowerInvariant();
        CardDataManifest manifest = source.Manifest with
        {
            CommitSha = hash[..40],
            OutputSha256 = hash,
            CardCount = definitions.Count,
            PackCode = Pack,
            PackName = "Developer reduced full-match fixture",
            SourcePackPath = "data/m4/developer",
        };
        CardDataValidator.Validate(definitions, manifest, requireOfficialGateway: false);
        CardDataSet data = new(definitions, manifest);
        BehaviorCatalog slice = BehaviorCatalog.CreateSlice(GatewayBehaviorCatalog.Create(), Codes.Select(code => new CardDefinitionId(code)));
        BehaviorCatalog behavior = new BehaviorCatalogBuilder(BehaviorVersion, slice)
            .Add(new CardBehaviorDefinition(new CardDefinitionId(CorpIdentityCode), PlayerSide.Corp))
            .Add(new CardBehaviorDefinition(new CardDefinitionId(RunnerIdentityCode), PlayerSide.Runner)).Build();
        return new DeveloperMatchCards(data, behavior, GatewayRuntimeCardCatalog.Create(data));
    }

    public DeckRequest Deck(PlayerSide side)
    {
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }
        using Stream stream = typeof(DeveloperMatchCards).Assembly.GetManifestResourceStream("Netrunner.Cards.Data.m4.developer.decks.json")
            ?? throw new InvalidOperationException("Developer deck fixture is missing.");
        using JsonDocument document = JsonDocument.Parse(stream);
        DeckCardEntry[] entries = document.RootElement.GetProperty(side == PlayerSide.Corp ? "corp" : "runner")
            .EnumerateArray().Select(entry => new DeckCardEntry(entry.GetProperty("code").GetString()!, entry.GetProperty("quantity").GetInt32())).ToArray();
        DeckRequest deck = new(DeckRequest.CurrentSchemaVersion, side,
            side == PlayerSide.Corp ? CorpIdentityCode : RunnerIdentityCode,
            entries,
            DeckValidationProfile.DeveloperReduced, cardDataVersion: Data.Manifest.CommitSha);
        DeckValidator.ValidateWithBehaviorCatalog(deck, Data, Behavior).ThrowIfInvalid();
        return deck;
    }

    private static NormalizedCardDefinition Identity(string code, string title, string side) =>
        new(code, title, side, side == "corp" ? "neutral-corp" : "neutral-runner", "identity", [], "", Pack,
            1, 1, false, null, null, null, null, null, null, null, 5, 15, side == "runner" ? 0 : null);

    private static NormalizedCardDefinition Repack(NormalizedCardDefinition card) =>
        new(card.Code, card.Title, card.Side, card.Faction, card.Type, card.Subtypes, card.Text, Pack,
            card.Quantity, card.DeckLimit, card.Uniqueness, card.Cost, card.Strength, card.MemoryCost,
            card.TrashCost, card.AgendaPoints, card.AdvancementRequirement, card.InfluenceCost,
            card.MinimumDeckSize, card.InfluenceLimit, card.BaseLink);
}
