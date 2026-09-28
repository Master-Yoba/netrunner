using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Plays beats from <see cref="BeatPlanner"/> in order (UI plan U3): a caption
/// banner over the board, the run's server brought into focus, the encountered
/// ICE enlarged, and pulses for fired subroutines, counters and scores. The
/// board itself has already settled to the new view; beats direct the eye
/// through what changed and set the pace of playback.
/// </summary>
public partial class BeatPresenter : Control
{
	private static readonly Color CorpColor = new(0.78f, 0.55f, 0.98f);
	private static readonly Color RunnerColor = new(0.43f, 0.94f, 0.72f);
	private static readonly Color HighColor = new(0.98f, 0.76f, 0.37f);
	private readonly Queue<Beat> _queue = new();
	private PanelContainer _banner = null!;
	private Label _caption = null!;
	private StyleBoxFlat _style = null!;
	private double _remaining;

	public GameTableControl Board { get; set; } = null!;

	/// <summary>Speed, fast opponent and reduced motion, from the settings.</summary>
	public BeatPacing Pacing { get; set; } = new();

	/// <summary>The local seat: its own low-emphasis beats play briefly.</summary>
	public PlayerSide Viewer { get; set; } = PlayerSide.Runner;

	private bool ReducedMotion => Pacing.ReducedMotion;

	/// <summary>Raised as each beat starts to play, for capture evidence.</summary>
	public event Action<Beat>? BeatStarted;

	/// <summary>Beats shown since the session started, for capture evidence.</summary>
	public List<(PresentationEventKind Kind, string Caption)> Played { get; } = [];

	public bool IsPlaying => _remaining > 0 || _queue.Count > 0;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		SetAnchorsPreset(LayoutPreset.FullRect);
		_style = new StyleBoxFlat
		{
			BgColor = new Color(0.03f, 0.07f, 0.08f, 0.92f),
			BorderWidthBottom = 3,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			ContentMarginLeft = 22,
			ContentMarginRight = 22,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
		};
		_banner = new PanelContainer { Name = "BeatBanner", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
		_banner.AddThemeStyleboxOverride("panel", _style);
		_caption = new Label { Name = "BeatCaption", HorizontalAlignment = HorizontalAlignment.Center };
		_caption.AddThemeFontSizeOverride("font_size", 22);
		_banner.AddChild(_caption);
		AddChild(_banner);
	}

	/// <summary>Queues beats and returns their total length at the current <see cref="Pacing"/>, in seconds.</summary>
	public double Play(IReadOnlyList<Beat> beats)
	{
		double total = 0;
		foreach (Beat beat in beats)
		{
			_queue.Enqueue(beat);
			total += Pacing.Seconds(beat, Viewer);
		}
		if (_remaining <= 0) Next();
		return total;
	}

	/// <summary>Drops every queued beat and clears the board's beat effects (skip, new game).</summary>
	public void Stop()
	{
		_queue.Clear();
		_remaining = 0;
		_linger = 0;
		if (_banner is not null) _banner.Visible = false;
		Board.ClearBeatEffects();
	}

	/// <summary>How long the last caption stays up after its beat, so it can be read while the player decides.</summary>
	private const double LingerSeconds = 2.0;
	private double _linger;

	public override void _Process(double delta)
	{
		if (_remaining > 0)
		{
			_remaining -= delta;
			if (_remaining <= 0) Next();
			return;
		}
		if (_linger <= 0) return;
		_linger -= delta;
		if (_linger <= 0) _banner.Visible = false;
	}

	private void Next()
	{
		if (!_queue.TryDequeue(out Beat? beat))
		{
			_remaining = 0;
			_linger = LingerSeconds / Math.Max(0.1, Pacing.Speed);
			return;
		}
		_linger = 0;
		_remaining = Pacing.Seconds(beat, Viewer);
		Played.Add((beat.Kind, beat.Caption));
		Show(beat);
		Direct(beat);
		BeatStarted?.Invoke(beat);
	}

	private void Show(Beat beat)
	{
		_caption.Text = beat.Caption;
		Color accent = beat.Emphasis == BeatEmphasis.High ? HighColor : beat.Side == PlayerSide.Corp ? CorpColor : RunnerColor;
		_style.BorderColor = accent;
		_caption.AddThemeColorOverride("font_color", beat.Emphasis == BeatEmphasis.Low ? accent.Lerp(Colors.White, 0.6f) : Colors.White);
		_banner.Visible = true;
		_banner.ResetSize();
		// The empty lower half of the server field, clear of lane headers and cards.
		_banner.Position = new Vector2((Size.X - _banner.Size.X) / 2, Size.Y * 0.42f);
		if (ReducedMotion) return;
		_banner.PivotOffset = _banner.Size / 2;
		_banner.Scale = Vector2.One * (beat.Emphasis == BeatEmphasis.High ? 1.12f : 1.04f);
		CreateTween().TweenProperty(_banner, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
	}

	/// <summary>Points the board at what the beat is about.</summary>
	private void Direct(Beat beat)
	{
		switch (beat.Kind)
		{
			case PresentationEventKind.TurnStarted:
				Board.PulseStats(beat.Side);
				break;
			case PresentationEventKind.CardDrawn:
				Board.PulseZone(beat.Side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack);
				break;
			case PresentationEventKind.CreditsChanged:
				Board.PulseCredits(beat.Side);
				break;
			case PresentationEventKind.RunStarted:
				Board.FocusServer(beat.ServerId);
				break;
			case PresentationEventKind.IceApproached:
			case PresentationEventKind.EncounterStarted:
				Board.EmphasizeCard(beat.CardKey, true);
				break;
			case PresentationEventKind.SubroutinesBroken:
				Board.PulseCardKey(beat.CardKey);
				Board.PulseCardKey(beat.SourceCardKey);
				break;
			case PresentationEventKind.AgendaScored:
			case PresentationEventKind.AgendaStolen:
				Board.PulseCardKey(beat.CardKey);
				Board.PulseZone(beat.Kind == PresentationEventKind.AgendaScored ? ZoneKind.CorpScore : ZoneKind.RunnerScore);
				break;
			case PresentationEventKind.CardRezzed:
				Board.FlipCard(beat.CardKey);
				Board.PulseCardKey(beat.CardKey);
				break;
			case PresentationEventKind.SubroutineFired:
			case PresentationEventKind.CardAdvanced:
			case PresentationEventKind.CardAccessed:
			case PresentationEventKind.CardInstalled:
			case PresentationEventKind.CardPlayed:
			case PresentationEventKind.BreakerPumped:
				Board.PulseCardKey(beat.CardKey);
				break;
			case PresentationEventKind.CardMoved:
				Board.PulseZone(beat.ToZone);
				break;
			case PresentationEventKind.DamageDealt:
				Board.FlashDamage();
				Board.Shake(Math.Clamp(4f + 2f * (beat.Amount ?? 1), 4f, 12f));
				Board.PulseZone(ZoneKind.RunnerHeap);
				break;
			case PresentationEventKind.TagsChanged:
				Board.PulseStats(PlayerSide.Runner);
				break;
			case PresentationEventKind.ServerApproached:
				Board.ReleaseEmphasis();
				break;
			case PresentationEventKind.RunEnded:
			case PresentationEventKind.GameEnded:
				Board.ClearBeatEffects();
				break;
		}
	}
}
