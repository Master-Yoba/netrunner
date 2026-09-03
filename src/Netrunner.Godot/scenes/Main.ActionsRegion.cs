using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Owns legal-action rendering and local target staging. Engine submission
/// remains delegated to the MatchCoordinator and still carries only IDs.
/// </summary>
public partial class Main
{
	private void RenderActions(MatchSnapshot snapshot)
	{
		ActionsRegionState region = ActionsRegionPresenter.Present(snapshot);
		ClearChildren(_actions);
		UpdateTargetHighlights(region.DisplayActions);
		if (region.Status == ActionsRegionStatus.OpponentThinking)
		{
			_actions.AddChild(MakeLabel("Opponent thinking…", 13, Warning));
			return;
		}

		if (region.Status == ActionsRegionStatus.Unsupported)
		{
			_actions.AddChild(MakeWrappedLabel(
				region.Diagnostic ?? "No controls rendered for the unsupported issued decision.",
				12,
				Warning));
			return;
		}

		DecisionViewModel? decision = region.Decision;
		if (region.Status is ActionsRegionStatus.WaitingForDecision or ActionsRegionStatus.Complete)
		{
			_actions.AddChild(MakeLabel(
				region.Status == ActionsRegionStatus.Complete
					? "REPRESENTATIVE RUN COMPLETE · no further actions"
					: "Waiting for the next issued Runner decision.",
				12,
				Muted));
			return;
		}

		if (decision is null)
		{
			// The presenter only emits AwaitingRunnerDecision with a decision;
			// keep the renderer fail-closed if a future projection changes.
			_actions.AddChild(MakeLabel("Waiting for the next issued Runner decision.", 12, Muted));
			return;
		}

		if (region.IsForcedDiscard)
		{
			_actions.AddChild(MakeWrappedLabel(
				$"FORCED DISCARD · {decision.Prompt}\n"
				+ "Choose only from the discard actions issued by Core.",
				12,
				Warning));
		}

		if (region.ActionGroups.Count == 0)
		{
			_actions.AddChild(MakeWrappedLabel(
				"No supported action controls are currently issued for this decision.",
				12,
				Muted));
			return;
		}

		if (_stagedAction is { } staged && _stagedDecision is { } stagedDecision)
		{
			_smokeTargetControlsObserved |= _smokeTest;
			PanelContainer selected = MakePanel();
			VBoxContainer selectedContent = new();
			selectedContent.AddThemeConstantOverride("separation", 3);
			selected.AddChild(selectedContent);
			selectedContent.AddChild(MakeLabel("TARGET SELECTED · confirm or cancel", 12, Warning));
			selectedContent.AddChild(MakeLabel(
				$"{staged.Kind.ToString().ToUpperInvariant()} · {staged.Description}\n"
				+ "No engine action has been submitted yet.",
				11,
				Colors.White));
			HBoxContainer selectedButtons = new();
			Button confirm = new()
			{
				Text = "Confirm target",
				CustomMinimumSize = new Vector2(150, 34),
			};
			confirm.Pressed += () => ConfirmStagedAction(stagedDecision, staged);
			selectedButtons.AddChild(confirm);
			Button cancel = new()
			{
				Text = "Cancel / Back",
				CustomMinimumSize = new Vector2(150, 34),
			};
			cancel.Pressed += CancelStagedAction;
			selectedButtons.AddChild(cancel);
			selectedContent.AddChild(selectedButtons);
			_actions.AddChild(selected);
		}

		foreach (ActionGroupViewModel group in region.ActionGroups)
		{
			_actions.AddChild(MakeLabel(group.Label, 11, Accent));
			foreach (LegalActionViewModel action in group.Actions)
			{
				bool targeted = ActionsRegionPresenter.GetTargetCardId(action.Parameters) is not null
					|| action.Parameters is BreachCandidateParameters;
				Button button = new()
				{
					Text = targeted
						? $"SELECT TARGET · {action.Kind}: {action.Description}"
						: $"{action.Kind}: {action.Description}",
					Alignment = HorizontalAlignment.Left,
					Disabled = Volatile.Read(ref _submissionInFlight) != 0,
				};
				if (targeted)
				{
					button.Pressed += () => StageTargetAction(decision, action);
				}
				else
				{
					button.Pressed += () => SubmitAction(decision, action);
				}

				_actions.AddChild(button);
			}
		}
	}

	private void StageTargetAction(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (!SameDecision(decision, _currentSnapshot?.View.Decision)
			|| _currentSnapshot is null
			|| _currentSnapshot.IsOpponentThinking)
		{
			return;
		}

		_stagedDecision = decision;
		_stagedAction = action;
		UpdateTargetHighlights(_currentSnapshot.View.LegalActions);
		RenderActions(_currentSnapshot);
	}

	private void ConfirmStagedAction(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (!SameDecision(decision, _currentSnapshot?.View.Decision))
		{
			CancelStagedAction();
			return;
		}

		SubmitAction(decision, action);
	}

	private void CancelStagedAction()
	{
		_stagedAction = null;
		_stagedDecision = null;
		if (_currentSnapshot is not null)
		{
			RenderActions(_currentSnapshot);
		}
	}

	private void UpdateTargetHighlights(IReadOnlyList<LegalActionViewModel> actions)
	{
		foreach (List<TextCardControl> controls in _cardControls.Values)
		{
			foreach (TextCardControl control in controls)
			{
				control.SetTargetCandidate(false);
			}
		}

		HashSet<string> candidateIds = actions
			.Select(action => ActionsRegionPresenter.GetTargetCardId(action.Parameters))
			.Where(value => value is not null)
			.Select(value => value!)
			.ToHashSet(StringComparer.Ordinal);
		foreach (string candidateId in candidateIds)
		{
			if (!_cardControls.TryGetValue(candidateId, out List<TextCardControl>? controls))
			{
				continue;
			}

			foreach (TextCardControl control in controls)
			{
				control.SetTargetCandidate(true);
				_smokeTargetCueObserved |= _smokeTest;
			}
		}

		if (_stagedAction is { } staged
			&& ActionsRegionPresenter.GetTargetCardId(staged.Parameters) is { } selectedId
			&& _cardControls.TryGetValue(selectedId, out List<TextCardControl>? selectedControls))
		{
			foreach (TextCardControl control in selectedControls)
			{
				control.SetSelected(true);
			}
		}
	}

	private void SubmitAction(DecisionViewModel decision, LegalActionViewModel action)
	{
		if (_coordinator is null || Interlocked.Exchange(ref _submissionInFlight, 1) != 0)
		{
			return;
		}

		_ = SubmitActionAsync(new ActionSubmission(
			decision.Owner,
			new DecisionId(decision.Id),
			decision.Revision,
			action.Id));
	}

	private async Task SubmitActionAsync(ActionSubmission submission)
	{
		try
		{
			await _coordinator!.SubmitActionAsync(submission);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
		}
		finally
		{
			if (Interlocked.Exchange(ref _submissionInFlight, 0) != 0)
			{
				// The coordinator publishes the accepted snapshot before this
				// await resumes, so a frame can render the next decision while
				// the flag is still set and leave its buttons disabled. When
				// the next decision is the Runner's no later publication
				// arrives, so rebuild the action row once the flag clears.
				RefreshActionsAfterSubmission();
			}
		}
	}

	private void RefreshActionsAfterSubmission()
	{
		if (_currentSnapshot is not { } snapshot)
		{
			return;
		}

		if (_stagedAction is not null && !SameDecision(snapshot.View.Decision, _stagedDecision))
		{
			_stagedAction = null;
			_stagedDecision = null;
		}

		RenderActions(snapshot);
	}
}
