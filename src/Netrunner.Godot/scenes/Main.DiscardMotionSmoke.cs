using Godot;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckDiscardMotion()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (bool corp in new[] { false, true })
			{
				_labScenario = corp ? 18 : 17;
				foreach (bool rightmost in new[] { false, true })
				{
					await ResetDiscardMotionFixture();
					foreach (int index in rightmost ? new[] { 6, 5 } : new[] { 0, 1 })
					{
						string id = $"lab-discard-{index}";
						await BeginDiscardDrag(id);
						Vector2 drop = _gameTable.GetDropRect(corp ? "CorpArchives" : "RunnerHeap", null)!.Value.GetCenter();
						await LabPointer(drop);
						TableCardControl card = _gameTable.VisibleCards[id];
						Vector2 origin = card.GetGlobalTransform() * (card.Size * 0.5f);
						LabButton(drop, false);
						await TableFrames(2);
						TableCardControl flight = DiscardFlights().Single();
						RequireInteraction(_gameTable.DiscardMotionCount == 1 && _gameTable.ActiveMotionCount == 1 && flight.CardId == id,
							$"Discard {id} produced missing or duplicate animations (rightmost={rightmost}).");
						Vector2 start = flight.GetGlobalTransform() * (flight.Size * 0.5f);
						RequireInteraction(start.DistanceTo(origin) < origin.DistanceTo(drop) * 0.5f,
							"Discard animation started outside the hand, such as the Corp header.");
						if (_labSubmissionCount == 2 && !rightmost)
							RequireInteraction(!_gameTable.VisibleCards[id].Visible, "The arriving pile face duplicated its discard flight.");
						await HandAnimationTime(0.13);
						RequireInteraction(IsInstanceValid(flight)
							&& (flight.GetGlobalTransform() * (flight.Size * 0.5f)).DistanceTo(drop) < start.DistanceTo(drop),
							"Discard flight did not travel toward the correct pile.");
						if (!corp && rightmost && size.X == 1920 && OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots"))
							await SaveRefinementEvidence($"discard-motion-{_labSubmissionCount}-1920");
						await HandAnimationTime(0.4);
						RequireInteraction(_gameTable.DiscardMotionCount == 0 && _gameTable.ActiveMotionCount == 0 && !DiscardFlights().Any(),
							"Discard flight did not finish cleanly.");
						RenderSnapshot(_currentSnapshot!);
						await TableFrames(2);
						RequireInteraction(_gameTable.ActiveMotionCount == 0, "An unchanged snapshot replayed a discard.");
					}
				}
				// Skip a pending selection, then enable Reduced motion while the final
				// pile face is hidden by its flight. Both must release every visual.
				await ResetDiscardMotionFixture();
				SubmitInteractionLab(_currentSnapshot!.View.Decision!, _currentSnapshot.View.LegalActions[0]);
				await TableFrames(2);
				RequireInteraction(_gameTable.DiscardMotionCount == 1, "Menu discard did not animate its first selection.");
				_gameTable.StopMotion();
				await TableFrames(2);
				RequireInteraction(_gameTable.DiscardMotionCount == 0 && !DiscardFlights().Any(), "Skip left a pending discard ghost.");
				SubmitInteractionLab(_currentSnapshot.View.Decision!, _currentSnapshot.View.LegalActions[0]);
				await TableFrames(2);
				RequireInteraction(_gameTable.DiscardMotionCount == 1 && !_gameTable.VisibleCards["lab-discard-1"].Visible,
					"Final discard did not own its arriving pile face.");
				_reducedMotion.ButtonPressed = true;
				await TableFrames(2);
				RequireInteraction(_gameTable.DiscardMotionCount == 0 && _gameTable.VisibleCards["lab-discard-1"].Visible,
					"Reduced motion left a discard face hidden or a flight alive.");

				// Taking the top discard back into hand exposes the older pile face;
				// neither a second discard nor a fictitious Corp-origin flight occurs.
				_reducedMotion.ButtonPressed = false;
				MatchViewModel complete = _currentSnapshot.View;
				CardViewModel recovered = (corp ? complete.CorpDiscard : complete.RunnerHeap)[^1];
				ZoneViewModel hand = corp ? complete.CorpHand : complete.RunnerHand;
				hand = hand with { Count = hand.Count + 1, VisibleCards = [.. hand.VisibleCards, recovered] };
				PublishLabView(corp ? complete with { CorpHand = hand, CorpDiscard = [complete.CorpDiscard[0]], CorpDiscardCount = 1 }
					: complete with { RunnerHand = hand, RunnerHeap = [complete.RunnerHeap[0]] });
				await TableFrames(3);
				RequireInteraction(_gameTable.ActiveMotionCount == 0 && _gameTable.VisibleCards["lab-discard-0"].Visible,
					"Exposing an older pile face invented another card movement.");
			}
		}
		GD.Print("M412_DISCARD_MOTION PASS seats=2 resolutions=2 leftmost=true rightmost=true first_selection=true single_flight=true pile_top=true skip=true reduced_motion=true");
	}

	private async Task ResetDiscardMotionFixture()
	{
		_reducedMotion.ButtonPressed = true;
		ResetInteractionLab();
		await TableFrames(4);
		_gameTable.StopMotion();
		_reducedMotion.ButtonPressed = false;
		await TableFrames(2);
	}

	private IEnumerable<TableCardControl> DiscardFlights() => _gameTable.GetChildren().OfType<TableCardControl>()
		.Where(card => card.Name.ToString().StartsWith("DiscardFlight", StringComparison.Ordinal));
}
