using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckLabScenarioPicker()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (PlayerSide side in new[] { PlayerSide.Runner, PlayerSide.Corp })
			{
				_labSides!.EmitSignal(OptionButton.SignalName.ItemSelected, (long)_labSides.GetItemIndex((int)side));
				await TableFrames(3);
				RequireInteraction(_currentSnapshot!.View.Viewer == side && _labSubmissionCount == 0,
					"Switching practice side did not reset to that side's board.");
				int[] ids = Enumerable.Range(0, _labScenarios!.ItemCount).Select(_labScenarios.GetItemId).ToArray();
				RequireInteraction(ids.Distinct().Count() == ids.Length, "Practice menu contains duplicate scenarios.");
				foreach (int id in ids)
				{
					SelectLabScenarioItem(id);
					await TableFrames(2);
					RequireInteraction(_labScenario == id && _currentSnapshot!.View.Viewer == side
						&& _labScenarios.GetSelectedId() == id && _labInstructions!.Text == InteractionLabSamples.Instructions[id],
						"Reordered scenario selection opened the wrong fixture or crossed sides.");
				}
				SelectLabScenarioItem(ids[0]);
				await TableFrames(3);
				if (OS.GetCmdlineUserArgs().Contains("--lab-selector-screenshots"))
				{
					// Embed the popup so the viewport screenshot includes the full selector.
					bool embedded = GetViewport().GuiEmbedSubwindows;
					try
					{
						GetViewport().GuiEmbedSubwindows = true;
						_labScenarios.ShowPopup();
						await SaveInteractionEvidence($"lab-selector-{side.ToString().ToLowerInvariant()}-{size.X}");
					}
					finally
					{
						_labScenarios.GetPopup().Hide();
						GetViewport().GuiEmbedSubwindows = embedded;
					}
				}
				MatchViewModel before = _currentSnapshot!.View;
				int credits = (side == PlayerSide.Corp ? before.Corp : before.Runner).Credits;
				BoardInteractionSource source = BoardInteractionProjector.Project(before).Sources[0];
				await DragLabPracticeCard(source, source.Destinations.Single());
				MatchViewModel after = _currentSnapshot!.View;
				RequireInteraction(_labSubmissionCount == 1
					&& (side == PlayerSide.Corp ? after.CorpDiscard : after.RunnerHeap).Any(card => card.Id == source.CardId)
					&& !(side == PlayerSide.Corp ? after.CorpHand : after.RunnerHand).VisibleCards.Any(card => card.Id == source.CardId)
					&& (side == PlayerSide.Corp ? after.Corp : after.Runner).Credits == credits - 3
					&& after.LegalActions.Any(action => action.Kind == ActionKind.GainCredit)
					&& !after.LegalActions.Any(action => action.Parameters is PlayCardParameters play && play.CardId.Value == source.CardId),
					"Board basics lost event/operation play or retained its stale action.");
			}
			foreach (bool newRemote in new[] { false, true })
			{
				SelectLabScenarioItem(5);
				await TableFrames(3);
				BoardInteractionSource source = BoardInteractionProjector.Project(_currentSnapshot!.View).Sources.Single();
				BoardInteractionDestination destination = source.Destinations.First(target => target.Kind ==
					(newRemote ? BoardInteractionDestinationKind.NewRemote : BoardInteractionDestinationKind.ServerRoot));
				await DragLabPracticeCard(source, destination);
				RequireInteraction(_labSubmissionCount == 1 && _currentSnapshot!.View.Servers.Any(server =>
					server.Id == (newRemote ? "lab-new-remote" : destination.TargetId) && server.Root.Any(card => card?.Id == source.CardId)),
					"Combined Corp installation lost an existing-server or new-remote destination.");
			}
		}
		GD.Print("LAB_SCENARIOS PASS sides=2 resolutions=2 grouped=true stable_ids=true merged_play=true merged_install=true");
	}

	private async Task CheckHostedVirusCounters()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			_labSides!.EmitSignal(OptionButton.SignalName.ItemSelected, (long)_labSides.GetItemIndex((int)PlayerSide.Runner));
			SelectLabScenarioItem(InteractionLabSamples.HostedVirusScenario);
			await TableFrames(5);
			MatchViewModel original = _currentSnapshot!.View;
			CardViewModel virus = original.RunnerRig.Single();
			RequireInteraction(virus.HostCardId == original.Run!.Encounter!.Ice!.Id
				&& _gameTable.VisibleCards.ContainsKey(virus.Id) && _gameTable.VisibleCards[virus.Id].IsActionableSource,
				"Hosted virus practice did not expose its attached card as an actionable source.");
			_gameTable.VisibleCards[virus.Id].GrabFocus();
			await TableFrames(3);
			RequireInteraction(CardHoverPreviewVisible && _cardHoverPreviewCounters!.Text.Contains("VIRUS ×2", StringComparison.Ordinal)
				&& _cardHoverPreviewStatus!.Text.Contains("Hosted on Practice Barrier", StringComparison.Ordinal),
				"Hosted virus preview lost its counters or host.");
			await ClickSpatialCard(virus.Id);
			RequireInteraction(_icebreakerPanel?.Visible == true && _boardChooseBreak is { Disabled: false }
				&& _icebreakerPanel.GetNode<Label>("%Hint").Text.Contains("Hosted virus counters: 2", StringComparison.Ordinal),
				"Hosted virus controls advertised credit costs or required icebreaker strength.");
			await ClickTableButton(_boardChooseBreak!);
			await ClickTableButton(_boardSubroutineButtons[2]);
			RequireInteraction(_labSubmissionCount == 0 && ReferenceEquals(original, _currentSnapshot!.View),
				"Selecting a hosted-virus target spent a counter before confirmation.");
			await PressModalEscapeAsync();
			RequireInteraction(_labSubmissionCount == 0 && _boardBreakTargets.Count == 0 && ReferenceEquals(original, _currentSnapshot!.View),
				"Cancelling a hosted-virus break spent a counter or retained its target.");
			foreach (int ordinal in new[] { 2, 0 })
			{
				await ClickSpatialCard(virus.Id);
				await ClickTableButton(_boardChooseBreak!);
				await ClickTableButton(_boardSubroutineButtons[ordinal]);
				Button confirm = _boardBreakerButtons.Values.Single();
				RequireInteraction(confirm.Text.Contains("1 hosted virus counter", StringComparison.Ordinal)
					&& _boardSubroutineButtons.Where(pair => pair.Key != ordinal).All(pair => pair.Value.Disabled),
					"Hosted-virus confirmation lost its counter cost or allowed more than one target.");
				if (OS.GetCmdlineUserArgs().Contains("--hosted-virus-screenshots") && ordinal == 2)
					await SaveInteractionEvidence($"hosted-virus-{size.X}");
				await ClickTableButton(confirm);
				await TableFrames(3);
				MatchViewModel after = _currentSnapshot!.View;
				int remaining = 2 - _labSubmissionCount;
				RequireInteraction(after.RunnerRig.Single().TypedCounters.Sum(counter => counter.Count) == remaining
					&& after.Run!.Encounter!.Subroutines[ordinal].Broken
					&& after.Run.Encounter.Subroutines.Count(subroutine => subroutine.Broken) == _labSubmissionCount
					&& !after.Run.Encounter.Subroutines[1].Broken
					&& after.Runner == original.Runner && after.Run.RunCredits == original.Run!.RunCredits,
					"Hosted-virus break spent credits, failed to consume exactly one counter, or broke the wrong subroutine.");
				Label counterChip = _gameTable.VisibleCards[virus.Id].GetNode<Label>("LayoutHost/CompactLayout/Counters");
				RequireInteraction(counterChip.Text == (remaining > 0 ? $"VIR×{remaining}" : "") && counterChip.Visible == (remaining > 0),
					"The hosted card's displayed counter chip did not follow counter depletion.");
			}
			SubmitInteractionLab(original.Decision!, original.LegalActions[0]);
			RequireInteraction(_labSubmissionCount == 2 && _currentSnapshot!.View.LegalActions.All(action => action.Kind != ActionKind.BreakSubroutines)
				&& !_gameTable.VisibleCards[virus.Id].IsActionableSource && _icebreakerPanel?.Visible != true,
				"An exhausted virus retained a break action or accepted a stale counter payment.");
			ResetInteractionLab();
			await TableFrames(3);
			RequireInteraction(_labSubmissionCount == 0 && _currentSnapshot!.View.RunnerRig.Single().TypedCounters.Single().Count == 2
				&& _currentSnapshot.View.Run!.Encounter!.RemainingSubroutines == 3,
				"Reset did not restore the virus counters and unbroken subroutines.");
		}
		GD.Print("HOSTED_VIRUS PASS resolutions=2 hosted=true counters=true exact_targets=true no_credit_cost=true escape=true depleted=true stale=true reset=true");
	}

	private void SelectLabScenarioItem(int id) =>
		_labScenarios!.EmitSignal(OptionButton.SignalName.ItemSelected, (long)_labScenarios.GetItemIndex(id));

	private async Task DragLabPracticeCard(BoardInteractionSource source, BoardInteractionDestination destination)
	{
		Vector2 start = _gameTable.VisibleCards[source.CardId].GetGlobalTransform() * new Vector2(12, 45);
		await LabPointer(start); LabButton(start, true);
		await LabPointer(start + new Vector2(0, -40));
		Rect2? rect = _gameTable.GetDropRect(destination.Kind.ToString(), destination.TargetId);
		for (int scroll = 0; rect is null && scroll < 100; scroll++)
		{
			Rect2 board = _gameTable.GetGlobalRect();
			_gameTable.ScrollDragEdges(new Vector2(board.End.X - 12, board.Position.Y + 180), .05, 60, 1500);
			await TableFrames(1);
			rect = _gameTable.GetDropRect(destination.Kind.ToString(), destination.TargetId);
		}
		RequireInteraction(_boardDrag!.IsDragging && rect is not null, "Merged practice action has no draggable destination.");
		Vector2 drop = destination.Kind == BoardInteractionDestinationKind.GeneralPlay
			? _gameTable.GetDropRect("RunnerRig", null)!.Value.GetCenter() : rect!.Value.GetCenter();
		await LabPointer(drop); LabButton(drop, false);
		await TableFrames(4);
	}
}
