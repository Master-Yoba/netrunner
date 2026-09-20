using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private static PackedScene? _icebreakerPanelScene;
	private PanelContainer? _icebreakerPanel;
	private string? _boardBreakerId;
	private string? _boardBreakerEncounter;
	private long _boardBreakerSequence = -1;
	private readonly Dictionary<ActionId, Button> _boardBreakerButtons = new();

	private bool TrySelectBoardIcebreaker(string id)
	{
		if (_currentSnapshot is not { } snapshot || !CanUseBoardActions(snapshot)
			|| BoardIcebreakerPresenter.Present(snapshot.View, id) is not { } breaker) return false;
		_boardBreakerId = id;
		_boardBreakerEncounter = breaker.EncounterId;
		_boardBreakerSequence = -1;
		_tableSelectedCard = id;
		_tableSelectedServer = null;
		_tableAllActions = false;
		_gameTable.SetSelection(id);
		_gameTable.SetSelectedServer(null);
		HideCardHoverPreview();
		RecordFullMatchSelection("board-icebreaker", id);
		RenderCardTableActions(snapshot);
		return true;
	}

	private void CloseBoardIcebreaker()
	{
		_boardBreakerId = null;
		_boardBreakerEncounter = null;
		_boardBreakerSequence = -1;
		_icebreakerPanel?.Hide();
		_boardBreakerButtons.Clear();
	}

	private void RefreshBoardIcebreaker(MatchSnapshot snapshot)
	{
		if (_boardBreakerId is not { } id) return;
		if (snapshot.IsTerminal || snapshot.View.Run?.Encounter?.Id != _boardBreakerEncounter
			|| !snapshot.View.RunnerRig.Any(card => card.Id == id && card.IsKnown))
		{
			CloseBoardIcebreaker();
			return;
		}
		if (!CanUseBoardActions(snapshot) || _tableAllActions
			|| BoardIcebreakerPresenter.Present(snapshot.View, id) is not { } breaker)
		{
			_icebreakerPanel?.Hide();
			_boardBreakerSequence = -1;
			return;
		}
		if (_icebreakerPanel is null)
		{
			_icebreakerPanelScene ??= GD.Load<PackedScene>("res://scenes/components/IcebreakerPanel.tscn");
			_icebreakerPanel = _icebreakerPanelScene.Instantiate<PanelContainer>();
			_icebreakerPanel.ZIndex = 120;
			_tableViewport.AddChild(_icebreakerPanel);
			_icebreakerPanel.GetNode<Button>("%Close").Pressed += ClearTableSelection;
		}
		if (_boardBreakerSequence != snapshot.Sequence)
		{
			_boardBreakerSequence = snapshot.Sequence;
			_icebreakerPanel.GetNode<Label>("%Title").Text = ActionCardName(id);
			string ice = breaker.Encounter.Ice!.Title;
			string current = breaker.Strength is { } strength ? $"Strength {strength.EffectiveStrength}" : "Hosted ability";
			_icebreakerPanel.GetNode<Label>("%Strength").Text = $"{current}  →  {ice} · strength {breaker.Encounter.EffectiveStrength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}";
			_icebreakerPanel.GetNode<Label>("%Subroutines").Text = string.Join(" · ", breaker.Encounter.Subroutines.Select(sub =>
				$"{sub.Ordinal + 1}: {(sub.Broken ? "broken" : sub.Resolved ? "resolved" : "unbroken")}"));
			_icebreakerPanel.GetNode<Label>("%Hint").Text = $"Available: {snapshot.View.Runner.Credits} personal + {snapshot.View.Run!.RunCredits} hosted credits.\nEach click uses an ability and pays its displayed cost.";
			VBoxContainer actions = _icebreakerPanel.GetNode<VBoxContainer>("%Actions");
			ClearChildren(actions);
			_boardBreakerButtons.Clear();
			foreach (LegalActionViewModel action in breaker.Actions.OrderBy(action => action.Kind == ActionKind.PumpBreaker ? 0 : 1))
			{
				Button button = CreateTableActionButton(TableActionLabel(action));
				button.Alignment = HorizontalAlignment.Left;
				button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				button.ClipText = true;
				button.CustomMinimumSize = new Vector2(0, 44);
				button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				IReadOnlyList<int> ordinals = BoardIcebreakerPresenter.BrokenOrdinals(breaker.Encounter, action);
				if (ordinals.Count > 0) button.Text += "\nSubroutines: " + string.Join(", ", ordinals.Select(ordinal => ordinal + 1));
				DecisionViewModel decision = snapshot.View.Decision!;
				button.Pressed += () => SubmitBoardIcebreaker(decision, action);
				actions.AddChild(button);
				_boardBreakerButtons[action.Id] = button;
			}
			if (!breaker.Actions.Any(action => action.Kind == ActionKind.BreakSubroutines))
				actions.AddChild(MakeWrappedLabel(breaker.Encounter.RemainingSubroutines == 0 ? "All subroutines are broken."
					: breaker.Strength is { } value && breaker.Encounter.EffectiveStrength is { } required && value.EffectiveStrength < required
						? "Boost strength to match the ICE before breaking."
						: "No break ability is available for this ICE with your current resources.", 12, Muted));
			_icebreakerPanel.Size = new Vector2(470, 330);
		}
		_icebreakerPanel.Show();
		_gameTable.SetSelection(id);
		_gameTable.SetTargets(new HashSet<string>([breaker.Encounter.Ice!.Id], StringComparer.Ordinal));
		PositionBoardIcebreaker();
	}

	private void PositionBoardIcebreaker()
	{
		if (_icebreakerPanel?.Visible != true || _boardBreakerId is not { } id
			|| !_gameTable.VisibleCards.TryGetValue(id, out TableCardControl? card) || !card.IsVisibleInTree()) return;
		// Wrapped labels initially measure before their container has a width.
		// Reapply the bounded size after layout so that first-frame height cannot stick.
		_icebreakerPanel.Size = new Vector2(470, 330);
		Vector2 anchor = _tableViewport.GetGlobalTransform().AffineInverse() * card.GetGlobalRect().Position;
		Vector2 size = _icebreakerPanel.Size;
		float x = anchor.X + card.Size.X + 12;
		if (x + size.X > _tableViewport.Size.X - 12) x = anchor.X - size.X - 12;
		_icebreakerPanel.Position = new Vector2(Math.Clamp(x, 12, Math.Max(12, _tableViewport.Size.X - size.X - 12)),
			Math.Clamp(anchor.Y - size.Y + 52, 12, Math.Max(12, _tableViewport.Size.Y - size.Y - 12)));
	}

	private void SubmitBoardIcebreaker(DecisionViewModel decision, LegalActionViewModel selected)
	{
		if (_icebreakerPanel?.Visible != true || _tableAllActions
			|| _currentSnapshot is not { } snapshot || !CanUseBoardActions(snapshot)
			|| !SameDecision(decision, snapshot.View.Decision) || _boardBreakerId is not { } id
			|| BoardIcebreakerPresenter.Present(snapshot.View, id) is not { } breaker
			|| breaker.Actions.FirstOrDefault(action => action.Id == selected.Id) is not { } action) return;
		foreach (Button button in _boardBreakerButtons.Values) button.Disabled = true;
		SubmitAction(decision, action);
	}
}
