using System.Globalization;
using System.Text;
using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Persistent, viewer-scoped action history for the M4.9 table.
///
/// The panel only accepts MatchHistoryEntry projections. It does not inspect
/// snapshots, engine events, or authoritative state, so hidden-card history
/// remains bounded by the projection that created each entry.
/// </summary>
[Tool]
public partial class MatchHistoryPanel : PanelContainer
{
	public const string PanelScenePath = "res://scenes/components/MatchHistoryPanel.tscn";
	public const string EntryScenePath = "res://scenes/components/HistoryEntry.tscn";
	public const string DetailScenePath = "res://scenes/components/HistoryDetail.tscn";
	public const string RailEntryScenePath = "res://scenes/components/HistoryRailEntry.tscn";

	private const int MaxEntriesPerPage = 80;
	private const int MaxRailMarkers = 24;
	private const float DefaultRailWidth = 60.0f;
	private const float DefaultExpandedWidth = 320.0f;
	public const int ExpandedZIndex = 40;

	private readonly List<MatchHistoryEntry> _entries = new();
	private readonly HashSet<string> _entryKeys = new(StringComparer.Ordinal);
	private readonly HashSet<string> _expandedKeys = new(StringComparer.Ordinal);
	private readonly List<StyleBox> _connectedStyles = new();
	private string _reportHeader = "MATCH HISTORY";
	private bool _ready;
	private bool _expanded;
	private bool _pinned;
	private bool _followNewestOnRefresh;
	private bool _pagePositionIntentional;
	private bool _samplePreview;
	private float _railWidth = DefaultRailWidth;
	private float _expandedWidth = DefaultExpandedWidth;
	private float _rowSpacing = 7.0f;
	private float _railRowSpacing = 4.0f;
	private int _headerFontSize = 15;
	private int _bodyFontSize = 12;
	private int _smallFontSize = 11;
	private Color _accentColor = new(0.36f, 0.9f, 0.74f);
	private Color _mutedColor = new(0.64f, 0.74f, 0.79f);
	private Color _corpCueColor = new(0.95f, 0.55f, 0.42f);
	private Color _runnerCueColor = new(0.4f, 0.75f, 1.0f);
	private StyleBox? _panelStyle;
	private StyleBox? _surfaceStyle;
	private StyleBox? _raisedStyle;
	private PackedScene? _entryScene;
	private PackedScene? _detailScene;
	private PackedScene? _railEntryScene;
	private int _pageStart;
	private int _newEntryCount;
	private readonly int _pageSize = MaxEntriesPerPage;
	private string? _focusedEntryKey;
	private bool _fixedSignalsBound;

	private VBoxContainer _shell = null!;
	private VBoxContainer _railRoot = null!;
	private VBoxContainer _railRows = null!;
	private Button _openButton = null!;
	private Label _railCount = null!;
	private VBoxContainer _expandedRoot = null!;
	private Label _headerLabel = null!;
	private Button _pinButton = null!;
	private Button _collapseButton = null!;
	private Button _copyButton = null!;
	private Button _newEntriesButton = null!;
	private Button _olderButton = null!;
	private Button _newerButton = null!;
	private Label _pageLabel = null!;
	private ScrollContainer _pageScroll = null!;
	private VBoxContainer _pageRows = null!;
	private Label _emptyRailLabel = null!;
	private Label _emptyPageLabel = null!;

	/// <summary>Raised when a known historical card is selected for inspection.</summary>
	public event Action<CardViewModel>? InspectCard;

	/// <summary>Raised when the panel changes between the rail and full history.</summary>
	public event Action<bool>? ExpandedChanged;

	/// <summary>Raised when the reader asks the parent layout to reserve the panel.</summary>
	public event Action<bool>? PinnedChanged;

	/// <summary>Raised when an Inspector edit changes the width used by the parent layout.</summary>
	public event Action? LayoutChanged;

	[ExportGroup("History layout")]
	[Export(PropertyHint.Range, "40,480,1")]
	public float RailWidth
	{
		get => _railWidth;
		set
		{
			float width = Mathf.Max(1.0f, value);
			if (Mathf.IsEqualApprox(_railWidth, width))
			{
				return;
			}

			_railWidth = width;
			ApplyLayout();
			LayoutChanged?.Invoke();
		}
	}

	[Export(PropertyHint.Range, "160,640,1")]
	public float ExpandedWidth
	{
		get => _expandedWidth;
		set
		{
			float width = Mathf.Max(1.0f, value);
			if (Mathf.IsEqualApprox(_expandedWidth, width))
			{
				return;
			}

			_expandedWidth = width;
			ApplyLayout();
			LayoutChanged?.Invoke();
		}
	}

	[Export(PropertyHint.Range, "0,32,1")]
	public float RowSpacing
	{
		get => _rowSpacing;
		set
		{
			_rowSpacing = Mathf.Clamp(value, 0.0f, 32.0f);
			ApplyAppearance();
		}
	}

	[Export(PropertyHint.Range, "0,24,1")]
	public float RailRowSpacing
	{
		get => _railRowSpacing;
		set
		{
			_railRowSpacing = Mathf.Clamp(value, 0.0f, 24.0f);
			ApplyAppearance();
		}
	}

	[ExportGroup("History typography")]
	[Export(PropertyHint.Range, "8,32,1")]
	public int HeaderFontSize
	{
		get => _headerFontSize;
		set
		{
			_headerFontSize = Math.Clamp(value, 8, 32);
			ApplyAppearance();
		}
	}

	[Export(PropertyHint.Range, "8,28,1")]
	public int BodyFontSize
	{
		get => _bodyFontSize;
		set
		{
			_bodyFontSize = Math.Clamp(value, 8, 28);
			ApplyAppearance();
		}
	}

	[Export(PropertyHint.Range, "7,24,1")]
	public int SmallFontSize
	{
		get => _smallFontSize;
		set
		{
			_smallFontSize = Math.Clamp(value, 7, 24);
			ApplyAppearance();
		}
	}

	[ExportGroup("History colours")]
	[Export]
	public Color AccentColor
	{
		get => _accentColor;
		set
		{
			_accentColor = value;
			ApplyAppearance();
		}
	}

	[Export]
	public Color MutedColor
	{
		get => _mutedColor;
		set
		{
			_mutedColor = value;
			ApplyAppearance();
		}
	}

	[Export]
	public Color CorpCueColor
	{
		get => _corpCueColor;
		set
		{
			_corpCueColor = value;
			ApplyAppearance();
		}
	}

	[Export]
	public Color RunnerCueColor
	{
		get => _runnerCueColor;
		set
		{
			_runnerCueColor = value;
			ApplyAppearance();
		}
	}

	[ExportGroup("History style boxes")]
	[Export]
	public StyleBox? PanelStyle
	{
		get => _panelStyle;
		set
		{
			_panelStyle = value;
			ReconnectStyleSignals();
			ApplyAppearance();
		}
	}

	[Export]
	public StyleBox? SurfaceStyle
	{
		get => _surfaceStyle;
		set
		{
			_surfaceStyle = value;
			ReconnectStyleSignals();
			ApplyAppearance();
		}
	}

	[Export]
	public StyleBox? RaisedStyle
	{
		get => _raisedStyle;
		set
		{
			_raisedStyle = value;
			ReconnectStyleSignals();
			ApplyAppearance();
		}
	}

	[ExportGroup("Scene templates")]
	[Export]
	public PackedScene? EntryScene
	{
		get => _entryScene ??= GD.Load<PackedScene>(EntryScenePath);
		set => _entryScene = value;
	}

	[Export]
	public PackedScene? DetailScene
	{
		get => _detailScene ??= GD.Load<PackedScene>(DetailScenePath);
		set => _detailScene = value;
	}

	[Export]
	public PackedScene? RailEntryScene
	{
		get => _railEntryScene ??= GD.Load<PackedScene>(RailEntryScenePath);
		set => _railEntryScene = value;
	}

	[ExportGroup("Editor preview")]
	[Export]
	public bool PreviewSamples
	{
		get => _samplePreview;
		set => _samplePreview = value;
	}

	/// <summary>Compatibility alias for preview hosts using the earlier name.</summary>
	public bool SamplePreview
	{
		get => PreviewSamples;
		set => PreviewSamples = value;
	}

	/// <summary>Whether the readable history panel is expanded beside the rail.</summary>
	public bool Expanded => _expanded;

	/// <summary>Whether the expanded history panel should remain reserved beside the table.</summary>
	public bool Pinned => _pinned;

	/// <summary>Metadata header included in exports and available from the panel title tooltip.</summary>
	public string ReportHeader
	{
		get => _reportHeader;
		set
		{
			_reportHeader = string.IsNullOrWhiteSpace(value) ? "MATCH HISTORY" : value.Trim();
			if (_headerLabel is not null && IsInstanceValid(_headerLabel))
			{
				_headerLabel.TooltipText = _reportHeader;
			}
		}
	}

	/// <summary>Number of accepted, deduplicated entries retained by this panel.</summary>
	public int EntryCount => _entries.Count;

	/// <summary>Alias used by native smoke checks to assert full-session retention.</summary>
	public int RetainedCount => _entries.Count;

	/// <summary>Number of entries accepted while the reader browsed an older page.</summary>
	public int NewEntryCount => _newEntryCount;

	/// <summary>First retained entry currently shown in the expanded page.</summary>
	public int PageStart => _pageStart;

	/// <summary>Maximum number of entry groups rendered in one expanded page.</summary>
	public int PageSize => _pageSize;

	/// <summary>Number of entry groups currently materialized in the expanded view.</summary>
	public int RenderedEntryCount { get; private set; }

	public override void _Ready()
	{
		EnsureAuthoredLayout();
		BindAuthoredNodes();
		ReconnectStyleSignals();
		_ready = true;
		if (Engine.IsEditorHint() && PreviewSamples)
		{
			_expanded = true;
		}
		ApplyAppearance();
		if (Engine.IsEditorHint() && PreviewSamples)
		{
			Append(CreatePreviewEntries());
		}
		RefreshPanel();
	}

	public override void _EnterTree()
	{
		if (_shell is null || !IsInstanceValid(_shell))
		{
			return;
		}

		// Godot calls _Ready once for a node lifetime. Reconnect the authored
		// controls and appearance resources when an already-built scene is
		// temporarily detached and re-entered.
		BindAuthoredNodes();
		ReconnectStyleSignals();
		_ready = true;
		Callable.From(() =>
		{
			if (IsInsideTree())
			{
				ApplyAppearance();
				RefreshPanel();
			}
		}).CallDeferred();
	}

	public override void _ExitTree()
	{
		_ready = false;
		DisconnectFixedSignals();
		DisconnectStyleSignals();
	}

	/// <summary>Changes the layout preference without changing history or page state.</summary>
	public void SetPinned(bool pinned)
	{
		if (_pinned == pinned)
		{
			return;
		}

		_pinned = pinned;
		if (_pinButton is not null && IsInstanceValid(_pinButton))
		{
			_pinButton.SetPressedNoSignal(pinned);
			_pinButton.TooltipText = pinned
				? "Keep expanded history reserved beside the table"
				: "Allow expanded history to overlay the table";
		}

		PinnedChanged?.Invoke(pinned);
	}

	/// <summary>Shows the oldest retained page and preserves that browsing position.</summary>
	public void JumpToOldest()
	{
		_pageStart = 0;
		_pagePositionIntentional = true;
		_followNewestOnRefresh = false;
		if (_ready && _expanded)
		{
			_pageScroll.ScrollVertical = 0;
		}
		RefreshPanel();
	}

	/// <summary>Shows the newest retained page and clears the unread-entry count.</summary>
	public void JumpToLatest()
	{
		_pageStart = LastPageStart();
		_pagePositionIntentional = false;
		_followNewestOnRefresh = true;
		_newEntryCount = 0;
		if (_ready && _expanded)
		{
			_pageScroll.ScrollVertical = 0;
		}
		RefreshPanel();
	}

	/// <summary>
	/// Appends accepted viewer-scoped entries exactly once by stable key.
	/// Reading an older page preserves that page and exposes a new-entry count;
	/// a reader already at the newest page follows the new entries.
	/// </summary>
	public void Append(IReadOnlyList<MatchHistoryEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		bool wasAtNewest = IsAtNewestPage();
		int accepted = 0;
		foreach (MatchHistoryEntry entry in entries)
		{
			if (entry is null || !_entryKeys.Add(entry.Key))
			{
				continue;
			}

			_entries.Add(entry);
			accepted++;
		}

		if (accepted == 0)
		{
			return;
		}

		if (wasAtNewest)
		{
			_pageStart = LastPageStart();
			_newEntryCount = 0;
			_pagePositionIntentional = false;
			_followNewestOnRefresh = true;
		}
		else
		{
			_newEntryCount += accepted;
			_followNewestOnRefresh = false;
		}

		RefreshPanel();
	}

	/// <summary>Removes all retained entries and resets the reader to the newest page.</summary>
	public void ClearHistory()
	{
		_entries.Clear();
		_entryKeys.Clear();
		_expandedKeys.Clear();
		_pageStart = 0;
		_newEntryCount = 0;
		_pagePositionIntentional = false;
		_followNewestOnRefresh = true;
		RefreshPanel();
	}

	/// <summary>Changes between the narrow marker rail and readable history panel.</summary>
	public void SetExpanded(bool expanded)
	{
		bool wasExpanded = _expanded;
		bool changed = _expanded != expanded;
		if (changed && wasExpanded && !expanded && !_pagePositionIntentional && !IsAtNewestPage())
		{
			_pagePositionIntentional = true;
			_followNewestOnRefresh = false;
		}

		_expanded = expanded;
		// The hand fan raises cards to ZIndex 30 on hover; an expanded panel
		// must stay above it (modals and motion ghosts use 70 and up).
		ZIndex = expanded ? ExpandedZIndex : 0;
		if (changed && expanded && !_pagePositionIntentional)
		{
			_pageStart = LastPageStart();
			_followNewestOnRefresh = true;
		}
		ApplyLayout();
		if (_ready)
		{
			RefreshPanel();
		}
		if (changed)
		{
			ExpandedChanged?.Invoke(expanded);
		}
	}

	/// <summary>Exports all retained public summaries, independent of the visible page.</summary>
	public string ExportText()
	{
		var report = new StringBuilder();
		report.AppendLine(ReportHeader);
		if (_entries.Count == 0)
		{
			return report.ToString().TrimEnd();
		}

		report.AppendLine();
		foreach (MatchHistoryEntry entry in _entries)
		{
			report.Append("Turn ")
				.Append(entry.TurnNumber)
				.Append(" · ")
				.Append(FormatSide(entry.Side))
				.Append(": ")
				.AppendLine(entry.Summary);

			foreach (string detail in entry.Details ?? Array.Empty<string>())
			{
				if (!string.IsNullOrWhiteSpace(detail))
				{
					report.Append("  • ").AppendLine(detail.Trim());
				}
			}

			if (entry.Cards is { Count: > 0 })
			{
				string[] titles = entry.Cards
					.Where(static card => card is not null && !string.IsNullOrWhiteSpace(card.Title))
					.Select(static card => card.Title)
					.ToArray();
				if (titles.Length > 0)
				{
					report.Append("  Cards: ").AppendLine(string.Join(", ", titles));
				}
			}
		}

		return report.ToString().TrimEnd();
	}

	private void EnsureAuthoredLayout()
	{
		if (GetNodeOrNull<Node>("Shell") is not null)
		{
			return;
		}

		PackedScene? scene = GD.Load<PackedScene>(PanelScenePath);
		if (scene is null)
		{
			return;
		}

		Node? template = scene.Instantiate();
		if (template is MatchHistoryPanel templatePanel)
		{
			_panelStyle ??= templatePanel.PanelStyle;
			_surfaceStyle ??= templatePanel.SurfaceStyle;
			_raisedStyle ??= templatePanel.RaisedStyle;
			_entryScene ??= templatePanel.EntryScene;
			_detailScene ??= templatePanel.DetailScene;
			_railEntryScene ??= templatePanel.RailEntryScene;
			foreach (Node child in template.GetChildren().ToArray())
			{
				template.RemoveChild(child);
				AddChild(child);
			}
		}
		else
		{
			template?.Free();
		}

		if (template is not null && IsInstanceValid(template))
		{
			template.Free();
		}
	}

	private void BindAuthoredNodes()
	{
		_shell = GetNode<VBoxContainer>("Shell");
		_railRoot = GetNode<VBoxContainer>("Shell/RailRoot");
		_railRows = GetNode<VBoxContainer>("Shell/RailRoot/RailScroll/RailRows");
		_openButton = GetNode<Button>("Shell/RailRoot/OpenButton");
		_railCount = GetNode<Label>("Shell/RailRoot/RailCount");
		_emptyRailLabel = GetNode<Label>("Shell/RailRoot/EmptyRailLabel");
		_expandedRoot = GetNode<VBoxContainer>("Shell/ExpandedRoot");
		_headerLabel = GetNode<Label>("Shell/ExpandedRoot/Heading/HeaderLabel");
		_pinButton = GetNode<Button>("Shell/ExpandedRoot/Heading/PinButton");
		_copyButton = GetNode<Button>("Shell/ExpandedRoot/Heading/CopyButton");
		_collapseButton = GetNode<Button>("Shell/ExpandedRoot/Heading/CollapseButton");
		_olderButton = GetNode<Button>("Shell/ExpandedRoot/Navigation/OlderButton");
		_newerButton = GetNode<Button>("Shell/ExpandedRoot/Navigation/NewerButton");
		_pageLabel = GetNode<Label>("Shell/ExpandedRoot/Navigation/PageLabel");
		_newEntriesButton = GetNode<Button>("Shell/ExpandedRoot/Navigation/NewEntriesButton");
		_pageScroll = GetNode<ScrollContainer>("Shell/ExpandedRoot/PageScroll");
		_pageRows = GetNode<VBoxContainer>("Shell/ExpandedRoot/PageScroll/PageRows");
		_emptyPageLabel = GetNode<Label>("Shell/ExpandedRoot/EmptyPageLabel");

		ConnectFixedSignals();
	}

	private void ConnectFixedSignals()
	{
		if (_fixedSignalsBound)
		{
			return;
		}

		_openButton.Pressed += OpenExpanded;
		_pinButton.Toggled += SetPinned;
		_copyButton.Pressed += CopyReport;
		_collapseButton.Pressed += CloseExpanded;
		_olderButton.Pressed += ShowOlderPage;
		_newerButton.Pressed += ShowNewerPage;
		_newEntriesButton.Pressed += FollowNewest;
		_fixedSignalsBound = true;
	}

	private void DisconnectFixedSignals()
	{
		if (!_fixedSignalsBound)
		{
			return;
		}

		if (_openButton is not null && IsInstanceValid(_openButton)) _openButton.Pressed -= OpenExpanded;
		if (_pinButton is not null && IsInstanceValid(_pinButton)) _pinButton.Toggled -= SetPinned;
		if (_copyButton is not null && IsInstanceValid(_copyButton)) _copyButton.Pressed -= CopyReport;
		if (_collapseButton is not null && IsInstanceValid(_collapseButton)) _collapseButton.Pressed -= CloseExpanded;
		if (_olderButton is not null && IsInstanceValid(_olderButton)) _olderButton.Pressed -= ShowOlderPage;
		if (_newerButton is not null && IsInstanceValid(_newerButton)) _newerButton.Pressed -= ShowNewerPage;
		if (_newEntriesButton is not null && IsInstanceValid(_newEntriesButton)) _newEntriesButton.Pressed -= FollowNewest;
		_fixedSignalsBound = false;
	}

	private void OpenExpanded() => SetExpanded(true);

	private void CloseExpanded() => SetExpanded(false);

	private void ApplyLayout()
	{
		if (!IsInsideTree() || _shell is null || !IsInstanceValid(_shell))
		{
			return;
		}

		float width = _expanded ? ExpandedWidth : RailWidth;
		CustomMinimumSize = new Vector2(width, 0);
		_shell.CustomMinimumSize = new Vector2(Mathf.Max(0, width - PanelHorizontalMargins()), 0);
	}

	private void ApplyAppearance()
	{
		if (!_ready || _shell is null || !IsInstanceValid(_shell))
		{
			return;
		}

		float width = _expanded ? ExpandedWidth : RailWidth;
		CustomMinimumSize = new Vector2(width, 0);
		_shell.CustomMinimumSize = new Vector2(Mathf.Max(0, width - PanelHorizontalMargins()), 0);
		_shell.AddThemeConstantOverride("separation", Mathf.RoundToInt(RowSpacing));
		_railRoot.AddThemeConstantOverride("separation", Mathf.RoundToInt(RailRowSpacing + 2));
		_railRows.AddThemeConstantOverride("separation", Mathf.RoundToInt(RailRowSpacing));
		_pageRows.AddThemeConstantOverride("separation", Mathf.RoundToInt(RowSpacing));
		_headerLabel.AddThemeFontSizeOverride("font_size", HeaderFontSize);
		_headerLabel.AddThemeColorOverride("font_color", AccentColor);
		_headerLabel.TooltipText = ReportHeader;
		_railCount.AddThemeFontSizeOverride("font_size", SmallFontSize);
		_railCount.AddThemeColorOverride("font_color", MutedColor);
		_emptyRailLabel.AddThemeFontSizeOverride("font_size", HeaderFontSize);
		_emptyRailLabel.AddThemeColorOverride("font_color", MutedColor);
		_pageLabel.AddThemeFontSizeOverride("font_size", SmallFontSize);
		_pageLabel.AddThemeColorOverride("font_color", MutedColor);
		_openButton.AddThemeFontSizeOverride("font_size", BodyFontSize + 1);
		_openButton.AddThemeColorOverride("font_hover_color", AccentColor);
		_copyButton.AddThemeFontSizeOverride("font_size", SmallFontSize);
		_collapseButton.AddThemeFontSizeOverride("font_size", SmallFontSize);
		_pinButton.AddThemeFontSizeOverride("font_size", SmallFontSize);
		_olderButton.AddThemeFontSizeOverride("font_size", SmallFontSize);
		_newerButton.AddThemeFontSizeOverride("font_size", SmallFontSize);
		_newEntriesButton.AddThemeFontSizeOverride("font_size", SmallFontSize);
		foreach (Node child in _pageRows.GetChildren())
		{
			if (child is HistoryEntry entry)
			{
				entry.ApplyAppearance(
					RowSpacing,
					HeaderFontSize,
					BodyFontSize,
					SmallFontSize,
					AccentColor,
					MutedColor,
					CorpCueColor,
					RunnerCueColor,
					SurfaceStyle,
					RaisedStyle);
			}
		}
		foreach (Node child in _railRows.GetChildren())
		{
			if (child is HistoryRailEntry marker)
			{
				marker.ApplyAppearance(SmallFontSize, AccentColor, MutedColor, CorpCueColor, RunnerCueColor);
			}
		}
		if (PanelStyle is not null)
		{
			AddThemeStyleboxOverride("panel", PanelStyle);
		}
		else
		{
			RemoveThemeStyleboxOverride("panel");
		}
	}

	private void ReconnectStyleSignals()
	{
		DisconnectStyleSignals();
		if (!IsInsideTree())
		{
			return;
		}

		foreach (StyleBox style in new[] { PanelStyle, SurfaceStyle, RaisedStyle }.OfType<StyleBox>())
		{
			style.Changed += HandleStyleChanged;
			_connectedStyles.Add(style);
		}
	}

	private void DisconnectStyleSignals()
	{
		foreach (StyleBox style in _connectedStyles)
		{
			if (GodotObject.IsInstanceValid(style))
			{
				style.Changed -= HandleStyleChanged;
			}
		}
		_connectedStyles.Clear();
	}

	private void HandleStyleChanged()
	{
		ApplyAppearance();
	}

	private float PanelHorizontalMargins()
		=> PanelStyle is StyleBoxFlat flat
			? Math.Max(0.0f, flat.ContentMarginLeft + flat.ContentMarginRight)
			: 0.0f;

	private void RefreshPanel()
	{
		if (!_ready)
		{
			return;
		}

		_railRoot.Visible = !_expanded;
		_expandedRoot.Visible = _expanded;
		ApplyLayout();
		RefreshRail();
		if (_expanded)
		{
			RefreshExpandedPage();
		}
	}

	private void RefreshRail()
	{
		ClearContainer(_railRows);
		_railCount.Text = _newEntryCount > 0
			? $"{_entries.Count}\n+{_newEntryCount}"
			: _entries.Count == 0
				? "0"
				: _entries.Count.ToString(CultureInfo.InvariantCulture);
		_emptyRailLabel.Visible = _entries.Count == 0;
		if (_entries.Count == 0)
		{
			return;
		}

		HistoryRailEntry? template = RailEntryScene?.Instantiate<HistoryRailEntry>();
		if (template is null)
		{
			return;
		}

		int first = Math.Max(0, _entries.Count - MaxRailMarkers);
		for (int index = _entries.Count - 1; index >= first; index--)
		{
			HistoryRailEntry marker = index == _entries.Count - 1
				? template
				: RailEntryScene!.Instantiate<HistoryRailEntry>();
			MatchHistoryEntry entry = _entries[index];
			_railRows.AddChild(marker);
			marker.Bind(
				entry,
				OpenEntry,
				SmallFontSize,
				AccentColor,
				MutedColor,
				CorpCueColor,
				RunnerCueColor);
		}
	}

	private void RefreshExpandedPage()
	{
		int previousScroll = _pageScroll.ScrollVertical;
		bool followNewest = _followNewestOnRefresh;
		_pageStart = Math.Clamp(_pageStart, 0, LastPageStart());
		int pageEnd = Math.Min(_entries.Count, _pageStart + MaxEntriesPerPage);
		_pageLabel.Text = _entries.Count == 0
			? "0 entries"
			: $"{_pageStart + 1}–{pageEnd} of {_entries.Count}";
		_olderButton.Disabled = _pageStart == 0;
		_newerButton.Disabled = _entries.Count == 0 || _pageStart >= LastPageStart();
		_newEntriesButton.Visible = _newEntryCount > 0;
		_newEntriesButton.Text = $"{_newEntryCount} new";
		_focusedEntryKey ??= FindFocusedEntryKey();
		ClearContainer(_pageRows);
		_emptyPageLabel.Visible = _entries.Count == 0;
		RenderedEntryCount = 0;

		if (_entries.Count == 0)
		{
			return;
		}

		HistoryEntry? template = EntryScene?.Instantiate<HistoryEntry>();
		if (template is null)
		{
			return;
		}

		// Newest first: a page's rows run from its newest entry down, and a turn's
		// header sits above that turn's newest entry.
		for (int index = pageEnd - 1; index >= _pageStart; index--)
		{
			HistoryEntry row = index == pageEnd - 1
				? template
				: EntryScene!.Instantiate<HistoryEntry>();
			MatchHistoryEntry entry = _entries[index];
			_pageRows.AddChild(row);
			row.Bind(
				entry,
				index == _entries.Count - 1 || entry.TurnNumber != _entries[index + 1].TurnNumber,
				_expandedKeys.Contains(entry.Key),
				ToggleEntry,
				NotifyInspectCard,
				RowSpacing,
				HeaderFontSize,
				BodyFontSize,
				SmallFontSize,
				AccentColor,
				MutedColor,
				CorpCueColor,
				RunnerCueColor,
				SurfaceStyle,
				RaisedStyle,
				DetailScene);
			RenderedEntryCount++;
		}

		string? restoreFocusKey = _focusedEntryKey;
		_focusedEntryKey = null;
		int preservedScroll = Math.Max(0, previousScroll);
		ScrollContainer pageScroll = _pageScroll;
		Callable.From(() =>
		{
			if (!IsInstanceValid(pageScroll))
			{
				return;
			}

			pageScroll.ScrollVertical = followNewest ? 0 : preservedScroll;
			if (!string.IsNullOrWhiteSpace(restoreFocusKey))
			{
				foreach (Node child in _pageRows.GetChildren())
				{
					if (child is HistoryEntry row && row.EntryKey == restoreFocusKey)
					{
						row.GrabSummaryFocus();
						break;
					}
				}
			}
		}).CallDeferred();
		_followNewestOnRefresh = false;
	}

	private void ToggleEntry(string key)
	{
		_focusedEntryKey = key;
		_pagePositionIntentional = true;
		_followNewestOnRefresh = false;
		if (!_expandedKeys.Add(key))
		{
			_expandedKeys.Remove(key);
		}
		RefreshPanel();
	}

	private void NotifyInspectCard(CardViewModel card)
	{
		InspectCard?.Invoke(card);
	}

	private void OpenEntry(string key)
	{
		int index = _entries.FindIndex(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
		if (index < 0)
		{
			return;
		}

		int page = index / MaxEntriesPerPage;
		_pageStart = Math.Clamp(page * MaxEntriesPerPage, 0, LastPageStart());
		_pagePositionIntentional = true;
		_followNewestOnRefresh = false;
		_expandedKeys.Add(key);
		_focusedEntryKey = key;
		SetExpanded(true);
	}

	private void ShowOlderPage()
	{
		_pagePositionIntentional = true;
		_followNewestOnRefresh = false;
		_pageStart = Math.Max(0, _pageStart - MaxEntriesPerPage);
		_pageScroll.ScrollVertical = 0;
		RefreshPanel();
	}

	private void ShowNewerPage()
	{
		_pagePositionIntentional = true;
		_followNewestOnRefresh = false;
		_pageStart = Math.Min(LastPageStart(), _pageStart + MaxEntriesPerPage);
		if (_pageStart >= LastPageStart())
		{
			_newEntryCount = 0;
		}
		_pageScroll.ScrollVertical = 0;
		RefreshPanel();
	}

	private void FollowNewest()
	{
		_pagePositionIntentional = false;
		_followNewestOnRefresh = true;
		_pageStart = LastPageStart();
		_newEntryCount = 0;
		_pageScroll.ScrollVertical = 0;
		RefreshPanel();
	}

	private void CopyReport()
	{
		DisplayServer.ClipboardSet(ExportText());
		_copyButton.Text = "Copied";
	}

	private bool IsAtNewestPage()
	{
		if (_entries.Count == 0)
		{
			return true;
		}
		if (_pageStart < LastPageStart())
		{
			return false;
		}

		if (!_ready || !_expanded)
		{
			return !_pagePositionIntentional;
		}

		// The newest entry is at the top of the page.
		return _pageScroll.ScrollVertical <= 1;
	}

	private int LastPageStart()
		=> Math.Max(0, _entries.Count - MaxEntriesPerPage);

	private string? FindFocusedEntryKey()
	{
		if (!_ready || !_expanded)
		{
			return null;
		}

		Control? owner = GetViewport().GuiGetFocusOwner();
		while (owner is not null && IsInstanceValid(owner))
		{
			if (owner is HistoryEntry entry)
			{
				return entry.EntryKey;
			}

			owner = owner.GetParent() as Control;
		}

		return null;
	}

	private static void ClearContainer(Container container)
	{
		foreach (Node child in container.GetChildren().ToArray())
		{
			container.RemoveChild(child);
			// Deferred: a row's own button may still be emitting the click that rebuilt the list.
			child.QueueFree();
		}
	}

	private static string FormatSide(PlayerSide side)
		=> side == PlayerSide.Corp ? "CORP" : "RUNNER";

	private static IReadOnlyList<MatchHistoryEntry> CreatePreviewEntries()
	{
		CardViewModel sampleCard = new(
			"history-preview-card",
			null,
			"Synthetic Asset",
			"Asset",
			true,
			false,
			2,
			2,
			null,
			null,
			null,
			["Connection", "Virtual"],
			"Synthetic viewer-safe card content for the editor preview.",
			"Shaper");
		return
		[
			new MatchHistoryEntry(
				"history-preview-1",
				1,
				1,
				PlayerSide.Runner,
				"Runner runs Remote 1.",
				["Runner approaches a synthetic piece of ICE.", "A public effect remains visible while the reader browses."])
			{
				Cards = [sampleCard],
			},
			new MatchHistoryEntry(
				"history-preview-2",
				2,
				1,
				PlayerSide.Corp,
				"Corp gains a credit.",
				["The preview contains no coordinator or authoritative state."]),
			new MatchHistoryEntry(
				"history-preview-3",
				3,
				2,
				PlayerSide.Runner,
				"Runner continues after access.",
				["Synthetic history demonstrates pagination and keyboard focus."]),
		];
	}

	internal static string FormatEntryTooltip(MatchHistoryEntry entry)
		=> $"Turn {entry.TurnNumber} · {FormatSide(entry.Side)}\n{entry.Summary}";

	internal static Color SideColour(PlayerSide side, Color corpCue, Color runnerCue)
		=> side == PlayerSide.Corp ? corpCue : runnerCue;
}
