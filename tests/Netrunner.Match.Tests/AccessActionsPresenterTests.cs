using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class AccessActionsPresenterTests
{
    [Fact]
    public void AccessExposesOneIssuedTrashPaymentAndLeaveWithoutReselectingTheCard()
    {
        MatchViewModel view = View();
        IReadOnlyList<LegalActionViewModel> actions = AccessActionsPresenter.Actions(view);
        Assert.Equal(["trash-2", "leave"], actions.Select(action => action.Id.Value));
        Assert.All(actions, action => Assert.Same(view.LegalActions.Single(issued => issued.Id == action.Id), action));
        Assert.Empty(BoardSpatialActions.Project(view));
        Assert.Equal("Trash · 3 credits (2 run + 1 personal)", AccessActionsPresenter.Label(actions[0], Card));
        Assert.Equal("Leave card", AccessActionsPresenter.Label(actions[1], Card));
        Assert.Equal(2, view.Run!.RunCredits);
        Assert.Equal(6, view.Runner.Credits);
    }

    [Fact]
    public void MissingOrUnaffordableTrashDoesNotCreateAnActionFromThePrintedCost()
    {
        MatchViewModel view = View();
        view = view with { LegalActions = view.LegalActions.Where(action => action.Kind == ActionKind.NoAccessAction).ToArray() };
        Assert.Equal(ActionKind.NoAccessAction, Assert.Single(AccessActionsPresenter.Actions(view)).Kind);
        Assert.Empty(AccessActionsPresenter.Actions(view with { LegalActions = [] }));
    }

    [Fact]
    public void AccessControlsRequireCurrentVisibleCardAndOwnedAccessDecision()
    {
        MatchViewModel view = View();
        Assert.Empty(AccessActionsPresenter.Actions(view with { Viewer = PlayerSide.Corp }));
        Assert.Empty(AccessActionsPresenter.Actions(view with { Decision = null }));
        Assert.Empty(AccessActionsPresenter.Actions(view with { Decision = view.Decision! with { OwnerSide = PlayerSide.Corp } }));
        Assert.Empty(AccessActionsPresenter.Actions(view with { Decision = view.Decision! with { Kind = DecisionKind.BehaviorChoice } }));
        Assert.Empty(AccessActionsPresenter.Actions(view with { Run = null }));
        Assert.Empty(AccessActionsPresenter.Actions(view with { Run = view.Run! with { Step = RunTimingStep.EncounterPaidWindow } }));
        Assert.Empty(AccessActionsPresenter.Actions(view with { Run = view.Run! with { Access = view.Run.Access with { Accessed = [Card with { IsKnown = false }] } } }));
        Assert.Empty(AccessActionsPresenter.Actions(view with { Outcome = new(GameOutcomeKind.Won, PlayerSide.Runner, GameWinReason.AgendaPoints) }));
        MatchViewModel changed = view with { Run = view.Run! with { Access = view.Run.Access with { Accessed = [Card with { Id = "other" }] } } };
        Assert.Equal(ActionKind.NoAccessAction, Assert.Single(AccessActionsPresenter.Actions(changed)).Kind);
    }

    [Fact]
    public void AgendaContinuationDoesNotOfferToLeaveOrInventAnOptionalSteal()
    {
        LegalActionViewModel pass = View().LegalActions.Single(action => action.Kind == ActionKind.NoAccessAction);
        Assert.Equal("Continue access", AccessActionsPresenter.Label(pass, Card with { Type = "Agenda" }));
    }

    private static CardViewModel Card => new("asset", "definition", "Asset", "Asset", true, false, 0, null, 99, null, null, [], "");
    private static MatchViewModel View() => new(PlayerSide.Runner,
        new("corp", PlayerSide.Corp, 6, 1, 0), new("runner", PlayerSide.Runner, 6, 1, 0), [],
        new(ZoneKind.CorpHand, 0, []), new(ZoneKind.CorpDeck, 0, []), new(ZoneKind.RunnerGrip, 0, []), new(ZoneKind.RunnerStack, 0, []),
        [], [], [], new("decision", 1, new("runner"), PlayerSide.Runner, DecisionKind.RunTiming, "Access"),
        new("run", "hq", PlayerSide.Runner, RunCrPhase.Success, RunTimingStep.BreachAccessAbility, 0, null, true, false, null, null, null,
            new([], 0, null, [Card], [Card], RunAccessState.NotStarted), 2, null),
        new(GameOutcomeKind.Ongoing, null, null),
        [.. Enumerable.Range(0, 3).Select(hosted => new LegalActionViewModel(new("trash-" + hosted), ActionKind.TrashAccessedCard,
            $"Trash asset for 3 credits ({hosted} hosted + {3 - hosted} personal)", new RunCardPaymentParameters(new("asset"), hosted))),
            new(new("leave"), ActionKind.NoAccessAction, "No action", new NoActionParameters())]);
}
