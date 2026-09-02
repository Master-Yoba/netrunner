using System.Text.Json;
using Xunit;

namespace Netrunner.Core.Tests;

public sealed class RuntimeCardCatalogTests
{
    [Fact]
    public void CatalogIsIndependentFromExecutableBehaviorIdsAndRoundTripsWithStableFingerprint()
    {
        CardRuntimeSpec[] specs = ValidSpecs();
        RuntimeCardCatalog first = new RuntimeCardCatalogBuilder("runtime:test")
            .Add(specs[0])
            .Add(specs[1])
            .Add(specs[2])
            .Add(specs[3])
            .Add(specs[4])
            .Add(specs[5])
            .Add(specs[6])
            .Add(specs[7])
            .Add(specs[8])
            .Build();
        RuntimeCardCatalog reordered = new RuntimeCardCatalogBuilder("runtime:test")
            .Add(specs[8])
            .Add(specs[3])
            .Add(specs[0])
            .Add(specs[5])
            .Add(specs[1])
            .Add(specs[7])
            .Add(specs[4])
            .Add(specs[2])
            .Add(specs[6])
            .Build();

        Assert.Equal(first.Fingerprint, reordered.Fingerprint);
        Assert.Equal(specs.Length, first.DefinitionIds.Count);
        Assert.Equal(
            first.DefinitionIds.OrderBy(value => value.Value, StringComparer.Ordinal),
            first.DefinitionIds);

        string json = EngineJson.Serialize(first);
        RuntimeCardCatalog restored = EngineJson.Deserialize<RuntimeCardCatalog>(json);
        Assert.Equal(first.Version, restored.Version);
        Assert.Equal(first.Fingerprint, restored.Fingerprint);
        Assert.Equal(first.DefinitionIds, restored.DefinitionIds);
        Assert.True(restored.TryGet(new CardDefinitionId("runtime:agenda"), out CardRuntimeSpec? agenda));
        Assert.Equal(RuntimeCardClass.Agenda, agenda.CardClass);

        CardDefinitionId behaviorId = new("runtime:behavior-id");
        BehaviorCatalog behavior = new BehaviorCatalogBuilder("behavior:test")
            .Add(new CardBehaviorDefinition(behaviorId, PlayerSide.Corp))
            .Build();
        RuntimeCardCatalog adjunct = new RuntimeCardCatalogBuilder("runtime:adjunct")
            .Add(new CardRuntimeSpec(
                behaviorId,
                PlayerSide.Corp,
                RuntimeCardClass.Identity))
            .Build();

        Assert.Contains(behaviorId, behavior.AllDefinitionIds);
        Assert.Contains(behaviorId, adjunct.DefinitionIds);
        Assert.DoesNotContain(new CardDefinitionId("runtime:agenda"), behavior.AllDefinitionIds);
    }

    [Fact]
    public void SubtypesAreCanonicalizedBeforeFingerprinting()
    {
        CardRuntimeSpec unordered = new(
            new CardDefinitionId("runtime:subtype-order"),
            PlayerSide.Runner,
            RuntimeCardClass.Resource,
            installCost: 0,
            subtypes: ["zeta", "alpha", "zeta"]);
        CardRuntimeSpec canonical = new(
            new CardDefinitionId("runtime:subtype-order"),
            PlayerSide.Runner,
            RuntimeCardClass.Resource,
            installCost: 0,
            subtypes: ["alpha", "zeta"]);

        Assert.Equal(["alpha", "zeta"], unordered.Subtypes);
        Assert.Equal(unordered.Subtypes, canonical.Subtypes);
        Assert.Equal(
            new RuntimeCardCatalogBuilder("runtime:subtypes").Add(unordered).Build().Fingerprint,
            new RuntimeCardCatalogBuilder("runtime:subtypes").Add(canonical).Build().Fingerprint);
    }

    [Fact]
    public void ScenarioAddCardsReconstructionPreservesRuntimeCatalogPins()
    {
        RuntimeCardCatalog catalog = RuntimeCatalog("runtime:scenario-add-cards");
        ScenarioArtifact artifact = new(
            seed: 55,
            cards:
            [
                new ScenarioCardSpec(
                    new CardInstanceId("card:scenario-runtime"),
                    new CardDefinitionId("fixture:first"),
                    ZoneKind.CorpHand,
                    PlayerSide.Corp),
            ]);

        GameState state = ScenarioBuilder.BuildWithRuntimeCatalogForTesting(artifact, catalog);

        Assert.Equal(catalog.Version, state.RuntimeCardCatalogVersion);
        Assert.Equal(catalog.Fingerprint, state.RuntimeCardCatalogFingerprint);
        Assert.Contains(
            state.GetZone(ZoneKind.CorpHand).CardIds,
            id => id == new CardInstanceId("card:scenario-runtime"));

        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(json, null, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
    }

    [Fact]
    public void ToyRulesExpandReconstructionPreservesRuntimeCatalogPins()
    {
        RuntimeCardCatalog catalog = RuntimeCatalog("runtime:toy-expand");
        ToyDeckRecipe corp = new(
            "corp-runtime",
            PlayerSide.Corp,
            [
                ToyCardDefinitionIds.CorpAgenda3,
                ToyCardDefinitionIds.CorpAgenda3,
                ToyCardDefinitionIds.CorpAgenda3,
                ToyCardDefinitionIds.CorpAgenda3,
            ]);
        ToyDeckRecipe runner = new(
            "runner-runtime",
            PlayerSide.Runner,
            [
                ToyCardDefinitionIds.RunnerProgram,
                ToyCardDefinitionIds.RunnerProgram,
                ToyCardDefinitionIds.RunnerProgram,
                ToyCardDefinitionIds.RunnerProgram,
            ]);

        GameState state = ToyGameSetup.CreateWithRuntimeCatalogForTesting(56, corp, runner, catalog);

        Assert.Equal(catalog.Version, state.RuntimeCardCatalogVersion);
        Assert.Equal(catalog.Fingerprint, state.RuntimeCardCatalogFingerprint);
        Assert.NotNull(state.PendingDecision);

        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(json, null, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RuntimeCatalogPinsMustBeSuppliedAsACompletePair(bool versionOnly)
    {
        RuntimeCardCatalog catalog = RuntimeCatalog("runtime:pins");

        Assert.Throws<StateInvariantException>(() => GameState.CreateNew(
            seed: versionOnly ? 51UL : 52UL,
            runtimeCardCatalogVersion: versionOnly ? catalog.Version : null,
            runtimeCardCatalogFingerprint: versionOnly ? null : catalog.Fingerprint));
    }

    [Fact]
    public void DualCatalogJsonRestoreRejectsWrongRuntimeCatalogAtomically()
    {
        BehaviorCatalog behavior = new BehaviorCatalogBuilder("behavior:json-runtime").Build();
        RuntimeCardCatalog expectedRuntime = RuntimeCatalog("runtime:json-expected");
        RuntimeCardCatalog wrongRuntime = RuntimeCatalog("runtime:json-wrong");
        GameState state = GameState.CreateNew(
            seed: 53,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: expectedRuntime.Version,
            runtimeCardCatalogFingerprint: expectedRuntime.Fingerprint);
        string stateJson = EngineJson.Serialize(state);

        GameState restored = EngineJson.DeserializeState(stateJson, behavior, expectedRuntime);
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(stateJson, behavior, wrongRuntime));
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<GameState>(stateJson, behavior, wrongRuntime));

        byte[] stateUtf8 = EngineJson.SerializeUtf8(state);
        GameState restoredUtf8 = EngineJson.Deserialize<GameState>(stateUtf8, behavior, expectedRuntime);
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restoredUtf8));
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<GameState>(stateUtf8, behavior, wrongRuntime));

        GameSnapshot snapshot = state.CreateSnapshot(behavior, expectedRuntime);
        string snapshotJson = EngineJson.Serialize(snapshot);
        GameSnapshot restoredSnapshot = EngineJson.DeserializeSnapshot(snapshotJson, behavior, expectedRuntime);
        Assert.Equal(
            DeterministicHash.ForState(state),
            DeterministicHash.ForState(restoredSnapshot.ToState()));
        Assert.Throws<JsonException>(() => EngineJson.DeserializeSnapshot(snapshotJson, behavior, wrongRuntime));
    }

    [Fact]
    public void RuntimeCatalogBoundSessionRejectsMismatchedStateAtEverySetupBoundary()
    {
        BehaviorCatalog behavior = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        RuntimeCardCatalog sessionRuntime = RuntimeCatalog("runtime:session-a");
        RuntimeCardCatalog stateRuntime = RuntimeCatalog("runtime:session-b");
        GameState state = GameState.CreateNew(
            seed: 54,
            behaviorRegistryVersion: behavior.Version,
            runtimeCardCatalogVersion: stateRuntime.Version,
            runtimeCardCatalogFingerprint: stateRuntime.Fingerprint);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        ServerId server = state.Servers[0].Id;
        GameEngineSession session = new(behavior, sessionRuntime);

        Assert.Throws<ResolutionFailureException>(() => session.BeginResolution(
            state,
            new ChooseNumberFrame(corp, 1, 1)));
        Assert.Throws<ResolutionFailureException>(() => session.BeginTurn(state, PlayerSide.Corp));
        Assert.Throws<ResolutionFailureException>(() => session.BeginTurnWithEvents(state, PlayerSide.Corp));
        Assert.Throws<ResolutionFailureException>(() => session.BeginRun(state, server));
        Assert.Throws<ResolutionFailureException>(() => session.BeginRunWithEvents(state, server));

        // The short alias must retain the same runtime-bound behavior at its
        // own setup boundaries rather than silently falling back to Apply.
        EngineSession alias = new(behavior, sessionRuntime);
        Assert.Throws<ResolutionFailureException>(() => alias.BeginResolution(
            state,
            new ChooseNumberFrame(corp, 1, 1)));
        Assert.Throws<ResolutionFailureException>(() => alias.BeginRunWithEvents(state, server));
    }

    [Fact]
    public void RuntimeCatalogFingerprintIsPinnedInStateAndSnapshotWithoutChangingBehaviorPin()
    {
        RuntimeCardCatalog catalog = new RuntimeCardCatalogBuilder("runtime:state")
            .Add(new CardRuntimeSpec(
                new CardDefinitionId("runtime:identity"),
                PlayerSide.Runner,
                RuntimeCardClass.Identity))
            .Build();

        GameState state = GameState.CreateNew(
            seed: 41,
            runtimeCardCatalogVersion: catalog.Version,
            runtimeCardCatalogFingerprint: catalog.Fingerprint);
        StateInvariantValidator.Validate(state, catalog);

        GameState restored = EngineJson.DeserializeState(EngineJson.Serialize(state));
        Assert.Equal(catalog.Version, restored.RuntimeCardCatalogVersion);
        Assert.Equal(catalog.Fingerprint, restored.RuntimeCardCatalogFingerprint);
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));

        GameSnapshot snapshot = state.CreateSnapshot(catalog);
        GameState fromSnapshot = GameState.FromSnapshot(snapshot, catalog);
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(fromSnapshot));

        GameState mismatched = new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            initialSeed: state.InitialSeed,
            behaviorRegistryVersion: state.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: catalog.Version,
            runtimeCardCatalogFingerprint: new string('0', 64));
        Assert.Contains(
            StateInvariantValidator.Collect(mismatched, null, catalog),
            violation => violation.Code == "metadata.runtime-card-fingerprint-mismatch");
    }

    [Fact]
    public void ObservationProjectsEffectiveMaximumHandSizeFromActiveStaticModifier()
    {
        CardDefinitionId modifierId = new("runtime:max-hand-modifier");
        BehaviorCatalog behavior = new BehaviorCatalogBuilder("behavior:max-hand")
            .Add(new CardBehaviorDefinition(
                modifierId,
                PlayerSide.Runner,
                staticModifiers: [new BehaviorStaticModifier(BehaviorStaticModifierKind.MaximumHandSize, 2)]))
            .Build();
        GameState baseline = GameState.CreateNew(
            seed: 42,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint);
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState installed = new(
            new CardInstanceId("card:max-hand-modifier"),
            modifierId,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true);
        RulesState rules = new RulesState(
        [
            new PlayerRulesState(corp.Id, 5, 0, 0, 0, 0),
            new PlayerRulesState(runner.Id, 5, 0, 0, 4, 0),
        ]);
        GameState state = new GameState(
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            [installed],
            baseline.Zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            initialSeed: baseline.InitialSeed,
            rulesState: rules,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint);

        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner, behavior);
        var players = Assert.IsType<Dictionary<string, object?>>(observation.PublicState["players"]);
        var runnerView = Assert.IsType<Dictionary<string, object?>>(players["runner"]);
        var corpView = Assert.IsType<Dictionary<string, object?>>(players["corp"]);
        Assert.Equal(7, runnerView["maximumHandSize"]);
        Assert.Equal(5, corpView["maximumHandSize"]);
        Assert.Equal(5, rules.Players.Single(value => value.PlayerId == runner.Id).BaseHandSize);
    }

    [Theory]
    [InlineData(RuntimeCardClass.Identity, PlayerSide.Corp, 1, null, null, 0, 0, 0)]
    [InlineData(RuntimeCardClass.Agenda, PlayerSide.Corp, null, 1, null, 0, 0, 0)]
    [InlineData(RuntimeCardClass.Asset, PlayerSide.Corp, null, null, null, 0, 0, 0)]
    [InlineData(RuntimeCardClass.Program, PlayerSide.Corp, null, 1, null, 1, 0, 0)]
    [InlineData(RuntimeCardClass.Program, PlayerSide.Runner, null, null, null, 1, 0, 0)]
    public void CardRuntimeSpecRejectsInvalidClassShape(
        RuntimeCardClass cardClass,
        PlayerSide side,
        int? playCost,
        int? installCost,
        int? rezCost,
        int memoryUnits,
        int agendaPoints,
        int advancementRequirement)
    {
        Assert.Throws<ArgumentException>(() => new CardRuntimeSpec(
            new CardDefinitionId("runtime:invalid"),
            side,
            cardClass,
            playCost,
            installCost,
            rezCost,
            memoryUnits,
            agendaPoints: agendaPoints,
            advancementRequirement: advancementRequirement));
    }

    [Fact]
    public void CardRuntimeSpecRequiresConsoleSubtypeAndKeyAndUniqueKey()
    {
        Assert.Throws<ArgumentException>(() => new CardRuntimeSpec(
            new CardDefinitionId("runtime:console-no-subtype"),
            PlayerSide.Runner,
            RuntimeCardClass.Hardware,
            installCost: 2,
            console: true,
            consoleKey: "console"));
        Assert.Throws<ArgumentException>(() => new CardRuntimeSpec(
            new CardDefinitionId("runtime:console-no-key"),
            PlayerSide.Runner,
            RuntimeCardClass.Hardware,
            installCost: 2,
            subtypes: ["console"],
            console: true));
        Assert.Throws<ArgumentException>(() => new CardRuntimeSpec(
            new CardDefinitionId("runtime:unique-no-key"),
            PlayerSide.Runner,
            RuntimeCardClass.Resource,
            installCost: 0,
            unique: true));
        Assert.Throws<ArgumentException>(() => new CardRuntimeSpec(
            new CardDefinitionId("runtime:event-trash-cost"),
            PlayerSide.Runner,
            RuntimeCardClass.Event,
            playCost: 1,
            trashCost: 1));
    }

    private static CardRuntimeSpec[] ValidSpecs() =>
    [
        new(new CardDefinitionId("runtime:identity"), PlayerSide.Corp, RuntimeCardClass.Identity),
        new(new CardDefinitionId("runtime:agenda"), PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 4),
        new(new CardDefinitionId("runtime:asset"), PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 2, trashCost: 2),
        new(new CardDefinitionId("runtime:ice"), PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 3),
        new(new CardDefinitionId("runtime:operation"), PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 1),
        new(new CardDefinitionId("runtime:event"), PlayerSide.Runner, RuntimeCardClass.Event, playCost: 1),
        new(new CardDefinitionId("runtime:program"), PlayerSide.Runner, RuntimeCardClass.Program, installCost: 2, memoryUnits: 1),
        new(new CardDefinitionId("runtime:hardware"), PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 3),
        new(new CardDefinitionId("runtime:resource"), PlayerSide.Runner, RuntimeCardClass.Resource, installCost: 0),
    ];

    private static RuntimeCardCatalog RuntimeCatalog(string version) =>
        new RuntimeCardCatalogBuilder(version)
            .Add(new CardRuntimeSpec(
                new CardDefinitionId("runtime:session-identity"),
                PlayerSide.Runner,
                RuntimeCardClass.Identity))
            .Build();
}
