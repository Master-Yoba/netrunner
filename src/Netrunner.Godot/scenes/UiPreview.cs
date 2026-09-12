using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Opt-in authoring host using synthetic presentation models and production scenes.
/// It has no coordinator, action submission, metadata catalog, save or trace access.
/// </summary>
[Tool]
public partial class UiPreview : Control
{
	public enum SampleDecision { Ordinary, Access, NestedChoice, Outcome, Recovery }
	public enum PreviewPage { Board, Cards, Setup, Inspection, Piles }

	private PlayerSide _viewerSeat = PlayerSide.Runner;
	private UiPreviewSamples.Density _density = UiPreviewSamples.Density.Normal;
	private SampleDecision _decision = SampleDecision.Access;
	private PreviewPage _page;
	private bool _historyExpanded = true;
	private bool _showHoverPreview;
	private bool _dirty = true;
	private TabContainer? _tabs;

	[ExportGroup("Synthetic board")]
	[Export]
	public PlayerSide ViewerSeat { get => _viewerSeat; set { _viewerSeat = value; _dirty = true; } }
	[Export]
	public UiPreviewSamples.Density Density { get => _density; set { _density = value; _dirty = true; } }
	[Export]
	public SampleDecision Decision { get => _decision; set { _decision = value; _dirty = true; } }
	[Export]
	public bool HistoryExpanded { get => _historyExpanded; set { _historyExpanded = value; _dirty = true; } }
	[Export]
	public bool ShowHoverPreview { get => _showHoverPreview; set { _showHoverPreview = value; _dirty = true; } }
	[Export]
	public PreviewPage Page { get => _page; set { _page = value; _dirty = true; } }

	public override void _EnterTree() => _dirty = true;

	public override void _Ready()
	{
		_tabs = GetNode<TabContainer>("Margin/Content/Pages");
		RefreshPreview();
	}

	public override void _Process(double delta)
	{
		if (_dirty && _tabs is not null) RefreshPreview();
	}

	private void RefreshPreview()
	{
		if (_tabs is null) return;
		_dirty = false;
		_tabs.CurrentTab = (int)Page;
		Control table = _tabs.GetNode<Control>("Board/MatchTable");
		Control viewport = table.GetNode<Control>("TableViewport");
		GameTableControl board = viewport.GetNode<GameTableControl>("GameBoard");
		MatchViewModel view = UiPreviewSamples.Board(ViewerSeat, Density);
		board.PreviewMode = GameTableControl.PreviewDensity.Off;
		board.MotionEnabled = false;
		board.Render(view);
		board.SetActionableSources(new HashSet<string>(["preview-hand-1", "preview-hand-3"], StringComparer.Ordinal));
		board.SetTargets(new HashSet<string>(["preview-root-0"], StringComparer.Ordinal));
		board.SetSelection("preview-rig-0");
		MatchHistoryPanel history = viewport.GetNode<MatchHistoryPanel>("History");
		history.PreviewSamples = false;
		history.ClearHistory();
		history.Append(Enumerable.Range(1, 24).Select(i => new MatchHistoryEntry(
			$"preview-history-{i}", i, 3, i % 2 == 0 ? PlayerSide.Corp : PlayerSide.Runner,
			i % 3 == 0 ? "Runner accesses Sample Asset." : i % 2 == 0 ? "Corp installs a card in Remote 1." : "Runner gains 1 credit.",
			["Synthetic detail: this entry is safe to inspect and does not describe a real match."])
		{ Cards = i % 3 == 0 ? [UiPreviewSamples.Known("preview-access", "Asset")] : [] }).ToArray());
		history.SetExpanded(HistoryExpanded);
		CardPreviewLayout hover = viewport.GetNode<CardPreviewLayout>("CardHoverPreview");
		hover.Visible = ShowHoverPreview;
		hover.Position = new Vector2(420, 24);
		hover.Size = hover.PreferredSize;
		hover.GetNode<TableCardControl>("%Card").SetCard(UiPreviewSamples.Known("preview-hover"));
		hover.GetNode<Label>("%Title").Text = "Synthetic hovered card with a long title";
		hover.GetNode<Label>("%Rules").Text = UiPreviewSamples.Known("preview-hover").RulesText;
		Control dock = table.GetNode<Control>("TableViewport/DecisionDock");
		Label prompt = dock.GetNode<Label>("%Prompt");
		prompt.Text = Decision switch
		{
			SampleDecision.Access => "Accessing R&D · choose what to do with the accessed card.",
			SampleDecision.NestedChoice => "Choose a server, then choose an installed card.",
			SampleDecision.Outcome => "Match complete.",
			SampleDecision.Recovery => "Recovered at action 42. Continue when ready.",
			_ => "Choose a card, a server or a basic action.",
		};
		Control actions = dock.GetNode<Control>("%Actions");
		Clear(actions);
		string[] labels = Decision switch
		{
			SampleDecision.Access => ["Trash · 3 credits", "Continue access"],
			SampleDecision.NestedChoice => ["Choose Remote 1", "Choose Remote 2", "Back"],
			SampleDecision.Outcome => [],
			SampleDecision.Recovery => ["Continue recovered match"],
			_ => ["Gain 1 credit · 1 click", "Draw a card · 1 click", "All legal actions…"],
		};
		foreach (string label in labels)
		{
			Button button = GD.Load<PackedScene>("res://scenes/components/ActionButton.tscn").Instantiate<Button>();
			button.Text = label;
			// An authoring-only click demonstrates feedback without an engine action ID.
			button.Pressed += () => prompt.Text = $"Preview selection: {label}";
			actions.AddChild(button);
		}
		Control focus = dock.GetNode<Control>("%FocusPresentation");
		Clear(focus);
		if (Decision == SampleDecision.Outcome)
			focus.AddChild(GD.Load<PackedScene>("res://scenes/components/OutcomePanel.tscn").Instantiate());
		Control access = viewport.GetNode<Control>("AccessPanel");
		access.Visible = Decision == SampleDecision.Access;
		access.GetNode<TableCardControl>("%Card").SetCard(UiPreviewSamples.Known("preview-access", "Asset"));
		access.GetNode<Label>("%Title").Text = "Sample accessed asset";
		table.GetNode<Label>("Toolbar/LastAction").Text = "Synthetic preview · no match is running";
		Control inspection = _tabs.GetNode<Control>("Inspection/CardInspection");
		inspection.GetNode<Label>("%Title").Text = "Sample card inspection";
		inspection.GetNode<Label>("%Rules").Text = UiPreviewSamples.Known("preview-inspection").RulesText;
		Control piles = _tabs.GetNode<Control>("Piles/PileBrowser");
		piles.GetNode<Label>("%Title").Text = "Sample public discard pile";
		Control pileCards = piles.GetNode<Control>("%Cards");
		Clear(pileCards);
		for (int i = 0; i < 6; i++)
		{
			TableCardControl card = TableCardControl.Create();
			pileCards.AddChild(card);
			card.SetCard(UiPreviewSamples.Known($"preview-pile-{i}"));
		}
	}

	private static void Clear(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			parent.RemoveChild(child);
			child.QueueFree();
		}
	}
}
