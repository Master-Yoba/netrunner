using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private BoardDragController? _boardDrag;

	private void BuildBoardDrag()
	{
		_boardDrag = _tableViewport.GetNode<BoardDragController>("BoardDragController");
		_boardDrag.Board = _gameTable;
		_boardDrag.CanInteract = () => _currentSnapshot is { IsOpponentThinking: false, IsTerminal: false } snapshot
			&& snapshot.View.Run is null
			&& snapshot.View.Decision is { } decision && decision.OwnerSide == snapshot.View.Viewer
			&& !_fullMatchResumePaused && !_tableAwaitingSettlement && _stagedAction is null
			&& Volatile.Read(ref _submissionInFlight) == 0
			&& _zoomOverlay?.Visible != true && _tablePileOverlay?.Visible != true;
		_boardDrag.DragStarted += () => { HideCardHoverPreview(); DismissBoardChoices(); };
		_boardDrag.GestureEnded += (outcome, cardId) => WriteFullMatchDiagnostic("drag-outcome", new
		{
			outcome,
			cardId,
			decisionId = _currentSnapshot?.View.Decision?.Id,
			revision = _currentSnapshot?.View.Decision?.Revision,
		});
		_boardDrag.Dropped += (decision, actions) =>
		{
			if (_currentSnapshot?.View.Decision is not { } current || !SameDecision(decision, current)) return;
			if (actions.Count == 1)
			{
				if (decision.Kind != DecisionKind.TurnDiscard) BeginBoardPlayResolution();
				SubmitAction(decision, actions[0]);
			}
			else if (actions.Count > 1) StageBoardPlayChoices(decision, actions);
		};
	}

	private void RefreshDragPlay(MatchSnapshot snapshot)
	{
		if (_boardDrag is null) return;
		_boardDrag.ReducedMotion = _reducedMotion.ButtonPressed;
		_boardDrag.UpdateInteraction(snapshot.View);
	}
}
