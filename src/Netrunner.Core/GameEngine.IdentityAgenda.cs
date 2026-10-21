namespace Netrunner.Core;

/// <summary>Composable identity and agenda timing rules; no card identifiers are interpreted here.</summary>
public static partial class GameEngine
{
    private const string ScoreProhibitionKey = "score-prohibited";

    private static void QueueIdentityOccurrence(WorkingState working, OccurrenceKind kind,
        PlayerSide side, CardInstanceId? subject = null)
    {
        if (!HasActiveCatalogTrigger(working, kind)) return;
        PlayerId controller = GetPlayer(working, side).Id;
        working.ResolutionStack.Add(new EffectResolutionFrame(controller, new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(kind, controller, subject))));
    }

    private static bool CatalogIdentityTriggerMatches(WorkingState working, CardInstanceState source,
        BehaviorTrigger trigger, OccurrenceSnapshot occurrence)
    {
        if (trigger.OccurrenceControllerSide is { } side
            && GetPlayer(working, occurrence.Controller).Side != side) return false;
        TurnFactsState? facts = working.Turn.History?.ThisTurn(working.Turn.ActiveSide);
        if (trigger.OncePerTurn && facts?.HasUsedAbility(source.Id, trigger.Key) == true) return false;
        return trigger.Condition switch
        {
            null => true,
            BehaviorTriggerCondition.FacedownArchives => working.Cards.Any(card => !card.FaceUp
                && working.Zones.Any(zone => zone.Id == card.ZoneId && zone.Kind == ZoneKind.CorpDiscard)),
            BehaviorTriggerCondition.FirstRunnerTag => occurrence.Effect is TagAdjustmentEffect { Delta: > 0 } tag
                && tag.Target == GetPlayer(working, PlayerSide.Runner).Id
                && (facts is null || facts.TagTurnChanges.Count(change => change.Delta > 0
                    && change.AffectedPlayerId == tag.Target) == 1),
            BehaviorTriggerCondition.HqOrRdRun => occurrence.Effect is GameplayOccurrenceEffect { ServerId: { } server, RunContext.AccessOccurrenceCount: > 0 }
                && working.Servers.Any(value => value.Id == server
                    && value.Kind is ServerKind.Headquarters or ServerKind.RAndD),
            BehaviorTriggerCondition.FaceUpInstalledSubject => occurrence.SourceCardId is { } subject
                && working.Cards.FirstOrDefault(card => card.Id == subject) is { FaceUp: true } faceUp
                && IsInstalledCard(working, faceUp.Id),
            BehaviorTriggerCondition.SubjectInstalledWithoutCredits => occurrence.SourceCardId is { } installed
                && facts?.FreeInstallIds?.Contains(installed) == true,
            BehaviorTriggerCondition.SubroutineResolvedThisRun => working.RunState?.ResolvedSubroutineLedger is { Count: > 0 },
            // VP-SCORING-FACTS: the turn's installs still name an agenda after it moves to a score area.
            BehaviorTriggerCondition.SubjectInstalledThisTurn => occurrence.SourceCardId is { } installedNow
                && facts?.InstalledCardIds.Contains(installedNow) == true,
            BehaviorTriggerCondition.SubjectNotInstalledThisTurn => occurrence.SourceCardId is { } installedEarlier
                && facts?.InstalledCardIds.Contains(installedEarlier) != true,
            BehaviorTriggerCondition.SubjectOutermostFullyBroken => occurrence.SourceCardId is { } passed
                && IsOutermostFullyBrokenIce(working, passed),
            BehaviorTriggerCondition.RunStartedBySource => working.RunState?.InitiatingSource == source.Id,
            BehaviorTriggerCondition.RunOnAnotherServer => working.RunState is { } otherRun
                && SourceServerId(working, source) != otherRun.AttackedServer,
            BehaviorTriggerCondition.SubjectStrengthAtMostZero => occurrence.SourceCardId is { } broken
                && EncounteredIceStrength(working, broken) is <= 0,
            BehaviorTriggerCondition.TrashedSubjectWasInstalled => occurrence.Effect is GameplayOccurrenceEffect
            {
                Kind: OccurrenceKind.AccessedCardTrashed,
                ServerId: not null,
            },
            _ => false,
        };
    }

    /// <summary>
    /// VP-ICE-PASSED: the ICE is outermost in its server's ICE zone (the last position of its ICE zone), and the
    /// run's last encounter was with it and left no subroutine unbroken.
    /// </summary>
    private static bool IsOutermostFullyBrokenIce(WorkingState working, CardInstanceId iceId) =>
        working.RunState is { Encounter: { } encounter } run
        && encounter.IceId == iceId
        && encounter.Subroutines.Count > 0
        && encounter.Subroutines.All(static subroutine => subroutine.Broken)
        && working.Servers.Single(server => server.Id == run.AttackedServer) is var server
        && working.Zones.Single(zone => zone.Id == server.IceZoneId).CardIds is { Count: > 0 } ice
        && ice[^1] == iceId;

    /// <summary>VP-SUBROUTINE-BROKEN: the encountered ICE's current strength, or null outside its encounter.</summary>
    private static int? EncounteredIceStrength(WorkingState working, CardInstanceId iceId)
    {
        if (working.RunState is not { Encounter.IceId: var encountered } run || encountered != iceId)
        {
            return null;
        }

        CardInstanceState ice = GetCard(working, iceId);
        ServerState server = working.Servers.Single(value => value.Id == run.AttackedServer);
        return EncounterStrength.ProjectIceQuote(
            working.BehaviorCatalog,
            working.Cards,
            working.Zones,
            working.RulesState,
            run,
            ice,
            server.Kind)?.EffectiveValue;
    }

    private static void MarkCatalogTriggerUsed(WorkingState working, CardInstanceState source, BehaviorTrigger trigger)
    {
        if (!trigger.OncePerTurn || working.Turn.History?.ThisTurn(working.Turn.ActiveSide) is null) return;
        TurnState turn = working.Turn;
        ReplaceTurn(working, turn.ActiveSide, turn.Phase, turn.TurnNumber, turn.Step,
            turn.History.MarkAbilityUsed(turn.ActiveSide, source.Id, trigger.Key));
    }

    private static bool IsScoringProhibited(WorkingState working) =>
        working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.UsedAbilityKeys
            .Any(key => key.AbilityKey == ScoreProhibitionKey) == true;

    private static void MarkScoreProhibition(WorkingState working, CardInstanceId source)
    {
        TurnState turn = working.Turn;
        if (turn.History?.ThisTurn(turn.ActiveSide) is not null)
            ReplaceTurn(working, turn.ActiveSide, turn.Phase, turn.TurnNumber, turn.Step,
                turn.History.MarkAbilityUsed(turn.ActiveSide, source, ScoreProhibitionKey));
    }

    /// <summary>
    /// EL-REZ-PAYING-LESS: rezzes the selected installed ICE, paying its rez
    /// cost less the amount (Mycoweb); one the Corp cannot afford stays unrezzed.
    /// </summary>
    private static void ResolveBehaviorRezPayingLess(WorkingState working, BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        IReadOnlyList<CardInstanceId> selected = ResolveBehaviorCards(working, frame, instruction.Target);
        if (selected.Count == 0)
        {
            AdvanceBehaviorCursor(working, RecordBehaviorAmount(frame, instruction.RecordAs, 0));
            return;
        }

        if (selected.Count != 1) throw new ResolutionFailureException("A rez instruction requires one selected ICE.");
        CardInstanceState card = GetCard(working, selected[0]);
        ZoneState zone = ZoneForCard(working, card.Id);
        if (zone.Kind != ZoneKind.ServerIce || card.OwnerSide != PlayerSide.Corp
            || working.RuntimeCardCatalog?.GetSpec(card.DefinitionId).CardClass != RuntimeCardClass.Ice)
            throw new ResolutionFailureException("The selected rez target must be installed Corp ICE.");
        // VP-INSTALL-AND-REZ-LESS: what a shared discount leaves after the install tax, never below zero.
        int discount = instruction.AmountOperand is { Kind: BehaviorAmountKind.DiscountLeftAfterIceInstallTax } shared
            ? Math.Max(0, checked(shared.Offset - zone.CardIds.TakeWhile(id => id != card.Id).Count() * shared.Multiplier))
            : instruction.Amount;
        int fullCost = 0;
        bool rezzes = !card.Rezzed && TryGetRezCostQuote(working, card, RuntimeCardClass.Ice, out fullCost, out _);
        int cost = Math.Max(0, fullCost - discount);
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        // Paid like an ordinary rez: the wallet and declared hosted credits (EL.6 review).
        rezzes &= corp.Credits + DeclaredHostedCredits(working, PlayerSide.Corp, BehaviorHostedCreditUseKind.RezInServer, card) >= cost;
        AdvanceBehaviorCursor(working, RecordBehaviorAmount(frame, instruction.RecordAs,
            rezzes && (instruction.Filter is null || BehaviorCardMatches(working, card.Id, instruction.Filter)) ? 1 : 0));
        if (!rezzes) return;
        PayCreditsWithDeclaredHosted(working, corp.Id, BehaviorHostedCreditUseKind.RezInServer, card, cost);
        ReplaceCard(working, new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide,
            faceUp: true, rezzed: true, card.AdvancementCounters));
        Emit(working, EngineEventKinds.CardRezzed, new CardRezzedEventPayload(card.Id, cost));
        RecordOptInTurnEvent(working, TurnEventKind.IceRezzed);
        TrashCorpUniqueRezConflicts(working, card.Id);
        QueueCardRezzedOccurrence(working, corp.Id, card.Id, zone.ServerId);
    }

    private static void ResolveBehaviorRezIgnoringCosts(WorkingState working, BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        IReadOnlyList<CardInstanceId> selected = ResolveBehaviorCards(working, frame, instruction.Target);
        AdvanceBehaviorCursor(working, frame);
        if (selected.Count == 0) return;
        if (selected.Count != 1) throw new ResolutionFailureException("A rez instruction requires one selected ICE.");
        CardInstanceState card = GetCard(working, selected[0]);
        ZoneState zone = ZoneForCard(working, card.Id);
        if (zone.Kind != ZoneKind.ServerIce || card.OwnerSide != PlayerSide.Corp
            || working.RuntimeCardCatalog?.GetSpec(card.DefinitionId).CardClass != RuntimeCardClass.Ice)
            throw new ResolutionFailureException("The selected rez target must be installed Corp ICE.");
        if (card.Rezzed) return;
        ReplaceCard(working, new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide,
            faceUp: true, rezzed: true, card.AdvancementCounters));
        Emit(working, EngineEventKinds.CardRezzed, new CardRezzedEventPayload(card.Id, 0));
        RecordOptInTurnEvent(working, TurnEventKind.IceRezzed);
        TrashCorpUniqueRezConflicts(working, card.Id);
        QueueCardRezzedOccurrence(working, GetPlayer(working, PlayerSide.Corp).Id, card.Id, zone.ServerId);
    }
    private static void ResolveSearchedDeckShuffle(WorkingState working, BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        CardBehaviorDefinition definition = GetBehaviorDefinition(working, frame);
        IReadOnlyList<BehaviorInstruction> root = frame.TriggerKey is { } trigger
            ? GetTriggerDeclaration(definition, trigger).Instructions
            : frame.AbilityKey is { } ability ? GetAbilityDeclaration(definition, ability).Instructions : definition.Instructions;
        List<BehaviorInstruction> replay = root.ToList();
        BehaviorInstruction? search = null;
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
                throw new ResolutionFailureException("Search shuffle has an unreachable continuation.");
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection)
                search = replay[step.InstructionCursor].Kind == BehaviorInstructionKind.Search ? replay[step.InstructionCursor] : null;
            if (!BehaviorContinuation.TryApply(replay, step, out _))
                throw new ResolutionFailureException("Search shuffle has an unreachable continuation.");
        }
        if (search?.SourceZone != instruction.SourceZone || search?.Target is null)
            throw new ResolutionFailureException("Search shuffle must follow a search of its named deck.");
        PlayerSide owner = search.Target.Filter?.OwnerSide ?? GetPlayer(working, frame.Controller).Side;
        // The found card remains in BehaviorSetAside. Shuffle only the remaining
        // deck, preserving its binding through reveal and the final transfer.
        ShuffleProvenEmptyZone(working, frame, instruction.SourceZone!.Value, owner);
    }

}
