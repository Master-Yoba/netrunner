using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Card-owned binding proofs for the four Batch 3 C2 cards.  The assertions
/// intentionally inspect typed declarations rather than card text so a
/// title/code branch cannot accidentally satisfy the contract.
/// </summary>
public sealed class GatewayBatch3C2BehaviorSpecTests
{
    [Fact]
    public void CatalogComposesTheFinalBatch3VersionAndAllSevenDefinitions()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.Equal("gateway-m4-4.4", catalog.Version);
        Assert.Equal(39, catalog.AllDefinitionIds.Count);
        Assert.Equal(
            new[]
            {
                GatewayBatch3CardCodes.Leech,
                GatewayBatch3CardCodes.TreadLightly,
                GatewayBatch3CardCodes.DocklandsPass,
                GatewayBatch3CardCodes.DzmzOptimizer,
                GatewayBatch3CardCodes.Echelon,
                GatewayBatch3CardCodes.Unity,
                GatewayBatch3CardCodes.VerbalPlasticity,
            }.OrderBy(id => id.Value, StringComparer.Ordinal),
            new[]
            {
                GatewayBatch3CardCodes.Leech,
                GatewayBatch3CardCodes.TreadLightly,
                GatewayBatch3CardCodes.DocklandsPass,
                GatewayBatch3CardCodes.DzmzOptimizer,
                GatewayBatch3CardCodes.Echelon,
                GatewayBatch3CardCodes.Unity,
                GatewayBatch3CardCodes.VerbalPlasticity,
            }.Where(catalog.AllDefinitionIds.Contains)
                .OrderBy(id => id.Value, StringComparer.Ordinal));
    }

    [Fact]
    public void TreadLightlyDeclaresExactlyOneRunRezModifierAndRoundTrips()
    {
        RunEventBehaviorSpec spec = GatewayRunEventBehaviorSpecs.RunEventSpecs.Single(
            candidate => candidate.DefinitionId == GatewayBatch3CardCodes.TreadLightly);

        Assert.Equal(1, spec.Cost);
        Assert.Equal(0, spec.HostedCredits);
        BehaviorRunRezCostModifier modifier = Assert.Single(spec.RezCostModifiers!);
        Assert.Equal(3, modifier.Delta);
        Assert.Equal("tread-lightly-ice-rez-cost", modifier.ReasonKey);
        Assert.Equal(
            new[] { GatewayCardCodes.Overclock, GatewayBatch3CardCodes.TreadLightly },
            GatewayRunEventBehaviorSpecs.RunEventSpecs.Select(candidate => candidate.DefinitionId));

        string json = EngineJson.Serialize(spec);
        RunEventBehaviorSpec restored = EngineJson.Deserialize<RunEventBehaviorSpec>(json);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(spec.DefinitionId, restored.DefinitionId);
        Assert.Equal(spec.Cost, restored.Cost);
        Assert.Equal(spec.HostedCredits, restored.HostedCredits);
        Assert.Equal(spec.RezCostModifiers!.ToArray(), restored.RezCostModifiers!.ToArray());
    }

    [Fact]
    public void DzmzDocklandsAndVerbalUseTheirTypedFirstWindowPrimitives()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        CardBehaviorDefinition dzmz = Definition(catalog, GatewayBatch3CardCodes.DzmzOptimizer);
        BehaviorStaticModifier memory = Assert.Single(dzmz.StaticModifiers);
        Assert.Equal(BehaviorStaticModifierKind.MemoryUnits, memory.Kind);
        Assert.Equal(1, memory.Delta);
        BehaviorFirstProgramInstallCostModifier install = Assert.Single(
            dzmz.FirstProgramInstallCostModifiers!);
        Assert.Equal(-1, install.Delta);
        Assert.Equal("dzmz-first-program-install", install.ReasonKey);

        CardBehaviorDefinition docklands = Definition(catalog, GatewayBatch3CardCodes.DocklandsPass);
        BehaviorFirstHqBreachAccessModifier breach = Assert.Single(
            docklands.FirstHqBreachAccessModifiers!);
        Assert.Equal(1, breach.Delta);
        Assert.Equal("docklands-pass-first-hq-breach", breach.ReasonKey);

        CardBehaviorDefinition verbal = Definition(catalog, GatewayBatch3CardCodes.VerbalPlasticity);
        BehaviorFirstBasicDrawReplacement draw = Assert.Single(verbal.FirstBasicDrawReplacements!);
        Assert.Equal(2, draw.DrawCount);
        Assert.Equal("verbal-plasticity-first-basic-draw", draw.ReasonKey);

        foreach (CardDefinitionId id in new[]
        {
            GatewayBatch3CardCodes.DzmzOptimizer,
            GatewayBatch3CardCodes.DocklandsPass,
            GatewayBatch3CardCodes.VerbalPlasticity,
        })
        {
            CardBehaviorDefinition original = Definition(catalog, id);
            string json = EngineJson.Serialize(original);
            CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);
            Assert.Equal(json, EngineJson.Serialize(restored));
            Assert.Equal(catalog.GetDefinitionFingerprint(id), Fingerprint(restored));
        }
    }

    [Fact]
    public void GatewayCatalogFingerprintIsStableAndRuntimeMetadataMatchesPrintedCosts()
    {
        BehaviorCatalog first = GatewayBehaviorCatalog.Create();
        BehaviorCatalog second = GatewayBehaviorCatalog.Create();
        Assert.Equal(first.Fingerprint, second.Fingerprint);

        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        Assert.Equal(1, runtime.GetSpec(GatewayBatch3CardCodes.TreadLightly).PlayCost);
        Assert.Equal(RuntimeCardClass.Event, runtime.GetSpec(GatewayBatch3CardCodes.TreadLightly).CardClass);
        Assert.Equal(2, runtime.GetSpec(GatewayBatch3CardCodes.DzmzOptimizer).InstallCost);
        Assert.Equal(0, runtime.GetSpec(GatewayBatch3CardCodes.DzmzOptimizer).MemoryUnits);
        Assert.Equal(2, runtime.GetSpec(GatewayBatch3CardCodes.DocklandsPass).InstallCost);
        Assert.True(runtime.GetSpec(GatewayBatch3CardCodes.DocklandsPass).IsUnique);
        Assert.Equal(3, runtime.GetSpec(GatewayBatch3CardCodes.VerbalPlasticity).InstallCost);
        Assert.Equal(RuntimeCardClass.Resource, runtime.GetSpec(GatewayBatch3CardCodes.VerbalPlasticity).CardClass);
    }

    internal static RuntimeCardCatalog CreateRuntimeCatalog()
    {
        NormalizedCardDefinition[] cards =
        [
            Card("30005", "Buzzsaw", "runner", "anarch", "program", ["Icebreaker", "Decoder"], cost: 4, strength: 3, memoryCost: 1),
            Card("30008", "Leech", "runner", "anarch", "program", ["Virus"], cost: 1, memoryCost: 1),
            Card("30012", "Tread Lightly", "runner", "criminal", "event", ["Run"], cost: 1),
            Card("30013", "Docklands Pass", "runner", "criminal", "hardware", uniqueness: true, cost: 2),
            Card("30022", "DZMZ Optimizer", "runner", "shaper", "hardware", cost: 2),
            Card("30025", "Echelon", "runner", "shaper", "program", ["Icebreaker", "Killer"], cost: 3, strength: 0, memoryCost: 1),
            Card("30026", "Unity", "runner", "shaper", "program", ["Icebreaker", "Decoder"], cost: 3, strength: 1, memoryCost: 1),
            Card("30034", "Verbal Plasticity", "runner", "neutral-runner", "resource", ["Genetics"], uniqueness: true, cost: 3),
            Card("30072", "Palisade", "corp", "neutral-corp", "ice", ["Barrier"], cost: 3, strength: 2),
        ];
        NormalizedCardDefinition[] ordered = cards
            .OrderBy(card => card.Title, StringComparer.Ordinal)
            .ThenBy(card => card.Code, StringComparer.Ordinal)
            .ToArray();
        byte[] cardBytes = CardDataJson.SerializeCards(ordered);
        CardDataManifest manifest = new(
            CardDataValidator.CurrentManifestSchemaVersion,
            "https://example.invalid/gateway-batch3-c2-tests",
            new string('c', 40),
            "2026-09-04",
            "fixture-v1",
            CardDataValidator.CurrentNormalizedSchemaVersion,
            CardDataJson.ComputeSha256Hex(cardBytes),
            "fixtures/NOTICE.txt",
            "NOASSERTION",
            "fixtures/cards.json",
            "fixture",
            "Gateway Batch 3 C2 fixture",
            ordered.Length);
        CardDataSet data = CardDataResource.LoadBytes(
            cardBytes,
            CardDataJson.SerializeManifest(manifest),
            "gateway batch 3 c2 fixture notice"u8);
        return GatewayRuntimeCardCatalog.Create(data);
    }

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
        int? memoryCost = null) =>
        new(
            code,
            title,
            side,
            faction,
            type,
            subtypes ?? [],
            string.Empty,
            "fixture",
            quantity: 3,
            deckLimit: 3,
            uniqueness,
            cost,
            strength,
            memoryCost,
            trashCost: null,
            agendaPoints: null,
            advancementRequirement: null,
            influenceCost: 0,
            minimumDeckSize: null,
            influenceLimit: null,
            baseLink: null);

    private static CardBehaviorDefinition Definition(BehaviorCatalog catalog, CardDefinitionId id) =>
        catalog.GenericSpecs.Single(candidate => candidate.DefinitionId == id);

    private static string Fingerprint(CardBehaviorDefinition definition)
    {
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:definition-fingerprint")
            .Add(definition)
            .Build();
        return catalog.GetDefinitionFingerprint(definition.DefinitionId);
    }
}
