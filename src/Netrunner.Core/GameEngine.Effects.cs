namespace Netrunner.Core;

/// <summary>Atomic reducers for the reusable M2 cost and effect instructions.</summary>
public static partial class GameEngine
{
    /// <summary>
    /// Friend-visible C2a seam for opening a typed gameplay occurrence. The
    /// production lifecycle reducers will call the same effect/checkpoint
    /// path in a later slice; this method intentionally performs no lifecycle
    /// integration of its own.
    /// </summary>
    internal static GameState BeginGameplayOccurrenceForTesting(
        GameState state,
        GameplayOccurrenceEffect occurrence,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog = null,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        runtimeCardCatalog is null
            ? BeginResolution(
                state,
                new EffectResolutionFrame(occurrence.Controller, new CostBundle(), new EffectBundle(occurrence)),
                behaviorCatalog,
                maxResolutionSteps)
            : BeginResolution(
                state,
                new EffectResolutionFrame(occurrence.Controller, new CostBundle(), new EffectBundle(occurrence)),
                behaviorCatalog,
                runtimeCardCatalog,
                maxResolutionSteps);

    public static GameState ResolveEffects(
        GameState state,
        PlayerId controller,
        CostBundle costs,
        EffectBundle effects,
        int maxResolutionSteps = DefaultResolutionStepBudget)
    {
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(effects);
        return BeginResolution(state, new EffectResolutionFrame(controller, costs, effects), maxResolutionSteps);
    }

    public static GameState CleanupLingeringEffects(
        GameState state,
        PlayerId controller,
        LingeringEffectExpiry expiry)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(expiry))
        {
            throw new ArgumentOutOfRangeException(nameof(expiry));
        }

        if (state.RulesState is null)
        {
            return state;
        }

        EffectInstruction[] removals = state.RulesState.LingeringEffects
            .Where(effect => effect.Expiry == expiry)
            .Select(effect => (EffectInstruction)new RemoveLingeringEffect(effect.EffectId))
            .ToArray();
        if (removals.Length == 0)
        {
            return state;
        }

        return ResolveEffects(state, controller, new CostBundle(), new EffectBundle(removals));
    }

    private static void StepEffectResolution(WorkingState working, EffectResolutionFrame frame)
    {
        switch (frame.Stage)
        {
            case EffectResolutionStage.PayCosts:
                // Turn-boundary occurrences are no-op checkpoints.  They may
                // still materialize registered triggers, but opening a legacy
                // M1 turn must not eagerly create an otherwise-unused
                // RulesState and change its canonical hash.
                if (!IsTurnBoundaryBundle(frame.Effects))
                {
                    EnsureRulesState(working);
                }

                PayCosts(working, frame.Costs);
                ReplaceTopFrame(working, new EffectResolutionFrame(
                    frame.Version, frame.BehaviorId, frame.Controller, frame.Costs, frame.Effects,
                    EffectResolutionStage.ApplyEffects,
                    frame.EffectCursor,
                    frame.SourceOccurrence,
                    frame.SuppressedTriggers,
                    frame.SuppressedInterrupts));
                return;
            case EffectResolutionStage.ApplyEffects:
                if (frame.EffectCursor >= frame.Effects.Instructions.Count)
                {
                    FinishEffectBundle(working, frame);
                    return;
                }

                EffectInstruction instruction = frame.Effects.Instructions[frame.EffectCursor];
                OccurrenceSnapshot imminent = CreateOccurrence(working, frame, instruction, OccurrencePhase.Imminent);
                InterruptInvocation[] interrupts = MaterializeInterrupts(working, frame, imminent);
                ReplaceTopFrame(working, new EffectResolutionFrame(
                    frame.Version,
                    frame.BehaviorId,
                    frame.Controller,
                    frame.Costs,
                    frame.Effects,
                    EffectResolutionStage.AwaitingCheckpoint,
                    frame.EffectCursor,
                    frame.SourceOccurrence,
                    frame.SuppressedTriggers,
                    frame.SuppressedInterrupts));
                working.ResolutionStack.Add(new EffectCheckpointFrame(
                    1,
                    ResolutionBehaviorIds.EffectCheckpoint,
                    frame.Controller,
                    imminent,
                    instruction,
                    instruction,
                    interrupts.Length > 0 ? EffectCheckpointStage.InterruptChoice : EffectCheckpointStage.Resolve,
                    [],
                    0,
                    interrupts,
                    0,
                    frame.SuppressedTriggers,
                    frame.SuppressedInterrupts,
                    0,
                    EffectCheckpointChildKind.None,
                    null,
                    null,
                    null));
                return;
            case EffectResolutionStage.AwaitingCheckpoint:
                if (working.ResolutionStack.Count > 0
                    && working.ResolutionStack[^1] is EffectCheckpointFrame)
                {
                    throw new ResolutionFailureException("An effect checkpoint child must finish before its parent resumes.");
                }

                if (frame.EffectCursor >= frame.Effects.Instructions.Count)
                {
                    throw new ResolutionFailureException("An effect checkpoint resumed with an exhausted effect cursor.");
                }

                ReplaceTopFrame(working, new EffectResolutionFrame(
                    frame.Version,
                    frame.BehaviorId,
                    frame.Controller,
                    frame.Costs,
                    frame.Effects,
                    EffectResolutionStage.ApplyEffects,
                    checked(frame.EffectCursor + 1),
                    frame.SourceOccurrence,
                    frame.SuppressedTriggers,
                    frame.SuppressedInterrupts));
                return;
            case EffectResolutionStage.AwaitingMemoryCleanup:
                PlayerRulesState? illegal = working.RulesState!.Players
                    .FirstOrDefault(player => player.UsedMemory > EffectiveMemoryLimit(working, player.PlayerId));
                if (illegal is not null)
                {
                    throw new ResolutionFailureException(
                        $"Memory cleanup returned while player '{illegal.PlayerId}' still exceeds available memory.");
                }

                working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
                return;
            default:
                throw new ResolutionFailureException($"Unsupported effect stage '{frame.Stage}'.");
        }
    }

    private static void FinishEffectBundle(WorkingState working, EffectResolutionFrame frame)
    {
        if (IsTurnBoundaryBundle(frame.Effects))
        {
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        PlayerRulesState? overMemory = working.RulesState!.Players
            .FirstOrDefault(player => player.UsedMemory > EffectiveMemoryLimit(working, player.PlayerId));
        if (overMemory is null)
        {
            CompleteHostedCreditOccurrenceParent(working, frame);
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        ProgramMemoryState[] candidates = MemoryCandidates(working, overMemory.PlayerId);
        if (candidates.Length == 0)
        {
            throw new ResolutionFailureException(
                $"Player '{overMemory.PlayerId}' exceeds available memory but has no installed program to trash.");
        }

        ReplaceTopFrame(working, new EffectResolutionFrame(
            frame.Version,
            frame.BehaviorId,
            frame.Controller,
            frame.Costs,
            frame.Effects,
            EffectResolutionStage.AwaitingMemoryCleanup,
            frame.EffectCursor,
            frame.SourceOccurrence,
            frame.SuppressedTriggers,
            frame.SuppressedInterrupts));
        working.ResolutionStack.Add(new MemoryCleanupFrame(
            overMemory.PlayerId,
            overMemory.UsedMemory - EffectiveMemoryLimit(working, overMemory.PlayerId),
            candidates));
    }

    private static void CompleteHostedCreditOccurrenceParent(
        WorkingState working,
        EffectResolutionFrame occurrenceFrame)
    {
        if (occurrenceFrame.Effects.Instructions.Count != 1
            || occurrenceFrame.Effects.Instructions[0] is not GameplayOccurrenceEffect occurrence
            || occurrence.Kind != OccurrenceKind.HostedCreditPoolEmptied)
        {
            return;
        }

        if (working.ResolutionStack.Count < 2
            || working.ResolutionStack[^2] is not BehaviorResolutionFrame parent)
        {
            return;
        }

        BehaviorOccurrenceContinuation continuation = parent.OccurrenceContinuation
            ?? throw new ResolutionFailureException(
                "A hosted-credit occurrence effect returned without its awaiting behavior continuation.");
        if (parent.Stage != BehaviorResolutionStage.AwaitingOccurrence
            || continuation.Kind != occurrence.Kind
            || continuation.SourceCardId != occurrence.SubjectCardId
            || continuation.SourceCardId != parent.SourceCardId
            || continuation.FrameIdentity != parent.FrameIdentity
            || continuation.InstructionCursor != parent.InstructionCursor
            || occurrence.Controller != parent.Controller)
        {
            throw new ResolutionFailureException(
                "A hosted-credit occurrence effect does not match its exact awaiting behavior parent.");
        }

        CardInstanceState source = GetCard(working, parent.SourceCardId);
        PlayerState controller = GetPlayer(working, parent.Controller);
        if (source.DefinitionId != parent.DefinitionId
            || source.OwnerSide != controller.Side)
        {
            throw new ResolutionFailureException(
                "The hosted-credit occurrence parent source definition or owner is stale.");
        }

        if (working.RulesState is null
            || working.RulesState.Counters.Any(counter =>
                counter.CardId == parent.SourceCardId
                && counter.Purpose == CardCounterPurpose.HostedCredit))
        {
            throw new ResolutionFailureException(
                "A hosted-credit occurrence parent must have an exhausted hosted-credit pool.");
        }

        bool sourceMoved = parent.SourceZoneId is not { } sourceZoneId
            || source.ZoneId != sourceZoneId;
        if (!sourceMoved)
        {
            ReplaceFrameAfterHostedCreditOccurrence(working, parent, occurrenceContinuation: null);
            return;
        }

        ZoneState currentSourceZone = ZoneForCard(working, source.Id);
        ZoneKind expectedDiscard = source.OwnerSide == PlayerSide.Corp
            ? ZoneKind.CorpDiscard
            : ZoneKind.RunnerHeap;
        if (currentSourceZone.Kind != expectedDiscard
            || !HasReachableHostedCreditSelfTrashTrigger(working, parent))
        {
            throw new ResolutionFailureException(
                "A hosted-credit occurrence may authorize only its source owner's discard after an explicit self-trash trigger.");
        }

        ReplaceFrameAfterHostedCreditOccurrence(
            working,
            parent,
            new BehaviorOccurrenceContinuation(
                continuation.Kind,
                continuation.SourceCardId,
                continuation.FrameIdentity,
                continuation.InstructionCursor,
                sourceMovedAfterCompletion: true));
    }

    private static void ReplaceFrameAfterHostedCreditOccurrence(
        WorkingState working,
        BehaviorResolutionFrame parent,
        BehaviorOccurrenceContinuation? occurrenceContinuation)
    {
        int parentIndex = working.ResolutionStack.Count - 2;
        if (parentIndex < 0 || working.ResolutionStack[parentIndex] is not BehaviorResolutionFrame)
        {
            throw new ResolutionFailureException(
                "A hosted-credit occurrence parent is no longer on the active resolution stack.");
        }

        BehaviorResolutionFrame completed = occurrenceContinuation is null
            ? parent.With(
                stage: BehaviorResolutionStage.Execute,
                clearOccurrenceContinuation: true)
            : parent.With(
                stage: BehaviorResolutionStage.Execute,
                occurrenceContinuation: occurrenceContinuation);
        working.ResolutionStack[parentIndex] = completed;
    }

    private static bool HasReachableHostedCreditSelfTrashTrigger(
        WorkingState working,
        BehaviorResolutionFrame parent)
    {
        if (!working.BehaviorCatalog.TryGet(parent.DefinitionId, out CardBehaviorDefinition? definition))
        {
            return false;
        }

        return definition.Triggers.Any(trigger =>
            trigger.Occurrence == OccurrenceKind.HostedCreditPoolEmptied
            && BehaviorInstructionPath.CanReach(
                trigger.Instructions,
                BehaviorInstructionKind.TrashSelf));
    }

    private static bool IsTurnBoundaryBundle(EffectBundle effects) =>
        effects.Instructions.Count > 0
        && effects.Instructions.All(static instruction => instruction is TurnBoundaryEffect);

    private static void StepMemoryCleanup(WorkingState working, MemoryCleanupFrame frame)
    {
        PlayerRulesState owner = GetPlayerRules(RequireRulesState(working), frame.Owner);
        int memoryLimit = EffectiveMemoryLimit(working, frame.Owner);
        if (owner.UsedMemory <= memoryLimit)
        {
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        ProgramMemoryState[] candidates = MemoryCandidates(working, frame.Owner);
        if (candidates.Length == 0)
        {
            throw new ResolutionFailureException("Mandatory memory cleanup has no installed program candidate.");
        }

        if (!SameProgramCandidates(frame.Candidates, candidates)
            || frame.RequiredReduction != owner.UsedMemory - memoryLimit)
        {
            frame = new MemoryCleanupFrame(
                frame.Owner,
                owner.UsedMemory - memoryLimit,
                candidates);
            ReplaceTopFrame(working, frame);
        }

        LegalAction[] actions = candidates.Select(program => new LegalAction(
            working.Allocator.NextActionId(),
            ActionKind.TrashProgram,
            $"Trash program {program.CardId} to free {program.MemoryCost} memory",
            new CardChoiceParameters(program.CardId))).ToArray();
        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            frame.Owner,
            DecisionKind.MemoryCleanup,
            "Trash installed programs until memory use is legal",
            actions);
    }

    private static void ConsumeMemoryCleanupAction(WorkingState working, CardInstanceId cardId)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not MemoryCleanupFrame frame
            || !frame.Candidates.Any(candidate => candidate.CardId == cardId))
        {
            throw new ResolutionFailureException("The selected program is not a current memory-cleanup candidate.");
        }

        CardInstanceState card = GetCard(working, cardId);
        ZoneState source = working.Zones.SingleOrDefault(zone => zone.Id == card.ZoneId)
            ?? throw new ResolutionFailureException($"Program '{cardId}' has no source zone.");
        if (source.Kind != ZoneKind.RunnerRig)
        {
            throw new ResolutionFailureException($"Program '{cardId}' is no longer installed in the Runner rig.");
        }

        TrashCard(working, card, source, GetZone(working, ZoneKind.RunnerHeap));
        RemoveProgramRegistration(working, cardId);
        Emit(working, EngineEventKinds.ProgramTrashedForMemory, new ProgramTrashedForMemoryEventPayload(
            cardId,
            frame.Owner,
            frame.Candidates.Single(candidate => candidate.CardId == cardId).MemoryCost));

        PlayerRulesState owner = GetPlayerRules(RequireRulesState(working), frame.Owner);
        if (owner.UsedMemory <= EffectiveMemoryLimit(working, frame.Owner))
        {
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        ProgramMemoryState[] candidates = MemoryCandidates(working, frame.Owner);
        if (candidates.Length == 0)
        {
            throw new ResolutionFailureException("Memory cleanup cannot make the rig legal.");
        }

        ReplaceTopFrame(working, new MemoryCleanupFrame(
            frame.Owner,
            owner.UsedMemory - EffectiveMemoryLimit(working, frame.Owner),
            candidates));
    }

    private static void PayCosts(WorkingState working, CostBundle bundle)
    {
        var creditTotals = new Dictionary<PlayerId, int>();
        var clickTotals = new Dictionary<PlayerId, int>();
        foreach (CostInstruction instruction in bundle.Instructions)
        {
            switch (instruction)
            {
                case CreditCost credit when credit.Amount >= 0:
                    AddCost(creditTotals, credit.Payer, credit.Amount);
                    break;
                case ClickCost click when click.Amount >= 0:
                    AddCost(clickTotals, click.Payer, click.Amount);
                    break;
                case CreditCost:
                case ClickCost:
                    throw new ResolutionFailureException("Cost amounts must be nonnegative.");
                default:
                    throw new ResolutionFailureException($"Unsupported cost instruction '{instruction.GetType().Name}'.");
            }
        }

        foreach ((PlayerId payer, int amount) in creditTotals)
        {
            if (GetPlayer(working, payer).Credits < amount)
            {
                throw new ResolutionFailureException($"Player '{payer}' cannot pay {amount} credits.");
            }
        }

        foreach ((PlayerId payer, int amount) in clickTotals)
        {
            if (GetPlayer(working, payer).Clicks < amount)
            {
                throw new ResolutionFailureException($"Player '{payer}' cannot pay {amount} clicks.");
            }
        }

        foreach ((PlayerId payer, int amount) in creditTotals)
        {
            if (amount > 0) ApplyCreditDelta(working, payer, -amount);
        }

        foreach ((PlayerId payer, int amount) in clickTotals)
        {
            if (amount > 0) ApplyClickDelta(working, payer, -amount);
        }

        static void AddCost(Dictionary<PlayerId, int> totals, PlayerId payer, int amount)
        {
            totals.TryGetValue(payer, out int current);
            totals[payer] = checked(current + amount);
        }
    }

    private static void ApplyEffect(WorkingState working, EffectInstruction instruction)
    {
        switch (instruction)
        {
            case CreditAdjustmentEffect value: ApplyCreditDelta(working, value.Target, value.Delta); break;
            case ClickAdjustmentEffect value: ApplyClickDelta(working, value.Target, value.Delta); break;
            case DamageEffect value: ApplyDamage(working, value); break;
            case TagAdjustmentEffect value: ApplyTags(working, value.Target, value.Delta); break;
            case MemoryAdjustmentEffect value: ApplyBaseMemory(working, value.Target, value.BaseDelta); break;
            case RegisterProgramMemoryEffect value: RegisterProgram(working, value); break;
            case RemoveProgramMemoryEffect value: RemoveProgramRegistration(working, value.CardId); break;
            case CardCounterAdjustmentEffect value: AdjustCounter(working, value.CardId, value.CounterName, value.Delta); break;
            case VirusPurgeEffect: PurgeVirusCounters(working); break;
            case AddLingeringEffect value: AddLingering(working, value.Effect); break;
            case RemoveLingeringEffect value: RemoveLingering(working, value.EffectId); break;
            case MoveCardEffect value: ApplyMoveCard(working, value.CardId, value.DestinationZoneId); break;
            case TurnBoundaryEffect:
            case GameplayOccurrenceEffect:
                break;
            default: throw new ResolutionFailureException($"Unsupported effect instruction '{instruction.GetType().Name}'.");
        }
    }

    private static void ApplyDamage(WorkingState working, DamageEffect effect)
    {
        if (effect.Amount < 0 || !Enum.IsDefined(effect.Kind))
        {
            throw new ResolutionFailureException("Damage kind and amount must be valid.");
        }

        PlayerState target = GetPlayer(working, effect.Target);
        if (target.Side != PlayerSide.Runner)
        {
            throw new ResolutionFailureException("Only the Runner can suffer damage.");
        }

        ZoneState grip = GetZone(working, ZoneKind.RunnerGrip);
        int available = grip.CardIds.Count;
        int suffered = Math.Min(effect.Amount, available);
        var random = DeterministicRandom.Restore(working.RandomState);
        for (int index = 0; index < suffered; index++)
        {
            ZoneState currentGrip = GetZone(working, ZoneKind.RunnerGrip);
            CardInstanceState card = GetCard(working, currentGrip.CardIds[random.NextInt(currentGrip.CardIds.Count)]);
            TrashCard(working, card, currentGrip, GetZone(working, ZoneKind.RunnerHeap));
        }

        working.RandomState = random.State;
        int coreDamageAfter = 0;
        if (effect.Kind == DamageKind.Core && suffered > 0)
        {
            PlayerRulesState current = GetPlayerRules(RequireRulesState(working), effect.Target);
            ReplacePlayerRules(working, new PlayerRulesState(
                current.PlayerId, current.BaseHandSize, checked(current.CoreDamage + effect.Amount),
                current.Tags, current.BaseMemory, current.UsedMemory));
            coreDamageAfter = checked(current.CoreDamage + effect.Amount);
        }
        else
        {
            coreDamageAfter = GetPlayerRules(RequireRulesState(working), effect.Target).CoreDamage;
        }

        bool flatlined = effect.Amount > available;
        Emit(working, EngineEventKinds.DamageDealt, new DamageDealtEventPayload(
            effect.Target,
            effect.Kind,
            effect.Amount,
            suffered,
            coreDamageAfter,
            flatlined));
        if (flatlined)
        {
            CompleteToyGame(working, PlayerSide.Corp, GameWinReason.Flatline);
        }
    }

    private static void ApplyTags(WorkingState working, PlayerId target, int delta)
    {
        if (GetPlayer(working, target).Side != PlayerSide.Runner)
        {
            throw new ResolutionFailureException("Only the Runner can have tags.");
        }

        PlayerRulesState current = GetPlayerRules(RequireRulesState(working), target);
        long next = checked((long)current.Tags + delta);
        if (next is < 0 or > int.MaxValue)
        {
            throw new ResolutionFailureException("Tag adjustment would produce an invalid count.");
        }

        ReplacePlayerRules(working, new PlayerRulesState(
            current.PlayerId, current.BaseHandSize, current.CoreDamage, (int)next,
            current.BaseMemory, current.UsedMemory));
        Emit(working, EngineEventKinds.TagsAdjusted, new TagsAdjustedEventPayload(
            target,
            delta,
            (int)next));
        RecordTurnTagChange(working, target, delta);
    }

    private static void ApplyBaseMemory(WorkingState working, PlayerId target, int delta)
    {
        PlayerRulesState current = GetPlayerRules(RequireRulesState(working), target);
        long next = checked((long)current.BaseMemory + delta);
        if (next is < 0 or > int.MaxValue)
        {
            throw new ResolutionFailureException("Memory adjustment would produce an invalid base memory value.");
        }

        ReplacePlayerRules(working, new PlayerRulesState(
            current.PlayerId, current.BaseHandSize, current.CoreDamage, current.Tags,
            (int)next, current.UsedMemory));
        Emit(working, EngineEventKinds.MemoryAdjusted, new MemoryAdjustedEventPayload(
            target,
            (int)next,
            current.UsedMemory));
    }

    private static void RegisterProgram(WorkingState working, RegisterProgramMemoryEffect effect)
    {
        if (effect.MemoryCost < 0)
        {
            throw new ResolutionFailureException("Program memory cost must be nonnegative.");
        }

        CardInstanceState card = GetCard(working, effect.CardId);
        ZoneState zone = working.Zones.Single(candidate => candidate.Id == card.ZoneId);
        if (GetPlayer(working, effect.Owner).Side != PlayerSide.Runner || zone.Kind != ZoneKind.RunnerRig)
        {
            throw new ResolutionFailureException("Only an installed Runner program can consume memory.");
        }

        RulesState rules = RequireRulesState(working);
        if (rules.Programs.Any(program => program.CardId == effect.CardId))
        {
            throw new ResolutionFailureException($"Program '{effect.CardId}' is already registered for memory.");
        }

        SetRulesState(working, programs: rules.Programs
            .Append(new ProgramMemoryState(effect.CardId, effect.Owner, effect.MemoryCost)).ToArray());
        RecalculateUsedMemory(working);
        PlayerRulesState owner = GetPlayerRules(RequireRulesState(working), effect.Owner);
        Emit(working, EngineEventKinds.MemoryAdjusted, new MemoryAdjustedEventPayload(
            effect.Owner,
            owner.BaseMemory,
            owner.UsedMemory));
    }

    private static void RemoveProgramRegistration(WorkingState working, CardInstanceId cardId)
    {
        RulesState rules = RequireRulesState(working);
        if (!rules.Programs.Any(program => program.CardId == cardId))
        {
            throw new ResolutionFailureException($"Program '{cardId}' has no memory registration.");
        }

        ProgramMemoryState registration = rules.Programs.Single(program => program.CardId == cardId);
        SetRulesState(working, programs: rules.Programs.Where(program => program.CardId != cardId).ToArray());
        RecalculateUsedMemory(working);
        PlayerRulesState owner = GetPlayerRules(RequireRulesState(working), registration.Owner);
        Emit(working, EngineEventKinds.MemoryAdjusted, new MemoryAdjustedEventPayload(
            registration.Owner,
            owner.BaseMemory,
            owner.UsedMemory));
    }

    private static void AdjustCounter(
        WorkingState working,
        CardInstanceId cardId,
        string counterName,
        int delta)
    {
        _ = GetCard(working, cardId);
        if (!StableIdentifier.TryValidate(counterName))
        {
            throw new ResolutionFailureException("Counter name must be a stable nonblank identifier.");
        }

        RulesState rules = RequireRulesState(working);
        CardCounterState? current = rules.Counters.SingleOrDefault(counter =>
            counter.CardId == cardId && string.Equals(counter.CounterName, counterName, StringComparison.Ordinal));
        if (current?.Purpose == CardCounterPurpose.HostedCredit)
        {
            throw new ResolutionFailureException(
                $"Hosted-credit counter '{counterName}' cannot be adjusted by a generic counter effect.");
        }

        long next = checked((long)(current?.Count ?? 0) + delta);
        if (next is < 0 or > int.MaxValue)
        {
            throw new ResolutionFailureException("Counter adjustment would produce an invalid count.");
        }

        var counters = rules.Counters.Where(counter => counter != current).ToList();
        if (next > 0)
        {
            counters.Add(current is null
                ? new CardCounterState(cardId, counterName, (int)next)
                : new CardCounterState(
                    cardId,
                    counterName,
                    (int)next,
                    current.Purpose,
                    current.SpendRestriction));
        }
        SetRulesState(working, counters: counters);
        Emit(working, EngineEventKinds.CounterAdjusted, new CounterAdjustedEventPayload(
            cardId,
            counterName,
            delta,
            (int)next));
    }

    /// <summary>
    /// Applies one typed hosted-credit pool transition. The counter aggregate
    /// and its event are changed together; transfers then credit the recipient
    /// after the pool event so event order follows source debit before wallet
    /// credit. The enclosing reducer discards the whole working copy on any
    /// failure, keeping pool and wallet changes atomic.
    /// </summary>
    private static bool ChangeHostedCreditPool(
        WorkingState working,
        CardInstanceId cardId,
        HostedCreditPoolOperation operation,
        int amount,
        HostedCreditSpendRestriction? declaredRestriction,
        PlayerId? recipient)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ResolutionFailureException($"Hosted-credit operation '{operation}' is not supported.");
        }

        CardInstanceState source = GetCard(working, cardId);
        EnsureRulesState(working);
        RulesState rules = RequireRulesState(working);
        CardCounterState? current = rules.Counters.SingleOrDefault(counter =>
            counter.CardId == cardId
            && string.Equals(counter.CounterName, "credit", StringComparison.Ordinal));
        if (current is not null && current.Purpose != CardCounterPurpose.HostedCredit)
        {
            throw new ResolutionFailureException(
                $"Counter '{current.CounterName}' on '{cardId}' is not a hosted-credit pool.");
        }

        HostedCreditSpendRestriction restriction;
        int nextCount;
        int transitionAmount;

        switch (operation)
        {
            case HostedCreditPoolOperation.Load:
            case HostedCreditPoolOperation.Place:
                if (amount <= 0)
                {
                    throw new ResolutionFailureException("Hosted-credit load and place amounts must be positive.");
                }

                if (declaredRestriction is not { } loadRestriction
                    || !Enum.IsDefined(loadRestriction))
                {
                    throw new ResolutionFailureException(
                        "Hosted-credit load and place operations require a valid spend restriction.");
                }

                if (recipient is not null)
                {
                    throw new ResolutionFailureException(
                        "Hosted-credit load and place operations cannot have a recipient.");
                }

                if (current is not null && current.SpendRestriction != loadRestriction)
                {
                    throw new ResolutionFailureException(
                        $"Hosted-credit pool '{cardId}' has restriction '{current.SpendRestriction}', not '{loadRestriction}'.");
                }

                nextCount = operation == HostedCreditPoolOperation.Load
                    ? CardCounterRules.Load(current, amount)
                    : CardCounterRules.Place(current, amount);
                restriction = current?.SpendRestriction ?? loadRestriction;
                transitionAmount = amount;
                break;

            case HostedCreditPoolOperation.Take:
                if (amount <= 0)
                {
                    throw new ResolutionFailureException("Hosted-credit take amounts must be positive.");
                }

                if (declaredRestriction is not null)
                {
                    throw new ResolutionFailureException("Hosted-credit take operations do not declare a restriction.");
                }

                if (recipient is not { } takeRecipient)
                {
                    throw new ResolutionFailureException("Hosted-credit take operations require a recipient.");
                }

                _ = GetPlayer(working, takeRecipient);
                if (current?.SpendRestriction is not { } takeRestriction)
                {
                    throw new ResolutionFailureException($"Card '{cardId}' has no hosted-credit pool restriction.");
                }

                nextCount = current is null
                    ? throw new ResolutionFailureException($"Card '{cardId}' has no hosted-credit pool.")
                    : CardCounterRules.Take(current, amount);
                restriction = takeRestriction;
                transitionAmount = -amount;
                break;

            case HostedCreditPoolOperation.TakeUpTo:
                if (amount <= 0)
                {
                    throw new ResolutionFailureException("Hosted-credit take-up-to amounts must be positive.");
                }

                if (declaredRestriction is not null)
                {
                    throw new ResolutionFailureException("Hosted-credit take-up-to operations do not declare a restriction.");
                }

                if (recipient is not { } takeUpToRecipient)
                {
                    throw new ResolutionFailureException("Hosted-credit take-up-to operations require a recipient.");
                }

                _ = GetPlayer(working, takeUpToRecipient);
                if (current is null)
                {
                    // A mandatory take-up-to operation is a successful no-op
                    // when its source has no hosted pool. There is no debit,
                    // wallet transfer, empty occurrence, or event to record.
                    return false;
                }

                if (current.SpendRestriction is not { } takeUpToRestriction)
                {
                    throw new ResolutionFailureException($"Card '{cardId}' has no hosted-credit pool restriction.");
                }

                int takeUpToAmount = Math.Min(amount, current.Count);
                nextCount = CardCounterRules.Take(current, takeUpToAmount);
                restriction = takeUpToRestriction;
                transitionAmount = -takeUpToAmount;
                break;

            case HostedCreditPoolOperation.TakeAll:
                if (amount != 0)
                {
                    throw new ResolutionFailureException("TakeAllHostedCredits does not carry an amount.");
                }

                if (declaredRestriction is not null)
                {
                    throw new ResolutionFailureException("Hosted-credit take-all operations do not declare a restriction.");
                }

                if (recipient is not { } takeAllRecipient)
                {
                    throw new ResolutionFailureException("Hosted-credit take-all operations require a recipient.");
                }

                _ = GetPlayer(working, takeAllRecipient);
                if (current?.SpendRestriction is not { } takeAllRestriction)
                {
                    throw new ResolutionFailureException($"Card '{cardId}' has no hosted-credit pool restriction.");
                }

                if (current.Count <= 0)
                {
                    throw new ResolutionFailureException($"Card '{cardId}' has no hosted credits to take.");
                }

                transitionAmount = -current.Count;
                nextCount = CardCounterRules.Take(current, current.Count);
                restriction = takeAllRestriction;
                break;

            case HostedCreditPoolOperation.Spend:
                if (amount <= 0)
                {
                    throw new ResolutionFailureException("Hosted-credit spend amounts must be positive.");
                }

                if (declaredRestriction is not { } spendRestriction
                    || spendRestriction is HostedCreditSpendRestriction.CardEffectOnly
                    or HostedCreditSpendRestriction.AnyCost)
                {
                    throw new ResolutionFailureException(
                        "Hosted-credit spend operations require a concrete cost restriction.");
                }

                if (recipient is not null)
                {
                    throw new ResolutionFailureException("Hosted-credit spend operations cannot have a recipient.");
                }

                if (current?.SpendRestriction is not { } currentRestriction)
                {
                    throw new ResolutionFailureException($"Card '{cardId}' has no hosted-credit pool restriction.");
                }

                nextCount = current is null
                    ? throw new ResolutionFailureException($"Card '{cardId}' has no hosted-credit pool.")
                    : CardCounterRules.Spend(current, amount, spendRestriction);
                restriction = currentRestriction;
                transitionAmount = -amount;
                break;

            default:
                throw new ResolutionFailureException($"Hosted-credit operation '{operation}' is not executable.");
        }

        if (recipient is { } recipientId)
        {
            PlayerState player = GetPlayer(working, recipientId);
            if (checked((long)player.Credits + -transitionAmount) > int.MaxValue)
            {
                throw new ResolutionFailureException("Hosted-credit transfer would overflow the recipient wallet.");
            }
        }

        var counters = rules.Counters.ToList();
        int counterIndex = current is null
            ? -1
            : counters.Select((counter, index) => (counter, index))
                .Where(value => value.counter == current)
                .Select(value => value.index)
                .DefaultIfEmpty(-1)
                .First();
        if (nextCount == 0)
        {
            if (counterIndex >= 0)
            {
                counters.RemoveAt(counterIndex);
            }
        }
        else
        {
            CardCounterState replacement = CardCounterState.CreateHostedCredit(cardId, nextCount, restriction);
            if (counterIndex >= 0)
            {
                counters[counterIndex] = replacement;
            }
            else
            {
                counters.Add(replacement);
            }
        }

        SetRulesState(working, counters: counters);
        Emit(
            working,
            EngineEventKinds.HostedCreditPoolChanged,
            new HostedCreditPoolChangedEventPayload(
                operation,
                source.Id,
                transitionAmount,
                nextCount,
                restriction,
                transitionAmount < 0 && nextCount == 0,
                recipient));

        if (recipient is { } transferRecipient)
        {
            ApplyCreditDelta(working, transferRecipient, checked(-transitionAmount));
        }

        return transitionAmount < 0 && nextCount == 0;
    }

    private static void PurgeVirusCounters(WorkingState working)
    {
        RulesState rules = RequireRulesState(working);
        long removed = 0;
        foreach (CardCounterState counter in rules.Counters)
        {
            if (counter.Purpose == CardCounterPurpose.Virus)
            {
                removed = checked(removed + counter.Count);
            }
        }

        if (removed is < 0 or > int.MaxValue)
        {
            throw new ResolutionFailureException(
                "The virus-counter total does not fit the event payload.");
        }

        SetRulesState(working, counters: rules.Counters
            .Where(counter => counter.Purpose != CardCounterPurpose.Virus).ToArray());
        Emit(working, EngineEventKinds.VirusPurged, new VirusPurgedEventPayload((int)removed));
    }

    private static void AddLingering(WorkingState working, GameLingeringEffect effect)
    {
        RulesState rules = RequireRulesState(working);
        if (rules.LingeringEffects.Any(candidate => candidate.EffectId == effect.EffectId))
        {
            throw new ResolutionFailureException($"Lingering effect '{effect.EffectId}' already exists.");
        }

        SetRulesState(working, lingeringEffects: rules.LingeringEffects.Append(effect).ToArray());
        Emit(working, EngineEventKinds.LingeringEffectAdded, new LingeringEffectAddedEventPayload(
            effect.EffectId,
            effect.BehaviorId,
            effect.Expiry));
    }

    private static void RemoveLingering(WorkingState working, RunEffectId effectId)
    {
        RulesState rules = RequireRulesState(working);
        if (!rules.LingeringEffects.Any(effect => effect.EffectId == effectId))
        {
            throw new ResolutionFailureException($"Lingering effect '{effectId}' does not exist.");
        }

        SetRulesState(working, lingeringEffects: rules.LingeringEffects
            .Where(effect => effect.EffectId != effectId).ToArray());
        Emit(working, EngineEventKinds.LingeringEffectRemoved, new LingeringEffectRemovedEventPayload(effectId));
    }

    private static void ApplyMoveCard(WorkingState working, CardInstanceId cardId, ZoneId destinationZoneId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ZoneState source = working.Zones.SingleOrDefault(zone => zone.Id == card.ZoneId)
            ?? throw new ResolutionFailureException($"Card '{cardId}' has no source zone.");
        ZoneState destination = working.Zones.SingleOrDefault(zone => zone.Id == destinationZoneId)
            ?? throw new ResolutionFailureException($"Destination zone '{destinationZoneId}' does not exist.");
        MoveCard(working, card, source, destination);
        if (working.RulesState?.Programs.Any(program => program.CardId == cardId) == true
            && destination.Kind != ZoneKind.RunnerRig)
        {
            RemoveProgramRegistration(working, cardId);
        }
    }

    private static void ApplyClickDelta(WorkingState working, PlayerId target, int delta)
    {
        PlayerState player = GetPlayer(working, target);
        long next = checked((long)player.Clicks + delta);
        if (next is < 0 or > int.MaxValue)
        {
            throw new ResolutionFailureException("Click adjustment would produce an invalid value.");
        }

        ReplacePlayer(working, new PlayerState(
            player.Id, player.Side, player.Credits, (int)next, player.AgendaPoints));
        Emit(working, EngineEventKinds.ClicksAdjusted, new ClicksAdjustedEventPayload(
            target,
            delta,
            (int)next));
    }

    private static PlayerState GetPlayer(WorkingState working, PlayerId id) =>
        working.Players.SingleOrDefault(player => player.Id == id)
        ?? throw new ResolutionFailureException($"Player '{id}' does not exist.");

    private static void EnsureRulesState(WorkingState working) =>
        working.RulesState ??= RulesState.CreateDefault(working.Players);

    private static RulesState RequireRulesState(WorkingState working) =>
        working.RulesState ?? throw new ResolutionFailureException("This effect requires initialized M2 rules state.");

    private static PlayerRulesState GetPlayerRules(RulesState rules, PlayerId playerId) =>
        rules.Players.SingleOrDefault(player => player.PlayerId == playerId)
        ?? throw new ResolutionFailureException($"Player '{playerId}' has no M2 rules state.");

    private static void ReplacePlayerRules(WorkingState working, PlayerRulesState replacement)
    {
        RulesState rules = RequireRulesState(working);
        SetRulesState(working, players: rules.Players
            .Select(player => player.PlayerId == replacement.PlayerId ? replacement : player).ToArray());
    }

    private static void RecalculateUsedMemory(WorkingState working)
    {
        RulesState rules = RequireRulesState(working);
        PlayerRulesState[] players = rules.Players.Select(player => new PlayerRulesState(
            player.PlayerId, player.BaseHandSize, player.CoreDamage, player.Tags, player.BaseMemory,
            rules.Programs.Where(program => program.Owner == player.PlayerId).Sum(program => program.MemoryCost)))
            .ToArray();
        SetRulesState(working, players: players);
    }

    private static ProgramMemoryState[] MemoryCandidates(WorkingState working, PlayerId owner)
    {
        RulesState rules = RequireRulesState(working);
        ZoneState rig = GetZone(working, ZoneKind.RunnerRig);
        return rules.Programs
            .Where(program => program.Owner == owner && rig.CardIds.Contains(program.CardId))
            .OrderBy(program => program.CardId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool SameProgramCandidates(
        IReadOnlyList<ProgramMemoryState> left,
        ProgramMemoryState[] right) =>
        left.Count == right.Length && left.Zip(right).All(pair => pair.First == pair.Second);

    private static void SetRulesState(
        WorkingState working,
        IReadOnlyList<PlayerRulesState>? players = null,
        IReadOnlyList<ProgramMemoryState>? programs = null,
        IReadOnlyList<CardCounterState>? counters = null,
        IReadOnlyList<GameLingeringEffect>? lingeringEffects = null,
        IReadOnlyList<TriggerRegistration>? triggers = null,
        IReadOnlyList<InterruptRegistration>? interrupts = null)
    {
        RulesState rules = RequireRulesState(working);
        working.RulesState = new RulesState(
            rules.Version,
            players ?? rules.Players,
            programs ?? rules.Programs,
            counters ?? rules.Counters,
            lingeringEffects ?? rules.LingeringEffects,
            triggers ?? rules.Triggers,
            interrupts ?? rules.Interrupts);
    }

    private static void ReplaceTopFrame(WorkingState working, ResolutionFrame frame)
    {
        if (working.ResolutionStack.Count == 0)
        {
            throw new ResolutionFailureException("A resolution frame cannot be replaced on an empty stack.");
        }

        working.ResolutionStack[^1] = frame;
    }
}
