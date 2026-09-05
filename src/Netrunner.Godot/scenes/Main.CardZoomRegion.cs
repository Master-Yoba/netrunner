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
		_zoomText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_zoomScroll = new ScrollContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			FocusMode = FocusModeEnum.All,
		};
		content.AddChild(_zoomScroll);
		_zoomScroll.AddChild(_zoomText);
		Label cue = MakeLabel(
			"Full text view · hidden information remains concealed · press Escape or Close",
			12,
			Muted);
		content.AddChild(cue);
		_zoomCloseButton = new Button
		{
			Text = "Close card zoom",
			CustomMinimumSize = new Vector2(180, 42),
		};
		_zoomCloseButton.Pressed += HideCardZoom;
		content.AddChild(_zoomCloseButton);
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
		else if (_zoomButton.IsVisibleInTree() && !_zoomButton.Disabled)
		{
			_zoomButton.GrabFocus();
		}
		_zoomPreviousFocus = null;
	}

	private static string FormatCardZoom(CardViewModel card)
		=> CardZoomRegionPresenter.Format(card);
}
