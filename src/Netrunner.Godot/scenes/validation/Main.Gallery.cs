using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Provisioned-lane gallery for auditing the metadata-to-client path. It is
/// deliberately a development affordance: the button is enabled only when
/// the authenticated official 77-card payload was embedded by MSBuild.
/// </summary>
public partial class Main
{
	private void RunGallerySmoke()
	{
		_gallerySmokePassed = TryShowCardGallery();
		Callable.From(FinishGallerySmoke).CallDeferred();
	}

	private void FinishGallerySmoke()
	{
		GetTree().Quit(_gallerySmokePassed ? 0 : 1);
	}

	private void ShowCardGallery()
	{
		_ = TryShowCardGallery();
	}

	private bool TryShowCardGallery()
	{
		if (_galleryOverlay is not null)
		{
			_galleryOverlay.Visible = true;
			_galleryOverlay.MoveToFront();
			return true;
		}

		if (_metadataResolver.Cards.Count == 0)
		{
			GD.PrintErr($"M4_CARD_GALLERY FAIL {_metadataSnapshot.Diagnostic}");
			return false;
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
			$"SYSTEM GATEWAY METADATA GALLERY · {_metadataResolver.Cards.Count} cards",
			20,
			Accent));
		content.AddChild(MakeWrappedLabel(
			$"Authenticated mode: {_metadataSnapshot.Mode} · snapshot {_metadataSnapshot.CommitSha}\n"
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

		foreach (CardPresentationMetadata card in _metadataResolver.Cards)
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
				$"{card.DefinitionId} · {card.Title} · {card.Type.ToUpperInvariant()}",
				14,
				Colors.White));
			rows.AddChild(MakeWrappedLabel(
				$"{stats}\n{string.Join(" · ", card.Subtypes)}\n{FormatCardRules(card.RulesText)}",
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

		bool passed = _metadataSnapshot.IsOfficial
			&& _metadataResolver.Cards.Count == _metadataSnapshot.CardCount;
		if (passed)
		{
			GD.Print(
				$"M4_CARD_GALLERY PASS cards={_metadataSnapshot.CardCount} "
				+ $"mode={_metadataSnapshot.Mode} commit={_metadataSnapshot.CommitSha}");
		}
		else
		{
			GD.PrintErr(
				$"M4_CARD_GALLERY FAIL expected official snapshot, "
				+ $"found {_metadataResolver.Cards.Count} resolver cards in {_metadataSnapshot.Mode} mode");
		}

		return passed;
	}

	private void HideCardGallery()
	{
		if (_galleryOverlay is not null)
		{
			_galleryOverlay.Visible = false;
		}
	}
}
