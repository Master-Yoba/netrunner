using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class UiRegionPresenterTests
{
    private static readonly string[] ExpectedFeedLines = ["newer", "newest"];
    private static readonly string[] ZoomSubtypes = ["Icebreaker", "Decoder"];

    [Fact]
    public void BoardPresenterOwnsCopiesOfBoardCollectionsAndFormatsRunPool()
    {
        MatchViewModel view = RepresentativeView();

        BoardRegionState board = BoardRegionPresenter.Present(view);

        Assert.Equal(view.Servers, board.Servers);
        Assert.Equal(view.RunnerRig, board.RunnerRig);
        Assert.Equal(view.RunnerHand.Count, board.RunnerHand.Count);
        Assert.Contains("Credits", board.RunnerSummary, StringComparison.Ordinal);
        Assert.Contains("Hand", board.CorpSummary, StringComparison.Ordinal);
        Assert.Same(view.Run, board.Run);
        Assert.NotSame(view.Servers, board.Servers);
        Assert.NotSame(view.RunnerRig, board.RunnerRig);

        MatchViewModel zeroPoolView = view with { Run = BaseRun() };
        Assert.DoesNotContain("Run pool", BoardRegionPresenter.Present(zeroPoolView).RunnerSummary, StringComparison.Ordinal);
        MatchViewModel hostedPoolView = zeroPoolView with
        {
            Run = zeroPoolView.Run! with { RunCredits = 1 },
        };
        Assert.Contains("Run pool 1", BoardRegionPresenter.Present(hostedPoolView).RunnerSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionsPresenterRetainsIssuedTargetsAndStatusWithoutMutableSetLeak()
    {
        MatchViewModel view = RepresentativeView();
        DecisionViewModel decision = new(
            "decision:test",
            1,
            new PlayerId("player:runner"),
            PlayerSide.Runner,
            DecisionKind.ChooseCard,
            "Choose a card");
        LegalActionViewModel targeted = new(
            new ActionId("action:target"),
            ActionKind.ChooseCard,
            "Choose a card",
            new CardChoiceParameters(new CardInstanceId("card:target")));
        MatchSnapshot snapshot = Snapshot(view with
        {
            Decision = decision,
            LegalActions = new[] { targeted },
        });

        ActionsRegionState actions = ActionsRegionPresenter.Present(snapshot);

        Assert.Equal(ActionsRegionStatus.AwaitingRunnerDecision, actions.Status);
        Assert.Same(decision, actions.Decision);
        Assert.Single(actions.Actions);
        Assert.Contains(new ActionId("action:target"), actions.TargetedActions);
        Assert.False(actions.TargetedActions is HashSet<ActionId>);
        Assert.Equal("card:target", ActionsRegionPresenter.GetTargetCardId(targeted.Parameters));
        Assert.True(ActionsRegionPresenter.IsTargetedAction(targeted));
    }

    [Fact]
    public void FocusPresenterSelectsEachRenderedFocusSeamFromSnapshotOnly()
    {
        MatchViewModel view = RepresentativeView() with { Run = BaseRun() };
        MatchSnapshot snapshot = Snapshot(view);
        RunFocusViewModel run = view.Run!;

        Assert.Equal(FocusRegionMode.PaidWindow, FocusRegionPresenter.Present(snapshot).Mode);

        RunFocusViewModel encounterRun = run with
        {
            Encounter = new EncounterViewModel(
                "encounter:test",
                run.CurrentIce,
                RunEncounterStatus.Initialized,
                Array.Empty<SubroutineViewModel>()),
        };
        Assert.Equal(
            FocusRegionMode.Encounter,
            FocusRegionPresenter.Present(Snapshot(view with { Run = encounterRun })).Mode);

        RunFocusViewModel accessRun = run with
        {
            Priority = null,
            Access = run.Access with { State = RunAccessState.InProgress },
        };
        Assert.Equal(
            FocusRegionMode.Access,
            FocusRegionPresenter.Present(Snapshot(view with { Run = accessRun })).Mode);

        RunFocusViewModel ordinaryRun = run with
        {
            Priority = null,
            Access = run.Access with { State = RunAccessState.NotStarted },
        };
        Assert.Equal(
            FocusRegionMode.Run,
            FocusRegionPresenter.Present(Snapshot(view with { Run = ordinaryRun })).Mode);

        Assert.Equal(
            FocusRegionMode.Outcome,
            FocusRegionPresenter.Present(
                Snapshot(view with { Run = null, Decision = null },
                    new RunOutcomeViewModel("run:test", PlayerSide.Runner, RunEndReason.BreachCompleted, true))).Mode);
        Assert.Equal(
            FocusRegionMode.Empty,
            FocusRegionPresenter.Present(Snapshot(view with { Run = null, Decision = null })).Mode);
    }

    [Fact]
    public void EventFeedPresenterTrimsOldLinesAndKeepsNewestAtTop()
    {
        var feed = new EventFeedRegionPresenter(maxVisibleLines: 2);
        feed.Append("old");
        feed.Append("newer");
        feed.Append("newest");

        Assert.Equal(ExpectedFeedLines, feed.Lines);
        Assert.Equal("newest\nnewer", feed.RenderedText);
        feed.Clear();
        Assert.Empty(feed.Lines);
    }

    [Fact]
    public void CardZoomPresenterFormatsAllVisibleStatsThroughTheSharedMarkupBoundary()
    {
        var card = new CardViewModel(
            "card:zoom",
            "30001",
            "Zoom Card",
            "Program",
            IsKnown: true,
            Rezzed: false,
            AdvancementCounters: 1,
            Cost: 2,
            Strength: 3,
            AgendaPoints: null,
            TrashCost: 1,
            Subtypes: ZoomSubtypes,
            RulesText: "<strong>Break</strong> <symbol:subroutine>.",
            Faction: "shaper",
            MemoryCost: 1,
            AdvancementRequirement: null);

        string formatted = CardZoomRegionPresenter.Format(card);

        Assert.Contains("Program · Icebreaker · Decoder", formatted, StringComparison.Ordinal);
        Assert.Contains("Cost · 2", formatted, StringComparison.Ordinal);
        Assert.Contains("Strength · 3", formatted, StringComparison.Ordinal);
        Assert.Contains("Trash cost · 1", formatted, StringComparison.Ordinal);
        Assert.Contains("Memory · 1", formatted, StringComparison.Ordinal);
        Assert.Contains("Status · KNOWN", formatted, StringComparison.Ordinal);
        Assert.Contains("Advancement ×1", formatted, StringComparison.Ordinal);
        Assert.Contains("Break [subroutine].", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>", formatted, StringComparison.Ordinal);
    }

    private static MatchViewModel RepresentativeView()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        GameState state = M2ScenarioSetup.Build(document);
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner);
        return ObservationViewMapper.Build(observation);
    }

    private static RunFocusViewModel BaseRun() => new(
        "run:test",
        "server:remote",
        PlayerSide.Runner,
        RunCrPhase.Approach,
        RunTimingStep.ApproachPaidWindow,
        CurrentIceIndex: 0,
        CurrentIce: null,
        Successful: false,
        Unsuccessful: false,
        EndReason: null,
        Priority: new RunPriorityViewModel(
            RunPriorityWindowKind.ApproachPaid,
            PlayerSide.Runner,
            ConsecutivePasses: 0,
            CorpMayRezApproachedIce: true,
            CorpMayRezNonIce: false),
        Encounter: null,
        Access: new AccessViewModel(
            Candidates: Array.Empty<CardViewModel?>(),
            RandomAccessLimit: 0,
            HiddenCandidateSource: null,
            Chosen: Array.Empty<CardViewModel?>(),
            Accessed: Array.Empty<CardViewModel?>(),
            State: RunAccessState.NotStarted),
        RunCredits: 0,
        InitiatingSource: null);

    private static MatchSnapshot Snapshot(
        MatchViewModel view,
        RunOutcomeViewModel? outcome = null) =>
        new(
            Sequence: 1,
            Observation: new Observation(
                PlayerSide.Runner,
                view.Decision?.Id ?? string.Empty,
                Array.Empty<LegalAction>(),
                new Dictionary<string, object?>(),
                new Dictionary<string, object?>()),
            View: view,
            Events: Array.Empty<EngineEvent>(),
            Feedback: null,
            IsOpponentThinking: false,
            RunOutcome: outcome);
}
