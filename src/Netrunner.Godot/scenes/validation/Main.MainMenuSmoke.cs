using Godot;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// The main menu (U7): the title screen's entries, the New game view's Gateway
/// and Tutorial areas (and, previewed, the Gateway + Elevation area), seat and
/// deck choice, and a Gateway game started from the menu with the chosen decks,
/// clicked as a player would.
/// </summary>
public partial class Main
{
	private string[] MenuPersonalities() => [.. FindDescendants<Button>(_menuPersonalities!).Select(button => button.Text)];

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
				if (!HasAuthenticatedOfficialContent())
				{
					// The synthetic lane (CI's fast tier) has no official card data: games can't start, the title says
					// how to provision it, and only the practice board opens.
					Button[] title = FindDescendants<Button>(_menuTitleView);
					RequireInteraction(title.Single(button => button.Text == "New game").Disabled
						&& title.Single(button => button.Text == "Load game").Disabled
						&& !title.Single(button => button.Text == "Practice board").Disabled
						&& FindDescendants<Label>(_menuTitleView).Any(label => label.Visible
							&& label.Text.Contains("card data isn't installed", StringComparison.Ordinal)),
						"Without official content the title screen doesn't explain itself or still offers games.");
					continue;
				}

				await ClickTableButton(FindDescendants<Button>(_menuTitleView).Single(button => button.Text == "New game"));
				await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Gateway decks"));
				await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Play as Runner"));
				RequireInteraction(_menuNewGameView!.Visible && _menuMode == GatewayMode, "New game doesn't show the Gateway decks.");
				OfficialPreset[] runners = MenuModeDecks(GatewayMode).Where(preset => preset.Side == PlayerSide.Runner).ToArray();
				RequireInteraction(FindDescendants<Button>(_menuYourDecks!).Length == runners.Length
					&& FindDescendants<Button>(_menuOpponentDecks!).Length == 5, "The Gateway area doesn't list three Runner decks and four Corp decks plus Random.");
				// A6: three difficulties, and the Corp opponent's personalities.
				string[] difficulties = [.. FindDescendants<Button>(_menuNewGameView!.GetNode<Control>("Difficulty")).Select(button => button.Text)];
				RequireInteraction(difficulties.SequenceEqual(["Easy opponent", "Standard opponent", "Hard opponent"]), "The menu doesn't offer Easy, Standard and Hard.");
				RequireInteraction(MenuPersonalities().SequenceEqual(["Balanced style", "Glacier", "Rush", "Asset spam"]), "Playing the Runner doesn't offer the Corp's personalities.");
				if (screenshots) await SaveInteractionEvidence($"menu-gateway-runner-{size.X}");

				await ClickTableButton(FindDescendants<Button>(_menuNewGameView).Single(button => button.Text == "Play as Corp"));
				RequireInteraction(_menuSeat == PlayerSide.Corp && FindDescendants<Button>(_menuYourDecks!).Length == 4,
					"Playing the Corp doesn't offer the four Corp decks.");
				RequireInteraction(MenuPersonalities().SequenceEqual(["Balanced style", "Aggressive", "Rig-builder"]), "Playing the Corp doesn't offer the Runner's personalities.");
				if (ElevationMenuOffered)
				{
					// D-EL-10: the Gateway + Elevation format offers the Core Battle Box, Random drawing from it.
					await ClickTableButton(FindDescendants<Button>(_menuNewGameView).Single(button => button.Text == "Core Battle Box"));
					RequireInteraction(_menuMode == ElevationMode
						&& FindDescendants<Button>(_menuYourDecks!).Length == 4
						&& FindDescendants<Button>(_menuOpponentDecks!).Length == 4
						&& _menuOpponentDeck == RandomOpponent
						&& FindDescendants<Button>(_menuYourDecks!).All(button => button.Name.ToString().StartsWith("Deck-elevation-box-", StringComparison.Ordinal)),
						"The Core Battle Box area doesn't list four Corp decks and three Runner decks plus Random.");
					RequireInteraction(FindDescendants<Button>(_menuDifficulties!).Single(button => button.Text == "Hard opponent").Visible
						&& _menuPersonalities!.Visible, "The Core Battle Box area lacks Hard or the personalities.");
					if (screenshots) await SaveInteractionEvidence($"menu-elevation-corp-{size.X}");
				}
				if (NsgCoreMenuOffered)
				{
					// NSG Core Sets offers NSG's twenty-one decks, Random drawing from them.
					await ClickTableButton(FindDescendants<Button>(_menuNewGameView).Single(button => button.Text == "NSG Core Sets"));
					RequireInteraction(_menuMode == NsgCoreMode
						&& FindDescendants<Button>(_menuYourDecks!).Length == 12
						&& FindDescendants<Button>(_menuOpponentDecks!).Length == 10
						&& _menuOpponentDeck == RandomOpponent
						&& FindDescendants<Button>(_menuYourDecks!).All(button => button.Name.ToString().StartsWith("Deck-nsg-core-", StringComparison.Ordinal)),
						"The NSG Core Sets area doesn't list twelve Corp decks and nine Runner decks plus Random.");
					RequireInteraction(FindDescendants<Button>(_menuDifficulties!).Single(button => button.Text == "Hard opponent").Visible
						&& _menuPersonalities!.Visible, "The NSG Core Sets area lacks Hard or the personalities.");
					if (screenshots) await SaveInteractionEvidence($"menu-nsg-core-corp-{size.X}");
				}
				if (StartupMenuOffered)
				{
					// D-VP-7: Startup 2026 offers its fourteen tournament lists, Random drawing from them.
					await ClickTableButton(FindDescendants<Button>(_menuNewGameView).Single(button => button.Text == "Startup 2026"));
					RequireInteraction(_menuMode == StartupMode
						&& FindDescendants<Button>(_menuYourDecks!).Length == 8
						&& FindDescendants<Button>(_menuOpponentDecks!).Length == 7
						&& _menuOpponentDeck == RandomOpponent
						&& FindDescendants<Button>(_menuYourDecks!).All(button => button.Name.ToString().StartsWith("Deck-startup-", StringComparison.Ordinal)),
						"The Startup 2026 area doesn't list eight Corp decks and six Runner decks plus Random.");
					RequireInteraction(FindDescendants<Button>(_menuDifficulties!).Single(button => button.Text == "Hard opponent").Visible
						&& _menuPersonalities!.Visible, "The Startup 2026 area lacks Hard or the personalities.");
					if (screenshots) await SaveInteractionEvidence($"menu-startup-corp-{size.X}");
				}
				await ClickTableButton(FindDescendants<Button>(_menuNewGameView).Single(button => button.Text == "Tutorial"));
				RequireInteraction(_menuMode == TutorialMode && _menuYourDeck == "system-gateway-expanded-corp"
					&& _menuOpponentDeck == "system-gateway-expanded-runner", "The Tutorial area doesn't default to the expanded decks.");
				RequireInteraction(!_menuPersonalities!.Visible && !FindDescendants<Button>(_menuDifficulties!).Single(button => button.Text == "Hard opponent").Visible,
					"The Tutorial area offers Hard or a personality its bot doesn't have.");
				if (screenshots) await SaveInteractionEvidence($"menu-tutorial-corp-{size.X}");
				await ClickTableButton(FindDescendants<Button>(_menuNewGameActions!).Single(button => button.Text == "Back"));
				RequireInteraction(_menuTitleView.Visible, "Back doesn't return to the title screen.");
			}

			if (!HasAuthenticatedOfficialContent())
			{
				GD.Print("MAIN_MENU PASS views=1 resolutions=2 content=false");
				QuitCleanly();
				return;
			}

			// Start a Gateway game as the Runner: Stolen Goods against Advanced Yomi.
			await ClickTableButton(FindDescendants<Button>(_menuTitleView!).Single(button => button.Text == "New game"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Gateway decks"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Play as Runner"));
			await ClickTableButton(FindDescendants<Button>(_menuYourDecks!).Single(button => button.Name == "Deck-gateway-stolen-goods"));
			await ClickTableButton(FindDescendants<Button>(_menuOpponentDecks!).Single(button => button.Name == "Deck-gateway-advanced-yomi"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Hard opponent"));
			await ClickTableButton(FindDescendants<Button>(_menuPersonalities!).Single(button => button.Text == "Rush"));
			_reducedMotion.ButtonPressed = true;
			await ClickTableButton(FindDescendants<Button>(_menuNewGameActions!).Single(button => button.Name == "StartGame"));
			RequireInteraction(SelectedDifficulty == "hard" && SelectedPersonality(PlayerSide.Corp) == Netrunner.AI.AgentPersonality.Rush,
				"The setup form didn't take the menu's Hard Rush opponent.");
			for (int frame = 0; frame < 600 && _currentSnapshot is null; frame++) await TableFrames(1);
			MatchViewModel view = _currentSnapshot?.View ?? throw new InvalidOperationException("Start game didn't start a match.");
			RequireInteraction(view.Viewer == PlayerSide.Runner
				&& view.RunnerIdentity?.DefinitionId == _officialPresetCatalog!.Get("gateway-stolen-goods").IdentityCode
				&& view.CorpIdentity?.DefinitionId == _officialPresetCatalog.Get("gateway-advanced-yomi").IdentityCode,
				"The match doesn't use the decks chosen in the menu.");
			RequireInteraction(!_startPanel.IsVisibleInTree(), "The menu stays visible during the match.");
			GD.Print($"MAIN_MENU PASS views=3 resolutions=2 gateway_start=true difficulties=3 personalities=true elevation={(ElevationMenuOffered ? "true" : "false")} nsg_core={(NsgCoreMenuOffered ? "true" : "false")}");
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
