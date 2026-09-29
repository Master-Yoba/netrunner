using Godot;

namespace Netrunner.GodotClient;

public partial class Main
{
	private Control? _settingsMenu;
	private Button? _settingsButton;
	private bool SettingsVisible => _settingsMenu?.Visible == true;

	private void BuildSettingsMenu()
	{
		Control header = _layout.GetNode<Control>("Header");
		_settingsMenu = GetNode<Control>("SettingsLayer/SettingsMenu");
		_settingsButton = header.GetNode<Button>("Settings");
		_settingsButton.Pressed += () => SetSettingsVisible(!SettingsVisible);
		_settingsMenu.GetNode<Button>("%Close").Pressed += () => SetSettingsVisible(false);
		_settingsMenu.GetNode<Control>("Backdrop").GuiInput += input =>
		{
			if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			{
				SetSettingsVisible(false);
				GetViewport().SetInputAsHandled();
			}
		};
		header.GetNode<Button>("NewGame").Pressed += () => GetTree().ReloadCurrentScene();
		_reducedMotion = _settingsMenu.GetNode<CheckButton>("%ReducedMotion");
		_reducedMotion.Toggled += reduced => { if (reduced) { _tablePlaybackWait = 0; _gameTable.StopMotion(); } };
		_fastOpponent = _settingsMenu.GetNode<CheckButton>("%FastOpponent");
		_autoPass = _settingsMenu.GetNode<CheckButton>("%AutoPass");
		_autoPass.Toggled += _ => ApplyAutoPassSetting();
		_stopAtWindow = header.GetNode<Button>("StopAtWindow");
		_stopAtWindow.Toggled += stop => { RecordFullMatchSelection("ui-control", stop ? "stop-at-window" : "stop-at-window-off"); _coordinator?.StopAtNextWindow(stop); };
		_stopAtWindow.Visible = false;
		_speed = _settingsMenu.GetNode<HSlider>("%Speed");
		_speed.ValueChanged += value => _settingsMenu.GetNode<Label>("%SpeedLabel").Text = $"Playback speed · {value:0.#}×";
		_settingsMenu.GetNode<OptionButton>("%Scale").ItemSelected += index =>
		{
			float factor = new[] { 1f, 1.25f, 1.5f, 2f }[(int)index];
			Vector2I physical = GetWindow().Size;
			GetWindow().ContentScaleSize = new Vector2I(Math.Max(1920, (int)(physical.X / factor)), Math.Max(1080, (int)(physical.Y / factor)));
		};
		_skipAnimations = header.GetNode<Button>("SkipAnimation");
		_skipAnimations.Pressed += () => { RecordFullMatchSelection("ui-control", "skip-animation"); _tablePlaybackWait = 0; _skipAnimation = true; _gameTable.StopMotion(); _beats.Stop(); };
		_sessionLogsButton = _settingsMenu.GetNode<Button>("%OpenLogs");
		_sessionLogsButton.Pressed += OpenSessionLogs;
		_settingsMenu.GetNode<Button>("%SaveGame").Pressed += () => ShowSaveGame(false);
		_settingsMenu.GetNode<Button>("%LoadGame").Pressed += ShowSaveBrowser;
		_settingsMenu.GetNode<Button>("%SaveAndQuit").Pressed += () => ShowSaveGame(true);
	}

	/// <summary>
	/// Passes the player's optional windows when the setting is on (U3 auto-pass).
	/// Scripted smoke, gate and capture runs keep every window, as they always have.
	/// </summary>
	private void ApplyAutoPassSetting()
	{
		if (_autoPass is null || _stopAtWindow is null) return;
		bool scripted = OS.GetCmdlineUserArgs().Any(arg => arg.Contains("smoke", StringComparison.Ordinal)
			|| arg.Contains("capture", StringComparison.Ordinal) || arg.Contains("gate", StringComparison.Ordinal));
		bool on = _autoPass.ButtonPressed && !scripted && !_interactionLab && _coordinator is not null;
		if (_coordinator is { } coordinator && coordinator.AutoPassWindows != on) coordinator.AutoPassWindows = on;
		bool benefits = _autoBenefitsEnabled && !scripted && !_interactionLab && _coordinator is not null;
		if (_coordinator is { } resolving && resolving.AutoResolveBenefits != benefits) resolving.AutoResolveBenefits = benefits;
		_stopAtWindow.Visible = on;
		_stopAtWindow.SetPressedNoSignal(on && _coordinator!.StopAtNextWindowRequested);
	}

	private void SetSettingsVisible(bool visible)
	{
		if (_settingsMenu is null) return;
		_settingsMenu.Visible = visible;
		if (visible)
		{
			bool canSave = _coordinator?.CurrentSnapshot is not null && _coordinator.SupportsPlayerSaves && !_interactionLab && !_manualSaveInFlight;
			_settingsMenu.GetNode<Button>("%SaveGame").Disabled = !canSave;
			_settingsMenu.GetNode<Button>("%SaveAndQuit").Disabled = !canSave;
			_boardDrag?.Cancel("settings", false);
			ClearTableSelection();
			HideCardHoverPreview();
			ApplyAccessibilityFocusOrder(_settingsMenu);
			_settingsMenu.GetNode<Button>("%Close").GrabFocus();
		}
		else _settingsButton?.GrabFocus();
		if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
	}
}
