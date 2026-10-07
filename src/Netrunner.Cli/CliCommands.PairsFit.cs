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
    /// one-dimensional fit of bias and slope on the play-out values. Reports, on the held-out deals' pairs, the gap-weighted
    /// within-root pairwise accuracy and the accuracy over pairs more than 0.1 apart of four arms (set 4 shipped, set 4 and
    /// set 5 fitted to rank, set 5 fitted to the end-of-line values), paired intervals over roots, and the ceiling: how
    /// often a second play-out of the same pairs agrees on their order, with the hidden state dealt again and with only the
    /// engine's random stream reseeded (the rows <c>ai-regret label --end-lines-repeat</c> recorded). Writes
    /// evaluation-v5-pairs.json, evaluation-v4-pairs.json and the report.
    /// </summary>
    private static int RunPairsFit(string[] args, TextWriter output, TextWriter error)
    {
        string? suiteDirectory = null, destination = null, endLines = null;
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
        EndLineRow[] rows = [.. RegretSuite.ReadLines<EndLineRow>(Path.Combine(suiteDirectory, endLines)).Where(row => core.Contains(row.RootId))];
        if (rows.Any(row => row.Sample < 0)) throw new InvalidOperationException("The end-of-line rows carry no sample ids (S4.2c records them).");
        int setFour = EvaluationFeatures.NamesFor(4).Count;
        var models = new Dictionary<(PlayerSide Side, int Set), LogisticSideModel>();
        var report = new List<object>();
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            // One end state of each distinct position a sample reached (two lines leaving the turn alike are one state).
            State[][] groups = [.. rows.Where(row => row.Side == side)
                .GroupBy(row => (row.RootId, row.Sample))
                .OrderBy(group => group.Key.RootId, StringComparer.Ordinal).ThenBy(group => group.Key.Sample)
                .Select(group => group.GroupBy(row => string.Join(',', row.Features.Select(value => value.ToString("R", CultureInfo.InvariantCulture))))
                    .Select(same => same.First())
                    .Select(row => new State(row.RootId, row.Split, row.Features, row.Value, row.Redeal, row.Reseed, Behavior(row.GameId)))
                    .ToArray())];
            State[][] train = [.. groups.Where(group => group[0].Split != "held-out")];
            State[][] held = [.. groups.Where(group => group[0].Split == "held-out")];

            // Training pairs, at most perRoot a root, drawn on one stream in the roots' order.
            var random = new DeterministicRandom(1);
            var pairs = new List<(State A, State B)>();
            foreach (IGrouping<string, State[]> root in train.GroupBy(group => group[0].RootId, StringComparer.Ordinal))
            {
                List<(State, State)> all = [.. root.SelectMany(Pairs)];
                random.Shuffle(all);
                pairs.AddRange(all.Take(perRoot));
            }
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
                State[] states = [.. train.SelectMany(group => group)];
                LogisticSideModel calibration = LogisticSideModel.Fit([.. states.Select(state => (double[])[Score(state)])], [.. states.Select(state => state.Value)]);
                double slope = calibration.Weights[0] / calibration.Deviation[0];
                return new LogisticSideModel(new double[ranker.Weights.Length], ranker.Deviation,
                    [.. ranker.Weights.Select(weight => weight * slope)], calibration.Bias - slope * calibration.Mean[0]);
            }
            models[(side, 4)] = RankOn(state => state.Features[..setFour]);
            models[(side, 5)] = RankOn(state => state.Features);
            LogisticSideModel Shipped(State state) =>
                side == PlayerSide.Corp ? FittedEvaluation.DocumentFor(4, state.Behavior).Corp : FittedEvaluation.DocumentFor(4, state.Behavior).Runner;
            LogisticSideModel valueFit = side == PlayerSide.Corp ? FittedEvaluation.DocumentFor(7).Corp : FittedEvaluation.DocumentFor(7).Runner;
            (string Name, Func<State, double> Score)[] arms =
            [
                ("set 4, shipped weights", state => Shipped(state).Probability(state.Features[..setFour])),
                ("set 4, fitted to rank", state => models[(side, 4)].Probability(state.Features[..setFour])),
                ("set 5, fitted to rank", state => models[(side, 5)].Probability(state.Features)),
                ("set 5, fitted to the end-of-line values", state => valueFit.Probability(state.Features)),
            ];
            // Per root: each arm's gap-weighted correct and its weight, and its correct and count over pairs more than 0.1 apart.
            var perArm = arms.Select(_ => new Dictionary<string, (double Correct, double Weight, double Wide, double Count)>(StringComparer.Ordinal)).ToArray();
            var ceilings = new Dictionary<string, Dictionary<string, (double Correct, double Weight, double Wide, double Count)>>(StringComparer.Ordinal)
            {
                ["redeal"] = new(StringComparer.Ordinal),
                ["reseed"] = new(StringComparer.Ordinal),
            };
            var onCeilingRoots = arms.Select(_ => new Dictionary<string, (double Correct, double Weight, double Wide, double Count)>(StringComparer.Ordinal)).ToArray();
            static void Add(Dictionary<string, (double Correct, double Weight, double Wide, double Count)> into, string root, double gap, double credit)
            {
                (double correct, double weight, double wide, double count) = into.GetValueOrDefault(root);
                into[root] = (correct + gap * credit, weight + gap, wide + (gap > 0.1 ? credit : 0), count + (gap > 0.1 ? 1 : 0));
            }
            static double Credit(double truth, double guess) => guess == 0 ? 0.5 : Math.Sign(truth) == Math.Sign(guess) ? 1 : 0;
            foreach (State[] group in held)
            {
                double[][] scores = [.. arms.Select(arm => group.Select(arm.Score).ToArray())];
                bool repeated = group.All(state => state.Redeal is not null && state.Reseed is not null);
                for (int i = 0; i < group.Length; i++)
                    for (int j = i + 1; j < group.Length; j++)
                    {
                        double difference = group[i].Value - group[j].Value;
                        if (difference == 0) continue;
                        double gap = Math.Abs(difference);
                        string root = group[i].RootId;
                        for (int arm = 0; arm < arms.Length; arm++)
                        {
                            double credit = Credit(difference, scores[arm][i] - scores[arm][j]);
                            Add(perArm[arm], root, gap, credit);
                            if (repeated) Add(onCeilingRoots[arm], root, gap, credit);
                        }
                        if (!repeated) continue;
                        Add(ceilings["redeal"], root, gap, Credit(difference, group[i].Redeal!.Value - group[j].Redeal!.Value));
                        Add(ceilings["reseed"], root, gap, Credit(difference, group[i].Reseed!.Value - group[j].Reseed!.Value));
                    }
            }
            string[] roots = [.. perArm[0].Keys.Order(StringComparer.Ordinal)];
            (double Mean, double Lower, double Upper) Interval(Func<string, (double Value, double Weight)> of, string[] over)
            {
                (double Value, double Weight)[] items = [.. over.Select(of)];
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
                double total = items.Sum(item => item.Weight);
                return (total == 0 ? 0 : items.Sum(item => item.Value) / total, means[50], means[1949]);
            }
            object Accuracy(Dictionary<string, (double Correct, double Weight, double Wide, double Count)> of, string[] over)
            {
                var weighted = Interval(root => (of.GetValueOrDefault(root).Correct, of.GetValueOrDefault(root).Weight), over);
                var wide = Interval(root => (of.GetValueOrDefault(root).Wide, of.GetValueOrDefault(root).Count), over);
                return new
                {
                    gapWeighted = new { mean = weighted.Mean, lower = weighted.Lower, upper = weighted.Upper },
                    overTenth = new { mean = wide.Mean, lower = wide.Lower, upper = wide.Upper, pairs = over.Sum(root => of.GetValueOrDefault(root).Count) },
                };
            }
            object[] accuracy = [.. arms.Select((arm, index) => (object)new { arm = arm.Name, accuracy = Accuracy(perArm[index], roots) })];
            // Set 5 fitted to rank against each other arm, paired root by root (gap-weighted).
            object[] paired = [.. OtherArms.Select(other =>
            {
                var difference = Interval(root => (perArm[2][root].Correct - perArm[other][root].Correct, perArm[2][root].Weight), roots);
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{side,-6} set 5 ranked - {arms[other].Name}: gap-weighted accuracy {difference.Mean:+0.0000;-0.0000} [{difference.Lower:+0.0000;-0.0000}, {difference.Upper:+0.0000;-0.0000}]"));
                return (object)new { against = arms[other].Name, gapWeighted = new { mean = difference.Mean, lower = difference.Lower, upper = difference.Upper } };
            })];
            string[] ceilingRoots = [.. ceilings["redeal"].Keys.Order(StringComparer.Ordinal)];
            // Two draws that each order a pair correctly with probability p agree with probability p² + (1 − p)².
            static double Implied(double agreement) => (1 + Math.Sqrt(Math.Max(0, 2 * agreement - 1))) / 2;
            object ceiling = new
            {
                roots = ceilingRoots.Length,
                redeal = Accuracy(ceilings["redeal"], ceilingRoots),
                reseed = Accuracy(ceilings["reseed"], ceilingRoots),
                arms = arms.Select((arm, index) => new { arm = arm.Name, accuracy = Accuracy(onCeilingRoots[index], ceilingRoots) }).ToArray(),
            };
            // Calibration: log loss of the play-out values on the held-out states, reported beside.
            State[] heldStates = [.. held.SelectMany(group => group)];
            object[] calibration = [.. arms.Select(arm => (object)new { arm = arm.Name, logLoss = heldStates.Average(state => SoftLoss(arm.Score(state), state.Value)) })];
            report.Add(new
            {
                side = side.ToString().ToLowerInvariant(),
                trainPairs = pairs.Count,
                heldOutRoots = roots.Length,
                accuracy,
                paired,
                ceiling,
                calibration,
            });
            output.WriteLine(JsonSerializer.Serialize(new { side = side.ToString(), accuracy, ceiling }, FitJsonOptions)
                .ReplaceLineEndings(" "));
            if (ceilingRoots.Length > 0)
            {
                double redeal = ceilings["redeal"].Values.Sum(value => value.Correct) / ceilings["redeal"].Values.Sum(value => value.Weight);
                double reseed = ceilings["reseed"].Values.Sum(value => value.Correct) / ceilings["reseed"].Values.Sum(value => value.Weight);
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{side,-6} ceiling over {ceilingRoots.Length} roots: re-dealt agreement {redeal:0.000} (one draw at most {Implied(redeal):0.000}), reseeded {reseed:0.000} (at most {Implied(reseed):0.000})"));
            }
        }
        Directory.CreateDirectory(destination);
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

    /// <summary>The arms set 5 fitted to rank (arm 2) is compared with: set 4 shipped, set 4 ranked, set 5 value-fitted.</summary>
    private static readonly int[] OtherArms = [0, 1, 3];

    /// <summary>Every pair of a sample's distinct end states whose play-out values differ.</summary>
    private static IEnumerable<(State A, State B)> Pairs(State[] group)
    {
        for (int i = 0; i < group.Length; i++)
            for (int j = i + 1; j < group.Length; j++)
                if (group[i].Value != group[j].Value) yield return (group[i], group[j]);
    }

    /// <summary>One distinct end state of a sample: its root, split, set 5, play-out value, the ceiling's repeats and its pool.</summary>
    private sealed record State(string RootId, string Split, double[] Features, double Value, double? Redeal, double? Reseed, string Behavior);
}
