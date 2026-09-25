using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckCompactBoardLayout()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (PlayerSide side in new[] { PlayerSide.Runner, PlayerSide.Corp })
			{
				_labScenario = side == PlayerSide.Runner ? 15 : 16;
				ResetInteractionLab();
				foreach (bool dense in new[] { false, true })
				{
					MatchViewModel view = UiPreviewSamples.Board(side, dense ? UiPreviewSamples.Density.Dense : UiPreviewSamples.Density.Normal);
					string hostId = view.Servers.First(server => server.Kind == ServerKind.RAndD).Ice[0]!.Id;
					CardViewModel[] viruses = Enumerable.Range(0, dense ? 3 : 1).Select(index =>
						UiPreviewSamples.Known("fit-virus-" + index, "Program", "Practice virus") with
						{ HostCardId = hostId, TypedCounters = [new("virus", CardCounterPurpose.Virus, 2, null)] }).ToArray();
					PublishLabView(view with { RunnerRig = [.. view.RunnerRig, .. viruses] });
					await TableFrames(8);
					_gameTable.StopMotion();
					AssertServerNetworkFits();
					AssertBoardCardAlignment(_currentSnapshot!.View);
					Control summary = _gameTable.GetNode<Control>("Surface/Margins/Layout/ServerField/Content/Summary");
					Control dock = _tableViewport.GetNode<Control>("DecisionDock");
					RequireInteraction(summary.Size.X < 220 && summary.Size.Y < 220 && dock.Size.Y < 180,
						"Compact Corp summary or action dock regressed to an oversized pane.");
					if (OS.GetCmdlineUserArgs().Contains("--compact-layout-screenshots"))
						await SaveInteractionEvidence($"compact-{side.ToString().ToLowerInvariant()}-{(dense ? "dense" : "normal")}-{size.X}");
					_matchHistory.SetExpanded(true);
					_matchHistory.SetPinned(true);
					await TableFrames(6);
					AssertServerNetworkFits();
					_matchHistory.SetExpanded(false);
					_matchHistory.SetPinned(false);
				}
			}
			_labScenario = InteractionLabSamples.HostedVirusScenario;
			ResetInteractionLab();
			await TableFrames(6);
			AssertServerNetworkFits();
			Control header = _layout.GetNode<Control>("Header");
			RequireInteraction(header.GetGlobalRect().Encloses(_settingsButton!.GetGlobalRect())
				&& header.GetGlobalRect().Encloses(_labScenarios!.GetGlobalRect())
				&& _settingsMenu!.IsAncestorOf(_sessionLogsButton!) && _settingsMenu.IsAncestorOf(_reducedMotion),
				"Global controls or practice selectors escaped the top bar or settings menu.");
			await ClickTableButton(_settingsButton);
			RequireInteraction(SettingsVisible && !CanUseBoardActions(_currentSnapshot!) && !SpatialInputAvailable(_currentSnapshot!)
				&& !CardHoverPreviewVisible && IsFullyInsideViewport(_settingsMenu!.GetNode<Control>("Panel")),
				"Settings failed to cover and block the board.");
			RequireInteraction(_settingsMenu!.GetNode<OptionButton>("%Scale").Selected == 0,
				"Settings did not show the initial UI scale.");
			for (int tab = 0; tab < 8; tab++)
			{
				GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Tab, Pressed = true });
				await TableFrames(1);
				RequireInteraction(_settingsMenu.IsAncestorOf(GetViewport().GuiGetFocusOwner()), "Tab escaped the settings menu.");
			}
			await ClickTableButton(_reducedMotion);
			RequireInteraction(!_reducedMotion.ButtonPressed, "Reduced motion could not be changed in settings.");
			await ClickTableButton(_reducedMotion);
			if (OS.GetCmdlineUserArgs().Contains("--compact-layout-screenshots")) await SaveInteractionEvidence($"compact-settings-{size.X}");
			await PressModalEscapeAsync();
			RequireInteraction(!SettingsVisible && _settingsButton.HasFocus() && _labSubmissionCount == 0,
				"Escape did not close settings without submitting an action.");
			await ClickTableButton(_settingsButton);
			Vector2 outside = header.GetGlobalRect().GetCenter();
			await LabPointer(outside);
			LabButton(outside, true); LabButton(outside, false);
			await TableFrames(2);
			RequireInteraction(!SettingsVisible && _labSubmissionCount == 0, "Clicking outside settings did not dismiss it safely.");
		}
		GD.Print("COMPACT_LAYOUT PASS resolutions=2 seats=2 dense=true hosted=true no_server_scroll=true history=true settings=true compact=true");
	}

	private void AssertServerNetworkFits()
	{
		BoardFitViewport viewport = _gameTable.GetNode<BoardFitViewport>("Surface/Margins/Layout/ServerField/Content/Network/Viewport");
		Rect2 bounds = viewport.GetGlobalRect().Grow(1);
		RequireInteraction(FindDescendants<ScrollContainer>(viewport).Length == 0, "Server network still contains a scroll pane.");
		foreach (TableCardControl card in FindDescendants<TableCardControl>(viewport).Where(card => card.IsVisibleInTree()))
			RequireInteraction(bounds.Encloses(card.GetGlobalRect()), $"Server card {card.CardId} is clipped: {card.GetGlobalRect()} outside {bounds}.");
		foreach (Button header in viewport.FindChildren("Header", "Button", true, false).OfType<Button>().Where(header => header.IsVisibleInTree()))
			RequireInteraction(bounds.Encloses(header.GetGlobalRect()), "A server header became unreachable.");
	}
}
