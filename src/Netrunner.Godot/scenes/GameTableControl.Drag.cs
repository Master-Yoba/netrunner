using Godot;

namespace Netrunner.GodotClient;

public partial class GameTableControl
{
	public const string DragAppearancePath = "res://themes/DragAppearance.tres";
	private DragAppearance? _dragAppearance;
	private PanelContainer? _newRemoteDrop;
	private bool _newRemoteDropRequested;

	/// <summary>
	/// Shared, editor-authored values for drag previews and destination styling.
	/// The drag controller may also hold this resource directly when it is an
	/// overlay sibling of the board.
	/// </summary>
	[ExportGroup("Drag authoring")]
	[Export]
	public DragAppearance DragAppearance
	{
		get => _dragAppearance ??= LoadDefaultDragAppearance();
		set => _dragAppearance = value ?? LoadDefaultDragAppearance();
	}

	/// <summary>
	/// Resolves a viewer-safe destination to a global, visible rectangle.
	/// Rectangles are clipped to the board, every clipping ancestor, and every
	/// scroll viewport between the destination and the board's root.
	/// </summary>
	public Rect2? GetDropRect(string kind, string? targetId)
	{
		if (!TryEnsureDragShell() || string.IsNullOrWhiteSpace(kind))
		{
			return null;
		}

		return kind.Trim() switch
		{
			"GeneralPlay" => GetGeneralPlayRect(),
			"RunnerRig" => GetClippedGlobalRect(_runnerRigScroll),
			"HostCard" => GetVisibleCardRect(targetId),
			"ServerRoot" => GetServerRootRect(targetId),
			"ServerIce" => GetServerIceRect(targetId),
			"NewRemote" => GetNewRemoteRect(),
			"ServerTarget" => GetServerTargetRect(targetId),
			"RunnerHeap" => GetClippedGlobalRect(_runnerHeapPile),
			"CorpArchives" => _zonePiles.TryGetValue("zone:corp-archives", out BoardActionButton? archives)
				? GetClippedGlobalRect(archives) : null,
			_ => null,
		};
	}

	/// <summary>
	/// Adds a transient destination at the end of the authored server row.
	/// The placeholder remains independent of server reconciliation so a board
	/// render cannot discard it while a drag gesture is active.
	/// </summary>
	public void SetNewRemoteDropVisible(bool visible)
	{
		_newRemoteDropRequested = visible;
		if (!visible)
		{
			if (_newRemoteDrop is not null && GodotObject.IsInstanceValid(_newRemoteDrop))
			{
				_newRemoteDrop.Visible = false;
			}

			return;
		}

		if (!TryEnsureDragShell())
		{
			if (IsInsideTree())
			{
				Callable.From(() =>
				{
					if (GodotObject.IsInstanceValid(this) && _newRemoteDropRequested)
					{
						SetNewRemoteDropVisible(true);
					}
				}).CallDeferred();
			}

			return;
		}

		PanelContainer drop = EnsureNewRemoteDrop();
		ApplyNewRemoteDropLayout(drop);
		drop.Visible = true;
		MoveNewRemoteDropToEnd();
	}

	/// <summary>
	/// Scrolls any visible board scroll viewport while a drag pointer is near
	/// one of its edges. Pointer coordinates and destination rectangles use the
	/// same global canvas space.
	/// </summary>
	public void ScrollDragEdges(Vector2 globalPointer, double delta, float edgeWidth, float speed)
	{
		if (!TryEnsureDragShell() || delta <= 0.0 || edgeWidth <= 0.0f || speed <= 0.0f)
		{
			return;
		}

		float width = Mathf.Max(1.0f, edgeWidth);
		float amount = Mathf.Max(1.0f, (float)(delta * speed));
		foreach (ScrollContainer scroll in EnumerateScrollContainers(this))
		{
			if (!GodotObject.IsInstanceValid(scroll) || !scroll.IsVisibleInTree())
			{
				continue;
			}

			Rect2? visible = GetClippedGlobalRect(scroll);
			if (visible is not { } area || !area.Grow(width).HasPoint(globalPointer))
			{
				continue;
			}

			ScrollHorizontalEdge(scroll, area, globalPointer, width, amount);
			ScrollVerticalEdge(scroll, area, globalPointer, width, amount);
		}
	}

	private static DragAppearance LoadDefaultDragAppearance()
	{
		DragAppearance? appearance = GD.Load<DragAppearance>(DragAppearancePath);
		if (appearance is not null)
		{
			return appearance;
		}

		GD.PushError($"Shared drag appearance resource is missing: {DragAppearancePath}");
		return new DragAppearance();
	}

	private bool TryEnsureDragShell()
	{
		if (!_shellBuilt && IsInsideTree())
		{
			BuildShell();
		}

		return _shellBuilt && IsInsideTree();
	}

	private Rect2? GetGeneralPlayRect() => GetClippedGlobalRect(PlayArea);

	private Rect2? GetVisibleCardRect(string? targetId)
	{
		if (string.IsNullOrWhiteSpace(targetId)
			|| !TryGetVisibleCard(targetId.Trim(), out TableCardControl? card))
		{
			return null;
		}

		return card is null ? null : GetClippedGlobalRect(card);
	}

	private Rect2? GetServerRootRect(string? targetId)
	{
		return TryGetServerLane(targetId, out ServerLaneParts lane)
			? GetClippedGlobalRect(lane.RootCards)
			: null;
	}

	private Rect2? GetServerIceRect(string? targetId)
	{
		if (!TryGetServerLane(targetId, out ServerLaneParts lane))
		{
			return null;
		}

		// RenderServerIce inserts cards inner-to-outer, so the final visible child
		// is the ordinary outermost ICE installation surface.
		Control? outermost = lane.IceCards.GetChildren()
			.OfType<Control>()
			.LastOrDefault(static child => child.Visible && child.IsInsideTree());
		Control fallback = lane.IceCards.GetParent() as Control ?? lane.IceCards;
		return GetClippedGlobalRect(outermost ?? fallback);
	}

	private Rect2? GetNewRemoteRect()
	{
		if (!_newRemoteDropRequested || !TryEnsureDragShell())
		{
			return null;
		}

		PanelContainer drop = EnsureNewRemoteDrop();
		ApplyNewRemoteDropLayout(drop);
		drop.Visible = true;
		MoveNewRemoteDropToEnd();
		return GetClippedGlobalRect(drop);
	}

	private Rect2? GetServerTargetRect(string? targetId)
	{
		return TryGetServerLane(targetId, out ServerLaneParts lane)
			? GetClippedGlobalRect(lane.Panel)
			: null;
	}

	private bool TryGetVisibleCard(string targetId, out TableCardControl? card)
	{
		if (_visibleCards.TryGetValue(targetId, out TableCardControl? direct))
		{
			card = direct;
			return IsVisibleCard(card);
		}

		string unprefixed = targetId.StartsWith("card:", StringComparison.Ordinal)
			? targetId[5..]
			: targetId;
		if (_visibleCards.TryGetValue(unprefixed, out direct))
		{
			card = direct;
			return IsVisibleCard(card);
		}

		foreach ((string key, TableCardControl candidate) in _visibleCards)
		{
			if (string.Equals(key, targetId, StringComparison.Ordinal)
				|| string.Equals(candidate.CardId, unprefixed, StringComparison.Ordinal))
			{
				card = candidate;
				return IsVisibleCard(card);
			}
		}

		foreach (var server in _pendingView?.Servers ?? [])
		{
			var position = server.IcePositions.FirstOrDefault(p => p.CardId == targetId);
			if (position is not null && _visibleCards.TryGetValue($"server:{server.Id}:ice:{position.Index}", out var positional))
			{
				card = positional;
				return IsVisibleCard(card);
			}
		}
		card = null;
		return false;
	}

	private static bool IsVisibleCard(TableCardControl? card)
		=> card is not null
			&& GodotObject.IsInstanceValid(card)
			&& card.IsVisibleInTree();

	private bool TryGetServerLane(string? targetId, out ServerLaneParts lane)
	{
		lane = null!;
		if (string.IsNullOrWhiteSpace(targetId))
		{
			return false;
		}

		string id = targetId.Trim();
		if (!_serverLanes.TryGetValue(id, out ServerLaneParts? found) || found is null)
		{
			return false;
		}

		lane = found;
		return GodotObject.IsInstanceValid(lane.Panel)
			&& lane.Panel.IsVisibleInTree();
	}

	private Rect2? GetClippedGlobalRect(Control control)
	{
		if (!GodotObject.IsInstanceValid(control)
			|| !control.IsInsideTree()
			|| !control.IsVisibleInTree())
		{
			return null;
		}

		Rect2 result = control.GetGlobalRect();
		for (Node? parent = control; parent is not null; parent = parent.GetParent())
		{
			if (parent is not Control ancestor)
			{
				continue;
			}

			if (ReferenceEquals(ancestor, this)
				|| ancestor.ClipContents
				|| ancestor is ScrollContainer)
			{
				result = Intersect(result, ancestor.GetGlobalRect());
				if (!result.HasArea())
				{
					return null;
				}
			}
		}

		return result.HasArea() ? result : null;
	}

	private PanelContainer EnsureNewRemoteDrop()
	{
		if (_newRemoteDrop is not null && GodotObject.IsInstanceValid(_newRemoteDrop))
		{
			return _newRemoteDrop;
		}

		PackedScene? scene = GD.Load<PackedScene>("res://scenes/components/DropDestination.tscn");
		_newRemoteDrop = scene?.Instantiate<PanelContainer>() ?? new PanelContainer();
		_newRemoteDrop.Name = "NewRemoteDrop";
		_newRemoteDrop.MouseFilter = MouseFilterEnum.Ignore;
		if (_newRemoteDrop.GetNodeOrNull<Label>("%Caption") is not { } caption)
		{
			caption = new Label
			{
				Name = "Caption",
				Text = "New remote",
				MouseFilter = MouseFilterEnum.Ignore,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			_newRemoteDrop.AddChild(caption);
		}
		else
		{
			caption.Text = "New remote";
		}

		_serverLaneRow.AddChild(_newRemoteDrop);
		_newRemoteDrop.Visible = false;
		return _newRemoteDrop;
	}

	private void ApplyNewRemoteDropLayout(PanelContainer drop)
	{
		float laneWidth = _boardLayout?.LaneWidth ?? 285.0f;
		drop.CustomMinimumSize = new Vector2(Mathf.Max(180.0f, laneWidth), 58.0f);
		drop.SizeFlagsVertical = SizeFlags.ExpandFill;
	}

	private void MoveNewRemoteDropToEnd()
	{
		if (_newRemoteDrop is null
			|| !GodotObject.IsInstanceValid(_newRemoteDrop)
			|| _newRemoteDrop.GetParent() != _serverLaneRow)
		{
			return;
		}

		_serverLaneRow.MoveChild(_newRemoteDrop, _serverLaneRow.GetChildCount() - 1);
	}

	private static Rect2 Intersect(Rect2 left, Rect2 right)
	{
		float x1 = Mathf.Max(left.Position.X, right.Position.X);
		float y1 = Mathf.Max(left.Position.Y, right.Position.Y);
		float x2 = Mathf.Min(left.End.X, right.End.X);
		float y2 = Mathf.Min(left.End.Y, right.End.Y);
		return new Rect2(x1, y1, Mathf.Max(0.0f, x2 - x1), Mathf.Max(0.0f, y2 - y1));
	}

	private static IEnumerable<ScrollContainer> EnumerateScrollContainers(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			if (child is ScrollContainer scroll)
			{
				yield return scroll;
			}

			foreach (ScrollContainer nested in EnumerateScrollContainers(child))
			{
				yield return nested;
			}
		}
	}

	private static void ScrollHorizontalEdge(
		ScrollContainer scroll,
		Rect2 area,
		Vector2 pointer,
		float edgeWidth,
		float amount)
	{
		if (scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled)
		{
			return;
		}

		float distance;
		int direction;
		if (pointer.X < area.Position.X + edgeWidth)
		{
			distance = pointer.X - area.Position.X;
			direction = -1;
		}
		else if (pointer.X > area.End.X - edgeWidth)
		{
			distance = area.End.X - pointer.X;
			direction = 1;
		}
		else
		{
			return;
		}

		float intensity = 1.0f - Mathf.Clamp(distance / edgeWidth, 0.0f, 1.0f);
		int maximum = Mathf.Max(0, Mathf.RoundToInt(scroll.GetHScrollBar().MaxValue));
		scroll.ScrollHorizontal = Mathf.Clamp(
			scroll.ScrollHorizontal + Mathf.RoundToInt(direction * amount * intensity),
			0,
			maximum);
	}

	private static void ScrollVerticalEdge(
		ScrollContainer scroll,
		Rect2 area,
		Vector2 pointer,
		float edgeWidth,
		float amount)
	{
		if (scroll.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled)
		{
			return;
		}

		float distance;
		int direction;
		if (pointer.Y < area.Position.Y + edgeWidth)
		{
			distance = pointer.Y - area.Position.Y;
			direction = -1;
		}
		else if (pointer.Y > area.End.Y - edgeWidth)
		{
			distance = area.End.Y - pointer.Y;
			direction = 1;
		}
		else
		{
			return;
		}

		float intensity = 1.0f - Mathf.Clamp(distance / edgeWidth, 0.0f, 1.0f);
		int maximum = Mathf.Max(0, Mathf.RoundToInt(scroll.GetVScrollBar().MaxValue));
		scroll.ScrollVertical = Mathf.Clamp(
			scroll.ScrollVertical + Mathf.RoundToInt(direction * amount * intensity),
			0,
			maximum);
	}
}
