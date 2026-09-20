using System.Text.RegularExpressions;
using Godot;

namespace Netrunner.GodotClient;

/// <summary>Plain text with allow-listed local game glyphs; never interprets card text as BBCode.</summary>
[Tool]
public partial class GameSymbolLabel : RichTextLabel
{
	private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
	{
		["click"] = "click",
		["click-cost"] = "click",
		["credit"] = "credit",
		["credit-cost"] = "credit",
		["recurring-credit"] = "recurring-credit",
		["mu"] = "memory",
		["memory"] = "memory",
		["link"] = "link",
		["subroutine"] = "subroutine",
		["trash"] = "trash",
		["trash-cost"] = "trash-cost",
		["agenda-point"] = "agenda-point",
		["advancement-counter"] = "advancement-counter",
		["bad-pub"] = "bad-publicity",
		["bad-publicity"] = "bad-publicity",
		["brain-damage"] = "core-damage",
		["core-damage"] = "core-damage",
		["interrupt"] = "interrupt",
		["rez"] = "rez",
		["tag"] = "tag",
		["virus-counter"] = "virus-counter",
	};
	private string _symbolText = string.Empty;

	/// <summary>Already validated/plain card text, including legacy bracketed symbol names.</summary>
	[Export(PropertyHint.MultilineText)]
	public string SymbolText
	{
		get => _symbolText;
		set
		{
			if (_symbolText == value) return;
			_symbolText = value;
			RenderSymbols();
		}
	}

	public int RenderedSymbolCount { get; private set; }

	public override void _Ready() => RenderSymbols();

	private void RenderSymbols()
	{
		Clear();
		RenderedSymbolCount = 0;
		int offset = 0;
		foreach (System.Text.RegularExpressions.Match token in SymbolToken().Matches(_symbolText))
		{
			string key = token.Groups[1].Value.Replace(' ', '-');
			if (!Symbols.TryGetValue(key, out string? asset)) continue;
			Texture2D? texture = GD.Load<Texture2D>($"res://icons/{asset}.svg");
			if (texture is null) continue;
			AddText(_symbolText[offset..token.Index]);
			// Em units keep glyphs aligned and scaled with the surrounding font and UI scale.
			AddImage(texture, width: 1.5f, height: 1.5f, inlineAlign: InlineAlignment.Center,
				widthUnit: ImageUnit.Em, heightUnit: ImageUnit.Em, altText: asset.Replace('-', ' '));
			RenderedSymbolCount++;
			offset = token.Index + token.Length;
		}
		AddText(_symbolText[offset..]);
	}

	[GeneratedRegex(@"\[([a-zA-Z -]+)\]")]
	private static partial Regex SymbolToken();
}
