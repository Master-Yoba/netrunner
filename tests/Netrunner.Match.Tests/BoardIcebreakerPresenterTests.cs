using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class BoardIcebreakerPresenterTests
{
    [Fact]
    public void BoardChoicesDriveAnEngineEncounterWithExactPaymentsAndFreshStrength()
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        string before = EngineJson.Serialize(state);
        MatchViewModel view = View(state, catalog);
        BoardIcebreakerViewModel board = Assert.IsType<BoardIcebreakerViewModel>(BoardIcebreakerPresenter.Present(view, "card:5"));
        Assert.Equal(3, board.Strength!.EffectiveStrength);
        Assert.Equal(4, board.Encounter.EffectiveStrength);
        Assert.DoesNotContain(board.Actions, action => action.Kind == ActionKind.BreakSubroutines);
        Assert.Equal(before, EngineJson.Serialize(state));
        Assert.Single(board.Actions); // One boost, using the largest issued hosted-credit portion.
        LegalActionViewModel pump = Assert.Single(board.Actions, action => action.Parameters is RunBreakerParameters { HostedCredits: 2 });
        Assert.Same(view.LegalActions.Single(action => action.Id == pump.Id), pump);
        int credits = view.Runner.Credits;
        int hosted = view.Run!.RunCredits;
        state = Apply(state, catalog, pump.Id);
        view = View(state, catalog);
        board = Assert.IsType<BoardIcebreakerViewModel>(BoardIcebreakerPresenter.Present(view, "card:5"));
        Assert.Equal(credits, view.Runner.Credits);
        Assert.Equal(hosted - 2, view.Run!.RunCredits);
        Assert.Equal(4, board.Strength!.EffectiveStrength);
        Assert.DoesNotContain(board.Actions, action => action.Id == pump.Id);
        // At the ICE's strength a pump only wastes credits, so the board no longer offers one.
        Assert.DoesNotContain(board.Actions, action => action.Kind == ActionKind.PumpBreaker);
        LegalActionViewModel broken = board.Actions.First(action => action.Parameters is RunBreakParameters { Quantity: 1, HostedCredits: 1 });
        IReadOnlyList<int> targets = BoardIcebreakerPresenter.BrokenOrdinals(board.Encounter, broken);
        state = Apply(state, catalog, broken.Id);
        EncounterViewModel after = View(state, catalog).Run!.Encounter!;
        Assert.All(after.Subroutines, subroutine => Assert.Equal(targets.Contains(subroutine.Ordinal), subroutine.Broken));
        Assert.Empty(StateInvariantValidator.Collect(state, catalog));
    }

    [Fact]
    public void BoardChoicesRequireRunnerPriorityVisibleInstalledSourceAndCurrentEncounter()
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        MatchViewModel view = View(state, catalog);
        Assert.Null(BoardIcebreakerPresenter.Present(view, "missing"));
        Assert.Null(BoardIcebreakerPresenter.Present(view with { Viewer = PlayerSide.Corp }, "card:5"));
        Assert.Null(BoardIcebreakerPresenter.Present(view with { Decision = null }, "card:5"));
        Assert.Null(BoardIcebreakerPresenter.Present(view with { Decision = view.Decision! with { Kind = DecisionKind.InterruptChoice } }, "card:5"));
        Assert.Null(BoardIcebreakerPresenter.Present(view with { RunnerRig = [] }, "card:5"));
        Assert.Null(BoardIcebreakerPresenter.Present(view with
        { RunnerRig = view.RunnerRig.Select(card => card with { IsKnown = false }).ToArray() }, "card:5"));
        Assert.Null(BoardIcebreakerPresenter.Present(view with { Run = null }, "card:5"));
        Assert.Null(BoardIcebreakerPresenter.Present(view with
        { Run = view.Run! with { Priority = view.Run.Priority! with { Holder = PlayerSide.Corp } } }, "card:5"));
        Assert.Null(BoardIcebreakerPresenter.Present(view with
        { Run = view.Run! with { Step = RunTimingStep.MovementPaidWindow } }, "card:5"));
    }

    [Fact]
    public void BreakPreviewSkipsAlreadyBrokenSubroutinesAndDoesNotInventHostedTargets()
    {
        var encounter = new EncounterViewModel("encounter:test", null, RunEncounterStatus.Initialized,
            [new(0, true, false), new(1, false, false), new(2, true, false), new(3, false, false)]);
        var action = new LegalActionViewModel(new("action:test"), ActionKind.BreakSubroutines, "Break two",
            new RunBreakParameters(new("card:5"), 2));
        Assert.Equal([1, 3], BoardIcebreakerPresenter.BrokenOrdinals(encounter, action));
        Assert.Empty(BoardIcebreakerPresenter.BrokenOrdinals(encounter, action with
        { Parameters = new RunBreakParameters(new("card:5"), 1, subroutineId: new("subroutine:opaque")) }));
    }

    [Fact]
    public void SelectedTargetsReturnOnlyIssuedActionsForThatExactSet()
    {
        var encounter = new EncounterViewModel("encounter:test", null, RunEncounterStatus.Initialized,
            [new(0, false, false), new(1, false, false), new(2, false, false)]);
        LegalActionViewModel[] actions = Enumerable.Range(0, 2).Select(hosted => new LegalActionViewModel(
            new($"action:{hosted}"), ActionKind.BreakSubroutines, "Break selected",
            new RunBreakParameters(new("card:5"), 2, hosted, subroutineOrdinals: [0, 2]))).ToArray();
        var breaker = new BoardIcebreakerViewModel(encounter.Id, null!, encounter, null, actions);
        Assert.Empty(BoardIcebreakerPresenter.SelectedBreaks(breaker, []));
        Assert.Empty(BoardIcebreakerPresenter.SelectedBreaks(breaker, [0]));
        Assert.Empty(BoardIcebreakerPresenter.SelectedBreaks(breaker, [0, 1]));
        IReadOnlyList<LegalActionViewModel> payments = BoardIcebreakerPresenter.SelectedBreaks(breaker, [2, 0]);
        Assert.Same(actions[1], Assert.Single(payments));
        Assert.Equal([0, 2], BoardIcebreakerPresenter.BrokenOrdinals(encounter, actions[0]));
    }

    [Fact]
    public void IncompatibleBreakerHasNoControlsOrFallbackActionsWhileMatchingBreakerCanBoost()
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner, catalog);
        MatchViewModel view = View(state, catalog);
        Assert.True(view.Run!.Encounter!.Breakers.Single(breaker => breaker.CardId == "card:5").CanInterfaceEncounter);
        Assert.False(view.Run.Encounter.Breakers.Single(breaker => breaker.CardId == "card:6").CanInterfaceEncounter);
        Assert.Contains(view.LegalActions, action => action.Parameters is RunBreakerParameters { BreakerId.Value: "card:6" });
        Assert.Null(BoardIcebreakerPresenter.Present(view, "card:6"));
        BoardIcebreakerViewModel matching = Assert.IsType<BoardIcebreakerViewModel>(BoardIcebreakerPresenter.Present(view, "card:5"));
        Assert.Equal(ActionKind.PumpBreaker, Assert.Single(matching.Actions).Kind);
        var snapshot = new MatchSnapshot(1, observation, view, [], null, false);
        IReadOnlyList<LegalActionViewModel> controls = ActionsRegionPresenter.Present(snapshot).DisplayActions;
        Assert.DoesNotContain(controls, action => BoardIcebreakerPresenter.Source(action) == "card:6");
        Assert.Equal(2, controls.Count); // The matching boost plus pass priority.
        Assert.Contains(controls, action => action.Kind == ActionKind.PassPriority);
    }

    [Theory]
    [InlineData(IceType.Barrier, IceType.Barrier, true)]
    [InlineData(IceType.Barrier, IceType.CodeGate, false)]
    [InlineData(IceType.CodeGate, IceType.CodeGate, true)]
    [InlineData(IceType.Sentry, IceType.Barrier, false)]
    [InlineData(IceType.Barrier, IceType.Any, true)]
    [InlineData(IceType.CodeGate, IceType.Any, true)]
    [InlineData(IceType.Sentry, IceType.Any, true)]
    public void CompatibilityUsesTypedInterfacesIncludingWildcardBreakers(IceType iceType, IceType breakerType, bool matches)
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        var injected = new BehaviorCatalogBuilder("ui-interface-test")
            .AddIce(new IceBehaviorSpec(new("30072"), 4, iceType, 0, [], []))
            .AddIcebreaker(new IcebreakerBehaviorSpec(new("30006"), 1, breakerType, 1, 2, 2, 1)).Build();
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner, catalog);
        MatchViewModel view = ObservationViewMapper.Build(observation, CardMetadataResolvers.Default, injected);
        Assert.Equal(matches, Assert.Single(view.Run!.Encounter!.Breakers).CanInterfaceEncounter);
        Assert.Equal(matches, BoardIcebreakerPresenter.Present(view, "card:5") is not null);
    }

    [Fact]
    public void RepeatedBoostsSpendHostedThenMixedThenPersonalCreditsThroughIssuedIds()
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        Assert.Equal(5, state.RunState!.RunCredits);
        foreach (int expectedHosted in new[] { 2, 2, 1, 0 })
        {
            string before = EngineJson.Serialize(state);
            MatchViewModel view = View(state, catalog);
            // Boosts past the ICE's strength leave the board panel, so take them from the
            // full encounter list, which applies the same payment preference.
            LegalActionViewModel boost = Assert.Single(BoardIcebreakerPresenter.EncounterActions(view, view.LegalActions),
                action => action.Kind == ActionKind.PumpBreaker && BoardIcebreakerPresenter.Source(action) == "card:5");
            Assert.Equal(expectedHosted, Assert.IsType<RunBreakerParameters>(boost.Parameters).HostedCredits);
            Assert.Same(view.LegalActions.Single(action => action.Id == boost.Id), boost);
            Assert.Equal(before, EngineJson.Serialize(state));
            int personal = view.Runner.Credits;
            int hosted = view.Run!.RunCredits;
            state = Apply(state, catalog, boost.Id);
            MatchViewModel after = View(state, catalog);
            Assert.Equal(hosted - expectedHosted, after.Run!.RunCredits);
            Assert.Equal(personal - (2 - expectedHosted), after.Runner.Credits);
        }
        BoardIcebreakerViewModel ready = BoardIcebreakerPresenter.Present(View(state, catalog), "card:5")!;
        LegalActionViewModel breakOnly = Assert.Single(BoardIcebreakerPresenter.SelectedBreaks(ready, [0]));
        Assert.Equal(0, Assert.IsType<RunBreakParameters>(breakOnly.Parameters).HostedCredits);
        int credits = View(state, catalog).Runner.Credits;
        state = Apply(state, catalog, breakOnly.Id);
        Assert.Equal(credits - 1, View(state, catalog).Runner.Credits);
        Assert.True(Assert.Single(state.RunState!.Encounter!.Subroutines).Broken);
        Assert.Empty(StateInvariantValidator.Collect(state, catalog));
    }

    [Fact]
    public void HostedCounterAbilitiesKeepTheirDistinctTargetsWithoutStrengthMetadata()
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        MatchViewModel view = View(state, catalog);
        LegalActionViewModel[] actions = Enumerable.Range(0, 2).Select(ordinal => new LegalActionViewModel(
            new($"action:host-{ordinal}"), ActionKind.BreakSubroutines, "Spend a counter",
            new RunBreakParameters(new("card:5"), 1, subroutineId: new($"subroutine:{ordinal}"), subroutineOrdinals: [ordinal]))).ToArray();
        view = view with { LegalActions = actions, Run = view.Run! with { Encounter = view.Run.Encounter! with { Breakers = [] } } };
        BoardIcebreakerViewModel breaker = BoardIcebreakerPresenter.Present(view, "card:5")!;
        Assert.Equal(2, breaker.Actions.Count);
        Assert.Same(actions[1], Assert.Single(BoardIcebreakerPresenter.SelectedBreaks(breaker, [1])));
    }

    private static MatchViewModel View(GameState state, IBehaviorCatalog catalog) =>
        ObservationViewMapper.Build(ObservationBuilder.Build(state, PlayerSide.Runner, catalog));

    private static (GameState, BehaviorCatalog) Encounter()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        BehaviorCatalog catalog = document.CreateBehaviorCatalog(GatewayBehaviorCatalog.Create());
        GameState state = M2ScenarioSetup.Build(document, catalog);
        state = GameEngine.BeginRunWithEvents(state, state.Servers.Single(server => server.Kind == ServerKind.Remote).Id,
            catalog, initiatingSource: new CardInstanceId(document.Run!.Source)).State;
        foreach (ActionKind kind in new[] { ActionKind.PassPriority, ActionKind.PassPriority, ActionKind.PassPriority,
            ActionKind.RezIce, ActionKind.PassPriority, ActionKind.PassPriority })
            state = Apply(state, catalog, state.PendingDecision!.LegalActions.Single(action => action.Kind == kind).Id);
        return (state, catalog);
    }

    private static GameState Apply(GameState state, IBehaviorCatalog catalog, ActionId id)
    {
        PendingDecision decision = state.PendingDecision!;
        ApplyResult result = GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, id), catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }
}
