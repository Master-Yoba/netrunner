using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckSingleCardHoverAsync(bool liveCache)
	{
		CheckGameSymbolRendering();
		_labScenario = 12;
		_labScenarios!.Select(_labScenario);
		ResetInteractionLab();
		MatchViewModel view = _currentSnapshot!.View;
		CardViewModel hosted = view.RunnerRig[0] with
		{
			Title = "Hover details fixture",
			Subtypes = ["Icebreaker", "Decoder"],
			AdvancementCounters = 2,
			AdvancementRequirement = 4,
			TypedCounters = [new("virus", CardCounterPurpose.Virus, 3, null)],
			AbilityCue = AbilityCueLabels.Used,
			FirstXUses = [new(view.RunnerRig[0].Id, "hover-fixture", 1)],
			RulesText = "<strong>Current rules:</strong> [click]: Gain 2 [credit].\n2 [recurring-credit] · 1 [mu] · +1 [link]\n<symbol:subroutine> Pay 1 <symbol:credit> or [trash] this card.",
		};
		view = view with { RunnerRig = view.RunnerRig.Select((card, index) => index == 0 ? hosted : card).ToArray() };
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			PublishLabView(view);
			await TableFrames(3);
			TableCardControl source = _gameTable.VisibleCards[hosted.Id];
			source.GrabFocus();
			await TableFrames(3);
			RequireInteraction(CardHoverPreviewVisible && _cardHoverPreviewFace is { HasFullCardArtwork: true }
				&& source.TooltipText.Length == 0 && _cardHoverPreviewFace.TooltipText.Length == 0,
				"The artwork preview still has a competing native card tooltip.");
			RequireInteraction(_cardHoverPreviewType!.Text.Contains("Icebreaker · Decoder", StringComparison.Ordinal)
				&& _cardHoverPreviewStats!.SymbolText.Contains("Advancement requirement 4", StringComparison.Ordinal)
				&& _cardHoverPreviewCounters!.Text.Contains("Advancement ×2", StringComparison.Ordinal)
				&& _cardHoverPreviewCounters.Text.Contains("VIRUS ×3", StringComparison.Ordinal)
				&& _cardHoverPreviewStatus!.Text.Contains(AbilityCueLabels.Used, StringComparison.Ordinal)
				&& _cardHoverPreviewStatus.Text.Contains(FirstXUseLabels.UsedThisTurn, StringComparison.Ordinal)
				&& _cardHoverPreviewStatus.Text.Contains("Hosted on", StringComparison.Ordinal)
				&& _cardHoverPreviewRules!.SymbolText == FormatCardRules(hosted.RulesText)
				&& _cardHoverPreviewHint!.Text.Contains("Enter or Space", StringComparison.Ordinal),
				"The single artwork preview omitted card details or current state.");
			RequireInteraction(CardHoverPreviewGlobalRect.Encloses(_cardHoverPreviewHint!.GetGlobalRect()),
				"The hover details or final hint were clipped.");
			RequireInteraction(_cardHoverPreviewRules!.RenderedSymbolCount == 8
				&& !_cardHoverPreviewRules.GetParsedText().Contains("[credit]", StringComparison.Ordinal),
				"Hover rules still display literal symbol placeholders.");
			GameSymbolLabel corpResources = _gameTable.GetNode<GameSymbolLabel>("Surface/Margins/Layout/CorpStrip/Content/Resources");
			GameSymbolLabel runnerResources = _gameTable.GetNode<GameSymbolLabel>("Surface/Margins/Layout/RunnerField/Content/Summary/Content/Resources");
			RequireInteraction(corpResources.RenderedSymbolCount == 2 && runnerResources.RenderedSymbolCount >= 3
				&& runnerResources.GetContentHeight() <= runnerResources.Size.Y + 1,
				"Board resource symbols are missing or clipped.");
			if (OS.GetCmdlineUserArgs().Contains("--card-art-screenshots"))
				await SaveArtworkEvidence($"hover-details-{size.X}", liveCache);
			source.ReleaseFocus();
			_inspectedCard = hosted;
			ShowInspectedCardZoom();
			await TableFrames(3);
			RequireInteraction(_zoomText.RenderedSymbolCount == 8, "Full card inspection lost its inline symbols.");
			if (OS.GetCmdlineUserArgs().Contains("--card-art-screenshots"))
				await SaveArtworkEvidence($"symbol-inspection-{size.X}", liveCache);
			HideCardZoom();
		}
		// A concealed host must not reveal its title through the remaining preview.
		view = view with
		{
			Servers = view.Servers.Select(server => server with
			{
				Ice = server.Ice.Select(card => card is not null && card.Id == hosted.HostCardId
					? card with { IsKnown = false, Title = "PRIVATE HOST TITLE" } : card).ToArray(),
			}).ToArray(),
		};
		PublishLabView(view);
		await TableFrames(3);
		_gameTable.VisibleCards[hosted.Id].GrabFocus();
		RequireInteraction(_cardHoverPreviewStatus!.Text.Contains("Hosted on face-down card", StringComparison.Ordinal)
			&& !_cardHoverPreviewStatus.Text.Contains("PRIVATE", StringComparison.Ordinal), "Hover revealed a concealed host.");
		_gameTable.VisibleCards[hosted.Id].ReleaseFocus();
		foreach (CardViewModel top in new[] { view.CorpDiscard[^1], view.RunnerHeap[^1] })
		{
			TableCardControl face = _gameTable.VisibleCards[top.Id];
			var pile = (BoardActionButton)face.GetParent();
			pile.GrabFocus();
			RequireInteraction(CardHoverPreviewCardId == top.Id && face.TooltipText.Length == 0 && pile.TooltipText.Length == 0
				&& _cardHoverPreviewHint!.Text.Contains("browse this pile", StringComparison.Ordinal),
				"A face-up pile retained a duplicate tooltip or lost its browse hint.");
			pile.ReleaseFocus();
		}
		var heapPile = (BoardActionButton)_gameTable.VisibleCards[view.RunnerHeap[^1].Id].GetParent();
		PublishLabView(view with { RunnerHeap = [] });
		RequireInteraction(heapPile.TooltipText.Contains("0 public cards", StringComparison.Ordinal),
			"An emptied pile lost its ordinary tooltip after removing the face preview.");
		_tableAccessCard.SetCard(hosted);
		RequireInteraction(_tableAccessCard.TooltipText.Length == 0, "The access card retained its native tooltip.");
		_tableAccessCard.SetCard(null);
		HideCardHoverPreview();
		RequireInteraction(_cardHoverPreviewStatus!.Text.Length == 0 && _cardHoverPreviewCounters!.Text.Length == 0
			&& _cardHoverPreviewHint!.Text.Length == 0 && _cardHoverPreviewRules!.RenderedSymbolCount == 0,
			"Dismissed hover retained card details.");
	}

	private static void CheckGameSymbolRendering()
	{
		var label = new GameSymbolLabel();
		try
		{
			label.SymbolText = FormatCardRules("[click][credit][mu][link][recurring-credit][subroutine][trash]"
				+ "<symbol:click-cost><symbol:credit-cost><symbol:memory><symbol:trash-cost>"
				+ "<symbol:agenda-point><symbol:advancement-counter><symbol:bad-pub>"
				+ "<symbol:brain-damage><symbol:interrupt><symbol:rez><symbol:tag><symbol:virus-counter>");
			RequireInteraction(label.RenderedSymbolCount == 19, "A released game glyph or normalized alias is missing.");
			const string literal = "[url=https://example.invalid]literal[/url] [img]res://private.png[/img] [strength] [unknown]";
			label.SymbolText = literal;
			RequireInteraction(label.GetParsedText() == literal && label.RenderedSymbolCount == 0,
				"Card text was interpreted as BBCode, or an unsupported symbol lost its text fallback.");
			label.SymbolText = string.Empty;
			RequireInteraction(label.GetParsedText().Length == 0, "Cleared symbol text retained rendered content.");
		}
		finally
		{
			label.Free();
		}
	}
}
