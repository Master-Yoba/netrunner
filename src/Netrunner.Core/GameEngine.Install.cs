namespace Netrunner.Core;

/// <summary>Install and agenda play reducers for the legacy toy rules.</summary>
public static partial class GameEngine
{
    /// <summary>VP-INSTALL-FACEUP: an agenda installed faceup, neither rezzed nor unrezzed (Sacrifice Zone Expansion).</summary>
    private static bool InstallsFaceUp(WorkingState working, CardDefinitionId definitionId) =>
        working.BehaviorCatalog.TryGetAgenda(definitionId, out AgendaBehaviorSpec? agenda) && agenda.InstallsFaceUp;

    private sealed record InstallPlan(
        CardInstanceState Card,
        CardRuntimeSpec Spec,
        ZoneState Source,
        InstallDestinationKind DestinationKind,
        ServerId? ServerId,
        bool CreateNewRemote,
        ZoneId? DestinationZoneId,
        int InstallCost,
        CostQuote? InstallCostQuote,
        IReadOnlyList<CardInstanceId> CandidateReplacementCardIds,
        IReadOnlyList<CardInstanceId> RequiredReplacementCardIds,
        IReadOnlyList<CardDefinitionId> CandidateReplacementDefinitionIds,
        CardInstanceId? InwardAnchorIceId);

    /// <summary>
    /// The live run cursor before an Ansel-style normal ICE install mutates
    /// the attacked server's ordered ICE zone.  Replacement selection is
    /// allowed to include the currently encountered ICE, so cursor repair is
    /// based on stable physical IDs rather than a pre-trash numeric index.
    /// </summary>
    private sealed record RunIceCursorSnapshot(
        RunId RunId,
        EncounterId EncounterId,
        ServerId ServerId,
        CardInstanceId CurrentIceId,
        int CurrentIceIndex,
        IReadOnlyList<CardInstanceId> IceIds);

    private static void InstallToyCard(WorkingState working, PlayerSide side, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneKind handKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneState hand = GetZone(working, handKind);
        if (card.OwnerSide != side || card.ZoneId != hand.Id)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not in the active player's hand.");
        }

        if (behavior.OwnerSide != side
            || (side == PlayerSide.Corp && behavior.Kind != ToyCardKind.CorpAgenda)
            || (side == PlayerSide.Runner && behavior.Kind != ToyCardKind.RunnerProgram))
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not installable by '{side}'.");
        }

        PlayerState player = GetPlayer(working, side);
        if (player.Credits < behavior.InstallCost)
        {
            throw new ResolutionFailureException($"Installing '{cardId}' requires {behavior.InstallCost} credits.");
        }

        ZoneState destination = side == PlayerSide.Corp
            ? GetRemoteRoot(working)
            : GetZone(working, ZoneKind.RunnerRig);
        if (side == PlayerSide.Corp && destination.CardIds.Count > 0)
        {
            throw new ResolutionFailureException("The fixture remote already contains an installed card.");
        }

        MoveCard(working, card, hand, destination);
        MarkInstalledThisTurn(working, cardId);
        if (side == PlayerSide.Runner && behavior.Kind == ToyCardKind.RunnerProgram)
        {
            RecordTurnProgramInstall(working, cardId);
        }

        ApplyCreditDelta(working, player.Id, -behavior.InstallCost);
        Emit(working, EngineEventKinds.CardInstalled, new CardInstalledEventPayload(
            side,
            cardId,
            hand.Id,
            destination.Id,
            behavior.InstallCost));
    }

    // ---------------------------------------------------------------------
    // Real CR 8.5 basic install (ENG-405 Phase B1)
    // ---------------------------------------------------------------------

    private static void StepInstall(WorkingState working, InstallFrame frame)
    {
        InstallPlan plan = ValidateInstallFrame(working, frame, requireFinalLegality: false);
        if (frame.CandidateReplacementCardIds.Count == 0 && !NeedsInstallHostChoice(working, frame))
        {
            CommitInstall(working, frame, plan);
            return;
        }

        YieldInstallChoice(working, frame, plan);
    }

    private static void YieldInstallChoice(
        WorkingState working,
        InstallFrame frame,
        InstallPlan plan)
    {
        List<InstallCardParameters> materialized = MaterializeInstallChoiceParameters(working, frame, plan);
        var actions = new List<LegalAction>(materialized.Count);
        foreach (InstallCardParameters parameters in materialized)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.InstallCard,
                parameters.FinalizeSelection ? "Install selected card" : "Select install replacement",
                parameters));
        }

        if (actions.Count == 0)
        {
            throw new ResolutionFailureException("The install selection has no legal continuation.");
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            frame.Owner,
            DecisionKind.InstallChoice,
            "Choose cards to trash before installing",
            actions);
    }

    /// <summary>
    /// Materializes the exact semantic InstallChoice continuation payloads.
    /// This projection is deliberately read-only: it never asks the allocator
    /// for an ID and does not mutate the supplied working state.  Yielding a
    /// pending decision wraps these payloads in action IDs, while validation
    /// compares a detached projection against serialized actions.
    /// </summary>
    private static List<InstallCardParameters> MaterializeInstallChoiceParameters(
        WorkingState working,
        InstallFrame frame,
        InstallPlan plan)
    {
        if (NeedsInstallHostChoice(working, frame))
        {
            return InstallHosts(working, plan.Card).Select(hostId => new InstallCardParameters(
                frame.CardId, frame.DestinationKind, frame.ServerId, frame.CreateNewRemote, frame.InstallCost,
                frame.CandidateReplacementCardIds, frame.RequiredReplacementCardIds, frame.SelectedReplacementCardIds,
                finalizeSelection: false, costQuote: frame.CostQuote, hostCardId: hostId)).ToList();
        }
        var materialized = new List<InstallCardParameters>(frame.CandidateReplacementCardIds.Count + 1);
        foreach (CardInstanceId candidate in frame.CandidateReplacementCardIds
                     .Where(cardId => !frame.SelectedReplacementCardIds.Contains(cardId)))
        {
            IReadOnlyList<CardInstanceId> selected = frame.SelectedReplacementCardIds.Append(candidate).ToArray();
            (int effectiveCost, CostQuote? quote) = InstallFrameCost(working, frame, plan, selected);
            materialized.Add(new InstallCardParameters(
                frame.CardId,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                effectiveCost,
                frame.CandidateReplacementCardIds,
                frame.RequiredReplacementCardIds,
                selected,
                finalizeSelection: false,
                costQuote: quote, hostCardId: frame.HostCardId));
        }

        PlayerState player = GetPlayer(working, frame.Owner);
        (int selectedCost, CostQuote? selectedQuote) = InstallFrameCost(working, frame, plan, frame.SelectedReplacementCardIds);
        if (InstallSelectionSatisfiesRequirements(working, frame, plan, frame.SelectedReplacementCardIds)
            && (IsClickFreeInstall(frame.InitiationKind) || player.Clicks >= 1)
            // The same affordability as planning and commit: the wallet and declared hosted credits (Open Market).
            && player.Credits + DeclaredHostedCredits(working, player.Side, BehaviorHostedCreditUseKind.InstallResource,
                GetCard(working, frame.CardId)) >= selectedCost)
        {
            materialized.Add(new InstallCardParameters(
                frame.CardId,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                selectedCost,
                frame.CandidateReplacementCardIds,
                frame.RequiredReplacementCardIds,
                frame.SelectedReplacementCardIds,
                finalizeSelection: true,
                costQuote: selectedQuote, hostCardId: frame.HostCardId));
        }

        return materialized;
    }

    private static void ConsumeTurnInstallAction(
        WorkingState working,
        InstallCardParameters parameters)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame turnFrame
            || turnFrame.Stage != TurnFrameStage.AwaitingAction
            || working.Turn.ActiveSide != turnFrame.ActiveSide)
        {
            throw new ResolutionFailureException("Installs require the active turn action frame.");
        }

        PlayerSide side = turnFrame.ActiveSide;
        PlayerState player = GetPlayer(working, side);
        CardInstanceState card = working.Cards.SingleOrDefault(candidate => candidate.Id == parameters.CardId)
            ?? throw new ResolutionFailureException($"Card '{parameters.CardId}' does not exist.");
        ZoneKind sourceKind = side == PlayerSide.Corp
            ? ZoneKind.CorpHand
            : IsGripLikeHosted(working, card) ? ZoneKind.RunnerHostedFaceUp : ZoneKind.RunnerGrip;
        ZoneState source = GetZone(working, sourceKind);
        if (card.OwnerSide != side || card.ZoneId != source.Id || !source.CardIds.Contains(card.Id))
        {
            throw new ResolutionFailureException($"Card '{parameters.CardId}' is not in the active player's install source.");
        }

        InstallPlan plan = BuildInstallPlan(
            working,
            side,
            parameters.CardId,
            parameters.DestinationKind,
            parameters.ServerId,
            parameters.CreateNewRemote,
            hostCardId: parameters.HostCardId)
            ?? throw new ResolutionFailureException("The requested install is not currently legal.");

        if (!parameters.CandidateReplacementCardIds.SequenceEqual(plan.CandidateReplacementCardIds)
            || !parameters.RequiredReplacementCardIds.SequenceEqual(plan.RequiredReplacementCardIds)
            || parameters.SelectedReplacementCardIds.Count != 0
            || parameters.FinalizeSelection != (plan.CandidateReplacementCardIds.Count == 0)
            || parameters.CreditCost != plan.InstallCost
            || !CostQuotesEqual(parameters.CostQuote, plan.InstallCostQuote))
        {
            throw new ResolutionFailureException("The install action does not match the materialized install plan.");
        }

        // Corp installs into an occupied root use the same serialized,
        // incremental replacement stream as Runner installs.  The first
        // action only freezes the source, destination, candidates, and
        // required set; movement, trash, payment, and click spending remain
        // at the final commit boundary.
        if (side == PlayerSide.Corp)
        {
            if (plan.CandidateReplacementCardIds.Count > 0)
            {
                if (parameters.SelectedReplacementCardIds.Count != 0
                    || parameters.FinalizeSelection)
                {
                    throw new ResolutionFailureException("The occupied Corp root install must begin with an empty incremental selection.");
                }

                var corpFrame = new InstallFrame(
                    player.Id,
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
                    costQuote: plan.InstallCostQuote, hostCardId: parameters.HostCardId);
                working.ResolutionStack.Add(corpFrame);
                return;
            }

            if (parameters.SelectedReplacementCardIds.Count != 0
                || !parameters.FinalizeSelection)
            {
                throw new ResolutionFailureException("Corp root installs must finalize without replacement selection.");
            }

            CommitInstall(working, turnFrame, plan);
            return;
        }

        var frame = new InstallFrame(
            player.Id,
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
            costQuote: plan.InstallCostQuote, hostCardId: parameters.HostCardId);

        if (plan.CandidateReplacementCardIds.Count == 0)
        {
            CommitInstall(working, frame, plan);
            return;
        }

        working.ResolutionStack.Add(frame);
    }

    private static void ConsumeInstallChoiceAction(
        WorkingState working,
        InstallCardParameters parameters)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not InstallFrame frame)
        {
            throw new ResolutionFailureException("The install choice does not match an active install frame.");
        }

        InstallPlan plan = ValidateInstallFrame(working, frame, requireFinalLegality: false);
        if (NeedsInstallHostChoice(working, frame))
        {
            if (!MaterializeInstallChoiceParameters(working, frame, plan).Any(candidate =>
                EngineJson.Serialize(candidate) == EngineJson.Serialize(parameters)))
            {
                throw new ResolutionFailureException("The selected install host does not match an issued host choice.");
            }
            working.ResolutionStack[^1] = frame.WithHost(parameters.HostCardId!.Value);
            return;
        }
        if (parameters.HostCardId != frame.HostCardId
            || parameters.CardId != frame.CardId
            || parameters.DestinationKind != frame.DestinationKind
            || parameters.ServerId != frame.ServerId
            || parameters.CreateNewRemote != frame.CreateNewRemote
            || !parameters.CandidateReplacementCardIds.SequenceEqual(frame.CandidateReplacementCardIds)
            || !parameters.RequiredReplacementCardIds.SequenceEqual(frame.RequiredReplacementCardIds))
        {
            throw new ResolutionFailureException("The install choice does not match the frozen install frame.");
        }

        if (parameters.FinalizeSelection)
        {
            (int expectedFinalCost, CostQuote? expectedFinalQuote) =
                InstallFrameCost(working, frame, plan, frame.SelectedReplacementCardIds);
            if (!parameters.SelectedReplacementCardIds.SequenceEqual(frame.SelectedReplacementCardIds)
                || parameters.CreditCost != expectedFinalCost
                || !CostQuotesEqual(parameters.CostQuote, expectedFinalQuote)
                || !InstallSelectionSatisfiesRequirements(working, frame, plan, frame.SelectedReplacementCardIds))
            {
                throw new ResolutionFailureException("The final install selection is stale or incomplete.");
            }

            ValidateInstallFrame(working, frame, requireFinalLegality: true);
            CommitInstall(working, frame, plan);
            return;
        }

        if (parameters.SelectedReplacementCardIds.Count != frame.SelectedReplacementCardIds.Count + 1
            || !parameters.SelectedReplacementCardIds
                .Take(frame.SelectedReplacementCardIds.Count)
                .SequenceEqual(frame.SelectedReplacementCardIds))
        {
            throw new ResolutionFailureException("An incremental install selection must append one candidate.");
        }

        CardInstanceId selected = parameters.SelectedReplacementCardIds[^1];
        if (!frame.CandidateReplacementCardIds.Contains(selected)
            || frame.SelectedReplacementCardIds.Contains(selected))
        {
            throw new ResolutionFailureException("The incremental install selection is not a remaining frozen candidate.");
        }

        (int expectedCost, CostQuote? expectedQuote) =
            InstallFrameCost(working, frame, plan, parameters.SelectedReplacementCardIds);
        if (parameters.CreditCost != expectedCost
            || !CostQuotesEqual(parameters.CostQuote, expectedQuote))
        {
            throw new ResolutionFailureException("The incremental install cost is stale.");
        }

        working.ResolutionStack[^1] = frame.Select(selected, expectedCost, expectedQuote);
    }

    private static void CommitInstall(
        WorkingState working,
        InstallFrame frame,
        InstallPlan plan)
    {
        ValidateInstallFrame(working, frame, requireFinalLegality: true);
        RunIceCursorSnapshot? runIceSnapshot = SnapshotNormalRunIceCursor(working, frame);
        PlayerState player = GetPlayer(working, frame.Owner);
        if (frame.InitiationKind == InstallInitiationKind.BasicAction && player.Clicks < 1)
        {
            throw new ResolutionFailureException("Installing a card requires one click.");
        }

        int baseCost = frame.InitiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts
            ? 0
            : CalculateInstallCost(
                working,
                plan.Spec,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                frame.SelectedReplacementCardIds);
        // ValidateInstallFrame above has already checked that the frozen
        // quote is the current quote for this card and selection.  Use its
        // effective amount for the actual payment so the transaction cannot
        // silently fall back to the printed/base cost.
        int cost = frame.InitiationKind == InstallInitiationKind.BehaviorZoneInstall
            ? InstallFrameCost(working, frame, plan, frame.SelectedReplacementCardIds).Cost
            : frame.CostQuote?.EffectiveCost ?? baseCost;
        if (player.Credits + DeclaredHostedCredits(working, player.Side, BehaviorHostedCreditUseKind.InstallResource, GetCard(working, frame.CardId)) < cost)
        {
            throw new ResolutionFailureException($"Installing '{frame.CardId}' requires {cost} credits.");
        }

        // Trash selected replacements before the new card moves. All
        // selection and affordability checks above have already passed, so
        // this loop cannot partially pay or mutate on a rejected action.
        foreach (CardInstanceId replacementId in frame.SelectedReplacementCardIds)
        {
            CardInstanceState replacement = GetCard(working, replacementId);
            ZoneState replacementSource = ZoneForInstallCard(working, replacement);
            ZoneState discard = GetZone(
                working,
                player.Side == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap);
            // The atomic install publishes one final memory adjustment after
            // registering the incoming program; uninstall still cleans state.
            TrashCard(working, replacement, replacementSource, discard, emitMemoryAdjustment: false);
        }

        // MoveCard replaces both source and destination zone records.  The
        // destination captured before the trash loop would therefore retain
        // the pre-trash card list and could reintroduce selected cards when
        // the incoming card is moved from Grip.  Resolve the current zone
        // after all replacements have moved.
        ZoneState destination = frame.CreateNewRemote
            ? CreateNewRemoteInstallDestination(working, frame.DestinationKind)
            : ResolveExistingInstallDestination(
                working,
                frame.DestinationKind,
                frame.ServerId);

        if (frame.CardClass == RuntimeCardClass.Program)
        {
            EnsureProgramInstallRulesState(working);
            RulesState rules = RequireRulesState(working);
            SetRulesState(
                working,
                programs: rules.Programs
                    .Where(program => !frame.SelectedReplacementCardIds.Contains(program.CardId))
                    .Append(new ProgramMemoryState(frame.CardId, player.Id, plan.Spec.MemoryUnits))
                    .ToArray());
            RecalculateUsedMemory(working);
        }

        PayCreditsWithDeclaredHosted(working, player.Id, BehaviorHostedCreditUseKind.InstallResource, GetCard(working, frame.CardId), cost);
        if (plan.Source.Kind is ZoneKind.CorpScore or ZoneKind.RunnerScore && plan.Spec.AgendaPoints > 0)
        {
            // EL-INSTALL-FROM-SCORE-AREA: the agenda's points leave that score area with it, at their value there
            // (VP-AGENDA-POINTS-MODIFIER: Let Them Dream is worth one less in the Runner's).
            PlayerSide scoreSide = plan.Source.Kind == ZoneKind.CorpScore ? PlayerSide.Corp : PlayerSide.Runner;
            int leaving = working.BehaviorCatalog.TryGetAgenda(GetCard(working, frame.CardId).DefinitionId, out AgendaBehaviorSpec? leavingSpec)
                ? leavingSpec.PointsIn(scoreSide)
                : plan.Spec.AgendaPoints;
            AddAgendaPoints(working, scoreSide, -leaving);
        }
        if (frame.InitiationKind == InstallInitiationKind.BasicAction)
        {
            SpendClick(working, player.Id);
        }

        CardInstanceState card = GetCard(working, frame.CardId);
        ZoneState source = ZoneForInstallCard(working, card);
        MoveCard(working, card, source, destination);
        if (frame.InitiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
        {
            destination = InsertBeforeInwardAnchor(working, destination, frame.CardId, frame.InwardAnchorIceId);
            RepairInwardRunCursor(working, frame, destination);
        }
        CardInstanceState installed = GetCard(working, card.Id);
        ReplaceCard(working, new CardInstanceState(
            installed.Id,
            installed.DefinitionId,
            installed.ZoneId,
            installed.OwnerSide,
            frame.CardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware or RuntimeCardClass.Resource
                || InstallsFaceUp(working, installed.DefinitionId),
            false,
            installed.AdvancementCounters, frame.HostCardId));
        MarkInstalledThisTurn(working, frame.CardId);
        if (frame.CardClass == RuntimeCardClass.Program
            && player.Side == PlayerSide.Runner)
        {
            RecordTurnProgramInstall(working, frame.CardId);
            RecordTurnFirstProgramInstallUses(working, frame.CardId, frame.CostQuote);
        }

        if (frame.CardClass == RuntimeCardClass.Program)
        {
            PlayerRulesState memory = GetPlayerRules(RequireRulesState(working), player.Id);
            Emit(working, EngineEventKinds.MemoryAdjusted, new MemoryAdjustedEventPayload(
                player.Id,
                memory.BaseMemory,
                memory.UsedMemory));
        }

        Emit(working, EngineEventKinds.CardInstalled, new CardInstalledEventPayload(
            player.Side,
            frame.CardId,
            source.Id,
            destination.Id,
            cost));
        RecordFreeInstall(working, frame.CardId, cost);

        if (working.ResolutionStack.Count > 0 && working.ResolutionStack[^1] is InstallFrame)
        {
            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        }

        if (frame.InitiationKind == InstallInitiationKind.BehaviorEffect)
        {
            if (working.ResolutionStack.Count == 0
                || working.ResolutionStack[^1] is not BehaviorResolutionFrame behaviorParent
                || !string.Equals(
                    frame.ParentFrameIdentity,
                    BehaviorFrameIdentity.Compute(behaviorParent),
                    StringComparison.Ordinal))
            {
                throw new ResolutionFailureException(
                    "The completed behavior install has no matching behavior parent frame.");
            }

            IReadOnlyList<BehaviorBinding> bindings = behaviorParent.Bindings
                .Select(binding => new BehaviorBinding(
                    binding.Key,
                    binding.CardIds.Where(cardId => cardId != frame.CardId).ToArray()))
                .Where(binding => binding.CardIds.Count > 0
                    && !string.Equals(binding.Key, "chosen", StringComparison.Ordinal))
                .ToArray();
            AdvanceBehaviorCursor(
                working,
                behaviorParent.With(bindings: bindings));
            QueueCardInstalledOccurrence(
                working,
                player.Id,
                frame.CardId,
                destination.ServerId);
            return;
        }

        if (frame.InitiationKind == InstallInitiationKind.BehaviorZoneInstall)
        {
            CompleteBehaviorZoneInstall(working, frame, player.Id, destination.ServerId);
            return;
        }

        if (frame.InitiationKind is InstallInitiationKind.IceSubroutineNormal
            or InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
        {
            if (working.ResolutionStack.Count == 0
                || working.ResolutionStack[^1] is not IceSubroutineFrame parentFrame
                || !string.Equals(
                    frame.ParentFrameIdentity,
                    parentFrame.FrameIdentity,
                    StringComparison.Ordinal)
                || parentFrame.Stage != IceSubroutineFrameStage.AwaitingInstall)
            {
                throw new ResolutionFailureException(
                    "The completed ICE subroutine install has no matching parent frame.");
            }

            bool encounteredIceRemoved = false;
            if (runIceSnapshot is not null)
            {
                encounteredIceRemoved = !RepairNormalRunIceCursor(
                    working,
                    runIceSnapshot,
                    destination);
            }

            RunState run = working.RunState
                ?? throw new ResolutionFailureException("The completed ICE subroutine install requires a live run.");
            if (run.Id != parentFrame.RunId
                || run.Encounter is not { } encounter
                || encounter.Id != parentFrame.EncounterId
                || encounter.IceId != parentFrame.IceId
                || encounter.Subroutines.SingleOrDefault(candidate => candidate.Id == parentFrame.SubroutineId) is not { } subroutine
                || subroutine.Ordinal != parentFrame.Ordinal
                || subroutine.Broken
                || subroutine.Resolved)
            {
                throw new ResolutionFailureException(
                    "The completed ICE subroutine install no longer matches its encounter.");
            }

            RunSubroutineState[] resolved = encounter.Subroutines
                .Select(candidate => candidate.Id == parentFrame.SubroutineId
                    ? new RunSubroutineState(candidate.Version, candidate.Id, candidate.Ordinal, candidate.Broken, true)
                : candidate)
                .ToArray();
            CardInstanceState encounteredIce = GetCard(working, encounter.IceId);
            run = RecordResolvedUnbrokenSubroutine(
                run,
                encounteredIce,
                encounter.Id,
                subroutine);
            if (encounteredIceRemoved)
            {
                // CR 8.5.10: if the approached/encountered ICE is uninstalled
                // during its paid install window, finish the current window
                // and pass that position. The numeric cursor is retained as a
                // conceptual boundary: it counts retained ICE inward of the
                // removed source, and MoveInward decrements it to the next
                // actual protector. The newly installed ICE was appended
                // outermost and is therefore never re-encountered.
                RunEncounterState completedEncounter = new(
                    encounter.Version,
                    encounter.Id,
                    encounter.IceId,
                    RunEncounterStatus.Complete,
                    resolved);
                RunIceCursorSnapshot cursorSnapshot = runIceSnapshot
                    ?? throw new ResolutionFailureException("The removed encountered ICE has no cursor snapshot.");
                RunState progressed = run.WithLingeringEffects(
                        run.LingeringEffects
                            .Where(effect => effect.Expiry != RunLingeringEffectExpiry.Encounter)
                            .ToArray())
                    .WithTiming(
                        RunTimingStep.MovementPaidWindow,
                        cursorSnapshot.IceIds
                            .Take(cursorSnapshot.CurrentIceIndex)
                            .Count(destination.CardIds.Contains),
                        null,
                        run.Successful,
                        run.Unsuccessful,
                        run.EndReason,
                        CreatePriority(RunPriorityWindowKind.MovementPaid),
                        completedEncounter,
                        run.AccessState);
                Emit(working, EngineEventKinds.EncounterCompleted, new EncounterCompletedEventPayload(
                    run.Id,
                    encounter.Id));
                working.RunState = progressed;
                working.ResolutionStack[^1] = new RunTimingFrame(run.Id, RunTimingStep.MovementPaidWindow);
            }
            else
            {
                working.RunState = run.WithEncounter(new RunEncounterState(
                    encounter.Version,
                    encounter.Id,
                    encounter.IceId,
                    encounter.Status,
                    resolved));
                working.ResolutionStack[^1] = new RunTimingFrame(run.Id, RunTimingStep.Encounter);
            }
            QueueCardInstalledOccurrence(
                working,
                player.Id,
                frame.CardId,
                destination.ServerId);
            if (encounteredIceRemoved)
            {
                // VP-ENCOUNTER-ENDED (PR #154's third review): trashing the encountered ICE ended its encounter.
                QueueRunIceOccurrence(working, working.RunState!, OccurrenceKind.EncounterEnded, encounter.IceId);
            }

            return;
        }

        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame parent
            || parent.Stage != TurnFrameStage.AwaitingAction)
        {
            throw new ResolutionFailureException("The completed install has no matching turn action frame.");
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
        QueueCardInstalledOccurrence(
            working,
            player.Id,
            frame.CardId,
            destination.ServerId);
    }

    private static ZoneState InsertBeforeInwardAnchor(
        WorkingState working,
        ZoneState destination,
        CardInstanceId cardId,
        CardInstanceId? anchorId)
    {
        if (destination.Kind != ZoneKind.ServerIce
            || destination.ServerId is null
            || anchorId is not { } anchor)
        {
            throw new ResolutionFailureException("An inward install requires an ICE-zone anchor.");
        }

        List<CardInstanceId> ordered = destination.CardIds.ToList();
        ordered.Remove(cardId);
        int anchorIndex = ordered.IndexOf(anchor);
        if (anchorIndex < 0)
        {
            throw new ResolutionFailureException("The inward install anchor is no longer in the destination ICE zone.");
        }

        ordered.Insert(anchorIndex, cardId);
        ZoneState reordered = new ZoneState(
            destination.Id,
            destination.Kind,
            destination.OwnerSide,
            destination.ServerId,
            ordered);
        ReplaceZone(working, reordered);
        return reordered;
    }

    private static RunIceCursorSnapshot? SnapshotNormalRunIceCursor(
        WorkingState working,
        InstallFrame frame)
    {
        if (frame.InitiationKind != InstallInitiationKind.IceSubroutineNormal
            || frame.DestinationKind != InstallDestinationKind.ServerIce
            || frame.ServerId is not { } serverId
            || working.RunState is not { } run
            || run.Step != RunTimingStep.Encounter
            || run.AttackedServer != serverId
            || run.CurrentIceId is not { } currentIceId
            || run.Encounter is not { Status: RunEncounterStatus.Initialized } encounter
            || encounter.IceId != currentIceId)
        {
            return null;
        }

        ZoneState iceZone = ResolveExistingInstallDestination(
            working,
            InstallDestinationKind.ServerIce,
            serverId);
        if (run.CurrentIceIndex < 0
            || run.CurrentIceIndex >= iceZone.CardIds.Count
            || iceZone.CardIds[run.CurrentIceIndex] != currentIceId)
        {
            throw new ResolutionFailureException("The normal ICE install cannot snapshot the live run cursor.");
        }

        return new RunIceCursorSnapshot(
            run.Id,
            encounter.Id,
            serverId,
            currentIceId,
            run.CurrentIceIndex,
            iceZone.CardIds.ToArray());
    }

    private static bool RepairNormalRunIceCursor(
        WorkingState working,
        RunIceCursorSnapshot snapshot,
        ZoneState destination)
    {
        if (destination.Kind != ZoneKind.ServerIce
            || destination.ServerId != snapshot.ServerId)
        {
            throw new ResolutionFailureException("The normal ICE install changed the attacked server topology.");
        }

        RunState run = working.RunState
            ?? throw new ResolutionFailureException("The normal ICE install requires a live run cursor.");
        if (run.Id != snapshot.RunId
            || run.AttackedServer != snapshot.ServerId
            || run.Encounter is not { } encounter
            || encounter.Id != snapshot.EncounterId)
        {
            throw new ResolutionFailureException("The normal ICE install changed the live encounter identity.");
        }

        if (destination.CardIds.Contains(snapshot.CurrentIceId))
        {
            int repairedIndex = destination.CardIds.ToList().IndexOf(snapshot.CurrentIceId);
            working.RunState = run.WithTiming(
                run.Step,
                repairedIndex,
                snapshot.CurrentIceId,
                run.Successful,
                run.Unsuccessful,
                run.EndReason,
                run.Priority,
                run.Encounter,
                run.AccessState);
            return true;
        }

        return false;
    }

    private static void RepairInwardRunCursor(
        WorkingState working,
        InstallFrame frame,
        ZoneState destination)
    {
        RunState run = working.RunState
            ?? throw new ResolutionFailureException("An inward ICE install requires a live run.");
        CardInstanceId anchor = frame.InwardAnchorIceId
            ?? throw new ResolutionFailureException("An inward ICE install requires an anchor.");
        if (run.AttackedServer != destination.ServerId
            || run.CurrentIceId != anchor
            || run.Encounter?.IceId != anchor)
        {
            throw new ResolutionFailureException("The inward install anchor does not match the current encounter ICE.");
        }

        int anchorIndex = destination.CardIds.ToList().IndexOf(anchor);
        if (anchorIndex < 0 || run.CurrentIceIndex + 1 != anchorIndex)
        {
            throw new ResolutionFailureException("The inward install could not repair the live ICE cursor.");
        }

        working.RunState = run.WithTiming(
            run.Step,
            anchorIndex,
            anchor,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            run.Priority,
            run.Encounter,
            run.AccessState);
    }

    private static void CommitInstall(
        WorkingState working,
        TurnFrame parent,
        InstallPlan plan)
    {
        bool isCorpRootInstall = plan.DestinationKind == InstallDestinationKind.ServerRoot
            && plan.Spec.CardClass is RuntimeCardClass.Agenda or RuntimeCardClass.Asset or RuntimeCardClass.Upgrade;
        bool isCorpIceInstall = plan.DestinationKind == InstallDestinationKind.ServerIce
            && plan.Spec.CardClass == RuntimeCardClass.Ice;
        if (plan.Spec.Side != PlayerSide.Corp
            || (!isCorpRootInstall && !isCorpIceInstall)
            || plan.CandidateReplacementCardIds.Count != 0
            || plan.RequiredReplacementCardIds.Count != 0)
        {
            throw new ResolutionFailureException("The Corp install is outside the bounded B1b root/ICE-install contract.");
        }

        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        if (corp.Clicks < 1 || corp.Credits < plan.InstallCost)
        {
            throw new ResolutionFailureException("The Corp install is no longer affordable.");
        }

        CardInstanceState card = GetCard(working, plan.Card.Id);
        ZoneState source = ZoneForInstallCard(working, card);
        if (source.Id != plan.Source.Id)
        {
            throw new ResolutionFailureException("The Corp install source is stale.");
        }

        // A new remote is allocated only after every materialized action and
        // final legality check has passed.  Existing destinations are resolved
        // by their frozen server identity and never allocate topology.
        ZoneState destination = plan.CreateNewRemote
            ? CreateNewRemoteInstallDestination(working, plan.DestinationKind)
            : ResolveExistingInstallDestination(working, plan.DestinationKind, plan.ServerId);

        if (!plan.CreateNewRemote && destination.CardIds.Count != 0)
        {
            throw new ResolutionFailureException("A direct Corp install requires an empty destination zone.");
        }

        ApplyCreditDeltaIfNonZero(working, corp.Id, -plan.InstallCost);
        SpendClick(working, corp.Id);
        MoveCard(working, card, source, destination);
        CardInstanceState installed = GetCard(working, card.Id);
        ReplaceCard(working, new CardInstanceState(
            installed.Id,
            installed.DefinitionId,
            installed.ZoneId,
            installed.OwnerSide,
            faceUp: InstallsFaceUp(working, installed.DefinitionId),
            rezzed: false,
            installed.AdvancementCounters));
        MarkInstalledThisTurn(working, plan.Card.Id);
        Emit(working, EngineEventKinds.CardInstalled, new CardInstalledEventPayload(
            PlayerSide.Corp,
            plan.Card.Id,
            source.Id,
            destination.Id,
            plan.InstallCost));
        RecordFreeInstall(working, plan.Card.Id, plan.InstallCost);

        ResumeTurnAfterInstall(working, parent);
        QueueCardInstalledOccurrence(
            working,
            corp.Id,
            plan.Card.Id,
            destination.ServerId);
    }

    /// <summary>
    /// Opens the normal occurrence/checkpoint pipeline after an install has
    /// committed all of its own mutations and its parent continuation has
    /// been advanced.  Keeping the marker as a regular effect preserves
    /// trigger ordering and makes optional choices serializable without
    /// repeating payment or install work.
    /// </summary>
    private static void QueueCardInstalledOccurrence(
        WorkingState working,
        PlayerId controller,
        CardInstanceId cardId,
        ServerId? serverId)
    {
        working.ResolutionStack.Add(new EffectResolutionFrame(
            controller,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.CardInstalled,
                controller,
                cardId,
                serverId))));
    }

    private static void ApplyCreditDeltaIfNonZero(WorkingState working, PlayerId playerId, int delta)
    {
        if (delta != 0)
        {
            ApplyCreditDelta(working, playerId, delta);
        }
    }

    private static void ResumeTurnAfterInstall(WorkingState working, TurnFrame parent)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame activeParent
            || activeParent.Stage != TurnFrameStage.AwaitingAction
            || activeParent.ActiveSide != parent.ActiveSide
            || activeParent.TurnNumber != parent.TurnNumber)
        {
            throw new ResolutionFailureException("The completed install has no matching turn action frame.");
        }

        working.ResolutionStack[^1] = new TurnFrame(
            activeParent.Version,
            activeParent.BehaviorId,
            activeParent.ActiveSide,
            activeParent.TurnNumber,
            TurnFrameStage.ActionPriority,
            TurnWindow(activeParent.ActiveSide),
            activeParent.ActiveSide,
            forcedActionEnd: activeParent.ForcedActionEnd);
    }

    /// <summary>VP-HOST-RESOURCES: a resource installed onto a host that accepts it costs the host's discount less.</summary>
    private static int HostedInstallDiscount(WorkingState working, CardInstanceState card, CardInstanceId? hostCardId, int cost) =>
        hostCardId is { } host && ResourceHost(working, host, card) is { } hosting
            ? Math.Max(0, cost - hosting.InstallDiscount)
            : cost;

    private static bool IsClickFreeInstall(InstallInitiationKind initiationKind) =>
        initiationKind is InstallInitiationKind.BehaviorEffect
            or InstallInitiationKind.IceSubroutineNormal
            or InstallInitiationKind.IceSubroutineInwardIgnoreCosts
            or InstallInitiationKind.BehaviorZoneInstall;

    private static CostQuote? InstallCostQuote(
        WorkingState working,
        CardInstanceState card,
        int baseCost,
        InstallInitiationKind initiationKind) =>
        card.OwnerSide == PlayerSide.Runner
            && initiationKind is not InstallInitiationKind.IceSubroutineNormal
                and not InstallInitiationKind.IceSubroutineInwardIgnoreCosts
            ? CostModifierProjection.RunnerInstall(working, card, baseCost)
            : null;

    private static int CalculateInstallBaseCost(
        WorkingState working,
        InstallFrame frame,
        InstallPlan plan,
        IReadOnlyList<CardInstanceId> selected) =>
        frame.InitiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts
            ? 0
            : CalculateInstallCost(
                working,
                plan.Spec,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                selected);

    /// <summary>
    /// The install frame's effective credit cost and quote for a replacement
    /// selection: the base cost, any self-install modifiers, then (for a
    /// behavior zone install) the declaring frame's cost rule and discount.
    /// </summary>
    private static (int Cost, CostQuote? Quote) InstallFrameCost(
        WorkingState working,
        InstallFrame frame,
        InstallPlan plan,
        IReadOnlyList<CardInstanceId> selected)
    {
        int baseCost = CalculateInstallBaseCost(working, frame, plan, selected);
        BehaviorZoneInstallRule? rule = frame.InitiationKind == InstallInitiationKind.BehaviorZoneInstall
            ? ZoneInstallRuleFor(working, frame)
            : null;
        if (rule is not null)
        {
            // As in BuildInstallPlan: the zone discount comes before the quoted modifiers.
            baseCost = rule.Apply(baseCost);
        }

        baseCost = HostedInstallDiscount(working, plan.Card, frame.HostCardId, baseCost);

        CostQuote? quote = rule?.CostRule == BehaviorInstallCostRule.IgnoreAllCosts
            ? null
            : InstallCostQuote(working, plan.Card, baseCost, frame.InitiationKind);
        return (quote?.EffectiveCost ?? baseCost, quote);
    }

    private static InstallPlan? BuildInstallPlan(
        WorkingState working,
        PlayerSide side,
        CardInstanceId cardId,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote,
        InstallInitiationKind initiationKind = InstallInitiationKind.BasicAction,
        CardInstanceId? inwardAnchorIceId = null,
        CardInstanceId? hostCardId = null,
        BehaviorZoneInstallRule? zoneRule = null)
    {
        if (initiationKind == InstallInitiationKind.BehaviorZoneInstall)
        {
            return zoneRule is null || !IsLegalInstallHost(working, cardId, hostCardId)
                ? null
                : BuildInstallPlanCore(working, side, cardId, destinationKind, serverId, createNewRemote,
                    initiationKind, inwardAnchorIceId, zoneRule);
        }

        bool deferredHost = initiationKind == InstallInitiationKind.BehaviorEffect && hostCardId is null
            && HostingSpec(working, GetCard(working, cardId)) is not null
            && InstallHosts(working, GetCard(working, cardId)).Any();
        if (!deferredHost && !IsLegalInstallHost(working, cardId, hostCardId))
        {
            return null;
        }

        if (initiationKind is InstallInitiationKind.IceSubroutineNormal
            or InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
        {
            return BuildCorpSubroutineInstallPlan(
                working,
                side,
                cardId,
                destinationKind,
                serverId,
                createNewRemote,
                initiationKind,
                inwardAnchorIceId);
        }

        return BuildInstallPlanCore(
            working,
            side,
            cardId,
            destinationKind,
            serverId,
            createNewRemote,
            initiationKind,
            inwardAnchorIceId,
            hostCardId: hostCardId);
    }

    private static InstallPlan? BuildInstallPlanCore(
        WorkingState working,
        PlayerSide side,
        CardInstanceId cardId,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote,
        InstallInitiationKind initiationKind,
        CardInstanceId? inwardAnchorIceId,
        BehaviorZoneInstallRule? zoneRule = null,
        CardInstanceId? hostCardId = null)
    {
        if (side is not (PlayerSide.Runner or PlayerSide.Corp))
        {
            return null;
        }

        if (!Enum.IsDefined(initiationKind))
        {
            return null;
        }

        if (!IsAllowedInstallServer(working, cardId, destinationKind, serverId, createNewRemote))
        {
            return null;
        }

        if (initiationKind == InstallInitiationKind.BehaviorEffect)
        {
            return BuildBehaviorInstallPlan(
                working,
                side,
                cardId,
                destinationKind,
                serverId,
                createNewRemote);
        }

        IRuntimeCardCatalog? catalog = working.RuntimeCardCatalog;
        if (catalog is null)
        {
            return null;
        }

        CardInstanceState card = GetCard(working, cardId);
        if (!catalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec))
        {
            return null;
        }

        bool isCorpSubroutineInstall = initiationKind is InstallInitiationKind.IceSubroutineNormal
            or InstallInitiationKind.IceSubroutineInwardIgnoreCosts;
        bool inwardInstall = initiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts;
        ZoneKind[] sourceKinds = initiationKind == InstallInitiationKind.BehaviorZoneInstall
            ? side == PlayerSide.Corp
                ? [ZoneKind.CorpHand, ZoneKind.CorpDiscard, ZoneKind.CorpDeck, ZoneKind.RunnerScore]
                : [ZoneKind.RunnerGrip, ZoneKind.RunnerHeap, ZoneKind.RunnerStack, ZoneKind.RunnerHostedFaceUp]
            : side == PlayerSide.Corp
                ? isCorpSubroutineInstall
                    ? [ZoneKind.CorpHand, ZoneKind.CorpDiscard]
                    : [ZoneKind.CorpHand]
                : IsGripLikeHosted(working, card)
                    ? [ZoneKind.RunnerHostedFaceUp]
                    : [ZoneKind.RunnerGrip];
        ZoneState? source = working.Zones.FirstOrDefault(
            zone => sourceKinds.Contains(zone.Kind) && zone.ServerId is null && zone.Id == card.ZoneId);
        if (source is null || card.OwnerSide != side || card.ZoneId != source.Id || spec.Side != side)
        {
            return null;
        }

        if (!IsBasicInstallClass(side, spec.CardClass))
        {
            return null;
        }

        if (!InstallDestinationExistsAndMatches(working, side, spec.CardClass, destinationKind, serverId, createNewRemote))
        {
            return null;
        }

        if (isCorpSubroutineInstall && side != PlayerSide.Corp)
        {
            return null;
        }

        if (inwardInstall
            && (spec.CardClass != RuntimeCardClass.Ice
                || destinationKind != InstallDestinationKind.ServerIce
                || serverId is null
                || createNewRemote
                || inwardAnchorIceId is null))
        {
            return null;
        }

        // A Runner Program install cannot safely infer the memory of an
        // unclassified card already in the Rig. Fail closed before issuing
        // the action regardless of whether RulesState is present; the
        // initialization path below only reconstructs runtime-known cards.
        if (side == PlayerSide.Runner
            && spec.CardClass == RuntimeCardClass.Program
            && !AllRuntimeCardsKnown(working, GetZone(working, ZoneKind.RunnerRig).CardIds))
        {
            return null;
        }

        // A unique or Console Runner non-Program must inspect every active
        // Rig card to prove the exact conflict set. If any such card lacks
        // authoritative runtime metadata, fail closed before issuing an
        // action; plain non-unique/non-Console Hardware and Resources do not
        // need that stronger gate.
        if (side == PlayerSide.Runner
            && spec.CardClass is not RuntimeCardClass.Program
            && (spec.Unique || spec.Console)
            && !AllActiveRunnerRigCardsKnown(working))
        {
            return null;
        }

        ZoneState? destination = TryResolveInstallDestination(
            working,
            destinationKind,
            serverId,
            createNewRemote);
        if (!createNewRemote
            && (destination is null
                || side == PlayerSide.Corp
                    && destination.Kind != (spec.CardClass == RuntimeCardClass.Ice
                        ? ZoneKind.ServerIce
                        : ZoneKind.ServerRoot)))
        {
            return null;
        }

        // An occupied Corp root is a bounded replacement transaction. Every
        // physical root card is a candidate, so unknown runtime metadata must
        // fail closed before the action or required set can be issued.
        if (side == PlayerSide.Corp
            && destination is not null
            && destination.CardIds.Count != 0
            && !AllRuntimeCardsKnown(working, destination.CardIds))
        {
            return null;
        }

        CardInstanceId[] candidates = inwardInstall
            ? []
            : InstallCandidates(working, spec, destinationKind, serverId, createNewRemote);
        // A card-effect install during a run (Mycoweb) trashes no ICE on the
        // attacked server, so the run's ICE cursor stays where it is.
        if (initiationKind == InstallInitiationKind.BehaviorZoneInstall
            && destinationKind == InstallDestinationKind.ServerIce
            && working.RunState is { } liveRun
            && serverId == liveRun.AttackedServer)
        {
            candidates = [];
        }
        if (side == PlayerSide.Corp
            && destinationKind == InstallDestinationKind.ServerIce
            && candidates.Any(candidateId => RuntimeClass(working, candidateId) != RuntimeCardClass.Ice))
        {
            // A physical ICE zone is authoritative for installation legality;
            // never infer a candidate's class from title/text when runtime
            // metadata is missing or says the card is not ICE.
            return null;
        }

        CardInstanceId[] required = inwardInstall
            ? []
            : InstallRequiredCandidates(working, spec, candidates, destinationKind, serverId, createNewRemote);
        if (required.Any(cardOption => !candidates.Contains(cardOption)))
        {
            return null;
        }

        // Class metadata is authoritative for every installed card whose
        // replacement legality matters. Unknown cards are never classified by
        // title/text and therefore fail closed for a root/rig selection.
        if (side == PlayerSide.Runner
            && destinationKind is (InstallDestinationKind.ServerRoot or InstallDestinationKind.ServerIce)
            && !AllRuntimeCardsKnown(working, candidates))
        {
            return null;
        }

        int baseInstallCost = inwardInstall
            ? 0
            : CalculateInstallCost(working, spec, destinationKind, serverId, createNewRemote, []);
        // A zone install's own discount ("install it, paying 1 less") lowers
        // the cost the other modifiers then quote, so the frozen quote stays
        // the cost actually paid.
        if (zoneRule is not null)
        {
            baseInstallCost = zoneRule.Apply(baseInstallCost);
        }

        baseInstallCost = HostedInstallDiscount(working, card, hostCardId, baseInstallCost);

        CostQuote? installCostQuote = zoneRule?.CostRule == BehaviorInstallCostRule.IgnoreAllCosts
            ? null
            : InstallCostQuote(
                working,
                card,
                baseInstallCost,
                initiationKind);
        int installCost = installCostQuote?.EffectiveCost ?? baseInstallCost;

        if (!isCorpSubroutineInstall
            && zoneRule is null
            && side == PlayerSide.Corp
            && spec.CardClass is not RuntimeCardClass.Ice
            && installCost != 0)
        {
            // This checkpoint is deliberately limited to the zero-credit
            // Corp root-install stream. Do not issue an action that the
            // bounded commit reducer would later reject. ICE uses its
            // dynamic protector count as its printed install cost.
            return null;
        }

        bool feasible = side == PlayerSide.Runner && spec.CardClass == RuntimeCardClass.Program
            ? CanInstallProgram(working, card, spec, candidates)
            : true;
        if (!feasible)
        {
            return null;
        }

        int minimumCost = destinationKind == InstallDestinationKind.ServerIce && !createNewRemote
            ? Math.Max(0, installCost - candidates.Length)
            : installCost;
        PlayerState player = GetPlayer(working, side);
        if ((!IsClickFreeInstall(initiationKind) && player.Clicks < 1)
            || player.Credits + DeclaredHostedCredits(working, side, BehaviorHostedCreditUseKind.InstallResource, card) < minimumCost)
        {
            return null;
        }

        CardDefinitionId[] candidateDefinitions = candidates
            .Select(candidateId => GetCard(working, candidateId).DefinitionId)
            .ToArray();

        return new InstallPlan(
            card,
            spec,
            source,
            destinationKind,
            serverId,
            createNewRemote,
            destination?.Id,
            installCost,
            installCostQuote,
            candidates,
            required,
            candidateDefinitions,
            inwardAnchorIceId);
    }

    /// <summary>
    /// Shared planner entry point for Corp choices initiated by a printed ICE
    /// subroutine. The ordinary planner remains the sole source of destination,
    /// replacement, metadata, and normal cost rules; this mode only changes
    /// source visibility, click semantics, and (for Brân) the inward anchor and
    /// ignore-cost rule.
    /// </summary>
    private static InstallPlan? BuildCorpSubroutineInstallPlan(
        WorkingState working,
        PlayerSide side,
        CardInstanceId cardId,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote,
        InstallInitiationKind initiationKind,
        CardInstanceId? inwardAnchorIceId) =>
        side == PlayerSide.Corp
            ? BuildInstallPlanCore(
                working,
                side,
                cardId,
                destinationKind,
                serverId,
                createNewRemote,
                initiationKind,
                inwardAnchorIceId)
            : null;

    /// <summary>
    /// Builds the same authoritative Runner install plan used by a basic
    /// install, with only its initiation/source semantics changed. A
    /// behavior-owned install is paid in credits but never consumes a click;
    /// all uniqueness, memory, replacement, and metadata checks remain shared
    /// with the normal install pipeline.
    /// </summary>
    private static InstallPlan? BuildBehaviorInstallPlan(
        WorkingState working,
        PlayerSide side,
        CardInstanceId cardId,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote)
    {
        if (side != PlayerSide.Runner || working.RuntimeCardCatalog is null)
        {
            return null;
        }

        CardInstanceState card = GetCard(working, cardId);
        if (!working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
            || spec.Side != PlayerSide.Runner
            || !IsBasicInstallClass(PlayerSide.Runner, spec.CardClass)
            || card.OwnerSide != PlayerSide.Runner)
        {
            return null;
        }

        ZoneState? source = working.Zones.FirstOrDefault(
            zone => zone.Kind is (ZoneKind.RunnerStack or ZoneKind.RunnerGrip)
                && zone.OwnerSide == PlayerSide.Runner
                && zone.ServerId is null
                && zone.Id == card.ZoneId);
        ZoneState? destination = working.Zones.FirstOrDefault(
            zone => zone.Kind == ZoneKind.RunnerRig
                && zone.OwnerSide == PlayerSide.Runner
                && zone.ServerId is null);
        if (source is null
            || destination is null
            || card.ZoneId != source.Id
            || !source.CardIds.Contains(card.Id)
            || destinationKind != InstallDestinationKind.RunnerRig
            || serverId is not null
            || createNewRemote
            || !AllRuntimeCardsKnown(working, destination.CardIds))
        {
            return null;
        }

        CardInstanceId[] candidates = InstallCandidates(
            working,
            spec,
            destinationKind,
            serverId,
            createNewRemote);
        if (!CanInstallProgram(working, card, spec, candidates))
        {
            return null;
        }

        CardInstanceId[] required = ActiveRunnerInstallConflictIds(working, spec);

        int baseInstallCost = CalculateInstallCost(
            working,
            spec,
            destinationKind,
            serverId,
            createNewRemote,
            []);
        CostQuote? installCostQuote = CostModifierProjection.RunnerInstall(working, card, baseInstallCost);
        int installCost = installCostQuote?.EffectiveCost ?? baseInstallCost;
        PlayerState player = GetPlayer(working, PlayerSide.Runner);
        if (player.Credits + DeclaredHostedCredits(working, PlayerSide.Runner, BehaviorHostedCreditUseKind.InstallResource, card) < installCost)
        {
            return null;
        }

        CardDefinitionId[] candidateDefinitions = candidates
            .Select(candidateId => GetCard(working, candidateId).DefinitionId)
            .ToArray();
        return new InstallPlan(
            card,
            spec,
            source,
            destinationKind,
            serverId,
            createNewRemote,
            destination.Id,
            installCost,
            installCostQuote,
            candidates,
            required,
            candidateDefinitions,
            null);
    }

    private static void EnsureProgramInstallRulesState(WorkingState working)
    {
        if (working.RulesState is not null)
        {
            return;
        }

        IRuntimeCardCatalog catalog = working.RuntimeCardCatalog
            ?? throw new ResolutionFailureException(
                "Runner program installs require an injected runtime card catalog to initialize memory state.");
        RulesState defaults = RulesState.CreateDefault(working.Players);
        ZoneState rig = GetZone(working, ZoneKind.RunnerRig);
        if (!AllRuntimeCardsKnown(working, rig.CardIds))
        {
            throw new ResolutionFailureException(
                "Runner program memory cannot be initialized while a Rig card lacks runtime metadata.");
        }

        ProgramMemoryState[] programs = working.Cards
            .Where(card => card.OwnerSide == PlayerSide.Runner && card.ZoneId == rig.Id)
            .Select(card => catalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                && spec.CardClass == RuntimeCardClass.Program
                    ? new ProgramMemoryState(
                        card.Id,
                        working.Players.Single(player => player.Side == PlayerSide.Runner).Id,
                        spec.MemoryUnits)
                    : null)
            .Where(static program => program is not null)
            .Cast<ProgramMemoryState>()
            .OrderBy(program => program.CardId.Value, StringComparer.Ordinal)
            .ToArray();
        PlayerRulesState[] players = defaults.Players
            .Select(player => new PlayerRulesState(
                player.PlayerId,
                player.BaseHandSize,
                player.CoreDamage,
                player.Tags,
                player.BaseMemory,
                programs.Where(program => program.Owner == player.PlayerId).Sum(program => program.MemoryCost)))
            .ToArray();
        working.RulesState = new RulesState(
            defaults.Version,
            players,
            programs,
            defaults.Counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts,
            defaults.BadPublicity,
            defaults.ScoredAsAgendas);
    }

    private static bool IsBasicInstallClass(PlayerSide side, RuntimeCardClass cardClass) =>
        side switch
        {
            PlayerSide.Runner => cardClass is (RuntimeCardClass.Program or RuntimeCardClass.Hardware or RuntimeCardClass.Resource),
            PlayerSide.Corp => cardClass is (RuntimeCardClass.Agenda or RuntimeCardClass.Asset or RuntimeCardClass.Upgrade or RuntimeCardClass.Ice),
            _ => false,
        };

    private static bool InstallDestinationExistsAndMatches(
        WorkingState working,
        PlayerSide side,
        RuntimeCardClass cardClass,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote)
    {
        if (side == PlayerSide.Runner)
        {
            return cardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware or RuntimeCardClass.Resource
                && destinationKind == InstallDestinationKind.RunnerRig
                && serverId is null
                && !createNewRemote;
        }

        bool isServerRoot = destinationKind == InstallDestinationKind.ServerRoot;
        bool isServerIce = destinationKind == InstallDestinationKind.ServerIce;
        if ((!isServerRoot && !isServerIce)
            || (serverId is null) == !createNewRemote)
        {
            return false;
        }

        if (createNewRemote)
        {
            return side == PlayerSide.Corp
                && ((isServerRoot
                        && cardClass is (RuntimeCardClass.Agenda or RuntimeCardClass.Asset or RuntimeCardClass.Upgrade))
                    || (isServerIce && cardClass == RuntimeCardClass.Ice));
        }

        ZoneState? destination = TryResolveInstallDestination(
            working,
            destinationKind,
            serverId,
            createNewRemote);
        ZoneKind expectedKind = isServerIce ? ZoneKind.ServerIce : ZoneKind.ServerRoot;
        if (destination is null || destination.Kind != expectedKind)
        {
            return false;
        }

        ServerKind serverKind = working.Servers
            .Single(server => server.Id == serverId!.Value)
            .Kind;
        return isServerIce
            ? cardClass == RuntimeCardClass.Ice
            : cardClass == RuntimeCardClass.Upgrade
                || (serverKind == ServerKind.Remote
                    && cardClass is (RuntimeCardClass.Agenda or RuntimeCardClass.Asset));
    }

    private static CardInstanceId[] InstallCandidates(
        WorkingState working,
        CardRuntimeSpec spec,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote)
    {
        if (createNewRemote)
        {
            return [];
        }

        ZoneState destination = ResolveExistingInstallDestination(working, destinationKind, serverId);
        if (destinationKind == InstallDestinationKind.RunnerRig
            && spec.Side == PlayerSide.Runner)
        {
            CardInstanceId[] conflicts = ActiveRunnerInstallConflictIds(working, spec);
            if (spec.CardClass != RuntimeCardClass.Program)
            {
                return conflicts;
            }

            CardInstanceId[] programs = destination.CardIds
                .Select(cardId => working.Cards.FirstOrDefault(card => card.Id == cardId))
                .Where(card => card is not null
                    && card.OwnerSide == PlayerSide.Runner
                    && card.ZoneId == destination.Id
                    && working.RuntimeCardCatalog!.TryGet(card.DefinitionId, out CardRuntimeSpec? candidateSpec)
                    && candidateSpec.CardClass == RuntimeCardClass.Program)
                .Select(card => card!.Id)
                .Concat(conflicts)
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
            return programs;
        }

        if (spec.CardClass == RuntimeCardClass.Program)
        {
            return working.Cards
                .Where(card => card.OwnerSide == PlayerSide.Runner
                    && card.ZoneId == destination.Id
                    && working.RuntimeCardCatalog!.TryGet(card.DefinitionId, out CardRuntimeSpec? candidateSpec)
                    && candidateSpec.CardClass == RuntimeCardClass.Program)
                .Select(card => card.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
        }

        if (destinationKind == InstallDestinationKind.ServerIce)
        {
            // ICE is ordered from innermost to outermost. Preserve that
            // physical order through the frozen candidate list so selected
            // trash leaves the remaining protectors in the same order and a
            // normal install appends the incoming ICE outermost.
            return destination.CardIds.ToArray();
        }

        // Root installs use one bounded candidate list. Required like-card
        // replacements are a subset, while upgrades may optionally trash any
        // root card without flattening all subsets into actions.
        return destination.CardIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
    }

    private static CardInstanceId[] InstallRequiredCandidates(
        WorkingState working,
        CardRuntimeSpec spec,
        IReadOnlyList<CardInstanceId> candidates,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote)
    {
        if (destinationKind == InstallDestinationKind.RunnerRig
            && spec.Side == PlayerSide.Runner)
        {
            return ActiveRunnerInstallConflictIds(working, spec);
        }

        if (createNewRemote || destinationKind == InstallDestinationKind.RunnerRig)
        {
            return [];
        }

        if (destinationKind == InstallDestinationKind.ServerIce)
        {
            return [];
        }

        return spec.CardClass switch
        {
            RuntimeCardClass.Agenda or RuntimeCardClass.Asset => candidates
                .Where(cardId => RuntimeClass(working, cardId) is RuntimeCardClass.Agenda or RuntimeCardClass.Asset)
                .ToArray(),
            RuntimeCardClass.Upgrade when IsRegion(spec) => candidates
                .Where(cardId => RuntimeClass(working, cardId) == RuntimeCardClass.Upgrade && IsRegion(working, cardId))
                .ToArray(),
            _ => [],
        };
    }

    private static bool AllRuntimeCardsKnown(WorkingState working, IEnumerable<CardInstanceId> cardIds) =>
        cardIds.All(cardId => working.RuntimeCardCatalog!.TryGet(GetCard(working, cardId).DefinitionId, out _));

    private static bool AllActiveRunnerRigCardsKnown(WorkingState working)
    {
        ZoneState? rig = working.Zones.FirstOrDefault(zone =>
            zone.Kind == ZoneKind.RunnerRig
            && zone.OwnerSide == PlayerSide.Runner
            && zone.ServerId is null);
        if (rig is null)
        {
            return false;
        }

        foreach (CardInstanceId cardId in rig.CardIds)
        {
            CardInstanceState? card = working.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
            if (card is not null
                && card.OwnerSide == PlayerSide.Runner
                && card.ZoneId == rig.Id
                && card.FaceUp
                && !working.RuntimeCardCatalog!.TryGet(card.DefinitionId, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static CardInstanceId[] ActiveRunnerInstallConflictIds(
        WorkingState working,
        CardRuntimeSpec incoming)
    {
        ZoneState? rig = working.Zones.FirstOrDefault(zone =>
            zone.Kind == ZoneKind.RunnerRig
            && zone.OwnerSide == PlayerSide.Runner
            && zone.ServerId is null);
        if (rig is null || (!incoming.Unique && !incoming.Console))
        {
            return [];
        }

        return rig.CardIds
            .Select(cardId => working.Cards.FirstOrDefault(card => card.Id == cardId))
            .Where(card => card is not null
                && card.OwnerSide == PlayerSide.Runner
                && card.ZoneId == rig.Id
                && card.FaceUp
                && working.RuntimeCardCatalog!.TryGet(card.DefinitionId, out CardRuntimeSpec? current)
                && ((incoming.Unique
                        && incoming.UniquenessKey is not null
                        && current.Unique
                        && current.UniquenessKey is not null
                        && string.Equals(current.UniquenessKey, incoming.UniquenessKey, StringComparison.Ordinal))
                    || (incoming.Console && current.Console)))
            .Select(card => card!.Id)
            .Distinct()
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static RuntimeCardClass RuntimeClass(WorkingState working, CardInstanceId cardId) =>
        working.RuntimeCardCatalog!.GetSpec(GetCard(working, cardId).DefinitionId).CardClass;

    private static bool IsRegion(CardRuntimeSpec spec) =>
        spec.Subtypes.Any(subtype => string.Equals(subtype, "region", StringComparison.OrdinalIgnoreCase));

    private static bool IsRegion(WorkingState working, CardInstanceId cardId) => IsRegion(
        working.RuntimeCardCatalog!.GetSpec(GetCard(working, cardId).DefinitionId));

    private static bool CanInstallProgram(
        WorkingState working,
        CardInstanceState incoming,
        CardRuntimeSpec incomingSpec,
        IReadOnlyList<CardInstanceId> allCandidates)
    {
        int used = CurrentUsedMemory(working, incoming.OwnerSide);
        int freed = 0;
        int removedStatic = 0;
        foreach (CardInstanceId candidateId in allCandidates)
        {
            CardInstanceState candidate = GetCard(working, candidateId);
            if (working.RuntimeCardCatalog!.TryGet(candidate.DefinitionId, out CardRuntimeSpec? candidateSpec))
            {
                freed = checked(freed + candidateSpec.MemoryUnits);
                removedStatic = checked(removedStatic + ActiveInstalledStaticModifier(working, candidate, BehaviorStaticModifierKind.MemoryUnits));
            }
        }

        int currentStatic = BehaviorStaticModifierProjection.Sum(
            working.BehaviorCatalog,
            working.Cards,
            working.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits);
        int incomingStatic = IncomingStaticModifier(working, incoming, BehaviorStaticModifierKind.MemoryUnits);
        PlayerRulesState? rules = working.RulesState?.Players.SingleOrDefault(player => player.PlayerId == GetPlayer(working, incoming.OwnerSide).Id);
        int baseMemory = rules?.BaseMemory ?? 4;
        int effective = checked(baseMemory + currentStatic - removedStatic + incomingStatic);
        return checked(used - freed + incomingSpec.MemoryUnits) <= effective;
    }

    private static int CurrentUsedMemory(WorkingState working, PlayerSide side)
    {
        PlayerId owner = GetPlayer(working, side).Id;
        if (working.RulesState?.Players.SingleOrDefault(player => player.PlayerId == owner) is { } rules)
        {
            return rules.UsedMemory;
        }

        return working.Cards
            .Where(card => card.OwnerSide == PlayerSide.Runner
                && working.Zones.FirstOrDefault(zone => zone.Id == card.ZoneId)?.Kind == ZoneKind.RunnerRig)
            .Sum(card => working.RuntimeCardCatalog!.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                && spec.CardClass == RuntimeCardClass.Program
                ? spec.MemoryUnits
                : 0);
    }

    private static int IncomingStaticModifier(
        WorkingState working,
        CardInstanceState card,
        BehaviorStaticModifierKind kind)
    {
        if (!working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition))
        {
            return 0;
        }

        return definition.StaticModifiers
            .Where(modifier => modifier.Kind == kind)
            .Sum(modifier => modifier.Delta);
    }

    private static int ActiveInstalledStaticModifier(
        WorkingState working,
        CardInstanceState card,
        BehaviorStaticModifierKind kind)
    {
        ZoneState zone = ZoneForInstallCard(working, card);
        if (card.OwnerSide != PlayerSide.Runner
            || zone.Kind != ZoneKind.RunnerRig
            || !card.FaceUp)
        {
            return 0;
        }

        return IncomingStaticModifier(working, card, kind);
    }

    private static int CalculateInstallCost(
        WorkingState working,
        CardRuntimeSpec spec,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote,
        IReadOnlyList<CardInstanceId> selected)
    {
        if (spec.CardClass != RuntimeCardClass.Ice)
        {
            return spec.InstallCost ?? 0;
        }

        if (createNewRemote)
        {
            return 0;
        }

        if (destinationKind != InstallDestinationKind.ServerIce || serverId is null)
        {
            throw new ResolutionFailureException("ICE install requires a server ICE destination.");
        }

        ZoneState ice = ResolveExistingInstallDestination(working, destinationKind, serverId);
        int selectedCount = selected.Count(cardId => ice.CardIds.Contains(cardId));
        return Math.Max(0, ice.CardIds.Count - selectedCount);
    }

    private static bool InstallSelectionSatisfiesRequirements(
        WorkingState working,
        InstallFrame frame,
        InstallPlan plan,
        IReadOnlyList<CardInstanceId> selected)
    {
        if (!plan.RequiredReplacementCardIds.All(selected.Contains))
        {
            return false;
        }

        if (frame.CardClass == RuntimeCardClass.Program)
        {
            CardInstanceState incoming = GetCard(working, frame.CardId);
            return CanInstallProgramWithSelection(working, incoming, plan.Spec, selected);
        }

        return true;
    }

    private static bool CanInstallProgramWithSelection(
        WorkingState working,
        CardInstanceState incoming,
        CardRuntimeSpec incomingSpec,
        IReadOnlyList<CardInstanceId> selected)
    {
        int used = CurrentUsedMemory(working, incoming.OwnerSide);
        int freed = 0;
        int removedStatic = 0;
        foreach (CardInstanceId candidateId in selected)
        {
            CardInstanceState candidate = GetCard(working, candidateId);
            CardRuntimeSpec candidateSpec = working.RuntimeCardCatalog!.GetSpec(candidate.DefinitionId);
            freed = checked(freed + candidateSpec.MemoryUnits);
            removedStatic = checked(removedStatic + ActiveInstalledStaticModifier(working, candidate, BehaviorStaticModifierKind.MemoryUnits));
        }

        int currentStatic = BehaviorStaticModifierProjection.Sum(
            working.BehaviorCatalog,
            working.Cards,
            working.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits);
        int incomingStatic = IncomingStaticModifier(working, incoming, BehaviorStaticModifierKind.MemoryUnits);
        PlayerRulesState? rules = working.RulesState?.Players.SingleOrDefault(player => player.PlayerId == GetPlayer(working, incoming.OwnerSide).Id);
        int effective = checked((rules?.BaseMemory ?? 4) + currentStatic - removedStatic + incomingStatic);
        return checked(used - freed + incomingSpec.MemoryUnits) <= effective;
    }

    private static InstallPlan ValidateInstallFrame(
        WorkingState working,
        InstallFrame frame,
        bool requireFinalLegality)
    {
        if (working.RuntimeCardCatalog is null)
        {
            throw new ResolutionFailureException("Install enumeration requires an injected runtime card catalog.");
        }
        if (requireFinalLegality && !IsLegalInstallHost(working, frame.CardId, frame.HostCardId))
        {
            throw new ResolutionFailureException("The final install must identify a legal host.");
        }

        InstallPlan plan = BuildInstallPlan(
                working,
                GetPlayer(working, frame.Owner).Side,
                frame.CardId,
                frame.DestinationKind,
                frame.ServerId,
                frame.CreateNewRemote,
                frame.InitiationKind,
                frame.InwardAnchorIceId, frame.HostCardId,
                frame.InitiationKind == InstallInitiationKind.BehaviorZoneInstall
                    ? ZoneInstallRuleFor(working, frame)
                    : null)
            ?? throw new ResolutionFailureException("The frozen install destination or card is stale.");

        if (frame.DefinitionId != plan.Card.DefinitionId
            || frame.CardClass != plan.Spec.CardClass
            || frame.SourceZoneId != plan.Source.Id
            || frame.SourceZoneKind != plan.Source.Kind
            || !frame.CandidateReplacementCardIds.SequenceEqual(plan.CandidateReplacementCardIds)
            || !frame.RequiredReplacementCardIds.SequenceEqual(plan.RequiredReplacementCardIds)
            || frame.CandidateReplacementDefinitionIds is not null
                && !frame.CandidateReplacementDefinitionIds.SequenceEqual(plan.CandidateReplacementDefinitionIds)
            || plan.Spec.Side == PlayerSide.Corp
                && frame.CandidateReplacementCardIds.Count > 0
                && frame.CandidateReplacementDefinitionIds is null
            || frame.SelectedReplacementCardIds.Any(id => !frame.CandidateReplacementCardIds.Contains(id))
            || frame.SelectedReplacementCardIds.Distinct().Count() != frame.SelectedReplacementCardIds.Count
            || frame.InwardAnchorIceId != plan.InwardAnchorIceId
            || (frame.InitiationKind == InstallInitiationKind.BasicAction
                ? frame.ParentFrameIdentity is not null
                : !StableIdentifier.TryValidate(frame.ParentFrameIdentity))
            || (frame.InitiationKind == InstallInitiationKind.BehaviorEffect
                && (frame.CandidateReplacementDefinitionIds is null
                    || !frame.CandidateReplacementDefinitionIds.SequenceEqual(plan.CandidateReplacementDefinitionIds)))
            || (frame.InitiationKind == InstallInitiationKind.BehaviorEffect
                && (frame.Owner != GetPlayer(working, PlayerSide.Runner).Id
                    || !IsBasicInstallClass(PlayerSide.Runner, frame.CardClass)
                    || frame.SourceZoneKind is not (ZoneKind.RunnerStack or ZoneKind.RunnerGrip)
                    || frame.DestinationKind != InstallDestinationKind.RunnerRig
                    || frame.ServerId is not null
                    || frame.CreateNewRemote))
            || (frame.InitiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts
                && (frame.DestinationKind != InstallDestinationKind.ServerIce
                    || frame.ServerId is null
                    || frame.CreateNewRemote
                    || frame.InwardAnchorIceId is null
                    || frame.CardClass != RuntimeCardClass.Ice
                    || frame.InstallCost != 0
                    || frame.CandidateReplacementCardIds.Count != 0
                    || frame.RequiredReplacementCardIds.Count != 0
                    || frame.SelectedReplacementCardIds.Count != 0))
        )
        {
            throw new ResolutionFailureException("The frozen install frame no longer matches the current state.");
        }

        (int expectedEffectiveCost, CostQuote? expectedQuote) =
            InstallFrameCost(working, frame, plan, frame.SelectedReplacementCardIds);
        if (frame.InstallCost != expectedEffectiveCost)
        {
            throw new ResolutionFailureException("The frozen install cost is stale.");
        }

        if (!CostQuotesEqual(frame.CostQuote, expectedQuote))
        {
            throw new ResolutionFailureException("The frozen install cost quote is stale or forged.");
        }

        // The destination kind is only a logical selector.  The physical zone
        // is part of the frozen install transaction so a loaded/pending choice
        // cannot silently retarget a reconstructed topology. New-remote
        // actions intentionally retain a null zone until final commit.
        if (frame.DestinationZoneId != plan.DestinationZoneId)
        {
            throw new ResolutionFailureException("The frozen install destination zone is stale.");
        }

        if (frame.InitiationKind == InstallInitiationKind.BehaviorEffect)
        {
            if (working.ResolutionStack.Count < 2
                || working.ResolutionStack[^2] is not BehaviorResolutionFrame behaviorParent
                || !string.Equals(
                    frame.ParentFrameIdentity,
                    BehaviorFrameIdentity.Compute(behaviorParent),
                    StringComparison.Ordinal))
            {
                throw new ResolutionFailureException(
                    "The behavior install parent frame identity is stale or forged.");
            }

            List<BehaviorInstruction> behaviorInstructions = GetBehaviorInstructions(working, behaviorParent);
            if (behaviorParent.InstructionCursor < 0
                || behaviorParent.InstructionCursor >= behaviorInstructions.Count
                || behaviorInstructions[behaviorParent.InstructionCursor].Kind != BehaviorInstructionKind.InstallCard)
            {
                throw new ResolutionFailureException(
                    "The behavior install parent cursor no longer identifies an InstallCard instruction.");
            }

            InstallPlan behaviorPlan = ValidateBehaviorInstallInstruction(
                working,
                behaviorParent,
                behaviorInstructions[behaviorParent.InstructionCursor]);
            if (behaviorPlan.Card.Id != frame.CardId
                || behaviorPlan.DestinationZoneId != frame.DestinationZoneId)
            {
                throw new ResolutionFailureException(
                    "The behavior install frame no longer matches its parent instruction binding.");
            }
        }
        else if (frame.InitiationKind == InstallInitiationKind.BehaviorZoneInstall)
        {
            if (working.ResolutionStack.Count < 2
                || working.ResolutionStack[^2] is not BehaviorInstallChoiceFrame choiceParent
                || !string.Equals(frame.ParentFrameIdentity, choiceParent.FrameIdentity, StringComparison.Ordinal)
                || choiceParent.Stage != BehaviorInstallChoiceStage.AwaitingInstall
                || choiceParent.Owner != frame.Owner
                || !choiceParent.Choices.Any(choice => choice.CardId == frame.CardId
                    && choice.DestinationKind == frame.DestinationKind
                    && choice.ServerId == frame.ServerId
                    && choice.CreateNewRemote == frame.CreateNewRemote))
            {
                throw new ResolutionFailureException(
                    "The behavior zone install parent choice is stale or forged.");
            }
        }
        else if (frame.InitiationKind is InstallInitiationKind.IceSubroutineNormal
            or InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
        {
            if (working.ResolutionStack.Count < 2
                || working.ResolutionStack[^2] is not IceSubroutineFrame subroutineParent
                || !string.Equals(
                    frame.ParentFrameIdentity,
                    subroutineParent.FrameIdentity,
                    StringComparison.Ordinal)
                || subroutineParent.Stage != IceSubroutineFrameStage.AwaitingInstall
                || (frame.InitiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts
                    && subroutineParent.IceId != frame.InwardAnchorIceId))
            {
                throw new ResolutionFailureException(
                    "The ICE subroutine install parent frame identity or stage is stale or forged.");
            }

            RunState parentRun = working.RunState
                ?? throw new ResolutionFailureException("The ICE subroutine install parent requires a live run.");
            ZoneState parentIceZone = ResolveExistingInstallDestination(
                working,
                InstallDestinationKind.ServerIce,
                subroutineParent.ServerId);
            if (parentRun.Id != subroutineParent.RunId
                || parentRun.Step != RunTimingStep.Encounter
                || parentRun.AttackedServer != subroutineParent.ServerId
                || parentRun.CurrentIceId != subroutineParent.IceId
                || parentRun.Encounter is not { Status: RunEncounterStatus.Initialized } parentEncounter
                || parentEncounter.Id != subroutineParent.EncounterId
                || parentEncounter.IceId != subroutineParent.IceId
                || parentIceZone.Id != subroutineParent.IceZoneId
                || !parentIceZone.CardIds.SequenceEqual(subroutineParent.IceZoneCardIds))
            {
                throw new ResolutionFailureException(
                    "The ICE subroutine install parent no longer matches the live encounter topology.");
            }

            if (frame.InitiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts
                && (subroutineParent.RunId != parentRun.Id
                    || subroutineParent.ServerId != frame.ServerId
                    || subroutineParent.IceId != frame.InwardAnchorIceId))
            {
                throw new ResolutionFailureException(
                    "The inward ICE-subroutine install anchor is stale or forged.");
            }
        }

        foreach (CardInstanceId selected in frame.SelectedReplacementCardIds)
        {
            CardInstanceState selectedCard = GetCard(working, selected);
            ZoneState selectedZone = ZoneForInstallCard(working, selectedCard);
            if (plan.DestinationZoneId is null || selectedZone.Id != plan.DestinationZoneId.Value)
            {
                throw new ResolutionFailureException("A selected install replacement moved out of the destination.");
            }
        }

        if (requireFinalLegality)
        {
            if (!InstallSelectionSatisfiesRequirements(working, frame, plan, frame.SelectedReplacementCardIds))
            {
                throw new ResolutionFailureException("The install selection does not satisfy replacement or memory requirements.");
            }

            PlayerState player = GetPlayer(working, frame.Owner);
            if ((frame.InitiationKind == InstallInitiationKind.BasicAction && player.Clicks < 1)
                || player.Credits + DeclaredHostedCredits(working, player.Side, BehaviorHostedCreditUseKind.InstallResource, GetCard(working, frame.CardId)) < expectedEffectiveCost)
            {
                throw new ResolutionFailureException("The install is no longer affordable.");
            }
        }

        return plan;
    }

    private static ZoneState ZoneForInstallCard(WorkingState working, CardInstanceState card) =>
        working.Zones.SingleOrDefault(zone => zone.Id == card.ZoneId)
        ?? throw new ResolutionFailureException($"Card '{card.Id}' references an unknown zone.");

    private static ZoneState? TryResolveInstallDestination(
        WorkingState working,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote)
    {
        if (createNewRemote)
        {
            // Creation is deliberately represented by a null physical zone
            // while planning/validation. Only CommitInstall may allocate its
            // server and two zones at the final commit boundary.
            return null;
        }

        if (destinationKind == InstallDestinationKind.RunnerRig)
        {
            return working.Zones.FirstOrDefault(zone =>
                zone.Kind == ZoneKind.RunnerRig
                && zone.ServerId is null
                && zone.OwnerSide == PlayerSide.Runner);
        }

        if (serverId is null)
        {
            return null;
        }

        ServerState? server = working.Servers.SingleOrDefault(candidate => candidate.Id == serverId.Value);
        if (server is null || !server.Exists)
        {
            return null;
        }

        ZoneId zoneId = destinationKind == InstallDestinationKind.ServerIce
            ? server.IceZoneId
            : server.RootZoneId;
        ZoneKind expectedKind = destinationKind == InstallDestinationKind.ServerIce
            ? ZoneKind.ServerIce
            : ZoneKind.ServerRoot;
        return working.Zones.FirstOrDefault(zone =>
            zone.Id == zoneId
            && zone.Kind == expectedKind
            && zone.OwnerSide == PlayerSide.Corp
            && zone.ServerId == server.Id);
    }

    private static ZoneState ResolveExistingInstallDestination(
        WorkingState working,
        InstallDestinationKind destinationKind,
        ServerId? serverId) =>
        TryResolveInstallDestination(working, destinationKind, serverId, createNewRemote: false)
        ?? throw new ResolutionFailureException("The existing install destination does not exist.");

    private static ZoneState CreateNewRemoteInstallDestination(
        WorkingState working,
        InstallDestinationKind destinationKind)
    {
        if (destinationKind is not (InstallDestinationKind.ServerRoot or InstallDestinationKind.ServerIce))
        {
            throw new ResolutionFailureException("A new remote install requires a server root or ICE destination.");
        }

        ServerId remoteId = working.Allocator.NextServerId();
        ZoneId iceId = working.Allocator.NextZoneId();
        ZoneId rootId = working.Allocator.NextZoneId();
        working.Servers.Add(new ServerState(remoteId, ServerKind.Remote, iceId, rootId));
        working.Zones.Add(new ZoneState(iceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId));
        working.Zones.Add(new ZoneState(rootId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId));
        ZoneId destinationId = destinationKind == InstallDestinationKind.ServerIce ? iceId : rootId;
        return working.Zones.Single(zone => zone.Id == destinationId);
    }

    private static void AddInstallActions(WorkingState working, PlayerSide side, List<LegalAction> actions)
    {
        if (side is not (PlayerSide.Runner or PlayerSide.Corp) || working.RuntimeCardCatalog is null)
        {
            return;
        }

        if (GetPlayer(working, side).Clicks <= 0)
        {
            return;
        }

        ZoneState hand = GetZone(
            working,
            side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip);
        IEnumerable<CardInstanceId> sources = hand.CardIds.OrderBy(id => id.Value, StringComparer.Ordinal);
        if (side == PlayerSide.Runner)
        {
            sources = sources.Concat(GripLikeHostedCardIds(working));
        }

        foreach (CardInstanceId cardId in sources)
        {
            CardInstanceState card = GetCard(working, cardId);
            if (!working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                || !IsBasicInstallClass(side, spec.CardClass))
            {
                continue;
            }

            foreach ((InstallDestinationKind destinationKind, ServerId? serverId, bool createNewRemote) in InstallDestinations(working, side, spec))
            {
                foreach (CardInstanceId? hostCardId in InstallHosts(working, card))
                {
                    InstallPlan? plan = BuildInstallPlan(working, side, cardId, destinationKind, serverId, createNewRemote, hostCardId: hostCardId);
                    if (plan is null)
                    {
                        continue;
                    }

                    actions.Add(new LegalAction(
                        working.Allocator.NextActionId(),
                        ActionKind.InstallCard,
                        "Install card",
                        new InstallCardParameters(
                            cardId,
                            destinationKind,
                            serverId,
                            createNewRemote,
                            plan.InstallCost,
                            plan.CandidateReplacementCardIds,
                            plan.RequiredReplacementCardIds,
                            [],
                            finalizeSelection: plan.CandidateReplacementCardIds.Count == 0,
                            costQuote: plan.InstallCostQuote, hostCardId: hostCardId)));
                }
            }
        }
    }

    /// <summary>
    /// Re-materializes the Runner's basic-install payloads against a detached
    /// working copy. State validation uses this projection to prove that a
    /// serialized TurnAction decision contains exactly the actions Core would
    /// issue for the current hand, credits, clicks, catalog, and memory.
    /// </summary>
    internal static IReadOnlyList<InstallCardParameters> MaterializeRunnerInstallParameters(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);

        WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        var actions = new List<LegalAction>();
        AddInstallActions(working, PlayerSide.Runner, actions);
        return actions
            .Select(static action => (InstallCardParameters)action.Parameters)
            .ToArray();
    }

    /// <summary>
    /// Re-materializes the bounded Corp root-install payloads against a
    /// detached working copy.  In particular, planning a new remote must not
    /// consume allocator state or change topology; the commit reducer is the
    /// only path that creates the server and its two zones.
    /// </summary>
    internal static IReadOnlyList<InstallCardParameters> MaterializeCorpInstallParameters(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);

        WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        var actions = new List<LegalAction>();
        AddInstallActions(working, PlayerSide.Corp, actions);
        return actions
            .Select(static action => (InstallCardParameters)action.Parameters)
            .ToArray();
    }

    /// <summary>
    /// Re-materializes an InstallChoice continuation set from a detached
    /// working copy.  The projection intentionally returns payloads rather
    /// than <see cref="LegalAction"/> instances so it cannot consume the
    /// state's allocator; callers compare semantic payloads independently of
    /// serialized ActionId values.
    /// </summary>
    internal static IReadOnlyList<InstallCardParameters> MaterializeInstallChoiceParameters(
        GameState state,
        InstallFrame frame,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);

        WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        InstallPlan plan = ValidateInstallFrame(working, frame, requireFinalLegality: false);
        return MaterializeInstallChoiceParameters(working, frame, plan);
    }

    /// <summary>
    /// VP-INSTALL-SERVER-KIND: whether a card that may be installed only in some kinds of server (The Red Room, Flagship) may
    /// go to this destination; a new server is a remote.
    /// </summary>
    private static bool IsAllowedInstallServer(
        WorkingState working,
        CardInstanceId cardId,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote)
    {
        if (destinationKind != InstallDestinationKind.ServerRoot
            || !working.BehaviorCatalog.TryGet(GetCard(working, cardId).DefinitionId, out CardBehaviorDefinition? definition)
            || definition.InstallServerKinds is not { } allowed)
        {
            return true;
        }

        ServerKind? kind = createNewRemote
            ? ServerKind.Remote
            : working.Servers.FirstOrDefault(server => server.Id == serverId)?.Kind;
        return kind is { } serverKind && allowed.Contains(serverKind);
    }

    private static IEnumerable<(InstallDestinationKind DestinationKind, ServerId? ServerId, bool CreateNewRemote)> InstallDestinations(
        WorkingState working,
        PlayerSide side,
        CardRuntimeSpec spec)
    {
        if (side == PlayerSide.Runner)
        {
            yield return (InstallDestinationKind.RunnerRig, null, false);
            yield break;
        }

        if (side != PlayerSide.Corp
            || spec.CardClass is not (RuntimeCardClass.Agenda or RuntimeCardClass.Asset or RuntimeCardClass.Upgrade or RuntimeCardClass.Ice))
        {
            yield break;
        }

        IEnumerable<ServerState> servers = working.Servers
            .Where(server => server.Exists)
            .OrderBy(server => (int)server.Kind)
            .ThenBy(server => server.Id.Value, StringComparer.Ordinal);
        foreach (ServerState server in servers)
        {
            if (spec.CardClass == RuntimeCardClass.Ice)
            {
                yield return (InstallDestinationKind.ServerIce, server.Id, false);
                continue;
            }

            if (spec.CardClass is (RuntimeCardClass.Agenda or RuntimeCardClass.Asset)
                && server.Kind != ServerKind.Remote)
            {
                continue;
            }

            yield return (InstallDestinationKind.ServerRoot, server.Id, false);
        }

        yield return spec.CardClass == RuntimeCardClass.Ice
            ? (InstallDestinationKind.ServerIce, null, true)
            : (InstallDestinationKind.ServerRoot, null, true);
    }

    private static void StartNextTurnActionWindow(WorkingState working, TurnFrame frame)
    {
        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            frame.ActiveSide,
            frame.TurnNumber,
            TurnFrameStage.ActionPriority,
            TurnWindow(frame.ActiveSide),
            frame.ActiveSide,
            forcedActionEnd: frame.ForcedActionEnd);
    }

    private static bool IsInstallFrameTop(WorkingState working) =>
        working.ResolutionStack.Count > 0
        && working.ResolutionStack[^1] is InstallFrame;

}
