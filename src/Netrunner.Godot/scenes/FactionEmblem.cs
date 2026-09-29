using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

/// <summary>
/// A faction emblem: the official Null Signal Games faction glyph where one is
/// bundled (<c>icons/factions</c>, CC BY-ND 4.0, see <c>icons/SOURCE.md</c>),
/// otherwise simple line art for the neutral factions.
/// </summary>
[Tool]
public partial class FactionEmblem : Control
{
	private string _faction = string.Empty;
	private PlayerSide _side = PlayerSide.Corp;
	private Color _color = FactionPalette.NeutralCorp.Accent;

	[Export]
	public string Faction
	{
		get => _faction;
		set { _faction = value ?? string.Empty; QueueRedraw(); }
	}

	[Export]
	public PlayerSide Side
	{
		get => _side;
		set { _side = value; QueueRedraw(); }
	}

	[Export]
	public Color EmblemColor
	{
		get => _color;
		set { _color = value; QueueRedraw(); }
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		if (CustomMinimumSize == Vector2.Zero)
		{
			CustomMinimumSize = new Vector2(30, 30);
		}
	}

	public void SetFaction(string? faction, PlayerSide side)
	{
		_faction = faction ?? string.Empty;
		_side = side;
		_color = FactionPalette.For(_faction, side).Accent;
		QueueRedraw();
	}

	/// <summary>The bundled glyph for a faction on its own side, or null for the neutral factions.</summary>
	public static Texture2D? GlyphFor(string? faction, PlayerSide side)
	{
		string key = (faction ?? string.Empty).Trim().ToLowerInvariant();
		string[] names = side == PlayerSide.Runner
			? ["criminal", "shaper", "anarch", "adam", "apex", "sunny-lebeau"]
			: ["weyland-consortium", "jinteki", "haas-bioroid", "nbn"];
		string? file = names.FirstOrDefault(name => key.Contains(name.Split('-')[0], StringComparison.Ordinal)
			|| (name == "haas-bioroid" && key.Contains("bioroid", StringComparison.Ordinal)));
		string path = $"res://icons/factions/{file}.svg";
		return file is not null && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
	}

	public override void _Draw()
	{
		if (GlyphFor(_faction, _side) is { } glyph)
		{
			Vector2 textureSize = glyph.GetSize();
			float scale = Mathf.Min(Size.X / textureSize.X, Size.Y / textureSize.Y);
			Vector2 drawn = textureSize * scale;
			DrawTextureRect(glyph, new Rect2((Size - drawn) / 2f, drawn), false);
			return;
		}

		// Author in a 40×40 box and scale to the control's square.
		float extent = Mathf.Min(Size.X, Size.Y);
		if (extent <= 0) return;
		float unit = extent / 40f;
		Vector2 origin = (Size - new Vector2(extent, extent)) / 2f;
		Vector2 P(float x, float y) => origin + new Vector2(x, y) * unit;
		float width = Mathf.Max(1.5f, 2.4f * unit);
		void Line(params Vector2[] points) => DrawPolyline(points, _color, width, true);

		string key = _faction.ToLowerInvariant();
		if (key.Contains("criminal", StringComparison.Ordinal))
		{
			Line(P(20, 4), P(34, 20), P(20, 36), P(6, 20), P(20, 4));
			Line(P(13, 20), P(27, 20));
			Line(P(22, 15), P(27, 20), P(22, 25));
		}
		else if (key.Contains("shaper", StringComparison.Ordinal))
		{
			Line(P(20, 5), P(35, 33), P(5, 33), P(20, 5));
			DrawCircle(P(20, 24), 3.2f * unit, _color);
			Line(P(20, 24), P(20, 12));
		}
		else if (key.Contains("anarch", StringComparison.Ordinal))
		{
			Line(P(6, 35), P(20, 5), P(34, 35));
			Line(P(12, 24), P(24, 20), P(16, 30), P(29, 26));
		}
		else if (key.Contains("weyland", StringComparison.Ordinal))
		{
			Line(P(6, 36), P(14, 6), P(26, 6), P(34, 36), P(6, 36));
			Line(P(11, 24), P(29, 24));
			Line(P(9, 30), P(31, 30));
			Line(P(13, 16), P(27, 16));
		}
		else if (key.Contains("jinteki", StringComparison.Ordinal))
		{
			DrawArc(P(20, 20), 15 * unit, 0, Mathf.Tau, 40, _color, width, true);
			Line(P(8, 26), P(32, 14));
			DrawCircle(P(20, 20), 3 * unit, _color);
		}
		else if (key.Contains("haas", StringComparison.Ordinal) || key.Contains("bioroid", StringComparison.Ordinal))
		{
			Line(P(20, 4), P(34, 12), P(34, 28), P(20, 36), P(6, 28), P(6, 12), P(20, 4));
			Line(P(20, 12), P(28, 26), P(12, 26), P(20, 12));
		}
		else if (key.Contains("nbn", StringComparison.Ordinal))
		{
			DrawCircle(P(20, 30), 3 * unit, _color);
			DrawArc(P(20, 30), 10 * unit, -Mathf.Pi * 0.85f, -Mathf.Pi * 0.15f, 20, _color, width, true);
			DrawArc(P(20, 30), 18 * unit, -Mathf.Pi * 0.85f, -Mathf.Pi * 0.15f, 28, _color, width, true);
		}
		else if (_side == PlayerSide.Runner)
		{
			Line(P(8, 8), P(32, 8), P(32, 32), P(8, 32), P(8, 8));
			Line(P(14, 20), P(26, 20));
		}
		else
		{
			Line(P(8, 34), P(8, 10), P(20, 4), P(32, 10), P(32, 34), P(8, 34));
			Line(P(14, 22), P(26, 22));
		}
	}
}
