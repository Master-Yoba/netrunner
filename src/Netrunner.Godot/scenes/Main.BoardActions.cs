using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private bool CanUseBoardActions(MatchSnapshot snapshot) => !snapshot.IsOpponentThinking && !snapshot.IsTerminal
		&& snapshot.View.Decision is { } decision && decision.OwnerSide == snapshot.View.Viewer
		&& !_fullMatchResumePaused && !_tableAwaitingSettlement && Volatile.Read(ref _submissionInFlight) == 0
		&& _stagedAction is null && !_boardChoiceVisible && _boardDrag?.IsDragging != true
		&& _gameTable.DrawMotionCount == 0
		&& _zoomOverlay?.Visible != true && _tablePileOverlay?.Visible != true && _galleryOverlay?.Visible != true;

	private void RefreshBasicBoardActions(MatchSnapshot snapshot)
	{
		bool available = CanUseBoardActions(snapshot);
		_gameTable.SetBasicActionAvailability(
			available && snapshot.View.LegalActions.Any(action => action.Kind == ActionKind.GainCredit),
			available && snapshot.View.LegalActions.Any(action => action.Kind == ActionKind.DrawCard));
	}

	private void SubmitBasicBoardAction(PlayerSide side, ActionKind kind)
	{
		if (_currentSnapshot is not { } snapshot || snapshot.View.Viewer != side || !CanUseBoardActions(snapshot)) return;
		LegalActionViewModel[] actions = snapshot.View.LegalActions.Where(action => action.Kind == kind).ToArray();
		if (actions.Length != 1 || snapshot.View.Decision is not { } decision) return;
		_gameTable.SetBasicActionAvailability(false, false);
		SubmitAction(decision, actions[0]);
	}

	private void StartBoardRunSelection()
	{
		if (_currentSnapshot is not { } snapshot || snapshot.View.Viewer != PlayerSide.Runner || !CanUseBoardActions(snapshot)) return;
		LegalActionViewModel[] runs = snapshot.View.LegalActions
			.Where(action => action.Kind == ActionKind.StartRun && action.Parameters is RunServerParameters).ToArray();
		if (runs.Length == 0) return;
		StageBoardInitialChoices(snapshot.View.Decision!, runs, runSelection: true);
		RefreshBasicBoardActions(snapshot);
	}
}
