using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// U6 onboarding: a Gateway game started as the Corp shows the first-game hint
/// at its first action window, in the table's free corner, and the hint's
/// buttons dismiss it and turn hints off, as the Settings toggle shows.
/// </summary>
public partial class Main
{
	private async Task RunOnboardingSmokeAsync()
	{
		try
		{
			RequireInteraction(!_preferencesPersist, "A scripted run would write the player's preferences.");
			RequireInteraction(_hintsToggle is not null && _intentCuesToggle is not null && _hintsToggle.ButtonPressed,
				"Settings lacks the hint and intent cue toggles.");
			GetWindow().Size = new Vector2I(1920, 1080);
			GetWindow().ContentScaleSize = new Vector2I(1920, 1080);
			ShowMenuView(MenuView.Title);
			await TableFrames(4);
			await ClickTableButton(FindDescendants<Button>(_menuTitleView!).Single(button => button.Text == "New game"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Gateway decks"));
			await ClickTableButton(FindDescendants<Button>(_menuNewGameView!).Single(button => button.Text == "Play as Corp"));
			await ClickTableButton(FindDescendants<Button>(_menuYourDecks!).Single(button => button.Name == "Deck-gateway-discretion-advised"));
			await ClickTableButton(FindDescendants<Button>(_menuOpponentDecks!).Single(button => button.Name == "Deck-gateway-party-hard"));
			_reducedMotion.ButtonPressed = true;
			await ClickTableButton(FindDescendants<Button>(_menuNewGameActions!).Single(button => button.Name == "StartGame"));

			// Keep the opening hand until the first action window shows its hint.
			long submitted = -1;
			for (int frame = 0; frame < 1800 && _hintPanel?.Visible != true; frame++)
			{
				await TableFrames(1);
				if (_currentSnapshot is not { View.Decision: { } decision } snapshot || decision.OwnerSide != snapshot.View.Viewer
					|| decision.Revision == submitted || snapshot.View.LegalActions.Count == 0) continue;
				submitted = decision.Revision;
				SubmitAction(decision, snapshot.View.LegalActions[0]);
			}
			RequireInteraction(_hintPanel!.Visible && _shownHint == OnboardingHints.Basics && _hintTitle!.Text == "Your turn",
				$"The Corp's first action window shows '{_shownHint}' instead of the basics hint.");
			Rect2 viewport = GetViewport().GetVisibleRect();
			Rect2 panel = _hintPanel.GetGlobalRect();
			RequireInteraction(viewport.Encloses(panel) && panel.Size.Y < viewport.Size.Y / 2,
				$"The hint panel {panel} doesn't fit the table's corner in {viewport}.");
			if (OS.GetCmdlineUserArgs().Contains("--onboarding-screenshots")) await SaveInteractionEvidence("onboarding-hint");

			await ClickTableButton(FindDescendants<Button>(_hintPanel).Single(button => button.Name == "GotIt"));
			RequireInteraction(_seenHints.Contains(OnboardingHints.Basics) && _shownHint != OnboardingHints.Basics,
				"Got it doesn't record the hint as seen.");
			string? next = _shownHint;
			if (_hintPanel.Visible)
				await ClickTableButton(FindDescendants<Button>(_hintPanel).Single(button => button.Name == "HideHints"));
			else
				_hintsToggle!.ButtonPressed = false;
			RequireInteraction(!_hintsEnabled && !_hintPanel.Visible && !_hintsToggle!.ButtonPressed,
				"Hiding hints doesn't turn them off and clear the Settings toggle.");
			// U5 sound: every cue is synthesized, ends within two seconds, and doesn't clip.
			SoundCue[] cues = [.. Enum.GetValues<SoundCue>().Where(cue => cue != SoundCue.None)];
			RequireInteraction(_sound!.CueCount == cues.Length, "The sound bank lacks a cue.");
			foreach (SoundCue cue in cues)
			{
				float[] samples = SoundBank.Synthesize(cue);
				float peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
				RequireInteraction(samples.Length is > 0 and < 44100 && peak is > 0.05f and < 1f, $"The {cue} cue is empty, too long or clips (peak {peak:0.00}).");
			}
			// Play on (gaining credits where offered) until a beat reaches the sound bank.
			long played = -1;
			for (int frame = 0; frame < 1200 && _sound.Requested == 0; frame++)
			{
				await TableFrames(1);
				if (_currentSnapshot is not { View.Decision: { } decision } snapshot || decision.OwnerSide != snapshot.View.Viewer
					|| decision.Revision == played || snapshot.View.LegalActions.Count == 0) continue;
				played = decision.Revision;
				SubmitAction(decision, snapshot.View.LegalActions.FirstOrDefault(action => action.Kind == Netrunner.Core.ActionKind.GainCredit)
					?? snapshot.View.LegalActions[0]);
			}
			RequireInteraction(_sound.Requested > 0, "The table's beats don't reach the sound bank.");
			GD.Print($"ONBOARDING PASS sound_cues={cues.Length} sounds_requested={_sound.Requested} first=basics next={next ?? "none"} hidden=true");
			QuitCleanly();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("ONBOARDING FAIL " + exception.Message);
			QuitCleanly(1);
		}
	}
}
