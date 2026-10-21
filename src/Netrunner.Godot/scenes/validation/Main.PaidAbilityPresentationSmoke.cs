using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Bounded UI-404 evidence for the Slice 4.3 client surface. This path is
/// deliberately independent from the match worker: it renders typed,
/// synthetic view values through the real controls and presenters without
/// constructing or mutating an authoritative GameState.
/// </summary>
public partial class Main
{
	private async Task RunSlice43UiSmokeAsync()
	{
		bool cardInSceneTree = false;
		bool readyCue = false;
		bool readyChips = false;
		bool usedCue = false;
		bool usedChips = false;
		bool abilityAffordance = false;
		bool rfgRemoved = false;
		TextCardControl? cardControl = null;
		Button? abilityButton = null;
		try
		{
			// Keep the synthetic card visible in the normal client shell while the
			// scene-tree assertions exercise the same control used by the board.
			_board.Visible = true;
			CardViewModel readyCard = CreateSyntheticSlice43Card();
			string expectedChipText = string.Join("  ", readyCard.CounterChips);
			cardControl = new TextCardControl();
			cardControl.SetCard(readyCard);
			_runnerRig.AddChild(cardControl);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			cardInSceneTree = cardControl.IsInsideTree()
				&& cardControl.GetParent() == _runnerRig;
			readyCue = cardInSceneTree
				&& string.Equals(
					cardControl.DisplayedAbilityCue,
					AbilityCueLabels.Ready,
					StringComparison.Ordinal);
			readyChips = cardInSceneTree
				&& string.Equals(
					cardControl.DisplayedCounterChips,
					expectedChipText,
					StringComparison.Ordinal)
				&& expectedChipText.Contains("HOSTED CREDIT ×3", StringComparison.Ordinal)
				&& expectedChipText.Contains("VIRUS ×2", StringComparison.Ordinal)
				&& expectedChipText.Contains("POWER ×1", StringComparison.Ordinal);

			// Retain the same physical control and update only the typed view
			// value, proving that the displayed cue changes after tree entry while
			// the typed counter chips remain visible.
			cardControl.SetCard(readyCard with { AbilityCue = AbilityCueLabels.Used });
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			usedCue = cardInSceneTree
				&& string.Equals(
					cardControl.DisplayedAbilityCue,
					AbilityCueLabels.Used,
					StringComparison.Ordinal);
			usedChips = cardInSceneTree
				&& string.Equals(
					cardControl.DisplayedCounterChips,
					expectedChipText,
					StringComparison.Ordinal);

			// Keep the issued action typed through the Match presenter, then use
			// the same text shape as Main.RenderActions for the client affordance.
			LegalActionViewModel issuedAbility = CreateSyntheticAbilityAction();
			LegalActionViewModel presentedAbility = ActionsRegionPresenter.Describe(issuedAbility);
			abilityButton = new Button
			{
				Text = $"{presentedAbility.Kind}: {presentedAbility.Description}",
				Alignment = HorizontalAlignment.Left,
			};
			_actions.AddChild(abilityButton);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			abilityAffordance = abilityButton.IsInsideTree()
				&& presentedAbility.Kind == ActionKind.UseAbility
				&& presentedAbility.Parameters is AbilityActionParameters parameters
				&& parameters.AbilityKey == "telework-take"
				&& abilityButton.Text.StartsWith(
					$"{ActionKind.UseAbility}: ",
					StringComparison.Ordinal);

			// The typed endpoint kind is what makes this an RFG event; opaque zone
			// IDs alone must never make the feed guess "Removed from game".
			EngineEvent removedEvent = new(
				1,
				EngineEventKinds.CardMoved,
				new CardMovedEventPayload(
					new CardInstanceId("card:slice43-rfg"),
					new ZoneId("zone:runner-rig"),
					new ZoneId("zone:runner-rfg"),
					ZoneKind.RunnerRig,
					ZoneKind.RunnerRemovedFromGame));
			string removedLine = EventFeedRegionPresenter.Describe(removedEvent);
			AppendEventFeed(removedLine);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			rfgRemoved = removedLine.Contains("Removed from game", StringComparison.Ordinal)
				&& _eventFeed.Text.Contains("Removed from game", StringComparison.Ordinal);

			bool passed = cardInSceneTree
				&& readyCue
				&& readyChips
				&& usedCue
				&& usedChips
				&& abilityAffordance
				&& rfgRemoved;
			GD.Print(
				$"M4_SLICE43_UI {(passed ? "PASS" : "FAIL")} "
				+ $"card_in_scene_tree={FormatSlice43Bool(cardInSceneTree)} "
				+ $"ready_cue={FormatSlice43Bool(readyCue)} "
				+ $"ready_chips={FormatSlice43Bool(readyChips)} "
				+ $"used_cue={FormatSlice43Bool(usedCue)} "
				+ $"used_chips={FormatSlice43Bool(usedChips)} "
				+ $"ability_affordance={FormatSlice43Bool(abilityAffordance)} "
				+ $"rfg_removed={FormatSlice43Bool(rfgRemoved)}");
			cardControl.QueueFree();
			abilityButton.QueueFree();
			QuitCleanly(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			string error = exception.Message.Replace('\n', ' ').Replace('\r', ' ');
			GD.Print(
				$"M4_SLICE43_UI FAIL card_in_scene_tree={FormatSlice43Bool(cardInSceneTree)} "
				+ $"ready_cue={FormatSlice43Bool(readyCue)} "
				+ $"ready_chips={FormatSlice43Bool(readyChips)} "
				+ $"used_cue={FormatSlice43Bool(usedCue)} "
				+ $"used_chips={FormatSlice43Bool(usedChips)} "
				+ $"ability_affordance={FormatSlice43Bool(abilityAffordance)} "
				+ $"rfg_removed={FormatSlice43Bool(rfgRemoved)} error={error}");
			if (cardControl is not null && IsInstanceValid(cardControl))
			{
				cardControl.QueueFree();
			}

			if (abilityButton is not null && IsInstanceValid(abilityButton))
			{
				abilityButton.QueueFree();
			}

			QuitCleanly(1);
		}
	}

	private static string FormatSlice43Bool(bool value) => value ? "true" : "false";

	private static CardViewModel CreateSyntheticSlice43Card() => new(
		"card:slice43-ui",
		"synthetic:slice43-ui",
		"Synthetic Ability Card",
		"Resource",
		true,
		false,
		0,
		null,
		null,
		null,
		null,
		Array.Empty<string>(),
		string.Empty)
	{
		AbilityCue = AbilityCueLabels.Ready,
		TypedCounters =
		[
			new CardCounterViewModel(
				"credit",
				CardCounterPurpose.HostedCredit,
				3,
				HostedCreditSpendRestriction.CardEffectOnly),
			new CardCounterViewModel(
				"virus",
				CardCounterPurpose.Virus,
				2,
				null),
			new CardCounterViewModel(
				"power",
				CardCounterPurpose.Generic,
				1,
				null),
		],
	};

	private static LegalActionViewModel CreateSyntheticAbilityAction()
	{
		AbilityActionParameters parameters = new(
			new CardInstanceId("card:slice43-ui"),
			new CardDefinitionId("30027"),
			"telework-take",
			BehaviorAbilityTiming.ActionPhase,
			BehaviorAbilityActivation.ClickAction,
			[new BehaviorCost(BehaviorCostKind.Clicks)],
			Array.Empty<CardInstanceId>(),
			new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 0),
			new string('a', 64),
			new string('b', 64),
			new ZoneId("zone:runner-rig"),
			ZoneKind.RunnerRig);
		return new LegalActionViewModel(
			new ActionId("action:slice43-ui-ability"),
			ActionKind.UseAbility,
			"Use issued ability",
			parameters);
	}
}
