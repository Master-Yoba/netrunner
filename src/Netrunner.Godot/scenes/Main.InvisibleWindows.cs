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
	private (string Signature, string WindowId)? _turnIntent;

	/// <summary>Merges the previewed turn actions into a window the player holds, without its pass.</summary>
	private MatchSnapshot WithTurnPreview(MatchSnapshot snapshot)
	{
		_turnPreviewActionIds.Clear();
		_turnPreview = null;
		if (snapshot.TurnPreview is not { } preview || !snapshot.AwaitingHumanDecision || snapshot.IsTerminal
			|| snapshot.View.Decision is not { Kind: DecisionKind.TurnTiming }) return snapshot;
		_turnPreview = preview;
		foreach (LegalActionViewModel action in preview.Actions) _turnPreviewActionIds.Add(action.Id);
		IReadOnlyList<LegalActionViewModel> merged = snapshot.View.LegalActions
			.Where(action => action.Kind != ActionKind.PassPriority)
			.Concat(preview.Actions)
			.ToArray();
		return snapshot with { View = snapshot.View with { LegalActions = merged } };
	}

	/// <summary>Passes the window for a previewed action; returns false for the window's own actions.</summary>
	private bool SubmitThroughWindow(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (!_turnPreviewActionIds.Contains(action.Id)) return false;
		if (_coordinator?.CurrentSnapshot is not { } latest || !SameDecision(decision, latest.View.Decision)
			|| latest.View.LegalActions.FirstOrDefault(candidate => candidate.Kind == ActionKind.PassPriority) is not { } pass)
		{
			_tableLastAction.Text = "The decision changed. Choose from the current actions.";
			return true;
		}
		_turnIntent = (TurnSignature(action), decision.Id);
		SubmitAction(decision, pass);
		return true;
	}

	/// <summary>Called every frame; submits the intended action once the real turn decision arrives.</summary>
	private void ContinueTurnIntent()
	{
		if (_turnIntent is not { } intent || _currentSnapshot is not { } snapshot) return;
		if (snapshot.IsTerminal)
		{
			_turnIntent = null;
			return;
		}
		if (_tableAwaitingSettlement || Volatile.Read(ref _submissionInFlight) != 0 || !snapshot.AwaitingHumanDecision
			|| snapshot.View.Decision is not { } decision || decision.Id == intent.WindowId) return;
		_turnIntent = null;
		if (decision.Kind != DecisionKind.TurnAction) return;
		LegalActionViewModel? match = snapshot.View.LegalActions
			.FirstOrDefault(candidate => TurnSignature(candidate) == intent.Signature);
		if (match is null) return;
		// Already confirmed against the preview, so a targeted action isn't staged twice.
		SubmitAction(decision, match);
	}

	private static string TurnSignature(LegalActionViewModel action) =>
		$"{action.Kind}:{EngineJson.Serialize(action.Parameters)}";
}
