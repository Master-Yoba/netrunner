using Godot;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// The game menu (Wave 4 U7). A title screen leads to a new game (Tutorial or
/// Gateway decks), a saved game, settings, the practice board and, in developer
/// builds, the developer tools. The original setup form stays as the menu's
/// backing state: the deck, side, difficulty and seed controls every launch
/// path, save and smoke already uses. The developer view shows that form.
/// </summary>
public partial class Main
{
	private const string TutorialMode = "tutorial";
	private const string GatewayMode = "gateway";
	private const string RandomOpponent = "random";

	private readonly List<Control> _legacySetupControls = [];
	private VBoxContainer? _menuTitleView;
	private VBoxContainer? _menuNewGameView;
	private VBoxContainer? _menuDeveloperView;
	private HBoxContainer? _menuNewGameActions;
	private Button? _menuContinue;
	private Label? _menuContinueDetail;
	private HFlowContainer? _menuYourDecks;
	private HFlowContainer? _menuOpponentDecks;
	private Label? _menuModeDescription;
	private string _menuMode = GatewayMode;
	private PlayerSide _menuSeat = PlayerSide.Runner;
	private string? _menuYourDeck;
	private string? _menuOpponentDeck;
	private int _menuDifficulty;
	private SoloSavePreview? _latestSave;

	/// <summary>The view the menu shows; the developer view is the original setup form.</summary>
	private enum MenuView { Title, NewGame, Developer }

	private void BuildMainMenu()
	{
		foreach (Node child in _startPanel.GetChildren())
			if (child is Control control) _legacySetupControls.Add(control);
		var menu = new VBoxContainer { Name = "MainMenu", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		menu.AddThemeConstantOverride("separation", 16);
		_startPanel.AddChild(menu);
		_startPanel.MoveChild(menu, 0);
		_menuTitleView = BuildMenuTitleView();
		_menuNewGameView = BuildMenuNewGameView();
		_menuDeveloperView = BuildMenuDeveloperView();
		menu.AddChild(_menuTitleView);
		// The deck tiles fit one row per side at 1920; a smaller window scrolls.
		var newGameScroll = new ScrollContainer
		{
			Name = "NewGameScroll",
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		newGameScroll.AddChild(_menuNewGameView);
		menu.AddChild(newGameScroll);
		// Start and Back stay in view below the scrolling deck choice.
		if (_menuNewGameView.GetNodeOrNull<HBoxContainer>("Actions") is { } actions)
		{
			_menuNewGameView.RemoveChild(actions);
			menu.AddChild(actions);
			_menuNewGameActions = actions;
		}
		menu.AddChild(_menuDeveloperView);
		menu.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_startPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		if (_startPanel.GetParent() is Control setupPanel) setupPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		ShowMenuView(MenuView.Title);
		_ = RefreshContinueAsync();
	}

	private void ShowMenuView(MenuView view)
	{
		if (_menuTitleView is null || _menuNewGameView is null || _menuDeveloperView is null) return;
		_menuTitleView.Visible = view == MenuView.Title;
		_menuNewGameView.Visible = view == MenuView.NewGame;
		if (_menuNewGameView.GetParent() is Control scroll) scroll.Visible = view == MenuView.NewGame;
		if (_menuNewGameActions is not null) _menuNewGameActions.Visible = view == MenuView.NewGame;
		_menuDeveloperView.Visible = view == MenuView.Developer;
		foreach (Control control in _legacySetupControls) control.Visible = view == MenuView.Developer;
		SetMenuHeader(true);
		if (view == MenuView.NewGame) RefreshMenuDecks();
		ApplyAccessibilityFocusOrder(_startPanel);
		Control? first = view switch
		{
			MenuView.Title => FindDescendants<Button>(_menuTitleView).FirstOrDefault(button => button.Visible && !button.Disabled),
			MenuView.NewGame => FindDescendants<Button>(_menuNewGameView).FirstOrDefault(button => button.Visible),
			_ => _startButton,
		};
		first?.CallDeferred(Control.MethodName.GrabFocus);
	}

	/// <summary>The in-game header controls (new game, skip animation, status) are hidden on the menu.</summary>
	private void SetMenuHeader(bool menu)
	{
		if (_layout?.GetNodeOrNull<Control>("Header") is not { } header) return;
		foreach (string name in new[] { "NewGame", "SkipAnimation", "Status", "Phase" })
			if (header.GetNodeOrNull<Control>(name) is { } control) control.Visible = !menu;
		if (menu && _onboardingColumn is not null) _onboardingColumn.Visible = false;
	}

	private VBoxContainer BuildMenuTitleView()
	{
		var view = new VBoxContainer { Name = "TitleView", SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
		view.AddThemeConstantOverride("separation", 12);
		view.AddChild(MakeLabel("NETRUNNER", 64, Accent));
		view.AddChild(MakeLabel("Solo · System Gateway", 20, Muted));
		view.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });
		bool content = HasAuthenticatedOfficialContent();
		if (!content)
		{
			Label missing = MakeWrappedLabel(
				"The official System Gateway card data isn't installed, so games can't start. It's never downloaded "
				+ "automatically: run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 "
				+ "--retrieval-date 2026-08-28, then rebuild (make play). The practice board works without it.", 16, Warning);
			missing.CustomMinimumSize = new Vector2(520, 0);
			view.AddChild(missing);
		}
		_menuContinue = MenuButton(view, "Continue", () =>
		{
			if (_latestSave is not { } save) return;
			ShowSaveBrowser();
			_ = LoadSavedMatchAsync(save.SaveId, backup: false);
		});
		_menuContinue.Visible = false;
		_menuContinueDetail = MakeLabel("", 14, Muted);
		_menuContinueDetail.Visible = false;
		view.AddChild(_menuContinueDetail);
		MenuButton(view, "New game", () => ShowMenuView(MenuView.NewGame)).Disabled = !content;
		MenuButton(view, "Load game", ShowSaveBrowser).Disabled = !content;
		MenuButton(view, "Settings", () => SetSettingsVisible(true));
		MenuButton(view, "Practice board", OpenInteractionLab);
		if (DeveloperToolsEnabled) MenuButton(view, "Developer", () => ShowMenuView(MenuView.Developer));
		MenuButton(view, "Quit", () => GetTree().Quit());
		return view;
	}

	private VBoxContainer BuildMenuNewGameView()
	{
		var view = new VBoxContainer { Name = "NewGameView", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		view.AddThemeConstantOverride("separation", 12);
		view.AddChild(MakeLabel("NEW GAME", 32, Accent));
		HBoxContainer modes = MenuToggleRow(view, [("Gateway decks", GatewayMode), ("Tutorial", TutorialMode)], _menuMode, mode =>
		{
			_menuMode = mode;
			_menuYourDeck = null;
			_menuOpponentDeck = null;
			RefreshMenuDecks();
		});
		modes.Name = "Modes";
		_menuModeDescription = MakeWrappedLabel("", 15, Muted);
		view.AddChild(_menuModeDescription);
		MenuToggleRow(view, [("Play as Runner", "runner"), ("Play as Corp", "corp")], "runner", seat =>
		{
			_menuSeat = seat == "corp" ? PlayerSide.Corp : PlayerSide.Runner;
			_menuYourDeck = null;
			_menuOpponentDeck = null;
			RefreshMenuDecks();
		}).Name = "Seat";
		view.AddChild(MakeLabel("YOUR DECK", 14, Muted));
		_menuYourDecks = new HFlowContainer { Name = "YourDecks" };
		_menuYourDecks.AddThemeConstantOverride("h_separation", 12);
		_menuYourDecks.AddThemeConstantOverride("v_separation", 12);
		view.AddChild(_menuYourDecks);
		view.AddChild(MakeLabel("OPPONENT", 14, Muted));
		_menuOpponentDecks = new HFlowContainer { Name = "OpponentDecks" };
		_menuOpponentDecks.AddThemeConstantOverride("h_separation", 12);
		_menuOpponentDecks.AddThemeConstantOverride("v_separation", 12);
		view.AddChild(_menuOpponentDecks);
		MenuToggleRow(view, [("Standard opponent", "0"), ("Easy opponent", "1")], "0",
			value => _menuDifficulty = value == "1" ? 1 : 0).Name = "Difficulty";
		var actions = new HBoxContainer { Name = "Actions" };
		actions.AddThemeConstantOverride("separation", 12);
		view.AddChild(actions);
		MenuButton(actions, "Start game", StartMenuGame).Name = "StartGame";
		MenuButton(actions, "Back", () => ShowMenuView(MenuView.Title));
		return view;
	}

	private VBoxContainer BuildMenuDeveloperView()
	{
		var view = new VBoxContainer { Name = "DeveloperView" };
		view.AddThemeConstantOverride("separation", 8);
		view.AddChild(MakeLabel("DEVELOPER", 24, Warning));
		view.AddChild(MakeWrappedLabel("The original setup form: any preset pairing, a fixed seed, the metadata gallery and the interaction lab.", 14, Muted));
		MenuButton(view, "Back to the menu", () => ShowMenuView(MenuView.Title));
		return view;
	}

	/// <summary>The decks for the current mode and seat, as cards; the opponent side adds "Random" for Gateway decks.</summary>
	private void RefreshMenuDecks()
	{
		if (_menuYourDecks is null || _menuOpponentDecks is null || _officialPresetCatalog is null) return;
		PlayerSide opponent = _menuSeat == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
		OfficialPreset[] Decks(PlayerSide side) => (_menuMode == GatewayMode ? _officialPresetCatalog.Gateway : _officialPresetCatalog.Teaching)
			.Where(preset => preset.Side == side && preset.Playable).ToArray();
		OfficialPreset[] yours = Decks(_menuSeat);
		OfficialPreset[] theirs = Decks(opponent);
		// Expanded is the tutorial default (playtest, 27 September); Gateway starts on the first deck.
		string Default(OfficialPreset[] decks) => decks.FirstOrDefault(preset => preset.Kind == "expanded")?.PresetId ?? decks[0].PresetId;
		_menuYourDeck ??= yours.Length > 0 ? Default(yours) : null;
		_menuOpponentDeck ??= _menuMode == GatewayMode ? RandomOpponent : theirs.Length > 0 ? Default(theirs) : null;
		if (_menuModeDescription is not null)
			_menuModeDescription.Text = _menuMode == GatewayMode
				? "The seven System Gateway sample decks from Null Signal Games: one per faction, built to show how each plays."
				: "The Catalyst and the Syndicate, the learn-to-play decks. The basic decks start small; the expanded decks add the booster cards.";
		FillMenuDecks(_menuYourDecks, yours, _menuYourDeck, allowRandom: false, id => { _menuYourDeck = id; RefreshMenuDecks(); });
		FillMenuDecks(_menuOpponentDecks, theirs, _menuOpponentDeck, allowRandom: _menuMode == GatewayMode,
			id => { _menuOpponentDeck = id; RefreshMenuDecks(); });
	}

	private void FillMenuDecks(HFlowContainer container, OfficialPreset[] decks, string? selected, bool allowRandom, Action<string> choose)
	{
		// Remove before freeing: a queued node keeps its name until the frame ends,
		// and the new tile of the same deck would otherwise be renamed.
		foreach (Node child in container.GetChildren())
		{
			container.RemoveChild(child);
			child.QueueFree();
		}
		foreach (OfficialPreset preset in decks)
			container.AddChild(MenuDeckCard(preset.PresetId, DeckTitle(preset), IdentityTitle(preset.IdentityCode), DeckSummary(preset),
				preset.IdentityCode, selected == preset.PresetId, choose));
		if (allowRandom)
			container.AddChild(MenuDeckCard(RandomOpponent, "Random", "Any of the decks above", "A different opponent each game.",
				null, selected == RandomOpponent, choose));
	}

	private Button MenuDeckCard(string id, string title, string identity, string summary, string? identityCode, bool selected, Action<string> choose)
	{
		var button = new Button
		{
			Name = "Deck-" + id,
			ToggleMode = true,
			ButtonPressed = selected,
			CustomMinimumSize = new Vector2(360, 232),
			TooltipText = summary,
		};
		button.Pressed += () => choose(id);
		var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("separation", 10);
		SetFullRect(row);
		row.OffsetLeft = 10;
		row.OffsetTop = 10;
		row.OffsetRight = -10;
		row.OffsetBottom = -10;
		button.AddChild(row);
		if (identityCode is not null && _metadataResolver.TryResolve(identityCode, out CardPresentationMetadata metadata))
		{
			var card = new TableCardControl
			{
				CustomMinimumSize = new Vector2(112, 156),
				SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			row.AddChild(card);
			card.SetCard(new CardViewModel("menu-" + identityCode, identityCode, metadata.Title, metadata.Type, true, false, 0,
				null, null, null, null, metadata.Subtypes, metadata.RulesText, metadata.Faction, null));
		}
		var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
		text.AddThemeConstantOverride("separation", 4);
		row.AddChild(text);
		text.AddChild(MakeWrappedLabel(title, 19, selected ? Accent : Colors.White));
		Label identityLabel = MakeWrappedLabel(identity, 13, Muted);
		text.AddChild(identityLabel);
		text.AddChild(MakeWrappedLabel(summary, 13, Colors.White));
		return button;
	}

	private static string DeckTitle(OfficialPreset preset) => preset.Name
		?? (preset.Kind == "starter" ? "Basic" : "Expanded") + (preset.Side == PlayerSide.Corp ? " · The Syndicate" : " · The Catalyst");

	private static string DeckSummary(OfficialPreset preset) => preset.Summary
		?? (preset.Kind == "starter"
			? $"The learn-to-play deck: {preset.CardCount} cards, fewer card types."
			: $"The learn-to-play deck with the booster cards: {preset.CardCount} cards.");

	private string IdentityTitle(string code) =>
		_metadataResolver.TryResolve(code, out CardPresentationMetadata metadata) ? metadata.Title : code;

	/// <summary>Starts the chosen game through the setup form: its decks, side, difficulty and a fresh seed.</summary>
	private void StartMenuGame()
	{
		if (_officialPresetCatalog is null || _menuYourDeck is null || _menuOpponentDeck is null) return;
		string opponent = _menuOpponentDeck;
		if (opponent == RandomOpponent)
		{
			OfficialPreset[] pool = _officialPresetCatalog.Gateway
				.Where(preset => preset.Side != _menuSeat && preset.Playable).ToArray();
			opponent = pool[(int)(GD.Randi() % (uint)pool.Length)].PresetId;
		}
		string corp = _menuSeat == PlayerSide.Corp ? _menuYourDeck : opponent;
		string runner = _menuSeat == PlayerSide.Runner ? _menuYourDeck : opponent;
		SelectPresetById(_corpPreset, _corpPresetIds, corp);
		SelectPresetById(_runnerPreset, _runnerPresetIds, runner);
		_humanSide.Select(_menuSeat == PlayerSide.Runner ? 0 : 1);
		_difficulty?.Select(_menuDifficulty);
		_seedInput.Text = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)))
			.ToString(System.Globalization.CultureInfo.InvariantCulture);
		_startButton.EmitSignal(BaseButton.SignalName.Pressed);
	}

	/// <summary>Shows Continue for the most recent compatible, unfinished save.</summary>
	private async Task RefreshContinueAsync()
	{
		if (_menuContinue is null || !HasAuthenticatedOfficialContent()) return;
		try
		{
			IReadOnlyList<SoloSaveBrowserEntry> entries = await GetSoloSessionService().BrowseAsync();
			_latestSave = entries.Where(entry => entry.IsCompatible && entry.Preview is { IsTerminal: false })
				.Select(entry => entry.Preview!).OrderByDescending(preview => preview.UpdatedUtc).FirstOrDefault();
		}
		catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
		{
			_latestSave = null;
		}
		if (!IsInstanceValid(_menuContinue)) return;
		_menuContinue.Visible = _latestSave is not null;
		if (_menuContinueDetail is not null)
		{
			_menuContinueDetail.Visible = _latestSave is not null;
			if (_latestSave is { } save)
				_menuContinueDetail.Text = $"{save.HumanSide} · {SaveDeckName(save.CorpDeck)} vs {SaveDeckName(save.RunnerDeck)} · turn {save.Turn}";
		}
	}

	private string SaveDeckName(string presetId) =>
		_officialPresetCatalog?.TryGet(presetId, out OfficialPreset preset) == true ? DeckTitle(preset) : presetId;

	private static Button MenuButton(Container parent, string text, Action pressed)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(320, 52), Name = text.Replace(" ", "", StringComparison.Ordinal) };
		button.AddThemeFontSizeOverride("font_size", 20);
		button.Pressed += pressed;
		parent.AddChild(button);
		return button;
	}

	/// <summary>A row of mutually exclusive toggle buttons.</summary>
	private static HBoxContainer MenuToggleRow(Container parent, (string Text, string Value)[] options, string selected, Action<string> choose)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		var group = new ButtonGroup();
		foreach ((string text, string value) in options)
		{
			var button = new Button { Text = text, ToggleMode = true, ButtonGroup = group, ButtonPressed = value == selected, CustomMinimumSize = new Vector2(200, 44) };
			button.AddThemeFontSizeOverride("font_size", 17);
			button.Pressed += () => choose(value);
			row.AddChild(button);
		}
		parent.AddChild(row);
		return row;
	}
}
