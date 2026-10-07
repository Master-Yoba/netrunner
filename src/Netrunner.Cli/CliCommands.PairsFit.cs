using System.Globalization;
using System.Text.Json;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match.Regret;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// Bot plan S4.2c: ai-fit-evaluation --target pairs --suite SUITE-DIR --end-lines FILE --output-dir DIR [--pairs-per-root N].
    /// The leaf fitted to rank: on the core's training deals, pairs of distinct end states of one root on one sample (common
    /// random numbers) are the examples, the sign of their play-out values' difference the target and its size the weight;
    /// the model is linear on the feature difference with no bias (both orders of every pair), then calibrated by a
    /// one-dimensional fit of bias and slope on the play-out values, so it is a scalar scorer. On the held-out deals it is
    /// read against the <b>reference order</b> (plan 4.2): two lines of a root ordered by their values' mean difference over
    /// every sample, counted where that exceeds twice its standard error. Each arm (set 4 shipped, sets 4 and 5 fitted to
    /// the end-of-line values, sets 4 and 5 fitted to rank) is scored on the state pairs of every sample (the gate) and by
    /// the order of its mean leaf over the samples (the planner's), gap-weighted by the reference difference, with paired
    /// intervals over roots. Beside it: the reference's split-half repeatability, and where the labelling recorded them,
    /// the second play-outs' agreement with the first (repeatability, not a ceiling). Writes evaluation-v5-pairs.json,
    /// evaluation-v4-pairs.json and the report.
    /// </summary>
    private static int RunPairsFit(string[] args, TextWriter output, TextWriter error)
    {
        string? suiteDirectory = null, destination = null, endLines = null, evaluate = null;
        int perRoot = 200;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--target" when index + 1 < args.Length: index++; break;
                    case "--suite" when index + 1 < args.Length: suiteDirectory = args[++index]; break;
                    case "--end-lines" when index + 1 < args.Length: endLines = args[++index]; break;
                    case "--output-dir" when index + 1 < args.Length: destination = args[++index]; break;
                    case "--pairs-per-root" when index + 1 < args.Length:
                        perRoot = Math.Clamp(int.Parse(args[++index], CultureInfo.InvariantCulture), 1, 100_000);
                        break;
                    case "--workers" when index + 1 < args.Length: index++; break;
                    // S4.2c: re-read fitted rankers (a directory holding evaluation-v5-pairs.json and evaluation-v4-pairs.json)
                    // against these rows' reference, without fitting.
                    case "--evaluate" when index + 1 < args.Length: evaluate = args[++index]; break;
                    default: throw new ArgumentException($"Unsupported ai-fit-evaluation --target pairs argument '{args[index]}'.");
                }
            }
            if (suiteDirectory is null || destination is null || endLines is null)
                throw new ArgumentException("--suite, --end-lines and --output-dir are required.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("Fitting needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            error.WriteLine($"ai-fit-evaluation failed: {exception.Message}");
            return 2;
        }
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        HashSet<string> core = [.. suite.Roots.Where(root => root.Categories.Contains(RegretSuite.CoreCategory)).Select(root => root.Id)];
        var behaviors = new Dictionary<string, string>(StringComparer.Ordinal);
        string Behavior(string gameId)
        {
            if (!behaviors.TryGetValue(gameId, out string? version))
                behaviors[gameId] = version = RegretGames.Definition(suite.Games.First(game => game.Id == gameId)).Behavior.Version;
            return version;
        }
        State[] rows = [.. RegretSuite.ReadLines<EndLineRow>(Path.Combine(suiteDirectory, endLines)).Where(row => core.Contains(row.RootId))
            .Select(row => row.Sample < 0 || row.Line is null
                ? throw new InvalidOperationException("The end-of-line rows carry no sample or line ids (S4.2c records them).")
                : new State(row.RootId, row.Split, row.Side, row.Sample, row.Line, row.Ended, row.Features, row.Value, row.Redeal, row.Reseed,
                    Behavior(row.GameId)))];
        int setFour = EvaluationFeatures.NamesFor(4).Count;
        var models = new Dictionary<(PlayerSide Side, int Set), LogisticSideModel>();
        var report = new List<object>();
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            State[] sided = [.. rows.Where(row => row.Side == side)];
            // Training: pairs of a sample's distinct end states (a game that ended inside the turn is scored exactly, not by the leaf).
            var random = new DeterministicRandom(1);
            var pairs = new List<(State A, State B)>();
            foreach (IGrouping<string, State> root in sided.Where(row => row.Split != "held-out").GroupBy(row => row.RootId, StringComparer.Ordinal)
                .OrderBy(root => root.Key, StringComparer.Ordinal))
            {
                List<(State, State)> all = [.. root.Where(row => row.Ended != EndGame).GroupBy(row => row.Sample).OrderBy(sample => sample.Key)
                    .SelectMany(sample => Pairs([.. sample.GroupBy(row => FeatureKey(row.Features)).Select(same => same.First())]))];
                random.Shuffle(all);
                pairs.AddRange(all.Take(perRoot));
            }
            State[] calibrationRows = [.. sided.Where(row => row.Split != "held-out" && row.Ended != EndGame)];
            var calibrations = new Dictionary<int, (double Slope, double Bias)>();
            LogisticSideModel RankOn(Func<State, double[]> features)
            {
                var differences = new List<double[]>(pairs.Count * 2);
                var targets = new List<double>(pairs.Count * 2);
                var weights = new List<double>(pairs.Count * 2);
                foreach ((State a, State b) in pairs)
                {
                    double[] fa = features(a), fb = features(b);
                    double gap = Math.Abs(a.Value - b.Value);
                    differences.Add([.. fa.Select((value, index) => value - fb[index])]);
                    differences.Add([.. fb.Select((value, index) => value - fa[index])]);
                    targets.Add(a.Value > b.Value ? 1 : 0);
                    targets.Add(a.Value > b.Value ? 0 : 1);
                    weights.Add(gap);
                    weights.Add(gap);
                }
                LogisticSideModel ranker = LogisticSideModel.Fit(differences, targets, sampleWeights: weights);
                // The ranker's score of one state, calibrated to the play-out values by bias and slope.
                double Score(State state)
                {
                    double[] values = features(state);
                    double score = 0;
                    for (int index = 0; index < values.Length; index++) score += ranker.Weights[index] * values[index] / ranker.Deviation[index];
                    return score;
                }
                (double slope, double bias) = Calibrate([.. calibrationRows.Select(state => (state.RootId, Score(state), state.Value))]);
                calibrations[features(calibrationRows[0]).Length] = (slope, bias);
                return new LogisticSideModel(new double[ranker.Weights.Length], ranker.Deviation,
                    [.. ranker.Weights.Select(weight => weight * slope)], bias);
            }
            if (evaluate is null)
            {
                models[(side, 4)] = RankOn(state => state.Features[..setFour]);
                models[(side, 5)] = RankOn(state => state.Features);
            }
            else
                foreach ((int set, string file) in new[] { (4, "evaluation-v4-pairs.json"), (5, "evaluation-v5-pairs.json") })
                {
                    FittedEvaluationDocument fitted = FittedEvaluation.Parse(File.ReadAllText(Path.Combine(evaluate, file)));
                    models[(side, set)] = side == PlayerSide.Corp ? fitted.Corp : fitted.Runner;
                }
            LogisticSideModel Shipped(State state) =>
                side == PlayerSide.Corp ? FittedEvaluation.DocumentFor(4, state.Behavior).Corp : FittedEvaluation.DocumentFor(4, state.Behavior).Runner;
            LogisticSideModel Of(int version) => side == PlayerSide.Corp ? FittedEvaluation.DocumentFor(version).Corp : FittedEvaluation.DocumentFor(version).Runner;
            LogisticSideModel valueFive = Of(7), valueFour = Of(8), general = Of(2);
            // A game that ended inside the turn is its result, as the planner scores it.
            (string Name, Func<State, double> Score)[] arms =
            [
                ("set 4, general fit (evaluation-v2)", state => state.Ended == EndGame ? state.Value : general.Probability(state.Features[..setFour])),
                ("set 4, the pool's fit (shipped)", state => state.Ended == EndGame ? state.Value : Shipped(state).Probability(state.Features[..setFour])),
                ("set 4, fitted to the end-of-line values", state => state.Ended == EndGame ? state.Value : valueFour.Probability(state.Features[..setFour])),
                ("set 5, fitted to the end-of-line values", state => state.Ended == EndGame ? state.Value : valueFive.Probability(state.Features)),
                ("set 4, fitted to rank", state => state.Ended == EndGame ? state.Value : models[(side, 4)].Probability(state.Features[..setFour])),
                ("set 5, fitted to rank", state => state.Ended == EndGame ? state.Value : models[(side, 5)].Probability(state.Features)),
            ];
            const int ranked = 5;

            // Per held-out root: each arm's gap-weighted credit against the reference, on the state pairs and by the mean leaf.
            var states = new Dictionary<string, Tally>[arms.Length];
            var means = new Dictionary<string, Tally>[arms.Length];
            for (int arm = 0; arm < arms.Length; arm++) (states[arm], means[arm]) = (new(StringComparer.Ordinal), new(StringComparer.Ordinal));
            Dictionary<string, Tally> halves = new(StringComparer.Ordinal), halvesAll = new(StringComparer.Ordinal);
            Dictionary<string, Tally> redeal = new(StringComparer.Ordinal), reseed = new(StringComparer.Ordinal);
            int reliable = 0, compared = 0;
            foreach (IGrouping<string, State> root in sided.Where(row => row.Split == "held-out").GroupBy(row => row.RootId, StringComparer.Ordinal)
                .OrderBy(root => root.Key, StringComparer.Ordinal))
            {
                // Each line's row on each sample; the lines are compared on the samples they share (every one, S4.2c).
                Dictionary<string, Dictionary<int, State>> lines = root.GroupBy(row => row.Line, StringComparer.Ordinal)
                    .ToDictionary(line => line.Key, line => line.GroupBy(row => row.Sample).ToDictionary(sample => sample.Key, sample => sample.First()),
                        StringComparer.Ordinal);
                var scored = lines.ToDictionary(line => line.Key, line => line.Value.ToDictionary(sample => sample.Key,
                    sample => arms.Select(arm => arm.Score(sample.Value)).ToArray()), StringComparer.Ordinal);
                string[] names = [.. lines.Keys.Order(StringComparer.Ordinal)];
                for (int i = 0; i < names.Length; i++)
                    for (int j = i + 1; j < names.Length; j++)
                    {
                        int[] shared = [.. lines[names[i]].Keys.Intersect(lines[names[j]].Keys).Order()];
                        if (shared.Length < 2) continue;
                        double[] differences = [.. shared.Select(sample => lines[names[i]][sample].Value - lines[names[j]][sample].Value)];
                        double mean = differences.Average();
                        if (mean == 0) continue;
                        compared++;
                        double standardError = Math.Sqrt(differences.Sum(value => (value - mean) * (value - mean)) / (differences.Length - 1) / differences.Length);
                        double gap = Math.Abs(mean);
                        // Split halves of the samples: does the order on the first half match the order on the second?
                        int half = shared.Length / 2;
                        double first = differences[..half].Average(), second = differences[half..].Average();
                        Tally.Add(halvesAll, root.Key, gap, Credit(first, second));
                        if (gap <= 2 * standardError) continue;
                        reliable++;
                        Tally.Add(halves, root.Key, gap, Credit(first, second));
                        // The state pairs leave out a sample where either line's game ended inside the turn (scored exactly).
                        int[] open = [.. shared.Where(sample => lines[names[i]][sample].Ended != EndGame && lines[names[j]][sample].Ended != EndGame)];
                        for (int arm = 0; arm < arms.Length; arm++)
                        {
                            if (open.Length > 0)
                                Tally.Add(states[arm], root.Key, gap, open.Average(sample => Credit(mean, scored[names[i]][sample][arm] - scored[names[j]][sample][arm])));
                            Tally.Add(means[arm], root.Key, gap, Credit(mean,
                                shared.Average(sample => scored[names[i]][sample][arm]) - shared.Average(sample => scored[names[j]][sample][arm])));
                        }
                        // The second play-outs, where recorded: does their mean order match the reference's?
                        if (shared.All(sample => lines[names[i]][sample].Redeal is not null && lines[names[j]][sample].Redeal is not null))
                        {
                            Tally.Add(redeal, root.Key, gap, Credit(mean, shared.Average(sample => lines[names[i]][sample].Redeal!.Value - lines[names[j]][sample].Redeal!.Value)));
                            Tally.Add(reseed, root.Key, gap, Credit(mean, shared.Average(sample => lines[names[i]][sample].Reseed!.Value - lines[names[j]][sample].Reseed!.Value)));
                        }
                    }
            }
            string[] roots = [.. means[0].Keys.Order(StringComparer.Ordinal)];
            object Interval(Dictionary<string, Tally> of, string[] over)
            {
                (double Mean, double Lower, double Upper) range = Bootstrap(over, root => of.TryGetValue(root, out Tally tally) ? (tally.Credit, tally.Weight) : (0, 0));
                return new { mean = range.Mean, lower = range.Lower, upper = range.Upper };
            }
            object[] accuracy = [.. arms.Select((arm, index) => (object)new { arm = arm.Name, meanLeaf = Interval(means[index], roots), statePairs = Interval(states[index], roots) })];
            Tally Of0(Dictionary<string, Tally> of, string root) => of.GetValueOrDefault(root);
            object[] paired = [.. Enumerable.Range(0, ranked).Select(other =>
            {
                (double Mean, double Lower, double Upper) state = Bootstrap(roots, root => (Of0(states[ranked], root).Credit - Of0(states[other], root).Credit, Of0(states[ranked], root).Weight));
                (double Mean, double Lower, double Upper) leaf = Bootstrap(roots, root => (Of0(means[ranked], root).Credit - Of0(means[other], root).Credit, Of0(means[ranked], root).Weight));
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{side,-6} set 5 ranked - {arms[other].Name}: state pairs {state.Mean:+0.0000;-0.0000} [{state.Lower:+0.0000;-0.0000}, {state.Upper:+0.0000;-0.0000}], mean leaf {leaf.Mean:+0.0000;-0.0000} [{leaf.Lower:+0.0000;-0.0000}, {leaf.Upper:+0.0000;-0.0000}]"));
                return (object)new
                {
                    against = arms[other].Name,
                    statePairs = new { mean = state.Mean, lower = state.Lower, upper = state.Upper },
                    meanLeaf = new { mean = leaf.Mean, lower = leaf.Lower, upper = leaf.Upper },
                };
            })];
            // The test of the 4-turn reference's confound: the pool's fit against the general fit, the same features.
            (double Mean, double Lower, double Upper) poolLeaf = Bootstrap(roots, root => (Of0(means[1], root).Credit - Of0(means[0], root).Credit, Of0(means[1], root).Weight));
            (double Mean, double Lower, double Upper) poolState = Bootstrap(roots, root => (Of0(states[1], root).Credit - Of0(states[0], root).Credit, Of0(states[1], root).Weight));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{side,-6} pool's fit - general fit: mean leaf {poolLeaf.Mean:+0.0000;-0.0000} [{poolLeaf.Lower:+0.0000;-0.0000}, {poolLeaf.Upper:+0.0000;-0.0000}], state pairs {poolState.Mean:+0.0000;-0.0000} [{poolState.Lower:+0.0000;-0.0000}, {poolState.Upper:+0.0000;-0.0000}]"));
            string[] repeated = [.. redeal.Keys.Order(StringComparer.Ordinal)];
            object repeatability = new
            {
                splitHalvesReliable = Interval(halves, roots),
                splitHalvesAll = Interval(halvesAll, [.. halvesAll.Keys.Order(StringComparer.Ordinal)]),
                secondPlayOutRoots = repeated.Length,
                redealt = repeated.Length == 0 ? null : Interval(redeal, repeated),
                reseeded = repeated.Length == 0 ? null : Interval(reseed, repeated),
            };
            // Calibration: log loss of the play-out values on the held-out states the leaf scores, reported beside.
            State[] heldStates = [.. sided.Where(row => row.Split == "held-out" && row.Ended != EndGame)];
            object[] calibration = [.. arms.Select(arm => (object)new { arm = arm.Name, logLoss = heldStates.Average(state => SoftLoss(arm.Score(state), state.Value)) })];
            // A monotone calibration of the set 5 ranker over all training rows (isotonic, so its order is kept), on these values.
            LogisticSideModel rankFive = models[(side, 5)];
            Func<double, double> isotonic = Isotonic([.. sided.Where(row => row.Split != "held-out" && row.Ended != EndGame)
                .Select(row => (Logit(rankFive, row.Features), row.Value))]);
            double isotonicLoss = heldStates.Average(state => SoftLoss(isotonic(Logit(rankFive, state.Features)), state.Value));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{side,-6} log loss: set 5 ranked, isotonic {isotonicLoss:0.0000}; as fitted {heldStates.Average(state => SoftLoss(arms[ranked].Score(state), state.Value)):0.0000}; set 5 value-fitted {heldStates.Average(state => SoftLoss(arms[3].Score(state), state.Value)):0.0000}"));
            int nonBinary = sided.Count(row => row.Ended != EndGame && row.Value is not (0 or 1));
            report.Add(new
            {
                side = side.ToString().ToLowerInvariant(),
                trainPairs = pairs.Count,
                heldOutRoots = roots.Length,
                linePairs = compared,
                reliableLinePairs = reliable,
                accuracy,
                paired,
                repeatability,
                calibration,
                isotonicLogLoss = isotonicLoss,
                rowsScoredByTheLeaf = nonBinary,
                poolOverGeneral = new
                {
                    meanLeaf = new { mean = poolLeaf.Mean, lower = poolLeaf.Lower, upper = poolLeaf.Upper },
                    statePairs = new { mean = poolState.Mean, lower = poolState.Lower, upper = poolState.Upper },
                },
                calibrationSlope = evaluate is null
                    ? new { set4 = calibrations[setFour].Slope, set5 = calibrations[EvaluationFeatures.NamesFor(5).Count].Slope } : null,
            });
            output.WriteLine(JsonSerializer.Serialize(new { side = side.ToString(), linePairs = compared, reliableLinePairs = reliable, accuracy, repeatability },
                FitJsonOptions).ReplaceLineEndings(" "));
        }
        Directory.CreateDirectory(destination);
        if (evaluate is not null)
        {
            File.WriteAllText(Path.Combine(destination, "pairs-reread.report.json"), JsonSerializer.Serialize(
                new { schemaVersion = 1, target = "pairs", evaluate, suite = suiteDirectory, endLines, sides = report }, FitJsonOptions) + "\n");
            output.WriteLine($"ai-fit-evaluation pairs re-read rows={rows.Length} output={destination}");
            return 0;
        }
        string provenance = string.Create(CultureInfo.InvariantCulture,
            $"Fitted by ai-fit-evaluation --target pairs (bot plan S4.2c) on the training deals' end-of-line rows of {Path.GetFileName(Path.GetFullPath(suiteDirectory))}'s core ({endLines}, teacher v2): pairs of distinct end states of one root on one sample, at most {perRoot} a root, ranked by the sign of their play-out values' difference weighted by its size, linear on the feature difference, then calibrated by bias and slope on the play-out values.");
        File.WriteAllText(Path.Combine(destination, "evaluation-v5-pairs.json"), JsonSerializer.Serialize(new FittedEvaluationDocument(1, "evaluation-v5-pairs",
            EvaluationFeatures.NamesFor(5), models[(PlayerSide.Corp, 5)], models[(PlayerSide.Runner, 5)], provenance), FitJsonOptions) + "\n");
        File.WriteAllText(Path.Combine(destination, "evaluation-v4-pairs.json"), JsonSerializer.Serialize(new FittedEvaluationDocument(1, "evaluation-v4-pairs",
            EvaluationFeatures.NamesFor(4), models[(PlayerSide.Corp, 4)], models[(PlayerSide.Runner, 4)], provenance), FitJsonOptions) + "\n");
        File.WriteAllText(Path.Combine(destination, "evaluation-v5-pairs.report.json"), JsonSerializer.Serialize(
            new { schemaVersion = 1, target = "pairs", suite = suiteDirectory, endLines, pairsPerRoot = perRoot, sides = report }, FitJsonOptions) + "\n");
        output.WriteLine($"ai-fit-evaluation pairs rows={rows.Length} output={destination}");
        return 0;
    }

    private const string EndGame = "game";

    /// <summary>A logistic model's logit of a state: its score before the sigmoid.</summary>
    private static double Logit(LogisticSideModel model, double[] features)
    {
        double logit = model.Bias;
        for (int index = 0; index < model.Weights.Length; index++) logit += model.Weights[index] * (features[index] - model.Mean[index]) / model.Deviation[index];
        return logit;
    }

    /// <summary>
    /// A monotone non-decreasing map from a score to a probability, fitted by pool-adjacent-violators to the values, read
    /// as a step function (clamped to [0.001, 0.999]); its order is the score's.
    /// </summary>
    private static Func<double, double> Isotonic(IReadOnlyList<(double Score, double Value)> rows)
    {
        (double Score, double Value)[] sorted = [.. rows.OrderBy(row => row.Score)];
        var blocks = new List<(double Last, double Sum, int Count)>();
        foreach ((double score, double value) in sorted)
        {
            blocks.Add((score, value, 1));
            while (blocks.Count > 1 && blocks[^2].Sum / blocks[^2].Count > blocks[^1].Sum / blocks[^1].Count)
            {
                (double last, double sum, int count) = blocks[^1];
                blocks.RemoveAt(blocks.Count - 1);
                blocks[^1] = (last, blocks[^1].Sum + sum, blocks[^1].Count + count);
            }
        }
        double[] ends = [.. blocks.Select(block => block.Last)];
        double[] levels = [.. blocks.Select(block => Math.Clamp(block.Sum / block.Count, 0.001, 0.999))];
        return score =>
        {
            int at = Array.BinarySearch(ends, score);
            if (at < 0) at = Math.Min(~at, ends.Length - 1);
            return levels[at];
        };
    }

    /// <summary>
    /// The ranker's score calibrated to a win probability, σ(bias + slope · score), without disturbing its order within a
    /// root: the slope from the score's least-squares fit to the play-out values within each root (each root's means taken
    /// out, so the level across roots, which a ranker does not model, cannot turn it negative), put on the logistic scale at
    /// the mean value and never below zero; the bias so the mean predicted value is the mean play-out value.
    /// </summary>
    private static (double Slope, double Bias) Calibrate(IReadOnlyList<(string Root, double Score, double Value)> rows)
    {
        double covariance = 0, variance = 0;
        foreach (IGrouping<string, (string Root, double Score, double Value)> root in rows.GroupBy(row => row.Root, StringComparer.Ordinal))
        {
            double score = root.Average(row => row.Score), value = root.Average(row => row.Value);
            foreach ((_, double s, double v) in root)
            {
                covariance += (s - score) * (v - value);
                variance += (s - score) * (s - score);
            }
        }
        double mean = rows.Average(row => row.Value);
        double slope = variance <= 0 ? 0 : Math.Max(0, covariance / variance / Math.Max(1e-6, mean * (1 - mean)));
        double Predicted(double bias) => rows.Average(row => 1 / (1 + Math.Exp(-(bias + slope * row.Score))));
        double low = -50, high = 50;
        for (int step = 0; step < 100; step++)
        {
            double middle = (low + high) / 2;
            if (Predicted(middle) < mean) low = middle; else high = middle;
        }
        return (slope, (low + high) / 2);
    }

    /// <summary>Credit for an order: 1 when <paramref name="guess"/> has the reference's sign, 0 when not, a half for a tie.</summary>
    private static double Credit(double reference, double guess) => guess == 0 ? 0.5 : Math.Sign(reference) == Math.Sign(guess) ? 1 : 0;

    private static string FeatureKey(double[] features) => string.Join(',', features.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

    /// <summary>A ratio of sums over roots, and its 95% bootstrap interval resampling roots (2,000 draws).</summary>
    private static (double Mean, double Lower, double Upper) Bootstrap(string[] roots, Func<string, (double Value, double Weight)> of)
    {
        (double Value, double Weight)[] items = [.. roots.Select(of)];
        double total = items.Sum(item => item.Weight);
        if (items.Length == 0 || total == 0) return (0, 0, 0);
        var draws = new DeterministicRandom(1);
        double[] means = [.. Enumerable.Range(0, 2000).Select(_ =>
        {
            double value = 0, weight = 0;
            for (int draw = 0; draw < items.Length; draw++)
            {
                (double v, double w) = items[draws.NextInt(items.Length)];
                value += v;
                weight += w;
            }
            return weight == 0 ? 0 : value / weight;
        }).Order()];
        return (items.Sum(item => item.Value) / total, means[50], means[1949]);
    }

    /// <summary>A root's gap-weighted credit and weight.</summary>
    private readonly record struct Tally(double Credit, double Weight)
    {
        public static void Add(Dictionary<string, Tally> into, string root, double gap, double credit)
        {
            Tally tally = into.GetValueOrDefault(root);
            into[root] = new Tally(tally.Credit + gap * credit, tally.Weight + gap);
        }
    }

    /// <summary>Every pair of a sample's distinct end states whose play-out values differ.</summary>
    private static IEnumerable<(State A, State B)> Pairs(State[] group)
    {
        for (int i = 0; i < group.Length; i++)
            for (int j = i + 1; j < group.Length; j++)
                if (group[i].Value != group[j].Value) yield return (group[i], group[j]);
    }

    /// <summary>One line's end state on one sample: its root, split, side, sample, line, where it ended, set 5, the play-out's
    /// value, the second play-outs' where recorded, and its pool.</summary>
    private sealed record State(string RootId, string Split, PlayerSide Side, int Sample, string Line, string Ended, double[] Features,
        double Value, double? Redeal, double? Reseed, string Behavior);
}
