using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// U5 sound: every beat the table plays has a synthesized cue (see
/// <see cref="SoundBank"/>). Settings has a sound toggle and an effects volume,
/// kept with the other preferences.
/// </summary>
public partial class Main
{
	private SoundBank? _sound;
	private CheckButton? _soundToggle;
	private HSlider? _effectsVolume;
	private bool _soundEnabled = true;
	private float _effectsLevel = 0.7f;

	private CheckButton? _autoBenefitsToggle;
	private bool _autoBenefitsEnabled = true;

	/// <summary>
	/// Playtest, 29 September: on-success effects that only benefit the player (Pennyshaver, Conduit,
	/// Zahya) stacked up as buttons in the dock; they resolve by themselves unless this is turned off.
	/// </summary>
	private void BuildAutoBenefits()
	{
		if (_settingsMenu?.GetNode<VBoxContainer>("Panel/Content") is not { } settings) return;
		_autoBenefitsToggle = new CheckButton
		{
			Name = "AutoBenefits",
			Text = "Resolve beneficial effects automatically",
			ButtonPressed = _autoBenefitsEnabled,
			TooltipText = "Effects that only gain you credits, counters or cards, with nothing to choose, resolve on their own.",
		};
		_autoBenefitsToggle.Toggled += enabled =>
		{
			_autoBenefitsEnabled = enabled;
			SavePreferences();
			ApplyAutoPassSetting();
		};
		settings.AddChild(_autoBenefitsToggle);
		settings.MoveChild(_autoBenefitsToggle, settings.GetNode("AutoPass").GetIndex() + 1);
	}

	private void BuildSound()
	{
		_sound = new SoundBank { Name = "SoundBank", Muted = !_soundEnabled, Volume = _effectsLevel };
		AddChild(_sound);
		_beats.BeatStarted += beat => _sound.Play(SoundCues.For(beat, _beats.Viewer));
		if (_settingsMenu?.GetNode<VBoxContainer>("Panel/Content") is not { } settings) return;
		_soundToggle = new CheckButton { Name = "Sound", Text = "Sound", ButtonPressed = _soundEnabled };
		_soundToggle.Toggled += enabled =>
		{
			_soundEnabled = enabled;
			_sound.Muted = !enabled;
			if (_effectsVolume is not null) _effectsVolume.Editable = enabled;
			SavePreferences();
		};
		var label = new Label { Name = "EffectsVolumeLabel", Text = EffectsVolumeText(_effectsLevel) };
		_effectsVolume = new HSlider
		{
			Name = "EffectsVolume",
			MinValue = 0,
			MaxValue = 1,
			Step = 0.05,
			Value = _effectsLevel,
			CustomMinimumSize = new Vector2(0, 28),
			Editable = _soundEnabled,
			AccessibilityName = "Effects volume",
		};
		_effectsVolume.ValueChanged += value =>
		{
			_effectsLevel = (float)value;
			_sound.Volume = _effectsLevel;
			label.Text = EffectsVolumeText(_effectsLevel);
		};
		_effectsVolume.DragEnded += _ => { SavePreferences(); _sound.Play(SoundCue.Credit); };
		int after = settings.GetNode("Speed").GetIndex() + 1;
		foreach (Control control in new Control[] { _soundToggle, label, _effectsVolume })
		{
			settings.AddChild(control);
			settings.MoveChild(control, after++);
		}
	}

	private static string EffectsVolumeText(float level) => $"Effects volume · {level * 100:0}%";
}
