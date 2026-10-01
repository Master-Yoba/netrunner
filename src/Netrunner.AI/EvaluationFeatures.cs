using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// The feature vector the fitted evaluation (A5) reads from a position, from the
/// observing side's own view: the score race, money, cards, the board, tags and
/// damage, decks, the turn, and the whole-run model's view of the key servers
/// (A4a). Every value is public or the observer's own; a Corp view prices runs
/// with its own ICE known, a Runner view with unrezzed ICE unknown.
/// </summary>
public static class EvaluationFeatures
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "agenda-self", "agenda-opponent", "near-win-self", "near-win-opponent",
        "money-self", "money-opponent", "hand-self", "hand-opponent",
        "ice-installed", "ice-rezzed", "remotes", "board-advancement", "own-agenda-progress",
        "breaker-types", "rig-value", "tags", "core-damage",
        "hq-open", "hq-cost", "rd-open", "rd-cost", "remote-open", "remote-cost",
        "corp-deck", "runner-stack", "turn", "observer-active",
    ];

    /// <summary>
    /// Feature set 2 (playtest, 28 September): v1's plain count of remotes let
    /// the planner learn that spawning remotes wins, so v2 counts remotes with
    /// something in their root apart from empty ones.
    /// </summary>
    public static IReadOnlyList<string> NamesV2 { get; } =
        [.. Names.Select(name => name == "remotes" ? "occupied-remotes" : name), "empty-remotes"];

    public static IReadOnlyList<string> NamesFor(int version) => version switch
    {
        1 => Names,
        2 or 3 => NamesV2,
        _ => throw new ArgumentOutOfRangeException(nameof(version), "Unknown evaluation feature set."),
    };

    public static double[] Extract(AgentFeatures features, ICardEvaluationCatalog catalog, int version)
    {
        double[] v1 = Extract(features, catalog);
        if (version == 1) return v1;
        if (version is not (2 or 3)) throw new ArgumentOutOfRangeException(nameof(version), "Unknown evaluation feature set.");
        // Feature set 3 (Oddity lints, 1 October): set 2 with its fitted weights, where board advancement
        // counts only counters that matter, on agendas and cards that can be advanced. Set 2 counted any
        // counter in a remote, so the planner played Seamless Launch onto a Nico Campaign.
        if (version == 3)
            v1[Names.ToList().IndexOf("board-advancement")] = features.Board.Servers.Where(server => server.Kind == ServerKind.Remote)
                .SelectMany(server => server.Root).OfType<AgentCard>()
                .Sum(card => !catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor definition)
                    || definition.Agenda is not null || definition.Behavior?.CanBeAdvanced == true ? card.AdvancementCounters ?? 0 : 0);
        AgentServer[] remotes = [.. features.Board.Servers.Where(server => server.Kind == ServerKind.Remote)];
        int occupied = remotes.Count(server => server.Root.Length > 0);
        double[] v2 = [.. v1, remotes.Length - occupied];
        v2[Names.ToList().IndexOf("remotes")] = occupied;
        return v2;
    }

    public static double[] Extract(AgentFeatures features, ICardEvaluationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(catalog);
        var context = new HeuristicContext(features, catalog,
            features.Side == PlayerSide.Corp
                ? CorpPolicyProfiles.Create("standard", 0, CorpHeuristicPolicy.LatestHeuristic)
                : RunnerPolicyProfiles.Create("standard", 0, RunnerHeuristicPolicy.LatestHeuristic),
            generation: 6);
        bool corp = features.Side == PlayerSide.Corp;
        AgentPlayer runner = features.Board.Players.Runner;
        AgentPlayer corpPlayer = features.Board.Players.Corp;
        AgentServer[] servers = [.. features.Board.Servers];
        IceType[] covered = features.Board.RunnerRig.Select(card => context.Definition(card)?.Icebreaker?.InterfaceType)
            .OfType<IceType>().Distinct().ToArray();
        int breakerTypes = covered.Contains(IceType.Any) ? 3 : covered.Length;
        int grip = corp ? features.Board.HiddenZoneCounts.RunnerGrip : features.Hand.Length;
        (double Open, double Cost) Access(AgentServer? server)
        {
            if (server is null) return (1, 0);
            RunForecast forecast = RunModel.Plan(context, server, server.Ice.Length - 1, runner.Credits, 3, grip: grip,
                corpRezBudget: corp ? corpPlayer.Credits : null);
            if (!forecast.CanGetIn) return (0, 10);
            return (forecast.UnknownIce > 0 ? 0.5 : 1,
                Math.Min(10, forecast.Credits + forecast.CreditsLost + forecast.Clicks * 2 + forecast.Tags * 2 + forecast.Damage * 2 + forecast.UnknownIce * 3));
        }
        (double hqOpen, double hqCost) = Access(servers.FirstOrDefault(server => server.Kind == ServerKind.Headquarters));
        (double rdOpen, double rdCost) = Access(servers.FirstOrDefault(server => server.Kind == ServerKind.RAndD));
        AgentServer? bestRemote = servers.Where(server => server.Kind == ServerKind.Remote && server.Root.Any(card => card is not null))
            .OrderByDescending(server => server.Root.Sum(card => card?.AdvancementCounters ?? 0)).FirstOrDefault();
        (double remoteOpen, double remoteCost) = bestRemote is null ? (0, 10) : Access(bestRemote);
        double ownProgress = 0;
        if (corp)
            foreach (AgentCard? card in servers.SelectMany(server => server.Root))
                if (context.Definition(card)?.Agenda is not null && card is not null)
                {
                    CardEvaluationDescriptor definition = context.Definition(card)!;
                    int requirement = Math.Max(1, definition.Printed.AdvancementRequirement);
                    ownProgress += definition.Printed.AgendaPoints * Math.Min(1.0, (card.AdvancementCounters ?? 0) / (double)requirement);
                }
        return
        [
            features.Self.AgendaPoints, features.Opponent.AgendaPoints,
            features.Self.AgendaPoints >= 5 ? 1 : 0, features.Opponent.AgendaPoints >= 5 ? 1 : 0,
            Money(features.Self.Credits), Money(features.Opponent.Credits),
            features.Hand.Length, corp ? features.Board.HiddenZoneCounts.RunnerGrip : features.Board.HiddenZoneCounts.CorpHand,
            servers.Sum(server => server.Ice.Length), servers.Sum(server => server.Ice.Count(card => card?.Rezzed == true)),
            servers.Count(server => server.Kind == ServerKind.Remote),
            servers.Where(server => server.Kind == ServerKind.Remote).SelectMany(server => server.Root).Sum(card => card?.AdvancementCounters ?? 0),
            ownProgress,
            breakerTypes, features.Board.RunnerRig.Sum(card => context.CardValue(card, installed: true)) / 1000.0,
            runner.Tags ?? 0, runner.CoreDamage ?? 0,
            hqOpen, hqCost, rdOpen, rdCost, remoteOpen, remoteCost,
            features.Board.HiddenZoneCounts.CorpDeck / 10.0, features.Board.HiddenZoneCounts.RunnerStack / 10.0,
            features.Board.Turn.TurnNumber / 10.0, features.Board.Turn.ActiveSide == features.Side ? 1 : 0,
        ];
    }

    /// <summary>The hand-weighted evaluation (A3) of the same position, for A5's comparison.</summary>
    public static long HandScore(AgentFeatures features, ICardEvaluationCatalog catalog) => PositionEvaluation.ScoreForFitting(features, catalog);

    /// <summary>Credits on a log scale: each one is worth less the more there are.</summary>
    private static double Money(int credits) => Math.Log(1 + Math.Max(0, credits) / 5.0);
}
