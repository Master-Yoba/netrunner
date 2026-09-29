using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

/// <summary>Faction colours for one side of the table (U5 "chrome and neon").</summary>
/// <param name="Accent">Neon or trim colour: borders, emblems, the run connection.</param>
/// <param name="Text">A lighter tint of the accent that stays readable as text.</param>
/// <param name="Ground">The Corp's tinted steel, or the Runner's near-black.</param>
/// <param name="Name">Upper-case faction name for identity plates and wordmarks.</param>
public readonly record struct FactionColors(Color Accent, Color Text, Color Ground, string Name);

/// <summary>
/// The chosen faction palette (roadmap U5, 29 September): Criminal blue, Shaper
/// acid green, Anarch magenta, neutral Runner cyan; Weyland green, Jinteki red,
/// Haas-Bioroid violet, NBN yellow, neutral Corp steel.
/// </summary>
public static class FactionPalette
{
	public static readonly FactionColors Criminal = new(new Color("4fb3ff"), new Color("8fd0ff"), new Color("07090c"), "CRIMINAL");
	public static readonly FactionColors Shaper = new(new Color("a6ff3d"), new Color("c8ff86"), new Color("07090c"), "SHAPER");
	public static readonly FactionColors Anarch = new(new Color("ff4fd8"), new Color("ff9be8"), new Color("07090c"), "ANARCH");
	public static readonly FactionColors NeutralRunner = new(new Color("39e6d4"), new Color("8ff3e8"), new Color("07090c"), "RUNNER");
	public static readonly FactionColors Weyland = new(new Color("5fcf86"), new Color("9fe3b6"), new Color("121814"), "WEYLAND CONSORTIUM");
	public static readonly FactionColors Jinteki = new(new Color("ff5566"), new Color("ff9aa4"), new Color("1a1213"), "JINTEKI");
	public static readonly FactionColors HaasBioroid = new(new Color("a98bff"), new Color("cbb9ff"), new Color("151320"), "HAAS-BIOROID");
	public static readonly FactionColors Nbn = new(new Color("ffd23f"), new Color("ffe38a"), new Color("1a1810"), "NBN");
	public static readonly FactionColors NeutralCorp = new(new Color("9fb4c7"), new Color("c9d6e2"), new Color("121518"), "CORP");

	/// <summary>Virus counters keep one colour whatever the faction; their hexagon shape carries the meaning.</summary>
	public static readonly Color Virus = new("c8ff3a");

	/// <summary>Advancement counters and other Corp tokens placed on cards.</summary>
	public static readonly Color Advancement = new("ffcf6e");

	/// <summary>
	/// Maps a card faction (a code such as <c>haas-bioroid</c>, or a display name
	/// such as <c>Haas-Bioroid</c>) to its colours. An unknown or empty faction
	/// falls back to that side's neutral colours.
	/// </summary>
	public static FactionColors For(string? faction, PlayerSide side)
	{
		string key = (faction ?? string.Empty).Trim().ToLowerInvariant();
		if (key.Contains("criminal", StringComparison.Ordinal)) return Criminal;
		if (key.Contains("shaper", StringComparison.Ordinal)) return Shaper;
		if (key.Contains("anarch", StringComparison.Ordinal)) return Anarch;
		if (key.Contains("weyland", StringComparison.Ordinal)) return Weyland;
		if (key.Contains("jinteki", StringComparison.Ordinal)) return Jinteki;
		if (key.Contains("haas", StringComparison.Ordinal) || key.Contains("bioroid", StringComparison.Ordinal)) return HaasBioroid;
		if (key.Contains("nbn", StringComparison.Ordinal)) return Nbn;
		return side == PlayerSide.Corp ? NeutralCorp : NeutralRunner;
	}
}
