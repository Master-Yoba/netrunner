using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class MatchPresentationProjectorTests
{
    [Fact]
    public async Task ClientPositionsRemainAvailableWithoutEnteringAgentObservations()
    {
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions { Delay = static (_, _) => ValueTask.CompletedTask }, new FirstLegalAgent());
        MatchSnapshot snapshot = await coordinator.StartAsync();
        Assert.NotEmpty(snapshot.View.CardPositions);
        Observation agentObservation = await coordinator.GetObservationAsync(PlayerSide.Runner);
        string properties = AllProperties(agentObservation);
        foreach (CardPositionViewModel hidden in snapshot.View.CardPositions)
            Assert.DoesNotContain(hidden.CardId, properties, StringComparison.Ordinal);
        Assert.All(snapshot.View.Servers.SelectMany(server => server.Ice.Concat(server.Root)), card => Assert.Null(card));
        Assert.DoesNotContain(snapshot.View.ChoiceCards, card => snapshot.View.CardPositions.Any(position => position.CardId == card.Id));
    }

    [Fact]
    public async Task OpponentAgentReceivesNoClientPositionSidecar()
    {
        var agent = new CaptureAgent();
        await using MatchCoordinator coordinator = new(FullMatchDefinition.Developer(42), agent,
            new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp, Delay = static (_, _) => ValueTask.CompletedTask });
        MatchSnapshot started = await coordinator.StartAsync();
        LegalAction keep = started.Observation.LegalActions.Single(action => action.Parameters is NumberChoiceParameters { Value: 0 });
        ObservationDecision decision = started.Observation.Decision!;
        Assert.True((await coordinator.SubmitActionAsync(new ActionSubmission(decision.Owner, decision.Id, decision.Revision, keep.Id))).Accepted);
        Observation observed = await agent.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PlayerSide.Runner, observed.Viewer);
        Assert.DoesNotContain("BoardContext", AllProperties(observed), StringComparison.Ordinal);
        Assert.DoesNotContain("ActionSources", AllProperties(observed), StringComparison.Ordinal);
        foreach (CardViewModel hidden in started.View.CorpHand.VisibleCards)
            Assert.DoesNotContain(hidden.Id, AllProperties(observed), StringComparison.Ordinal);
    }

    [Fact]
    public void ChoicePresentationSortsCandidatesRetainsAcceptedPrefixAndDoesNotMutateState()
    {
        GameState before = SearchChoice([]);
        string stateJson = EngineJson.Serialize(before);
        MatchPresentationContext context = MatchPresentationProjector.Build(before, PlayerSide.Runner);
        Assert.Equal(["card:a", "card:z"], context.Selection!.Candidates);
        Assert.Equal(["card:a", "card:z"], context.ChoiceCards.Select(card => card["id"]));
        Assert.DoesNotContain(context.ChoiceCards, card => Equals(card["id"], "card:unselected"));
        Assert.Null(ObservationBuilder.ObserveCard(before, PlayerSide.Runner, new("card:unselected")));
        MatchViewModel view = ObservationViewMapper.Build(ObservationBuilder.Build(before, PlayerSide.Runner),
            CardMetadataResolvers.Default, BehaviorCatalog.Empty, handSizeProjection: null, presentationContext: context);
        Assert.Equal(2, view.ChoiceCards.Count);
        Assert.NotEmpty(BoardSpatialActions.Project(view));
        Assert.Equal(stateJson, EngineJson.Serialize(before));
        MatchPresentationContext partial = MatchPresentationProjector.Build(SearchChoice([new("card:z")]), PlayerSide.Runner);
        Assert.Equal(context.Selection.ContinuationId, partial.Selection!.ContinuationId);
        Assert.Equal(context.Selection.Candidates, partial.Selection.Candidates);
        Assert.Equal(["card:z"], partial.Selection.Selected);
        Assert.Equal(MatchPresentationContext.Empty, MatchPresentationProjector.Build(before, PlayerSide.Corp));
        Assert.DoesNotContain("definition:a", AllProperties(ObservationBuilder.Build(before, PlayerSide.Runner)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlayerSide.Runner, true)]
    [InlineData(PlayerSide.Corp, false)]
    public void TriggerSourcesRequireDecisionOwnershipAndCurrentVisibility(PlayerSide controller, bool visible)
    {
        GameState baseline = GameState.CreateNew(217);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        var source = new CardInstanceState(new("card:action-source"), new("test:source"), grip.Id, PlayerSide.Runner);
        ZoneState[] zones = baseline.Zones.Select(zone => zone.Id == grip.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [source.Id]) : zone).ToArray();
        PlayerId owner = baseline.Players.Single(player => player.Side == controller).Id;
        RulesState defaults = RulesState.CreateDefault(baseline.Players);
        var rules = new RulesState(defaults.Version, defaults.Players, defaults.Programs, defaults.Counters, defaults.LingeringEffects,
            [new TriggerRegistration(new("trigger:source"), owner, TriggerMode.Optional,
                new TriggerCondition(OccurrenceKind.CreditAdjustment), "fixture.source", source.Id,
                new CostBundle(), new EffectBundle(new CreditAdjustmentEffect(owner, 2)))]);
        GameState state = Copy(baseline, [source], zones, rules: rules);
        GameState prompt = GameEngine.ResolveEffects(state, owner, new CostBundle(), new EffectBundle(new CreditAdjustmentEffect(owner, 1)));
        string before = EngineJson.Serialize(prompt);
        MatchPresentationContext context = MatchPresentationProjector.Build(prompt, controller);
        Assert.Equal(visible ? 2 : 0, context.ActionSources.Count);
        Assert.All(context.ActionSources, sourceContext =>
        {
            Assert.Equal(source.Id, sourceContext.CardId);
            Assert.Contains(prompt.PendingDecision!.LegalActions, action => action.Id == sourceContext.ActionId);
        });
        PlayerSide opponent = controller == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        Assert.Empty(MatchPresentationProjector.Build(prompt, opponent).ActionSources);
        Assert.Equal(before, EngineJson.Serialize(prompt));
    }

    private static GameState SearchChoice(IReadOnlyList<CardInstanceId> selected)
    {
        GameState state = GameState.CreateNew(219);
        ZoneState stack = state.GetZone(ZoneKind.RunnerStack);
        ZoneState grip = state.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState[] cards = [new(new("card:source"), new("definition:source"), grip.Id, PlayerSide.Runner),
            new(new("card:z"), new("definition:z"), stack.Id, PlayerSide.Runner),
            new(new("card:a"), new("definition:a"), stack.Id, PlayerSide.Runner),
            new(new("card:unselected"), new("definition:unselected"), stack.Id, PlayerSide.Runner)];
        ZoneState[] zones = state.Zones.Select(zone => zone.Id == stack.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [new("card:z"), new("card:a"), new("card:unselected")])
            : zone.Id == grip.Id ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [new("card:source")]) : zone).ToArray();
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        var choice = new BehaviorChoiceMaterialization("search", runner, BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinalityKind.AnyNumber, null, candidateCardIds: [new("card:z"), new("card:a")], selectedCardIds: selected);
        var frame = new BehaviorResolutionFrame(1, ResolutionBehaviorIds.CardBehavior, new("definition:source"), new("card:source"),
            runner, runner, "catalog", "definition", BehaviorResolutionStage.Execute, choice: choice, frameIdentity: "frame:search");
        LegalAction[] actions = [.. choice.CandidateCardIds.Except(selected).Select(id => new LegalAction(new("action:add-" + id.Value),
            ActionKind.ChooseBehavior, "Add", new BehaviorChoiceParameters(choice.Key, cardIds: [id], finalizeSelection: false))),
            new(new("action:finish"), ActionKind.ChooseBehavior, "Finish", new BehaviorChoiceParameters(choice.Key, cardIds: selected))];
        var decision = new PendingDecision(new("decision:search"), 1 + selected.Count, runner, DecisionKind.BehaviorChoice, "Choose cards", actions);
        return Copy(state, cards, zones, decision, [frame]);
    }

    private static GameState Copy(GameState state, IReadOnlyList<CardInstanceState> cards, IReadOnlyList<ZoneState> zones,
        PendingDecision? decision = null, IReadOnlyList<ResolutionFrame>? frames = null, RulesState? rules = null) =>
        new(state.GameId, state.RulesVersion, state.CardDataVersion, state.Players, cards, zones, state.Servers,
            state.Turn, state.Outcome, state.RandomState, state.Allocator, pendingDecision: decision, resolutionStack: frames, rulesState: rules);

    private static string AllProperties(Observation observation) => EngineJson.Serialize(typeof(Observation).GetProperties()
        .ToDictionary(property => property.Name, property => property.GetValue(observation)));

    private sealed class CaptureAgent : IGameAgent
    {
        public TaskCompletionSource<Observation> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
        {
            Received.TrySetResult(observation);
            return ValueTask.FromResult(observation.LegalActions[0].Id);
        }
    }
}
