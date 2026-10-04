using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>One scheduled ladder game: a Corp entrant against a Runner entrant on fixed content and seed.</summary>
/// <remarks><paramref name="Deal"/> numbers a seed-and-pairing deal that every entrant pairing plays (A1b); −1 for seed-per-pairing plans.</remarks>
public sealed record LadderGame(string Id, string ContentId, ulong GameSeed, string Corp, string Runner, int Deal = -1);

/// <summary>Corp score is 1 for a Corp win, 0 for a Runner win and 0.5 for a capped game.</summary>
public sealed record LadderGameResult(LadderGame Game, double CorpScore, bool Capped, int Actions, string? Error);

public sealed record LadderRating(PlayerSide Side, string Entrant, double Elo, double Lower, double Upper, int Games, double ScoreRate);

/// <summary>Rating gap between adjacent entrants in expected order, with a paired bootstrap interval.</summary>
public sealed record LadderDifference(PlayerSide Side, string Weaker, string Stronger, double Elo, double Lower, double Upper)
{
    public bool Separated => Lower > 0;
}

public sealed record LadderSeparation(PlayerSide Side, IReadOnlyList<string> Order, bool Separated);

public sealed record LadderReport(IReadOnlyList<LadderRating> Ratings, IReadOnlyList<LadderDifference> Differences,
    IReadOnlyList<LadderSeparation> Separations, int Games, int Capped, int Errors);

/// <summary>
/// Head-to-head rating ladder over a fixed entrant pool. Every Corp entrant
/// meets every Runner entrant on the same seeds and preset pairings. Ratings
/// come from a Bradley–Terry fit (P(Corp wins) = σ(r_corp − r_runner)) with
/// a small ridge prior, reported on the Elo scale relative to the seat's
/// weakest expected entrant, with seeded bootstrap intervals. An order is
/// separated when every adjacent gap's paired bootstrap interval is above
/// zero; comparing separate rating intervals would be too strict because
/// ratings fitted from the same games are correlated.
/// </summary>
public static partial class RatingLadder
{
    public const int DefaultActionCap = 3000;

    /// <summary>
    /// Entrants in expected strength order, weakest first. "easy" and
    /// "standard" are the latest policy versions; the "-m5" entrants are the
    /// frozen M5 policies (Runner v1, Corp v2), "standard-w1" the Wave 1
    /// policies (Runner v2, Corp v3) and "standard-w2" the last policies that
    /// don't plan (Runner v2, Corp v4), for before/after comparisons;
    /// "standard-plan" is the A3 turn planner (Runner v3, Corp v5). The A6
    /// entrants close the list: the personalities, each Standard with the style
    /// in its own seat, and "hard".
    /// </summary>
    public static IReadOnlyList<string> EntrantOrder { get; } =
        ["random", "contract", "easy-m5", "standard-m5", "easy", "standard-w1", "standard-w2", "standard-w3", "standard-c1", "standard-r22", "standard-c26", "standard-r23", "standard-c27", "standard-r24", "standard-c28", "standard", "standard-plan", "standard-plan-fit", "standard-run", "standard-rez", "standard-w4a", "standard-w4b", "standard-arch", "standard-rarch", "standard-rgen", "standard-w4c", "standard-c13", "standard-w4d", "standard-c14", "standard-c15", "standard-c16", "standard-r13", "standard-c17", "standard-c18", "standard-c19", "standard-r14", "standard-c20", "standard-r15", "standard-c21", "standard-r16", "standard-c22", "standard-b3", "standard-c23", "standard-r17", "standard-r18", "standard-c24", "standard-r19", "standard-w5", "standard-r21", "hard-r21", "hard-a9c", "hard-a9d", "hard-a9d-list", "hard-a9d-pins", "glacier", "rush", "asset-spam", "aggressive", "rig-builder", "hard", "hard-wide", "hard-weight", "hard-rez", "hard-model"];

    private const double EloScale = 400 / 2.302585092994046;
    private const double Ridge = 0.01;

    public static IReadOnlyList<LadderGame> Plan(IReadOnlyList<string> entrants, int seedsPerPairing, ulong baseSeed,
        IReadOnlyList<string> contentIds)
    {
        ArgumentNullException.ThrowIfNull(entrants);
        ArgumentNullException.ThrowIfNull(contentIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seedsPerPairing);
        foreach (string entrant in entrants)
        {
            if (!EntrantOrder.Contains(entrant)) throw new ArgumentException($"Unknown ladder entrant '{entrant}'.", nameof(entrants));
        }

        var games = new List<LadderGame>();
        foreach (string corp in entrants)
        {
            foreach (string runner in entrants)
            {
                for (int seed = 0; seed < seedsPerPairing; seed++)
                {
                    foreach (string content in contentIds)
                    {
                        ulong gameSeed = RulesFuzzRunner.DeriveGameSeed(baseSeed, seed);
                        games.Add(new LadderGame($"{corp}-vs-{runner}-{content}-{seed + 1:D3}", content, gameSeed, corp, runner));
                    }
                }
            }
        }
        return games;
    }

    public static IGameAgent CreateAgent(string entrant, PlayerSide side, FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        PlayerSide other = side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        return entrant switch
        {
            "random" => new RandomLegalAgent(AgentLifecycleHost.DerivePolicySeed(definition.Request.Seed, side)),
            "contract" => new FullGameContractAgent(new FullGameContractPolicy(side), definition.Runtime),
            "easy" or "standard" or "hard" => OfflineMatchFactory.CreateOpponent(definition, other, entrant),
            // A6 tuning, kept as entrants (none beat Hard): a wider plan (Corp 10 samples of 4 actions, Runner 6 of 4).
            "hard-wide" => Replanned(OfflineMatchFactory.CreateOpponent(definition, other, "hard"), definition,
                side == PlayerSide.Corp ? 10 : 6, 4),
            // A6 tuning: Hard counting its rollouts double, adding the rez/run search, or modelling the current opponent.
            "hard-weight" => Tuned(OfflineMatchFactory.CreateOpponent(definition, other, "hard"), definition, ("plan-weight", 200)),
            "hard-rez" => Tuned(OfflineMatchFactory.CreateOpponent(definition, other, "hard"), definition,
                ("run-samples", RunnerPolicyProfiles.RunSamples), ("run-width", 2)),
            "hard-model" => Tuned(OfflineMatchFactory.CreateOpponent(definition, other, "hard"), definition, ("plan-model", 1)),
            // A6 personalities: Standard with the style in its own seat, plain Standard in the other.
            "glacier" or "rush" or "asset-spam" or "aggressive" or "rig-builder" => OfflineMatchFactory.CreateOpponent(definition, other, "standard", null,
                AgentPersonalities.Keys[entrant] is var personality && AgentPersonalities.Side(personality) == side ? personality : AgentPersonality.Balanced),
            // The LLM opponent (Wave 4.5 P3) needs a model, or Codex through the MCP game server; their saved games
            // replay with the heuristic they wrap, so a review shows where their choices differ from it.
            "llm" or "codex" => OfflineMatchFactory.CreateOpponent(definition, other, "standard"),
            "easy-m5" or "standard-m5" => OfflineMatchFactory.CreateOpponent(definition, other, entrant[..^3],
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V2 : RunnerHeuristicPolicy.V1),
            "standard-w1" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V3 : RunnerHeuristicPolicy.V2),
            "standard-w2" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V4 : RunnerHeuristicPolicy.V2),
            // Wave 3's playtest defaults before the Runner toll repair (Runner v5).
            "standard-w3" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V6 : RunnerHeuristicPolicy.V4),
            // C1's shipped versions, before the whole-run model (Runner v7).
            "standard-c1" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V7 : RunnerHeuristicPolicy.V6),
            "standard-plan" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V5 : RunnerHeuristicPolicy.V3),
            // A3 on A4a and A5: the planners, scoring rollouts by the fitted evaluation.
            "standard-plan-fit" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V9 : RunnerHeuristicPolicy.V8),
            // A4, measured against Corp v8 and Runner v7: the Runner searches its run choices.
            "standard-run" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V8 : RunnerHeuristicPolicy.V9),
            // A4, measured against Corp v8 and Runner v7: the Corp searches its rezzes during runs.
            "standard-rez" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V10 : RunnerHeuristicPolicy.V7),
            // Wave 4's defaults before the Runner's central memory (Corp v9, Runner v7).
            "standard-w4a" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V9 : RunnerHeuristicPolicy.V7),
            // Wave 4's defaults before the Corp's playtest repairs (Corp v9, Runner v10).
            "standard-w4b" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V9 : RunnerHeuristicPolicy.V10),
            // A8, measured against Corp v11: the Corp plays to its deck's archetype (v12) with the default Runner.
            "standard-arch" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V12 : RunnerHeuristicPolicy.PolicyVersion),
            // Wave 4's defaults before the archetype Corp and the fixed Runner (Corp v11, Runner v10).
            "standard-w4c" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V11 : RunnerHeuristicPolicy.V10),
            // Wave 4's defaults before the 29 September playtest repairs (Corp v12, Runner v12).
            "standard-w4d" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V12 : RunnerHeuristicPolicy.V12),
            // The Corp's 29 September playtest repairs (v13) with the default Runner.
            "standard-c13" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V13 : RunnerHeuristicPolicy.PolicyVersion),
            // The Corp's rez timing for economy assets (v14) with the default Runner.
            "standard-c14" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V14 : RunnerHeuristicPolicy.PolicyVersion),
            // The Corp's self-removing abilities costed (v15) with the default Runner.
            "standard-c15" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V15 : RunnerHeuristicPolicy.PolicyVersion),
            // The Corp planning with the corrected evaluation (v19) with the default Runner.
            // Corp v26 with the default Runner: bot loop 1 measures Corp v27 against it.
            "standard-c26" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V26 : RunnerHeuristicPolicy.PolicyVersion),
            // Corp v28 with the default Runner: bot loop 2 measures Corp v29 against it.
            "standard-c28" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V28 : RunnerHeuristicPolicy.PolicyVersion),
            // Runner v24 with the default Corp: bot loop 2 measures Runner v25 against it.
            "standard-r24" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V24),
            // Corp v27 with the default Runner: bot loop 1's deferred fixes measure Corp v28 against it.
            "standard-c27" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V27 : RunnerHeuristicPolicy.PolicyVersion),
            // Runner v23 with the default Corp: bot loop 1's deferred fixes measure Runner v24 against it.
            "standard-r23" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V23),
            // Runner v22 with the default Corp: bot loop 1 measures Runner v23 against it.
            "standard-r22" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V22),
            // Runner v19 (economy fixes, 1 October) with the default Corp.
            "standard-r19" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V19),
            // A9c (2 October): copy counting, Corp v26 and Runner v22, at Hard (which knows the list, so the counts are exact).
            "hard-a9c" => OfflineMatchFactory.CreateOpponent(definition, other, "hard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V26 : RunnerHeuristicPolicy.V22),
            // A9d (2 October): hard-a9c whose planner deals its samples from the tracker (and the list).
            "hard-a9d" => Tuned(OfflineMatchFactory.CreateOpponent(definition, other, "hard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V26 : RunnerHeuristicPolicy.V22), definition, ("plan-tracker", 1)),
            "hard-a9d-list" => Tuned(OfflineMatchFactory.CreateOpponent(definition, other, "hard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V26 : RunnerHeuristicPolicy.V22), definition, ("plan-tracker", 2)),
            "hard-a9d-pins" => Tuned(OfflineMatchFactory.CreateOpponent(definition, other, "hard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V26 : RunnerHeuristicPolicy.V22), definition, ("plan-tracker", 3)),
            // A9b (2 October): Runner v21, inference from the tracker, with the default Corp; at Standard (the pool) and Hard (the list).
            "standard-r21" or "hard-r21" => OfflineMatchFactory.CreateOpponent(definition, other, entrant[..entrant.IndexOf('-', StringComparison.Ordinal)],
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V21),
            // The Wave 5 playtest fixes (2 October): Corp v25 and Runner v20.
            "standard-w5" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V25 : RunnerHeuristicPolicy.V20),
            // Corp v24 (removal fixes, 1 October) with the default Runner.
            "standard-c24" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V24 : RunnerHeuristicPolicy.PolicyVersion),
            // Runner v18 (v15 with the reinstall fix) with the default Corp.
            "standard-r18" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V18),
            // Runner v17 (batch 4's reviews, 1 October) with the default Corp.
            "standard-r17" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V17),
            // Corp v23 (reviews of batch 4's losses to Codex, 1 October) with the default Runner.
            "standard-c23" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V23 : RunnerHeuristicPolicy.PolicyVersion),
            // The bots of Codex's batch 3 (Corp v21, Runner v15), to replay its deals against them.
            "standard-b3" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V21 : RunnerHeuristicPolicy.V15),
            // Batch 3 (reviews of the losses to Codex, 1 October): Corp v22 and Runner v16, each with the other default.
            "standard-c22" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V22 : RunnerHeuristicPolicy.PolicyVersion),
            "standard-r16" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V16),
            // Corp v21 (reviews of the losses to Codex, 1 October) with the default Runner.
            "standard-c21" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V21 : RunnerHeuristicPolicy.PolicyVersion),
            // Corp v20 (Codex reviews, 1 October) with the default Runner.
            "standard-c20" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V20 : RunnerHeuristicPolicy.PolicyVersion),
            // Runner v15 (reviews of the losses to Codex, 1 October) with the default Corp.
            "standard-r15" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V15),
            // Runner v14 (Codex reviews, 1 October) with the default Corp.
            "standard-r14" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V14),
            "standard-c19" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V19 : RunnerHeuristicPolicy.PolicyVersion),
            // The Corp protecting HQ's agendas and aiming its counters (v18) with the default Runner.
            "standard-c18" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V18 : RunnerHeuristicPolicy.PolicyVersion),
            // The Corp's subroutine installs costed (v17) with the default Runner.
            "standard-c17" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V17 : RunnerHeuristicPolicy.PolicyVersion),
            // The Runner's 30 September playtest fixes (v13) with the default Corp.
            "standard-r13" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V13),
            // The Corp hides a threatened agenda in Archives for Spin Doctor (v16) with the default Runner.
            "standard-c16" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V16 : RunnerHeuristicPolicy.PolicyVersion),
            // Runner v11's fixes without its archetype knobs (every deck generic), with the default Corp.
            "standard-rgen" => GenericRunner(OfflineMatchFactory.CreateOpponent(definition, other, "standard", RunnerHeuristicPolicy.V11), definition),
            // A8: the Runner plays to its deck's archetype (v11) with the default Corp.
            "standard-rarch" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V11),
            _ => throw new ArgumentException($"Unknown ladder entrant '{entrant}'.", nameof(entrant)),
        };
    }

    /// <summary>The same agent planning a different number of samples and actions.</summary>
    private static OfflineAgent Replanned(OfflineAgent agent, FullMatchDefinition definition, int samples, int width)
    {
        OfflineAgentConfiguration current = agent.Configuration;
        OfflineAgentConfiguration configuration = OfflineAgentConfiguration.Create(current.Side, current.PolicyVersion, current.ProfileId,
            current.PolicySeed, current.WorkBudget, current.NearTieTolerance,
            [.. current.Parameters.Select(pair => pair.Key switch
            {
                "plan-samples" => new KeyValuePair<string, int>(pair.Key, samples),
                "plan-width" => new KeyValuePair<string, int>(pair.Key, width),
                _ => pair,
            })],
            current.FeatureVersion);
        return new OfflineAgent(configuration, OfflinePolicies.Create(configuration), new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior));
    }

    /// <summary>The same agent with planner settings added or replaced.</summary>
    private static OfflineAgent Tuned(OfflineAgent agent, FullMatchDefinition definition, params (string Key, int Value)[] settings)
    {
        OfflineAgentConfiguration current = agent.Configuration;
        OfflineAgentConfiguration configuration = OfflineAgentConfiguration.Create(current.Side, current.PolicyVersion, current.ProfileId,
            current.PolicySeed, current.WorkBudget, current.NearTieTolerance,
            [.. current.Parameters.Where(pair => settings.All(setting => setting.Key != pair.Key)),
                .. settings.Select(setting => new KeyValuePair<string, int>(setting.Key, setting.Value))],
            current.FeatureVersion);
        var tuned = new OfflineAgent(configuration, OfflinePolicies.Create(configuration), new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior));
        // A Hard bot's knowledge of the opponent's list carries over.
        if (agent.Checkpoint.Knowledge.OpponentList is { } list) tuned.KnowOpponentList(list.Select(entry => (entry.DefinitionId, entry.Quantity)));
        return tuned;
    }

    /// <summary>The same agent with a generic archetype profile.</summary>
    private static OfflineAgent GenericRunner(OfflineAgent agent, FullMatchDefinition definition)
    {
        OfflineAgentConfiguration configuration = RunnerPolicyProfiles.WithArchetypes(agent.Configuration, RunnerArchetype.Generic, RunnerArchetype.Generic);
        return new OfflineAgent(configuration, OfflinePolicies.Create(configuration), new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior));
    }

    public static Task<LadderGameResult> PlayAsync(LadderGame game, int actionCap = DefaultActionCap,
        CancellationToken cancellationToken = default) => PlayAsync(game, actionCap, null, cancellationToken);

    /// <param name="keep">Receives each finished or capped game with its replay (<c>ai-ladder --save-games</c>).</param>
    public static Task<LadderGameResult> PlayAsync(LadderGame game, int actionCap, Action<LadderSavedGame>? keep,
        CancellationToken cancellationToken = default) => PlayAsync(game, actionCap, keep, CreateAgent, cancellationToken);

    /// <param name="agents">Makes each seat's agent from its entrant name (the LLM opponent's games pass their own).</param>
    public static async Task<LadderGameResult> PlayAsync(LadderGame game, int actionCap, Action<LadderSavedGame>? keep,
        Func<string, PlayerSide, FullMatchDefinition, IGameAgent> agents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(agents);
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(game.ContentId, game.GameSeed);
        try
        {
            FullMatchResult result = await FullMatchRunner.RunAsync(definition, actionCap,
                agents(game.Corp, PlayerSide.Corp, definition),
                agents(game.Runner, PlayerSide.Runner, definition), cancellationToken).ConfigureAwait(false);
            double score = result.Outcome.Winner == PlayerSide.Corp ? 1 : 0;
            keep?.Invoke(new LadderSavedGame(game, score, false, result.Replay));
            return new LadderGameResult(game, score, false, result.ActionCount, null);
        }
        catch (FullMatchStallException stall)
        {
            keep?.Invoke(new LadderSavedGame(game, 0.5, true, stall.Replay));
            return new LadderGameResult(game, 0.5, true, stall.Replay.Actions.Count, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An engine or agent failure is a defect for the fuzzer, not a result.
            return new LadderGameResult(game, 0.5, false, 0, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static LadderReport Rate(IReadOnlyList<LadderGameResult> results, int bootstrap = 200, ulong bootstrapSeed = 1,
        double confidence = 0.95)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(confidence, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(confidence, 1);
        double tail = (1 - confidence) / 2;
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfNegative(bootstrap);
        LadderGameResult[] scored = results.Where(result => result.Error is null).ToArray();
        if (scored.Length == 0) throw new ArgumentException("No scored ladder games.", nameof(results));
        string[] keys = scored.SelectMany(result => new[] { Key(PlayerSide.Corp, result.Game.Corp), Key(PlayerSide.Runner, result.Game.Runner) })
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Dictionary<string, double> point = Anchor(Fit(scored, keys));
        var samples = keys.ToDictionary(key => key, _ => new List<double>(bootstrap), StringComparer.Ordinal);
        var draws = new List<Dictionary<string, double>>(bootstrap);
        var random = new DeterministicRandom(bootstrapSeed);
        for (int iteration = 0; iteration < bootstrap; iteration++)
        {
            var resample = new LadderGameResult[scored.Length];
            for (int index = 0; index < resample.Length; index++) resample[index] = scored[random.NextInt(scored.Length)];
            Dictionary<string, double> draw = Anchor(Fit(resample, keys));
            draws.Add(draw);
            foreach ((string key, double value) in draw) samples[key].Add(value);
        }

        var ratings = keys.Select(key =>
        {
            (PlayerSide side, string entrant) = Split(key);
            List<double> values = samples[key];
            values.Sort();
            LadderGameResult[] played = scored.Where(result => (side == PlayerSide.Corp ? result.Game.Corp : result.Game.Runner) == entrant).ToArray();
            double sideScore = played.Average(result => side == PlayerSide.Corp ? result.CorpScore : 1 - result.CorpScore);
            return new LadderRating(side, entrant, point[key],
                values.Count == 0 ? point[key] : Percentile(values, tail),
                values.Count == 0 ? point[key] : Percentile(values, 1 - tail),
                played.Length, sideScore);
        }).OrderBy(rating => rating.Side).ThenBy(rating => Rank(rating.Entrant)).ToArray();

        var differences = new List<LadderDifference>();
        var separations = new List<LadderSeparation>();
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            LadderRating[] ordered = ratings.Where(rating => rating.Side == side).OrderBy(rating => Rank(rating.Entrant)).ToArray();
            foreach ((LadderRating weaker, LadderRating stronger) in ordered.Zip(ordered.Skip(1)))
            {
                string weakerKey = Key(side, weaker.Entrant);
                string strongerKey = Key(side, stronger.Entrant);
                List<double> gaps = draws.Select(draw => draw[strongerKey] - draw[weakerKey]).Order().ToList();
                double gap = point[strongerKey] - point[weakerKey];
                differences.Add(new LadderDifference(side, weaker.Entrant, stronger.Entrant, gap,
                    gaps.Count == 0 ? gap : Percentile(gaps, tail), gaps.Count == 0 ? gap : Percentile(gaps, 1 - tail)));
            }
            separations.Add(new LadderSeparation(side, ordered.Select(rating => rating.Entrant).ToArray(),
                differences.Where(difference => difference.Side == side).All(difference => difference.Separated)));
        }

        return new LadderReport(ratings, differences, separations, results.Count, results.Count(result => result.Capped),
            results.Count(result => result.Error is not null));

        // Ratings are identified only up to a shift; anchor each seat at its weakest entrant.
        Dictionary<string, double> Anchor(Dictionary<string, double> fitted)
        {
            var anchored = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
            {
                string[] seat = fitted.Keys.Where(key => Split(key).Side == side).OrderBy(key => Rank(Split(key).Entrant)).ToArray();
                if (seat.Length == 0) continue;
                double origin = fitted[seat[0]];
                foreach (string key in seat) anchored[key] = (fitted[key] - origin) * EloScale;
            }
            return anchored;
        }
    }

    /// <summary>Ridge-regularized maximum likelihood by gradient ascent; deterministic for a given input order.</summary>
    private static Dictionary<string, double> Fit(LadderGameResult[] games, string[] keys)
    {
        var index = keys.Select((key, position) => (key, position)).ToDictionary(pair => pair.key, pair => pair.position, StringComparer.Ordinal);
        var ratings = new double[keys.Length];
        var gradient = new double[keys.Length];
        var pairs = games.Select(game => (Corp: index[Key(PlayerSide.Corp, game.Game.Corp)],
            Runner: index[Key(PlayerSide.Runner, game.Game.Runner)], Score: game.CorpScore)).ToArray();
        const double LearningRate = 4.0;
        for (int iteration = 0; iteration < 3000; iteration++)
        {
            Array.Clear(gradient);
            foreach ((int corp, int runner, double score) in pairs)
            {
                double residual = score - 1 / (1 + Math.Exp(ratings[runner] - ratings[corp]));
                gradient[corp] += residual;
                gradient[runner] -= residual;
            }
            for (int key = 0; key < ratings.Length; key++)
            {
                ratings[key] += LearningRate * (gradient[key] / pairs.Length - Ridge * ratings[key]);
            }
        }
        return keys.Select((key, position) => (key, position)).ToDictionary(pair => pair.key, pair => ratings[pair.position], StringComparer.Ordinal);
    }

    private static double Percentile(List<double> sorted, double quantile)
    {
        double position = quantile * (sorted.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = Math.Min(sorted.Count - 1, lower + 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static int Rank(string entrant)
    {
        int rank = -1;
        for (int index = 0; index < EntrantOrder.Count; index++)
        {
            if (EntrantOrder[index] == entrant) rank = index;
        }
        return rank;
    }

    private static string Key(PlayerSide side, string entrant) => $"{side}:{entrant}";

    private static (PlayerSide Side, string Entrant) Split(string key)
    {
        int separator = key.IndexOf(':', StringComparison.Ordinal);
        return (Enum.Parse<PlayerSide>(key[..separator]), key[(separator + 1)..]);
    }
}
