using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Owns upright artwork inspection alongside current, viewer-safe details.</summary>
public partial class Main
{
	private ScrollContainer _zoomScroll = null!;
	private Button _zoomCloseButton = null!;
	private Control? _zoomPreviousFocus;
	private TextureRect _zoomArtwork = null!;

	private void BuildCardZoomOverlay()
	{
		CanvasLayer layer = GetModalLayer("InspectionLayer", 11);
		_zoomOverlay = layer.GetNodeOrNull<PanelContainer>("CardInspection")!;
		if (_zoomOverlay is null)
		{
			_zoomOverlay = GD.Load<PackedScene>("res://scenes/components/CardInspection.tscn").Instantiate<PanelContainer>();
			layer.AddChild(_zoomOverlay);
		}
		_zoomOverlay.Visible = false;
		_zoomTitle = _zoomOverlay.GetNode<Label>("%Title");
		_zoomText = _zoomOverlay.GetNode<GameSymbolLabel>("%Rules");
		_zoomArtwork = _zoomOverlay.GetNode<TextureRect>("%Artwork");
		_zoomScroll = _zoomOverlay.GetNode<ScrollContainer>("%TextScroll");
		_zoomCloseButton = _zoomOverlay.GetNode<Button>("%Close");
		_zoomCloseButton.Pressed += HideCardZoom;
		_zoomScroll.FocusNext = _zoomCloseButton.GetPath();
		_zoomScroll.FocusPrevious = _zoomCloseButton.GetPath();
		_zoomCloseButton.FocusNext = _zoomScroll.GetPath();
		_zoomCloseButton.FocusPrevious = _zoomScroll.GetPath();
	}

	// Separate canvases keep board/hand ZIndex changes below modals, including
	// a focused pile card underneath the inspection opened from that pile.
	private CanvasLayer GetModalLayer(string name, int order)
	{
		CanvasLayer? layer = GetNodeOrNull<CanvasLayer>(name);
		if (layer is not null) return layer;
		layer = new CanvasLayer { Name = name, Layer = order };
		AddChild(layer);
		return layer;
	}

	private void ShowInspectedCardZoom()
	{
		if (_inspectedCard is not { IsKnown: true } card)
		{
			return;
		}

		if (!_zoomOverlay.Visible)
		{
			_zoomPreviousFocus = GetViewport().GuiGetFocusOwner();
		}
		RefreshCardZoomContent(card);
		HideCardHoverPreview();
		_zoomOverlay.Visible = true;
		_zoomOverlay.MoveToFront();
		_zoomScroll.ScrollVertical = 0;
		_zoomScroll.GrabFocus();
	}

	private void RefreshInspectedCard(MatchViewModel view)
	{
		if (_inspectedCard is not { } inspected) return;
		CardViewModel? visible = BoardChoiceCards(view)
			.Concat(view.CorpDeck.VisibleCards).Concat(view.RunnerDeck.VisibleCards)
			.FirstOrDefault(card => card.Id == inspected.Id && card.IsKnown && card.DefinitionId == inspected.DefinitionId);
		_inspectedCard = visible;
		if (visible is not null)
		{
			if (_zoomOverlay.Visible) RefreshCardZoomContent(visible);
			return;
		}
		if (_zoomOverlay.Visible) HideCardZoom();
		if (_useCardTable)
		{
			_tableInspect.Disabled = true;
			_tableInspect.Text = "Inspect card";
		}
	}

	private void RefreshCardZoomContent(CardViewModel card)
	{
		_zoomTitle.Text = $"CARD ZOOM · {card.Title}";
		_zoomText.SymbolText = FormatCardZoom(card);
		LocalCardArtworkResolver.Artwork? artwork = card.DefinitionId is { } definition
			? LocalCardArtworkResolver.Shared.TryLoad(definition) : null;
		_zoomArtwork.Texture = artwork is { FullFace: true } ? artwork.Texture : null;
		_zoomArtwork.Visible = _zoomArtwork.Texture is not null;
	}

	private void HideCardZoom()
	{
		_zoomOverlay.Visible = false;
		_zoomArtwork.Texture = null;
		if (IsInstanceValid(_zoomPreviousFocus)
			&& !_zoomPreviousFocus!.IsQueuedForDeletion()
			&& _zoomPreviousFocus.IsVisibleInTree())
		{
			_zoomPreviousFocus.GrabFocus();
		}
		else if (_useCardTable && _tableInspect.IsVisibleInTree())
		{
			_tableInspect.GrabFocus();
		}
		else if (_zoomButton is not null && _zoomButton.IsVisibleInTree() && !_zoomButton.Disabled)
		{
			_zoomButton.GrabFocus();
		}
		_zoomPreviousFocus = null;
	}

	private static string FormatCardZoom(CardViewModel card)
		=> CardZoomRegionPresenter.Format(card);
}
