using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Invisible priority windows (playtest, 27 September). During their own turn
/// the player holds a timing window after each action, so they can rez or score.
/// Instead of a "pass priority" step, the table shows their next turn actions
/// (previewed by the coordinator) with the window's rez and score chips. Taking a
/// turn action submits the window's issued pass first, then the matching issued
/// action of the real next decision, found by kind and parameters. If the real
/// decision has no match, the player simply sees it and chooses again.
/// </summary>
public partial class Main
{
	private readonly HashSet<ActionId> _turnPreviewActionIds = [];
	private TurnPreview? _turnPreview;
	private DecisionViewModel? _turnWindow;
	private (string Signature, string WindowId)? _turnIntent;

	/// <summary>
	/// Shows a window the player holds as their previewed turn decision: its
	/// actions plus the window's own (rez, score), without the pass. The whole
	/// table (drag, card clicks, chips) then works exactly as on a turn action.
	/// </summary>
	private MatchSnapshot WithTurnPreview(MatchSnapshot snapshot)
	{
		_turnPreviewActionIds.Clear();
		_turnPreview = null;
		_turnWindow = null;
		if (snapshot.TurnPreview is not { } preview || !snapshot.AwaitingHumanDecision || snapshot.IsTerminal
			|| snapshot.View.Decision is not { Kind: DecisionKind.TurnTiming } window) return snapshot;
		_turnPreview = preview;
		_turnWindow = window;
		foreach (LegalActionViewModel action in preview.Actions) _turnPreviewActionIds.Add(action.Id);
		IReadOnlyList<LegalActionViewModel> merged = snapshot.View.LegalActions
			.Where(action => action.Kind != ActionKind.PassPriority)
			.Concat(preview.Actions)
			.ToArray();
		return snapshot with { View = snapshot.View with { Decision = preview.Decision, LegalActions = merged } };
	}

	/// <summary>
	/// Routes a submission made against the previewed decision: the window's own
	/// actions go to the window; a previewed action passes the window first.
	/// Returns false when the decision isn't a preview.
	/// </summary>
	private bool SubmitThroughWindow(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (_turnPreview is not { } preview || _turnWindow is not { } window || !SameDecision(decision, preview.Decision)) return false;
		if (_coordinator?.CurrentSnapshot is not { } latest || !SameDecision(window, latest.View.Decision))
		{
			_tableLastAction.Text = "The decision changed. Choose from the current actions.";
			return true;
		}
		if (!_turnPreviewActionIds.Contains(action.Id))
		{
			SubmitAction(window, action);
			return true;
		}
		if (latest.View.LegalActions.FirstOrDefault(candidate => candidate.Kind == ActionKind.PassPriority) is not { } pass) return true;
		_turnIntent = (TurnSignature(action), window.Id);
		SubmitAction(window, pass);
		return true;
	}

	/// <summary>Called every frame; submits the intended action once the real turn decision arrives.</summary>
	private void ContinueTurnIntent()
	{
		if (_turnIntent is not { } intent || _currentSnapshot is not { } snapshot) return;
		if (snapshot.IsTerminal || _turnWindow is { } window && window.Id != intent.WindowId)
		{
			// Ended, or a different window came up: the player chooses again.
			_turnIntent = null;
			return;
		}
		if (_turnWindow is not null || _tableAwaitingSettlement || Volatile.Read(ref _submissionInFlight) != 0
			|| !snapshot.AwaitingHumanDecision || snapshot.View.Decision is not { } decision) return;
		_turnIntent = null;
		if (decision.Kind != DecisionKind.TurnAction) return;
		LegalActionViewModel? match = snapshot.View.LegalActions
			.FirstOrDefault(candidate => TurnSignature(candidate) == intent.Signature);
		// Already confirmed against the preview, so a targeted action isn't staged twice.
		if (match is not null) SubmitAction(decision, match);
	}

	private static string TurnSignature(LegalActionViewModel action) =>
		$"{action.Kind}:{EngineJson.Serialize(action.Parameters)}";
}
