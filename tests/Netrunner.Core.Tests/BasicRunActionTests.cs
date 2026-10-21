using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Focused ENG-402 coverage for the ordinary basic-run action inside a real
/// Runner turn.  Public BeginRun remains the standalone M2 compatibility
/// boundary; these tests exercise the additional TurnFrame parent and the
/// action's atomic materialization contract.
/// </summary>
public sealed class BasicRunActionTests
{
    [Fact]
    public void RunnerTurnOffersOneRunPerServerInPhysicalOrderAndCorpNeverReceivesThem()
    {
        GameState runnerAction = OpenTurnAction(CreateState(PlayerSide.Runner));
        RunServerParameters[] runnerRuns = runnerAction.PendingDecision!
            .LegalActions
            .Where(action => action.Kind == ActionKind.StartRun)
            .Select(action => Assert.IsType<RunServerParameters>(action.Parameters))
            .ToArray();

        Assert.Equal(runnerAction.Servers.Select(server => server.Id), runnerRuns.Select(run => run.ServerId));
        Assert.Equal(runnerAction.Servers.Count, runnerRuns.Length);

        GameState corpAction = OpenTurnAction(CreateState(PlayerSide.Corp, corpDeckCount: 1));
        Assert.DoesNotContain(corpAction.PendingDecision!.LegalActions, action => action.Kind == ActionKind.StartRun);
    }

    [Fact]
    public void BasicRunProjectionStopsWhenEitherTransientPlayAreaIsOccupied()
    {
        GameState actionState = OpenTurnAction(CreateState(PlayerSide.Runner));
        CardInstanceId cardId = new("card:basic-run-transient");
        CardDefinitionId definitionId = new("test:basic-run-transient");

        foreach ((ZoneKind kind, PlayerSide side) in new[]
        {
            (ZoneKind.CorpPlayArea, PlayerSide.Corp),
            (ZoneKind.RunnerPlayArea, PlayerSide.Runner),
        })
        {
            ZoneId zoneId = new($"zone:basic-run-{kind.ToString().ToLowerInvariant()}");
            ZoneState[] zones = actionState.Zones
                .Append(new ZoneState(zoneId, kind, side, cardIds: [cardId]))
                .ToArray();
            CardInstanceState card = new(cardId, definitionId, zoneId, side, faceUp: true);
            GameState forged = Rebuild(
                actionState,
                cards: [card],
                zones: zones);

            Assert.Empty(GameEngine.MaterializeRunServerParameters(forged));
        }
    }

    [Fact]
    public void BasicRunSpendsExactlyOneClickAndBeginsWithClickThenInitiatedEvents()
    {
        GameState before = OpenTurnAction(CreateState(PlayerSide.Runner));
        PendingDecision pending = before.PendingDecision!;
        LegalAction action = Assert.Single(
            pending.LegalActions,
            candidate => candidate.Kind == ActionKind.StartRun
                && Assert.IsType<RunServerParameters>(candidate.Parameters).ServerId == RemoteId(before));
        ServerId attackedServer = Assert.IsType<RunServerParameters>(action.Parameters).ServerId;
        PlayerState runnerBefore = before.GetPlayer(pending.Owner);

        ApplyResult result = Apply(before, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [EngineEventKinds.ClickSpent, EngineEventKinds.RunInitiated],
            result.Events.Select(@event => @event.Kind));
        Assert.DoesNotContain(result.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.DoesNotContain(result.Events, @event => @event.Kind == EngineEventKinds.RunEventPlayed);

        PlayerState runnerAfter = result.State.GetPlayer(pending.Owner);
        Assert.Equal(runnerBefore.Clicks - 1, runnerAfter.Clicks);
        Assert.Equal(runnerBefore.Credits, runnerAfter.Credits);
        Assert.Equal(
            [typeof(TurnFrame), typeof(RunTimingFrame)],
            result.State.ResolutionStack.Select(frame => frame.GetType()));
        Assert.Equal(TurnFrameStage.AwaitingAction, Assert.IsType<TurnFrame>(result.State.ResolutionStack[0]).Stage);
        Assert.Equal(RunTimingStep.InitiationPaidWindow, result.State.RunState!.Step);
        Assert.Null(result.State.RunState.InitiatingSource);
        Assert.Null(result.State.RunState.ActiveRunEventId);
        Assert.DoesNotContain(result.State.Zones, zone => zone.Kind == ZoneKind.RunnerPlayArea);
        Assert.Equal(DecisionKind.RunTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(PlayerSide.Runner, SideOf(result.State, result.State.PendingDecision.Owner));
        Assert.Equal([attackedServer], result.State.Turn.ThisTurnFacts!.AttemptedRunServerIds);
        Assert.Empty(result.State.Turn.ThisTurnFacts.SuccessfulRunServerIds);
        Assert.False(result.State.Turn.ThisTurnFacts.SuccessfulRun);
        StateInvariantValidator.Validate(result.State, FixtureBehaviorCatalog.Catalog);
    }

    [Fact]
    public void BasicRunMidRunRoundTripsJsonAndHashAndContinuesIdentically()
    {
        GameState started = StartBasicRun(CreateState(PlayerSide.Runner));
        string json = EngineJson.Serialize(started);
        GameState restored = EngineJson.DeserializeState(json);

        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(started), DeterministicHash.ForState(restored));
        Assert.Equal(started.RunState!.Id, restored.RunState!.Id);
        Assert.Equal(started.RunState.Step, restored.RunState.Step);
        Assert.Equal(started.ResolutionStack.Select(frame => frame.GetType()), restored.ResolutionStack.Select(frame => frame.GetType()));

        PendingDecision decision = started.PendingDecision!;
        LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        ActionSubmission submission = new(decision.Owner, decision.Id, decision.Revision, pass.Id);
        ApplyResult left = GameEngine.Apply(started, submission);
        ApplyResult right = GameEngine.Apply(restored, submission);

        Assert.Equal(left.Status, right.Status);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));
    }

    [Fact]
    public void CompletedBasicRunResumesActionPriorityWithRemainingClicks()
    {
        GameState started = StartBasicRun(CreateState(PlayerSide.Runner));
        int clicksAfterStart = started.GetPlayer(RunnerId(started)).Clicks;

        ApplyResult runnerPass = ApplyPass(started);
        ApplyResult completed = ApplyPass(runnerPass.State);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Null(completed.State.RunState);
        Assert.Equal(clicksAfterStart, completed.State.GetPlayer(RunnerId(completed.State)).Clicks);
        Assert.Equal(
            [typeof(TurnFrame)],
            completed.State.ResolutionStack.Select(frame => frame.GetType()));
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(completed.State.ResolutionStack[0]).Stage);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);
        Assert.Contains(completed.Events, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.Equal(
            [RemoteId(started)],
            completed.State.Turn.ThisTurnFacts!.AttemptedRunServerIds);
        Assert.Equal(
            [RemoteId(started)],
            completed.State.Turn.ThisTurnFacts.SuccessfulRunServerIds);
        Assert.True(completed.State.Turn.ThisTurnFacts.SuccessfulRun);

        GameState actionPriorityClosed = ApplyPass(completed.State).State;
        actionPriorityClosed = ApplyPass(actionPriorityClosed).State;
        Assert.Equal(DecisionKind.TurnAction, actionPriorityClosed.PendingDecision!.Kind);
        Assert.Equal(clicksAfterStart, actionPriorityClosed.GetPlayer(RunnerId(actionPriorityClosed)).Clicks);
    }

    [Fact]
    public void CompletedBasicRunResumesActionPriorityAfterTheFinalClick()
    {
        GameState turnAction = OpenTurnAction(CreateState(PlayerSide.Runner));
        for (int index = 0; index < 3; index++)
        {
            LegalAction gain = Assert.Single(
                turnAction.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.GainCredit);
            ApplyResult gained = Apply(turnAction, gain);
            Assert.True(gained.Accepted, gained.Rejection?.Message);
            turnAction = gained.State;
            if (index < 2)
            {
                turnAction = ApplyPass(turnAction).State;
                turnAction = ApplyPass(turnAction).State;
            }
        }

        turnAction = ApplyPass(turnAction).State;
        turnAction = ApplyPass(turnAction).State;
        Assert.Equal(1, turnAction.GetPlayer(RunnerId(turnAction)).Clicks);
        GameState started = StartBasicRun(turnAction);
        Assert.Equal(0, started.GetPlayer(RunnerId(started)).Clicks);

        ApplyResult runnerPass = ApplyPass(started);
        ApplyResult completed = ApplyPass(runnerPass.State);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Null(completed.State.RunState);
        Assert.Equal(0, completed.State.GetPlayer(RunnerId(completed.State)).Clicks);
        Assert.Single(completed.State.ResolutionStack);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(completed.State.ResolutionStack[0]).Stage);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision!.Kind);
        int runEnded = completed.Events.ToList().FindIndex(@event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.True(runEnded >= 0);
    }

    [Fact]
    public void ForgedBasicRunDecisionSetsAreDiagnosedAndRejectedAtomically()
    {
        GameState source = OpenTurnAction(CreateState(PlayerSide.Runner));
        PendingDecision pending = source.PendingDecision!;
        LegalAction issued = Assert.Single(
            pending.LegalActions,
            action => action.Kind == ActionKind.StartRun && Assert.IsType<RunServerParameters>(action.Parameters).ServerId == RemoteId(source));
        LegalAction[] runs = pending.LegalActions.Where(action => action.Kind == ActionKind.StartRun).ToArray();

        var variants = new (string Code, IReadOnlyList<LegalAction> Actions, ActionId SubmittedAction)[]
        {
            (
                "decision.missing-run-action",
                pending.LegalActions.Where(action => action.Id != issued.Id).ToArray(),
                issued.Id),
            (
                "decision.extra-run-action",
                pending.LegalActions.Append(new LegalAction(
                    new ActionId("action:basic-run-extra"),
                    ActionKind.StartRun,
                    "Run stale server",
                    new RunServerParameters(new ServerId("server:basic-run-unknown")))).ToArray(),
                new ActionId("action:basic-run-extra")),
            (
                "decision.duplicate-run-action",
                pending.LegalActions.Append(new LegalAction(
                    new ActionId("action:basic-run-duplicate"),
                    ActionKind.StartRun,
                    "Run duplicate",
                    new RunServerParameters(Assert.IsType<RunServerParameters>(runs[0].Parameters).ServerId))).ToArray(),
                new ActionId("action:basic-run-duplicate")),
            (
                "decision.action-parameters-mismatch",
                pending.LegalActions.Select(action => action.Id == issued.Id
                    ? new LegalAction(action.Id, ActionKind.StartRun, action.Description, new NoActionParameters())
                    : action).ToArray(),
                issued.Id),
        };

        foreach ((string code, IReadOnlyList<LegalAction> actions, ActionId submittedAction) in variants)
        {
            GameState forged = ReplacePending(source, actions);
            IReadOnlyList<StateInvariantViolation>? violations = null;
            Exception? collectException = Record.Exception(() =>
                violations = StateInvariantValidator.Collect(forged));
            Assert.Null(collectException);
            Assert.Contains(violations!, violation => violation.Code == code);

            string before = EngineJson.Serialize(forged);
            ApplyResult rejected = GameEngine.Apply(
                forged,
                new ActionSubmission(forged.PendingDecision!.Owner, forged.PendingDecision.Id, forged.PendingDecision.Revision, submittedAction));
            Assert.False(rejected.Accepted);
            Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
            Assert.Same(forged, rejected.State);
            Assert.Empty(rejected.Events);
            Assert.Equal(before, EngineJson.Serialize(rejected.State));
        }
    }

    [Fact]
    public void StaleBasicRunServerDecisionIsRejectedAtomically()
    {
        GameState source = OpenTurnAction(CreateState(PlayerSide.Runner));
        PendingDecision pending = source.PendingDecision!;
        LegalAction stale = Assert.Single(
            pending.LegalActions,
            action => action.Kind == ActionKind.StartRun && Assert.IsType<RunServerParameters>(action.Parameters).ServerId == RemoteId(source));
        ServerId staleServer = RemoteId(source);
        GameState withoutServer = RemoveServer(source, staleServer);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(withoutServer);
        Assert.Contains(violations, violation => violation.Code == "decision.extra-run-action");

        string before = EngineJson.Serialize(withoutServer);
        PendingDecision stalePending = withoutServer.PendingDecision!;
        ApplyResult rejected = GameEngine.Apply(
            withoutServer,
            new ActionSubmission(stalePending.Owner, stalePending.Id, stalePending.Revision, stale.Id));

        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(withoutServer, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    [Fact]
    public void MalformedNestedBasicRunTopologiesProduceDiagnosticsWithoutThrowing()
    {
        GameState valid = StartBasicRun(CreateState(PlayerSide.Runner));
        RunTimingFrame run = Assert.IsType<RunTimingFrame>(valid.ResolutionStack[^1]);
        TurnFrame parent = Assert.IsType<TurnFrame>(valid.ResolutionStack[0]);
        var variants = new (IReadOnlyList<ResolutionFrame> Stack, string Code)[]
        {
            ([new TurnFrame(PlayerSide.Corp, valid.Turn.TurnNumber, TurnFrameStage.AwaitingAction), run], "run.nested-parent-side"),
            ([new TurnFrame(PlayerSide.Runner, valid.Turn.TurnNumber, TurnFrameStage.ActionPriority), run], "run.nested-parent-stage"),
            ([new TurnFrame(PlayerSide.Runner, valid.Turn.TurnNumber + 1, TurnFrameStage.AwaitingAction), run], "run.nested-parent-turn"),
            ([run, parent], "run.stack-topology"),
            ([parent, new TurnFrame(PlayerSide.Runner, valid.Turn.TurnNumber, TurnFrameStage.AwaitingAction), run], "run.stack-topology"),
        };

        foreach ((IReadOnlyList<ResolutionFrame> stack, string code) in variants)
        {
            GameState forged = ReplaceResolutionStack(valid, stack);
            IReadOnlyList<StateInvariantViolation>? violations = null;
            Exception? exception = Record.Exception(() =>
                violations = StateInvariantValidator.Collect(forged));

            Assert.Null(exception);
            Assert.NotNull(violations);
            Assert.Contains(violations!, violation => violation.Code == code);

            ApplyResult rejected = GameEngine.Apply(
                forged,
                new ActionSubmission(
                    forged.PendingDecision!.Owner,
                    forged.PendingDecision.Id,
                    forged.PendingDecision.Revision,
                    forged.PendingDecision.LegalActions[0].Id));
            Assert.False(rejected.Accepted);
            Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
            Assert.Same(forged, rejected.State);
            Assert.Empty(rejected.Events);
        }

        CardInstanceId forgedSource = new("card:basic-run-forged-source");
        RunState liveRun = Assert.IsType<RunState>(valid.RunState);
        RunState sourcedRun = new(
            liveRun.Id,
            liveRun.AttackedServer,
            PlayerSide.Runner,
            liveRun.InitiatingPlayer,
            liveRun.CurrentIceIndex,
            liveRun.CurrentIceId,
            liveRun.Step,
            liveRun.Priority,
            initiatingSource: forgedSource,
            runCredits: liveRun.RunCredits,
            activeRunEventId: forgedSource);
        GameState sourced = Rebuild(valid, runState: sourcedRun);
        IReadOnlyList<StateInvariantViolation>? sourcedViolations = null;
        Exception? sourcedException = Record.Exception(() =>
            sourcedViolations = StateInvariantValidator.Collect(sourced));
        Assert.Null(sourcedException);
        Assert.Contains(sourcedViolations!, violation => violation.Code == "run.active-event-unknown-card");

        ApplyResult sourcedRejected = GameEngine.Apply(
            sourced,
            new ActionSubmission(
                sourced.PendingDecision!.Owner,
                sourced.PendingDecision.Id,
                sourced.PendingDecision.Revision,
                sourced.PendingDecision.LegalActions[0].Id));
        Assert.False(sourcedRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, sourcedRejected.Rejection?.Code);
        Assert.Same(sourced, sourcedRejected.State);
        Assert.Empty(sourcedRejected.Events);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(6, true)]
    public void AutomaticStealDuringBasicRunUsesSevenPointThreshold(int initialPoints, bool expectWin)
    {
        GameState turnAction = OpenTurnAction(AddRemoteAgenda(CreateState(PlayerSide.Runner), initialPoints));
        GameState started = StartBasicRun(turnAction);

        GameState candidateSelection = ApplyPass(started).State;
        candidateSelection = ApplyPass(candidateSelection).State;
        Assert.Equal(RunTimingStep.BreachCandidateSelection, candidateSelection.RunState!.Step);
        LegalAction accessAgenda = Assert.Single(
            candidateSelection.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.ChooseBreachCandidate);
        BreachCandidateParameters accessParameters =
            Assert.IsType<BreachCandidateParameters>(accessAgenda.Parameters);
        ApplyResult accessed = Apply(candidateSelection, accessAgenda);
        Assert.True(accessed.Accepted, accessed.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachAccessAbility, accessed.State.RunState!.Step);

        LegalAction noAction = Assert.Single(
            accessed.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.NoAccessAction);
        ApplyResult stolen = Apply(accessed.State, noAction);

        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        Assert.Contains(stolen.Events, @event => @event.Kind == EngineEventKinds.AgendaStolen);
        TurnFactsState facts = stolen.State.Turn.ThisTurnFacts!;
        Assert.Equal([RemoteId(started)], facts.AttemptedRunServerIds);
        Assert.Equal([RemoteId(started)], facts.SuccessfulRunServerIds);
        Assert.Equal([accessParameters.CardId!.Value], facts.AccessedCardOccurrenceIds);
        Assert.Equal(
            [new AgendaTurnChange(
                accessParameters.CardId.Value,
                PlayerSide.Runner,
                1,
                AgendaTurnChangeKind.Steal)],
            facts.AgendaTurnChanges);
        int runEnded = stolen.Events.ToList().FindIndex(@event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.True(runEnded >= 0);
        int gameWon = stolen.Events.ToList().FindIndex(@event => @event.Kind == EngineEventKinds.GameWon);
        if (expectWin)
        {
            Assert.True(gameWon > runEnded);
            Assert.Equal(GameOutcomeKind.Won, stolen.State.Outcome.Kind);
            Assert.Equal(PlayerSide.Runner, stolen.State.Outcome.Winner);
            Assert.Null(stolen.State.PendingDecision);
            Assert.Empty(stolen.State.ResolutionStack);
        }
        else
        {
            Assert.Equal(-1, gameWon);
            Assert.Equal(GameOutcomeKind.Ongoing, stolen.State.Outcome.Kind);
            Assert.Equal(initialPoints + 1, stolen.State.GetPlayer(RunnerId(stolen.State)).AgendaPoints);
            Assert.Null(stolen.State.RunState);
            Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(stolen.State.ResolutionStack.Single()).Stage);
        }
    }

    [Fact]
    public void StandaloneBeginRunRemainsOneFrameAndDoesNotSpendTurnClicks()
    {
        GameState initial = CreateState(PlayerSide.Runner);
        int clicks = initial.GetPlayer(RunnerId(initial)).Clicks;
        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(initial, RemoteId(initial));

        Assert.Equal(clicks, started.State.GetPlayer(RunnerId(started.State)).Clicks);
        Assert.Single(started.State.ResolutionStack);
        Assert.IsType<RunTimingFrame>(started.State.ResolutionStack[0]);
        Assert.DoesNotContain(started.Events, @event => @event.Kind == EngineEventKinds.ClickSpent);
        Assert.Contains(started.Events, @event => @event.Kind == EngineEventKinds.RunInitiated);
        Assert.Null(started.State.Turn.History);
        Assert.Null(started.State.Turn.ThisTurnFacts);
    }

    private static GameState StartBasicRun(GameState state)
    {
        if (state.PendingDecision is null)
        {
            state = OpenTurnAction(state);
        }

        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction run = Assert.Single(
            pending.LegalActions,
            action => action.Kind == ActionKind.StartRun && Assert.IsType<RunServerParameters>(action.Parameters).ServerId == RemoteId(state));
        ApplyResult result = Apply(state, run);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static ApplyResult ApplyPass(GameState state)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(pending.LegalActions, action => action.Kind == ActionKind.PassPriority);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pass.Id),
            FixtureBehaviorCatalog.Catalog);
    }

    private static ApplyResult Apply(GameState state, LegalAction action)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            FixtureBehaviorCatalog.Catalog);
    }

    private static GameState OpenTurnAction(GameState state)
    {
        state = GameEngine.BeginTurn(state, state.Turn.ActiveSide, FixtureBehaviorCatalog.Catalog);
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(pending.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = GameEngine.Apply(
                state,
                new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pass.Id),
                FixtureBehaviorCatalog.Catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static GameState CreateState(PlayerSide side, int corpDeckCount = 0)
    {
        GameState baseState = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        if (side == PlayerSide.Runner && corpDeckCount == 0)
        {
            return baseState;
        }

        var allocator = new StableIdAllocator(baseState.Allocator);
        var cards = baseState.Cards.ToList();
        ZoneState[] zones = baseState.Zones.ToArray();
        if (corpDeckCount > 0)
        {
            ZoneState deck = baseState.GetZone(ZoneKind.CorpDeck);
            List<CardInstanceId> ids = [];
            for (int index = 0; index < corpDeckCount; index++)
            {
                CardInstanceId cardId = allocator.NextCardInstanceId();
                ids.Add(cardId);
                cards.Add(new CardInstanceState(cardId, new CardDefinitionId($"test:basic-corp-deck-{index}"), deck.Id, PlayerSide.Corp));
            }

            zones = zones
                .Select(zone => zone.Id == deck.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, ids)
                    : zone)
                .ToArray();
        }

        PlayerState[] players = baseState.Players
            .Select(player => player.Side == side
                ? new PlayerState(player.Id, player.Side, player.Credits, player.Side == PlayerSide.Corp ? 3 : 4, player.AgendaPoints)
                : player)
            .ToArray();
        return Rebuild(
            baseState,
            players: players,
            cards: cards,
            zones: zones,
            turn: new TurnState(side, side == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner, baseState.Turn.TurnNumber),
            allocator: allocator.State);
    }

    private static GameState AddRemoteAgenda(GameState state, int runnerPoints)
    {
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        ZoneState root = state.GetZone(remote.RootZoneId);
        CardInstanceId agendaId = new("card:basic-run-agenda");
        CardInstanceState agenda = new(
            agendaId,
            FixtureAgendaSpecs.ScoredAgenda,
            root.Id,
            PlayerSide.Corp,
            faceUp: false);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == root.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(agendaId).ToArray())
                : zone)
            .ToArray();
        PlayerId runnerId = RunnerId(state);
        PlayerState[] players = state.Players
            .Select(player => player.Id == runnerId
                ? new PlayerState(player.Id, player.Side, player.Credits, player.Clicks, runnerPoints)
                : player)
            .ToArray();
        return Rebuild(state, players: players, cards: state.Cards.Append(agenda).ToArray(), zones: zones);
    }

    private static GameState RemoveServer(GameState state, ServerId serverId)
    {
        ServerState removed = state.Servers.Single(server => server.Id == serverId);
        HashSet<ZoneId> removedZones = new() { removed.IceZoneId, removed.RootZoneId };
        return Rebuild(
            state,
            zones: state.Zones.Where(zone => !removedZones.Contains(zone.Id)).ToArray(),
            servers: state.Servers.Where(server => server.Id != serverId).ToArray());
    }

    private static GameState ReplacePending(GameState state, IReadOnlyList<LegalAction> actions) =>
        Rebuild(
            state,
            pendingDecision: new PendingDecision(
                state.PendingDecision!.Id,
                state.PendingDecision.Revision,
                state.PendingDecision.Owner,
                state.PendingDecision.Kind,
                state.PendingDecision.Prompt,
                actions));

    private static GameState ReplaceResolutionStack(GameState state, IReadOnlyList<ResolutionFrame> stack) =>
        Rebuild(state, resolutionStack: stack);

    private static GameState Rebuild(
        GameState source,
        IReadOnlyList<PlayerState>? players = null,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null,
        IReadOnlyList<ServerState>? servers = null,
        TurnState? turn = null,
        IdAllocatorState? allocator = null,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null,
        PendingDecision? pendingDecision = null,
        RunState? runState = null) => new(
        source.SchemaVersion,
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        players ?? source.Players,
        cards ?? source.Cards,
        zones ?? source.Zones,
        servers ?? source.Servers,
        turn ?? source.Turn,
        source.Outcome,
        source.RandomState,
        allocator ?? source.Allocator,
        source.DecisionRevision,
        source.EventSequence,
        resolutionStack ?? source.ResolutionStack,
        pendingDecision ?? source.PendingDecision,
        source.ActionSequence,
        source.InitialSeed,
        runState ?? source.RunState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);

    private static PlayerId RunnerId(GameState state) => state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

    private static ServerId RemoteId(GameState state) => state.Servers.Single(server => server.Kind == ServerKind.Remote).Id;

    private static PlayerSide SideOf(GameState state, PlayerId id) => state.GetPlayer(id).Side;
}
