using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Bot plan S4.5, the apprentice at the root: a turn action's own features, beside the root's (feature set 5 from the
/// acting side's view), from what the acting side can see: what kind of action it is, how the heuristic rates it, the card
/// it plays and the server it targets (the run model's view of getting in), and the credits it moves.
/// </summary>
public static class ApprenticeFeatures
{
    /// <summary>The action kinds a turn action can be, each its own term; any other is <c>kind-other</c>.</summary>
    public static IReadOnlyList<ActionKind> Kinds { get; } =
    [
        ActionKind.GainCredit, ActionKind.DrawCard, ActionKind.InstallCard, ActionKind.PlayCard, ActionKind.AdvanceCard,
        ActionKind.AdvanceAgenda, ActionKind.ScoreAgenda, ActionKind.StartRun, ActionKind.UseAbility, ActionKind.RezCard,
        ActionKind.RemoveTag, ActionKind.TrashTaggedResource, ActionKind.PurgeVirusCounters, ActionKind.EndTurn,
    ];

    private static readonly RuntimeCardClass[] Classes =
    [
        RuntimeCardClass.Agenda, RuntimeCardClass.Asset, RuntimeCardClass.Upgrade, RuntimeCardClass.Ice, RuntimeCardClass.Operation,
        RuntimeCardClass.Event, RuntimeCardClass.Program, RuntimeCardClass.Hardware, RuntimeCardClass.Resource,
    ];

    /// <summary>The candidate's own terms, in order.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        .. Kinds.Select(kind => $"kind-{kind}"), "kind-other",
        "heuristic-band", "heuristic-score", "heuristic-rank", "heuristic-first",
        .. Classes.Select(cardClass => $"card-{cardClass}"), "card-cost", "card-agenda-points", "card-advancement-left",
        "target-remote", "target-new-remote", "target-hq", "target-rd", "target-archives", "target-ice", "target-root",
        "target-open", "target-cost", "target-agenda-points",
        "credits-moved", "credits-after",
    ];

    /// <summary>
    /// The root's terms a candidate's kind interacts with in the linear model (a root term alone is the same for every
    /// candidate of a root and cannot order them): the score race, the money, and set 5's strategic terms.
    /// </summary>
    public static IReadOnlyList<string> InteractingRootTerms { get; } =
    [
        "agenda-self", "agenda-opponent", "money-self", "money-opponent", "hand-self",
        "exposed-agenda-points", "hq-agenda-density", "rd-agenda-density", "advancements-left", "scoring-server-open",
        "grip-margin", "central-min-cost", "money-after-reserve", "race-to-move",
    ];

    /// <summary>
    /// The candidate's terms. <paramref name="candidates"/> are every candidate of the decision with the heuristic's
    /// utility, which ranks them.
    /// </summary>
    public static double[] Extract(AgentFeatures features, AgentAction action, IReadOnlyList<AgentCandidate> candidates,
        ICardEvaluationCatalog catalog, AgentKnowledge? knowledge)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(catalog);
        var values = new double[Names.Count];
        int at = 0;
        values[KindIndexes.Value.TryGetValue(action.Kind, out int kind) ? kind : Kinds.Count] = 1;
        at = Kinds.Count + 1;

        // The heuristic's view: its band, its score (on a log scale), its rank among the candidates and whether it is first.
        AgentCandidate own = candidates.First(candidate => candidate.ActionId == action.Id);
        AgentCandidate[] ranked = [.. candidates.OrderByDescending(candidate => candidate.Utility.Priority)
            .ThenByDescending(candidate => candidate.Utility.Score).ThenBy(candidate => candidate.ActionId.Value, StringComparer.Ordinal)];
        int rank = Array.FindIndex(ranked, candidate => candidate.ActionId == action.Id);
        int topPriority = ranked[0].Utility.Priority;
        values[at++] = Math.Clamp((own.Utility.Priority - topPriority) / 1000.0, -5, 0);
        values[at++] = Math.Sign(own.Utility.Score) * Math.Log(1 + Math.Abs(own.Utility.Score) / 100.0);
        values[at++] = ranked.Length <= 1 ? 0 : rank / (double)(ranked.Length - 1);
        values[at++] = rank == 0 ? 1 : 0;

        // The card it plays, installs, advances or uses.
        string? cardId = action.Parameters switch
        {
            InstallCardParameters install => install.CardId.Value,
            PlayCardParameters play => play.CardId.Value,
            AdvanceCardParameters advance => advance.CardId.Value,
            ScoreAgendaParameters score => score.CardId.Value,
            RezCardParameters rez => rez.CardId.Value,
            AbilityActionParameters ability => ability.SourceCardId.Value,
            _ => null,
        };
        AgentCard? card = cardId is null ? null : features.VisibleCards().Select(pair => pair.Card).FirstOrDefault(entry => entry.Id == cardId);
        CardEvaluationDescriptor? descriptor = card is not null && catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor found)
            ? found : null;
        foreach (RuntimeCardClass cardClass in Classes) values[at++] = descriptor?.Printed.CardClass == cardClass ? 1 : 0;
        int cost = action.Parameters switch
        {
            InstallCardParameters install => install.CreditCost,
            PlayCardParameters play => play.CreditCost,
            AdvanceCardParameters advance => advance.CreditCost,
            RezCardParameters rez => rez.CreditCost,
            _ => 0,
        };
        values[at++] = cost / 5.0;
        values[at++] = descriptor?.Agenda?.AgendaPoints ?? 0;
        values[at++] = descriptor?.Agenda is not null && card is not null
            ? Math.Max(0, descriptor.Printed.AdvancementRequirement - (card.AdvancementCounters ?? 0)) / 5.0 : 0;

        // The server it targets: an install's destination, a run's server, an advanced card's.
        string? serverId = action.Parameters switch
        {
            InstallCardParameters install => install.ServerId?.Value,
            RunServerParameters run => run.ServerId.Value,
            PlayCardParameters play => play.TargetServerId?.Value,
            _ => cardId is null ? null : features.Board.Servers.FirstOrDefault(server => server.Root.Any(root => root?.Id == cardId)
                || server.Ice.Any(ice => ice?.Id == cardId))?.Id,
        };
        AgentServer? target = serverId is null ? null : features.Board.Servers.FirstOrDefault(server => server.Id == serverId);
        bool newRemote = action.Parameters is InstallCardParameters { CreateNewRemote: true };
        values[at++] = target?.Kind == ServerKind.Remote || newRemote ? 1 : 0;
        values[at++] = newRemote ? 1 : 0;
        values[at++] = target?.Kind == ServerKind.Headquarters ? 1 : 0;
        values[at++] = target?.Kind == ServerKind.RAndD ? 1 : 0;
        values[at++] = target?.Kind == ServerKind.Archives ? 1 : 0;
        values[at++] = (target?.Ice.Length ?? 0) / 3.0;
        values[at++] = (target?.Root.Length ?? 0) / 2.0;
        (double open, double runCost) = target is null ? (0, 0) : Run(features, catalog, knowledge, target);
        values[at++] = open;
        values[at++] = runCost / 10.0;
        values[at++] = target is null ? 0 : target.Root.OfType<AgentCard>().Sum(root =>
            catalog.TryGet(new CardDefinitionId(root.DefinitionId), out CardEvaluationDescriptor rootCard) ? rootCard.Agenda?.AgendaPoints ?? 0 : 0);

        // The credits it moves, and what the side has after.
        int moved = action.Kind == ActionKind.GainCredit ? 1 : -cost;
        values[at++] = moved / 5.0;
        values[at++] = Math.Log(1 + Math.Max(0, features.Self.Credits + moved) / 5.0);
        return values;
    }

    /// <summary>
    /// Every candidate's terms for the model: the candidate's own, then each kind's interaction with the root terms that
    /// can order candidates (<see cref="InteractingRootTerms"/>), then the root's set 5.
    /// </summary>
    public static double[] Combine(double[] candidate, double[] root)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(root);
        int[] interacting = Interacting.Value;
        int kinds = Kinds.Count + 1;
        var values = new double[candidate.Length + kinds * interacting.Length + root.Length];
        candidate.CopyTo(values, 0);
        int at = candidate.Length;
        for (int kind = 0; kind < kinds; kind++)
            foreach (int term in interacting) values[at++] = candidate[kind] * root[term];
        root.CopyTo(values, at);
        return values;
    }

    private static readonly Lazy<int[]> Interacting = new(() =>
        [.. InteractingRootTerms.Select(name => EvaluationFeatures.NamesFor(5).ToList().IndexOf(name))]);

    private static readonly Lazy<Dictionary<ActionKind, int>> KindIndexes = new(() =>
        Kinds.Select((kind, index) => (kind, index)).ToDictionary(entry => entry.kind, entry => entry.index));

    /// <summary>The combined terms' names, in <see cref="Combine"/>'s order.</summary>
    public static IReadOnlyList<string> CombinedNames { get; } =
    [
        .. Names,
        .. Names.Take(Kinds.Count + 1).SelectMany(kind => InteractingRootTerms.Select(term => $"{kind}*{term}")),
        .. EvaluationFeatures.NamesFor(5),
    ];

    private static (double Open, double Cost) Run(AgentFeatures features, ICardEvaluationCatalog catalog, AgentKnowledge? knowledge, AgentServer server)
    {
        bool corp = features.Side == PlayerSide.Corp;
        var context = new HeuristicContext(features, catalog,
            corp ? CorpPolicyProfiles.Create("standard", 0, CorpHeuristicPolicy.PolicyVersion)
                : RunnerPolicyProfiles.Create("standard", 0, RunnerHeuristicPolicy.PolicyVersion), generation: 23)
        { Knowledge = knowledge };
        AgentPlayer runner = features.Board.Players.Runner;
        int grip = corp ? features.Board.HiddenZoneCounts.RunnerGrip : features.Hand.Length;
        RunForecast forecast = RunModel.Plan(context, server, server.Ice.Length - 1, runner.Credits, 3, grip: grip,
            corpRezBudget: corp ? features.Board.Players.Corp.Credits : null);
        if (!forecast.CanGetIn) return (0, 10);
        return (forecast.UnknownIce > 0 ? 0.5 : 1, Math.Min(10, forecast.Credits + forecast.CreditsLost + forecast.Clicks * 2 + forecast.Tags * 2
            + forecast.Damage * 2 + forecast.UnknownIce * 3));
    }
}
