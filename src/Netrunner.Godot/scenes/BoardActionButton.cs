using Godot;

namespace Netrunner.GodotClient;

/// <summary>A board affordance; availability is supplied by the action presenter.</summary>
[Tool]
public partial class BoardActionButton : Button
{
	private Color _glowColor = new(1, 1, 1, 0.65f);
	private int _glowWidth = 10;
	[Export]
	public Color GlowColor
	{
		get => _glowColor;
		set { _glowColor = value; QueueRedraw(); }
	}
	[Export(PropertyHint.Range, "0,24,1")]
	public int GlowWidth
	{
		get => _glowWidth;
		set { _glowWidth = Mathf.Clamp(value, 0, 24); QueueRedraw(); }
	}
	public bool ActionAvailable { get; private set; }
	public void SetActionAvailable(bool available)
	{
		Disabled = !available;
		if (ActionAvailable == available) return;
		ActionAvailable = available;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (!ActionAvailable || GlowWidth <= 0) return;
		// A filled gradient outside the button preserves the contrast of its icon and text.
		const int segments = 12;
		const int points = (segments + 1) * 4;
		const int bands = 4;
		Vector2[] vertices = new Vector2[(bands + 1) * points];
		Color[] colors = new Color[vertices.Length];
		int[] indices = new int[bands * points * 6];
		float radius = Mathf.Min(7, Mathf.Min(Size.X, Size.Y) * 0.5f);
		for (int ring = 0; ring <= bands; ring++)
		{
			float fraction = ring / (float)bands;
			float extent = GlowWidth * fraction;
			for (int corner = 0; corner < 4; corner++)
			{
				Vector2 center = new(corner is 0 or 3 ? radius : Size.X - radius,
					corner < 2 ? radius : Size.Y - radius);
				for (int step = 0; step <= segments; step++)
				{
					int point = corner * (segments + 1) + step;
					int offset = ring * points + point;
					float angle = Mathf.Pi + (corner + step / (float)segments) * Mathf.Pi * 0.5f;
					vertices[offset] = center + Vector2.FromAngle(angle) * (radius + extent);
					colors[offset] = new Color(GlowColor, GlowColor.A * (1 - fraction) * (1 - fraction));
					if (ring == bands) continue;
					int next = ring * points + (point + 1) % points;
					int index = offset * 6;
					indices[index] = offset;
					indices[index + 1] = next;
					indices[index + 2] = next + points;
					indices[index + 3] = offset;
					indices[index + 4] = next + points;
					indices[index + 5] = offset + points;
				}
			}
		}
		RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), indices, vertices, colors,
			Array.Empty<Vector2>(), Array.Empty<int>(), Array.Empty<float>(), default, -1);
	}
}
