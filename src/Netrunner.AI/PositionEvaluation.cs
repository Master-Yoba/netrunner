using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Static value of a position for the observing side, from its own
/// observation (AI plan A3). It scores what the A2 heuristics already weigh:
/// the score race, money, cards, the Corp's ICE and advancing agendas, the
/// Runner's rig, and tags and damage. The weights are named so A5 can tune
/// them; they are relative, not calibrated to a win probability.
/// </summary>
internal static class PositionEvaluation
{
    public const int Win = 1_000_000;
    public const int AgendaPoint = 1200;
    public const int IceInstalled = 180;
    public const int IceRezzed = 80;
    public const int UnprotectedAgendaPerPoint = 400;
    public const int AdvancementProgressPerPoint = 420;
    public const int TagForCorp = 150;
    public const int TagForRunner = 250;
    public const int CoreDamage = 300;
    public const int ShortGrip = 200;
    public const int HandCardCap = 300;
    public const int OpponentCreditShare = 2;

    public static long Score(AgentFeatures features, ICardEvaluationCatalog catalog, OfflineAgentConfiguration configuration)
    {
        AgentOutcome outcome = features.Board.Outcome;
        if (outcome.Kind != GameOutcomeKind.Ongoing) return outcome.Winner == features.Side ? Win : -Win;
        var context = new HeuristicContext(features, catalog, configuration, generation: 3);
        long score = (features.Self.AgendaPoints - features.Opponent.AgendaPoints) * (long)AgendaPoint;
        score += Money(features.Self.Credits) - Money(features.Opponent.Credits) / OpponentCreditShare;
        score += features.Hand.Sum(card => Math.Min(HandCardCap, context.CardValue(card))) / 2;
        return score + (features.Side == PlayerSide.Corp ? Corp(features, context) : Runner(features, context));
    }

    /// <summary>The hand evaluation for A5's comparison, with the default Standard profile of the observing side.</summary>
    public static long ScoreForFitting(AgentFeatures features, ICardEvaluationCatalog catalog) =>
        Score(features, catalog, features.Side == PlayerSide.Corp
            ? CorpPolicyProfiles.Create("standard", 0, CorpHeuristicPolicy.LatestHeuristic)
            : RunnerPolicyProfiles.Create("standard", 0, RunnerHeuristicPolicy.LatestHeuristic));

    /// <summary>Credits are worth less the more there are.</summary>
    private static long Money(int credits)
    {
        long value = 0;
        for (int credit = 0; credit < Math.Max(0, credits); credit++)
            value += credit < 5 ? 110 : credit < 10 ? 80 : credit < 15 ? 50 : 25;
        return value;
    }

    private static long Corp(AgentFeatures features, HeuristicContext context)
    {
        long score = 0;
        foreach (AgentServer server in features.Board.Servers)
        {
            score += server.Ice.Length * (long)IceInstalled
                + server.Ice.Count(card => card?.Rezzed == true) * (long)IceRezzed;
            foreach (AgentCard? card in server.Root)
            {
                if (card is null) continue;
                CardEvaluationDescriptor? definition = context.Definition(card);
                if (definition?.Agenda is { } agenda)
                {
                    int points = definition.Printed.AgendaPoints;
                    int requirement = Math.Max(1, definition.Printed.AdvancementRequirement);
                    score += points * (long)AdvancementProgressPerPoint * Math.Min(requirement, card.AdvancementCounters ?? 0) / requirement;
                    if (server.Ice.Length == 0) score -= points * (long)UnprotectedAgendaPerPoint;
                }
                else if (card.Rezzed == true)
                {
                    score += context.CardValue(card, installed: true);
                }
            }
        }
        return score + (features.Opponent.Tags ?? 0) * (long)TagForCorp;
    }

    private static long Runner(AgentFeatures features, HeuristicContext context)
    {
        long score = features.Board.RunnerRig.Sum(card => (long)context.CardValue(card, installed: true));
        score -= (features.Self.Tags ?? 0) * (long)TagForRunner;
        score -= (features.Self.CoreDamage ?? 0) * (long)CoreDamage;
        if (features.Hand.Length < 2) score -= ShortGrip;
        return score;
    }
}
