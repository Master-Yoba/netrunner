using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Authored row template for public history details and known-card inspection.
/// </summary>
[Tool]
public partial class HistoryDetail : HBoxContainer
{
	private Label _bullet = null!;
	private Label _text = null!;
	private Control _cardHost = null!;
	private Button _inspectButton = null!;
	private CardViewModel? _card;
	private Action<CardViewModel>? _inspect;
	private Color _detailColour = Colors.White;
	private bool _bound;

	public override void _EnterTree()
	{
		BindNodes();
		WireSignals();
	}

	public override void _Ready()
	{
		BindNodes();
		WireSignals();
	}

	public override void _ExitTree()
	{
		if (_bound && IsInstanceValid(_inspectButton))
		{
			_inspectButton.Pressed -= HandleInspectPressed;
		}
		_bound = false;
	}

	private void WireSignals()
	{
		if (_bound || _inspectButton is null || !IsInstanceValid(_inspectButton))
		{
			return;
		}

		_inspectButton.Pressed += HandleInspectPressed;
		_bound = true;
	}

	public void BindText(string value, int fontSize, Color colour)
	{
		BindNodes();
		_card = null;
		_inspect = null;
		_detailColour = colour;
		_bullet.Visible = true;
		_bullet.Text = "•";
		_bullet.AddThemeFontSizeOverride("font_size", fontSize);
		_bullet.AddThemeColorOverride("font_color", colour);
		_text.Visible = true;
		_text.Text = value;
		_text.AddThemeFontSizeOverride("font_size", fontSize);
		_text.AddThemeColorOverride("font_color", colour);
		_cardHost.Visible = false;
		_inspectButton.Visible = false;
		CustomMinimumSize = new Vector2(0, 24);
	}

	public void BindCard(
		CardViewModel card,
		int fontSize,
		Color accentColor,
		Action<CardViewModel>? inspect)
	{
		ArgumentNullException.ThrowIfNull(card);
		BindNodes();
		_card = card;
		_detailColour = Colors.White;
		_inspect = inspect;
		_bullet.Visible = false;
		_text.Visible = false;
		_cardHost.Visible = true;
		_inspectButton.Visible = true;
		_inspectButton.Text = $"Inspect · {card.Title}";
		_inspectButton.AddThemeFontSizeOverride("font_size", fontSize);
		_inspectButton.AddThemeColorOverride("font_hover_color", accentColor);
		EnsureCardView(card);
		CustomMinimumSize = new Vector2(0, 132);
	}

	public void ApplyAppearance(int fontSize, Color accentColor, Color mutedColor)
	{
		if (!_bound)
		{
			return;
		}

		Color colour = _card is null ? _detailColour : Colors.White;
		_bullet.AddThemeFontSizeOverride("font_size", fontSize);
		_bullet.AddThemeColorOverride("font_color", colour);
		_text.AddThemeFontSizeOverride("font_size", fontSize);
		_text.AddThemeColorOverride("font_color", colour);
		_inspectButton.AddThemeFontSizeOverride("font_size", fontSize);
		_inspectButton.AddThemeColorOverride("font_hover_color", accentColor);
		_inspectButton.AddThemeColorOverride("font_color", _card is null ? mutedColor : Colors.White);
	}

	private void BindNodes()
	{
		if (_bound || GetNodeOrNull<Label>("Bullet") is null)
		{
			return;
		}

		_bullet = GetNode<Label>("Bullet");
		_text = GetNode<Label>("Text");
		_cardHost = GetNode<Control>("CardHost");
		_inspectButton = GetNode<Button>("InspectButton");
	}

	private void EnsureCardView(CardViewModel card)
	{
		foreach (Node child in _cardHost.GetChildren().ToArray())
		{
			_cardHost.RemoveChild(child);
			child.Free();
		}

		TableCardControl view = TableCardControl.Create();
		view.SetDisplaySize(new Vector2(92, 128));
		view.SetCard(card);
		view.CardSelected += HandleCardSelected;
		_cardHost.AddChild(view);
	}

	private void HandleInspectPressed()
	{
		if (_card is not null)
		{
			_inspect?.Invoke(_card);
		}
	}

	private void HandleCardSelected(string _)
	{
		if (_card is not null)
		{
			_inspect?.Invoke(_card);
		}
	}
}
