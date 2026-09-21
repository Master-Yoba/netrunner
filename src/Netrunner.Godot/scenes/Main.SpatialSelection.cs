using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private List<string>? _spatialIncrementalCards;
	private BoardSelectionSubmission? _spatialSubmission;
	private bool _spatialBatchSubmitting;

	private bool InitializeIncrementalSelection(MatchViewModel view)
	{
		if (view.CardSelection is not { } context || context.Selected.Count == 0 && !view.LegalActions.Any(action => action.Parameters is BehaviorChoiceParameters { FinalizeSelection: false })) return false;
		_spatialIncrementalCards ??= context.Selected.ToList();
		return true;
	}

	private bool TrySelectIncrementalCard(string id)
	{
		if (_spatialIncrementalCards is null || !SpatialVisible) return false;
		if (_spatialSubmission is not null || _spatialDecision is not { } decision || !CurrentSpatialDecision(decision)
			|| _currentSnapshot!.View.CardSelection is not { } context) return true;
		if (!context.Candidates.Contains(id) || context.Selected.Contains(id)) return true;
		if (!_spatialIncrementalCards.Remove(id))
		{
			if (context.Cardinality != BehaviorChoiceCardinalityKind.AnyNumber && _spatialIncrementalCards.Count >= context.Count) return true;
			_spatialIncrementalCards.Add(id);
		}
		HideCardHoverPreview();
		RenderSpatialPanel(_currentSnapshot, BoardSpatialActions.Project(_currentSnapshot.View));
		return true;
	}

	private void RenderIncrementalSelection(MatchSnapshot snapshot)
	{
		CardSelectionViewModel context = snapshot.View.CardSelection!;
		IReadOnlyList<string> selected = _spatialIncrementalCards!;
		BoardSelectionGuidance guidance = BoardSelectionPresenter.Incremental(context, selected);
		_spatialPanel!.GetNode<Label>("%Prompt").Text = guidance.Instruction + "\n"
			+ string.Join(", ", selected.Select(id => SpatialTargetLabel(new(BoardTargetKind.Card, id))))
			+ "\nClick a newly selected card to remove it. Esc resets unconfirmed choices.";
		RenderSpatialTargets(snapshot, context.Candidates.Select(id => new BoardTarget(BoardTargetKind.Card, id)).ToArray());
		if (guidance.CanConfirm)
			AddSpatialButton(guidance.ConfirmationLabel, () =>
			{
				if (!CurrentSpatialDecision(snapshot.View.Decision!) || _spatialSubmission is not null) return;
				_spatialSubmission = new(context, selected);
				AdvanceSpatialSubmission(_currentSnapshot!);
			});
		else AddPendingSpatialConfirm(guidance);
	}

	private bool AdvanceSpatialSubmission(MatchSnapshot snapshot)
	{
		if (_spatialSubmission is not { } submission) return false;
		if (snapshot.Feedback?.IsError == true)
		{
			_spatialSubmission = null;
			CloseSpatialActions();
			return false;
		}
		if (Volatile.Read(ref _submissionInFlight) != 0 || _tableAwaitingSettlement) return true;
		LegalActionViewModel? next = submission.Next(snapshot.View);
		if (submission.Error is { } error)
		{
			_spatialSubmission = null;
			CloseSpatialActions();
			_tableLastAction.Text = error;
			return false;
		}
		if (next is null) return true;
		if (submission.Finished)
		{
			_spatialSubmission = null;
			CloseSpatialActions();
		}
		try
		{
			_spatialBatchSubmitting = true;
			SubmitAction(snapshot.View.Decision!, next);
		}
		finally { _spatialBatchSubmitting = false; }
		return true;
	}
}
