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
		set { _faction = value ?? string.Empty; if (IsInsideTree()) UpdateGlyph(); }
	}

	[Export]
	public PlayerSide Side
	{
		get => _side;
		set { _side = value; if (IsInsideTree()) UpdateGlyph(); }
	}

	[Export]
	public Color EmblemColor
	{
		get => _color;
		set { _color = value; QueueRedraw(); }
	}

	private TextureRect? _glyph;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		if (CustomMinimumSize == Vector2.Zero)
		{
			CustomMinimumSize = new Vector2(30, 30);
		}
		UpdateGlyph();
	}

	/// <summary>The official glyph shows in its own TextureRect; the line art is drawn only without one.</summary>
	private void UpdateGlyph()
	{
		Texture2D? texture = GlyphFor(_faction, _side);
		if (_glyph is null && texture is not null)
		{
			_glyph = new TextureRect
			{
				Name = "Glyph",
				MouseFilter = MouseFilterEnum.Ignore,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			};
			AddChild(_glyph);
			_glyph.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}
		if (_glyph is not null)
		{
			_glyph.Texture = texture;
			_glyph.Visible = texture is not null;
		}
		QueueRedraw();
	}

	public void SetFaction(string? faction, PlayerSide side)
	{
		_faction = faction ?? string.Empty;
		_side = side;
		_color = FactionPalette.For(_faction, side).Accent;
		if (IsInsideTree()) UpdateGlyph();
	}

	/// <summary>The bundled glyph for a faction on its own side, or null for the neutral factions.</summary>
	public static Texture2D? GlyphFor(string? faction, PlayerSide side) =>
		GlyphPath(faction, side) is { } path && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

	/// <summary>The resource path of a faction's bundled glyph, or null for the neutral factions.</summary>
	public static string? GlyphPath(string? faction, PlayerSide side)
	{
		string key = (faction ?? string.Empty).Trim().ToLowerInvariant();
		string[] names = side == PlayerSide.Runner
			? ["criminal", "shaper", "anarch", "adam", "apex", "sunny-lebeau"]
			: ["weyland-consortium", "jinteki", "haas-bioroid", "nbn"];
		string? file = names.FirstOrDefault(name => key.Contains(name.Split('-')[0], StringComparison.Ordinal)
			|| (name == "haas-bioroid" && key.Contains("bioroid", StringComparison.Ordinal)));
		return file is null ? null : $"res://icons/factions/{file}.svg";
	}

	private static readonly Dictionary<string, Texture2D?> Silhouettes = new(StringComparer.Ordinal);

	/// <summary>
	/// A large one-colour silhouette of a faction's glyph for the table's background decor: the
	/// SVG rendered at <paramref name="pixels"/> when its source is at hand, or the imported
	/// texture, with every pixel white and its alpha kept, so a tint colours it evenly.
	/// </summary>
	public static Texture2D? SilhouetteFor(string? faction, PlayerSide side, int pixels = 512)
	{
		if (GlyphPath(faction, side) is not { } path) return null;
		string key = $"{path}@{pixels}";
		if (Silhouettes.TryGetValue(key, out Texture2D? cached)) return cached;
		Image? image = null;
		if (Godot.FileAccess.FileExists(path))
		{
			var rendered = new Image();
			if (rendered.LoadSvgFromString(Godot.FileAccess.GetFileAsString(path), pixels / 128f) == Error.Ok) image = rendered;
		}
		image ??= GlyphFor(faction, side)?.GetImage();
		Texture2D? texture = null;
		if (image is not null)
		{
			image.Convert(Image.Format.Rgba8);
			for (int y = 0; y < image.GetHeight(); y++)
				for (int x = 0; x < image.GetWidth(); x++)
					image.SetPixel(x, y, new Color(1, 1, 1, image.GetPixel(x, y).A));
			texture = ImageTexture.CreateFromImage(image);
		}
		Silhouettes[key] = texture;
		return texture;
	}

	public override void _Draw()
	{
		if (_glyph is { Visible: true }) return;

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
