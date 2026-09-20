using Godot;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckModalLayersAndEscapeAsync(bool liveCache)
	{
		_isolateSmokeInput = true;
		_boardDrag!.InputFilter = input => input.Device == SmokeInputDevice;
		_reducedMotion.ButtonPressed = true;
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			_labScenario = 15;
			_labScenarios!.Select(_labScenario);
			ResetInteractionLab();
			await TableFrames(3);
			TableCardControl source = _gameTable.VisibleCards["lab-fan-0"];
			source.GrabFocus();
			SelectTableCard(source.CardId!);
			RequireInteraction(source.IsSelected && !_tableActions.GetChildren().OfType<Button>()
				.Any(button => button.Text is "Clear selection" or "Choose a card / server"),
				"Selection still requires a clear-selection button.");
			await PressModalEscapeAsync();
			RequireInteraction(_tableSelectedCard is null && !source.IsSelected && _labSubmissionCount == 0,
				"Escape did not clear the card selection without submitting an action.");
			await ClickTableButton(FindDescendants<Button>(_gameTable).Single(button => button.Text == "HQ"));
			RequireInteraction(_tableSelectedServer is not null, "Server selection fixture did not select HQ.");
			await PressModalEscapeAsync();
			RequireInteraction(_tableSelectedServer is null && _labSubmissionCount == 0,
				"Escape did not clear the server selection.");
			SelectTableCard(source.CardId!);
			source.GrabFocus();

			// Exercise the actual rendered order, even above the board's highest possible Z.
			var marker = new ColorRect
			{
				Position = new Vector2(12, 64),
				Size = new Vector2(16, 16),
				Color = Colors.Magenta,
				ZIndex = 4095,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			AddChild(marker);
			try
			{
				await CheckModalPixelsAsync(covered: false);
				foreach (string pile in new[] { "zone:corp-score", "zone:runner-score", "zone:corp-archives", "zone:runner-heap" })
				{
					RequireInteraction(ShowTablePile(pile), $"Could not open {pile}.");
					await TableFrames(3);
					RequireInteraction(_tablePileOverlay!.GetParent() is CanvasLayer { Layer: > 0 }
						&& !CardHoverPreviewVisible, "Pile browser shares the board canvas or retained its hover preview.");
					await CheckModalPixelsAsync();
					if (OS.GetCmdlineUserArgs().Contains("--card-art-screenshots") && pile == "zone:runner-score")
						await SaveArtworkEvidence($"score-modal-{size.X}", liveCache);
					TableCardControl face = _tablePileCards.GetChildren().OfType<TableCardControl>().First(card => card.Card?.IsKnown == true);
					face.GrabFocus();
					Vector2 point = face.GetGlobalRect().GetCenter();
					await LabPointer(point); LabButton(point, true); LabButton(point, false);
					await TableFrames(3);
					RequireInteraction(_zoomOverlay.Visible && _tablePileOverlay.Visible
						&& ((CanvasLayer)_zoomOverlay.GetParent()).Layer > ((CanvasLayer)_tablePileOverlay.GetParent()).Layer,
						"Inspection did not open above the pile browser.");
					marker.Reparent(_tablePileOverlay);
					marker.Position = new Vector2(12, 64);
					await CheckModalPixelsAsync();
					if (OS.GetCmdlineUserArgs().Contains("--card-art-screenshots") && pile == "zone:runner-heap")
						await SaveArtworkEvidence($"pile-inspection-{size.X}", liveCache);
					await PressModalEscapeAsync(echo: true);
					RequireInteraction(_zoomOverlay.Visible, "A held Escape key dismissed another modal.");
					await PressModalEscapeAsync();
					RequireInteraction(!_zoomOverlay.Visible && _tablePileOverlay.Visible && face.HasFocus()
						&& _tableSelectedCard == source.CardId, "Escape did not return inspection focus to its pile card.");
					marker.Reparent(this);
					marker.Position = new Vector2(12, 64);
					await PressModalEscapeAsync();
					RequireInteraction(!_tablePileOverlay.Visible && source.HasFocus() && source.IsSelected,
						"Closing the pile changed the underlying selection or lost its focus.");
				}
			}
			finally { marker.Free(); }

			StartBoardRunSelection();
			_inspectedCard = source.Card;
			ShowInspectedCardZoom();
			await PressModalEscapeAsync();
			RequireInteraction(!_zoomOverlay.Visible && _boardChoiceVisible, "Escape dismissed choices underneath inspection.");
			await PressModalEscapeAsync();
			RequireInteraction(!_boardChoiceVisible && source.IsSelected, "Escape did not dismiss choices before clearing selection.");
			await PressModalEscapeAsync();
			RequireInteraction(_tableSelectedCard is null && !source.IsSelected && _labSubmissionCount == 0,
				"Escape retained the selection or submitted an action after closing modals.");
		}
	}

	private async Task PressModalEscapeAsync(bool echo = false)
	{
		GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Escape, Pressed = true, Echo = echo });
		GetViewport().PushInput(new InputEventKey { Device = SmokeInputDevice, Keycode = Key.Escape, Pressed = false });
		await TableFrames(2);
	}

	private async Task CheckModalPixelsAsync(bool covered = true)
	{
		if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase)) return;
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		RenderingServer.ForceDraw();
		using Image frame = GetViewport().GetTexture().GetImage();
		Vector2 scale = new Vector2(frame.GetWidth(), frame.GetHeight()) / GetViewportRect().Size;
		Color pixel = frame.GetPixel((int)(20 * scale.X), (int)(72 * scale.Y));
		RequireInteraction(covered ? pixel.R < 0.1f && pixel.G < 0.1f && pixel.B < 0.1f
			: pixel.R > 0.9f && pixel.G < 0.1f && pixel.B > 0.9f,
			covered ? "A high-Z board/pile element drew above the modal background."
				: "The modal coverage fixture was not visible before opening a modal.");
	}
}
