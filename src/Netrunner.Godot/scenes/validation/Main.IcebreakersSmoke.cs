using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckBoardIcebreakers()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			_labScenario = 19;
			ResetInteractionLab();
			await TableFrames(5);
			_gameTable.StopMotion();
			RequireInteraction(!_tableActionButtons.Keys.Any(id => _currentSnapshot!.View.LegalActions
				.Any(action => action.Id == id && BoardIcebreakerPresenter.Source(action) is not null)), "Breaker actions remain in the default dock.");
			RequireInteraction(_gameTable.VisibleCards["lab-fracter"].IsActionableSource
				&& !_gameTable.VisibleCards["lab-decoder"].IsActionableSource, "Encounter highlights include an incompatible breaker.");
			await ClickBreaker("lab-fracter");
			RequireInteraction(_icebreakerPanel?.Visible == true && _labSubmissionCount == 0
				&& _boardBreakerButtons.Count == 1, "Clicking an understrength breaker did not open only its legal boost options.");
			RequireInteraction(_tableViewport.GetGlobalRect().Encloses(_icebreakerPanel!.GetGlobalRect()),
				$"Breaker panel {_icebreakerPanel.GetGlobalRect()} escaped viewport {_tableViewport.GetGlobalRect()}.");
			RequireInteraction(!CardHoverPreviewVisible, "Hover preview covers the selected breaker controls.");
			if (OS.GetCmdlineUserArgs().Contains("--icebreaker-screenshots")) await SaveInteractionEvidence($"icebreaker-boost-{size.X}");
			DecisionViewModel oldDecision = _currentSnapshot!.View.Decision!;
			LegalActionViewModel oldPump = _currentSnapshot.View.LegalActions.Single(action => action.Parameters is RunBreakerParameters
			{ BreakerId.Value: "lab-fracter", HostedCredits: 1 });
			LegalActionViewModel personalPump = _currentSnapshot.View.LegalActions.Single(action => action.Parameters is RunBreakerParameters
			{ BreakerId.Value: "lab-fracter", HostedCredits: 0 });
			SubmitBoardIcebreaker(oldDecision, personalPump);
			RequireInteraction(_labSubmissionCount == 0, "A nonpreferred payment was submitted through the board.");
			await ClickAbility(oldPump.Id);
			RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot!.View.Run!.RunCredits == 1
				&& _currentSnapshot.View.Runner.Credits == 6 && _icebreakerPanel.Visible
				&& _gameTable.VisibleCards["lab-fracter"].EncounterStrength == 3
				&& _icebreakerPanel.GetNode<Label>("%Strength").Text.StartsWith("Strength 3", StringComparison.Ordinal),
				"Boost did not pay the selected hosted credit exactly once and refresh the selected breaker.");
			SubmitBoardIcebreaker(oldDecision, oldPump);
			RequireInteraction(_labSubmissionCount == 1, "A stale boost submitted again.");
			RequireInteraction(_boardBreakerButtons.Count == 1 && _boardChooseBreak?.Disabled == false, "Break selection did not become available after boosting.");

			await ClickBreaker("lab-decoder");
			RequireInteraction(_boardBreakerId is null && !_icebreakerPanel.Visible && _boardBreakerButtons.Count == 0 && _labSubmissionCount == 1,
				"An incompatible breaker opened encounter controls or spent credits.");
			await ClickBreaker("lab-fracter");
			LegalActionViewModel breakOne = _currentSnapshot!.View.LegalActions.Single(action => action.Parameters is RunBreakParameters
			{ BreakerId.Value: "lab-fracter", Quantity: 1, HostedCredits: 1, SubroutineOrdinals: [1] });
			await ClickAbility(breakOne.Id);
			RequireInteraction(_labSubmissionCount == 2 && _currentSnapshot!.View.Runner.Credits == 6
				&& _currentSnapshot.View.Run!.RunCredits == 0
				&& !_currentSnapshot.View.Run!.Encounter!.Subroutines[0].Broken
				&& _currentSnapshot.View.Run.Encounter.Subroutines[1].Broken,
				"Breaking did not use the remaining hosted credit and mark only the selected subroutine.");
			await ClickTableButton(_boardChooseBreak!);
			RequireInteraction(_boardSubroutineButtons[1].Disabled && _boardBreakerButtons.Count == 0,
				"Broken subroutines remain selectable or payment was chosen before targets.");
			await ClickTableButton(_boardSubroutineButtons[0]);
			await PressModalEscapeAsync();
			RequireInteraction(!_boardChoosingSubroutines && _boardBreakTargets.Count == 0 && _labSubmissionCount == 2,
				"Escape did not cancel target selection without spending.");
			await PressModalEscapeAsync();
			RequireInteraction(!_icebreakerPanel.Visible && _boardBreakerId is null && _labSubmissionCount == 2, "Escape did not cancel without spending.");
			await ClickBreaker("lab-fracter");
			MatchSnapshot current = _currentSnapshot!;
			RenderSnapshot(current with { IsOpponentThinking = true });
			RequireInteraction(!_icebreakerPanel.Visible, "Breaker controls stayed active while the opponent was thinking.");
			SubmitBoardIcebreaker(current.View.Decision!, current.View.LegalActions[0]);
			RequireInteraction(_labSubmissionCount == 2, "A blocked breaker action submitted.");
			RenderSnapshot(current);
			await TableFrames(3);
			_tableAwaitingSettlement = true;
			RefreshBoardIcebreaker(current);
			RequireInteraction(!_icebreakerPanel.Visible, "Breaker controls stayed active during settlement.");
			_tableAwaitingSettlement = false;
			RenderCardTableActions(current);
			await TableFrames(3);
			Button fallback = _tableActions.GetChildren().OfType<Button>().Single(button => button.Text == "All legal actions…");
			await ClickTableButton(fallback);
			RequireInteraction(!_icebreakerPanel.Visible && ActionsRegionPresenter.Present(current).Actions.All(action => _tableActionButtons.ContainsKey(action.Id))
				&& !_tableActionButtons.Keys.Any(id => current.View.LegalActions.Any(action => action.Id == id && BoardIcebreakerPresenter.Source(action) == "lab-decoder")),
				"The complete action fallback is missing or duplicates the board panel.");
			SubmitBoardIcebreaker(current.View.Decision!, current.View.LegalActions[0]);
			RequireInteraction(_labSubmissionCount == 2, "A callback from the hidden breaker panel submitted an action.");
			_tableAllActions = false;
			RenderCardTableActions(current);
			await TableFrames(3);
			LegalActionViewModel final = current.View.LegalActions.Single(action => action.Parameters is RunBreakParameters
			{ BreakerId.Value: "lab-fracter", Quantity: 2, HostedCredits: 0, SubroutineOrdinals: [0, 2] });
			await ClickAbility(final.Id);
			RequireInteraction(_labSubmissionCount == 3 && _currentSnapshot!.View.Run!.Encounter!.RemainingSubroutines == 0
				&& _currentSnapshot.View.Runner.Credits == 5 && _currentSnapshot.View.Run.RunCredits == 0,
				"Repeated breaker use did not finish the encounter subroutines.");
			PublishLabView(_currentSnapshot!.View with { Run = null, Decision = null, LegalActions = [] });
			RequireInteraction(!_icebreakerPanel.Visible && _boardBreakerId is null, "Encounter exit retained stale breaker controls.");
			RequireInteraction(_gameTable.VisibleCards["lab-fracter"].EncounterStrength is null, "Encounter exit retained a strength badge.");
			ResetInteractionLab();
			await TableFrames(4);
			await ClickBreaker("lab-fracter");
			LegalActionViewModel boost = _currentSnapshot!.View.LegalActions.Single(action => action.Parameters is RunBreakerParameters
			{ BreakerId.Value: "lab-fracter", HostedCredits: 1 });
			await ClickAbility(boost.Id);
			LegalActionViewModel breakTwo = _currentSnapshot!.View.LegalActions.Single(action => action.Parameters is RunBreakParameters
			{ BreakerId.Value: "lab-fracter", Quantity: 2, HostedCredits: 1, SubroutineOrdinals: [0, 2] });
			await ClickAbility(breakTwo.Id);
			RequireInteraction(_labSubmissionCount == 2 && _currentSnapshot!.View.Run!.Encounter!.RemainingSubroutines == 1
				&& !_currentSnapshot.View.Run.Encounter.Subroutines[1].Broken
				&& _currentSnapshot.View.Run.RunCredits == 0 && _currentSnapshot.View.Runner.Credits == 6,
				"A two-subroutine activation did not pay once and break both targets.");
			_inspectedCard = _currentSnapshot.View.RunnerRig[0];
			ShowInspectedCardZoom();
			await TableFrames(3);
			RequireInteraction(!_icebreakerPanel.Visible, "Breaker panel remained active behind inspection.");
			await PressModalEscapeAsync();
			await TableFrames(3);
			RequireInteraction(_icebreakerPanel.Visible, "Closing inspection lost the current breaker controls.");
		}
		GD.Print("BOARD_ICEBREAKERS PASS resolutions=2 click=true boost=true break=true exact_targets=true selection_no_payment=true nonadjacent=true payments=true hosted_first=true matching_only=true repeated=true stale=true guards=true escape=true fallback=true");

		async Task ClickBreaker(string id)
		{
			TableCardControl card = _gameTable.VisibleCards[id];
			Vector2 point = card.GetGlobalRect().GetCenter();
			await LabPointer(point);
			LabButton(point, true);
			LabButton(point, false);
			await TableFrames(4);
		}

		async Task ClickAbility(ActionId id)
		{
			LegalActionViewModel action = _currentSnapshot!.View.LegalActions.Single(action => action.Id == id);
			if (action.Parameters is RunBreakParameters { SubroutineOrdinals: { } targets })
			{
				int submissions = _labSubmissionCount;
				await ClickTableButton(_boardChooseBreak!);
				RequireInteraction(_boardChoosingSubroutines && _boardBreakerButtons.Count == 0, "Break did not open target selection on the ICE.");
				Button pending = _icebreakerPanel!.GetNode<VBoxContainer>("%Actions").GetChildren().OfType<Button>().Single(button => button.HasMeta("pending_break"));
				RequireInteraction(pending.Disabled && pending.Text.Contains("select 1 more", StringComparison.Ordinal)
					&& _icebreakerPanel.GetNode<Label>("%Subroutines").Text.Contains("0 selected", StringComparison.Ordinal),
					"Break selection does not explain its missing target or show pending confirmation.");
				if (OS.GetCmdlineUserArgs().Contains("--interaction-guidance-screenshots"))
					await SaveInteractionEvidence($"guidance-break-selection-{GetWindow().Size.X}");
				foreach (int ordinal in targets) await ClickTableButton(_boardSubroutineButtons[ordinal]);
				RequireInteraction(_tableViewport.GetGlobalRect().Encloses(_icebreakerPanel!.GetGlobalRect()), "Subroutine controls escaped the board viewport.");
				RequireInteraction(_boardSubroutineButtons[0].GetChild<GameSymbolLabel>(0).RenderedSymbolCount == 2,
					"Subroutine target text did not render subroutine and credit icons.");
				RequireInteraction(_boardBreakerButtons.Count == 1, "Break selection did not collapse payments to one action.");
				RequireInteraction(_boardBreakerButtons.Values.Single().Text.StartsWith("Confirm · ", StringComparison.Ordinal), "Break payment does not identify its confirmation step.");
				RequireInteraction(_labSubmissionCount == submissions && _boardBreakTargets.SetEquals(targets), "Selecting targets paid or selected the wrong set.");
				RequireInteraction(_boardSubroutineButtons.Where(pair => !targets.Contains(pair.Key)).All(pair => targets.Count < 2 || pair.Value.Disabled),
					"Selection exceeded the ability's target limit.");
				if (OS.GetCmdlineUserArgs().Contains("--icebreaker-screenshots")) await SaveInteractionEvidence($"icebreaker-break-{GetWindow().Size.X}");
			}
			Button button = _boardBreakerButtons[id];
			_icebreakerPanel!.GetNode<ScrollContainer>("Content/Scroll").EnsureControlVisible(button);
			await TableFrames(2);
			await ClickTableButton(button);
		}
	}
}
