using System.Security.Cryptography;
using System.Text;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayRuntimeCardCatalogTests
{
    [Fact]
    public void CompilesEveryNormalizedDefinitionIntoTheExpectedRuntimeShape()
    {
        CardDataSet data = BuildData(AllRuntimeCards());
        RuntimeCardCatalog catalog = GatewayRuntimeCardCatalog.Create(data);

        Assert.Equal(data.Cards.Count, catalog.Specs.Count);
        Assert.Equal(data.Cards.Count, catalog.DefinitionIds.Count);
        Assert.Equal($"gateway-runtime-v1-{data.Manifest.CommitSha}", catalog.Version);
        Assert.Equal(
            catalog.DefinitionIds.OrderBy(id => id.Value, StringComparer.Ordinal),
            catalog.DefinitionIds);

        AssertSpec(catalog, "identity-corp", PlayerSide.Corp, RuntimeCardClass.Identity);
        AssertSpec(
            catalog,
            "agenda-corp",
            PlayerSide.Corp,
            RuntimeCardClass.Agenda,
            agendaPoints: 2,
            advancementRequirement: 3,
            subtypes: ["priority"]);
        AssertSpec(
            catalog,
            "asset-corp",
            PlayerSide.Corp,
            RuntimeCardClass.Asset,
            rezCost: 2,
            trashCost: 2);
        AssertSpec(
            catalog,
            "upgrade-corp",
            PlayerSide.Corp,
            RuntimeCardClass.Upgrade,
            rezCost: 1,
            trashCost: 1);
        AssertSpec(
            catalog,
            "ice-corp",
            PlayerSide.Corp,
            RuntimeCardClass.Ice,
            rezCost: 3);
        AssertSpec(
            catalog,
            "operation-corp",
            PlayerSide.Corp,
            RuntimeCardClass.Operation,
            playCost: 1);
        AssertSpec(
            catalog,
            "event-runner",
            PlayerSide.Runner,
            RuntimeCardClass.Event,
            playCost: 2);
        AssertSpec(
            catalog,
            "program-runner",
            PlayerSide.Runner,
            RuntimeCardClass.Program,
            installCost: 4,
            memoryUnits: 2);
        AssertSpec(
            catalog,
            "hardware-runner",
            PlayerSide.Runner,
            RuntimeCardClass.Hardware,
            installCost: 3,
            subtypes: ["console"]);
        AssertSpec(
            catalog,
            "resource-runner",
            PlayerSide.Runner,
            RuntimeCardClass.Resource,
            installCost: 0);

        Assert.False(catalog.TryGet(new CardDefinitionId("unknown"), out _));
    }

    [Fact]
    public void UniquenessAndConsoleKeysUseOnlyNormalizedMetadata()
    {
        CardDataSet data = BuildData(
        [
            Card(
                "unique-a",
                "Same Normalized Name",
                "runner",
                "shaper",
                "hardware",
                subtypes: ["console"],
                uniqueness: true,
                cost: 3),
            Card(
                "unique-b",
                "Same Normalized Name",
                "runner",
                "shaper",
                "hardware",
                subtypes: ["Console"],
                uniqueness: true,
                cost: 1),
            Card(
                "ordinary",
                "Ordinary Resource",
                "runner",
                "shaper",
                "resource",
                cost: 0),
        ]);

        RuntimeCardCatalog catalog = GatewayRuntimeCardCatalog.Create(data);
        CardRuntimeSpec first = catalog.GetSpec(new CardDefinitionId("unique-a"));
        CardRuntimeSpec second = catalog.GetSpec(new CardDefinitionId("unique-b"));
        CardRuntimeSpec ordinary = catalog.GetSpec(new CardDefinitionId("ordinary"));
        string expectedTitleKey =
            "unique-title-sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("Same Normalized Name")))
                .ToLowerInvariant();

        Assert.True(first.IsUnique);
        Assert.True(second.IsUnique);
        Assert.Equal(expectedTitleKey, first.UniquenessKey);
        Assert.Equal(expectedTitleKey, second.UniquenessKey);
        Assert.True(first.IsConsole);
        Assert.True(second.IsConsole);
        Assert.Equal("runner-console", first.ConsoleKey);
        Assert.Equal("runner-console", second.ConsoleKey);
        Assert.False(ordinary.IsUnique);
        Assert.False(ordinary.IsConsole);
        Assert.Null(ordinary.UniquenessKey);
        Assert.Null(ordinary.ConsoleKey);
    }

    [Fact]
    public void ReorderedEquivalentSnapshotsHaveTheSameVersionAndFingerprint()
    {
        NormalizedCardDefinition[] cards = AllRuntimeCards();
        RuntimeCardCatalog first = GatewayRuntimeCardCatalog.Create(BuildData(cards));
        RuntimeCardCatalog second = GatewayRuntimeCardCatalog.Create(BuildData(cards.Reverse()));

        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.DefinitionIds, second.DefinitionIds);
        foreach (CardDefinitionId definitionId in first.DefinitionIds)
        {
            CardRuntimeSpec left = first.GetSpec(definitionId);
            CardRuntimeSpec right = second.GetSpec(definitionId);
            Assert.Equal(left.DefinitionId, right.DefinitionId);
            Assert.Equal(left.Side, right.Side);
            Assert.Equal(left.CardClass, right.CardClass);
            Assert.Equal(left.PlayCost, right.PlayCost);
            Assert.Equal(left.InstallCost, right.InstallCost);
            Assert.Equal(left.RezCost, right.RezCost);
            Assert.Equal(left.MemoryUnits, right.MemoryUnits);
            Assert.Equal(left.TrashCost, right.TrashCost);
            Assert.Equal(left.AgendaPoints, right.AgendaPoints);
            Assert.Equal(left.AdvancementRequirement, right.AdvancementRequirement);
            Assert.Equal(left.Subtypes.ToArray(), right.Subtypes.ToArray());
            Assert.Equal(left.IsUnique, right.IsUnique);
            Assert.Equal(left.IsConsole, right.IsConsole);
            Assert.Equal(left.UniquenessKey, right.UniquenessKey);
            Assert.Equal(left.ConsoleKey, right.ConsoleKey);
        }
    }

    [Fact]
    public void SubtypesUseStrictMetadataOnlyKeyNormalization()
    {
        CardDataSet data = BuildData(
        [
            Card(
                "code-gate",
                "Code Gate",
                "corp",
                "neutral-corp",
                "ice",
                subtypes: ["Code Gate"],
                cost: 1),
            Card(
                "gray-ops",
                "Gray Ops",
                "corp",
                "neutral-corp",
                "ice",
                subtypes: ["Gray  Ops"],
                cost: 1),
            Card(
                "g-mod",
                "G-mod",
                "runner",
                "neutral-runner",
                "program",
                subtypes: ["G-mod"],
                cost: 1,
                memoryCost: 1),
            Card(
                "ai",
                "AI",
                "runner",
                "neutral-runner",
                "hardware",
                subtypes: ["AI"],
                cost: 1),
        ]);

        RuntimeCardCatalog catalog = GatewayRuntimeCardCatalog.Create(data);

        Assert.Equal(["code-gate"], catalog.GetSpec(new CardDefinitionId("code-gate")).Subtypes);
        Assert.Equal(["gray-ops"], catalog.GetSpec(new CardDefinitionId("gray-ops")).Subtypes);
        Assert.Equal(["g-mod"], catalog.GetSpec(new CardDefinitionId("g-mod")).Subtypes);
        Assert.Equal(["ai"], catalog.GetSpec(new CardDefinitionId("ai")).Subtypes);
    }

    [Fact]
    public void InvalidSubtypeCharactersAndNormalizationCollisionsNameTheCardCode()
    {
        AssertCardFailure(
            BuildData(
            [Card(
                "bad-subtype",
                "Bad Subtype",
                "corp",
                "neutral-corp",
                "ice",
                subtypes: ["Code_Gate"],
                cost: 1)]),
            "bad-subtype");
        AssertCardFailure(
            BuildData(
            [Card(
                "collision-subtype",
                "Collision Subtype",
                "corp",
                "neutral-corp",
                "ice",
                subtypes: ["Code Gate", "code-gate"],
                cost: 1)]),
            "collision-subtype");
    }

    [Fact]
    public void DuplicateNormalizedDefinitionsAreRejectedBeforeComposition()
    {
        CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
            BuildData(
            [
                Card("duplicate-code", "First", "runner", "shaper", "event", cost: 1),
                Card("duplicate-code", "Second", "runner", "shaper", "event", cost: 1),
            ]));

        Assert.Contains("duplicate-code", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedCatalogPreservesTheSelectedSyntheticOrOfficialMode()
    {
        CardDataSet data = CardDataResource.LoadEmbedded();
        RuntimeCardCatalog catalog = GatewayRuntimeCardCatalog.CreateEmbedded();

        Assert.Equal(data.Cards.Count, catalog.DefinitionIds.Count);
        Assert.Equal($"gateway-runtime-v1-{data.Manifest.CommitSha}", catalog.Version);

        if (string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal))
        {
            Assert.Equal(CardDataValidator.OfficialCardCount, catalog.DefinitionIds.Count);
            Assert.Equal(CardDataValidator.OfficialCommitSha, data.Manifest.CommitSha);
        }
        else
        {
            Assert.Equal(CardDataResource.SyntheticMode, CardDataResource.EmbeddedMode);
        }
    }

    [Fact]
    public void AnXPlayCostCompilesToZeroAndTheBehaviorChargesX()
    {
        // Elevation's IP Enforcement prints a cost of X, null upstream.
        RuntimeCardCatalog catalog = GatewayRuntimeCardCatalog.Create(BuildData(
            [Card("x-cost", "X Cost", "corp", "nbn", "operation", trashCost: 5)]));

        CardRuntimeSpec spec = catalog.GetSpec(new CardDefinitionId("x-cost"));
        Assert.Equal(0, spec.PlayCost);
        Assert.Equal(5, spec.TrashCost);
    }

    [Fact]
    public void MissingOrForbiddenMappedFieldsFailWithTheCardCode()
    {
        AssertCardFailure(
            BuildData(
            [Card("missing-cost", "Missing Cost", "runner", "shaper", "resource")]),
            "missing-cost");
        AssertCardFailure(
            BuildData(
            [Card(
                "missing-memory",
                "Missing Memory",
                "runner",
                "shaper",
                "program",
                cost: 1)]),
            "missing-memory");
        AssertCardFailure(
            BuildData(
            [Card(
                "missing-trash",
                "Missing Trash",
                "corp",
                "haas-bioroid",
                "asset",
                cost: 1)]),
            "missing-trash");
        AssertCardFailure(
            BuildData(
            [Card(
                "identity-cost",
                "Identity Cost",
                "corp",
                "haas-bioroid",
                "identity",
                cost: 1,
                minimumDeckSize: 40)]),
            "identity-cost");
        AssertCardFailure(
            BuildData(
            [Card(
                "agenda-cost",
                "Agenda Cost",
                "corp",
                "haas-bioroid",
                "agenda",
                cost: 1,
                agendaPoints: 2,
                advancementRequirement: 3)]),
            "agenda-cost");
        CardDataValidationException negativeCost = Assert.Throws<CardDataValidationException>(() =>
            BuildData(
            [Card(
                "negative-cost",
                "Negative Cost",
                "corp",
                "haas-bioroid",
                "operation",
                cost: -1)]));
        Assert.Contains("negative-cost", negativeCost.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OfficialLabelCannotBypassPinnedAuthentication()
    {
        NormalizedCardDefinition card = Card(
            "official-label",
            "Official Label",
            "runner",
            "shaper",
            "event",
            cost: 1,
            setCode: CardDataValidator.OfficialPackCode);
        CardDataSet data = BuildData(
            [card],
            packCode: CardDataValidator.OfficialPackCode,
            commitSha: new string('a', 40));

        CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
            GatewayRuntimeCardCatalog.Create(data));
        Assert.Contains("pinned System Gateway", exception.Message, StringComparison.Ordinal);
    }

    private static void AssertCardFailure(CardDataSet data, string code)
    {
        CardDataValidationException exception = Assert.Throws<CardDataValidationException>(() =>
            GatewayRuntimeCardCatalog.Create(data));
        Assert.Contains(code, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertSpec(
        RuntimeCardCatalog catalog,
        string code,
        PlayerSide side,
        RuntimeCardClass cardClass,
        int? playCost = null,
        int? installCost = null,
        int? rezCost = null,
        int memoryUnits = 0,
        int? trashCost = null,
        int agendaPoints = 0,
        int advancementRequirement = 0,
        IReadOnlyList<string>? subtypes = null)
    {
        Assert.True(catalog.TryGet(new CardDefinitionId(code), out CardRuntimeSpec? spec));
        Assert.Equal(side, spec.Side);
        Assert.Equal(cardClass, spec.CardClass);
        Assert.Equal(playCost, spec.PlayCost);
        Assert.Equal(installCost, spec.InstallCost);
        Assert.Equal(rezCost, spec.RezCost);
        Assert.Equal(memoryUnits, spec.MemoryUnits);
        Assert.Equal(trashCost, spec.TrashCost);
        Assert.Equal(agendaPoints, spec.AgendaPoints);
        Assert.Equal(advancementRequirement, spec.AdvancementRequirement);
        if (subtypes is not null)
        {
            Assert.Equal(subtypes.OrderBy(value => value, StringComparer.Ordinal), spec.Subtypes);
        }
    }

    private static CardDataSet BuildData(
        IEnumerable<NormalizedCardDefinition> cards,
        string packCode = "fixture",
        string? commitSha = null)
    {
        NormalizedCardDefinition[] ordered = cards
            .OrderBy(card => card.Title, StringComparer.Ordinal)
            .ThenBy(card => card.Code, StringComparer.Ordinal)
            .ToArray();
        byte[] cardBytes = CardDataJson.SerializeCards(ordered);
        CardDataManifest manifest = new(
            CardDataValidator.CurrentManifestSchemaVersion,
            "https://example.invalid/gateway-runtime-tests",
            commitSha ?? new string('b', 40),
            "2026-09-02",
            "fixture-v1",
            CardDataValidator.CurrentNormalizedSchemaVersion,
            CardDataJson.ComputeSha256Hex(cardBytes),
            "fixtures/NOTICE.txt",
            "NOASSERTION",
            "fixtures/cards.json",
            packCode,
            "Gateway runtime fixture",
            ordered.Length);
        return CardDataResource.LoadBytes(
            cardBytes,
            CardDataJson.SerializeManifest(manifest),
            Encoding.UTF8.GetBytes("runtime fixture notice"));
    }

    private static NormalizedCardDefinition[] AllRuntimeCards() =>
    [
        Card(
            "identity-corp",
            "Identity Corp",
            "corp",
            "neutral-corp",
            "identity",
            minimumDeckSize: 40,
            influenceLimit: 15),
        Card(
            "agenda-corp",
            "Agenda Corp",
            "corp",
            "neutral-corp",
            "agenda",
            subtypes: ["Priority"],
            agendaPoints: 2,
            advancementRequirement: 3),
        Card(
            "asset-corp",
            "Asset Corp",
            "corp",
            "neutral-corp",
            "asset",
            cost: 2,
            trashCost: 2),
        Card(
            "upgrade-corp",
            "Upgrade Corp",
            "corp",
            "neutral-corp",
            "upgrade",
            cost: 1,
            trashCost: 1),
        Card(
            "ice-corp",
            "Ice Corp",
            "corp",
            "neutral-corp",
            "ice",
            cost: 3,
            strength: 4),
        Card(
            "operation-corp",
            "Operation Corp",
            "corp",
            "neutral-corp",
            "operation",
            cost: 1),
        Card(
            "event-runner",
            "Event Runner",
            "runner",
            "neutral-runner",
            "event",
            cost: 2),
        Card(
            "program-runner",
            "Program Runner",
            "runner",
            "neutral-runner",
            "program",
            cost: 4,
            memoryCost: 2),
        Card(
            "hardware-runner",
            "Hardware Runner",
            "runner",
            "neutral-runner",
            "hardware",
            subtypes: ["Console"],
            cost: 3),
        Card(
            "resource-runner",
            "Resource Runner",
            "runner",
            "neutral-runner",
            "resource",
            cost: 0),
    ];

    private static NormalizedCardDefinition Card(
        string code,
        string title,
        string side,
        string faction,
        string type,
        IReadOnlyList<string>? subtypes = null,
        bool uniqueness = false,
        int? cost = null,
        int? strength = null,
        int? memoryCost = null,
        int? trashCost = null,
        int? agendaPoints = null,
        int? advancementRequirement = null,
        int? minimumDeckSize = null,
        int? influenceLimit = null,
        string setCode = "fixture") =>
        new(
            code,
            title,
            side,
            faction,
            type,
            subtypes ?? [],
            string.Empty,
            setCode,
            quantity: type == "identity" ? 1 : 3,
            deckLimit: type == "identity" ? 1 : 3,
            uniqueness,
            cost,
            strength,
            memoryCost,
            trashCost,
            agendaPoints,
            advancementRequirement,
            influenceCost: type == "identity" ? null : 0,
            minimumDeckSize: type == "identity" ? minimumDeckSize ?? 40 : null,
            influenceLimit: type == "identity" ? influenceLimit : null,
            baseLink: side == "runner" && type == "identity" ? 0 : null);
}
