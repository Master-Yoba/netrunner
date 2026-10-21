using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

internal static class AgentBoundaryScenario
{
    public static (GameState State, BehaviorCatalog Behavior, RuntimeCardCatalog Runtime) Search(PlayerSide side, bool twin)
    {
        var source = new CardDefinitionId("test:source");
        var eligible = new CardDefinitionId("test:eligible");
        BehaviorZone deck = side == PlayerSide.Corp ? BehaviorZone.Deck : BehaviorZone.Stack;
        var filter = new BehaviorCardFilter(definitionId: eligible, ownerSide: side);
        BehaviorCatalog behavior = new BehaviorCatalogBuilder("test:boundary-search")
            .Add(new CardBehaviorDefinition(source, side, instructions:
                [new BehaviorInstruction(BehaviorInstructionKind.Search, argument: "search", sourceZone: deck,
                    filter: filter, target: BehaviorTarget.CardInZone(deck, filter))]))
            .Add(new CardBehaviorDefinition(eligible, side))
            .Add(new CardBehaviorDefinition(new("test:draw"), side))
            .Add(new CardBehaviorDefinition(new("test:hidden-b"), side))
            .Add(new CardBehaviorDefinition(new("test:hidden-c"), side)).Build();
        var builder = new RuntimeCardCatalogBuilder("test:boundary-runtime");
        foreach (CardDefinitionId id in behavior.AllDefinitionIds)
            builder.Add(new CardRuntimeSpec(id, side, side == PlayerSide.Corp ? RuntimeCardClass.Operation : RuntimeCardClass.Event, playCost: 0));
        RuntimeCardCatalog runtime = builder.Build();
        GameState basis = GameState.CreateNew(42, behaviorRegistryVersion: behavior.Version, behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        ZoneKind hand = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneKind draw = side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack;
        GameState state = Place(basis, new TurnState(side),
            ("card:source", source.Value, hand, side),
            ("card:mandatory-draw", "test:draw", draw, side),
            ("card:hidden-one", twin ? "test:hidden-c" : "test:hidden-b", draw, side),
            ("card:eligible", eligible.Value, draw, side),
            ("card:hidden-two", twin ? "test:hidden-b" : "test:hidden-c", draw, side));
        state = GameEngine.BeginTurn(state, side, behavior, runtime);
        for (int i = 0; i < 20 && state.PendingDecision?.Kind != DecisionKind.TurnAction; i++)
            state = Apply(state, state.PendingDecision!.LegalActions.Single(action => action.Kind == ActionKind.PassPriority), behavior, runtime);
        LegalAction play = state.PendingDecision!.LegalActions.Single(action => action.Parameters is PlayCardParameters { CardId.Value: "card:source" });
        return (Apply(state, play, behavior, runtime), behavior, runtime);
    }

    public static GameState Access(PlayerSide viewer, bool twin)
    {
        GameState basis = GameState.CreateNew(42);
        GameState state = Place(basis, new TurnState(PlayerSide.Runner, TurnPhase.Runner),
            ("card:deck-a", viewer == PlayerSide.Runner && twin ? "test:b" : "test:a", ZoneKind.CorpDeck, PlayerSide.Corp),
            ("card:deck-b", viewer == PlayerSide.Runner && twin ? "test:a" : "test:b", ZoneKind.CorpDeck, PlayerSide.Corp),
            ("card:runner-a", viewer == PlayerSide.Corp && twin ? "test:d" : "test:c", ZoneKind.RunnerStack, PlayerSide.Runner),
            ("card:runner-b", viewer == PlayerSide.Corp && twin ? "test:c" : "test:d", ZoneKind.RunnerStack, PlayerSide.Runner));
        state = GameEngine.BeginRun(state, state.Servers.Single(server => server.Kind == ServerKind.RAndD).Id);
        for (int i = 0; i < 30 && state.RunState?.Step != RunTimingStep.BreachCandidateSelection; i++)
        {
            LegalAction action = state.PendingDecision!.LegalActions.First(value => value.Kind is ActionKind.PassPriority or ActionKind.ContinueRun);
            state = Apply(state, action, BehaviorCatalog.Empty);
        }
        Assert.Equal(RunTimingStep.BreachCandidateSelection, state.RunState!.Step);
        return state;
    }

    private static GameState Place(GameState basis, TurnState turn, params (string Id, string Definition, ZoneKind Zone, PlayerSide Side)[] placements)
    {
        CardInstanceState[] cards = placements.Select(value => new CardInstanceState(new(value.Id), new(value.Definition), basis.GetZone(value.Zone).Id, value.Side)).ToArray();
        ZoneState[] zones = basis.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
            placements.Where(value => value.Zone == zone.Kind).Select(value => new CardInstanceId(value.Id)).ToArray())).ToArray();
        return new(basis.SchemaVersion, basis.GameId, basis.RulesVersion, basis.CardDataVersion, basis.Players, cards, zones, basis.Servers,
            turn, basis.Outcome, basis.RandomState, basis.Allocator, basis.DecisionRevision, basis.EventSequence,
            basis.ResolutionStack, basis.PendingDecision, basis.ActionSequence, basis.InitialSeed, basis.RunState, basis.RulesState,
            basis.BehaviorRegistryVersion, basis.BehaviorCatalogFingerprint, basis.RuntimeCardCatalogVersion, basis.RuntimeCardCatalogFingerprint);
    }

    private static GameState Apply(GameState state, LegalAction action, IBehaviorCatalog behavior, IRuntimeCardCatalog? runtime = null)
    {
        PendingDecision pending = state.PendingDecision!;
        var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id);
        ApplyResult result = runtime is null ? GameEngine.Apply(state, submission, behavior) : GameEngine.Apply(state, submission, behavior, runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }
}
