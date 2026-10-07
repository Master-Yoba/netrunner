using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-fit-evaluation [--content official|gateway|both|gateway-elevation|nsg-core-sets] [--games N] [--holdout-games N] [--seed S]
    /// [--workers 1..64] [--compare fitted.json] --output file.json. A5: plays Standard self-play, takes each side's view
    /// at the start of every turn, labels it with who won, fits one logistic model per side, and
    /// reports log loss on held-out seeds for the fitted model and for the hand evaluation
    /// (calibrated on the same training games). Exit 0 when the fitted model is better on both sides. A7:
    /// <c>--compare</c> also scores an existing fit (the general one) on the same held-out games, to decide
    /// whether a pool keeps a fit of its own.
    /// </summary>
    private static int RunAiFitEvaluation(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Contains("--target", StringComparer.Ordinal)) return RunTeacherFit(args, output, error);
        string content = "both";
        int games = 400, holdoutGames = 120, workers = 1, featureVersion = 1;
        ulong seed = 1;
        string? destination = null;
        string? compare = null;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--content" when index + 1 < args.Length:
                        content = args[++index];
                        if (content is not ("official" or "gateway" or "both" or "gateway-elevation" or "nsg-core-sets"))
                            throw new ArgumentException("--content requires official, gateway, both, gateway-elevation or nsg-core-sets.");
                        break;
                    case "--games" when index + 1 < args.Length: games = int.Parse(args[++index], CultureInfo.InvariantCulture); break;
                    case "--holdout-games" when index + 1 < args.Length: holdoutGames = int.Parse(args[++index], CultureInfo.InvariantCulture); break;
                    case "--seed" when index + 1 < args.Length: seed = ulong.Parse(args[++index], CultureInfo.InvariantCulture); break;
                    case "--workers" when index + 1 < args.Length: workers = Math.Clamp(int.Parse(args[++index], CultureInfo.InvariantCulture), 1, 64); break;
                    case "--output" when index + 1 < args.Length: destination = args[++index]; break;
                    case "--compare" when index + 1 < args.Length: compare = args[++index]; break;
                    case "--feature-version" when index + 1 < args.Length:
                        featureVersion = int.Parse(args[++index], CultureInfo.InvariantCulture);
                        if (featureVersion is not (1 or 2)) throw new ArgumentException("--feature-version requires 1 or 2.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-fit-evaluation argument '{args[index]}'.");
                }
            }
            if (destination is null) throw new ArgumentException("--output is required.");
            if (compare is not null && !File.Exists(compare)) throw new ArgumentException($"--compare file does not exist: {compare}");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("Fitting needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            error.WriteLine($"ai-fit-evaluation failed: {exception.Message}");
            return 2;
        }

        IReadOnlyList<string> pool = content switch
        {
            "official" => RulesFuzzRunner.OfficialContentIds,
            "gateway" => RulesFuzzRunner.GatewayContentIds,
            "gateway-elevation" => RulesFuzzRunner.GatewayElevationContentIds,
            "nsg-core-sets" => RulesFuzzRunner.NsgCoreSetsContentIds,
            _ => [.. RulesFuzzRunner.OfficialContentIds, .. RulesFuzzRunner.GatewayContentIds],
        };
        // Held-out seeds come from a separate base seed, so no training game repeats.
        Samples training = Collect(pool, games, seed, workers, featureVersion);
        Samples holdout = Collect(pool, holdoutGames, seed + 1_000_003, workers, featureVersion);
        FittedEvaluationDocument? compared = compare is null ? null
            : JsonSerializer.Deserialize<FittedEvaluationDocument>(File.ReadAllText(compare), FitJsonOptions)
                ?? throw new InvalidOperationException($"--compare file is empty: {compare}");
        var models = new Dictionary<PlayerSide, LogisticSideModel>();
        var report = new List<object>();
        bool better = true;
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            (List<double[]> x, List<double> y, List<double> hand) = training.For(side);
            (List<double[]> hx, List<double> hy, List<double> hhand) = holdout.For(side);
            LogisticSideModel model = LogisticSideModel.Fit(x, y);
            models[side] = model;
            // The hand evaluation, calibrated to a probability by a one-feature fit on the same training games.
            LogisticSideModel handModel = LogisticSideModel.Fit(hand.Select(value => new[] { value }).ToList(), y);
            double fittedLoss = LogisticSideModel.LogLoss(model.Probability, hx, hy);
            double handLoss = LogisticSideModel.LogLoss(sample => handModel.Probability(sample), hhand.Select(value => new[] { value }).ToList(), hy);
            double baseRate = y.Average();
            double baseLoss = LogisticSideModel.LogLoss(_ => baseRate, hx, hy);
            double accuracy = hx.Select((sample, index) => (model.Probability(sample) >= 0.5 ? 1.0 : 0.0) == hy[index] ? 1 : 0).Average();
            better &= fittedLoss < handLoss;
            LogisticSideModel? comparedModel = compared is null ? null : side == PlayerSide.Corp ? compared.Corp : compared.Runner;
            double? comparedLoss = comparedModel is null ? null : LogisticSideModel.LogLoss(comparedModel.Probability, hx, hy);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{side,-6} train={x.Count} holdout={hx.Count} logloss fitted={fittedLoss:F4} hand={handLoss:F4} base-rate={baseLoss:F4} accuracy={accuracy:P1}")
                + (comparedLoss is { } loss ? string.Create(CultureInfo.InvariantCulture, $" compared={loss:F4}") : ""));
            report.Add(new { side = side.ToString().ToLowerInvariant(), train = x.Count, holdout = hx.Count, fittedLoss, handLoss, baseLoss, accuracy, comparedLoss });
        }
        var document = new FittedEvaluationDocument(1, $"evaluation-v{featureVersion}", EvaluationFeatures.NamesFor(featureVersion), models[PlayerSide.Corp], models[PlayerSide.Runner],
            string.Create(CultureInfo.InvariantCulture,
                $"Fitted by ai-fit-evaluation (feature set {featureVersion}) on {games} Standard self-play games (content {content}, seed {seed}); held out {holdoutGames} games on another seed."));
        File.WriteAllText(destination, JsonSerializer.Serialize(document, FitJsonOptions) + "\n");
        File.WriteAllText(Path.ChangeExtension(destination, ".report.json"),
            JsonSerializer.Serialize(new { schemaVersion = 1, featureVersion, content, games, holdoutGames, seed, compare, sides = report }, FitJsonOptions) + "\n");
        output.WriteLine($"ai-fit-evaluation {(better ? "better" : "not-better")} output={destination}");
        return better ? 0 : 1;
    }

    /// <summary>
    /// Bot plan S4.2: ai-fit-evaluation --target teacher --suite SUITE-DIR [--outcome-weight W] [--workers N] --output-dir DIR.
    /// Fits one model per side to the teacher's root value (soft targets, weight 1) and the game's outcome (weight W) on
    /// the core's training deals, for set 5 and for set 4's features, and scores them against set 4 with its shipped weights
    /// on the core's held-out roots: log loss and mean absolute error against the teacher's value, and log loss against the
    /// outcome. Writes evaluation-v5.json, evaluation-v4-teacher.json and the report.
    /// S4.2b: --target end-lines --end-lines FILE fits the same arms to the rows the labelling recorded where the root
    /// side's turn ended (the states the planner's leaf reads), the play-out's value the target, held out by deal; it
    /// writes evaluation-v5-lines.json, evaluation-v4-lines.json and their report. Intervals resample roots.
    /// </summary>
    private static int RunTeacherFit(string[] args, TextWriter output, TextWriter error)
    {
        string? suiteDirectory = null, destination = null, endLines = null, target = null;
        double outcomeWeight = 0.2;
        int workers = 1;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--target" when index + 1 < args.Length:
                        target = args[++index];
                        if (target is not ("teacher" or "end-lines")) throw new ArgumentException("--target requires teacher or end-lines.");
                        break;
                    case "--suite" when index + 1 < args.Length: suiteDirectory = args[++index]; break;
                    case "--end-lines" when index + 1 < args.Length: endLines = args[++index]; break;
                    case "--output-dir" when index + 1 < args.Length: destination = args[++index]; break;
                    case "--outcome-weight" when index + 1 < args.Length: outcomeWeight = double.Parse(args[++index], CultureInfo.InvariantCulture); break;
                    case "--workers" when index + 1 < args.Length: workers = Math.Clamp(int.Parse(args[++index], CultureInfo.InvariantCulture), 1, 64); break;
                    default: throw new ArgumentException($"Unsupported ai-fit-evaluation --target argument '{args[index]}'.");
                }
            }
            if (suiteDirectory is null || destination is null) throw new ArgumentException("--suite and --output-dir are required.");
            if (target == "end-lines" && endLines is null) throw new ArgumentException("--target end-lines needs --end-lines FILE.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("Fitting needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            error.WriteLine($"ai-fit-evaluation failed: {exception.Message}");
            return 2;
        }
        Netrunner.Match.Regret.RegretSuite suite = Netrunner.Match.Regret.RegretSuite.Load(suiteDirectory);
        bool lines = target == "end-lines";
        FitRow[] rows = lines ? EndLineRows(suite, Path.Combine(suiteDirectory, endLines!)) : TeacherRows(suite, workers);
        // The outcome is the teacher's auxiliary target; an end-of-line row has only its play-out.
        double weight = lines ? 0 : outcomeWeight;
        string fitted = lines ? "fitted to the end-of-line values" : "fitted to the teacher";
        var models = new Dictionary<(PlayerSide Side, int Set), LogisticSideModel>();
        var report = new List<object>();
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            FitRow[] sided = [.. rows.Where(row => row.Side == side)];
            FitRow[] train = [.. sided.Where(row => row.Split != "held-out")];
            FitRow[] held = [.. sided.Where(row => row.Split == "held-out")];
            LogisticSideModel FitOn(Func<FitRow, double[]> features) => weight > 0
                ? LogisticSideModel.Fit([.. train.Select(features), .. train.Select(features)], [.. train.Select(row => row.Target), .. train.Select(row => row.Won)],
                    sampleWeights: [.. train.Select(_ => 1.0), .. train.Select(_ => weight)])
                : LogisticSideModel.Fit([.. train.Select(features)], [.. train.Select(row => row.Target)]);
            models[(side, 4)] = FitOn(row => row.Set4);
            models[(side, 5)] = FitOn(row => row.Set5);
            LogisticSideModel Shipped(FitRow row) =>
                side == PlayerSide.Corp ? FittedEvaluation.DocumentFor(4, row.BehaviorVersion).Corp : FittedEvaluation.DocumentFor(4, row.BehaviorVersion).Runner;
            object Score(string arm, Func<FitRow, double> predict) => new
            {
                arm,
                teacherLogLoss = held.Average(row => SoftLoss(predict(row), row.Target)),
                teacherMeanAbsoluteError = held.Average(row => Math.Abs(predict(row) - row.Target)),
                outcomeLogLoss = lines ? (double?)null : held.Average(row => SoftLoss(predict(row), row.Won)),
            };
            double baseRate = train.Average(row => row.Target);
            object[] arms =
            [
                Score("teacher's mean", _ => baseRate),
                Score("set 4, shipped weights", row => Shipped(row).Probability(row.Set4)),
                Score($"set 4, {fitted}", row => models[(side, 4)].Probability(row.Set4)),
                Score($"set 5, {fitted}", row => models[(side, 5)].Probability(row.Set5)),
            ];
            // Set 5 against each set 4 arm, paired root by root: the mean difference of the held-out losses and its 95%
            // bootstrap interval, resampling roots (a root's end-of-line rows share its samples).
            Func<FitRow, double> five = row => models[(side, 5)].Probability(row.Set5);
            object Against(string arm, Func<FitRow, double> other)
            {
                (double Mean, double Lower, double Upper) Paired(Func<double, double, double> loss)
                {
                    (double Sum, int Count)[] roots = [.. held.GroupBy(row => row.RootId, StringComparer.Ordinal)
                        .Select(group => (group.Sum(row => loss(five(row), row.Target) - loss(other(row), row.Target)), group.Count()))];
                    var random = new DeterministicRandom(1);
                    double[] means = [.. Enumerable.Range(0, 2000).Select(_ =>
                    {
                        double sum = 0, count = 0;
                        for (int draw = 0; draw < roots.Length; draw++)
                        {
                            (double rootSum, int rootCount) = roots[random.NextInt(roots.Length)];
                            sum += rootSum;
                            count += rootCount;
                        }
                        return sum / count;
                    }).Order()];
                    return (roots.Sum(root => root.Sum) / roots.Sum(root => root.Count), means[50], means[1949]);
                }
                var logLoss = Paired(SoftLoss);
                var absolute = Paired((prediction, target) => Math.Abs(prediction - target));
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{side,-6} set 5 - {arm}: target log loss {logLoss.Mean:+0.0000;-0.0000} [{logLoss.Lower:+0.0000;-0.0000}, {logLoss.Upper:+0.0000;-0.0000}], mean absolute error {absolute.Mean:+0.0000;-0.0000} [{absolute.Lower:+0.0000;-0.0000}, {absolute.Upper:+0.0000;-0.0000}]"));
                return new
                {
                    arm,
                    logLoss = new { mean = logLoss.Mean, lower = logLoss.Lower, upper = logLoss.Upper },
                    absolute = new { mean = absolute.Mean, lower = absolute.Lower, upper = absolute.Upper },
                };
            }
            object[] paired =
            [
                Against("set 4 shipped", row => Shipped(row).Probability(row.Set4)),
                Against("set 4 fitted", row => models[(side, 4)].Probability(row.Set4)),
            ];
            int heldRoots = held.Select(row => row.RootId).Distinct(StringComparer.Ordinal).Count();
            report.Add(new { side = side.ToString().ToLowerInvariant(), train = train.Length, heldOut = held.Length, heldOutRoots = heldRoots, arms, paired });
            foreach (object arm in arms)
                output.WriteLine($"{side,-6} train={train.Length} held-out={held.Length} {JsonSerializer.Serialize(arm, FitJsonOptions).Replace("\n", " ", StringComparison.Ordinal).ReplaceLineEndings(" ")}");
        }
        Directory.CreateDirectory(destination);
        string suiteName = Path.GetFileName(Path.GetFullPath(suiteDirectory));
        string provenance = lines
            ? string.Create(CultureInfo.InvariantCulture,
                $"Fitted by ai-fit-evaluation --target end-lines (bot plan S4.2b) on the training deals' end-of-line rows of {suiteName}'s core ({endLines}, teacher v2): the state where the root side's turn ended on a line played to the horizon, the play-out's value from it the target.")
            : string.Create(CultureInfo.InvariantCulture,
                $"Fitted by ai-fit-evaluation --target teacher (bot plan S4.2) on the core's training roots of {suiteName} (teacher v2), the teacher's root value as the target and the game's outcome at weight {outcomeWeight}.");
        (string fiveName, string fourName) = lines ? ("evaluation-v5-lines", "evaluation-v4-lines") : ("evaluation-v5", "evaluation-v4-teacher");
        File.WriteAllText(Path.Combine(destination, fiveName + ".json"), JsonSerializer.Serialize(new FittedEvaluationDocument(1, fiveName,
            EvaluationFeatures.NamesFor(5), models[(PlayerSide.Corp, 5)], models[(PlayerSide.Runner, 5)], provenance), FitJsonOptions) + "\n");
        File.WriteAllText(Path.Combine(destination, fourName + ".json"), JsonSerializer.Serialize(new FittedEvaluationDocument(1, fourName,
            EvaluationFeatures.NamesFor(4), models[(PlayerSide.Corp, 4)], models[(PlayerSide.Runner, 4)], provenance), FitJsonOptions) + "\n");
        File.WriteAllText(Path.Combine(destination, fiveName + ".report.json"),
            JsonSerializer.Serialize(lines
                ? new { schemaVersion = 1, target, suite = suiteDirectory, endLines, outcomeWeight = 0.0, sides = report }
                : (object)new { schemaVersion = 1, target, suite = suiteDirectory, outcomeWeight, sides = report }, FitJsonOptions) + "\n");
        output.WriteLine($"ai-fit-evaluation {target} rows={rows.Length} output={destination}");
        return 0;
    }

    /// <summary>A fitting row: its root (the bootstrap's unit), side and split, sets 4 and 5, the target and the outcome.</summary>
    private sealed record FitRow(string RootId, PlayerSide Side, string Split, double[] Set4, double[] Set5, double Target, double Won,
        string? BehaviorVersion);

    /// <summary>S4.2: the core's labelled roots, read at the root.</summary>
    private static FitRow[] TeacherRows(Netrunner.Match.Regret.RegretSuite suite, int workers)
    {
        var labels = suite.Labels.ToDictionary(label => label.RootId);
        var byGame = suite.Roots.Where(root => root.Categories.Contains(Netrunner.Match.Regret.RegretSuite.CoreCategory) && labels.ContainsKey(root.Id))
            .GroupBy(root => root.GameId).ToDictionary(group => group.Key, group => group.Select(root => (root, labels[root.Id])).ToList());
        Netrunner.Match.Regret.RegretGame[] games = [.. suite.Games.Where(game => byGame.ContainsKey(game.Id))];
        var rows = new ConcurrentBag<Netrunner.Match.Regret.TeacherFitRow>();
        Parallel.ForEach(games, new ParallelOptions { MaxDegreeOfParallelism = workers }, game =>
        {
            foreach (Netrunner.Match.Regret.TeacherFitRow row in Netrunner.Match.Regret.TeacherFit.Collect(game, byGame[game.Id])) rows.Add(row);
        });
        return [.. rows.OrderBy(row => row.RootId, StringComparer.Ordinal)
            .Select(row => new FitRow(row.RootId, row.Side, row.Split, row.Set4, row.Set5, row.Teacher, row.Won, row.BehaviorVersion))];
    }

    /// <summary>S4.2b: the end-of-line rows of the core's roots, in the file's order; a row's pool is its game's.</summary>
    private static FitRow[] EndLineRows(Netrunner.Match.Regret.RegretSuite suite, string path)
    {
        HashSet<string> core = [.. suite.Roots.Where(root => root.Categories.Contains(Netrunner.Match.Regret.RegretSuite.CoreCategory)).Select(root => root.Id)];
        var behaviors = new Dictionary<string, string>(StringComparer.Ordinal);
        string Behavior(string gameId)
        {
            if (!behaviors.TryGetValue(gameId, out string? version))
                behaviors[gameId] = version = Netrunner.Match.Regret.RegretGames.Definition(suite.Games.First(game => game.Id == gameId)).Behavior.Version;
            return version;
        }
        int setFour = EvaluationFeatures.NamesFor(4).Count;
        return [.. Netrunner.Match.Regret.RegretSuite.ReadLines<Netrunner.Match.Regret.EndLineRow>(path).Where(row => core.Contains(row.RootId))
            .Select(row => new FitRow(row.RootId, row.Side, row.Split, row.Features[..setFour], row.Features, row.Value, double.NaN, Behavior(row.GameId)))];
    }

    /// <summary>The log loss of a probability against a target that may be soft (the teacher's value).</summary>
    private static double SoftLoss(double probability, double target)
    {
        double clamped = Math.Clamp(probability, 1e-6, 1 - 1e-6);
        return -(target * Math.Log(clamped) + (1 - target) * Math.Log(1 - clamped));
    }

    private static readonly JsonSerializerOptions FitJsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed class Samples
    {
        public readonly ConcurrentBag<(PlayerSide Side, int Game, int Order, double[] Features, double Hand, double Won)> Rows = [];

        public (List<double[]> X, List<double> Y, List<double> Hand) For(PlayerSide side)
        {
            var rows = Rows.Where(row => row.Side == side).OrderBy(row => row.Game).ThenBy(row => row.Order).ToArray();
            return (rows.Select(row => row.Features).ToList(), rows.Select(row => row.Won).ToList(), rows.Select(row => row.Hand).ToList());
        }
    }

    /// <summary>Standard self-play; each side's view at the first action of every turn, labelled with whether that side won.</summary>
    private static Samples Collect(IReadOnlyList<string> pool, int games, ulong baseSeed, int workers, int featureVersion)
    {
        var samples = new Samples();
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = workers }, game =>
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(pool[game % pool.Count], RulesFuzzRunner.DeriveGameSeed(baseSeed, game));
            FullMatchResult result;
            try
            {
                result = FullMatchRunner.RunAsync(definition, RatingLadder.DefaultActionCap,
                    RatingLadder.CreateAgent("standard", PlayerSide.Corp, definition),
                    RatingLadder.CreateAgent("standard", PlayerSide.Runner, definition)).GetAwaiter().GetResult();
            }
            catch (FullMatchStallException)
            {
                return;
            }
            if (result.Outcome.Winner is not { } winner) return;
            var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
            GameState state = result.Replay.InitialSnapshot.ToState();
            var seenTurns = new HashSet<(int, PlayerSide)>();
            int order = 0;
            foreach (ReplayAction action in result.Replay.Actions)
            {
                if (state.PendingDecision is { Kind: DecisionKind.TurnAction } && seenTurns.Add((state.Turn.TurnNumber, state.Turn.ActiveSide)))
                {
                    foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
                    {
                        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, side, definition.Behavior),
                            AgentFeatures.VersionWithHiddenAdvancements);
                        double hand = EvaluationFeatures.HandScore(features, catalog) / 1000.0;
                        samples.Rows.Add((side, game, order++, EvaluationFeatures.Extract(features, catalog, featureVersion), hand, winner == side ? 1 : 0));
                    }
                }
                state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime, ApplyMode.Simulation).State;
            }
        });
        return samples;
    }
}
