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
			_labScenarios!.Select(_labScenario);
			ResetInteractionLab();
			await TableFrames(5);
			_gameTable.StopMotion();
			RequireInteraction(!_tableActionButtons.Keys.Any(id => _currentSnapshot!.View.LegalActions
				.Any(action => action.Id == id && BoardIcebreakerPresenter.Source(action) is not null)), "Breaker actions remain in the default dock.");
			await ClickBreaker("lab-fracter");
			RequireInteraction(_icebreakerPanel?.Visible == true && _labSubmissionCount == 0
				&& _boardBreakerButtons.Count == 2, "Clicking an understrength breaker did not open only its legal boost options.");
			RequireInteraction(_tableViewport.GetGlobalRect().Encloses(_icebreakerPanel!.GetGlobalRect()),
				$"Breaker panel {_icebreakerPanel.GetGlobalRect()} escaped viewport {_tableViewport.GetGlobalRect()}.");
			RequireInteraction(!CardHoverPreviewVisible, "Hover preview covers the selected breaker controls.");
			if (OS.GetCmdlineUserArgs().Contains("--icebreaker-screenshots")) await SaveInteractionEvidence($"icebreaker-boost-{size.X}");
			DecisionViewModel oldDecision = _currentSnapshot!.View.Decision!;
			LegalActionViewModel oldPump = _currentSnapshot.View.LegalActions.Single(action => action.Parameters is RunBreakerParameters
			{ BreakerId.Value: "lab-fracter", HostedCredits: 1 });
			await ClickAbility(oldPump.Id);
			RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot!.View.Run!.RunCredits == 1
				&& _currentSnapshot.View.Runner.Credits == 6 && _icebreakerPanel.Visible
				&& _gameTable.VisibleCards["lab-fracter"].EncounterStrength == 3
				&& _icebreakerPanel.GetNode<Label>("%Strength").Text.StartsWith("Strength 3", StringComparison.Ordinal),
				"Boost did not pay the selected hosted credit exactly once and refresh the selected breaker.");
			SubmitBoardIcebreaker(oldDecision, oldPump);
			RequireInteraction(_labSubmissionCount == 1, "A stale boost submitted again.");
			RequireInteraction(_boardBreakerButtons.Count == 6, "Fresh legal break quantities/payments did not appear after boosting.");
			if (OS.GetCmdlineUserArgs().Contains("--icebreaker-screenshots")) await SaveInteractionEvidence($"icebreaker-break-{size.X}");
			await ClickBreaker("lab-decoder");
			RequireInteraction(_boardBreakerId == "lab-decoder" && _boardBreakerButtons.Count == 2,
				"Switching to an incompatible breaker retained another breaker's actions.");
			await ClickBreaker("lab-fracter");
			LegalActionViewModel breakOne = _currentSnapshot!.View.LegalActions.Single(action => action.Parameters is RunBreakParameters
			{ BreakerId.Value: "lab-fracter", Quantity: 1, HostedCredits: 0 });
			await ClickAbility(breakOne.Id);
			RequireInteraction(_labSubmissionCount == 2 && _currentSnapshot!.View.Runner.Credits == 5
				&& _currentSnapshot.View.Run!.Encounter!.Subroutines[0].Broken
				&& !_currentSnapshot.View.Run.Encounter.Subroutines[1].Broken,
				"Breaking did not pay the exact personal cost and mark only the previewed subroutine.");
			RequireInteraction(_boardBreakerButtons.Values.Any(button => button.Text.Contains("Subroutines: 2", StringComparison.Ordinal)),
				"The next break preview still points to an already broken subroutine.");
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
			RequireInteraction(!_icebreakerPanel.Visible && current.View.LegalActions.All(action => _tableActionButtons.ContainsKey(action.Id)),
				"The complete action fallback is missing or duplicates the board panel.");
			SubmitBoardIcebreaker(current.View.Decision!, current.View.LegalActions[0]);
			RequireInteraction(_labSubmissionCount == 2, "A callback from the hidden breaker panel submitted an action.");
			_tableAllActions = false;
			RenderCardTableActions(current);
			await TableFrames(3);
			LegalActionViewModel final = current.View.LegalActions.Single(action => action.Parameters is RunBreakParameters
			{ BreakerId.Value: "lab-fracter", Quantity: 1, HostedCredits: 1 });
			await ClickAbility(final.Id);
			RequireInteraction(_labSubmissionCount == 3 && _currentSnapshot!.View.Run!.Encounter!.RemainingSubroutines == 0,
				"Repeated breaker use did not finish the encounter subroutines.");
			PublishLabView(_currentSnapshot!.View with { Run = null, Decision = null, LegalActions = [] });
			RequireInteraction(!_icebreakerPanel.Visible && _boardBreakerId is null, "Encounter exit retained stale breaker controls.");
			RequireInteraction(_gameTable.VisibleCards["lab-fracter"].EncounterStrength is null, "Encounter exit retained a strength badge.");
			ResetInteractionLab();
			await TableFrames(4);
			await ClickBreaker("lab-fracter");
			LegalActionViewModel boost = _currentSnapshot!.View.LegalActions.Single(action => action.Parameters is RunBreakerParameters
			{ BreakerId.Value: "lab-fracter", HostedCredits: 0 });
			await ClickAbility(boost.Id);
			LegalActionViewModel breakTwo = _currentSnapshot!.View.LegalActions.Single(action => action.Parameters is RunBreakParameters
			{ BreakerId.Value: "lab-fracter", Quantity: 2, HostedCredits: 1 });
			await ClickAbility(breakTwo.Id);
			RequireInteraction(_labSubmissionCount == 2 && _currentSnapshot!.View.Run!.Encounter!.RemainingSubroutines == 0
				&& _currentSnapshot.View.Run.RunCredits == 1 && _currentSnapshot.View.Runner.Credits == 5,
				"A two-subroutine activation did not pay once and break both targets.");
			_inspectedCard = _currentSnapshot.View.RunnerRig[0];
			ShowInspectedCardZoom();
			await TableFrames(3);
			RequireInteraction(!_icebreakerPanel.Visible, "Breaker panel remained active behind inspection.");
			await PressModalEscapeAsync();
			await TableFrames(3);
			RequireInteraction(_icebreakerPanel.Visible, "Closing inspection lost the current breaker controls.");
		}
		GD.Print("BOARD_ICEBREAKERS PASS resolutions=2 click=true boost=true break=true payments=true repeated=true stale=true guards=true escape=true fallback=true");

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
			Button button = _boardBreakerButtons[id];
			_icebreakerPanel!.GetNode<ScrollContainer>("Content/Scroll").EnsureControlVisible(button);
			await TableFrames(2);
			await ClickTableButton(button);
		}
	}
}
