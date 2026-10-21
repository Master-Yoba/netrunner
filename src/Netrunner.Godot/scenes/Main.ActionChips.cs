using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// Contextual actions (UI plan U4): selecting a card or server shows its
/// actions as chips beside it, instead of in the decision dock. The dock keeps
/// the one-line instruction and the actions that belong to no card.
/// </summary>
public partial class Main
{
	private PanelContainer _actionChips = null!;
	private HBoxContainer _actionChipRow = null!;
	private Rect2? _actionChipAnchor;

	private void BuildActionChips()
	{
		_actionChips = new PanelContainer { Name = "ActionChips", Visible = false, ZIndex = 40, MouseFilter = Control.MouseFilterEnum.Stop };
		_actionChips.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.03f, 0.07f, 0.08f, 0.96f),
			BorderColor = new Color(0.43f, 0.94f, 0.72f, 0.8f),
			BorderWidthLeft = 1,
			BorderWidthRight = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
			ContentMarginLeft = 6,
			ContentMarginRight = 6,
			ContentMarginTop = 4,
			ContentMarginBottom = 4,
		});
		_actionChipRow = new HBoxContainer { Name = "Chips" };
		_actionChipRow.AddThemeConstantOverride("separation", 6);
		_actionChips.AddChild(_actionChipRow);
		_tableViewport.AddChild(_actionChips);
	}

	/// <summary>Removes the chips; the next render adds them again for the current selection.</summary>
	private void ClearActionChips()
	{
		if (_actionChipRow is null) return;
		ClearChildren(_actionChipRow);
		_actionChips.Visible = false;
		_actionChipAnchor = null;
	}

	/// <summary>
	/// Where the chips go: beside the selected card, or under the selected
	/// server's name. Null when the selection isn't on screen, and the dock is used.
	/// </summary>
	private Rect2? ActionChipAnchor() =>
		_tableSelectedCard is { } card && _gameTable.VisibleCards.TryGetValue(card, out TableCardControl? control)
			&& control.IsVisibleInTree()
			? control.GetGlobalRect()
			: _tableSelectedServer is { } server ? _gameTable.GetServerHeaderRect(server) : null;

	private Container ActionContainerFor(bool contextual)
	{
		if (!contextual || _actionChipRow is null || ActionChipAnchor() is not { } anchor) return _tableActions;
		_actionChipAnchor = anchor;
		_actionChips.Visible = true;
		return _actionChipRow;
	}

	/// <summary>Keeps the chips beside their anchor and on screen, after layout.</summary>
	private void PlaceActionChips()
	{
		if (_actionChips is null || !_actionChips.Visible || _actionChipAnchor is not { } anchor) return;
		_actionChips.ResetSize();
		Vector2 size = _actionChips.Size;
		Rect2 bounds = _tableViewport.GetGlobalRect();
		Vector2 position = new(anchor.End.X + 8, anchor.Position.Y + (anchor.Size.Y - size.Y) / 2);
		if (position.X + size.X > bounds.End.X) position.X = anchor.Position.X - size.X - 8;
		if (position.X < bounds.Position.X) position = new(anchor.Position.X, anchor.End.Y + 6);
		position.X = Math.Clamp(position.X, bounds.Position.X, Math.Max(bounds.Position.X, bounds.End.X - size.X));
		position.Y = Math.Clamp(position.Y, bounds.Position.Y, Math.Max(bounds.Position.Y, bounds.End.Y - size.Y));
		_actionChips.GlobalPosition = position;
	}

	public const string AllLegalActionsLabel = "All legal actions…";

	/// <summary>
	/// The full action list stays as an accessibility and debug fallback (U4):
	/// a quiet button in the dock, and F2 to open or close it.
	/// </summary>
	private bool ToggleAllLegalActions()
	{
		if (!_useCardTable || _currentSnapshot is not { } snapshot || snapshot.View.Decision?.OwnerSide != snapshot.View.Viewer) return false;
		_tableAllActions = !_tableAllActions;
		RenderCardTableActions(snapshot);
		return true;
	}

	private static void QuietFallback(Button button)
	{
		if (button.Text != AllLegalActionsLabel) return;
		button.Flat = true;
		button.TooltipText = "Every legal action as a list · F2";
		button.Modulate = new Color(1, 1, 1, 0.7f);
	}
}
