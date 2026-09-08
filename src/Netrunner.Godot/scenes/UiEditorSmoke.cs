using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Godot-native regressions for appearance binding and preview isolation.</summary>
public partial class UiEditorSmoke : Node
{
	public override async void _Ready()
	{
		try
		{
			await CheckCards();
			await CheckPreview();
			GD.Print("M410_EDITOR_UI PASS resource_changes=true replacement=true reentry=true shared_state_safe=true hidden_clearing=true preview_isolated=true");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PrintErr($"M410_EDITOR_UI FAIL {error}");
			GetTree().Quit(1);
		}
	}

	private async Task CheckCards()
	{
		foreach (bool ice in new[] { false, true })
		{
			TableCardControl configured = TableCardControl.Create();
			configured.SetIce(ice);
			if (!ice) configured.SetDisplaySize(new Vector2(105, 147));
			configured.SetCard(UiPreviewSamples.Known("editor-smoke-role", ice ? "ICE" : "Asset"));
			AddChild(configured);
			Require(configured.FindChildren("Title", "Label", true, false).Cast<Label>().Count(label => label.IsVisibleInTree()) == 1,
				"Preconfigured card role left multiple label layouts visible.");
			configured.Free();
		}
		CardAppearance shared = GD.Load<CardAppearance>(TableCardControl.DefaultAppearancePath);
		int baselineConnections = shared.GetSignalConnectionList(Resource.SignalName.Changed).Count;
		int savedFont = shared.TitleFontSize;
		Vector2 savedSize = shared.PortraitSize;
		TableCardControl first = TableCardControl.Create();
		TableCardControl second = TableCardControl.Create();
		AddChild(first);
		AddChild(second);
		first.SetCard(UiPreviewSamples.Known("editor-smoke-first"));
		second.SetCard(UiPreviewSamples.Known("editor-smoke-second"));
		Require(ReferenceEquals(first.Appearance, shared) && ReferenceEquals(second.Appearance, shared), "Card scene did not bind the shared resource.");
		try
		{
			shared.TitleFontSize = 13;
			shared.PortraitSize = new Vector2(160, 224);
			Require(first.DisplaySize == shared.PortraitSize && second.DisplaySize == shared.PortraitSize, "Resource size change did not propagate.");
			Require(VisibleTitle(first).GetThemeFontSize("font_size") == 13, "Resource typography change did not propagate.");
			Color savedSelection = shared.SelectedColor;
			first.SetSelected(true);
			Require(!second.IsSelected && shared.SelectedColor == savedSelection, "Instance selection mutated shared appearance.");
			CardAppearance replacement = (CardAppearance)shared.Duplicate();
			first.Appearance = replacement;
			shared.TitleFontSize = 14;
			Require(VisibleTitle(first).GetThemeFontSize("font_size") == 13 && VisibleTitle(second).GetThemeFontSize("font_size") == 14,
				"Replaced resource remained subscribed or isolated resources were shared.");
			RemoveChild(first);
			replacement.TitleFontSize = 15;
			AddChild(first);
			replacement.TitleFontSize = 16;
			Require(VisibleTitle(first).GetThemeFontSize("font_size") == 16, "Resource subscription did not reconnect after tree reentry.");
			first.EmitSignal(Control.SignalName.MouseEntered);
			Require(first.IsInteractionActive, "Known-card hover did not activate.");
			first.SetCard(new CardViewModel("editor-smoke-hidden", null, "PRIVATE TITLE MUST NOT RENDER", "", false, false, 0,
				null, null, null, null, [], "PRIVATE RULES MUST NOT RENDER"));
			Require(!first.IsInteractionActive && !first.IsActionableSource, "Concealing a card left interaction state active.");
			Require(!first.FindChildren("*", "Label", true, false).Cast<Label>()
				.Any(label => label.IsVisibleInTree() && label.Text.Contains("PRIVATE", StringComparison.Ordinal)), "Hidden card leaked old label content.");
		}
		finally
		{
			shared.TitleFontSize = savedFont;
			shared.PortraitSize = savedSize;
			first.Free();
			second.Free();
		}
		await Frames(2);
		Require(shared.GetSignalConnectionList(Resource.SignalName.Changed).Count == baselineConnections, "Card consumers retained resource subscriptions after freeing.");
	}

	private async Task CheckPreview()
	{
		string diagnostics = ProjectSettings.GlobalizePath("user://diagnostics");
		string[] before = DiagnosticFiles(diagnostics);
		CardAppearance appearance = GD.Load<CardAppearance>(TableCardControl.DefaultAppearancePath);
		int beforeConnections = appearance.GetSignalConnectionList(Resource.SignalName.Changed).Count;
		for (int cycle = 0; cycle < 3; cycle++)
		{
			UiPreview preview = GD.Load<PackedScene>("res://scenes/UiPreview.tscn").Instantiate<UiPreview>();
			AddChild(preview);
			await Frames(3);
			GameTableControl board = preview.GetNode<GameTableControl>("Margin/Content/Pages/Board/MatchTable/TableViewport/GameBoard");
			Control access = preview.GetNode<Control>("Margin/Content/Pages/Board/MatchTable/TableViewport/AccessPanel");
			Require(access.GetNode<TableCardControl>("%Card").Card is { IsKnown: true }, "Preview did not bind the accessed card.");
			TableCardControl stable = board.VisibleCards["preview-hand-1"];
			CardAppearance original = stable.Appearance;
			CardAppearance replacement = (CardAppearance)original.Duplicate();
			stable.Appearance = replacement;
			replacement.PortraitSize = new Vector2(180, 252);
			Require(stable.DisplaySize == replacement.PortraitSize * board.Layout.CompactPortraitScale,
				"Board did not rebind the replacement resource to its card footprint.");
			stable.Appearance = original;
			float width = board.Layout.LaneWidth;
			float overlap = board.Layout.HandOverlap;
			try
			{
				board.Layout.LaneWidth = width + 8;
				board.Layout.HandOverlap = overlap + 4;
				await Frames(2);
				Require(ReferenceEquals(stable, board.VisibleCards["preview-hand-1"]), "Appearance editing replaced a stable card control.");
			}
			finally
			{
				board.Layout.LaneWidth = width;
				board.Layout.HandOverlap = overlap;
			}
			preview.Density = UiPreviewSamples.Density.Dense;
			preview.ViewerSeat = cycle % 2 == 0 ? PlayerSide.Corp : PlayerSide.Runner;
			preview.Decision = UiPreview.SampleDecision.NestedChoice;
			await Frames(3);
			Require(!preview.FindChildren("*", "Control", true, false).OfType<Main>().Any(), "Preview instantiated gameplay composition.");
			preview.Free();
			await Frames(3);
		}
		Require(DiagnosticFiles(diagnostics).SequenceEqual(before), "Preview created session diagnostics.");
		Require(appearance.GetSignalConnectionList(Resource.SignalName.Changed).Count == beforeConnections, "Preview retained appearance subscriptions after repeated reloads.");
	}

	private static string[] DiagnosticFiles(string directory) => System.IO.Directory.Exists(directory)
		? System.IO.Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray() : [];

	private static Label VisibleTitle(TableCardControl card) => card.FindChildren("Title", "Label", true, false)
		.Cast<Label>().First(label => label.IsVisibleInTree());

	private async Task Frames(int count)
	{
		for (int frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}
}
