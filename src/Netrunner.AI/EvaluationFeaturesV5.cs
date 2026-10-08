using Netrunner.Core;

namespace Netrunner.AI;

public static partial class EvaluationFeatures
{
    /// <summary>
    /// Bot plan S4.1, feature set 5: set 4 and the strategic terms the loops named (plan 4.2), each from the observing
    /// side's view: what the observation shows, and what its knowledge (the tracker, the opponent's list for Hard) infers.
    /// The score race is set 1's (points, near-win, turn, who moves), with one interaction added.
    /// </summary>
    public static IReadOnlyList<string> NamesV5 { get; } =
    [
        .. NamesV2,
        "exposed-agenda-points", "hq-agenda-density", "rd-agenda-density", "advancements-left", "scoring-server-open",
        "punisher-exposure", "grip-margin", "central-min-cost", "central-affordable",
        "money-after-reserve", "income-cards", "race-to-move",
    ];

    /// <summary>
    /// Feature set 5 (bot plan S4.1). <paramref name="knowledge"/> is the observer's (null reads as knowing nothing past the
    /// observation: the pool prior and no tracker). The terms:
    /// <list type="bullet">
    /// <item><c>exposed-agenda-points</c>: agenda points in remotes the Runner can get into with its credits now (the run
    /// model's cheapest path; unrezzed ICE counted, not priced), known for the Corp and inferred for the Runner
    /// (<see cref="RunnerInference.RootAgenda"/>: the tracker's belief, else the unseen root cards' share moved by protection).</item>
    /// <item><c>hq-agenda-density</c>, <c>rd-agenda-density</c>: agendas over HQ's size (the Corp's own hand; the Runner's
    /// tracker), and agenda points per unseen Corp card (the pool, or the list for Hard, less what is visible).</item>
    /// <item><c>advancements-left</c>, <c>scoring-server-open</c>: for the most advanced agenda (the Runner: the most
    /// advanced face-down card, its requirement the prior's average), counters still needed (5 with none), and whether the
    /// Runner can get into its server.</item>
    /// <item><c>punisher-exposure</c>, <c>grip-margin</c>: the chance a successful run arms an operation the Runner cannot
    /// pay off or survive (the Runner: <see cref="RunnerInference.Punishers"/>; the Corp: one it holds and can pay for),
    /// and the grip less the largest such damage (and a known trap's), clamped to ±5.</item>
    /// <item><c>central-min-cost</c>, <c>central-affordable</c>: the central tax as set 1 prices HQ and R&amp;D, the cheaper
    /// of the two, and whether either is open at the Runner's credits (Archives, almost never protected, says nothing).</item>
    /// <item><c>money-after-reserve</c>, <c>income-cards</c>: the observer's credits less its reserve (the Corp: rezzing its
    /// cheapest unrezzed ICE; the Runner: the cheapest central), on the money scale, and its installed cards that gain
    /// credits (recurring credits, or a trigger or paid ability that gains or takes them).</item>
    /// <item><c>race-to-move</c>: the agenda lead in sevenths when the observer moves next, else 0.</item>
    /// </list>
    /// </summary>
    public static double[] ExtractV5(AgentFeatures features, ICardEvaluationCatalog catalog, AgentKnowledge? knowledge)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(catalog);
        double[] set4 = Extract(features, catalog, 4);
        bool corp = features.Side == PlayerSide.Corp;
        var context = new HeuristicContext(features, catalog,
            corp ? CorpPolicyProfiles.Create("standard", 0, CorpHeuristicPolicy.PolicyVersion)
                : RunnerPolicyProfiles.Create("standard", 0, RunnerHeuristicPolicy.PolicyVersion), generation: 23)
        { Knowledge = knowledge };
        AgentPlayer runner = features.Board.Players.Runner, corpPlayer = features.Board.Players.Corp;
        int grip = corp ? features.Board.HiddenZoneCounts.RunnerGrip : features.Hand.Length;
        AgentServer[] servers = [.. features.Board.Servers];
        // Each server's run is planned once: set 5 is the planner's leaf, read at the end of every rollout.
        var forecasts = new Dictionary<string, RunForecast>(StringComparer.Ordinal);
        RunForecast Forecast(AgentServer server)
        {
            if (!forecasts.TryGetValue(server.Id, out RunForecast? forecast))
                forecasts[server.Id] = forecast = RunModel.Plan(context, server, server.Ice.Length - 1, runner.Credits, 3, grip: grip,
                    corpRezBudget: corp ? corpPlayer.Credits : null);
            return forecast;
        }
        double Open(AgentServer server) => Forecast(server) is { CanGetIn: true } forecast ? forecast.UnknownIce > 0 ? 0.5 : 1 : 0;
        double Cost(AgentServer? server)
        {
            if (server is null) return 0;
            RunForecast forecast = Forecast(server);
            return !forecast.CanGetIn ? 10
                : Math.Min(10, forecast.Credits + forecast.CreditsLost + forecast.Clicks * 2 + forecast.Tags * 2 + forecast.Damage * 2 + forecast.UnknownIce * 3);
        }
        double AgendaPoints(AgentCard card) => catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor definition)
            && definition.Agenda is { } agenda ? agenda.AgendaPoints : 0;

        // Unseen Corp cards: the Runner's prior (the list for Hard), or for the Corp the pool less what it sees.
        IReadOnlyDictionary<string, double> unseen = corp
            ? TrackerPrior.Remaining(TrackerPrior.Pool(catalog, PlayerSide.Corp), features) : RunnerInference.Prior(context);
        double unseenCards = unseen.Values.Sum(), unseenPoints = 0, agendaCopies = 0, requirements = 0;
        foreach ((string definition, double weight) in unseen)
            if (catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card) && card.Agenda is { } agenda)
            {
                unseenPoints += weight * agenda.AgendaPoints;
                agendaCopies += weight;
                requirements += weight * card.Printed.AdvancementRequirement;
            }
        double averageRequirement = agendaCopies > 0 ? requirements / agendaCopies : 4;

        // Exposed agenda points, and the most advanced agenda.
        double exposed = 0, left = 5, scoringOpen = 0;
        foreach (AgentServer server in servers.Where(server => server.Kind == ServerKind.Remote))
        {
            double points = 0;
            for (int index = 0; index < server.Root.Length; index++)
            {
                AgentCard? card = server.Root[index];
                int counters = card?.AdvancementCounters ?? context.HiddenAdvancement(server.Id, "root", index);
                double requirement, cardPoints;
                if (card is not null && catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor known))
                {
                    cardPoints = AgendaPoints(card);
                    requirement = known.Agenda is null ? double.NaN : Math.Max(1, known.Printed.AdvancementRequirement);
                }
                else if (corp) continue;
                else
                {
                    (double rootChance, double worth) = RunnerInference.RootAgenda(context, server, index);
                    cardPoints = rootChance * Math.Max(0, (worth - 500) / 180);
                    requirement = counters > 0 ? averageRequirement : double.NaN;
                }
                points += cardPoints;
                if (!double.IsNaN(requirement) && requirement - counters < left)
                {
                    left = Math.Max(0, requirement - counters);
                    scoringOpen = Open(server);
                }
            }
            if (points > 0 && Open(server) > 0) exposed += points * Open(server);
        }

        // HQ: the Corp's own hand, or the Runner's tracker.
        int hqSize = features.Board.HiddenZoneCounts.CorpHand;
        double hqDensity = corp
            ? features.Hand.Length == 0 ? 0 : features.Hand.Count(card => AgendaPoints(card) > 0) / (double)features.Hand.Length
            : hqSize == 0 ? 0 : RunnerInference.HqAgendaShare(context);
        double rdDensity = unseenCards <= 0 ? 0 : unseenPoints / unseenCards;

        // Punishers: the Runner's inference, or what the Corp holds and can pay for against the grip.
        int threat = Math.Max(runner.AgendaPoints, corpPlayer.AgendaPoints);
        (int need, double chance) = corp
            ? (features.Hand.Select(card => catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor definition)
                ? RunnerInference.PunisherDamage(definition, threat, corpPlayer.Credits, runner.Credits) : 0).DefaultIfEmpty(0).Max(), 1.0)
            : RunnerInference.Punishers(context);
        if (need == 0) chance = 0;
        int trap = servers.Where(server => server.Kind == ServerKind.Remote)
            .SelectMany(server => server.Root.Select((card, index) => (card, counters: card?.AdvancementCounters ?? context.HiddenAdvancement(server.Id, "root", index))))
            .Select(entry => entry.card is not null && catalog.TryGet(new CardDefinitionId(entry.card.DefinitionId), out CardEvaluationDescriptor definition)
                ? RunnerInference.AccessDamage(definition, entry.counters) : 0)
            .DefaultIfEmpty(0).Max();
        double exposure = grip < need ? chance : 0;
        double margin = Math.Clamp(grip - Math.Max(need, trap), -5, 5);

        // The central tax: HQ and R&D.
        AgentServer[] centrals = [.. servers.Where(server => server.Kind is ServerKind.Headquarters or ServerKind.RAndD)];
        double minCost = centrals.Length == 0 ? 10 : centrals.Min(Cost);
        double affordable = centrals.Any(server => Forecast(server) is { CanGetIn: true } forecast && forecast.Credits + forecast.CreditsLost <= runner.Credits) ? 1 : 0;

        // Economy tempo.
        int reserve = corp
            ? servers.SelectMany(server => server.Ice).OfType<AgentCard>().Where(card => card.Rezzed != true)
                .Select(card => catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor definition) ? definition.Printed.RezCost ?? 0 : 0)
                .DefaultIfEmpty(0).Min()
            : (int)Math.Min(10, centrals.Select(server => Forecast(server)).Where(forecast => forecast.CanGetIn)
                .Select(forecast => forecast.Credits + forecast.CreditsLost).DefaultIfEmpty(10).Min());
        int credits = features.Self.Credits;
        double afterReserve = Math.Sign(credits - reserve) * Math.Log(1 + Math.Abs(credits - reserve) / 5.0);
        IEnumerable<AgentCard> installed = corp
            ? servers.SelectMany(server => server.Root).OfType<AgentCard>().Where(card => card.Rezzed == true)
            : features.Board.RunnerRig;
        int income = installed.Count(card => catalog.TryGet(new CardDefinitionId(card.DefinitionId), out CardEvaluationDescriptor definition)
            && definition.Behavior is { } behavior && GainsCredits(behavior));

        double raceToMove = features.Board.Turn.ActiveSide == features.Side
            ? (features.Self.AgendaPoints - features.Opponent.AgendaPoints) / 7.0 : 0;
        return
        [
            .. set4,
            exposed, hqDensity, rdDensity, left, scoringOpen,
            exposure, margin, minCost, affordable,
            afterReserve, income, raceToMove,
        ];
    }

    /// <summary>Whether a card makes money: recurring credits, or a trigger or paid ability that gains or takes credits.</summary>
    private static bool GainsCredits(CardBehaviorDefinition behavior) =>
        behavior.RecurringCredits > 0
        || behavior.Triggers.SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
            .Concat(behavior.PaidAbilities.SelectMany(ability => HeuristicContext.Flatten(ability.Instructions)))
            .Any(instruction => instruction.Kind is BehaviorInstructionKind.GainCredits or BehaviorInstructionKind.TakeHostedCredits
                or BehaviorInstructionKind.TakeUpToHostedCredits or BehaviorInstructionKind.TakeAllHostedCredits);
}
