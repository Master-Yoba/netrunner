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
        if (invocation.Mode == TriggerMode.Optional
            && invocation.IsCatalogTrigger
            && invocation.SourceCardId is { } sourceId
            && working.BehaviorCatalog.TryGet(GetCard(working, sourceId).DefinitionId, out CardBehaviorDefinition? definition)
            && !CatalogTriggerDamageCostIsPayable(working, GetCard(working, sourceId),
                GetTriggerDeclaration(definition, invocation.TriggerKey!)))
        {
            // An optional queued ability with an unpayable self-trash cost
            // cannot be initiated. Consume it before issuing an action ID.
            if (frame.TriggerCursor + 1 == frame.TriggerQueue.Count)
            {
                working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            }
            else
            {
                ReplaceCheckpoint(working, frame, RemainingTriggerStageAfterAdvance(frame),
                    triggerCursor: frame.TriggerCursor + 1,
                    appliedTriggers: frame.AppliedTriggers.Append(invocation.Id).ToArray());
            }
            return;
        }
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

        if (invocation.IsCatalogTrigger)
        {
            if (invocation.TriggerKey is null
                || invocation.CatalogFingerprint is null
                || invocation.DefinitionFingerprint is null
                || invocation.SourceCardId is not { } sourceCardId
                || invocation.SourceDefinitionId is not { } sourceDefinitionId
                || invocation.SourceZoneId is not { } sourceZoneId
                || invocation.SourceOwnerSide is not { } sourceOwnerSide
                || invocation.SourceZoneKind is not { } sourceZoneKind)
            {
                throw new ResolutionFailureException("A catalog trigger invocation is missing its reconstruction provenance.");
            }

            if (!string.Equals(invocation.CatalogFingerprint, working.BehaviorCatalog.Fingerprint, StringComparison.Ordinal)
                || !string.Equals(
                    invocation.DefinitionFingerprint,
                    working.BehaviorCatalog.GetDefinitionFingerprint(sourceDefinitionId),
                    StringComparison.Ordinal))
            {
                throw new ResolutionFailureException("Catalog trigger invocation provenance does not match the injected catalog.");
            }

            ZoneState sourceSnapshotZone = working.Zones.SingleOrDefault(zone => zone.Id == sourceZoneId)
                ?? throw new ResolutionFailureException("Catalog trigger source snapshot zone no longer exists.");
            if (sourceSnapshotZone.Kind != sourceZoneKind)
            {
                throw new ResolutionFailureException("Catalog trigger source snapshot zone kind no longer matches.");
            }

            CardInstanceState source = GetCard(working, sourceCardId);
            PlayerState sourceController = GetPlayer(working, sourceOwnerSide);
            if (source.DefinitionId != sourceDefinitionId
                || source.OwnerSide != sourceOwnerSide
                || sourceController.Id != invocation.Controller)
            {
                throw new ResolutionFailureException("Catalog trigger source snapshot owner or definition is stale.");
            }

            CardBehaviorDefinition definition = GetBehaviorDefinition(working, sourceDefinitionId);
            BehaviorTrigger trigger = GetTriggerDeclaration(definition, invocation.TriggerKey);
            MarkCatalogTriggerUsed(working, source, trigger);
            if (trigger.RequireSourceInstalled
                && sourceZoneKind is not (ZoneKind.ServerRoot or ZoneKind.ServerIce or ZoneKind.RunnerRig))
            {
                throw new ResolutionFailureException("This catalog trigger requires an installed source snapshot.");
            }
            EffectCheckpointFrame awaiting = (EffectCheckpointFrame)working.ResolutionStack[^1];
            BehaviorResolutionFrame child = new(
                definition,
                new BehaviorInvocationContext(source.Id, invocation.Controller, invocation.Occurrence.Controller),
                working.BehaviorCatalog.Fingerprint,
                working.BehaviorCatalog.GetDefinitionFingerprint(sourceDefinitionId),
                working.Allocator.NextBehaviorFrameId(),
                sourceZoneId,
                sourceZoneKind,
                awaiting.BehaviorId,
                BehaviorFrameIdentity.Compute(awaiting),
                triggerKey: trigger.Key,
                rootInstructions: trigger.Instructions,
                instructions: trigger.Instructions,
                catalogTriggerSourceMovedBeforeStart: source.ZoneId != sourceZoneId);
            var bindings = invocation.Occurrence.SourceCardId is { } subjectCardId
                ? new[] { new BehaviorBinding("trigger-subject", [subjectCardId]) }
                : Array.Empty<BehaviorBinding>();
            child = child.With(stage: BehaviorResolutionStage.Execute, bindings: bindings);
            working.ResolutionStack.Add(child);
            return;
        }

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

        if (invocation.IsCatalogInterrupt)
        {
            ValidateCatalogBasicDrawReplacement(working, frame, invocation);
            RecordTurnFirstBasicDrawUse(working, invocation);
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

    private static void ValidateCatalogBasicDrawReplacement(
        WorkingState working,
        EffectCheckpointFrame frame,
        InterruptInvocation invocation)
    {
        if (invocation.Kind != InterruptKind.Replacement
            || !invocation.Mandatory
            || invocation.BehaviorId != ResolutionBehaviorIds.CardBehavior
            || invocation.InterruptKey is null
            || invocation.CatalogFingerprint is null
            || invocation.DefinitionFingerprint is null
            || invocation.SourceCardId is not { } sourceCardId
            || invocation.SourceDefinitionId is not { } sourceDefinitionId
            || invocation.SourceZoneId is not { } sourceZoneId
            || invocation.SourceOwnerSide is not { } sourceOwnerSide
            || invocation.SourceZoneKind is not { } sourceZoneKind
            || invocation.BasicDrawCount is not { } drawCount
            || invocation.ReplacementReasonKey is null)
        {
            throw new ResolutionFailureException("The basic-draw replacement invocation is missing typed provenance.");
        }

        if (!string.Equals(invocation.InterruptKey, invocation.ReplacementReasonKey, StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("The basic-draw replacement invocation reason provenance is inconsistent.");
        }

        if (frame.Occurrence.Kind != OccurrenceKind.BasicDraw
            || frame.Occurrence.Phase != OccurrencePhase.Imminent
            || frame.Occurrence.Effect is not BasicDrawEffect basicDraw
            || basicDraw.IsReplacement
            || !basicDraw.IsFirstBasicDraw
            || basicDraw.OccurrenceId is not { } occurrenceId
            || occurrenceId != frame.Occurrence.Id
            || invocation.Occurrence.Id != frame.Occurrence.Id
            || invocation.Occurrence.Kind != frame.Occurrence.Kind
            || invocation.Occurrence.Phase != frame.Occurrence.Phase
            || invocation.BasicDrawCount.Value != drawCount
            || invocation.ReplacementEffects.Instructions.Count != 1
            || invocation.ReplacementEffects.Instructions[0] is not BasicDrawEffect replacement
            || !replacement.IsReplacement
            || replacement.Side != basicDraw.Side
            || replacement.DrawCount != drawCount
            || replacement.IsFirstBasicDraw)
        {
            throw new ResolutionFailureException("The basic-draw replacement invocation does not match the frozen imminent effect.");
        }

        if (!string.Equals(invocation.CatalogFingerprint, working.BehaviorCatalog.Fingerprint, StringComparison.Ordinal)
            || !string.Equals(
                invocation.DefinitionFingerprint,
                working.BehaviorCatalog.GetDefinitionFingerprint(sourceDefinitionId),
                StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("Basic-draw replacement catalog provenance does not match the injected catalog.");
        }

        ZoneState sourceSnapshotZone = working.Zones.SingleOrDefault(zone => zone.Id == sourceZoneId)
            ?? throw new ResolutionFailureException("Basic-draw replacement source snapshot zone no longer exists.");
        if (sourceSnapshotZone.Kind != sourceZoneKind)
        {
            throw new ResolutionFailureException("Basic-draw replacement source snapshot zone kind no longer matches.");
        }
        if (!RuntimeCardActivity.IsActiveInstalledZone(
                sourceOwnerSide,
                sourceSnapshotZone,
                working.Zones,
                working.Servers))
        {
            throw new ResolutionFailureException("Basic-draw replacement source snapshot zone is not an active installed zone for its owner.");
        }

        CardInstanceState source = GetCard(working, sourceCardId);
        if (source.DefinitionId != sourceDefinitionId || source.OwnerSide != sourceOwnerSide)
        {
            throw new ResolutionFailureException("Basic-draw replacement source definition or owner is stale.");
        }

        PlayerState sourceController = GetPlayer(working, sourceOwnerSide);
        if (sourceController.Id != invocation.Controller
            || sourceOwnerSide != basicDraw.Side
            || frame.Controller != sourceController.Id
            || frame.Occurrence.Controller != sourceController.Id
            || invocation.Occurrence.Controller != frame.Occurrence.Controller
            || frame.Occurrence.TargetPlayer != sourceController.Id
            || invocation.Occurrence.TargetPlayer != frame.Occurrence.TargetPlayer)
        {
            throw new ResolutionFailureException("Basic-draw replacement source controller or occurrence context is stale.");
        }

        TurnFactsState facts = working.Turn.History?.ThisTurn(basicDraw.Side)
            ?? throw new ResolutionFailureException("Basic-draw replacement requires current formal-turn facts.");
        if (facts.Side != basicDraw.Side
            || facts.TurnNumber != working.Turn.TurnNumber
            || !facts.TurnBegan
            || facts.BasicDrawOccurrenceIds.Count == 0
            || facts.BasicDrawOccurrenceIds[0] != frame.Occurrence.Id)
        {
            throw new ResolutionFailureException("Basic-draw replacement occurrence ledger is stale or missing.");
        }

        if (facts.GetFirstXCount(sourceCardId, invocation.ReplacementReasonKey) != 0)
        {
            throw new ResolutionFailureException("Basic-draw replacement FirstX usage has already been consumed.");
        }

        CardBehaviorDefinition definition = GetBehaviorDefinition(working, sourceDefinitionId);
        BehaviorFirstBasicDrawReplacement declaration = definition.FirstBasicDrawReplacements?
            .SingleOrDefault(candidate => string.Equals(candidate.ReasonKey, invocation.ReplacementReasonKey, StringComparison.Ordinal))
            ?? throw new ResolutionFailureException("Basic-draw replacement declaration is no longer present.");
        if (declaration.DrawCount != drawCount)
        {
            throw new ResolutionFailureException("Basic-draw replacement declaration count is stale.");
        }
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

        EffectCheckpointStage nextStage = stage ?? frame.Stage;
        EffectCheckpointChildKind nextChildKind = childKind
            ?? (nextStage == EffectCheckpointStage.AwaitingChild
                ? frame.ChildKind
                : EffectCheckpointChildKind.None);
        TriggerId? nextTriggerId = nextStage == EffectCheckpointStage.AwaitingChild
            ? childTriggerId ?? frame.ChildTriggerId
            : null;
        InterruptId? nextInterruptId = nextStage == EffectCheckpointStage.AwaitingChild
            ? childInterruptId ?? frame.ChildInterruptId
            : null;

        working.ResolutionStack[^1] = new EffectCheckpointFrame(
            frame.Version,
            frame.BehaviorId,
            frame.Controller,
            frame.Occurrence,
            frame.OriginalEffect,
            currentEffect ?? frame.CurrentEffect,
            nextStage,
            triggerQueue ?? frame.TriggerQueue,
            triggerCursor ?? frame.TriggerCursor,
            interruptQueue ?? frame.InterruptQueue,
            interruptCursor ?? frame.InterruptCursor,
            appliedTriggers ?? frame.AppliedTriggers,
            appliedInterrupts ?? frame.AppliedInterrupts,
            replacementDepth ?? frame.ReplacementDepth,
            nextChildKind,
            nextTriggerId,
            nextInterruptId,
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

        PlayerId occurrenceController = instruction is GameplayOccurrenceEffect gameplay
            ? gameplay.Controller
            : frame.Controller;
        return new OccurrenceSnapshot(
            1,
            instruction is BasicDrawEffect { OccurrenceId: { } materializedOccurrenceId }
                ? materializedOccurrenceId
                : working.Allocator.NextOccurrenceId(),
            EffectOccurrenceKind(instruction),
            phase,
            occurrenceController,
            TargetPlayer(working, instruction),
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
            .Concat(MaterializeCatalogBasicDrawReplacements(working, frame, occurrence))
            .Where(invocation => !frame.SuppressedInterrupts.Contains(invocation.Id))
            .ToArray();

    private static InterruptInvocation[] MaterializeCatalogBasicDrawReplacements(
        WorkingState working,
        EffectResolutionFrame frame,
        OccurrenceSnapshot occurrence)
    {
        if (occurrence.Kind != OccurrenceKind.BasicDraw
            || occurrence.Phase != OccurrencePhase.Imminent
            || occurrence.Effect is not BasicDrawEffect { IsReplacement: false, IsFirstBasicDraw: true } basicDraw)
        {
            return [];
        }

        PlayerState occurrenceController = GetPlayer(working, occurrence.Controller);
        TurnFactsState? facts = working.Turn.History?.ThisTurn(occurrenceController.Side);
        if (facts is null
            || facts.Side != occurrenceController.Side
            || facts.TurnNumber != working.Turn.TurnNumber
            || !facts.TurnBegan
            || facts.BasicDrawOccurrenceIds.Count == 0
            || facts.BasicDrawOccurrenceIds[0] != occurrence.Id)
        {
            return [];
        }

        foreach (CardInstanceState source in working.Cards.OrderBy(card => card.Id.Value, StringComparer.Ordinal))
        {
            if (source.OwnerSide != occurrenceController.Side
                || !RuntimeCardActivity.IsActive(source, working.Zones)
                || !working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
                || definition.Controller != source.OwnerSide
                || definition.FirstBasicDrawReplacements is not { Count: > 0 } declarations)
            {
                continue;
            }

            ZoneState sourceZone = working.Zones.Single(zone => zone.Id == source.ZoneId);
            foreach (BehaviorFirstBasicDrawReplacement declaration in declarations)
            {
                if (facts.GetFirstXCount(source.Id, declaration.ReasonKey) != 0)
                {
                    continue;
                }

                InterruptId interruptId = CatalogBasicDrawReplacementId(occurrence, source, declaration.ReasonKey);
                if (frame.SuppressedInterrupts.Contains(interruptId))
                {
                    continue;
                }

                return
                [
                    new InterruptInvocation(
                        1,
                        interruptId,
                        occurrenceController.Id,
                        InterruptKind.Replacement,
                        true,
                        ResolutionBehaviorIds.CardBehavior,
                        source.Id,
                        source.DefinitionId,
                        source.ZoneId,
                        occurrence,
                        new CostBundle(),
                        0,
                        new EffectBundle(new BasicDrawEffect(
                            basicDraw.Side,
                            declaration.DrawCount,
                            true,
                            null,
                            false)),
                        declaration.ReasonKey,
                        working.BehaviorCatalog.Fingerprint,
                        working.BehaviorCatalog.GetDefinitionFingerprint(source.DefinitionId),
                        source.OwnerSide,
                        sourceZone.Kind,
                        declaration.DrawCount,
                        declaration.ReasonKey),
                ];
            }
        }

        return [];
    }

    private static TriggerInvocation[] MaterializeTriggers(
        WorkingState working,
        OccurrenceSnapshot occurrence,
        EffectCheckpointFrame frame)
    {
        TriggerInvocation[] legacy = (working.RulesState?.Triggers ?? [])
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

        TriggerInvocation[] catalog = MaterializeCatalogTriggers(working, occurrence, frame);
        return legacy
            .Concat(catalog)
            .OrderBy(trigger => GetPlayer(working, trigger.Controller).Side == working.Turn.ActiveSide ? 0 : 1)
            .ThenBy(trigger => trigger.Controller.Value, StringComparer.Ordinal)
            .ThenBy(trigger => trigger.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static TriggerInvocation[] MaterializeCatalogTriggers(
        WorkingState working,
        OccurrenceSnapshot occurrence,
        EffectCheckpointFrame frame)
    {
        if (occurrence.Kind is not (OccurrenceKind.CardInstalled
            or OccurrenceKind.CardRezzed
            or OccurrenceKind.ServerApproached
            or OccurrenceKind.IceEncountered
            or OccurrenceKind.IceApproached
            or OccurrenceKind.RunSuccessful
            or OccurrenceKind.RunEnded
            or OccurrenceKind.AgendaScoredOrStolen
            or OccurrenceKind.CardAccessed
            or OccurrenceKind.HostedCreditPoolEmptied
            or OccurrenceKind.TurnBegin
            or OccurrenceKind.Tags
            or OccurrenceKind.CardAdvancedFromZero
            or OccurrenceKind.AccessedCardTrashed
            or OccurrenceKind.DiscardPhaseEnded
                or OccurrenceKind.ActionPhaseEnded
                or OccurrenceKind.RunBegan
                or OccurrenceKind.AgendaForfeited
                or OccurrenceKind.FirstTagRemovalThisTurn
                or OccurrenceKind.FirstBadPublicityThisTurn
                or OccurrenceKind.CreditsSpentFromOutsidePool
                or OccurrenceKind.CardAdvanced
                or OccurrenceKind.FirstCardCreditGainThisTurn
                or OccurrenceKind.FirstOperationPlayedThisTurn
                or OccurrenceKind.FirstEventPlayedThisTurn
                or OccurrenceKind.IcePassed
                or OccurrenceKind.IceBypassed
                or OccurrenceKind.IceFullyBroken
                or OccurrenceKind.SubroutineBroken
                or OccurrenceKind.EncounterEnded
                or OccurrenceKind.ServerWouldBeApproached
                or OccurrenceKind.DamageDone
                or OccurrenceKind.CardsTrashedFromHq
                or OccurrenceKind.HandSizeDiscarded))
        {
            return [];
        }

        PlayerState occurrenceController = GetPlayer(working, occurrence.Controller);
        var materialized = new List<TriggerInvocation>();
        foreach (CardInstanceState source in working.Cards.OrderBy(card => card.Id.Value, StringComparer.Ordinal))
        {
            // VP-RETURN-AT-RUN-END: a run event's own abilities as its run begins too (Beta Build's install).
            bool activeRunEventSource = occurrence.Kind is OccurrenceKind.RunSuccessful or OccurrenceKind.RunBegan
                && IsActiveRunEventSource(working, source);
            if (!working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
                || definition.Controller != source.OwnerSide)
            {
                continue;
            }

            foreach (BehaviorTrigger trigger in definition.Triggers)
            {
                if (!(CatalogTriggerSourceIsActive(working, source, trigger, activeRunEventSource)
                        || IsTrashedSelfTriggerSource(working, source, trigger, occurrence.SourceCardId)
                            && SubjectWasRezzed(occurrence)
                        || IsEndedRunEventSource(working, source, trigger, occurrence.SourceCardId))
                    || trigger.Occurrence != occurrence.Kind
                    || occurrence.Kind == OccurrenceKind.TurnBegin
                        && trigger.Scope == BehaviorTriggerScope.Self
                    || frame.AppliedTriggers.Contains(CatalogTriggerId(occurrence, source, trigger.Key))
                    || !CatalogTriggerScopeMatches(trigger.Scope, source, occurrence, occurrenceController)
                    || !CatalogIdentityTriggerMatches(working, source, trigger, occurrence)
                    || !CatalogTriggerOutcomeMatches(trigger, occurrence)
                    || !CatalogRunAgendaConditionMatches(trigger, occurrence)
                    || !CatalogTriggerServerMatches(working, trigger, occurrence)
                    || !CatalogTriggerSourceLocationMatches(working, trigger, source, occurrence)
                    || !CatalogTriggerRequirementMatches(working, source, trigger)
                    || !CatalogTriggerCanStart(working, source, trigger))
                {
                    continue;
                }

                if (trigger.SubjectCardFilter is not null
                    && !CatalogSubjectMatches(working, occurrence.SourceCardId, trigger.SubjectCardFilter))
                {
                    continue;
                }

                PlayerId sourceController = GetPlayer(working, source.OwnerSide).Id;
                materialized.Add(new TriggerInvocation(
                    CatalogTriggerId(occurrence, source, trigger.Key),
                    sourceController,
                    trigger,
                    source.Id,
                    source.DefinitionId,
                    source.ZoneId,
                    source.OwnerSide,
                    working.Zones.Single(zone => zone.Id == source.ZoneId).Kind,
                    occurrence,
                    working.BehaviorCatalog.Fingerprint,
                    working.BehaviorCatalog.GetDefinitionFingerprint(source.DefinitionId)));
            }
        }

        materialized.AddRange(MaterializePersistentRunTriggers(working, occurrence, frame));
        return materialized.DistinctBy(value => value.Id).ToArray();
    }

    private static bool IsActiveRunEventSource(WorkingState working, CardInstanceState source)
    {
        if (working.RunState?.ActiveRunEventId != source.Id
            || source.OwnerSide != PlayerSide.Runner
            || !source.FaceUp)
        {
            return false;
        }

        ZoneState? sourceZone = working.Zones.FirstOrDefault(zone => zone.Id == source.ZoneId);
        return sourceZone is not null
            && sourceZone.Kind == ZoneKind.RunnerPlayArea
            && sourceZone.OwnerSide == PlayerSide.Runner
            && sourceZone.ServerId is null
            && sourceZone.CardIds.Count == 1
            && sourceZone.CardIds[0] == source.Id
            && working.BehaviorCatalog.TryGetRunEvent(source.DefinitionId, out _);
    }

    private static bool CatalogTriggerOutcomeMatches(
        BehaviorTrigger trigger,
        OccurrenceSnapshot occurrence)
    {
        if (trigger.RunOutcomeRequirement is not { } requirement)
        {
            return true;
        }

        if (occurrence.Kind != OccurrenceKind.RunEnded
            || occurrence.Effect is not GameplayOccurrenceEffect gameplay
            || gameplay.Successful is not { } successful)
        {
            return false;
        }

        return requirement == (successful
            ? RunOutcomeRequirement.Successful
            : RunOutcomeRequirement.Unsuccessful);
    }

    private static bool CatalogTriggerServerMatches(
        WorkingState working,
        BehaviorTrigger trigger,
        OccurrenceSnapshot occurrence)
    {
        if (trigger.ServerKindRequirement is not { } requiredServerKind)
        {
            return true;
        }

        if (occurrence.Kind is not (OccurrenceKind.ServerApproached
            or OccurrenceKind.IceEncountered
            or OccurrenceKind.IceApproached
            or OccurrenceKind.RunSuccessful
            or OccurrenceKind.RunEnded
            or OccurrenceKind.CardRezzed)
            || occurrence.Effect is not GameplayOccurrenceEffect gameplay
            || gameplay.ServerId is not { } serverId)
        {
            return false;
        }

        ServerState? server = working.Servers.FirstOrDefault(candidate => candidate.Id == serverId);
        return server?.Kind == requiredServerKind;
    }

    private static bool CatalogTriggerSourceLocationMatches(
        WorkingState working,
        BehaviorTrigger trigger,
        CardInstanceState source,
        OccurrenceSnapshot occurrence)
    {
        if (!trigger.RequireSourceOnAttackedServer)
        {
            return true;
        }

        // VP-CREDITS-OUTSIDE-POOL: the deferred occurrence carries no server; the payment was made during this run, which
        // is still going on when the occurrence opens (Shackleton Grid).
        if (occurrence.Kind == OccurrenceKind.CreditsSpentFromOutsidePool)
        {
            return working.RunState?.AttackedServer is { } runServer
                && SourceServerId(working, source) == runServer;
        }

        if (occurrence.Kind is not (OccurrenceKind.ServerApproached or OccurrenceKind.IceEncountered or OccurrenceKind.IceApproached or OccurrenceKind.CardRezzed or OccurrenceKind.RunEnded or OccurrenceKind.AgendaScoredOrStolen
                or OccurrenceKind.RunSuccessful)
            || occurrence.Effect is not GameplayOccurrenceEffect gameplay
            || gameplay.ServerId is not { } attackedServer)
        {
            return false;
        }

        return SourceServerId(working, source) == attackedServer
            && (occurrence.Kind != OccurrenceKind.CardRezzed
                || working.RunState?.AttackedServer == attackedServer);
    }

    /// <summary>
    /// The server a card is installed in; a card hosted on installed ICE (a trojan) is in its host's server
    /// (VP-HOSTED-SERVER, Stowaway: "a successful run on this server").
    /// </summary>
    private static ServerId? SourceServerId(WorkingState working, CardInstanceState source)
    {
        CardInstanceState placed = source.HostCardId is { } hostId
            && working.Cards.FirstOrDefault(card => card.Id == hostId) is { } host ? host : source;
        return working.Zones.FirstOrDefault(zone => zone.Id == placed.ZoneId)?.ServerId;
    }

    /// <summary>
    /// Reports whether the active installed-card projection declares a
    /// catalog trigger for the supplied occurrence. Lifecycle reducers use
    /// this cheap gate to avoid opening an otherwise empty occurrence
    /// checkpoint and changing legacy no-trigger state hashes.
    /// </summary>
    private static bool HasActiveCatalogTrigger(
        WorkingState working,
        OccurrenceKind occurrenceKind,
        ServerId? serverId = null,
        CardInstanceId? subjectCardId = null)
    {
        foreach (CardInstanceState source in working.Cards)
        {
            bool activeRunEventSource = occurrenceKind is OccurrenceKind.RunSuccessful or OccurrenceKind.RunBegan
                && IsActiveRunEventSource(working, source);
            if (!working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
                || definition.Controller != source.OwnerSide)
            {
                continue;
            }

            if (definition.Triggers.Any(trigger =>
                trigger.Occurrence == occurrenceKind
                && (CatalogTriggerSourceIsActive(working, source, trigger, activeRunEventSource)
                    || IsTrashedSelfTriggerSource(working, source, trigger, subjectCardId)
                    || IsEndedRunEventSource(working, source, trigger, subjectCardId))
                && (trigger.ServerKindRequirement is null
                    || serverId is { } suppliedServerId
                        && working.Servers.FirstOrDefault(candidate => candidate.Id == suppliedServerId)?.Kind
                            == trigger.ServerKindRequirement)
                && (!trigger.RequireSourceOnAttackedServer
                    || occurrenceKind == OccurrenceKind.CreditsSpentFromOutsidePool
                        && working.RunState?.AttackedServer is { } paymentServer
                        && SourceServerId(working, source) == paymentServer
                    || occurrenceKind is OccurrenceKind.ServerApproached or OccurrenceKind.IceEncountered or OccurrenceKind.IceApproached or OccurrenceKind.CardRezzed or OccurrenceKind.RunEnded or OccurrenceKind.AgendaScoredOrStolen
                            or OccurrenceKind.RunSuccessful
                        && (occurrenceKind != OccurrenceKind.CardRezzed || working.RunState?.AttackedServer == serverId)
                        && serverId is { } suppliedServer
                        && SourceServerId(working, source) == suppliedServer)
                && (occurrenceKind is not (OccurrenceKind.ServerApproached or OccurrenceKind.IceEncountered or OccurrenceKind.IceApproached)
                    || trigger.Scope != BehaviorTriggerScope.Controller
                    || source.OwnerSide == PlayerSide.Runner)
                && (occurrenceKind != OccurrenceKind.RunSuccessful
                    || trigger.Scope != BehaviorTriggerScope.Self
                    || subjectCardId is { } successfulSource
                        && source.Id == successfulSource)
                && (occurrenceKind != OccurrenceKind.IceEncountered
                    || trigger.Scope != BehaviorTriggerScope.Self
                        || subjectCardId is { } encounteredIce
                        && source.Id == encounteredIce)
                && (occurrenceKind != OccurrenceKind.IceEncountered
                    || trigger.SubjectCardFilter is null
                    || subjectCardId is { } subject
                        && CatalogSubjectMatches(working, subject, trigger.SubjectCardFilter))
                && CatalogTriggerRequirementMatches(working, source, trigger)
                && CatalogTriggerCanStart(working, source, trigger)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CatalogTriggerSourceIsActive(
        WorkingState working,
        CardInstanceState source,
        BehaviorTrigger trigger,
        bool activeRunEventSource)
    {
        bool installed = IsInstalledCard(working, source.Id);
        if (trigger.Face is { } face && face != SourceFace(working, source.Id))
        {
            return false;
        }

        return (!trigger.RequireSourceInstalled || installed)
            && (activeRunEventSource
                || RuntimeCardActivity.IsActive(source, working.Zones)
                || trigger.Occurrence == OccurrenceKind.AgendaScoredOrStolen
                    && trigger.Scope == BehaviorTriggerScope.Self
                    && working.Zones.Any(zone => zone.Id == source.ZoneId && zone.Kind == ZoneKind.RunnerScore)
                || trigger.Occurrence == OccurrenceKind.AgendaForfeited
                    && trigger.Scope == BehaviorTriggerScope.Self
                    && working.Zones.Any(zone => zone.Id == source.ZoneId && zone.Kind == ZoneKind.CorpRemovedFromGame)
                || trigger.ActiveWhileUnrezzed && installed && source.OwnerSide == PlayerSide.Corp
                || trigger.ActiveWhileAccessed && IsBeingAccessedOutsideArchives(working, source)
                || installed && source.FaceUp && InstallsFaceUp(working, source.DefinitionId)
                    && working.Zones.Any(zone => zone.Id == source.ZoneId && zone.Kind == ZoneKind.ServerRoot));
    }

    private static bool IsBeingAccessedOutsideArchives(WorkingState working, CardInstanceState source) =>
        source.OwnerSide == PlayerSide.Corp
        && working.RunState is { ChosenAccessCards.Count: > 0 } run
        && run.ChosenAccessCards[^1] == source.Id
        && working.Zones.FirstOrDefault(zone => zone.Id == source.ZoneId)?.Kind is not ZoneKind.CorpDiscard;

    private static bool CatalogTriggerRequirementMatches(
        WorkingState working,
        CardInstanceState source,
        BehaviorTrigger trigger) =>
        trigger.Requirement is not { } requirement
        || OptionConditionMatches(working, GetPlayer(working, source.OwnerSide).Id, source.Id, requirement);

    private static bool CatalogTriggerDamageCostIsPayable(
        WorkingState working,
        CardInstanceState source,
        BehaviorTrigger trigger) =>
        !trigger.Instructions.Any(instruction => instruction.DamageAmountOperand?.TrashSourceAsCost == true)
            || IsInstalledCard(working, source.Id);

    private static bool CatalogTriggerCanStart(
        WorkingState working,
        CardInstanceState source,
        BehaviorTrigger trigger)
    {
        if (!CatalogTriggerDamageCostIsPayable(working, source, trigger))
        {
            return false;
        }
        BehaviorChoiceSpec? openingChoice = trigger.Instructions.Count > 0
            && trigger.Instructions[0] is
            {
                Kind: BehaviorInstructionKind.Choose,
                Choice: { } choice,
            }
                ? choice
                : null;
        if (openingChoice is null)
        {
            return true;
        }

        if (openingChoice.Target is { } target)
        {
            int candidates = MaterializeBehaviorCandidates(working, target).Length;
            return (trigger.Mode != TriggerMode.Optional || candidates > 0)
                && (openingChoice.Cardinality.Kind != BehaviorChoiceCardinalityKind.Exactly
                    || candidates >= openingChoice.Cardinality.Count);
        }

        PlayerId controller = GetPlayer(working, source.OwnerSide).Id;
        return openingChoice.Options.Any(option =>
            (trigger.Mode != TriggerMode.Optional
                || option.Semantic != BehaviorChoiceOptionSemantic.Fallback)
            &&
            IsBehaviorChoiceOptionAffordable(working, controller, option, source.Id));
    }

    private static bool CatalogTriggerScopeMatches(
        BehaviorTriggerScope scope,
        CardInstanceState source,
        OccurrenceSnapshot occurrence,
        PlayerState occurrenceController) => scope switch
        {
            BehaviorTriggerScope.Self => occurrence.SourceCardId == source.Id,
            BehaviorTriggerScope.Controller => occurrenceController.Side == source.OwnerSide,
            BehaviorTriggerScope.Any => true,
            _ => false,
        };

    private static bool CatalogSubjectMatches(
        WorkingState working,
        CardInstanceId? subjectCardId,
        BehaviorCardFilter filter)
    {
        if (!subjectCardId.HasValue)
        {
            return false;
        }

        CardInstanceState? subject = working.Cards.SingleOrDefault(card => card.Id == subjectCardId.Value);
        if (subject is null
            || filter.Rezzed is { } requiredRezzed && subject.Rezzed != requiredRezzed
            || filter.DefinitionId is { } definitionId && subject.DefinitionId != definitionId
            || filter.OwnerSide is { } ownerSide && subject.OwnerSide != ownerSide)
        {
            return false;
        }

        if (working.RuntimeCardCatalog is not null)
        {
            if (!working.RuntimeCardCatalog.TryGet(subject.DefinitionId, out CardRuntimeSpec? runtime))
            {
                return false;
            }

            if (!filter.SubtypesMatch(runtime.Subtypes))
            {
                return false;
            }

            return filter.CardType switch
            {
                BehaviorCardType.Any => true,
                BehaviorCardType.NonAgenda => runtime.CardClass != RuntimeCardClass.Agenda,
                BehaviorCardType.Agenda => runtime.CardClass == RuntimeCardClass.Agenda,
                BehaviorCardType.Asset => runtime.CardClass == RuntimeCardClass.Asset,
                BehaviorCardType.Event => runtime.CardClass == RuntimeCardClass.Event,
                BehaviorCardType.Hardware => runtime.CardClass == RuntimeCardClass.Hardware,
                BehaviorCardType.Ice => runtime.CardClass == RuntimeCardClass.Ice,
                BehaviorCardType.Icebreaker => runtime.CardClass == RuntimeCardClass.Program
                    && runtime.Subtypes.Contains("icebreaker", StringComparer.Ordinal),
                BehaviorCardType.Program => runtime.CardClass == RuntimeCardClass.Program,
                BehaviorCardType.Identity => runtime.CardClass == RuntimeCardClass.Identity,
                BehaviorCardType.Operation => runtime.CardClass == RuntimeCardClass.Operation,
                BehaviorCardType.Resource => runtime.CardClass == RuntimeCardClass.Resource,
                BehaviorCardType.Upgrade => runtime.CardClass == RuntimeCardClass.Upgrade,
                BehaviorCardType.ProgramOrHardware => runtime.CardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware,
                BehaviorCardType.ResourceOrHardware => runtime.CardClass is RuntimeCardClass.Resource or RuntimeCardClass.Hardware,
                BehaviorCardType.Advanceable => runtime.CardClass == RuntimeCardClass.Agenda
                    || working.BehaviorCatalog.TryGet(subject.DefinitionId, out CardBehaviorDefinition? advanceable)
                        && advanceable.CanBeAdvanced,
                _ => false,
            };
        }

        if (filter.NamesSubtype)
        {
            return false;
        }

        if (filter.CardType == BehaviorCardType.Any)
        {
            return true;
        }

        return filter.CardType switch
        {
            BehaviorCardType.Ice => working.BehaviorCatalog.TryGetIce(subject.DefinitionId, out _),
            BehaviorCardType.Icebreaker => working.BehaviorCatalog.TryGetIcebreaker(subject.DefinitionId, out _),
            BehaviorCardType.Asset => working.BehaviorCatalog.TryGetAsset(subject.DefinitionId, out _),
            BehaviorCardType.Agenda => working.BehaviorCatalog.TryGetAgenda(subject.DefinitionId, out _),
            BehaviorCardType.Event => working.BehaviorCatalog.TryGetRunEvent(subject.DefinitionId, out _),
            _ => working.BehaviorCatalog.TryGetCardType(subject.DefinitionId, out BehaviorCardType type)
                && type == filter.CardType,
        };
    }

    private static TriggerId CatalogTriggerId(
        OccurrenceSnapshot occurrence,
        CardInstanceState source,
        string triggerKey)
    {
        string material = $"catalog-trigger|{occurrence.Id.Value}|{source.Id.Value}|{source.DefinitionId.Value}|{triggerKey}";
        byte[] digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(material));
        return new TriggerId("catalog-trigger:" + Convert.ToHexString(digest).ToLowerInvariant());
    }

    private static InterruptId CatalogBasicDrawReplacementId(
        OccurrenceSnapshot occurrence,
        CardInstanceState source,
        string reasonKey)
    {
        string material = $"catalog-basic-draw-replacement|{occurrence.Id.Value}|{source.Id.Value}|{source.DefinitionId.Value}|{reasonKey}";
        byte[] digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(material));
        return new InterruptId("catalog-basic-draw:" + Convert.ToHexString(digest).ToLowerInvariant());
    }

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
        GameplayOccurrenceEffect value => value.SubjectCardId,
        AddLingeringEffect value => value.Effect.SourceCardId,
        _ => null,
    };

    private static PlayerId? TargetPlayer(WorkingState working, EffectInstruction instruction) => instruction switch
    {
        TurnBoundaryEffect value => value.Target,
        CreditAdjustmentEffect value => value.Target,
        ClickAdjustmentEffect value => value.Target,
        DamageEffect value => value.Target,
        TagAdjustmentEffect value => value.Target,
        MemoryAdjustmentEffect value => value.Target,
        RegisterProgramMemoryEffect value => value.Owner,
        BasicDrawEffect value => GetPlayer(working, value.Side).Id,
        _ => null,
    };

    private static OccurrenceKind EffectOccurrenceKind(EffectInstruction instruction) => instruction switch
    {
        GameplayOccurrenceEffect value => value.Kind,
        TurnBoundaryEffect value => value.Kind,
        CreditAdjustmentEffect => OccurrenceKind.CreditAdjustment,
        ClickAdjustmentEffect => OccurrenceKind.ClickAdjustment,
        DamageEffect => OccurrenceKind.Damage,
        TagAdjustmentEffect => OccurrenceKind.Tags,
        MemoryAdjustmentEffect or RegisterProgramMemoryEffect or RemoveProgramMemoryEffect => OccurrenceKind.Memory,
        CardCounterAdjustmentEffect => OccurrenceKind.CounterAdjustment,
        AddLingeringEffect => OccurrenceKind.LingeringEffectAdded,
        RemoveLingeringEffect => OccurrenceKind.LingeringEffectRemoved,
        MoveCardEffect => OccurrenceKind.CardMove,
        BasicDrawEffect { IsReplacement: false } => OccurrenceKind.BasicDraw,
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
