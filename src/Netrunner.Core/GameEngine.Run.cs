namespace Netrunner.Core;

/// <summary>
/// CR 26.03 section 6.9 timing reducer.  This file deliberately contains the
/// run-specific registry and state transitions separately from the generic
/// resolution and M1 fixture turn reducer in <c>GameEngine.cs</c>.
/// </summary>
public static partial class GameEngine
{
    /// <summary>
    /// Begins a Runner run at the next serialized timing decision.  Starting a
    /// run is a rules/setup boundary; subsequent choices must use the normal
    /// atomic <see cref="Apply(GameState, ActionSubmission?)"/> boundary.
    /// </summary>
    /// <summary>
    /// The run-start boundary result: the state with the initiation window
    /// yielded, plus the opening event batch the boundary consumed (the played
    /// run event, kept faceup in the public RunnerPlayArea for the run's
    /// duration). These
    /// events must be prepended to the run's event stream; the following
    /// accepted action does not re-emit them.
    /// </summary>
    public sealed record RunStartResult(GameState State, IReadOnlyList<EngineEvent> Events);

    public static GameState BeginRun(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        BeginRunWithEvents(
            state,
            serverId,
            initiatingPlayer,
            initiatingSource,
            BehaviorCatalog.Empty,
            runtimeCardCatalog: null,
            enforceCatalogCompatibility: false).State;

    /// <summary>Begins a run with an explicitly injected immutable catalog.</summary>
    public static GameState BeginRun(
        GameState state,
        ServerId serverId,
        IBehaviorCatalog behaviorCatalog,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        BeginRunWithEvents(
            state,
            serverId,
            initiatingPlayer,
            initiatingSource,
            behaviorCatalog,
            runtimeCardCatalog: null,
            enforceCatalogCompatibility: true).State;

    /// <summary>Begins a run with both injected immutable catalogs.</summary>
    public static GameState BeginRun(
        GameState state,
        ServerId serverId,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        BeginRunWithEvents(
            state,
            serverId,
            initiatingPlayer,
            initiatingSource,
            behaviorCatalog,
            runtimeCardCatalog,
            enforceCatalogCompatibility: true).State;

    /// <summary>Parameter-order adapter for callers that append dependencies.</summary>
    public static GameState BeginRun(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer,
        CardInstanceId? initiatingSource,
        IBehaviorCatalog behaviorCatalog) =>
        BeginRun(state, serverId, behaviorCatalog, initiatingPlayer, initiatingSource);

    public static RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null)
    {
        return BeginRunWithEvents(
            state,
            serverId,
            initiatingPlayer,
            initiatingSource,
            BehaviorCatalog.Empty,
            runtimeCardCatalog: null,
            enforceCatalogCompatibility: false);
    }

    /// <summary>Begins a run with an explicitly injected immutable catalog.</summary>
    public static RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        IBehaviorCatalog behaviorCatalog,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        BeginRunWithEvents(
            state,
            serverId,
            initiatingPlayer,
            initiatingSource,
            behaviorCatalog,
            runtimeCardCatalog: null,
            enforceCatalogCompatibility: true);

    /// <summary>Begins a run with both injected immutable catalogs.</summary>
    public static RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        BeginRunWithEvents(
            state,
            serverId,
            initiatingPlayer,
            initiatingSource,
            behaviorCatalog,
            runtimeCardCatalog,
            enforceCatalogCompatibility: true);

    /// <summary>Parameter-order adapter for callers that append dependencies.</summary>
    public static RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer,
        CardInstanceId? initiatingSource,
        IBehaviorCatalog behaviorCatalog) =>
        BeginRunWithEvents(state, serverId, behaviorCatalog, initiatingPlayer, initiatingSource);

    private static RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer,
        CardInstanceId? initiatingSource,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        bool enforceCatalogCompatibility)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        if (enforceCatalogCompatibility
            && !CatalogMatchesState(state, behaviorCatalog, out string? catalogMismatch))
        {
            throw new ResolutionFailureException(catalogMismatch!);
        }

        if (runtimeCardCatalog is not null
            && !CatalogMatchesState(state, runtimeCardCatalog, out string? runtimeMismatch))
        {
            throw new ResolutionFailureException(runtimeMismatch!);
        }

        StateInvariantValidator.Validate(
            state,
            enforceCatalogCompatibility ? behaviorCatalog : null,
            runtimeCardCatalog);

        if (state.Outcome.Kind != GameOutcomeKind.Ongoing)
        {
            throw new ResolutionFailureException("A run cannot begin after the game has reached a terminal outcome.");
        }

        if (state.RunState is not null)
        {
            throw new ResolutionFailureException("A run is already active.");
        }

        if (state.PendingDecision is not null || state.ResolutionStack.Count > 0)
        {
            throw new ResolutionFailureException("A run cannot begin while another decision or resolution is pending.");
        }

        if (state.Turn.ActiveSide != PlayerSide.Runner || state.Turn.Phase != TurnPhase.Runner)
        {
            throw new ResolutionFailureException("A run can begin only during the Runner turn.");
        }

        var working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        try
        {
            StartRunInWorkingState(
                working,
                serverId,
                initiatingPlayer,
                initiatingSource);
            Advance(working, DefaultResolutionStepBudget);
            GameState next = working.Build(state);
            StateInvariantValidator.Validate(
                next,
                enforceCatalogCompatibility ? behaviorCatalog : null,
                runtimeCardCatalog);
            return new RunStartResult(next, working.Events.ToList());
        }
        catch (ResolutionFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is StateInvariantException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new ResolutionFailureException(
                $"The run could not be started: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Shared M2 run-start routine used by the standalone compatibility
    /// boundary and the real-turn basic-run action. It owns run setup, the
    /// optional legacy run-event play, initiation evidence, and the first
    /// timing decision so the two entry points cannot drift apart.
    /// </summary>
    private static void StartRunInWorkingState(
        WorkingState working,
        ServerId serverId,
        PlayerId? initiatingPlayer,
        CardInstanceId? initiatingSource,
        BehaviorRunOperand? runOperand = null)
    {
        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        if (initiatingPlayer.HasValue && initiatingPlayer.Value != runner.Id)
        {
            throw new ResolutionFailureException("The initiating player must be the Runner.");
        }

        ServerState server = working.Servers.SingleOrDefault(candidate => candidate.Id == serverId)
            ?? throw new ResolutionFailureException($"Server '{serverId}' does not exist.");
        ZoneState iceZone = working.Zones.SingleOrDefault(zone => zone.Id == server.IceZoneId)
            ?? throw new ResolutionFailureException($"Server '{serverId}' has no ICE zone.");

        if (working.RunState is not null)
        {
            throw new ResolutionFailureException("A run is already active.");
        }

        bool installedRunSource = runOperand is not null;
        if (installedRunSource && !initiatingSource.HasValue)
        {
            throw new ResolutionFailureException("An installed run source must identify its source card.");
        }

        CardInstanceState? installedSource = null;
        if (installedRunSource)
        {
            installedSource = working.Cards.SingleOrDefault(card => card.Id == initiatingSource!.Value)
                ?? throw new ResolutionFailureException($"Run source card '{initiatingSource}' does not exist.");
            ZoneState sourceZone = working.Zones.SingleOrDefault(zone => zone.Id == installedSource.ZoneId)
                ?? throw new ResolutionFailureException($"Run source card '{installedSource.Id}' references no zone.");
            if (installedSource.OwnerSide != PlayerSide.Runner
                || !installedSource.FaceUp
                || sourceZone.Kind != ZoneKind.RunnerRig
                || sourceZone.OwnerSide != PlayerSide.Runner
                || !sourceZone.CardIds.Contains(installedSource.Id)
                || !working.BehaviorCatalog.TryGet(installedSource.DefinitionId, out CardBehaviorDefinition? sourceDefinition)
                || !BehaviorRunRules.DefinitionHasStartRunForServer(sourceDefinition, server.Kind))
            {
                throw new ResolutionFailureException(
                    "An installed run source must be an active face-up Runner Rig card with a matching click StartRun declaration.");
            }

            if (runOperand!.RequireNotRunThisTurn
                && (working.Turn.History?.ThisTurn(PlayerSide.Runner) is not { } facts
                    || facts.AttemptedRunServerIds.Contains(server.Id)))
            {
                throw new ResolutionFailureException(
                    $"The Runner has already attempted server '{server.Id}' this turn.");
            }
        }

        // A registered run event (Overclock) is played at initiation. CR 26.03
        // 1.10.3c: its printed cost comes from the Runner wallet, while hosted
        // credits become the run-scoped pool (1.10.4b/c).
        RunEventBehaviorSpec? runEventSpec = null;
        if (initiatingSource.HasValue && !installedRunSource)
        {
            CardInstanceState source = working.Cards.SingleOrDefault(card => card.Id == initiatingSource.Value)
                ?? throw new ResolutionFailureException($"Run source card '{initiatingSource.Value}' does not exist.");
            if (source.OwnerSide != PlayerSide.Runner)
            {
                throw new ResolutionFailureException("A run source must be owned by the Runner.");
            }

            runEventSpec = ValidateRunEventSource(working, initiatingSource.Value, source);
            ValidateRunEventTarget(working, runEventSpec, server);
            ValidateRunEventSuccessDraw(working, source, runEventSpec);
            if (runner.Credits < runEventSpec.Cost)
            {
                throw new ResolutionFailureException(
                    $"The Runner has {runner.Credits} credits but the run event "
                    + $"'{runEventSpec.DefinitionId.Value}' costs {runEventSpec.Cost} credits to play.");
            }
        }

        IReadOnlyList<RunBreachAccessModifier>? breachModifiers = installedRunSource
            ? SnapshotRunBreachAccessModifiers(
                working,
                installedSource!,
                server.Id,
                runOperand!)
            : runEventSpec is not null
                ? SnapshotRunEventBreachAccessModifiers(
                    working,
                    initiatingSource
                        ?? throw new ResolutionFailureException("A run event requires a source card."),
                    runEventSpec,
                    server)
                : null;
        IReadOnlyList<RunRezCostModifier>? rezCostModifiers = runEventSpec is not null
            ? SnapshotRunRezCostModifiers(
                runEventSpec,
                initiatingSource
                    ?? throw new ResolutionFailureException("A run event requires a source card."))
            : null;

        RunId runId = working.Allocator.NextRunId();
        int currentIceIndex = iceZone.CardIds.Count - 1;
        CardInstanceId? currentIceId = currentIceIndex >= 0
            ? iceZone.CardIds[currentIceIndex]
            : null;
        RunState run = new(
            runId,
            server.Id,
            PlayerSide.Runner,
            runner.Id,
            currentIceIndex,
            currentIceId,
            RunTimingStep.InitiationPaidWindow,
            CreatePriority(RunPriorityWindowKind.InitiationPaid, corpMayRezNonIce: true),
            initiatingSource,
            runEventSpec?.HostedCredits ?? 0,
            runEventSpec is null ? null : initiatingSource,
            breachModifiers,
            rezCostModifiers);
        working.RunState = run;

        // CR 26.03 8.6.7: a played event leaves the Runner's grip and enters a
        // public play area faceup for the run's duration. The zone is lazy so
        // ordinary M1/M2 states retain their canonical topology and hashes.
        if (runEventSpec is not null)
        {
            ZoneState grip = GetZone(working, ZoneKind.RunnerGrip);
            ZoneState? existingPlayArea = working.Zones.SingleOrDefault(zone =>
                zone.Kind == ZoneKind.RunnerPlayArea
                && zone.OwnerSide == PlayerSide.Runner
                && zone.ServerId is null);
            if (existingPlayArea is not null && existingPlayArea.CardIds.Count > 0)
            {
                throw new ResolutionFailureException(
                    "A new run event cannot be played while the Runner play area contains an orphaned card.");
            }

            ZoneState playArea = existingPlayArea ?? new ZoneState(
                working.Allocator.NextZoneId(),
                ZoneKind.RunnerPlayArea,
                PlayerSide.Runner);
            if (existingPlayArea is null)
            {
                working.Zones.Add(playArea);
            }
            CardInstanceId sourceId = initiatingSource
                ?? throw new ResolutionFailureException("A run event requires a source card.");
            CardInstanceState sourceCard = GetCard(working, sourceId);
            MoveCard(
                working,
                new CardInstanceState(
                    sourceCard.Id,
                    sourceCard.DefinitionId,
                    sourceCard.ZoneId,
                    sourceCard.OwnerSide,
                    faceUp: true,
                    rezzed: sourceCard.Rezzed,
                    advancementCounters: sourceCard.AdvancementCounters),
                grip,
                playArea);
        }

        working.ResolutionStack.Add(new RunTimingFrame(runId, RunTimingStep.InitiationPaidWindow));
        if (runEventSpec is not null)
        {
            // The wallet pays the printed cost only; hosted credits are already
            // represented in RunState and are untouched by this payment.
            ApplyCreditDelta(working, runner.Id, -runEventSpec.Cost);
            CardInstanceId eventSourceId = initiatingSource
                ?? throw new ResolutionFailureException("A run event requires a source card.");
            Emit(working, EngineEventKinds.RunEventPlayed, new RunEventPlayedEventPayload(
                runId,
                eventSourceId,
                runEventSpec.DefinitionId,
                runEventSpec.Cost,
                runEventSpec.HostedCredits,
                runner.Id,
                GetPlayer(working, runner.Id).Credits));
        }

        RecordTurnRunAttempt(working, server.Id);

        // CR 6.9.1: initiation follows event placement/payment. The first
        // decision is yielded directly, preserving the standalone M2 boundary
        // and giving a basic turn action the same run timing cursor.
        Emit(working, EngineEventKinds.RunInitiated, new RunInitiatedEventPayload(
            run.Id, server.Id, runner.Id));
        YieldRunPriorityDecision(
            working,
            run,
            "CR 6.9.1e: choose a paid ability or pass.",
            emitWindowEvent: false);
    }

    private static IReadOnlyList<RunBreachAccessModifier>? SnapshotRunBreachAccessModifiers(
        WorkingState working,
        CardInstanceState source,
        ServerId attackedServer,
        BehaviorRunOperand operand)
    {
        if (operand.BreachAccessDeltaPerCounter is not { } deltaPerCounter)
        {
            return null;
        }

        int counter = CurrentCounter(working, source.Id, operand.SourceCounterName!);
        if (counter <= 0)
        {
            return null;
        }

        int delta = checked(counter * deltaPerCounter);
        return delta > 0
            ? [new RunBreachAccessModifier(source.Id, attackedServer, delta, operand.BreachAccessReasonKey!)]
            : null;
    }

    private static IReadOnlyList<RunBreachAccessModifier>? SnapshotRunEventBreachAccessModifiers(
        WorkingState working,
        CardInstanceId sourceCardId,
        RunEventBehaviorSpec runEventSpec,
        ServerState attackedServer)
    {
        if (runEventSpec.BreachAccessModifier is not { } declaration)
        {
            return null;
        }

        return [new RunBreachAccessModifier(
            sourceCardId,
            attackedServer.Id,
            declaration.Delta,
            declaration.ReasonKey,
            RunBreachAccessModifierSourceKind.RunEvent)];

    }

    private static void ValidateRunEventSuccessDraw(
        WorkingState working,
        CardInstanceState source,
        RunEventBehaviorSpec runEventSpec)
    {
        if (runEventSpec.SuccessDraw is not { } declaration)
        {
            return;
        }

        if (!working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
            || !BehaviorRunRules.IsRunEventSuccessDrawDefinition(definition, declaration))
        {
            throw new ResolutionFailureException(
                $"Run event '{runEventSpec.DefinitionId}' has no authenticated RunSuccessful draw trigger binding.");
        }
    }

    private static List<RunBreachAccessModifier> SnapshotFirstHqBreachAccessModifiers(
        WorkingState working,
        ServerId attackedServer)
    {
        ZoneState? rig = working.Zones.FirstOrDefault(zone =>
            zone.Kind == ZoneKind.RunnerRig
            && zone.OwnerSide == PlayerSide.Runner
            && zone.ServerId is null);
        if (rig is null)
        {
            return [];
        }

        var snapshots = new List<RunBreachAccessModifier>();
        foreach (CardInstanceState source in rig.CardIds
            .Select(cardId => working.Cards.FirstOrDefault(card => card.Id == cardId))
            .Where(card => card is not null)
            .Cast<CardInstanceState>()
            .Where(card => card.OwnerSide == PlayerSide.Runner
                && card.FaceUp
                && card.ZoneId == rig.Id)
            .OrderBy(card => card.Id.Value, StringComparer.Ordinal))
        {
            if (!working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
                || definition.FirstHqBreachAccessModifiers is not { Count: > 0 } declarations)
            {
                continue;
            }

            foreach (BehaviorFirstHqBreachAccessModifier declaration in declarations)
            {
                snapshots.Add(new RunBreachAccessModifier(
                    source.Id,
                    attackedServer,
                    declaration.Delta,
                    declaration.ReasonKey,
                    RunBreachAccessModifierSourceKind.FirstHeadquartersBreach));
            }
        }

        return snapshots;
    }

    private static RunRezCostModifier[]? SnapshotRunRezCostModifiers(
        RunEventBehaviorSpec runEventSpec,
        CardInstanceId sourceCardId)
    {
        if (runEventSpec.RezCostModifiers is not { Count: > 0 } declarations)
        {
            return null;
        }

        return declarations
            .Select(declaration => new RunRezCostModifier(
                runEventSpec.DefinitionId,
                sourceCardId,
                declaration.Delta,
                declaration.ReasonKey))
            .ToArray();
    }

    /// <summary>
    /// Re-materializes basic-run payloads without consuming allocator state or
    /// reading any mutable card registry. The returned order is the physical
    /// <see cref="GameState.Servers"/> order captured by the current state.
    /// </summary>
    internal static IReadOnlyList<RunServerParameters> MaterializeRunServerParameters(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        WorkingState working = WorkingState.From(state);
        return MaterializeRunServerParameters(working);
    }

    private static List<RunServerParameters> MaterializeRunServerParameters(WorkingState working)
    {
        if (working.Turn.ActiveSide != PlayerSide.Runner
            || working.Turn.Phase != TurnPhase.Runner
            || working.RunState is not null
            || working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame
            )
        {
            return [];
        }

        TurnFrame turn = (TurnFrame)working.ResolutionStack[^1];
        if (turn.ActiveSide != PlayerSide.Runner
            || turn.Stage != TurnFrameStage.AwaitingAction
            || turn.TurnNumber != working.Turn.TurnNumber)
        {
            return [];
        }

        PlayerState? runner = working.Players.FirstOrDefault(player => player.Side == PlayerSide.Runner);
        if (runner is null || runner.Clicks < 1 || HasOccupiedTransientPlayArea(working))
        {
            return [];
        }

        // Deliberately do not sort: this is the persisted server topology
        // order, and each action is a complete immutable server payload.
        return working.Servers
            .Select(server => new RunServerParameters(server.Id))
            .ToList();
    }

    private static bool HasOccupiedTransientPlayArea(WorkingState working) =>
        working.Zones.Any(zone =>
            (zone.Kind is ZoneKind.CorpPlayArea or ZoneKind.RunnerPlayArea)
            && zone.CardIds.Count > 0);

    private static void AddRunActions(WorkingState working, List<LegalAction> actions)
    {
        foreach (RunServerParameters parameters in MaterializeRunServerParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.StartRun,
                $"Run {parameters.ServerId.Value}",
                parameters));
        }
    }

    private static void ConsumeTurnStartRunAction(WorkingState working, RunServerParameters parameters)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame turnFrame
            || turnFrame.Stage != TurnFrameStage.AwaitingAction
            || turnFrame.ActiveSide != PlayerSide.Runner
            || working.Turn.ActiveSide != PlayerSide.Runner
            || working.RunState is not null)
        {
            throw new ResolutionFailureException("A basic run requires the active Runner turn action frame.");
        }

        IReadOnlyList<RunServerParameters> materialized = MaterializeRunServerParameters(working);
        if (!materialized.Any(candidate => candidate.ServerId == parameters.ServerId))
        {
            throw new ResolutionFailureException("The basic-run action does not match the materialized server set.");
        }

        // The click is the complete basic-run cost. Setup and initiation then
        // flow through the same M2 routine used by BeginRunWithEvents.
        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        SpendClick(working, runner.Id);
        StartRunInWorkingState(working, parameters.ServerId, runner.Id, initiatingSource: null);
    }

    public static GameState InitiateRun(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        BeginRun(state, serverId, initiatingPlayer, initiatingSource);

    public static GameState InitiateRun(
        GameState state,
        ServerId serverId,
        IBehaviorCatalog behaviorCatalog,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        BeginRun(state, serverId, behaviorCatalog, initiatingPlayer, initiatingSource);

    /// <summary>
    /// The run-event play validation: the source must be a card in the
    /// Runner's grip (faceup or facedown) whose definition is registered in
    /// the run-event behavior spec catalog, and it must not already be the
    /// active run event. (Runner-ownership of the source is checked by the
    /// caller, <c>BeginRunWithEvents</c>.) A run plays at most one run event: the
    /// initiating source is the run's single event (recorded in
    /// <see cref="RunState.ActiveRunEventId"/>), and there is no mid-run play
    /// action, so a second play is an illegal action by construction.
    /// </summary>
    private static RunEventBehaviorSpec ValidateRunEventSource(
        WorkingState working,
        CardInstanceId sourceId,
        CardInstanceState source)
    {
        ZoneState? grip = working.Zones.FirstOrDefault(zone =>
            zone.Kind == ZoneKind.RunnerGrip && zone.OwnerSide == PlayerSide.Runner);
        if (grip is null || grip.Id != source.ZoneId)
        {
            throw new ResolutionFailureException(
                $"Run source '{sourceId.Value}' must be a card in the Runner's grip.");
        }

        if (!working.BehaviorCatalog.TryGetRunEvent(source.DefinitionId, out RunEventBehaviorSpec? spec))
        {
            throw new ResolutionFailureException(
                $"Card definition '{source.DefinitionId.Value}' is not a registered run event "
                + "and cannot initiate a run.");
        }

        // Defense in depth: unreachable from the current BeginRun call site
        // (a second BeginRun is rejected before validation), but it pins the
        // one-event-per-run invariant for future call sites.
        if (working.RunState is { ActiveRunEventId: { } active } && active == sourceId)
        {
            throw new ResolutionFailureException(
                $"Run source '{sourceId.Value}' is already the active run event; a run plays at most one run event.");
        }

        return spec;
    }

    private static void ValidateRunEventTarget(
        WorkingState working,
        RunEventBehaviorSpec spec,
        ServerState target)
    {
        if (spec.AllowedTargetServers is { } allowed
            && !allowed.Contains(target.Kind))
        {
            throw new ResolutionFailureException(
                $"Run event '{spec.DefinitionId.Value}' cannot attack server '{target.Id.Value}'.");
        }

        if (working.RuntimeCardCatalog is not null
            && (!working.RuntimeCardCatalog.TryGet(spec.DefinitionId, out CardRuntimeSpec? runtime)
                || runtime.Side != PlayerSide.Runner
                || runtime.CardClass != RuntimeCardClass.Event
                || runtime.PlayCost != spec.Cost))
        {
            throw new ResolutionFailureException(
                $"Run event '{spec.DefinitionId.Value}' does not match authenticated runtime card metadata.");
        }
    }

    private static void StepRunTiming(WorkingState working, RunTimingFrame frame)
    {
        RunState run = working.RunState
            ?? throw new ResolutionFailureException("A run timing frame requires live run state.");
        if (run.Id != frame.RunId || run.Step != frame.Step)
        {
            throw new ResolutionFailureException("Run timing frame and authoritative run cursor do not agree.");
        }

        switch (frame.Step)
        {
            case RunTimingStep.InitiationPaidWindow:
                YieldRunPriorityDecision(working, run, "CR 6.9.1e: choose a paid ability or pass.");
                break;
            case RunTimingStep.Approach:
                StepRunApproach(working, run);
                break;
            case RunTimingStep.ApproachPaidWindow:
                YieldRunPriorityDecision(working, run, "CR 6.9.2b: approach paid/rez window.");
                break;
            case RunTimingStep.EncounterInitialization:
                StepEncounterInitialization(working, run);
                break;
            case RunTimingStep.EncounterPaidWindow:
                YieldRunPriorityDecision(working, run, "CR 6.9.3b: encounter paid window.");
                break;
            case RunTimingStep.Encounter:
                StepEncounter(working, run);
                break;
            case RunTimingStep.MovementPaidWindow:
                YieldRunPriorityDecision(working, run, "CR 6.9.4b: movement paid window.");
                break;
            case RunTimingStep.JackOutDecision:
                YieldJackOutDecision(working, run);
                break;
            case RunTimingStep.MoveInward:
                StepMoveInward(working, run);
                break;
            case RunTimingStep.AfterMovePaidWindow:
                YieldRunPriorityDecision(working, run, "CR 6.9.4e: after-movement paid/rez window.");
                break;
            case RunTimingStep.ApproachServer:
                StepRunApproachServer(working, run);
                break;
            case RunTimingStep.Success:
                StepRunSuccess(working, run);
                break;
            case RunTimingStep.BreachBoundary:
                StepBreachBoundary(working, run);
                break;
            case RunTimingStep.BreachCandidateSelection:
                YieldBreachCandidateDecision(working, run);
                break;
            case RunTimingStep.BreachAccessAbility:
                YieldBreachAccessDecision(working, run);
                break;
            case RunTimingStep.RunEnd:
                CompleteRun(working, run);
                break;
            default:
                throw new ResolutionFailureException($"Unsupported run timing step '{frame.Step}'.");
        }
    }

    private static void StepRunApproach(WorkingState working, RunState run)
    {
        ZoneState iceZone = GetRunIceZone(working, run);
        if (run.CurrentIceIndex < 0)
        {
            SetRunTiming(working, run.WithTiming(
                RunTimingStep.ApproachServer,
                -1,
                null,
                run.Successful,
                run.Unsuccessful,
                run.EndReason,
                null,
                run.Encounter,
                run.AccessState));
            return;
        }

        CardInstanceId iceId = GetCurrentIceId(run, iceZone);
        CardInstanceState ice = GetCard(working, iceId);
        Emit(working, EngineEventKinds.IceApproached, new IceApproachedEventPayload(run.Id, ice.Id));

        SetRunTiming(working, run.WithTiming(
            RunTimingStep.ApproachPaidWindow,
            run.CurrentIceIndex,
            ice.Id,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            CreatePriority(
                RunPriorityWindowKind.ApproachPaid,
                corpMayRezApproachedIce: !ice.Rezzed,
                corpMayRezNonIce: true),
            null,
            run.AccessState));
    }

    private static void StepEncounterInitialization(WorkingState working, RunState run)
    {
        ZoneState iceZone = GetRunIceZone(working, run);
        CardInstanceId iceId = GetCurrentIceId(run, iceZone);
        CardInstanceState ice = GetCard(working, iceId);
        if (!ice.Rezzed)
        {
            throw new ResolutionFailureException("An encounter can only initialize against rezzed ICE.");
        }

        if (!working.BehaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec spec))
        {
            throw new ResolutionFailureException(
                $"ICE definition '{ice.DefinitionId}' is not registered in the ICE behavior spec catalog; "
                + "the encounter cannot be initialized.");
        }

        EncounterId encounterId = new($"encounter:{run.Id.Value}:{ice.Id.Value}");
        var subroutines = new List<RunSubroutineState>(spec.Subroutines.Count);
        for (int index = 0; index < spec.Subroutines.Count; index++)
        {
            subroutines.Add(new RunSubroutineState(
                new SubroutineId($"subroutine:{encounterId.Value}:{index}"),
                index));
        }

        var encounter = new RunEncounterState(encounterId, ice.Id, subroutines);
        Emit(working, EngineEventKinds.EncounterInitialized, new EncounterInitializedEventPayload(
            run.Id,
            encounterId,
            ice.Id,
            encounter.Subroutines.Count));
        SetRunTiming(working, run.WithTiming(
            RunTimingStep.EncounterPaidWindow,
            run.CurrentIceIndex,
            ice.Id,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            CreatePriority(RunPriorityWindowKind.EncounterPaid),
            encounter,
            run.AccessState));
        if (HasActiveCatalogTrigger(working, OccurrenceKind.IceEncountered, run.AttackedServer, ice.Id))
        {
            QueueIceEncounteredOccurrence(working, run, ice.Id);
        }
    }

    private static void StepEncounter(WorkingState working, RunState run)
    {
        RunEncounterState encounter = run.Encounter
            ?? throw new ResolutionFailureException("Encounter processing requires an initialized encounter.");
        if (encounter.Status != RunEncounterStatus.Initialized)
        {
            throw new ResolutionFailureException("An encounter cannot be completed twice.");
        }

        CardInstanceState ice = GetCard(working, encounter.IceId);
        if (!working.BehaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec iceSpec))
        {
            throw new ResolutionFailureException(
                $"Encounter ICE '{ice.DefinitionId}' is not registered in the ICE behavior spec catalog; "
                + "its subroutines cannot be resolved.");
        }

        // CR 6.5/9.8: every unbroken subroutine resolves in printed order;
        // broken subroutines are skipped, and an end-the-run subroutine stops
        // the remaining sequence.
        RunState currentRun = run;
        var resolvedSubroutines = new List<RunSubroutineState>(encounter.Subroutines.Count);
        bool endTheRun = false;
        for (int index = 0; index < encounter.Subroutines.Count && !endTheRun; index++)
        {
            RunSubroutineState subroutine = encounter.Subroutines[index];
            if (subroutine.Broken || subroutine.Resolved)
            {
                resolvedSubroutines.Add(subroutine);
                continue;
            }

            IceSubroutineSpec subroutineSpec = iceSpec.Subroutines[subroutine.Ordinal];
            if (subroutineSpec is TrashInstalledRunnerCardSubroutineSpec
                or InstallCardSubroutineSpec
                or PayCreditsOrTagSubroutineSpec)
            {
                // Persist all preceding progress before yielding. The
                // serialized ICE cursor replaces the run timing frame and
                // resumes this exact ordinal, so already-resolved effects
                // are never replayed after a save/load boundary.
                for (int remaining = resolvedSubroutines.Count; remaining < encounter.Subroutines.Count; remaining++)
                {
                    resolvedSubroutines.Add(encounter.Subroutines[remaining]);
                }

                var partialEncounter = new RunEncounterState(
                    encounter.Version,
                    encounter.Id,
                    encounter.IceId,
                    RunEncounterStatus.Initialized,
                    resolvedSubroutines);
                RunState partialRun = currentRun.WithEncounter(partialEncounter);
                working.RunState = partialRun;
                YieldIceSubroutineChoice(
                    working,
                    partialRun,
                    ice,
                    subroutine,
                    subroutineSpec);
                return;
            }

            switch (subroutineSpec)
            {
                case LoseCreditsSubroutineSpec loseCredits:
                    PlayerState debtor = GetPlayer(working, loseCredits.Target);
                    // CR 26.03 1.10.3b (LOSE path): losing credits moves them
                    // from the debtor's credit pool (wallet) to the bank, and
                    // is clamped to what the wallet holds — "Players cannot
                    // lose credits from cards." The run-scoped hosted pool
                    // (Overclock) is never touched by a credit loss; only the
                    // SPEND path (PayRunnerRunCredits) draws it.
                    int loss = Math.Min(loseCredits.Amount, debtor.Credits);
                    if (loss > 0)
                    {
                        ApplyCreditDelta(working, debtor.Id, -loss);
                    }

                    break;
                case EndRunIfCreditsAtMostSubroutineSpec endIf:
                    PlayerState endIfDebtor = GetPlayer(working, endIf.Target);
                    // CR 26.03 1.13.3: a conditional "has N credits or less"
                    // checks the credits in the player's credit pool/wallet.
                    // Hosted credits on a run event are a separate source and
                    // do not count toward this threshold. They remain
                    // spendable only for costs that explicitly allow them.
                    if (endIfDebtor.Credits <= endIf.CreditLimit)
                    {
                        endTheRun = true;
                    }

                    break;
                case EndRunSubroutineSpec:
                    endTheRun = true;
                    break;
                case RunRestrictionSubroutineSpec restriction:
                    currentRun = ApplyRunRestriction(
                        working,
                        currentRun,
                        ice,
                        subroutine,
                        restriction);
                    break;
                default:
                    throw new ResolutionFailureException(
                        $"Encounter subroutine at ordinal {subroutine.Ordinal} is not supported by subroutine resolution.");
            }

            currentRun = RecordResolvedUnbrokenSubroutine(
                currentRun,
                ice,
                encounter.Id,
                subroutine);

            resolvedSubroutines.Add(new RunSubroutineState(
                subroutine.Version,
                subroutine.Id,
                subroutine.Ordinal,
                subroutine.Broken,
                true));
        }

        for (int index = resolvedSubroutines.Count; index < encounter.Subroutines.Count; index++)
        {
            resolvedSubroutines.Add(encounter.Subroutines[index]);
        }

        var completed = new RunEncounterState(
            encounter.Version,
            encounter.Id,
            encounter.IceId,
            RunEncounterStatus.Complete,
            resolvedSubroutines);
        // Encounter-scoped lingering effects expire when the encounter completes.
        RunState progressed = currentRun.WithLingeringEffects(
            currentRun.LingeringEffects
                .Where(effect => effect.Expiry != RunLingeringEffectExpiry.Encounter)
                .ToArray());
        Emit(working, EngineEventKinds.EncounterCompleted, new EncounterCompletedEventPayload(run.Id, encounter.Id));
        if (endTheRun)
        {
            SetRunTiming(working, progressed.WithTiming(
                RunTimingStep.RunEnd,
                progressed.CurrentIceIndex,
                progressed.CurrentIceId,
                false,
                true,
                RunEndReason.EndedByRule,
                null,
                completed,
                progressed.AccessState));
            return;
        }

        SetRunTiming(working, progressed.WithTiming(
            RunTimingStep.MovementPaidWindow,
            progressed.CurrentIceIndex,
            progressed.CurrentIceId,
            progressed.Successful,
            progressed.Unsuccessful,
            progressed.EndReason,
            CreatePriority(RunPriorityWindowKind.MovementPaid),
            completed,
            progressed.AccessState));
    }

    private static RunState ApplyRunRestriction(
        WorkingState working,
        RunState run,
        CardInstanceState ice,
        RunSubroutineState subroutine,
        RunRestrictionSubroutineSpec specification)
    {
        // The restriction and its execution-history entry are advanced from
        // the same immutable reducer boundary. This gives save/load
        // validation a structural proof that the restriction was not merely
        // inserted into a detached RunState snapshot.
        run = RecordResolvedUnbrokenSubroutine(
            run,
            ice,
            run.Encounter?.Id
                ?? throw new ResolutionFailureException("The encounter is missing for a run restriction."),
            subroutine);
        RunRestriction restriction = new(
            specification.Restriction,
            ice.Id,
            ice.DefinitionId,
            subroutine.Id,
            subroutine.Ordinal,
            "ice-subroutine:cannot-steal-or-trash-corp-cards");
        IReadOnlyList<RunRestriction> existing = run.RunRestrictions ?? [];
        if (existing.Any(candidate => candidate == restriction))
        {
            return run;
        }

        RunState updated = run.WithRunRestrictions(existing.Append(restriction).ToArray());
        working.RunState = updated;
        return updated;
    }

    private static RunState RecordResolvedUnbrokenSubroutine(
        RunState run,
        CardInstanceState ice,
        EncounterId encounterId,
        RunSubroutineState subroutine)
    {
        if (subroutine.Broken)
        {
            throw new ResolutionFailureException(
                "A broken ICE subroutine cannot be recorded as resolved execution history.");
        }

        RunResolvedSubroutineLedgerEntry entry = new(
            run.Id,
            encounterId,
            ice.Id,
            ice.DefinitionId,
            subroutine.Id,
            subroutine.Ordinal);
        IReadOnlyList<RunResolvedSubroutineLedgerEntry> existing = run.ResolvedSubroutineLedger ?? [];
        if (existing.Any(candidate => candidate == entry))
        {
            return run;
        }

        return run.WithResolvedSubroutineLedger(existing.Append(entry).ToArray());
    }

    private static void YieldIceSubroutineChoice(
        WorkingState working,
        RunState run,
        CardInstanceState ice,
        RunSubroutineState subroutine,
        IceSubroutineSpec specification)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not RunTimingFrame timing
            || timing.RunId != run.Id
            || timing.Step != RunTimingStep.Encounter)
        {
            throw new ResolutionFailureException(
                "An ICE subroutine choice requires the exact encounter timing frame.");
        }

        ZoneState iceZone = GetRunIceZone(working, run);
        string frameIdentity = IceSubroutineFrame.ComputeIdentity(
            run.Id,
            run.Encounter?.Id ?? throw new ResolutionFailureException("The encounter is missing."),
            ice.Id,
            subroutine.Id,
            subroutine.Ordinal);
        (PlayerId owner, string prompt, IReadOnlyList<IceSubroutineChoiceParameters> materialized) =
            MaterializeIceSubroutineChoices(working, run, ice, subroutine, specification, frameIdentity);
        var choices = materialized.ToList();

        // A mandatory trash instruction with no legal target has no effect and
        // advances to the next printed subroutine.
        if (specification is TrashInstalledRunnerCardSubroutineSpec && choices.Count == 0)
        {
            ResolveIceSubroutineWithoutChoice(working, run, ice, subroutine);
            return;
        }

        working.ResolutionStack[^1] = new IceSubroutineFrame(
            run.Id,
            run.Encounter?.Id ?? throw new ResolutionFailureException("The encounter is missing."),
            ice.Id,
            ice.DefinitionId,
            subroutine.Id,
            subroutine.Ordinal,
            run.AttackedServer,
            iceZone.Id,
            iceZone.CardIds,
            working.BehaviorCatalog.Fingerprint,
            choices);
        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            owner,
            DecisionKind.IceSubroutineChoice,
            prompt,
            choices.Select(choice => new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ResolveIceSubroutine,
                DescribeIceSubroutineChoice(choice),
                choice)).ToArray());
    }

    /// <summary>
    /// Materializes the exact typed choice set for a printed ICE
    /// subroutine. This method is shared by the reducer and state-invariant
    /// projection so a loaded frame cannot retain a forged or stale action
    /// set while still passing validation.
    /// </summary>
    private static (PlayerId Owner, string Prompt, IReadOnlyList<IceSubroutineChoiceParameters> Choices)
        MaterializeIceSubroutineChoices(
            WorkingState working,
            RunState run,
            CardInstanceState ice,
            RunSubroutineState subroutine,
            IceSubroutineSpec specification,
            string frameIdentity)
    {
        var choices = new List<IceSubroutineChoiceParameters>();
        PlayerId owner;
        string prompt;

        switch (specification)
        {
            case TrashInstalledRunnerCardSubroutineSpec trashSpec:
                owner = GetPlayer(working, PlayerSide.Corp).Id;
                prompt = "Resolve the ICE subroutine: choose an installed Runner card to trash.";
                ZoneState? rig = working.Zones.FirstOrDefault(zone =>
                    zone.Kind == ZoneKind.RunnerRig
                    && zone.OwnerSide == PlayerSide.Runner
                    && zone.ServerId is null);
                if (rig is not null)
                {
                    foreach (CardInstanceId cardId in rig.CardIds)
                    {
                        CardInstanceState candidate = GetCard(working, cardId);
                        if (candidate.OwnerSide != PlayerSide.Runner
                            || candidate.ZoneId != rig.Id
                            || !candidate.FaceUp)
                        {
                            continue;
                        }

                        if (trashSpec.RequiredCardClass is { } requiredClass
                            && (working.RuntimeCardCatalog is null
                                || !working.RuntimeCardCatalog.TryGet(candidate.DefinitionId, out CardRuntimeSpec? runtime)
                                || runtime.CardClass != requiredClass))
                        {
                            continue;
                        }

                        choices.Add(new IceSubroutineChoiceParameters(
                            frameIdentity,
                            IceSubroutineChoiceKind.TrashInstalledRunnerCard,
                            cardId: candidate.Id));
                    }
                }

                break;
            case PayCreditsOrTagSubroutineSpec paySpec:
                owner = GetPlayer(working, PlayerSide.Runner).Id;
                prompt = $"Resolve the ICE subroutine: pay {paySpec.CreditAmount} credits or take {paySpec.TagAmount} tag(s).";
                PlayerState runner = GetPlayer(working, PlayerSide.Runner);
                if (CanAffordRunCredits(runner, run, paySpec.CreditAmount))
                {
                    foreach (int hostedCredits in LegalHostedCreditSplits(runner, run, paySpec.CreditAmount))
                    {
                        choices.Add(new IceSubroutineChoiceParameters(
                            frameIdentity,
                            IceSubroutineChoiceKind.PayCredits,
                            creditCost: paySpec.CreditAmount,
                            hostedCredits: hostedCredits));
                    }
                }

                choices.Add(new IceSubroutineChoiceParameters(
                    frameIdentity,
                    IceSubroutineChoiceKind.TakeTag));
                break;
            case InstallCardSubroutineSpec installSpec:
                owner = GetPlayer(working, PlayerSide.Corp).Id;
                prompt = installSpec.DestinationRule == IceInstallDestinationRule.DirectlyInward
                    ? "Resolve the ICE subroutine: choose an ICE from HQ or Archives to install directly inward."
                    : "Resolve the ICE subroutine: choose a Corp card from HQ or Archives to install.";
                InstallInitiationKind initiationKind = installSpec.DestinationRule == IceInstallDestinationRule.DirectlyInward
                    ? InstallInitiationKind.IceSubroutineInwardIgnoreCosts
                    : InstallInitiationKind.IceSubroutineNormal;
                foreach (ZoneState sourceZone in working.Zones
                             .Where(zone => zone.Kind is ZoneKind.CorpHand or ZoneKind.CorpDiscard
                                 && zone.OwnerSide == PlayerSide.Corp
                                 && zone.ServerId is null)
                             .OrderBy(zone => zone.Kind == ZoneKind.CorpHand ? 0 : 1))
                {
                    foreach (CardInstanceId cardId in sourceZone.CardIds.OrderBy(id => id.Value, StringComparer.Ordinal))
                    {
                        CardInstanceState candidate = GetCard(working, cardId);
                        if (candidate.OwnerSide != PlayerSide.Corp
                            || candidate.ZoneId != sourceZone.Id
                            || working.RuntimeCardCatalog is null
                            || !working.RuntimeCardCatalog.TryGet(candidate.DefinitionId, out CardRuntimeSpec? runtime)
                            || !IsBasicInstallClass(PlayerSide.Corp, runtime.CardClass)
                            || (installSpec.IceOnly && runtime.CardClass != RuntimeCardClass.Ice))
                        {
                            continue;
                        }

                        IEnumerable<(InstallDestinationKind DestinationKind, ServerId? ServerId, bool CreateNewRemote)> destinations =
                            installSpec.DestinationRule == IceInstallDestinationRule.DirectlyInward
                                ? [(InstallDestinationKind.ServerIce, run.AttackedServer, false)]
                                : InstallDestinations(working, PlayerSide.Corp, runtime);
                        foreach ((InstallDestinationKind destinationKind, ServerId? serverId, bool createNewRemote) in destinations)
                        {
                            InstallPlan? plan = BuildInstallPlan(
                                working,
                                PlayerSide.Corp,
                                cardId,
                                destinationKind,
                                serverId,
                                createNewRemote,
                                initiationKind,
                                installSpec.DestinationRule == IceInstallDestinationRule.DirectlyInward
                                    ? ice.Id
                                    : null);
                            if (plan is null)
                            {
                                continue;
                            }

                            choices.Add(new IceSubroutineChoiceParameters(
                                frameIdentity,
                                IceSubroutineChoiceKind.InstallCard,
                                cardId: cardId,
                                destinationKind: destinationKind,
                                serverId: serverId,
                                createNewRemote: createNewRemote,
                                creditCost: plan.InstallCost));
                        }
                    }
                }

                choices.Add(new IceSubroutineChoiceParameters(
                    frameIdentity,
                    IceSubroutineChoiceKind.Decline));
                break;
            default:
                throw new ResolutionFailureException(
                    $"ICE subroutine type '{specification.GetType().Name}' cannot yield a choice.");
        }

        return (owner, prompt, choices);
    }

    /// <summary>
    /// Detached projection used by invariant validation. It materializes only
    /// the semantic payloads, never action IDs or allocator state.
    /// </summary>
    internal static IReadOnlyList<IceSubroutineChoiceParameters> MaterializeIceSubroutineChoiceParameters(
        GameState state,
        IceSubroutineFrame frame,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        RunState run = working.RunState
            ?? throw new ResolutionFailureException("An ICE subroutine choice requires a live run.");
        if (run.Id != frame.RunId
            || run.Step != RunTimingStep.Encounter
            || run.Encounter is not { } encounter
            || encounter.Id != frame.EncounterId
            || encounter.IceId != frame.IceId)
        {
            throw new ResolutionFailureException("The ICE subroutine frame is stale for the current encounter.");
        }

        CardInstanceState ice = GetCard(working, frame.IceId);
        if (ice.DefinitionId != frame.IceDefinitionId
            || !behaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec? iceSpec)
            || frame.Ordinal < 0
            || frame.Ordinal >= iceSpec.Subroutines.Count
            || encounter.Subroutines.SingleOrDefault(candidate => candidate.Id == frame.SubroutineId) is not { } subroutine
            || subroutine.Ordinal != frame.Ordinal)
        {
            throw new ResolutionFailureException("The ICE subroutine frame does not identify the current catalog subroutine.");
        }

        return MaterializeIceSubroutineChoices(
            working,
            run,
            ice,
            subroutine,
            iceSpec.Subroutines[frame.Ordinal],
            frame.FrameIdentity).Choices;
    }

    private static void StepIceSubroutineFrame(WorkingState working, IceSubroutineFrame frame)
    {
        if (working.PendingDecision is not null)
        {
            throw new ResolutionFailureException(
                "An ICE subroutine frame cannot advance while another decision is pending.");
        }

        if (frame.Stage != IceSubroutineFrameStage.AwaitingChoice
            || frame.Choices.Count == 0)
        {
            throw new ResolutionFailureException(
                "An ICE subroutine frame has no legal serialized continuation.");
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            GetPlayer(working, frame.Choices[0].Choice is IceSubroutineChoiceKind.PayCredits
                or IceSubroutineChoiceKind.TakeTag
                ? PlayerSide.Runner
                : PlayerSide.Corp).Id,
            DecisionKind.IceSubroutineChoice,
            "Resolve the printed ICE subroutine.",
            frame.Choices.Select(choice => new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ResolveIceSubroutine,
                DescribeIceSubroutineChoice(choice),
                choice)).ToArray());
    }

    private static string DescribeIceSubroutineChoice(IceSubroutineChoiceParameters choice) => choice.Choice switch
    {
        IceSubroutineChoiceKind.TrashInstalledRunnerCard => $"Trash installed Runner card {choice.CardId!.Value.Value}",
        IceSubroutineChoiceKind.PayCredits => $"Pay {choice.CreditCost} credits",
        IceSubroutineChoiceKind.TakeTag => "Take the tag fallback",
        IceSubroutineChoiceKind.Decline => "Decline",
        IceSubroutineChoiceKind.InstallCard => $"Install Corp card {choice.CardId!.Value.Value}",
        _ => "Resolve ICE subroutine",
    };

    private static void ResolveIceSubroutineWithoutChoice(
        WorkingState working,
        RunState run,
        CardInstanceState ice,
        RunSubroutineState subroutine)
    {
        RunEncounterState encounter = run.Encounter
            ?? throw new ResolutionFailureException("The encounter is missing.");
        RunSubroutineState[] resolved = encounter.Subroutines
            .Select(candidate => candidate.Id == subroutine.Id
                ? new RunSubroutineState(candidate.Version, candidate.Id, candidate.Ordinal, candidate.Broken, true)
                : candidate)
            .ToArray();
        RunState recorded = RecordResolvedUnbrokenSubroutine(
            run,
            ice,
            encounter.Id,
            subroutine);
        working.RunState = recorded.WithEncounter(new RunEncounterState(
            encounter.Version,
            encounter.Id,
            encounter.IceId,
            encounter.Status,
            resolved));
    }

    private static void ConsumeIceSubroutineChoice(
        WorkingState working,
        IceSubroutineChoiceParameters parameters)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not IceSubroutineFrame frame)
        {
            throw new ResolutionFailureException(
                "The submitted ICE subroutine choice does not match the active frame.");
        }

        if (frame.Stage != IceSubroutineFrameStage.AwaitingChoice
            || !frame.Choices.Contains(parameters))
        {
            throw new ResolutionFailureException(
                "The submitted ICE subroutine choice is not part of the frozen action bijection.");
        }

        RunState run = working.RunState
            ?? throw new ResolutionFailureException("An ICE subroutine choice requires a live run.");
        if (run.Id != frame.RunId
            || run.Step != RunTimingStep.Encounter
            || run.AttackedServer != frame.ServerId
            || run.Encounter is null
            || run.Encounter.Id != frame.EncounterId
            || run.Encounter.IceId != frame.IceId
            || run.Encounter.Subroutines.SingleOrDefault(candidate => candidate.Id == frame.SubroutineId) is not { } subroutine
            || subroutine.Ordinal != frame.Ordinal
            || subroutine.Resolved
            || subroutine.Broken)
        {
            throw new ResolutionFailureException(
                "The ICE subroutine choice is stale for the authoritative run encounter.");
        }

        if (!string.Equals(frame.CatalogFingerprint, working.BehaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            throw new ResolutionFailureException(
                "The ICE subroutine choice catalog fingerprint does not match the active behavior catalog.");
        }

        RunEncounterState encounter = run.Encounter
            ?? throw new ResolutionFailureException("The encounter is missing.");

        ZoneState iceZone = GetRunIceZone(working, run);
        if (iceZone.Id != frame.IceZoneId
            || !iceZone.CardIds.SequenceEqual(frame.IceZoneCardIds)
            || !iceZone.CardIds.Contains(frame.IceId))
        {
            throw new ResolutionFailureException(
                "The ICE subroutine choice is stale for the authoritative ICE topology.");
        }

        CardInstanceState ice = GetCard(working, frame.IceId);
        if (ice.DefinitionId != frame.IceDefinitionId
            || !working.BehaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec? iceSpec)
            || frame.Ordinal >= iceSpec.Subroutines.Count)
        {
            throw new ResolutionFailureException(
                "The ICE subroutine choice is stale for the authenticated ICE catalog entry.");
        }

        IceSubroutineSpec specification = iceSpec.Subroutines[frame.Ordinal];
        switch (parameters.Choice)
        {
            case IceSubroutineChoiceKind.TrashInstalledRunnerCard:
                if (specification is not TrashInstalledRunnerCardSubroutineSpec trashSpec
                    || parameters.CardId is not { } targetId)
                {
                    throw new ResolutionFailureException("The choice is not valid for this ICE subroutine.");
                }

                CardInstanceState target = GetCard(working, targetId);
                ZoneState rig = working.Zones.SingleOrDefault(zone =>
                    zone.Kind == ZoneKind.RunnerRig
                    && zone.OwnerSide == PlayerSide.Runner
                    && zone.ServerId is null)
                    ?? throw new ResolutionFailureException("The Runner Rig does not exist.");
                if (target.OwnerSide != PlayerSide.Runner
                    || target.ZoneId != rig.Id
                    || !rig.CardIds.Contains(target.Id)
                    || !target.FaceUp)
                {
                    throw new ResolutionFailureException("The selected card is no longer an installed Runner card.");
                }

                if (trashSpec.RequiredCardClass is { } requiredClass
                    && (working.RuntimeCardCatalog is null
                        || !working.RuntimeCardCatalog.TryGet(target.DefinitionId, out CardRuntimeSpec? runtime)
                        || runtime.CardClass != requiredClass))
                {
                    throw new ResolutionFailureException("The selected Runner card no longer satisfies the typed class filter.");
                }

                TrashCard(working, target, rig, GetZone(working, ZoneKind.RunnerHeap));
                break;
            case IceSubroutineChoiceKind.PayCredits:
                if (specification is not PayCreditsOrTagSubroutineSpec paySpec
                    || parameters.CreditCost != paySpec.CreditAmount
                    || parameters.HostedCredits < 0)
                {
                    throw new ResolutionFailureException("The payment choice is not valid for this ICE subroutine.");
                }

                run = PayRunnerRunCredits(
                    working,
                    run,
                    paySpec.CreditAmount,
                    parameters.HostedCredits);
                break;
            case IceSubroutineChoiceKind.TakeTag:
                if (specification is not PayCreditsOrTagSubroutineSpec tagSpec)
                {
                    throw new ResolutionFailureException("The tag fallback is not valid for this ICE subroutine.");
                }

                ApplyTags(working, GetPlayer(working, PlayerSide.Runner).Id, tagSpec.TagAmount);
                break;
            case IceSubroutineChoiceKind.Decline:
                if (specification is not InstallCardSubroutineSpec)
                {
                    throw new ResolutionFailureException("Decline is not valid for this ICE subroutine.");
                }

                break;
            case IceSubroutineChoiceKind.InstallCard:
                if (specification is not InstallCardSubroutineSpec installSpec
                    || parameters.CardId is not { } installCardId
                    || parameters.DestinationKind is not { } installDestination)
                {
                    throw new ResolutionFailureException("The install choice is not valid for this ICE subroutine.");
                }

                InstallInitiationKind initiationKind = installSpec.DestinationRule == IceInstallDestinationRule.DirectlyInward
                    ? InstallInitiationKind.IceSubroutineInwardIgnoreCosts
                    : InstallInitiationKind.IceSubroutineNormal;
                CardInstanceState installCard = GetCard(working, installCardId);
                InstallPlan plan = BuildInstallPlan(
                    working,
                    PlayerSide.Corp,
                    installCardId,
                    installDestination,
                    parameters.ServerId,
                    parameters.CreateNewRemote,
                    initiationKind,
                    installSpec.DestinationRule == IceInstallDestinationRule.DirectlyInward
                        ? frame.IceId
                        : null)
                    ?? throw new ResolutionFailureException("The selected Corp install is no longer legal.");
                if (parameters.CreditCost != plan.InstallCost
                    || parameters.HostedCredits != 0
                    || plan.Card.Id != installCard.Id)
                {
                    throw new ResolutionFailureException("The selected Corp install cost or source is stale.");
                }

                working.ResolutionStack[^1] = frame.WithStage(IceSubroutineFrameStage.AwaitingInstall);
                working.ResolutionStack.Add(new InstallFrame(
                    GetPlayer(working, PlayerSide.Corp).Id,
                    plan.Card.Id,
                    plan.Card.DefinitionId,
                    plan.Spec.CardClass,
                    plan.Source.Id,
                    plan.Source.Kind,
                    plan.DestinationKind,
                    plan.ServerId,
                    plan.CreateNewRemote,
                    plan.InstallCost,
                    plan.CandidateReplacementCardIds,
                    plan.RequiredReplacementCardIds,
                    [],
                    plan.DestinationZoneId,
                    plan.CandidateReplacementDefinitionIds,
                    initiationKind,
                    frame.FrameIdentity,
                    plan.InstallCostQuote,
                    plan.InwardAnchorIceId));
                return;
            default:
                throw new ResolutionFailureException(
                    $"ICE subroutine choice '{parameters.Choice}' is not yet executable.");
        }

        encounter = run.Encounter
            ?? throw new ResolutionFailureException("The encounter is missing after ICE subroutine resolution.");
        var resolvedSubroutines = encounter.Subroutines
            .Select(candidate => candidate.Id == subroutine.Id
                ? new RunSubroutineState(candidate.Version, candidate.Id, candidate.Ordinal, candidate.Broken, true)
                : candidate)
            .ToArray();
        run = RecordResolvedUnbrokenSubroutine(
            run,
            ice,
            encounter.Id,
            subroutine);
        working.RunState = run.WithEncounter(new RunEncounterState(
            encounter.Version,
            encounter.Id,
            encounter.IceId,
            encounter.Status,
            resolvedSubroutines));
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        working.ResolutionStack.Add(new RunTimingFrame(run.Id, RunTimingStep.Encounter));
    }

    private static void StepMoveInward(WorkingState working, RunState run)
    {
        if (run.CurrentIceIndex < 0)
        {
            throw new ResolutionFailureException("A run cannot move inward after reaching the server.");
        }

        ZoneState iceZone = GetRunIceZone(working, run);
        int nextIndex = run.CurrentIceIndex - 1;
        CardInstanceId? nextIceId = nextIndex >= 0
            ? iceZone.CardIds[nextIndex]
            : null;
        Emit(working, EngineEventKinds.RunMoved, new RunMovedEventPayload(
            run.Id,
            run.CurrentIceIndex,
            nextIndex,
            nextIceId));
        SetRunTiming(working, run.WithTiming(
            RunTimingStep.AfterMovePaidWindow,
            nextIndex,
            nextIceId,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            CreatePriority(RunPriorityWindowKind.AfterMovePaid, corpMayRezNonIce: true),
            null,
            run.AccessState));
    }

    private static void StepRunApproachServer(WorkingState working, RunState run)
    {
        if (run.CurrentIceIndex >= 0 || run.CurrentIceId is not null)
        {
            throw new ResolutionFailureException("A run can approach the server only after passing all ICE.");
        }

        Emit(working, EngineEventKinds.ServerApproached, new ServerApproachedEventPayload(
            run.Id,
            run.AttackedServer));
        SetRunTiming(working, run.WithTiming(
            RunTimingStep.Success,
            -1,
            null,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            null,
            null,
            run.AccessState));
        if (HasActiveCatalogTrigger(working, OccurrenceKind.ServerApproached, run.AttackedServer))
        {
            QueueServerApproachedOccurrence(working, run);
        }
    }

    private static void StepRunSuccess(WorkingState working, RunState run)
    {
        if (run.CurrentIceIndex >= 0)
        {
            throw new ResolutionFailureException("A run can succeed only after all ICE has been passed.");
        }

        Emit(working, EngineEventKinds.RunSuccessful, new RunSuccessfulEventPayload(run.Id, run.AttackedServer));
        RecordTurnRunSuccess(working, run.AttackedServer);
        SetRunTiming(working, run.WithTiming(
            RunTimingStep.BreachBoundary,
            -1,
            null,
            true,
            false,
            null,
            null,
            null,
            RunAccessState.NotStarted));
        if (HasActiveCatalogTrigger(
                working,
                OccurrenceKind.RunSuccessful,
                run.AttackedServer,
                run.InitiatingSource))
        {
            QueueRunSuccessfulOccurrence(working, run);
        }
    }

    /// <summary>
    /// Opens the normal occurrence/checkpoint pipeline after success has
    /// committed its event and advanced the run cursor to the breach
    /// boundary. The boundary remains below this marker so a yielded trigger
    /// cannot re-declare success or begin breach processing early.
    /// </summary>
    private static void QueueRunSuccessfulOccurrence(WorkingState working, RunState run)
    {
        working.ResolutionStack.Add(new EffectResolutionFrame(
            run.InitiatingPlayer,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.RunSuccessful,
                run.InitiatingPlayer,
                run.InitiatingSource,
                run.AttackedServer))));
    }

    private static void QueueServerApproachedOccurrence(WorkingState working, RunState run)
    {
        working.ResolutionStack.Add(new EffectResolutionFrame(
            run.InitiatingPlayer,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.ServerApproached,
                run.InitiatingPlayer,
                serverId: run.AttackedServer))));
    }

    private static void QueueIceEncounteredOccurrence(
        WorkingState working,
        RunState run,
        CardInstanceId iceId)
    {
        working.ResolutionStack.Add(new EffectResolutionFrame(
            run.InitiatingPlayer,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.IceEncountered,
                run.InitiatingPlayer,
                subjectCardId: iceId,
                serverId: run.AttackedServer))));
    }

    private static void StepBreachBoundary(WorkingState working, RunState run)
    {
        if (!run.Successful)
        {
            throw new ResolutionFailureException("The breach boundary requires a successful run.");
        }

        ServerState server = GetBreachServer(working, run);
        ZoneState rootZone = working.Zones.SingleOrDefault(zone => zone.Id == server.RootZoneId)
            ?? throw new ResolutionFailureException($"Run server '{run.AttackedServer}' has no root zone.");
        ZoneState? hiddenZone = GetBreachHiddenZone(working, server);

        // BeginBreach is the authoritative boundary for first-HQ semantics.
        // Determine firstness before appending the occurrence, then snapshot
        // active installed sources and consume their FirstX keys while the
        // same formal-turn facts remain current. The existing
        // RunBreachAccessModifier arithmetic below remains the sole access
        // calculation for both legacy and first-HQ terms.
        if (server.Kind == ServerKind.Headquarters
            && RecordTurnHqBreach(working, server.Id))
        {
            List<RunBreachAccessModifier> firstHqModifiers =
                SnapshotFirstHqBreachAccessModifiers(working, server.Id);
            if (firstHqModifiers.Count > 0)
            {
                IReadOnlyList<RunBreachAccessModifier> combined =
                    (run.BreachAccessModifiers ?? [])
                    .Concat(firstHqModifiers)
                    .ToArray();
                run = run.WithBreachAccessModifiers(combined);
                RecordTurnFirstHqBreachUses(working, firstHqModifiers);
            }
        }

        // CR 26.03 7.4.1: "The candidates at the beginning of a breach are
        // as follows: a. Each card in the root of the breached server is a
        // candidate. b. If the breached server is HQ, each card in the
        // Corp's hand is a candidate. c. If the breached server is R&D, the
        // top card of the Corp's deck is a candidate. d. If the breached
        // server is Archives, each card in the Corp's discard pile is a
        // candidate." The root cards are candidates for every server kind
        // and are listed first (7.4.1a; for a remote server the root zone IS
        // the candidate pool, so it is not added twice); the central-zone
        // candidates follow in 7.4.1 order: the HQ hand / R&D deck top are
        // the hidden-source mechanism below (randomAccessLimit, CR 26.03
        // 7.3.5/7.4.7), and the Archives discard follows its root per 7.4.1d.
        List<CardInstanceId> candidates = server.Kind switch
        {
            ServerKind.Remote => new(rootZone.CardIds),
            ServerKind.Archives =>
            [
                .. rootZone.CardIds,
                .. GetZone(working, ZoneKind.CorpDiscard).CardIds,
            ],
            ServerKind.Headquarters or ServerKind.RAndD => new(rootZone.CardIds),
            _ => new List<CardInstanceId>(),
        };
        int baseRandomAccessLimit = hiddenZone is { CardIds.Count: > 0 } ? 1 : 0;
        int additionalRandomAccess = 0;
        if (run.BreachAccessModifiers is { } modifiers)
        {
            foreach (RunBreachAccessModifier modifier in modifiers)
            {
                if (modifier.AttackedServerId == server.Id)
                {
                    additionalRandomAccess = checked(additionalRandomAccess + modifier.Delta);
                }
            }
        }

        int requestedRandomAccessLimit = checked(baseRandomAccessLimit + additionalRandomAccess);
        int availableHiddenCount = RunState.AvailableHiddenBreachCards(hiddenZone, run.AccessedCards).Count;
        int randomAccessLimit = availableHiddenCount > 0
            ? Math.Min(availableHiddenCount, requestedRandomAccessLimit)
            : 0;

        // CR 26.03 7.5.2: at the beginning of an Archives breach, every card
        // in the Corp discard is turned faceup authoritatively and remains
        // faceup. This is intentionally separate from selecting/accessing a
        // candidate: an Archives root card is not revealed merely because a
        // breach began, while discard cards are all permanently revealed.
        if (server.Kind == ServerKind.Archives)
        {
            ZoneState discard = GetZone(working, ZoneKind.CorpDiscard);
            foreach (CardInstanceId discardId in discard.CardIds)
            {
                CardInstanceState discardCard = GetCard(working, discardId);
                if (!discardCard.FaceUp)
                {
                    ReplaceCard(working, new CardInstanceState(
                        discardCard.Id,
                        discardCard.DefinitionId,
                        discardCard.ZoneId,
                        discardCard.OwnerSide,
                        faceUp: true,
                        rezzed: discardCard.Rezzed,
                        advancementCounters: discardCard.AdvancementCounters));
                }
            }
        }

        run = run.WithBreach(RunAccessState.InProgress, candidates, randomAccessLimit, [], []);
        Emit(working, EngineEventKinds.BreachStarted, new BreachStartedEventPayload(
            run.Id,
            server.Id,
            randomAccessLimit,
            candidates.Count));

        if (candidates.Count == 0 && randomAccessLimit == 0)
        {
            SetRunTiming(working, run.WithTiming(
                RunTimingStep.RunEnd,
                -1,
                null,
                run.Successful,
                run.Unsuccessful,
                RunEndReason.BreachCompleted,
                null,
                null,
                RunAccessState.Completed));
            return;
        }

        run = run.WithTiming(
            RunTimingStep.BreachCandidateSelection,
            -1,
            null,
            run.Successful,
            run.Unsuccessful,
            null,
            null,
            null,
            RunAccessState.InProgress);
        SetRunTiming(working, run);
        YieldBreachCandidateDecision(working, run);
    }

    private static void CompleteRun(WorkingState working, RunState run)
    {
        if (run.EndReason is null)
        {
            throw new ResolutionFailureException("Run-end cleanup requires an explicit end reason.");
        }

        RunId runId = run.Id;
        PlayerSide initiatingSide = run.InitiatingSide;
        PlayerId initiatingPlayer = run.InitiatingPlayer;
        CardInstanceId? initiatingSource = run.InitiatingSource;
        ServerId attackedServer = run.AttackedServer;
        RunEndReason endReason = run.EndReason.Value;
        bool successful = run.Successful;
        CardInstanceId? activeRunEventId = run.ActiveRunEventId;
        bool realTurnRun = IsRealTurnRun(working, run);

        // Run-scoped hosted credits (Overclock) cannot leave the run: any
        // unspent pool expires at run end without returning to the wallet.
        if (run.RunCredits > 0)
        {
            Emit(working, EngineEventKinds.RunCreditsAdjusted, new RunCreditsAdjustedEventPayload(
                runId, -run.RunCredits, 0));
        }

        Emit(working, EngineEventKinds.RunEnded, new RunEndedEventPayload(
            runId,
            initiatingSide,
            endReason,
            successful));
        ExpireRunLingeringEffects(working, run);
        // CR 26.03 6.9.6/8.6.7: the played run event is trashed to the
        // Runner's heap faceup/public when the run ends; its hosted credits
        // expired with the pool above.
        if (activeRunEventId is { } activeEvent)
        {
            CardInstanceState eventCard = GetCard(working, activeEvent);
            ZoneState eventZone = working.Zones.Single(zone => zone.Id == eventCard.ZoneId);
            ZoneState heap = GetZone(working, ZoneKind.RunnerHeap);
            TrashCard(working, eventCard, eventZone, heap);
        }
        // CR 26.03 6.5/9.8: breakers never leave the rig; encounter-scoped
        // pump effects live on the run/encounter state, so no per-server
        // cleanup is needed when the run ends.
        working.RunState = null;
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not RunTimingFrame frame
            || frame.RunId != run.Id)
        {
            throw new ResolutionFailureException("Run-end cleanup did not have the matching timing frame.");
        }

        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        working.PendingDecision = null;

        if (realTurnRun)
        {
            if (working.ResolutionStack.Count != 1
                || working.ResolutionStack[^1] is not TurnFrame parent
                || parent.Stage != TurnFrameStage.AwaitingAction
                || parent.ActiveSide != PlayerSide.Runner
                || parent.TurnNumber != working.Turn.TurnNumber)
            {
                throw new ResolutionFailureException(
                    "A real-turn run ended without its matching Runner action frame.");
            }

            working.ResolutionStack[^1] = new TurnFrame(
                parent.Version,
                parent.BehaviorId,
                parent.ActiveSide,
                parent.TurnNumber,
                TurnFrameStage.ActionPriority,
                TurnWindow(parent.ActiveSide),
                parent.ActiveSide,
                forcedActionEnd: parent.ForcedActionEnd);
        }

        if (HasActiveCatalogTrigger(working, OccurrenceKind.RunEnded, attackedServer))
        {
            QueueRunEndedOccurrence(
                working,
                initiatingPlayer,
                initiatingSource,
                attackedServer,
                successful);
        }
    }

    /// <summary>
    /// Expires every run-local effect at the single run-end boundary. An
    /// authenticated icebreaker cleanup may move its still-active physical
    /// source to the Runner heap; a source that moved or became inactive is
    /// intentionally left untouched. Matching the catalog declaration keeps
    /// this generic for future breakers without definition-ID branches.
    /// </summary>
    private static void ExpireRunLingeringEffects(WorkingState working, RunState run)
    {
        foreach (RunLingeringEffect effect in run.LingeringEffects)
        {
            Emit(working, EngineEventKinds.LingeringEffectExpired, new LingeringEffectExpiredEventPayload(
                effect.Id,
                effect.Expiry == RunLingeringEffectExpiry.Run
                    ? LingeringEffectExpiry.Run
                    : LingeringEffectExpiry.Encounter));

            if (effect.Expiry != RunLingeringEffectExpiry.Run
                || effect.Kind != RunLingeringEffectKind.IcebreakerRunEndCleanup
                || effect.SourceCardId is not { } sourceCardId)
            {
                continue;
            }

            CardInstanceState? source = working.Cards.FirstOrDefault(card => card.Id == sourceCardId);
            if (source is null
                || source.OwnerSide != PlayerSide.Runner
                || !RuntimeCardActivity.IsActive(source, working.Zones)
                || !working.BehaviorCatalog.TryGetIcebreaker(source.DefinitionId, out IcebreakerBehaviorSpec? sourceSpec)
                || sourceSpec.RunEndCleanup is not { } cleanup
                || cleanup.BehaviorId != effect.BehaviorId)
            {
                continue;
            }

            ZoneState sourceZone = working.Zones.Single(zone => zone.Id == source.ZoneId);
            TrashCard(working, source, sourceZone, GetZone(working, ZoneKind.RunnerHeap));
            if (working.RulesState?.Programs.Any(program => program.CardId == sourceCardId) == true)
            {
                RemoveProgramRegistration(working, sourceCardId);
            }
        }
    }

    private static void QueueRunEndedOccurrence(
        WorkingState working,
        PlayerId controller,
        CardInstanceId? sourceCardId,
        ServerId serverId,
        bool successful)
    {
        working.ResolutionStack.Add(new EffectResolutionFrame(
            controller,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.RunEnded,
                controller,
                sourceCardId,
                serverId,
                successful))));
    }

    private static void QueueCardAccessedOccurrence(
        WorkingState working,
        PlayerId controller,
        CardInstanceId cardId,
        ServerId serverId)
    {
        working.ResolutionStack.Add(new EffectResolutionFrame(
            controller,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.CardAccessed,
                controller,
                cardId,
                serverId))));
    }

    private static bool IsRealTurnRun(WorkingState working, RunState run) =>
        run.InitiatingSide == PlayerSide.Runner
        && working.ResolutionStack.Count == 2
        && working.ResolutionStack[^1] is RunTimingFrame timing
        && timing.RunId == run.Id
        && working.ResolutionStack[^2] is TurnFrame parent
        && parent.Stage == TurnFrameStage.AwaitingAction
        && parent.ActiveSide == PlayerSide.Runner
        && parent.TurnNumber == working.Turn.TurnNumber
        && working.Turn.ActiveSide == PlayerSide.Runner
        && (run.ActiveRunEventId is { } activeEventId
            ? run.InitiatingSource == activeEventId
                && IsValidActiveRunEvent(working, run, activeEventId)
            : run.InitiatingSource is null || IsValidInstalledRunSource(working, run));

    private static bool IsValidActiveRunEvent(WorkingState working, RunState run, CardInstanceId activeEventId)
    {
        CardInstanceState? source = working.Cards.FirstOrDefault(card => card.Id == activeEventId);
        ZoneState? sourceZone = source is null
            ? null
            : working.Zones.FirstOrDefault(zone => zone.Id == source.ZoneId);
        if (source is null
            || source.OwnerSide != PlayerSide.Runner
            || !source.FaceUp
            || sourceZone is null
            || sourceZone.Kind != ZoneKind.RunnerPlayArea
            || sourceZone.OwnerSide != PlayerSide.Runner
            || sourceZone.ServerId is not null
            || sourceZone.CardIds.Count != 1
            || sourceZone.CardIds[0] != activeEventId
            || !working.BehaviorCatalog.TryGetRunEvent(source.DefinitionId, out RunEventBehaviorSpec? spec))
        {
            return false;
        }

        ServerState? attackedServer = working.Servers.FirstOrDefault(server => server.Id == run.AttackedServer);
        return attackedServer is not null
            && (spec.AllowedTargetServers is null
                || spec.AllowedTargetServers.Contains(attackedServer.Kind));
    }

    private static bool IsValidInstalledRunSource(WorkingState working, RunState run)
    {
        if (run.InitiatingSource is not { } sourceId
            || run.ActiveRunEventId is not null)
        {
            return false;
        }

        CardInstanceState? source = working.Cards.FirstOrDefault(card => card.Id == sourceId);
        ZoneState? sourceZone = source is null
            ? null
            : working.Zones.FirstOrDefault(zone => zone.Id == source.ZoneId);
        ServerState? server = working.Servers.FirstOrDefault(candidate => candidate.Id == run.AttackedServer);
        return source is not null
            && source.OwnerSide == PlayerSide.Runner
            && sourceZone is not null
            && sourceZone.CardIds.Contains(sourceId)
            && server is not null
            && working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
            && BehaviorRunRules.DefinitionHasStartRunForServer(definition!, server.Kind);
    }

    private static void ConsumeRunAction(WorkingState working, ActionKind kind, ActionParameters parameters)
    {
        RunState run = working.RunState
            ?? throw new ResolutionFailureException("A run action requires live run state.");
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not RunTimingFrame frame
            || frame.RunId != run.Id
            || frame.Step != run.Step)
        {
            throw new ResolutionFailureException("The submitted run action does not match the active run timing frame.");
        }

        switch (kind, parameters)
        {
            case (ActionKind.PassPriority, NoActionParameters):
                ConsumeRunPass(working, run);
                break;
            case (ActionKind.RezIce, RunIceParameters rez):
                ConsumeRunRez(working, run, rez.IceId, rez.CostQuote);
                break;
            case (ActionKind.BreakSubroutines, RunBreakParameters runBreak):
                ConsumeRunBreakSubroutines(
                    working,
                    run,
                    runBreak.BreakerId,
                    runBreak.Quantity,
                    runBreak.HostedCredits,
                    runBreak.CostQuote);
                break;
            case (ActionKind.PumpBreaker, RunBreakerParameters pump):
                ConsumeRunPumpBreaker(working, run, pump.BreakerId, pump.HostedCredits);
                break;
            case (ActionKind.UseBioroidAbility, RunBioroidAbilityParameters bioroid):
                ConsumeRunBioroidAbility(working, run, bioroid);
                break;
            case (ActionKind.JackOut, NoActionParameters):
                ConsumeRunJackOut(working, run);
                break;
            case (ActionKind.ContinueRun, NoActionParameters):
                ConsumeRunContinue(working, run);
                break;
            case (ActionKind.ChooseBreachCandidate, BreachCandidateParameters breach):
                ConsumeRunChooseBreachCandidate(working, run, breach);
                break;
            case (ActionKind.NoAccessAction, NoActionParameters):
                ConsumeRunAccessNoAction(working, run);
                break;
            case (ActionKind.TrashAccessedCard, RunCardPaymentParameters trash):
                ConsumeRunTrashAccessedCard(working, run, trash.CardId, trash.HostedCredits);
                break;
            default:
                throw new ResolutionFailureException($"Action kind '{kind}' has no registered run resolver.");
        }
    }

    private static void ConsumeRunChooseBreachCandidate(WorkingState working, RunState run, BreachCandidateParameters breach)
    {
        if (run.Step != RunTimingStep.BreachCandidateSelection)
        {
            throw new ResolutionFailureException("Choosing a breach candidate is legal only during candidate selection.");
        }

        ServerState server = GetBreachServer(working, run);
        ZoneState? hiddenZone = GetBreachHiddenZone(working, server);
        BreachHiddenCandidateSource? hiddenSource = RunState.HiddenBreachCandidate(
            server.Kind,
            hiddenZone,
            run.RandomAccessLimit,
            run.AccessedCards);

        CardInstanceId cardId;
        BreachHiddenCandidateSource? chosenSource = null;
        if (breach.CardId.HasValue)
        {
            cardId = breach.CardId.Value;
            if (!run.BreachCandidates.Contains(cardId))
            {
                throw new ResolutionFailureException($"Breach candidate '{cardId}' is not in the run's candidate list.");
            }
        }
        else
        {
            BreachHiddenCandidateSource source = breach.Source
                ?? throw new ResolutionFailureException("A breach candidate must name a card or a hidden source.");
            if (source != hiddenSource)
            {
                throw new ResolutionFailureException($"Hidden breach source '{source}' is not available from server '{server.Kind}'.");
            }

            chosenSource = source;
            IReadOnlyList<CardInstanceId> availableHiddenCards =
                RunState.AvailableHiddenBreachCards(hiddenZone, run.AccessedCards);
            if (availableHiddenCards.Count == 0)
            {
                throw new ResolutionFailureException("No unaccessed hidden breach card remains.");
            }

            if (source == BreachHiddenCandidateSource.CorpHand)
            {
                var random = DeterministicRandom.Restore(working.RandomState);
                int index = random.NextInt(availableHiddenCards.Count);
                working.RandomState = random.State;
                cardId = availableHiddenCards[index];
            }
            else
            {
                cardId = availableHiddenCards[0];
            }
        }

        // CR 26.03 7.3.1a: choosing a candidate does not turn the card
        // faceup. It stays facedown in the authoritative state and remains
        // visible to both viewers only for the remainder of this breach, which
        // the observation layer derives from the run state.
        run = run.WithBreach(
            run.AccessState,
            run.BreachCandidates.Where(candidate => candidate != cardId).ToArray(),
            chosenSource is null ? run.RandomAccessLimit : run.RandomAccessLimit - 1,
            [.. run.ChosenAccessCards, cardId],
            [.. run.AccessedCards, cardId]);
        Emit(working, EngineEventKinds.BreachCandidateChosen, new BreachCandidateChosenEventPayload(
            run.Id,
            server.Id,
            cardId,
            chosenSource));
        Emit(working, EngineEventKinds.CardAccessed, new CardAccessedEventPayload(run.Id, server.Id, cardId));
        run = run.WithTiming(
            RunTimingStep.BreachAccessAbility,
            -1,
            null,
            run.Successful,
            run.Unsuccessful,
            null,
            null,
            null,
            RunAccessState.InProgress);
        SetRunTiming(working, run);
        RecordTurnAccess(working, cardId);
        if (HasActiveCatalogTrigger(working, OccurrenceKind.CardAccessed))
        {
            QueueCardAccessedOccurrence(
                working,
                GetPlayer(working, PlayerSide.Runner).Id,
                cardId,
                server.Id);
        }
        else
        {
            YieldBreachAccessDecision(working, run);
        }
    }

    private static void ConsumeRunAccessNoAction(WorkingState working, RunState run)
    {
        if (run.Step != RunTimingStep.BreachAccessAbility)
        {
            throw new ResolutionFailureException("The no-access action is legal only during the access ability step.");
        }

        // CR 26.03 7.2.3/7.1.6: after the mid-access window resolves without
        // an ability, an agenda that is still being accessed must be stolen.
        // The source-zone guard is essential here: an agenda moved out of the
        // attacked server is no longer an accessed agenda and must not be
        // auto-stolen.
        if (!HasRunRestriction(run, RunRestrictionKind.CannotStealOrTrashCorpCards)
            && run.ChosenAccessCards.Count > 0
            && TryGetCurrentAccessCard(working, run, out CardInstanceState? card, requireTrashableSource: false)
            && working.BehaviorCatalog.TryGetAgenda(card!.DefinitionId, out _))
        {
            StealAccessedAgendaInternal(working, run, card.Id);
            return;
        }

        AdvanceBreachAfterAccess(working, run);
    }

    private static void ConsumeRunTrashAccessedCard(
        WorkingState working,
        RunState run,
        CardInstanceId cardId,
        int hostedCredits)
    {
        if (run.Step != RunTimingStep.BreachAccessAbility)
        {
            throw new ResolutionFailureException("Trashing the accessed card is legal only during the access ability step.");
        }

        if (run.ChosenAccessCards.Count == 0)
        {
            throw new ResolutionFailureException("The access ability step requires a chosen access card.");
        }

        if (HasRunRestriction(run, RunRestrictionKind.CannotStealOrTrashCorpCards))
        {
            throw new ResolutionFailureException(
                "The active run restriction forbids trashing Corp cards during access.");
        }

        CardInstanceId accessed = run.ChosenAccessCards[^1];
        if (cardId != accessed)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not the accessed card '{accessed}'.");
        }

        if (!TryGetCurrentAccessCard(working, run, out CardInstanceState? card, requireTrashableSource: true)
            || card!.Id != accessed)
        {
            throw new ResolutionFailureException(
                $"The accessed card '{accessed}' is no longer in the authoritative source zone.");
        }

        if (!working.BehaviorCatalog.TryGetAsset(card.DefinitionId, out AssetBehaviorSpec? spec))
        {
            throw new ResolutionFailureException($"The accessed card '{accessed}' has no registered asset behavior spec.");
        }

        ZoneState discardZone = GetZone(working, ZoneKind.CorpDiscard);
        if (card.ZoneId == discardZone.Id)
        {
            throw new ResolutionFailureException($"The accessed card '{accessed}' is already in the Corp discard.");
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        if (!CanAffordRunCredits(runner, run, spec.TrashCost))
        {
            throw new ResolutionFailureException(
                $"The Runner cannot pay the {spec.TrashCost} credit trash cost of '{accessed}' "
                + $"from {runner.Credits} wallet and {run.RunCredits} run credits.");
        }

        run = PayRunnerRunCredits(working, run, spec.TrashCost, hostedCredits);
        TrashCard(working, card, working.Zones.Single(zone => zone.Id == card.ZoneId), discardZone);
        // A card trashed through access is revealed: it enters the Corp
        // discard faceup and unrezzed so both viewers can see what left the
        // breach.
        if (!card.FaceUp || card.Rezzed)
        {
            ReplaceCard(working, new CardInstanceState(
                card.Id,
                card.DefinitionId,
                discardZone.Id,
                card.OwnerSide,
                true,
                false,
                card.AdvancementCounters));
        }
        Emit(working, EngineEventKinds.AccessedCardTrashed, new AccessedCardTrashedEventPayload(run.Id, accessed, spec.TrashCost));
        AdvanceBreachAfterAccess(working, run);
    }

    /// <summary>
    /// Engine-internal automatic steal of a still-accessed agenda (CR 26.03
    /// 7.2.3/7.1.6). It is not a player action: the mid-access window offers
    /// only pass for an accessed agenda, and progression past that window
    /// resolves the steal. The emitted <c>AgendaStolen</c> event is identical
    /// to the pre-U6 player-steal path.
    /// </summary>
    private static void StealAccessedAgendaInternal(WorkingState working, RunState run, CardInstanceId cardId)
    {
        if (run.Step != RunTimingStep.BreachAccessAbility)
        {
            throw new ResolutionFailureException("The automatic agenda steal resolves only during the access ability step.");
        }

        if (run.ChosenAccessCards.Count == 0)
        {
            throw new ResolutionFailureException("The access ability step requires a chosen access card.");
        }

        if (HasRunRestriction(run, RunRestrictionKind.CannotStealOrTrashCorpCards))
        {
            throw new ResolutionFailureException(
                "The active run restriction forbids stealing or trashing Corp cards during access.");
        }

        CardInstanceId accessed = run.ChosenAccessCards[^1];
        if (cardId != accessed)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not the accessed card '{accessed}'.");
        }

        if (!TryGetCurrentAccessCard(working, run, out CardInstanceState? card, requireTrashableSource: false)
            || card!.Id != accessed)
        {
            throw new ResolutionFailureException(
                $"The accessed agenda '{accessed}' is no longer in the authoritative source zone.");
        }

        if (!working.BehaviorCatalog.TryGetAgenda(card.DefinitionId, out AgendaBehaviorSpec? spec))
        {
            throw new ResolutionFailureException($"The accessed card '{accessed}' is not a registered agenda.");
        }

        ZoneState score = GetZone(working, ZoneKind.RunnerScore);
        MoveCard(working, card, working.Zones.Single(zone => zone.Id == card.ZoneId), score);
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            score.Id,
            card.OwnerSide,
            true,
            GetCard(working, card.Id).Rezzed,
            GetCard(working, card.Id).AdvancementCounters));
        AddAgendaPoints(working, PlayerSide.Runner, spec.AgendaPoints);
        Emit(working, EngineEventKinds.AgendaStolen, new AgendaStolenEventPayload(PlayerSide.Runner, cardId, spec.AgendaPoints));
        RecordTurnAgendaChange(
            working,
            card.Id,
            PlayerSide.Runner,
            spec.AgendaPoints,
            AgendaTurnChangeKind.Steal);

        bool realTurnRun = IsRealTurnRun(working, run);
        int agendaPointsToWin = realTurnRun
            ? RealGameRules.AgendaPointsToWin
            : ToyRules.AgendaPointsToWin;
        if (GetPlayer(working, PlayerSide.Runner).AgendaPoints >= agendaPointsToWin)
        {
            // CR 7.2.3: the run completes normally first (RunEnded, frame
            // pop) and only then the game ends. Real CR turns use seven
            // agenda points; standalone M2 fixtures retain their three-point
            // compatibility threshold.
            RunState terminal = run.WithTiming(
                RunTimingStep.RunEnd,
                -1,
                null,
                run.Successful,
                run.Unsuccessful,
                RunEndReason.BreachCompleted,
                null,
                null,
                RunAccessState.Completed);
            CompleteRun(working, terminal);
            if (!realTurnRun)
            {
                CompleteToyGame(working, PlayerSide.Runner, GameWinReason.AgendaPoints);
            }
            else
            {
                CompleteTurnGame(working, PlayerSide.Runner, GameWinReason.AgendaPoints);
            }
            return;
        }

        bool hasAgendaTrigger = HasActiveCatalogTrigger(working, OccurrenceKind.AgendaScoredOrStolen);
        AdvanceBreachAfterAccess(working, run, yieldDecision: !hasAgendaTrigger);
        if (hasAgendaTrigger)
        {
            QueueAgendaScoredOrStolenOccurrence(
                working,
                GetPlayer(working, PlayerSide.Runner).Id,
                card.Id,
                run.AttackedServer);
        }
    }

    private static void AdvanceBreachAfterAccess(
        WorkingState working,
        RunState run,
        bool yieldDecision = true)
    {
        ServerState server = GetBreachServer(working, run);
        ZoneState? hiddenZone = GetBreachHiddenZone(working, server);
        if (run.BreachCandidates.Count > 0
            || RunState.HiddenBreachCandidate(
                server.Kind,
                hiddenZone,
                run.RandomAccessLimit,
                run.AccessedCards) is not null)
        {
            run = run.WithTiming(
                RunTimingStep.BreachCandidateSelection,
                -1,
                null,
                run.Successful,
                run.Unsuccessful,
                null,
                null,
                null,
                RunAccessState.InProgress);
            SetRunTiming(working, run);
            if (yieldDecision)
            {
                YieldBreachCandidateDecision(working, run);
            }

            return;
        }

        SetRunTiming(working, run.WithTiming(
            RunTimingStep.RunEnd,
            -1,
            null,
            run.Successful,
            run.Unsuccessful,
            RunEndReason.BreachCompleted,
            null,
            null,
            RunAccessState.Completed));
    }

    private static bool HasRunRestriction(RunState run, RunRestrictionKind kind) =>
        run.RunRestrictions is { Count: > 0 }
        && run.RunRestrictions.Any(restriction => restriction.Kind == kind);

    private static void ConsumeRunPass(WorkingState working, RunState run)
    {
        RunPriorityState priority = run.Priority
            ?? throw new ResolutionFailureException("Pass priority requires an active paid window.");
        if (!IsPaidWindow(run.Step) || priority.PriorityHolder is not (PlayerSide.Runner or PlayerSide.Corp))
        {
            throw new ResolutionFailureException("Pass priority is legal only in a run paid window.");
        }

        int nextPasses = checked(priority.ConsecutivePasses + 1);
        PlayerSide nextHolder = OtherSide(priority.PriorityHolder);
        Emit(working, EngineEventKinds.RunPriorityPassed, new RunPriorityPassedEventPayload(
            run.Id,
            priority.Window,
            priority.PriorityHolder,
            nextHolder,
            nextPasses));

        if (nextPasses < 2)
        {
            SetRunTiming(working, run.WithTiming(
                run.Step,
                run.CurrentIceIndex,
                run.CurrentIceId,
                run.Successful,
                run.Unsuccessful,
                run.EndReason,
                new RunPriorityState(
                    priority.Version,
                    priority.Window,
                    nextHolder,
                    nextPasses,
                    priority.CorpMayRezApproachedIce,
                    priority.CorpMayRezNonIce),
                run.Encounter,
                run.AccessState));
            return;
        }

        ClosePriorityWindow(working, run);
    }

    private static void ClosePriorityWindow(WorkingState working, RunState run)
    {
        RunPriorityState priority = run.Priority
            ?? throw new ResolutionFailureException("A run paid window is missing priority state.");
        switch (priority.Window)
        {
            case RunPriorityWindowKind.InitiationPaid:
                SetRunTiming(working, run.WithTiming(
                    RunTimingStep.Approach,
                    run.CurrentIceIndex,
                    run.CurrentIceId,
                    run.Successful,
                    run.Unsuccessful,
                    run.EndReason,
                    null,
                    null,
                    run.AccessState));
                break;
            case RunPriorityWindowKind.ApproachPaid:
                if (run.CurrentIceId.HasValue && !GetCard(working, run.CurrentIceId.Value).Rezzed)
                {
                    SetRunTiming(working, run.WithTiming(
                        RunTimingStep.MovementPaidWindow,
                        run.CurrentIceIndex,
                        run.CurrentIceId,
                        run.Successful,
                        run.Unsuccessful,
                        run.EndReason,
                        CreatePriority(RunPriorityWindowKind.MovementPaid),
                        null,
                        run.AccessState));
                }
                else
                {
                    SetRunTiming(working, run.WithTiming(
                        RunTimingStep.EncounterInitialization,
                        run.CurrentIceIndex,
                        run.CurrentIceId,
                        run.Successful,
                        run.Unsuccessful,
                        run.EndReason,
                        null,
                        null,
                        run.AccessState));
                }

                break;
            case RunPriorityWindowKind.EncounterPaid:
                SetRunTiming(working, run.WithTiming(
                    RunTimingStep.Encounter,
                    run.CurrentIceIndex,
                    run.CurrentIceId,
                    run.Successful,
                    run.Unsuccessful,
                    run.EndReason,
                    null,
                    run.Encounter,
                    run.AccessState));
                break;
            case RunPriorityWindowKind.MovementPaid:
                SetRunTiming(working, run.WithTiming(
                    RunTimingStep.JackOutDecision,
                    run.CurrentIceIndex,
                    run.CurrentIceId,
                    run.Successful,
                    run.Unsuccessful,
                    run.EndReason,
                    null,
                    run.Encounter,
                    run.AccessState));
                break;
            case RunPriorityWindowKind.AfterMovePaid:
                SetRunTiming(working, run.WithTiming(
                    run.CurrentIceIndex >= 0 ? RunTimingStep.Approach : RunTimingStep.ApproachServer,
                    run.CurrentIceIndex,
                    run.CurrentIceId,
                    run.Successful,
                    run.Unsuccessful,
                    run.EndReason,
                    null,
                    null,
                    run.AccessState));
                break;
            default:
                throw new ResolutionFailureException($"Unsupported run priority window '{priority.Window}'.");
        }
    }

    private static void ConsumeRunRez(
        WorkingState working,
        RunState run,
        CardInstanceId iceId,
        CostQuote? suppliedCostQuote)
    {
        if (working.RuntimeCardCatalog is not null)
        {
            throw new ResolutionFailureException(
                "Runtime-catalog-backed runs must use the typed RezCard action.");
        }

        if (run.Step != RunTimingStep.ApproachPaidWindow
            || run.Priority is not { PriorityHolder: PlayerSide.Corp, CorpMayRezApproachedIce: true }
            || run.CurrentIceId != iceId)
        {
            throw new ResolutionFailureException("Rez is legal only for the unrezzed approached ICE while Corp has priority.");
        }

        CardInstanceState ice = GetCard(working, iceId);
        if (ice.Rezzed)
        {
            throw new ResolutionFailureException("The approached ICE is already rezzed.");
        }

        if (!working.BehaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec spec))
        {
            throw new ResolutionFailureException(
                $"Approached ICE '{ice.DefinitionId}' is not registered in the ICE behavior spec catalog; "
                + "it cannot be rezzed.");
        }

        CostQuote? expectedCostQuote = CostModifierProjection.IceRez(working, ice, spec.RezCost);
        if (!CostQuotesEqual(suppliedCostQuote, expectedCostQuote))
        {
            throw new ResolutionFailureException(
                "The approached ICE rez action does not match the authoritative cost quote.");
        }

        int effectiveCost = expectedCostQuote?.EffectiveCost ?? spec.RezCost;
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        if (corp.Credits < effectiveCost)
        {
            throw new ResolutionFailureException(
                $"The Corp cannot pay the {effectiveCost} credit rez cost of approached ICE '{ice.DefinitionId}'.");
        }

        ApplyCreditDelta(working, corp.Id, -effectiveCost);
        // CR 26.03 8.1.1: a rezzed installed ICE is faceup, so rezzing the
        // approached ICE turns it faceup for both players.
        ReplaceCard(working, new CardInstanceState(
            ice.Id,
            ice.DefinitionId,
            ice.ZoneId,
            ice.OwnerSide,
            true,
            true,
            ice.AdvancementCounters));
        Emit(working, EngineEventKinds.IceRezzed, new IceRezzedEventPayload(run.Id, iceId));
        SetRunTiming(working, run.WithTiming(
            RunTimingStep.ApproachPaidWindow,
            run.CurrentIceIndex,
            run.CurrentIceId,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            new RunPriorityState(
                run.Priority!.Version,
                RunPriorityWindowKind.ApproachPaid,
                PlayerSide.Corp,
                0,
                false,
                true),
            null,
            run.AccessState));
    }

    private static void ConsumeRunBreakSubroutines(
        WorkingState working,
        RunState run,
        CardInstanceId breakerId,
        int quantity,
        int hostedCredits,
        CostQuote? suppliedCostQuote)
    {
        RunPriorityState? priority = run.Priority;
        RunEncounterState? encounter = run.Encounter;
        if (priority is null
            || run.Step != RunTimingStep.EncounterPaidWindow
            || priority.PriorityHolder != PlayerSide.Runner
            || encounter is not { Status: RunEncounterStatus.Initialized })
        {
            throw new ResolutionFailureException(
                "Breaking subroutines is legal only while the Runner has priority in an initialized encounter paid window.");
        }

        if (quantity < 1)
        {
            throw new ResolutionFailureException($"The break quantity {quantity} must be at least 1.");
        }

        CardInstanceState breaker = GetCard(working, breakerId);
        if (breaker.OwnerSide != PlayerSide.Runner)
        {
            throw new ResolutionFailureException("Only a Runner-owned breaker can break subroutines.");
        }

        ZoneState? breakerZone = working.Zones.FirstOrDefault(zone => zone.Id == breaker.ZoneId);
        if (breakerZone is null || breakerZone.Kind != ZoneKind.RunnerRig || breakerZone.OwnerSide != PlayerSide.Runner)
        {
            throw new ResolutionFailureException("The breaking breaker must be installed in the Runner rig.");
        }

        if (!working.BehaviorCatalog.TryGetIcebreaker(breaker.DefinitionId, out IcebreakerBehaviorSpec breakerSpec))
        {
            throw new ResolutionFailureException(
                $"Breaker '{breaker.DefinitionId}' is not registered in the icebreaker behavior spec catalog; "
                + "it cannot break subroutines.");
        }

        ServerState server = working.Servers.SingleOrDefault(candidate => candidate.Id == run.AttackedServer)
            ?? throw new ResolutionFailureException($"Run server '{run.AttackedServer}' does not exist.");

        CardInstanceId iceId = encounter.IceId;
        CardInstanceState ice = GetCard(working, iceId);
        if (!working.BehaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec iceSpec))
        {
            throw new ResolutionFailureException(
                $"Encounter ICE '{ice.DefinitionId}' is not registered in the ICE behavior spec catalog; "
                + "its subroutines cannot be broken.");
        }

        // CR 26.03 3.9.5h: an interface ability that names a subtype of ice
        // can only be used on encountered ice with that subtype. An explicit
        // wildcard interface may be used with any concrete encountered ICE.
        if (!IceInterfaceMatcher.Matches(iceSpec, breakerSpec))
        {
            throw new ResolutionFailureException(
                $"Breaker '{breakerSpec.DefinitionId}' interfaces with {breakerSpec.InterfaceType} ICE, "
                + $"but encountered ICE '{iceSpec.DefinitionId}' is {iceSpec.IceType}.");
        }

        int unbrokenCount = encounter.Subroutines.Count(subroutine => !subroutine.Broken);
        if (quantity > unbrokenCount)
        {
            throw new ResolutionFailureException(
                $"The encounter has only {unbrokenCount} unbroken subroutine(s); {quantity} cannot be broken.");
        }

        // CR 26.03 3.9.5g: the interface ability is usable only at breaker
        // strength greater than or equal to the encountered ice's strength.
        ValueQuote breakerQuote = EncounterStrength.ProjectBreakerQuote(
                working.BehaviorCatalog,
                working.Cards,
                working.Zones,
                working.RulesState,
                run,
                breaker)
            ?? throw new ResolutionFailureException(
                $"Breaker '{breakerSpec.DefinitionId}' has no executable strength quote.");
        ValueQuote iceQuote = EncounterStrength.ProjectIceQuote(
                working.BehaviorCatalog,
                working.Cards,
                working.Zones,
                working.RulesState,
                run,
                ice,
                server.Kind)
            ?? throw new ResolutionFailureException(
                $"Encounter ICE '{iceSpec.DefinitionId}' has no executable strength quote.");
        int breakerStrength = breakerQuote.EffectiveValue;
        int iceStrength = iceQuote.EffectiveValue;
        if (breakerStrength < iceStrength)
        {
            throw new ResolutionFailureException(
                $"Breaker '{breakerSpec.DefinitionId}' at strength {breakerStrength} cannot break "
                + $"encounter ICE '{iceSpec.DefinitionId}' at strength {iceStrength}.");
        }

        // CR 26.03 3.9.5: the breaker's printed limit applies to this
        // interface ability activation. A later activation in the same
        // encounter may use the limit again after paying its own cost.
        if (quantity > breakerSpec.BreakLimit)
        {
            throw new ResolutionFailureException(
                $"Breaker '{breakerSpec.DefinitionId}' can break at most {breakerSpec.BreakLimit} subroutine(s) "
                + "per interface ability activation.");
        }

        CostQuote? expectedCostQuote = CostModifierProjection.IcebreakerBreak(
            working,
            breaker,
            breakerSpec.BreakCost);
        int effectiveBreakCost = expectedCostQuote?.EffectiveCost ?? breakerSpec.BreakCost;
        if (!CostQuotesEqual(suppliedCostQuote, expectedCostQuote))
        {
            throw new ResolutionFailureException("The frozen break cost quote is stale or forged.");
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        if (!CanAffordRunCredits(runner, run, effectiveBreakCost))
        {
            throw new ResolutionFailureException(
                $"The Runner cannot pay the {effectiveBreakCost} credit break cost of breaker "
                + $"'{breakerSpec.DefinitionId}' from {runner.Credits} wallet and {run.RunCredits} run credits.");
        }

        run = PayRunnerRunCredits(working, run, effectiveBreakCost, hostedCredits);

        var nextSubroutines = new List<RunSubroutineState>(encounter.Subroutines.Count);
        int brokenSoFar = 0;
        foreach (RunSubroutineState subroutine in encounter.Subroutines)
        {
            if (!subroutine.Broken && brokenSoFar < quantity)
            {
                brokenSoFar++;
                nextSubroutines.Add(new RunSubroutineState(
                    subroutine.Version,
                    subroutine.Id,
                    subroutine.Ordinal,
                    true,
                    subroutine.Resolved));
            }
            else
            {
                nextSubroutines.Add(subroutine);
            }
        }

        RunState brokenRun = run.WithEncounter(new RunEncounterState(
            encounter.Version,
            encounter.Id,
            iceId,
            encounter.Status,
            nextSubroutines));
        if (breakerSpec.RunEndCleanup is { } runEndCleanup
            && !brokenRun.LingeringEffects.Any(effect =>
                effect.Expiry == RunLingeringEffectExpiry.Run
                && effect.Kind == RunLingeringEffectKind.IcebreakerRunEndCleanup
                && effect.SourceCardId == breakerId
                && effect.BehaviorId == runEndCleanup.BehaviorId))
        {
            RunEffectId effectId = new($"effect:run-end:{run.Id.Value}:{breakerId.Value}:{runEndCleanup.BehaviorId}");
            brokenRun = brokenRun.WithLingeringEffects(
                brokenRun.LingeringEffects.Append(
                    new RunLingeringEffect(
                        effectId,
                        runEndCleanup.BehaviorId,
                        breakerId,
                        RunLingeringEffectExpiry.Run,
                        valueAdjustment: null,
                        kind: RunLingeringEffectKind.IcebreakerRunEndCleanup)).ToArray());
            Emit(working, EngineEventKinds.LingeringEffectAdded, new LingeringEffectAddedEventPayload(
                effectId,
                runEndCleanup.BehaviorId,
                LingeringEffectExpiry.Run));
        }

        Emit(working, EngineEventKinds.SubroutinesBroken, new SubroutinesBrokenEventPayload(
            run.Id,
            encounter.Id,
            breakerId,
            quantity));
        SetRunTiming(working, brokenRun.WithTiming(
            RunTimingStep.EncounterPaidWindow,
            brokenRun.CurrentIceIndex,
            brokenRun.CurrentIceId,
            brokenRun.Successful,
            brokenRun.Unsuccessful,
            brokenRun.EndReason,
            new RunPriorityState(
                priority.Version,
                RunPriorityWindowKind.EncounterPaid,
                PlayerSide.Runner,
                0,
                priority.CorpMayRezApproachedIce,
                priority.CorpMayRezNonIce),
            brokenRun.Encounter,
            brokenRun.AccessState));
    }

    private static void ConsumeRunBioroidAbility(
        WorkingState working,
        RunState run,
        RunBioroidAbilityParameters parameters)
    {
        RunPriorityState priority = run.Priority
            ?? throw new ResolutionFailureException("A bioroid ability requires encounter priority state.");
        RunEncounterState encounter = run.Encounter
            ?? throw new ResolutionFailureException("A bioroid ability requires an initialized encounter.");
        if (run.Step != RunTimingStep.EncounterPaidWindow
            || priority.PriorityHolder != PlayerSide.Runner
            || encounter.Status != RunEncounterStatus.Initialized)
        {
            throw new ResolutionFailureException(
                "A bioroid ability is legal only while the Runner has priority in an initialized encounter paid window.");
        }

        if (encounter.IceId != parameters.IceId)
        {
            throw new ResolutionFailureException(
                $"Bioroid ability target ICE '{parameters.IceId}' is not the current encountered ICE '{encounter.IceId}'.");
        }

        CardInstanceState ice = GetCard(working, encounter.IceId);
        if (!working.BehaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec? iceSpec))
        {
            throw new ResolutionFailureException(
                $"Encounter ICE '{ice.DefinitionId}' is not registered in the ICE behavior spec catalog.");
        }

        EncounteredBioroidAbilitySpec? declaration = iceSpec.EncounteredBioroidAbilities?
            .SingleOrDefault(ability => ability.Key == parameters.AbilityKey);
        if (declaration is null)
        {
            throw new ResolutionFailureException(
                $"ICE '{ice.DefinitionId}' does not declare encountered-bioroid ability '{parameters.AbilityKey}'.");
        }

        if (parameters.Cost != declaration.Cost)
        {
            throw new ResolutionFailureException("The frozen bioroid ability cost is stale or forged.");
        }

        RunSubroutineState subroutine = encounter.Subroutines
            .SingleOrDefault(candidate => candidate.Id == parameters.SubroutineId)
            ?? throw new ResolutionFailureException(
                $"Subroutine '{parameters.SubroutineId}' is not part of the current encounter.");

        if (parameters.SubroutineOrdinal != subroutine.Ordinal)
        {
            throw new ResolutionFailureException(
                "The bioroid ability subroutine ordinal is stale or forged.");
        }
        if (subroutine.Broken || subroutine.Resolved)
        {
            throw new ResolutionFailureException(
                $"Subroutine '{parameters.SubroutineId}' is no longer an unbroken encounter subroutine.");
        }

        if (subroutine.Ordinal < 0 || subroutine.Ordinal >= iceSpec.Subroutines.Count)
        {
            throw new ResolutionFailureException(
                $"Subroutine '{parameters.SubroutineId}' has an ordinal outside the encountered ICE declaration.");
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        if (runner.Clicks < declaration.Cost.Amount)
        {
            throw new ResolutionFailureException(
                $"The Runner cannot pay the {declaration.Cost.Amount} click cost of bioroid ability '{declaration.Key}'.");
        }

        // LoseClick is intentionally paid through the typed behavior-cost
        // path, not SpendClick: this preserves the semantic distinction from
        // a structured option that spends a click as an action cost.
        ApplyClickDelta(working, runner.Id, -declaration.Cost.Amount);

        var nextSubroutines = new List<RunSubroutineState>(encounter.Subroutines.Count);
        foreach (RunSubroutineState candidate in encounter.Subroutines)
        {
            nextSubroutines.Add(candidate.Id == subroutine.Id
                ? new RunSubroutineState(
                    candidate.Version,
                    candidate.Id,
                    candidate.Ordinal,
                    true,
                    candidate.Resolved)
                : candidate);
        }

        RunState brokenRun = run.WithEncounter(new RunEncounterState(
            encounter.Version,
            encounter.Id,
            encounter.IceId,
            encounter.Status,
            nextSubroutines));
        Emit(working, EngineEventKinds.BioroidAbilityUsed, new BioroidAbilityUsedEventPayload(
            run.Id,
            encounter.Id,
            encounter.IceId,
            subroutine.Id,
            declaration.Key,
            declaration.Cost.Amount));
        SetRunTiming(working, brokenRun.WithTiming(
            RunTimingStep.EncounterPaidWindow,
            brokenRun.CurrentIceIndex,
            brokenRun.CurrentIceId,
            brokenRun.Successful,
            brokenRun.Unsuccessful,
            brokenRun.EndReason,
            new RunPriorityState(
                priority.Version,
                RunPriorityWindowKind.EncounterPaid,
                PlayerSide.Runner,
                0,
                priority.CorpMayRezApproachedIce,
                priority.CorpMayRezNonIce),
            brokenRun.Encounter,
            brokenRun.AccessState));
    }

    private static void ConsumeRunPumpBreaker(
        WorkingState working,
        RunState run,
        CardInstanceId breakerId,
        int hostedCredits)
    {
        RunPriorityState? priority = run.Priority;
        if (priority is null
            || run.Step != RunTimingStep.EncounterPaidWindow
            || priority.PriorityHolder != PlayerSide.Runner
            || run.Encounter is not { Status: RunEncounterStatus.Initialized })
        {
            throw new ResolutionFailureException(
                "Pumping a breaker is legal only while the Runner has priority in an initialized encounter paid window.");
        }

        CardInstanceState breaker = GetCard(working, breakerId);
        if (breaker.OwnerSide != PlayerSide.Runner)
        {
            throw new ResolutionFailureException("Only a Runner-owned breaker can be pumped.");
        }

        ZoneState? breakerZone = working.Zones.FirstOrDefault(zone => zone.Id == breaker.ZoneId);
        if (breakerZone is null || breakerZone.Kind != ZoneKind.RunnerRig || breakerZone.OwnerSide != PlayerSide.Runner)
        {
            throw new ResolutionFailureException("The pumped breaker must be installed in the Runner rig.");
        }

        if (!working.BehaviorCatalog.TryGetIcebreaker(breaker.DefinitionId, out IcebreakerBehaviorSpec breakerSpec))
        {
            throw new ResolutionFailureException(
                $"Breaker '{breaker.DefinitionId}' is not registered in the icebreaker behavior spec catalog; "
                + "it cannot be pumped.");
        }

        RunValueAdjustment? dynamicAdjustment = null;
        if (breakerSpec.DynamicPump is { } dynamicPump)
        {
            if (!EncounterStrength.TryResolveDynamicPump(
                    breakerSpec,
                    breakerId,
                    working.Cards,
                    working.Zones,
                    working.BehaviorCatalog,
                    out int dynamicQuantity,
                    out int dynamicDelta))
            {
                throw new ResolutionFailureException(
                    $"Breaker '{breakerSpec.DefinitionId}' has no active installed cards for its dynamic pump.");
            }

            dynamicAdjustment = new RunValueAdjustment(
                BehaviorValueKind.Strength,
                BehaviorValueSourceKind.Variable,
                BehaviorValueOperation.Add,
                breakerId,
                breakerId,
                dynamicDelta,
                dynamicPump.ReasonKey,
                dynamicPump.VariableKey,
                quantity: dynamicQuantity,
                unitDelta: dynamicPump.UnitDelta);
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        if (!CanAffordRunCredits(runner, run, breakerSpec.PumpCost))
        {
            throw new ResolutionFailureException(
                $"The Runner cannot pay the {breakerSpec.PumpCost} credit pump cost of breaker "
                + $"'{breakerSpec.DefinitionId}' from {runner.Credits} wallet and {run.RunCredits} run credits.");
        }

        run = PayRunnerRunCredits(working, run, breakerSpec.PumpCost, hostedCredits);
        RunEffectId effectId = new($"effect:pump:{run.Id.Value}:{breakerId.Value}:{run.LingeringEffects.Count}");
        RunState pumpedRun = run.WithLingeringEffects(
            run.LingeringEffects.Append(
                new RunLingeringEffect(
                    effectId,
                    EncounterStrength.IcebreakerPumpBehavior,
                    breakerId,
                    RunLingeringEffectExpiry.Encounter,
                    dynamicAdjustment)).ToArray());
        Emit(working, EngineEventKinds.BreakerPumped, new BreakerPumpedEventPayload(run.Id, breakerId));
        SetRunTiming(working, pumpedRun.WithTiming(
            RunTimingStep.EncounterPaidWindow,
            pumpedRun.CurrentIceIndex,
            pumpedRun.CurrentIceId,
            pumpedRun.Successful,
            pumpedRun.Unsuccessful,
            pumpedRun.EndReason,
            new RunPriorityState(
                priority.Version,
                RunPriorityWindowKind.EncounterPaid,
                PlayerSide.Runner,
                0,
                priority.CorpMayRezApproachedIce,
                priority.CorpMayRezNonIce),
            pumpedRun.Encounter,
            pumpedRun.AccessState));
    }

    private static void ConsumeRunJackOut(WorkingState working, RunState run)
    {
        if (run.Step != RunTimingStep.JackOutDecision || run.CurrentIceIndex < 0)
        {
            throw new ResolutionFailureException("Jack out is legal only at the Runner's movement decision.");
        }

        SetRunTiming(working, run.WithTiming(
            RunTimingStep.RunEnd,
            run.CurrentIceIndex,
            run.CurrentIceId,
            false,
            true,
            RunEndReason.JackedOut,
            null,
            run.Encounter,
            run.AccessState));
    }

    private static void ConsumeRunContinue(WorkingState working, RunState run)
    {
        if (run.Step != RunTimingStep.JackOutDecision || run.CurrentIceIndex < 0)
        {
            throw new ResolutionFailureException("Continue run is legal only at the Runner's movement decision.");
        }

        SetRunTiming(working, run.WithTiming(
            RunTimingStep.MoveInward,
            run.CurrentIceIndex,
            run.CurrentIceId,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            null,
            run.Encounter,
            run.AccessState));
    }

    private static void YieldRunPriorityDecision(
        WorkingState working,
        RunState run,
        string prompt,
        bool emitWindowEvent = true)
    {
        RunPriorityState priority = run.Priority
            ?? throw new ResolutionFailureException("A run paid-window decision requires priority state.");
        if (!IsPaidWindow(run.Step))
        {
            throw new ResolutionFailureException("A non-paid run step cannot yield a priority decision.");
        }

        if (emitWindowEvent)
        {
            Emit(working, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(
                run.Id,
                run.Phase,
                run.Step,
                priority.Window,
                priority.PriorityHolder));
        }

        var actions = new List<LegalAction>
        {
            new(working.Allocator.NextActionId(), ActionKind.PassPriority, "Pass priority", new NoActionParameters()),
        };
        if (working.RuntimeCardCatalog is not null)
        {
            AddRezCardActions(working, actions);
        }
        else if (run.Step == RunTimingStep.ApproachPaidWindow
            && priority.PriorityHolder == PlayerSide.Corp
            && priority.CorpMayRezApproachedIce
            && run.CurrentIceId.HasValue)
        {
            CardInstanceState approachedIce = GetCard(working, run.CurrentIceId.Value);
            if (!working.BehaviorCatalog.TryGetIce(approachedIce.DefinitionId, out IceBehaviorSpec? approachedSpec))
            {
                throw new ResolutionFailureException(
                    $"Approached ICE '{approachedIce.DefinitionId}' is not registered in the ICE behavior spec catalog; "
                    + "the approach window cannot be yielded.");
            }

            CostQuote? approachedCostQuote = CostModifierProjection.IceRez(
                working,
                approachedIce,
                approachedSpec.RezCost);
            int approachedCost = approachedCostQuote?.EffectiveCost ?? approachedSpec.RezCost;
            if (GetPlayer(working, PlayerSide.Corp).Credits >= approachedCost)
            {
                actions.Add(new LegalAction(
                    working.Allocator.NextActionId(),
                    ActionKind.RezIce,
                    $"Rez approached ICE {run.CurrentIceId.Value}",
                    new RunIceParameters(run.CurrentIceId.Value, approachedCostQuote)));
            }
        }

        // CR 26.03 6.5/9.8/3.9.5: while the encounter paid window is open, every
        // registered rig icebreaker materializes its interface abilities
        // against the encountered ICE — break quantities (only while the
        // interface matches, the current strength covers the ICE, and the
        // quantity stays within this activation's BreakLimit) and a pump
        // (never strength- or subtype-gated, because it exists to raise
        // strength into range). There is no persistent server attachment:
        // breakers stay in the rig and any eligible one may act, so the window
        // is switchable. CR 1.10.3c materializes every legal wallet/hosted
        // credit division as a distinct serialized action.
        if (run.Step == RunTimingStep.EncounterPaidWindow
            && priority.PriorityHolder == PlayerSide.Runner
            && run.Encounter is { Status: RunEncounterStatus.Initialized })
        {
            ServerState attackedServer = working.Servers.SingleOrDefault(candidate => candidate.Id == run.AttackedServer)
                ?? throw new ResolutionFailureException($"Run server '{run.AttackedServer}' does not exist.");
            CardInstanceState encounteredIce = GetCard(working, run.Encounter.IceId);
            if (!working.BehaviorCatalog.TryGetIce(encounteredIce.DefinitionId, out IceBehaviorSpec? iceSpec))
            {
                throw new ResolutionFailureException(
                    $"Encounter ICE '{encounteredIce.DefinitionId}' is not registered in the ICE behavior spec catalog; "
                    + "the encounter window cannot be yielded.");
            }

            ValueQuote iceQuote = EncounterStrength.ProjectIceQuote(
                    working.BehaviorCatalog,
                    working.Cards,
                    working.Zones,
                    working.RulesState,
                    run,
                    encounteredIce,
                    attackedServer.Kind)
                ?? throw new ResolutionFailureException(
                    $"Encounter ICE '{iceSpec.DefinitionId}' has no executable strength quote.");
            int iceStrength = iceQuote.EffectiveValue;
            int unbrokenCount = run.Encounter.Subroutines.Count(subroutine => !subroutine.Broken);
            PlayerState runner = GetPlayer(working, PlayerSide.Runner);
            long availableCredits = (long)runner.Credits + run.RunCredits;

            // Encountered bioroid interface abilities are separate from the
            // installed icebreaker loop below. They do not inspect breaker
            // subtype or strength, and each action names one exact currently
            // unbroken subroutine so repeated uses remain independently
            // materialized until clicks or targets are exhausted.
            if (iceSpec.EncounteredBioroidAbilities is { Count: > 0 } bioroidAbilities)
            {
                foreach (EncounteredBioroidAbilitySpec ability in bioroidAbilities)
                {
                    if (runner.Clicks < ability.Cost.Amount)
                    {
                        continue;
                    }

                    foreach (RunSubroutineState subroutine in run.Encounter.Subroutines)
                    {
                        if (subroutine.Broken || subroutine.Resolved)
                        {
                            continue;
                        }

                        actions.Add(new LegalAction(
                            working.Allocator.NextActionId(),
                            ActionKind.UseBioroidAbility,
                            $"Use {ability.Key} to break subroutine {subroutine.Ordinal + 1} on {encounteredIce.Id.Value} (lose {ability.Cost.Amount} click)",
                            new RunBioroidAbilityParameters(
                                encounteredIce.Id,
                                subroutine.Id,
                                ability.Key,
                                ability.Cost,
                                subroutine.Ordinal)));
                    }
                }
            }

            foreach (ZoneState rigZone in working.Zones)
            {
                if (rigZone.Kind != ZoneKind.RunnerRig || rigZone.OwnerSide != PlayerSide.Runner)
                {
                    continue;
                }

                foreach (CardInstanceId cardId in rigZone.CardIds)
                {
                    CardInstanceState breaker = GetCard(working, cardId);
                    if (!working.BehaviorCatalog.TryGetIcebreaker(
                        breaker.DefinitionId,
                        out IcebreakerBehaviorSpec? breakerSpec))
                    {
                        continue;
                    }

                    CostQuote? breakCostQuote = CostModifierProjection.IcebreakerBreak(
                        working,
                        breaker,
                        breakerSpec.BreakCost);
                    int effectiveBreakCost = breakCostQuote?.EffectiveCost ?? breakerSpec.BreakCost;

                    ValueQuote breakerQuote = EncounterStrength.ProjectBreakerQuote(
                            working.BehaviorCatalog,
                            working.Cards,
                            working.Zones,
                            working.RulesState,
                            run,
                            breaker)
                        ?? throw new ResolutionFailureException(
                            $"Breaker '{breakerSpec.DefinitionId}' has no executable strength quote.");

                    if (IceInterfaceMatcher.Matches(iceSpec, breakerSpec)
                        && breakerQuote.EffectiveValue >= iceStrength
                        && availableCredits >= effectiveBreakCost)
                    {
                        for (int quantity = 1;
                            quantity <= Math.Min(breakerSpec.BreakLimit, unbrokenCount);
                            quantity++)
                        {
                            foreach (int hostedCredits in LegalHostedCreditSplits(runner, run, effectiveBreakCost))
                            {
                                actions.Add(new LegalAction(
                                    working.Allocator.NextActionId(),
                                    ActionKind.BreakSubroutines,
                                    $"Break {quantity} {(quantity == 1 ? "subroutine" : "subroutines")} on {run.Encounter.IceId.Value} "
                                    + $"with {cardId.Value} ({FormatPaymentSplit(effectiveBreakCost, hostedCredits)})",
                                    new RunBreakParameters(cardId, quantity, hostedCredits, breakCostQuote)));
                            }
                        }
                    }

                    bool dynamicPumpAvailable = breakerSpec.DynamicPump is null
                        || EncounterStrength.TryResolveDynamicPump(
                            breakerSpec,
                            cardId,
                            working.Cards,
                            working.Zones,
                            working.BehaviorCatalog,
                            out _,
                            out _);
                    if (dynamicPumpAvailable && availableCredits >= breakerSpec.PumpCost)
                    {
                        foreach (int hostedCredits in LegalHostedCreditSplits(runner, run, breakerSpec.PumpCost))
                        {
                            actions.Add(new LegalAction(
                                working.Allocator.NextActionId(),
                                ActionKind.PumpBreaker,
                                $"Pump {cardId.Value} ({FormatPaymentSplit(breakerSpec.PumpCost, hostedCredits)})",
                                new RunBreakerParameters(cardId, hostedCredits)));
                        }
                    }
                }
            }
        }

        AddAbilityActions(working, actions);

        YieldRunDecision(working, GetPlayer(working, priority.PriorityHolder).Id, prompt, actions);
    }

    private static void YieldJackOutDecision(WorkingState working, RunState run)
    {
        if (run.CurrentIceIndex < 0)
        {
            throw new ResolutionFailureException("A jack-out decision cannot be yielded at the server.");
        }

        Emit(working, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(
            run.Id,
            run.Phase,
            run.Step,
            null,
            null));

        var actions = new List<LegalAction>
        {
            new(working.Allocator.NextActionId(), ActionKind.JackOut, "Jack out", new NoActionParameters()),
            new(working.Allocator.NextActionId(), ActionKind.ContinueRun, "Continue run", new NoActionParameters()),
        };
        YieldRunDecision(working, GetPlayer(working, PlayerSide.Runner).Id, "CR 6.9.4c: jack out or continue the run.", actions);
    }

    private static void YieldBreachCandidateDecision(WorkingState working, RunState run)
    {
        ServerState server = GetBreachServer(working, run);
        ZoneState? hiddenZone = GetBreachHiddenZone(working, server);
        BreachHiddenCandidateSource? hiddenSource = RunState.HiddenBreachCandidate(
            server.Kind,
            hiddenZone,
            run.RandomAccessLimit,
            run.AccessedCards);

        Emit(working, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(
            run.Id,
            run.Phase,
            run.Step,
            null,
            null));

        var actions = new List<LegalAction>();
        foreach (CardInstanceId candidate in run.BreachCandidates)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ChooseBreachCandidate,
                $"Access {candidate.Value}",
                new BreachCandidateParameters(candidate)));
        }

        if (hiddenSource is not null)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ChooseBreachCandidate,
                hiddenSource == BreachHiddenCandidateSource.CorpHand
                    ? "Access a random card from the Corp hand"
                    : "Access the top card of R&D",
                new BreachCandidateParameters(hiddenSource.Value)));
        }

        YieldRunDecision(working, run.InitiatingPlayer, "CR 7.5.4: choose a breach candidate to access.", actions);
    }

    /// <summary>
    /// Returns every legal hosted-credit portion for a cost under CR 1.10.3c.
    /// The remainder is inferred to come from the Runner's wallet. Keeping the
    /// split in the already-materialized action makes the choice replayable
    /// and atomic: the reducer validates the exact portion before changing
    /// either source.
    /// </summary>
    private static IEnumerable<int> LegalHostedCreditSplits(
        PlayerState runner,
        RunState run,
        int amount)
    {
        if (amount < 0)
        {
            throw new ResolutionFailureException("A run credit payment amount must be nonnegative.");
        }

        int minimumHosted = Math.Max(0, amount - runner.Credits);
        int maximumHosted = Math.Min(amount, run.RunCredits);
        for (int hosted = minimumHosted; hosted <= maximumHosted; hosted++)
        {
            yield return hosted;
        }
    }

    private static bool CanAffordRunCredits(PlayerState runner, RunState run, int amount) =>
        amount >= 0 && (long)runner.Credits + run.RunCredits >= amount;

    private static string FormatPaymentSplit(int amount, int hostedCredits)
    {
        int walletCredits = checked(amount - hostedCredits);
        return walletCredits == 0
            ? $"pay {hostedCredits} hosted run credit(s)"
            : hostedCredits == 0
                ? $"pay {walletCredits} wallet credit(s)"
                : $"pay {walletCredits} wallet + {hostedCredits} hosted run credit(s)";
    }

    /// <summary>
    /// The run SPEND path (CR 26.03 1.10.3c/1.10.4b): pays a Runner credit
    /// cost while a run is live — breaker break/pump costs and the trash cost
    /// of an accessed card. The caller supplies the exact hosted-credit
    /// portion chosen in the serialized legal action; the wallet covers the
    /// remainder. Losing credits is a different path: see the
    /// LoseCreditsSubroutineSpec resolution in StepEncounter, which never
    /// touches the pool (1.10.3b).
    /// </summary>
    private static RunState PayRunnerRunCredits(
        WorkingState working,
        RunState run,
        int amount,
        int hostedCredits)
    {
        if (amount < 0)
        {
            throw new ResolutionFailureException("A run credit payment amount must be nonnegative.");
        }

        if (hostedCredits < 0 || hostedCredits > amount)
        {
            throw new ResolutionFailureException(
                $"A payment of {amount} credits cannot spend {hostedCredits} hosted run credits.");
        }

        int fromWallet = checked(amount - hostedCredits);
        if (hostedCredits > run.RunCredits || fromWallet > GetPlayer(working, PlayerSide.Runner).Credits)
        {
            throw new ResolutionFailureException(
                $"The Runner cannot pay {amount} credits with {fromWallet} wallet and {hostedCredits} run credits: "
                + $"{GetPlayer(working, PlayerSide.Runner).Credits} wallet and {run.RunCredits} run credits are available.");
        }

        if (amount == 0)
        {
            return run;
        }

        PlayerState runner = GetPlayer(working, PlayerSide.Runner);
        if (fromWallet > 0)
        {
            ApplyCreditDelta(working, runner.Id, -fromWallet);
        }

        if (hostedCredits > 0)
        {
            RunState spentRun = run.WithRunCredits(checked(run.RunCredits - hostedCredits));
            working.RunState = spentRun;
            Emit(working, EngineEventKinds.RunCreditsAdjusted, new RunCreditsAdjustedEventPayload(
                run.Id, -hostedCredits, spentRun.RunCredits));
            return spentRun;
        }

        return run;
    }

    private static void YieldBreachAccessDecision(WorkingState working, RunState run)
    {
        Emit(working, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(
            run.Id,
            run.Phase,
            run.Step,
            null,
            null));

        if (run.ChosenAccessCards.Count == 0)
        {
            throw new ResolutionFailureException("The access ability step requires a chosen access card.");
        }

        CardInstanceId accessed = run.ChosenAccessCards[^1];
        var actions = new List<LegalAction>();

        // A child ability may have moved the card to Corp discard. In that
        // case this resumed access window is intentionally NoAction-only. A
        // moved/replaced card in any other zone likewise cannot be retargeted
        // by a newly materialized ability or the basic access trash.
        bool currentAccess = TryGetCurrentAccessCard(
            working,
            run,
            out CardInstanceState? liveCard,
            requireTrashableSource: false);
        bool trashableAccess = currentAccess
            && TryGetCurrentAccessCard(
                working,
                run,
                out _,
                requireTrashableSource: true);
        CardInstanceState? accessedCard = liveCard
            ?? working.Cards.FirstOrDefault(card => card.Id == accessed);

        if (currentAccess)
        {
            AddAbilityActions(
                working,
                actions,
                suppressCorpCardTrash: HasRunRestriction(
                    run,
                    RunRestrictionKind.CannotStealOrTrashCorpCards));
        }

        // CR 26.03 7.2.2/7.1.5: the basic trash is offered only for a
        // registered asset with a trash cost the Runner can pay. Agendas
        // instead resolve an automatic steal after NoAction.
        if (trashableAccess
            && !HasRunRestriction(run, RunRestrictionKind.CannotStealOrTrashCorpCards)
            && accessedCard is not null
            && !working.BehaviorCatalog.TryGetAgenda(accessedCard.DefinitionId, out _)
            && working.BehaviorCatalog.TryGetAsset(accessedCard.DefinitionId, out AssetBehaviorSpec? assetSpec)
            && CanAffordRunCredits(GetPlayer(working, PlayerSide.Runner), run, assetSpec.TrashCost))
        {
            PlayerState runner = GetPlayer(working, PlayerSide.Runner);
            foreach (int hostedCredits in LegalHostedCreditSplits(runner, run, assetSpec.TrashCost))
            {
                actions.Add(new LegalAction(
                    working.Allocator.NextActionId(),
                    ActionKind.TrashAccessedCard,
                    $"Trash {accessed} for {assetSpec.TrashCost} credits "
                    + $"({FormatPaymentSplit(assetSpec.TrashCost, hostedCredits)})",
                    new RunCardPaymentParameters(accessed, hostedCredits)));
            }
        }

        actions.Add(new LegalAction(
            working.Allocator.NextActionId(),
            ActionKind.NoAccessAction,
            "No action",
            new NoActionParameters()));
        string prompt = currentAccess
            && accessedCard is not null
            && working.BehaviorCatalog.TryGetAgenda(accessedCard.DefinitionId, out _)
            ? "CR 7.2.2: take the mid-access ability or pass; the accessed agenda is then stolen automatically (CR 7.2.3)."
            : "CR 7.2.2: take the mid-access ability or no action.";
        YieldRunDecision(working, run.InitiatingPlayer, prompt, actions);
    }

    private static ServerState GetBreachServer(WorkingState working, RunState run) =>
        working.Servers.SingleOrDefault(candidate => candidate.Id == run.AttackedServer)
        ?? throw new ResolutionFailureException($"Run server '{run.AttackedServer}' does not exist.");

    private static ZoneState? GetBreachHiddenZone(WorkingState working, ServerState server) => server.Kind switch
    {
        ServerKind.Headquarters => GetZone(working, ZoneKind.CorpHand),
        ServerKind.RAndD => GetZone(working, ZoneKind.CorpDeck),
        _ => null,
    };

    private static void YieldRunDecision(
        WorkingState working,
        PlayerId owner,
        string prompt,
        IReadOnlyList<LegalAction> actions)
    {
        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            owner,
            DecisionKind.RunTiming,
            prompt,
            actions);
    }

    private static void SetRunTiming(WorkingState working, RunState next)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not RunTimingFrame frame
            || frame.RunId != next.Id)
        {
            throw new ResolutionFailureException("The run timing frame is missing while advancing the run.");
        }

        working.RunState = next;
        working.ResolutionStack[^1] = new RunTimingFrame(next.Id, next.Step);
    }

    private static RunPriorityState CreatePriority(
        RunPriorityWindowKind window,
        bool corpMayRezApproachedIce = false,
        bool corpMayRezNonIce = false) =>
        new(
            window,
            PlayerSide.Runner,
            corpMayRezApproachedIce,
            corpMayRezNonIce);

    private static bool IsPaidWindow(RunTimingStep step) => step is
        RunTimingStep.InitiationPaidWindow
        or RunTimingStep.ApproachPaidWindow
        or RunTimingStep.EncounterPaidWindow
        or RunTimingStep.MovementPaidWindow
        or RunTimingStep.AfterMovePaidWindow;

    private static PlayerSide OtherSide(PlayerSide side) => side switch
    {
        PlayerSide.Corp => PlayerSide.Runner,
        PlayerSide.Runner => PlayerSide.Corp,
        _ => throw new ResolutionFailureException($"Unknown player side '{side}'."),
    };

    private static ZoneState GetRunIceZone(WorkingState working, RunState run)
    {
        ServerState server = working.Servers.SingleOrDefault(candidate => candidate.Id == run.AttackedServer)
            ?? throw new ResolutionFailureException($"Run server '{run.AttackedServer}' does not exist.");
        return working.Zones.SingleOrDefault(zone => zone.Id == server.IceZoneId)
            ?? throw new ResolutionFailureException($"Run server '{run.AttackedServer}' has no ICE zone.");
    }

    private static CardInstanceId GetCurrentIceId(RunState run, ZoneState iceZone)
    {
        if (run.CurrentIceIndex < 0 || run.CurrentIceIndex >= iceZone.CardIds.Count)
        {
            throw new ResolutionFailureException($"Run ICE index {run.CurrentIceIndex} is outside the ordered ICE zone.");
        }

        CardInstanceId cardId = iceZone.CardIds[run.CurrentIceIndex];
        if (run.CurrentIceId != cardId)
        {
            throw new ResolutionFailureException("Run current ICE ID does not match its ordered ICE position.");
        }

        return cardId;
    }
}
