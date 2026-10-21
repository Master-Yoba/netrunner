namespace Netrunner.Core;

/// <summary>
/// Real CR Corp advance and score reducers. These actions intentionally live
/// beside, rather than inside, the legacy toy agenda reducers so the two
/// action contracts cannot accidentally share legality or victory rules.
/// </summary>
public static partial class GameEngine
{
    /// <summary>
    /// Re-materializes real Corp advance payloads without allocating action or
    /// decision IDs. Runtime metadata supplies agenda requirements; compiled
    /// declarations grant advancement permission to other installed cards.
    /// </summary>
    internal static IReadOnlyList<AdvanceCardParameters> MaterializeAdvanceCardParameters(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog,
        IBehaviorCatalog? behaviorCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);
        return MaterializeAdvanceCardParameters(
            WorkingState.From(state, behaviorCatalog ?? BehaviorCatalog.Empty, runtimeCardCatalog));
    }

    private static List<AdvanceCardParameters> MaterializeAdvanceCardParameters(WorkingState working)
    {
        if (!IsCorpTurnActionWindow(working)
            || working.RuntimeCardCatalog is null)
        {
            return [];
        }

        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        if (corp.Clicks < 1 || corp.Credits < 1 || working.RunState is not null)
        {
            return [];
        }

        if (HasOccupiedTransientPlayArea(working))
        {
            return [];
        }

        var materialized = new List<AdvanceCardParameters>();
        foreach ((ServerState server, ZoneState root) in PhysicalAdvanceZones(working))
        {
            // Keep the physical order of every root. Do not order by card ID:
            // root ordering is part of the action issuance contract.
            foreach (CardInstanceId cardId in root.CardIds)
            {
                CardInstanceState? card = working.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
                if (card is null
                    || card.OwnerSide != PlayerSide.Corp
                    || card.ZoneId != root.Id
                    || !working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                    || spec.Side != PlayerSide.Corp
                    || !(spec.CardClass == RuntimeCardClass.Agenda
                            && server.Kind == ServerKind.Remote && root.Kind == ZoneKind.ServerRoot
                            && !card.Rezzed && spec.AdvancementRequirement > 0
                        || spec.CardClass is RuntimeCardClass.Asset or RuntimeCardClass.Ice or RuntimeCardClass.Upgrade
                            && working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
                            && definition.CanBeAdvanced))
                {
                    continue;
                }

                // Overadvancement is legal for this basic action; the
                // requirement is authoritative evidence only.
                materialized.Add(new AdvanceCardParameters(card.Id, creditCost: 1));
            }
        }

        return materialized;
    }

    private static void AddAdvanceCardActions(WorkingState working, List<LegalAction> actions)
    {
        foreach (AdvanceCardParameters parameters in MaterializeAdvanceCardParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.AdvanceCard,
                "Advance card",
                parameters));
        }
    }

    private static void ConsumeTurnAdvanceCardAction(
        WorkingState working,
        AdvanceCardParameters parameters)
    {
        if (!IsCorpTurnActionWindow(working))
        {
            throw new ResolutionFailureException(
                "A real advance requires the active Corp turn action frame.");
        }

        IReadOnlyList<AdvanceCardParameters> materialized = MaterializeAdvanceCardParameters(working);
        AdvanceCardParameters? issued = materialized.FirstOrDefault(candidate =>
            candidate.CardId == parameters.CardId
            && candidate.CreditCost == parameters.CreditCost);
        if (issued is null)
        {
            throw new ResolutionFailureException(
                "The advance action does not match the materialized advanceable Corp cards.");
        }

        CardInstanceState card = GetCard(working, parameters.CardId);
        CardRuntimeSpec spec = working.RuntimeCardCatalog!.GetSpec(card.DefinitionId);
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);

        // The complete basic-action cost is one click plus one credit. The
        // order is intentional and is part of the typed event contract.
        SpendClick(working, corp.Id);
        ApplyCreditDelta(working, corp.Id, -1);

        int counters = checked(card.AdvancementCounters + 1);
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            card.ZoneId,
            card.OwnerSide,
            card.FaceUp,
            card.Rezzed,
            counters));
        if (spec.CardClass == RuntimeCardClass.Agenda)
        {
            Emit(working, EngineEventKinds.AgendaAdvanced, new AgendaAdvancedEventPayload(
                card.Id, counters, spec.AdvancementRequirement));
        }
        else
        {
            Emit(working, EngineEventKinds.BehaviorCardAdvanced, new BehaviorCardAdvancedEventPayload(
                card.Id, 1, counters));
        }

        StartNextTurnActionWindow(working, (TurnFrame)working.ResolutionStack[^1]);
        if (card.AdvancementCounters == 0)
            QueueIdentityOccurrence(working, OccurrenceKind.CardAdvancedFromZero, PlayerSide.Corp, card.Id);
        QueueIdentityOccurrence(working, OccurrenceKind.CardAdvanced, PlayerSide.Corp, card.Id);
    }

    /// <summary>
    /// Re-materializes score payloads for the Corp action-priority paid,
    /// rez, and score window. Scoring is deliberately not a TurnAction and
    /// does not consume allocator state here.
    /// </summary>
    internal static IReadOnlyList<ScoreAgendaParameters> MaterializeScoreAgendaParameters(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);
        return MaterializeScoreAgendaParameters(
            WorkingState.From(state, BehaviorCatalog.Empty, runtimeCardCatalog));
    }

    private static List<ScoreAgendaParameters> MaterializeScoreAgendaParameters(WorkingState working)
    {
        if (!IsCorpScoreWindow(working)
            || IsScoringProhibited(working)
            || working.RuntimeCardCatalog is null
            || working.RunState is not null)
        {
            return [];
        }

        var materialized = new List<ScoreAgendaParameters>();
        foreach ((ServerState server, ZoneState root) in PhysicalRemoteRoots(working))
        {
            foreach (CardInstanceId cardId in root.CardIds)
            {
                CardInstanceState? card = working.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
                if (card is null
                    || card.OwnerSide != PlayerSide.Corp
                    || card.ZoneId != root.Id
                    || card.Rezzed
                    || !working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                    || spec.Side != PlayerSide.Corp
                    || spec.CardClass != RuntimeCardClass.Agenda
                    || spec.AgendaPoints <= 0
                    || spec.AdvancementRequirement <= 0
                    || card.AdvancementCounters < spec.AdvancementRequirement
                    || IsCardScoreProhibited(working, card.Id))
                {
                    continue;
                }

                materialized.Add(new ScoreAgendaParameters(
                    card.Id,
                    spec.AgendaPoints,
                    spec.AdvancementRequirement));
            }
        }

        return materialized;
    }

    private static void AddScoreActions(
        WorkingState working,
        TurnFrame frame,
        List<LegalAction> actions)
    {
        if (frame.ActiveSide != PlayerSide.Corp
            || frame.Stage != TurnFrameStage.ActionPriority
            || frame.PriorityWindow != TurnPriorityWindowKind.PaidRezAndScore
            || frame.PriorityHolder != PlayerSide.Corp)
        {
            return;
        }

        foreach (ScoreAgendaParameters parameters in MaterializeScoreAgendaParameters(working))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ScoreAgenda,
                "Score agenda",
                parameters));
        }
    }

    private static void ConsumeTurnScoreAgendaAction(
        WorkingState working,
        ScoreAgendaParameters parameters)
    {
        if (!IsCorpScoreWindow(working))
        {
            throw new ResolutionFailureException(
                "Scoring requires the Corp action-priority paid, rez, and score window.");
        }

        IReadOnlyList<ScoreAgendaParameters> materialized = MaterializeScoreAgendaParameters(working);
        ScoreAgendaParameters? issued = materialized.FirstOrDefault(candidate =>
            candidate.CardId == parameters.CardId
            && candidate.AgendaPoints == parameters.AgendaPoints
            && candidate.AdvancementRequirement == parameters.AdvancementRequirement);
        if (issued is null)
        {
            throw new ResolutionFailureException(
                "The score action does not match the materialized Corp agenda set.");
        }

        // VP-SCORE-STEAL-COST: additional costs to score are paid first (Word on the Street), before the zones are read.
        PayScoreCostsAsAgendas(working, parameters.CardId);
        CardInstanceState card = GetCard(working, parameters.CardId);
        CardRuntimeSpec spec = working.RuntimeCardCatalog!.GetSpec(card.DefinitionId);
        ZoneState source = working.Zones.Single(zone => zone.Id == card.ZoneId);
        ZoneState score = GetZone(working, ZoneKind.CorpScore);
        ServerState server = working.Servers.Single(candidate => candidate.RootZoneId == source.Id);

        // Scoring never spends a click or credit. Move first so CardMoved is
        // the first evidence, then normalize the public scored card and emit
        // the typed score evidence.
        MoveCard(working, card, source, score);
        // EL-DIVIDENDS: a scored agenda keeps its advancement counters as
        // last-known information for "each excess advancement counter".
        // Only the Elevation pool reads it, so Gateway scores keep their bytes.
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            score.Id,
            PlayerSide.Corp,
            faceUp: true,
            rezzed: false,
            working.BehaviorCatalog.TracksTurnEvents ? card.AdvancementCounters : GetCard(working, card.Id).AdvancementCounters));
        AddAgendaPoints(working, PlayerSide.Corp, spec.AgendaPoints);
        Emit(working, EngineEventKinds.AgendaScored, new AgendaScoredEventPayload(
            PlayerSide.Corp,
            card.Id,
            spec.AgendaPoints));
        RecordTurnAgendaChange(
            working,
            card.Id,
            PlayerSide.Corp,
            spec.AgendaPoints,
            AgendaTurnChangeKind.Score);

        RefreshRemoteLifecycle(working, server.Id);
        RecordActiveTurnEvent(working, TurnEventKind.AgendaScoredOrStolen);
        // VP-SCORING-FACTS: recorded only by catalogs with a card that reads them, so other games keep their bytes.
        RecordOptInTurnEvent(working, TurnEventKind.AgendaScored);
        if (working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.InstalledCardIds.Contains(card.Id) != true)
        {
            RecordOptInTurnEvent(working, TurnEventKind.AgendaScoredNotInstalledThisTurn);
        }

        if (GetPlayer(working, PlayerSide.Corp).AgendaPoints >= RealGameRules.AgendaPointsToWin)
        {
            // A terminal score ends the game immediately. CompleteTurnGame
            // clears the resolution stack, so no post-score trigger marker is
            // left beneath a terminal outcome.
            CompleteTurnGame(working, PlayerSide.Corp, GameWinReason.AgendaPoints);
            return;
        }

        // A nonterminal score restarts the same Corp action-priority window;
        // the following Advance pass yields a fresh timing decision with
        // Corp as holder and zero passes.
        StartNextTurnActionWindow(working, (TurnFrame)working.ResolutionStack[^1]);
        if (HasActiveCatalogTrigger(working, OccurrenceKind.AgendaScoredOrStolen, server.Id, card.Id))
        {
            QueueAgendaScoredOrStolenOccurrence(
                working,
                GetPlayer(working, PlayerSide.Corp).Id,
                card.Id,
                server.Id);
        }
    }

    /// <summary>
    /// Opens the normal occurrence/checkpoint pipeline after a real agenda
    /// score or steal has committed its card, points, and event evidence.
    /// The caller has already advanced its parent continuation, so a yielded
    /// trigger cannot repeat the move or points change.
    /// </summary>
    private static void QueueAgendaScoredOrStolenOccurrence(
        WorkingState working,
        PlayerId controller,
        CardInstanceId agendaId,
        ServerId serverId)
    {
        working.ResolutionStack.Add(new EffectResolutionFrame(
            controller,
            new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(
                OccurrenceKind.AgendaScoredOrStolen,
                controller,
                agendaId,
                serverId))));
    }

    private static bool IsCorpTurnActionWindow(WorkingState working) =>
        working.RuntimeCardCatalog is not null
        && working.Turn.ActiveSide == PlayerSide.Corp
        && working.Turn.Phase == TurnPhase.Corp
        && working.RunState is null
        && working.ResolutionStack.Count == 1
        && working.ResolutionStack[^1] is TurnFrame
        {
            ActiveSide: PlayerSide.Corp,
            Stage: TurnFrameStage.AwaitingAction,
        } turn
        && turn.TurnNumber == working.Turn.TurnNumber
        && !HasOccupiedTransientPlayArea(working);

    private static bool IsCorpScoreWindow(WorkingState working) =>
        working.RuntimeCardCatalog is not null
        && working.Turn.ActiveSide == PlayerSide.Corp
        && working.Turn.Phase == TurnPhase.Corp
        && working.ResolutionStack.Count == 1
        && working.ResolutionStack[^1] is TurnFrame
        {
            ActiveSide: PlayerSide.Corp,
            Stage: TurnFrameStage.ActionPriority,
            PriorityWindow: TurnPriorityWindowKind.PaidRezAndScore,
            PriorityHolder: PlayerSide.Corp,
        } turn
        && turn.TurnNumber == working.Turn.TurnNumber;

    private static IEnumerable<(ServerState Server, ZoneState Root)> PhysicalAdvanceZones(WorkingState working)
    {
        // Retain historical agenda/root ordering, then append additional
        // installed locations using physical server and card order.
        foreach (var entry in PhysicalRemoteRoots(working))
        {
            yield return entry;
        }
        foreach (ServerState server in working.Servers)
        {
            foreach (ZoneState zone in working.Zones.Where(zone =>
                zone.ServerId == server.Id && zone.OwnerSide == PlayerSide.Corp
                && (zone.Id == server.IceZoneId && zone.Kind == ZoneKind.ServerIce
                    || server.Kind != ServerKind.Remote && zone.Id == server.RootZoneId && zone.Kind == ZoneKind.ServerRoot)))
            {
                yield return (server, zone);
            }
        }
    }

    private static IEnumerable<(ServerState Server, ZoneState Root)> PhysicalRemoteRoots(WorkingState working)
    {
        foreach (ServerState server in working.Servers)
        {
            if (server.Kind != ServerKind.Remote)
            {
                continue;
            }

            ZoneState? root = working.Zones.FirstOrDefault(zone =>
                zone.Id == server.RootZoneId
                && zone.Kind == ZoneKind.ServerRoot
                && zone.ServerId == server.Id
                && zone.OwnerSide == PlayerSide.Corp);
            if (root is not null)
            {
                yield return (server, root);
            }
        }
    }

}
