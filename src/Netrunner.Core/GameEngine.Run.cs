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
    public static GameState BeginRun(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        StateInvariantValidator.Validate(state);

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

        PlayerState runner = state.Players.Single(player => player.Side == PlayerSide.Runner);
        if (initiatingPlayer.HasValue && initiatingPlayer.Value != runner.Id)
        {
            throw new ResolutionFailureException("The initiating player must be the Runner.");
        }

        ServerState server = state.Servers.SingleOrDefault(candidate => candidate.Id == serverId)
            ?? throw new ResolutionFailureException($"Server '{serverId}' does not exist.");
        ZoneState iceZone = state.Zones.SingleOrDefault(zone => zone.Id == server.IceZoneId)
            ?? throw new ResolutionFailureException($"Server '{serverId}' has no ICE zone.");

        if (initiatingSource.HasValue)
        {
            CardInstanceState source = state.Cards.SingleOrDefault(card => card.Id == initiatingSource.Value)
                ?? throw new ResolutionFailureException($"Run source card '{initiatingSource.Value}' does not exist.");
            if (source.OwnerSide != PlayerSide.Runner)
            {
                throw new ResolutionFailureException("A run source must be owned by the Runner.");
            }
        }

        var working = WorkingState.From(state);
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
            initiatingSource);
        working.RunState = run;
        working.ResolutionStack.Add(new RunTimingFrame(runId, RunTimingStep.InitiationPaidWindow));

        try
        {
            // The setup boundary yields the first choice directly.  The
            // following accepted action records the opening evidence in its
            // event batch; no event sequence is consumed and then discarded.
            YieldRunPriorityDecision(
                working,
                run,
                "CR 6.9.1e: choose a paid ability or pass.",
                emitWindowEvent: false);
            Advance(working, DefaultResolutionStepBudget);
            GameState next = working.Build(state);
            StateInvariantValidator.Validate(next);
            return next;
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

    public static GameState InitiateRun(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        BeginRun(state, serverId, initiatingPlayer, initiatingSource);

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
            case RunTimingStep.Success:
                StepRunSuccess(working, run);
                break;
            case RunTimingStep.BreachBoundary:
                StepBreachBoundary(working, run);
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
                RunTimingStep.Success,
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

        EncounterId encounterId = new($"encounter:{run.Id.Value}:{ice.Id.Value}");
        var encounter = new RunEncounterState(encounterId, ice.Id, []);
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
    }

    private static void StepEncounter(WorkingState working, RunState run)
    {
        RunEncounterState encounter = run.Encounter
            ?? throw new ResolutionFailureException("Encounter processing requires an initialized encounter.");
        if (encounter.Status != RunEncounterStatus.Initialized)
        {
            throw new ResolutionFailureException("An encounter cannot be completed twice.");
        }

        var completed = new RunEncounterState(
            encounter.Version,
            encounter.Id,
            encounter.IceId,
            RunEncounterStatus.Complete,
            encounter.Subroutines);
        Emit(working, EngineEventKinds.EncounterCompleted, new EncounterCompletedEventPayload(run.Id, encounter.Id));
        SetRunTiming(working, run.WithTiming(
            RunTimingStep.MovementPaidWindow,
            run.CurrentIceIndex,
            run.CurrentIceId,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            CreatePriority(RunPriorityWindowKind.MovementPaid),
            completed,
            run.AccessState));
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

    private static void StepRunSuccess(WorkingState working, RunState run)
    {
        if (run.CurrentIceIndex >= 0)
        {
            throw new ResolutionFailureException("A run can succeed only after all ICE has been passed.");
        }

        Emit(working, EngineEventKinds.RunSuccessful, new RunSuccessfulEventPayload(run.Id, run.AttackedServer));
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
    }

    private static void StepBreachBoundary(WorkingState working, RunState run)
    {
        if (!run.Successful)
        {
            throw new ResolutionFailureException("The breach boundary requires a successful run.");
        }

        Emit(working, EngineEventKinds.BreachPlaceholderReached, new BreachPlaceholderReachedEventPayload(
            run.Id,
            run.AttackedServer));
        SetRunTiming(working, run.WithTiming(
            RunTimingStep.RunEnd,
            -1,
            null,
            run.Successful,
            run.Unsuccessful,
            RunEndReason.BreachCompleted,
            null,
            null,
            RunAccessState.PlaceholderBoundary));
    }

    private static void CompleteRun(WorkingState working, RunState run)
    {
        if (run.EndReason is null)
        {
            throw new ResolutionFailureException("Run-end cleanup requires an explicit end reason.");
        }

        Emit(working, EngineEventKinds.RunEnded, new RunEndedEventPayload(
            run.Id,
            run.InitiatingSide,
            run.EndReason.Value,
            run.Successful));
        working.RunState = null;
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not RunTimingFrame frame
            || frame.RunId != run.Id)
        {
            throw new ResolutionFailureException("Run-end cleanup did not have the matching timing frame.");
        }

        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        working.PendingDecision = null;
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
                ConsumeRunRez(working, run, rez.IceId);
                break;
            case (ActionKind.JackOut, NoActionParameters):
                ConsumeRunJackOut(working, run);
                break;
            case (ActionKind.ContinueRun, NoActionParameters):
                ConsumeRunContinue(working, run);
                break;
            default:
                throw new ResolutionFailureException($"Action kind '{kind}' has no registered run resolver.");
        }
    }

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
                    run.CurrentIceIndex >= 0 ? RunTimingStep.Approach : RunTimingStep.Success,
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

    private static void ConsumeRunRez(WorkingState working, RunState run, CardInstanceId iceId)
    {
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

        ReplaceCard(working, new CardInstanceState(
            ice.Id,
            ice.DefinitionId,
            ice.ZoneId,
            ice.OwnerSide,
            ice.FaceUp,
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
        if (run.Step == RunTimingStep.ApproachPaidWindow
            && priority.PriorityHolder == PlayerSide.Corp
            && priority.CorpMayRezApproachedIce
            && run.CurrentIceId.HasValue)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.RezIce,
                $"Rez approached ICE {run.CurrentIceId.Value}",
                new RunIceParameters(run.CurrentIceId.Value)));
        }

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
