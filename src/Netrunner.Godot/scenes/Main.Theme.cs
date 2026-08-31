using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Theme bootstrap kept separate from region composition.</summary>
public partial class Main
{
	private void ApplyThemeFoundation()
	{
		// Main.tscn assigns this resource so editor previews inherit it. Loading
		// here too keeps code-created Main instances and headless smoke scenes on
		// the same palette/focus contract.
		Theme? theme = GD.Load<Theme>("res://themes/M4Theme.tres");
		if (theme is not null)
		{
			Theme = theme;
		}
	}

	private static string FormatCardRules(string text)
	{
		try
		{
			return CardMarkupFormatter.Format(text);
		}
		catch (CardMarkupValidationException)
		{
			return "Card text unavailable: unsupported markup.";
		}
	}
}
