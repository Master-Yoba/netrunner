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
	public void ResetPresentation()
	{
		StopMotion();
		Hand.ResetPresentation();
		_motionView = null;
		_lastBoardFingerprint = null;
	}

	public void StopMotion()
	{
		_motionRevision++;
		foreach (Tween tween in _motionTweens) if (tween.IsValid()) tween.Kill();
		_motionTweens.Clear();
		RestoreBeatMotion();
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
		foreach (TableCardControl card in _heldArrivals.Keys.ToArray()) ReleaseArrival(card);
	}

	private readonly Dictionary<TableCardControl, Color> _heldArrivals = new();
	private readonly Dictionary<Button, (int Shown, Tween? Tween)> _counts = new();

	/// <summary>
	/// Counts a number between values instead of jumping, so a credit change reads
	/// as a change. Reduced motion, disabled motion and the first value set it directly.
	/// </summary>
	private void ShowCount(Button label, int value)
	{
		string Format(int number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);
		if (_counts.TryGetValue(label, out var current) && current.Tween is { } running && running.IsValid()) running.Kill();
		int from = _counts.TryGetValue(label, out current) ? current.Shown : value;
		if (from == value || !MotionEnabled || Hand.ReducedMotion || MotionDuration <= 0 || !label.IsInsideTree())
		{
			_counts[label] = (value, null);
			label.Text = Format(value);
			return;
		}
		Tween tween = CreateTween();
		_counts[label] = (value, tween);
		tween.TweenMethod(Callable.From<float>(progress => label.Text = Format((int)Mathf.Round(Mathf.Lerp(from, value, progress)))),
			0.0f, 1.0f, MotionDuration * 1.5);
	}

	/// <summary>The rectangle of a card still held at its drop point, consumed when it settles.</summary>
	public Func<string, Rect2?>? TakePendingDropRect { get; set; }

	private const string MotionKeyMeta = "motion_key";

	/// <summary>
	/// Cards visible twice right now: a flight stand-in and the real card at the
	/// same time. The motion contract is that this is always empty (U2 gate).
	/// </summary>
	public IReadOnlyList<string> DuplicateMotionKeys() => [.. _motionCards
		.Where(ghost => IsInstanceValid(ghost) && ghost.IsVisibleInTree() && ghost.Modulate.A > 0.01f && ghost.HasMeta(MotionKeyMeta))
		.Select(ghost => ghost.GetMeta(MotionKeyMeta).AsString())
		.Where(key => _visibleCards.GetValueOrDefault(key) is { } real && real.IsVisibleInTree() && real.Modulate.A > 0.01f)
		.Distinct(StringComparer.Ordinal)];

	private void ReleaseArrival(TableCardControl card)
	{
		if (_heldArrivals.Remove(card, out Color modulate) && IsInstanceValid(card)) card.Modulate = modulate;
	}

	private sealed record MotionCard(CardViewModel? Card, Rect2 Rect, Transform2D Pose, Vector2 Size, bool IsDiscardPile);

	private bool IsScorePileFace(TableCardControl card) => card.GetParent() is { } parent
		&& (parent == _corpScoredPile || parent == _runnerScoredPile);

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
			PresentOpponentMotion(previousView, view, previous.Keys.ToHashSet(StringComparer.Ordinal));
			foreach (string id in incoming)
				if (previous.TryGetValue(id, out MotionCard? source)) PresentDiscardMotion(id, source, view.Viewer);
			foreach ((string id, var old) in previous)
			{
				if (selected.Contains(id) || old.IsDiscardPile || _visibleCards.ContainsKey(id) || old.Card is not { IsKnown: true }) continue;
				// Animate only a destination explicitly present in this viewer's
				// discard projection. Never track a remembered identity into a deck.
				Control? destination = view.CorpDiscard.Any(card => card.Id == id) ? _zonePiles.GetValueOrDefault("zone:corp-archives")
					: view.RunnerHeap.Any(card => card.Id == id) ? _runnerHeapPile : null;
				// The pile may already show this card on top: hold that face until the card lands.
				TableCardControl? face = _visibleCards.GetValueOrDefault(id) is { } top && IsDiscardPileFace(top) ? top : null;
				if (destination is not null)
					AnimateCard(old.Card, old.Rect, destination.GlobalPosition - GlobalPosition, false, arrival: face, key: id);
			}
			foreach ((string id, TableCardControl current) in _visibleCards)
			{
				// The fan owns hand reflow; arrivals use the dedicated deck-to-hand flight.
				if (Hand.ContainsCard(current) || selected.Contains(id)) continue;
				Rect2 rect = current.GetGlobalRect();
				if (!GetGlobalRect().Encloses(rect)) continue;
				// A revealed card moves from the key it had while face-down.
				if (previous.TryGetValue(id, out var old)
					|| view.RevealedFrom.TryGetValue(id, out string? tracked) && previous.TryGetValue(tracked, out old))
				{
					if (old.IsDiscardPile && IsDiscardPileFace(current)) continue;
					// Centers, so a card scaled in place (beat emphasis) doesn't read as moved.
					if (old.Rect.GetCenter().DistanceTo(rect.GetCenter()) > 8)
						AnimateCard(current.Card, TakePendingDropRect?.Invoke(id) ?? old.Rect, rect.Position - GlobalPosition, current.IsIce,
							current.BackSide, rect.Size, current, id);
					else if (!CardsMatch(old.Card, current.Card)) PulseCard(rect);
					continue;
				}
				// A newly exposed pile top is presentation bookkeeping, not a card
				// arriving from an inferred hand or the Corp header. An empty
				// pile keeps a hidden face that must not fly in on every update.
				if (IsDiscardPileFace(current) || IsScorePileFace(current) || !current.IsVisibleInTree()) continue;
				// A new face-down slot receives a neutral back, never a card
				// recovered from an authoritative action or hidden instance ID.
				bool runnerCard = view.RunnerRig.Any(card => card.Id == id);
				bool localCard = runnerCard == (view.Viewer == Netrunner.Core.PlayerSide.Runner);
				Vector2 source = localCard
					? _localHandRow.GlobalPosition + new Vector2(_localHandRow.Size.X / 2 - rect.Size.X / 2, 0)
					: OpponentLaunchRect((runnerCard ? view.RunnerHand : view.CorpHand).Count, rect.Size).Position;
				AnimateCard(current.Card, new Rect2(source, rect.Size), rect.Position - GlobalPosition, current.IsIce, current.BackSide,
					arrival: current, key: id);
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

	/// <summary>
	/// Flies a stand-in from <paramref name="source"/> to <paramref name="destination"/>.
	/// When the real card is already at the destination (<paramref name="arrival"/>),
	/// it is held invisible until the stand-in lands, so exactly one copy of a
	/// card is ever on screen.
	/// </summary>
	private void AnimateCard(CardViewModel? model, Rect2 source, Vector2 destination, bool ice, Netrunner.Core.PlayerSide side = Netrunner.Core.PlayerSide.Corp,
		Vector2? destinationSize = null, TableCardControl? arrival = null, string? key = null, Action? landed = null, double delay = 0)
	{
		if (arrival is not null && !_heldArrivals.ContainsKey(arrival))
		{
			_heldArrivals[arrival] = arrival.Modulate;
			arrival.Modulate = arrival.Modulate with { A = 0 };
		}
		// A portrait card becoming sideways ICE flies upright and flips into place, rather than squash.
		if (ice && destinationSize is { X: > 0, Y: > 0 } iceSize && iceSize.X > iceSize.Y && source.Size.Y > source.Size.X)
		{
			AnimateIceFlip(model, source, destination, iceSize, side, arrival, key, landed, delay);
			return;
		}
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
		if (key is not null) ghost.SetMeta(MotionKeyMeta, key);
		_motionCards.Add(ghost);
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		tween.SetParallel();
		// A queued flight waits out of sight so it never sits on its source pile.
		if (delay > 0)
		{
			ghost.Modulate = ghost.Modulate with { A = 0 };
			tween.TweenProperty(ghost, "modulate:a", 1.0, 0.01).SetDelay(delay);
		}
		tween.TweenProperty(ghost, "position", destination, MotionDuration).SetDelay(delay)
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		// Land at the destination's size, e.g. a mini score-pile card, not the source's.
		if (destinationSize is { X: > 0, Y: > 0 } size && source.Size is { X: > 0, Y: > 0 })
			tween.TweenProperty(ghost, "scale", size / source.Size, MotionDuration).SetDelay(delay)
				.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		// A departing card fades on its way out; an arriving one lands and hands over to the real card.
		if (arrival is null && landed is null)
			tween.TweenProperty(ghost, "modulate:a", 0.0, MotionDuration).SetDelay(delay + MotionDuration * 0.2);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			_motionTweens.Remove(tween);
			_motionCards.Remove(ghost);
			ghost.QueueFree();
			if (arrival is not null) ReleaseArrival(arrival);
			landed?.Invoke();
		}));
	}

	/// <summary>
	/// An ICE install (playtest, 30 September: scaling a portrait card straight to the sideways
	/// slot looked like a squash). The card flies upright at an even scale to the slot's centre,
	/// turns edge-on, and opens out sideways at the slot's size.
	/// </summary>
	private void AnimateIceFlip(CardViewModel? model, Rect2 source, Vector2 destination, Vector2 iceSize,
		Netrunner.Core.PlayerSide side, TableCardControl? arrival, string? key, Action? landed, double delay)
	{
		TableCardControl ghost = TableCardControl.Create();
		ghost.MouseFilter = MouseFilterEnum.Ignore;
		ghost.FocusMode = FocusModeEnum.None;
		ghost.ZIndex = 20;
		AddChild(ghost);
		ghost.SetCard(model);
		ghost.SetBackSide(side);
		ghost.SetIce(false);
		ghost.SetDisplaySize(source.Size);
		ghost.PivotOffset = source.Size / 2;
		ghost.Position = source.Position - GlobalPosition;
		if (key is not null) ghost.SetMeta(MotionKeyMeta, key);
		_motionCards.Add(ghost);
		// Upright, the card's width becomes the slot's height once it lies sideways.
		float scale = iceSize.Y / source.Size.X;
		Vector2 centre = destination + iceSize / 2;
		double flight = MotionDuration * 0.75, turn = MotionDuration * 0.14;
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		tween.SetParallel();
		if (delay > 0)
		{
			ghost.Modulate = ghost.Modulate with { A = 0 };
			tween.TweenProperty(ghost, "modulate:a", 1.0, 0.01).SetDelay(delay);
		}
		tween.TweenProperty(ghost, "position", centre - source.Size / 2, flight).SetDelay(delay)
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(ghost, "scale", Vector2.One * scale, flight).SetDelay(delay)
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		tween.Chain().TweenProperty(ghost, "scale:x", 0.0, turn).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			ghost.SetIce(true);
			ghost.SetDisplaySize(iceSize);
			ghost.PivotOffset = iceSize / 2;
			ghost.Position = destination;
			ghost.Scale = new Vector2(0, 1);
		}));
		tween.Chain().TweenProperty(ghost, "scale:x", 1.0, turn).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			_motionTweens.Remove(tween);
			_motionCards.Remove(ghost);
			ghost.QueueFree();
			if (arrival is not null) ReleaseArrival(arrival);
			landed?.Invoke();
		}));
	}
}
