using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class GameTableControl
{
	private void PresentOpponentMotion(MatchViewModel? previous, MatchViewModel view)
	{
		if (previous is null || previous.Viewer != view.Viewer) return;
		bool corp = view.Viewer == PlayerSide.Runner;
		PlayerSide side = corp ? PlayerSide.Corp : PlayerSide.Runner;
		PlayerViewModel before = corp ? previous.Corp : previous.Runner;
		PlayerViewModel after = corp ? view.Corp : view.Runner;
		if (before.Credits != after.Credits || before.Clicks != after.Clicks)
			PulseCard((corp ? _corpCredits : _runnerCredits).GetGlobalRect());
		ZoneViewModel oldHand = corp ? previous.CorpHand : previous.RunnerHand;
		ZoneViewModel hand = corp ? view.CorpHand : view.RunnerHand;
		int deckLoss = (corp ? previous.CorpDeck.Count : previous.RunnerDeck.Count) - (corp ? view.CorpDeck.Count : view.RunnerDeck.Count);
		int drawn = Math.Min(deckLoss, hand.Count - oldHand.Count);
		Control? deck = corp ? _zonePiles.GetValueOrDefault("zone:corp-deck") : _runnerDeckPile;
		Control handCaption = corp ? _corpHandCaption : _runnerHandCaption;
		if (drawn > 0 && deck is not null)
		{
			// Public counts only: no opponent card identities are available here.
			for (int index = 0; index < Math.Min(drawn, 5); index++)
			{
				Vector2 offset = new(index * 8, index * 5);
				AnimateCard(null, new Rect2(deck.GlobalPosition + offset, TableCardControl.PortraitCardSize * 0.6f),
					handCaption.GlobalPosition - GlobalPosition + offset, false, side);
			}
		}
		else if (hand.Count < oldHand.Count
			&& (corp ? view.CorpDiscardCount > previous.CorpDiscardCount : view.RunnerHeap.Count > previous.RunnerHeap.Count))
		{
			Control? discard = corp ? _zonePiles.GetValueOrDefault("zone:corp-archives") : _runnerHeapPile;
			if (discard is not null)
				AnimateCard(null, new Rect2(handCaption.GlobalPosition, TableCardControl.PortraitCardSize * 0.6f),
					discard.GlobalPosition - GlobalPosition, false, side);
		}
	}
}
