using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task CheckDrawMotion()
	{
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (bool corp in new[] { false, true })
			{
				_labScenario = corp ? 16 : 15;
				_labScenarios!.Select(_labScenario);
				_reducedMotion.ButtonPressed = true;
				ResetInteractionLab();
				await TableFrames(4);
				_reducedMotion.ButtonPressed = false;
				await LabPointer(new Vector2(20, 20));
				BoardActionButton deck = corp
					? FindDescendants<BoardActionButton>(_gameTable).Single(button => button.Name == "DrawPile")
					: _gameTable.GetNode<BoardActionButton>("Surface/Margins/Layout/RunnerField/Content/Deck");
				Vector2 origin = deck.GetGlobalRect().GetCenter();
				// The normal click path publishes an updated observation into the same animation path.
				deck.EmitSignal(BaseButton.SignalName.Pressed);
				TableCardControl arrival = _gameTable.VisibleCards[$"lab-drawn-{_labRevision}"];
				TableCardControl flight = DrawFlights().Single();
				RequireInteraction(_labSubmissionCount == 1 && _gameTable.DrawMotionCount == 1 && !arrival.Visible,
					"A drawn card appeared in hand before its flight completed.");
				RequireInteraction(flight.Card is null && flight.BackSide == (corp ? PlayerSide.Corp : PlayerSide.Runner)
					&& (flight.GetGlobalTransform() * (flight.Size * 0.5f)).DistanceTo(origin) < 1,
					"Draw did not start as the correct card back at the deck.");
				await HandAnimationTime(0.30);
				RequireInteraction(IsInstanceValid(flight) && flight.Card?.Id == arrival.CardId && !arrival.Visible
					&& (flight.GetGlobalTransform() * (flight.Size * 0.5f)).DistanceTo(origin) > 40,
					"Draw did not travel and reveal its known face before landing.");
				RequireInteraction(!deck.ActionAvailable, "Draw stayed clickable during its animation.");
				if (OS.GetCmdlineUserArgs().Contains("--m4-12-screenshots") && size.X == 1920)
					await SaveRefinementEvidence(corp ? "draw-corp-1920" : "draw-runner-1920");
				await HandAnimationTime(0.5);
				RequireInteraction(_gameTable.DrawMotionCount == 0 && !DrawFlights().Any() && arrival.Visible
					&& _gameTable.Hand.ContainsCard(arrival), "Draw did not settle into the hand fan.");
				RenderSnapshot(_currentSnapshot!);
				await TableFrames(2);
				RequireInteraction(_gameTable.DrawMotionCount == 0, "An unchanged snapshot replayed the draw.");

				// Multi-draw observations get one flight per admitted card; skipping reveals every arrival.
				MatchViewModel current = _currentSnapshot!.View;
				MatchViewModel batch = AddDrawnCards(current, corp, "batch-one", "batch-two");
				PublishLabView(batch);
				RequireInteraction(_gameTable.DrawMotionCount == 2 && DrawFlights().Count() == 2,
					"A multi-card draw did not create separate flights.");
				_gameTable.StopMotion();
				await TableFrames(2);
				RequireInteraction(_gameTable.DrawMotionCount == 0 && !DrawFlights().Any()
					&& _gameTable.VisibleCards["batch-one"].Visible && _gameTable.VisibleCards["batch-two"].Visible,
					"Skipping a draw left a ghost or invisible hand card.");
				PublishLabView(AddDrawnCards(batch, corp, "interrupted"));
				RequireInteraction(_gameTable.DrawMotionCount == 1, "Interruption fixture did not begin a draw.");
				_reducedMotion.ButtonPressed = true;
				await TableFrames(2);
				RequireInteraction(_gameTable.DrawMotionCount == 0 && _gameTable.VisibleCards["interrupted"].Visible,
					"Enabling Reduced motion failed to finish an in-flight draw.");
				PublishLabView(AddDrawnCards(_currentSnapshot!.View, corp, "reduced"));
				await TableFrames(2);
				RequireInteraction(!DrawFlights().Any() && _gameTable.VisibleCards["reduced"].Visible,
					"Reduced motion animated a newly drawn card.");

				// No deck loss: a card returning from discard must not be depicted as a draw.
				_reducedMotion.ButtonPressed = false;
				MatchViewModel recovered = AddDrawnCards(_currentSnapshot.View, corp, "recovered");
				recovered = corp ? recovered with { CorpDeck = _currentSnapshot.View.CorpDeck }
					: recovered with { RunnerDeck = _currentSnapshot.View.RunnerDeck };
				PublishLabView(recovered);
				RequireInteraction(_gameTable.DrawMotionCount == 0 && _gameTable.VisibleCards["recovered"].Visible,
					"A hand addition without a deck loss was animated as a draw.");
				MatchViewModel hiddenDraw = corp ? recovered with
				{
					RunnerDeck = recovered.RunnerDeck with { Count = recovered.RunnerDeck.Count - 1 },
					RunnerHand = recovered.RunnerHand with { Count = recovered.RunnerHand.Count + 1 },
				} : recovered with
				{
					CorpDeck = recovered.CorpDeck with { Count = recovered.CorpDeck.Count - 1 },
					CorpHand = recovered.CorpHand with { Count = recovered.CorpHand.Count + 1 },
				};
				PublishLabView(hiddenDraw);
				RequireInteraction(_gameTable.DrawMotionCount == 0, "An opponent draw created a local face-bearing flight.");
			}
		}
		GD.Print("M412_DRAW_MOTION PASS seats=2 resolutions=2 origin=true back_to_face=true arrival=true batch=true skip=true reduced_motion=true hidden_safe=true");
	}

	private IEnumerable<TableCardControl> DrawFlights() => _gameTable.GetChildren().OfType<TableCardControl>()
		.Where(card => card.Name.ToString().StartsWith("DrawFlight", StringComparison.Ordinal));

	private static MatchViewModel AddDrawnCards(MatchViewModel view, bool corp, params string[] ids)
	{
		ZoneViewModel hand = corp ? view.CorpHand : view.RunnerHand;
		CardViewModel[] cards = ids.Select(id => UiPreviewSamples.Known(id, corp ? "Operation" : "Event", "Practice drawn card")).ToArray();
		hand = hand with { Count = hand.Count + cards.Length, VisibleCards = [.. hand.VisibleCards, .. cards] };
		return corp ? view with { CorpHand = hand, CorpDeck = view.CorpDeck with { Count = view.CorpDeck.Count - cards.Length } }
			: view with { RunnerHand = hand, RunnerDeck = view.RunnerDeck with { Count = view.RunnerDeck.Count - cards.Length } };
	}
}
