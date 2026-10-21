using System.Text.RegularExpressions;
using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private readonly Dictionary<ActionId, Button> _accessActionButtons = new();

	private void RefreshAccessActions(MatchSnapshot snapshot)
	{
		VBoxContainer controls = _tableAccessPanel.GetNode<VBoxContainer>("%Actions");
		ClearChildren(controls);
		_accessActionButtons.Clear();
		if (AccessActionsPresenter.Card(snapshot.View) is not { } card) return;
		IReadOnlyList<LegalActionViewModel> actions = AccessActionsPresenter.Actions(snapshot.View);
		Label hint = _tableAccessPanel.GetNode<Label>("%PaymentHint");
		hint.Text = snapshot.View.Run!.RunCredits > 0
			? $"{snapshot.View.Run.RunCredits} run credits available · expire at run end.\nRun credits are spent before personal credits."
			: "";
		hint.Visible = hint.Text.Length > 0 && actions.Any(action => action.Kind == ActionKind.TrashAccessedCard);
		foreach (LegalActionViewModel action in actions)
		{
			DecisionViewModel decision = snapshot.View.Decision!;
			string text = AccessActionsPresenter.Label(action, card);
			Button button = CreateTableActionButton("");
			button.TooltipText = FriendlyText(text);
			button.AccessibilityName = button.TooltipText;
			button.CustomMinimumSize = new(260, 44);
			var label = new GameSymbolLabel
			{
				SymbolText = Regex.Replace(FriendlyText(text), @"\bcredits?\b", "[credit]"),
				MouseFilter = MouseFilterEnum.Ignore,
				FitContent = true,
				ScrollActive = false,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			button.AddChild(label);
			label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			label.OffsetLeft = 10; label.OffsetRight = -10; label.OffsetTop = 8; label.OffsetBottom = -8;
			button.Pressed += () =>
			{
				if (_currentSnapshot is not { } current || !CanUseAccessActions(current)
					|| !SameDecision(decision, current.View.Decision)
					|| AccessActionsPresenter.Card(current.View)?.Id != card.Id) return;
				LegalActionViewModel? issued = AccessActionsPresenter.Actions(current.View).FirstOrDefault(candidate => candidate.Id == action.Id);
				if (issued is null) return;
				HideCardHoverPreview();
				SubmitAction(current.View.Decision!, issued);
			};
			controls.AddChild(button);
			_accessActionButtons[action.Id] = button;
			_tableActionButtons[action.Id] = button;
		}
		UpdateAccessControls();
	}

	private bool CanUseAccessActions(MatchSnapshot snapshot) => _tableAccessPanel.IsVisibleInTree()
		&& SpatialInputAvailable(snapshot) && !SpatialVisible && !_boardChoiceVisible && _spatialSubmission is null;

	private void UpdateAccessControls()
	{
		if (!_tableAccessPanel.IsVisibleInTree()) return;
		bool enabled = _currentSnapshot is { } snapshot && CanUseAccessActions(snapshot);
		foreach (Button button in _accessActionButtons.Values)
		{
			button.Disabled = !enabled;
			GameSymbolLabel label = button.GetChildren().OfType<GameSymbolLabel>().Single();
			label.Modulate = new Color(1, 1, 1, enabled ? 1 : 0.5f);
			button.CustomMinimumSize = new(260, Math.Max(44, label.GetContentHeight() + 16));
		}
	}
}
