using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class GameTableControl
{
	public bool MotionEnabled { get; set; } = true;
	public const double DefaultMotionDuration = 0.32;
	public double MotionDuration { get; set; } = DefaultMotionDuration;
	private readonly List<Control> _motionCards = new();
	private readonly List<Tween> _motionTweens = new();
	private int _motionRevision;
	private MatchViewModel? _motionView;
	public int ActiveMotionCount => _motionCards.Count;
	public void StopMotion()
	{
		_motionRevision++;
		foreach (Tween tween in _motionTweens) if (tween.IsValid()) tween.Kill();
		_motionTweens.Clear();
		foreach (Control ghost in _motionCards)
			if (IsInstanceValid(ghost)) ghost.QueueFree();
		_motionCards.Clear();
		foreach (TableCardControl card in _drawArrivals)
			if (IsInstanceValid(card)) card.Show();
		_drawArrivals.Clear();
		foreach (TableCardControl card in _discardArrivals)
			if (IsInstanceValid(card)) card.Show();
		_discardArrivals.Clear();
		_discardFlights.Clear();
		_pendingDiscardMotion = 0;
	}

	private sealed record MotionCard(CardViewModel? Card, Rect2 Rect, Transform2D Pose, Vector2 Size, bool IsDiscardPile);

	private bool IsDiscardPileFace(TableCardControl card) => card.GetParent() == _runnerHeapPile
		|| (_zonePiles.TryGetValue("zone:corp-archives", out BoardActionButton? archives) && card.GetParent() == archives);

	private Dictionary<string, MotionCard> CaptureMotionCards()
	{
		StopMotion();
		return _visibleCards.Where(pair => pair.Value.IsVisibleInTree() && GetGlobalRect().Intersects(pair.Value.GetGlobalRect()))
			.ToDictionary(pair => pair.Key, pair => new MotionCard(pair.Value.Card, pair.Value.GetGlobalRect(),
				GetGlobalTransform().AffineInverse() * pair.Value.GetGlobalTransform(), pair.Value.Size, IsDiscardPileFace(pair.Value)), StringComparer.Ordinal);
	}

	private void PresentMotion(Dictionary<string, MotionCard> previous, MatchViewModel view, MatchViewModel? previousView)
	{
		if (previous.Count == 0) return;
		var (selected, incoming) = DiscardChanges(previousView, view);
		_pendingDiscardMotion = MotionEnabled && !Hand.ReducedMotion && MotionDuration > 0 ? incoming.Count : 0;
		int revision = _motionRevision;
		Callable.From(() =>
		{
			if (!IsInsideTree() || revision != _motionRevision) return;
			_pendingDiscardMotion = 0;
			if (!MotionEnabled)
			{
				foreach ((string id, TableCardControl current) in _visibleCards)
					if (current.IsVisibleInTree() && GetGlobalRect().Encloses(current.GetGlobalRect())
						&& (!previous.TryGetValue(id, out var old) || !CardsMatch(old.Card, current.Card)))
						PulseCard(current.GetGlobalRect());
				return;
			}
			foreach (string id in incoming)
				if (previous.TryGetValue(id, out MotionCard? source)) PresentDiscardMotion(id, source, view.Viewer);
			foreach ((string id, var old) in previous)
			{
				if (selected.Contains(id) || old.IsDiscardPile || _visibleCards.ContainsKey(id) || old.Card is not { IsKnown: true }) continue;
				// Animate only a destination explicitly present in this viewer's
				// discard projection. Never track a remembered identity into a deck.
				Control? destination = view.CorpDiscard.Any(card => card.Id == id) ? _zonePiles.GetValueOrDefault("zone:corp-archives")
					: view.RunnerHeap.Any(card => card.Id == id) ? _runnerHeapPile : null;
				if (destination is not null)
					AnimateCard(old.Card, old.Rect, destination.GlobalPosition - GlobalPosition, false);
			}
			foreach ((string id, TableCardControl current) in _visibleCards)
			{
				// The fan owns hand reflow; arrivals use the dedicated deck-to-hand flight.
				if (Hand.ContainsCard(current) || selected.Contains(id)) continue;
				Rect2 rect = current.GetGlobalRect();
				if (!GetGlobalRect().Encloses(rect)) continue;
				if (previous.TryGetValue(id, out var old))
				{
					if (old.IsDiscardPile && IsDiscardPileFace(current)) continue;
					if (old.Rect.Position.DistanceTo(rect.Position) > 8)
						AnimateCard(current.Card, old.Rect, rect.Position - GlobalPosition, current.IsIce, current.BackSide);
					continue;
				}
				// A newly exposed pile top is presentation bookkeeping, not a card
				// arriving from an inferred hand or the Corp header.
				if (IsDiscardPileFace(current)) continue;
				// A new face-down slot receives a neutral back, never a card
				// recovered from an authoritative action or hidden instance ID.
				Vector2 source = view.RunnerRig.Any(card => card.Id == id)
					? _localHandRow.GlobalPosition : _corpHandCaption.GlobalPosition;
				AnimateCard(current.Card, new Rect2(source, rect.Size), rect.Position - GlobalPosition, current.IsIce, current.BackSide);
			}
		}).CallDeferred();
	}

	private void PulseCard(Rect2 rect)
	{
		Panel ghost = new() { MouseFilter = MouseFilterEnum.Ignore, Position = rect.Position - GlobalPosition, Size = rect.Size, ZIndex = 20 };
		ghost.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = Colors.Transparent,
			BorderColor = WarningColor,
			BorderWidthLeft = 3,
			BorderWidthRight = 3,
			BorderWidthTop = 3,
			BorderWidthBottom = 3,
		});
		AddChild(ghost);
		_motionCards.Add(ghost);
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		tween.TweenProperty(ghost, "modulate:a", 0.0, 0.3);
		tween.TweenCallback(Callable.From(() => { _motionTweens.Remove(tween); _motionCards.Remove(ghost); ghost.QueueFree(); }));
	}

	private void AnimateCard(CardViewModel? model, Rect2 source, Vector2 destination, bool ice, Netrunner.Core.PlayerSide side = Netrunner.Core.PlayerSide.Corp)
	{
		TableCardControl ghost = TableCardControl.Create();
		ghost.MouseFilter = MouseFilterEnum.Ignore;
		ghost.FocusMode = FocusModeEnum.None;
		ghost.ZIndex = 20;
		AddChild(ghost);
		ghost.MouseFilter = MouseFilterEnum.Ignore;
		ghost.FocusMode = FocusModeEnum.None;
		ghost.SetCard(model);
		ghost.SetBackSide(side);
		ghost.SetIce(ice);
		ghost.SetDisplaySize(source.Size);
		ghost.Position = source.Position - GlobalPosition;
		_motionCards.Add(ghost);
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		tween.SetParallel();
		tween.TweenProperty(ghost, "position", destination, MotionDuration).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(ghost, "modulate:a", 0.0, MotionDuration).SetDelay(MotionDuration * 0.2);
		tween.Chain().TweenCallback(Callable.From(() => { _motionTweens.Remove(tween); _motionCards.Remove(ghost); ghost.QueueFree(); }));
	}
}
