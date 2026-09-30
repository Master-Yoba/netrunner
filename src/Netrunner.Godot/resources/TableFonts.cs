using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// The bundled U5 typefaces (SIL Open Font License, see <c>fonts/*/OFL.txt</c>):
/// Rajdhani for display text, IBM Plex Sans for body text and IBM Plex Mono for
/// numbers and state. The theme uses the same files; this class serves code that
/// draws text directly.
/// </summary>
public static class TableFonts
{
	public const string DisplayBoldPath = "res://fonts/rajdhani/Rajdhani-Bold.ttf";
	public const string DisplaySemiBoldPath = "res://fonts/rajdhani/Rajdhani-SemiBold.ttf";
	public const string BodyPath = "res://fonts/ibm-plex-sans/IBMPlexSans-Variable.ttf";
	public const string MonoPath = "res://fonts/ibm-plex-mono/IBMPlexMono-Regular.ttf";
	public const string MonoMediumPath = "res://fonts/ibm-plex-mono/IBMPlexMono-Medium.ttf";

	private static Font? _display;
	private static Font? _displayWide;
	private static Font? _mono;
	private static Font? _monoMedium;

	/// <summary>Rajdhani Bold.</summary>
	public static Font? Display => _display ??= Load(DisplayBoldPath);

	/// <summary>Rajdhani Bold with wide letter spacing, for wordmarks.</summary>
	public static Font? DisplayWide => _displayWide ??= Spaced(Display, 2);

	/// <summary>IBM Plex Mono Regular.</summary>
	public static Font? Mono => _mono ??= Load(MonoPath);

	/// <summary>IBM Plex Mono Medium.</summary>
	public static Font? MonoMedium => _monoMedium ??= Load(MonoMediumPath);

	private static Font? Load(string path) => ResourceLoader.Exists(path) ? GD.Load<Font>(path) : null;

	private static FontVariation? Spaced(Font? font, int glyphSpacing)
	{
		if (font is null) return null;
		var variation = new FontVariation { BaseFont = font };
		variation.SetSpacing(TextServer.SpacingType.Glyph, glyphSpacing);
		return variation;
	}
}
