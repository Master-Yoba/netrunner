using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Owns the inspected-card zoom overlay and its text-only details.</summary>
public partial class Main
{
	private ScrollContainer _zoomScroll = null!;
	private Button _zoomCloseButton = null!;
	private Control? _zoomPreviousFocus;

	private void BuildCardZoomOverlay()
	{
		_zoomOverlay = GetNodeOrNull<PanelContainer>("CardInspection")!;
		if (_zoomOverlay is null)
		{
			_zoomOverlay = GD.Load<PackedScene>("res://scenes/components/CardInspection.tscn").Instantiate<PanelContainer>();
			AddChild(_zoomOverlay);
		}
		_zoomOverlay.Visible = false;
		_zoomTitle = _zoomOverlay.GetNode<Label>("%Title");
		_zoomText = _zoomOverlay.GetNode<Label>("%Rules");
		_zoomScroll = _zoomOverlay.GetNode<ScrollContainer>("%TextScroll");
		_zoomCloseButton = _zoomOverlay.GetNode<Button>("%Close");
		_zoomCloseButton.Pressed += HideCardZoom;
		_zoomScroll.FocusNext = _zoomCloseButton.GetPath();
		_zoomScroll.FocusPrevious = _zoomCloseButton.GetPath();
		_zoomCloseButton.FocusNext = _zoomScroll.GetPath();
		_zoomCloseButton.FocusPrevious = _zoomScroll.GetPath();
	}

	private void ShowInspectedCardZoom()
	{
		if (_inspectedCard is not { } card)
		{
			return;
		}

		if (!_zoomOverlay.Visible)
		{
			_zoomPreviousFocus = GetViewport().GuiGetFocusOwner();
		}
		_zoomTitle.Text = $"CARD ZOOM · {card.Title}";
		_zoomText.Text = FormatCardZoom(card);
		_zoomOverlay.Visible = true;
		_zoomOverlay.MoveToFront();
		_zoomScroll.ScrollVertical = 0;
		_zoomScroll.GrabFocus();
	}

	private void HideCardZoom()
	{
		_zoomOverlay.Visible = false;
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
