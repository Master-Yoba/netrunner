using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildCorpInformation(TacticalCaseMetadata metadata)
    {
        if (metadata.Id is "corp-information-01" or "corp-information-02")
        {
            (TacticalBoard board, GameState state) = Twin(false);
            (_, GameState hidden) = Twin(true);
            return TacticalFixture.FirstChoice(metadata, board, state, _ => true, requireAlternative: false) with { HiddenTwin = hidden };
        }
        TacticalBoard position = Board().FillDecks();
        switch (metadata.Id)
        {
            case "corp-information-03":
                position.Add("agenda", "30044", ZoneKind.ServerRoot, advancement: _runtime.GetSpec(new("30044")).AdvancementRequirement)
                    .Add("seen", "30075", ZoneKind.CorpDiscard, faceUp: true);
                GameState root = position.Until(Score(position), state => state.PendingDecision!.LegalActions.Any(action =>
                    action.Parameters is BehaviorChoiceParameters { ChoiceKey: "longevity-archives" }),
                    state => state.PendingDecision!.LegalActions.FirstOrDefault(action =>
                        action.Parameters is BehaviorChoiceParameters { CardIds.Count: 0, FinalizeSelection: true }) ?? TriggerOrPass(state));
                CardInstanceId seen = position.Card("seen");
                return TargetChain(metadata, position, root, [seen], trace =>
                    trace.State.GetZone(trace.State.GetCard(seen).ZoneId).Kind == ZoneKind.CorpDeck
                    && trace.Knowledge.VisibleCards.All(card => card.Id != seen.Value)
                    && trace.Knowledge.Reveals.Any(card => card.DefinitionId == "30075")
                    && (trace.PolicyCheckpoint is null || trace.PolicyCheckpoint.Knowledge.VisibleCards.All(card => card.Id != seen.Value)));
            case "corp-information-04":
                position.CorpCredits = 3;
                position.Add("ansel", "30038", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("useful", "30072", ZoneKind.CorpHand).Add("too-expensive", "30063", ZoneKind.CorpHand);
                GameState install = position.Until(position.Run("hq"), state => state.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice);
                TacticalFixture useful = TacticalFixture.FirstChoice(metadata, position, install,
                    action => action.Parameters is IceSubroutineChoiceParameters choice && choice.Choice == IceSubroutineChoiceKind.InstallCard
                        && choice.CardId == position.Card("useful") && choice.ServerId == position.Server("rd"));
                return useful with
                {
                    Success = trace => useful.Success(trace)
                    && trace.State.GetCard(position.Card("useful")).ZoneId == trace.State.GetServer(position.Server("rd")).IceZoneId
                    && trace.Player(PlayerSide.Corp).Credits == 3
                };
            case "corp-information-05":
                position.CorpCredits = 0;
                position.RunnerCredits = 0;
                position.RunnerPoints = 5;
                position.Add("bran", "30039", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("decoy", "30063", ZoneKind.CorpHand).Add("agenda-one", "30067", ZoneKind.CorpHand)
                    .Add("agenda-two", "30069", ZoneKind.CorpHand);
                // Both end-the-run subroutines are already broken. The only
                // eligible inward install removes HQ's non-agenda decoy, and
                // Corp cannot rez it. Declining avoids making the winning HQ
                // access certain; this branch never assumes hidden Runner cards.
                GameState optional = position.Until(position.Run("hq"), state => state.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice,
                    state => state.PendingDecision!.LegalActions.FirstOrDefault(action => action.Parameters is RunBioroidAbilityParameters bioroid
                        && bioroid.SubroutineOrdinal is 1 or 2) ?? TacticalBoard.Passive(state));
                TacticalFixture decline = TacticalFixture.FirstChoice(metadata, position, optional,
                    action => action.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.Decline });
                return decline with { Success = trace => decline.Success(trace) && trace.State.GetZone(ZoneKind.CorpHand).CardIds.Contains(position.Card("decoy")) };
            default: throw new NotSupportedException(metadata.Id);
        }

        (TacticalBoard Board, GameState State) Twin(bool changeHidden)
        {
            TacticalBoard board = Board().FillDecks(runner: 0);
            board.Add("economy", "30075", ZoneKind.CorpHand).Add("ice", "30072", ZoneKind.CorpHand);
            if (metadata.Id == "corp-information-01")
            {
                board.Add("hidden-one", changeHidden ? "30006" : "30030", ZoneKind.RunnerGrip)
                    .Add("hidden-two", changeHidden ? "30030" : "30006", ZoneKind.RunnerGrip);
            }
            board.Add("stack-one", "30030", ZoneKind.RunnerStack).Add("stack-two", "30005", ZoneKind.RunnerStack);
            if (metadata.Id == "corp-information-02" && changeHidden) board.Reverse(ZoneKind.RunnerStack);
            return (board, board.Action(PlayerSide.Corp));
        }
    }
}
