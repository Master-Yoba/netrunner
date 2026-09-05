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
public partial class MatchHistoryPanel : PanelContainer
{
	private const int RailWidth = 60;
	private const int ExpandedWidth = 320;
	private const int MaxEntriesPerPage = 80;
	private const int MaxRailMarkers = 24;
	private const string PanelTitle = "Match history";

	private static readonly Color Background = new(0.035f, 0.055f, 0.07f);
	private static readonly Color Surface = new(0.055f, 0.085f, 0.105f);
	private static readonly Color SurfaceRaised = new(0.075f, 0.11f, 0.13f);
	private static readonly Color Border = new(0.18f, 0.31f, 0.36f);
	private static readonly Color Accent = new(0.36f, 0.9f, 0.74f);
	private static readonly Color Muted = new(0.64f, 0.74f, 0.79f);
	private static readonly Color CorpCue = new(0.95f, 0.55f, 0.42f);
	private static readonly Color RunnerCue = new(0.4f, 0.75f, 1.0f);

	private readonly List<MatchHistoryEntry> _entries = new();
	private readonly HashSet<string> _entryKeys = new(StringComparer.Ordinal);
	private readonly HashSet<string> _expandedKeys = new(StringComparer.Ordinal);
	private string _reportHeader = "MATCH HISTORY";
	private bool _ready;
	private bool _expanded;
	private bool _followNewestOnRefresh;
	private bool _pagePositionIntentional;
	private int _pageStart;
	private int _newEntryCount;
	private readonly int _pageSize = MaxEntriesPerPage;

	private VBoxContainer _shell = null!;
	private VBoxContainer _railRoot = null!;
	private VBoxContainer _railRows = null!;
	private ScrollContainer _railScroll = null!;
	private Button _openButton = null!;
	private Label _railCount = null!;
	private VBoxContainer _expandedRoot = null!;
	private Label _headerLabel = null!;
	private Button _collapseButton = null!;
	private Button _copyButton = null!;
	private Button _newEntriesButton = null!;
	private Button _olderButton = null!;
	private Button _newerButton = null!;
	private Label _pageLabel = null!;
	private ScrollContainer _pageScroll = null!;
	private VBoxContainer _pageRows = null!;

	/// <summary>Raised when a known historical card is selected for inspection.</summary>
	public event Action<CardViewModel>? InspectCard;

	/// <summary>Raised when the panel changes between the rail and full history.</summary>
	public event Action<bool>? ExpandedChanged;

	/// <summary>Whether the readable history panel is expanded beside the rail.</summary>
	public bool Expanded => _expanded;

	/// <summary>Metadata header included in exports and available from the panel title tooltip.</summary>
	public string ReportHeader
	{
		get => _reportHeader;
		set
		{
			_reportHeader = string.IsNullOrWhiteSpace(value) ? "MATCH HISTORY" : value.Trim();
			if (_headerLabel is not null)
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
		BuildPanel();
		_ready = true;
		RefreshPanel();
	}

	public override void _ExitTree()
	{
		_ready = false;
		InspectCard = null;
		ExpandedChanged = null;
		FreeDetachedBranch(_railRoot);
		FreeDetachedBranch(_expandedRoot);
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
		if (changed && expanded && !_pagePositionIntentional)
		{
			_pageStart = LastPageStart();
			_followNewestOnRefresh = true;
		}
		CustomMinimumSize = new Vector2(expanded ? ExpandedWidth : RailWidth, 0);
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

	private void BuildPanel()
	{
		MouseFilter = MouseFilterEnum.Stop;
		CustomMinimumSize = new Vector2(_expanded ? ExpandedWidth : RailWidth, 0);
		SizeFlagsVertical = SizeFlags.ExpandFill;
		AddThemeStyleboxOverride("panel", CreateStyle(Background, Border, 1, 8));

		_shell = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_shell.AddThemeConstantOverride("separation", 8);
		AddChild(_shell);

		BuildRail();
		BuildExpandedPanel();
	}

	private void BuildRail()
	{
		_railRoot = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_railRoot.AddThemeConstantOverride("separation", 6);
		_shell.AddChild(_railRoot);

		Label title = MakeLabel("H\nI\nS\nT", 10, Accent);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		_railRoot.AddChild(title);

		_openButton = MakeButton("Open history", 13);
		_openButton.Text = "H";
		_openButton.TooltipText = "Open readable match history";
		_openButton.Pressed += () => SetExpanded(true);
		_railRoot.AddChild(_openButton);

		_railCount = MakeLabel("0", 11, Muted);
		_railCount.HorizontalAlignment = HorizontalAlignment.Center;
		_railRoot.AddChild(_railCount);

		_railScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			ClipContents = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_railRows = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_railRows.AddThemeConstantOverride("separation", 4);
		_railScroll.AddChild(_railRows);
		_railRoot.AddChild(_railScroll);
	}

	private void BuildExpandedPanel()
	{
		_expandedRoot = new VBoxContainer
		{
			Visible = false,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_expandedRoot.AddThemeConstantOverride("separation", 7);
		_shell.AddChild(_expandedRoot);

		HBoxContainer heading = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		heading.AddThemeConstantOverride("separation", 5);
		_headerLabel = MakeLabel(PanelTitle, 15, Accent);
		_headerLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_headerLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		_headerLabel.TooltipText = ReportHeader;
		heading.AddChild(_headerLabel);
		_copyButton = MakeButton("Copy", 11);
		_copyButton.CustomMinimumSize = new Vector2(58, 30);
		_copyButton.TooltipText = "Copy the complete public history report";
		_copyButton.Pressed += CopyReport;
		heading.AddChild(_copyButton);
		_collapseButton = MakeButton("Close", 11);
		_collapseButton.CustomMinimumSize = new Vector2(58, 30);
		_collapseButton.TooltipText = "Collapse to the history rail";
		_collapseButton.Pressed += () => SetExpanded(false);
		heading.AddChild(_collapseButton);
		_expandedRoot.AddChild(heading);

		HBoxContainer navigation = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		navigation.AddThemeConstantOverride("separation", 5);
		_olderButton = MakeButton("Older", 11);
		_olderButton.TooltipText = "Read older history entries";
		_olderButton.Pressed += ShowOlderPage;
		navigation.AddChild(_olderButton);
		_newerButton = MakeButton("Newer", 11);
		_newerButton.TooltipText = "Read newer history entries";
		_newerButton.Pressed += ShowNewerPage;
		navigation.AddChild(_newerButton);
		_pageLabel = MakeLabel("0 entries", 11, Muted);
		_pageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		navigation.AddChild(_pageLabel);
		_newEntriesButton = MakeButton("", 11);
		_newEntriesButton.TooltipText = "Follow the newest history entries";
		_newEntriesButton.Pressed += FollowNewest;
		_newEntriesButton.Visible = false;
		navigation.AddChild(_newEntriesButton);
		_expandedRoot.AddChild(navigation);

		_pageScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
			ClipContents = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		_pageRows = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_pageRows.AddThemeConstantOverride("separation", 7);
		_pageScroll.AddChild(_pageRows);
		_expandedRoot.AddChild(_pageScroll);
	}

	private void RefreshPanel()
	{
		if (!_ready)
		{
			return;
		}

		if (_expanded)
		{
			if (_railRoot.GetParent() == _shell)
			{
				_shell.RemoveChild(_railRoot);
			}
			if (_expandedRoot.GetParent() != _shell)
			{
				_shell.AddChild(_expandedRoot);
			}
			_expandedRoot.Visible = true;
		}
		else
		{
			if (_expandedRoot.GetParent() == _shell)
			{
				_shell.RemoveChild(_expandedRoot);
			}
			if (_railRoot.GetParent() != _shell)
			{
				_shell.AddChild(_railRoot);
			}
			_railRoot.Visible = true;
		}
		CustomMinimumSize = new Vector2(_expanded ? ExpandedWidth : RailWidth, 0);
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
		if (_entries.Count == 0)
		{
			Label empty = MakeLabel("·", 16, Muted);
			empty.HorizontalAlignment = HorizontalAlignment.Center;
			_railRows.AddChild(empty);
			return;
		}

		int first = Math.Max(0, _entries.Count - MaxRailMarkers);
		for (int index = first; index < _entries.Count; index++)
		{
			MatchHistoryEntry entry = _entries[index];
			Button marker = MakeButton(FormatSide(entry.Side)[..1], 11);
			marker.TooltipText = FormatEntryTooltip(entry);
			marker.AddThemeColorOverride("font_color", SideColour(entry.Side));
			string key = entry.Key;
			marker.Pressed += () => OpenEntry(key);
			_railRows.AddChild(marker);
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
		ClearContainer(_pageRows);
		RenderedEntryCount = 0;

		if (_entries.Count == 0)
		{
			_pageRows.AddChild(MakeLabel("No public actions yet.", 13, Muted));
			return;
		}

		for (int index = _pageStart; index < pageEnd; index++)
		{
			AddEntryGroup(index, _entries[index]);
			RenderedEntryCount++;
		}

		int preservedScroll = Math.Max(0, previousScroll);
		ScrollContainer pageScroll = _pageScroll;
		Callable.From(() =>
		{
			if (IsInstanceValid(pageScroll))
			{
				pageScroll.ScrollVertical = followNewest
					? (int)Math.Max(0, pageScroll.GetVScrollBar().MaxValue)
					: preservedScroll;
			}
		}).CallDeferred();
		_followNewestOnRefresh = false;
	}

	private void AddEntryGroup(int index, MatchHistoryEntry entry)
	{
		VBoxContainer group = new();
		group.AddThemeConstantOverride("separation", 4);

		if (index == 0 || entry.TurnNumber != _entries[index - 1].TurnNumber)
		{
			Label turn = MakeLabel($"TURN {entry.TurnNumber}", 10, Muted);
			turn.AddThemeColorOverride("font_color", Accent);
			group.AddChild(turn);
		}

		PanelContainer row = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		row.AddThemeStyleboxOverride("panel", CreateStyle(Surface, Border, 1, 5));
		HBoxContainer rowContent = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		rowContent.AddThemeConstantOverride("separation", 6);
		Label side = MakeLabel(FormatSide(entry.Side), 10, SideColour(entry.Side));
		side.CustomMinimumSize = new Vector2(48, 0);
		side.VerticalAlignment = VerticalAlignment.Center;
		rowContent.AddChild(side);

		bool detailsExpanded = _expandedKeys.Contains(entry.Key);
		Button summary = MakeButton((detailsExpanded ? "▾ " : "▸ ") + entry.Summary, 12);
		summary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		summary.Alignment = HorizontalAlignment.Left;
		summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		summary.CustomMinimumSize = new Vector2(0, 42);
		summary.TooltipText = FormatEntryTooltip(entry);
		string key = entry.Key;
		summary.Pressed += () => ToggleEntry(key);
		rowContent.AddChild(summary);
		row.AddChild(rowContent);
		group.AddChild(row);

		if (detailsExpanded)
		{
			group.AddChild(BuildDetails(entry));
		}

		_pageRows.AddChild(group);
	}

	private PanelContainer BuildDetails(MatchHistoryEntry entry)
	{
		PanelContainer panel = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		panel.AddThemeStyleboxOverride("panel", CreateStyle(SurfaceRaised, Border, 1, 5));
		VBoxContainer details = new()
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		details.AddThemeConstantOverride("separation", 4);
		panel.AddChild(details);

		string[] publicDetails = (entry.Details ?? Array.Empty<string>())
			.Where(static detail => !string.IsNullOrWhiteSpace(detail))
			.Select(static detail => detail.Trim())
			.ToArray();
		if (publicDetails.Length == 0)
		{
			details.AddChild(MakeLabel("No additional public details.", 11, Muted));
		}
		else
		{
			foreach (string detail in publicDetails)
			{
				Label line = MakeLabel("• " + detail, 11, Colors.White);
				line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				details.AddChild(line);
			}
		}

		if (entry.Cards is { Count: > 0 })
		{
			Label cardsHeading = MakeLabel("KNOWN CARDS", 10, Muted);
			details.AddChild(cardsHeading);
			foreach (CardViewModel card in entry.Cards.Where(static card => card is not null))
			{
				Button cardButton = MakeButton($"Inspect · {card.Title}", 11);
				cardButton.TooltipText = "Inspect this known historical card";
				cardButton.Pressed += () => InspectCard?.Invoke(card);
				details.AddChild(cardButton);
			}
		}

		return panel;
	}

	private void ToggleEntry(string key)
	{
		_pagePositionIntentional = true;
		_followNewestOnRefresh = false;
		if (!_expandedKeys.Add(key))
		{
			_expandedKeys.Remove(key);
		}
		RefreshPanel();
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

		return _pageScroll.ScrollVertical
			>= _pageScroll.GetVScrollBar().MaxValue - 1;
	}

	private int LastPageStart()
		=> Math.Max(0, _entries.Count - MaxEntriesPerPage);

	private static void ClearContainer(Container container)
	{
		foreach (Node child in container.GetChildren().ToArray())
		{
			container.RemoveChild(child);
			child.Free();
		}
	}

	private static void FreeDetachedBranch(Node? branch)
	{
		if (branch is not null
			&& IsInstanceValid(branch)
			&& branch.GetParent() is null)
		{
			branch.Free();
		}
	}

	private static string FormatEntryTooltip(MatchHistoryEntry entry)
		=> $"Turn {entry.TurnNumber} · {FormatSide(entry.Side)}\n{entry.Summary}";

	private static string FormatSide(PlayerSide side)
		=> side == PlayerSide.Corp ? "CORP" : "RUNNER";

	private static Color SideColour(PlayerSide side)
		=> side == PlayerSide.Corp ? CorpCue : RunnerCue;

	private static Label MakeLabel(string text, int size, Color colour)
	{
		var label = new Label
		{
			Text = text,
			MouseFilter = MouseFilterEnum.Ignore,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", colour);
		return label;
	}

	private static Button MakeButton(string text, int size)
	{
		var button = new Button
		{
			Text = text,
			FocusMode = FocusModeEnum.All,
			CustomMinimumSize = new Vector2(0, 30),
		};
		button.AddThemeFontSizeOverride("font_size", size);
		button.AddThemeColorOverride("font_color", Colors.White);
		button.AddThemeColorOverride("font_hover_color", Accent);
		return button;
	}

	private static StyleBoxFlat CreateStyle(Color background, Color border, int width, int radius)
		=> new()
		{
			BgColor = background,
			BorderColor = border,
			BorderWidthLeft = width,
			BorderWidthTop = width,
			BorderWidthRight = width,
			BorderWidthBottom = width,
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomLeft = radius,
			CornerRadiusBottomRight = radius,
			ContentMarginLeft = 7,
			ContentMarginTop = 6,
			ContentMarginRight = 7,
			ContentMarginBottom = 6,
		};
}
