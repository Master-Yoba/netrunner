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
[Tool]
public partial class GameTableControl : Control
{
	private static readonly Color WarningColor = new(0.98f, 0.76f, 0.37f);
	private static readonly Color AccentColor = new(0.43f, 0.94f, 0.72f);
	private static readonly Color MutedColor = new(0.62f, 0.76f, 0.75f);
	public const string BoardScenePath = "res://scenes/components/GameBoard.tscn";
	public const string BoardLayoutPath = "res://themes/BoardLayout.tres";
	public enum PreviewDensity { Off, Empty, Normal, Dense }
	private enum CardSizeRole { CompactPortrait, Ice, HostedPortrait, FullPortrait }

	private readonly Dictionary<string, TableCardControl> _cards = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CardViewModel?> _cardModels = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ServerLaneParts> _serverLanes = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Control> _rigSlots = new(StringComparer.Ordinal);
	private readonly HashSet<string> _activeRigSlots = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _serverDisplayNames = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ServerKind> _serverKinds = new(StringComparer.Ordinal);
	private readonly HashSet<string> _activeCardKeys = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TableCardControl> _visibleCards = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CardSizeIntent> _cardSizeIntents = new(StringComparer.Ordinal);
	private readonly Dictionary<string, (CardAppearance Appearance, Action Handler)> _cardAppearanceHandlers = new(StringComparer.Ordinal);

	private MatchViewModel? _pendingView;
	private string? _lastBoardFingerprint;
	private BoardLayout? _boardLayout;
	private bool _boardLayoutConnected;
	private bool _previewDirty;
	private VBoxContainer _tableLayout = null!;
	private VBoxContainer _corpSummary = null!;
	private HBoxContainer _serverLaneRow = null!;
	private HBoxContainer _runnerRigRow = null!;
	private HandFanControl _localHandRow = null!;
	public HandFanControl Hand => _localHandRow;
	private BoardFitViewport _serverViewport = null!;
	private ScrollContainer _runnerRigScroll = null!;
	private Label _turnLabel = null!;
	private Label _runLabel = null!;
	private GameSymbolLabel _corpResources = null!;
	private GameSymbolLabel _runnerResources = null!;
	private Label _corpHandCaption = null!;
	private Label _runnerHandCaption = null!;
	private Label _localHandCaption = null!;
	private MarginContainer _runnerIdentitySlot = null!;
	private Label _tableHint = null!;
	private Label _corpScoreLabel = null!;
	private Label _runnerScoreLabel = null!;
	private Button _runnerDeckPile = null!;
	private Button _runnerHeapPile = null!;
	/// <summary>Positional handles ("server:X:ice:0") for the card currently in each server slot.</summary>
	private readonly Dictionary<string, TableCardControl> _slotAliases = new(StringComparer.Ordinal);
	private BoardActionButton? _corpScoredPile;
	private BoardActionButton? _runnerScoredPile;
	private string? _selection;
	private string? _selectedServer;
	private IReadOnlySet<string> _targets = new HashSet<string>(StringComparer.Ordinal);
	private IReadOnlySet<string> _actionableSources = new HashSet<string>(StringComparer.Ordinal);
	private int _nextRemoteDisplayNumber = 1;
	private bool _shellBuilt;
	private PreviewDensity _previewMode;
	private PlayerSide _previewSeat = PlayerSide.Runner;

	/// <summary>
	/// Shared authored board geometry. Card footprints are derived from each
	/// card's CardAppearance and the scale intent in this resource.
	/// </summary>
	[ExportGroup("Board authoring")]
	[Export]
	public BoardLayout BoardLayout
	{
		get => _boardLayout ??= LoadDefaultBoardLayout();
		set
		{
			var next = value ?? LoadDefaultBoardLayout();
			if (ReferenceEquals(_boardLayout, next))
			{
				return;
			}

			DisconnectBoardLayout();
			_boardLayout = next;
			ConnectBoardLayout();
			ApplyBoardLayout();
		}
	}

	/// <summary>Compatibility alias used by board-authoring helpers.</summary>
	public BoardLayout Layout => BoardLayout;

	[Export]
	public PreviewDensity PreviewMode
	{
		get => _previewMode;
		set
		{
			if (_previewMode == value)
			{
				return;
			}

			_previewMode = value;
			_previewDirty = true;
			if (IsInsideTree())
			{
				SetProcess(Engine.IsEditorHint() && value != PreviewDensity.Off);
			}
		}
	}

	[Export]
	public PlayerSide PreviewSeat
	{
		get => _previewSeat;
		set
		{
			if (_previewSeat == value)
			{
				return;
			}

			_previewSeat = value;
			_previewDirty = true;
			if (IsInsideTree() && Engine.IsEditorHint() && _previewMode != PreviewDensity.Off)
			{
				SetProcess(true);
			}
		}
	}

	/// <summary>Raised with an engine-visible card handle when a card is selected.</summary>
	public event Action<string>? CardSelected;

	/// <summary>Raised with an engine-visible card handle when a card is inspected.</summary>
	public event Action<string>? CardInspected;

	/// <summary>Raised when a known card enters or leaves mouse/focus interaction.</summary>
	public event Action<TableCardControl, bool>? CardHoverChanged;

	/// <summary>Set before rendering when the owner provides a custom card hover preview.</summary>
	public bool UseCardHoverPreview { get; set; }

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

	/// <summary>
	/// Marks only card handles that occur as the source of a currently issued
	/// action. The table never infers availability from card metadata.
	/// </summary>
	public void SetActionableSources(IReadOnlySet<string> sources)
	{
		ArgumentNullException.ThrowIfNull(sources);
		_actionableSources = sources;
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

		if (_serverKinds.TryGetValue(serverId, out ServerKind knownKind)
			&& knownKind != ServerKind.Remote)
		{
			// The engine's central-server kind is authoritative. Rewriting the
			// cached value also repairs a label that was allocated before the
			// server appeared in an observation.
			string authoritative = DisplayNameForKind(knownKind, 0);
			_serverDisplayNames[serverId] = authoritative;
			return authoritative;
		}

		if (_serverDisplayNames.TryGetValue(serverId, out string? existing))
		{
			return existing;
		}

		// A caller may ask for the run label before the server list has been
		// reconciled. An unknown ID must not consume a remote ordinal.
		return "Server";
	}

	public static GameTableControl Create()
	{
		PackedScene? scene = GD.Load<PackedScene>(BoardScenePath);
		if (scene is null)
		{
			string message = $"Production board scene is missing: {BoardScenePath}";
			GD.PushError(message);
			throw new InvalidOperationException(message);
		}

		return scene.Instantiate<GameTableControl>();
	}

	public override void _Ready()
	{
		BuildShell();
		if (_pendingView is { } view)
		{
			RenderView(view);
		}
		else if (Engine.IsEditorHint() && _previewMode != PreviewDensity.Off)
		{
			_previewDirty = true;
			SetProcess(true);
			RefreshEditorPreview();
		}
	}

	public override void _EnterTree()
	{
		ConnectBoardLayout();
		foreach ((string identity, TableCardControl card) in _cards)
		{
			AttachCard(card);
			ConnectCardAppearance(identity, card);
			ApplyCardDisplaySize(identity);
		}
		ApplyBoardLayout();
	}

	public override void _Process(double delta)
	{
		Breathe(delta);
		UpdateBackdropSplit();
		UpdateRunConnection(delta);
		UpdateTargetGlow(delta);
		BalanceRigSpacers();
		if (Engine.IsEditorHint() && _previewDirty && _shellBuilt)
		{
			RefreshEditorPreview();
		}
	}

	public override void _ExitTree()
	{
		DisconnectBoardLayout();
		StopMotion();
		foreach ((string identity, TableCardControl card) in _cards.Select(pair => (pair.Key, pair.Value)).ToArray())
		{
			// The board can leave and re-enter the tree while an editor preview or
			// host scene is rebuilt. Drop callbacks, but retain each card's sizing
			// intent so re-entry can reconnect and refresh the same controls.
			DetachCardAppearance(identity, clearIntent: false);
			DetachCard(card);
		}
	}

	private void BuildShell()
	{
		if (_shellBuilt)
		{
			return;
		}

		// Main still has a compatibility path that constructs this control with
		// `new`. Move the authored children from the production PackedScene into
		// that instance so there is one layout definition and no duplicate tree.
		if (!TryBindAuthoredShell())
		{
			TransferAuthoredShell();
		}

		if (!TryBindAuthoredShell())
		{
			GD.PushError("GameTableControl requires the authored GameBoard scene tree.");
			return;
		}

		_shellBuilt = true;
		MouseFilter = MouseFilterEnum.Ignore;
		FocusMode = FocusModeEnum.None;
		ClipContents = true;
		ConnectAuthoredZoneButtons();
		ConnectBoardLayout();
		ApplyBoardLayout();
	}

	private void ConnectAuthoredZoneButtons()
	{
		_runnerDeckPile.Pressed += () => BasicActionRequested?.Invoke(PlayerSide.Runner, ActionKind.DrawCard);
		_corpCredits.Pressed += () => BasicActionRequested?.Invoke(PlayerSide.Corp, ActionKind.GainCredit);
		_runnerCredits.Pressed += () => BasicActionRequested?.Invoke(PlayerSide.Runner, ActionKind.GainCredit);
		_runnerHeapPile.Pressed += () => ServerSelected?.Invoke("zone:runner-heap");
		BoardActionButton heap = (BoardActionButton)_runnerHeapPile;
		heap.MouseEntered += () => HoverPileFace(heap, true);
		heap.MouseExited += () => HoverPileFace(heap, false);
		heap.FocusEntered += () => HoverPileFace(heap, true);
		heap.FocusExited += () => HoverPileFace(heap, false);
		_corpScoredPile = GetNodeOrNull<BoardActionButton>("Surface/Margins/Layout/ServerField/Content/Summary/Content/CorpScored");
		_runnerScoredPile = GetNodeOrNull<BoardActionButton>("Surface/Margins/Layout/RunnerField/Content/Summary/Content/Scored");
		foreach ((BoardActionButton? pile, string zone) in new[] { (_corpScoredPile, "zone:corp-score"), (_runnerScoredPile, "zone:runner-score") })
		{
			if (pile is null) continue;
			pile.Pressed += () => ServerSelected?.Invoke(zone);
			pile.MouseEntered += () => HoverPileFace(pile, true);
			pile.MouseExited += () => HoverPileFace(pile, false);
		}
	}

	private bool TryBindAuthoredShell()
	{
		_tableLayout = GetNodeOrNull<VBoxContainer>("Surface/Margins/Layout")!;
		_corpSummary = GetNodeOrNull<VBoxContainer>("Surface/Margins/Layout/ServerField/Content/Summary/Content")!;
		_serverViewport = GetNodeOrNull<BoardFitViewport>("Surface/Margins/Layout/ServerField/Content/Network/Viewport")!;
		_serverLaneRow = GetNodeOrNull<HBoxContainer>("Surface/Margins/Layout/ServerField/Content/Network/Viewport/Lanes")!;
		_runnerRigScroll = GetNodeOrNull<ScrollContainer>("Surface/Margins/Layout/RunnerField/Content/Rig/Content/Scroll")!;
		_runnerRigRow = GetNodeOrNull<HBoxContainer>("Surface/Margins/Layout/RunnerField/Content/Rig/Content/Scroll/Cards")!;
		_localHandRow = GetNodeOrNull<HandFanControl>("LocalHand")!;
		_turnLabel = GetNodeOrNull<Label>("Surface/Margins/Layout/ServerField/Content/Network/Situation/Turn")!;
		_runLabel = GetNodeOrNull<Label>("Surface/Margins/Layout/ServerField/Content/Network/Situation/Run")!;
		_corpResources = GetNodeOrNull<GameSymbolLabel>("Surface/Margins/Layout/ServerField/Content/Summary/Content/Resources")!;
		_runnerResources = GetNodeOrNull<GameSymbolLabel>("Surface/Margins/Layout/RunnerField/Content/Summary/Content/Resources")!;
		_corpHandCaption = GetNodeOrNull<Label>("Surface/Margins/Layout/ServerField/Content/Summary/Content/HQ")!;
		_runnerHandCaption = GetNodeOrNull<Label>("Surface/Margins/Layout/RunnerField/Content/Summary/Content/HandCaption")!;
		_localHandCaption = GetNodeOrNull<Label>("LocalHand/Caption")!;
		_runnerIdentitySlot = GetNodeOrNull<MarginContainer>("Surface/Margins/Layout/RunnerField/Content/Identity/Content/Cards")!;
		_tableHint = GetNodeOrNull<Label>("Surface/Margins/Layout/Hint")!;
		_corpScoreLabel = GetNodeOrNull<Label>("Surface/Margins/Layout/ServerField/Content/Summary/Content/Score")!;
		_runnerScoreLabel = GetNodeOrNull<Label>("Surface/Margins/Layout/RunnerField/Content/Summary/Content/Score")!;
		_corpCredits = GetNodeOrNull<BoardActionButton>("Surface/Margins/Layout/ServerField/Content/Summary/Content/Credits")!;
		_runnerCredits = GetNodeOrNull<BoardActionButton>("Surface/Margins/Layout/RunnerField/Content/Summary/Content/Credits")!;
		_runnerDeckPile = GetNodeOrNull<Button>("Surface/Margins/Layout/RunnerField/Content/Deck")!;
		_runnerHeapPile = GetNodeOrNull<Button>("Surface/Margins/Layout/RunnerField/Content/Discard")!;

		return _tableLayout is not null
			&& _corpSummary is not null
			&& _serverViewport is not null
			&& _serverLaneRow is not null
			&& _runnerRigScroll is not null
			&& _runnerRigRow is not null
			&& _localHandRow is not null
			&& _turnLabel is not null
			&& _runLabel is not null
			&& _corpResources is not null
			&& _runnerResources is not null
			&& _corpHandCaption is not null
			&& _runnerHandCaption is not null
			&& _localHandCaption is not null
			&& _runnerIdentitySlot is not null
			&& _tableHint is not null
			&& _corpScoreLabel is not null
			&& _runnerScoreLabel is not null
			&& _corpCredits is not null
			&& _runnerCredits is not null
			&& _runnerDeckPile is not null
			&& _runnerHeapPile is not null;
	}

	private void TransferAuthoredShell()
	{
		PackedScene? scene = GD.Load<PackedScene>(BoardScenePath);
		if (scene is null)
		{
			return;
		}

		Node? template = scene.Instantiate();
		if (template is not GameTableControl templateBoard)
		{
			template?.Free();
			return;
		}

		if (_boardLayout is null)
		{
			_boardLayout = templateBoard.BoardLayout;
		}

		foreach (Node child in template.GetChildren().ToArray())
		{
			template.RemoveChild(child);
			AddChild(child);
		}

		template.Free();
	}

	private static BoardLayout LoadDefaultBoardLayout()
	{
		BoardLayout? layout = GD.Load<BoardLayout>(BoardLayoutPath);
		if (layout is not null)
		{
			return layout;
		}

		GD.PushError($"Shared board layout resource is missing: {BoardLayoutPath}");
		return new BoardLayout();
	}

	private void ConnectBoardLayout()
	{
		if (!IsInsideTree() || _boardLayoutConnected || _boardLayout is null || !GodotObject.IsInstanceValid(_boardLayout))
		{
			return;
		}

		_boardLayout.Changed += HandleBoardLayoutChanged;
		_boardLayoutConnected = true;
	}

	private void DisconnectBoardLayout()
	{
		if (!_boardLayoutConnected || _boardLayout is null || !GodotObject.IsInstanceValid(_boardLayout))
		{
			_boardLayoutConnected = false;
			return;
		}

		_boardLayout.Changed -= HandleBoardLayoutChanged;
		_boardLayoutConnected = false;
	}

	private void HandleBoardLayoutChanged()
	{
		ApplyBoardLayout();
	}

	private void ApplyBoardLayout()
	{
		if (!_shellBuilt || _boardLayout is null || !IsInstanceValid(_boardLayout))
		{
			return;
		}

		int regionSpacing = Mathf.RoundToInt(_boardLayout.RegionSpacing);
		int cardSeparation = Mathf.RoundToInt(_boardLayout.CardSeparation);
		CustomMinimumSize = new Vector2(_boardLayout.MinimumBoardWidth, CustomMinimumSize.Y);
		MarginContainer? margins = GetNodeOrNull<MarginContainer>("Surface/Margins");
		if (margins is not null)
		{
			Vector4 outer = _boardLayout.Margins;
			margins.AddThemeConstantOverride("margin_left", Mathf.RoundToInt(outer.X));
			margins.AddThemeConstantOverride("margin_top", Mathf.RoundToInt(outer.Y));
			margins.AddThemeConstantOverride("margin_right", Mathf.RoundToInt(outer.Z));
			margins.AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(outer.W));
		}

		_tableLayout.AddThemeConstantOverride("separation", regionSpacing);
		_corpSummary.AddThemeConstantOverride("separation", 3);
		_serverLaneRow.AddThemeConstantOverride("separation", regionSpacing);
		_runnerRigRow.AddThemeConstantOverride("separation", cardSeparation);

		PanelContainer? serverField = GetNodeOrNull<PanelContainer>("Surface/Margins/Layout/ServerField");
		if (serverField is not null)
		{
			serverField.CustomMinimumSize = new Vector2(serverField.CustomMinimumSize.X, _boardLayout.ServerFieldMinimumHeight);
		}
		Node? runnerField = GetNodeOrNull<Node>("Surface/Margins/Layout/RunnerField");
		if (runnerField is Control runnerControl)
		{
			runnerControl.CustomMinimumSize = new Vector2(runnerControl.CustomMinimumSize.X, _boardLayout.RunnerFieldMinimumHeight);
		}

		foreach (ServerLaneParts lane in _serverLanes.Values)
		{
			ApplyLaneLayout(lane);
		}
		foreach (string identity in _cardSizeIntents.Keys.ToArray())
		{
			ApplyCardDisplaySize(identity);
		}
	}

	private void ApplyLaneLayout(ServerLaneParts lane)
	{
		if (_boardLayout is null || !IsInstanceValid(lane.Panel))
		{
			return;
		}

		lane.Panel.CustomMinimumSize = new Vector2(_boardLayout.LaneWidth, lane.Panel.CustomMinimumSize.Y);
		lane.RootCards.AddThemeConstantOverride("separation", -Mathf.RoundToInt(_boardLayout.RootCardOverlap));
		lane.IceCards.AddThemeConstantOverride("separation", Mathf.RoundToInt(_boardLayout.CardSeparation));
	}

	private void RefreshEditorPreview()
	{
		_previewDirty = false;
		if (!Engine.IsEditorHint() || !_shellBuilt || _previewMode == PreviewDensity.Off)
		{
			return;
		}

		UiPreviewSamples.Density density = (UiPreviewSamples.Density)((int)_previewMode - 1);
		Render(UiPreviewSamples.Board(_previewSeat, density));
	}

	private void RenderView(MatchViewModel view)
	{
		ApplyFactionChrome(view);
		ApplySeatOrientation(view.Viewer);
		UpdateOpponentHand(view);
		CacheServerDisplayNames(view.Servers);
		_turnLabel.Text = FormatTurn(view);
		_runLabel.Text = FormatRun(view);
		_corpResources.SymbolText = $"[click] {ClickPips(view.Corp.Clicks, 3)} · [agenda point] {view.Corp.AgendaPoints}";
		ShowCount(_corpCredits, view.Corp.Credits);
		ShowCount(_runnerCredits, view.Runner.Credits);
		_runnerResources.SymbolText = $"[click] {ClickPips(view.Runner.Clicks, 4)} · [agenda point] {view.Runner.AgendaPoints}\n"
			+ $"[tag] {view.Runner.Tags?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}";
		_corpScoreLabel.Text = $"Score {view.Corp.AgendaPoints} · Threat {view.ThreatLevel}";
		if (view.Runner.UsedMemory is { } used && view.Runner.MemoryLimit is { } limit)
			_runnerResources.SymbolText += $" · [MU] {used}/{limit}";
		_runnerScoreLabel.Text = $"Score pile · {view.RunnerScore.Count} · {view.Runner.AgendaPoints} agenda point(s)";
		string boardFingerprint = BuildBoardFingerprint(view);
		if (string.Equals(_lastBoardFingerprint, boardFingerprint, StringComparison.Ordinal))
		{
			// Priority/thinking snapshots commonly change only the decision or
			// legal-action envelope. Keep every physical card and lane node in
			// place while those publications settle.
			ApplySelectionAndTargets();
			RefreshEncounterStrength(view);
			return;
		}
		MatchViewModel? previousView = _motionView;
		_motionView = view;
		DrawPlan? draw = CaptureDrawPlan(previousView, view);
		var previousCards = CaptureMotionCards();
		_lastBoardFingerprint = boardFingerprint;

		Dictionary<string, TableCardControl> nextVisibleCards = new(StringComparer.Ordinal);
		_activeCardKeys.Clear();
		_slotAliases.Clear();
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
			DetachCardAppearance(staleKey);
			stale.QueueFree();
			_cards.Remove(staleKey);
			_cardModels.Remove(staleKey);
		}

		_visibleCards.Clear();
		foreach ((string key, TableCardControl card) in nextVisibleCards)
		{
			_visibleCards[key] = card;
		}
		RefreshEncounterStrength(view);
		ApplySelectionAndTargets();
		ReapplyBeatEffects();
		PresentDrawMotion(draw, view.Viewer);
		PresentMotion(previousCards, view, previousView);
	}

	private void RefreshEncounterStrength(MatchViewModel view)
	{
		EncounterViewModel? encounter = view.Run?.Encounter is { Status: RunEncounterStatus.Initialized } current ? current : null;
		foreach ((string id, TableCardControl card) in _visibleCards)
			card.SetEncounterStrength(encounter?.Ice?.Id == id ? encounter.EffectiveStrength
				: encounter?.Breakers.FirstOrDefault(breaker => breaker.CardId == id)?.EffectiveStrength);
	}

	private void RenderTopZones(
		MatchViewModel view,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		bool runnerLocal = view.Viewer == PlayerSide.Runner;
		_corpHandCaption.Text = $"HQ · {view.CorpHand.Count} cards";
		_runnerHandCaption.Text = runnerLocal
			? $"Grip · {view.RunnerHand.Count} cards · local"
			: $"Grip · {view.RunnerHand.Count} cards · opponent backs";

		_runnerDeckPile.TooltipText = $"Stack · {view.RunnerDeck.Count} cards · draw for 1 click when available";
		TableCardControl stack = EnsureCard("runner-stack", _runnerDeckPile, null, false, nextVisibleCards, PlayerSide.Runner);
		stack.MouseFilter = MouseFilterEnum.Ignore;
		stack.FocusMode = FocusModeEnum.None;
		stack.Visible = view.RunnerDeck.Count > 0;
		_runnerDeckPile.GetNode<Label>("Count").Text = view.RunnerDeck.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
		_runnerDeckPile.Text = view.RunnerDeck.Count == 0 ? "Stack\nEmpty" : string.Empty;
		RenderPileFace("zone:runner-heap", (BoardActionButton)_runnerHeapPile,
			view.RunnerHeap.Count > 0 ? view.RunnerHeap[^1] : null, view.RunnerHeap.Count, "Heap", PlayerSide.Runner, nextVisibleCards);
		UpdatePileTooltip(_runnerHeapPile, $"Runner Heap · {view.RunnerHeap.Count} public cards",
			view.RunnerHeap.Count > 0 && view.RunnerHeap[^1].IsKnown);

		RenderScorePile("zone:corp-score", _corpScoredPile, view.CorpScore, PlayerSide.Corp, nextVisibleCards);
		RenderScorePile("zone:runner-score", _runnerScoredPile, view.RunnerScore, PlayerSide.Runner, nextVisibleCards);

		RenderIdentity("runner-identity", _runnerIdentitySlot, view.RunnerIdentity, false, nextVisibleCards);
		// The upper strip carries the opponent HQ count and the local hand is
		// rendered in the lower hand lane. The opponent Grip remains a count-only
		// projection, preserving hidden information at both seats.
	}

	private void RenderServers(
		MatchViewModel view,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		CacheServerDisplayNames(view.Servers);

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
			ApplyLaneIcon(lane.Header, server.Kind);
			lane.Header.TooltipText = $"Select {_serverDisplayNames[server.Id]} for contextual actions.";
			lane.RunMarker.Text = view.Run is { } run
				&& string.Equals(run.AttackedServerId, server.Id, StringComparison.Ordinal)
				? "▶ Run in progress"
				: string.Empty;
			// Keep this row in every lane so entering a run never moves its cards.
			lane.RunMarker.TooltipText = string.Empty;
			if (lane.RunMarker.Text.Length > 0 && view.Run?.Encounter is { } encounter)
			{
				lane.RunMarker.Text = $"▶ {encounter.Ice?.Title ?? "Face-down ICE"} · {encounter.RemainingSubroutines} unbroken";
				lane.RunMarker.TooltipText = $"Strength {encounter.EffectiveStrength ?? encounter.Ice?.Strength}\n"
					+ string.Join("\n", encounter.Subroutines.Select(sub => $"Subroutine {sub.Ordinal + 1}: {(sub.Broken ? "broken" : sub.Resolved ? "resolved" : "unbroken")}"));
			}
			// Servers are defined by their cards (U4): no root or ICE captions.
			lane.RootCaption.Visible = false;
			lane.IceCaption.Visible = false;

			ClearNonCardChildren(lane.RootCards);
			ApplyLaneLayout(lane);
			ClearNonCardChildren(lane.IceCards);
			RenderServerRoot(
				server,
				lane.RootCards,
				server.Kind == ServerKind.Headquarters ? view.CorpIdentity : null,
				nextVisibleCards);
			if (server.Kind == ServerKind.RAndD)
			{
				RenderZonePile("zone:corp-deck", lane.RootCards, null, view.CorpDeck.Count, nextVisibleCards);
			}
			if (server.Kind == ServerKind.Archives)
				RenderZonePile("zone:corp-archives", lane.RootCards, view.CorpDiscard.Count > 0 ? view.CorpDiscard[^1] : null, view.CorpDiscardCount, nextVisibleCards);
			RenderServerIce(server, lane.IceCards, nextVisibleCards);
		}

		int visibleIndex = 0;
		foreach (ServerViewModel server in ordered)
		{
			ServerLaneParts lane = _serverLanes[server.Id];
			if (lane.Panel.GetParent() != _serverLaneRow)
			{
				_serverLaneRow.AddChild(lane.Panel);
			}
			_serverLaneRow.MoveChild(lane.Panel, visibleIndex++);
		}

		foreach ((string id, ServerLaneParts lane) in _serverLanes)
		{
			if (currentServerIds.Contains(id))
			{
				continue;
			}

			lane.Panel.Visible = false;
		}
		ApplyServerSelection();
	}

	/// <summary>Clicks as pips: filled for those left, hollow for those spent this turn.</summary>
	private static string ClickPips(int clicks, int allotment) =>
		new string('●', Math.Max(0, clicks)) + new string('○', Math.Max(0, allotment - clicks));

	/// <summary>
	/// A face-down card keeps its tracked key for as long as it stays hidden,
	/// so installing new ICE in front of it never re-keys (and re-animates) it.
	/// </summary>
	private int HiddenAdvancement(IReadOnlyList<string?> keys, int index) =>
		index < keys.Count && keys[index] is { } key && key.StartsWith(CardKeyTracker.TrackedPrefix, StringComparison.Ordinal)
			? _pendingView?.AdvancementByKey.GetValueOrDefault(key) ?? 0 : 0;

	private static string TrackedSlot(IReadOnlyList<string?> keys, int index, string positional) =>
		index < keys.Count && keys[index] is { } key && key.StartsWith(CardKeyTracker.TrackedPrefix, StringComparison.Ordinal)
			? key : positional;

	private void RenderServerRoot(
		ServerViewModel server,
		HBoxContainer rootCards,
		CardViewModel? corpIdentity,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		int index = 0;
		if (corpIdentity is not null)
		{
			EnsureCard("corp-identity", rootCards, corpIdentity, false, nextVisibleCards);
		}
		foreach (CardViewModel? card in server.OrderedRoot)
		{
			string slot = $"server:{server.Id}:root:{index}";
			TableCardControl root = EnsureCard(TrackedSlot(server.RootKeys, index, slot), rootCards, card, isIce: false, nextVisibleCards);
			root.HiddenAdvancement = HiddenAdvancement(server.RootKeys, index);
			_slotAliases[slot] = root;
			index++;
		}
	}

	private void RenderServerIce(
		ServerViewModel server,
		VBoxContainer iceCards,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		// Match supplies outer-to-inner; the table puts inner ICE nearest the
		// root and the outermost ICE nearest the Runner (see OrderIceRows).
		List<Control> innerFirst = new();
		for (int displayIndex = server.OrderedIce.Count - 1; displayIndex >= 0; displayIndex--)
		{
			CardViewModel? card = server.OrderedIce[displayIndex];
			string slot = TrackedSlot(server.IceKeys, displayIndex, $"server:{server.Id}:ice:{displayIndex}");
			string? hostId = card?.Id ?? server.IcePositions.FirstOrDefault(position => position.Index == displayIndex)?.CardId;
			CardViewModel[] hosted = _pendingView?.RunnerRig.Where(child => hostId is not null && child.HostCardId == hostId).ToArray() ?? [];
			if (hosted.Length == 0)
			{
				TableCardControl ice = EnsureCard(slot, iceCards, card, isIce: true, nextVisibleCards);
				ice.HiddenAdvancement = HiddenAdvancement(server.IceKeys, displayIndex);
				_slotAliases[$"server:{server.Id}:ice:{displayIndex}"] = ice;
				innerFirst.Add(ice);
			}
			else
			{
				Control stack = new()
				{
					CustomMinimumSize = HostedStackMinimumSize(hosted.Length),
					SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
					MouseFilter = MouseFilterEnum.Ignore,
				};
				iceCards.AddChild(stack);
				innerFirst.Add(stack);
				TableCardControl host = EnsureCard(slot, stack, card, true, nextVisibleCards);
				_slotAliases[$"server:{server.Id}:ice:{displayIndex}"] = host;
				Vector2 hostOffset = new((stack.CustomMinimumSize.X - host.DisplaySize.X) * 0.5f, 0);
				host.Position = hostOffset;
				for (int index = 0; index < hosted.Length; index++)
				{
					TableCardControl attachment = EnsureCard(
						$"hosted:{hosted[index].Id}",
						stack,
						hosted[index],
						false,
						nextVisibleCards,
						PlayerSide.Runner,
						CardSizeRole.HostedPortrait);
					attachment.Position = hostOffset + HostedCardPosition(index);
					if (!attachment.UseHoverPreview)
						attachment.TooltipText = $"Hosted on {card?.Title ?? "face-down ICE"}\n" + attachment.TooltipText;
				}
			}
		}
		OrderIceRows(iceCards, innerFirst);
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
			Control slot = EnsureRigSlot(card.Id, RigArea(card));
			_activeRigSlots.Add(card.Id);
			ClearRigSlot(slot);
			RenderHostedSlot(slot, card, hosted.GetValueOrDefault(card.Id) ?? Array.Empty<CardViewModel>(), nextVisibleCards);
		}
		foreach (CardViewModel card in cards.Where(card => !string.IsNullOrEmpty(card.HostCardId) && !cards.Any(host => host.Id == card.HostCardId)
			&& !view.Servers.SelectMany(server => server.OrderedIce).Any(host => host?.Id == card.HostCardId)
			&& !view.Servers.SelectMany(server => server.IcePositions).Any(host => host.CardId == card.HostCardId)))
		{
			_activeRigSlots.Add($"orphan:{card.Id}");
			Control slot = EnsureRigSlot($"orphan:{card.Id}", RigArea(card));
			ClearRigSlot(slot);
			RenderHostedSlot(slot, card, Array.Empty<CardViewModel>(), nextVisibleCards);
		}
		foreach ((string key, Control slot) in _rigSlots)
		{
			slot.Visible = _activeRigSlots.Contains(key);
		}
		OrderRigAreas(cards);
	}

	/// <summary>The rig's areas (U4): programs, hardware and resources, each with its own cards and label.</summary>
	private static readonly string[] RigAreaNames = ["Programs", "Hardware", "Resources"];
	private readonly (PanelContainer Panel, HBoxContainer Cards, Label Caption)[] _rigAreas = new (PanelContainer, HBoxContainer, Label)[3];

	private static int RigArea(CardViewModel card) => card.Type switch
	{
		"Program" => 0,
		"Hardware" => 1,
		_ => 2,
	};

	private HBoxContainer EnsureRigArea(int index)
	{
		if (_rigAreas[index].Panel is { } existing && IsInstanceValid(existing)) return _rigAreas[index].Cards;
		// Even margins, so the cards stay centred like the identity beside them; the label sits below the cards.
		var panel = new PanelContainer { Name = $"Rig{RigAreaNames[index]}", MouseFilter = MouseFilterEnum.Ignore };
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.43f, 0.94f, 0.72f, 0.05f),
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
			ContentMarginLeft = 4,
			ContentMarginRight = 4,
			ContentMarginTop = 2,
			ContentMarginBottom = 2,
		});
		var column = new VBoxContainer { Name = "Column", MouseFilter = MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 2);
		var row = new HBoxContainer { Name = "Cards", MouseFilter = MouseFilterEnum.Ignore };
		var caption = new Label
		{
			Name = "Caption",
			Text = RigAreaNames[index],
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		caption.AddThemeFontSizeOverride("font_size", 10);
		caption.AddThemeColorOverride("font_color", new Color(0.62f, 0.76f, 0.75f, 0.8f));
		// An empty line as tall as the caption below the cards, so the cards sit in the middle of the
		// column and line up with the identity and the stack and heap, all centred in the row
		// (playtest, 30 September: the rig's cards sat at the row's top).
		var spacer = new Label { Name = "Spacer", Text = " ", MouseFilter = MouseFilterEnum.Ignore };
		spacer.AddThemeFontSizeOverride("font_size", 10);
		column.AddChild(spacer);
		column.AddChild(row);
		column.AddChild(caption);
		panel.AddChild(column);
		_runnerRigRow.AddChild(panel);
		_rigAreas[index] = (panel, row, caption);
		return row;
	}

	/// <summary>
	/// Keeps the rig's cards centred in the row, level with the stack and heap (playtest, 30 September):
	/// whatever sits under the cards (the caption, hosted cards hanging below their host, the scroll
	/// bar) is matched by the spacer above them.
	/// </summary>
	private void BalanceRigSpacers()
	{
		if (_runnerRigRow.GetParent() is not ScrollContainer scroll || !scroll.IsVisibleInTree()) return;
		float scrollBar = Mathf.Max(0, Mathf.Round(scroll.Size.Y - _runnerRigRow.Size.Y));
		float cardHeight = RigSlotMinimumSize().Y;
		foreach ((PanelContainer panel, HBoxContainer row, Label caption) in _rigAreas)
		{
			if (panel is null || !IsInstanceValid(panel) || panel.GetNodeOrNull<Label>("Column/Spacer") is not { } spacer) continue;
			float hosted = row.GetChildren().OfType<Control>().Where(child => child.Visible)
				.Select(child => child.CustomMinimumSize.Y - cardHeight).DefaultIfEmpty(0).Max();
			float wanted = Mathf.Max(0, hosted) + scrollBar + caption.GetMinimumSize().Y;
			if (!Mathf.IsEqualApprox(spacer.CustomMinimumSize.Y, wanted)) spacer.CustomMinimumSize = new Vector2(0, wanted);
		}
	}

	/// <summary>Keeps the areas in type order and each area's cards in install order; an empty area stays as a narrow label.</summary>
	private void OrderRigAreas(IReadOnlyList<CardViewModel> cards)
	{
		for (int area = 0; area < _rigAreas.Length; area++)
		{
			HBoxContainer row = EnsureRigArea(area);
			_runnerRigRow.MoveChild(_rigAreas[area].Panel, area);
			row.AddThemeConstantOverride("separation", _runnerRigRow.GetThemeConstant("separation"));
			int index = 0;
			foreach (CardViewModel card in cards.Where(card => string.IsNullOrEmpty(card.HostCardId) && RigArea(card) == area
				&& _rigSlots.TryGetValue(card.Id, out Control? slot) && slot.GetParent() == row))
				row.MoveChild(_rigSlots[card.Id], index++);
			bool empty = !row.GetChildren().OfType<Control>().Any(child => child.Visible);
			_rigAreas[area].Panel.CustomMinimumSize = empty ? new Vector2(76, 0) : Vector2.Zero;
			_rigAreas[area].Caption.Modulate = new Color(1, 1, 1, empty ? 0.55f : 1);

		}
	}

	private Control EnsureRigSlot(string key, int area)
	{
		HBoxContainer parent = EnsureRigArea(area);
		if (!_rigSlots.TryGetValue(key, out Control? slot))
		{
			slot = new Control
			{
				CustomMinimumSize = RigSlotMinimumSize(),
				MouseFilter = MouseFilterEnum.Ignore,
			};
			_rigSlots[key] = slot;
		}

		if (slot.GetParent() != parent)
		{
			if (slot.GetParent() is Node oldParent)
			{
				oldParent.RemoveChild(slot);
			}
			parent.AddChild(slot);
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
		TableCardControl hostControl = EnsureCard(
			$"rig:{host.Id}",
			slot,
			host,
			false,
			nextVisibleCards,
			PlayerSide.Runner);
		hostControl.Position = Vector2.Zero;
		Vector2 footprint = hostControl.DisplaySize;
		foreach ((CardViewModel attachment, int index) in attachments.Select((card, index) => (card, index)))
		{
			TableCardControl attachmentControl = EnsureCard(
				$"rig:{host.Id}:hosted:{attachment.Id}",
				slot,
				attachment,
				false,
				nextVisibleCards,
				PlayerSide.Runner,
				CardSizeRole.HostedPortrait);
			attachmentControl.Position = HostedCardPosition(index);
			attachmentControl.ZIndex = index + 2;
			Vector2 edge = attachmentControl.Position + attachmentControl.DisplaySize;
			footprint = new Vector2(Mathf.Max(footprint.X, edge.X), Mathf.Max(footprint.Y, edge.Y));
		}
		slot.CustomMinimumSize = footprint;
	}

	private void RenderLocalHand(
		MatchViewModel view,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		bool corpLocal = view.Viewer == PlayerSide.Corp;
		IReadOnlyList<CardViewModel> cards = corpLocal
			? view.CorpHand.VisibleCards
			: view.RunnerHand.VisibleCards;
		HashSet<string> selected = BoardInteractionProjector.SelectedDiscards(view).Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
		int handCount = corpLocal ? view.CorpHand.Count : view.RunnerHand.Count;
		cards = cards.Where(card => !selected.Contains(card.Id)).ToArray();
		string zone = corpLocal ? "corp-hand" : "runner-hand";
		// The hand needs no label; it says something only when a discard is being chosen.
		_localHandCaption.Text = string.Empty;
		HashSet<string> activeHandIds = cards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
		foreach (TableCardControl existing in _localHandRow.GetChildren().OfType<TableCardControl>().ToArray())
		{
			if (!activeHandIds.Contains(existing.CardId))
			{
				_localHandRow.RemoveChild(existing);
			}
		}
		var handControls = new List<TableCardControl>();
		foreach (CardViewModel card in cards)
		{
			TableCardControl control = EnsureCard(
				$"{zone}:{card.Id}",
				_localHandRow,
				card,
				false,
				nextVisibleCards,
				corpLocal ? PlayerSide.Corp : PlayerSide.Runner, CardSizeRole.FullPortrait);
			handControls.Add(control);
		}
		_localHandRow.SetCards(handControls, MotionEnabled);
		_localHandRow.GetNode<Label>("Count").Text = handCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
		if (selected.Count > 0) _localHandCaption.Text = $"{selected.Count} selected for discard";
	}

	private void RenderIdentity(
		string slot,
		Control parent,
		CardViewModel? card,
		bool isIce,
		Dictionary<string, TableCardControl> nextVisibleCards)
	{
		foreach (Control child in parent.GetChildren().OfType<Control>().ToArray())
		{
			parent.RemoveChild(child);
			if (child is not TableCardControl)
			{
				child.QueueFree();
			}
		}
		if (card is null)
		{
			Label label = MakeLabel("Identity\nconcealed", 10, MutedColor);
			label.HorizontalAlignment = HorizontalAlignment.Center;
			label.VerticalAlignment = VerticalAlignment.Center;
			parent.AddChild(label);
			return;
		}

		EnsureCard(slot, parent, card, isIce, nextVisibleCards);
	}

	private TableCardControl EnsureCard(
		string slot,
		Control parent,
		CardViewModel? model,
		bool isIce,
		Dictionary<string, TableCardControl> nextVisibleCards,
		PlayerSide? backSide = null,
		CardSizeRole sizeRole = CardSizeRole.CompactPortrait)
	{
		string identity = model is { Id.Length: > 0 }
			? $"card:{model.Id}"
			: $"slot:{slot}";
		// A revealed face-down card keeps its node, so it flips in place.
		if (model is { Id.Length: > 0 } && !_cards.ContainsKey(identity)
			&& _pendingView?.RevealedFrom.GetValueOrDefault(model.Id) is { } tracked
			&& _cards.Remove($"slot:{tracked}", out TableCardControl? revealed))
		{
			_cards[identity] = revealed;
			_cardModels.Remove($"slot:{tracked}");
			_cardModels[identity] = null;
		}
		if (!_cards.TryGetValue(identity, out TableCardControl? control))
		{
			control = TableCardControl.Create();
			AttachCard(control);
			_cards[identity] = control;
			_cardModels[identity] = null;
		}

		bool retainHandPose = parent is HandFanControl && control.GetParent() == parent;
		if (control.GetParent() is Node oldParent && oldParent != parent)
		{
			if (oldParent is BoardActionButton) control.SetContainerHover(false);
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
		control.SetBackSide(backSide ?? BackSideForSlot(slot));
		// The Corp sees its own installed cards face-up; say which aren't rezzed yet.
		control.SetUnrezzed(_pendingView?.Viewer == PlayerSide.Corp && model is { IsKnown: true, Rezzed: false }
			&& model.Type is not "Identity" && (parent.Name.ToString() is "RootCards" or "IceCards"
				|| parent.GetParent()?.Name.ToString() is "RootCards" or "IceCards"));
		if (!retainHandPose)
		{
			control.Scale = Vector2.One;
			control.Rotation = 0;
			control.PivotOffset = Vector2.Zero;
		}
		control.PointerHitTest = null;
		control.MouseFilter = MouseFilterEnum.Stop;
		control.FocusMode = FocusModeEnum.All;
		SetCardSizeIntent(identity, control, isIce, sizeRole);
		_activeCardKeys.Add(identity);
		string publicKey = model?.Id is { Length: > 0 } id ? id : slot;
		nextVisibleCards[publicKey] = control;
		return control;
	}

	private void AttachCard(TableCardControl card)
	{
		card.UseHoverPreview = UseCardHoverPreview;
		card.CardSelected += HandleCardSelected;
		card.CardInspected += HandleCardInspected;
		card.CardHoverChanged += HandleCardHoverChanged;
		card.AppearanceReplaced += HandleCardAppearanceReplaced;
	}

	private void HandleCardAppearanceReplaced(TableCardControl changed)
	{
		foreach ((string identity, TableCardControl card) in _cards)
		{
			if (!ReferenceEquals(card, changed)) continue;
			ConnectCardAppearance(identity, card);
			ApplyCardDisplaySize(identity);
			break;
		}
	}

	private void ConnectCardAppearance(string identity, TableCardControl card)
	{
		CardAppearance appearance = card.Appearance;
		if (_cardAppearanceHandlers.TryGetValue(identity, out (CardAppearance Appearance, Action Handler) existing))
		{
			if (ReferenceEquals(existing.Appearance, appearance))
			{
				return;
			}

			DetachCardAppearance(identity, clearIntent: false);
		}

		Action handler = () => ApplyCardDisplaySize(identity);
		appearance.Changed += handler;
		_cardAppearanceHandlers[identity] = (appearance, handler);
	}

	private void DetachCardAppearance(string identity, bool clearIntent = true)
	{
		if (!_cardAppearanceHandlers.TryGetValue(identity, out (CardAppearance Appearance, Action Handler) subscription))
		{
			if (clearIntent)
			{
				_cardSizeIntents.Remove(identity);
			}
			return;
		}

		if (GodotObject.IsInstanceValid(subscription.Appearance))
		{
			subscription.Appearance.Changed -= subscription.Handler;
		}
		_cardAppearanceHandlers.Remove(identity);
		if (clearIntent)
		{
			_cardSizeIntents.Remove(identity);
		}
	}

	private void SetCardSizeIntent(string identity, TableCardControl control, bool isIce, CardSizeRole role)
	{
		ConnectCardAppearance(identity, control);
		_cardSizeIntents[identity] = new CardSizeIntent(control, isIce, role);
		ApplyCardDisplaySize(identity);
	}

	private void ApplyCardDisplaySize(string identity)
	{
		if (!_cardSizeIntents.TryGetValue(identity, out CardSizeIntent? intent)
			|| intent is null
			|| !GodotObject.IsInstanceValid(intent.Control))
		{
			return;
		}

		Vector2 source = intent.IsIce ? intent.Control.Appearance.IceSize : intent.Control.Appearance.PortraitSize;
		float scale = intent.IsIce ? 1.0f : intent.Role switch
		{
			CardSizeRole.HostedPortrait => BoardLayout.HostedPortraitScale,
			CardSizeRole.FullPortrait => 1.0f,
			_ => BoardLayout.CompactPortraitScale,
		};
		intent.Control.SetDisplaySize(intent.IsIce ? source * new Vector2(1, BoardLayout.IceHeightScale) : source * scale);
		if (intent.Control.GetParent() is BoardActionButton pile) pile.CustomMinimumSize = intent.Control.DisplaySize;
	}

	private Vector2 HostedCardPosition(int index)
	{
		if (_boardLayout is null)
		{
			return Vector2.Zero;
		}

		return _boardLayout.HostedCardOffset + (_boardLayout.HostedCardStep * index);
	}

	private Vector2 HostedStackMinimumSize(int count)
	{
		BoardLayout layout = BoardLayout;
		CardAppearance? appearance = GD.Load<CardAppearance>(TableCardControl.DefaultAppearancePath);
		Vector2 ice = (appearance?.IceSize ?? TableCardControl.IceCardSize) * new Vector2(1, layout.IceHeightScale);
		Vector2 hosted = (appearance?.PortraitSize ?? TableCardControl.PortraitCardSize) * layout.HostedPortraitScale;
		Vector2 end = hosted + HostedCardPosition(count - 1);
		float sideOverflow = Mathf.Max(0, Mathf.Max(end.X - ice.X, -layout.HostedCardOffset.X));
		return new Vector2(ice.X + sideOverflow * 2, Mathf.Max(ice.Y, end.Y));
	}

	private Vector2 RigSlotMinimumSize()
	{
		BoardLayout layout = BoardLayout;
		CardAppearance? appearance = GD.Load<CardAppearance>(TableCardControl.DefaultAppearancePath);
		Vector2 card = (appearance?.PortraitSize ?? TableCardControl.PortraitCardSize) * layout.CompactPortraitScale;
		return card;
	}

	private static PlayerSide BackSideForSlot(string slot)
	{
		return slot.StartsWith("runner-", StringComparison.Ordinal)
			|| slot.StartsWith("rig:", StringComparison.Ordinal)
			|| slot.StartsWith("hosted:", StringComparison.Ordinal)
			? PlayerSide.Runner
			: PlayerSide.Corp;
	}

	private ServerLaneParts CreateServerLane(string serverId)
	{
		PackedScene? scene = GD.Load<PackedScene>("res://scenes/components/ServerLane.tscn");
		if (scene is null)
		{
			throw new InvalidOperationException("Production server lane scene is missing.");
		}

		PanelContainer panel = scene.Instantiate<PanelContainer>();
		Button header = panel.GetNode<Button>("Content/Header");
		Label runMarker = panel.GetNode<Label>("Content/RunMarker");
		Label rootCaption = panel.GetNode<Label>("Content/RootCaption");
		HBoxContainer rootCards = panel.GetNode<HBoxContainer>("Content/RootCards");
		Label iceCaption = panel.GetNode<Label>("Content/IceCaption");
		VBoxContainer iceCards = panel.GetNode<VBoxContainer>("Content/IceCards");
		header.Pressed += () => ServerSelected?.Invoke(serverId);
		panel.Draw += () => DrawLaneBrackets(panel);
		_serverLaneRow.AddChild(panel);
		ServerLaneParts lane = new(panel, header, runMarker, rootCaption, rootCards, iceCaption, iceCards);
		ApplyLaneLayout(lane);
		ApplyLaneOrientation(lane);
		return lane;
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
			$"corpdiscard:{view.CorpDiscardCount}:{CardsFingerprint(view.CorpDiscard)}",
			$"runnerheap:{view.RunnerHeap.Count}:{CardsFingerprint(view.RunnerHeap)}",
			$"selected-discard:{string.Join(',', BoardInteractionProjector.SelectedDiscards(view).Select(card => card.Id))}",
			$"corphand:{view.CorpHand.Count}:{CardsFingerprint(view.CorpHand.VisibleCards)}",
			$"resources:{view.Corp.Credits}:{view.Corp.Clicks}:{view.Runner.Credits}:{view.Runner.Clicks}",
			$"runnerhand:{view.RunnerHand.Count}:{CardsFingerprint(view.RunnerHand.VisibleCards)}",
			$"rigrig:{CardsFingerprint(view.RunnerRig)}",
			$"corpscore:{CardsFingerprint(view.CorpScore)}",
			$"runnerscore:{CardsFingerprint(view.RunnerScore)}",
			$"corpid:{CardFingerprint(view.CorpIdentity)}",
			$"runnerid:{CardFingerprint(view.RunnerIdentity)}",
			$"run:{RunFingerprint(view.Run)}",
			$"advancement:{string.Join(',', view.AdvancementByKey.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"))}",
		};
		foreach (ServerViewModel server in view.Servers)
		{
			parts.Add($"server:{server.Id}:{server.Kind}:{CardsFingerprint(server.OrderedIce)}:{CardsFingerprint(server.OrderedRoot)}"
				+ $":{string.Join(',', server.IceKeys)}:{string.Join(',', server.RootKeys)}");
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
			run.RunCredits,
			run.Encounter?.Id,
			run.Encounter?.Status,
			CardFingerprint(run.Encounter?.Ice),
			run.Encounter?.EffectiveStrength,
			run.Encounter is { } encounter
				? string.Join(";", encounter.Subroutines.Select(sub => $"{sub.Ordinal}:{sub.Broken}:{sub.Resolved}"))
				: string.Empty);
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
			card.HandCondition?.Summary ?? string.Empty,
			card.HandCondition?.InstallCost?.Summary ?? string.Empty,
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

	private void CacheServerDisplayNames(IReadOnlyList<ServerViewModel> servers)
	{
		foreach (ServerViewModel server in servers)
		{
			_serverKinds[server.Id] = server.Kind;
			if (server.Kind != ServerKind.Remote)
			{
				// Central names come from the server kind on every observation. This
				// prevents a prior fallback from surviving once the authoritative
				// server list is available.
				_serverDisplayNames[server.Id] = DisplayNameForKind(server.Kind, 0);
			}
			else if (!_serverDisplayNames.ContainsKey(server.Id))
			{
				_serverDisplayNames[server.Id] = AllocateServerDisplayName(server);
			}
		}
	}

	private string AllocateServerDisplayName(ServerViewModel server)
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
		ServerKind.Archives => 0,
		ServerKind.RAndD => 1,
		ServerKind.Headquarters => 2,
		_ => 3,
	};

	private static int RemoteNumber(string displayName)
	{
		int separator = displayName.LastIndexOf(' ');
		return separator >= 0 && int.TryParse(displayName[(separator + 1)..], out int value)
			? value
			: int.MaxValue;
	}

	private void UpdatePileTooltip(Button button, string tooltip, bool knownFace)
	{
		button.TooltipText = UseCardHoverPreview && knownFace
			? string.Empty : $"{tooltip}. Click to inspect the public pile summary.";
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
			bool actionable = _actionableSources.Contains(card.CardId)
				|| _actionableSources.Contains(key)
				|| _actionableSources.Contains(key.StartsWith("card:", StringComparison.Ordinal) ? key[5..] : key);
			card.SetTargetCandidate(candidate);
			card.SetActionableSource(actionable);
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

	private void HandleCardInspected(string cardId) => CardInspected?.Invoke(cardId);

	private void HandleCardHoverChanged(TableCardControl card, bool active)
	{
		_localHandRow.Hover(card, active);
		CardHoverChanged?.Invoke(card, active);
	}

	private static void ClearNonCardChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren().ToArray())
		{
			if (child is not TableCardControl and not BoardActionButton)
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
			&& left.HandCondition?.Summary == right.HandCondition?.Summary
			&& left.HandCondition?.InstallCost?.Summary == right.HandCondition?.InstallCost?.Summary
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
		card.CardHoverChanged -= HandleCardHoverChanged;
		card.AppearanceReplaced -= HandleCardAppearanceReplaced;
	}

	private sealed record ServerLaneParts(
		PanelContainer Panel,
		Button Header,
		Label RunMarker,
		Label RootCaption,
		HBoxContainer RootCards,
		Label IceCaption,
		VBoxContainer IceCards);

	private sealed record CardSizeIntent(TableCardControl Control, bool IsIce, CardSizeRole Role);
}
