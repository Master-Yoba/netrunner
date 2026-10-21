using System.Globalization;
using System.Text;

namespace Netrunner.Core;

/// <summary>One cited event in the ENG-401 CR turn trace.</summary>
public sealed record TurnTimingTraceEntry(
    int Index,
    long Sequence,
    string EventKind,
    string Detail,
    string Citation);

/// <summary>
/// Review-friendly evidence for the serializable CR 5 turn shell. The trace
/// validator is intentionally independent of card behavior and can therefore
/// be used by SCN-400 before ENG-402 action implementations land.
/// </summary>
public sealed class TurnTimingTrace
{
    internal TurnTimingTrace(IReadOnlyList<TurnTimingTraceEntry> entries, IReadOnlyList<string> problems)
    {
        Entries = entries;
        Problems = problems;
    }

    public IReadOnlyList<TurnTimingTraceEntry> Entries { get; }

    public IReadOnlyList<string> Problems { get; }

    public bool IsValid => Problems.Count == 0;

    public string RenderText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Turn timing trace (CR 26.03):");
        for (int index = 0; index < Entries.Count; index++)
        {
            TurnTimingTraceEntry entry = Entries[index];
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"  {index + 1,3}. [seq {entry.Sequence}] {entry.EventKind} — {entry.Detail} ({entry.Citation})");
        }

        if (Problems.Count == 0)
        {
            builder.AppendLine("  Result: no CR 26.03 turn-timing problems found.");
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

/// <summary>CR 26.03 citations for turn events and paid windows.</summary>
public static class TurnTimingCitations
{
    public static string ForEvent(string kind) => kind switch
    {
        EngineEventKinds.TurnClicksAllotted => "CR 26.03 5.6.1a/5.7.1a: the active player receives the allotted clicks",
        EngineEventKinds.TurnTimingWindowOpened => "CR 26.03 9.2.1/9.2.2: a paid-ability timing window opens",
        EngineEventKinds.TurnPriorityPassed => "CR 26.03 9.2.7: priority passes between players inside a paid-ability window",
        EngineEventKinds.TurnStarted => "CR 26.03 5.6.1d/5.7.1d: the turn formally begins",
        EngineEventKinds.TurnPhaseStarted => "CR 26.03 5.1.1-5.1.4: the turn phase formally begins",
        EngineEventKinds.TurnPhaseEnded => "CR 26.03 5.1.1-5.1.4: the turn phase formally ends",
        EngineEventKinds.TurnCardDiscarded => "CR 26.03 5.6.1g/5.7.1f: the active player discards down to maximum hand size",
        EngineEventKinds.TurnClicksLost => "CR 26.03 5.6.1h/5.7.1g: unspent clicks are lost at the turn boundary",
        EngineEventKinds.TurnEnded => "CR 26.03 5.6.1h/5.7.1g: the turn formally ends",
        EngineEventKinds.CardDrawn => "CR 26.03 5.3.3/8.4: an ordered draw moves the top card into the player's hand",
        _ => throw new ArgumentException($"No CR citation is defined for turn event kind '{kind}'.", nameof(kind)),
    };

    public static string ForWindow(TurnFrameStage stage, PlayerSide side) => stage switch
    {
        TurnFrameStage.BeforeTurnPriority when side == PlayerSide.Corp
            => "CR 26.03 5.6.1b: Corp pre-turn window (P)(R)(S)",
        TurnFrameStage.BeforeTurnPriority
            => "CR 26.03 5.7.1b: Runner pre-turn window (P)(R)",
        TurnFrameStage.ActionPriority when side == PlayerSide.Corp
            => "CR 26.03 5.6.1f: Corp action window (P)(R)(S)",
        TurnFrameStage.ActionPriority
            => "CR 26.03 5.7.1e: Runner action window (P)(R)",
        TurnFrameStage.DiscardPriority when side == PlayerSide.Corp
            => "CR 26.03 5.6.1g: Corp discard window (P)(R)(S)",
        TurnFrameStage.DiscardPriority
            => "CR 26.03 5.7.1f: Runner discard window (P)(R)",
        _ => throw new ArgumentException($"Turn stage '{stage}' is not a paid-ability window.", nameof(stage)),
    };

    public static string ForWindow(TurnFrameStage stage) => stage switch
    {
        TurnFrameStage.BeforeTurnPriority or TurnFrameStage.ActionPriority or TurnFrameStage.DiscardPriority
            => "CR 26.03 9.2: turn paid-ability window (P), with Corp (R)(S) capabilities where applicable",
        _ => throw new ArgumentException($"Turn stage '{stage}' is not a paid-ability window.", nameof(stage)),
    };
}

/// <summary>Validates ordering and typed payload consistency for SCN-400 traces.</summary>
public static class TurnTimingTraceValidator
{
    private static readonly HashSet<string> TurnKinds = new(
    [
        EngineEventKinds.TurnClicksAllotted,
        EngineEventKinds.TurnTimingWindowOpened,
        EngineEventKinds.TurnPriorityPassed,
        EngineEventKinds.TurnStarted,
        EngineEventKinds.TurnPhaseStarted,
        EngineEventKinds.TurnPhaseEnded,
        EngineEventKinds.TurnCardDiscarded,
        EngineEventKinds.TurnClicksLost,
        EngineEventKinds.TurnEnded,
        EngineEventKinds.CardDrawn,
    ],
    StringComparer.Ordinal);

    public static TurnTimingTrace Validate(IReadOnlyList<EngineEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var problems = new List<string>();
        var entries = new List<TurnTimingTraceEntry>();
        var filtered = events
            .Select((value, index) => (value, index))
            .Where(pair => TurnKinds.Contains(pair.value.Kind))
            .ToArray();

        foreach ((EngineEvent @event, int sourceIndex) in filtered)
        {
            IReadOnlyList<StateInvariantViolation> eventViolations = EngineEventValidator.Collect(@event, $"events[{sourceIndex}]");
            if (eventViolations.Count > 0)
            {
                problems.AddRange(eventViolations.Select(static violation => violation.ToString()));
                continue;
            }

            string detail = @event.Payload switch
            {
                TurnClicksAllottedEventPayload allot => $"{allot.Side} receives {allot.Clicks} clicks",
                TurnTimingWindowOpenedEventPayload window => $"{window.Side} opens {window.Window} at {window.Stage}",
                TurnPriorityPassedEventPayload pass => $"{pass.PassedBy} passes ({pass.ConsecutivePasses})",
                TurnStartedEventPayload started => $"{started.ActiveSide} turn {started.TurnNumber} begins",
                TurnPhaseStartedEventPayload phase => $"{phase.Side} enters {phase.Step}",
                TurnPhaseEndedEventPayload phase => $"{phase.Side} leaves {phase.Step}",
                TurnCardDiscardedEventPayload discard => $"{discard.Side} discards {discard.CardId}",
                TurnClicksLostEventPayload lost => $"{lost.Side} loses {lost.ClicksLost} leftover clicks",
                TurnEndedEventPayload ended => $"{ended.Side} turn {ended.TurnNumber} ends",
                _ => @event.Kind,
            };
            string citation = @event.Payload is TurnTimingWindowOpenedEventPayload timing
                ? ForWindowCitation(timing)
                : TurnTimingCitations.ForEvent(@event.Kind);
            entries.Add(new TurnTimingTraceEntry(entries.Count, @event.Sequence, @event.Kind, detail, citation));
        }

        long previousSequence = 0;
        foreach (TurnTimingTraceEntry entry in entries)
        {
            if (entry.Sequence <= previousSequence)
            {
                problems.Add("turn event sequences must be strictly increasing.");
            }

            previousSequence = entry.Sequence;
        }

        ValidateTurnBoundaries(filtered.Select(static pair => pair.value).ToArray(), problems);

        return new TurnTimingTrace(entries.AsReadOnly(), problems.AsReadOnly());
    }

    private static void ValidateTurnBoundaries(
        IReadOnlyList<EngineEvent> events,
        List<string> problems)
    {
        TraceCursor cursor = TraceCursor.AwaitingAllotment;
        PlayerSide activeSide = default;
        int turnNumber = 0;
        int allottedClicks = 0;
        int completedTurns = 0;
        bool formalStarted = false;
        bool mandatoryDraw = false;
        bool windowOpen = false;
        TurnFrameStage? openStage = null;
        TurnPriorityWindowKind? openWindow = null;
        PlayerSide priorityHolder = default;
        int consecutivePasses = 0;

        foreach (EngineEvent @event in events)
        {
            switch (@event.Kind)
            {
                case EngineEventKinds.TurnClicksAllotted:
                    if (@event.Payload is not TurnClicksAllottedEventPayload allot)
                    {
                        continue;
                    }

                    if (cursor is not (TraceCursor.AwaitingAllotment or TraceCursor.Completed))
                    {
                        problems.Add("turn click allotment arrived before the preceding turn boundary completed.");
                    }

                    if (completedTurns > 0)
                    {
                        PlayerSide expectedSide = activeSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
                        int expectedNumber = activeSide == PlayerSide.Runner ? turnNumber + 1 : turnNumber;
                        if (allot.Side != expectedSide || allot.TurnNumber != expectedNumber)
                        {
                            problems.Add("turn click allotments must alternate Corp/Runner and increment after the Runner turn.");
                        }
                    }

                    int expectedClicks = allot.Side == PlayerSide.Corp ? 3 : 4;
                    if (allot.Clicks != expectedClicks)
                    {
                        problems.Add($"{allot.Side} turn {allot.TurnNumber} must allot exactly {expectedClicks} clicks.");
                    }

                    activeSide = allot.Side;
                    turnNumber = allot.TurnNumber;
                    allottedClicks = allot.Clicks;
                    formalStarted = false;
                    mandatoryDraw = false;
                    windowOpen = false;
                    openStage = null;
                    openWindow = null;
                    consecutivePasses = 0;
                    cursor = TraceCursor.AwaitingPreWindow;
                    break;

                case EngineEventKinds.TurnTimingWindowOpened:
                    if (@event.Payload is not TurnTimingWindowOpenedEventPayload window)
                    {
                        continue;
                    }

                    RequireActiveTurn(window.Side, window.TurnNumber, activeSide, turnNumber, cursor, problems, "timing window");
                    if (windowOpen)
                    {
                        problems.Add("a turn timing window cannot open before the preceding window has closed.");
                        break;
                    }

                    if (window.Stage == TurnFrameStage.BeforeTurnPriority)
                    {
                        if (cursor != TraceCursor.AwaitingPreWindow)
                        {
                            problems.Add("the pre-turn timing window must open immediately after click allotment.");
                        }
                    }
                    else if (window.Stage == TurnFrameStage.ActionPriority)
                    {
                        if (cursor is not (TraceCursor.AwaitingActionWindow or TraceCursor.ActionProgress))
                        {
                            problems.Add("an action timing window requires formal turn begin and the action phase.");
                        }
                    }
                    else if (window.Stage == TurnFrameStage.DiscardPriority)
                    {
                        if (cursor != TraceCursor.AwaitingDiscardWindow)
                        {
                            problems.Add("the discard timing window must follow the action phase and discard setup.");
                        }
                    }
                    else
                    {
                        problems.Add("turn timing windows may only open at pre-turn, action, or discard stages.");
                    }

                    TurnPriorityWindowKind expectedWindow = activeSide == PlayerSide.Corp
                        ? TurnPriorityWindowKind.PaidRezAndScore
                        : TurnPriorityWindowKind.PaidAndRez;
                    if (window.Window != expectedWindow)
                    {
                        problems.Add($"{activeSide} timing windows must use '{expectedWindow}'.");
                    }

                    if (window.Side != activeSide || window.PriorityHolder != activeSide)
                    {
                        problems.Add("a turn timing window must belong to the active side and open with that side's priority.");
                    }

                    windowOpen = true;
                    openStage = window.Stage;
                    openWindow = window.Window;
                    priorityHolder = window.PriorityHolder;
                    consecutivePasses = 0;
                    cursor = window.Stage switch
                    {
                        TurnFrameStage.BeforeTurnPriority => TraceCursor.PreWindow,
                        TurnFrameStage.ActionPriority => TraceCursor.ActionWindow,
                        TurnFrameStage.DiscardPriority => TraceCursor.DiscardWindow,
                        _ => cursor,
                    };
                    break;

                case EngineEventKinds.TurnPriorityPassed:
                    if (@event.Payload is not TurnPriorityPassedEventPayload pass)
                    {
                        continue;
                    }

                    RequireActiveTurn(pass.Side, pass.TurnNumber, activeSide, turnNumber, cursor, problems, "priority pass");
                    if (!windowOpen || openStage is null || openWindow is null)
                    {
                        problems.Add("a priority pass requires an open turn timing window.");
                        break;
                    }

                    if (pass.Window != openWindow.Value
                        || pass.PassedBy != priorityHolder
                        || pass.ConsecutivePasses != consecutivePasses + 1
                        || pass.ConsecutivePasses > 2
                        || pass.NextPriorityHolder == pass.PassedBy)
                    {
                        problems.Add("turn priority passes must alternate holders and close exactly after two consecutive passes.");
                    }

                    consecutivePasses = Math.Max(consecutivePasses, pass.ConsecutivePasses);
                    priorityHolder = pass.NextPriorityHolder;
                    if (consecutivePasses >= 2)
                    {
                        windowOpen = false;
                        TurnFrameStage closedStage = openStage.Value;
                        openStage = null;
                        openWindow = null;
                        cursor = closedStage switch
                        {
                            TurnFrameStage.BeforeTurnPriority => TraceCursor.AwaitingFormalBegin,
                            TurnFrameStage.ActionPriority => TraceCursor.ActionProgress,
                            TurnFrameStage.DiscardPriority => TraceCursor.AwaitingClicksLost,
                            _ => cursor,
                        };
                    }

                    break;

                case EngineEventKinds.TurnStarted:
                    if (@event.Payload is not TurnStartedEventPayload started)
                    {
                        continue;
                    }

                    RequireActiveTurn(started.ActiveSide, started.TurnNumber, activeSide, turnNumber, cursor, problems, "formal turn begin");
                    if (cursor != TraceCursor.AwaitingFormalBegin || formalStarted)
                    {
                        problems.Add("formal turn begin must follow the closed pre-turn window exactly once.");
                    }

                    if (started.Clicks != allottedClicks)
                    {
                        problems.Add("formal turn begin must report the clicks allotted for that turn.");
                    }

                    formalStarted = true;
                    cursor = TraceCursor.AwaitingInitialPhase;
                    break;

                case EngineEventKinds.TurnPhaseStarted:
                    if (@event.Payload is not TurnPhaseStartedEventPayload phaseStarted)
                    {
                        continue;
                    }

                    RequireActiveTurn(phaseStarted.Side, phaseStarted.TurnNumber, activeSide, turnNumber, cursor, problems, "phase begin");
                    if (cursor == TraceCursor.AwaitingInitialPhase)
                    {
                        TurnStep expectedInitial = activeSide == PlayerSide.Corp ? TurnStep.CorpDraw : TurnStep.RunnerAction;
                        if (phaseStarted.Step != expectedInitial)
                        {
                            problems.Add($"{activeSide} formal turn begin must enter '{expectedInitial}'.");
                        }

                        cursor = activeSide == PlayerSide.Corp
                            ? TraceCursor.AwaitingMandatoryDraw
                            : TraceCursor.AwaitingActionWindow;
                    }
                    else if (cursor == TraceCursor.AwaitingDiscardPhase)
                    {
                        TurnStep expectedDiscard = activeSide == PlayerSide.Corp ? TurnStep.CorpDiscard : TurnStep.RunnerDiscard;
                        if (phaseStarted.Step != expectedDiscard)
                        {
                            problems.Add($"{activeSide} action phase must enter '{expectedDiscard}'.");
                        }

                        cursor = TraceCursor.AwaitingDiscardWindow;
                    }
                    else
                    {
                        problems.Add("turn phase boundaries must be ordered around formal begin, action end, and discard end.");
                    }

                    break;

                case EngineEventKinds.CardDrawn:
                    if (@event.Payload is not CardDrawnEventPayload drawn)
                    {
                        continue;
                    }

                    if (cursor == TraceCursor.AwaitingMandatoryDraw)
                    {
                        if (activeSide != PlayerSide.Corp || drawn.Side != PlayerSide.Corp)
                        {
                            problems.Add("only Corp may satisfy the mandatory draw at formal turn begin.");
                        }
                        else
                        {
                            mandatoryDraw = true;
                            cursor = TraceCursor.AwaitingMandatoryDrawEnd;
                        }
                    }
                    else if (cursor == TraceCursor.ActionProgress)
                    {
                        if (drawn.Side != activeSide)
                        {
                            problems.Add("a voluntary turn draw must belong to the active side.");
                        }
                    }
                    else
                    {
                        problems.Add(activeSide == PlayerSide.Runner
                            ? "Runner has no mandatory draw at formal turn begin."
                            : "Corp mandatory draw must occur before the first action window.");
                    }

                    break;

                case EngineEventKinds.TurnPhaseEnded:
                    if (@event.Payload is not TurnPhaseEndedEventPayload phaseEnded)
                    {
                        continue;
                    }

                    RequireActiveTurn(phaseEnded.Side, phaseEnded.TurnNumber, activeSide, turnNumber, cursor, problems, "phase end");
                    if (cursor == TraceCursor.AwaitingMandatoryDrawEnd)
                    {
                        if (!mandatoryDraw || phaseEnded.Step != TurnStep.CorpDraw)
                        {
                            problems.Add("Corp mandatory draw must complete before CorpDraw phase end.");
                        }
                        cursor = TraceCursor.AwaitingActionWindow;
                    }
                    else if (cursor == TraceCursor.ActionProgress)
                    {
                        TurnStep expectedAction = activeSide == PlayerSide.Corp ? TurnStep.CorpAction : TurnStep.RunnerAction;
                        if (phaseEnded.Step != expectedAction)
                        {
                            problems.Add("the action phase must end with the active side's action step.");
                        }
                        cursor = TraceCursor.AwaitingDiscardPhase;
                    }
                    else if (cursor == TraceCursor.AwaitingDiscardEnd)
                    {
                        TurnStep expectedDiscard = activeSide == PlayerSide.Corp ? TurnStep.CorpDiscard : TurnStep.RunnerDiscard;
                        if (phaseEnded.Step != expectedDiscard)
                        {
                            problems.Add("the discard phase must end with the active side's discard step.");
                        }
                        cursor = TraceCursor.AwaitingTurnEnd;
                    }
                    else
                    {
                        problems.Add("turn phase end arrived without its corresponding open phase.");
                    }

                    break;

                case EngineEventKinds.TurnCardDiscarded:
                    if (@event.Payload is not TurnCardDiscardedEventPayload discarded)
                    {
                        continue;
                    }

                    RequireActiveTurn(discarded.Side, discarded.TurnNumber, activeSide, turnNumber, cursor, problems, "discard");
                    if (cursor != TraceCursor.AwaitingDiscardWindow)
                    {
                        problems.Add("forced hand-size discards must occur after action end and before the discard window.");
                    }
                    break;

                case EngineEventKinds.TurnClicksLost:
                    if (@event.Payload is not TurnClicksLostEventPayload lost)
                    {
                        continue;
                    }

                    RequireActiveTurn(lost.Side, lost.TurnNumber, activeSide, turnNumber, cursor, problems, "click loss");
                    if (cursor != TraceCursor.AwaitingClicksLost)
                    {
                        problems.Add("leftover clicks must be lost after the discard priority window closes.");
                    }
                    cursor = TraceCursor.AwaitingDiscardEnd;
                    break;

                case EngineEventKinds.TurnEnded:
                    if (@event.Payload is not TurnEndedEventPayload ended)
                    {
                        continue;
                    }

                    RequireActiveTurn(ended.Side, ended.TurnNumber, activeSide, turnNumber, cursor, problems, "formal turn end");
                    if (cursor != TraceCursor.AwaitingTurnEnd || !formalStarted)
                    {
                        problems.Add("formal turn end requires completed action, discard, and leftover-click boundaries.");
                    }

                    completedTurns++;
                    cursor = TraceCursor.Completed;
                    break;
            }
        }

        if (completedTurns == 0)
        {
            problems.Add("a turn timing trace must contain at least one complete formal turn.");
        }

        if (cursor is TraceCursor.AwaitingAllotment or TraceCursor.Completed)
        {
            return;
        }

        // EndFormalTurn advances the cursor into the next turn in the same
        // atomic batch. A trace may therefore contain the next allotment and
        // opening window without yet containing that next turn's decisions.
        if (completedTurns > 0
            && cursor == TraceCursor.PreWindow
            && windowOpen
            && consecutivePasses == 0
            && activeSide is PlayerSide.Corp or PlayerSide.Runner)
        {
            return;
        }

        problems.Add("turn timing trace ended before all formal boundaries and windows were completed.");
    }

    private static void RequireActiveTurn(
        PlayerSide side,
        int number,
        PlayerSide activeSide,
        int activeNumber,
        TraceCursor cursor,
        List<string> problems,
        string boundary)
    {
        if (cursor == TraceCursor.AwaitingAllotment)
        {
            problems.Add($"{boundary} requires a preceding click allotment.");
        }

        if (side != activeSide || number != activeNumber)
        {
            problems.Add($"{boundary} side and turn number must match the current turn.");
        }
    }

    private enum TraceCursor
    {
        AwaitingAllotment,
        AwaitingPreWindow,
        PreWindow,
        AwaitingFormalBegin,
        AwaitingInitialPhase,
        AwaitingMandatoryDraw,
        AwaitingMandatoryDrawEnd,
        AwaitingActionWindow,
        ActionWindow,
        ActionProgress,
        AwaitingDiscardPhase,
        AwaitingDiscardWindow,
        DiscardWindow,
        AwaitingClicksLost,
        AwaitingDiscardEnd,
        AwaitingTurnEnd,
        Completed,
    }

    private static string ForWindowCitation(TurnTimingWindowOpenedEventPayload window) =>
        TurnTimingCitations.ForWindow(window.Stage, window.Side);
}
