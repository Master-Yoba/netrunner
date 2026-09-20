using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private void CancelBoardBreakTargets()
	{
		_boardChoosingSubroutines = false;
		_boardBreakDecision = null;
		_boardBreakTargets.Clear();
		_boardSubroutineButtons.Clear();
		_boardChooseBreak = null;
		_boardBreakerSequence = -1;
	}

	private void BeginBoardBreakTargets(DecisionViewModel decision)
	{
		if (_icebreakerPanel?.Visible != true || _currentSnapshot is not { } snapshot || !CanUseBoardActions(snapshot)
			|| !SameDecision(decision, snapshot.View.Decision) || _boardBreakerId is not { } id
			|| BoardIcebreakerPresenter.Present(snapshot.View, id) is not { } breaker
			|| !breaker.Actions.Any(action => action.Kind == ActionKind.BreakSubroutines)) return;
		_boardChoosingSubroutines = true;
		_boardBreakDecision = decision;
		_boardBreakTargets.Clear();
		_boardBreakerSequence = -1;
		HideCardHoverPreview();
		RefreshBoardIcebreaker(snapshot);
	}

	private static Button BoardBreakerButton(string text)
	{
		Button button = CreateTableActionButton(text);
		button.Alignment = HorizontalAlignment.Left;
		button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		button.ClipText = true;
		button.CustomMinimumSize = new Vector2(0, 44);
		button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		return button;
	}

	private void AddBoardBreakerAction(MatchSnapshot snapshot, VBoxContainer actions, LegalActionViewModel action)
	{
		Button button = BoardBreakerButton(TableActionLabel(action));
		DecisionViewModel decision = snapshot.View.Decision!;
		button.Pressed += () => SubmitBoardIcebreaker(decision, action);
		actions.AddChild(button);
		_boardBreakerButtons[action.Id] = button;
	}

	private void BuildBoardSubroutineControls(MatchSnapshot snapshot, BoardIcebreakerViewModel breaker, VBoxContainer actions)
	{
		_icebreakerPanel!.GetNode<Label>("%Strength").Text = $"Breaking with {breaker.Card.Title} · strength {breaker.Strength?.EffectiveStrength.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}";
		_icebreakerPanel.GetNode<Label>("%Subroutines").Text = "Select subroutines on this ICE, then choose a payment below.";
		_icebreakerPanel.GetNode<Label>("%Hint").Text = _boardBreakTargets.Count == 0
			? "Selecting targets spends nothing. Back or Esc cancels."
			: $"Selected: {string.Join(", ", _boardBreakTargets.Order().Select(ordinal => ordinal + 1))}. One payment activates the ability once.";
		string[] printed = (breaker.Encounter.Ice!.RulesText ?? "").Split("[subroutine]", StringSplitOptions.None).Skip(1)
			.Select(text => text.Trim()).ToArray();
		foreach (SubroutineViewModel subroutine in breaker.Encounter.Subroutines)
		{
			int ordinal = subroutine.Ordinal;
			bool selected = _boardBreakTargets.Contains(ordinal);
			string text = ordinal < printed.Length ? printed[ordinal] : $"Subroutine {ordinal + 1}";
			Button button = BoardBreakerButton("");
			var label = new GameSymbolLabel
			{
				SymbolText = $"{(selected ? "✓" : "○")} {ordinal + 1}. [subroutine] {text}{(subroutine.Broken ? " · broken" : subroutine.Resolved ? " · resolved" : "")}",
				MouseFilter = MouseFilterEnum.Ignore,
				FitContent = true,
				ScrollActive = false,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			button.AddChild(label);
			label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			label.OffsetLeft = 10;
			label.OffsetRight = -10;
			label.OffsetTop = 8;
			label.OffsetBottom = -8;
			button.ToggleMode = true;
			button.ButtonPressed = selected;
			int[] proposed = _boardBreakTargets.Append(ordinal).Distinct().ToArray();
			button.Disabled = subroutine.Broken || subroutine.Resolved || (!selected && !breaker.Actions.Any(action =>
				action.Kind == ActionKind.BreakSubroutines && proposed.All(target => BoardIcebreakerPresenter.BrokenOrdinals(breaker.Encounter, action).Contains(target))));
			DecisionViewModel decision = snapshot.View.Decision!;
			button.Pressed += () =>
			{
				if (!_boardChoosingSubroutines || _icebreakerPanel?.Visible != true || _tableAllActions
					|| _currentSnapshot is not { } current || !CanUseBoardActions(current)
					|| !SameDecision(decision, current.View.Decision) || _boardBreakerId != breaker.Card.Id
					|| !_boardSubroutineButtons.TryGetValue(ordinal, out Button? active) || active != button || button.Disabled) return;
				if (!_boardBreakTargets.Add(ordinal)) _boardBreakTargets.Remove(ordinal);
				_boardBreakerSequence = -1;
				RefreshBoardIcebreaker(current);
			};
			actions.AddChild(button);
			_boardSubroutineButtons[ordinal] = button;
		}
		IReadOnlyList<LegalActionViewModel> payments = BoardIcebreakerPresenter.SelectedBreaks(breaker, _boardBreakTargets);
		if (payments.Count > 0) actions.AddChild(MakeWrappedLabel("Pay and break selected subroutines", 13, Accent));
		foreach (LegalActionViewModel payment in payments) AddBoardBreakerAction(snapshot, actions, payment);
	}
}
