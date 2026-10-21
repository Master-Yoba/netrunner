using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class GameTableControl
{
	/// <summary>How long a card the opponent plays stays revealed mid-table before it goes to the discard pile.</summary>
	public const double PlayedCardHoldSeconds = 0.55;

	private OpponentHandFan? OpponentHand => GetNodeOrNull<OpponentHandFan>("OpponentHand");

	/// <summary>The opponent's hand fan shows only the public hand size.</summary>
	private void UpdateOpponentHand(MatchViewModel view)
	{
		if (OpponentHand is not { } fan) return;
		bool corpOpponent = view.Viewer == PlayerSide.Runner;
		fan.SetSide(corpOpponent ? PlayerSide.Corp : PlayerSide.Runner,
			corpOpponent ? _corpFaction.Accent : _runnerFaction.Accent);
		fan.ReducedMotion = Hand.ReducedMotion || !MotionEnabled;
		fan.SetCount(corpOpponent ? view.CorpHand.Count : view.RunnerHand.Count);
	}

	/// <summary>Where a card leaving the opponent's hand starts, in global coordinates.</summary>
	private Rect2 OpponentLaunchRect(int handCountAfter, Vector2 size)
	{
		if (OpponentHand is { } fan)
		{
			Rect2 slot = fan.LaunchGlobalRect(handCountAfter);
			return new Rect2(slot.GetCenter() - size / 2, size);
		}
		return new Rect2(GlobalPosition + new Vector2(Size.X / 2, 0) - size / 2, size);
	}

	private void PresentOpponentMotion(MatchViewModel? previous, MatchViewModel view, HashSet<string> previouslyVisible)
	{
		if (previous is null || previous.Viewer != view.Viewer) return;
		bool corp = view.Viewer == PlayerSide.Runner;
		PlayerSide side = corp ? PlayerSide.Corp : PlayerSide.Runner;
		PlayerViewModel before = corp ? previous.Corp : previous.Runner;
		PlayerViewModel after = corp ? view.Corp : view.Runner;
		if (before.Credits != after.Credits || before.Clicks != after.Clicks)
			PulseCard((corp ? _corpCredits : _runnerCredits).GetGlobalRect());
		ZoneViewModel oldHand = corp ? previous.CorpHand : previous.RunnerHand;
		ZoneViewModel hand = corp ? view.CorpHand : view.RunnerHand;
		OpponentHandFan? fan = OpponentHand;
		Vector2 flightSize = TableCardControl.PortraitCardSize * 0.6f;
		Control? discard = corp ? _zonePiles.GetValueOrDefault("zone:corp-archives") : _runnerHeapPile;
		IReadOnlyList<CardViewModel> pile = corp ? view.CorpDiscard : view.RunnerHeap;
		IReadOnlyList<CardViewModel> oldPile = corp ? previous.CorpDiscard : previous.RunnerHeap;
		int discarded = corp ? view.CorpDiscardCount - previous.CorpDiscardCount : view.RunnerHeap.Count - previous.RunnerHeap.Count;

		// Cards that reached the pile from the hand, not from the table: the
		// newly public ones the viewer never saw installed.
		CardViewModel[] fromHand = pile.Where(card => card.IsKnown && !oldPile.Any(old => old.Id == card.Id)
			&& !previouslyVisible.Contains(card.Id)).ToArray();
		double delay = 0;
		if (discard is not null && fromHand.Length == 1 && discarded == 1)
		{
			// One card played from hand (an operation or event): show it, then file it.
			CardViewModel played = fromHand[0];
			TableCardControl? face = _visibleCards.GetValueOrDefault(played.Id) is { } shown && IsDiscardPileFace(shown) ? shown : null;
			AnimatePlayedCard(played, OpponentLaunchRect(hand.Count, flightSize), discard, side, face, played.Id);
			delay = MotionDuration * 2 + PlayedCardHoldSeconds;
		}
		else if (discard is not null && discarded > 0 && hand.Count < oldHand.Count)
		{
			// Discards from hand (end of turn, damage) fly face down.
			for (int index = 0; index < Math.Min(oldHand.Count - hand.Count, 5); index++)
				AnimateCard(null, OpponentLaunchRect(hand.Count + index, flightSize), discard.GlobalPosition - GlobalPosition, false, side);
		}

		int deckLoss = (corp ? previous.CorpDeck.Count : previous.RunnerDeck.Count) - (corp ? view.CorpDeck.Count : view.RunnerDeck.Count);
		int handGain = hand.Count - oldHand.Count + (delay > 0 ? 1 : 0);
		int drawn = Math.Min(deckLoss, handGain);
		Control? deck = corp ? _zonePiles.GetValueOrDefault("zone:corp-deck") : _runnerDeckPile;
		if (drawn <= 0 || deck is null) return;
		// Public counts only: no opponent card identities are available here.
		int flights = Math.Min(drawn, 5);
		fan?.HoldArrivals(flights);
		for (int index = 0; index < flights; index++)
		{
			Vector2 offset = new(index * 8, index * 5);
			Rect2 target = fan?.SlotGlobalRect(hand.Count - flights + index)
				?? new Rect2(_corpHandCaption.GlobalPosition, flightSize);
			AnimateCard(null, new Rect2(deck.GlobalPosition + offset, flightSize),
				target.Position - GlobalPosition, false, side, target.Size, landed: () => fan?.ReleaseArrival(),
				delay: delay + index * 0.06);
		}
	}

	/// <summary>
	/// Flies a card the opponent played from their hand fan to the middle of the
	/// table, holds it face up long enough to read, then files it on the pile.
	/// </summary>
	private void AnimatePlayedCard(CardViewModel card, Rect2 source, Control pile, PlayerSide side, TableCardControl? arrival, string key)
	{
		if (arrival is not null && !_heldArrivals.ContainsKey(arrival))
		{
			_heldArrivals[arrival] = arrival.Modulate;
			arrival.Modulate = arrival.Modulate with { A = 0 };
		}
		TableCardControl ghost = TableCardControl.Create();
		ghost.MouseFilter = MouseFilterEnum.Ignore;
		ghost.FocusMode = FocusModeEnum.None;
		ghost.ZIndex = 40;
		AddChild(ghost);
		ghost.SetCard(card);
		ghost.SetBackSide(side);
		ghost.SetDisplaySize(source.Size);
		ghost.PivotOffset = source.Size / 2;
		ghost.Position = source.Position - GlobalPosition;
		ghost.SetMeta(MotionKeyMeta, key);
		_motionCards.Add(ghost);
		const float showScale = 1.7f;
		Vector2 centre = Size * new Vector2(0.5f, 0.3f) - source.Size / 2;
		Vector2 landing = pile.GlobalPosition - GlobalPosition;
		Vector2 pileScale = pile.Size is { X: > 0, Y: > 0 } pileSize ? pileSize / source.Size : Vector2.One;
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		tween.SetParallel();
		tween.TweenProperty(ghost, "position", centre, MotionDuration).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(ghost, "scale", Vector2.One * showScale, MotionDuration).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		tween.Chain().TweenInterval(PlayedCardHoldSeconds);
		tween.Chain().TweenProperty(ghost, "position", landing - source.Size * (Vector2.One - pileScale) / 2, MotionDuration)
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(ghost, "scale", pileScale, MotionDuration).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			_motionTweens.Remove(tween);
			_motionCards.Remove(ghost);
			ghost.QueueFree();
			if (arrival is not null) ReleaseArrival(arrival);
		}));
	}
}
