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
		TableCardControl face = EnsureCard(handle, pile, model, false, visible, PlayerSide.Corp);
		face.Position = Vector2.Zero;
		face.MouseFilter = MouseFilterEnum.Ignore;
		face.FocusMode = FocusModeEnum.None;
		face.Visible = count > 0;
		pile.CustomMinimumSize = face.DisplaySize;
		pile.GetNode<Label>("Count").Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
		pile.Text = count == 0 ? (handle == "zone:corp-deck" ? "R&D\nEmpty" : "Archives\nEmpty") : string.Empty;
	}

	private static void HoverPileFace(BoardActionButton pile, bool active)
	{
		TableCardControl? face = pile.GetChildren().OfType<TableCardControl>().FirstOrDefault(card => card.Visible);
		face?.SetContainerHover(active || pile.IsHovered() || pile.HasFocus());
	}
}
