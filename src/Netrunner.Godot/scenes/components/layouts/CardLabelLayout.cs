using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// Authored layout values that are consumed by the card surface before its
/// child labels render. The status badge remains editable with the layout
/// scene while the status label itself only owns text and padding.
/// </summary>
[Tool]
public partial class CardLabelLayout : Control
{
	private StyleBox? _statusBadgeStyle;

	[Export]
	public StyleBox? StatusBadgeStyle
	{
		get => _statusBadgeStyle;
		set
		{
			if (ReferenceEquals(_statusBadgeStyle, value))
			{
				return;
			}

			_statusBadgeStyle = value;
			StatusBadgeStyleChanged?.Invoke();
		}
	}

	public event Action? StatusBadgeStyleChanged;
}
