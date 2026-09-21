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
		RenderSpatialPanel(_currentSnapshot, BoardSpatialActions.Project(_currentSnapshot.View));
		return true;
	}

	private void RenderIncrementalSelection(MatchSnapshot snapshot)
	{
		ObservationCardSelection context = snapshot.View.CardSelection!;
		_spatialPanel!.GetNode<Label>("%Prompt").Text = $"Selected {_spatialIncrementalCards!.Count} · {context.Cardinality} {context.Count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "cards"}\n"
			+ string.Join(", ", _spatialIncrementalCards.Select(id => SpatialTargetLabel(new(BoardTargetKind.Card, id))))
			+ "\nConfirm the complete selection. Esc clears unsubmitted choices.";
		RenderSpatialTargets(snapshot, context.Candidates.Select(id => new BoardTarget(BoardTargetKind.Card, id)).ToArray());
		if (BoardSelectionSubmission.CanConfirm(context, _spatialIncrementalCards))
			AddSpatialButton($"Confirm selection · {_spatialIncrementalCards.Count} cards", () =>
			{
				if (!CurrentSpatialDecision(snapshot.View.Decision!) || _spatialSubmission is not null) return;
				_spatialSubmission = new(context, _spatialIncrementalCards.ToArray());
				AdvanceSpatialSubmission(_currentSnapshot!);
			});
		AddSpatialButton("Reset selection · Esc", CancelSpatialSelection);
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
