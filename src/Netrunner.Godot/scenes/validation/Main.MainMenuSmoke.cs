using Godot;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// The main menu (U7): the title screen's entries, the New game view's Gateway
/// and Tutorial areas, seat and deck choice, and a Gateway game started from
/// the menu with the chosen decks, clicked as a player would.
/// </summary>
public partial class Main
{
	private async Task RunMainMenuSmokeAsync()
	{
		try
		{
			bool screenshots = OS.GetCmdlineUserArgs().Contains("--main-menu-screenshots");
			foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
			{
				GetWindow().Size = size;
				GetWindow().ContentScaleSize = size;
				ShowMenuView(MenuView.Title);
				await TableFrames(4);
				RequireInteraction(_menuTitleView!.Visible && !_legacySetupControls.Any(control => control.Visible),
					"The title screen doesn't hide the setup form.");
				string[] entries = FindDescendants<Button>(_menuTitleView).Where(button => button.Visible).Select(button => button.Text).ToArray();
				foreach (string entry in new[] { "New game", "Load game", "Settings", "Practice board", "Quit" })
					RequireInteraction(entries.Contains(entry), $"The title screen lacks '{entry}'.");
				RequireInteraction(!entries.Contains("Developer"), "The developer entry shows without --developer.");
				if (screenshots) await SaveInteractionEvidence($"menu-title-{size.X}");

				await ClickTableButton(FindDescendants<Button>(_menuTitleView).Single(button => button.Text == "New game"));
				await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Gateway decks"));
				await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Play as Runner"));
				RequireInteraction(_menuNewGameView!.Visible && _menuMode == GatewayMode, "New game doesn't show the Gateway decks.");
				OfficialPreset[] runners = _officialPresetCatalog!.Gateway.Where(preset => preset.Side == PlayerSide.Runner).ToArray();
				RequireInteraction(FindDescendants<Button>(_menuYourDecks!).Length == runners.Length
					&& FindDescendants<Button>(_menuOpponentDecks!).Length == 5, "The Gateway area doesn't list three Runner decks and four Corp decks plus Random.");
				if (screenshots) await SaveInteractionEvidence($"menu-gateway-runner-{size.X}");

				await ClickTableButton(FindDescendants<Button>(_menuNewGameView).Single(button => button.Text == "Play as Corp"));
				RequireInteraction(_menuSeat == PlayerSide.Corp && FindDescendants<Button>(_menuYourDecks!).Length == 4,
					"Playing the Corp doesn't offer the four Corp decks.");
				await ClickTableButton(FindDescendants<Button>(_menuNewGameView).Single(button => button.Text == "Tutorial"));
				RequireInteraction(_menuMode == TutorialMode && _menuYourDeck == "system-gateway-expanded-corp"
					&& _menuOpponentDeck == "system-gateway-expanded-runner", "The Tutorial area doesn't default to the expanded decks.");
				if (screenshots) await SaveInteractionEvidence($"menu-tutorial-corp-{size.X}");
				await ClickTableButton(FindDescendants<Button>(_menuNewGameActions!).Single(button => button.Text == "Back"));
				RequireInteraction(_menuTitleView.Visible, "Back doesn't return to the title screen.");
			}

			// Start a Gateway game as the Runner: Stolen Goods against Advanced Yomi.
			await ClickTableButton(FindDescendants<Button>(_menuTitleView!).Single(button => button.Text == "New game"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Gateway decks"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Play as Runner"));
			await ClickTableButton(FindDescendants<Button>(_menuYourDecks!).Single(button => button.Name == "Deck-gateway-stolen-goods"));
			await ClickTableButton(FindDescendants<Button>(_menuOpponentDecks!).Single(button => button.Name == "Deck-gateway-advanced-yomi"));
			_reducedMotion.ButtonPressed = true;
			await ClickTableButton(FindDescendants<Button>(_menuNewGameActions!).Single(button => button.Name == "StartGame"));
			for (int frame = 0; frame < 600 && _currentSnapshot is null; frame++) await TableFrames(1);
			MatchViewModel view = _currentSnapshot?.View ?? throw new InvalidOperationException("Start game didn't start a match.");
			RequireInteraction(view.Viewer == PlayerSide.Runner
				&& view.RunnerIdentity?.DefinitionId == _officialPresetCatalog!.Get("gateway-stolen-goods").IdentityCode
				&& view.CorpIdentity?.DefinitionId == _officialPresetCatalog.Get("gateway-advanced-yomi").IdentityCode,
				"The match doesn't use the decks chosen in the menu.");
			RequireInteraction(!_startPanel.IsVisibleInTree(), "The menu stays visible during the match.");
			GD.Print("MAIN_MENU PASS views=3 resolutions=2 gateway_start=true");
			QuitCleanly();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("MAIN_MENU FAIL " + exception.Message);
			QuitCleanly(1);
		}
	}
}
