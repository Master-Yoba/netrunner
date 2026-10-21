using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class GameTableControl
{
	private readonly HashSet<TableCardControl> _drawArrivals = new();
	public int DrawMotionCount => _drawArrivals.Count;

	private sealed record DrawPlan(Rect2 Source, IReadOnlyList<string> CardIds);

	private DrawPlan? CaptureDrawPlan(MatchViewModel? previous, MatchViewModel view)
	{
		if (previous is null || previous.Viewer != view.Viewer || !MotionEnabled || MotionDuration <= 0
			|| Hand.ReducedMotion || Hand.DrawSeconds <= 0 || Engine.IsEditorHint()) return null;
		bool corp = view.Viewer == PlayerSide.Corp;
		int deckLoss = (corp ? previous.CorpDeck.Count : previous.RunnerDeck.Count)
			- (corp ? view.CorpDeck.Count : view.RunnerDeck.Count);
		if (deckLoss <= 0) return null;
		Control? pile = corp ? _zonePiles.GetValueOrDefault("zone:corp-deck") : _runnerDeckPile;
		if (pile is null || !pile.IsVisibleInTree()) return null;

		// Only animate newly observed local hand cards alongside an observed deck loss.
		// Exclude cards returning from known zones; never obtain identities from events or decks.
		HashSet<string> knownOutsideDeck = previous.CorpHand.VisibleCards.Concat(previous.RunnerHand.VisibleCards)
			.Concat(previous.CorpDiscard).Concat(previous.RunnerHeap).Concat(previous.CorpScore).Concat(previous.RunnerScore)
			.Select(card => card.Id).Concat(_visibleCards.Keys).ToHashSet(StringComparer.Ordinal);
		string[] incoming = (corp ? view.CorpHand : view.RunnerHand).VisibleCards
			.Where(card => card.IsKnown && !knownOutsideDeck.Contains(card.Id)).Take(deckLoss).Select(card => card.Id).ToArray();
		return incoming.Length == 0 ? null : new(pile.GetGlobalRect(), incoming);
	}

	private void PresentDrawMotion(DrawPlan? plan, PlayerSide side)
	{
		if (plan is null) return;
		foreach ((string id, int index) in plan.CardIds.Select((id, index) => (id, index)))
		{
			if (!_visibleCards.TryGetValue(id, out TableCardControl? destination)
				|| destination.Card is not { IsKnown: true } model || !Hand.ContainsCard(destination)) continue;
			destination.Hide();
			_drawArrivals.Add(destination);
			TableCardControl ghost = TableCardControl.Create();
			ghost.Name = $"DrawFlight_{_motionRevision}_{index}";
			ghost.ZIndex = 70 + index;
			AddChild(ghost);
			ghost.MouseFilter = MouseFilterEnum.Ignore;
			ghost.FocusMode = FocusModeEnum.None;
			ghost.SetCard(null);
			ghost.SetBackSide(side);
			ghost.SetDisplaySize(destination.DisplaySize);
			ghost.PivotOffset = ghost.Size * 0.5f;
			_motionCards.Add(ghost);
			Vector2 source = GetGlobalTransform().AffineInverse() * plan.Source.GetCenter();
			Vector2 sourceScale = plan.Source.Size / (ghost.Size * GetGlobalTransform().Scale);
			int revision = _motionRevision;
			bool faceShown = false;
			void Pose(float time)
			{
				if (revision != _motionRevision || !IsInstanceValid(destination) || !IsInstanceValid(ghost)) return;
				float progress = time * time * (3 - 2 * time);
				Transform2D targetPose = GetGlobalTransform().AffineInverse() * destination.GetGlobalTransform();
				Vector2 target = targetPose * (destination.Size * 0.5f);
				Vector2 center = source.Lerp(target, progress) - new Vector2(0, Hand.DrawArcHeight * Mathf.Sin(Mathf.Pi * progress));
				ghost.Position = center - ghost.PivotOffset;
				ghost.Rotation = targetPose.Rotation * progress;
				float flip = Mathf.Clamp((time - 0.12f) / 0.30f, 0, 1);
				if (flip >= 0.5f && !faceShown) { ghost.SetCard(model); faceShown = true; }
				Vector2 scale = sourceScale.Lerp(targetPose.Scale, progress);
				scale.X *= Mathf.Max(0.025f, Mathf.Abs(Mathf.Cos(Mathf.Pi * flip)));
				ghost.Scale = scale;
			}
			Pose(0);
			double speedScale = MotionDuration / DefaultMotionDuration;
			Tween tween = CreateTween();
			_motionTweens.Add(tween);
			tween.TweenInterval(index * Hand.DrawStaggerSeconds * speedScale);
			tween.TweenMethod(Callable.From<float>(Pose), 0.0f, 1.0f, Hand.DrawSeconds * speedScale);
			tween.TweenCallback(Callable.From(() =>
			{
				if (revision != _motionRevision) return;
				if (IsInstanceValid(destination)) destination.Show();
				_drawArrivals.Remove(destination);
				_motionTweens.Remove(tween);
				_motionCards.Remove(ghost);
				ghost.QueueFree();
			}));
		}
	}
}
