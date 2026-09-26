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
	private readonly Dictionary<int, Button> _boardSubroutineButtons = new();
	private readonly HashSet<int> _boardBreakTargets = new();
	private bool _boardChoosingSubroutines;
	private DecisionViewModel? _boardBreakDecision;
	private Button? _boardChooseBreak;

	private bool TrySelectBoardIcebreaker(string id)
	{
		if (_currentSnapshot is not { } snapshot || !CanUseBoardActions(snapshot)
			|| BoardIcebreakerPresenter.Present(snapshot.View, id) is not { } breaker) return false;
		CancelBoardBreakTargets();
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
		CancelBoardBreakTargets();
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
			CancelBoardBreakTargets();
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
			_icebreakerPanel.GetNode<Button>("%Close").Pressed += () =>
			{
				if (_boardChoosingSubroutines) { CancelBoardBreakTargets(); RefreshBoardIcebreaker(_currentSnapshot!); }
				else ClearTableSelection();
			};
		}
		if (_boardChoosingSubroutines && !SameDecision(_boardBreakDecision, snapshot.View.Decision)) CancelBoardBreakTargets();
		if (_boardBreakerSequence != snapshot.Sequence)
		{
			_boardBreakerSequence = snapshot.Sequence;
			_icebreakerPanel.GetNode<Label>("%Title").Text = _boardChoosingSubroutines ? breaker.Encounter.Ice!.Title : ActionCardName(id);
			_icebreakerPanel.GetNode<Button>("%Close").Text = _boardChoosingSubroutines ? "Back · Esc" : "Close · Esc";
			string ice = breaker.Encounter.Ice!.Title;
			string current = breaker.Strength is { } strength ? $"Strength {strength.EffectiveStrength}" : "Hosted ability";
			_icebreakerPanel.GetNode<Label>("%Strength").Text = $"{current}  →  {ice} · strength {breaker.Encounter.EffectiveStrength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}";
			_icebreakerPanel.GetNode<Label>("%Subroutines").Text = string.Join(" · ", breaker.Encounter.Subroutines.Select(sub =>
				$"{sub.Ordinal + 1}: {(sub.Broken ? "broken" : sub.Resolved ? "resolved" : "unbroken")}"));
			bool hostedVirus = breaker.Card.HostCardId == breaker.Encounter.Ice?.Id
				&& breaker.Actions.Any(action => action.Parameters is RunBreakParameters { SubroutineId: not null });
			int virusCounters = breaker.Card.TypedCounters.Where(counter => counter.Purpose == CardCounterPurpose.Virus).Sum(counter => counter.Count);
			_icebreakerPanel.GetNode<Label>("%Hint").Text = hostedVirus
				? $"Hosted virus counters: {virusCounters}.\nEach break spends 1 counter. Select a subroutine, then confirm."
				: $"Available: {snapshot.View.Run!.RunCredits} run + {snapshot.View.Runner.Credits} personal credits.\nRun credits expire at run end and are spent first. Boost pays immediately; Break lets you select targets first.";
			VBoxContainer actions = _icebreakerPanel.GetNode<VBoxContainer>("%Actions");
			ClearChildren(actions);
			_boardBreakerButtons.Clear();
			_boardSubroutineButtons.Clear();
			_boardChooseBreak = null;
			if (_boardChoosingSubroutines)
			{
				BuildBoardSubroutineControls(snapshot, breaker, actions);
			}
			else
			{
				foreach (LegalActionViewModel action in breaker.Actions.Where(action => action.Kind == ActionKind.PumpBreaker))
					AddBoardBreakerAction(snapshot, actions, action);
				_boardChooseBreak = BoardBreakerButton("Break subroutines…");
				_boardChooseBreak.Disabled = !breaker.Actions.Any(action => action.Kind == ActionKind.BreakSubroutines);
				_boardChooseBreak.Pressed += () => BeginBoardBreakTargets(snapshot.View.Decision!);
				actions.AddChild(_boardChooseBreak);
			}
			if (!breaker.Actions.Any(action => action.Kind == ActionKind.BreakSubroutines))
				actions.AddChild(MakeWrappedLabel(breaker.Encounter.RemainingSubroutines == 0 ? "All subroutines are broken."
					: breaker.Strength is { } value && breaker.Encounter.EffectiveStrength is { } required && value.EffectiveStrength < required
						? "Boost strength to match the ICE before breaking."
						: "No break ability is available for this ICE with your current resources.", 12, Muted));
			_icebreakerPanel.Size = new Vector2(470, _boardChoosingSubroutines ? 470 : 330);
		}
		_icebreakerPanel.Show();
		_gameTable.SetSelection(id);
		_gameTable.SetTargets(new HashSet<string>([breaker.Encounter.Ice!.Id], StringComparer.Ordinal));
		PositionBoardIcebreaker();
	}

	private void PositionBoardIcebreaker()
	{
		if (_icebreakerPanel?.Visible != true || _boardBreakerId is not { } id
			|| !_gameTable.VisibleCards.TryGetValue(_boardChoosingSubroutines ? _currentSnapshot!.View.Run!.Encounter!.Ice!.Id : id, out TableCardControl? card) || !card.IsVisibleInTree()) return;
		foreach (Button row in _boardSubroutineButtons.Values)
			if (row.GetChildCount() > 0 && row.GetChild(0) is GameSymbolLabel label)
				row.CustomMinimumSize = new Vector2(0, Math.Max(44, label.GetContentHeight() + 16));
		// Wrapped labels initially measure before their container has a width.
		// Reapply the bounded size after layout so that first-frame height cannot stick.
		_icebreakerPanel.Size = new Vector2(470, _boardChoosingSubroutines ? 470 : 330);
		Vector2 anchor = _tableViewport.GetGlobalTransform().AffineInverse() * card.GetGlobalRect().Position;
		Vector2 size = _icebreakerPanel.Size;
		float x = anchor.X + card.GetGlobalRect().Size.X / _tableViewport.GetGlobalTransform().Scale.X + 12;
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
		if (action.Kind == ActionKind.BreakSubroutines
			&& (!_boardChoosingSubroutines || !BoardIcebreakerPresenter.SelectedBreaks(breaker, _boardBreakTargets).Any(candidate => candidate.Id == action.Id))) return;
		foreach (Button button in _boardBreakerButtons.Values) button.Disabled = true;
		CancelBoardBreakTargets();
		SubmitAction(decision, action);
	}
}
