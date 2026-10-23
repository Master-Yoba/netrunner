using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// U6 onboarding: first-game hints and optional opponent intent cues, in the
/// empty top-right of the table. Hints are shown once each, turn themselves off
/// after the first finished game, and can be turned back on in Settings. The
/// preferences live in <c>user://preferences.cfg</c>; scripted smoke, capture and
/// gate runs use defaults in memory, with hints off unless a smoke asks for them.
/// </summary>
public partial class Main
{
	private const string PreferencesPath = "user://preferences.cfg";
	private readonly HashSet<string> _seenHints = new(StringComparer.Ordinal);
	private bool _hintsEnabled = true;
	private bool _intentCuesEnabled = true;
	private bool _firstGameFinished;
	private bool _preferencesPersist;
	private CanvasLayer? _onboardingLayer;
	private VBoxContainer? _onboardingColumn;
	private VBoxContainer? _intentCues;
	private PanelContainer? _hintPanel;
	private Label? _hintTitle;
	private Label? _hintText;
	private string? _shownHint;
	private CheckButton? _hintsToggle;
	private CheckButton? _intentCuesToggle;

	private static bool ScriptedRun => OS.GetCmdlineUserArgs().Any(arg => arg.Contains("smoke", StringComparison.Ordinal)
		|| arg.Contains("capture", StringComparison.Ordinal) || arg.Contains("gate", StringComparison.Ordinal));

	private void BuildOnboarding()
	{
		_preferencesPersist = !ScriptedRun;
		if (_preferencesPersist) LoadPreferences();
		else _hintsEnabled = OS.GetCmdlineUserArgs().Contains("--onboarding-smoke");

		_onboardingLayer = new CanvasLayer { Name = "OnboardingLayer", Layer = 2 };
		AddChild(_onboardingLayer);
		_onboardingColumn = new VBoxContainer
		{
			Name = "OnboardingColumn",
			AnchorLeft = 1,
			AnchorRight = 1,
			OffsetLeft = -520,
			OffsetRight = -110,
			OffsetTop = 118,
			GrowHorizontal = Control.GrowDirection.Begin,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_onboardingColumn.AddThemeConstantOverride("separation", 10);
		_onboardingLayer.AddChild(_onboardingColumn);

		_intentCues = new VBoxContainer { Name = "IntentCues", MouseFilter = Control.MouseFilterEnum.Ignore };
		_intentCues.AddThemeConstantOverride("separation", 4);
		_onboardingColumn.AddChild(_intentCues);

		_hintPanel = new PanelContainer { Name = "HintPanel", Visible = false };
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.04f, 0.08f, 0.1f, 0.96f),
			BorderColor = new Color(0.25f, 0.7f, 0.65f),
			ContentMarginLeft = 16,
			ContentMarginRight = 16,
			ContentMarginTop = 12,
			ContentMarginBottom = 12,
		};
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(8);
		_hintPanel.AddThemeStyleboxOverride("panel", style);
		_onboardingColumn.AddChild(_hintPanel);
		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 8);
		_hintPanel.AddChild(content);
		_hintTitle = new Label { Name = "HintTitle" };
		_hintTitle.AddThemeFontSizeOverride("font_size", 18);
		_hintTitle.AddThemeColorOverride("font_color", new Color(0.45f, 0.95f, 0.85f));
		content.AddChild(_hintTitle);
		_hintText = new Label { Name = "HintText", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) };
		content.AddChild(_hintText);
		var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		buttons.AddThemeConstantOverride("separation", 8);
		content.AddChild(buttons);
		var hideAll = new Button { Name = "HideHints", Text = "Hide hints", TooltipText = "Turn first-game hints off; Settings turns them back on." };
		hideAll.Pressed += () => SetHintsEnabled(false);
		buttons.AddChild(hideAll);
		var gotIt = new Button { Name = "GotIt", Text = "Got it" };
		gotIt.Pressed += DismissHint;
		buttons.AddChild(gotIt);

		if (_settingsMenu?.GetNode<VBoxContainer>("Panel/Content") is { } settings)
		{
			_hintsToggle = new CheckButton
			{
				Name = "Hints",
				Text = "First-game hints",
				ButtonPressed = _hintsEnabled,
				TooltipText = "Short tips the first time each action comes up. They turn off after your first finished game."
			};
			_hintsToggle.Toggled += SetHintsEnabled;
			_intentCuesToggle = new CheckButton
			{
				Name = "IntentCues",
				Text = "Opponent intent cues",
				ButtonPressed = _intentCuesEnabled,
				TooltipText = "Point out what the opponent is visibly building toward, such as an advanced remote."
			};
			_intentCuesToggle.Toggled += enabled => { _intentCuesEnabled = enabled; SavePreferences(); RenderOnboarding(_currentSnapshot); };
			int after = settings.GetNode("AutoPass").GetIndex() + 1;
			settings.AddChild(_hintsToggle);
			settings.MoveChild(_hintsToggle, after);
			settings.AddChild(_intentCuesToggle);
			settings.MoveChild(_intentCuesToggle, after + 1);
		}
	}

	private void RenderOnboarding(MatchSnapshot? snapshot)
	{
		if (_onboardingColumn is null || _intentCues is null || _hintPanel is null) return;
		bool inMatch = snapshot is not null && !_interactionLab && _useCardTable;
		_onboardingColumn.Visible = inMatch;
		if (!inMatch) return;
		MatchViewModel view = snapshot!.View;
		if (view.Outcome.IsTerminal && !_firstGameFinished)
		{
			_firstGameFinished = true;
			SetHintsEnabled(false);
		}

		IReadOnlyList<string> cues = _intentCuesEnabled
			? OpponentIntentCues.From(view, server => _gameTable.GetServerDisplayName(server.Id))
			: [];
		if (!cues.SequenceEqual(_intentCues.GetChildren().OfType<Label>().Select(label => label.Text)))
		{
			foreach (Node child in _intentCues.GetChildren())
			{
				_intentCues.RemoveChild(child);
				child.QueueFree();
			}
			foreach (string cue in cues)
			{
				var label = new Label { Text = cue, HorizontalAlignment = HorizontalAlignment.Right };
				label.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.3f));
				_intentCues.AddChild(label);
			}
		}

		OnboardingHint? hint = _hintsEnabled && !snapshot.IsOpponentThinking ? OnboardingHints.Next(view, _seenHints) : null;
		// Keep a shown hint up until it is dismissed, even once its action has passed.
		if (_shownHint is not null && _hintsEnabled) return;
		_hintPanel.Visible = hint is not null;
		_shownHint = hint?.Id;
		if (hint is null) return;
		RecordFullMatchSelection("hint", hint.Id);
		_hintTitle!.Text = hint.Title;
		_hintText!.Text = hint.Text;
	}

	private void DismissHint()
	{
		RecordFullMatchSelection("hint-dismissed", _shownHint);
		if (_shownHint is { } id && _seenHints.Add(id)) SavePreferences();
		_shownHint = null;
		_hintPanel!.Visible = false;
		RenderOnboarding(_currentSnapshot);
	}

	private void SetHintsEnabled(bool enabled)
	{
		if (enabled != _hintsEnabled) RecordFullMatchSelection("ui-control", enabled ? "hints-on" : "hints-off");
		_hintsEnabled = enabled;
		if (enabled && _firstGameFinished) _seenHints.Clear();
		_hintsToggle?.SetPressedNoSignal(enabled);
		if (!enabled)
		{
			_shownHint = null;
			if (_hintPanel is not null) _hintPanel.Visible = false;
		}
		SavePreferences();
		RenderOnboarding(_currentSnapshot);
	}

	private void LoadPreferences()
	{
		var config = new ConfigFile();
		if (config.Load(PreferencesPath) != Error.Ok) return;
		_firstGameFinished = (bool)config.GetValue("onboarding", "first_game_finished", false);
		_hintsEnabled = (bool)config.GetValue("onboarding", "hints", !_firstGameFinished);
		_intentCuesEnabled = (bool)config.GetValue("onboarding", "intent_cues", true);
		_soundEnabled = (bool)config.GetValue("audio", "sound", true);
		_autoBenefitsEnabled = (bool)config.GetValue("play", "auto_benefits", true);
		_effectsLevel = Math.Clamp((float)config.GetValue("audio", "effects_volume", 0.7f), 0f, 1f);
		LoadLlmPreferences(config);
		foreach (string id in ((string)config.GetValue("onboarding", "seen_hints", "")).Split(',', StringSplitOptions.RemoveEmptyEntries))
			_seenHints.Add(id);
	}

	private void SavePreferences()
	{
		if (!_preferencesPersist) return;
		var config = new ConfigFile();
		config.SetValue("onboarding", "first_game_finished", _firstGameFinished);
		config.SetValue("onboarding", "hints", _hintsEnabled);
		config.SetValue("onboarding", "intent_cues", _intentCuesEnabled);
		config.SetValue("onboarding", "seen_hints", string.Join(',', _seenHints.Order(StringComparer.Ordinal)));
		config.SetValue("audio", "sound", _soundEnabled);
		config.SetValue("play", "auto_benefits", _autoBenefitsEnabled);
		config.SetValue("audio", "effects_volume", _effectsLevel);
		SaveLlmPreferences(config);
		Error result = config.Save(PreferencesPath);
		if (result != Error.Ok) GD.PushWarning($"Could not save preferences: {result}");
	}
}
