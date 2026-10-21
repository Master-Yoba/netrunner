using Netrunner.Core;
using Netrunner.Match.Regret;

namespace Netrunner.Match.Puzzles;

/// <summary>A situation to look for when writing puzzles (bot plan S2.2). Every condition given must hold.</summary>
/// <param name="TurnStart">The side's first turn action of its turn (all its clicks left).</param>
/// <param name="Hand">Card definitions all in the side's hand.</param>
/// <param name="Installed">Card definitions all installed by the side.</param>
/// <param name="Legal">Actions of which each must match at least one legal action.</param>
public sealed record PuzzleSituation(PlayerSide Side, bool TurnStart = false, IReadOnlyList<string>? Hand = null,
    IReadOnlyList<string>? Installed = null, IReadOnlyList<ActionMatch>? Legal = null);

/// <summary>Finds the decisions of games where a situation holds: the authoring aid behind <c>ai-puzzles find</c>.</summary>
public static class PuzzleFinder
{
    public static IEnumerable<RegretPosition> Find(RegretGame game, PuzzleSituation situation)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(situation);
        FullMatchDefinition definition = RegretGames.Definition(game);
        foreach (RegretPosition position in RegretGames.Positions(game, definition))
            if (Holds(position.State, situation)) yield return position;
    }

    public static bool Holds(GameState state, PuzzleSituation situation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(situation);
        PendingDecision pending = state.PendingDecision!;
        PlayerSide side = state.GetPlayer(pending.Owner).Side;
        if (side != situation.Side) return false;
        PlayerState player = state.Players.Single(entry => entry.Side == side);
        if (situation.TurnStart && (pending.Kind != DecisionKind.TurnAction || state.RunState is not null || state.Turn.ActiveSide != side
            || player.Clicks < (side == PlayerSide.Corp ? 3 : 4))) return false;
        ZoneKind hand = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        bool In(string definition, Func<ZoneKind, bool> zone) => state.Cards.Any(card => card.DefinitionId.Value == definition
            && card.OwnerSide == side && zone(state.GetZone(card.ZoneId).Kind));
        return (situation.Hand ?? []).All(definition => In(definition, kind => kind == hand))
            && (situation.Installed ?? []).All(definition => In(definition, kind => kind is ZoneKind.ServerIce or ZoneKind.ServerRoot or ZoneKind.RunnerRig))
            && (situation.Legal ?? []).All(match => pending.LegalActions.Any(action => match.Matches(state, action)));
    }
}
