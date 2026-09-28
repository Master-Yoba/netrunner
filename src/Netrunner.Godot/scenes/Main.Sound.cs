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

	private CheckButton? _tiltToggle;
	private bool _tiltEnabled = true;

	/// <summary>Experimental (U5): the hovered hand card leans toward the pointer. Scripted runs keep it off.</summary>
	private void BuildTilt()
	{
		if (!_preferencesPersist) _tiltEnabled = false;
		_gameTable.Hand.HoverTilt = _tiltEnabled;
		if (_settingsMenu?.GetNode<VBoxContainer>("Panel/Content") is not { } settings) return;
		_tiltToggle = new CheckButton
		{
			Name = "HoverTilt",
			Text = "Tilt hovered cards (experimental)",
			ButtonPressed = _tiltEnabled,
			TooltipText = "The card under the pointer leans toward it. Reduced motion keeps it straight.",
		};
		_tiltToggle.Toggled += enabled =>
		{
			_tiltEnabled = enabled;
			_gameTable.Hand.HoverTilt = enabled;
			SavePreferences();
		};
		settings.AddChild(_tiltToggle);
		settings.MoveChild(_tiltToggle, settings.GetNode("ReducedMotion").GetIndex() + 1);
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
