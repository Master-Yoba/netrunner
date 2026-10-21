using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>The player's next turn action, as it would be if they passed the window they hold now.</summary>
public sealed record TurnPreview(DecisionViewModel Decision, IReadOnlyList<LegalActionViewModel> Actions);

public sealed partial class MatchCoordinator
{
    /// <summary>
    /// Invisible priority windows (U4, playtest 27 September): when the player
    /// holds a timing window during their own turn that offers a rez or a score,
    /// the client shows their next turn action alongside it, and taking one passes
    /// the window first. This previews that action decision by passing in
    /// Simulation mode, assuming the opponent passes too. Nothing authoritative
    /// changes; if the preview turns out wrong the client shows the real decision.
    /// </summary>
    private TurnPreview? PreviewNextTurnAction(GameState state)
    {
        if (_runtimeCardCatalog is not { } runtime
            || state.PendingDecision is not { Kind: DecisionKind.TurnTiming } window
            || state.GetPlayer(window.Owner).Side != _options.HumanSide
            || state.Turn.ActiveSide != _options.HumanSide
            || !window.LegalActions.Any(action => action.Kind is ActionKind.RezCard or ActionKind.ScoreAgenda)
            || window.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority) is not { } pass)
            return null;
        GameState simulated = state;
        ActionId next = pass.Id;
        for (int step = 0; step < 6; step++)
        {
            PendingDecision pending = simulated.PendingDecision!;
            ApplyResult applied = GameEngine.Apply(simulated, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, next),
                _behaviorCatalog, runtime, ApplyMode.Simulation);
            if (!applied.Accepted || applied.State.PendingDecision is not { } after) return null;
            simulated = applied.State;
            PlayerSide owner = simulated.GetPlayer(after.Owner).Side;
            if (owner == _options.HumanSide)
            {
                if (after.Kind != DecisionKind.TurnAction) return null;
                Observation observation = ObservationBuilder.Build(simulated, _options.HumanSide, _behaviorCatalog);
                MatchViewModel view = ObservationViewMapper.Build(observation, _metadataResolver, _behaviorCatalog, handSizeProjection: null);
                return view.Decision is { } decision ? new TurnPreview(decision, view.LegalActions) : null;
            }
            // Assume the opponent passes its window; anything else and the preview isn't useful.
            if (after.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority) is not { } opponentPass) return null;
            next = opponentPass.Id;
        }
        return null;
    }
}
