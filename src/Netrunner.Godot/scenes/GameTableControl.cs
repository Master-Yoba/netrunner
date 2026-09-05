using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Spatial presentation of one filtered match observation.
///
/// This control owns only presentation state. It does not know how an action
/// is issued and it never reaches into Core. The parent can listen to the
/// card/server events and map the selected presentation handle to its current
/// legal action set.
/// </summary>
public partial class GameTableControl : Control
{
	private static readonly Color TableColor = new(0.018f, 0.083f, 0.074f);
	private static readonly Color TableEdgeColor = new(0.07f, 0.28f, 0.23f);
	private static readonly Color SurfaceColor = new(0.035f, 0.12f, 0.12f, 0.96f);
	private static readonly Color SurfaceSoftColor = new(0.025f, 0.095f, 0.095f, 0.93f);
	private static readonly Color AccentColor = new(0.43f, 0.94f, 0.72f);
	private static readonly Color MutedColor = new(0.62f, 0.76f, 0.75f);
	private static readonly Color CorpColor = new(0.82f, 0.47f, 0.62f);
	private static readonly Color RunnerColor = new(0.34f, 0.73f, 0.96f);
	private static readonly Color WarningColor = new(0.98f, 0.76f, 0.37f);

	private readonly Dictionary<string, TableCardControl> _cards = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CardViewModel?> _cardModels = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ServerLaneParts> _serverLanes = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Control> _rigSlots = new(StringComparer.Ordinal);
	private readonly HashSet<string> _activeRigSlots = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _serverDisplayNames = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ServerKind> _serverKinds = new(StringComparer.Ordinal);
	private readonly HashSet<string> _activeCardKeys = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TableCardControl> _visibleCards = new(StringComparer.Ordinal);

	private MatchViewModel? _pendingView;
	private string? _lastBoardFingerprint;
	private VBoxContainer _tableLayout = null!;
	private HBoxContainer _corpStrip = null!;
	private HBoxContainer _serverLaneRow = null!;
	private HBoxContainer _runnerRigRow = null!;
	private HBoxContainer _localHandRow = null!;
	private ScrollContainer _serverScroll = null!;
	private ScrollContainer _runnerRigScroll = null!;
	private ScrollContainer _localHandScroll = null!;
	private Label _turnLabel = null!;
	private Label _runLabel = null!;
	private Label _corpResources = null!;
	private Label _runnerResources = null!;
	private Label _corpHandCaption = null!;
	private Label _runnerHandCaption = null!;
	private Label _localHandCaption = null!;
	private PanelContainer _runnerIdentitySlot = null!;
	private Label _tableHint = null!;
	private Label _corpScoreLabel = null!;
	private Label _runnerScoreLabel = null!;
	private Button _corpDeckPile = null!;
	private Button _corpArchivesPile = null!;
	private Button _runnerDeckPile = null!;
	private Button _runnerHeapPile = null!;
	private string? _selection;
	private string? _selectedServer;
	private IReadOnlySet<string> _targets = new HashSet<string>(StringComparer.Ordinal);
	private int _nextRemoteDisplayNumber = 1;
	private bool _shellBuilt;
	private string? _lastRunFocus;

	/// <summary>Raised with an engine-visible card handle when a card is selected.</summary>
	public event Action<string>? CardSelected;

	/// <summary>Raised with an engine-visible card handle when a card is inspected.</summary>
	public event Action<string>? CardInspected;

	/// <summary>
	/// Raised with a server or public zone handle when a lane or pile is clicked.
	/// Zone handles are prefixed with <c>zone:</c>; server handles remain the
	/// supplied opaque presentation reference.
	/// </summary>
	public event Action<string>? ServerSelected;

	/// <summary>
	/// All card controls currently represented on the board. Keys are visible
	/// card IDs for known cards and stable slot handles for face-down cards.
	/// Hidden IDs are never used as display text.
	/// </summary>
	public IReadOnlyDictionary<string, TableCardControl> VisibleCards => _visibleCards;

	/// <summary>
	/// Renders a new immutable observation into the persistent table shell.
	/// Thinking-only publications leave the existing controls in place; card
	/// controls are updated only when their viewer-safe presentation changed.
	/// </summary>
	public void Render(MatchViewModel view)
	{
		ArgumentNullException.ThrowIfNull(view);
		_pendingView = view;
		if (!_shellBuilt)
		{
			if (IsInsideTree())
			{
				BuildShell();
			}
			return;
		}

		RenderView(view);
	}

	/// <summary>Sets the presentation selection without submitting an action.</summary>
	public void SetSelection(string? cardId)
	{
		_selection = string.IsNullOrWhiteSpace(cardId) ? null : cardId;
		ApplySelectionAndTargets();
	}

	/// <summary>Marks viewer-safe legal targets without inferring any new ones.</summary>
	public void SetTargets(IReadOnlySet<string> targets)
	{
		ArgumentNullException.ThrowIfNull(targets);
		_targets = targets;
		ApplySelectionAndTargets();
	}

	/// <summary>Marks the selected server lane without committing an action.</summary>
	public void SetSelectedServer(string? serverId)
	{
		_selectedServer = string.IsNullOrWhiteSpace(serverId) ? null : serverId;
		ApplyServerSelection();
	}

	/// <summary>
	/// Returns the stable display name assigned to a server. Remote labels are
	/// allocated once and remain stable when another remote disappears.
	/// </summary>
	public string GetServerDisplayName(string serverId)
	{
		if (string.IsNullOrWhiteSpace(serverId))
		{
			return "Server";
		}

		if (_serverDisplayNames.TryGetValue(serverId, out string? existing))
		{
			return existing;
		}

		if (_serverKinds.TryGetValue(serverId, out ServerKind knownKind))
		{
			return DisplayNameForKind(knownKind, 0);
		}

		string fallback = $"Remote {_nextRemoteDisplayNumber++}";
		_serverDisplayNames[serverId] = fallback;
		return fallback;
	}

	public override void _Ready()
	{
		BuildShell();
		if (_pendingView is { } view)
		{
			RenderView(view);
		}
	}

	public override void _ExitTree()
	{
		StopMotion();
		foreach (TableCardControl card in _cards.Values)
		{
			DetachCard(card);
			// Some cards can be temporarily detached while a slot is being
			// reconciled. Queue every cached node explicitly so it cannot outlive
			// the table and leak artwork/text resources on scene reload.
			card.QueueFree();
		}
		foreach (Control slot in _rigSlots.Values)
		{
			if (!slot.IsQueuedForDeletion())
			{
				slot.QueueFree();
			}
		}
	}

	private void BuildShell()
	{
		if (_shellBuilt)
		{
			return;
		}

		_shellBuilt = true;
		MouseFilter = MouseFilterEnum.Ignore;
		FocusMode = FocusModeEnum.None;
		// The parent reserves the decision dock below the table. Keep the board
		// willing to fit a 1280×720 viewport; each hand/ICE strip owns its local
		// overflow instead of forcing a document-sized table.
		CustomMinimumSize = new Vector2(760, 0);

		ColorRect tableBackdrop = new()
		{
			Color = TableColor,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		SetFullRect(tableBackdrop);
		AddChild(tableBackdrop);

		PanelContainer tableSurface = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
		};
		SetFullRect(tableSurface);
		tableSurface.AddThemeStyleboxOverride("panel", MakePanelStyle(TableColor, TableEdgeColor, 1, 10));
		AddChild(tableSurface);

		MarginContainer margin = new();
		margin.AddThemeConstantOverride("margin_left", 14);
		margin.AddThemeConstantOverride("margin_top", 4);
		margin.AddThemeConstantOverride("margin_right", 14);
		margin.AddThemeConstantOverride("margin_bottom", 4);
		SetFullRect(margin);
		tableSurface.AddChild(margin);

		_tableLayout = new VBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_tableLayout.AddThemeConstantOverride("separation", 4);
		margin.AddChild(_tableLayout);

		BuildCorpStrip();
		BuildServerField();
		BuildRunnerField();

		_tableHint = MakeLabel(
			"Select a card or lane to stage a contextual action · Escape cancels local staging",
			11,
			MutedColor);
		_tableHint.HorizontalAlignment = HorizontalAlignment.Center;
		_tableHint.CustomMinimumSize = new Vector2(0, 12);
		_tableLayout.AddChild(_tableHint);
		_tableHint.Visible = false;
	}

	private void BuildHeader()
	{
		HBoxContainer header = new()
		{
			CustomMinimumSize = new Vector2(0, 42),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		header.AddThemeConstantOverride("separation", 10);
		_tableLayout.AddChild(header);

		Label title = MakeLabel("NETRUNNER TABLE", 18, AccentColor);
		title.VerticalAlignment = VerticalAlignment.Center;
		title.CustomMinimumSize = new Vector2(190, 0);
		header.AddChild(title);

		_turnLabel = MakeLabel("Waiting for a public turn", 13, Colors.White);
		_turnLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_turnLabel.VerticalAlignment = VerticalAlignment.Center;
		_turnLabel.HorizontalAlignment = HorizontalAlignment.Center;
		header.AddChild(_turnLabel);

		_runLabel = MakeLabel("No active run", 12, MutedColor);
		_runLabel.CustomMinimumSize = new Vector2(220, 0);
		_runLabel.VerticalAlignment = VerticalAlignment.Center;
		_runLabel.HorizontalAlignment = HorizontalAlignment.Right;
		header.AddChild(_runLabel);
	}

	private void BuildCorpStrip()
	{
		PanelContainer surface = MakeSurfacePanel(new Vector2(0, 26), expandVertical: false);
		_tableLayout.AddChild(surface);
		_corpStrip = new HBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_corpStrip.AddThemeConstantOverride("separation", 7);
		surface.AddChild(_corpStrip);

		Label side = MakeLabel("CORP", 11, CorpColor);
		side.CustomMinimumSize = new Vector2(48, 0);
		side.VerticalAlignment = VerticalAlignment.Center;
		_corpStrip.AddChild(side);
		_corpResources = MakeLabel("Credits · Clicks · Score", 11, Colors.White);
		_corpResources.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_corpResources.VerticalAlignment = VerticalAlignment.Center;
		_corpStrip.AddChild(_corpResources);
		_corpScoreLabel = MakeLabel("Score pile · 0", 10, MutedColor);
		_corpScoreLabel.CustomMinimumSize = new Vector2(92, 0);
		_corpScoreLabel.VerticalAlignment = VerticalAlignment.Center;
		_corpStrip.AddChild(_corpScoreLabel);
		_corpStrip.AddChild(MakePileButton("Scored", "zone:corp-score", "Browse Corp score area"));

		_corpHandCaption = MakeLabel("HQ · hand", 10, MutedColor);
		_corpHandCaption.CustomMinimumSize = new Vector2(150, 0);
		_corpHandCaption.VerticalAlignment = VerticalAlignment.Center;
		_corpStrip.AddChild(_corpHandCaption);

		_corpDeckPile = MakePileButton("R&D · —", "zone:corp-deck", "Corp R&D · face-down pile");
		_corpStrip.AddChild(_corpDeckPile);
		_corpArchivesPile = MakePileButton("Archives · —", "zone:corp-archives", "Corp Archives · public discard pile");
		_corpStrip.AddChild(_corpArchivesPile);

		_turnLabel = MakeLabel("Turn · phase", 10, AccentColor);
		_turnLabel.CustomMinimumSize = new Vector2(132, 0);
		_turnLabel.VerticalAlignment = VerticalAlignment.Center;
		_turnLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_corpStrip.AddChild(_turnLabel);
		_runLabel = MakeLabel("No active run", 10, WarningColor);
		_runLabel.CustomMinimumSize = new Vector2(160, 0);
		_runLabel.VerticalAlignment = VerticalAlignment.Center;
		_runLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_corpStrip.AddChild(_runLabel);
	}

	private void BuildServerField()
	{
		PanelContainer surface = MakeSurfacePanel(new Vector2(0, 200), expandVertical: true);
		_tableLayout.AddChild(surface);
		VBoxContainer content = MakeVBox(4);
		surface.AddChild(content);

		HBoxContainer heading = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(0, 16),
		};
		content.AddChild(heading);
		Label label = MakeLabel("CORP NETWORK · roots above · inner ICE top · outer ICE toward Runner", 10, MutedColor);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		heading.AddChild(label);

		_serverScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
			VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
			FollowFocus = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_serverLaneRow = new HBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_serverLaneRow.AddThemeConstantOverride("separation", 9);
		_serverScroll.AddChild(_serverLaneRow);
		content.AddChild(_serverScroll);
	}

	private void BuildRunnerField()
	{
		PanelContainer surface = MakeSurfacePanel(new Vector2(0, 150), expandVertical: false);
		_tableLayout.AddChild(surface);
		HBoxContainer field = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		field.AddThemeConstantOverride("separation", 6);
		surface.AddChild(field);

		PanelContainer summary = MakeInnerPanel(new Vector2(174, 0));
		VBoxContainer summaryContent = MakeVBox(2);
		summary.AddChild(summaryContent);
		summaryContent.AddChild(MakeLabel("RUNNER", 11, RunnerColor));
		_runnerResources = MakeLabel("Credits · Clicks · Score", 10, Colors.White);
		summaryContent.AddChild(_runnerResources);
		_runnerHandCaption = MakeLabel("Grip · hand", 10, MutedColor);
		summaryContent.AddChild(_runnerHandCaption);
		_runnerScoreLabel = MakeLabel("Score pile · 0", 10, MutedColor);
		summaryContent.AddChild(_runnerScoreLabel);
		summaryContent.AddChild(MakePileButton("Scored", "zone:runner-score", "Browse Runner score area"));
		field.AddChild(summary);

		_runnerIdentitySlot = MakeIdentitySlot("Runner identity");
		_runnerIdentitySlot.CustomMinimumSize = new Vector2(64, 100);
		field.AddChild(_runnerIdentitySlot);

		_runnerDeckPile = MakePileButton("Stack\n—", "zone:runner-deck", "Runner Stack · face-down pile");
		_runnerDeckPile.CustomMinimumSize = new Vector2(62, 30);
		field.AddChild(_runnerDeckPile);
		_runnerHeapPile = MakePileButton("Heap\n—", "zone:runner-heap", "Runner Heap · public discard pile");
		_runnerHeapPile.CustomMinimumSize = new Vector2(62, 30);
		field.AddChild(_runnerHeapPile);

		PanelContainer rig = MakeInnerPanel(new Vector2(210, 0));
		rig.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		VBoxContainer rigContent = MakeVBox(1);
		rig.AddChild(rigContent);
		rigContent.AddChild(MakeLabel("RIG", 9, MutedColor));
		_runnerRigScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
			VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
			FollowFocus = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_runnerRigRow = new HBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_runnerRigRow.AddThemeConstantOverride("separation", 8);
		_runnerRigScroll.AddChild(_runnerRigRow);
		rigContent.AddChild(_runnerRigScroll);
		field.AddChild(rig);

		PanelContainer hand = MakeInnerPanel(new Vector2(270, 0));
		hand.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		VBoxContainer handContent = MakeVBox(1);
		hand.AddChild(handContent);
		HBoxContainer heading = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(0, 17),
		};
		_localHandCaption = MakeLabel("LOCAL HAND", 9, AccentColor);
		_localHandCaption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		heading.AddChild(_localHandCaption);
		Label hint = MakeLabel("focus to lift", 9, MutedColor);
		hint.HorizontalAlignment = HorizontalAlignment.Right;
		heading.AddChild(hint);
		handContent.AddChild(heading);

		_localHandScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
			VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
			FollowFocus = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_localHandRow = new HBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		// A controlled overlap keeps a normal five-card hand compact while the
		// scroll container still makes every card focusable at overflow.
		_localHandRow.AddThemeConstantOverride("separation", -24);
		_localHandScroll.AddChild(_localHandRow);
		handContent.AddChild(_localHandScroll);
		field.AddChild(hand);
	}

	private void RenderView(MatchViewModel view)
	{
		_turnLabel.Text = FormatTurn(view);
		_runLabel.Text = FormatRun(view);
		_corpResources.Text = FormatResources(view.Corp);
		_runnerResources.Text = $"Credits {view.Runner.Credits} · Clicks {view.Runner.Clicks}\nScore {view.Runner.AgendaPoints} · Tags {view.Runner.Tags?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}";
		_corpScoreLabel.Text = $"Score {view.Corp.AgendaPoints}";
		if (view.Runner.UsedMemory is { } used && view.Runner.MemoryLimit is { } limit)
			_runnerResources.Text += $"\nMU {used}/{limit}";
		_runnerScoreLabel.Text = $"Score pile · {view.RunnerScore.Count} · {view.Runner.AgendaPoints} agenda point(s)";
		string boardFingerprint = BuildBoardFingerprint(view);
		if (string.Equals(_lastBoardFingerprint, boardFingerprint, StringComparison.Ordinal))
		{
			// Priority/thinking snapshots commonly change only the decision or
			// legal-action envelope. Keep every physical card and lane node in
			// place while those publications settle.
			ApplySelectionAndTargets();
			return;
		}
		var previousCards = CaptureMotionCards();
		_lastBoardFingerprint = boardFingerprint;

		Dictionary<string, TableCardControl> nextVisibleCards = new(StringComparer.Ordinal);
		_activeCardKeys.Clear();
		RenderTopZones(view, nextVisibleCards);
		RenderServers(view, nextVisibleCards);
		RenderRunnerRig(view, nextVisibleCards);
		RenderLocalHand(view, nextVisibleCards);

		foreach (string staleKey in _cards.Keys.Where(key => !_activeCardKeys.Contains(key)).ToArray())
		{
			TableCardControl stale = _cards[staleKey];
			if (stale.GetParent() is Node parent)
			{
				parent.RemoveChild(stale);
			}
			DetachCard(stale);
			stale.QueueFree();
			_cards.Remove(staleKey);
			_cardModels.Remove(staleKey);
		}

		_visibleCards.Clear();
		foreach ((string key, TableCardControl card) in nextVisibleCards)
		{
			_visibleCards[key] = card;
		}
		ApplySelectionAndTargets();
		PresentMotion(previousCards, view);
		EnsureRunVisible(view.Run);
	}

	private void EnsureRunVisible(RunFocusViewModel? run)
	{
		string? focusKey = run is null ? null : $"{run.Id}:{run.Step}:{run.CurrentIceIndex}";
		if (_lastRunFocus == focusKey) return;
		_lastRunFocus = focusKey;
		if (run is null || !_serverLanes.TryGetValue(run.AttackedServerId, out ServerLaneParts? lane)) return;
		Callable.From(() =>
		{
			if (!IsInsideTree() || _lastRunFocus != focusKey) return;
			_serverScroll.EnsureControlVisible(lane.Panel);
			string? id = run.CurrentIce?.Id;
			if (id is null || !_visibleCards.TryGetValue(id, out TableCardControl? ice)) return;
			for (Node? parent = ice.GetParent(); parent is not null && parent != this; parent = parent.GetParent())
				if (parent is ScrollContainer scroll) scroll.EnsureControlVisible(ice);
		}).CallDeferred();
	}

	private void RenderTopZones(
		MatchViewModel view,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		bool corpLocal = view.Viewer == PlayerSide.Corp;
		bool runnerLocal = view.Viewer == PlayerSide.Runner;
		_corpHandCaption.Text = corpLocal
			? $"HQ · {view.CorpHand.Count} cards · local"
			: $"HQ · {view.CorpHand.Count} cards · opponent backs";
		_runnerHandCaption.Text = runnerLocal
			? $"Grip · {view.RunnerHand.Count} cards · local"
			: $"Grip · {view.RunnerHand.Count} cards · opponent backs";

		_corpDeckPile.Text = $"R&D · {view.CorpDeck.Count}";
		_corpArchivesPile.Text = $"Archives · {view.CorpDiscard.Count}";
		_runnerDeckPile.Text = $"Stack\n{view.RunnerDeck.Count} cards";
		_runnerHeapPile.Text = $"Heap\n{view.RunnerHeap.Count} cards";
		UpdatePileTooltip(_corpDeckPile, $"Corp R&D · {view.CorpDeck.Count} face-down cards");
		UpdatePileTooltip(_corpArchivesPile, $"Corp Archives · {view.CorpDiscard.Count} public cards");
		UpdatePileTooltip(_runnerDeckPile, $"Runner Stack · {view.RunnerDeck.Count} face-down cards");
		UpdatePileTooltip(_runnerHeapPile, $"Runner Heap · {view.RunnerHeap.Count} public cards");

		RenderIdentity("runner-identity", _runnerIdentitySlot, view.RunnerIdentity, false, nextVisibleCards);
		// The upper strip carries the opponent HQ count and the local hand is
		// rendered in the lower hand lane. The opponent Grip remains a count-only
		// projection, preserving hidden information at both seats.
	}

	private void RenderServers(
		MatchViewModel view,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		foreach (ServerViewModel server in view.Servers)
		{
			_serverKinds[server.Id] = server.Kind;
			if (!_serverDisplayNames.ContainsKey(server.Id))
			{
				_serverDisplayNames[server.Id] = AllocateServerDisplayName(server, view.Servers);
			}
		}

		IReadOnlyList<ServerViewModel> ordered = view.Servers
			.OrderBy(static server => ServerKindRank(server.Kind))
			.ThenBy(server => server.Kind == ServerKind.Remote
				? RemoteNumber(_serverDisplayNames[server.Id])
				: 0)
			.ToArray();
		HashSet<string> currentServerIds = ordered.Select(server => server.Id).ToHashSet(StringComparer.Ordinal);

		foreach (ServerViewModel server in ordered)
		{
			if (!_serverLanes.TryGetValue(server.Id, out ServerLaneParts? lane))
			{
				lane = CreateServerLane(server.Id);
				_serverLanes[server.Id] = lane;
			}

			lane.Panel.Visible = true;
			lane.Header.Text = _serverDisplayNames[server.Id];
			lane.Header.TooltipText = $"Select {_serverDisplayNames[server.Id]} for contextual actions.";
			lane.RunMarker.Text = view.Run is { } run
				&& string.Equals(run.AttackedServerId, server.Id, StringComparison.Ordinal)
				? $"ACTIVE RUN · {run.FocusLabel}"
				: string.Empty;
			lane.RunMarker.Visible = lane.RunMarker.Text.Length > 0;
			lane.Panel.AddThemeStyleboxOverride("panel", MakePanelStyle(SurfaceSoftColor,
				lane.RunMarker.Visible ? WarningColor : TableEdgeColor, lane.RunMarker.Visible ? 3 : 1, 8));
			if (lane.RunMarker.Visible && view.Run?.Encounter is { } encounter)
			{
				lane.RunMarker.Text = $"▶ {encounter.Ice?.Title ?? "Face-down ICE"} · {encounter.RemainingSubroutines} unbroken";
				lane.RunMarker.TooltipText = $"Strength {encounter.EffectiveStrength ?? encounter.Ice?.Strength}\n"
					+ string.Join("\n", encounter.Subroutines.Select(sub => $"Subroutine {sub.Ordinal + 1}: {(sub.Broken ? "broken" : sub.Resolved ? "resolved" : "unbroken")}"));
			}
			lane.RootCaption.Text = server.Kind == ServerKind.Remote
				? "ROOT · remote content"
				: server.Kind == ServerKind.Archives
					? "ROOT · public archive"
					: server.Kind == ServerKind.RAndD
						? "ROOT · deck stack"
						: "ROOT · HQ";

			ClearNonCardChildren(lane.RootCards);
			ClearNonCardChildren(lane.IceCards);
			RenderServerRoot(
				server,
				lane.RootCards,
				server.Kind == ServerKind.Headquarters ? view.CorpIdentity : null,
				nextVisibleCards);
			if (server.Kind is ServerKind.RAndD or ServerKind.Headquarters)
			{
				int count = server.Kind == ServerKind.RAndD ? view.CorpDeck.Count : view.CorpHand.Count;
				for (int back = 0; back < Math.Min(count, 2); back++)
					EnsureCard($"pile:{server.Id}:{back}", lane.RootCards, null, false, nextVisibleCards);
			}
			if (server.Kind == ServerKind.Archives && view.CorpDiscard.Count > 0)
				EnsureCard($"archives:{view.CorpDiscard[^1].Id}", lane.RootCards, view.CorpDiscard[^1], false, nextVisibleCards);
			RenderServerIce(server, lane.IceCards, nextVisibleCards);
		}

		foreach ((string id, ServerLaneParts lane) in _serverLanes)
		{
			if (currentServerIds.Contains(id))
			{
				if (lane.Panel.GetParent() != _serverLaneRow)
				{
					_serverLaneRow.AddChild(lane.Panel);
				}
				continue;
			}

			lane.Panel.Visible = false;
		}
		ApplyServerSelection();
	}

	private void RenderServerRoot(
		ServerViewModel server,
		HBoxContainer rootCards,
		CardViewModel? corpIdentity,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		int index = 0;
		if (corpIdentity is not null)
		{
			TableCardControl identity = EnsureCard("corp-identity", rootCards, corpIdentity, false, nextVisibleCards);
			SetCompactCard(identity, new Vector2(70, 98));
		}
		foreach (CardViewModel? card in server.OrderedRoot)
		{
			string slot = $"server:{server.Id}:root:{index++}";
			TableCardControl control = EnsureCard(slot, rootCards, card, isIce: false, nextVisibleCards);
			SetCompactCard(control, new Vector2(70, 98));
		}
	}

	private void RenderServerIce(
		ServerViewModel server,
		VBoxContainer iceCards,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		// Match supplies outer-to-inner; the table puts inner ICE at the top
		// nearest the root and the outermost ICE at the bottom near the Runner.
		for (int displayIndex = server.OrderedIce.Count - 1; displayIndex >= 0; displayIndex--)
		{
			CardViewModel? card = server.OrderedIce[displayIndex];
			string slot = $"server:{server.Id}:ice:{displayIndex}";
			string? hostId = card?.Id ?? server.IcePositions.FirstOrDefault(position => position.Index == displayIndex)?.CardId;
			CardViewModel[] hosted = _pendingView?.RunnerRig.Where(child => hostId is not null && child.HostCardId == hostId).ToArray() ?? [];
			if (hosted.Length == 0)
				EnsureCard(slot, iceCards, card, isIce: true, nextVisibleCards);
			else
			{
				Control stack = new() { CustomMinimumSize = new Vector2(160, 104), MouseFilter = MouseFilterEnum.Ignore };
				iceCards.AddChild(stack);
				EnsureCard(slot, stack, card, true, nextVisibleCards);
				for (int index = 0; index < hosted.Length; index++)
				{
					TableCardControl attachment = EnsureCard($"hosted:{hosted[index].Id}", stack, hosted[index], false, nextVisibleCards);
					attachment.SetDisplaySize(new Vector2(60, 84));
					attachment.Position = new Vector2(22 + 14 * index, 20);
					attachment.TooltipText = $"Hosted on {card?.Title ?? "face-down ICE"}\n" + attachment.TooltipText;
				}
			}
		}
	}

	private void RenderRunnerRig(
		MatchViewModel view,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		_activeRigSlots.Clear();

		IReadOnlyList<CardViewModel> cards = view.RunnerRig;
		var hosted = cards
			.Where(card => card.HostCardId is { Length: > 0 })
			.GroupBy(card => card.HostCardId!, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
		foreach (CardViewModel card in cards.Where(card => string.IsNullOrEmpty(card.HostCardId)))
		{
			Control slot = EnsureRigSlot(card.Id);
			_activeRigSlots.Add(card.Id);
			ClearRigSlot(slot);
			RenderHostedSlot(slot, card, hosted.GetValueOrDefault(card.Id) ?? Array.Empty<CardViewModel>(), nextVisibleCards);
		}
		foreach (CardViewModel card in cards.Where(card => !string.IsNullOrEmpty(card.HostCardId) && !cards.Any(host => host.Id == card.HostCardId)
			&& !view.Servers.SelectMany(server => server.OrderedIce).Any(host => host?.Id == card.HostCardId)
			&& !view.Servers.SelectMany(server => server.IcePositions).Any(host => host.CardId == card.HostCardId)))
		{
			_activeRigSlots.Add($"orphan:{card.Id}");
			Control slot = EnsureRigSlot($"orphan:{card.Id}");
			ClearRigSlot(slot);
			RenderHostedSlot(slot, card, Array.Empty<CardViewModel>(), nextVisibleCards);
		}
		foreach ((string key, Control slot) in _rigSlots)
		{
			slot.Visible = _activeRigSlots.Contains(key);
		}
	}

	private Control EnsureRigSlot(string key)
	{
		if (!_rigSlots.TryGetValue(key, out Control? slot))
		{
			slot = new Control
			{
				CustomMinimumSize = new Vector2(80, 110),
				MouseFilter = MouseFilterEnum.Ignore,
			};
			_rigSlots[key] = slot;
		}

		if (slot.GetParent() != _runnerRigRow)
		{
			if (slot.GetParent() is Node oldParent)
			{
				oldParent.RemoveChild(slot);
			}
			_runnerRigRow.AddChild(slot);
		}
		return slot;
	}

	private static void ClearRigSlot(Control slot)
	{
		foreach (Node child in slot.GetChildren().ToArray())
		{
			slot.RemoveChild(child);
			if (child is not TableCardControl)
			{
				child.QueueFree();
			}
		}
	}

	private void RenderHostedSlot(
		Control slot,
		CardViewModel host,
		IReadOnlyList<CardViewModel> attachments,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		TableCardControl hostControl = EnsureCard($"rig:{host.Id}", slot, host, false, nextVisibleCards);
		hostControl.Position = Vector2.Zero;
		foreach ((CardViewModel attachment, int index) in attachments.Select((card, index) => (card, index)))
		{
			TableCardControl attachmentControl = EnsureCard(
				$"rig:{host.Id}:hosted:{attachment.Id}",
				slot,
				attachment,
				false,
				nextVisibleCards);
			attachmentControl.CustomMinimumSize = new Vector2(60, 84);
			attachmentControl.Position = new Vector2(22 + (index * 13), 18 + (index * 7));
			attachmentControl.ZIndex = index + 2;
		}
	}

	private void RenderLocalHand(
		MatchViewModel view,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		bool corpLocal = view.Viewer == PlayerSide.Corp;
		IReadOnlyList<CardViewModel> cards = corpLocal
			? view.CorpHand.VisibleCards
			: view.RunnerHand.VisibleCards;
		string zone = corpLocal ? "corp-hand" : "runner-hand";
		_localHandCaption.Text = corpLocal
			? $"LOCAL HAND · HQ · {cards.Count} visible cards"
			: $"LOCAL HAND · GRIP · {cards.Count} visible cards";
		foreach (Control child in _localHandRow.GetChildren().OfType<Control>().ToArray())
		{
			_localHandRow.RemoveChild(child);
		}
		foreach (CardViewModel card in cards)
		{
			EnsureCard($"{zone}:{card.Id}", _localHandRow, card, false, nextVisibleCards);
		}
	}

	private void RenderHandOrBacks(
		string zone,
		VBoxContainer target,
		IReadOnlyList<CardViewModel> visibleCards,
		int backCount,
		bool isIce,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		HBoxContainer row = target.GetChildren().OfType<HBoxContainer>().FirstOrDefault() ?? new HBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		if (row.GetParent() != target)
		{
			target.AddChild(row);
		}
		row.AddThemeConstantOverride("separation", visibleCards.Count > 0 ? -62 : -74);
		foreach (Node child in row.GetChildren().ToArray())
		{
			row.RemoveChild(child);
			if (child is not TableCardControl)
			{
				child.QueueFree();
			}
		}
		if (visibleCards.Count > 0)
		{
			foreach (CardViewModel card in visibleCards)
			{
				EnsureCard($"{zone}:{card.Id}", row, card, isIce, nextVisibleCards);
			}
			return;
		}

		int shownBacks = Math.Min(backCount, 4);
		for (int index = 0; index < shownBacks; index++)
		{
			EnsureCard($"{zone}:back:{index}", row, null, isIce, nextVisibleCards);
		}
		if (backCount > shownBacks)
		{
			Label more = MakeLabel($"+{backCount - shownBacks}", 11, MutedColor);
			more.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(more);
		}
	}

	private void RenderIdentity(
		string slot,
		PanelContainer parent,
		CardViewModel? card,
		bool isIce,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		foreach (Control child in parent.GetChildren().OfType<Control>().ToArray())
		{
			parent.RemoveChild(child);
		}
		if (card is null)
		{
			Label label = MakeLabel("Identity\nconcealed", 10, MutedColor);
			label.HorizontalAlignment = HorizontalAlignment.Center;
			label.VerticalAlignment = VerticalAlignment.Center;
			parent.AddChild(label);
			return;
		}

		TableCardControl control = EnsureCard(slot, parent, card, isIce, nextVisibleCards);
		SetCompactCard(control, new Vector2(70, 98));
	}

	private void RenderScoreCards(
		string zone,
		HBoxContainer target,
		IReadOnlyList<CardViewModel> cards,
		bool isIce,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		foreach (Control child in target.GetChildren().OfType<Control>().ToArray())
		{
			target.RemoveChild(child);
		}
		foreach (CardViewModel card in cards.Take(3))
		{
			TableCardControl control = EnsureCard($"{zone}:{card.Id}", target, card, isIce, nextVisibleCards);
			control.CustomMinimumSize = new Vector2(62, 86);
		}
		if (cards.Count > 3)
		{
			Label more = MakeLabel($"+{cards.Count - 3}", 11, MutedColor);
			more.VerticalAlignment = VerticalAlignment.Center;
			target.AddChild(more);
		}
	}

	private TableCardControl EnsureCard(
		string slot,
		Control parent,
		CardViewModel? model,
		bool isIce,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		string identity = model is { Id.Length: > 0 }
			? $"card:{model.Id}"
			: $"slot:{slot}";
		if (!_cards.TryGetValue(identity, out TableCardControl? control))
		{
			control = new TableCardControl();
			control.CardSelected += HandleCardSelected;
			control.CardInspected += HandleCardInspected;
			_cards[identity] = control;
			_cardModels[identity] = null;
		}

		if (control.GetParent() is Node oldParent && oldParent != parent)
		{
			oldParent.RemoveChild(control);
		}
		if (control.GetParent() != parent)
		{
			parent.AddChild(control);
		}

		if (!CardsMatch(_cardModels[identity], model))
		{
			control.SetCard(model);
			_cardModels[identity] = model;
		}
		control.SetIce(isIce);
		control.Scale = Vector2.One;
		{
			control.SetDisplaySize(isIce ? new Vector2(160, 58) : new Vector2(70, 98));
		}
		_activeCardKeys.Add(identity);
		string publicKey = model?.Id is { Length: > 0 } id ? id : slot;
		nextVisibleCards[publicKey] = control;
		return control;
	}

	private static void SetCompactCard(TableCardControl control, Vector2 layoutSize)
	{
		// TableCardControl draws its bounded face from the control's requested
		// size. Keep this helper scale-neutral; the caller's compact dimensions
		// are the actual card face size and remain legible in the server lane.
		control.Scale = Vector2.One;
		control.SetDisplaySize(layoutSize);
	}

	private ServerLaneParts CreateServerLane(string serverId)
	{
		PanelContainer panel = new()
		{
			CustomMinimumSize = new Vector2(190, 0),
			SizeFlagsVertical = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		panel.AddThemeStyleboxOverride("panel", MakePanelStyle(SurfaceSoftColor, TableEdgeColor, 1, 8));
		VBoxContainer content = MakeVBox(2);
		panel.AddChild(content);

		Button header = new()
		{
			CustomMinimumSize = new Vector2(0, 22),
			Flat = true,
			FocusMode = FocusModeEnum.All,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};
		header.Pressed += () => ServerSelected?.Invoke(serverId);
		content.AddChild(header);

		Label runMarker = MakeLabel(string.Empty, 9, WarningColor);
		runMarker.HorizontalAlignment = HorizontalAlignment.Center;
		runMarker.CustomMinimumSize = new Vector2(0, 20);
		runMarker.AutowrapMode = TextServer.AutowrapMode.Off;
		runMarker.Visible = false;
		content.AddChild(runMarker);

		Label rootCaption = MakeLabel("ROOT", 9, MutedColor);
		rootCaption.Visible = false;
		content.AddChild(rootCaption);
		HBoxContainer rootCards = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(0, 94),
		};
		rootCards.AddThemeConstantOverride("separation", -18);
		content.AddChild(rootCards);

		Label iceCaption = MakeLabel("ICE · inner → outer", 9, MutedColor);
		iceCaption.Visible = false;
		content.AddChild(iceCaption);
		ScrollContainer iceScroll = new()
		{
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 64),
			FollowFocus = true,
		};
		VBoxContainer iceCards = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		iceCards.AddThemeConstantOverride("separation", 4);
		iceScroll.AddChild(iceCards);
		content.AddChild(iceScroll);

		_serverLaneRow.AddChild(panel);
		return new ServerLaneParts(panel, header, runMarker, rootCaption, rootCards, iceCards);
	}

	private Button MakePileButton(string text, string handle, string tooltip)
	{
		Button button = new()
		{
			Text = text,
			CustomMinimumSize = new Vector2(68, 26),
			TooltipText = tooltip,
			FocusMode = FocusModeEnum.All,
			MouseDefaultCursorShape = Control.CursorShape.PointingHand,
		};
		button.AddThemeFontSizeOverride("font_size", 11);
		button.Pressed += () => ServerSelected?.Invoke(handle);
		return button;
	}

	private static PanelContainer MakeIdentitySlot(string tooltip)
	{
		PanelContainer panel = new()
		{
			CustomMinimumSize = new Vector2(96, 76),
			TooltipText = tooltip,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		panel.AddThemeStyleboxOverride("panel", MakePanelStyle(SurfaceColor, TableEdgeColor, 1, 6));
		return panel;
	}

	private static PanelContainer MakeSurfacePanel(Vector2 minimum, bool expandVertical)
	{
		PanelContainer panel = new()
		{
			CustomMinimumSize = minimum,
			SizeFlagsVertical = expandVertical ? SizeFlags.ExpandFill : SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		panel.AddThemeStyleboxOverride("panel", MakePanelStyle(SurfaceColor, TableEdgeColor, 1, 8));
		return panel;
	}

	private static PanelContainer MakeInnerPanel(Vector2 minimum)
	{
		PanelContainer panel = new()
		{
			CustomMinimumSize = minimum,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		panel.AddThemeStyleboxOverride("panel", MakePanelStyle(new Color(0.02f, 0.08f, 0.08f, 0.68f), TableEdgeColor, 1, 6));
		return panel;
	}

	private static VBoxContainer MakeVBox(int separation)
	{
		VBoxContainer box = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		box.AddThemeConstantOverride("separation", separation);
		return box;
	}

	private static Label MakeLabel(string text, int fontSize, Color color)
	{
		Label label = new()
		{
			Text = text,
			MouseFilter = MouseFilterEnum.Ignore,
			AutowrapMode = TextServer.AutowrapMode.Off,
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
		};
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private static StyleBoxFlat MakePanelStyle(Color background, Color border, int borderWidth, int radius)
	{
		return new StyleBoxFlat
		{
			BgColor = background,
			BorderColor = border,
			BorderWidthLeft = borderWidth,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomLeft = radius,
			CornerRadiusBottomRight = radius,
			ContentMarginLeft = 8,
			ContentMarginTop = 6,
			ContentMarginRight = 8,
			ContentMarginBottom = 6,
		};
	}

	private static void SetFullRect(Control control)
	{
		control.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
	}

	private static string FormatResources(PlayerViewModel player)
	{
		string tags = player.Tags is { } tagCount ? $" · Tags {tagCount}" : string.Empty;
		return $"Credits {player.Credits} · Clicks {player.Clicks} · Score {player.AgendaPoints}{tags}";
	}

	private static string BuildBoardFingerprint(MatchViewModel view)
	{
		var parts = new List<string>
		{
			$"viewer:{view.Viewer}",
			$"corpdeck:{view.CorpDeck.Count}",
			$"runnerdeck:{view.RunnerDeck.Count}",
			$"corpdiscard:{view.CorpDiscard.Count}",
			$"runnerheap:{view.RunnerHeap.Count}",
			$"corphand:{view.CorpHand.Count}:{CardsFingerprint(view.CorpHand.VisibleCards)}",
			$"runnerhand:{view.RunnerHand.Count}:{CardsFingerprint(view.RunnerHand.VisibleCards)}",
			$"rigrig:{CardsFingerprint(view.RunnerRig)}",
			$"corpscore:{CardsFingerprint(view.CorpScore)}",
			$"runnerscore:{CardsFingerprint(view.RunnerScore)}",
			$"corpid:{CardFingerprint(view.CorpIdentity)}",
			$"runnerid:{CardFingerprint(view.RunnerIdentity)}",
			$"run:{RunFingerprint(view.Run)}",
		};
		foreach (ServerViewModel server in view.Servers)
		{
			parts.Add($"server:{server.Id}:{server.Kind}:{CardsFingerprint(server.OrderedIce)}:{CardsFingerprint(server.OrderedRoot)}");
		}

		return string.Join("|", parts);
	}

	private static string RunFingerprint(RunFocusViewModel? run)
	{
		if (run is null)
		{
			return "none";
		}

		return string.Join(
			":",
			run.Id,
			run.AttackedServerId,
			run.Phase,
			run.Step,
			run.CurrentIceIndex,
			CardFingerprint(run.CurrentIce),
			run.Successful,
			run.Unsuccessful,
			run.FocusLabel,
			run.Access.State,
			run.RunCredits);
	}

	private static string CardsFingerprint(IEnumerable<CardViewModel?> cards) =>
		string.Join(",", cards.Select(CardFingerprint));

	private static string CardFingerprint(CardViewModel? card)
	{
		if (card is null)
		{
			return "hidden";
		}

		return string.Join(
			":",
			card.IsKnown ? card.Id : "hidden",
			card.IsKnown ? card.Title : string.Empty,
			card.IsKnown ? card.Type : string.Empty,
			card.Rezzed,
			card.AdvancementCounters,
			card.HostCardId ?? string.Empty,
			string.Join(";", card.CounterChips),
			card.AbilityCue ?? string.Empty,
			card.FirstXUseCue ?? string.Empty);
	}

	private static string FormatTurn(MatchViewModel view)
	{
		if (!view.Turn.IsAvailable)
		{
			return "TURN · awaiting public timing";
		}

		string turn = view.Turn.TurnNumber is { } number ? $"Turn {number}" : "Turn";
		string active = view.Turn.ActiveSide is { } side ? side.ToString() : "side unavailable";
		string phase = view.Turn.Phase is { } currentPhase ? currentPhase.ToString() : "phase unavailable";
		return $"{turn} · {active} · {phase}";
	}

	private string FormatRun(MatchViewModel view)
	{
		if (view.Run is not { } run)
		{
			return "No active run";
		}

		return $"RUN · {GetServerDisplayName(run.AttackedServerId)} · {run.FocusLabel}";
	}

	private string AllocateServerDisplayName(ServerViewModel server, IReadOnlyList<ServerViewModel> allServers)
	{
		if (server.Kind != ServerKind.Remote)
		{
			return DisplayNameForKind(server.Kind, 0);
		}

		// Allocate remotes in first-observed order, then keep the mapping even if
		// a server is removed. This prevents “Remote 2” becoming “Remote 1”.
		int ordinal = _nextRemoteDisplayNumber++;
		return $"Remote {ordinal}";
	}

	private static string DisplayNameForKind(ServerKind kind, int remoteNumber)
	{
		return kind switch
		{
			ServerKind.Headquarters => "HQ",
			ServerKind.RAndD => "R&D",
			ServerKind.Archives => "Archives",
			ServerKind.Remote => $"Remote {remoteNumber}",
			_ => "Server",
		};
	}

	private static int ServerKindRank(ServerKind kind) => kind switch
	{
		ServerKind.Headquarters => 0,
		ServerKind.RAndD => 1,
		ServerKind.Archives => 2,
		_ => 3,
	};

	private static int RemoteNumber(string displayName)
	{
		int separator = displayName.LastIndexOf(' ');
		return separator >= 0 && int.TryParse(displayName[(separator + 1)..], out int value)
			? value
			: int.MaxValue;
	}

	private static void UpdatePileTooltip(Button button, string tooltip)
	{
		button.TooltipText = $"{tooltip}. Click to inspect the public pile summary.";
	}

	private void ApplySelectionAndTargets()
	{
		foreach ((string key, TableCardControl card) in _cards)
		{
			bool selected = _selection is not null
				&& (string.Equals(key, $"card:{_selection}", StringComparison.Ordinal)
					|| string.Equals(key, $"slot:{_selection}", StringComparison.Ordinal)
					|| string.Equals(card.CardId, _selection, StringComparison.Ordinal));
			bool candidate = _targets.Contains(card.CardId)
				|| _targets.Contains(key)
				|| _targets.Contains(key.StartsWith("card:", StringComparison.Ordinal) ? key[5..] : key);
			card.SetTargetCandidate(candidate);
			card.SetSelected(selected);
		}
	}

	private void ApplyServerSelection()
	{
		foreach ((string id, ServerLaneParts lane) in _serverLanes)
		{
			bool selected = string.Equals(id, _selectedServer, StringComparison.Ordinal);
			lane.Header.AddThemeColorOverride("font_color", selected ? WarningColor : Colors.White);
			lane.Header.AddThemeColorOverride("font_hover_color", selected ? WarningColor : AccentColor);
			lane.Header.TooltipText = selected
				? $"Selected {_serverDisplayNames.GetValueOrDefault(id, "server")}. Choose a contextual action below."
				: $"Select {_serverDisplayNames.GetValueOrDefault(id, "server")} for contextual actions.";
		}
	}

	private void HandleCardSelected(string cardId)
	{
		if (string.IsNullOrWhiteSpace(cardId))
		{
			return;
		}

		_selection = cardId;
		ApplySelectionAndTargets();
		CardSelected?.Invoke(cardId);
	}

	private void HandleCardInspected(string cardId)
	{
		// A concealed projection may still carry an internal presentation handle;
		// never pass that handle to a zoom/history consumer as if it were a
		// visible identity. Empty means “clear the prior public inspection”.
		if (!_visibleCards.TryGetValue(cardId, out TableCardControl? control)
			|| control.Card is not { IsKnown: true })
		{
			CardInspected?.Invoke(string.Empty);
			return;
		}

		CardInspected?.Invoke(cardId);
	}

	private static void ClearNonCardChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren().ToArray())
		{
			if (child is not TableCardControl)
			{
				parent.RemoveChild(child);
				child.QueueFree();
			}
		}
	}

	private static bool CardsMatch(CardViewModel? left, CardViewModel? right)
	{
		if (ReferenceEquals(left, right))
		{
			return true;
		}
		if (left is null || right is null)
		{
			return false;
		}

		return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
			&& string.Equals(left.DefinitionId, right.DefinitionId, StringComparison.Ordinal)
			&& string.Equals(left.Title, right.Title, StringComparison.Ordinal)
			&& string.Equals(left.Type, right.Type, StringComparison.Ordinal)
			&& left.IsKnown == right.IsKnown
			&& left.Rezzed == right.Rezzed
			&& left.AdvancementCounters == right.AdvancementCounters
			&& left.Cost == right.Cost
			&& left.Strength == right.Strength
			&& left.AgendaPoints == right.AgendaPoints
			&& left.TrashCost == right.TrashCost
			&& string.Equals(left.RulesText, right.RulesText, StringComparison.Ordinal)
			&& string.Equals(left.HostCardId, right.HostCardId, StringComparison.Ordinal)
			&& left.Subtypes.SequenceEqual(right.Subtypes, StringComparer.Ordinal)
			&& left.CounterChips.SequenceEqual(right.CounterChips, StringComparer.Ordinal)
			&& string.Equals(left.AbilityCue, right.AbilityCue, StringComparison.Ordinal)
			&& string.Equals(left.FirstXUseCue, right.FirstXUseCue, StringComparison.Ordinal);
	}

	private void DetachCard(TableCardControl card)
	{
		card.CardSelected -= HandleCardSelected;
		card.CardInspected -= HandleCardInspected;
	}

	private sealed record ServerLaneParts(
		PanelContainer Panel,
		Button Header,
		Label RunMarker,
		Label RootCaption,
		HBoxContainer RootCards,
		VBoxContainer IceCards);
}
