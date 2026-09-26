using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class GameTableControl
{
	private readonly Dictionary<string, BoardActionButton> _zonePiles = new(StringComparer.Ordinal);
	private BoardActionButton _corpCredits = null!;
	private BoardActionButton _runnerCredits = null!;
	public event Action<PlayerSide, ActionKind>? BasicActionRequested;
	public event Action<string>? PileRequested;
	public Control PlayArea => GetNode<Control>("Surface");

	public void SetBasicActionAvailability(bool credit, bool draw)
	{
		if (!_shellBuilt || _pendingView is not { } view) return;
		_corpCredits.SetActionAvailable(credit && view.Viewer == PlayerSide.Corp);
		_runnerCredits.SetActionAvailable(credit && view.Viewer == PlayerSide.Runner);
		((BoardActionButton)_runnerDeckPile).SetActionAvailable(draw && view.Viewer == PlayerSide.Runner);
		if (_zonePiles.TryGetValue("zone:corp-deck", out BoardActionButton? deck))
			deck.SetActionAvailable(draw && view.Viewer == PlayerSide.Corp);
	}

	private void RenderZonePile(string handle, HBoxContainer parent, CardViewModel? model, int count,
		Dictionary<string, TableCardControl> visible)
	{
		if (!_zonePiles.TryGetValue(handle, out BoardActionButton? pile))
		{
			pile = GD.Load<PackedScene>("res://scenes/components/BoardPile.tscn").Instantiate<BoardActionButton>();
			pile.Name = handle == "zone:corp-deck" ? "DrawPile" : "ArchivesPile";
			parent.AddChild(pile);
			if (handle == "zone:corp-deck") pile.Pressed += () => BasicActionRequested?.Invoke(PlayerSide.Corp, ActionKind.DrawCard);
			else
			{
				pile.Pressed += () => PileRequested?.Invoke(handle);
				pile.MouseEntered += () => HoverPileFace(pile, true);
				pile.MouseExited += () => HoverPileFace(pile, false);
				pile.FocusEntered += () => HoverPileFace(pile, true);
				pile.FocusExited += () => HoverPileFace(pile, false);
			}
			_zonePiles[handle] = pile;
		}
		if (pile.GetParent() != parent) pile.Reparent(parent);
		pile.TooltipText = handle == "zone:corp-deck" ? $"R&D · {count} cards · draw for 1 click when available"
			: $"Archives · {count} cards · click to browse";
		if (handle == "zone:corp-deck") pile.SetActionAvailable(false);
		RenderPileFace(handle, pile, model, count, handle == "zone:corp-deck" ? "R&D" : "Archives", PlayerSide.Corp, visible);
		if (UseCardHoverPreview && model is { IsKnown: true }) pile.TooltipText = string.Empty;
	}

	private TableCardControl RenderPileFace(string handle, BoardActionButton pile, CardViewModel? model, int count, string title,
		PlayerSide side, Dictionary<string, TableCardControl> visible)
	{
		TableCardControl face = EnsureCard(handle, pile, model, false, visible, side);
		face.Position = Vector2.Zero;
		face.MouseFilter = MouseFilterEnum.Ignore;
		face.FocusMode = FocusModeEnum.None;
		face.Visible = count > 0;
		pile.CustomMinimumSize = face.DisplaySize;
		pile.GetNode<Label>("Count").Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
		pile.Text = count == 0 ? $"{title}\nEmpty" : string.Empty;
		int selected = handle != "zone:corp-deck" && _pendingView is { } view && view.Viewer == side
			? BoardInteractionProjector.SelectedDiscards(view).Count : 0;
		Label pending = pile.GetNode<Label>("Pending");
		pending.Visible = selected > 0;
		bool heap = handle == "zone:runner-heap";
		pile.GetNode<Label>("Caption").Visible = heap && selected == 0;
		pending.Text = $"{(heap ? "Heap · " : "")}{selected} selected";
		return face;
	}

	private const float ScorePileScale = 0.55f;

	/// <summary>Scored agendas are public: show the latest one face-up with the pile count.</summary>
	private void RenderScorePile(string handle, BoardActionButton? pile, IReadOnlyList<CardViewModel> scored, PlayerSide side,
		Dictionary<string, TableCardControl> visible)
	{
		if (pile is null) return;
		// A mini card keeps the summary panes compact; hovering still opens the full preview.
		// Scale the face just rendered: the pile can still hold a stale face, such as the
		// hidden empty-pile slot, until cleanup removes it.
		TableCardControl face = RenderPileFace(handle, pile, scored.Count > 0 ? scored[^1] : null, scored.Count, "Score", side, visible);
		face.Scale = new Vector2(ScorePileScale, ScorePileScale);
		pile.CustomMinimumSize = face.DisplaySize * ScorePileScale;
		int points = scored.Sum(card => card.AgendaPoints ?? 0);
		string owner = side == PlayerSide.Corp ? "Corp score area" : "Runner score area";
		// Like other public piles, a known top card shows the hover preview instead of a tooltip.
		UpdatePileTooltip(pile, scored.Count == 0 ? $"{owner} · empty"
			: $"{owner} · {points} agenda {(points == 1 ? "point" : "points")}", scored.Count > 0 && scored[^1].IsKnown);
	}

	private static void HoverPileFace(BoardActionButton pile, bool active)
	{
		TableCardControl? face = pile.GetChildren().OfType<TableCardControl>().FirstOrDefault(card => card.Visible);
		face?.SetContainerHover(active || pile.IsHovered() || pile.HasFocus(ignoreHiddenFocus: true));
	}
}
