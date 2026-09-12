using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class GameTableControl
{
	private readonly HashSet<TableCardControl> _discardFlights = new();
	private readonly HashSet<TableCardControl> _discardArrivals = new();
	private int _pendingDiscardMotion;
	public int DiscardMotionCount => _discardFlights.Count + _pendingDiscardMotion;

	private static (HashSet<string> Selected, HashSet<string> Incoming) DiscardChanges(MatchViewModel? previous, MatchViewModel view)
	{
		if (previous?.Viewer != view.Viewer || previous.Decision is not { Kind: DecisionKind.TurnDiscard } decision
			|| decision.OwnerSide != view.Viewer) return ([], []);
		bool corp = view.Viewer == PlayerSide.Corp;
		HashSet<string> selected = BoardInteractionProjector.SelectedDiscards(previous).Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
		HashSet<string> currentHand = (corp ? view.CorpHand : view.RunnerHand).VisibleCards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
		HashSet<string> pile = (corp ? view.CorpDiscard : view.RunnerHeap).Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
		HashSet<string> incoming = BoardInteractionProjector.SelectedDiscards(view).Select(card => card.Id)
			.Concat((corp ? previous.CorpHand : previous.RunnerHand).VisibleCards
				.Where(card => !currentHand.Contains(card.Id) && pile.Contains(card.Id)).Select(card => card.Id))
			.Where(id => !selected.Contains(id)).ToHashSet(StringComparer.Ordinal);
		// Previously selected cards must not fly again when the engine commits
		// the complete set, regardless of which one becomes the visible pile top.
		selected.UnionWith(incoming);
		return (selected, incoming);
	}

	private void PresentDiscardMotion(string id, MotionCard source, PlayerSide side)
	{
		if (!MotionEnabled || Hand.ReducedMotion || MotionDuration <= 0 || source.Card is not { IsKnown: true } model) return;
		Control? pile = side == PlayerSide.Corp ? _zonePiles.GetValueOrDefault("zone:corp-archives") : _runnerHeapPile;
		if (pile is null || !pile.IsVisibleInTree()) return;
		TableCardControl? arrival = _visibleCards.GetValueOrDefault(id);
		if (arrival is not null && IsDiscardPileFace(arrival)) { arrival.Hide(); _discardArrivals.Add(arrival); }
		else arrival = null;
		TableCardControl ghost = TableCardControl.Create();
		ghost.Name = $"DiscardFlight_{_motionRevision}_{_discardFlights.Count}";
		ghost.ZIndex = 70;
		AddChild(ghost);
		ghost.MouseFilter = MouseFilterEnum.Ignore;
		ghost.FocusMode = FocusModeEnum.None;
		ghost.SetCard(model);
		ghost.SetDisplaySize(source.Size);
		ghost.PivotOffset = ghost.Size * 0.5f;
		_motionCards.Add(ghost);
		_discardFlights.Add(ghost);
		Vector2 start = source.Pose * (source.Size * 0.5f);
		Transform2D target = GetGlobalTransform().AffineInverse() * pile.GetGlobalTransform();
		Vector2 end = target * (pile.Size * 0.5f);
		Vector2 targetScale = target.Scale * pile.Size / source.Size;
		int revision = _motionRevision;
		void Pose(float time)
		{
			if (revision != _motionRevision || !IsInstanceValid(ghost)) return;
			float progress = time * time * (3 - 2 * time);
			ghost.Position = start.Lerp(end, progress) - ghost.PivotOffset;
			ghost.Scale = source.Pose.Scale.Lerp(targetScale, progress);
			ghost.Rotation = Mathf.LerpAngle(source.Pose.Rotation, target.Rotation, progress);
			// Pending choices do not create an authoritative pile face yet.
			if (arrival is null) ghost.Modulate = new Color(1, 1, 1, 1 - Mathf.Clamp((time - 0.75f) / 0.25f, 0, 1));
		}
		Pose(0);
		Tween tween = CreateTween();
		_motionTweens.Add(tween);
		tween.TweenMethod(Callable.From<float>(Pose), 0.0f, 1.0f, MotionDuration);
		tween.TweenCallback(Callable.From(() =>
		{
			if (revision != _motionRevision) return;
			if (IsInstanceValid(arrival)) arrival!.Show();
			if (arrival is not null) _discardArrivals.Remove(arrival);
			_discardFlights.Remove(ghost);
			_motionTweens.Remove(tween);
			_motionCards.Remove(ghost);
			ghost.QueueFree();
		}));
	}
}
