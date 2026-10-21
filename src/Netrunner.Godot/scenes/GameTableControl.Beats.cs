using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

/// <summary>Board hooks the beat presenter uses to direct attention (UI plan U3).</summary>
public partial class GameTableControl
{
	private const float UnfocusedLaneAlpha = 0.45f;
	private string? _focusedServer;
	private readonly Dictionary<TableCardControl, Tween> _emphasis = new();
	private string? _emphasizedKey;

	/// <summary>Dims every server lane but <paramref name="serverId"/>; null restores them all.</summary>
	public void FocusServer(string? serverId)
	{
		_focusedServer = serverId;
		ApplyServerFocus();
	}

	private void ApplyServerFocus()
	{
		foreach ((string id, ServerLaneParts lane) in _serverLanes)
			lane.Panel.Modulate = lane.Panel.Modulate with
			{
				A = _focusedServer is null || string.Equals(id, _focusedServer, StringComparison.Ordinal) ? 1 : UnfocusedLaneAlpha,
			};
		BreatheLane(_focusedServer is { } focused && _serverLanes.TryGetValue(focused, out ServerLaneParts? parts) ? parts.Panel : null);
	}

	private Control? _breathingLane;
	private double _breathSeconds;

	/// <summary>
	/// Idle life (U5): the attacked server's lane glows slowly while its run
	/// lasts. Only the panel's own background brightens, not its cards; reduced
	/// motion keeps the lane still. <see cref="Breathe"/> drives it each frame.
	/// </summary>
	private void BreatheLane(Control? lane)
	{
		if (ReferenceEquals(lane, _breathingLane)) return;
		if (_breathingLane is not null && IsInstanceValid(_breathingLane)) _breathingLane.SelfModulate = Colors.White;
		_breathingLane = lane;
		_breathSeconds = 0;
	}

	private void Breathe(double delta)
	{
		if (_breathingLane is null) return;
		if (!IsInstanceValid(_breathingLane)) { _breathingLane = null; return; }
		if (!MotionEnabled || Hand.ReducedMotion)
		{
			_breathingLane.SelfModulate = Colors.White;
			return;
		}
		_breathSeconds += delta;
		float glow = 1 + 0.15f * (1 - Mathf.Cos((float)(_breathSeconds * Math.PI / 1.2)));
		_breathingLane.SelfModulate = new Color(glow, glow, glow);
	}

	/// <summary>Grows a card in place (an encountered ICE) until released; reduced motion only raises it.</summary>
	public void EmphasizeCard(string? key, bool on)
	{
		if (key is null) return;
		if (on && _emphasizedKey is { } previous && previous != key) EmphasizeCard(previous, false);
		if (on) _emphasizedKey = key;
		else if (_emphasizedKey == key) _emphasizedKey = null;
		if (!_visibleCards.TryGetValue(key, out TableCardControl? card)) return;
		if (_emphasis.Remove(card, out Tween? running) && running.IsValid()) running.Kill();
		card.PivotOffset = card.Size * 0.5f;
		card.ZIndex = on ? 30 : 0;
		float scale = on ? 1.12f : 1f;
		if (!MotionEnabled || Hand.ReducedMotion || MotionDuration <= 0)
		{
			card.Scale = Vector2.One * (on ? 1.04f : 1f);
			return;
		}
		Tween tween = CreateTween();
		tween.TweenProperty(card, "scale", Vector2.One * scale, MotionDuration).SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.Out);
		_emphasis[card] = tween;
	}

	/// <summary>Returns the emphasized card, if any, to its normal size.</summary>
	public void ReleaseEmphasis() => EmphasizeCard(_emphasizedKey, false);

	/// <summary>Flashes a card's outline, for a fired subroutine, a new counter or a score.</summary>
	public void PulseCardKey(string? key)
	{
		if (key is not null && _visibleCards.TryGetValue(key, out TableCardControl? card) && card.IsVisibleInTree())
			PulseCard(card.GetGlobalRect());
	}

	/// <summary>Where a server's name sits on screen, for anchoring its action chips.</summary>
	public Rect2? GetServerHeaderRect(string serverId) =>
		_serverLanes.TryGetValue(serverId, out ServerLaneParts? lane) && lane.Header.IsVisibleInTree() ? lane.Header.GetGlobalRect() : null;

	/// <summary>Flashes a public pile or score area a beat moved a card into or out of.</summary>
	public void PulseZone(ZoneKind? zone)
	{
		Control? target = zone switch
		{
			ZoneKind.CorpDeck => _zonePiles.GetValueOrDefault("zone:corp-deck"),
			ZoneKind.CorpDiscard => _zonePiles.GetValueOrDefault("zone:corp-archives"),
			ZoneKind.RunnerStack => _runnerDeckPile,
			ZoneKind.RunnerHeap => _runnerHeapPile,
			ZoneKind.CorpScore => _corpScoredPile,
			ZoneKind.RunnerScore => _runnerScoredPile,
			_ => null,
		};
		Pulse(target);
	}

	/// <summary>Flashes a side's credit pool.</summary>
	public void PulseCredits(PlayerSide? side) => Pulse(side == PlayerSide.Corp ? _corpCredits : _runnerCredits);

	/// <summary>Flashes a side's clicks, points and tags line, for a new turn or a tag.</summary>
	public void PulseStats(PlayerSide? side) => Pulse(side == PlayerSide.Corp ? _corpResources : _runnerResources);

	/// <summary>A red wash over the board for damage; reduced motion keeps the tint and drops the fade-in.</summary>
	public void FlashDamage()
	{
		var wash = new ColorRect
		{
			MouseFilter = MouseFilterEnum.Ignore,
			Color = new Color(0.85f, 0.1f, 0.12f, 0.22f),
			ZIndex = 25,
			Size = Size,
		};
		AddChild(wash);
		_motionCards.Add(wash);
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		if (MotionEnabled && !Hand.ReducedMotion) tween.TweenProperty(wash, "color:a", 0.32, 0.12);
		tween.TweenProperty(wash, "color:a", 0.0, 0.5);
		tween.TweenCallback(Callable.From(() => { _motionTweens.Remove(tween); _motionCards.Remove(wash); wash.QueueFree(); }));
	}

	/// <summary>A rez turns the card over: it narrows to its edge and opens again (U5). Reduced motion keeps the pulse only.</summary>
	public void FlipCard(string? key)
	{
		if (!MotionEnabled || Hand.ReducedMotion || key is null || !_visibleCards.TryGetValue(key, out TableCardControl? card)
			|| !card.IsVisibleInTree()) return;
		float width = _flipping.TryGetValue(card, out float flipping) ? flipping : card.Scale.X;
		_flipping[card] = width;
		card.PivotOffset = card.Size * 0.5f;
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		tween.TweenProperty(card, "scale:x", 0.0, 0.1).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Sine);
		tween.TweenProperty(card, "scale:x", width, 0.16).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
		tween.TweenCallback(Callable.From(() => { _motionTweens.Remove(tween); _flipping.Remove(card); if (IsInstanceValid(card)) card.Scale = card.Scale with { X = width }; }));
	}

	/// <summary>A brief shake of the table for damage (U5); none with reduced motion.</summary>
	public void Shake(float strength)
	{
		if (!MotionEnabled || Hand.ReducedMotion || _shaking) return;
		_shaking = true;
		Vector2 home = Position;
		_shakeHome = home;
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		float[] offsets = [1f, -0.8f, 0.6f, -0.4f, 0.2f];
		for (int index = 0; index < offsets.Length; index++)
			tween.TweenProperty(this, "position", home + new Vector2(offsets[index] * strength, (index % 2 == 0 ? 0.5f : -0.5f) * strength), 0.035);
		tween.TweenProperty(this, "position", home, 0.04);
		tween.TweenCallback(Callable.From(() => { _motionTweens.Remove(tween); Position = home; _shaking = false; _shakeHome = null; }));
	}

	private bool _shaking;
	private Vector2? _shakeHome;
	private readonly Dictionary<TableCardControl, float> _flipping = [];

	/// <summary>After motion is stopped mid-beat, put a flipped card and a shaken table back.</summary>
	private void RestoreBeatMotion()
	{
		foreach ((TableCardControl card, float width) in _flipping)
			if (IsInstanceValid(card)) card.Scale = card.Scale with { X = width };
		_flipping.Clear();
		if (_shakeHome is { } home) Position = home;
		_shakeHome = null;
		_shaking = false;
	}

	private void Pulse(Control? target)
	{
		if (target is not null && IsInstanceValid(target) && target.IsVisibleInTree()) PulseCard(target.GetGlobalRect());
	}

	/// <summary>A render rebuilds poses; keep the current focus and emphasis.</summary>
	private void ReapplyBeatEffects()
	{
		ApplyServerFocus();
		if (_emphasizedKey is { } key && _visibleCards.TryGetValue(key, out TableCardControl? card))
		{
			card.PivotOffset = card.Size * 0.5f;
			card.ZIndex = 30;
			card.Scale = Vector2.One * (!MotionEnabled || Hand.ReducedMotion ? 1.04f : 1.12f);
		}
	}

	/// <summary>Clears every beat effect: lane focus and card emphasis.</summary>
	public void ClearBeatEffects()
	{
		_emphasizedKey = null;
		FocusServer(null);
		foreach ((TableCardControl card, Tween tween) in _emphasis.ToArray())
		{
			if (tween.IsValid()) tween.Kill();
			if (IsInstanceValid(card))
			{
				card.Scale = Vector2.One;
				card.ZIndex = 0;
			}
		}
		_emphasis.Clear();
	}
}
