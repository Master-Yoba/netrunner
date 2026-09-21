using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private PanelContainer? _spatialPanel;
	private VBoxContainer? _spatialOptions;
	private DecisionViewModel? _spatialDecision;
	private string? _spatialSource;
	private BoardSpatialSelection? _spatialSelection;
	private bool _spatialAutomatic;
	private readonly List<(Button Button, BoardTarget Target)> _spatialCues = [];
	private readonly Dictionary<ActionId, Button> _spatialConfirmButtons = [];
	private bool SpatialVisible => _spatialPanel?.Visible == true;

	private bool SpatialInputAvailable(MatchSnapshot snapshot) => !snapshot.IsTerminal && !snapshot.IsOpponentThinking
		&& snapshot.View.Decision is { } decision && decision.OwnerSide == snapshot.View.Viewer
		&& !_fullMatchResumePaused && !_tableAwaitingSettlement && Volatile.Read(ref _submissionInFlight) == 0
		&& _stagedAction is null && _boardDrag?.IsDragging != true && _gameTable.DrawMotionCount == 0
		&& _zoomOverlay?.Visible != true && _tablePileOverlay?.Visible != true && _galleryOverlay?.Visible != true;

	private bool TrySelectSpatialCard(string id)
	{
		if (_currentSnapshot is not { } snapshot || !SpatialInputAvailable(snapshot)) return SpatialVisible;
		if (_spatialSelection is not null && SpatialVisible)
		{
			BoardTarget? target = _spatialSelection.Candidates.Concat(_spatialSelection.Selected)
				.FirstOrDefault(target => target.Kind == BoardTargetKind.Card && target.Id == id);
			if (target is not null) SelectSpatialTarget(target);
			return true;
		}
		if (TrySelectIncrementalCard(id)) return true;
		BoardSpatialAction[] actions = BoardSpatialActions.Project(snapshot.View).Where(action => action.SourceId == id).ToArray();
		if (actions.Length == 0) { if (!_spatialAutomatic) CloseSpatialActions(); return false; }
		CloseBoardIcebreaker();
		DismissBoardChoices();
		CloseSpatialActions();
		_spatialSource = id;
		_spatialDecision = snapshot.View.Decision;
		_tableAllActions = false;
		_tableSelectedCard = id;
		_gameTable.SetSelection(id);
		HideCardHoverPreview();
		RenderCardTableActions(snapshot);
		return true;
	}

	private bool TrySelectSpatialServer(string id)
	{
		if (!SpatialVisible || _spatialSelection is null) return false;
		BoardTarget? target = _spatialSelection.Candidates.FirstOrDefault(target =>
			target.Kind is BoardTargetKind.Server or BoardTargetKind.ServerRoot or BoardTargetKind.ServerIce && target.Id == id);
		if (target is not null) SelectSpatialTarget(target);
		return true;
	}

	private void CloseSpatialActions()
	{
		_spatialPanel?.Hide();
		_spatialDecision = null;
		_spatialSelection = null;
		_spatialSource = null;
		_spatialAutomatic = false;
		_spatialIncrementalCards = null;
		ClearSpatialCues();
	}

	private void CancelSpatialSelection()
	{
		if (_spatialSubmission is not null) return;
		if (_spatialAutomatic)
		{
			_spatialSelection = null;
			_spatialIncrementalCards = null;
		}
		else CloseSpatialActions();
		_tableSelectedCard = null;
		_gameTable.SetSelection(null);
		HideCardHoverPreview();
		if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
	}

	private void RefreshSpatialActions(MatchSnapshot snapshot)
	{
		if (AdvanceSpatialSubmission(snapshot)) return;
		if (!SameDecision(_spatialDecision, snapshot.View.Decision)) CloseSpatialActions();
		if (_tableAllActions || snapshot.IsTerminal || snapshot.IsOpponentThinking
			|| snapshot.View.Decision is not { } decision || decision.OwnerSide != snapshot.View.Viewer)
		{
			CloseSpatialActions();
			return;
		}
		BoardSpatialAction[] all = BoardSpatialActions.Project(snapshot.View).ToArray();
		BoardSpatialAction[] automatic = all.Where(action => action.Automatic).ToArray();
		if (_spatialSource is null && automatic.Length > 0)
		{
			_spatialAutomatic = true;
			_spatialDecision = decision;
			if (!InitializeIncrementalSelection(snapshot.View)) _spatialSelection ??= new(automatic);
		}
		if (_spatialSource is null && !_spatialAutomatic) return;
		bool wasVisible = SpatialVisible;
		_spatialDecision = decision;
		EnsureSpatialPanel();
		_spatialPanel!.Show();
		RenderSpatialPanel(snapshot, all);
		if (!wasVisible) _spatialOptions!.GetChildren().OfType<Button>().FirstOrDefault()?.GrabFocus();
		UpdateSpatialGeometry();
	}

	private void EnsureSpatialPanel()
	{
		if (_spatialPanel is not null) return;
		_spatialPanel = GD.Load<PackedScene>(BoardChoicePanelPath).Instantiate<PanelContainer>();
		_spatialPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
		_spatialPanel.ZIndex = 120;
		_spatialPanel.MouseFilter = MouseFilterEnum.Stop;
		_tableViewport.AddChild(_spatialPanel);
		_spatialOptions = _spatialPanel.GetNode<VBoxContainer>("%Options");
		Button dismiss = _spatialPanel.GetNode<Button>("%Dismiss");
		dismiss.Text = "Use action list";
		dismiss.Pressed += () =>
		{
			if (_spatialSubmission is not null) return;
			_tableAllActions = true;
			CloseSpatialActions();
			if (_currentSnapshot is { } snapshot) RenderCardTableActions(snapshot);
		};
	}

	private void RenderSpatialPanel(MatchSnapshot snapshot, IReadOnlyList<BoardSpatialAction> all)
	{
		ClearChildren(_spatialOptions!);
		ClearSpatialCues();
		_spatialConfirmButtons.Clear();
		_spatialPanel!.GetNode<ScrollContainer>("Content/OptionsScroll").CustomMinimumSize = new(0, 52);
		_spatialPanel.GetNode<Label>("%Title").Text = _spatialSource is { } source ? ActionCardName(source) : FriendlyText(snapshot.View.Decision!.Prompt);
		_spatialPanel.GetNode<Label>("%Prompt").Text = "Choose an action · Esc closes";
		if (_spatialIncrementalCards is not null) { RenderIncrementalSelection(snapshot); return; }
		if (_spatialSelection is null)
		{
			foreach (var family in all.Where(action => action.SourceId == _spatialSource).GroupBy(action => action.Family))
			{
				BoardSpatialAction[] choices = family.ToArray();
				BoardSpatialAction first = choices[0];
				if (choices.All(action => action.Targets.Count == 0))
				{
					foreach (BoardSpatialAction action in choices) AddSpatialConfirm(snapshot, action);
					continue;
				}
				string label = first.Action.Parameters is RunBioroidAbilityParameters ? "Break a subroutine · lose 1 click…"
					: ActionLabelPresenter.AbilityChoiceLabel(first.Action, ActionCardName);
				AddSpatialButton(label + " · choose targets…", () =>
				{
					if (!CurrentSpatialDecision(snapshot.View.Decision!)) return;
					_spatialSelection = new(choices);
					RenderSpatialPanel(_currentSnapshot!, BoardSpatialActions.Project(_currentSnapshot!.View));
				});
			}
			return;
		}
		BoardSpatialSelection selection = _spatialSelection;
		string selected = string.Join("; ", selection.Selected.Select(target => $"{target.Role}: {SpatialTargetLabel(target)}"));
		_spatialPanel.GetNode<Label>("%Prompt").Text = (selected.Length == 0 ? "Select highlighted targets." : selected)
			+ "\nConfirm after selecting. Esc clears unsubmitted choices.";
		RenderSpatialTargets(snapshot, selection.Candidates.Concat(selection.Selected).Distinct().ToArray());
		foreach (BoardSpatialAction complete in selection.CompleteActions) AddSpatialConfirm(snapshot, complete);
	}

	private void AddSpatialConfirm(MatchSnapshot snapshot, BoardSpatialAction action)
	{
		DecisionViewModel decision = snapshot.View.Decision!;
		Button confirm = AddSpatialButton((_spatialSelection is null ? "" : "Confirm · ") + TableActionLabel(action.Action), () =>
		{
			if (!CurrentSpatialDecision(decision)) return;
			BoardSpatialAction? current = BoardSpatialActions.Project(_currentSnapshot!.View).FirstOrDefault(candidate => candidate.Action.Id == action.Action.Id);
			if (current is null || current.SourceId != _spatialSource && !current.Automatic
				|| current.Targets.Count > 0 && (_spatialSelection is null || current.Targets.Count != _spatialSelection.Selected.Count
					|| !_spatialSelection.Selected.All(current.Targets.Contains))) return;
			CloseSpatialActions();
			SubmitAction(decision, current.Action);
		});
		_spatialConfirmButtons[action.Action.Id] = confirm;
	}

	private bool CurrentSpatialDecision(DecisionViewModel decision) => SpatialVisible
		&& _currentSnapshot is { } snapshot && SpatialInputAvailable(snapshot) && SameDecision(decision, snapshot.View.Decision)
		&& SameDecision(_spatialDecision, decision);

	private Button AddSpatialButton(string label, Action callback)
	{
		Button button = CreateTableActionButton(label);
		button.Alignment = HorizontalAlignment.Left;
		button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		button.Disabled = _currentSnapshot is not { } snapshot || !SpatialInputAvailable(snapshot);
		button.Pressed += () => { if (!button.Disabled) callback(); };
		_spatialOptions!.AddChild(button);
		return button;
	}

	private void SelectSpatialTarget(BoardTarget target)
	{
		if (_spatialDecision is not { } decision || !CurrentSpatialDecision(decision) || _spatialSelection?.Toggle(target) != true) return;
		HideCardHoverPreview();
		RenderSpatialPanel(_currentSnapshot!, BoardSpatialActions.Project(_currentSnapshot!.View));
		_spatialConfirmButtons.Values.FirstOrDefault()?.GrabFocus();
	}

	private string SpatialTargetLabel(BoardTarget target) => target.Kind switch
	{
		BoardTargetKind.Card => ActionCardName(target.Id),
		BoardTargetKind.Subroutine => $"Subroutine {int.Parse(target.Id, System.Globalization.CultureInfo.InvariantCulture) + 1}",
		BoardTargetKind.NewRemote => "New remote",
		_ => _gameTable.GetServerDisplayName(target.Id),
	};

	private void RenderSpatialTargets(MatchSnapshot snapshot, IReadOnlyList<BoardTarget> targets)
	{
		_gameTable.SetTargets(targets.Where(target => target.Kind == BoardTargetKind.Card).Select(target => target.Id).ToHashSet(StringComparer.Ordinal));
		_gameTable.SetNewRemoteDropVisible(targets.Any(target => target.Kind == BoardTargetKind.NewRemote));
		CardViewModel[] cards = BoardSpatialActions.Cards(snapshot.View).ToArray();
		HFlowContainer? browser = null;
		foreach (BoardTarget target in targets.OrderBy(target => target.Kind).ThenBy(target => target.Role, StringComparer.Ordinal).ThenBy(target => target.Id, StringComparer.Ordinal))
		{
			if (target.Kind == BoardTargetKind.Subroutine)
			{
				int ordinal = int.Parse(target.Id, System.Globalization.CultureInfo.InvariantCulture);
				string[] printed = (snapshot.View.Run?.Encounter?.Ice?.RulesText ?? "").Split("[subroutine]", StringSplitOptions.None).Skip(1).Select(text => text.Trim()).ToArray();
				string text = ordinal < printed.Length ? printed[ordinal] : "Break subroutine";
				Button row = AddSpatialButton("", () => SelectSpatialTarget(target));
				row.SetMeta("subroutine_ordinal", ordinal);
				row.ToggleMode = true;
				row.ButtonPressed = _spatialSelection?.Selected.Contains(target) == true;
				var label = new GameSymbolLabel
				{
					SymbolText = $"{(row.ButtonPressed ? "✓" : "○")} {ordinal + 1}. [subroutine] {text}",
					MouseFilter = MouseFilterEnum.Ignore,
					FitContent = true,
					ScrollActive = false,
					AutowrapMode = TextServer.AutowrapMode.WordSmart,
				};
				row.AddChild(label);
				label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
				label.OffsetLeft = 10; label.OffsetRight = -10; label.OffsetTop = 8; label.OffsetBottom = -8;
				row.CustomMinimumSize = new(0, 44);
				continue;
			}
			if (target.Kind == BoardTargetKind.Card && SpatialTargetRect(target) is null && cards.FirstOrDefault(card => card.Id == target.Id) is { } model)
			{
				_spatialPanel!.GetNode<ScrollContainer>("Content/OptionsScroll").CustomMinimumSize = new(0, 230);
				TableCardControl card = TableCardControl.Create();
				card.UseHoverPreview = true;
				card.SetCard(model);
				card.SetDisplaySize(new(105, 147));
				card.SetTargetCandidate(true);
				card.SetSelected(_spatialSelection?.Selected.Contains(target) == true || _spatialIncrementalCards?.Contains(target.Id) == true);
				card.CardSelected += _ => { if (_spatialIncrementalCards is not null) TrySelectIncrementalCard(target.Id); else SelectSpatialTarget(target); };
				card.CardHoverChanged += HandleTableCardHoverChanged;
				if (browser is null)
				{
					browser = GD.Load<PackedScene>("res://scenes/components/BoardChoiceCards.tscn").Instantiate<HFlowContainer>();
					_spatialOptions!.AddChild(browser);
				}
				browser.AddChild(card);
				continue;
			}
			if (target.Kind == BoardTargetKind.Card && _gameTable.VisibleCards.TryGetValue(target.Id, out TableCardControl? visibleCard)
				&& visibleCard.MouseFilter != MouseFilterEnum.Ignore)
			{
				visibleCard.SetSelected(_spatialSelection?.Selected.Contains(target) == true || _spatialIncrementalCards?.Contains(target.Id) == true);
				continue;
			}
			Button cue = CreateTableActionButton(target.Role + " · " + SpatialTargetLabel(target));
			cue.ZIndex = 20;
			var outline = new StyleBoxFlat { BgColor = new Color(0.04f, 0.12f, 0.13f, 0.45f), BorderColor = Accent };
			outline.SetBorderWidthAll(3);
			cue.AddThemeStyleboxOverride("normal", outline);
			cue.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			cue.Pressed += () => { if (_spatialIncrementalCards is not null) TrySelectIncrementalCard(target.Id); else SelectSpatialTarget(target); };
			_tableViewport.AddChild(cue);
			_spatialCues.Add((cue, target));
		}
		UpdateSpatialGeometry();
	}

	private Rect2? SpatialTargetRect(BoardTarget target) => target.Kind switch
	{
		BoardTargetKind.Card => _tableAccessPanel.Visible && _tableAccessCard.CardId == target.Id ? _tableAccessCard.GetGlobalRect() : _gameTable.GetDropRect("HostCard", target.Id),
		BoardTargetKind.Server => _gameTable.GetDropRect("ServerAccess", target.Id),
		BoardTargetKind.ServerRoot => _gameTable.GetDropRect("ServerRoot", target.Id),
		BoardTargetKind.ServerIce => _gameTable.GetDropRect("ServerIce", target.Id),
		BoardTargetKind.NewRemote => _gameTable.GetDropRect("NewRemote", null),
		_ => null,
	};

	private void UpdateSpatialGeometry()
	{
		if (!SpatialVisible) return;
		bool enabled = _currentSnapshot is { } snapshot && SpatialInputAvailable(snapshot) && _spatialSubmission is null;
		foreach (Button button in _spatialOptions!.GetChildren().OfType<Button>())
		{
			button.Disabled = !enabled;
			if (button.GetChildren().OfType<GameSymbolLabel>().FirstOrDefault() is { } symbols)
				button.CustomMinimumSize = new(0, Math.Max(44, symbols.GetContentHeight() + 16));
		}
		foreach (TableCardControl card in _spatialOptions.FindChildren("*", "Control", true, false).OfType<TableCardControl>())
		{
			card.SetTargetCandidate(enabled);
			card.MouseFilter = enabled ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
			card.Modulate = new Color(1, 1, 1, enabled ? 1 : 0.5f);
		}
		IEnumerable<string> targetIds = _spatialSelection?.Candidates.Concat(_spatialSelection.Selected)
			.Where(target => target.Kind == BoardTargetKind.Card).Select(target => target.Id)
			?? _currentSnapshot?.View.CardSelection?.Candidates ?? [];
		_gameTable.SetTargets(enabled ? targetIds.ToHashSet(StringComparer.Ordinal) : []);
		_spatialPanel!.GetNode<Button>("%Dismiss").Disabled = _spatialSubmission is not null;
		foreach ((Button cue, BoardTarget target) in _spatialCues)
		{
			Rect2? rect = SpatialTargetRect(target);
			cue.Visible = enabled && rect is { } area && area.HasArea();
			if (rect is not { } visible) continue;
			cue.GlobalPosition = visible.Position;
			cue.Size = visible.Size;
		}
		float contentHeight = Math.Max(_spatialPanel.GetNode<ScrollContainer>("Content/OptionsScroll").CustomMinimumSize.Y, _spatialOptions.GetCombinedMinimumSize().Y);
		float desiredHeight = contentHeight + _spatialPanel.GetNode<Label>("%Title").GetCombinedMinimumSize().Y
			+ _spatialPanel.GetNode<Label>("%Prompt").GetCombinedMinimumSize().Y
			+ _spatialPanel.GetNode<Button>("%Dismiss").GetCombinedMinimumSize().Y + 40;
		Vector2 size = new(440, Math.Min(Math.Clamp(desiredHeight, 200, 440), _tableViewport.Size.Y - 24));
		_spatialPanel.Size = size;
		Rect2? anchor = _spatialSource is { } source ? SpatialTargetRect(new(BoardTargetKind.Card, source)) : null;
		Vector2 local = anchor is { } anchorCard ? _tableViewport.GetGlobalTransform().AffineInverse() * anchorCard.Position : new(_tableViewport.Size.X / 2 - size.X / 2, 12);
		float x = anchor is { } bounds ? local.X + bounds.Size.X + 12 : local.X;
		if (x + size.X > _tableViewport.Size.X - 12) x = local.X - size.X - 12;
		float right = Math.Max(12, _tableViewport.Size.X - size.X - 12);
		float bottom = Math.Max(12, _tableViewport.Size.Y - size.Y - 12);
		Vector2 preferred = new(Math.Clamp(x, 12, right), Math.Clamp(local.Y, 12, bottom));
		BoardTarget[] targets = _spatialSelection?.Candidates.Concat(_spatialSelection.Selected).Distinct().ToArray()
			?? _spatialIncrementalCards?.Select(id => new BoardTarget(BoardTargetKind.Card, id)).ToArray() ?? [];
		Rect2[] targetRects = targets.Select(SpatialTargetRect).OfType<Rect2>().ToArray();
		Vector2[] positions = [preferred, new(12, 12), new(right, 12), new(right / 2, 12), new(12, bottom), new(right, bottom)];
		_spatialPanel.Position = positions.MinBy(position =>
		{
			Rect2 proposed = new(_tableViewport.GetGlobalTransform() * position, size);
			return targetRects.Sum(target => { Vector2 overlap = proposed.Intersection(target).Size; return overlap.X * overlap.Y; })
				+ position.DistanceTo(preferred) * 0.01f;
		});
	}

	private void ClearSpatialCues()
	{
		foreach ((Button cue, _) in _spatialCues) { cue.GetParent()?.RemoveChild(cue); cue.QueueFree(); }
		_spatialCues.Clear();
		if (_boardDrag?.IsDragging != true) _gameTable?.SetNewRemoteDropVisible(false);
	}
}
