using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// Interaction feedback for the whole UI (roadmap U5): buttons brighten on
/// hover and dip when pressed, and pop-up panels ease in when they appear.
/// Presentation only, and off under Reduced motion. Geometry never changes
/// while a control is hovered, so hit targets stay where the layout put them.
/// </summary>
public partial class UiMotion : Node
{
	private const double HoverSeconds = 0.08;
	private const double UnhoverSeconds = 0.14;
	private const double PressSeconds = 0.16;
	private const double PanelSeconds = 0.16;
	private static readonly Color HoverTint = new(1.16f, 1.16f, 1.2f);
	private static readonly StringName Bound = "ui_motion_bound";

	/// <summary>Pop-up panels that ease in when shown, by scene file name.</summary>
	private static readonly HashSet<string> EasedPanels = new(StringComparer.Ordinal)
	{
		"AccessPanel.tscn", "BoardChoicePanel.tscn", "CardInspection.tscn", "IcebreakerPanel.tscn",
		"OutcomePanel.tscn", "PileBrowser.tscn", "RunHud.tscn",
	};

	/// <summary>True while the player has asked for reduced motion.</summary>
	public Func<bool> Reduced { get; set; } = () => false;

	public override void _EnterTree()
	{
		GetTree().NodeAdded += OnNodeAdded;
		foreach (Node node in GetTree().Root.FindChildren("*", "", true, false)) OnNodeAdded(node);
	}

	public override void _ExitTree() => GetTree().NodeAdded -= OnNodeAdded;

	private void OnNodeAdded(Node node)
	{
		if (node.HasMeta(Bound)) return;
		switch (node)
		{
			case Button button:
				node.SetMeta(Bound, true);
				BindButton(button);
				break;
			case PanelContainer panel when EasedPanels.Contains(System.IO.Path.GetFileName(panel.SceneFilePath)):
				node.SetMeta(Bound, true);
				panel.VisibilityChanged += () => EaseIn(panel);
				break;
		}
	}

	private void BindButton(Button button)
	{
		button.MouseEntered += () => Tint(button, true);
		button.MouseExited += () => Tint(button, false);
		button.FocusExited += () => Tint(button, false);
		button.ButtonDown += () => Dip(button);
	}

	private void Tint(Button button, bool hovered)
	{
		if (!IsInstanceValid(button)) return;
		Color target = hovered && !button.Disabled ? HoverTint : Colors.White;
		if (Reduced() || !button.IsInsideTree())
		{
			button.SelfModulate = target;
			return;
		}
		button.CreateTween().TweenProperty(button, "self_modulate", target, hovered ? HoverSeconds : UnhoverSeconds);
	}

	/// <summary>A quick press dip around the centre, back to full size before the release lands.</summary>
	private void Dip(Button button)
	{
		if (Reduced() || !IsInstanceValid(button) || !button.IsInsideTree() || button.Disabled) return;
		button.PivotOffset = button.Size / 2;
		Tween tween = button.CreateTween();
		tween.TweenProperty(button, "scale", Vector2.One * 0.96f, PressSeconds * 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(button, "scale", Vector2.One, PressSeconds * 0.65).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
	}

	private void EaseIn(PanelContainer panel)
	{
		if (!panel.Visible || Reduced() || !panel.IsInsideTree()) return;
		panel.Modulate = panel.Modulate with { A = 0 };
		panel.PivotOffset = panel.Size / 2;
		panel.Scale = Vector2.One * 0.97f;
		Tween tween = panel.CreateTween().SetParallel();
		tween.TweenProperty(panel, "modulate:a", 1.0, PanelSeconds).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(panel, "scale", Vector2.One, PanelSeconds).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
	}
}
