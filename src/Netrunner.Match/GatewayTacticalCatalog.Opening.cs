using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildOpening(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board();
        string id = metadata.Id;
        if (id is "corp-opening-01" or "corp-opening-02" or "runner-opening-01" or "runner-opening-02")
        {
            string[] corp = id == "corp-opening-01" ? ["30036", "30044", "30052", "30067", "30069"]
                : ["30075", "30072", "30037", "30067", "30074"];
            string[] runner = id == "runner-opening-01" ? ["30031", "30031", "30023", "30003", "30003"]
                : ["30030", "30006", "30005", "30027", "30028"];
            for (int index = 0; index < 5; index++)
            {
                board.Add($"corp-hand-{index}", corp[index], ZoneKind.CorpHand);
                board.Add($"runner-hand-{index}", runner[index], ZoneKind.RunnerGrip);
            }
            board.FillDecks();
            int expected = id.EndsWith("01", StringComparison.Ordinal) ? 1 : 0;
            return TacticalFixture.FirstChoice(metadata, board, board.Opening(metadata.Side),
                action => action.Parameters is NumberChoiceParameters number && number.Value == expected);
        }
        board.FillDecks();
        switch (id)
        {
            case "corp-opening-03":
                board.Add("economy", "30075", ZoneKind.CorpHand);
                return Economy(board.Action(PlayerSide.Corp), "economy", 4);
            case "corp-opening-04":
                board.CorpCredits = 3;
                board.Add("reserve-ice", "30072", ZoneKind.ServerIce);
                board.Add("agenda", "30067", ZoneKind.ServerRoot, advancement: 1);
                board.Add("optional-campaign", "30037", ZoneKind.ServerRoot, "economy");
                return TacticalFixture.FirstChoice(metadata, board, board.Paid(PlayerSide.Corp, PlayerSide.Corp),
                    action => action.Kind == ActionKind.PassPriority);
            case "corp-opening-05":
                board.CorpCredits = 15;
                return TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Corp), action => action.Kind == ActionKind.DrawCard);
            case "runner-opening-03":
                board.Add("economy", "30030", ZoneKind.RunnerGrip);
                return Economy(board.Action(PlayerSide.Runner), "economy", 4);
            case "runner-opening-04":
                board.RunnerCredits = 9;
                board.Add("barrier", "30063", ZoneKind.ServerIce, "hq", rezzed: true);
                board.Add("code-gate", "30074", ZoneKind.ServerIce, "rd", rezzed: true);
                board.Add("sentry", "30062", ZoneKind.ServerIce, "archives", rezzed: true);
                return TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Runner), action => action.Kind == ActionKind.DrawCard);
            case "runner-opening-05":
                board.RunnerCredits = 1;
                board.RunnerClicks = 1;
                board.Add("commission", "30020", ZoneKind.RunnerGrip);
                TacticalFixture fixture = Economy(board.Action(PlayerSide.Runner), "commission", 4);
                return fixture with { Success = trace => fixture.Success(trace) && trace.Player(PlayerSide.Runner).Clicks == 0 };
            default: throw new NotSupportedException(id);
        }

        TacticalFixture Economy(GameState state, string role, int netGain)
        {
            int credits = state.Players.Single(player => player.Side == metadata.Side).Credits;
            TacticalFixture choice = TacticalFixture.FirstChoice(metadata, board, state,
                action => action.Parameters is PlayCardParameters play && play.CardId == board.Card(role));
            return choice with { Success = trace => choice.Success(trace) && trace.Player(metadata.Side).Credits == credits + netGain };
        }
    }
}
