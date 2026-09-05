using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Netrunner.Core.Tests;

public sealed class RemoteLifecycleTests
{
    private static readonly BehaviorCatalog Catalog = new BehaviorCatalogBuilder("test:remote-lifecycle", FixtureBehaviorCatalog.Catalog)
        .Add(new CardBehaviorDefinition(FixtureIceBehaviorSpecs.Plain, PlayerSide.Corp,
            paidAbilities: [new BehaviorPaidAbility("ability:leave-server", BehaviorAbilityTiming.AnyPaidWindow,
                BehaviorAbilityActivation.PaidAbility, [new BehaviorCost(BehaviorCostKind.TrashSelf)], [], [],
                [new BehaviorInstruction(BehaviorInstructionKind.NoOp)], BehaviorAbilityUsageLimit.Unlimited)])).Build();

    [Theory]
    [InlineData(ZoneKind.CorpDiscard)]
    [InlineData(ZoneKind.CorpHand)]
    public void LastCardLeavingRemoteRemovesItsServerAndZones(ZoneKind destination)
    {
        GameState state = CreateState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        GameState result = MoveLastIce(state, destination);
        Assert.DoesNotContain(result.Servers, server => server.Id == remote.Id);
        Assert.DoesNotContain(result.Zones, zone => zone.ServerId == remote.Id);
        StateInvariantValidator.Validate(result, Catalog);
    }

    [Theory]
    [InlineData(RunTimingStep.InitiationPaidWindow)]
    [InlineData(RunTimingStep.ApproachPaidWindow)]
    [InlineData(RunTimingStep.EncounterPaidWindow)]
    [InlineData(RunTimingStep.MovementPaidWindow)]
    public void LastIceLeavingAttackedRemoteFinishesPaidWindowBeforeEndingRun(RunTimingStep step)
    {
        GameState state = CreateState();
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        state = GameEngine.BeginRun(state, remote.Id, Catalog);
        for (int i = 0; state.RunState!.Step != step && i < 30; i++)
        {
            state = Pass(state);
        }
        Assert.Equal(step, state.RunState!.Step);

        if (state.GetPlayer(state.PendingDecision!.Owner).Side == PlayerSide.Runner)
        {
            state = Pass(state);
        }
        PendingDecision beforeTrash = state.PendingDecision!;
        LegalAction trash = Assert.Single(beforeTrash.LegalActions, action => action.Kind == ActionKind.UseAbility);
        ApplyResult trashed = GameEngine.Apply(state, new ActionSubmission(beforeTrash.Owner, beforeTrash.Id, beforeTrash.Revision, trash.Id), Catalog);
        Assert.True(trashed.Accepted, trashed.Rejection?.Message);
        state = trashed.State;
        Assert.Equal(step, state.RunState!.Step);
        Assert.Equal(ServerLifecycle.Destroyed, state.Servers.Single(server => server.Id == remote.Id).Lifecycle);
        Assert.DoesNotContain(state.PendingDecision!.LegalActions,
            action => action.Kind is ActionKind.RezIce or ActionKind.BreakSubroutines or ActionKind.UseBioroidAbility);
        state = EngineJson.DeserializeState(EngineJson.Serialize(state), Catalog);
        for (int i = 0; state.RunState is not null && i < 10; i++)
        {
            state = Pass(state);
        }
        Assert.Null(state.RunState);
        Assert.DoesNotContain(state.Servers, server => server.Id == remote.Id);
        Assert.DoesNotContain(state.Zones, zone => zone.ServerId == remote.Id);
        StateInvariantValidator.Validate(state, Catalog);
    }

    [Fact]
    public void PopulatedRemoteCannotBeForgedAsDestroyed()
    {
        GameState state = RunTimingTests.CreateRunState(1, rezzed: true);
        JsonNode node = JsonNode.Parse(EngineJson.Serialize(state))!;
        node["servers"]!.AsArray().Last()!["lifecycle"] = "destroyed";
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(node.ToJsonString(), FixtureBehaviorCatalog.Catalog));
    }

    private static GameState MoveLastIce(GameState state, ZoneKind destination)
    {
        CardInstanceState ice = Assert.Single(state.Cards);
        return GameEngine.BeginResolution(state,
            new EffectResolutionFrame(state.Players.Single(player => player.Side == PlayerSide.Corp).Id,
                new CostBundle(), new EffectBundle(new MoveCardEffect(ice.Id, state.Zones.Single(zone => zone.Kind == destination).Id))),
            Catalog);
    }

    private static GameState CreateState()
    {
        GameState baseline = RunTimingTests.CreateRunState(1, rezzed: true);
        JsonNode node = JsonNode.Parse(EngineJson.Serialize(baseline))!;
        node["behaviorRegistryVersion"] = Catalog.Version;
        node["behaviorCatalogFingerprint"] = Catalog.Fingerprint;
        TurnFactsState facts = new(1, PlayerSide.Runner, 1, 4, 0, true, false, false, false);
        node["turn"] = JsonNode.Parse(EngineJson.Serialize(new TurnState(PlayerSide.Runner, TurnPhase.Runner, 1,
            TurnStep.RunnerAction, new TurnHistoryState(1, null, null, facts, null))));
        node["rulesState"] = JsonNode.Parse(EngineJson.Serialize(RulesState.CreateDefault(baseline.Players)));
        return EngineJson.DeserializeState(node.ToJsonString(), Catalog);
    }

    private static GameState Pass(GameState state)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction action = decision.LegalActions.First(candidate => candidate.Kind == ActionKind.PassPriority);
        ApplyResult result = GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id), Catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }
}
