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
        Assert.True(board.Actions.Count > 1); // Exact hosted/personal splits stay separate.
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
        LegalActionViewModel broken = board.Actions.First(action => action.Parameters is RunBreakParameters { Quantity: 1, HostedCredits: 0 });
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
