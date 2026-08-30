namespace Netrunner.Core;

/// <summary>
/// Generic M2 checkpoint/replacement reducer.  The registry is deliberately
/// small and data driven: registrations identify a typed condition and a
/// frozen effect bundle, while all executable behavior remains in this
/// deterministic reducer.
/// </summary>
public static partial class GameEngine
{
    private static void StepEffectCheckpoint(WorkingState working, EffectCheckpointFrame frame)
    {
        switch (frame.Stage)
        {
            case EffectCheckpointStage.InterruptChoice:
                StepInterruptCheckpoint(working, frame);
                return;
            case EffectCheckpointStage.Resolve:
                ResolveImminentEffect(working, frame);
                return;
            case EffectCheckpointStage.TriggerOrdering:
                StepTriggerOrdering(working, frame);
                return;
            case EffectCheckpointStage.TriggerChoice:
                StepTriggerChoice(working, frame);
                return;
            case EffectCheckpointStage.AwaitingChild:
                ResumeCheckpointChild(working, frame);
                return;
            default:
                throw new ResolutionFailureException($"Unsupported effect checkpoint stage '{frame.Stage}'.");
        }
    }

    private static void StepInterruptCheckpoint(WorkingState working, EffectCheckpointFrame frame)
    {
        if (frame.InterruptCursor >= frame.InterruptQueue.Count
            || CurrentPreventableAmount(frame.CurrentEffect) == 0
                && frame.InterruptQueue.Skip(frame.InterruptCursor).All(static value => value.Kind == InterruptKind.Prevention))
        {
            ReplaceCheckpoint(working, frame, EffectCheckpointStage.Resolve);
            return;
        }

        InterruptInvocation invocation = frame.InterruptQueue[frame.InterruptCursor];
        if (invocation.Kind == InterruptKind.Prevention && CurrentPreventableAmount(frame.CurrentEffect) == 0)
        {
            ReplaceCheckpoint(working, frame, frame.Stage, interruptCursor: frame.InterruptCursor + 1);
            return;
        }

        YieldInterruptDecision(working, frame, invocation);
    }

    private static void YieldInterruptDecision(
        WorkingState working,
        EffectCheckpointFrame frame,
        InterruptInvocation invocation)
    {
        var actions = new List<LegalAction>();
        switch (invocation.Kind)
        {
            case InterruptKind.Prevention:
                int maximum = Math.Min(invocation.MaximumPreventionAmount, CurrentPreventableAmount(frame.CurrentEffect));
                if (maximum <= 0)
                {
                    throw new ResolutionFailureException(
                        $"Prevention interrupt '{invocation.Id}' has no remaining preventable amount.");
                }

                for (int amount = 1; amount <= maximum; amount++)
                {
                    actions.Add(new LegalAction(
                        working.Allocator.NextActionId(),
                        ActionKind.UsePrevention,
                        $"Prevent {amount}",
                        new PreventionChoiceParameters(invocation.Id, amount)));
                }

                if (!invocation.Mandatory)
                {
                    actions.Add(new LegalAction(
                        working.Allocator.NextActionId(),
                        ActionKind.DeclinePrevention,
                        "Decline prevention",
                        new PreventionChoiceParameters(invocation.Id, 0)));
                }

                break;
            case InterruptKind.Replacement:
                actions.Add(new LegalAction(
                    working.Allocator.NextActionId(),
                    ActionKind.UseReplacement,
                    "Use replacement",
                    new ReplacementChoiceParameters(invocation.Id)));
                if (!invocation.Mandatory)
                {
                    actions.Add(new LegalAction(
                        working.Allocator.NextActionId(),
                        ActionKind.DeclineReplacement,
                        "Decline replacement",
                        new ReplacementChoiceParameters(invocation.Id)));
                }

                break;
            default:
                throw new ResolutionFailureException($"Unsupported interrupt kind '{invocation.Kind}'.");
        }

        if (actions.Count == 0)
        {
            throw new ResolutionFailureException($"Interrupt '{invocation.Id}' produced no legal actions.");
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            invocation.Controller,
            DecisionKind.InterruptChoice,
            invocation.Kind == InterruptKind.Prevention
                ? "Choose whether and how much to prevent"
                : "Choose whether to replace the imminent effect",
            actions);
    }

    private static void ResolveImminentEffect(WorkingState working, EffectCheckpointFrame frame)
    {
        if (frame.CurrentEffect is DamageEffect { Amount: 0 })
        {
            // A fully prevented damage instruction has no committed damage
            // occurrence.  Complete this checkpoint without collecting a
            // post-effect trigger for a zero amount.
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        if (frame.CurrentEffect is not DamageEffect { Amount: 0 })
        {
            ApplyEffect(working, frame.CurrentEffect);
        }

        if (working.Outcome.Kind == GameOutcomeKind.Won)
        {
            return;
        }

        OccurrenceSnapshot resolved = WithPhase(frame.Occurrence, OccurrencePhase.Resolved, frame.CurrentEffect);
        TriggerInvocation[] triggers = MaterializeTriggers(working, resolved, frame);
        if (triggers.Length == 0)
        {
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        ReplaceCheckpoint(
            working,
            frame,
            triggers.Length > 1 ? EffectCheckpointStage.TriggerOrdering : EffectCheckpointStage.TriggerChoice,
            triggerQueue: triggers,
            triggerCursor: 0,
            resolvedOccurrence: resolved);
    }

    private static void StepTriggerOrdering(WorkingState working, EffectCheckpointFrame frame)
    {
        int remaining = frame.TriggerQueue.Count - frame.TriggerCursor;
        if (remaining <= 0)
        {
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        if (remaining == 1)
        {
            ReplaceCheckpoint(working, frame, EffectCheckpointStage.TriggerChoice);
            return;
        }

        PlayerId controller = frame.TriggerQueue[frame.TriggerCursor].Controller;
        TriggerInvocation[] sameController = frame.TriggerQueue
            .Skip(frame.TriggerCursor)
            .Where(invocation => invocation.Controller == controller)
            .ToArray();
        if (sameController.Length <= 1)
        {
            ReplaceCheckpoint(working, frame, EffectCheckpointStage.TriggerChoice);
            return;
        }

        var actions = sameController
            .Select(invocation => new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.OrderTriggers,
                $"Resolve trigger {invocation.Id} next",
                new TriggerOrderParameters(invocation.Id)))
            .ToArray();
        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            controller,
            DecisionKind.TriggerOrdering,
            "Choose the next simultaneous trigger",
            actions);
    }

    private static void StepTriggerChoice(WorkingState working, EffectCheckpointFrame frame)
    {
        if (frame.TriggerCursor >= frame.TriggerQueue.Count)
        {
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        TriggerInvocation invocation = frame.TriggerQueue[frame.TriggerCursor];
        if (invocation.Mode == TriggerMode.Mandatory)
        {
            StartTriggerChild(working, frame, invocation);
            return;
        }

        var actions = new List<LegalAction>
        {
            new(
                working.Allocator.NextActionId(),
                ActionKind.UseTrigger,
                $"Use trigger {invocation.Id}",
                new TriggerChoiceParameters(invocation.Id)),
        };
        if (invocation.Mode == TriggerMode.Optional)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.DeclineTrigger,
                $"Decline trigger {invocation.Id}",
                new TriggerChoiceParameters(invocation.Id)));
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            invocation.Controller,
            DecisionKind.TriggerChoice,
            invocation.Mode == TriggerMode.Mandatory
                ? "Resolve mandatory trigger"
                : "Use or decline optional trigger",
            actions);
    }

    private static void ResumeCheckpointChild(WorkingState working, EffectCheckpointFrame frame)
    {
        if (frame.ChildKind == EffectCheckpointChildKind.Trigger)
        {
            if (frame.TriggerCursor >= frame.TriggerQueue.Count)
            {
                working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
                return;
            }

            ReplaceCheckpoint(working, frame, RemainingTriggerStage(frame));
            return;
        }

        if (frame.ChildKind == EffectCheckpointChildKind.Replacement)
        {
            // A replacement resolves in place of the original instruction.
            // Its own EffectResolutionFrame has already run all of its
            // checkpoints, so the original checkpoint can now complete.
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        throw new ResolutionFailureException("An effect checkpoint resumed without a recognized child kind.");
    }

    private static EffectCheckpointStage RemainingTriggerStage(EffectCheckpointFrame frame) =>
        frame.TriggerQueue.Count - frame.TriggerCursor > 1
            ? EffectCheckpointStage.TriggerOrdering
            : EffectCheckpointStage.TriggerChoice;

    private static void ConsumeTriggerOrdering(WorkingState working, TriggerId triggerId)
    {
        EffectCheckpointFrame frame = CurrentCheckpoint(working, EffectCheckpointStage.TriggerOrdering);
        int selected = -1;
        for (int index = frame.TriggerCursor; index < frame.TriggerQueue.Count; index++)
        {
            if (frame.TriggerQueue[index].Id == triggerId)
            {
                selected = index;
                break;
            }
        }

        if (selected < 0)
        {
            throw new ResolutionFailureException($"Trigger '{triggerId}' is not a remaining simultaneous trigger.");
        }

        TriggerInvocation[] reordered = frame.TriggerQueue
            .Take(frame.TriggerCursor)
            .Append(frame.TriggerQueue[selected])
            .Concat(frame.TriggerQueue.Skip(frame.TriggerCursor).Take(selected - frame.TriggerCursor))
            .Concat(frame.TriggerQueue.Skip(selected + 1))
            .ToArray();
        ReplaceCheckpoint(
            working,
            frame,
            EffectCheckpointStage.TriggerChoice,
            triggerQueue: reordered);
    }

    private static void ConsumeTriggerChoice(WorkingState working, ActionKind kind, TriggerId triggerId)
    {
        EffectCheckpointFrame frame = CurrentCheckpoint(working, EffectCheckpointStage.TriggerChoice);
        if (frame.TriggerCursor >= frame.TriggerQueue.Count)
        {
            throw new ResolutionFailureException("There is no remaining trigger choice.");
        }

        TriggerInvocation invocation = frame.TriggerQueue[frame.TriggerCursor];
        if (invocation.Id != triggerId)
        {
            throw new ResolutionFailureException($"Trigger '{triggerId}' is not the current trigger choice.");
        }

        if (kind == ActionKind.DeclineTrigger)
        {
            if (invocation.Mode != TriggerMode.Optional)
            {
                throw new ResolutionFailureException("A mandatory trigger cannot be declined.");
            }

            int nextCursor = frame.TriggerCursor + 1;
            if (nextCursor >= frame.TriggerQueue.Count)
            {
                working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            }
            else
            {
                ReplaceCheckpoint(
                    working,
                    frame,
                    RemainingTriggerStageAfterAdvance(frame),
                    triggerCursor: nextCursor,
                    appliedTriggers: frame.AppliedTriggers.Append(triggerId).ToArray());
            }
            return;
        }

        if (kind != ActionKind.UseTrigger)
        {
            throw new ResolutionFailureException($"Action '{kind}' is not a trigger choice.");
        }

        StartTriggerChild(working, frame, invocation);
    }

    private static void StartTriggerChild(
        WorkingState working,
        EffectCheckpointFrame frame,
        TriggerInvocation invocation)
    {
        TriggerId triggerId = invocation.Id;
        ReplaceCheckpoint(
            working,
            frame,
            EffectCheckpointStage.AwaitingChild,
            triggerCursor: frame.TriggerCursor + 1,
            appliedTriggers: frame.AppliedTriggers.Append(triggerId).ToArray(),
            childKind: EffectCheckpointChildKind.Trigger,
            childTriggerId: triggerId,
            childInterruptId: null);
        EffectResolutionFrame parent = ParentEffectFrame(working);
        working.ResolutionStack.Add(new EffectResolutionFrame(
            1,
            ResolutionBehaviorIds.EffectResolution,
            invocation.Controller,
            invocation.Costs,
            invocation.Effects,
            EffectResolutionStage.PayCosts,
            0,
            invocation.Occurrence,
            parent.SuppressedTriggers.Append(triggerId).ToArray(),
            parent.SuppressedInterrupts));
    }

    private static EffectResolutionFrame ParentEffectFrame(WorkingState working)
    {
        if (working.ResolutionStack.Count < 2
            || working.ResolutionStack[^2] is not EffectResolutionFrame parent)
        {
            throw new ResolutionFailureException("A checkpoint child requires an effect resolution frame below the checkpoint.");
        }

        return parent;
    }

    private static EffectCheckpointStage RemainingTriggerStageAfterAdvance(EffectCheckpointFrame frame) =>
        frame.TriggerQueue.Count - (frame.TriggerCursor + 1) > 1
            ? EffectCheckpointStage.TriggerOrdering
            : frame.TriggerQueue.Count - (frame.TriggerCursor + 1) == 1
                ? EffectCheckpointStage.TriggerChoice
                : EffectCheckpointStage.Resolve;

    private static void ConsumePreventionChoice(WorkingState working, InterruptId interruptId, int amount)
    {
        EffectCheckpointFrame frame = CurrentCheckpoint(working, EffectCheckpointStage.InterruptChoice);
        InterruptInvocation invocation = CurrentInterrupt(frame, interruptId, InterruptKind.Prevention);
        int maximum = Math.Min(invocation.MaximumPreventionAmount, CurrentPreventableAmount(frame.CurrentEffect));
        if (amount <= 0 || amount > maximum)
        {
            throw new ResolutionFailureException($"Prevention amount {amount} is outside the current legal range 1..{maximum}.");
        }

        PayCosts(working, invocation.Costs);
        EffectInstruction current = ReducePreventableEffect(frame.CurrentEffect, amount);
        int nextCursor = frame.InterruptCursor + 1;
        ReplaceCheckpoint(
            working,
            frame,
            nextCursor < frame.InterruptQueue.Count && CurrentPreventableAmount(current) > 0
                ? EffectCheckpointStage.InterruptChoice
                : EffectCheckpointStage.Resolve,
            currentEffect: current,
            interruptCursor: nextCursor,
            appliedInterrupts: frame.AppliedInterrupts.Append(interruptId).ToArray());
    }

    private static void ConsumeInterruptDecline(WorkingState working, ActionKind kind, InterruptId interruptId)
    {
        EffectCheckpointFrame frame = CurrentCheckpoint(working, EffectCheckpointStage.InterruptChoice);
        InterruptInvocation invocation = CurrentInterrupt(frame, interruptId, null);
        bool correctKind = invocation.Kind == InterruptKind.Prevention
            ? kind == ActionKind.DeclinePrevention
            : kind == ActionKind.DeclineReplacement;
        if (!correctKind || invocation.Mandatory)
        {
            throw new ResolutionFailureException("This interrupt cannot be declined.");
        }

        int nextCursor = frame.InterruptCursor + 1;
        ReplaceCheckpoint(
            working,
            frame,
            nextCursor >= frame.InterruptQueue.Count
                || frame.InterruptQueue.Skip(nextCursor).All(value => value.Kind == InterruptKind.Prevention)
                    && CurrentPreventableAmount(frame.CurrentEffect) == 0
                ? EffectCheckpointStage.Resolve
                : EffectCheckpointStage.InterruptChoice,
            interruptCursor: nextCursor,
            appliedInterrupts: frame.AppliedInterrupts.Append(interruptId).ToArray());
    }

    private static void ConsumeReplacementChoice(WorkingState working, InterruptId interruptId)
    {
        EffectCheckpointFrame frame = CurrentCheckpoint(working, EffectCheckpointStage.InterruptChoice);
        InterruptInvocation invocation = CurrentInterrupt(frame, interruptId, InterruptKind.Replacement);
        if (frame.ReplacementDepth >= 8)
        {
            throw new ResolutionFailureException("Replacement depth exceeded the recursion guard.");
        }

        if (invocation.ReplacementEffects.Instructions.Count == 0)
        {
            throw new ResolutionFailureException($"Replacement interrupt '{interruptId}' has no replacement effects.");
        }

        PayCosts(working, invocation.Costs);
        ReplaceCheckpoint(
            working,
            frame,
            EffectCheckpointStage.AwaitingChild,
            interruptCursor: frame.InterruptCursor + 1,
            appliedInterrupts: frame.AppliedInterrupts.Append(interruptId).ToArray(),
            replacementDepth: frame.ReplacementDepth + 1,
            childKind: EffectCheckpointChildKind.Replacement,
            childTriggerId: null,
            childInterruptId: interruptId);
        EffectResolutionFrame parent = ParentEffectFrame(working);
        working.ResolutionStack.Add(new EffectResolutionFrame(
            1,
            ResolutionBehaviorIds.EffectResolution,
            invocation.Controller,
            new CostBundle(),
            invocation.ReplacementEffects,
            EffectResolutionStage.PayCosts,
            0,
            invocation.Occurrence,
            parent.SuppressedTriggers,
            parent.SuppressedInterrupts.Append(interruptId).ToArray()));
    }

    private static EffectCheckpointFrame CurrentCheckpoint(
        WorkingState working,
        EffectCheckpointStage expectedStage)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not EffectCheckpointFrame frame
            || frame.Stage != expectedStage)
        {
            throw new ResolutionFailureException($"The current resolution frame is not an '{expectedStage}' checkpoint.");
        }

        return frame;
    }

    private static InterruptInvocation CurrentInterrupt(
        EffectCheckpointFrame frame,
        InterruptId interruptId,
        InterruptKind? expectedKind)
    {
        if (frame.InterruptCursor >= frame.InterruptQueue.Count)
        {
            throw new ResolutionFailureException("There is no remaining interrupt choice.");
        }

        InterruptInvocation invocation = frame.InterruptQueue[frame.InterruptCursor];
        if (invocation.Id != interruptId || expectedKind.HasValue && invocation.Kind != expectedKind.Value)
        {
            throw new ResolutionFailureException($"Interrupt '{interruptId}' is not the current interrupt choice.");
        }

        return invocation;
    }

    private static void ReplaceCheckpoint(
        WorkingState working,
        EffectCheckpointFrame frame,
        EffectCheckpointStage? stage = null,
        IReadOnlyList<TriggerInvocation>? triggerQueue = null,
        int? triggerCursor = null,
        IReadOnlyList<InterruptInvocation>? interruptQueue = null,
        int? interruptCursor = null,
        IReadOnlyList<TriggerId>? appliedTriggers = null,
        IReadOnlyList<InterruptId>? appliedInterrupts = null,
        int? replacementDepth = null,
        EffectCheckpointChildKind? childKind = null,
        TriggerId? childTriggerId = null,
        InterruptId? childInterruptId = null,
        EffectInstruction? currentEffect = null,
        OccurrenceSnapshot? resolvedOccurrence = null)
    {
        if (working.ResolutionStack.Count == 0 || working.ResolutionStack[^1] is not EffectCheckpointFrame)
        {
            throw new ResolutionFailureException("A checkpoint can only be replaced at the top of the resolution stack.");
        }

        working.ResolutionStack[^1] = new EffectCheckpointFrame(
            frame.Version,
            frame.BehaviorId,
            frame.Controller,
            frame.Occurrence,
            frame.OriginalEffect,
            currentEffect ?? frame.CurrentEffect,
            stage ?? frame.Stage,
            triggerQueue ?? frame.TriggerQueue,
            triggerCursor ?? frame.TriggerCursor,
            interruptQueue ?? frame.InterruptQueue,
            interruptCursor ?? frame.InterruptCursor,
            appliedTriggers ?? frame.AppliedTriggers,
            appliedInterrupts ?? frame.AppliedInterrupts,
            replacementDepth ?? frame.ReplacementDepth,
            childKind ?? frame.ChildKind,
            childTriggerId,
            childInterruptId,
            resolvedOccurrence ?? frame.ResolvedOccurrence);
    }

    private static OccurrenceSnapshot CreateOccurrence(
        WorkingState working,
        EffectResolutionFrame frame,
        EffectInstruction instruction,
        OccurrencePhase phase)
    {
        CardInstanceId? sourceCardId = SourceCardId(instruction);
        CardDefinitionId? sourceDefinitionId = null;
        ZoneId? sourceZoneId = null;
        if (sourceCardId.HasValue)
        {
            CardInstanceState? source = working.Cards.SingleOrDefault(card => card.Id == sourceCardId.Value);
            if (source is not null)
            {
                sourceDefinitionId = source.DefinitionId;
                sourceZoneId = source.ZoneId;
            }
        }

        if (frame.SourceOccurrence is not null)
        {
            sourceCardId ??= frame.SourceOccurrence.SourceCardId;
            sourceDefinitionId ??= frame.SourceOccurrence.SourceDefinitionId;
            sourceZoneId ??= frame.SourceOccurrence.SourceZoneId;
        }

        return new OccurrenceSnapshot(
            1,
            working.Allocator.NextOccurrenceId(),
            EffectOccurrenceKind(instruction),
            phase,
            frame.Controller,
            TargetPlayer(instruction),
            sourceCardId,
            sourceDefinitionId,
            sourceZoneId,
            instruction);
    }

    private static OccurrenceSnapshot WithPhase(
        OccurrenceSnapshot occurrence,
        OccurrencePhase phase,
        EffectInstruction effect) => new(
            occurrence.Version,
            occurrence.Id,
            occurrence.Kind,
            phase,
            occurrence.Controller,
            occurrence.TargetPlayer,
            occurrence.SourceCardId,
            occurrence.SourceDefinitionId,
            occurrence.SourceZoneId,
            effect);

    private static InterruptInvocation[] MaterializeInterrupts(
        WorkingState working,
        EffectResolutionFrame frame,
        OccurrenceSnapshot occurrence) =>
        (working.RulesState?.Interrupts ?? [])
            .Where(interrupt => !frame.SuppressedInterrupts.Contains(interrupt.Id)
                && MatchesCondition(interrupt.Condition, occurrence, working))
            .OrderBy(interrupt => interrupt.Id.Value, StringComparer.Ordinal)
            .Select(interrupt =>
            {
                (CardDefinitionId? definitionId, ZoneId? zoneId) = SourceSnapshot(working, interrupt.SourceCardId);
                return new InterruptInvocation(interrupt, occurrence, definitionId, zoneId);
            })
            .Where(invocation => invocation.Kind != InterruptKind.Prevention || CurrentPreventableAmount(invocation.Occurrence.Effect) > 0)
            .ToArray();

    private static TriggerInvocation[] MaterializeTriggers(
        WorkingState working,
        OccurrenceSnapshot occurrence,
        EffectCheckpointFrame frame) =>
        (working.RulesState?.Triggers ?? [])
            .Where(trigger => !frame.AppliedTriggers.Contains(trigger.Id)
                && MatchesCondition(trigger.Condition, occurrence, working))
            // CR 9.2.8b: reaction-window priority begins with the active
            // player.  Stable IDs are evidence identifiers, not rules-level
            // priority, so they must never decide which controller acts first.
            .OrderBy(trigger => GetPlayer(working, trigger.Controller).Side == working.Turn.ActiveSide ? 0 : 1)
            .ThenBy(trigger => trigger.Controller.Value, StringComparer.Ordinal)
            .ThenBy(trigger => trigger.Id.Value, StringComparer.Ordinal)
            .Select(trigger =>
            {
                (CardDefinitionId? definitionId, ZoneId? zoneId) = SourceSnapshot(working, trigger.SourceCardId);
                return new TriggerInvocation(trigger, occurrence, definitionId, zoneId);
            })
            .ToArray();

    private static bool MatchesCondition(
        TriggerCondition condition,
        OccurrenceSnapshot occurrence,
        WorkingState working)
    {
        if (!condition.Matches(occurrence))
        {
            return false;
        }

        if (condition.TargetSide.HasValue)
        {
            if (!occurrence.TargetPlayer.HasValue)
            {
                return false;
            }

            PlayerState? target = working.Players.SingleOrDefault(player => player.Id == occurrence.TargetPlayer.Value);
            if (target is null || target.Side != condition.TargetSide.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static (CardDefinitionId? DefinitionId, ZoneId? ZoneId) SourceSnapshot(
        WorkingState working,
        CardInstanceId? sourceCardId)
    {
        if (!sourceCardId.HasValue)
        {
            return (null, null);
        }

        CardInstanceState? source = working.Cards.SingleOrDefault(card => card.Id == sourceCardId.Value);
        return source is null ? (null, null) : (source.DefinitionId, source.ZoneId);
    }

    private static CardInstanceId? SourceCardId(EffectInstruction instruction) => instruction switch
    {
        RegisterProgramMemoryEffect value => value.CardId,
        RemoveProgramMemoryEffect value => value.CardId,
        CardCounterAdjustmentEffect value => value.CardId,
        MoveCardEffect value => value.CardId,
        AddLingeringEffect value => value.Effect.SourceCardId,
        _ => null,
    };

    private static PlayerId? TargetPlayer(EffectInstruction instruction) => instruction switch
    {
        CreditAdjustmentEffect value => value.Target,
        ClickAdjustmentEffect value => value.Target,
        DamageEffect value => value.Target,
        TagAdjustmentEffect value => value.Target,
        MemoryAdjustmentEffect value => value.Target,
        RegisterProgramMemoryEffect value => value.Owner,
        _ => null,
    };

    private static OccurrenceKind EffectOccurrenceKind(EffectInstruction instruction) => instruction switch
    {
        CreditAdjustmentEffect => OccurrenceKind.CreditAdjustment,
        ClickAdjustmentEffect => OccurrenceKind.ClickAdjustment,
        DamageEffect => OccurrenceKind.Damage,
        TagAdjustmentEffect => OccurrenceKind.Tags,
        MemoryAdjustmentEffect or RegisterProgramMemoryEffect or RemoveProgramMemoryEffect => OccurrenceKind.Memory,
        CardCounterAdjustmentEffect => OccurrenceKind.CounterAdjustment,
        AddLingeringEffect => OccurrenceKind.LingeringEffectAdded,
        RemoveLingeringEffect => OccurrenceKind.LingeringEffectRemoved,
        MoveCardEffect => OccurrenceKind.CardMove,
        _ => OccurrenceKind.EffectImminent,
    };

    private static int CurrentPreventableAmount(EffectInstruction effect) => effect is DamageEffect damage
        ? damage.Amount
        : 0;

    private static DamageEffect ReducePreventableEffect(EffectInstruction effect, int amount) => effect switch
    {
        DamageEffect damage when amount <= damage.Amount => new DamageEffect(damage.Target, damage.Kind, damage.Amount - amount),
        _ => throw new ResolutionFailureException("Only damage effects support partial prevention in this slice."),
    };
}
