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
