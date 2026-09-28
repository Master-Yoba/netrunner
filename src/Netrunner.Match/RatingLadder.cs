using Netrunner.AI;
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
    /// "standard-plan" is the A3 turn planner (Runner v3, Corp v5).
    /// </summary>
    public static IReadOnlyList<string> EntrantOrder { get; } =
        ["random", "contract", "easy-m5", "standard-m5", "easy", "standard-w1", "standard-w2", "standard-w3", "standard-c1", "standard", "standard-plan", "standard-plan-fit", "standard-run", "standard-rez", "standard-w4a", "standard-w4b", "standard-arch", "standard-rarch"];

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
            "easy" or "standard" => OfflineMatchFactory.CreateOpponent(definition, other, entrant),
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
            // A8: the Corp plays to its deck's archetype (v12) with the default Runner.
            "standard-arch" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.V12 : RunnerHeuristicPolicy.PolicyVersion),
            // A8: the Runner plays to its deck's archetype (v11) with the default Corp.
            "standard-rarch" => OfflineMatchFactory.CreateOpponent(definition, other, "standard",
                side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.V11),
            _ => throw new ArgumentException($"Unknown ladder entrant '{entrant}'.", nameof(entrant)),
        };
    }

    public static async Task<LadderGameResult> PlayAsync(LadderGame game, int actionCap = DefaultActionCap,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(game.ContentId, game.GameSeed);
        try
        {
            FullMatchResult result = await FullMatchRunner.RunAsync(definition, actionCap,
                CreateAgent(game.Corp, PlayerSide.Corp, definition),
                CreateAgent(game.Runner, PlayerSide.Runner, definition), cancellationToken).ConfigureAwait(false);
            return new LadderGameResult(game, result.Outcome.Winner == PlayerSide.Corp ? 1 : 0, false, result.ActionCount, null);
        }
        catch (FullMatchStallException stall)
        {
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
