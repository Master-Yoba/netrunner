namespace Netrunner.Core;

/// <summary>
/// Elevation occurrence plumbing (EL.2): typed turn events for "the first
/// time each turn" conditions, occurrences deferred to the end of the current
/// action, and last-known information for a card trashed by the Runner. All
/// of it is gated on <see cref="IBehaviorCatalog.TracksTurnEvents"/>, so
/// Gateway states keep their bytes.
/// </summary>
public static partial class GameEngine
{
    /// <summary>Records a turn event on the active side's current turn facts.</summary>
    private static void RecordActiveTurnEvent(WorkingState working, TurnEventKind kind) =>
        RecordTurnEvent(working, working.Turn.ActiveSide, kind);

    private static int ActiveTurnEventCount(WorkingState working, TurnEventKind kind) =>
        working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.EventCount(kind) ?? 0;

    /// <summary>
    /// Raises an occurrence whose trigger checkpoint opens once the current
    /// action completes (CR 9.1: conditional abilities wait for the ability
    /// that met their condition to finish resolving).
    /// </summary>
    private static void DeferOccurrence(
        WorkingState working,
        OccurrenceKind kind,
        PlayerSide controller,
        CardInstanceId? subject = null)
    {
        TurnState turn = working.Turn;
        if (!working.BehaviorCatalog.TracksTurnEvents
            || turn.History?.ThisTurn(turn.ActiveSide) is not { } facts
            || !HasActiveCatalogTrigger(working, kind))
        {
            return;
        }

        DeferredOccurrence[] deferred = (facts.DeferredOccurrences ?? [])
            .Append(new DeferredOccurrence(kind, controller, subject))
            .ToArray();
        ReplaceTurn(working, turn.ActiveSide, turn.Phase, turn.TurnNumber, turn.Step,
            turn.History.WithDeferredOccurrences(turn.ActiveSide, deferred));
    }

    /// <summary>
    /// Records a turn event and, the first time it happens this turn, defers
    /// the matching first-time occurrence.
    /// </summary>
    private static void RecordFirstTimeOccurrence(
        WorkingState working,
        TurnEventKind eventKind,
        OccurrenceKind occurrenceKind,
        PlayerSide controller,
        CardInstanceId? subject = null)
    {
        if (!working.BehaviorCatalog.TracksTurnEvents)
        {
            return;
        }

        RecordActiveTurnEvent(working, eventKind);
        if (ActiveTurnEventCount(working, eventKind) == 1)
        {
            DeferOccurrence(working, occurrenceKind, controller, subject);
        }
    }

    /// <summary>Opens the oldest deferred occurrence, if any. Returns whether one was taken.</summary>
    private static bool FlushDeferredOccurrence(WorkingState working)
    {
        TurnState turn = working.Turn;
        if (turn.History?.ThisTurn(turn.ActiveSide) is not { DeferredOccurrences: { Count: > 0 } deferred })
        {
            return false;
        }

        DeferredOccurrence next = deferred[0];
        ReplaceTurn(working, turn.ActiveSide, turn.Phase, turn.TurnNumber, turn.Step,
            turn.History.WithDeferredOccurrences(turn.ActiveSide, deferred.Skip(1).ToArray()));
        QueueIdentityOccurrence(working, next.Kind, next.Controller, next.SubjectCardId);
        return true;
    }

    /// <summary>
    /// The Runner trashed a Corp card it was accessing. Records the turn
    /// events and queues the occurrence with last-known information: whether
    /// the card was rezzed, and its server when it was installed.
    /// </summary>
    private static void QueueAccessedCardTrashedOccurrence(
        WorkingState working,
        CardInstanceId trashed,
        bool wasRezzed,
        ServerId? installedServer)
    {
        bool tracks = working.BehaviorCatalog.TracksTurnEvents;
        if (tracks)
        {
            RecordActiveTurnEvent(working, TurnEventKind.CorpCardStolenOrTrashed);
            if (installedServer is not null)
            {
                RecordActiveTurnEvent(working, TurnEventKind.InstalledCorpCardTrashed);
            }
        }

        if (!HasActiveCatalogTrigger(working, OccurrenceKind.AccessedCardTrashed, subjectCardId: trashed))
        {
            return;
        }

        PlayerId runner = GetPlayer(working, PlayerSide.Runner).Id;
        working.ResolutionStack.Add(new EffectResolutionFrame(runner, new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.AccessedCardTrashed,
                runner,
                trashed,
                tracks ? installedServer : null,
                subjectWasRezzed: tracks && wasRezzed ? true : null))));
    }

    /// <summary>
    /// A card's own "when the Runner trashes this" ability: the card is in
    /// Archives, and its ability was active (it was rezzed) when trashed.
    /// </summary>
    private static bool IsTrashedSelfTriggerSource(
        WorkingState working,
        CardInstanceState source,
        BehaviorTrigger trigger,
        CardInstanceId? subjectCardId) =>
        trigger.Occurrence == OccurrenceKind.AccessedCardTrashed
        && trigger.Scope == BehaviorTriggerScope.Self
        && subjectCardId == source.Id
        && source.OwnerSide == PlayerSide.Corp
        && working.Zones.Any(zone => zone.Id == source.ZoneId && zone.Kind == ZoneKind.CorpDiscard);

    /// <summary>
    /// EL-RUN-EVENT-RUN-END: "when that run ends" on a run event, which the
    /// run's end has just trashed to the heap (Charm Offensive).
    /// </summary>
    private static bool IsEndedRunEventSource(
        WorkingState working,
        CardInstanceState source,
        BehaviorTrigger trigger,
        CardInstanceId? subjectCardId) =>
        trigger.Occurrence == OccurrenceKind.RunEnded
        && trigger.Scope == BehaviorTriggerScope.Self
        && subjectCardId == source.Id
        && source.OwnerSide == PlayerSide.Runner
        && working.Zones.Any(zone => zone.Id == source.ZoneId && zone.Kind == ZoneKind.RunnerHeap)
        && working.BehaviorCatalog.TryGetRunEvent(source.DefinitionId, out _);

    /// <summary>Whether a trashed-card occurrence records that its subject was rezzed.</summary>
    private static bool SubjectWasRezzed(OccurrenceSnapshot occurrence) =>
        occurrence.Effect is GameplayOccurrenceEffect { SubjectWasRezzed: true };

    /// <summary>
    /// CR 6.9.1: the run has begun. Records the turn event and, when a
    /// trigger waits on it, opens its checkpoint before the initiation window.
    /// Returns whether an occurrence frame was queued.
    /// </summary>
    private static bool QueueRunBeganOccurrence(WorkingState working, RunState run)
    {
        if (!working.BehaviorCatalog.TracksTurnEvents)
        {
            return false;
        }

        RecordActiveTurnEvent(working, TurnEventKind.RunBegan);
        if (!HasActiveCatalogTrigger(working, OccurrenceKind.RunBegan, run.AttackedServer))
        {
            return false;
        }

        working.ResolutionStack.Add(new EffectResolutionFrame(run.InitiatingPlayer, new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.RunBegan,
                run.InitiatingPlayer,
                serverId: run.AttackedServer))));
        return true;
    }

    private const string FlippedCounter = "flipped";

    private const string CardScoreProhibitionKey = "card-score-prohibited";

    /// <summary>EL-FLIP: the face a double-sided card shows; a hosted "flipped" marker means its back.</summary>
    private static BehaviorFace SourceFace(WorkingState working, CardInstanceId cardId) =>
        CurrentCounter(working, cardId, FlippedCounter) > 0 ? BehaviorFace.Back : BehaviorFace.Front;

    private static void FlipSource(WorkingState working, CardInstanceId cardId) =>
        AdjustCounter(working, cardId, FlippedCounter, SourceFace(working, cardId) == BehaviorFace.Back ? -1 : 1);

    /// <summary>"You get +N allotted clicks for your next turn", kept on the side's latest turn facts.</summary>
    private static void GrantNextTurnClicks(WorkingState working, PlayerSide side, int amount)
    {
        TurnState turn = working.Turn;
        if (amount <= 0 || turn.History?.ThisTurn(side) is not { } facts)
        {
            return;
        }

        ReplaceTurn(working, turn.ActiveSide, turn.Phase, turn.TurnNumber, turn.Step,
            turn.History.WithNextTurnExtraClicks(side, checked(facts.NextTurnExtraClicks + amount)));
    }

    /// <summary>"You cannot score that card this turn."</summary>
    private static void MarkCardScoreProhibition(WorkingState working, CardInstanceId cardId)
    {
        TurnState turn = working.Turn;
        if (turn.History?.ThisTurn(turn.ActiveSide) is { } facts && !facts.HasUsedAbility(cardId, CardScoreProhibitionKey))
        {
            ReplaceTurn(working, turn.ActiveSide, turn.Phase, turn.TurnNumber, turn.Step,
                turn.History.MarkAbilityUsed(turn.ActiveSide, cardId, CardScoreProhibitionKey));
        }
    }

    private static bool IsCardScoreProhibited(WorkingState working, CardInstanceId cardId) =>
        working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.HasUsedAbility(cardId, CardScoreProhibitionKey) == true;

    /// <summary>
    /// Credits gained through an ability on an agenda or operation (The
    /// Zwicky Group): recorded, and its first time each turn deferred.
    /// </summary>
    private static void RecordCardCreditGain(
        WorkingState working,
        BehaviorResolutionFrame frame,
        PlayerId gainer,
        int amount)
    {
        if (amount <= 0
            || !working.BehaviorCatalog.TracksTurnEvents
            || GetPlayer(working, gainer).Side != PlayerSide.Corp
            || working.RuntimeCardCatalog?.TryGet(frame.DefinitionId, out CardRuntimeSpec? spec) != true
            || spec!.CardClass is not (RuntimeCardClass.Agenda or RuntimeCardClass.Operation))
        {
            return;
        }

        RecordFirstTimeOccurrence(
            working,
            TurnEventKind.CardCreditGain,
            OccurrenceKind.FirstCardCreditGainThisTurn,
            PlayerSide.Corp,
            frame.SourceCardId);
    }

    /// <summary>EL-FORFEIT: forfeits the chosen agenda from its controller's score area.</summary>
    /// <summary>
    /// EL-ACCESS-OUTSIDE-RUN: the Runner steals an accessed agenda outside a run
    /// (Detente). Returns whether the steal ended the game.
    /// </summary>
    private static bool StealAgendaOutsideRun(WorkingState working, CardInstanceId agendaId)
    {
        CardInstanceState card = GetCard(working, agendaId);
        if (card.OwnerSide != PlayerSide.Corp
            || !working.BehaviorCatalog.TryGetAgenda(card.DefinitionId, out AgendaBehaviorSpec? spec))
        {
            return false;
        }

        ZoneState score = GetZone(working, ZoneKind.RunnerScore);
        MoveCard(working, card, ZoneForCard(working, card.Id), score);
        CardInstanceState stolen = GetCard(working, card.Id);
        ReplaceCard(working, new CardInstanceState(stolen.Id, stolen.DefinitionId, score.Id, stolen.OwnerSide,
            true, false, stolen.AdvancementCounters));
        AddAgendaPoints(working, PlayerSide.Runner, spec.AgendaPoints);
        Emit(working, EngineEventKinds.AgendaStolen, new AgendaStolenEventPayload(PlayerSide.Runner, card.Id, spec.AgendaPoints));
        RecordTurnAgendaChange(working, card.Id, PlayerSide.Runner, spec.AgendaPoints, AgendaTurnChangeKind.Steal);
        RecordActiveTurnEvent(working, TurnEventKind.AgendaScoredOrStolen);
        RecordActiveTurnEvent(working, TurnEventKind.CorpCardStolenOrTrashed);
        if (GetPlayer(working, PlayerSide.Runner).AgendaPoints < RealGameRules.AgendaPointsToWin)
        {
            return false;
        }

        CompleteTurnGame(working, PlayerSide.Runner, GameWinReason.AgendaPoints);
        working.RunState = null;
        return true;
    }

    /// <summary>
    /// The occurrence of an agenda stolen outside a run (Detente's HQ access), with HQ as its server, so agenda
    /// scored-or-stolen triggers see it like a breach steal.
    /// </summary>
    private static void QueueAgendaStolenOutsideRunOccurrence(WorkingState working, CardInstanceId agendaId)
    {
        if (!HasActiveCatalogTrigger(working, OccurrenceKind.AgendaScoredOrStolen))
        {
            return;
        }

        ServerState hq = working.Servers.Single(server => server.Kind == ServerKind.Headquarters);
        QueueAgendaScoredOrStolenOccurrence(working, GetPlayer(working, PlayerSide.Runner).Id, agendaId, hq.Id);
    }

    /// <summary>EL-FORFEIT: removes an agenda in its controller's score area from the game, with its points.</summary>
    private static void ForfeitScoredAgenda(WorkingState working, PlayerSide controller, CardInstanceId agendaId)
    {
        CardInstanceState agenda = GetCard(working, agendaId);
        ZoneState source = ZoneForCard(working, agendaId);
        if (source.Kind != (controller == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore)
            || working.RuntimeCardCatalog?.TryGet(agenda.DefinitionId, out CardRuntimeSpec? spec) != true
            || spec!.CardClass != RuntimeCardClass.Agenda)
        {
            throw new ResolutionFailureException("Only an agenda in its controller's score area can be forfeited.");
        }

        MoveCard(working, agenda, source, GetBehaviorZone(working, BehaviorZone.RemovedFromGame, agenda.OwnerSide));
        AddAgendaPoints(working, controller, -spec.AgendaPoints);
        DeferOccurrence(working, OccurrenceKind.AgendaForfeited, controller, agenda.Id);
    }

    private static void ResolveBehaviorForfeit(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        IReadOnlyList<CardInstanceId> chosen = ResolveBehaviorCards(working, frame, instruction.Target);
        if (chosen.Count == 0)
        {
            AdvanceBehaviorCursor(working, frame);
            return;
        }

        if (chosen.Count != 1)
        {
            throw new ResolutionFailureException("A forfeit names exactly one agenda.");
        }

        PlayerSide controller = GetPlayer(working, frame.Controller).Side;
        CardInstanceState agenda = GetCard(working, chosen[0]);
        ZoneState source = ZoneForCard(working, agenda.Id);
        ZoneKind origin = frame.SetAside.SingleOrDefault(selected => selected.CardId == agenda.Id) is { } frozen
            ? working.Zones.Single(zone => zone.Id == frozen.OriginalZoneId).Kind
            : source.Kind;
        if (origin != (controller == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore)
            || working.RuntimeCardCatalog?.TryGet(agenda.DefinitionId, out CardRuntimeSpec? spec) != true
            || spec!.CardClass != RuntimeCardClass.Agenda)
        {
            throw new ResolutionFailureException("Only an agenda in its controller's score area can be forfeited.");
        }

        ResolveBehaviorTransfer(
            working,
            frame,
            new BehaviorInstruction(
                BehaviorInstructionKind.MoveCard,
                target: instruction.Target,
                sourceZone: BehaviorZone.ScoreArea,
                destinationZone: BehaviorZone.RemovedFromGame),
            BehaviorTransferKind.Move);
        AddAgendaPoints(working, controller, -spec.AgendaPoints);
        DeferOccurrence(working, OccurrenceKind.AgendaForfeited, controller, agenda.Id);
    }

    /// <summary>
    /// EL-SABOTAGE: the Corp trashes the amount in cards: the HQ cards chosen
    /// by the preceding choice, then the rest from the top of R&amp;D.
    /// </summary>
    private static void ResolveBehaviorSabotage(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        IReadOnlyList<CardInstanceId> fromHq = instruction.Target is null
            ? []
            : ResolveBehaviorCards(working, frame, instruction.Target);
        if (fromHq.Count > instruction.Amount)
        {
            throw new ResolutionFailureException("Sabotage trashes at most its amount in cards chosen from HQ.");
        }

        if (fromHq.Count > 0)
        {
            DeferOccurrence(working, OccurrenceKind.CardsTrashedFromHq, PlayerSide.Corp);
            ResolveBehaviorTransfer(
                working,
                frame,
                new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: instruction.Target),
                BehaviorTransferKind.Trash);
        }
        else
        {
            AdvanceBehaviorCursor(working, frame);
        }

        for (int remaining = instruction.Amount - fromHq.Count; remaining > 0; remaining--)
        {
            ZoneState deck = GetZone(working, ZoneKind.CorpDeck);
            if (deck.CardIds.Count == 0)
            {
                break;
            }

            TrashCard(working, GetCard(working, deck.CardIds[0]), deck, GetZone(working, ZoneKind.CorpDiscard));
        }
    }

    /// <summary>Moves the targeted card to its owner's deck, on top.</summary>
    private static void ResolveBehaviorPlaceOnTopOfDeck(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        IReadOnlyList<CardInstanceId> cards = ResolveBehaviorCards(working, frame, instruction.Target);
        ResolveBehaviorTransfer(
            working,
            frame,
            new BehaviorInstruction(BehaviorInstructionKind.MoveCard, target: instruction.Target, destinationZone: BehaviorZone.Deck),
            BehaviorTransferKind.Move);
        foreach (CardInstanceId cardId in cards)
        {
            ZoneState deck = ZoneForCard(working, cardId);
            ReplaceZone(working, new ZoneState(deck.Id, deck.Kind, deck.OwnerSide, deck.ServerId,
                deck.CardIds.Where(candidate => candidate != cardId).Prepend(cardId).ToArray()));
        }
    }

    /// <summary>"Access N additional cards" in the coming breach of the attacked server.</summary>
    private static void AddBehaviorBreachAccess(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        RunState run = working.RunState
            ?? throw new ResolutionFailureException("Additional access requires a live run.");
        if (run.Step != RunTimingStep.BreachBoundary)
        {
            throw new ResolutionFailureException("Additional access is granted only before the breach begins.");
        }

        RunBreachAccessModifier modifier = new(
            frame.SourceCardId,
            run.AttackedServer,
            instruction.Amount,
            instruction.Argument ?? throw new ResolutionFailureException("Additional access needs a reason key."),
            RunBreachAccessModifierSourceKind.BehaviorEffect);
        working.RunState = run.WithBreachAccessModifiers((run.BreachAccessModifiers ?? []).Append(modifier).ToArray());
    }
}
