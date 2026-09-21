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
		if (_useCardTable) { RenderCardTableActions(snapshot); return; }
		ActionsRegionState region = ActionsRegionPresenter.Present(snapshot);
		ClearChildren(_actions);
		UpdateTargetHighlights(region.DisplayActions);
		if (region.Status == ActionsRegionStatus.OpponentThinking)
		{
			_actions.AddChild(MakeLabel("Opponent thinking…", 13, Warning));
			ApplyAccessibilityFocusOrder(_board);
			return;
		}

		if (region.Status == ActionsRegionStatus.Unsupported)
		{
			_actions.AddChild(MakeWrappedLabel(
				region.Diagnostic ?? "No controls rendered for the unsupported issued decision.",
				12,
				Warning));
			ApplyAccessibilityFocusOrder(_board);
			return;
		}

		DecisionViewModel? decision = region.Decision;
		if (region.Status is ActionsRegionStatus.WaitingForDecision or ActionsRegionStatus.Complete)
		{
			_actions.AddChild(MakeLabel(
				region.Status == ActionsRegionStatus.Complete
					? "COMPLETE · no further actions"
					: "Waiting for the next issued decision.",
				12,
				Muted));
			ApplyAccessibilityFocusOrder(_board);
			return;
		}

		if (decision is null)
		{
			// The presenter only emits AwaitingRunnerDecision with a decision;
			// keep the renderer fail-closed if a future projection changes.
			_actions.AddChild(MakeLabel("Waiting for the next issued decision.", 12, Muted));
			ApplyAccessibilityFocusOrder(_board);
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
			ApplyAccessibilityFocusOrder(_board);
			return;
		}

		Slice45DecisionViewModel slice45 = Slice45PresentationProjector.Present(snapshot.View);
		Slice46DecisionViewModel slice46 = Slice46PresentationProjector.Present(snapshot);
		if (slice45.IsProminent)
		{
			_actions.AddChild(MakeLabel(
				$"CHOICE OPTIONS · {slice45.Surface.ToString().ToUpperInvariant()} · "
				+ $"ACTING PLAYER {slice45.ActingPlayer!.Value.ToString().ToUpperInvariant()}",
				11,
				Warning));
			if (slice45.Prompt is { } prompt && !string.IsNullOrWhiteSpace(prompt))
			{
				_actions.AddChild(MakeWrappedLabel(prompt, 12, Colors.White));
			}
		}
		if (slice46.HasUnlessChoice)
		{
			_actions.AddChild(MakeLabel(
				"PAYMENT OPTIONS · UNLESS PAYMENT / FALLBACK",
				11,
				Warning));
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
				$"{FindSlice46Option(slice46, staged)?.OrderedPresentationLabel
					?? FindSlice45Option(slice45, staged)?.OrderedPresentationLabel
					?? Slice46PresentationProjector.Label(staged)}\n"
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
				bool targeted = ActionsRegionPresenter.IsTargetedAction(action);
				Slice46ActionPresentationViewModel? slice46Option =
					FindSlice46Option(slice46, action);
				Slice45ActionPresentationViewModel? slice45Option =
					FindSlice45Option(slice45, action);
				string actionLabel = slice46Option?.OrderedPresentationLabel
					?? slice45Option?.OrderedPresentationLabel
					?? Slice46PresentationProjector.Label(action);
				Button button = new()
				{
					Text = targeted
						? $"SELECT TARGET · {actionLabel}"
						: actionLabel,
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
		ApplyAccessibilityFocusOrder(_board);
	}

	private static Slice45ActionPresentationViewModel? FindSlice45Option(
		Slice45DecisionViewModel decision,
		LegalActionViewModel action) =>
		decision.IsProminent
			? decision.Options.FirstOrDefault(option => option.ActionId == action.Id)
			: null;

	private static Slice46ActionPresentationViewModel? FindSlice46Option(
		Slice46DecisionViewModel decision,
		LegalActionViewModel action) =>
		decision.IsProminent
			? decision.Options.FirstOrDefault(option => option.ActionId == action.Id)
			: null;

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
		if (_spatialSubmission is not null && !_spatialBatchSubmitting) return;
		if (SubmitInteractionLab(decision, action)) return;
		if (_fullMatchResumePaused)
		{
			_status.Text = "Recovered match is paused · press Continue before choosing an action.";
			return;
		}
		if (_useCardTable && _coordinator?.CurrentSnapshot is { } latest
			&& (!SameDecision(decision, latest.View.Decision)
				|| !latest.View.LegalActions.Any(candidate => candidate.Id == action.Id)))
		{
			_tableLastAction.Text = "The decision changed. Choose from the current actions.";
			_stagedAction = null;
			_stagedDecision = null;
			return;
		}
		if (_coordinator is null || Interlocked.Exchange(ref _submissionInFlight, 1) != 0)
		{
			return;
		}

		ActionSubmission submission = new(
			decision.Owner,
			new DecisionId(decision.Id),
			decision.Revision,
			action.Id);
		RecordFullMatchSubmission(submission, action.Kind);
		_ = SubmitActionAsync(submission);
	}

	private async Task SubmitActionAsync(ActionSubmission submission)
	{
		try
		{
			MatchSubmissionResult result = await _coordinator!.SubmitActionAsync(submission);
			if (!result.Accepted && _spatialSubmission is not null)
			{
				_spatialSubmission = null;
				CloseSpatialActions();
			}
			WriteFullMatchDiagnostic("submission-result", new
			{
				accepted = result.Accepted,
				actionCount = result.Snapshot.ActionCount,
				sequence = result.Snapshot.Sequence,
				feedback = result.Feedback,
			});
		}
		catch (Exception exception)
		{
			_spatialSubmission = null;
			CloseSpatialActions();
			RecordFullMatchException(exception, "submit");
			_status.Text = $"Action failed: {exception.Message}";
			if (_useCardTable && IsInstanceValid(_tableLastAction))
			{
				_tableLastAction.Text = exception.Message;
			}
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
