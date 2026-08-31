using Godot;
using Netrunner.Cards;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Provisioned-lane gallery for auditing the metadata-to-client path. It is
/// deliberately a development affordance: the button is enabled only when
/// the authenticated official 77-card payload was embedded by MSBuild.
/// </summary>
public partial class Main
{
	private void ShowCardGallery()
	{
		if (_galleryOverlay is not null)
		{
			_galleryOverlay.Visible = true;
			_galleryOverlay.MoveToFront();
			return;
		}

		if (!CardDataResource.TryLoadEmbedded(out CardDataSet? data, out string diagnostic)
			|| data is null)
		{
			GD.PrintErr($"M4_CARD_GALLERY FAIL {diagnostic}");
			return;
		}

		_galleryOverlay = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		SetFullRect(_galleryOverlay);
		AddChild(_galleryOverlay);

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 30);
		margin.AddThemeConstantOverride("margin_top", 24);
		margin.AddThemeConstantOverride("margin_right", 30);
		margin.AddThemeConstantOverride("margin_bottom", 24);
		_galleryOverlay.AddChild(margin);

		VBoxContainer content = new();
		content.AddThemeConstantOverride("separation", 8);
		margin.AddChild(content);
		content.AddChild(MakeLabel(
			$"SYSTEM GATEWAY METADATA GALLERY · {data.Cards.Count} cards",
			20,
			Accent));
		content.AddChild(MakeWrappedLabel(
			$"Authenticated mode: {CardDataResource.EmbeddedMode} · snapshot {data.Manifest.CommitSha}\n"
			+ "Development-only text audit. Symbols are rendered as labelled cues; no card text is executed.",
			12,
			Muted));

		ScrollContainer scroll = new()
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		VBoxContainer rows = new();
		rows.AddThemeConstantOverride("separation", 10);
		rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(rows);
		content.AddChild(scroll);

		foreach (NormalizedCardDefinition card in data.Cards)
		{
			string stats = string.Join(
				" · ",
				new[]
				{
					$"{card.Side}/{card.Faction}",
					card.Cost is { } cost ? $"cost {cost}" : null,
					card.Strength is { } strength ? $"strength {strength}" : null,
					card.MemoryCost is { } memory ? $"MU {memory}" : null,
					card.TrashCost is { } trash ? $"trash {trash}" : null,
					card.AgendaPoints is { } points ? $"agenda {points}" : null,
					card.AdvancementRequirement is { } advancement ? $"advance {advancement}" : null,
				}.Where(value => value is not null)!);
			rows.AddChild(MakeLabel(
				$"{card.Code} · {card.Title} · {card.Type.ToUpperInvariant()}",
				14,
				Colors.White));
			rows.AddChild(MakeWrappedLabel(
				$"{stats}\n{string.Join(" · ", card.Subtypes)}\n{CardMarkupFormatter.Format(card.Text)}",
				11,
				Muted));
		}

		Button close = new()
		{
			Text = "Close metadata gallery",
			CustomMinimumSize = new Vector2(220, 38),
		};
		close.Pressed += HideCardGallery;
		content.AddChild(close);

		if (data.Cards.Count == CardDataValidator.OfficialCardCount
			&& string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal))
		{
			GD.Print($"M4_CARD_GALLERY PASS cards={data.Cards.Count} mode={CardDataResource.EmbeddedMode}");
		}
		else
		{
			GD.PrintErr(
				$"M4_CARD_GALLERY FAIL expected {CardDataValidator.OfficialCardCount} official cards, "
				+ $"found {data.Cards.Count} in {CardDataResource.EmbeddedMode} mode");
		}
	}

	private void HideCardGallery()
	{
		if (_galleryOverlay is not null)
		{
			_galleryOverlay.Visible = false;
		}
	}
}
