using System.Globalization;
using System.Text;

namespace Netrunner.Core;

/// <summary>
/// One cited observation in a validated run-timing trace: an engine event, the
/// detail that identifies it, and the CR 26.03 section that requires it.
/// </summary>
public sealed record RunTimingTraceEntry(
    int Index,
    long Sequence,
    string EventKind,
    string Detail,
    string Citation);

/// <summary>
/// The automated view of a single run's timing evidence: every run-timing
/// event in stream order, each cited to CR 26.03, plus the ordered-window
/// problems found (an empty list means every required window is present and
/// in order).
/// </summary>
public sealed class RunTimingTrace
{
    internal RunTimingTrace(IReadOnlyList<RunTimingTraceEntry> entries, IReadOnlyList<string> problems)
    {
        Entries = entries;
        Problems = problems;
    }

    public IReadOnlyList<RunTimingTraceEntry> Entries { get; }

    public IReadOnlyList<string> Problems { get; }

    public bool IsValid => Problems.Count == 0;

    /// <summary>Renders the human-readable trace used for review.</summary>
    public string RenderText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Run timing trace (CR 26.03):");
        for (int index = 0; index < Entries.Count; index++)
        {
            RunTimingTraceEntry entry = Entries[index];
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"  {index + 1,3}. [seq {entry.Sequence}] {entry.EventKind} — {entry.Detail} ({entry.Citation})");
        }

        if (Problems.Count == 0)
        {
            builder.AppendLine("  Result: all required CR 26.03 run-timing windows are present and in order.");
        }
        else
        {
            builder.AppendLine("  Result: PROBLEMS FOUND:");
            foreach (string problem in Problems)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"    - {problem}");
            }
        }

        return builder.ToString();
    }
}

/// <summary>CR 26.03 citations for the run-timing events and paid windows.</summary>
public static class RunTimingCitations
{
    public static string ForEvent(string kind) => kind switch
    {
        EngineEventKinds.RunInitiated => "CR 26.03 6.9.1: the run is initiated",
        EngineEventKinds.RunEventPlayed => "CR 26.03 6.9.1: the run is initiated by playing a run event",
        EngineEventKinds.IceApproached => "CR 26.03 6.9.2a: the run approaches the next outermost remaining ICE",
        EngineEventKinds.IceRezzed => "CR 26.03 8.1: the Corp rezzes the approached ICE during the approach window",
        EngineEventKinds.EncounterInitialized => "CR 26.03 6.9.3a: the encounter is initialized",
        EngineEventKinds.BreakerPumped => "CR 26.03 6.5/3.9.5: the Runner pumps an installed icebreaker's strength for the encounter",
        EngineEventKinds.SubroutinesBroken => "CR 26.03 6.5/3.9.5: the Runner breaks subroutines with an installed icebreaker of sufficient strength and matching subtype",
        EngineEventKinds.EncounterCompleted => "CR 26.03 6.5/6.9.3: encounter processing completes",
        EngineEventKinds.RunMoved => "CR 26.03 6.9.4d: the run moves inward to the next position",
        EngineEventKinds.RunSuccessful => "CR 26.03 6.9.5a: the run is successful",
        EngineEventKinds.BreachStarted => "CR 26.03 6.9.5b/7.5.3: the run breaches the server and the access candidates are determined",
        EngineEventKinds.BreachCandidateChosen => "CR 26.03 7.5.4: a breach candidate is selected for access",
        EngineEventKinds.CardAccessed => "CR 26.03 7.1: the Runner accesses the card",
        EngineEventKinds.AgendaStolen => "CR 26.03 7.2.3: if the accessed card is an agenda, the Runner must steal it",
        EngineEventKinds.AccessedCardTrashed => "CR 26.03 7.2.2: the Runner uses the basic trash ability, a mid-access ability, to trash the accessed card",
        EngineEventKinds.RunCreditsAdjusted => "CR 26.03 1.10.3c/1.10.4b: spending credits uses the player-chosen division between the Runner wallet and hosted run credits",
        EngineEventKinds.RunPriorityPassed => "CR 26.03 9.2: priority is passed inside a paid-ability window (9.2.7; the engine's alternating-priority pass model is a documented simplification)",
        EngineEventKinds.RunEnded => "CR 26.03 6.9.6: the run ends and run-scoped effects are cleaned up",
        _ => throw new ArgumentException($"No CR citation is defined for run event kind '{kind}'.", nameof(kind)),
    };

    public static string ForWindow(RunTimingStep step) => step switch
    {
        RunTimingStep.InitiationPaidWindow => "CR 26.03 6.9.1e: initiation paid window (P)(R)",
        RunTimingStep.Approach => "CR 26.03 6.9.2a: the run approaches the outermost remaining ICE (or the server root)",
        RunTimingStep.ApproachPaidWindow => "CR 26.03 6.9.2b: approach paid window (P)(R); the Corp may rez the approached ICE",
        RunTimingStep.EncounterInitialization => "CR 26.03 6.9.3a: the encounter is initialized",
        RunTimingStep.EncounterPaidWindow => "CR 26.03 6.9.3b: encounter paid window (P)",
        RunTimingStep.Encounter => "CR 26.03 6.9.3c: encounter processing; unbroken subroutines fire in printed order",
        RunTimingStep.MovementPaidWindow => "CR 26.03 6.9.4b: movement paid window (P)",
        RunTimingStep.JackOutDecision => "CR 26.03 6.9.4c: the Runner decides to jack out or continue",
        RunTimingStep.MoveInward => "CR 26.03 6.9.4d: the run moves inward automatically",
        RunTimingStep.AfterMovePaidWindow => "CR 26.03 6.9.4e: after-movement paid window (P)(R)",
        RunTimingStep.Success => "CR 26.03 6.9.5a: the run is declared successful without a paid window",
        RunTimingStep.BreachBoundary => "CR 26.03 6.9.5b: the nested breach boundary starts candidate selection",
        RunTimingStep.BreachCandidateSelection => "CR 26.03 7.5.4: the Runner selects a breach candidate to access",
        RunTimingStep.BreachAccessAbility => "CR 26.03 7.2.2: the Runner may use a mid-access ability",
        RunTimingStep.RunEnd => "CR 26.03 6.9.6: the run ends; cleanup and run-scoped expiry occur",
        _ => throw new ArgumentException($"No CR citation is defined for timing step '{step}'.", nameof(step)),
    };
}

/// <summary>
/// Validates that a frozen event stream containing a run contains exactly one
/// run, that all of its run events agree on a single run ID, that a played
/// run event (when present) precedes the nested initiation event, that every CR
/// 26.03 run-timing window required by the run's shape (approaches,
/// encounters, movement, jack-out decisions, breach candidate selection, and
/// access windows) is present and in order, and that priority passes follow
/// the CR 26.03 9.2 paid-ability-window passing model (a documented
/// simplification). The completeness checks are
/// unconditional for a run trace: a stream that claims a successful run must
/// carry the timing windows and the breach the success depends on, and a
/// stream with no run events at all (the M1 no-run paths) stays out of scope
/// and validates with no problems. This is the automated half of the M2
/// exit-gate timing review; <see cref="RunTimingTrace.RenderText"/> is the
/// human-readable half.
/// </summary>
public static class RunTimingTraceValidator
{
    private static readonly HashSet<string> RunRelatedKindSet = new(
    [
        EngineEventKinds.RunInitiated,
        EngineEventKinds.RunEventPlayed,
        EngineEventKinds.RunTimingWindowOpened,
        EngineEventKinds.IceApproached,
        EngineEventKinds.IceRezzed,
        EngineEventKinds.EncounterInitialized,
        EngineEventKinds.BreakerPumped,
        EngineEventKinds.SubroutinesBroken,
        EngineEventKinds.EncounterCompleted,
        EngineEventKinds.RunMoved,
        EngineEventKinds.RunSuccessful,
        EngineEventKinds.BreachStarted,
        EngineEventKinds.BreachCandidateChosen,
        EngineEventKinds.CardAccessed,
        EngineEventKinds.AgendaStolen,
        EngineEventKinds.AccessedCardTrashed,
        EngineEventKinds.RunCreditsAdjusted,
        EngineEventKinds.RunPriorityPassed,
        EngineEventKinds.RunEnded,
    ],
    StringComparer.Ordinal);

    public static RunTimingTrace Validate(IReadOnlyList<EngineEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var problems = new List<string>();

        // (stream index, event) for every run-related event, in stream order.
        var runEvents = new List<(int Index, EngineEvent Event)>();
        for (int index = 0; index < events.Count; index++)
        {
            if (RunRelatedKindSet.Contains(events[index].Kind))
            {
                runEvents.Add((index, events[index]));
            }
        }

        // A stream with no run events at all is a no-run trace (the M1
        // paths): the run-timing gate does not apply to it.
        if (runEvents.Count == 0)
        {
            return new RunTimingTrace([], problems);
        }

        var initiated = runEvents.Where(entry => entry.Event.Kind == EngineEventKinds.RunInitiated).Select(entry => entry.Index).ToList();
        var played = runEvents.Where(entry => entry.Event.Kind == EngineEventKinds.RunEventPlayed).Select(entry => entry.Index).ToList();
        var ended = runEvents.Where(entry => entry.Event.Kind == EngineEventKinds.RunEnded).Select(entry => entry.Index).ToList();

        if (initiated.Count != 1)
        {
            problems.Add($"expected exactly one '{EngineEventKinds.RunInitiated}' event, found {initiated.Count}.");
        }

        if (ended.Count != 1)
        {
            problems.Add($"expected exactly one '{EngineEventKinds.RunEnded}' event, found {ended.Count}.");
        }

        if (ended.Count == 1 && runEvents[^1].Index != ended[0])
        {
            problems.Add($"the last run event is '{runEvents[^1].Event.Kind}', not '{EngineEventKinds.RunEnded}'.");
        }

        if (initiated.Count == 1 && played.Any(index => index > initiated[0]))
        {
            problems.Add(
                $"a run event was played after '{EngineEventKinds.RunInitiated}'; event placement/payment must precede the nested run initiation.");
        }

        var windows = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.RunTimingWindowOpened)
            .Select(entry => (entry.Index, Payload: (RunTimingWindowOpenedEventPayload)entry.Event.Payload))
            .ToList();

        var approaches = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.IceApproached)
            .Select(entry => (entry.Index, IceId: ((IceApproachedEventPayload)entry.Event.Payload).IceId.Value))
            .ToList();

        var rezzed = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.IceRezzed)
            .Select(entry => (entry.Index, IceId: ((IceRezzedEventPayload)entry.Event.Payload).IceId.Value))
            .ToList();

        var encounterInitiated = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.EncounterInitialized)
            .Select(entry => (entry.Index, IceId: ((EncounterInitializedEventPayload)entry.Event.Payload).IceId.Value))
            .ToList();

        var encounterCompleted = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.EncounterCompleted)
            .Select(entry => entry.Index)
            .ToList();

        var runSuccessful = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.RunSuccessful)
            .Select(entry => entry.Index)
            .ToList();

        var breachStarted = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.BreachStarted)
            .Select(entry => (entry.Index, Payload: (BreachStartedEventPayload)entry.Event.Payload))
            .ToList();

        var candidatesChosen = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.BreachCandidateChosen)
            .Select(entry =>
            {
                BreachCandidateChosenEventPayload chosen = (BreachCandidateChosenEventPayload)entry.Event.Payload;
                // Hidden-source choices (HQ/R&D hidden candidates) are drawn
                // by the engine and are not counted in BreachStarted's
                // CandidateCount, which counts the explicit candidate pool.
                return (entry.Index, CardId: chosen.CardId.Value, Hidden: chosen.Source is not null);
            })
            .ToList();

        var cardsAccessed = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.CardAccessed)
            .Select(entry => (entry.Index, CardId: ((CardAccessedEventPayload)entry.Event.Payload).CardId.Value))
            .ToList();

        int initiatedIndex = initiated.Count == 1 ? initiated[0] : -1;
        int endIndex = ended.Count == 1 ? ended[0] : -1;
        RunEndReason? endReason = ended.Count == 1
            ? ((RunEndedEventPayload)runEvents.Single(entry => entry.Index == endIndex).Event.Payload).Reason
            : null;

        // 0. Run-id consistency: every run event that carries a run ID must
        //    carry the trace's single run ID, so events of two interleaved
        //    runs cannot mix into one trace. agenda-stolen is the one
        //    run-related kind without a run ID and is exempt.
        RunId? traceRunId = null;
        int runIdConflicts = 0;
        for (int runEvent = 0; runEvent < runEvents.Count; runEvent++)
        {
            if (!TryGetRunId(runEvents[runEvent].Event.Payload, out RunId eventId))
            {
                continue;
            }

            if (traceRunId is null)
            {
                traceRunId = eventId;
            }
            else if (traceRunId.Value != eventId)
            {
                runIdConflicts++;
                if (runIdConflicts <= 5)
                {
                    problems.Add(
                        $"run event '{runEvents[runEvent].Event.Kind}' at stream index {runEvents[runEvent].Index} "
                        + $"belongs to run '{eventId.Value}' but the trace is from run '{traceRunId.Value}'.");
                }
            }
        }

        if (runIdConflicts > 5)
        {
            problems.Add($"and {runIdConflicts - 5} more run event(s) from another run.");
        }

        // 1. The run must open a first timing window, and that window must be
        //    the 6.9.1e initiation paid window, after the run is initiated.
        //    A run trace with zero windows — for example a successful run
        //    that never opened its initiation window — is a problem.
        if (windows.Count == 0)
        {
            problems.Add(
                "the run opened no timing window; every run opens the 6.9.1e initiation paid window (CR 26.03 6.9.1e).");
        }
        else
        {
            if (windows[0].Payload.Step != RunTimingStep.InitiationPaidWindow)
            {
                problems.Add($"the first timing window is step '{windows[0].Payload.Step}', expected 'InitiationPaidWindow' (CR 26.03 6.9.1e).");
            }

            if (initiatedIndex >= 0 && windows[0].Index < initiatedIndex)
            {
                problems.Add("a timing window opens before the run is initiated.");
            }
        }

        // 2. For every approached ICE the required window sequence is present
        //    in order: the 6.9.2b approach paid window, then either the
        //    6.9.3a/6.9.3b encounter (rezzed ICE) or nothing (passed ICE),
        //    then the 6.9.4b movement window, the 6.9.4c jack-out decision,
        //    and the 6.9.4e after-movement window — unless the run ended first
        //    (jacked out at 6.9.4c or by an end-the-run subroutine).
        for (int position = 0; position < approaches.Count; position++)
        {
            int approachIndex = approaches[position].Index;
            string iceId = approaches[position].IceId;
            // The stop for this ICE lasts until the next approach, or to the
            // end of the stream for the final ICE.
            int stopEnd = position + 1 < approaches.Count ? approaches[position + 1].Index : int.MaxValue;

            int approachWindow = NextWindow(windows, after: approachIndex, before: stopEnd, RunTimingStep.ApproachPaidWindow);
            if (approachWindow < 0)
            {
                problems.Add($"approached ICE '{iceId}' (CR 26.03 6.9.2a) is not followed by the 6.9.2b approach paid window.");
                continue;
            }

            // The run ended during this stop only if it never succeeded.
            bool endedDuringStop = endIndex > approachWindow && endIndex < stopEnd && runSuccessful.Count == 0;

            var rezIndexes = rezzed
                .Where(entry => entry.IceId == iceId && entry.Index > approachWindow && entry.Index < stopEnd)
                .Select(entry => entry.Index)
                .ToList();
            if (rezIndexes.Count > 1)
            {
                problems.Add($"ICE '{iceId}' was rezzed {rezIndexes.Count} times during its approach window (CR 26.03 8.1).");
            }

            int initialized = encounterInitiated
                .Where(entry => entry.IceId == iceId && entry.Index > approachWindow && entry.Index < stopEnd)
                .Select(entry => entry.Index)
                .DefaultIfEmpty(-1)
                .First();

            if (initialized >= 0)
            {
                if (rezIndexes.Count == 0)
                {
                    problems.Add($"the encounter for ICE '{iceId}' initialized without the ICE being rezzed (CR 26.03 8.1).");
                }
                else if (rezIndexes[0] > initialized)
                {
                    problems.Add($"ICE '{iceId}' was rezzed after its encounter initialized (rez belongs to the approach window, CR 26.03 6.9.2b/8.1).");
                }

                if (NextWindow(windows, after: initialized, before: stopEnd, RunTimingStep.EncounterPaidWindow) < 0)
                {
                    problems.Add($"the encounter for ICE '{iceId}' (CR 26.03 6.9.3a) is not followed by the 6.9.3b encounter paid window.");
                }

                if (!encounterCompleted.Exists(index => index > approachWindow && index < stopEnd))
                {
                    problems.Add($"the encounter for ICE '{iceId}' never completed (CR 26.03 6.5/6.9.3).");
                }
            }

            if (endedDuringStop)
            {
                // The run ended during this ICE's stop (end-the-run subroutine
                // or the jack-out decision); no later windows are required.
                if (endReason == RunEndReason.JackedOut
                    && NextWindow(windows, after: approachWindow, before: endIndex, RunTimingStep.JackOutDecision) < 0)
                {
                    problems.Add($"the run jacked out without the 6.9.4c jack-out decision for ICE '{iceId}'.");
                }

                continue;
            }

            int movementWindow = NextWindow(windows, after: approachWindow, before: stopEnd, RunTimingStep.MovementPaidWindow);
            if (movementWindow < 0)
            {
                problems.Add($"the approach of ICE '{iceId}' is not followed by the 6.9.4b movement paid window.");
                continue;
            }

            int jackOut = NextWindow(windows, after: movementWindow, before: stopEnd, RunTimingStep.JackOutDecision);
            if (jackOut < 0)
            {
                problems.Add($"movement after ICE '{iceId}' is not followed by the 6.9.4c jack-out decision.");
                continue;
            }

            if (NextWindow(windows, after: jackOut, before: stopEnd, RunTimingStep.AfterMovePaidWindow) < 0)
            {
                problems.Add($"the jack-out decision after ICE '{iceId}' was passed but the 6.9.4e after-movement paid window never opened.");
            }
        }

        // 3. Success and breach ordering: 6.9.5a success after the final ICE
        //    stop, then 6.9.5b/7.5.3 breach start, with no windows after the
        //    run is successful except the breach windows themselves.
        bool successfulRun = ended.Count == 1
            && ((RunEndedEventPayload)runEvents.Single(entry => entry.Index == endIndex).Event.Payload).Successful;
        if (successfulRun != (runSuccessful.Count == 1))
        {
            problems.Add($"the run-successful event count ({runSuccessful.Count}) contradicts the run-ended successful flag ({successfulRun}).");
        }

        // A successful run always breaches the server (CR 26.03 6.9.5b):
        // the stream's own success claim requires the breach to be there.
        if (successfulRun && breachStarted.Count != 1)
        {
            problems.Add(
                "the run ended successful but the trace has no matching breach-started event; a successful run "
                + "always breaches the server (CR 26.03 6.9.5b/7.5.3).");
        }

        if (runSuccessful.Count == 1 && windows.Any(window => window.Index > runSuccessful[0]
                && window.Payload.Step != RunTimingStep.BreachBoundary
                && window.Payload.Step != RunTimingStep.BreachCandidateSelection
                && window.Payload.Step != RunTimingStep.BreachAccessAbility))
        {
            problems.Add("a non-breach timing window opens after the run is successful (CR 26.03 6.9.5a).");
        }

        if (breachStarted.Count == 1 && runSuccessful.Count == 1 && breachStarted[0].Index < runSuccessful[0])
        {
            problems.Add("the breach started before the run was successful (CR 26.03 6.9.5b).");
        }

        // 4. Breach access: every chosen candidate has its own 7.5.4 selection
        //    window and exactly one CardAccessed segment. Each segment is
        //    bounded by the next candidate/access/end event and contains
        //    exactly one 7.2.2 BreachAccessAbility window, even when the
        //    Runner takes no action. Any agenda-steal/basic-trash effect in a
        //    segment must name that segment's accessed card; effects outside a
        //    segment, duplicate windows, and cross-card effects are invalid.
        int explicitChosen = candidatesChosen.Count(entry => !entry.Hidden);
        if (breachStarted.Count == 1 && explicitChosen != breachStarted[0].Payload.CandidateCount)
        {
            problems.Add(
                $"the breach reported {breachStarted[0].Payload.CandidateCount} candidates but {explicitChosen} were chosen (CR 26.03 7.5.3/7.5.4).");
        }

        var accessWindows = windows
            .Where(window => window.Payload.Step == RunTimingStep.BreachAccessAbility)
            .Select(window => window.Index)
            .ToList();
        var accessEffects = runEvents
            .Where(entry => entry.Event.Kind is EngineEventKinds.AgendaStolen or EngineEventKinds.AccessedCardTrashed)
            .Select(entry =>
            {
                string effectCardId = entry.Event.Payload switch
                {
                    AgendaStolenEventPayload stolen => stolen.AgendaId.Value,
                    AccessedCardTrashedEventPayload trashed => trashed.CardId.Value,
                    _ => string.Empty,
                };
                return (entry.Index, Kind: entry.Event.Kind, CardId: effectCardId);
            })
            .ToList();
        var accessSegments = new List<(int CandidateIndex, int AccessIndex, string CardId, int Boundary)>();

        for (int chosen = 0; chosen < candidatesChosen.Count; chosen++)
        {
            int previousBoundary = chosen > 0
                ? candidatesChosen[chosen - 1].Index
                : (breachStarted.Count == 1 ? breachStarted[0].Index : initiatedIndex);
            int chosenIndex = candidatesChosen[chosen].Index;
            string cardId = candidatesChosen[chosen].CardId;

            // The first selection happens inside the nested 6.9.5b breach
            // boundary itself; every later selection needs its own 7.5.4
            // candidate-selection window.
            bool hasSelectionWindow = windows.Any(
                window => window.Index > previousBoundary && window.Index < chosenIndex
                    && (window.Payload.Step == RunTimingStep.BreachCandidateSelection
                        || (chosen == 0 && window.Payload.Step == RunTimingStep.BreachBoundary)));
            if (!hasSelectionWindow)
            {
                problems.Add($"breach candidate '{cardId}' was chosen without a preceding 7.5.4 candidate-selection window.");
                continue;
            }

            int nextCandidateIndex = chosen + 1 < candidatesChosen.Count
                ? candidatesChosen[chosen + 1].Index
                : int.MaxValue;
            if (endIndex >= 0)
            {
                nextCandidateIndex = Math.Min(nextCandidateIndex, endIndex);
            }
            List<(int Index, string CardId)> accessesForCandidate = cardsAccessed
                .Where(entry => entry.Index > chosenIndex && entry.Index < nextCandidateIndex)
                .ToList();
            if (accessesForCandidate.Count == 0)
            {
                problems.Add($"the chosen breach candidate '{cardId}' was never accessed (CR 26.03 7.1).");
                continue;
            }

            if (accessesForCandidate.Count > 1)
            {
                problems.Add(
                    $"breach candidate '{cardId}' has {accessesForCandidate.Count} CardAccessed events before the next candidate boundary; "
                    + "each candidate must have exactly one access segment (CR 26.03 7.1).");
            }

            (int Index, string CardId) accessed = accessesForCandidate[0];
            if (!string.Equals(accessed.CardId, cardId, StringComparison.Ordinal))
            {
                problems.Add(
                    $"breach candidate '{cardId}' was followed by CardAccessed for '{accessed.CardId}'; "
                    + "the accessed card must match the chosen candidate (CR 26.03 7.1).");
            }

            int nextAccessIndex = cardsAccessed
                .Where(entry => entry.Index > accessed.Index)
                .Select(entry => entry.Index)
                .DefaultIfEmpty(int.MaxValue)
                .First();
            int boundary = Math.Min(nextCandidateIndex, nextAccessIndex);
            if (endIndex >= 0)
            {
                boundary = Math.Min(boundary, endIndex);
            }

            accessSegments.Add((chosenIndex, accessed.Index, accessed.CardId, boundary));
        }

        foreach ((int Index, string CardId) accessed in cardsAccessed)
        {
            if (!accessSegments.Any(segment => segment.AccessIndex == accessed.Index))
            {
                problems.Add(
                    $"CardAccessed for '{accessed.CardId}' at stream index {accessed.Index} is not associated with exactly one "
                    + "chosen-candidate access segment (CR 26.03 7.1).");
            }
        }

        foreach ((int CandidateIndex, int AccessIndex, string CardId, int Boundary) segment in accessSegments)
        {
            int accessWindowCount = accessWindows.Count(index =>
                index > segment.AccessIndex
                && index < segment.Boundary);
            if (accessWindowCount != 1)
            {
                problems.Add(
                    $"CardAccessed for '{segment.CardId}' at stream index {segment.AccessIndex} has {accessWindowCount} "
                    + $"BreachAccessAbility window(s); exactly one is required before the next candidate/access/end boundary "
                    + "(CR 26.03 7.2.2).");
            }

            List<(int Index, string Kind, string CardId)> effects = accessEffects
                .Where(effect => effect.Index > segment.AccessIndex && effect.Index < segment.Boundary)
                .ToList();
            if (effects.Count > 1)
            {
                problems.Add(
                    $"CardAccessed for '{segment.CardId}' has {effects.Count} access effects in one segment; "
                    + "an access resolves at most one matching steal/trash effect.");
            }

            foreach ((int Index, string Kind, string CardId) effect in effects)
            {
                if (!string.Equals(effect.CardId, segment.CardId, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"access effect '{effect.Kind}' at stream index {effect.Index} names card '{effect.CardId}', "
                        + $"but the segment accessed '{segment.CardId}' (CR 26.03 7.1/7.2).");
                }
            }
        }

        foreach (int accessWindowIndex in accessWindows)
        {
            if (!accessSegments.Any(segment =>
                accessWindowIndex > segment.AccessIndex
                && accessWindowIndex < segment.Boundary))
            {
                problems.Add(
                    $"BreachAccessAbility window at stream index {accessWindowIndex} is not bounded by a CardAccessed "
                    + "segment (CR 26.03 7.2.2).");
            }
        }

        foreach ((int Index, string Kind, string CardId) effect in accessEffects)
        {
            if (!accessSegments.Any(segment =>
                effect.Index > segment.AccessIndex
                && effect.Index < segment.Boundary))
            {
                problems.Add(
                    $"access effect '{effect.Kind}' at stream index {effect.Index} is outside every CardAccessed segment.");
            }
        }

        // 5. Run-scoped hosted credits (Overclock, CR 26.03 1.10.4): the pool
        //    ledger must never go negative and must reach zero by run end.
        //    Hosted run credits leave with their card at cleanup; they are not
        //    wallet credits and are never a credit-loss source.
        var creditLedger = runEvents
            .Where(entry => entry.Event.Kind == EngineEventKinds.RunCreditsAdjusted)
            .Select(entry => ((RunCreditsAdjustedEventPayload)entry.Event.Payload).RunCreditsAfter)
            .ToList();
        for (int ledger = 0; ledger < creditLedger.Count; ledger++)
        {
            if (creditLedger[ledger] < 0)
            {
                problems.Add($"the run credit pool went negative ({creditLedger[ledger]}) in the event ledger.");
            }
        }

        if (creditLedger.Count > 0 && creditLedger[^1] != 0)
        {
            problems.Add(
                $"the run ended with {creditLedger[^1]} unspent hosted run credit(s); they leave with Overclock at run end (CR 26.03 6.9.6b/1.10.4).");
        }

        // 6. Priority alternation (CR 26.03 9.2): every pass must happen
        //    while a paid window is open, be made by the current priority
        //    holder, alternate holders, and close the window at the second
        //    consecutive pass.
        CheckPriorityAlternation(runEvents, problems);

        var entries = runEvents.Select(entry => BuildEntry(entry.Index, entry.Event)).ToList();
        return new RunTimingTrace(entries, problems);
    }

    private static int NextWindow(
        List<(int Index, RunTimingWindowOpenedEventPayload Payload)> windows,
        int after,
        int before,
        RunTimingStep step)
    {
        return windows
            .Where(window => window.Index > after && window.Index < before && window.Payload.Step == step)
            .Select(window => window.Index)
            .DefaultIfEmpty(-1)
            .First();
    }

    /// <summary>
    /// CR 26.03 9.2 priority contract for run paid windows (a documented
    /// simplification of the 9.2.7 passing rules), checked as a state machine
    /// over the run events in stream order: every paid window
    /// opens with the Runner holding priority (GameEngine.CreatePriority); a
    /// pass is legal only while a paid window is open, only by the current
    /// priority holder, and only with the next consecutive-pass count; the
    /// holder alternates on every pass; a Corp rez (CR 26.03 8.1) is legal
    /// only while the Corp holds priority in the 6.9.2b approach window and
    /// restarts the counter at zero with priority kept by the Corp; and a
    /// paid window closes only at the second consecutive pass, so no other
    /// window may open — and the run may not end — while one is still open.
    /// The only eventless first yield is the run's initiation window
    /// (BeginRunWithEvents passes emitWindowEvent: false); every other window
    /// emits on its first yield and re-emits on each re-yield. So a pass may
    /// legitimately precede the window's first event only for the initiation
    /// window, and a re-emitted window event must carry the current holder.
    /// </summary>
    private static void CheckPriorityAlternation(
        List<(int Index, EngineEvent Event)> runEvents,
        List<string> problems)
    {
        RunTimingStep? openStep = null;
        PlayerSide holder = PlayerSide.Runner;
        int consecutivePasses = 0;

        foreach (var (_, @event) in runEvents)
        {
            switch (@event.Kind)
            {
                case EngineEventKinds.RunInitiated:
                    if (openStep is not null)
                    {
                        problems.Add(
                            $"a run was initiated while the {openStep} window was still open (CR 26.03 6.9.1e/9.2).");
                    }

                    openStep = RunTimingStep.InitiationPaidWindow;
                    holder = PlayerSide.Runner;
                    consecutivePasses = 0;
                    break;

                case EngineEventKinds.RunTimingWindowOpened:
                    RunTimingWindowOpenedEventPayload window = (RunTimingWindowOpenedEventPayload)@event.Payload;
                    if (window.PriorityWindow is not null)
                    {
                        if (openStep is not null && openStep != window.Step)
                        {
                            problems.Add(
                                $"the {window.Step} window opened while the {openStep} window was still open (CR 26.03 9.2).");
                        }

                        if (openStep is not null && openStep == window.Step)
                        {
                            // Re-yield of the same logical window: the
                            // re-emitted holder must be the current holder.
                            if (window.PriorityHolder is not null && window.PriorityHolder.Value != holder)
                            {
                                problems.Add(
                                    $"the {window.Step} window re-emitted with priority holder {window.PriorityHolder.Value}, "
                                    + $"but the current holder was {holder} (CR 26.03 9.2).");
                            }
                        }
                        else
                        {
                            // A new logical window opens (its first yield
                            // emits no window event): it opens with the
                            // Runner holding priority.
                            openStep = window.Step;
                            holder = PlayerSide.Runner;
                            consecutivePasses = 0;
                            if (window.PriorityHolder is not null && window.PriorityHolder.Value != PlayerSide.Runner)
                            {
                                problems.Add(
                                    $"the {window.Step} window opened with priority holder {window.PriorityHolder.Value}, "
                                    + $"expected {PlayerSide.Runner} (CR 26.03 9.2).");
                            }
                        }
                    }
                    else if (openStep is not null)
                    {
                        problems.Add(
                            $"the {window.Step} decision window opened while the {openStep} paid window was still open (CR 26.03 9.2).");
                    }
                    break;

                case EngineEventKinds.RunPriorityPassed:
                    RunPriorityPassedEventPayload pass = (RunPriorityPassedEventPayload)@event.Payload;
                    if (openStep is null)
                    {
                        problems.Add(
                            $"priority was passed for the '{pass.Window}' window while no paid window was open (CR 26.03 9.2).");
                        break;
                    }

                    if (pass.Window != PriorityWindowKindFor(openStep.Value))
                    {
                        problems.Add(
                            $"a priority pass for the '{pass.Window}' window was made while the {openStep} window was open (CR 26.03 9.2).");
                    }

                    if (pass.PassedBy != holder)
                    {
                        problems.Add(
                            $"priority in the {openStep} window was passed by {pass.PassedBy} but the priority holder was {holder} (CR 26.03 9.2).");
                    }

                    if (pass.ConsecutivePasses != consecutivePasses + 1)
                    {
                        problems.Add(
                            $"the {openStep} window pass reports {pass.ConsecutivePasses} consecutive pass(es), expected {consecutivePasses + 1} (CR 26.03 9.2).");
                    }

                    if (pass.NextPriorityHolder != OtherSide(pass.PassedBy))
                    {
                        problems.Add(
                            $"the {openStep} window pass hands priority to {pass.NextPriorityHolder}, expected {OtherSide(pass.PassedBy)} (CR 26.03 9.2).");
                    }

                    consecutivePasses = pass.ConsecutivePasses;
                    holder = pass.NextPriorityHolder;
                    if (pass.ConsecutivePasses >= 2)
                    {
                        openStep = null;
                    }
                    break;

                case EngineEventKinds.IceRezzed:
                    if (openStep != RunTimingStep.ApproachPaidWindow || holder != PlayerSide.Corp)
                    {
                        problems.Add("ICE was rezzed outside the Corp's 6.9.2b approach priority window (CR 26.03 8.1/6.9.2b).");
                    }
                    else
                    {
                        // CR 26.03 8.1: the rez restarts the pass counter
                        // and keeps priority with the Corp.
                        consecutivePasses = 0;
                        holder = PlayerSide.Corp;
                    }
                    break;

                case EngineEventKinds.RunEnded:
                    if (openStep is not null)
                    {
                        problems.Add($"the run ended while the {openStep} window was still open (CR 26.03 6.9.6/9.2).");
                    }

                    openStep = null;
                    break;
            }
        }

        if (openStep is not null)
        {
            problems.Add($"the stream ended while the {openStep} window was still open (CR 26.03 9.2).");
        }
    }

    private static RunPriorityWindowKind PriorityWindowKindFor(RunTimingStep step) => step switch
    {
        RunTimingStep.InitiationPaidWindow => RunPriorityWindowKind.InitiationPaid,
        RunTimingStep.ApproachPaidWindow => RunPriorityWindowKind.ApproachPaid,
        RunTimingStep.EncounterPaidWindow => RunPriorityWindowKind.EncounterPaid,
        RunTimingStep.MovementPaidWindow => RunPriorityWindowKind.MovementPaid,
        RunTimingStep.AfterMovePaidWindow => RunPriorityWindowKind.AfterMovePaid,
        _ => throw new ArgumentException($"Step '{step}' is not a paid window.", nameof(step)),
    };

    /// <summary>
    /// The run ID carried by a run-related event payload. agenda-stolen is
    /// the one run-related kind without a run ID (the steal is a game-level
    /// effect) and reports false here.
    /// </summary>
    private static bool TryGetRunId(EngineEventPayload payload, out RunId runId)
    {
        switch (payload)
        {
            case RunInitiatedEventPayload initiated:
                runId = initiated.RunId;
                return true;
            case RunEventPlayedEventPayload played:
                runId = played.RunId;
                return true;
            case RunTimingWindowOpenedEventPayload window:
                runId = window.RunId;
                return true;
            case IceApproachedEventPayload approached:
                runId = approached.RunId;
                return true;
            case IceRezzedEventPayload iceRezzed:
                runId = iceRezzed.RunId;
                return true;
            case SubroutinesBrokenEventPayload broken:
                runId = broken.RunId;
                return true;
            case BreakerPumpedEventPayload pumped:
                runId = pumped.RunId;
                return true;
            case EncounterInitializedEventPayload encounterInitialized:
                runId = encounterInitialized.RunId;
                return true;
            case EncounterCompletedEventPayload completed:
                runId = completed.RunId;
                return true;
            case RunMovedEventPayload moved:
                runId = moved.RunId;
                return true;
            case RunSuccessfulEventPayload successful:
                runId = successful.RunId;
                return true;
            case BreachStartedEventPayload breach:
                runId = breach.RunId;
                return true;
            case BreachCandidateChosenEventPayload chosen:
                runId = chosen.RunId;
                return true;
            case CardAccessedEventPayload accessed:
                runId = accessed.RunId;
                return true;
            case AccessedCardTrashedEventPayload trashed:
                runId = trashed.RunId;
                return true;
            case RunCreditsAdjustedEventPayload credits:
                runId = credits.RunId;
                return true;
            case RunPriorityPassedEventPayload passed:
                runId = passed.RunId;
                return true;
            case RunEndedEventPayload ended:
                runId = ended.RunId;
                return true;
            default:
                runId = default;
                return false;
        }
    }

    private static PlayerSide OtherSide(PlayerSide side) => side is PlayerSide.Corp
        ? PlayerSide.Runner
        : PlayerSide.Corp;


    private static RunTimingTraceEntry BuildEntry(int index, EngineEvent engineEvent)
    {
        string detail = engineEvent.Payload switch
        {
            RunTimingWindowOpenedEventPayload window => $"{window.Step} (priority window: {window.PriorityWindow?.ToString().ToLowerInvariant() ?? "none"}, holder: {window.PriorityHolder?.ToString().ToLowerInvariant() ?? "none"})",
            IceApproachedEventPayload approached => $"approached ICE '{approached.IceId.Value}'",
            IceRezzedEventPayload rezzed => $"rezzed ICE '{rezzed.IceId.Value}'",
            EncounterInitializedEventPayload initialized => $"encounter initialized against '{initialized.IceId.Value}'",
            BreakerPumpedEventPayload pumped => $"pumped breaker '{pumped.BreakerId.Value}'",
            SubroutinesBrokenEventPayload broken => $"broke subroutines with '{broken.BreakerId.Value}'",
            RunSuccessfulEventPayload successful => $"the run reached server '{successful.ServerId.Value}'",
            BreachStartedEventPayload breach => $"the breach has {breach.CandidateCount} candidate(s)",
            BreachCandidateChosenEventPayload chosen => $"chose candidate '{chosen.CardId.Value}'",
            CardAccessedEventPayload accessed => $"accessed '{accessed.CardId.Value}'",
            RunCreditsAdjustedEventPayload credits => $"run credits {credits.Delta:+#;-#;0} (pool now {credits.RunCreditsAfter})",
            RunPriorityPassedEventPayload passed => $"priority passed by {passed.PassedBy.ToString().ToLowerInvariant()} (consecutive passes: {passed.ConsecutivePasses})",
            RunEndedEventPayload ended => $"the run ended ({ended.Reason}, successful: {ended.Successful.ToString().ToLowerInvariant()})",
            RunInitiatedEventPayload initiated => $"run initiated on server '{initiated.ServerId.Value}'",
            RunEventPlayedEventPayload played => $"played run event '{played.EventCardId.Value}' ({played.DefinitionId.Value}, cost {played.Cost}, hosted {played.HostedCredits} run credits)",
            EncounterCompletedEventPayload completed => $"encounter '{completed.EncounterId.Value}' completed",
            RunMovedEventPayload moved => $"run moved from ice {moved.FromIceIndex} to ice {moved.ToIceIndex}",
            AgendaStolenEventPayload stolen => $"stole agenda '{stolen.AgendaId.Value}' for {stolen.AgendaPoints} agenda point(s)",
            AccessedCardTrashedEventPayload trashed => $"trashed accessed '{trashed.CardId.Value}' for {trashed.TrashCost} credit(s)",
            _ => string.Empty,
        };

        string citation = engineEvent.Kind == EngineEventKinds.RunTimingWindowOpened
            ? RunTimingCitations.ForWindow(((RunTimingWindowOpenedEventPayload)engineEvent.Payload).Step)
            : RunTimingCitations.ForEvent(engineEvent.Kind);

        return new RunTimingTraceEntry(index, engineEvent.Sequence, engineEvent.Kind, detail, citation);
    }
}
