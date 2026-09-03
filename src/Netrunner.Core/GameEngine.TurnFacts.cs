namespace Netrunner.Core;

/// <summary>
/// Facts-only update boundary for the formal turn shell.  M1/M2 states and
/// standalone run fixtures have no current formal facts, so these helpers are
/// intentionally no-ops for those states.
/// </summary>
public static partial class GameEngine
{
    private static void RecordTurnRunAttempt(WorkingState working, ServerId serverId)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        if (history is null || history.ThisTurn(turn.ActiveSide) is null)
        {
            return;
        }

        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history.MarkRunAttempted(turn.ActiveSide, serverId));
    }

    private static void RecordTurnRunSuccess(WorkingState working, ServerId serverId)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        if (history is null || history.ThisTurn(turn.ActiveSide) is null)
        {
            return;
        }

        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history.MarkSuccessfulRun(turn.ActiveSide, serverId));
    }

    private static void RecordTurnAccess(WorkingState working, CardInstanceId cardId)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        if (history is null || history.ThisTurn(turn.ActiveSide) is null)
        {
            return;
        }

        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history.RecordAccess(turn.ActiveSide, cardId));
    }

    private static void RecordTurnTrash(WorkingState working, CardInstanceId cardId)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        if (history is null || history.ThisTurn(turn.ActiveSide) is null)
        {
            return;
        }

        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history.RecordTrash(turn.ActiveSide, cardId));
    }

    private static void RecordTurnAgendaChange(
        WorkingState working,
        CardInstanceId cardId,
        PlayerSide beneficiarySide,
        int points,
        AgendaTurnChangeKind kind)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        if (history is null || history.ThisTurn(turn.ActiveSide) is null)
        {
            return;
        }

        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history.RecordAgendaChange(turn.ActiveSide, cardId, beneficiarySide, points, kind));
    }

    private static void RecordTurnTagChange(WorkingState working, PlayerId affectedPlayerId, int delta)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        if (delta == 0 || history is null || history.ThisTurn(turn.ActiveSide) is null)
        {
            return;
        }

        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history.RecordTagChange(turn.ActiveSide, affectedPlayerId, delta));
    }
}
