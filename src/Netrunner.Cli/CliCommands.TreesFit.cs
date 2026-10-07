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
    /// Bot plan S4.2c, the nonlinear fallback: ai-fit-evaluation --target trees --suite SUITE-DIR --end-lines FILE --output-dir DIR
    /// [--trees N] [--depth D] [--learning-rate R] [--pairs-per-root N] [--compare NAME=FITTED.json ...] [--read NAME=ROWS ...].
    /// Fits the scalar boosted trees <c>g(set 5)</c> per side with the pairwise gradient on the training deals' pairs of a
    /// sample's distinct end states (as <c>--target pairs</c> forms them), calibrates them monotonically on every training
    /// row, times their inference, and reads them against each reference (the fit's rows, and each <c>--read</c> rows
    /// file) beside the general fit, the pool's fit and every <c>--compare</c> fit: gap-weighted accuracy against the
    /// reference order by the mean leaf (the planner's view) and on the state pairs, with the trees' paired differences,
    /// and the held-out log loss. Writes evaluation-v5-trees.json and its report.
    /// </summary>
    private static int RunTreesFit(string[] args, TextWriter output, TextWriter error)
    {
        string? suiteDirectory = null, destination = null, endLines = null;
        int perRoot = 200, trees = 200, depth = 3;
        double learningRate = 0.1;
        var compare = new List<(string Name, string Path)>();
        var reads = new List<(string Name, string Path)>();
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
                (string, string) Named(string value) => value.IndexOf('=', StringComparison.Ordinal) is int at and > 0
                    ? (value[..at], value[(at + 1)..]) : throw new ArgumentException($"'{value}' is not NAME=PATH.");
                switch (args[index])
                {
                    case "--target": Next(); break;
                    case "--suite": suiteDirectory = Next(); break;
                    case "--end-lines": endLines = Next(); break;
                    case "--output-dir": destination = Next(); break;
                    case "--pairs-per-root": perRoot = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 100_000); break;
                    case "--trees": trees = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 5_000); break;
                    case "--depth": depth = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 8); break;
                    case "--learning-rate": learningRate = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--compare": compare.Add(Named(Next())); break;
                    case "--read": reads.Add(Named(Next())); break;
                    case "--workers": Next(); break;
                    default: throw new ArgumentException($"Unsupported ai-fit-evaluation --target trees argument '{args[index]}'.");
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
        Func<string, State[]> load = CoreStates(suite);
        State[] rows = load(Path.Combine(suiteDirectory, endLines));
        var readRows = new List<(string Name, State[] Rows)> { ("fit's rows", rows) };
        readRows.AddRange(reads.Select(read => (read.Name, load(read.Path))));
        FittedEvaluationDocument[] compared = [.. compare.Select(entry => FittedEvaluation.Parse(File.ReadAllText(entry.Path)))];
        var models = new Dictionary<PlayerSide, BoostedTreeScorer>();
        var report = new List<object>();
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            State[] sided = [.. rows.Where(row => row.Side == side)];
            // The training pairs, as --target pairs forms them: a sample's distinct end states, at most perRoot a root.
            var random = new DeterministicRandom(1);
            var states = new List<double[]>();
            var indexes = new Dictionary<State, int>(ReferenceEqualityComparer.Instance);
            var pairs = new List<(int Better, int Worse)>();
            var weights = new List<double>();
            int IndexOf(State state)
            {
                if (!indexes.TryGetValue(state, out int index))
                {
                    indexes[state] = index = states.Count;
                    states.Add(state.Features);
                }
                return index;
            }
            foreach (IGrouping<string, State> root in sided.Where(row => row.Split != "held-out").GroupBy(row => row.RootId, StringComparer.Ordinal)
                .OrderBy(root => root.Key, StringComparer.Ordinal))
            {
                List<(State, State)> all = [.. root.Where(row => row.Ended != EndGame).GroupBy(row => row.Sample).OrderBy(sample => sample.Key)
                    .SelectMany(sample => Pairs([.. sample.GroupBy(row => FeatureKey(row.Features)).Select(same => same.First())]))];
                random.Shuffle(all);
                foreach ((State a, State b) in all.Take(perRoot))
                {
                    pairs.Add(a.Value > b.Value ? (IndexOf(a), IndexOf(b)) : (IndexOf(b), IndexOf(a)));
                    weights.Add(Math.Abs(a.Value - b.Value));
                }
            }
            State[] calibrationRows = [.. sided.Where(row => row.Split != "held-out" && row.Ended != EndGame)];
            var fitting = System.Diagnostics.Stopwatch.StartNew();
            BoostedTreeScorer model = BoostedTreeScorer.Fit(states, pairs, weights,
                fitted => [.. calibrationRows.Select(row => (fitted.Score(row.Features), row.Value))], trees, depth, learningRate);
            fitting.Stop();
            models[side] = model;
            // Inference cost: a held-out state's score and calibration, on one thread.
            State[] timed = [.. sided.Where(row => row.Split == "held-out" && row.Ended != EndGame).Take(20_000)];
            double sink = 0;
            foreach (State state in timed.Take(1_000)) sink += model.Probability(state.Features);
            var timing = System.Diagnostics.Stopwatch.StartNew();
            foreach (State state in timed) sink += model.Probability(state.Features);
            timing.Stop();
            double microseconds = timing.Elapsed.TotalMicroseconds / Math.Max(1, timed.Length);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{side,-6} trees: {pairs.Count} pairs over {states.Count} states, fitted in {fitting.Elapsed.TotalSeconds:F0} s; inference {microseconds:F2} µs a state ({sink:F0})"));

            int setFour = EvaluationFeatures.NamesFor(4).Count;
            LogisticSideModel general = side == PlayerSide.Corp ? FittedEvaluation.DocumentFor(2).Corp : FittedEvaluation.DocumentFor(2).Runner;
            LogisticSideModel Pool(State state) =>
                side == PlayerSide.Corp ? FittedEvaluation.DocumentFor(4, state.Behavior).Corp : FittedEvaluation.DocumentFor(4, state.Behavior).Runner;
            var arms = new List<(string Name, Func<State, double> Score)>
            {
                ("set 4, general fit", state => state.Ended == EndGame ? state.Value : general.Probability(state.Features[..setFour])),
                ("set 4, the pool's fit (shipped)", state => state.Ended == EndGame ? state.Value : Pool(state).Probability(state.Features[..setFour])),
            };
            for (int at = 0; at < compared.Length; at++)
            {
                LogisticSideModel fit = side == PlayerSide.Corp ? compared[at].Corp : compared[at].Runner;
                int width = fit.Weights.Length;
                arms.Add((compare[at].Name, state => state.Ended == EndGame ? state.Value : fit.Probability(state.Features[..width])));
            }
            arms.Add(("set 5, boosted trees", state => state.Ended == EndGame ? state.Value : model.Probability(state.Features)));
            var readings = new List<object>();
            foreach ((string name, State[] read) in readRows)
            {
                State[] held = [.. read.Where(row => row.Side == side && row.Split == "held-out")];
                readings.Add(ReadAgainstReference(held, arms, output, side, name));
            }
            report.Add(new
            {
                side = side.ToString().ToLowerInvariant(),
                trainPairs = pairs.Count,
                trainStates = states.Count,
                fitSeconds = fitting.Elapsed.TotalSeconds,
                inferenceMicroseconds = microseconds,
                readings,
            });
        }
        Directory.CreateDirectory(destination);
        string provenance = string.Create(CultureInfo.InvariantCulture,
            $"Fitted by ai-fit-evaluation --target trees (bot plan S4.2c) on the training deals' end-of-line rows of {Path.GetFileName(Path.GetFullPath(suiteDirectory))}'s core ({endLines}): {trees} trees of depth {depth}, learning rate {learningRate}, on pairs of a sample's distinct end states (at most {perRoot} a root) with the pairwise gradient, calibrated isotonically on every training row.");
        File.WriteAllText(Path.Combine(destination, "evaluation-v5-trees.json"), JsonSerializer.Serialize(new TreeEvaluationDocument(1, "evaluation-v5-trees",
            EvaluationFeatures.NamesFor(5), models[PlayerSide.Corp], models[PlayerSide.Runner], provenance), FitJsonOptions) + "\n");
        File.WriteAllText(Path.Combine(destination, "evaluation-v5-trees.report.json"), JsonSerializer.Serialize(
            new { schemaVersion = 1, target = "trees", suite = suiteDirectory, endLines, trees, depth, learningRate, pairsPerRoot = perRoot, sides = report },
            FitJsonOptions) + "\n");
        output.WriteLine($"ai-fit-evaluation trees rows={rows.Length} output={destination}");
        return 0;
    }

    /// <summary>A reader of a rows file's core rows, each with its game's pool.</summary>
    private static Func<string, State[]> CoreStates(RegretSuite suite)
    {
        HashSet<string> core = [.. suite.Roots.Where(root => root.Categories.Contains(RegretSuite.CoreCategory)).Select(root => root.Id)];
        var behaviors = new Dictionary<string, string>(StringComparer.Ordinal);
        string Behavior(string gameId)
        {
            if (!behaviors.TryGetValue(gameId, out string? version))
                behaviors[gameId] = version = RegretGames.Definition(suite.Games.First(game => game.Id == gameId)).Behavior.Version;
            return version;
        }
        return path => [.. RegretSuite.ReadLines<EndLineRow>(path).Where(row => core.Contains(row.RootId))
            .Select(row => row.Sample < 0 || row.Line is null
                ? throw new InvalidOperationException("The end-of-line rows carry no sample or line ids (S4.2c records them).")
                : new State(row.RootId, row.Split, row.Side, row.Sample, row.Line, row.Ended, row.Features, row.Value, row.Redeal, row.Reseed,
                    Behavior(row.GameId)))];
    }

    /// <summary>
    /// The arms against the reference order of <paramref name="held"/>'s lines (S4.2c): gap-weighted accuracy by the mean
    /// leaf over the samples and on the state pairs (without samples whose game ended in the turn), with the last arm's
    /// paired differences against each other, intervals over roots, and the arms' log loss on the held-out values.
    /// </summary>
    private static object ReadAgainstReference(State[] held, List<(string Name, Func<State, double> Score)> arms, TextWriter output,
        PlayerSide side, string reference)
    {
        var states = new Dictionary<string, Tally>[arms.Count];
        var means = new Dictionary<string, Tally>[arms.Count];
        for (int arm = 0; arm < arms.Count; arm++) (states[arm], means[arm]) = (new(StringComparer.Ordinal), new(StringComparer.Ordinal));
        int counted = 0;
        foreach (IGrouping<string, State> root in held.GroupBy(row => row.RootId, StringComparer.Ordinal).OrderBy(root => root.Key, StringComparer.Ordinal))
        {
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
                    double standardError = Math.Sqrt(differences.Sum(value => (value - mean) * (value - mean)) / (differences.Length - 1) / differences.Length);
                    double gap = Math.Abs(mean);
                    if (gap <= 2 * standardError) continue;
                    counted++;
                    int[] open = [.. shared.Where(sample => lines[names[i]][sample].Ended != EndGame && lines[names[j]][sample].Ended != EndGame)];
                    for (int arm = 0; arm < arms.Count; arm++)
                    {
                        if (open.Length > 0)
                            Tally.Add(states[arm], root.Key, gap, open.Average(sample => Credit(mean, scored[names[i]][sample][arm] - scored[names[j]][sample][arm])));
                        Tally.Add(means[arm], root.Key, gap, Credit(mean,
                            shared.Average(sample => scored[names[i]][sample][arm]) - shared.Average(sample => scored[names[j]][sample][arm])));
                    }
                }
        }
        string[] roots = [.. means[0].Keys.Order(StringComparer.Ordinal)];
        static object Range((double Mean, double Lower, double Upper) range) => new { mean = range.Mean, lower = range.Lower, upper = range.Upper };
        Tally Of(Dictionary<string, Tally> of, string root) => of.GetValueOrDefault(root);
        State[] scoredStates = [.. held.Where(row => row.Ended != EndGame)];
        object[] accuracy = [.. arms.Select((arm, index) => (object)new
        {
            arm = arm.Name,
            meanLeaf = Range(Bootstrap(roots, root => (Of(means[index], root).Credit, Of(means[index], root).Weight))),
            statePairs = Range(Bootstrap(roots, root => (Of(states[index], root).Credit, Of(states[index], root).Weight))),
            logLoss = scoredStates.Average(state => SoftLoss(arm.Score(state), state.Value)),
        })];
        int last = arms.Count - 1;
        object[] paired = [.. Enumerable.Range(0, last).Select(other =>
        {
            var leaf = Bootstrap(roots, root => (Of(means[last], root).Credit - Of(means[other], root).Credit, Of(means[last], root).Weight));
            var state = Bootstrap(roots, root => (Of(states[last], root).Credit - Of(states[other], root).Credit, Of(states[last], root).Weight));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{side,-6} [{reference}] {arms[last].Name} - {arms[other].Name}: mean leaf {leaf.Mean:+0.000;-0.000} [{leaf.Lower:+0.000;-0.000}, {leaf.Upper:+0.000;-0.000}], state pairs {state.Mean:+0.000;-0.000} [{state.Lower:+0.000;-0.000}, {state.Upper:+0.000;-0.000}]"));
            return (object)new { against = arms[other].Name, meanLeaf = Range(leaf), statePairs = Range(state) };
        })];
        return new { reference, heldOutRoots = roots.Length, countedPairs = counted, accuracy, paired };
    }
}
