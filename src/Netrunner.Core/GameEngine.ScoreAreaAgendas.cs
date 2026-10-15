namespace Netrunner.Core;

/// <summary>
/// VP-CARD-AS-AGENDA and VP-AGENDA-POINTS-MODIFIER: cards added to a score area as agendas (CR 26.03 10.1.3), and agendas
/// worth a different number of points in the Runner's score area. A score area's points are a running total, so each
/// card leaving it takes back exactly what it added.
/// </summary>
public static partial class GameEngine
{
    /// <summary>The record of a card added to a score area as an agenda, if it is one.</summary>
    private static ScoreAreaAgenda? ScoreAreaAgendaEntry(WorkingState working, CardInstanceId cardId) =>
        working.RulesState?.ScoredAsAgendas?.FirstOrDefault(entry => entry.CardId == cardId);

    /// <summary>
    /// The points a card in a side's score area adds there, if it may be forfeited: a card added as an agenda unless it
    /// can't be forfeited, or an agenda at its value in that score area.
    /// </summary>
    private static int? ForfeitablePoints(WorkingState working, CardInstanceState card, PlayerSide scoreAreaSide)
    {
        if (ScoreAreaAgendaEntry(working, card.Id) is { } converted)
        {
            return converted.CannotForfeit ? null : converted.Points;
        }

        if (working.RuntimeCardCatalog?.TryGet(card.DefinitionId, out CardRuntimeSpec? spec) != true
            || spec!.CardClass != RuntimeCardClass.Agenda)
        {
            return null;
        }

        return working.BehaviorCatalog.TryGetAgenda(card.DefinitionId, out AgendaBehaviorSpec? agenda)
            ? agenda.PointsIn(scoreAreaSide)
            : spec.AgendaPoints;
    }

    /// <summary>Forgets a card's score-area agenda record once it leaves the score area.</summary>
    private static void LeaveScoreAreaAsAgenda(WorkingState working, CardInstanceId cardId)
    {
        if (ScoreAreaAgendaEntry(working, cardId) is null)
        {
            return;
        }

        // An empty list is stored as null, so the state's bytes match one that never had any.
        SetRulesState(working, scoredAsAgendas: [.. RequireRulesState(working).ScoredAsAgendas!.Where(entry => entry.CardId != cardId)]);
    }

    /// <summary>
    /// VP-CARD-AS-AGENDA: moves the behavior's source card to the recipient's score area as an agenda worth the
    /// instruction's amount. Like a score, reaching 7 points wins at once.
    /// </summary>
    private static void AddSourceToScoreAreaAsAgenda(WorkingState working, BehaviorResolutionFrame frame, BehaviorInstruction instruction)
    {
        PlayerSide side = GetPlayer(working, ResolveBehaviorRecipient(working, frame, instruction)).Side;
        AddCardToScoreAreaAsAgenda(working, frame.SourceCardId, side, instruction.Amount,
            string.Equals(instruction.Argument, BehaviorInstruction.CannotForfeitArgument, StringComparison.Ordinal));
        AdvanceBehaviorCursor(working, frame);
        if (working.Turn.History is not null && GetPlayer(working, side).AgendaPoints >= RealGameRules.AgendaPointsToWin)
        {
            CompleteTurnGame(working, side, GameWinReason.AgendaPoints);
        }
    }

    /// <summary>VP-CARD-AS-AGENDA: moves a card to a side's score area as an agenda worth the points, and records it.</summary>
    private static void AddCardToScoreAreaAsAgenda(WorkingState working, CardInstanceId cardId, PlayerSide side, int points, bool cannotForfeit)
    {
        CardInstanceState card = GetCard(working, cardId);
        ZoneState source = ZoneForCard(working, card.Id);
        ZoneState score = GetZone(working, side == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore);
        MoveCard(working, card, source, score);
        CardInstanceState moved = GetCard(working, card.Id);
        ReplaceCard(working, new CardInstanceState(moved.Id, moved.DefinitionId, score.Id, moved.OwnerSide, faceUp: true,
            rezzed: false, advancementCounters: 0));
        SetRulesState(working, scoredAsAgendas:
            [.. (RequireRulesState(working).ScoredAsAgendas ?? []).Where(existing => existing.CardId != card.Id),
                new ScoreAreaAgenda(card.Id, points, cannotForfeit)]);
        AddAgendaPoints(working, side, points);
    }

    /// <summary>
    /// VP-SCORE-STEAL-COST: as an additional cost to score the agenda, each active card that declares it goes to the
    /// Corp's score area as an agenda (Word on the Street, for agendas installed this turn). The cost is always payable,
    /// so the Corp pays it with the score.
    /// </summary>
    private static void PayScoreCostsAsAgendas(WorkingState working, CardInstanceId scoredAgenda)
    {
        bool installedThisTurn = working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.InstalledCardIds.Contains(scoredAgenda) == true;
        foreach (CardInstanceState card in working.Cards
            .Where(card => working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
                && definition.ScoreCostAsAgenda is { } cost
                && (!cost.OnlyAgendasInstalledThisTurn || installedThisTurn)
                && RuntimeCardActivity.IsActive(card, working.Zones))
            .OrderBy(static card => card.Id.Value, StringComparer.Ordinal)
            .ToArray())
        {
            BehaviorScoreCostAsAgenda cost = working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
                ? definition.ScoreCostAsAgenda!
                : throw new ResolutionFailureException("A score cost's card lost its declaration.");
            AddCardToScoreAreaAsAgenda(working, card.Id, PlayerSide.Corp, cost.Points, cost.CannotForfeit);
        }
    }
}
