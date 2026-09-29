using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class BreakAllPresenterTests
{
    [Fact]
    public void BreakAllBoostsThenBreaksThroughIssuedActionsUntilNothingIsLeft()
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        MatchViewModel view = View(state, catalog);
        BreakAllPlan first = Assert.IsType<BreakAllPlan>(BreakAllPresenter.Plan(view, "card:5"));
        Assert.Equal(ActionKind.PumpBreaker, first.Next.Kind);
        Assert.Equal(1, first.Boosts);
        Assert.NotNull(first.EstimatedCredits);
        Assert.True(first.Affordable);
        int before = view.Runner.Credits + view.Run!.RunCredits;
        int steps = 0;
        while (BreakAllPresenter.Plan(view, "card:5") is { } plan)
        {
            Assert.Same(view.LegalActions.Single(action => action.Id == plan.Next.Id), plan.Next);
            state = Apply(state, catalog, plan.Next.Id);
            view = View(state, catalog);
            Assert.True(++steps < 10, "Break all did not converge.");
        }
        Assert.All(state.RunState!.Encounter!.Subroutines, subroutine => Assert.True(subroutine.Broken));
        int spent = before - (view.Runner.Credits + view.Run!.RunCredits);
        Assert.Equal(first.EstimatedCredits, spent);
        Assert.Empty(StateInvariantValidator.Collect(state, catalog));
    }

    [Fact]
    public void BreakAllIsUnavailableForAnIncompatibleBreakerOrOutsideTheEncounterWindow()
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        MatchViewModel view = View(state, catalog);
        Assert.Null(BreakAllPresenter.Plan(view, "card:6"));
        Assert.Null(BreakAllPresenter.Plan(view with { Viewer = PlayerSide.Corp }, "card:5"));
        Assert.Null(BreakAllPresenter.Plan(view with { Run = view.Run! with { Step = RunTimingStep.MovementPaidWindow } }, "card:5"));
    }

    [Fact]
    public void AnEstimateAboveTheRunnersCreditsIsMarkedUnaffordable()
    {
        (GameState state, BehaviorCatalog catalog) = Encounter();
        MatchViewModel view = View(state, catalog);
        MatchViewModel broke = view with { Runner = view.Runner with { Credits = 0 }, Run = view.Run! with { RunCredits = 0 } };
        BreakAllPlan plan = Assert.IsType<BreakAllPlan>(BreakAllPresenter.Plan(broke, "card:5"));
        Assert.False(plan.Affordable);
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
