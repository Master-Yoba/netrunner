using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Owns the inspected-card zoom overlay and its text-only details.</summary>
public partial class Main
{
	private void BuildCardZoomOverlay()
	{
		_zoomOverlay = new PanelContainer
		{
			Visible = false,
			MouseFilter = MouseFilterEnum.Stop,
		};
		_zoomOverlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.015f, 0.025f, 0.04f, 0.97f),
			BorderColor = Accent,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 10,
			CornerRadiusTopRight = 10,
			CornerRadiusBottomLeft = 10,
			CornerRadiusBottomRight = 10,
			ContentMarginLeft = 36,
			ContentMarginTop = 30,
			ContentMarginRight = 36,
			ContentMarginBottom = 30,
		});
		SetFullRect(_zoomOverlay);
		AddChild(_zoomOverlay);

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 72);
		margin.AddThemeConstantOverride("margin_top", 56);
		margin.AddThemeConstantOverride("margin_right", 72);
		margin.AddThemeConstantOverride("margin_bottom", 56);
		_zoomOverlay.AddChild(margin);
		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 14);
		margin.AddChild(content);
		_zoomTitle = MakeLabel("CARD ZOOM", 24, Accent);
		_zoomTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		content.AddChild(_zoomTitle);
		_zoomText = MakeLabel(string.Empty, 18, Colors.White);
		_zoomText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_zoomText.SizeFlagsVertical = SizeFlags.ExpandFill;
		content.AddChild(_zoomText);
		Label cue = MakeLabel(
			"Full text view · hidden information remains concealed · press Escape or Close",
			12,
			Muted);
		content.AddChild(cue);
		Button close = new()
		{
			Text = "Close card zoom",
			CustomMinimumSize = new Vector2(180, 42),
		};
		close.Pressed += HideCardZoom;
		content.AddChild(close);
	}

	private void ShowInspectedCardZoom()
	{
		if (_inspectedCard is not { } card)
		{
			return;
		}

		_zoomTitle.Text = $"CARD ZOOM · {card.Title}";
		_zoomText.Text = FormatCardZoom(card);
		_zoomOverlay.Visible = true;
		_zoomOverlay.MoveToFront();
	}

	private void HideCardZoom()
	{
		_zoomOverlay.Visible = false;
	}

	private static string FormatCardZoom(CardViewModel card)
	{
		List<string> lines =
		[
			$"{card.Type}{(card.Subtypes.Count == 0 ? string.Empty : " · " + string.Join(" · ", card.Subtypes))}",
		];
		if (card.Cost is { } cost)
		{
			lines.Add($"Cost · {cost}");
		}

		if (card.Strength is { } strength)
		{
			lines.Add($"Strength · {strength}");
		}

		if (card.AgendaPoints is { } agendaPoints)
		{
			lines.Add($"Agenda points · {agendaPoints}");
		}

		if (card.TrashCost is { } trashCost)
		{
			lines.Add($"Trash cost · {trashCost}");
		}

		if (card.MemoryCost is { } memoryCost)
		{
			lines.Add($"Memory · {memoryCost}");
		}

		if (card.AdvancementRequirement is { } advancementRequirement)
		{
			lines.Add($"Advancement · {advancementRequirement}");
		}

		lines.Add($"Status · {(card.Rezzed ? "REZZED" : card.IsKnown ? "KNOWN" : "FACE-DOWN")}");
		if (card.Counters.Count > 0)
		{
			lines.Add(string.Join(" · ", card.Counters));
		}

		lines.Add(string.Empty);
		lines.Add(FormatCardRules(card.RulesText));
		return string.Join('\n', lines);
	}
}
