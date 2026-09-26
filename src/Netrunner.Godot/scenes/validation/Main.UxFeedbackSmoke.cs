using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckHandConditionFeedback()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			await LabPointer(new Vector2(20, 20));
			_labScenario = 15;
			ResetInteractionLab();
			MatchViewModel before = _currentSnapshot!.View;
			CardViewModel plain = before.RunnerHand.VisibleCards[^1] with { Title = "Practice Carmen", Cost = 5, Type = "Program" };
			CostQuotePresentationViewModel cost = new("Install", 5, 3, [new(BehaviorCostModifierKind.RunnerInstallCreditCost, -2,
				"carmen-successful-run-install", null, null)]);
			CardViewModel ready = plain with { HandCondition = new("successful run this turn", cost) };
			MatchViewModel view = before with { RunnerHand = new(ZoneKind.RunnerGrip, 1, [plain]), LegalActions = [] };
			PublishLabView(view);
			await TableFrames(3);
			TableCardControl control = _gameTable.VisibleCards[plain.Id];
			RequireInteraction(!control.HasMetHandCondition, "An unmet hand condition glows.");
			PublishLabView(view with { RunnerHand = new(ZoneKind.RunnerGrip, 1, [ready]), Runner = before.Runner with { Credits = 0 } });
			await TableFrames(4);
			RequireInteraction(ReferenceEquals(control, _gameTable.VisibleCards[plain.Id]) && control.HasMetHandCondition
				&& !control.IsActionableSource && control.GetNode<Control>("HandConditionBadge").Visible,
				$"A fulfilled condition did not update the existing hand card independently of affordability: same={ReferenceEquals(control, _gameTable.VisibleCards[plain.Id])}, condition={control.HasMetHandCondition}, actionable={control.IsActionableSource}, badge={control.GetNode<Control>("HandConditionBadge").Visible}.");
			GameSymbolLabel badge = control.GetNode<GameSymbolLabel>("HandConditionBadge/Cost");
			RequireInteraction(badge.SymbolText == "3 [credit]" && badge.RenderedSymbolCount == 1,
				"Discount badge did not show the actual credit cost.");
			await LabPointer(control.GetGlobalTransform() * new Vector2(20, 45));
			await TableFrames(3);
			RequireInteraction(CardHoverPreviewVisible && _cardHoverPreviewStats!.SymbolText.Contains("Install now 3", StringComparison.Ordinal)
				&& _cardHoverPreviewStatus!.Text.Contains("successful run this turn", StringComparison.Ordinal),
				"Hover preview omitted the cost or fulfilled condition.");
			if (OS.GetCmdlineUserArgs().Contains("--ux-feedback-screenshots")) await SaveInteractionEvidence($"hand-condition-{size.X}");
			PublishLabView(view);
			await TableFrames(3);
			RequireInteraction(!control.HasMetHandCondition && !control.GetNode<Control>("HandConditionBadge").Visible,
				"An expired condition retained its glow or discounted cost.");
		}
		GD.Print("HAND_CONDITIONS PASS resolutions=2 discount=true unaffordable=true live_update=true expiry=true symbols=true hover=true");
	}

	private async Task CheckOpponentPlayback()
	{
		long originalSequence = _renderedSequence;
		foreach (PlayerSide viewer in new[] { PlayerSide.Runner, PlayerSide.Corp })
		{
			_labScenario = viewer == PlayerSide.Runner ? 15 : 16;
			_reducedMotion.ButtonPressed = true;
			ResetInteractionLab();
			await TableFrames(4);
			_gameTable.StopMotion();
			_reducedMotion.ButtonPressed = false;
			bool corp = viewer == PlayerSide.Runner;
			PlayerSide opponent = corp ? PlayerSide.Corp : PlayerSide.Runner;
			MatchSnapshot baseline = _currentSnapshot!;
			MatchViewModel before = baseline.View;
			MatchViewModel drawn = before with { Decision = null, LegalActions = [] };
			drawn = corp ? drawn with
			{
				CorpHand = before.CorpHand with { Count = before.CorpHand.Count + 1 },
				CorpDeck = before.CorpDeck with { Count = before.CorpDeck.Count - 1 }
			}
				: drawn with
				{
					RunnerHand = before.RunnerHand with { Count = before.RunnerHand.Count + 1 },
					RunnerDeck = before.RunnerDeck with { Count = before.RunnerDeck.Count - 1 }
				};
			long first = _renderedSequence + 1;
			MatchHistoryEntry history = new($"playback-{viewer}-draw", first, 3, opponent, $"{opponent} draws a card.", []);
			QueueSnapshot(baseline with { Sequence = first, ActionCount = first, View = drawn, IsOpponentThinking = true, History = [history] });
			// A fast agent can publish many bookkeeping snapshots in one frame.
			for (int index = 1; index <= 30; index++)
				QueueSnapshot(baseline with { Sequence = first + index, ActionCount = first, View = drawn, IsOpponentThinking = true, History = [] });
			MatchViewModel gained = corp ? drawn with { Corp = drawn.Corp with { Credits = drawn.Corp.Credits + 1 } }
				: drawn with { Runner = drawn.Runner with { Credits = drawn.Runner.Credits + 1 } };
			MatchHistoryEntry gain = new($"playback-{viewer}-credit", first + 31, 3, opponent, $"{opponent} gains 1 credit.", []);
			QueueSnapshot(baseline with { Sequence = first + 31, ActionCount = first + 31, View = gained, IsOpponentThinking = true, History = [gain] });
			await TableFrames(4);
			RequireInteraction(_renderedSequence == first && !_skipAnimation && _tableAwaitingSettlement && _gameTable.ActiveMotionCount > 0,
				$"A burst of opponent publications skipped or interrupted the draw animation: shown={_renderedSequence}, expected={first}, skip={_skipAnimation}, settling={_tableAwaitingSettlement}, motions={_gameTable.ActiveMotionCount}.");
			RequireInteraction(_tableLastAction.Text == history.Summary && _tableHistory.Entries.All(entry => entry.Key != gain.Key),
				"Playback exposed a later action before displaying its board state.");
			TableCardControl[] flights = _gameTable.GetChildren().OfType<TableCardControl>().ToArray();
			RequireInteraction(flights.Length > 0 && flights.All(card => card.Card is null && card.BackSide == opponent),
				"An opponent draw exposed a hidden identity or used the wrong card back.");
			if (OS.GetCmdlineUserArgs().Contains("--ux-feedback-screenshots")) await SaveInteractionEvidence($"opponent-draw-{opponent.ToString().ToLowerInvariant()}");
			// Clearing the reading delay must still leave an active flight undisturbed.
			_tablePlaybackWait = 0;
			await TableFrames(2);
			RequireInteraction(_renderedSequence == first && _gameTable.ActiveMotionCount > 0, "Bookkeeping interrupted an active flight.");
			for (double elapsed = 0; elapsed < 5 && _renderedSequence < first + 31; elapsed += GetProcessDeltaTime()) await TableFrames(1);
			RequireInteraction(_renderedSequence == first + 31 && _tableLastAction.Text == gain.Summary,
				"Opponent action playback did not advance in order.");
			await TableFrames(2);
			RequireInteraction(_gameTable.ActiveMotionCount > 0, "An opponent credit action had no visual feedback.");
			_skipAnimations.EmitSignal(BaseButton.SignalName.Pressed);
			QueueSnapshot(baseline with { Sequence = first + 32, History = [] });
			await TableFrames(3);
			RequireInteraction(_renderedSequence == first + 32 && _gameTable.ActiveMotionCount == 0
				&& !_tableAwaitingSettlement && _tableHistory.Entries.Count == 2, "Skip lost history or left the board blocked.");
			_reducedMotion.ButtonPressed = true;
		}
		_renderedSequence = originalSequence;
		ResetInteractionLab();
		GD.Print("OPPONENT_PLAYBACK PASS seats=2 burst=true ordered=true hidden_draw=true resource_feedback=true complete_motion=true skip=true history=true");
	}
}
