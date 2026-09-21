using System.Globalization;

namespace Netrunner.Core;

/// <summary>Catalog-driven interpreter for declarative card behavior trees.</summary>
public static partial class GameEngine
{
    private const int MaxMaterializedBehaviorChoices = 256;

    /// <summary>
    /// Internal composition seam used by Core's future play/install reducers.
    /// The caller has already performed the legal card action and is handing
    /// an engine-owned invocation into the same atomic working state. Public
    /// callers cannot manufacture this boundary.
    /// </summary>
    internal static GameState BeginBehaviorFromEngine(
        GameState state,
        BehaviorInvocationContext invocation,
        IBehaviorCatalog behaviorCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginBehaviorCore(
            state,
            invocation,
            behaviorCatalog,
            enforceCatalogCompatibility: true,
            maxResolutionSteps,
            engineIssuedInvocation: true,
            allowHandSourceForTest: false);

    /// <summary>
    /// Explicitly non-production fixture boundary. It is friend-visible only
    /// to Core tests and permits a synthetic hand source so runtime tests can
    /// exercise a behavior without implementing ENG-405 card play first.
    /// </summary>
    internal static GameState BeginBehaviorForTesting(
        GameState state,
        BehaviorInvocationContext invocation,
        IBehaviorCatalog behaviorCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginBehaviorCore(
            state,
            invocation,
            behaviorCatalog,
            enforceCatalogCompatibility: true,
            maxResolutionSteps,
            engineIssuedInvocation: true,
            allowHandSourceForTest: true);

    /// <summary>
    /// Explicitly non-production child-push fixture boundary. It requires an
    /// already active parent frame and routes through the same private
    /// WorkingState push/resume seam used by ENG-405 composition. The friend
    /// test assembly is the only caller that may use a synthetic hand source.
    /// </summary>
    internal static GameState BeginBehaviorChildForTesting(
        GameState state,
        BehaviorInvocationContext invocation,
        IBehaviorCatalog behaviorCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginBehaviorCore(
            state,
            invocation,
            behaviorCatalog,
            enforceCatalogCompatibility: true,
            maxResolutionSteps,
            engineIssuedInvocation: true,
            allowHandSourceForTest: true,
            requireExistingParent: true);

    private static GameState BeginBehaviorCore(
        GameState state,
        BehaviorInvocationContext invocation,
        IBehaviorCatalog behaviorCatalog,
        bool enforceCatalogCompatibility,
        int maxResolutionSteps,
        bool engineIssuedInvocation,
        bool allowHandSourceForTest,
        bool requireExistingParent = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        if (enforceCatalogCompatibility
            && !CatalogMatchesState(state, behaviorCatalog, out string? catalogMismatch))
        {
            throw new ResolutionFailureException(catalogMismatch!);
        }

        StateInvariantValidator.Validate(state, enforceCatalogCompatibility ? behaviorCatalog : null);
        if (state.Outcome.Kind == GameOutcomeKind.Won)
        {
            throw new ResolutionFailureException("A terminal state cannot begin a card behavior.");
        }

        if (state.PendingDecision is not null)
        {
            throw new ResolutionFailureException("A card behavior cannot begin while a decision is pending.");
        }

        if (requireExistingParent && state.ResolutionStack.Count == 0)
        {
            throw new ResolutionFailureException(
                "A behavior child requires an already active parent resolution frame.");
        }

        if (maxResolutionSteps <= 0)
        {
            throw new ResolutionStepBudgetExceededException(maxResolutionSteps);
        }

        if (!engineIssuedInvocation)
        {
            throw new ResolutionFailureException(
                "Generic card behaviors may only be opened by an engine-owned card play/install invocation.");
        }

        CardInstanceState source = state.GetCard(invocation.SourceCardId);
        ZoneState sourceZone = state.Zones.SingleOrDefault(candidate => candidate.Id == source.ZoneId)
            ?? throw new ResolutionFailureException($"Behavior source card '{source.Id}' references an unknown zone.");
        if (!IsBehaviorActivationZone(state, source)
            || (!allowHandSourceForTest
                && sourceZone.Kind is ZoneKind.CorpHand or ZoneKind.RunnerGrip))
        {
            throw new ResolutionFailureException(
                $"Behavior source card '{source.Id}' is not in an activatable zone; generic behavior may not be fired from a deck, discard, score, or heap.");
        }
        if (!behaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition))
        {
            throw new ResolutionFailureException(
                $"Card definition '{source.DefinitionId}' has no generic behavior in the injected catalog.");
        }

        PlayerState controller = state.GetPlayer(invocation.Controller);
        if (controller.Side != definition.Controller)
        {
            throw new ResolutionFailureException(
                $"Behavior '{source.DefinitionId}' requires a '{definition.Controller}' controller.");
        }

        if (source.OwnerSide != controller.Side)
        {
            throw new ResolutionFailureException(
                $"Behavior source card '{source.Id}' is owned by '{source.OwnerSide}' and cannot be invoked by '{controller.Side}'.");
        }

        PlayerId actingPlayer = invocation.ActingPlayer ??
            state.Players.Single(player => player.Side == source.OwnerSide).Id;
        _ = state.GetPlayer(actingPlayer);

        var working = WorkingState.From(state, behaviorCatalog);
        try
        {
            PushBehaviorChild(
                working,
                new BehaviorInvocationContext(source.Id, controller.Id, actingPlayer),
                definition,
                sourceZone);
            ResumeBehaviorChild(working, maxResolutionSteps);
            GameState next = working.Build(state);
            StateInvariantValidator.Validate(next, enforceCatalogCompatibility ? behaviorCatalog : null);
            return next;
        }
        catch (ResolutionFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is StateInvariantException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new ResolutionFailureException(
                $"The card behavior could not be started: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Internal composition seam for ENG-405 play/install reducers. The
    /// caller supplies an already-authorized invocation in the same atomic
    /// working state; this method captures the exact source location and
    /// immediate parent frame identity before pushing the child.
    /// </summary>
    private static BehaviorResolutionFrame PushBehaviorChild(
        WorkingState working,
        BehaviorInvocationContext invocation,
        CardBehaviorDefinition definition,
        ZoneState sourceZone)
    {
        ResolutionFrame? parent = working.ResolutionStack.Count == 0
            ? null
            : working.ResolutionStack[^1];
        var frame = new BehaviorResolutionFrame(
            definition,
            invocation,
            working.BehaviorCatalog.Fingerprint,
            working.BehaviorCatalog.GetDefinitionFingerprint(definition.DefinitionId),
            working.Allocator.NextBehaviorFrameId(),
            working.Cards.Single(card => card.Id == invocation.SourceCardId).ZoneId,
            sourceZone.Kind,
            parent?.BehaviorId,
            parent is null ? null : BehaviorFrameIdentity.Compute(parent));
        working.ResolutionStack.Add(frame);
        return frame;
    }

    /// <summary>
    /// Resume seam paired with <see cref="PushBehaviorChild"/>. It is kept
    /// private to the Core partial so future reducers cannot bypass the
    /// engine-owned invocation checks or push a forgeable serialized program.
    /// </summary>
    private static void ResumeBehaviorChild(WorkingState working, int maxResolutionSteps)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not BehaviorResolutionFrame)
        {
            throw new ResolutionFailureException("A behavior child cannot resume without an active behavior frame.");
        }

        Advance(working, maxResolutionSteps);
    }

    private static bool IsBehaviorActivationZone(GameState state, CardInstanceState source)
    {
        ZoneState zone = state.Zones.SingleOrDefault(candidate => candidate.Id == source.ZoneId)
            ?? throw new ResolutionFailureException($"Behavior source card '{source.Id}' references an unknown zone.");
        return source.OwnerSide switch
        {
            PlayerSide.Corp => zone.Kind is ZoneKind.CorpHand or ZoneKind.CorpPlayArea or ZoneKind.ServerRoot or ZoneKind.ServerIce,
            PlayerSide.Runner => zone.Kind is ZoneKind.RunnerGrip or ZoneKind.RunnerRig or ZoneKind.RunnerPlayArea,
            _ => false,
        };
    }

    private static void StepBehaviorResolution(WorkingState working, BehaviorResolutionFrame frame)
    {
        ValidateBehaviorRuntimeFrame(working, frame);
        if (frame.Stage == BehaviorResolutionStage.AwaitingOccurrence)
        {
            throw new ResolutionFailureException(
                "A behavior occurrence continuation cannot resume before its nested occurrence child completes.");
        }

        List<BehaviorInstruction> instructions = GetBehaviorInstructions(working, frame);
        if (frame.Stage == BehaviorResolutionStage.PayCosts)
        {
            CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
            PayBehaviorCosts(working, frame, definition);
            ApplyStaticModifiers(working, frame, definition);
            ReplaceTopFrame(working, frame.With(stage: BehaviorResolutionStage.Execute));
            return;
        }

        if (frame.Choice is not null)
        {
            throw new ResolutionFailureException("A behavior frame with a materialized choice cannot execute concurrently.");
        }

        if (frame.InstructionCursor >= instructions.Count)
        {
            // A search selection may be revealed without a subsequent move.
            // Reinsert every remaining selected card into its frozen source
            // zone before completing the frame, restoring private visibility
            // and the original order.
            RestoreBehaviorSetAside(working, frame);

            // A source counter retained across a self-movement ability is
            // consumed only by that ability's child. The parent ability
            // clears the counters at its own completion boundary; a
            // top-level behavior has no such parent and therefore performs
            // the cleanup here.
            if (frame.AbilityKey is null
                && ShouldRetainSourceCountersThroughChild(working, frame))
            {
                ClearCardCounters(working, frame.SourceCardId);
            }

            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            return;
        }

        BehaviorInstruction instruction = instructions[frame.InstructionCursor];
        switch (instruction.Kind)
        {
            case BehaviorInstructionKind.Choose:
                YieldBehaviorChoice(working, frame, instruction);
                return;
            case BehaviorInstructionKind.Search:
                YieldBehaviorSearch(working, frame, instruction);
                return;
            case BehaviorInstructionKind.Branch:
                ResolveBehaviorBranch(working, frame, instruction);
                return;
            case BehaviorInstructionKind.GainCreditsPerAccess:
                ResolveRunAccessCredits(working, frame);
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.SwapInstalledIce:
                ResolveSwapInstalledIce(working, frame, instruction);
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.GainCredits:
                int creditGain = instruction.AmountOperand is null
                    ? instruction.Amount
                    : ResolveBehaviorDynamicAmount(working, frame, instruction);
                ApplyCreditDeltaIfNonZero(working, frame.Controller, creditGain);
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.LoseCredits:
                ApplyCreditDelta(working, frame.Controller, -instruction.Amount);
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.GainClicks:
                ApplyClickDelta(working, frame.Controller, instruction.Amount);
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.LoseClicks:
                if (instruction.Condition is null || BehaviorConditionMatches(working, frame, instruction.Condition))
                {
                    ApplyClickDelta(working, frame.Controller, -instruction.Amount);
                }

                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.Draw:
                ResolveBehaviorDraw(working, frame, instruction.Amount);
                if (working.Outcome.Kind == GameOutcomeKind.Won)
                {
                    return;
                }

                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.Reveal:
                foreach (CardInstanceId cardId in ResolveBehaviorCards(working, frame, instruction.Target))
                {
                    CardInstanceState card = GetCard(working, cardId);
                    ZoneState source = ZoneForCard(working, cardId);
                    ZoneId evidenceSource = frame.SetAside
                        .SingleOrDefault(selected => selected.CardId == cardId)?.OriginalZoneId
                        ?? source.Id;
                    Emit(
                        working,
                        EngineEventKinds.BehaviorCardRevealed,
                        new BehaviorCardRevealedEventPayload(
                            card.Id,
                            card.DefinitionId,
                            evidenceSource,
                            card.OwnerSide));
                }

                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.ShuffleSearchedDeck:
                ResolveSearchedDeckShuffle(working, frame, instruction);
                return;
            case BehaviorInstructionKind.ShuffleIntoDeck:
                ResolveBehaviorTransfer(working, frame, instruction, BehaviorTransferKind.Shuffle);
                return;
            case BehaviorInstructionKind.AddToHand:
                ResolveBehaviorTransfer(working, frame, instruction, BehaviorTransferKind.AddToHand);
                return;
            case BehaviorInstructionKind.TrashCard:
                ResolveBehaviorTransfer(working, frame, instruction, BehaviorTransferKind.Trash);
                return;
            case BehaviorInstructionKind.InstallCard:
                ResolveBehaviorInstall(working, frame, instruction);
                return;
            case BehaviorInstructionKind.AdvanceCard:
                ResolveBehaviorAdvance(working, frame, instruction);
                return;
            case BehaviorInstructionKind.MoveCard:
                ResolveBehaviorTransfer(working, frame, instruction, BehaviorTransferKind.Move);
                return;
            case BehaviorInstructionKind.LoadHostedCredits:
                ChangeHostedCreditPool(
                    working,
                    frame.SourceCardId,
                    HostedCreditPoolOperation.Load,
                    instruction.Amount,
                    instruction.SpendRestriction,
                    recipient: null);
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.PlaceHostedCredits:
                ChangeHostedCreditPool(
                    working,
                    frame.SourceCardId,
                    HostedCreditPoolOperation.Place,
                    instruction.Amount,
                    instruction.SpendRestriction,
                    recipient: null);
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.TakeHostedCredits:
                bool poolEmptied = ChangeHostedCreditPool(
                    working,
                    frame.SourceCardId,
                    HostedCreditPoolOperation.Take,
                    instruction.Amount,
                    declaredRestriction: null,
                    recipient: frame.Controller);
                if (poolEmptied)
                {
                    QueueHostedCreditPoolEmptiedOccurrence(working, frame);
                }
                else
                {
                    AdvanceBehaviorCursor(working, frame);
                }

                return;
            case BehaviorInstructionKind.TakeUpToHostedCredits:
                bool poolEmptiedByTakeUpTo = ChangeHostedCreditPool(
                    working,
                    frame.SourceCardId,
                    HostedCreditPoolOperation.TakeUpTo,
                    instruction.Amount,
                    declaredRestriction: null,
                    recipient: frame.Controller);
                if (poolEmptiedByTakeUpTo)
                {
                    QueueHostedCreditPoolEmptiedOccurrence(working, frame);
                }
                else
                {
                    AdvanceBehaviorCursor(working, frame);
                }

                return;
            case BehaviorInstructionKind.TakeAllHostedCredits:
                bool poolEmptiedByTakeAll = ChangeHostedCreditPool(
                    working,
                    frame.SourceCardId,
                    HostedCreditPoolOperation.TakeAll,
                    amount: 0,
                    declaredRestriction: null,
                    recipient: frame.Controller);
                if (poolEmptiedByTakeAll)
                {
                    QueueHostedCreditPoolEmptiedOccurrence(working, frame);
                }
                else
                {
                    AdvanceBehaviorCursor(working, frame);
                }

                return;
            case BehaviorInstructionKind.ModifyEncounteredIceStrength:
                ResolveBehaviorModifyEncounteredIceStrength(working, frame, instruction);
                return;
            case BehaviorInstructionKind.AddCounter:
                IReadOnlyList<CardInstanceId> counterTargets = instruction.Target is null
                    || instruction.Target.Kind == BehaviorTargetKind.Self
                    ? [frame.SourceCardId]
                    : ResolveBehaviorCards(working, frame, instruction.Target);
                if (counterTargets.Count != 1)
                {
                    throw new ResolutionFailureException(
                        "A counter instruction must resolve exactly one card target.");
                }

                AdjustCounter(working, counterTargets[0], instruction.Argument ?? "counter", instruction.Amount);
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.AddTags:
                PlayerId tagRecipient = instruction.Recipient switch
                {
                    null or BehaviorPlayerSelector.Controller => frame.Controller,
                    BehaviorPlayerSelector.ActingPlayer => frame.ActingPlayer
                        ?? throw new ResolutionFailureException(
                            "An AddTags instruction targeting ActingPlayer requires an acting player."),
                    BehaviorPlayerSelector.Corp => GetPlayer(working, PlayerSide.Corp).Id,
                    BehaviorPlayerSelector.Runner => GetPlayer(working, PlayerSide.Runner).Id,
                    _ => throw new ResolutionFailureException("The AddTags recipient is invalid."),
                };
                AdvanceBehaviorCursor(working, frame);
                working.ResolutionStack.Add(new EffectResolutionFrame(frame.Controller, new CostBundle(),
                    new EffectBundle(new TagAdjustmentEffect(tagRecipient, instruction.Amount))));
                return;
            case BehaviorInstructionKind.TrashSelf:
                ResolveBehaviorTransfer(
                    working,
                    frame,
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: new BehaviorTarget(BehaviorTargetKind.Self)),
                    BehaviorTransferKind.Trash);
                return;
            case BehaviorInstructionKind.ProhibitScoringThisTurn:
                MarkScoreProhibition(working, frame.SourceCardId);
                AdvanceBehaviorCursor(working, frame);
                break;
            case BehaviorInstructionKind.RezIgnoringCosts:
                ResolveBehaviorRezIgnoringCosts(working, frame, instruction);
                break;
            case BehaviorInstructionKind.NoOp:
                AdvanceBehaviorCursor(working, frame);
                return;
            case BehaviorInstructionKind.StartRun:
                ResolveBehaviorStartRun(working, frame, instruction, instructions);
                return;
            case BehaviorInstructionKind.EndRun:
                ResolveBehaviorEndRun(working, frame);
                return;
            case BehaviorInstructionKind.DealDamage:
                ResolveBehaviorDamage(working, frame, instruction);
                return;
            default:
                throw new ResolutionFailureException($"Behavior instruction '{instruction.Kind}' is not executable.");
        }
    }

    private static void ResolveBehaviorDamage(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        int amount = instruction.Amount;
        if (instruction.DamageAmountOperand is { } operand)
        {
            int variable = operand.Kind switch
            {
                BehaviorDamageAmountKind.SourceAdvancementCounters => GetCard(working, frame.SourceCardId).AdvancementCounters,
                BehaviorDamageAmountKind.ScoredAgendaPrintedPointsThisTurn =>
                    working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.AgendaTurnChanges
                        .Where(change => change.Kind == AgendaTurnChangeKind.Score
                            && change.BeneficiarySide == PlayerSide.Corp)
                        .Sum(change => working.RuntimeCardCatalog is { } catalog
                            && catalog.TryGet(GetCard(working, change.CardId).DefinitionId, out CardRuntimeSpec? spec)
                                ? spec.AgendaPoints
                                : change.Points) ?? 0,
                _ => throw new ResolutionFailureException("Unknown damage amount operand."),
            };
            amount = checked(operand.BaseAmount + variable);
            if (operand.TrashSourceAsCost)
            {
                if (!IsInstalledCard(working, frame.SourceCardId))
                {
                    throw new ResolutionFailureException("The damage source is no longer installed to pay its trash cost.");
                }
                MoveBehaviorCardToTrash(working, GetCard(working, frame.SourceCardId));
                ClearCardCounters(working, frame.SourceCardId);
            }
        }

        // Persist the computed amount in the effect child before any damage
        // prevention or reaction can run. Self-trash clears live counters.
        AdvanceBehaviorCursor(working, frame);
        if (amount == 0)
        {
            return;
        }
        working.ResolutionStack.Add(new EffectResolutionFrame(
            frame.Controller,
            new CostBundle(),
            new EffectBundle(new DamageEffect(
                GetPlayer(working, PlayerSide.Runner).Id,
                instruction.DamageKind ?? throw new ResolutionFailureException("Damage requires a typed kind."),
                amount))));
    }

    private static void ResolveBehaviorStartRun(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction,
        List<BehaviorInstruction> instructions)
    {
        if (frame.AbilityKey is null
            || instruction.RunOperand is not { } operand
            || frame.InstructionCursor != instructions.Count - 1
            || working.ResolutionStack.Count != 3
            || working.ResolutionStack[^2] is not AbilityFrame ability
            || working.ResolutionStack[^3] is not TurnFrame turn
            || ability.Activation != BehaviorAbilityActivation.ClickAction
            || ability.Stage != AbilityLifecycleStage.AwaitingChild
            || turn.ActiveSide != PlayerSide.Runner
            || turn.Stage != TurnFrameStage.AwaitingAction
            || working.RunState is not null)
        {
            throw new ResolutionFailureException(
                "StartRun must be the final instruction of a click ability under an AwaitingAction Runner turn.");
        }

        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
        if (!BehaviorRunRules.TryGetFinalStartRun(declaration, out BehaviorRunOperand declared)
            || !Equals(declared, operand)
            || ability.SourceCardId != frame.SourceCardId
            || ability.DefinitionId != frame.DefinitionId
            || ability.Controller != frame.Controller)
        {
            throw new ResolutionFailureException(
                "The StartRun instruction no longer matches its exact paid-ability declaration.");
        }

        CardInstanceState source = GetCard(working, frame.SourceCardId);
        if (!CanStartRunAbility(working, source, definition, operand, out ServerState? server)
            || server is null)
        {
            throw new ResolutionFailureException(
                "The StartRun ability is no longer legal for its source, server topology, or turn facts.");
        }

        // Payment and once-per-turn usage have already committed in the
        // AbilityFrame. Retire both cursors before starting the ordinary run,
        // leaving the original AwaitingAction turn frame untouched beneath it.
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        StartRunInWorkingState(
            working,
            server.Id,
            GetPlayer(working, PlayerSide.Runner).Id,
            frame.SourceCardId,
            operand);
    }

    /// <summary>
    /// Ends a live run from an engine-owned catalog reaction. The behavior
    /// child is deliberately allowed to sit above the run timing frame (and
    /// its occurrence/checkpoint parents), so ending the run must first
    /// discard that stale reaction suffix and then enter the ordinary
    /// serialized RunEnd cleanup exactly once.
    /// </summary>
    private static void ResolveBehaviorEndRun(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        if (frame.TriggerKey is null || frame.AbilityKey is not null)
        {
            throw new ResolutionFailureException(
                "EndRun is executable only from an engine-owned catalog reaction.");
        }

        RunState run = working.RunState
            ?? throw new ResolutionFailureException("EndRun requires a live run.");
        int runFrameIndex = -1;
        for (int index = working.ResolutionStack.Count - 1; index >= 0; index--)
        {
            if (working.ResolutionStack[index] is RunTimingFrame timing
                && timing.RunId == run.Id
                && timing.Step == run.Step)
            {
                runFrameIndex = index;
                break;
            }
        }

        if (runFrameIndex < 0
            || working.ResolutionStack[^1] is not BehaviorResolutionFrame active
            || !string.Equals(active.FrameIdentity, frame.FrameIdentity, StringComparison.Ordinal)
            || runFrameIndex >= working.ResolutionStack.Count - 1)
        {
            throw new ResolutionFailureException(
                "EndRun requires the exact live run timing frame beneath its reaction child.");
        }

        // The immediate parent must be an effect checkpoint for a gameplay
        // occurrence tied to this run. This excludes a forgeable standalone
        // behavior frame and prevents an old/non-run trigger from ending a
        // later run.
        if (working.ResolutionStack[^2] is not EffectCheckpointFrame checkpoint
            || checkpoint.ChildKind != EffectCheckpointChildKind.Trigger
            || checkpoint.Occurrence.Effect is not GameplayOccurrenceEffect occurrence
            || occurrence.ServerId != run.AttackedServer
            || occurrence.Kind == OccurrenceKind.RunEnded
            || checkpoint.ChildTriggerId is not { } childTriggerId
            || !checkpoint.TriggerQueue.Any(invocation =>
                invocation.Id == childTriggerId
                && invocation.IsCatalogTrigger
                && string.Equals(invocation.TriggerKey, frame.TriggerKey, StringComparison.Ordinal)
                && invocation.SourceCardId == frame.SourceCardId
                && invocation.SourceDefinitionId == frame.DefinitionId
                && invocation.Controller == frame.Controller))
        {
            throw new ResolutionFailureException(
                "EndRun requires a current run-owned catalog reaction occurrence.");
        }

        RunState ending = run.WithTiming(
            RunTimingStep.RunEnd,
            -1,
            null,
            run.Successful,
            true,
            RunEndReason.EndedByRule,
            null,
            run.Encounter,
            run.AccessState);
        working.RunState = ending;
        working.PendingDecision = null;

        // Remove the behavior child and every reaction/checkpoint frame above
        // the authoritative run cursor. CompleteRun expects that cursor to
        // be the top frame and owns all normal event/turn cleanup from there.
        while (working.ResolutionStack.Count - 1 > runFrameIndex)
        {
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        }

        CompleteRun(working, ending);
    }

    private enum BehaviorTransferKind
    {
        Move,
        Shuffle,
        AddToHand,
        Trash,
    }

    /// <summary>
    /// Expands a declarative InstallCard instruction into the authoritative
    /// Runner install cursor.  The instruction consumes one stable binding
    /// proven to originate in a reachable hidden-zone choice and always uses
    /// the ordinary Runner install pipeline.  Hidden Grip selections remain
    /// in the transient set-aside zone while the choice is pending; they are
    /// restored to Grip immediately before the install frame is created so
    /// the normal install reducer owns custody and privacy from that point.
    /// </summary>
    private static void ResolveBehaviorInstall(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        if (instruction.Optional
            && instruction.Target is { Kind: BehaviorTargetKind.ChosenCard } optionalTarget
            && IsProvenEmptyChosenCard(frame, optionalTarget))
        {
            AdvanceBehaviorCursor(working, frame);
            return;
        }

        BehaviorResolutionFrame preparedFrame = RestoreBehaviorInstallSelection(
            working,
            frame,
            instruction);
        InstallPlan plan = ValidateBehaviorInstallInstruction(working, preparedFrame, instruction);
        var installFrame = new InstallFrame(
            preparedFrame.Controller,
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
            InstallInitiationKind.BehaviorEffect,
            BehaviorFrameIdentity.Compute(preparedFrame),
            plan.InstallCostQuote);
        working.ResolutionStack.Add(installFrame);
    }

    private static BehaviorResolutionFrame RestoreBehaviorInstallSelection(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        if (instruction.Target is not { Kind: BehaviorTargetKind.ChosenCard } target)
        {
            return frame;
        }

        string bindingKey = StableIdentifier.TryValidate(target.Selector)
            ? target.Selector!
            : "chosen";
        BehaviorBinding binding = frame.Bindings.SingleOrDefault(candidate => candidate.Key == bindingKey)
            ?? throw new ResolutionFailureException($"Behavior binding '{bindingKey}' is not available for install.");
        if (binding.CardIds.Count != 1)
        {
            throw new ResolutionFailureException("A behavior install must consume exactly one bound card.");
        }

        CardInstanceId cardId = binding.CardIds[0];
        ZoneState source = ZoneForCard(working, cardId);
        if (source.Kind != ZoneKind.BehaviorSetAside)
        {
            return frame;
        }

        BehaviorSetAsideCard frozenSelection = frame.SetAside.SingleOrDefault(
                selected => selected.CardId == cardId)
            ?? throw new ResolutionFailureException(
                "A behavior install card in the set-aside zone has no authenticated selection record.");
        var selectedOnly = frame.SetAside
            .Where(selected => selected.CardId == cardId)
            .ToArray();
        RestoreBehaviorSetAside(
            working,
            frame.With(
                setAside: selectedOnly,
                clearSetAsideZone: false));

        BehaviorSetAsideCard[] remaining = frame.SetAside
            .Where(selected => selected.CardId != frozenSelection.CardId)
            .ToArray();
        BehaviorResolutionFrame prepared = frame.With(
            setAside: remaining,
            clearSetAsideZone: remaining.Length == 0);
        ReplaceTopFrame(working, prepared);
        return prepared;
    }

    private static InstallPlan ValidateBehaviorInstallInstruction(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        if (instruction.Kind != BehaviorInstructionKind.InstallCard
            || instruction.Target is not { Kind: BehaviorTargetKind.ChosenCard } target
            || instruction.SourceZone is not (BehaviorZone.Stack or BehaviorZone.Grip)
            || instruction.DestinationZone != BehaviorZone.Rig)
        {
            throw new ResolutionFailureException(
                "Behavior installs must target one chosen Runner Stack or Grip card and the Runner Rig.");
        }

        string bindingKey = StableIdentifier.TryValidate(target.Selector)
            ? target.Selector!
            : "chosen";
        BehaviorBinding binding = frame.Bindings.SingleOrDefault(candidate => candidate.Key == bindingKey)
            ?? throw new ResolutionFailureException($"Behavior binding '{bindingKey}' is not available for install.");
        if (binding.CardIds.Count != 1)
        {
            throw new ResolutionFailureException("A behavior install must consume exactly one chosen card.");
        }

        CardInstanceId cardId = binding.CardIds[0];
        IReadOnlyList<CardInstanceId> resolved = ResolveBehaviorCards(working, frame, target);
        if (resolved.Count != 1 || resolved[0] != cardId)
        {
            throw new ResolutionFailureException("The behavior install binding does not resolve to exactly one card.");
        }

        ValidateBehaviorInstallOrigin(
            working,
            frame,
            bindingKey,
            binding.CardIds,
            instruction.SourceZone.Value);
        InstallPlan plan = BuildInstallPlan(
                working,
                PlayerSide.Runner,
                cardId,
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                initiationKind: InstallInitiationKind.BehaviorEffect)
            ?? throw new ResolutionFailureException("The chosen behavior install card is no longer legally installable.");
        return plan;
    }

    private static void ValidateBehaviorInstallOrigin(
        WorkingState working,
        BehaviorResolutionFrame frame,
        string bindingKey,
        IReadOnlyList<CardInstanceId> cardIds,
        BehaviorZone expectedSourceZone)
    {
        CardInstanceState card = GetCard(working, cardIds.Single());
        ZoneState source = ZoneForCard(working, card.Id);
        ZoneKind expectedSourceKind = expectedSourceZone == BehaviorZone.Grip
            ? ZoneKind.RunnerGrip
            : ZoneKind.RunnerStack;
        if (card.OwnerSide != PlayerSide.Runner
            || source.Kind != expectedSourceKind)
        {
            throw new ResolutionFailureException(
                $"A behavior install card must remain Runner-owned in the declared Runner {expectedSourceZone}.");
        }

        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> rootInstructions = frame.AbilityKey is not null
            ? GetAbilityDeclaration(definition, frame.AbilityKey).Instructions
            : frame.TriggerKey is not null
                ? GetTriggerDeclaration(definition, frame.TriggerKey).Instructions
                : definition.Instructions;
        List<BehaviorInstruction> replay = rootInstructions.ToList();
        BehaviorTarget? originatingTarget = null;
        IReadOnlyList<CardInstanceId>? originatingSelection = null;
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
            {
                throw new ResolutionFailureException("The behavior install origin continuation is not reachable.");
            }

            BehaviorInstruction originInstruction = replay[step.InstructionCursor];
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection
                && step.CardIds.Count > 0
                && (string.Equals(step.Key, bindingKey, StringComparison.Ordinal)
                    || (bindingKey == "chosen"
                        && frame.Bindings.Any(binding =>
                            string.Equals(binding.Key, step.Key, StringComparison.Ordinal)
                            && binding.CardIds.SequenceEqual(cardIds)))))
            {
                originatingTarget = originInstruction.Kind switch
                {
                    BehaviorInstructionKind.Search => originInstruction.Target,
                    BehaviorInstructionKind.Choose => originInstruction.Choice?.Target,
                    _ => null,
                };
                originatingSelection = step.CardIds;
            }

            if (!BehaviorContinuation.TryApply(replay, step, out string? error))
            {
                throw new ResolutionFailureException(
                    $"The behavior install origin continuation is not reachable: {error}");
            }
        }

        if (originatingTarget is null
            || originatingTarget.Kind is not (BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards)
            || originatingTarget.Zone != expectedSourceZone
            || originatingSelection is null
            || !originatingSelection.SequenceEqual(cardIds))
        {
            throw new ResolutionFailureException(
                $"A behavior install requires a reachable {expectedSourceZone} search or choice as its origin.");
        }

        if (!MaterializeBehaviorCandidates(working, originatingTarget).Contains(card.Id))
        {
            throw new ResolutionFailureException(
                "The chosen behavior install card no longer matches its declared origin selector.");
        }
    }

    private static CardBehaviorDefinition GetBehaviorDefinition(WorkingState working, BehaviorResolutionFrame frame)
    {
        if (!working.BehaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            throw new ResolutionFailureException($"Behavior definition '{frame.DefinitionId}' is unavailable.");
        }

        return definition;
    }

    private static CardBehaviorDefinition GetBehaviorDefinition(
        WorkingState working,
        CardDefinitionId definitionId)
    {
        if (!working.BehaviorCatalog.TryGet(definitionId, out CardBehaviorDefinition? definition))
        {
            throw new ResolutionFailureException($"Behavior definition '{definitionId}' is unavailable.");
        }

        return definition;
    }

    private static BehaviorTrigger GetTriggerDeclaration(
        CardBehaviorDefinition definition,
        string triggerKey)
    {
        if (!StableIdentifier.TryValidate(triggerKey))
        {
            throw new ResolutionFailureException("Behavior trigger key is missing or invalid.");
        }

        return definition.Triggers.SingleOrDefault(trigger =>
                   string.Equals(trigger.Key, triggerKey, StringComparison.Ordinal))
            ?? throw new ResolutionFailureException(
                $"Behavior trigger '{triggerKey}' is not declared by definition '{definition.DefinitionId}'.");
    }

    /// <summary>
    /// Replays the immutable continuation path from the catalog root. The
    /// resulting list is a transient execution projection only; it is never
    /// trusted from or written to serialized state.
    /// </summary>
    private static List<BehaviorInstruction> GetBehaviorInstructions(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> root = definition.Instructions;
        if (frame.AbilityKey is not null)
        {
            root = GetAbilityDeclaration(definition, frame.AbilityKey).Instructions;
        }
        else if (frame.TriggerKey is not null)
        {
            root = GetTriggerDeclaration(definition, frame.TriggerKey).Instructions;
        }

        var instructions = root.ToList();
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (!BehaviorContinuation.TryApply(instructions, step, out string? continuationError))
            {
                throw new ResolutionFailureException(
                    $"Behavior continuation is not reachable from the catalog: {continuationError}");
            }
        }

        return instructions;
    }

    private static void ValidateBehaviorRuntimeFrame(WorkingState working, BehaviorResolutionFrame frame)
    {
        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> rootInstructions = definition.Instructions;
        BehaviorPaidAbility? abilityDeclaration = null;
        BehaviorTrigger? triggerDeclaration = null;
        if (frame.AbilityKey is not null && frame.TriggerKey is not null)
        {
            throw new ResolutionFailureException("A behavior frame cannot carry both an ability key and a trigger key.");
        }

        if (frame.AbilityKey is not null)
        {
            if (!StableIdentifier.TryValidate(frame.AbilityKey))
            {
                throw new ResolutionFailureException("Behavior ability child key is missing or invalid.");
            }

            abilityDeclaration = GetAbilityDeclaration(definition, frame.AbilityKey);
            rootInstructions = abilityDeclaration.Instructions;
            if (frame.Stage is not (BehaviorResolutionStage.Execute or BehaviorResolutionStage.AwaitingOccurrence))
            {
                throw new ResolutionFailureException(
                    "An ability behavior child must begin and remain in Execute stage.");
            }
        }
        else if (frame.TriggerKey is not null)
        {
            triggerDeclaration = GetTriggerDeclaration(definition, frame.TriggerKey);
            rootInstructions = triggerDeclaration.Instructions;
            if (frame.Stage is not (BehaviorResolutionStage.Execute or BehaviorResolutionStage.AwaitingOccurrence))
            {
                throw new ResolutionFailureException(
                    "A catalog trigger behavior child must begin and remain in Execute stage.");
            }
        }

        if (rootInstructions.Any(static instruction =>
                instruction.Kind == BehaviorInstructionKind.ModifyEncounteredIceStrength)
            && (frame.AbilityKey is null
                || !BehaviorEncounterStrengthContracts.TryGetExecutableInstruction(
                    definition,
                    frame.AbilityKey,
                    out _,
                    out BehaviorInstruction? declaredInstruction)
                || declaredInstruction is null
                || rootInstructions.Count(static instruction =>
                    instruction.Kind == BehaviorInstructionKind.ModifyEncounteredIceStrength) != 1
                || !BehaviorContractStructure.AreEqual(
                    rootInstructions.Single(static instruction =>
                        instruction.Kind == BehaviorInstructionKind.ModifyEncounteredIceStrength),
                    declaredInstruction)))
        {
            throw new ResolutionFailureException(
                "Encountered-ICE strength instructions must be the exact single instruction of a Runner encounter-paid ability.");
        }

        // `trigger-subject` is an engine-seeded binding, never a general
        // behavior binding. It is valid only on a catalog-trigger child and
        // must remain exactly equal to the subject captured by that child's
        // current checkpoint invocation. Keeping this check in the runtime
        // validator protects both yielded execution and save/load resumes;
        // the state validator mirrors it for the persisted boundary.
        BehaviorBinding[] triggerSubjectBindings = frame.Bindings
            .Where(binding => string.Equals(binding.Key, "trigger-subject", StringComparison.Ordinal))
            .ToArray();
        if (frame.TriggerKey is null)
        {
            if (triggerSubjectBindings.Length > 0)
            {
                throw new ResolutionFailureException(
                    "The trigger-subject binding is reserved for catalog trigger children.");
            }
        }
        else
        {
            if (working.ResolutionStack.Count < 2
                || working.ResolutionStack[^2] is not EffectCheckpointFrame triggerParent
                || triggerParent.Stage != EffectCheckpointStage.AwaitingChild
                || triggerParent.ChildKind != EffectCheckpointChildKind.Trigger
                || triggerParent.ChildTriggerId is not { } childTriggerId)
            {
                throw new ResolutionFailureException(
                    "A catalog trigger behavior child requires an awaiting effect checkpoint parent.");
            }

            TriggerInvocation[] matchingInvocations = triggerParent.TriggerQueue
                .Where(candidate => candidate.Id == childTriggerId)
                .Take(2)
                .ToArray();
            if (matchingInvocations.Length != 1)
            {
                throw new ResolutionFailureException(
                    "A catalog trigger behavior child no longer matches its checkpoint invocation.");
            }

            TriggerInvocation invocation = matchingInvocations[0];
            if (!invocation.IsCatalogTrigger
                || !string.Equals(invocation.TriggerKey, frame.TriggerKey, StringComparison.Ordinal)
                || invocation.SourceCardId != frame.SourceCardId
                || invocation.SourceDefinitionId != frame.DefinitionId
                || invocation.SourceZoneId != frame.SourceZoneId
                || invocation.SourceOwnerSide != GetCard(working, frame.SourceCardId).OwnerSide
                || invocation.SourceZoneKind != frame.SourceZoneKind
                || invocation.Controller != frame.Controller
                || !string.Equals(invocation.CatalogFingerprint, frame.CatalogFingerprint, StringComparison.Ordinal)
                || !string.Equals(invocation.DefinitionFingerprint, frame.DefinitionFingerprint, StringComparison.Ordinal))
            {
                throw new ResolutionFailureException(
                    "A catalog trigger behavior child does not match its exact checkpoint invocation.");
            }

            CardInstanceId? subjectCardId = invocation.Occurrence.SourceCardId;
            if (subjectCardId is null)
            {
                if (triggerSubjectBindings.Length > 0)
                {
                    throw new ResolutionFailureException(
                        "A catalog trigger without an occurrence subject must not retain a trigger-subject binding.");
                }
            }
            else if (triggerSubjectBindings.Length != 1
                || triggerSubjectBindings[0].CardIds.Count != 1
                || triggerSubjectBindings[0].CardIds[0] != subjectCardId.Value)
            {
                throw new ResolutionFailureException(
                    "The trigger-subject binding must equal the exact checkpoint invocation subject.");
            }
        }

        ValidateBehaviorOccurrenceContinuation(working, frame);

        if (frame.TriggerKey is null && frame.CatalogTriggerSourceMovedBeforeStart)
        {
            throw new ResolutionFailureException(
                "Only a catalog trigger behavior child may retain detached-source authorization.");
        }

        if (!string.Equals(frame.CatalogFingerprint, working.BehaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("Behavior frame catalog provenance does not match the injected catalog.");
        }

        if (!string.Equals(
                frame.DefinitionFingerprint,
                working.BehaviorCatalog.GetDefinitionFingerprint(frame.DefinitionId),
                StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("Behavior frame definition provenance does not match the injected catalog.");
        }

        var derivedInstructions = rootInstructions.ToList();
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (!BehaviorContinuation.TryApply(derivedInstructions, step, out string? continuationError))
            {
                throw new ResolutionFailureException(
                    $"Behavior frame continuation is not reachable from the catalog: {continuationError}");
            }
        }

        if (frame.InstructionCursor < 0 || frame.InstructionCursor > derivedInstructions.Count)
        {
            throw new ResolutionFailureException("Behavior frame instruction cursor is outside its continuation.");
        }

        // In-memory compatibility projections are advisory. If a caller
        // manufactured one, reject a mismatch; a JSON-restored frame has no
        // executable lists and therefore derives cleanly from the catalog.
        if (frame.RootInstructions.Count > 0
            && !BehaviorContractStructure.SequenceEquals(
                frame.RootInstructions,
                rootInstructions,
                BehaviorContractStructure.AreEqual))
        {
            throw new ResolutionFailureException("Behavior frame root instructions do not match the injected catalog.");
        }

        if (frame.Instructions.Count > 0
            && !BehaviorContractStructure.SequenceEquals(
                derivedInstructions,
                frame.Instructions,
                BehaviorContractStructure.AreEqual))
        {
            throw new ResolutionFailureException(
                "Behavior frame instructions are not the catalog-derived continuation.");
        }

        CardInstanceState source = GetCard(working, frame.SourceCardId);
        if (source.DefinitionId != frame.DefinitionId)
        {
            throw new ResolutionFailureException("Behavior source card definition no longer matches the invocation.");
        }

        PlayerState controller = GetPlayer(working, frame.Controller);
        if (source.OwnerSide != controller.Side)
        {
            throw new ResolutionFailureException(
                "Behavior source card owner no longer matches its controller.");
        }

        if (frame.ActingPlayer.HasValue)
        {
            _ = GetPlayer(working, frame.ActingPlayer.Value);
        }

        if (frame.ParentBehaviorId is not null
            && (working.ResolutionStack.Count < 2
                || !string.Equals(
                    working.ResolutionStack[^2].BehaviorId,
                    frame.ParentBehaviorId,
                    StringComparison.Ordinal)))
        {
            throw new ResolutionFailureException(
                "Behavior frame parent provenance no longer matches the active resolution stack.");
        }

        if ((frame.ParentBehaviorId is null) != (frame.ParentFrameIdentity is null))
        {
            throw new ResolutionFailureException(
                "Behavior frame parent ID and parent frame identity must be present together.");
        }

        if (!BehaviorFrameIdentity.TryValidateIssued(frame.FrameIdentity, out _))
        {
            throw new ResolutionFailureException("Behavior frame identity is missing or invalid.");
        }

        if (frame.SourceZoneId is null || frame.SourceZoneKind is null)
        {
            throw new ResolutionFailureException("Behavior frame source location is missing.");
        }

        ZoneState sourceZone = working.Zones.SingleOrDefault(zone => zone.Id == frame.SourceZoneId.Value)
            ?? throw new ResolutionFailureException("Behavior frame source location no longer exists.");
        if (sourceZone.Kind != frame.SourceZoneKind.Value)
        {
            throw new ResolutionFailureException("Behavior frame source location kind no longer matches its invocation context.");
        }

        if (source.ZoneId != frame.SourceZoneId.Value)
        {
            bool selfTrash = abilityDeclaration is not null
                ? abilityDeclaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.TrashSelf)
                : definition.Costs.Any(static cost => cost.Kind == BehaviorCostKind.TrashSelf);
            bool selfRemove = abilityDeclaration is not null
                ? abilityDeclaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.RemoveSelfFromGame)
                : definition.Costs.Any(static cost => cost.Kind == BehaviorCostKind.RemoveSelfFromGame);
            ZoneState currentSourceZone = working.Zones.SingleOrDefault(zone => zone.Id == source.ZoneId)
                ?? throw new ResolutionFailureException("Behavior source card current zone no longer exists.");
            bool catalogTriggerSelfTrashReached = frame.TriggerKey is not null
                && BehaviorInstructionPath.HasReached(
                    rootInstructions,
                    frame.Continuation,
                    frame.InstructionCursor,
                    BehaviorInstructionKind.TrashSelf);
            bool damageSelfTrashReached = frame.TriggerKey is not null
                && frame.InstructionCursor > 0
                && GetBehaviorInstructions(working, frame).Take(frame.InstructionCursor)
                    .Any(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage
                        && instruction.DamageAmountOperand?.TrashSourceAsCost == true);
            bool catalogTriggerLifecycleZone = (catalogTriggerSelfTrashReached || damageSelfTrashReached)
                && IsAbilityTrashZone(currentSourceZone, source);
            bool catalogTriggerDetachedSource = frame.TriggerKey is not null
                && frame.CatalogTriggerSourceMovedBeforeStart;
            bool hostedCreditOccurrenceAuthorized = IsAuthorizedHostedCreditMovement(
                working,
                frame,
                source,
                currentSourceZone);
            bool validLifecycleZone = frame.TriggerKey is not null
                ? catalogTriggerDetachedSource || catalogTriggerLifecycleZone || hostedCreditOccurrenceAuthorized
                : (selfTrash && IsAbilityTrashZone(currentSourceZone, source))
                    || (selfRemove && IsAbilityRemoveZone(currentSourceZone, source))
                    || hostedCreditOccurrenceAuthorized;
            if (!validLifecycleZone
                || frame.Stage != BehaviorResolutionStage.Execute
                || (selfTrash && selfRemove))
            {
                throw new ResolutionFailureException(
                    "Behavior source card no longer occupies its captured invocation location.");
            }
        }
        else if (frame.OccurrenceContinuation?.SourceMovedAfterCompletion == true)
        {
            throw new ResolutionFailureException(
                "A completed hosted-credit occurrence cannot retain moved-source authorization after the source returned to its captured zone.");
        }

        if (frame.ParentFrameIdentity is not null)
        {
            if (working.ResolutionStack.Count < 2
                || !string.Equals(
                    frame.ParentFrameIdentity,
                    BehaviorFrameIdentity.Compute(working.ResolutionStack[^2]),
                    StringComparison.Ordinal))
            {
                throw new ResolutionFailureException(
                    "Behavior frame parent identity no longer matches the active parent frame context.");
            }
        }

        if (frame.AbilityKey is not null)
        {
            if (working.ResolutionStack.Count < 2
                || working.ResolutionStack[^2] is not AbilityFrame abilityParent
                || abilityParent.Stage != AbilityLifecycleStage.AwaitingChild
                || !string.Equals(abilityParent.AbilityKey, frame.AbilityKey, StringComparison.Ordinal)
                || abilityParent.SourceCardId != frame.SourceCardId
                || abilityParent.DefinitionId != frame.DefinitionId
                || abilityParent.Controller != frame.Controller)
            {
                throw new ResolutionFailureException(
                    "Ability behavior child no longer matches its exact awaiting ability parent.");
            }
        }

        foreach (BehaviorSetAsideCard selected in frame.SetAside)
        {
            CardInstanceState card = GetCard(working, selected.CardId);
            if (frame.SetAsideZoneId is null || card.ZoneId != frame.SetAsideZoneId.Value)
            {
                throw new ResolutionFailureException(
                    $"Behavior selection '{selected.CardId}' is stale; its set-aside zone changed.");
            }
        }

        if (frame.SetAside.Count == 0 && frame.SetAsideZoneId is not null)
        {
            throw new ResolutionFailureException("An empty behavior selection cannot retain a set-aside zone.");
        }
    }

    private static void PayBehaviorCosts(
        WorkingState working,
        BehaviorResolutionFrame frame,
        CardBehaviorDefinition definition)
    {
        CardInstanceState source = GetCard(working, frame.SourceCardId);
        PlayerId payer = GetPlayer(working, source.OwnerSide).Id;
        bool deferredCounterCleanup = false;
        foreach (BehaviorCost cost in definition.Costs)
        {
            switch (cost.Kind)
            {
                case BehaviorCostKind.Credits:
                    ApplyCreditDeltaIfNonZero(working, payer, -cost.Amount);
                    break;
                case BehaviorCostKind.Clicks:
                case BehaviorCostKind.LoseClick:
                    ApplyClickDelta(working, payer, -cost.Amount);
                    break;
                case BehaviorCostKind.Tags:
                    ApplyTags(working, payer, -cost.Amount);
                    break;
                case BehaviorCostKind.Counter:
                    AdjustCounter(working, frame.SourceCardId, cost.CounterName!, -cost.Amount);
                    break;
                case BehaviorCostKind.TrashSelf:
                    MoveBehaviorCardToTrash(working, source);
                    source = GetCard(working, frame.SourceCardId);
                    deferredCounterCleanup = true;
                    break;
                case BehaviorCostKind.RemoveSelfFromGame:
                    MoveBehaviorCardToRemovedFromGame(working, source);
                    source = GetCard(working, frame.SourceCardId);
                    deferredCounterCleanup = true;
                    break;
                default:
                    throw new ResolutionFailureException($"Behavior cost '{cost.Kind}' is not executable.");
            }
        }

        if (deferredCounterCleanup
            && !ShouldRetainSourceCountersThroughChild(working, frame))
        {
            ClearCardCounters(working, frame.SourceCardId);
        }
    }

    private static void ApplyStaticModifiers(
        WorkingState working,
        BehaviorResolutionFrame frame,
        CardBehaviorDefinition definition)
    {
        if (definition.StaticModifiers.Count == 0)
        {
            return;
        }

        // A static card modifier is a property of an active installed card,
        // not an effect paid when its generic behavior is invoked. Mutating
        // BaseMemory/BaseHandSize here made every replay or repeated ability
        // start stack the modifier permanently (and even applied an
        // installed-card modifier while the card was still in a hand). The
        // RulesState projection will derive these values from active cards;
        // this resolution step deliberately remains idempotent.
        if (!IsInstalledCard(working, frame.SourceCardId))
        {
            return;
        }
    }

    /// <summary>
    /// Returns the effective memory limit without changing the serialized
    /// base value. ENG-404 static modifiers are projected from active cards so
    /// trash/deactivation removes them and repeated behavior starts remain
    /// idempotent.
    /// </summary>
    private static int EffectiveMemoryLimit(WorkingState working, PlayerId playerId)
    {
        PlayerState player = GetPlayer(working, playerId);
        PlayerRulesState rules = working.RulesState?.Players
            .SingleOrDefault(value => value.PlayerId == playerId)
            ?? throw new ResolutionFailureException($"Player '{playerId}' has no M2 rules state.");
        int modifier = BehaviorStaticModifierProjection.Sum(
            working.BehaviorCatalog,
            working.Cards,
            working.Zones,
            player.Side,
            BehaviorStaticModifierKind.MemoryUnits);
        return checked(rules.BaseMemory + modifier);
    }

    private static int EffectiveMaximumHandSize(WorkingState working, PlayerSide side)
    {
        PlayerState player = GetPlayer(working, side);
        PlayerRulesState? rules = working.RulesState?.Players
            .SingleOrDefault(value => value.PlayerId == player.Id);
        if (rules is null)
        {
            return 5;
        }

        int modifier = BehaviorStaticModifierProjection.Sum(
            working.BehaviorCatalog,
            working.Cards,
            working.Zones,
            side,
            BehaviorStaticModifierKind.MaximumHandSize);
        return checked(rules.BaseHandSize + modifier - rules.CoreDamage);
    }

    private static void ResolveBehaviorDraw(WorkingState working, BehaviorResolutionFrame frame, int amount)
    {
        PlayerSide side = GetPlayer(working, frame.Controller).Side;
        for (int index = 0; index < amount; index++)
        {
            if (TryDrawCard(working, side))
            {
                continue;
            }

            if (side == PlayerSide.Corp)
            {
                if (!CompleteOrdinaryCorpPlayDeckOut(working, frame))
                {
                    // Standalone behavior fixtures intentionally retain the
                    // legacy terminal outcome.  Ordinary card plays take a
                    // bounded lifecycle path below so the played Operation
                    // is completed before the real-turn terminal cleanup.
                    if (working.Turn.History is not null)
                        CompleteTurnGame(working, PlayerSide.Runner, GameWinReason.DeckOut);
                    else
                        CompleteToyGame(working, PlayerSide.Runner, GameWinReason.DeckOut);
                    working.RunState = null;
                }

                return;
            }

            // Unlike the Corp, the Runner does not lose when a mandatory draw
            // finds an empty Stack. The draw simply yields no card; the
            // surrounding behavior continues atomically.
            return;
        }
    }

    private static void ResolveBehaviorModifyEncounteredIceStrength(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        if (frame.AbilityKey is not { } abilityKey
            || frame.TriggerKey is not null
            || working.ResolutionStack.Count < 3
            || working.ResolutionStack[^2] is not AbilityFrame abilityParent
            || working.ResolutionStack[^3] is not RunTimingFrame runFrame
            || abilityParent.Stage != AbilityLifecycleStage.AwaitingChild
            || abilityParent.Controller != frame.Controller
            || abilityParent.SourceCardId != frame.SourceCardId
            || abilityParent.DefinitionId != frame.DefinitionId)
        {
            throw new ResolutionFailureException(
                "Encountered-ICE strength modifiers require an ability-owned run child.");
        }

        RunState run = working.RunState
            ?? throw new ResolutionFailureException(
                "Encountered-ICE strength modifiers require a live run.");
        if (runFrame.RunId != run.Id
            || runFrame.Step != run.Step
            || run.Step != RunTimingStep.EncounterPaidWindow
            || run.Priority is not { PriorityHolder: PlayerSide.Runner }
            || run.Encounter is not { Status: RunEncounterStatus.Initialized } encounter)
        {
            throw new ResolutionFailureException(
                "Encountered-ICE strength modifiers are legal only in an initialized encounter paid window.");
        }

        CardInstanceState source = GetCard(working, frame.SourceCardId);
        if (source.OwnerSide != PlayerSide.Runner
            || !working.BehaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition)
            || !BehaviorEncounterStrengthContracts.MatchesEffect(
                definition,
                abilityKey,
                BehaviorEncounterStrengthContracts.EffectBehaviorId(frame.DefinitionId, abilityKey),
                new RunValueAdjustment(
                    source.Id,
                    encounter.IceId,
                    instruction.Amount,
                    instruction.Argument!),
                out _,
                out BehaviorInstruction? declaredInstruction)
            || declaredInstruction is null
            || !BehaviorContractStructure.AreEqual(declaredInstruction, instruction))
        {
            throw new ResolutionFailureException(
                "Encountered-ICE strength modifier provenance does not match its catalog paid ability.");
        }

        CardInstanceState ice = GetCard(working, encounter.IceId);
        if (!working.BehaviorCatalog.TryGetIce(ice.DefinitionId, out _))
        {
            throw new ResolutionFailureException(
                $"Encounter target '{ice.DefinitionId}' is not a registered ICE behavior.");
        }

        RunEffectId effectId = new(
            $"effect:encounter-strength:{run.Id.Value}:{encounter.Id.Value}:"
            + $"{source.Id.Value}:{ice.Id.Value}:{run.LingeringEffects.Count}");
        RunState adjusted = run.WithLingeringEffects(
            run.LingeringEffects.Append(
                new RunLingeringEffect(
                    effectId,
                    BehaviorEncounterStrengthContracts.EffectBehaviorId(frame.DefinitionId, abilityKey),
                    source.Id,
                    RunLingeringEffectExpiry.Encounter,
                    new RunValueAdjustment(
                        BehaviorValueKind.Strength,
                        BehaviorValueSourceKind.EncounterScoped,
                        BehaviorValueOperation.Add,
                        source.Id,
                        ice.Id,
                        instruction.Amount,
                        instruction.Argument!))).ToArray());
        working.RunState = adjusted;
        AdvanceBehaviorCursor(working, frame);
    }

    /// <summary>
    /// Completes the narrow ordinary Corp Operation lifecycle when its
    /// behavior's mandatory draw reaches an empty R&amp;D.  The behavior frame
    /// must be the exact child of a CardPlayFrame, which in turn must be the
    /// exact child of an active AwaitingAction TurnFrame.  A behavior opened
    /// through the standalone test seam deliberately returns false and keeps
    /// its existing toy terminal semantics.
    /// </summary>
    private static bool CompleteOrdinaryCorpPlayDeckOut(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        if (working.ResolutionStack.Count < 2
            || working.ResolutionStack[^2] is not CardPlayFrame cardPlay)
        {
            if (string.Equals(frame.ParentBehaviorId, ResolutionBehaviorIds.CardPlay, StringComparison.Ordinal))
            {
                throw new ResolutionFailureException(
                    "A behavior frame claiming CardPlay parent provenance is not immediately above a CardPlayFrame.");
            }

            return false;
        }

        if (working.ResolutionStack[^1] is not BehaviorResolutionFrame activeBehavior
            || !string.Equals(activeBehavior.FrameIdentity, frame.FrameIdentity, StringComparison.Ordinal))
        {
            throw new ResolutionFailureException(
                "The active behavior frame changed before ordinary Corp play deck-out completion.");
        }

        if (working.ResolutionStack.Count < 3
            || working.ResolutionStack[^3] is not TurnFrame turn
            || turn.Stage != TurnFrameStage.AwaitingAction
            || turn.ActiveSide != PlayerSide.Corp
            || working.Turn.ActiveSide != PlayerSide.Corp
            || cardPlay.Side != PlayerSide.Corp
            || frame.Controller != cardPlay.Owner
            || !string.Equals(frame.ParentBehaviorId, cardPlay.BehaviorId, StringComparison.Ordinal)
            || frame.ParentFrameIdentity is null
            || !string.Equals(
                frame.ParentFrameIdentity,
                BehaviorFrameIdentity.Compute(cardPlay),
                StringComparison.Ordinal))
        {
            throw new ResolutionFailureException(
                "The Corp draw behavior has a malformed ordinary card-play parent topology.");
        }

        // Hidden cards selected by the active behavior remain in its private
        // set-aside custody until the child closes, including on terminal
        // draw failure. Restore them before popping the child so no transient
        // BehaviorSetAside zone survives the terminal state.
        RestoreBehaviorSetAside(working, frame);
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);

        // StepCardPlay performs the existing frozen-frame validation and
        // emits CardPlayCompleted after moving the faceup Operation from the
        // canonical CorpPlayArea to CorpDiscard. Only then may terminal turn
        // cleanup clear the remaining stack and emit GameWon.
        StepCardPlay(working, cardPlay);
        CompleteTurnGame(working, PlayerSide.Runner, GameWinReason.DeckOut);
        return true;
    }

    private static void ResolveBehaviorBranch(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        BehaviorBranchSpec branch = instruction.Branch
            ?? throw new ResolutionFailureException("A branch instruction has no declaration.");
        bool conditionMatches = BehaviorConditionMatches(working, frame, branch.Condition);
        IReadOnlyList<BehaviorInstruction> arm = conditionMatches
            ? branch.WhenTrue
            : branch.WhenFalse;
        string continuationKey = BehaviorContinuation.BranchKey(instruction, frame.InstructionCursor);
        var continuation = frame.Continuation
            .Append(new BehaviorContinuationStep(
                frame.InstructionCursor,
                continuationKey,
                conditionMatches
                    ? BehaviorContinuationKind.BranchTrue
                    : BehaviorContinuationKind.BranchFalse))
            .ToArray();
        List<BehaviorInstruction> currentInstructions = GetBehaviorInstructions(working, frame);
        var instructions = currentInstructions
            .Take(frame.InstructionCursor)
            .Concat(arm)
            .Concat(currentInstructions.Skip(frame.InstructionCursor + 1))
            .ToArray();
        ReplaceTopFrame(working, frame.With(instructions: instructions, choice: null, continuation: continuation));
    }

    private static bool BehaviorConditionMatches(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorCondition condition)
    {
        PlayerState controller = GetPlayer(working, frame.Controller);
        return condition.Kind switch
        {
            BehaviorConditionKind.Tagged => TagsForCondition(working, frame, condition) > 0,
            BehaviorConditionKind.NotTagged => TagsForCondition(working, frame, condition) <= 0,
            BehaviorConditionKind.ClicksRemainingAtLeast => controller.Clicks >= condition.Value.GetValueOrDefault(),
            BehaviorConditionKind.Installed => IsInstalledCard(working, frame.SourceCardId),
            BehaviorConditionKind.InstalledThisTurn => CurrentFacts(working, frame.Controller)?.InstalledCardIds.Contains(frame.SourceCardId) == true,
            BehaviorConditionKind.SuccessfulRun => working.RunState?.Successful == true,
            BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn => working.Turn.History?.RunnerThisTurn?.SuccessfulRun == true,
            BehaviorConditionKind.SuccessfulRunThisTurn => CurrentFacts(working, frame.Controller)?.SuccessfulRun == true,
            BehaviorConditionKind.SourceController => GetCard(working, frame.SourceCardId).OwnerSide == controller.Side,
            BehaviorConditionKind.Encountering => working.RunState?.Step == RunTimingStep.Encounter,
            BehaviorConditionKind.CounterAtLeast => CurrentCounter(working, frame.SourceCardId, condition.Fact!) >= condition.Value.GetValueOrDefault(),
            BehaviorConditionKind.FactEquals => BehaviorFactEquals(working, frame, condition),
            BehaviorConditionKind.FoundCard => FoundCardConditionMatches(working, frame),
            _ => false,
        };
    }

    /// <summary>
    /// Reports whether the latest reachable card-selection continuation
    /// actually found a card and still agrees with the canonical chosen
    /// binding. Empty selections intentionally clear this condition, even if
    /// an earlier search in the same behavior found a card.
    /// </summary>
    private static bool FoundCardConditionMatches(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        // Validate reachability from the immutable catalog root as part of
        // the condition itself. StepBehaviorResolution already performs this
        // check for normal execution; retaining it here keeps the condition
        // fail-closed when called from a future resolution path.
        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> rootInstructions = frame.AbilityKey is not null
            ? GetAbilityDeclaration(definition, frame.AbilityKey).Instructions
            : frame.TriggerKey is not null
                ? GetTriggerDeclaration(definition, frame.TriggerKey).Instructions
                : definition.Instructions;
        List<BehaviorInstruction> replay = rootInstructions.ToList();
        BehaviorContinuationStep? latestSelection = null;
        bool latestSelectionWasSearch = false;
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
            {
                return false;
            }

            // Inspect the instruction before applying the continuation: the
            // cursor is a path into the mutable replay, so this is the only
            // authoritative record of whether this selection came from a
            // Search or an ordinary Choose. A later ordinary Choose must
            // invalidate an earlier successful search for this condition.
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection)
            {
                latestSelection = step;
                latestSelectionWasSearch = replay[step.InstructionCursor].Kind == BehaviorInstructionKind.Search;
            }

            if (!BehaviorContinuation.TryApply(replay, step, out _))
            {
                return false;
            }
        }

        if (latestSelection is null
            || !latestSelectionWasSearch
            || latestSelection.CardIds.Count == 0)
        {
            return false;
        }

        BehaviorBinding[] chosenBindings = frame.Bindings
            .Where(binding => string.Equals(binding.Key, "chosen", StringComparison.Ordinal))
            .ToArray();
        return chosenBindings.Length == 1
            && chosenBindings[0].CardIds.SequenceEqual(latestSelection.CardIds);
    }

    private static int? TagsForCondition(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorCondition condition)
    {
        PlayerId subject = condition.SubjectSide is { } subjectSide
            ? GetPlayer(working, subjectSide).Id
            : frame.Controller;
        return working.RulesState?.Players
            .SingleOrDefault(player => player.PlayerId == subject)
            ?.Tags;
    }

    private static bool BehaviorFactEquals(WorkingState working, BehaviorResolutionFrame frame, BehaviorCondition condition)
    {
        int? actual = condition.Fact switch
        {
            "credits" => GetPlayer(working, frame.Controller).Credits,
            "clicks" => GetPlayer(working, frame.Controller).Clicks,
            "tags" => working.RulesState?.Players.SingleOrDefault(player => player.PlayerId == frame.Controller)?.Tags,
            "advancement" => GetCard(working, frame.SourceCardId).AdvancementCounters,
            _ => null,
        };
        return actual.HasValue && actual.Value == condition.Value;
    }

    private static int CurrentCounter(WorkingState working, CardInstanceId cardId, string counterName) =>
        working.RulesState?.Counters.SingleOrDefault(counter =>
            counter.CardId == cardId
            && string.Equals(counter.CounterName, counterName, StringComparison.Ordinal))?.Count ?? 0;

    private static int CurrentAbilityCounter(WorkingState working, CardInstanceId cardId, string counterName) =>
        working.RulesState?.Counters.SingleOrDefault(counter =>
            counter.CardId == cardId
            && counter.Purpose != CardCounterPurpose.HostedCredit
            && string.Equals(counter.CounterName, counterName, StringComparison.Ordinal))?.Count ?? 0;

    private static int ResolveBehaviorDynamicAmount(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        BehaviorAmountOperand operand = instruction.AmountOperand
            ?? throw new ResolutionFailureException("A dynamic credit amount is missing its typed operand.");
        if (instruction.Kind != BehaviorInstructionKind.GainCredits)
        {
            throw new ResolutionFailureException("A dynamic amount is executable only on GainCredits.");
        }

        CardInstanceState source = GetCard(working, frame.SourceCardId);
        ZoneState sourceZone = ZoneForCard(working, source.Id);
        bool active = RuntimeCardActivity.IsActive(source, working.Zones);
        if (!active
            && (!IsAbilitySourceCounterRetention(working, frame, operand)
                || !IsAbilityCounterRetentionZone(working, frame, sourceZone, source)))
        {
            throw new ResolutionFailureException(
                "A source counter may be read after movement only by its exact self-movement ability child.");
        }

        CardCounterState? counter = working.RulesState?.Counters.SingleOrDefault(candidate =>
            candidate.CardId == frame.SourceCardId
            && string.Equals(candidate.CounterName, operand.SourceCounterName, StringComparison.Ordinal));
        if (counter?.Purpose == CardCounterPurpose.HostedCredit)
        {
            throw new ResolutionFailureException(
                "A dynamic behavior amount may not read a hosted-credit pool as a generic counter.");
        }

        int count = counter?.Count ?? 0;
        return checked(count * operand.Multiplier);
    }

    private static bool IsAbilitySourceCounterRetention(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorAmountOperand operand)
    {
        if (frame.AbilityKey is null
            || !working.BehaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            return false;
        }

        BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
        if (!declaration.Costs.Any(static cost =>
                cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame))
        {
            return false;
        }

        return ContainsDynamicAmount(declaration.Instructions, operand);
    }

    private static bool ShouldRetainSourceCountersThroughChild(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        IReadOnlyList<BehaviorInstruction> instructions = GetBehaviorInstructions(working, frame);
        if (!ContainsDynamicAmount(instructions))
        {
            return false;
        }

        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorCost> costs = frame.AbilityKey is null
            ? definition.Costs
            : GetAbilityDeclaration(definition, frame.AbilityKey).Costs;
        return costs.Any(static cost =>
            cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame);
    }

    private static bool ContainsDynamicAmount(
        IReadOnlyList<BehaviorInstruction> instructions,
        BehaviorAmountOperand? expected = null)
    {
        foreach (BehaviorInstruction instruction in instructions)
        {
            if (instruction.Kind == BehaviorInstructionKind.GainCredits
                && instruction.AmountOperand is { } amount
                && (expected is null || amount.Equals(expected)))
            {
                return true;
            }

            if (instruction.Branch is { } branch
                && (ContainsDynamicAmount(branch.WhenTrue, expected)
                    || ContainsDynamicAmount(branch.WhenFalse, expected)))
            {
                return true;
            }

            if (instruction.Choice is { } choice
                && choice.Options.Any(option => ContainsDynamicAmount(option.Instructions, expected)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAbilityCounterRetentionZone(
        WorkingState working,
        BehaviorResolutionFrame frame,
        ZoneState zone,
        CardInstanceState source)
    {
        if (frame.AbilityKey is null
            || !working.BehaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            return false;
        }

        BehaviorPaidAbility declaration = GetAbilityDeclaration(definition, frame.AbilityKey);
        bool selfRemove = declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.RemoveSelfFromGame);
        bool selfTrash = declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.TrashSelf);
        return source.OwnerSide switch
        {
            PlayerSide.Corp => selfRemove
                ? zone.Kind == ZoneKind.CorpRemovedFromGame
                : selfTrash && zone.Kind == ZoneKind.CorpDiscard,
            PlayerSide.Runner => selfRemove
                ? zone.Kind == ZoneKind.RunnerRemovedFromGame
                : selfTrash && zone.Kind == ZoneKind.RunnerHeap,
            _ => false,
        };
    }

    private static TurnFactsState? CurrentFacts(WorkingState working, PlayerId playerId)
    {
        PlayerSide side = GetPlayer(working, playerId).Side;
        return working.Turn.History?.ThisTurn(side);
    }

    private static void YieldBehaviorSearch(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        BehaviorTarget target = instruction.Target
            ?? throw new ResolutionFailureException("A search instruction has no candidate target.");
        CardInstanceId[] candidates = MaterializeBehaviorCandidates(working, target, frame.SourceCardId);
        if (candidates.Length == 0)
        {
            FinalizeEmptyBehaviorSearch(working, frame, instruction);
            return;
        }

        string key = StableIdentifier.TryValidate(instruction.Argument)
            ? instruction.Argument!
            : $"search-{frame.InstructionCursor.ToString(CultureInfo.InvariantCulture)}";
        bool constrainedSingleCardSearch = IsConstrainedSingleCardSearch(instruction);
        var choice = new BehaviorChoiceMaterialization(
            key,
            frame.Controller,
            BehaviorChoiceMode.Mandatory,
            constrainedSingleCardSearch
                ? BehaviorChoiceCardinalityKind.UpTo
                : BehaviorChoiceCardinalityKind.Exactly,
            1,
            candidateCardIds: candidates);
        ReplaceTopFrame(working, frame.With(choice: choice));
        YieldBehaviorActions(working, frame.With(choice: choice), choice);
    }

    private static bool IsConstrainedSingleCardSearch(BehaviorInstruction instruction) =>
        instruction.Target is { Kind: BehaviorTargetKind.ZoneCard }
        && instruction.SourceZone is BehaviorZone.Deck or BehaviorZone.Stack
        && instruction.Filter is not null
        && !BehaviorContractStructure.AreEqual(instruction.Filter, BehaviorCardFilter.Any);

    private static void FinalizeEmptyBehaviorSearch(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        List<BehaviorInstruction> currentInstructions = GetBehaviorInstructions(working, frame);
        string key = BehaviorContinuation.SearchKey(instruction, frame.InstructionCursor);
        var continuationStep = new BehaviorContinuationStep(
            frame.InstructionCursor,
            key,
            BehaviorContinuationKind.ChoiceSelection,
            cardIds: []);
        var instructions = currentInstructions
            .Take(frame.InstructionCursor)
            .Concat(currentInstructions.Skip(frame.InstructionCursor + 1))
            .ToArray();
        ReplaceTopFrame(
            working,
            frame.With(
                instructions: instructions,
                bindings: ClearBehaviorSelectionBindings(frame.Bindings, key),
                clearChoice: true,
                continuation: frame.Continuation.Append(continuationStep).ToArray()));
    }

    private static void YieldBehaviorChoice(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        BehaviorChoiceSpec spec = instruction.Choice
            ?? throw new ResolutionFailureException("A choose instruction has no choice declaration.");
        PlayerId owner = ResolveBehaviorChooser(working, frame, spec.Chooser);
        CardInstanceId[] candidates = spec.Target is null
            ? []
            : MaterializeBehaviorCandidates(working, spec.Target, frame.SourceCardId);
        candidates = FilterImmediateInstallCandidates(working, frame, spec, candidates);
        if (spec.Target is not null && candidates.Length == 0)
        {
            if (spec.Mode is BehaviorChoiceMode.Mandatory
                or BehaviorChoiceMode.Optional
                or BehaviorChoiceMode.TargetSelection
                or BehaviorChoiceMode.OrderSelection)
            {
                FinalizeEmptyBehaviorChoice(working, frame, spec);
                return;
            }

            throw new ResolutionFailureException($"Behavior choice '{spec.Key}' has no candidates.");
        }

        if (spec.Target is not null
            && spec.Cardinality.Kind == BehaviorChoiceCardinalityKind.Exactly
            && candidates.Length < spec.Cardinality.Count)
        {
            throw new ResolutionFailureException($"Behavior choice '{spec.Key}' has fewer candidates than required.");
        }

        string[] optionKeys = spec.Options
            .Where(option => !IsBehaviorInstallOption(option)
                || IsBehaviorInstallOptionAvailable(working, frame, option))
            .Where(option => IsBehaviorChoiceOptionAffordable(working, owner, option))
            .Select(option => option.Key)
            .ToArray();
        if (spec.Mode == BehaviorChoiceMode.Mode && optionKeys.Length == 0)
        {
            throw new ResolutionFailureException($"Behavior choice '{spec.Key}' has no currently legal mode option.");
        }

        // A choice option may carry a private additional card cost even when
        // the choice itself has no target. Keep the materialization's common
        // candidate projection empty in that case: the exact option-specific
        // combinations are issued below and are validated against the typed
        // cost descriptor again on submission.
        bool hasOptionCardCost = spec.Options
            .Where(option => optionKeys.Contains(option.Key, StringComparer.Ordinal))
            .Any(HasBehaviorChoiceAdditionalCardCost);
        if (hasOptionCardCost && candidates.Length > 0)
        {
            throw new ResolutionFailureException(
                $"Behavior choice '{spec.Key}' cannot combine a target card selection with an option card cost.");
        }

        var choice = new BehaviorChoiceMaterialization(
            spec.Key,
            owner,
            spec.Mode,
            spec.Cardinality.Kind,
            spec.Cardinality.Count,
            optionKeys,
            candidates);
        ReplaceTopFrame(working, frame.With(choice: choice));
        YieldBehaviorActions(working, frame.With(choice: choice), choice);
    }

    /// <summary>
    /// A target choice immediately followed by an InstallCard instruction is
    /// a single authenticated install decision (Pantograph).  Do not offer a
    /// hidden card that the shared install planner would reject later: doing
    /// so would expose a dead choice and make an optional empty selection
    /// impossible to finalize.  The hypothetical continuation carries the
    /// same stable binding and choice key that the real reducer will issue,
    /// so this check exercises the same origin, replacement, memory, and
    /// affordability gates without mutating state.
    /// </summary>
    private static CardInstanceId[] FilterImmediateInstallCandidates(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorChoiceSpec spec,
        CardInstanceId[] candidates)
    {
        if (spec.Target is not { Kind: BehaviorTargetKind.ZoneCard } target
            || target.Zone is not (BehaviorZone.Grip or BehaviorZone.Stack))
        {
            return candidates.ToArray();
        }

        List<BehaviorInstruction> instructions = GetBehaviorInstructions(working, frame);
        int consumerCursor = frame.InstructionCursor + 1;
        if (consumerCursor >= instructions.Count
            || instructions[consumerCursor] is not
            {
                Kind: BehaviorInstructionKind.InstallCard,
                Target: { Kind: BehaviorTargetKind.ChosenCard } installTarget,
                SourceZone: { } sourceZone,
                DestinationZone: BehaviorZone.Rig,
            }
            || sourceZone != target.Zone)
        {
            return candidates.ToArray();
        }

        // The reducer binds the selection under both the choice key and the
        // conventional "chosen" key. A different stable selector denotes a
        // separate binding, so this choice must not accidentally filter its
        // candidates as though it were the install origin.
        string bindingKey = StableIdentifier.TryValidate(installTarget.Selector)
            ? installTarget.Selector!
            : "chosen";
        if (!string.Equals(bindingKey, "chosen", StringComparison.Ordinal)
            && !string.Equals(bindingKey, spec.Key, StringComparison.Ordinal))
        {
            return candidates.ToArray();
        }

        var filtered = new List<CardInstanceId>(candidates.Length);
        foreach (CardInstanceId candidate in candidates)
        {
            BehaviorContinuationStep selectionStep = new(
                frame.InstructionCursor,
                spec.Key,
                BehaviorContinuationKind.ChoiceSelection,
                cardIds: [candidate]);
            IReadOnlyList<BehaviorBinding> bindings = SetBehaviorBinding(
                SetBehaviorBinding(frame.Bindings, spec.Key, [candidate]),
                "chosen",
                [candidate]);
            BehaviorResolutionFrame hypothetical = frame.With(
                bindings: bindings,
                continuation: frame.Continuation.Append(selectionStep).ToArray());
            try
            {
                _ = ValidateBehaviorInstallInstruction(
                    working,
                    hypothetical,
                    instructions[consumerCursor]);
                filtered.Add(candidate);
            }
            catch (ResolutionFailureException)
            {
                // Candidate projections fail closed. The real reducer will
                // re-run the same validation after the private choice is
                // consumed and the selected card is restored from set-aside.
            }
            catch (InvalidOperationException)
            {
                // A malformed catalog/state cannot make an install option
                // visible; it simply leaves the optional choice empty.
            }
            catch (ArgumentException)
            {
                // See the fail-closed rule above.
            }
            catch (OverflowException)
            {
                // See the fail-closed rule above.
            }
            catch (KeyNotFoundException)
            {
                // See the fail-closed rule above.
            }
        }

        return filtered.ToArray();
    }

    private static void FinalizeEmptyBehaviorChoice(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorChoiceSpec spec)
    {
        List<BehaviorInstruction> currentInstructions = GetBehaviorInstructions(working, frame);
        var continuationStep = new BehaviorContinuationStep(
            frame.InstructionCursor,
            spec.Key,
            BehaviorContinuationKind.ChoiceSelection,
            cardIds: []);
        var instructions = currentInstructions
            .Take(frame.InstructionCursor)
            .Concat(currentInstructions.Skip(frame.InstructionCursor + 1))
            .ToArray();
        ReplaceTopFrame(
            working,
            frame.With(
                instructions: instructions,
                clearChoice: true,
                continuation: frame.Continuation.Append(continuationStep).ToArray()));
    }

    private static void YieldBehaviorActions(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorChoiceMaterialization choice)
    {
        var actions = new List<LegalAction>();
        BehaviorChoiceSpec? specification = GetCurrentBehaviorChoiceSpecification(working, frame);
        BehaviorChoiceOption[] optionCardCostOptions = specification?.Options
            .Where(option => choice.OptionKeys.Contains(option.Key, StringComparer.Ordinal))
            .Where(HasBehaviorChoiceAdditionalCardCost)
            .ToArray()
            ?? [];

        if (optionCardCostOptions.Length > 0 && choice.CandidateCardIds.Count == 0)
        {
            foreach (BehaviorChoiceOption option in optionCardCostOptions)
            {
                foreach (CardInstanceId[] selection in EnumerateBehaviorChoiceAdditionalSelections(working, option))
                {
                    actions.Add(new LegalAction(
                        working.Allocator.NextActionId(),
                        ActionKind.ChooseBehavior,
                        DescribeBehaviorChoiceOption(option, selection),
                        new BehaviorChoiceParameters(
                            choice.Key,
                            [option.Key],
                            selection,
                            finalizeSelection: true)));
                }
            }

            // A fallback/optional option without a card cost remains a normal
            // option action in the same prompt. Its semantic marker is carried
            // by the catalog and is exposed only through the issued action
            // projection, never inferred from its key or description.
            foreach (string optionKey in choice.OptionKeys
                .Where(key => optionCardCostOptions.All(option => option.Key != key)))
            {
                BehaviorChoiceOption option = specification!.Options.Single(candidate => candidate.Key == optionKey);
                actions.Add(new LegalAction(
                    working.Allocator.NextActionId(),
                    ActionKind.ChooseBehavior,
                    DescribeBehaviorChoiceOption(option, []),
                    new BehaviorChoiceParameters(choice.Key, optionKey)));
            }

            if (choice.Mode == BehaviorChoiceMode.Optional
                && choice.CardinalityKind == BehaviorChoiceCardinalityKind.UpTo
                && choice.CardinalityCount == 1)
            {
                actions.Add(new LegalAction(
                    working.Allocator.NextActionId(),
                    ActionKind.ChooseBehavior,
                    "Skip optional choice",
                    new BehaviorChoiceParameters(choice.Key)));
            }
        }
        else if (choice.CandidateCardIds.Count > 0)
        {
            if (RequiresIncrementalSelection(choice))
            {
                foreach (CardInstanceId candidate in choice.CandidateCardIds
                    .Where(candidate => !choice.SelectedCardIds.Contains(candidate)))
                {
                    if (choice.CardinalityKind == BehaviorChoiceCardinalityKind.Exactly
                        && choice.SelectedCardIds.Count >= choice.CardinalityCount)
                    {
                        break;
                    }

                    actions.Add(new LegalAction(
                        working.Allocator.NextActionId(),
                        ActionKind.ChooseBehavior,
                        $"Add card '{candidate}' to behavior selection",
                        new BehaviorChoiceParameters(
                            choice.Key,
                            cardIds: [candidate],
                            finalizeSelection: false)));
                }

                bool canFinalize = choice.CardinalityKind != BehaviorChoiceCardinalityKind.Exactly
                    || choice.SelectedCardIds.Count == choice.CardinalityCount;
                if (canFinalize)
                {
                    actions.Add(new LegalAction(
                        working.Allocator.NextActionId(),
                        ActionKind.ChooseBehavior,
                        choice.SelectedCardIds.Count == 0
                            ? "Finish empty behavior selection"
                            : $"Finish behavior selection ({choice.SelectedCardIds.Count} card(s))",
                        new BehaviorChoiceParameters(
                            choice.Key,
                            cardIds: choice.SelectedCardIds,
                            finalizeSelection: true)));
                }
            }
            else
            {
                IEnumerable<CardInstanceId[]> selections = EnumerateCardSelections(
                    choice.CandidateCardIds,
                    choice.CardinalityKind,
                    choice.CardinalityCount);
                foreach (CardInstanceId[] selection in selections)
                {
                    actions.Add(new LegalAction(
                        working.Allocator.NextActionId(),
                        ActionKind.ChooseBehavior,
                        selection.Length == 0 ? "Skip card selection" : $"Choose {selection.Length} card(s)",
                        new BehaviorChoiceParameters(choice.Key, cardIds: selection)));
                }
            }
        }
        else
        {
            foreach (string optionKey in choice.OptionKeys)
            {
                BehaviorChoiceOption? option = specification?.Options
                    .SingleOrDefault(candidate => candidate.Key == optionKey);
                actions.Add(new LegalAction(
                    working.Allocator.NextActionId(),
                    ActionKind.ChooseBehavior,
                    option is null ? $"Choose '{optionKey}'" : DescribeBehaviorChoiceOption(option, []),
                    new BehaviorChoiceParameters(choice.Key, optionKey)));
            }

            if (choice.Mode == BehaviorChoiceMode.Optional
                && choice.CardinalityKind == BehaviorChoiceCardinalityKind.UpTo
                && choice.CardinalityCount == 1)
            {
                actions.Add(new LegalAction(
                    working.Allocator.NextActionId(),
                    ActionKind.ChooseBehavior,
                    "Skip optional choice",
                    new BehaviorChoiceParameters(choice.Key)));
            }
        }

        if (actions.Count == 0)
        {
            throw new ResolutionFailureException($"Behavior choice '{choice.Key}' produced no legal actions.");
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            choice.Owner,
            DecisionKind.BehaviorChoice,
            $"Choose behavior option '{choice.Key}'",
            actions);
    }

    private static BehaviorChoiceSpec? GetCurrentBehaviorChoiceSpecification(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        List<BehaviorInstruction> instructions = GetBehaviorInstructions(working, frame);
        return frame.InstructionCursor >= 0
            && frame.InstructionCursor < instructions.Count
            && instructions[frame.InstructionCursor] is { Kind: BehaviorInstructionKind.Choose } instruction
            ? instruction.Choice
            : null;
    }

    private static bool HasBehaviorChoiceAdditionalCardCost(BehaviorChoiceOption option) =>
        option.Costs?.Any(static cost => cost.Kind == BehaviorCostKind.Additional) == true;

    private static string DescribeBehaviorChoiceOption(
        BehaviorChoiceOption option,
        CardInstanceId[] selectedCards)
    {
        string semantic = option.Semantic is { } value
            ? $" [{value}]"
            : string.Empty;
        if (selectedCards.Length == 0)
        {
            return $"Choose '{option.Key}'{semantic}";
        }

        return $"Choose '{option.Key}'{semantic} (additional cost: {selectedCards.Length} card(s))";
    }

    private static bool IsBehaviorChoiceOptionAffordable(
        WorkingState working,
        PlayerId owner,
        BehaviorChoiceOption option)
    {
        try
        {
            PlayerState payer = GetPlayer(working, owner);
            long credits = 0;
            long clicks = 0;
            foreach (BehaviorCost cost in option.Costs ?? [])
            {
                switch (cost.Kind)
                {
                    case BehaviorCostKind.Credits:
                        credits = checked(credits + cost.Amount);
                        break;
                    case BehaviorCostKind.Clicks:
                        clicks = checked(clicks + cost.Amount);
                        break;
                    case BehaviorCostKind.Additional:
                        // Exact candidate cardinality and source ownership are
                        // checked by the bounded combination materializer.
                        break;
                    default:
                        return false;
                }
            }

            return credits <= int.MaxValue && credits <= AvailableCreditsForPayment(working, owner)
                && clicks <= payer.Clicks
                && (!HasBehaviorChoiceAdditionalCardCost(option)
                    || EnumerateBehaviorChoiceAdditionalSelections(working, option).Any());
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or InvalidOperationException
            or KeyNotFoundException
            or OverflowException
            or ArgumentException)
        {
            return false;
        }
    }

    // StateInvariantValidator deliberately has no access to the mutable
    // reducer workspace.  These read-only seams replay the exact reducer
    // validation against a cloned workspace so a forged legal-action payload
    // cannot pass validation merely because it has a plausible shape.
    internal static bool IsBehaviorChoiceOptionAffordable(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        PlayerId owner,
        BehaviorChoiceOption option)
    {
        try
        {
            WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
            return IsBehaviorChoiceOptionAffordable(working, owner, option);
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or InvalidOperationException
            or KeyNotFoundException
            or OverflowException
            or ArgumentException)
        {
            return false;
        }
    }

    internal static bool IsBehaviorChoiceActionLegal(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        BehaviorChoiceParameters parameters)
    {
        try
        {
            WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
            if (working.ResolutionStack[^1] is not BehaviorResolutionFrame frame
                || frame.Choice is not BehaviorChoiceMaterialization choice
                || !string.Equals(choice.Key, parameters.ChoiceKey, StringComparison.Ordinal))
            {
                return false;
            }

            ValidateBehaviorChoiceParameters(working, frame, choice, parameters);
            if (parameters.FinalizeSelection && parameters.OptionKeys.Count == 1)
            {
                List<BehaviorInstruction> instructions = GetBehaviorInstructions(working, frame);
                if (frame.InstructionCursor < 0
                    || frame.InstructionCursor >= instructions.Count
                    || instructions[frame.InstructionCursor].Choice is not { } specification)
                {
                    return false;
                }

                BehaviorChoiceOption? option = specification.Options.SingleOrDefault(candidate =>
                    string.Equals(candidate.Key, parameters.OptionKeys[0], StringComparison.Ordinal));
                if (option is null || !IsBehaviorChoiceOptionAffordable(working, choice.Owner, option))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or InvalidOperationException
            or KeyNotFoundException
            or OverflowException
            or ArgumentException
            or IndexOutOfRangeException)
        {
            return false;
        }
    }

    private static IEnumerable<CardInstanceId[]> EnumerateBehaviorChoiceAdditionalSelections(
        WorkingState working,
        BehaviorChoiceOption option)
    {
        BehaviorCost[] additionalCosts = (option.Costs ?? [])
            .Where(static cost => cost.Kind == BehaviorCostKind.Additional)
            .ToArray();
        if (additionalCosts.Length == 0)
        {
            yield return [];
            yield break;
        }

        var candidateSets = new List<(BehaviorCost Cost, CardInstanceId[] Candidates)>();
        foreach (BehaviorCost cost in additionalCosts)
        {
            if (cost.AdditionalKind != BehaviorAdditionalCostKind.TrashCards
                || cost.AdditionalTarget is not
                {
                    Kind: BehaviorTargetKind.ZoneCards,
                    Zone: { } sourceZone,
                    Filter: not null,
                    Reveal: false,
                }
                || cost.AdditionalTarget.Filter.OwnerSide is not { } ownerSide
                || FindExistingBehaviorChoiceCostZone(working, sourceZone, ownerSide) is not { } zone
                || !BehaviorZoneMatches(sourceZone, zone.Kind, ownerSide))
            {
                yield break;
            }

            CardInstanceId[] candidates = zone.CardIds
                .Where(cardId => GetCard(working, cardId).OwnerSide == ownerSide
                    && BehaviorCardMatches(working, cardId, cost.AdditionalTarget.Filter))
                .ToArray();
            if (candidates.Length < cost.Amount)
            {
                yield break;
            }

            candidateSets.Add((cost, candidates));
        }

        var results = new List<CardInstanceId[]>();
        var used = new HashSet<CardInstanceId>();
        var selected = new List<CardInstanceId>();
        int explorations = 0;
        bool exceeded = false;

        void Visit(int index)
        {
            if (exceeded || ++explorations > 4096)
            {
                exceeded = true;
                return;
            }

            if (index == candidateSets.Count)
            {
                results.Add(selected.ToArray());
                if (results.Count >= 256)
                {
                    exceeded = true;
                }

                return;
            }

            (BehaviorCost cost, CardInstanceId[] candidates) = candidateSets[index];
            foreach (CardInstanceId[] choice in EnumerateExactSelections(candidates, cost.Amount))
            {
                if (choice.Any(used.Contains))
                {
                    continue;
                }

                foreach (CardInstanceId cardId in choice)
                {
                    used.Add(cardId);
                    selected.Add(cardId);
                }

                Visit(index + 1);

                foreach (CardInstanceId cardId in choice)
                {
                    selected.RemoveAt(selected.Count - 1);
                    used.Remove(cardId);
                }

                if (exceeded)
                {
                    return;
                }
            }
        }

        Visit(0);
        foreach (CardInstanceId[] result in results)
        {
            yield return result;
        }
    }

    private static ZoneState? FindExistingBehaviorChoiceCostZone(
        WorkingState working,
        BehaviorZone zone,
        PlayerSide ownerSide)
    {
        ZoneKind kind = zone switch
        {
            BehaviorZone.Headquarters => ZoneKind.CorpHand,
            BehaviorZone.Archives => ZoneKind.CorpDiscard,
            BehaviorZone.Grip => ZoneKind.RunnerGrip,
            BehaviorZone.Stack => ZoneKind.RunnerStack,
            BehaviorZone.Deck => ownerSide == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack,
            BehaviorZone.Heap => ZoneKind.RunnerHeap,
            _ => (ZoneKind)(-1),
        };
        return Enum.IsDefined(kind)
            ? working.Zones.FirstOrDefault(candidate => candidate.Kind == kind
                && candidate.OwnerSide == ownerSide
                && candidate.ServerId is null)
            : null;
    }

    private static void ValidateBehaviorChoiceOptionCards(
        WorkingState working,
        BehaviorChoiceOption option,
        IReadOnlyList<CardInstanceId> selected)
    {
        if (!HasBehaviorChoiceAdditionalCardCost(option))
        {
            throw new ResolutionFailureException("The behavior option does not declare a card cost.");
        }

        BehaviorCost[] additionalCosts = (option.Costs ?? [])
            .Where(static cost => cost.Kind == BehaviorCostKind.Additional)
            .ToArray();
        int requiredCount = additionalCosts.Sum(static cost => cost.Amount);
        if (selected.Count != requiredCount || selected.Distinct().Count() != selected.Count)
        {
            throw new ResolutionFailureException(
                "A structured behavior option must submit the exact distinct card-cost selection.");
        }

        int offset = 0;
        var used = new HashSet<CardInstanceId>();
        foreach (BehaviorCost cost in additionalCosts)
        {
            if (cost.AdditionalTarget is not
                {
                    Kind: BehaviorTargetKind.ZoneCards,
                    Zone: { } sourceZone,
                    Filter: not null,
                    Reveal: false,
                }
                || cost.AdditionalTarget.Filter.OwnerSide is not { } ownerSide
                || FindExistingBehaviorChoiceCostZone(working, sourceZone, ownerSide) is not { } source
                || !BehaviorZoneMatches(sourceZone, source.Kind, ownerSide))
            {
                throw new ResolutionFailureException(
                    "The structured behavior option has no valid private owned source zone.");
            }

            foreach (CardInstanceId cardId in selected.Skip(offset).Take(cost.Amount))
            {
                if (!used.Add(cardId)
                    || !source.CardIds.Contains(cardId)
                    || !BehaviorCardMatches(working, cardId, cost.AdditionalTarget.Filter)
                    || GetCard(working, cardId).OwnerSide != ownerSide)
                {
                    throw new ResolutionFailureException(
                        $"Behavior option card '{cardId}' is stale or not a legal private cost card.");
                }
            }

            offset += cost.Amount;
        }
    }

    private static void PayBehaviorChoiceCosts(
        WorkingState working,
        PlayerId owner,
        BehaviorChoiceOption option,
        IReadOnlyList<CardInstanceId> selectedCards)
    {
        if (HasBehaviorChoiceAdditionalCardCost(option))
        {
            ValidateBehaviorChoiceOptionCards(working, option, selectedCards);
        }
        else if (selectedCards.Count > 0)
        {
            throw new ResolutionFailureException(
                "A behavior option without a card cost cannot carry selected card IDs.");
        }

        PlayerState payer = GetPlayer(working, owner);
        long credits = 0;
        long clicks = 0;
        foreach (BehaviorCost cost in option.Costs ?? [])
        {
            switch (cost.Kind)
            {
                case BehaviorCostKind.Credits:
                    credits = checked(credits + cost.Amount);
                    break;
                case BehaviorCostKind.Clicks:
                    clicks = checked(clicks + cost.Amount);
                    break;
                case BehaviorCostKind.Additional:
                    break;
                default:
                    throw new ResolutionFailureException(
                        $"Choice option cost '{cost.Kind}' is not executable.");
            }
        }

        if (credits > int.MaxValue || credits > AvailableCreditsForPayment(working, owner) || clicks > payer.Clicks)
        {
            throw new ResolutionFailureException(
                "The behavior option cost is no longer affordable by its chooser.");
        }

        // All validation above runs before the first mutation, preserving the
        // atomic rejection contract for stale/forged option payloads.
        if (credits != 0)
        {
            PayCreditCost(working, owner, (int)credits);
        }

        if (clicks != 0)
        {
            ApplyClickDelta(working, owner, checked(-(int)clicks));
        }

        int offset = 0;
        foreach (BehaviorCost cost in (option.Costs ?? [])
            .Where(static cost => cost.Kind == BehaviorCostKind.Additional))
        {
            BehaviorTarget target = cost.AdditionalTarget!;
            PlayerSide ownerSide = target.Filter!.OwnerSide!.Value;
            ZoneState source = FindExistingBehaviorChoiceCostZone(
                    working,
                    target.Zone!.Value,
                    ownerSide)
                ?? throw new ResolutionFailureException("The behavior option source zone disappeared during payment.");
            ZoneState destination = GetBehaviorZone(
                working,
                ownerSide == PlayerSide.Corp ? BehaviorZone.Archives : BehaviorZone.Heap,
                ownerSide);
            foreach (CardInstanceId cardId in selectedCards.Skip(offset).Take(cost.Amount).ToArray())
            {
                CardInstanceState card = GetCard(working, cardId);
                TrashCard(working, card, source, destination);
                CardInstanceState trashed = GetCard(working, cardId);
                ReplaceCard(working, new CardInstanceState(
                    trashed.Id,
                    trashed.DefinitionId,
                    trashed.ZoneId,
                    trashed.OwnerSide,
                    faceUp: true,
                    rezzed: false,
                    trashed.AdvancementCounters));
                source = FindExistingBehaviorChoiceCostZone(working, target.Zone.Value, ownerSide)
                    ?? throw new ResolutionFailureException("The behavior option source zone disappeared during payment.");
                destination = GetBehaviorZone(
                    working,
                    ownerSide == PlayerSide.Corp ? BehaviorZone.Archives : BehaviorZone.Heap,
                    ownerSide);
            }

            offset += cost.Amount;
        }
    }

    private static bool RequiresIncrementalSelection(BehaviorChoiceMaterialization choice)
    {
        int max = choice.CardinalityKind == BehaviorChoiceCardinalityKind.AnyNumber
            ? choice.CandidateCardIds.Count
            : Math.Min(choice.CardinalityCount ?? 0, choice.CandidateCardIds.Count);
        int min = choice.CardinalityKind == BehaviorChoiceCardinalityKind.Exactly
            ? max
            : 0;
        long combinations = 0;
        for (int size = min; size <= max; size++)
        {
            combinations += CombinationCountCapped(
                choice.CandidateCardIds.Count,
                size,
                MaxMaterializedBehaviorChoices);
            if (combinations > MaxMaterializedBehaviorChoices)
            {
                return true;
            }
        }

        return false;
    }

    private static long CombinationCountCapped(int n, int k, int cap)
    {
        if (k < 0 || k > n)
        {
            return 0;
        }

        k = Math.Min(k, n - k);
        long result = 1;
        for (int index = 1; index <= k; index++)
        {
            result = result * (n - k + index) / index;
            if (result > cap)
            {
                return cap + 1L;
            }
        }

        return result;
    }

    private static IEnumerable<CardInstanceId[]> EnumerateCardSelections(
        IReadOnlyList<CardInstanceId> candidates,
        BehaviorChoiceCardinalityKind kind,
        int? count)
    {
        int max = kind == BehaviorChoiceCardinalityKind.AnyNumber
            ? candidates.Count
            : Math.Min(count ?? 0, candidates.Count);
        int min = kind == BehaviorChoiceCardinalityKind.Exactly ? max : 0;
        if (kind == BehaviorChoiceCardinalityKind.Exactly && candidates.Count < (count ?? 0))
        {
            yield break;
        }

        for (int size = min; size <= max; size++)
        {
            foreach (CardInstanceId[] selection in Combinations(candidates, size))
            {
                yield return selection;
            }
        }

        static IEnumerable<CardInstanceId[]> Combinations(IReadOnlyList<CardInstanceId> values, int size)
        {
            if (size == 0)
            {
                yield return [];
                yield break;
            }

            var current = new CardInstanceId[size];
            foreach (CardInstanceId[] result in Walk(0, 0))
            {
                yield return result;
            }

            IEnumerable<CardInstanceId[]> Walk(int start, int depth)
            {
                if (depth == size)
                {
                    yield return current.ToArray();
                    yield break;
                }

                int remaining = size - depth;
                for (int index = start; index <= values.Count - remaining; index++)
                {
                    current[depth] = values[index];
                    foreach (CardInstanceId[] result in Walk(index + 1, depth + 1))
                    {
                        yield return result;
                    }
                }
            }
        }
    }

    private static PlayerId ResolveBehaviorChooser(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorChooser chooser)
    {
        PlayerSide side = chooser switch
        {
            BehaviorChooser.Corp => PlayerSide.Corp,
            BehaviorChooser.Runner => PlayerSide.Runner,
            BehaviorChooser.Opponent => GetPlayer(working, frame.Controller).Side == PlayerSide.Corp
                ? PlayerSide.Runner
                : PlayerSide.Corp,
            BehaviorChooser.ActingPlayer => frame.ActingPlayer.HasValue
                ? GetPlayer(working, frame.ActingPlayer.Value).Side
                : GetCard(working, frame.SourceCardId).OwnerSide,
            BehaviorChooser.Controller => GetPlayer(working, frame.Controller).Side,
            _ => throw new ResolutionFailureException($"Chooser '{chooser}' is not supported."),
        };
        return GetPlayer(working, side).Id;
    }

    private static CardInstanceId[] MaterializeBehaviorCandidates(
        WorkingState working,
        BehaviorTarget target,
        CardInstanceId? excludedCardId = null)
    {
        if (target.Kind is BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards)
        {
            ZoneState zone = GetBehaviorZone(working, target.Zone!.Value, target.Filter?.OwnerSide);
            return zone.CardIds
                .Where(cardId => !excludedCardId.HasValue || cardId != excludedCardId.Value)
                .Where(cardId => BehaviorCardMatches(working, cardId, target.Filter))
                .ToArray();
        }

        if (target.Kind is BehaviorTargetKind.InstalledCard or BehaviorTargetKind.InstalledCardNotThisTurn)
        {
            PlayerSide? owner = target.Filter?.OwnerSide;
            return working.Cards
                .Where(card => IsInstalledCard(working, card.Id)
                    && (owner is null || card.OwnerSide == owner.Value)
                    && (target.Kind != BehaviorTargetKind.InstalledCardNotThisTurn
                        || !IsInstalledThisTurn(working, card.Id, owner))
                    && BehaviorCardMatches(working, card.Id, target.Filter))
                .OrderBy(card => card.Id.Value, StringComparer.Ordinal)
                .Select(card => card.Id)
                .ToArray();
        }

        return [];
    }

    /// <summary>
    /// Evaluates the top-level target declarations that gate ordinary Event
    /// or Operation play. These are action preconditions, not instructions:
    /// they are projected before the PlayCard action is issued and checked
    /// again by the play reducer before it spends a click or credit.
    /// </summary>
    private static bool AreBehaviorPlayTargetsAvailable(
        WorkingState working,
        CardBehaviorDefinition definition)
    {
        foreach (BehaviorTarget target in definition.Targets)
        {
            bool available;
            try
            {
                available = target.Kind switch
                {
                    BehaviorTargetKind.Self => working.Cards.Any(card => card.DefinitionId == definition.DefinitionId),
                    BehaviorTargetKind.Controller => working.Players.Count > 0,
                    BehaviorTargetKind.Opponent => working.Players.Count >= 2,
                    BehaviorTargetKind.NamedCard => IsNamedBehaviorTargetAvailable(working, target),
                    BehaviorTargetKind.Server => working.Servers.Count > 0,
                    BehaviorTargetKind.AccessedCard => working.RunState?.ChosenAccessCards.Count > 0,
                    BehaviorTargetKind.ChosenCard => false,
                    BehaviorTargetKind.ZoneCard
                        or BehaviorTargetKind.ZoneCards
                        or BehaviorTargetKind.InstalledCard
                        or BehaviorTargetKind.InstalledCardNotThisTurn
                        => MaterializeBehaviorCandidates(working, target).Length > 0,
                    _ => false,
                };
            }
            catch (ResolutionFailureException)
            {
                available = false;
            }

            if (!available)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNamedBehaviorTargetAvailable(
        WorkingState working,
        BehaviorTarget target)
    {
        if (CardInstanceId.TryParse(target.Selector, out CardInstanceId cardId))
        {
            return working.Cards.Any(card => card.Id == cardId);
        }

        return working.Cards.Any(card => string.Equals(
            card.DefinitionId.Value,
            target.Selector,
            StringComparison.Ordinal));
    }

    private static bool IsBehaviorInstallOption(BehaviorChoiceOption option) =>
        option.Instructions.Count == 1
        && option.Instructions[0].Kind == BehaviorInstructionKind.InstallCard;

    private static bool IsBehaviorInstallOptionAvailable(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorChoiceOption option)
    {
        if (!IsBehaviorInstallOption(option))
        {
            return true;
        }

        try
        {
            _ = ValidateBehaviorInstallInstruction(working, frame, option.Instructions[0]);
            return true;
        }
        catch (ResolutionFailureException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool IsInstalledThisTurn(WorkingState working, CardInstanceId cardId, PlayerSide? owner)
    {
        IEnumerable<TurnFactsState?> facts = owner.HasValue
            ? [working.Turn.History?.ThisTurn(owner.Value)]
            : [
                working.Turn.History?.CorpThisTurn,
                working.Turn.History?.RunnerThisTurn,
            ];
        return facts.Any(current => current?.InstalledCardIds.Contains(cardId) == true);
    }

    private static bool BehaviorCardMatches(WorkingState working, CardInstanceId cardId, BehaviorCardFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        CardInstanceState card = GetCard(working, cardId);
        if (filter.Rezzed is { } requiredRezzed && card.Rezzed != requiredRezzed) return false;
        if (filter.DefinitionId.HasValue && card.DefinitionId != filter.DefinitionId.Value)
        {
            return false;
        }

        if (filter.OwnerSide.HasValue && card.OwnerSide != filter.OwnerSide.Value)
        {
            return false;
        }

        // Once a runtime catalog is present it is the sole authority for
        // executable card classification. BehaviorCatalog families are a
        // compatibility fallback for the catalog-free Core fixtures only;
        // using them alongside runtime metadata would allow a stale behavior
        // registration to make an unknown or mismatched physical card legal.
        if (working.RuntimeCardCatalog is not null)
        {
            if (!working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? runtimeSpec))
            {
                return false;
            }

            if (filter.Subtype is not null
                && !runtimeSpec.Subtypes.Contains(filter.Subtype, StringComparer.Ordinal))
            {
                return false;
            }

            return filter.CardType switch
            {
                BehaviorCardType.Any => true,
                BehaviorCardType.NonAgenda => runtimeSpec.CardClass != RuntimeCardClass.Agenda,
                BehaviorCardType.Agenda => runtimeSpec.CardClass == RuntimeCardClass.Agenda,
                BehaviorCardType.Asset => runtimeSpec.CardClass == RuntimeCardClass.Asset,
                BehaviorCardType.Event => runtimeSpec.CardClass == RuntimeCardClass.Event,
                BehaviorCardType.Hardware => runtimeSpec.CardClass == RuntimeCardClass.Hardware,
                BehaviorCardType.Ice => runtimeSpec.CardClass == RuntimeCardClass.Ice,
                BehaviorCardType.Icebreaker => runtimeSpec.CardClass == RuntimeCardClass.Program
                    && runtimeSpec.Subtypes.Contains("icebreaker", StringComparer.Ordinal),
                BehaviorCardType.Program => runtimeSpec.CardClass == RuntimeCardClass.Program,
                BehaviorCardType.ProgramOrHardware => runtimeSpec.CardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware,
                BehaviorCardType.Identity => runtimeSpec.CardClass == RuntimeCardClass.Identity,
                BehaviorCardType.Operation => runtimeSpec.CardClass == RuntimeCardClass.Operation,
                BehaviorCardType.Resource => runtimeSpec.CardClass == RuntimeCardClass.Resource,
                BehaviorCardType.Upgrade => runtimeSpec.CardClass == RuntimeCardClass.Upgrade,
                _ => false,
            };
        }

        if (filter.CardType == BehaviorCardType.Any)
        {
            return true;
        }

        return filter.CardType switch
        {
            BehaviorCardType.ProgramOrHardware => working.BehaviorCatalog.TryGetCardType(card.DefinitionId, out BehaviorCardType installedClass)
                && installedClass is BehaviorCardType.Program or BehaviorCardType.Icebreaker or BehaviorCardType.Hardware,
            BehaviorCardType.Ice => working.BehaviorCatalog.TryGetIce(card.DefinitionId, out _),
            BehaviorCardType.Icebreaker => working.BehaviorCatalog.TryGetIcebreaker(card.DefinitionId, out _),
            BehaviorCardType.Asset => working.BehaviorCatalog.TryGetAsset(card.DefinitionId, out _),
            BehaviorCardType.Agenda => working.BehaviorCatalog.TryGetAgenda(card.DefinitionId, out _),
            BehaviorCardType.Event => working.BehaviorCatalog.TryGetRunEvent(card.DefinitionId, out _)
                || working.BehaviorCatalog.TryGetCardType(card.DefinitionId, out BehaviorCardType eventClassified)
                && eventClassified == filter.CardType,
            _ => working.BehaviorCatalog.TryGetCardType(card.DefinitionId, out BehaviorCardType classified)
                && classified == filter.CardType,
        };
    }

    private static void ConsumeBehaviorChoice(WorkingState working, BehaviorChoiceParameters parameters)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not BehaviorResolutionFrame frame
            || frame.Choice is not BehaviorChoiceMaterialization choice)
        {
            throw new ResolutionFailureException("The submitted behavior choice does not match the active frame.");
        }

        if (!string.Equals(parameters.ChoiceKey, choice.Key, StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("The submitted behavior choice key is stale.");
        }

        ValidateBehaviorChoiceParameters(working, frame, choice, parameters);
        if (!parameters.FinalizeSelection)
        {
            CardInstanceId selectedCard = parameters.CardIds.Single();
            var selectedCards = choice.SelectedCardIds
                .Append(selectedCard)
                .ToArray();
            var nextChoice = new BehaviorChoiceMaterialization(
                choice.Key,
                choice.Owner,
                choice.Mode,
                choice.CardinalityKind,
                choice.CardinalityCount,
                choice.OptionKeys,
                choice.CandidateCardIds,
                selectedCards);
            BehaviorResolutionFrame partial = frame.With(choice: nextChoice);
            ReplaceTopFrame(working, partial);
            YieldBehaviorActions(working, partial, nextChoice);
            return;
        }

        List<BehaviorInstruction> currentInstructions = GetBehaviorInstructions(working, frame);
        BehaviorInstruction current = currentInstructions[frame.InstructionCursor];
        BehaviorChoiceOption? option = parameters.OptionKeys.Count == 0
            ? null
            : current.Choice?.Options.SingleOrDefault(candidate => candidate.Key == parameters.OptionKeys[0]);
        if (parameters.OptionKeys.Count > 0 && option is null)
        {
            throw new ResolutionFailureException("The submitted behavior option is not part of the current declaration.");
        }

        bool optionHasCardCost = option is not null && HasBehaviorChoiceAdditionalCardCost(option);
        IReadOnlyList<CardInstanceId> selected = parameters.CardIds;
        IReadOnlyList<BehaviorSetAsideCard> setAside = frame.SetAside;
        IReadOnlyList<BehaviorBinding> bindings = frame.Bindings;
        ZoneId? setAsideZoneId = frame.SetAsideZoneId;
        if (option is not null && option.Costs is { Count: > 0 })
        {
            // Additional card costs remain in their private source zone until
            // this authenticated option is accepted. PayBehaviorChoiceCosts
            // validates the complete bundle before mutating any wallet,
            // click, or card state and then trashes the exact IDs.
            PayBehaviorChoiceCosts(working, choice.Owner, option!, selected);
            if (optionHasCardCost)
            {
                selected = [];
            }
        }
        else if (choice.CandidateCardIds.Count > 0 && selected.Count > 0)
        {
            BehaviorTarget? candidateTarget = current.Kind == BehaviorInstructionKind.Search
                ? current.Target
                : current.Choice?.Target;
            bool isInstalledTarget = candidateTarget?.Kind is
                BehaviorTargetKind.InstalledCard or BehaviorTargetKind.InstalledCardNotThisTurn;
            if (!isInstalledTarget)
            {
                bool reveal = candidateTarget?.Reveal == true;
                setAside = SetAsideBehaviorCards(
                    working,
                    frame,
                    selected,
                    reveal,
                    out ZoneId materializedSetAsideZoneId);
                setAsideZoneId = materializedSetAsideZoneId;
            }

            // Installed/public selections are already in an authoritative
            // public zone. Freeze their identity in the binding, but retain
            // physical custody in that zone; only hidden-zone/search cards
            // use the transient set-aside zone.
            bindings = SetBehaviorBinding(bindings, choice.Key, selected);
            bindings = SetBehaviorBinding(bindings, "chosen", selected);
        }
        else if (current.Kind == BehaviorInstructionKind.Search)
        {
            // An explicit fail-to-find is an authenticated empty Search
            // continuation. It supersedes the prior search's selection and
            // must not leave an older key or canonical chosen binding
            // consumable by downstream instructions.
            bindings = ClearBehaviorSelectionBindings(bindings, choice.Key);
        }

        if (option is not null
            && IsBehaviorInstallOption(option)
            && !IsBehaviorInstallOptionAvailable(working, frame.With(bindings: bindings), option))
        {
            throw new ResolutionFailureException("The behavior install option is stale or no longer legal.");
        }

        BehaviorContinuationStep continuationStep;
        if (option is not null)
        {
            continuationStep = new BehaviorContinuationStep(
                frame.InstructionCursor,
                choice.Key,
                BehaviorContinuationKind.ChoiceOption,
                option.Key);
        }
        else
        {
            continuationStep = new BehaviorContinuationStep(
                frame.InstructionCursor,
                choice.Key,
                BehaviorContinuationKind.ChoiceSelection,
                cardIds: selected);
        }

        var instructions = currentInstructions
            .Take(frame.InstructionCursor)
            .Concat(option?.Instructions ?? [])
            .Concat(currentInstructions.Skip(frame.InstructionCursor + 1))
            .ToArray();
        BehaviorResolutionFrame next = frame.With(
            instructions: instructions,
            instructionCursor: frame.InstructionCursor,
            bindings: bindings,
            setAside: setAside,
            clearChoice: true,
            setAsideZoneId: setAsideZoneId,
            continuation: frame.Continuation.Append(continuationStep).ToArray());
        ReplaceTopFrame(working, next);
    }

    private static void ValidateBehaviorChoiceParameters(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorChoiceMaterialization choice,
        BehaviorChoiceParameters parameters)
    {
        bool hasOptions = parameters.OptionKeys.Count > 0;
        bool hasCards = parameters.CardIds.Count > 0;
        List<BehaviorInstruction> currentInstructions = GetBehaviorInstructions(working, frame);
        BehaviorInstruction current = currentInstructions[frame.InstructionCursor];
        BehaviorChoiceOption? option = parameters.OptionKeys.Count == 1
            ? current.Choice?.Options.SingleOrDefault(candidate => candidate.Key == parameters.OptionKeys[0])
            : null;
        bool optionHasCardCost = option is not null && HasBehaviorChoiceAdditionalCardCost(option);
        if (hasOptions && hasCards && !optionHasCardCost)
        {
            throw new ResolutionFailureException("A behavior choice cannot select both options and cards.");
        }

        if (optionHasCardCost)
        {
            if (choice.CandidateCardIds.Count != 0
                || !parameters.FinalizeSelection
                || parameters.OptionKeys.Count != 1
                || !choice.OptionKeys.Contains(option!.Key, StringComparer.Ordinal))
            {
                throw new ResolutionFailureException(
                    "A structured card-cost option must submit one frozen option key and a complete card selection.");
            }

            ValidateBehaviorChoiceOptionCards(working, option, parameters.CardIds);
            return;
        }

        if (!parameters.FinalizeSelection)
        {
            if (choice.CandidateCardIds.Count == 0
                || parameters.OptionKeys.Count != 0
                || parameters.CardIds.Count != 1
                || choice.SelectedCardIds.Contains(parameters.CardIds[0]))
            {
                throw new ResolutionFailureException("The incremental behavior selection payload is invalid.");
            }

            if (!choice.CandidateCardIds.Contains(parameters.CardIds[0]))
            {
                throw new ResolutionFailureException("The incremental behavior selection is not a frozen candidate.");
            }

            if (choice.CardinalityKind == BehaviorChoiceCardinalityKind.Exactly
                && choice.SelectedCardIds.Count >= choice.CardinalityCount)
            {
                throw new ResolutionFailureException("The exact behavior selection is already complete.");
            }

            return;
        }

        if (choice.CandidateCardIds.Count > 0)
        {
            if (hasOptions)
            {
                throw new ResolutionFailureException("A card target choice cannot select an option key.");
            }

            if (parameters.CardIds.Any(cardId => !choice.CandidateCardIds.Contains(cardId))
                || parameters.CardIds.Distinct().Count() != parameters.CardIds.Count)
            {
                throw new ResolutionFailureException("The submitted card selection is not a frozen candidate set.");
            }

            int count = parameters.CardIds.Count;
            if (choice.CardinalityKind == BehaviorChoiceCardinalityKind.Exactly
                && count != choice.CardinalityCount)
            {
                throw new ResolutionFailureException("The submitted card selection has the wrong cardinality.");
            }

            if (choice.CardinalityKind == BehaviorChoiceCardinalityKind.UpTo
                && count > choice.CardinalityCount)
            {
                throw new ResolutionFailureException("The submitted card selection exceeds its cardinality bound.");
            }

            if (choice.CardinalityKind == BehaviorChoiceCardinalityKind.AnyNumber
                && count > choice.CandidateCardIds.Count)
            {
                throw new ResolutionFailureException("The submitted card selection exceeds its candidate set.");
            }

            if (RequiresIncrementalSelection(choice)
                && !parameters.CardIds.SequenceEqual(choice.SelectedCardIds))
            {
                throw new ResolutionFailureException("The submitted behavior selection does not match its frozen incremental state.");
            }

            foreach (CardInstanceId cardId in parameters.CardIds)
            {
                CardInstanceState card = GetCard(working, cardId);
                BehaviorInstruction instruction = currentInstructions[frame.InstructionCursor];
                BehaviorTarget target = instruction.Kind == BehaviorInstructionKind.Search
                    ? instruction.Target!
                    : instruction.Choice!.Target!;
                if (!MaterializeBehaviorCandidates(working, target, frame.SourceCardId).Contains(card.Id))
                {
                    throw new ResolutionFailureException($"Behavior candidate '{cardId}' is stale.");
                }
            }

            return;
        }

        if (parameters.OptionKeys.Count > 1
            || parameters.OptionKeys.Any(key => !choice.OptionKeys.Contains(key, StringComparer.Ordinal)))
        {
            throw new ResolutionFailureException("The submitted behavior option is not a frozen option.");
        }

        if (choice.Mode is BehaviorChoiceMode.Mode or BehaviorChoiceMode.OrderSelection
            && parameters.OptionKeys.Count != 1)
        {
            throw new ResolutionFailureException("This behavior choice requires exactly one option.");
        }

        if (choice.Mode == BehaviorChoiceMode.Optional
            && parameters.OptionKeys.Count > 1)
        {
            throw new ResolutionFailureException("An optional behavior choice selects at most one option.");
        }

        if (choice.Mode is BehaviorChoiceMode.Mandatory or BehaviorChoiceMode.Mode or BehaviorChoiceMode.OrderSelection
            && parameters.OptionKeys.Count == 0)
        {
            throw new ResolutionFailureException("This behavior choice cannot be skipped.");
        }
    }

    private static List<BehaviorSetAsideCard> SetAsideBehaviorCards(
        WorkingState working,
        BehaviorResolutionFrame frame,
        IReadOnlyList<CardInstanceId> cardIds,
        bool reveal,
        out ZoneId setAsideZoneId)
    {
        ZoneState allocatedSetAsideZone = EnsureBehaviorSetAsideZone(working, frame.SetAsideZoneId);
        ZoneId allocatedSetAsideZoneId = allocatedSetAsideZone.Id;
        setAsideZoneId = allocatedSetAsideZoneId;
        var result = frame.SetAside.ToList();
        foreach (CardInstanceId cardId in cardIds)
        {
            if (result.Any(card => card.CardId == cardId))
            {
                continue;
            }

            CardInstanceState card = GetCard(working, cardId);
            ZoneState source = ZoneForCard(working, cardId);
            int originalIndex = source.CardIds
                .Select((candidate, index) => (candidate, index))
                .Where(entry => entry.candidate == cardId)
                .Select(entry => entry.index)
                .DefaultIfEmpty(-1)
                .First();
            // MoveCard replaces both source and destination zones. Resolve
            // the current destination on every iteration so a multi-card
            // selection appends to the updated transient zone rather than a
            // stale snapshot captured before the first move.
            ZoneState currentSetAsideZone = working.Zones.SingleOrDefault(zone => zone.Id == allocatedSetAsideZoneId)
                ?? throw new ResolutionFailureException("The behavior set-aside zone is missing.");
            MoveCard(working, card, source, currentSetAsideZone, emit: false);
            result.Add(new BehaviorSetAsideCard(
                card.Id,
                source.Id,
                card.FaceUp,
                card.Rezzed,
                originalIndex));
            if (reveal)
            {
                Emit(
                    working,
                    EngineEventKinds.BehaviorCardRevealed,
                    new BehaviorCardRevealedEventPayload(
                        card.Id,
                        card.DefinitionId,
                        source.Id,
                        card.OwnerSide));
            }
        }

        return result;
    }

    private static ZoneState EnsureBehaviorSetAsideZone(WorkingState working, ZoneId? existingId)
    {
        if (existingId is { } zoneId)
        {
            return working.Zones.SingleOrDefault(zone => zone.Id == zoneId)
                ?? throw new ResolutionFailureException("The behavior set-aside zone is missing.");
        }

        var zone = new ZoneState(working.Allocator.NextZoneId(), ZoneKind.BehaviorSetAside);
        working.Zones.Add(zone);
        return zone;
    }

    private static void RestoreBehaviorSetAside(WorkingState working, BehaviorResolutionFrame frame)
    {
        foreach (BehaviorSetAsideCard selected in frame.SetAside
            .OrderByDescending(entry => entry.OriginalIndex))
        {
            CardInstanceState card = GetCard(working, selected.CardId);
            ZoneState source = ZoneForCard(working, selected.CardId);
            if (source.Kind != ZoneKind.BehaviorSetAside)
            {
                throw new ResolutionFailureException(
                    $"Behavior selection '{selected.CardId}' is not in the transient set-aside zone.");
            }

            ZoneState destination = working.Zones.SingleOrDefault(zone => zone.Id == selected.OriginalZoneId)
                ?? throw new ResolutionFailureException(
                    $"Behavior selection '{selected.CardId}' original zone '{selected.OriginalZoneId}' is missing.");
            MoveCard(
                working,
                card,
                source,
                destination,
                emit: source.Kind != ZoneKind.BehaviorSetAside
                    || destination.Kind is ZoneKind.CorpDiscard
                        or ZoneKind.RunnerHeap
                        or ZoneKind.RunnerRig
                        or ZoneKind.RunnerPlayArea
                        or ZoneKind.ServerIce
                        or ZoneKind.ServerRoot);
            CardInstanceState restored = GetCard(working, selected.CardId);
            ReplaceCard(working, new CardInstanceState(
                restored.Id,
                restored.DefinitionId,
                restored.ZoneId,
                restored.OwnerSide,
                selected.OriginalFaceUp,
                selected.OriginalRezzed,
                restored.AdvancementCounters));
            if (selected.OriginalIndex >= 0)
            {
                ZoneState current = ZoneForCard(working, selected.CardId);
                var ids = current.CardIds.Where(id => id != selected.CardId).ToList();
                ids.Insert(Math.Min(selected.OriginalIndex, ids.Count), selected.CardId);
                ReplaceZone(working, new ZoneState(
                    current.Id,
                    current.Kind,
                    current.OwnerSide,
                    current.ServerId,
                    ids));
            }
        }

        if (frame.SetAsideZoneId is { } setAsideZoneId)
        {
            ZoneState? setAsideZone = working.Zones.SingleOrDefault(zone => zone.Id == setAsideZoneId);
            if (setAsideZone is not null && setAsideZone.CardIds.Count == 0)
            {
                working.Zones.Remove(setAsideZone);
            }
        }
    }

    private static BehaviorBinding[] SetBehaviorBinding(
        IReadOnlyList<BehaviorBinding> bindings,
        string key,
        IReadOnlyList<CardInstanceId> cardIds)
    {
        return bindings
            .Where(binding => !string.Equals(binding.Key, key, StringComparison.Ordinal))
            .Append(new BehaviorBinding(key, cardIds))
            .ToArray();
    }

    private static BehaviorBinding[] ClearBehaviorSelectionBindings(
        IReadOnlyList<BehaviorBinding> bindings,
        string selectionKey) =>
        bindings
            .Where(binding => !string.Equals(binding.Key, selectionKey, StringComparison.Ordinal)
                && !string.Equals(binding.Key, "chosen", StringComparison.Ordinal))
            .ToArray();

    private static void ResolveBehaviorTransfer(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction,
        BehaviorTransferKind kind)
    {
        if (kind == BehaviorTransferKind.Shuffle
            && instruction.Target is { Kind: BehaviorTargetKind.ChosenCard }
            && IsProvenEmptyChosenCard(frame, instruction.Target))
        {
            ResolveProvenEmptySelectionShuffle(working, frame, instruction);
            return;
        }

        if (kind == BehaviorTransferKind.Trash
            && instruction.Target is { Kind: BehaviorTargetKind.AccessedCard })
        {
            CardInstanceState accessed = ResolveAccessedCardTarget(working, frame);
            ZoneState source = ZoneForCard(working, accessed.Id);
            ZoneState discard = GetBehaviorZone(working, BehaviorZone.Archives, PlayerSide.Corp);
            TrashCard(working, accessed, source, discard);

            // Access trash is a public reveal even when the card was an
            // unrevealed root card.  Keep this normalization local to the
            // special operation so ordinary behavior trash retains its
            // existing visibility contract.
            CardInstanceState trashed = GetCard(working, accessed.Id);
            ReplaceCard(working, new CardInstanceState(
                trashed.Id,
                trashed.DefinitionId,
                trashed.ZoneId,
                trashed.OwnerSide,
                faceUp: true,
                rezzed: false,
                trashed.AdvancementCounters));
            RunState run = working.RunState
                ?? throw new ResolutionFailureException("An accessed-card trash requires a live run.");
            Emit(
                working,
                EngineEventKinds.AccessedCardTrashed,
                new AccessedCardTrashedEventPayload(run.Id, accessed.Id, trashCost: 0));
            AdvanceBehaviorCursor(working, frame);
            QueueIdentityOccurrence(working, OccurrenceKind.AccessedCardTrashed, PlayerSide.Runner, accessed.Id);
            return;
        }

        IReadOnlyList<CardInstanceId> cardIds = ResolveBehaviorCards(working, frame, instruction.Target);
        if (cardIds.Count == 0)
        {
            if (instruction.Target is { Kind: BehaviorTargetKind.ChosenCard }
                && IsProvenEmptyChosenCard(frame, instruction.Target))
            {
                AdvanceBehaviorCursor(working, frame);
                return;
            }

            throw new ResolutionFailureException($"Behavior instruction '{instruction.Kind}' resolved no cards.");
        }

        var remainingSetAside = frame.SetAside.ToList();
        var remainingBindings = frame.Bindings.ToList();
        foreach (CardInstanceId cardId in cardIds)
        {
            CardInstanceState card = GetCard(working, cardId);
            ZoneState source = ZoneForCard(working, cardId);
            BehaviorSetAsideCard? frozenSelection = frame.SetAside.SingleOrDefault(selected => selected.CardId == cardId);
            if (instruction.SourceZone.HasValue
                && !BehaviorZoneMatches(instruction.SourceZone.Value, source.Kind, card.OwnerSide)
                && (frozenSelection is null
                    || !BehaviorZoneMatches(
                        instruction.SourceZone.Value,
                        working.Zones.Single(zone => zone.Id == frozenSelection.OriginalZoneId).Kind,
                        card.OwnerSide)))
            {
                throw new ResolutionFailureException($"Behavior card '{cardId}' is not in the declared source zone.");
            }

            ZoneState destination = kind switch
            {
                BehaviorTransferKind.Trash => GetBehaviorZone(working, card.OwnerSide == PlayerSide.Corp ? BehaviorZone.Archives : BehaviorZone.Heap, card.OwnerSide),
                BehaviorTransferKind.AddToHand => GetBehaviorZone(working, card.OwnerSide == PlayerSide.Corp ? BehaviorZone.Headquarters : BehaviorZone.Grip, card.OwnerSide),
                BehaviorTransferKind.Shuffle => GetBehaviorZone(working, instruction.DestinationZone ?? throw new ResolutionFailureException("Shuffle has no destination."), card.OwnerSide),
                BehaviorTransferKind.Move => GetBehaviorZone(working, instruction.DestinationZone ?? throw new ResolutionFailureException("Move has no destination."), card.OwnerSide),
                _ => throw new ResolutionFailureException("Unknown behavior transfer."),
            };

            // A search can set a card aside and then shuffle it back into the
            // same hidden zone (for example Runner Stack). The frozen
            // selection is still consumed, but no physical move is needed in
            // that case; attempting MoveCard would incorrectly reject the
            // legal same-zone shuffle.
            if (source.Id != destination.Id)
            {
                bool emit = source.Kind != ZoneKind.BehaviorSetAside
                    || destination.Kind is ZoneKind.CorpDiscard
                        or ZoneKind.RunnerHeap
                        or ZoneKind.RunnerRig
                        or ZoneKind.RunnerPlayArea
                        or ZoneKind.ServerIce
                        or ZoneKind.ServerRoot;
                if (kind == BehaviorTransferKind.Trash)
                {
                    TrashCard(working, card, source, destination, emit);
                }
                else
                {
                    MoveCard(working, card, source, destination, emit);
                }
            }
            else if (source.Kind == ZoneKind.BehaviorSetAside)
            {
                throw new ResolutionFailureException("A behavior set-aside card must be reinserted before it can be shuffled.");
            }
            RestoreBehaviorVisibility(working, cardId, destination, frozenSelection, kind);
            remainingSetAside.RemoveAll(selected => selected.CardId == cardId);
            if (kind != BehaviorTransferKind.Shuffle)
            {
                remainingBindings = remainingBindings
                    .Select(binding => string.Equals(binding.Key, "trigger-subject", StringComparison.Ordinal)
                        ? binding
                        : new BehaviorBinding(binding.Key, binding.CardIds.Where(id => id != cardId).ToArray()))
                    .Where(binding => binding.CardIds.Count > 0)
                    .ToList();
            }
        }

        if (kind == BehaviorTransferKind.Shuffle)
        {
            ZoneState destination = GetBehaviorZone(
                working,
                instruction.DestinationZone ?? throw new ResolutionFailureException("Shuffle has no destination."),
                GetCard(working, cardIds[0]).OwnerSide);
            var shuffled = destination.CardIds.ToList();
            var random = DeterministicRandom.Restore(working.RandomState);
            random.Shuffle(shuffled);
            working.RandomState = random.State;
            ReplaceZone(working, new ZoneState(
                destination.Id,
                destination.Kind,
                destination.OwnerSide,
                destination.ServerId,
                shuffled));
            Emit(
                working,
                EngineEventKinds.BehaviorCardsShuffled,
                new BehaviorCardsShuffledEventPayload(destination.Id, cardIds.Count));
        }

        if (remainingSetAside.Count == 0 && frame.SetAsideZoneId is { } emptiedZoneId)
        {
            ZoneState? emptiedZone = working.Zones.SingleOrDefault(zone => zone.Id == emptiedZoneId);
            if (emptiedZone is not null && emptiedZone.CardIds.Count == 0)
            {
                working.Zones.Remove(emptiedZone);
            }
        }

        AdvanceBehaviorCursor(
            working,
            frame.With(
                bindings: remainingBindings,
                setAside: remainingSetAside,
                clearSetAsideZone: remainingSetAside.Count == 0));
    }

    private static void ResolveProvenEmptySelectionShuffle(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction shuffleInstruction)
    {
        BehaviorInstruction? searchInstruction = LatestEmptySearchContinuation(
            working,
            frame);
        if (searchInstruction is not null)
        {
            if (searchInstruction.SourceZone is not (BehaviorZone.Deck or BehaviorZone.Stack)
                || shuffleInstruction.SourceZone != searchInstruction.SourceZone
                || shuffleInstruction.DestinationZone != searchInstruction.SourceZone)
            {
                throw new ResolutionFailureException(
                    "An empty chosen-card shuffle must immediately follow a reachable empty Deck or Stack search and name the same source and destination zone.");
            }

            ShuffleProvenEmptyZone(
                working,
                frame,
                searchInstruction.SourceZone.Value,
                GetPlayer(working, frame.Controller).Side);
            return;
        }

        (BehaviorInstruction Instruction, BehaviorChoiceSpec Choice, BehaviorContinuationStep Selection)? choiceOrigin =
            LatestEmptyChoiceContinuation(working, frame);
        if (choiceOrigin is not
            {
                Instruction: { Kind: BehaviorInstructionKind.Choose },
                Choice: { Target: { } target } choice,
                Selection: { } selection
            })
        {
            throw new ResolutionFailureException(
                "An empty chosen-card shuffle must be backed by a reachable empty search or target choice.");
        }

        if (shuffleInstruction.Target?.Selector is not { } selector
            || !StableIdentifier.TryValidate(selector)
            || !string.Equals(selector, selection.Key, StringComparison.Ordinal)
            || target.Kind is not (BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards)
            || target.Zone is not { } sourceZone
            || shuffleInstruction.SourceZone != sourceZone
            || choice.Cardinality.Kind is not
                (BehaviorChoiceCardinalityKind.UpTo or BehaviorChoiceCardinalityKind.AnyNumber)
            || shuffleInstruction.DestinationZone is not { } destinationZone
            || destinationZone != (ResolveBehaviorOwner(target, frame, working) == PlayerSide.Corp
                ? BehaviorZone.Deck
                : BehaviorZone.Stack))
        {
            throw new ResolutionFailureException(
                "An empty chosen-card shuffle must match a reachable zero-permitted target choice, source zone, selector, and owner-correct Deck or Stack destination.");
        }

        PlayerSide ownerSide = ResolveBehaviorOwner(target, frame, working);
        ZoneState source = GetBehaviorZone(working, sourceZone, ownerSide);
        if (source.OwnerSide != ownerSide)
        {
            throw new ResolutionFailureException(
                "An empty chosen-card shuffle target owner does not match its source zone.");
        }

        ShuffleProvenEmptyZone(working, frame, destinationZone, ownerSide);
    }

    private static PlayerSide ResolveBehaviorOwner(
        BehaviorTarget target,
        BehaviorResolutionFrame frame,
        WorkingState working) =>
        target.Filter?.OwnerSide
        ?? GetPlayer(working, frame.Controller).Side;

    private static void ShuffleProvenEmptyZone(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorZone destinationZone,
        PlayerSide ownerSide)
    {
        ZoneState destination = GetBehaviorZone(
            working,
            destinationZone,
            ownerSide);
        var shuffled = destination.CardIds.ToList();
        var random = DeterministicRandom.Restore(working.RandomState);
        random.Shuffle(shuffled);
        working.RandomState = random.State;
        ReplaceZone(
            working,
            new ZoneState(
                destination.Id,
                destination.Kind,
                destination.OwnerSide,
                destination.ServerId,
                shuffled));
        Emit(
            working,
            EngineEventKinds.BehaviorCardsShuffled,
            new BehaviorCardsShuffledEventPayload(destination.Id, 0));
        AdvanceBehaviorCursor(working, frame);
    }

    private static (BehaviorInstruction Instruction, BehaviorChoiceSpec Choice, BehaviorContinuationStep Selection)?
        LatestEmptyChoiceContinuation(
            WorkingState working,
            BehaviorResolutionFrame frame)
    {
        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> rootInstructions = frame.AbilityKey is not null
            ? GetAbilityDeclaration(definition, frame.AbilityKey).Instructions
            : frame.TriggerKey is not null
                ? GetTriggerDeclaration(definition, frame.TriggerKey).Instructions
                : definition.Instructions;
        List<BehaviorInstruction> replay = rootInstructions.ToList();
        (BehaviorInstruction Instruction, BehaviorChoiceSpec Choice, BehaviorContinuationStep Selection)? latest = null;
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
            {
                return null;
            }

            BehaviorInstruction instruction = replay[step.InstructionCursor];
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection)
            {
                latest = step.CardIds.Count == 0
                    && instruction.Kind == BehaviorInstructionKind.Choose
                    && instruction.Choice is { } choice
                    ? (instruction, choice, step)
                    : null;
            }

            if (!BehaviorContinuation.TryApply(replay, step, out _))
            {
                return null;
            }
        }

        return latest;
    }

    private static BehaviorInstruction? LatestEmptySearchContinuation(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> rootInstructions = frame.AbilityKey is not null
            ? GetAbilityDeclaration(definition, frame.AbilityKey).Instructions
            : frame.TriggerKey is not null
                ? GetTriggerDeclaration(definition, frame.TriggerKey).Instructions
                : definition.Instructions;
        List<BehaviorInstruction> replay = rootInstructions.ToList();
        BehaviorInstruction? latestEmptySearch = null;
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
            {
                return null;
            }

            BehaviorInstruction instruction = replay[step.InstructionCursor];
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection)
            {
                latestEmptySearch = step.CardIds.Count == 0
                    && instruction.Kind == BehaviorInstructionKind.Search
                    ? instruction
                    : null;
            }

            if (!BehaviorContinuation.TryApply(replay, step, out _))
            {
                return null;
            }
        }

        return latestEmptySearch;
    }

    private static void RestoreBehaviorVisibility(
        WorkingState working,
        CardInstanceId cardId,
        ZoneState destination,
        BehaviorSetAsideCard? frozenSelection,
        BehaviorTransferKind kind)
    {
        CardInstanceState card = GetCard(working, cardId);
        bool faceUp = kind switch
        {
            BehaviorTransferKind.Shuffle when destination.Kind is ZoneKind.CorpDeck or ZoneKind.RunnerStack => false,
            BehaviorTransferKind.Shuffle => frozenSelection?.OriginalFaceUp ?? card.FaceUp,
            BehaviorTransferKind.AddToHand => false,
            BehaviorTransferKind.Trash => destination.Kind == ZoneKind.RunnerHeap
                || (frozenSelection?.OriginalFaceUp ?? card.FaceUp),
            _ => card.FaceUp,
        };
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            card.ZoneId,
            card.OwnerSide,
            faceUp,
            card.Rezzed && faceUp,
            card.AdvancementCounters));
    }

    private static void ResolveBehaviorAdvance(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        // This legacy instruction name represents placing advancement counters
        // (for example Seamless Launch), not taking an advance action. CR 1.18.2
        // explicitly distinguishes the two: do not emit an advance occurrence
        // or trigger advance-from-zero abilities for this counter placement.
        IReadOnlyList<CardInstanceId> cardIds = ResolveBehaviorCards(working, frame, instruction.Target);
        if (instruction.Target is { Kind: BehaviorTargetKind.ChosenCard })
        {
            ValidateChosenAdvanceTargets(working, frame, instruction.Target, cardIds);
        }

        foreach (CardInstanceId cardId in cardIds)
        {
            CardInstanceState card = GetCard(working, cardId);
            int advancementCounters = checked(card.AdvancementCounters + instruction.Amount);
            ReplaceCard(working, new CardInstanceState(
                card.Id,
                card.DefinitionId,
                card.ZoneId,
                card.OwnerSide,
                card.FaceUp,
                card.Rezzed,
                advancementCounters));
            Emit(working, EngineEventKinds.BehaviorCardAdvanced, new BehaviorCardAdvancedEventPayload(
                card.Id,
                instruction.Amount,
                advancementCounters));
        }

        AdvanceBehaviorCursor(working, frame);
    }

    /// <summary>
    /// A chosen-card advance is only valid when the binding was produced by
    /// a reachable catalog-declared installed-card choice. A serialized
    /// binding (or the card's current zone alone) is not sufficient proof:
    /// the originating selector is reconstructed from the immutable catalog
    /// and continuation path, then evaluated against current state.
    /// </summary>
    private static void ValidateChosenAdvanceTargets(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorTarget chosenTarget,
        IReadOnlyList<CardInstanceId> cardIds)
    {
        if (cardIds.Count == 0)
        {
            throw new ResolutionFailureException("A chosen-card advance resolved no cards.");
        }

        string bindingKey = StableIdentifier.TryValidate(chosenTarget.Selector)
            ? chosenTarget.Selector!
            : "chosen";
        BehaviorBinding binding = frame.Bindings.SingleOrDefault(candidate => candidate.Key == bindingKey)
            ?? throw new ResolutionFailureException($"Behavior binding '{bindingKey}' is not available.");
        if (!binding.CardIds.SequenceEqual(cardIds))
        {
            throw new ResolutionFailureException("The chosen-card advance binding is inconsistent with its continuation.");
        }

        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> rootInstructions = frame.AbilityKey is not null
            ? GetAbilityDeclaration(definition, frame.AbilityKey).Instructions
            : frame.TriggerKey is not null
                ? GetTriggerDeclaration(definition, frame.TriggerKey).Instructions
                : definition.Instructions;
        List<BehaviorInstruction> replay = rootInstructions.ToList();
        BehaviorTarget? originatingTarget = null;
        IReadOnlyList<CardInstanceId>? originatingSelection = null;
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
            {
                throw new ResolutionFailureException("The chosen-card advance continuation is not reachable.");
            }

            BehaviorInstruction instruction = replay[step.InstructionCursor];
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection
                && step.CardIds.Count > 0
                && (string.Equals(step.Key, bindingKey, StringComparison.Ordinal)
                    || (bindingKey == "chosen"
                        && frame.Bindings.Any(candidate =>
                            string.Equals(candidate.Key, step.Key, StringComparison.Ordinal)
                            && candidate.CardIds.SequenceEqual(binding.CardIds)))))
            {
                originatingTarget = instruction.Kind switch
                {
                    BehaviorInstructionKind.Choose => instruction.Choice?.Target,
                    BehaviorInstructionKind.Search => instruction.Target,
                    _ => null,
                };
                originatingSelection = step.CardIds;
            }

            if (!BehaviorContinuation.TryApply(replay, step, out string? error))
            {
                throw new ResolutionFailureException(
                    $"The chosen-card advance continuation is not reachable: {error}");
            }
        }

        if (originatingTarget is null
            || originatingTarget.Kind is not
                (BehaviorTargetKind.InstalledCard or BehaviorTargetKind.InstalledCardNotThisTurn)
            || originatingSelection is null
            || !originatingSelection.SequenceEqual(binding.CardIds))
        {
            throw new ResolutionFailureException(
                "A chosen-card advance requires a reachable installed-card selection as its origin.");
        }

        CardInstanceId[] currentCandidates = MaterializeBehaviorCandidates(working, originatingTarget);
        if (cardIds.Any(cardId => !currentCandidates.Contains(cardId)))
        {
            throw new ResolutionFailureException(
                "The chosen installed-card target is stale or no longer matches its selector.");
        }
    }

    private static bool IsProvenEmptyChosenCard(
        BehaviorResolutionFrame frame,
        BehaviorTarget target)
    {
        string? selector = StableIdentifier.TryValidate(target.Selector)
            ? target.Selector
            : null;
        BehaviorContinuationStep? latestSelection = frame.Continuation
            .LastOrDefault(step => step.Kind == BehaviorContinuationKind.ChoiceSelection);
        return latestSelection is not null
            && latestSelection.CardIds.Count == 0
            && (selector is null
                || string.Equals(latestSelection.Key, selector, StringComparison.Ordinal));
    }

    private static IReadOnlyList<CardInstanceId> ResolveBehaviorCards(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorTarget? target)
    {
        if (target is null)
        {
            throw new ResolutionFailureException("A card behavior instruction has no target.");
        }

        switch (target.Kind)
        {
            case BehaviorTargetKind.Self:
                return [frame.SourceCardId];
            case BehaviorTargetKind.ChosenCard:
                string key = StableIdentifier.TryValidate(target.Selector) ? target.Selector! : "chosen";
                return frame.Bindings.SingleOrDefault(binding => binding.Key == key)?.CardIds
                    ?? (IsProvenEmptyChosenCard(frame, target)
                        ? []
                        : throw new ResolutionFailureException($"Behavior binding '{key}' is not available."));
            case BehaviorTargetKind.NamedCard:
                if (CardInstanceId.TryParse(target.Selector, out CardInstanceId named))
                {
                    return [named];
                }

                CardInstanceState? match = working.Cards.FirstOrDefault(card => card.DefinitionId.Value == target.Selector);
                return match is null ? [] : [match.Id];
            case BehaviorTargetKind.AccessedCard:
                return [ResolveAccessedCardTarget(working, frame).Id];
            case BehaviorTargetKind.InstalledCard:
            case BehaviorTargetKind.InstalledCardNotThisTurn:
                CardInstanceId[] installed = MaterializeBehaviorCandidates(working, target);
                if (installed.Length == 0)
                {
                    throw new ResolutionFailureException("The installed-card target has no legal card.");
                }

                return installed;
            case BehaviorTargetKind.ZoneCard:
            case BehaviorTargetKind.ZoneCards:
                return MaterializeBehaviorCandidates(working, target);
            default:
                throw new ResolutionFailureException($"Card target kind '{target.Kind}' is not executable.");
        }
    }

    private static CardInstanceState ResolveAccessedCardTarget(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        if (working.ResolutionStack.Count < 2
            || working.ResolutionStack[^2] is not AbilityFrame ability
            || ability.TargetCardIds.Count != 1
            || working.RunState is not { } run
            || !TryGetCurrentAccessCard(working, run, out CardInstanceState? accessed)
            || ability.TargetCardIds[0] != accessed!.Id)
        {
            throw new ResolutionFailureException(
                "The accessed-card target is not the exact live target frozen by its ability frame.");
        }

        return accessed;
    }

    private static void AdvanceBehaviorCursor(WorkingState working, BehaviorResolutionFrame frame)
    {
        BehaviorResolutionFrame next = frame is null
            ? throw new ArgumentNullException(nameof(frame))
            : frame.With(instructionCursor: checked(frame.InstructionCursor + 1), clearChoice: true);
        ReplaceTopFrame(working, next);
    }

    private static void ValidateBehaviorOccurrenceContinuation(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        BehaviorOccurrenceContinuation? continuation = frame.OccurrenceContinuation;
        if (frame.Stage == BehaviorResolutionStage.AwaitingOccurrence)
        {
            if (continuation is null
                || continuation.Kind != OccurrenceKind.HostedCreditPoolEmptied
                || continuation.SourceCardId != frame.SourceCardId
                || continuation.FrameIdentity != frame.FrameIdentity
                || continuation.InstructionCursor != frame.InstructionCursor
                || continuation.SourceMovedAfterCompletion
                || frame.Choice is not null)
            {
                throw new ResolutionFailureException(
                    "An awaiting hosted-credit occurrence continuation is malformed or stale.");
            }

            if (working.ResolutionStack.Count < 2
                || working.ResolutionStack[^2] is not EffectResolutionFrame)
            {
                throw new ResolutionFailureException(
                    "An awaiting hosted-credit occurrence must retain its nested effect child.");
            }
        }
        else if (continuation is not null
            && (!continuation.SourceMovedAfterCompletion
                || frame.Stage != BehaviorResolutionStage.Execute
                || continuation.SourceCardId != frame.SourceCardId
                || continuation.FrameIdentity != frame.FrameIdentity))
        {
            throw new ResolutionFailureException(
                "A hosted-credit occurrence continuation is only retained as completed moved-source authorization.");
        }
    }

    private static bool IsAuthorizedHostedCreditMovement(
        WorkingState working,
        BehaviorResolutionFrame frame,
        CardInstanceState source,
        ZoneState currentSourceZone)
    {
        BehaviorOccurrenceContinuation? continuation = frame.OccurrenceContinuation;
        if (frame.Stage != BehaviorResolutionStage.Execute
            || continuation is null
            || !continuation.SourceMovedAfterCompletion
            || continuation.Kind != OccurrenceKind.HostedCreditPoolEmptied
            || continuation.SourceCardId != frame.SourceCardId
            || continuation.FrameIdentity != frame.FrameIdentity
            || source.OwnerSide != GetPlayer(working, frame.Controller).Side
            || currentSourceZone.Kind != (source.OwnerSide == PlayerSide.Corp
                ? ZoneKind.CorpDiscard
                : ZoneKind.RunnerHeap)
            || working.RulesState is null
            || working.RulesState.Counters.Any(counter =>
                counter.CardId == frame.SourceCardId
                && counter.Purpose == CardCounterPurpose.HostedCredit))
        {
            return false;
        }

        return HasReachableHostedCreditSelfTrashTrigger(working, frame);
    }

    private static void QueueHostedCreditPoolEmptiedOccurrence(
        WorkingState working,
        BehaviorResolutionFrame frame)
    {
        if (frame.FrameIdentity is not { } frameIdentity)
        {
            throw new ResolutionFailureException(
                "A hosted-credit occurrence continuation requires an issued behavior frame identity.");
        }

        int nextCursor = checked(frame.InstructionCursor + 1);
        BehaviorOccurrenceContinuation continuation = new(
            OccurrenceKind.HostedCreditPoolEmptied,
            frame.SourceCardId,
            frameIdentity,
            nextCursor);
        ReplaceTopFrame(
            working,
            frame.With(
                stage: BehaviorResolutionStage.AwaitingOccurrence,
                instructionCursor: nextCursor,
                clearChoice: true,
                occurrenceContinuation: continuation));
        working.ResolutionStack.Add(new EffectResolutionFrame(
            frame.Controller,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.HostedCreditPoolEmptied,
                frame.Controller,
                frame.SourceCardId))));
    }

    private static bool IsInstalledCard(WorkingState working, CardInstanceId cardId)
    {
        ZoneKind kind = ZoneForCard(working, cardId).Kind;
        return kind is ZoneKind.RunnerRig or ZoneKind.ServerIce or ZoneKind.ServerRoot;
    }

    private static void MoveBehaviorCardToTrash(WorkingState working, CardInstanceState card)
    {
        ZoneState source = ZoneForCard(working, card.Id);
        ZoneState destination = GetBehaviorZone(
            working,
            card.OwnerSide == PlayerSide.Corp ? BehaviorZone.Archives : BehaviorZone.Heap,
            card.OwnerSide);
        // Cost payment may declare TrashSelf before a counter or hosted-credit
        // cost. Keep the card's counters available until the complete payment
        // loop has run; the caller clears them at that boundary.
        TrashCard(working, card, source, destination, cleanupCounters: false);
    }

    private static void MoveBehaviorCardToRemovedFromGame(WorkingState working, CardInstanceState card)
    {
        ZoneState source = ZoneForCard(working, card.Id);
        MoveBehaviorCardToCostZone(working, card, BehaviorZone.RemovedFromGame, source);
    }

    private static void MoveBehaviorCardToCostZone(
        WorkingState working,
        CardInstanceState card,
        BehaviorZone destinationZone,
        ZoneState source)
    {
        ZoneState destination = GetBehaviorZone(working, destinationZone, card.OwnerSide);
        // Cost payment may declare TrashSelf before a counter or hosted-credit
        // cost. Keep the card's counters available until the complete payment
        // loop has run; the caller clears them at that boundary.
        MoveCard(working, card, source, destination, cleanupCounters: false);
    }

    private static ZoneState ZoneForCard(WorkingState working, CardInstanceId cardId) =>
        working.Zones.SingleOrDefault(zone => zone.CardIds.Contains(cardId))
        ?? throw new ResolutionFailureException($"Card '{cardId}' does not belong to a zone.");

    private static bool BehaviorZoneMatches(
        BehaviorZone expected,
        ZoneKind actual,
        PlayerSide? ownerSide = null) => expected switch
        {
            BehaviorZone.Deck => actual is ZoneKind.CorpDeck or ZoneKind.RunnerStack,
            BehaviorZone.Headquarters => actual == ZoneKind.CorpHand,
            BehaviorZone.Archives => actual == ZoneKind.CorpDiscard,
            BehaviorZone.Grip => actual == ZoneKind.RunnerGrip,
            BehaviorZone.Stack => actual == ZoneKind.RunnerStack,
            BehaviorZone.Heap => actual == ZoneKind.RunnerHeap,
            BehaviorZone.Rig => actual == ZoneKind.RunnerRig,
            BehaviorZone.PlayArea => actual == ZoneKind.RunnerPlayArea,
            BehaviorZone.ScoreArea => actual is ZoneKind.CorpScore or ZoneKind.RunnerScore,
            BehaviorZone.ServerIce => actual == ZoneKind.ServerIce,
            BehaviorZone.ServerRoot => actual == ZoneKind.ServerRoot,
            BehaviorZone.RemovedFromGame => ownerSide switch
            {
                PlayerSide.Corp => actual == ZoneKind.CorpRemovedFromGame,
                PlayerSide.Runner => actual == ZoneKind.RunnerRemovedFromGame,
                _ => false,
            },
            _ => false,
        };

    private static ZoneState GetBehaviorZone(WorkingState working, BehaviorZone zone, PlayerSide? ownerSide)
    {
        PlayerSide side = ownerSide ?? GetPlayer(working, PlayerSide.Corp).Side;
        ZoneKind kind = zone switch
        {
            BehaviorZone.Deck => side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack,
            BehaviorZone.Headquarters => ZoneKind.CorpHand,
            BehaviorZone.Archives => ZoneKind.CorpDiscard,
            BehaviorZone.Grip => ZoneKind.RunnerGrip,
            BehaviorZone.Stack => ZoneKind.RunnerStack,
            BehaviorZone.Heap => ZoneKind.RunnerHeap,
            BehaviorZone.Rig => ZoneKind.RunnerRig,
            BehaviorZone.PlayArea => ZoneKind.RunnerPlayArea,
            BehaviorZone.ScoreArea => side == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore,
            BehaviorZone.ServerIce => ZoneKind.ServerIce,
            BehaviorZone.ServerRoot => ZoneKind.ServerRoot,
            BehaviorZone.RemovedFromGame => side == PlayerSide.Corp
                ? ZoneKind.CorpRemovedFromGame
                : ZoneKind.RunnerRemovedFromGame,
            _ => throw new ResolutionFailureException($"Behavior zone '{zone}' is not executable."),
        };

        if (kind == ZoneKind.RunnerPlayArea)
        {
            ZoneState? existing = working.Zones.FirstOrDefault(candidate => candidate.Kind == kind && candidate.ServerId is null);
            if (existing is not null)
            {
                return existing;
            }

            var created = new ZoneState(working.Allocator.NextZoneId(), kind, PlayerSide.Runner);
            working.Zones.Add(created);
            return created;
        }

        if (kind is ZoneKind.CorpRemovedFromGame or ZoneKind.RunnerRemovedFromGame)
        {
            ZoneState? existing = working.Zones.FirstOrDefault(candidate => candidate.Kind == kind && candidate.ServerId is null);
            if (existing is not null)
            {
                return existing;
            }

            var created = new ZoneState(working.Allocator.NextZoneId(), kind, side);
            working.Zones.Add(created);
            return created;
        }

        if (kind == ZoneKind.ServerRoot)
        {
            return GetRemoteRoot(working);
        }

        if (kind == ZoneKind.ServerIce)
        {
            return working.Servers
                .Where(server => server.Kind == ServerKind.Remote && server.Exists)
                .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
                .Select(server => working.Zones.Single(zoneState => zoneState.Id == server.IceZoneId))
                .FirstOrDefault()
                ?? throw new ResolutionFailureException("No remote ICE zone is available.");
        }

        return GetZone(working, kind);
    }

    private static void MarkInstalledThisTurn(WorkingState working, CardInstanceId cardId)
    {
        PlayerSide activeSide = working.Turn.ActiveSide;
        TurnHistoryState? history = working.Turn.History;
        TurnFactsState? facts = history?.ThisTurn(activeSide);
        if (facts is null
            || facts.Side != activeSide
            || facts.TurnNumber != working.Turn.TurnNumber)
        {
            return;
        }

        ReplaceTurn(
            working,
            working.Turn.ActiveSide,
            working.Turn.Phase,
            working.Turn.TurnNumber,
            working.Turn.Step,
            history!.MarkInstalled(activeSide, cardId));
    }

}
