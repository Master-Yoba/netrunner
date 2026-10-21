using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Tests for the CR 26.03 run-timing trace: citation coverage for every
/// timing step, the validator's acceptance of a real engine-driven stream,
/// its detection of missing and misordered windows, and the rendered trace
/// text. The engine-driven stream uses one face-down ICE, every paid window
/// passed, and the run jacked out at the legal decision. The adversarial
/// tests hand-build synthetic event streams (validator-only testing) that
/// must be rejected: the zero-window success evasion, the missing breach
/// after success, the missing selection/mid-access breach windows, mixed
/// events from different runs, and broken priority-holder/pass
/// alternation — plus the positive control (a minimal well-formed
/// successful stream still passes) and the M1 no-run control (a stream
/// with no run events at all stays out of scope).
/// </summary>
public sealed class RunTimingTraceTests
{
    [Fact]
    public void CitationsCoverEveryRunTimingStep()
    {
        RunTimingStep[] steps = Enum.GetValues<RunTimingStep>();
        Assert.NotEmpty(steps);
        foreach (RunTimingStep step in steps)
        {
            string citation = RunTimingCitations.ForWindow(step);
            Assert.False(string.IsNullOrWhiteSpace(citation), $"step '{step}' has a blank citation.");
            Assert.StartsWith("CR 26.03 ", citation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EventCitationsMatchTheVerifiedCrSections()
    {
        // Exact citation strings for the corrected kinds (including the
        // wallet/hosted payment choice required by CR 1.10.3c), plus the
        // access kinds re-pinned by U6. Every string is asserted exactly so a
        // regression to a wrong rule number fails this test.
        Assert.Equal(
            "CR 26.03 1.10.3c/1.10.4b: credit costs use an issued source split, or run credits before the Runner wallet for generic ability/choice costs",
            RunTimingCitations.ForEvent(EngineEventKinds.RunCreditsAdjusted));
        Assert.Equal(
            "CR 26.03 9.2: priority is passed inside a paid-ability window (9.2.7; the engine's alternating-priority pass model is a documented simplification)",
            RunTimingCitations.ForEvent(EngineEventKinds.RunPriorityPassed));
        Assert.Equal(
            "CR 26.03 7.2.3: if the accessed card is an agenda, the Runner must steal it",
            RunTimingCitations.ForEvent(EngineEventKinds.AgendaStolen));
        Assert.Equal(
            "CR 26.03 7.2.2: the Runner uses the basic trash ability, a mid-access ability, to trash the accessed card",
            RunTimingCitations.ForEvent(EngineEventKinds.AccessedCardTrashed));
    }

    [Fact]
    public void WindowCitationsForTheCuratedSequenceMatchTheVerifiedCrSections()
    {
        // Exact window citations for the steps the curated run emits.
        Assert.Equal(
            "CR 26.03 6.9.1e: initiation paid window (P)(R)",
            RunTimingCitations.ForWindow(RunTimingStep.InitiationPaidWindow));
        Assert.Equal(
            "CR 26.03 6.9.2b: approach paid window (P)(R); the Corp may rez the approached ICE",
            RunTimingCitations.ForWindow(RunTimingStep.ApproachPaidWindow));
        Assert.Equal(
            "CR 26.03 6.9.3b: encounter paid window (P)",
            RunTimingCitations.ForWindow(RunTimingStep.EncounterPaidWindow));
        Assert.Equal(
            "CR 26.03 7.5.4: the Runner selects a breach candidate to access",
            RunTimingCitations.ForWindow(RunTimingStep.BreachCandidateSelection));
        Assert.Equal(
            "CR 26.03 7.2.2: the Runner may use a mid-access ability",
            RunTimingCitations.ForWindow(RunTimingStep.BreachAccessAbility));
    }

    [Fact]
    public void ValidatedStreamEntriesCarryTheVerifiedCitations()
    {
        // End-to-end: the entries the renderer emits from a validated stream
        // carry the exact verified citation strings, and no entry carries a
        // stale citation (10.5 credits, 9.3/9.4 priority, or a 6.9.x/9.x mix).
        RunTimingTrace engineTrace = RunTimingTraceValidator.Validate(BuildJackedOutRunStream());
        Assert.True(engineTrace.IsValid, string.Join("; ", engineTrace.Problems));
        Assert.Contains(engineTrace.Entries, entry =>
            entry.Citation == "CR 26.03 6.9.1e: initiation paid window (P)(R)" &&
            entry.EventKind == EngineEventKinds.RunTimingWindowOpened);
        // The jacked-out stream uses a facedown ICE, so no 6.9.3b encounter
        // window occurs in it (6.9.2c); its citation is pinned at the
        // mapping level in
        // WindowCitationsForTheCuratedSequenceMatchTheVerifiedCrSections.
        Assert.Contains(engineTrace.Entries, entry =>
            entry.Citation == "CR 26.03 6.9.2b: approach paid window (P)(R); the Corp may rez the approached ICE" &&
            entry.EventKind == EngineEventKinds.RunTimingWindowOpened);
        Assert.Contains(engineTrace.Entries, entry =>
            entry.Citation == "CR 26.03 9.2: priority is passed inside a paid-ability window (9.2.7; the engine's alternating-priority pass model is a documented simplification)" &&
            entry.EventKind == EngineEventKinds.RunPriorityPassed);

        RunTimingTrace minimalTrace = RunTimingTraceValidator.Validate(MinimalSuccessfulRun());
        Assert.True(minimalTrace.IsValid, string.Join("; ", minimalTrace.Problems));
        Assert.Equal(11, minimalTrace.Entries.Count);
        Assert.Equal("CR 26.03 6.9.1: the run is initiated", minimalTrace.Entries[0].Citation);
        Assert.Equal(
            "CR 26.03 9.2: priority is passed inside a paid-ability window (9.2.7; the engine's alternating-priority pass model is a documented simplification)",
            minimalTrace.Entries[1].Citation);
        Assert.Equal("CR 26.03 6.9.5a: the run is successful", minimalTrace.Entries[4].Citation);
        Assert.Equal(
            "CR 26.03 6.9.5b/7.5.3: the run breaches the server and the access candidates are determined",
            minimalTrace.Entries[5].Citation);
        Assert.Equal(
            "CR 26.03 7.5.4: the Runner selects a breach candidate to access",
            minimalTrace.Entries[6].Citation);
        Assert.Equal("CR 26.03 7.5.4: a breach candidate is selected for access", minimalTrace.Entries[7].Citation);
        Assert.Equal("CR 26.03 7.1: the Runner accesses the card", minimalTrace.Entries[8].Citation);
        Assert.Equal("CR 26.03 7.2.2: the Runner may use a mid-access ability", minimalTrace.Entries[9].Citation);
        Assert.Equal("CR 26.03 6.9.6: the run ends and run-scoped effects are cleaned up", minimalTrace.Entries[10].Citation);

        foreach (RunTimingTraceEntry entry in engineTrace.Entries.Concat(minimalTrace.Entries))
        {
            Assert.DoesNotContain("10.5", entry.Citation, StringComparison.Ordinal);
            Assert.DoesNotContain("9.3/9.4", entry.Citation, StringComparison.Ordinal);
            Assert.DoesNotContain("6.9.6/9.4", entry.Citation, StringComparison.Ordinal);
            Assert.DoesNotContain("6.9.1e/9.3", entry.Citation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ValidEngineDrivenStreamPassesValidation()
    {
        IReadOnlyList<EngineEvent> frozen = BuildJackedOutRunStream();
        Assert.NotEmpty(frozen);
        Assert.Contains(frozen, @event => @event.Kind == EngineEventKinds.RunInitiated);
        Assert.Contains(frozen, @event => @event.Kind == EngineEventKinds.RunEnded);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(frozen);
        Assert.True(trace.IsValid, string.Join("; ", trace.Problems));
        Assert.Empty(trace.Problems);
        Assert.NotEmpty(trace.Entries);
    }

    [Fact]
    public void ValidatorDetectsMissingWindow()
    {
        List<EngineEvent> stream = BuildJackedOutRunStream().ToList();
        int firstWindow = stream.FindIndex(static @event => @event.Kind == EngineEventKinds.RunTimingWindowOpened);
        Assert.True(firstWindow >= 0, "the engine stream opened no timing window.");
        stream.RemoveAt(firstWindow);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.NotEmpty(trace.Problems);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains("expected 'InitiationPaidWindow' (CR 26.03 6.9.1e)", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorDetectsMisorderedWindows()
    {
        // Swap the first approach paid window with the 6.9.4c jack-out
        // decision window: the decision then opens before the approach window
        // it belongs to, which the validator must flag.
        List<EngineEvent> stream = BuildJackedOutRunStream().ToList();
        int approach = stream.FindIndex(
            static @event => IsWindow(@event, RunTimingStep.ApproachPaidWindow));
        int jackOut = stream.FindIndex(
            static @event => IsWindow(@event, RunTimingStep.JackOutDecision));
        Assert.True(approach >= 0 && jackOut > approach, "the engine stream did not open the approach and jack-out windows.");
        (stream[approach], stream[jackOut]) = (stream[jackOut], stream[approach]);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.NotEmpty(trace.Problems);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains("jacked out without the 6.9.4c jack-out decision", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderTextContainsCitations()
    {
        RunTimingTrace trace = RunTimingTraceValidator.Validate(BuildJackedOutRunStream());
        string text = trace.RenderText();
        Assert.Contains("CR 26.03", text, StringComparison.Ordinal);
        Assert.Contains(
            "Result: all required CR 26.03 run-timing windows are present and in order.",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MinimalWellFormedSuccessfulStreamPasses()
    {
        // Positive control: a minimal synthetic successful run (no ICE on
        // the server) still validates with zero problems.
        IReadOnlyList<EngineEvent> frozen = EngineEventValidator.FreezeBatch(MinimalSuccessfulRun());

        RunTimingTrace trace = RunTimingTraceValidator.Validate(frozen);
        Assert.True(trace.IsValid, string.Join("; ", trace.Problems));
        Assert.Empty(trace.Problems);
        Assert.NotEmpty(trace.Entries);
    }

    [Fact]
    public void StreamWithoutRunEventsStaysOutOfScope()
    {
        // M1 no-run control: a stream with no run events at all must not
        // produce spurious problems.
        var stream = new List<EngineEvent>
        {
            new(1, EngineEventKinds.TurnStarted, new TurnStartedEventPayload(PlayerSide.Corp, 1, 4)),
            new(2, EngineEventKinds.ClickSpent, new ClickSpentEventPayload(PlayerSide.Corp, 3)),
            new(3, EngineEventKinds.TurnEnded, new TurnEndedEventPayload(PlayerSide.Corp, 1)),
        };

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.True(trace.IsValid, string.Join("; ", trace.Problems));
        Assert.Empty(trace.Problems);
        Assert.Empty(trace.Entries);
    }

    [Fact]
    public void SuccessfulRunWithZeroTimingWindowsIsRejected()
    {
        // The exact evasion the review described: a synthetic successful
        // stream with run-initiated, run-successful, and run-ended but no
        // timing windows or breach must be rejected.
        RunId run = new("run:1");
        ServerId server = new("server:hq");
        var stream = new List<EngineEvent>
        {
            new(1, EngineEventKinds.RunInitiated, new RunInitiatedEventPayload(run, server, new PlayerId("player:runner"))),
            new(2, EngineEventKinds.RunSuccessful, new RunSuccessfulEventPayload(run, server)),
            new(3, EngineEventKinds.RunEnded, new RunEndedEventPayload(run, PlayerSide.Runner, RunEndReason.BreachCompleted, true)),
        };

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "the run opened no timing window; every run opens the 6.9.1e initiation paid window", StringComparison.Ordinal));
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "the run ended successful but the trace has no matching breach-started event", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingBreachAfterSuccessIsRejected()
    {
        // Windows and success are present, but the breach event the success
        // depends on is absent.
        List<EngineEvent> stream = MinimalSuccessfulRun();
        int breach = stream.FindIndex(static @event => @event.Kind == EngineEventKinds.BreachStarted);
        Assert.True(breach >= 0, "the minimal stream has no breach-started event to remove.");
        stream.RemoveAt(breach);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "the run ended successful but the trace has no matching breach-started event", StringComparison.Ordinal));
    }

    [Fact]
    public void ChosenCandidateWithoutSelectionWindowIsRejected()
    {
        // The breach reports one candidate and the candidate is chosen and
        // accessed, but the 7.5.4 candidate-selection window is missing.
        List<EngineEvent> stream = MinimalSuccessfulRun();
        int selection = stream.FindIndex(static @event =>
            @event.Kind == EngineEventKinds.RunTimingWindowOpened
            && ((RunTimingWindowOpenedEventPayload)@event.Payload).Step == RunTimingStep.BreachCandidateSelection);
        Assert.True(selection >= 0, "the minimal stream has no selection window to remove.");
        stream.RemoveAt(selection);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "breach candidate 'card:1' was chosen without a preceding 7.5.4 candidate-selection window", StringComparison.Ordinal));
    }

    [Fact]
    public void AccessEffectWithoutMidAccessWindowIsRejected()
    {
        // The accessed card is trashed through its mid-access ability, but
        // the 7.2.2 mid-access window never opened before the effect.
        List<EngineEvent> stream = MinimalSuccessfulRun();
        RunId run = new("run:1");
        CardInstanceId card = new("card:1");
        int accessWindow = stream.FindIndex(static @event => IsWindow(@event, RunTimingStep.BreachAccessAbility));
        Assert.True(accessWindow >= 0, "the minimal stream has no access window to remove.");
        stream.RemoveAt(accessWindow);
        int accessed = stream.FindIndex(static @event => @event.Kind == EngineEventKinds.CardAccessed);
        Assert.True(accessed >= 0, "the minimal stream has no card-accessed event.");
        stream.Insert(accessed + 1, new EngineEvent(long.MaxValue, EngineEventKinds.AccessedCardTrashed, new AccessedCardTrashedEventPayload(run, card, 2)));
        for (int index = 0; index < stream.Count; index++)
        {
            stream[index] = new EngineEvent(index + 1, stream[index].Kind, stream[index].Payload);
        }

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "CardAccessed for 'card:1'", StringComparison.Ordinal)
                && problem.Contains("has 0 BreachAccessAbility window(s); exactly one is required", StringComparison.Ordinal));
    }

    [Fact]
    public void CardAccessedWithoutMidAccessWindowIsRejectedEvenForNoActionAccess()
    {
        List<EngineEvent> stream = MinimalSuccessfulRun();
        int accessWindow = stream.FindIndex(static @event => IsWindow(@event, RunTimingStep.BreachAccessAbility));
        Assert.True(accessWindow >= 0);
        stream.RemoveAt(accessWindow);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(trace.Problems, problem =>
            problem.Contains("CardAccessed for 'card:1'", StringComparison.Ordinal)
            && problem.Contains("has 0 BreachAccessAbility window(s); exactly one is required", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateMidAccessWindowsAreRejectedWithinOneAccessSegment()
    {
        List<EngineEvent> stream = MinimalSuccessfulRun();
        int accessWindow = stream.FindIndex(static @event => IsWindow(@event, RunTimingStep.BreachAccessAbility));
        Assert.True(accessWindow >= 0);
        stream.Insert(accessWindow + 1, stream[accessWindow]);
        Renumber(stream);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(trace.Problems, problem =>
            problem.Contains("CardAccessed for 'card:1'", StringComparison.Ordinal)
            && problem.Contains("has 2 BreachAccessAbility window(s); exactly one is required", StringComparison.Ordinal));
    }

    [Fact]
    public void CrossCardAccessEffectIsRejected()
    {
        List<EngineEvent> stream = MinimalSuccessfulRun();
        int accessed = stream.FindIndex(static @event => @event.Kind == EngineEventKinds.CardAccessed);
        Assert.True(accessed >= 0);
        stream.Insert(
            accessed + 2,
            new EngineEvent(
                long.MaxValue,
                EngineEventKinds.AccessedCardTrashed,
                new AccessedCardTrashedEventPayload(new RunId("run:1"), new CardInstanceId("card:2"), 2)));
        Renumber(stream);

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(trace.Problems, problem =>
            problem.Contains("names card 'card:2'", StringComparison.Ordinal)
            && problem.Contains("segment accessed 'card:1'", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedRunIdsAreRejected()
    {
        // Events of two different runs interleaved into one stream must be
        // rejected by the run-id consistency check (and the single-run
        // count checks).
        List<EngineEvent> runA = MinimalSuccessfulRun(new RunId("run:1"), new ServerId("server:a"));
        List<EngineEvent> runB = MinimalSuccessfulRun(new RunId("run:2"), new ServerId("server:b"));
        var interleaved = new List<EngineEvent>();
        for (int index = 0; index < Math.Max(runA.Count, runB.Count); index++)
        {
            if (index < runA.Count)
            {
                interleaved.Add(runA[index]);
            }

            if (index < runB.Count)
            {
                interleaved.Add(runB[index]);
            }
        }

        // Both hand-built runs start their sequences at 1; renumber for a
        // realistic stream. (Validate itself ignores sequences — only
        // FreezeBatch enforces strictly increasing ones.)
        for (int index = 0; index < interleaved.Count; index++)
        {
            interleaved[index] = new EngineEvent(index + 1, interleaved[index].Kind, interleaved[index].Payload);
        }

        RunTimingTrace trace = RunTimingTraceValidator.Validate(interleaved);
        Assert.False(trace.IsValid);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "belongs to run 'run:2' but the trace is from run 'run:1'", StringComparison.Ordinal));
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "expected exactly one 'run-initiated' event, found 2", StringComparison.Ordinal));
    }

    [Fact]
    public void PassByWrongHolderIsRejected()
    {
        // A pass issued when the passing side does not hold priority: the
        // initiation window opens with the Runner holding priority, so the
        // Corp cannot be the first to pass.
        RunId run = new("run:1");
        var stream = new List<EngineEvent>
        {
            new(1, EngineEventKinds.RunInitiated, new RunInitiatedEventPayload(run, new ServerId("server:hq"), new PlayerId("player:runner"))),
            new(2, EngineEventKinds.RunPriorityPassed, new RunPriorityPassedEventPayload(run, RunPriorityWindowKind.InitiationPaid, PlayerSide.Corp, PlayerSide.Runner, 1)),
            new(3, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(run, RunCrPhase.Initiation, RunTimingStep.InitiationPaidWindow, RunPriorityWindowKind.InitiationPaid, PlayerSide.Runner)),
            new(4, EngineEventKinds.RunPriorityPassed, new RunPriorityPassedEventPayload(run, RunPriorityWindowKind.InitiationPaid, PlayerSide.Runner, PlayerSide.Corp, 2)),
            new(5, EngineEventKinds.RunEnded, new RunEndedEventPayload(run, PlayerSide.Runner, RunEndReason.JackedOut, false)),
        };

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "priority in the InitiationPaidWindow window was passed by Corp but the priority holder was Runner", StringComparison.Ordinal));
    }

    [Fact]
    public void ConsecutivePassesBySameHolderAreRejected()
    {
        // The same holder passing twice in a row: after the Runner's first
        // pass the Corp holds priority, so a second pass by the Runner
        // breaks the required alternation.
        RunId run = new("run:1");
        var stream = new List<EngineEvent>
        {
            new(1, EngineEventKinds.RunInitiated, new RunInitiatedEventPayload(run, new ServerId("server:hq"), new PlayerId("player:runner"))),
            new(2, EngineEventKinds.RunPriorityPassed, new RunPriorityPassedEventPayload(run, RunPriorityWindowKind.InitiationPaid, PlayerSide.Runner, PlayerSide.Corp, 1)),
            new(3, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(run, RunCrPhase.Initiation, RunTimingStep.InitiationPaidWindow, RunPriorityWindowKind.InitiationPaid, PlayerSide.Corp)),
            new(4, EngineEventKinds.RunPriorityPassed, new RunPriorityPassedEventPayload(run, RunPriorityWindowKind.InitiationPaid, PlayerSide.Runner, PlayerSide.Corp, 2)),
            new(5, EngineEventKinds.RunEnded, new RunEndedEventPayload(run, PlayerSide.Runner, RunEndReason.JackedOut, false)),
        };

        RunTimingTrace trace = RunTimingTraceValidator.Validate(stream);
        Assert.False(trace.IsValid);
        Assert.Contains(
            trace.Problems,
            problem => problem.Contains(
                "priority in the InitiationPaidWindow window was passed by Runner but the priority holder was Corp", StringComparison.Ordinal));
    }

    /// <summary>
    /// Drives one full engine run against a single face-down ICE and returns
    /// the frozen stream: the BeginRun batch plus every accepted action's
    /// event batch, ending with the jack-out at the 6.9.4c decision.
    /// </summary>
    private static IReadOnlyList<EngineEvent> BuildJackedOutRunStream()
    {
        GameState state = RunTimingTests.CreateRunState(iceCount: 1, rezzed: false);
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(state, RunTimingTests.RemoteId(state));
        var events = new List<EngineEvent>(start.Events);
        state = start.State;

        int decisions = 0;
        while (state.RunState is not null && state.PendingDecision is not null)
        {
            Assert.True(++decisions <= 16, "the run timing loop did not make progress.");
            if (state.RunState.Step == RunTimingStep.JackOutDecision)
            {
                ApplyResult result = RunTimingTests.ApplyKind(state, ActionKind.JackOut);
                Assert.True(result.Accepted, result.Rejection?.Message);
                events.AddRange(result.Events);
                state = result.State;
                continue;
            }

            // Every accepted pass in a paid window re-yields the window and
            // emits its own event batch, so both passes must be collected.
            ApplyResult first = RunTimingTests.ApplyKind(state, ActionKind.PassPriority);
            Assert.True(first.Accepted, first.Rejection?.Message);
            events.AddRange(first.Events);
            ApplyResult second = RunTimingTests.ApplyKind(first.State, ActionKind.PassPriority);
            Assert.True(second.Accepted, second.Rejection?.Message);
            events.AddRange(second.Events);
            state = second.State;
        }

        Assert.Null(state.RunState);
        return EngineEventValidator.FreezeBatch(events);
    }

    private static bool IsWindow(EngineEvent @event, RunTimingStep step) =>
        @event.Kind == EngineEventKinds.RunTimingWindowOpened
        && ((RunTimingWindowOpenedEventPayload)@event.Payload).Step == step;

    /// <summary>
    /// Hand-builds the minimal well-formed successful run used by the
    /// adversarial tests: a run against an ICE-less server, the initiation
    /// paid window passed by both sides (the window's first yield emits no
    /// event, so the first pass precedes the first window event, exactly as
    /// the engine streams it), success, a one-candidate breach, the 7.5.4
    /// selection window, the access, and the run end.
    /// </summary>
    private static List<EngineEvent> MinimalSuccessfulRun(RunId? runId = null, ServerId? serverId = null)
    {
        RunId run = runId ?? new RunId("run:1");
        ServerId server = serverId ?? new ServerId("server:hq");
        CardInstanceId card = new("card:1");
        PlayerId runner = new("player:runner");

        var events = new List<EngineEvent>
        {
            new(1, EngineEventKinds.RunInitiated, new RunInitiatedEventPayload(run, server, runner)),
            new(2, EngineEventKinds.RunPriorityPassed, new RunPriorityPassedEventPayload(run, RunPriorityWindowKind.InitiationPaid, PlayerSide.Runner, PlayerSide.Corp, 1)),
            new(3, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(run, RunCrPhase.Initiation, RunTimingStep.InitiationPaidWindow, RunPriorityWindowKind.InitiationPaid, PlayerSide.Corp)),
            new(4, EngineEventKinds.RunPriorityPassed, new RunPriorityPassedEventPayload(run, RunPriorityWindowKind.InitiationPaid, PlayerSide.Corp, PlayerSide.Runner, 2)),
            new(5, EngineEventKinds.RunSuccessful, new RunSuccessfulEventPayload(run, server)),
            new(6, EngineEventKinds.BreachStarted, new BreachStartedEventPayload(run, server, 0, 1)),
            new(7, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(run, RunCrPhase.Success, RunTimingStep.BreachCandidateSelection, null, null)),
            new(8, EngineEventKinds.BreachCandidateChosen, new BreachCandidateChosenEventPayload(run, server, card, null)),
            new(9, EngineEventKinds.CardAccessed, new CardAccessedEventPayload(run, server, card)),
            new(10, EngineEventKinds.RunTimingWindowOpened, new RunTimingWindowOpenedEventPayload(run, RunCrPhase.Success, RunTimingStep.BreachAccessAbility, null, null)),
            new(11, EngineEventKinds.RunEnded, new RunEndedEventPayload(run, PlayerSide.Runner, RunEndReason.BreachCompleted, true)),
        };
        return events;
    }

    private static void Renumber(List<EngineEvent> stream)
    {
        for (int index = 0; index < stream.Count; index++)
        {
            stream[index] = new EngineEvent(index + 1, stream[index].Kind, stream[index].Payload);
        }
    }
}
