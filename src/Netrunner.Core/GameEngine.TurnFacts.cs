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

    /// <summary>
    /// Records a completed Runner program install in the current formal turn.
    /// The active formal side owns the ledger, matching the existing
    /// installed-card/ability fact boundary; no source-card or controller
    /// inference is performed here.
    /// </summary>
    private static void RecordTurnProgramInstall(WorkingState working, CardInstanceId cardId)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        TurnFactsState? facts = history?.ThisTurn(turn.ActiveSide);
        if (facts is null
            || facts.Side != turn.ActiveSide
            || facts.TurnNumber != turn.TurnNumber)
        {
            return;
        }

        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history!.RecordProgramInstall(turn.ActiveSide, cardId));
    }

    /// <summary>
    /// Records an accepted real click-for-card action in the active formal
    /// turn. The occurrence ID is allocated before the checkpoint opens, so
    /// the first-draw fact remains authoritative while a replacement choice
    /// is pending and cannot be re-armed by a late source.
    /// </summary>
    private static void RecordTurnBasicDraw(WorkingState working, OccurrenceId occurrenceId)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        TurnFactsState? facts = history?.ThisTurn(turn.ActiveSide);
        if (facts is null
            || facts.Side != turn.ActiveSide
            || facts.TurnNumber != turn.TurnNumber
            || !facts.TurnBegan)
        {
            return;
        }

        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history!.RecordBasicDraw(turn.ActiveSide, occurrenceId));
    }

    /// <summary>
    /// Records an actual HQ breach in the current formal turn and returns
    /// whether this occurrence is the first HQ breach of that turn. The
    /// occurrence is appended even when the server has already appeared so
    /// historical repetition remains available to invariant reconstruction.
    /// </summary>
    private static bool RecordTurnHqBreach(WorkingState working, ServerId serverId)
    {
        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        TurnFactsState? facts = history?.ThisTurn(turn.ActiveSide);
        if (facts is null
            || facts.Side != turn.ActiveSide
            || facts.TurnNumber != turn.TurnNumber
            || !facts.TurnBegan)
        {
            return false;
        }

        bool first = facts.HqBreachOccurrenceServerIds.Count == 0;
        ReplaceTurn(
            working,
            turn.ActiveSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history!.RecordHqBreach(turn.ActiveSide, serverId));
        return first;
    }

    /// <summary>
    /// Consumes the FirstX source/key only after a catalog basic-draw
    /// replacement has been selected. The invocation is already detached
    /// from the source's live zone, so moving that source after materializing
    /// the checkpoint does not invalidate the accepted use.
    /// </summary>
    private static void RecordTurnFirstBasicDrawUse(
        WorkingState working,
        InterruptInvocation invocation)
    {
        if (!invocation.IsCatalogInterrupt
            || invocation.SourceCardId is not { } sourceCardId
            || invocation.ReplacementReasonKey is not { } reasonKey
            || invocation.SourceOwnerSide is not { } sourceSide)
        {
            return;
        }

        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        TurnFactsState? facts = history?.ThisTurn(sourceSide);
        if (facts is null
            || facts.Side != sourceSide
            || facts.TurnNumber != turn.TurnNumber
            || !facts.TurnBegan)
        {
            throw new ResolutionFailureException("Basic-draw replacement use requires current formal-turn facts.");
        }

        ReplaceTurn(
            working,
            sourceSide,
            turn.Phase,
            turn.TurnNumber,
            turn.Step,
            history!.ConsumeFirstX(sourceSide, sourceCardId, reasonKey));
    }

    /// <summary>
    /// Consumes one FirstX source/key for each first-HQ modifier that was
    /// snapshotted at BeginBreach. The modifier snapshot is authoritative even
    /// if a source moves before this post-boundary hook runs.
    /// </summary>
    private static void RecordTurnFirstHqBreachUses(
        WorkingState working,
        List<RunBreachAccessModifier> modifiers)
    {
        if (modifiers.Count == 0)
        {
            return;
        }

        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        TurnFactsState? facts = history?.ThisTurn(turn.ActiveSide);
        if (facts is null
            || facts.Side != turn.ActiveSide
            || facts.TurnNumber != turn.TurnNumber
            || !facts.TurnBegan)
        {
            return;
        }

        TurnHistoryState updated = history!;
        foreach (RunBreachAccessModifier modifier in modifiers)
        {
            if (modifier.SourceKind == RunBreachAccessModifierSourceKind.FirstHeadquartersBreach)
            {
                updated = updated.ConsumeFirstX(
                    turn.ActiveSide,
                    modifier.SourceCardId,
                    modifier.ReasonKey);
            }
        }

        if (!ReferenceEquals(updated, history))
        {
            ReplaceTurn(
                working,
                turn.ActiveSide,
                turn.Phase,
                turn.TurnNumber,
                turn.Step,
                updated);
        }
    }

    /// <summary>
    /// Consumes only the source declarations that were present in the
    /// accepted install quote. This runs after payment, movement, and the
    /// program-install occurrence have committed; previews, rejected/stale
    /// actions, and multi-step selections therefore never mark a source used.
    /// </summary>
    private static void RecordTurnFirstProgramInstallUses(
        WorkingState working,
        CardInstanceId installedCardId,
        CostQuote? quote)
    {
        if (quote is null)
        {
            return;
        }

        TurnState turn = working.Turn;
        TurnHistoryState? history = turn.History;
        TurnFactsState? facts = history?.ThisTurn(turn.ActiveSide);
        if (facts is null
            || facts.Side != turn.ActiveSide
            || facts.TurnNumber != turn.TurnNumber
            || !facts.TurnBegan)
        {
            return;
        }

        TurnHistoryState updated = history!;
        foreach (CostAdjustment adjustment in quote.Adjustments)
        {
            if (adjustment.Kind != BehaviorCostModifierKind.RunnerInstallCreditCost
                || adjustment.SourceCardId is not { } sourceCardId
                || adjustment.SourceDefinitionId is not { } sourceDefinitionId)
            {
                continue;
            }

            // The installed card is now in the Rig, but it was not an active
            // source when this quote was projected. A self-cost declaration on
            // the new card must never be mistaken for an installed-source use.
            if (sourceCardId == installedCardId)
            {
                continue;
            }

            CardInstanceState? source = working.Cards.FirstOrDefault(card => card.Id == sourceCardId);
            // The source may have been selected as a replacement and moved to
            // Heap before this post-commit hook runs. The accepted quote is
            // the authenticated proof that it was active when projected; do
            // not lose its first-X consumption merely because the install
            // itself removed that source.
            if (source is null)
            {
                continue;
            }

            if (source.DefinitionId != sourceDefinitionId
                || !working.BehaviorCatalog.TryGet(sourceDefinitionId, out CardBehaviorDefinition? definition)
                || definition.FirstProgramInstallCostModifiers is not { Count: > 0 } declarations)
            {
                // This is a different typed install term (for example a
                // self-cost modifier on the card just moved from Grip).
                continue;
            }

            if (!declarations.Any(declaration =>
                    declaration.Delta == adjustment.Delta
                    && string.Equals(declaration.ReasonKey, adjustment.ReasonKey, StringComparison.Ordinal)))
            {
                throw new ResolutionFailureException(
                    "The accepted first-program install quote references an invalid source declaration.");
            }

            updated = updated.ConsumeFirstX(
                turn.ActiveSide,
                sourceCardId,
                adjustment.ReasonKey);
        }

        if (!ReferenceEquals(updated, history))
        {
            ReplaceTurn(
                working,
                turn.ActiveSide,
                turn.Phase,
                turn.TurnNumber,
                turn.Step,
                updated);
        }
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
