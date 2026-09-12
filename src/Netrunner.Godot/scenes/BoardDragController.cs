using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Pointer gestures over viewer-safe cards. All action routing belongs to Main.</summary>
public partial class BoardDragController : Control
{
	[Export] public DragAppearance Appearance { get; set; } = null!;
	public GameTableControl Board { get; set; } = null!;
	public Func<bool>? CanInteract { get; set; }
	public Func<InputEvent, bool>? InputFilter { get; set; }
	public event Action<DecisionViewModel, IReadOnlyList<LegalActionViewModel>>? Dropped;
	public event Action? DragStarted;
	public event Action<string, string?>? GestureEnded;
	public bool IsDragging => _preview is not null;
	public bool ReducedMotion { get; set; }
	private BoardInteractionViewModel? _interaction;
	private DecisionViewModel? _decision;
	private BoardInteractionSource? _source;
	private TableCardControl? _sourceControl;
	private TableCardControl? _preview;
	private Vector2 _pressedAt;
	private Vector2 _pointer;
	private Color _sourceModulate;
	private readonly List<(BoardInteractionDestination Destination, PanelContainer Panel, string GeometryKind)> _zones = new();
	private Label _hint = null!;
	private Tween? _returnTween;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Appearance ??= GD.Load<DragAppearance>("res://themes/DragAppearance.tres");
		_hint = GetNode<Label>("Hint");
		GetWindow().FocusExited += OnFocusLost;
	}

	public override void _ExitTree()
	{
		GetWindow().FocusExited -= OnFocusLost;
		RestoreSource();
	}

	public void UpdateInteraction(MatchViewModel view)
	{
		if (_decision?.Id != view.Decision?.Id || _decision?.Revision != view.Decision?.Revision)
			Cancel("decision-changed", false);
		_decision = view.Decision;
		_interaction = BoardInteractionProjector.Project(view);
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsVisibleInTree() || Engine.IsEditorHint() || InputFilter?.Invoke(@event) == false) return;
		if (@event is InputEventMouse pointerEvent) _pointer = GetCanvasTransform().AffineInverse() * pointerEvent.Position;
		if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape }
			|| @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
		{
			if (_source is null) return;
			Cancel("cancelled", true);
			GetViewport().SetInputAsHandled();
			return;
		}
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
		{
			if (button.Pressed)
			{
				if (CanInteract?.Invoke() != true || _interaction is null) return;
				Vector2 point = _pointer;
				// Query the GUI's topmost control, so obscured cards never start a drag.
				Control? hovered = GetViewport().GuiGetHoveredControl();
				while (hovered is not null && hovered is not TableCardControl) hovered = hovered.GetParent() as Control;
				if (hovered is not TableCardControl card || card.Card is not { IsKnown: true }) return;
				_source = _interaction.Sources.FirstOrDefault(source => source.CardId == card.CardId);
				if (_source is null || !Board.VisibleCards.Values.Contains(card)) return;
				_sourceControl = card;
				_sourceModulate = card.Modulate;
				_pressedAt = point;
			}
			else if (_source is not null)
			{
				if (!IsDragging) { ClearGesture(); return; }
				GetViewport().SetInputAsHandled();
				var destination = HitDestination(_pointer);
				if (CanInteract?.Invoke() != true || destination is null || _decision is null)
				{
					Cancel("invalid-drop", true);
					return;
				}
				DecisionViewModel decision = _decision;
				var actions = destination.Actions;
				GestureEnded?.Invoke("drop", _source.CardId);
				ClearGesture();
				Dropped?.Invoke(decision, actions);
			}
		}
		else if (@event is InputEventMouseMotion && _source is not null)
		{
			if (CanInteract?.Invoke() != true) { Cancel("unavailable", false); return; }
			if (!IsDragging && _pointer.DistanceTo(_pressedAt) >= Appearance.DragThreshold)
				BeginDrag();
			if (IsDragging) GetViewport().SetInputAsHandled();
		}
	}

	private void BeginDrag()
	{
		if (_sourceControl?.Card is not { IsKnown: true } card || _source is null) return;
		DragStarted?.Invoke();
		Board.Hand.DragActive = true;
		_preview = TableCardControl.Create();
		_preview.Name = "DragPreview";
		_preview.SetCard(card);
		_preview.SetDisplaySize(_sourceControl.Size * Appearance.PreviewScale);
		_preview.MouseFilter = MouseFilterEnum.Ignore;
		_preview.FocusMode = FocusModeEnum.None;
		AddChild(_preview);
		_sourceControl.Modulate = new Color(1, 1, 1, 0.3f);
		Board.SetNewRemoteDropVisible(_source.Destinations.Any(d => d.Kind.ToString() == "NewRemote"));
		foreach (var destination in _source.Destinations)
			foreach (string geometry in destination.Kind == BoardInteractionDestinationKind.GeneralPlay
				? new[] { "GeneralPlay", "RunnerRig" } : new[] { destination.Kind.ToString() })
			{
				PanelContainer panel = GD.Load<PackedScene>("res://scenes/components/DropDestination.tscn").Instantiate<PanelContainer>();
				if (destination.Kind is BoardInteractionDestinationKind.RunnerHeap or BoardInteractionDestinationKind.CorpArchives)
					panel.CustomMinimumSize = Vector2.Zero;
				panel.MouseFilter = MouseFilterEnum.Ignore;
				AddChild(panel);
				_zones.Add((destination, panel, geometry));
			}
		_preview.MoveToFront();
		_hint.MoveToFront();
		_hint.Show();
		RefreshGeometry();
	}

	public override void _Process(double delta)
	{
		if (_source is not null && (CanInteract?.Invoke() != true || !IsInstanceValid(_sourceControl)))
			Cancel("unavailable", false);
		if (!IsDragging) return;
		Board.ScrollDragEdges(_pointer, delta, Appearance.EdgeWidth, Appearance.ScrollSpeed);
		RefreshGeometry(delta);
	}

	private void RefreshGeometry(double delta = 0)
	{
		if (_preview is null) return;
		Vector2 pointer = _pointer;
		Vector2 target = pointer - _preview.GetGlobalRect().Size * 0.5f;
		// Exponential following feels the same at different frame rates. Start
		// centered immediately; only subsequent pointer movement is smoothed.
		float weight = ReducedMotion || Appearance.FollowSmoothing <= 0 || delta <= 0
			? 1f : 1f - Mathf.Exp(-(float)delta / Appearance.FollowSmoothing);
		_preview.GlobalPosition = _preview.GlobalPosition.Lerp(target, weight);
		if (_preview.GlobalPosition.DistanceSquaredTo(target) < 0.01f)
			_preview.GlobalPosition = target;
		var hovered = HitDestination(pointer);
		foreach (var (destination, panel, geometry) in _zones)
		{
			Rect2? rect = Board.GetDropRect(geometry, destination.TargetId);
			panel.Visible = rect is { } visible && visible.HasArea();
			if (rect is not { } area || !area.HasArea()) continue;
			panel.GlobalPosition = area.Position;
			panel.Size = area.Size;
			bool current = Equals(destination, hovered);
			panel.Modulate = current ? Appearance.CurrentColor : Appearance.LegalColor;
			StyleBox? style = current ? Appearance.CurrentStyle : Appearance.LegalStyle;
			if (style is not null) panel.AddThemeStyleboxOverride("panel", style);
			panel.GetNode<Label>("%Caption").Text = current || destination.Kind == BoardInteractionDestinationKind.NewRemote
				? string.Empty : DestinationLabel(destination);
		}
		_hint.Text = hovered is null ? "Release here to return to hand · Esc cancels" : DestinationLabel(hovered);
		_hint.GlobalPosition = new Vector2(Math.Clamp(pointer.X + 18, GlobalPosition.X, Math.Max(GlobalPosition.X, GetGlobalRect().End.X - 380)),
			Math.Clamp(target.Y - 40, GlobalPosition.Y, Math.Max(GlobalPosition.Y, GetGlobalRect().End.Y - 40)));
	}

	private BoardInteractionDestination? HitDestination(Vector2 point) => _zones
		.Where(z => Board.GetDropRect(z.GeometryKind, z.Destination.TargetId) is { } r && r.HasPoint(point))
		.OrderBy(z => z.Destination.Kind.ToString() switch
		{
			"HostCard" => 0,
			"RunnerRig" or "ServerRoot" or "ServerIce" or "NewRemote" => 1,
			"ServerTarget" => 2,
			_ => 3,
		})
		.Select(z => z.Destination).FirstOrDefault();

	private string DestinationLabel(BoardInteractionDestination destination)
	{
		if (destination.Kind is BoardInteractionDestinationKind.RunnerHeap or BoardInteractionDestinationKind.CorpArchives)
			return destination.Kind == BoardInteractionDestinationKind.RunnerHeap ? "Discard to Heap" : "Discard to Archives";
		string label = destination.Kind.ToString() switch
		{
			"GeneralPlay" => "Play",
			"RunnerRig" => "Install in rig",
			"NewRemote" => "Install in new remote",
			"HostCard" => "Install on host",
			"ServerRoot" => $"Install in {Board.GetServerDisplayName(destination.TargetId!)} root",
			"ServerIce" => $"Install outermost ICE · {Board.GetServerDisplayName(destination.TargetId!)}",
			"ServerTarget" => $"Play on {Board.GetServerDisplayName(destination.TargetId!)}",
			_ => "Choose destination",
		};
		var costs = destination.Actions.Select(a => a.Parameters switch
		{
			Netrunner.Core.PlayCardParameters p => p.CreditCost,
			Netrunner.Core.InstallCardParameters i => i.CreditCost,
			_ => 0,
		}).Distinct().ToArray();
		string cost = costs.Length == 1 ? $"{costs[0]} credits" : "choose cost";
		bool selection = destination.Actions.Any(a => a.Parameters is Netrunner.Core.InstallCardParameters { FinalizeSelection: false });
		return $"{label} · {cost}{(selection ? " · choose replacements" : destination.Actions.Count > 1 ? " · choose target" : "")}";
	}

	public void Cancel(string reason, bool animate)
	{
		if (_source is null) return;
		GestureEnded?.Invoke(reason, _source.CardId);
		TableCardControl? returning = _preview;
		Vector2? target = IsInstanceValid(_sourceControl) ? _sourceControl!.GlobalPosition : null;
		_preview = null;
		ClearGesture();
		if (returning is null) return;
		if (!animate || ReducedMotion || target is null) { returning.QueueFree(); return; }
		_returnTween = CreateTween();
		_returnTween.TweenProperty(returning, "global_position", target.Value, Appearance.ReturnDuration)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		_returnTween.TweenCallback(Callable.From(returning.QueueFree));
	}

	private void OnFocusLost() => Cancel("focus-lost", false);
	private void RestoreSource()
	{
		if (IsInstanceValid(_sourceControl)) _sourceControl!.Modulate = _sourceModulate;
		_sourceControl = null;
	}
	private void ClearGesture()
	{
		RestoreSource();
		_source = null;
		_preview?.QueueFree();
		_preview = null;
		foreach (var (_, panel, _) in _zones) { RemoveChild(panel); panel.QueueFree(); }
		_zones.Clear();
		if (IsInstanceValid(Board))
		{
			Board.SetNewRemoteDropVisible(false);
			Board.Hand.DragActive = false;
		}
		if (IsInstanceValid(_hint)) _hint.Hide();
	}
}
