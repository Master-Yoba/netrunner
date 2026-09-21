using Godot;

namespace Netrunner.GodotClient;

/// <summary>Fits the complete server network into its allotted space without scrolling.</summary>
[Tool]
public partial class BoardFitViewport : Control
{
	public float ContentScale { get; private set; } = 1;

	public override void _Process(double delta)
	{
		if (GetChildCount() == 0 || GetChild(0) is not Container content || Size.X <= 0 || Size.Y <= 0) return;
		Vector2 minimum = content.GetCombinedMinimumSize().Max(Vector2.One);
		ContentScale = Mathf.Min(1, Mathf.Min(Size.X / minimum.X, Size.Y / minimum.Y));
		Vector2 available = Size / ContentScale;
		// The content has a plain Control parent, so a container cannot reset its
		// transform. Card hit areas and overlays retain the same global transform.
		content.Scale = Vector2.One * ContentScale;
		content.Size = available;
		content.Position = Vector2.Zero;
	}
}
