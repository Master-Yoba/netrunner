using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class M2ScenarioSetupTests
{
    private static string RepresentativeRunJson =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "M2Scenarios", "representative-run.json"));

    private static M2ScenarioDocument LoadDocument(string json)
    {
        M2ScenarioDocument document = EngineJson.Deserialize<M2ScenarioDocument>(json);
        return document;
    }

    private static string MutatedRoot(Action<Dictionary<string, JsonElement>> mutate)
    {
        using JsonDocument parsed = JsonDocument.Parse(RepresentativeRunJson);
        var root = new Dictionary<string, JsonElement>(
            parsed.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value));
        mutate(root);
        return JsonSerializer.Serialize(root);
    }

    private static JsonElement MutateCard(JsonElement cards, int index, Action<Dictionary<string, JsonElement>> mutate)
    {
        var items = cards.EnumerateArray().ToList();
        var item = new Dictionary<string, JsonElement>(
            items[index].EnumerateObject().ToDictionary(property => property.Name, property => property.Value));
        mutate(item);
        items[index] = JsonSerializer.SerializeToElement(item);
        return JsonSerializer.SerializeToElement(items);
    }

    private static BehaviorCatalog RebuildGatewayCatalog(
        string? excludedDefinitionId = null,
        IceBehaviorSpec? replacementIce = null,
        AssetBehaviorSpec? replacementAsset = null,
        bool includeGeneric = true)
    {
        BehaviorCatalog gateway = GatewayBehaviorCatalog.Create();
        var builder = new BehaviorCatalogBuilder(gateway.Version)
            .AddIce(gateway.IceSpecs.Where(spec => spec.DefinitionId.Value != excludedDefinitionId))
            .AddIcebreaker(gateway.IcebreakerSpecs.Where(spec => spec.DefinitionId.Value != excludedDefinitionId))
            .AddAsset(gateway.AssetSpecs.Where(spec => spec.DefinitionId.Value != excludedDefinitionId))
            .AddAgenda(gateway.AgendaSpecs.Where(spec => spec.DefinitionId.Value != excludedDefinitionId))
            .AddRunEvent(gateway.RunEventSpecs.Where(spec => spec.DefinitionId.Value != excludedDefinitionId));
        if (includeGeneric)
        {
            builder.Add(gateway.GenericSpecs.Where(spec => spec.DefinitionId.Value != excludedDefinitionId));
        }

        if (replacementIce is not null)
        {
            builder.AddIce(replacementIce);
        }

        if (replacementAsset is not null)
        {
            builder.AddAsset(replacementAsset);
        }

        return builder.Build();
    }

    [Fact]
    public void RepresentativeRunScenarioBuildsPinnedInitialState()
    {
        M2ScenarioDocument document = LoadDocument(RepresentativeRunJson);
        document.Validate();

        GameState state = M2ScenarioSetup.Build(document);

        Assert.Equal(4, state.Servers.Count);
        ServerState remote = Assert.Single(state.Servers, server => server.Kind == ServerKind.Remote);

        ZoneState iceZone = state.GetZone(remote.IceZoneId);
        ZoneState rootZone = state.GetZone(remote.RootZoneId);
        // Innermost-to-outermost order: the run traverses from the last
        // element, so Palisade is encountered first and Whitespace second.
        Assert.Equal(
            ["card:1", "card:2"],
            iceZone.CardIds.Select(id => id.Value).ToArray());
        Assert.Equal(
            ["card:3", "card:4"],
            rootZone.CardIds.Select(id => id.Value).ToArray());
        Assert.Equal("30074", state.GetCard(new CardInstanceId("card:1")).DefinitionId.Value);
        Assert.Equal("30072", state.GetCard(new CardInstanceId("card:2")).DefinitionId.Value);
        Assert.Equal("30067", state.GetCard(new CardInstanceId("card:3")).DefinitionId.Value);
        Assert.Equal("30037", state.GetCard(new CardInstanceId("card:4")).DefinitionId.Value);

        ZoneState rig = state.GetZone(ZoneKind.RunnerRig);
        Assert.Equal(["card:5", "card:6"], rig.CardIds.Select(id => id.Value).ToArray());
        Assert.All(rig.CardIds, id => Assert.True(state.GetCard(id).FaceUp));
        Assert.Equal("30006", state.GetCard(new CardInstanceId("card:5")).DefinitionId.Value);
        Assert.Equal("30005", state.GetCard(new CardInstanceId("card:6")).DefinitionId.Value);

        ZoneState grip = state.GetZone(ZoneKind.RunnerGrip);
        Assert.Equal(["card:7"], grip.CardIds.Select(id => id.Value).ToArray());
        Assert.False(state.GetCard(new CardInstanceId("card:7")).FaceUp);

        PlayerState corp = state.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState runner = state.Players.Single(player => player.Side == PlayerSide.Runner);
        Assert.Equal(10, corp.Credits);
        Assert.Equal(3, corp.Clicks);
        Assert.Equal(15, runner.Credits);
        Assert.Equal(4, runner.Clicks);

        Assert.Equal(PlayerSide.Runner, state.Turn.ActiveSide);
        Assert.Equal(TurnPhase.Runner, state.Turn.Phase);
        Assert.Equal(2, state.Turn.TurnNumber);
        Assert.Null(state.RunState);
        Assert.Null(state.PendingDecision);

        Assert.Equal(
            "4460bc239b386ec53259af1f5110f0a14db000d552a0e1efbe6950771f6d5215",
            DeterministicHash.ForState(state));
    }

    [Fact]
    public void UnrelatedInjectedBehaviorDoesNotChangeScenarioSliceOrStateHash()
    {
        M2ScenarioDocument document = LoadDocument(RepresentativeRunJson);
        BehaviorCatalog gateway = GatewayBehaviorCatalog.Create();
        Assert.Equal(GatewayBehaviorCatalog.Version, gateway.Version);
        BehaviorCatalog expanded = new BehaviorCatalogBuilder(gateway.Version, gateway)
            .AddAsset(new AssetBehaviorSpec(new CardDefinitionId("test:m2-unrelated"), trashCost: 1))
            .Build();

        BehaviorCatalog gatewaySlice = document.CreateBehaviorCatalog(gateway);
        BehaviorCatalog expandedSlice = document.CreateBehaviorCatalog(expanded);
        Assert.Equal(EngineCompatibility.GatewayBehaviorRegistryVersion, gatewaySlice.Version);
        Assert.Equal(gatewaySlice.Version, expandedSlice.Version);
        Assert.Equal(gatewaySlice.Fingerprint, expandedSlice.Fingerprint);
        Assert.Equal(
            DeterministicHash.ForState(M2ScenarioSetup.Build(document, gateway)),
            DeterministicHash.ForState(M2ScenarioSetup.Build(document, expanded)));
    }

    [Fact]
    public void GenericOverlayOnReferencedLegacyAssetDoesNotChangeScenarioSliceOrPinnedStateHash()
    {
        M2ScenarioDocument document = LoadDocument(RepresentativeRunJson);
        BehaviorCatalog legacy = RebuildGatewayCatalog(includeGeneric: false);
        BehaviorCatalog expanded = new BehaviorCatalogBuilder(legacy.Version, legacy)
            .Add(new CardBehaviorDefinition(
                GatewayCardCodes.NicoCampaign,
                PlayerSide.Corp,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]))
            .Build();

        BehaviorCatalog legacySlice = document.CreateBehaviorCatalog(legacy);
        BehaviorCatalog expandedSlice = document.CreateBehaviorCatalog(expanded);

        Assert.Equal(legacy.Version, expanded.Version);
        Assert.Equal(legacySlice.Version, expandedSlice.Version);
        Assert.Equal(document.BehaviorCatalogFingerprint, legacySlice.Fingerprint);
        Assert.Equal(legacySlice.Fingerprint, expandedSlice.Fingerprint);
        Assert.DoesNotContain(expandedSlice.GenericSpecs, static definition =>
            definition.DefinitionId == GatewayCardCodes.NicoCampaign);
        Assert.Equal(
            DeterministicHash.ForState(M2ScenarioSetup.Build(document, legacy)),
            DeterministicHash.ForState(M2ScenarioSetup.Build(document, expanded)));
    }

    [Fact]
    public void MissingInjectedBehaviorReportsTheReferencingCardPath()
    {
        M2ScenarioDocument document = LoadDocument(RepresentativeRunJson);
        BehaviorCatalog missing = RebuildGatewayCatalog(excludedDefinitionId: GatewayCardCodes.Whitespace.Value);

        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(
            () => document.Validate(missing));

        Assert.Contains("cards[0].definitionId", exception.Message);
        Assert.Contains("not registered in the behavior spec catalog", exception.Message);
    }

    [Fact]
    public void ChangedInjectedBehaviorStillFailsTheScenarioFingerprintPin()
    {
        M2ScenarioDocument document = LoadDocument(RepresentativeRunJson);
        IceBehaviorSpec original = Assert.Single(
            GatewayBehaviorCatalog.Create().IceSpecs,
            spec => spec.DefinitionId == GatewayCardCodes.Whitespace);
        BehaviorCatalog changed = RebuildGatewayCatalog(
            excludedDefinitionId: GatewayCardCodes.Whitespace.Value,
            replacementIce: new IceBehaviorSpec(
                original.DefinitionId,
                original.Strength + 1,
                original.IceType,
                original.RezCost,
                original.Subroutines,
                original.StrengthModifiers));

        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(
            () => document.Validate(changed));

        Assert.Contains("behaviorCatalogFingerprint:", exception.Message);
        Assert.Contains("scenario behavior catalog fingerprint", exception.Message);
    }

    [Fact]
    public void ChangedReferencedAssetBehaviorStillFailsTheScenarioFingerprintPin()
    {
        M2ScenarioDocument document = LoadDocument(RepresentativeRunJson);
        AssetBehaviorSpec original = Assert.Single(
            GatewayBehaviorCatalog.Create().AssetSpecs,
            spec => spec.DefinitionId == GatewayCardCodes.NicoCampaign);
        BehaviorCatalog changed = RebuildGatewayCatalog(
            excludedDefinitionId: GatewayCardCodes.NicoCampaign.Value,
            replacementAsset: new AssetBehaviorSpec(
                original.DefinitionId,
                trashCost: original.TrashCost + 1));

        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(
            () => document.Validate(changed));

        Assert.Contains("behaviorCatalogFingerprint:", exception.Message);
        Assert.Contains("scenario behavior catalog fingerprint", exception.Message);
    }

    [Fact]
    public void UnknownFieldIsRejectedWithExactPath()
    {
        string mutated = MutatedRoot(root => root["bogusField"] = JsonDocument.Parse("true").RootElement);

        Exception exception = Assert.Throws<JsonException>(() => LoadDocument(mutated));
        Assert.Contains("bogusField", exception.Message);
    }

    [Fact]
    public void MissingRequiredFieldIsRejectedWithExactPath()
    {
        string mutated = MutatedRoot(root => root.Remove("turn"));

        Exception exception = Assert.Throws<JsonException>(() => LoadDocument(mutated));
        Assert.Contains("turn", exception.Message);
    }

    [Fact]
    public void VersionMigrationRejectsLegacyMetadataPinAndDoesNotInferBehaviorPin()
    {
        string mutated = MutatedRoot(root =>
        {
            root["cardDataVersion"] = JsonDocument.Parse("\"gateway-m2-3\"").RootElement;
            root.Remove("behaviorRegistryVersion");
            root.Remove("behaviorCatalogFingerprint");
        });

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());

        Assert.Contains("cardDataVersion: 'gateway-m2-3' is not the authenticated Gateway metadata snapshot", exception.Message);
        Assert.Contains("behaviorRegistryVersion: must be an explicit compiled behavior catalog version", exception.Message);
        Assert.Contains("behaviorCatalogFingerprint:", exception.Message);
    }

    [Fact]
    public void ServerZoneCardWithoutServerReferenceReportsExactPath()
    {
        string mutated = MutatedRoot(root => root["cards"] = MutateCard(root["cards"], 0, item => item.Remove("server")));

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("cards[0].server", exception.Message);
    }

    [Fact]
    public void ContradictoryOwnerSideReportsExactPath()
    {
        string mutated = MutatedRoot(root => root["cards"] = MutateCard(root["cards"], 4, item =>
            item["ownerSide"] = JsonDocument.Parse("\"corp\"").RootElement));

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("cards[4].ownerSide", exception.Message);
    }

    [Fact]
    public void DuplicateCardIdReportsExactPath()
    {
        string mutated = MutatedRoot(root =>
        {
            var items = root["cards"].EnumerateArray().ToList();
            var cloned = new Dictionary<string, JsonElement>(
                items[^1].EnumerateObject().ToDictionary(property => property.Name, property => property.Value));
            cloned["id"] = JsonDocument.Parse("\"card:7\"").RootElement.Clone();
            items.Add(JsonSerializer.SerializeToElement(cloned));
            root["cards"] = JsonSerializer.SerializeToElement(items);
        });

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("cards[7].id", exception.Message);
    }

    [Fact]
    public void UnregisteredDefinitionReportsExactPath()
    {
        string mutated = MutatedRoot(root => root["cards"] = MutateCard(root["cards"], 6, item =>
            item["definitionId"] = JsonDocument.Parse("\"99999\"").RootElement));

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("cards[6].definitionId", exception.Message);
        Assert.Contains("not registered in the behavior spec catalog", exception.Message);
    }

    [Theory]
    [InlineData(0, "30037", "ICE")]
    [InlineData(2, "30072", "an asset or agenda")]
    public void InstalledServerZoneRejectsAnIncompatibleRegisteredBehavior(
        int cardIndex,
        string definitionId,
        string expectedBehavior)
    {
        string mutated = MutatedRoot(root => root["cards"] = MutateCard(root["cards"], cardIndex, item =>
            item["definitionId"] = JsonSerializer.SerializeToElement(definitionId)));

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains($"cards[{cardIndex}].definitionId", exception.Message);
        Assert.Contains($"not registered as {expectedBehavior}", exception.Message);
    }

    [Fact]
    public void RunnerRigRejectsAnIncompatibleRegisteredBehavior()
    {
        string mutated = MutatedRoot(root => root["cards"] = MutateCard(root["cards"], 5, item =>
            item["definitionId"] = JsonSerializer.SerializeToElement("30037")));

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("cards[5].definitionId", exception.Message);
        Assert.Contains("not registered as an icebreaker", exception.Message);
    }

    [Fact]
    public void RunSourceThatIsNotRunnerOwnedReportsExactPath()
    {
        string mutated = MutatedRoot(root => root["run"] = JsonDocument.Parse(
            """{"server":"remote:0","source":"card:2"}""").RootElement);

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("run.source", exception.Message);
    }

    [Fact]
    public void RunSourceInRunnerRigIsRejectedBeforeRunStarts()
    {
        string mutated = MutatedRoot(root => root["run"] = JsonDocument.Parse(
            """{"server":"remote:0","source":"card:5"}""").RootElement);

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("run.source", exception.Message);
        Assert.Contains("RunnerGrip", exception.Message);
    }

    [Fact]
    public void RunSourceWithNonEventDefinitionIsRejectedBeforeRunStarts()
    {
        string mutated = MutatedRoot(root => root["cards"] = MutateCard(root["cards"], 6, item =>
            item["definitionId"] = JsonDocument.Parse("\"30006\"").RootElement));

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("run.source", exception.Message);
        Assert.Contains("not a registered run event", exception.Message);
    }

    [Fact]
    public void OutOfRangeRemoteServerReferenceReportsExactPath()
    {
        string mutated = MutatedRoot(root => root["run"] = JsonDocument.Parse(
            """{"server":"remote:5","source":"card:7"}""").RootElement);

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception = Assert.Throws<M2ScenarioException>(() => document.Validate());
        Assert.Contains("run.server", exception.Message);
    }

    [Fact]
    public void ExpectedInitialHashMismatchIsReported()
    {
        string mutated = MutatedRoot(root => root["expectedInitialStateHash"] = JsonDocument.Parse(
            "\"0000000000000000000000000000000000000000000000000000000000000000\"").RootElement);

        M2ScenarioDocument document = LoadDocument(mutated);
        M2ScenarioException exception =
            Assert.Throws<M2ScenarioException>(() => M2ScenarioSetup.Build(document));
        Assert.Contains("expectedInitialStateHash", exception.Message);
    }
}
