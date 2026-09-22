using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildCorpRez(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "corp-rez-01":
                board.CorpCredits = 3;
                board.Add("ice", "30072", ZoneKind.ServerIce).Add("agenda", "30067", ZoneKind.ServerRoot);
                return ApproachedIce();
            case "corp-rez-02":
                board.CorpCredits = 3;
                board.RunnerPoints = 5;
                board.Add("ice", "30072", ZoneKind.ServerIce).Add("agenda", "30067", ZoneKind.ServerRoot)
                    .Add("unrelated", "30037", ZoneKind.ServerRoot, "economy");
                return TacticalFixture.FirstChoice(metadata, board, board.Paid(PlayerSide.Runner, PlayerSide.Corp),
                    action => action.Kind == ActionKind.PassPriority);
            case "corp-rez-03":
                board.CorpCredits = 8;
                board.Add("ice", "30072", ZoneKind.ServerIce, rezzed: true).Add("campaign", "30037", ZoneKind.ServerRoot);
                TacticalFixture campaign = TacticalFixture.FirstChoice(metadata, board, board.Paid(PlayerSide.Corp, PlayerSide.Corp),
                    action => action.Parameters is RezCardParameters rez && rez.CardId == board.Card("campaign"));
                return campaign with
                {
                    Success = trace => campaign.Success(trace) && trace.State.GetCard(board.Card("campaign")).Rezzed
                    && trace.State.RulesState!.Counters.Any(counter => counter.CardId == board.Card("campaign") && counter.Count == 9)
                };
            case "corp-rez-04":
                board.CorpCredits = 3;
                board.RunnerCredits = 0;
                board.Add("ice", "30072", ZoneKind.ServerIce).Add("agenda", "30067", ZoneKind.ServerRoot)
                    .Add("upgrade", "30042", ZoneKind.ServerRoot);
                return ApproachedIce();
            case "corp-rez-05":
                board.Add("void", "30050", ZoneKind.ServerRoot, rezzed: true)
                    .Add("campaign", "30037", ZoneKind.ServerRoot, rezzed: true).Counter("campaign", "credit", 3, CardCounterPurpose.HostedCredit)
                    .Add("future-economy", "30075", ZoneKind.CorpHand).Add("future-defense", "30072", ZoneKind.CorpHand);
                GameState choice = board.Until(board.Run("remote"), state => state.PendingDecision?.Kind == DecisionKind.BehaviorChoice,
                    TriggerOrPass);
                TacticalFixture decline = TacticalFixture.FirstChoice(metadata, board, choice,
                    action => action.Parameters is BehaviorChoiceParameters mode && mode.OptionKeys.SequenceEqual(["decline"]));
                return decline with
                {
                    Success = trace => decline.Success(trace) && trace.Player(PlayerSide.Corp).Credits == board.CorpCredits
                    && trace.State.GetZone(ZoneKind.CorpHand).CardIds.Count == 2
                };
            default: throw new NotSupportedException(metadata.Id);
        }

        TacticalFixture ApproachedIce()
        {
            GameState approach = board.Until(board.Run("remote"), state => state.PendingDecision!.LegalActions.Any(IsTargetRez));
            TacticalFixture fixture = TacticalFixture.FirstChoice(metadata, board, approach,
                IsTargetRez);
            return fixture with { Success = trace => fixture.Success(trace) && trace.State.GetCard(board.Card("ice")).Rezzed };

            bool IsTargetRez(LegalAction action) => action.Parameters is RezCardParameters rez && rez.CardId == board.Card("ice")
                || action.Kind == ActionKind.RezIce && action.Parameters is RunIceParameters ice && ice.IceId == board.Card("ice");
        }
    }
}
