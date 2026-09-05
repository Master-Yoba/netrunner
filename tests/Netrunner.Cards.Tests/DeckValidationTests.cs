using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class DeckValidationTests
{
    [Fact]
    public void OfficialPresetsHavePublishedCountsAndCompleteBehavior()
    {
        OfficialPresetCatalog catalog = OfficialPresetCatalog.Embedded();

        catalog.Validate();
        Assert.Equal(CardDataValidator.OfficialCommitSha, catalog.Provenance.MetadataCommitSha);
        Assert.Equal(4, catalog.Presets.Count);
        Assert.Equal(30, catalog.Get("system-gateway-basic-runner").CardCount);
        Assert.Equal(40, catalog.Get("system-gateway-expanded-runner").CardCount);
        Assert.Equal(34, catalog.Get("system-gateway-basic-corp").CardCount);
        Assert.Equal(44, catalog.Get("system-gateway-expanded-corp").CardCount);
        Assert.All(catalog.Presets, preset => Assert.True(preset.Playable));
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        foreach (OfficialPreset preset in catalog.Presets)
        {
            DeckValidationResult result = DeckValidator.ValidateWithBehaviorCatalog(
                preset.ToDeckRequest(cardDataVersion: "fixture"), TestMetadata(), behavior, catalog);
            Assert.True(result.IsValid, string.Join("\n", result.Diagnostics));
        }
    }

    [Fact]
    public void PresetsContainOnlyCodesAndQuantities()
    {
        string json = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "data", "m4", "presets.json"));

        Assert.DoesNotContain("\"text\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"title\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("<symbol:", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactOfficialTutorialPresetsAreAccepted()
    {
        CardMetadataCatalog metadata = TestMetadata();
        OfficialPresetCatalog presets = OfficialPresetCatalog.Embedded();

        foreach (OfficialPreset preset in presets.Presets)
        {
            DeckValidationResult result = DeckValidator.Validate(
                preset.ToDeckRequest(cardDataVersion: "fixture"),
                metadata,
                presets);

            Assert.True(result.IsValid, string.Join("\n", result.Diagnostics));
            Assert.True(result.ExactTutorialPreset);
        }
    }

    [Fact]
    public void TutorialIdentityRejectsAnyNonExactDeck()
    {
        OfficialPreset starter = OfficialPresetCatalog.Embedded().Get("system-gateway-basic-runner");
        var altered = starter.Cards
            .Select(card => card.Code == "30030" ? new DeckCardEntry(card.Code, card.Quantity - 1) : card)
            .ToArray();
        DeckRequest request = new(
            DeckRequest.CurrentSchemaVersion,
            PlayerSide.Runner,
            "30076",
            altered);

        DeckValidationResult result = DeckValidator.Validate(request, TestMetadata());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "tutorial-preset");
    }

    [Fact]
    public void RejectsEveryDeckLegalityClass()
    {
        CardMetadataCatalog metadata = TestMetadata();

        AssertCode(
            new DeckRequest(99, PlayerSide.Runner, "runner-id", []),
            metadata,
            "schema-version");
        AssertCode(
            new DeckRequest(1, (PlayerSide)99, "runner-id", []),
            metadata,
            "side");
        AssertCode(
            new DeckRequest(1, PlayerSide.Runner, "missing", []),
            metadata,
            "identity-unknown");
        AssertCode(
            new DeckRequest(1, PlayerSide.Runner, "corp-id", []),
            metadata,
            "identity-side");
        AssertCode(
            new DeckRequest(1, PlayerSide.Runner, "runner-card", []),
            metadata,
            "identity-type");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("missing", 1)]),
            metadata,
            "card-unknown");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("corp-card", 1)]),
            metadata,
            "card-side");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("30076", 1)]),
            metadata,
            "identity-in-deck");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("runner-card", 0)]),
            metadata,
            "quantity-positive");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("runner-card", 2), new("runner-card", 2)]),
            metadata,
            "duplicate-entry");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("runner-card", 4)]),
            metadata,
            "copy-limit");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("limited", 2)]),
            metadata,
            "deck-limit");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("out-of-faction", 3)]),
            metadata,
            "influence-limit");
        AssertCode(
            Request(PlayerSide.Corp, "corp-id", [new("corp-out-of-faction", 3)]),
            metadata,
            "influence-limit");
        AssertCode(
            Request(PlayerSide.Corp, "corp-id", [new("corp-out-of-faction", 3)]),
            metadata,
            "influence-limit");
        AssertCode(
            Request(PlayerSide.Runner, "runner-id", [new("runner-card", 1)]),
            metadata,
            "minimum-deck-size");
        AssertCode(
            Request(PlayerSide.Corp, "corp-id", [new("filler", 40)]),
            metadata,
            "agenda-range");
    }

    [Fact]
    public void ReducedProfileWaivesOnlyMinimumAndCorpAgendaRange()
    {
        CardMetadataCatalog metadata = TestMetadata();
        DeckRequest runner = new(
            DeckRequest.CurrentSchemaVersion,
            PlayerSide.Runner,
            "runner-id",
            [new DeckCardEntry("runner-card", 1)],
            DeckValidationProfile.DeveloperReduced);
        DeckRequest corp = new(
            DeckRequest.CurrentSchemaVersion,
            PlayerSide.Corp,
            "corp-id",
            [new DeckCardEntry("filler", 1)],
            DeckValidationProfile.DeveloperReduced);

        Assert.True(DeckValidator.Validate(runner, metadata).IsValid);
        Assert.True(DeckValidator.Validate(corp, metadata).IsValid);

        DeckValidationResult stillIllegal = DeckValidator.Validate(
            new DeckRequest(
                DeckRequest.CurrentSchemaVersion,
                PlayerSide.Runner,
                "runner-id",
                [new DeckCardEntry("missing", 1), new DeckCardEntry("runner-card", 4)],
                DeckValidationProfile.DeveloperReduced),
            metadata);
        Assert.Contains(stillIllegal.Diagnostics, diagnostic => diagnostic.Code == "card-unknown");
        Assert.Contains(stillIllegal.Diagnostics, diagnostic => diagnostic.Code == "copy-limit");
    }

    [Fact]
    public void ReducedProfileNeverWaivesMissingImplementedBehaviorWhenCatalogIsSupplied()
    {
        BehaviorCatalog emptyBehavior = new BehaviorCatalogBuilder("test").Build();
        DeckRequest request = new(
            DeckRequest.CurrentSchemaVersion,
            PlayerSide.Runner,
            "runner-id",
            [new DeckCardEntry("runner-card", 1)],
            DeckValidationProfile.DeveloperReduced);

        DeckValidationResult result = DeckValidator.ValidateWithBehaviorCatalog(
            request,
            TestMetadata(),
            emptyBehavior);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "implemented-behavior");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "implemented-behavior" && diagnostic.Path == "identityCode");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "minimum-deck-size");
    }

    [Fact]
    public void ImplementedBehaviorAcceptsTypedAndGenericCatalogFamilies()
    {
        BehaviorCatalog behavior = new BehaviorCatalogBuilder("test")
            .Add(new CardBehaviorDefinition(new CardDefinitionId("runner-id"), PlayerSide.Runner))
            .AddAsset(new AssetBehaviorSpec(new CardDefinitionId("runner-card"), trashCost: 0))
            .Build();
        DeckRequest request = new(
            DeckRequest.CurrentSchemaVersion,
            PlayerSide.Runner,
            "runner-id",
            [new DeckCardEntry("runner-card", 1)],
            DeckValidationProfile.DeveloperReduced);

        DeckValidationResult result = DeckValidator.ValidateWithBehaviorCatalog(
            request,
            TestMetadata(),
            behavior);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "implemented-behavior");
    }

    [Fact]
    public void UniquenessAndConsoleAreNotDeckConstructionLimits()
    {
        var cards = new List<DeckCardEntry>
        {
            new("unique", 2),
            new("console", 2),
        };
        cards.AddRange(
            Enumerable.Range(0, 12)
                .Select(index => new DeckCardEntry($"runner-filler-{index:D2}", 3)));
        DeckRequest request = Request(PlayerSide.Runner, "runner-id", cards);

        DeckValidationResult result = DeckValidator.Validate(request, TestMetadata());

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is "uniqueness" or "console");
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is "copy-limit" or "deck-limit" or "minimum-deck-size");
    }

    private static void AssertCode(DeckRequest request, CardMetadataCatalog metadata, string code)
    {
        DeckValidationResult result = DeckValidator.Validate(request, metadata);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    private static DeckRequest Request(
        PlayerSide side,
        string identity,
        IReadOnlyList<DeckCardEntry> cards) =>
        new(DeckRequest.CurrentSchemaVersion, side, identity, cards);

    private static CardMetadataCatalog TestMetadata()
    {
        OfficialPresetCatalog presets = OfficialPresetCatalog.Embedded();
        var cards = new Dictionary<string, NormalizedCardDefinition>(StringComparer.Ordinal)
        {
            ["runner-id"] = Metadata("runner-id", "Runner Identity", "runner", "shaper", "identity", minimumDeckSize: 40, influenceLimit: 15),
            ["corp-id"] = Metadata("corp-id", "Corp Identity", "corp", "haas-bioroid", "identity", minimumDeckSize: 40, influenceLimit: 15),
            ["runner-card"] = Metadata("runner-card", "Runner Card", "runner", "shaper", "event"),
            ["corp-card"] = Metadata("corp-card", "Corp Card", "corp", "haas-bioroid", "operation"),
            ["out-of-faction"] = Metadata("out-of-faction", "Out Of Faction", "runner", "anarch", "event", influenceCost: 6),
            ["corp-out-of-faction"] = Metadata("corp-out-of-faction", "Corp Out Of Faction", "corp", "jinteki", "operation", influenceCost: 6),
            ["limited"] = Metadata("limited", "Limited Card", "runner", "shaper", "event", deckLimit: 1),
            ["unique"] = Metadata("unique", "Unique Card", "runner", "shaper", "resource", uniqueness: true),
            ["console"] = Metadata("console", "Console Card", "runner", "shaper", "hardware", subtypes: ["Console"]),
            ["runner-filler"] = Metadata("runner-filler", "Runner Filler Card", "runner", "shaper", "resource"),
            ["filler"] = Metadata("filler", "Filler Card", "corp", "haas-bioroid", "asset"),
        };

        for (int index = 0; index < 12; index++)
        {
            string code = $"runner-filler-{index:D2}";
            cards.Add(code, Metadata(code, $"Runner Filler Card {index:D2}", "runner", "shaper", "resource"));
        }

        foreach (OfficialPreset preset in presets.Presets)
        {
            foreach (DeckCardEntry entry in preset.Cards)
            {
                if (cards.ContainsKey(entry.Code))
                {
                    continue;
                }

                bool corp = preset.Side == PlayerSide.Corp;
                bool agenda = corp && entry.Code is "30067" or "30068" or "30069" or "30070";
                cards.Add(
                    entry.Code,
                    Metadata(
                        entry.Code,
                        $"Preset Card {entry.Code}",
                        corp ? "corp" : "runner",
                        corp ? "haas-bioroid" : "shaper",
                        agenda ? "agenda" : (corp ? "asset" : "event"),
                        agendaPoints: agenda ? 2 : null,
                        advancementRequirement: agenda ? 4 : null));
            }
        }

        cards["30076"] = Metadata("30076", "Catalyst", "runner", "neutral-runner", "identity", minimumDeckSize: 30);
        cards["30077"] = Metadata("30077", "Syndicate", "corp", "neutral-corp", "identity", minimumDeckSize: 30);
        return new CardMetadataCatalog(cards.Values.OrderBy(card => card.Title, StringComparer.Ordinal));
    }

    private static NormalizedCardDefinition Metadata(
        string code,
        string title,
        string side,
        string faction,
        string type,
        int deckLimit = 3,
        bool uniqueness = false,
        int? agendaPoints = null,
        int? advancementRequirement = null,
        int? influenceCost = 0,
        int? minimumDeckSize = null,
        int? influenceLimit = null,
        IReadOnlyList<string>? subtypes = null) =>
        new(
            code,
            title,
            side,
            faction,
            type,
            subtypes ?? [],
            string.Empty,
            "fixture",
            3,
            deckLimit,
            uniqueness,
            null,
            null,
            null,
            null,
            agendaPoints,
            advancementRequirement,
            influenceCost,
            minimumDeckSize,
            influenceLimit,
            null);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Netrunner.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
