using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class BoardSpatialActionsTests
{
    [Theory]
    [InlineData(ActionKind.RezCard)]
    [InlineData(ActionKind.RezIce)]
    [InlineData(ActionKind.AdvanceCard)]
    [InlineData(ActionKind.ScoreAgenda)]
    [InlineData(ActionKind.TrashTaggedResource)]
    public void CardLocalActionsUseTheirSourceAndIssuedId(ActionKind kind)
    {
        ActionParameters parameters = kind switch
        {
            ActionKind.RezCard => new RezCardParameters(new("a"), 2),
            ActionKind.RezIce => new RunIceParameters(new("a")),
            ActionKind.AdvanceCard => new AdvanceCardParameters(new("a"), 1),
            ActionKind.ScoreAgenda => new ScoreAgendaParameters(new("a"), 2, 3),
            ActionKind.TrashTaggedResource => new TrashTaggedResourceParameters(new("a"), 1, 2),
            _ => new RunCardPaymentParameters(new("a")),
        };
        LegalActionViewModel issued = Action("action", kind, parameters);
        BoardSpatialAction spatial = Assert.Single(BoardInteractionProjector.Project(View(issued)).SpatialActions);
        Assert.Same(issued, spatial.Action);
        Assert.Equal("a", spatial.SourceId);
        Assert.Empty(spatial.Targets);
        Assert.False(spatial.Automatic);
    }

    [Theory]
    [InlineData(ActionKind.StartRun)]
    [InlineData(ActionKind.PurgeVirusCounters)]
    [InlineData(ActionKind.RemoveTag)]
    [InlineData(ActionKind.PassPriority)]
    [InlineData(ActionKind.EndTurn)]
    [InlineData(ActionKind.JackOut)]
    [InlineData(ActionKind.ContinueRun)]
    [InlineData(ActionKind.ChooseNumber)]
    [InlineData(ActionKind.UseTrigger)]
    [InlineData(ActionKind.DeclineTrigger)]
    [InlineData(ActionKind.OrderTriggers)]
    [InlineData(ActionKind.UsePrevention)]
    [InlineData(ActionKind.DeclinePrevention)]
    [InlineData(ActionKind.UseReplacement)]
    [InlineData(ActionKind.DeclineReplacement)]
    [InlineData(ActionKind.TrashAccessedCard)]
    [InlineData(ActionKind.NoAccessAction)]
    [InlineData(ActionKind.StealAgenda)]
    public void GlobalAndAbstractDecisionsRemainButtons(ActionKind kind) =>
        Assert.Empty(BoardSpatialActions.Project(View(Action("button", kind, new NoActionParameters()))));

    [Fact]
    public void AbilityTargetsAndPaymentCardsRequireSeparateSelections()
    {
        var ability = new AbilityActionParameters(new("a"), new("definition"), "ability", BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility, [new(BehaviorCostKind.Additional, 1, additionalKey: "trash", additionalKind: BehaviorAdditionalCostKind.TrashCards, additionalTarget: BehaviorTarget.CardsInZone(BehaviorZone.Grip, new BehaviorCardFilter(ownerSide: PlayerSide.Runner)))], [new("b")],
            new(BehaviorAbilityUsageLimit.Unlimited, 0), new string('a', 64), new string('b', 64), new("rig"), ZoneKind.RunnerRig,
            [new(0, "trash", [new("c")])]);
        var view = View(Action("ability", ActionKind.UseAbility, ability));
        Assert.True(ActionsRegionPresenter.IsTargetedAction(view.LegalActions[0]));
        var selection = new BoardSpatialSelection(BoardSpatialActions.Project(view));
        Assert.Empty(selection.CompleteActions);
        Assert.Equal("b", Assert.Single(selection.Candidates).Id);
        Assert.Null(Assert.Single(selection.Candidates).PaymentKind);
        Assert.False(selection.Toggle(new(BoardTargetKind.Card, "c", "Target 1")));
        Assert.True(selection.Toggle(Assert.Single(selection.Candidates)));
        Assert.Empty(selection.CompleteActions);
        BoardTarget cost = Assert.Single(selection.Candidates);
        Assert.Equal("c", cost.Id);
        Assert.Equal(BehaviorAdditionalCostKind.TrashCards, cost.PaymentKind);
        Assert.StartsWith("Pay card cost", cost.Role, StringComparison.Ordinal);
        Assert.True(selection.Toggle(cost));
        Assert.Equal("ability", Assert.Single(selection.CompleteActions).Action.Id.Value);
        Assert.True(selection.Toggle(selection.Selected[0]));
        Assert.Empty(selection.Selected);
        Assert.Empty(selection.CompleteActions);
        Assert.Equal(1, view.Runner.Credits); // Local selection does not alter the publication.
    }

    [Fact]
    public void MultipleUnorderedTargetsAllowAnyClickOrderAndRejectIncompleteSets()
    {
        var view = View(Choose("ab", "a", "b"), Choose("ac", "a", "c"));
        Assert.All(view.LegalActions, action => Assert.True(ActionsRegionPresenter.IsTargetedAction(action)));
        var selection = new BoardSpatialSelection(BoardSpatialActions.Project(view));
        Assert.True(selection.Toggle(new(BoardTargetKind.Card, "b")));
        Assert.Equal("a", Assert.Single(selection.Candidates).Id);
        Assert.False(selection.Toggle(new(BoardTargetKind.Card, "c")));
        Assert.Empty(selection.CompleteActions);
        Assert.True(selection.Toggle(new(BoardTargetKind.Card, "a")));
        Assert.Equal("ab", Assert.Single(selection.CompleteActions).Action.Id.Value);
    }

    [Fact]
    public void OrderedChoicesPreserveTheirIssuedOrder()
    {
        MatchViewModel view = View(Choose("ab", "a", "b"), Choose("ba", "b", "a")) with
        { CardSelection = Context() with { Mode = BehaviorChoiceMode.OrderSelection } };
        var selection = new BoardSpatialSelection(BoardSpatialActions.Project(view));
        Assert.True(selection.Toggle(new(BoardTargetKind.Card, "b", "Position 1")));
        Assert.True(selection.Toggle(new(BoardTargetKind.Card, "a", "Position 2")));
        Assert.Equal("ba", Assert.Single(selection.CompleteActions).Action.Id.Value);
    }

    [Fact]
    public void HiddenInstalledCardsUsePositionsAndCentralAccessUsesAggregateSource()
    {
        var view = View(Action("hidden", ActionKind.ChooseBreachCandidate, new BreachCandidateParameters(new("hidden-root"), null)),
            Action("hq", ActionKind.ChooseBreachCandidate, new BreachCandidateParameters(null, BreachHiddenCandidateSource.CorpHand))) with
        {
            Servers = [new("hq", ServerKind.Headquarters, [], [null])],
            CardPositions = [new("hidden-root", "hq", ZoneKind.ServerRoot, 0)],
        };
        IReadOnlyList<BoardSpatialAction> actions = BoardSpatialActions.Project(view);
        Assert.Equal(2, actions.Count);
        Assert.Equal(new BoardTarget(BoardTargetKind.Card, "hidden-root", "Access card"), Assert.Single(actions[0].Targets));
        Assert.Equal(new BoardTarget(BoardTargetKind.Server, "hq", "Access source"), Assert.Single(actions[1].Targets));
        Assert.DoesNotContain(BoardSpatialActions.Cards(view), card => card.Id == "hidden-root");
        Assert.Single(BoardSpatialActions.Project(view with { CardPositions = [] }));
    }

    [Fact]
    public void UnexposedTargetsAndOpponentDecisionsFailClosed()
    {
        MatchViewModel view = View(Choose("hidden", "unexposed"));
        Assert.Empty(BoardSpatialActions.Project(view));
        view = View(Choose("a", "a"));
        Assert.Empty(BoardSpatialActions.Project(view with { Decision = view.Decision! with { OwnerSide = PlayerSide.Corp } }));
        Assert.Empty(BoardSpatialActions.Project(view with { Outcome = new(GameOutcomeKind.Won, PlayerSide.Corp, GameWinReason.DeckOut) }));
    }

    [Fact]
    public void SearchCandidatesAreSelectableOnlyThroughAuthorizedViews()
    {
        MatchViewModel view = View(Choose("search", "search-card"));
        Assert.Empty(BoardSpatialActions.Project(view));
        Assert.Single(BoardSpatialActions.Project(view with { ChoiceCards = [Card("search-card")] }));
    }

    [Fact]
    public void IceInstallRequiresBothCardAndExactDestination()
    {
        var view = View(Action("install", ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters("frame", IceSubroutineChoiceKind.InstallCard, new("a"), InstallDestinationKind.ServerIce, new("hq"))));
        var selection = new BoardSpatialSelection(BoardSpatialActions.Project(view));
        Assert.True(selection.Toggle(new(BoardTargetKind.Card, "a", "Install card")));
        Assert.Empty(selection.CompleteActions);
        Assert.True(selection.Toggle(new(BoardTargetKind.ServerIce, "hq", "Destination")));
        Assert.Single(selection.CompleteActions);
    }

    [Fact]
    public void AccessAndSubroutinePaymentsUseMaximumLegalHostedContribution()
    {
        var actions = new[] {
            Action("personal", ActionKind.TrashAccessedCard, new RunCardPaymentParameters(new("a"))),
            Action("hosted", ActionKind.TrashAccessedCard, new RunCardPaymentParameters(new("a"), 2)),
            Action("other", ActionKind.TrashAccessedCard, new RunCardPaymentParameters(new("b"))),
            Action("pay-personal", ActionKind.ResolveIceSubroutine, new IceSubroutineChoiceParameters("frame", IceSubroutineChoiceKind.PayCredits, creditCost: 3)),
            Action("pay-hosted", ActionKind.ResolveIceSubroutine, new IceSubroutineChoiceParameters("frame", IceSubroutineChoiceKind.PayCredits, creditCost: 3, hostedCredits: 2)),
            Action("distinct-cost", ActionKind.ResolveIceSubroutine, new IceSubroutineChoiceParameters("frame", IceSubroutineChoiceKind.PayCredits, creditCost: 2)) };
        Assert.Equal(["hosted", "other", "pay-hosted", "distinct-cost"], RunPaymentPreference.Apply(actions).Select(action => action.Id.Value));
    }

    [Fact]
    public void IncrementalSubmissionReacquiresActionsAndFinalizesExactlyOnce()
    {
        ObservationCardSelection context = Context();
        var submission = new BoardSelectionSubmission(context, ["b", "a"]);
        MatchViewModel initial = Incremental(context, 1);
        Assert.Equal("add-b-1", submission.Next(initial)!.Id.Value);
        Assert.Null(submission.Next(initial));
        MatchViewModel next = Incremental(context with { Selected = ["b"] }, 2);
        Assert.Equal("add-a-2", submission.Next(next)!.Id.Value);
        next = Incremental(context with { Selected = ["b", "a"] }, 3);
        Assert.Equal("finish-3", submission.Next(next)!.Id.Value);
        Assert.True(submission.Finished);
        Assert.Null(submission.Next(next));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InterruptedIncrementalSubmissionNeverContinuesAcrossDifferentState(bool continuationChanged)
    {
        ObservationCardSelection context = Context();
        var submission = new BoardSelectionSubmission(context, ["a", "b"]);
        Assert.NotNull(submission.Next(Incremental(context, 1)));
        var unexpected = continuationChanged ? context with { ContinuationId = "different", Selected = ["a"] } : context with { Selected = ["c"] };
        Assert.Null(submission.Next(Incremental(unexpected, 2)));
        Assert.NotNull(submission.Error);
        Assert.Null(submission.Next(Incremental(context with { Selected = ["a"] }, 3)));
    }

    [Fact]
    public void IncrementalConfirmationRespectsCardinalityAndAcceptedPrefix()
    {
        ObservationCardSelection context = Context() with { Cardinality = BehaviorChoiceCardinalityKind.Exactly, Count = 2, Selected = ["a"] };
        Assert.False(BoardSelectionSubmission.CanConfirm(context, ["a"]));
        Assert.False(BoardSelectionSubmission.CanConfirm(context, ["b", "a"]));
        Assert.False(BoardSelectionSubmission.CanConfirm(context, ["a", "a"]));
        Assert.True(BoardSelectionSubmission.CanConfirm(context, ["a", "b"]));
    }

    private static ObservationCardSelection Context() => new("frame:choice", "choice", BehaviorChoiceMode.Mandatory,
        BehaviorChoiceCardinalityKind.AnyNumber, null, ["a", "b", "c"], []);
    private static MatchViewModel Incremental(ObservationCardSelection context, int revision) => View(
        [.. context.Candidates.Except(context.Selected).Select(id => Action($"add-{id}-{revision}", ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("choice", cardIds: [new(id)], finalizeSelection: false))),
        Action($"finish-{revision}", ActionKind.ChooseBehavior, new BehaviorChoiceParameters("choice", cardIds: context.Selected.Select(id => new CardInstanceId(id)).ToArray()))]) with
    { CardSelection = context, Decision = View().Decision! with { Revision = revision } };
    private static LegalActionViewModel Choose(string id, params string[] cards) => Action(id, ActionKind.ChooseBehavior,
        new BehaviorChoiceParameters("choice", cardIds: cards.Select(card => new CardInstanceId(card)).ToArray()));
    private static LegalActionViewModel Action(string id, ActionKind kind, ActionParameters parameters) => new(new(id), kind, kind.ToString(), parameters);
    private static CardViewModel Card(string id) => new(id, "definition", id, "Program", true, false, 0, null, null, null, null, [], "");
    private static MatchViewModel View(params LegalActionViewModel[] actions) => new(PlayerSide.Runner,
        new("corp", PlayerSide.Corp, 1, 1, 0), new("runner", PlayerSide.Runner, 1, 1, 0), [],
        new(ZoneKind.CorpHand, 0, []), new(ZoneKind.CorpDeck, 0, []), new(ZoneKind.RunnerGrip, 0, []), new(ZoneKind.RunnerStack, 0, []),
        [Card("a"), Card("b"), Card("c")], [], [], new("decision", 1, new("runner"), PlayerSide.Runner, DecisionKind.RunTiming, "Choose"),
        null, new(GameOutcomeKind.Ongoing, null, null), actions);
}
