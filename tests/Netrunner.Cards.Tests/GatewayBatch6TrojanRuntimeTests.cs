using System.Text.Json;
using System.Text.Json.Nodes;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch6TrojanRuntimeTests
{
    private static readonly CardInstanceId ProgramId = new("card:trojan");
    private static readonly CardInstanceId IceId = new("card:host-ice");
    private static readonly CardDefinitionId IceDefinition = new("test:trojan-ice");

    [Theory]
    [InlineData("30004")]
    [InlineData("30017")]
    public void InstallFreezesIceHostAndRoundTripsPublicRelationship(string code)
    {
        Fixture fixture = Create(code);
        GameState ready = OpenAction(fixture);
        LegalAction install = Assert.Single(ready.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard);
        Assert.Equal(IceId, Assert.IsType<InstallCardParameters>(install.Parameters).HostCardId);
        GameState installed = Apply(fixture, ready, install);
        Assert.Equal(IceId, installed.GetCard(ProgramId).HostCardId);
        Assert.Equal(1, Assert.Single(installed.RulesState!.Counters).Count);
        string json = EngineJson.Serialize(installed);
        Assert.Equal(json, EngineJson.Serialize(EngineJson.DeserializeState(json, fixture.Catalog, fixture.Runtime)));
        Assert.Contains("hostCardId", EngineJson.Serialize(ObservationBuilder.Build(installed, PlayerSide.Runner, fixture.Catalog)));
        Assert.Contains(IceId.Value, EngineJson.Serialize(ObservationBuilder.Build(installed, PlayerSide.Corp, fixture.Catalog)));
    }

    [Fact]
    public void NoIceMeansNoInstallAndForgedHostIsRejected()
    {
        Fixture empty = Create("30004", hasIce: false);
        Assert.DoesNotContain(OpenAction(empty).PendingDecision!.LegalActions, action => action.Kind == ActionKind.InstallCard);
        Fixture fixture = Create("30004");
        GameState installed = Install(fixture);
        GameState forged = Copy(installed, cards: installed.Cards.Select(card => card.Id == ProgramId
            ? new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide, true, hostCardId: ProgramId)
            : card).ToArray());
        Assert.Contains(StateInvariantValidator.Collect(forged, fixture.Catalog, fixture.Runtime),
            violation => violation.Code == "hosting.invalid-host");
    }

    [Fact]
    public void HostUninstallTrashesProgramClearsCountersAndMemory()
    {
        Fixture fixture = Create("30004");
        GameState installed = Copy(Install(fixture));
        GameState moved = Resolve(fixture, installed, new MoveCardEffect(IceId, installed.GetZone(ZoneKind.CorpHand).Id));
        Assert.Equal(ZoneKind.RunnerHeap, moved.GetZone(moved.GetCard(ProgramId).ZoneId).Kind);
        Assert.Null(moved.GetCard(ProgramId).HostCardId);
        Assert.Empty(moved.RulesState!.Counters);
        Assert.Empty(moved.RulesState.Programs);
    }

    [Theory]
    [InlineData("test:ordinary-program")]
    [InlineData("test:unregistered-program")]
    public void SavedHostRelationshipRequiresHostingDefinition(string code)
    {
        Fixture fixture = Create(code);
        GameState installed = Install(fixture);
        Assert.Null(installed.GetCard(ProgramId).HostCardId);
        string json = EngineJson.Serialize(installed);
        Assert.Equal(json, EngineJson.Serialize(EngineJson.DeserializeState(json, fixture.Catalog, fixture.Runtime)));

        JsonObject forged = JsonNode.Parse(json)!.AsObject();
        JsonNode program = forged["cards"]!.AsArray().Single(card => card!["id"]!.GetValue<string>() == ProgramId.Value)!;
        program["hostCardId"] = IceId.Value;
        JsonException error = Assert.Throws<JsonException>(() =>
            EngineJson.DeserializeState(forged.ToJsonString(), fixture.Catalog, fixture.Runtime));
        StateInvariantException invariant = Assert.IsType<StateInvariantException>(error.GetBaseException());
        Assert.Contains(invariant.Violations, violation => violation.Code == "hosting.invalid-host");
    }

    [Fact]
    public void TranquilizerChecksEachPlacementAndDerezRetainsInstallation()
    {
        Fixture fixture = Create("30017");
        GameState installed = Copy(Install(fixture));
        GameState two = Resolve(fixture, installed, new CardCounterAdjustmentEffect(ProgramId, "virus", 1));
        Assert.True(two.GetCard(IceId).Rezzed);
        GameState three = Resolve(fixture, Copy(two), new CardCounterAdjustmentEffect(ProgramId, "virus", 1));
        Assert.False(three.GetCard(IceId).Rezzed);
        Assert.False(three.GetCard(IceId).FaceUp);
        Assert.Equal(installed.GetCard(IceId).ZoneId, three.GetCard(IceId).ZoneId);
        Assert.Equal(IceId, three.GetCard(ProgramId).HostCardId);
        Assert.Equal(3, Assert.Single(three.RulesState!.Counters).Count);
        string json = EngineJson.Serialize(three);
        Assert.Equal(json, EngineJson.Serialize(EngineJson.DeserializeState(json, fixture.Catalog, fixture.Runtime)));
    }

    [Fact]
    [Trait("GatewayCard", "30017")]
    public void DerezzedHostCanBeRezzedAndNextCounterPlacementDerezzesAgain()
    {
        Fixture fixture = Create("30017");
        GameState installed = Copy(Install(fixture));
        GameState three = Copy(Resolve(fixture, installed, new CardCounterAdjustmentEffect(ProgramId, "virus", 2)));
        ServerId server = three.GetZone(three.GetCard(IceId).ZoneId).ServerId!.Value;
        GameState run = GameEngine.BeginRun(three, server, fixture.Catalog, fixture.Runtime);
        for (int step = 0; step < 20 && !run.PendingDecision!.LegalActions.Any(action => action.Kind == ActionKind.RezCard); step++)
        {
            run = Apply(fixture, run, Assert.Single(run.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority));
        }
        GameState rezzed = Apply(fixture, run, Assert.Single(run.PendingDecision!.LegalActions, action => action.Kind == ActionKind.RezCard));
        Assert.True(rezzed.GetCard(IceId).Rezzed);
        GameState four = Resolve(fixture, Copy(rezzed), new CardCounterAdjustmentEffect(ProgramId, "virus", 1));
        Assert.False(four.GetCard(IceId).Rezzed);
        Assert.Equal(4, Assert.Single(four.RulesState!.Counters).Count);
    }

    [Fact]
    [Trait("GatewayCard", "30004")]
    public void BotulusBreaksHighStrengthHostUsingCounterWithoutCredits()
    {
        Fixture fixture = Create("30004", twoSubroutines: true);
        GameState installed = Copy(Install(fixture));
        ServerId server = installed.GetZone(installed.GetCard(IceId).ZoneId).ServerId!.Value;
        GameState run = GameEngine.BeginRun(installed, server, fixture.Catalog, fixture.Runtime);
        for (int step = 0; step < 30 && run.RunState?.Step != RunTimingStep.EncounterPaidWindow; step++)
        {
            run = Apply(fixture, run, Assert.Single(run.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority));
        }
        if (run.RunState?.Priority?.PriorityHolder != PlayerSide.Runner)
        {
            run = Apply(fixture, run, Assert.Single(run.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority));
        }
        SubroutineId later = run.RunState!.Encounter!.Subroutines[1].Id;
        LegalAction broken = Assert.Single(run.PendingDecision!.LegalActions, action => action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).SubroutineId == later);
        string json = EngineJson.Serialize(run);
        JsonNode forged = JsonNode.Parse(json)!;
        JsonArray actions = forged["pendingDecision"]!["legalActions"]!.AsArray();
        JsonNode breakAction = actions.First(action => action!["parameters"]?["subroutineId"] is not null)!;
        breakAction["parameters"]!["subroutineId"] = "subroutine:unoffered";
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(forged.ToJsonString(), fixture.Catalog, fixture.Runtime));
        GameState restored = EngineJson.DeserializeState(json, fixture.Catalog, fixture.Runtime);
        GameState after = Apply(fixture, restored, broken);
        Assert.Empty(after.RulesState!.Counters);
        Assert.False(after.RunState!.Encounter!.Subroutines[0].Broken);
        Assert.True(after.RunState.Encounter.Subroutines[1].Broken);
        Assert.DoesNotContain(after.PendingDecision!.LegalActions, action => action.Kind == ActionKind.BreakSubroutines);
    }

    [Fact]
    public void BehaviorInstallOffersAndRoundTripsExplicitHostChoice()
    {
        Fixture fixture = Create("30004", installer: true);
        GameState ready = OpenAction(fixture);
        GameState choosing = Apply(fixture, ready,
            Assert.Single(ready.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PlayCard));
        LegalAction chooseProgram = Assert.Single(choosing.PendingDecision!.LegalActions);
        GameState hosts = Apply(fixture, choosing, chooseProgram);
        Assert.Equal(DecisionKind.InstallChoice, hosts.PendingDecision!.Kind);
        LegalAction host = Assert.Single(hosts.PendingDecision.LegalActions);
        Assert.Equal(IceId, Assert.IsType<InstallCardParameters>(host.Parameters).HostCardId);
        string json = EngineJson.Serialize(hosts);
        hosts = EngineJson.DeserializeState(json, fixture.Catalog, fixture.Runtime);
        GameState installed = Apply(fixture, hosts, host);
        Assert.Equal(IceId, installed.GetCard(ProgramId).HostCardId);
        Assert.Equal(1, Assert.Single(installed.RulesState!.Counters).Count);
    }

    private static GameState Resolve(Fixture fixture, GameState state, EffectInstruction effect) =>
        GameEngine.BeginResolution(state,
            new EffectResolutionFrame(state.Players.Single(player => player.Side == PlayerSide.Runner).Id,
                new CostBundle(), new EffectBundle(effect)), fixture.Catalog, fixture.Runtime);

    private static GameState Install(Fixture fixture)
    {
        GameState ready = OpenAction(fixture);
        return Apply(fixture, ready, Assert.Single(ready.PendingDecision!.LegalActions, action => action.Kind == ActionKind.InstallCard));
    }

    private static GameState OpenAction(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(fixture.State, PlayerSide.Runner, fixture.Catalog, fixture.Runtime);
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            state = Apply(fixture, state, Assert.Single(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority));
        }
        return state;
    }

    private static GameState Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision decision = state.PendingDecision!;
        ApplyResult result = GameEngine.Apply(state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id), fixture.Catalog, fixture.Runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static GameState Copy(GameState state, IReadOnlyList<CardInstanceState>? cards = null) => new(
        state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, state.Players,
        cards ?? state.Cards, state.Zones, state.Servers, state.Turn, state.Outcome, state.RandomState, state.Allocator,
        eventSequence: state.EventSequence, actionSequence: state.ActionSequence, rulesState: state.RulesState,
        behaviorRegistryVersion: state.BehaviorRegistryVersion, behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint,
        runtimeCardCatalogVersion: state.RuntimeCardCatalogVersion, runtimeCardCatalogFingerprint: state.RuntimeCardCatalogFingerprint);

    private static Fixture Create(string code, bool hasIce = true, bool installer = false, bool twoSubroutines = false)
    {
        BehaviorCatalog catalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion)
            .Add(GatewayBatch6TrojanBehaviorSpecs.GenericSpecs)
            .Add(new CardBehaviorDefinition(new CardDefinitionId("test:ordinary-program"), PlayerSide.Runner))
            .Add(new CardBehaviorDefinition(new CardDefinitionId("test:trojan-installer"), PlayerSide.Runner,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose,
                        choice: new BehaviorChoiceSpec("install", BehaviorChooser.Controller, BehaviorChoiceMode.Mandatory,
                            BehaviorChoiceCardinality.Exactly(1), target: BehaviorTarget.CardInZone(BehaviorZone.Grip,
                                new BehaviorCardFilter(BehaviorCardType.Program)))),
                    new BehaviorInstruction(BehaviorInstructionKind.InstallCard,
                        target: new BehaviorTarget(BehaviorTargetKind.ChosenCard, selector: "install"),
                        sourceZone: BehaviorZone.Grip, destinationZone: BehaviorZone.Rig),
                ]))
            .AddIce(new IceBehaviorSpec(IceDefinition, 20, IceType.Barrier, 0, twoSubroutines ? [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1), new EndRunSubroutineSpec()] : [new EndRunSubroutineSpec()], []))
            .Build();
        RuntimeCardCatalog runtime = new RuntimeCardCatalogBuilder("test:trojan-runtime")
            .Add(new CardRuntimeSpec(new CardDefinitionId(code), PlayerSide.Runner, RuntimeCardClass.Program,
                installCost: 2, memoryUnits: 1, subtypes: ["virus", "trojan"]))
            .Add(new CardRuntimeSpec(IceDefinition, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0))
            .Add(new CardRuntimeSpec(new CardDefinitionId("test:trojan-installer"), PlayerSide.Runner, RuntimeCardClass.Event, playCost: 0))
            .Build();
        GameState baseline = GameState.CreateNew(seed: 435,
            behaviorRegistryVersion: catalog.Version, behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState iceZone = baseline.Zones.First(zone => zone.Kind == ZoneKind.ServerIce);
        List<CardInstanceState> cards = [new(ProgramId, new CardDefinitionId(code), grip.Id, PlayerSide.Runner)];
        if (installer)
        {
            cards.Add(new CardInstanceState(new CardInstanceId("card:installer"),
                new CardDefinitionId("test:trojan-installer"), grip.Id, PlayerSide.Runner));
        }
        if (hasIce)
        {
            cards.Add(new CardInstanceState(IceId, IceDefinition, iceZone.Id, PlayerSide.Corp, true, true));
        }
        PlayerState[] players = baseline.Players.Select(player => new PlayerState(player.Id, player.Side, 20, 4, 0)).ToArray();
        GameState state = new(baseline.SchemaVersion, baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion,
            players, cards, baseline.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
                cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToArray(),
            baseline.Servers, new TurnState(PlayerSide.Runner, TurnPhase.Runner, baseline.Turn.TurnNumber), baseline.Outcome,
            baseline.RandomState, baseline.Allocator, rulesState: RulesState.CreateDefault(players),
            behaviorRegistryVersion: catalog.Version, behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        return new Fixture(state, catalog, runtime);
    }

    private sealed record Fixture(GameState State, BehaviorCatalog Catalog, RuntimeCardCatalog Runtime);
}
