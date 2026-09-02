using Netrunner.Cards;
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
    public void ObservationMapperProjectsTurnScoresAndExplicitHandLimitDependency()
    {
        MatchViewModel view = RepresentativeView();

        Assert.True(view.Turn.IsAvailable);
        Assert.NotNull(view.Turn.ActiveSide);
        Assert.NotNull(view.Turn.Phase);
        Assert.Null(view.Turn.Step);
        Assert.Equal(view.CorpHand.Count, view.CorpHandSize.Count);
        Assert.Equal(view.RunnerHand.Count, view.RunnerHandSize.Count);
        Assert.False(view.RunnerHandSize.IsMaximumKnown);
        Assert.Contains("Core ObservationBuilder", view.RunnerHandSize.MaximumHandSizeDiagnostic, StringComparison.Ordinal);
        Assert.Equal(view.Runner.AgendaPoints, view.RunnerScoreArea.AgendaPoints);
        Assert.Equal(view.Corp.AgendaPoints, view.CorpScoreArea.AgendaPoints);

        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        Observation observation = ObservationBuilder.Build(
            M2ScenarioSetup.Build(document),
            PlayerSide.Runner);
        MatchViewModel injected = ObservationViewMapper.Build(
            observation,
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create(),
            new HandSizeProjection(5, 6));

        Assert.Equal(5, injected.CorpHandSize.MaximumHandSize);
        Assert.Equal(6, injected.RunnerHandSize.MaximumHandSize);
        Assert.True(injected.RunnerHandSize.IsMaximumKnown);
        Assert.Contains("Hand", BoardRegionPresenter.Present(injected).RunnerSummary, StringComparison.Ordinal);
        Assert.Contains("max 6", BoardRegionPresenter.Present(injected).RunnerSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ObservationMapperKeepsVisibleInstalledCountersAndRejectsUnknownTurnEnums()
    {
        Observation observation = new(
            PlayerSide.Runner,
            string.Empty,
            Array.Empty<LegalAction>(),
            new Dictionary<string, object?>
            {
                ["turn"] = new Dictionary<string, object?>
                {
                    ["activeSide"] = "runner",
                    ["phase"] = "runner",
                    ["turnNumber"] = 3,
                    ["step"] = "runnerAction",
                },
                ["players"] = new Dictionary<string, object?>
                {
                    ["corp"] = new Dictionary<string, object?>
                    {
                        ["id"] = "player:corp",
                        ["credits"] = 4,
                        ["clicks"] = 2,
                        ["agendaPoints"] = 0,
                    },
                    ["runner"] = new Dictionary<string, object?>
                    {
                        ["id"] = "player:runner",
                        ["credits"] = 7,
                        ["clicks"] = 3,
                        ["agendaPoints"] = 1,
                    },
                },
                ["hiddenZoneCounts"] = new Dictionary<string, object?>
                {
                    ["corpHand"] = 0,
                    ["corpDeck"] = 0,
                    ["runnerGrip"] = 0,
                    ["runnerStack"] = 0,
                },
                ["servers"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "server:remote",
                        ["kind"] = "remote",
                        ["ice"] = Array.Empty<object?>(),
                        ["root"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["id"] = "card:agenda",
                                ["definitionId"] = "unknown:agenda",
                                ["rezzed"] = false,
                                ["advancementCounters"] = 2,
                            },
                        },
                    },
                },
                ["runnerRig"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "card:rig",
                        ["definitionId"] = "unknown:rig",
                        ["rezzed"] = false,
                        ["advancementCounters"] = 1,
                    },
                },
                ["corpScore"] = Array.Empty<object?>(),
                ["runnerScore"] = Array.Empty<object?>(),
                ["outcome"] = new Dictionary<string, object?>
                {
                    ["kind"] = "ongoing",
                    ["winner"] = null,
                    ["reason"] = null,
                },
            },
            new Dictionary<string, object?>());

        MatchViewModel view = ObservationViewMapper.Build(observation);

        Assert.Equal(
            ["card:agenda", "card:rig"],
            view.InstalledAdvancementCounters.Select(counter => counter.CardId));
        Assert.Equal([2, 1], view.InstalledAdvancementCounters.Select(counter => counter.Count));
        Assert.Equal(PlayerSide.Corp, view.InstalledAdvancementCounters[0].Controller);
        Assert.Equal(PlayerSide.Runner, view.InstalledAdvancementCounters[1].Controller);

        Dictionary<string, object?> unknownTurn = new(observation.PublicState)
        {
            ["turn"] = new Dictionary<string, object?>
            {
                ["activeSide"] = "future-side",
                ["phase"] = "runner",
                ["turnNumber"] = 3,
                ["step"] = "runnerAction",
            },
        };
        MatchViewModel rejected = ObservationViewMapper.Build(
            observation with { PublicState = unknownTurn });
        Assert.False(rejected.Turn.IsAvailable);
        Assert.Contains("unsupported", rejected.Turn.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TurnRegionShowsActiveTurnPriorityAndForcedDiscardWithoutInferringCounts()
    {
        MatchViewModel view = RepresentativeView();
        DecisionViewModel decision = new(
            "decision:discard",
            2,
            new PlayerId("player:runner"),
            PlayerSide.Runner,
            DecisionKind.TurnDiscard,
            "Discard down to your maximum hand size.");
        MatchViewModel discardView = view with { Run = BaseRun(), Decision = decision };
        TurnRegionState state = TurnRegionPresenter.Present(Snapshot(discardView));

        Assert.True(state.IsAvailable);
        Assert.True(state.IsForcedDiscard);
        Assert.Contains(
            $"ACTIVE {view.Turn.ActiveSide!.Value.ToString().ToUpperInvariant()}",
            state.Summary,
            StringComparison.Ordinal);
        Assert.Contains("PRIORITY RUNNER", state.Summary, StringComparison.Ordinal);
        Assert.Contains("TURNDISCARD", state.Summary, StringComparison.Ordinal);
        Assert.Null(view.ForcedDiscard);

        MatchViewModel mapped = ObservationViewMapper.Build(
            new Observation(
                PlayerSide.Runner,
                decision.Id,
                Array.Empty<LegalAction>(),
                new Dictionary<string, object?>
                {
                    ["turn"] = new Dictionary<string, object?>
                    {
                        ["activeSide"] = "runner",
                        ["phase"] = "runner",
                        ["turnNumber"] = 2,
                        ["step"] = "runnerDiscard",
                    },
                    ["players"] = new Dictionary<string, object?>
                    {
                        ["corp"] = new Dictionary<string, object?>
                        {
                            ["id"] = "player:corp",
                            ["credits"] = 5,
                            ["clicks"] = 0,
                            ["agendaPoints"] = 0,
                        },
                        ["runner"] = new Dictionary<string, object?>
                        {
                            ["id"] = "player:runner",
                            ["credits"] = 5,
                            ["clicks"] = 0,
                            ["agendaPoints"] = 0,
                        },
                    },
                    ["hiddenZoneCounts"] = new Dictionary<string, object?>
                    {
                        ["corpHand"] = 0,
                        ["corpDeck"] = 0,
                        ["runnerGrip"] = 6,
                        ["runnerStack"] = 0,
                    },
                    ["servers"] = Array.Empty<object?>(),
                    ["runnerRig"] = Array.Empty<object?>(),
                    ["corpScore"] = Array.Empty<object?>(),
                    ["runnerScore"] = Array.Empty<object?>(),
                    ["outcome"] = new Dictionary<string, object?>
                    {
                        ["kind"] = "ongoing",
                        ["winner"] = null,
                        ["reason"] = null,
                    },
                },
                new Dictionary<string, object?>())
            {
                Decision = new ObservationDecision(
                    new DecisionId(decision.Id),
                    decision.Revision,
                    decision.Owner,
                    decision.OwnerSide,
                    decision.Kind,
                    decision.Prompt),
            },
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create(),
            new HandSizeProjection(5, 5));

        Assert.NotNull(mapped.ForcedDiscard);
        Assert.Equal(PlayerSide.Runner, mapped.ForcedDiscard!.Side);
        Assert.True(mapped.RunnerHandSize.IsOverLimit);
        Assert.Contains("FORCED DISCARD", mapped.ForcedDiscard.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionsProgressivelyDiscloseOnlyTheActiveDecisionGroup()
    {
        MatchViewModel view = RepresentativeView();
        DecisionViewModel decision = new(
            "decision:turn-action",
            1,
            new PlayerId("player:runner"),
            PlayerSide.Runner,
            DecisionKind.TurnAction,
            "Choose a basic action.");
        LegalActionViewModel[] issued =
        [
            new(new ActionId("action:gain"), ActionKind.GainCredit, "Gain 1 credit.", new NoActionParameters()),
            new(new ActionId("action:draw"), ActionKind.DrawCard, "Draw 1 card.", new NoActionParameters()),
            new(new ActionId("action:run"), ActionKind.ContinueRun, "Continue the run.", new NoActionParameters()),
            new(new ActionId("action:behavior"), ActionKind.ChooseBehavior, "Choose a mode.", new BehaviorChoiceParameters("mode", "gain")),
            new(new ActionId("action:discard"), ActionKind.ChooseCard, "Discard a card.", new CardChoiceParameters(new CardInstanceId("card:hidden"))),
        ];

        ActionsRegionState region = ActionsRegionPresenter.Present(
            Snapshot(view with { Decision = decision, LegalActions = issued }));

        Assert.Equal(
            [ActionKind.GainCredit, ActionKind.DrawCard, ActionKind.ChooseBehavior],
            region.DisplayActions.Select(action => action.Kind));
        Assert.Single(region.ActionGroups);
        Assert.Equal("BASIC ACTIONS ISSUED BY CORE", region.ActionGroups[0].Label);
        Assert.DoesNotContain(region.DisplayActions, action => action.Kind == ActionKind.ContinueRun);

        ActionsRegionState discard = ActionsRegionPresenter.Present(
            Snapshot(view with
            {
                Decision = decision with { Kind = DecisionKind.TurnDiscard },
                LegalActions = issued,
            }));
        Assert.True(discard.IsForcedDiscard);
        Assert.Equal([ActionKind.ChooseCard], discard.DisplayActions.Select(action => action.Kind));
    }

    [Fact]
    public void UnknownTurnAndDecisionEnumsFailClosedForPresentation()
    {
        MatchViewModel view = RepresentativeView() with
        {
            Turn = new TurnViewModel(
                true,
                (PlayerSide)999,
                TurnPhase.Corp,
                TurnStep.CorpAction,
                1,
                null),
            Decision = new DecisionViewModel(
                "decision:future",
                1,
                new PlayerId("player:runner"),
                PlayerSide.Runner,
                (DecisionKind)999,
                "Future decision"),
        };

        TurnRegionState timing = TurnRegionPresenter.Present(Snapshot(view));
        Assert.False(timing.IsAvailable);
        Assert.Contains("unavailable", timing.Summary, StringComparison.OrdinalIgnoreCase);

        ActionsRegionState actions = ActionsRegionPresenter.Present(Snapshot(view));
        Assert.Equal(ActionsRegionStatus.Unsupported, actions.Status);
        Assert.Empty(actions.DisplayActions);
        Assert.Empty(actions.ActionGroups);
        Assert.Empty(actions.TargetedActions);

        RunFocusViewModel invalidRun = BaseRun() with { Phase = (RunCrPhase)999 };
        Assert.Equal(
            FocusRegionMode.Empty,
            FocusRegionPresenter.Present(Snapshot(view with
            {
                Decision = null,
                Run = invalidRun,
            })).Mode);
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
