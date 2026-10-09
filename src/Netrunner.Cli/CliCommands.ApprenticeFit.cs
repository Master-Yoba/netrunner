using System.Collections.Concurrent;
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
    /// Bot plan S4.5: ai-fit-evaluation --target apprentice --suite SUITE-DIR --output-dir DIR [--pairs-per-root N] [--trees N]
    /// [--workers N] [--teacher-version V] [--version N]. The apprentice at the root, from every labelled turn-action root of the suite: one example per pair of
    /// a root's candidate groups, the sign of their teacher values' difference the target and its size the weight, held out
    /// by deal. Fits a linear ranker over <see cref="ApprenticeFeatures.Combine"/>'s terms and boosted scalar trees over the
    /// candidate's and the root's terms, and reads both beside the heuristic's own order on the held-out roots (the core's
    /// first): gap-weighted pairwise accuracy against the teacher's order, and the regret of each one's pick among the
    /// heuristic's top band (what <c>apprentice 1</c> re-orders), paired against the heuristic's pick. Writes
    /// apprentice-vN.json (the better of the two on held-out pairs) and its report. S4.5b: --teacher-version V fits on the roots
    /// that teacher labelled only, so the rows never mix teachers.
    /// </summary>
    private static int RunApprenticeFit(string[] args, TextWriter output, TextWriter error)
    {
        string? suiteDirectory = null, destination = null, teacher = null;
        int perRoot = 24, trees = 200, workers = 1, version = 1;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
                switch (args[index])
                {
                    case "--target": Next(); break;
                    case "--suite": suiteDirectory = Next(); break;
                    case "--output-dir": destination = Next(); break;
                    case "--pairs-per-root": perRoot = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 10_000); break;
                    case "--trees": trees = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 5_000); break;
                    case "--workers": workers = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 64); break;
                    case "--teacher-version": teacher = Next(); break;
                    case "--version": version = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 99); break;
                    default: throw new ArgumentException($"Unsupported ai-fit-evaluation --target apprentice argument '{args[index]}'.");
                }
            }
            if (suiteDirectory is null || destination is null) throw new ArgumentException("--suite and --output-dir are required.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("Fitting needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            error.WriteLine($"ai-fit-evaluation failed: {exception.Message}");
            return 2;
        }
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        var labels = suite.Labels.Where(label => teacher is null || label.TeacherName == teacher).ToDictionary(label => label.RootId);
        if (labels.Values.Select(label => label.TeacherName).Distinct(StringComparer.Ordinal).Count() > 1)
            throw new InvalidDataException("The suite's labels come from more than one teacher; choose one with --teacher-version.");
        var byGame = suite.Roots.Where(root => labels.ContainsKey(root.Id)).GroupBy(root => root.GameId)
            .ToDictionary(group => group.Key, group => group.Select(root => (root, labels[root.Id])).ToList());
        RegretGame[] games = [.. suite.Games.Where(game => byGame.ContainsKey(game.Id))];
        var collected = new ConcurrentBag<ApprenticeRow>();
        var started = System.Diagnostics.Stopwatch.StartNew();
        Parallel.ForEach(games, new ParallelOptions { MaxDegreeOfParallelism = workers }, game =>
        {
            foreach (ApprenticeRow row in ApprenticeRows.Collect(game, byGame[game.Id])) collected.Add(row);
        });
        ApprenticeRow[] rows = [.. collected.OrderBy(row => row.RootId, StringComparer.Ordinal).ThenBy(row => row.Group, StringComparer.Ordinal)];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"apprentice rows={rows.Length} roots={rows.Select(row => row.RootId).Distinct().Count()} seconds={started.Elapsed.TotalSeconds:F0}"));
        int band = ApprenticeFeatures.Names.ToList().IndexOf("heuristic-band"), rank = ApprenticeFeatures.Names.ToList().IndexOf("heuristic-rank");
        var sides = new Dictionary<PlayerSide, ApprenticeSide>();
        var report = new List<object>();
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            ApprenticeRow[][] roots = [.. rows.Where(row => row.Side == side).GroupBy(row => row.RootId, StringComparer.Ordinal)
                .Select(root => root.ToArray()).Where(root => root.Length > 1)];
            ApprenticeRow[][] train = [.. roots.Where(root => root[0].Split != "held-out")];
            // Training pairs, at most perRoot a root, drawn on one stream in the roots' order.
            var random = new DeterministicRandom(1);
            var pairs = new List<(ApprenticeRow Better, ApprenticeRow Worse)>();
            foreach (ApprenticeRow[] root in train)
            {
                List<(ApprenticeRow, ApprenticeRow)> all = [];
                for (int i = 0; i < root.Length; i++)
                    for (int j = i + 1; j < root.Length; j++)
                        if (root[i].Value != root[j].Value) all.Add(root[i].Value > root[j].Value ? (root[i], root[j]) : (root[j], root[i]));
                random.Shuffle(all);
                pairs.AddRange(all.Take(perRoot));
            }
            // The linear ranker: the combined terms' difference, both orders, weighted by the gap.
            var differences = new List<double[]>(pairs.Count * 2);
            var targets = new List<double>(pairs.Count * 2);
            var weights = new List<double>(pairs.Count * 2);
            foreach ((ApprenticeRow better, ApprenticeRow worse) in pairs)
            {
                double[] a = ApprenticeFeatures.Combine(better.Candidate, better.Root), b = ApprenticeFeatures.Combine(worse.Candidate, worse.Root);
                double gap = better.Value - worse.Value;
                differences.Add([.. a.Select((value, index) => value - b[index])]);
                differences.Add([.. b.Select((value, index) => value - a[index])]);
                targets.Add(1);
                targets.Add(0);
                weights.Add(gap);
                weights.Add(gap);
            }
            var fitting = System.Diagnostics.Stopwatch.StartNew();
            LogisticSideModel ranker = LogisticSideModel.Fit(differences, targets, iterations: 1500, sampleWeights: weights);
            var linear = new ApprenticeSide(new LogisticSideModel(new double[ranker.Weights.Length], ranker.Deviation, ranker.Weights, 0), null);
            double linearSeconds = fitting.Elapsed.TotalSeconds;
            // The trees: one state a candidate, its terms beside its root's.
            var states = new List<double[]>();
            var indexes = new Dictionary<ApprenticeRow, int>(ReferenceEqualityComparer.Instance);
            int IndexOf(ApprenticeRow row)
            {
                if (!indexes.TryGetValue(row, out int index))
                {
                    indexes[row] = index = states.Count;
                    states.Add([.. row.Candidate, .. row.Root]);
                }
                return index;
            }
            (int, int)[] treePairs = [.. pairs.Select(pair => (IndexOf(pair.Better), IndexOf(pair.Worse)))];
            fitting.Restart();
            BoostedTreeScorer forest = BoostedTreeScorer.Fit(states, treePairs, [.. pairs.Select(pair => pair.Better.Value - pair.Worse.Value)],
                fitted => [.. train.SelectMany(root => root).Select(row => (fitted.Score([.. row.Candidate, .. row.Root]), row.Value))], trees);
            var boosted = new ApprenticeSide(null, forest);
            double treeSeconds = fitting.Elapsed.TotalSeconds;
            (string Name, Func<ApprenticeRow, double> Score)[] arms =
            [
                ("heuristic", row => -row.Candidate[rank]),
                ("apprentice, linear", row => linear.Score(row.Candidate, row.Root)),
                ("apprentice, trees", row => boosted.Score(row.Candidate, row.Root)),
            ];
            // Inference cost: a held-out root's every candidate scored, one thread.
            ApprenticeRow[][] timed = [.. roots.Where(root => root[0].Split == "held-out").Take(400)];
            var timing = new double[2];
            for (int arm = 1; arm <= 2; arm++)
            {
                double sink = 0;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (ApprenticeRow[] root in timed) foreach (ApprenticeRow row in root) sink += arms[arm].Score(row);
                timing[arm - 1] = clock.Elapsed.TotalMicroseconds / Math.Max(1, timed.Length) + (sink == double.MaxValue ? 1 : 0);
            }
            object Read(string name, ApprenticeRow[][] over)
            {
                // Pairwise accuracy against the teacher's order (gap-weighted; and over pairs apart by twice their standard
                // error), and the regret of each arm's pick among the heuristic's top band.
                var credit = new Dictionary<string, (double[] Weighted, double Weight, double[] Clear, int Count)>(StringComparer.Ordinal);
                var regrets = new Dictionary<string, double[]>(StringComparer.Ordinal);
                foreach (ApprenticeRow[] root in over)
                {
                    double[][] scores = [.. arms.Select(arm => root.Select(arm.Score).ToArray())];
                    double[] weighted = new double[arms.Length], clear = new double[arms.Length];
                    double weight = 0;
                    int count = 0;
                    for (int i = 0; i < root.Length; i++)
                        for (int j = i + 1; j < root.Length; j++)
                        {
                            double difference = root[i].Value - root[j].Value;
                            if (difference == 0) continue;
                            bool apart = Math.Abs(difference) > 2 * Math.Sqrt(root[i].StandardError * root[i].StandardError + root[j].StandardError * root[j].StandardError);
                            weight += Math.Abs(difference);
                            if (apart) count++;
                            for (int arm = 0; arm < arms.Length; arm++)
                            {
                                double c = Credit(difference, scores[arm][i] - scores[arm][j]);
                                weighted[arm] += Math.Abs(difference) * c;
                                if (apart) clear[arm] += c;
                            }
                        }
                    credit[root[0].RootId] = (weighted, weight, clear, count);
                    double best = root.Where(row => !row.Pruned).Max(row => row.Value);
                    int[] top = [.. Enumerable.Range(0, root.Length).Where(index => root[index].Candidate[band] == 0)];
                    regrets[root[0].RootId] = [.. Enumerable.Range(0, arms.Length).Select(arm =>
                        best - root[top.OrderByDescending(index => scores[arm][index]).ThenBy(index => root[index].Group, StringComparer.Ordinal).First()].Value)];
                }
                string[] ids = [.. credit.Keys.Order(StringComparer.Ordinal)];
                object Range((double Mean, double Lower, double Upper) range) => new { mean = range.Mean, lower = range.Lower, upper = range.Upper };
                object[] accuracy = [.. arms.Select((arm, index) => (object)new
                {
                    arm = arm.Name,
                    gapWeighted = Range(Bootstrap(ids, id => (credit[id].Weighted[index], credit[id].Weight))),
                    apart = Range(Bootstrap(ids, id => (credit[id].Clear[index], credit[id].Count))),
                    regret = Range(Bootstrap(ids, id => (regrets[id][index], 1))),
                })];
                object[] paired = [.. Enumerable.Range(1, arms.Length - 1).Select(arm =>
                {
                    var regret = Bootstrap(ids, id => (regrets[id][arm] - regrets[id][0], 1));
                    var pairwise = Bootstrap(ids, id => (credit[id].Weighted[arm] - credit[id].Weighted[0], credit[id].Weight));
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{side,-6} [{name}] {arms[arm].Name} - heuristic: regret {regret.Mean:+0.0000;-0.0000} [{regret.Lower:+0.0000;-0.0000}, {regret.Upper:+0.0000;-0.0000}], pairwise {pairwise.Mean:+0.000;-0.000} [{pairwise.Lower:+0.000;-0.000}, {pairwise.Upper:+0.000;-0.000}]"));
                    return (object)new { arm = arms[arm].Name, regret = Range(regret), pairwise = Range(pairwise) };
                })];
                return new { roots = ids.Length, pairsApart = ids.Sum(id => credit[id].Count), accuracy, paired };
            }
            object coreHeld = Read("core held-out", [.. roots.Where(root => root[0].Split == "held-out" && root[0].Core)]);
            object allHeld = Read("every held-out root", [.. roots.Where(root => root[0].Split == "held-out")]);
            // The better model on held-out pairs (gap-weighted, every held-out root) goes forward: linear unless the trees are clearly better.
            double Accuracy(int arm) => roots.Where(root => root[0].Split == "held-out").Sum(root =>
            {
                double sum = 0;
                for (int i = 0; i < root.Length; i++)
                    for (int j = i + 1; j < root.Length; j++)
                        sum += Math.Abs(root[i].Value - root[j].Value) * Credit(root[i].Value - root[j].Value, arms[arm].Score(root[i]) - arms[arm].Score(root[j]));
                return sum;
            });
            bool useTrees = Accuracy(2) > Accuracy(1) * 1.01;
            sides[side] = useTrees ? boosted : linear;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{side,-6} pairs={pairs.Count} linear {linearSeconds:F0} s {timing[0]:F1} µs a root; trees {treeSeconds:F0} s {timing[1]:F1} µs a root; forward: {(useTrees ? "trees" : "linear")}"));
            report.Add(new
            {
                side = side.ToString().ToLowerInvariant(),
                trainRoots = train.Length,
                trainPairs = pairs.Count,
                microsecondsARoot = new { linear = timing[0], trees = timing[1] },
                forward = useTrees ? "trees" : "linear",
                coreHeldOut = coreHeld,
                everyHeldOut = allHeld,
            });
        }
        Directory.CreateDirectory(destination);
        string provenance = string.Create(CultureInfo.InvariantCulture,
            $"Fitted by ai-fit-evaluation --target apprentice (bot plan S4.5) on the turn-action roots of {Path.GetFileName(Path.GetFullPath(suiteDirectory))} labelled by teacher {labels.Values.FirstOrDefault()?.TeacherName} (training deals): pairs of a root's candidate groups (at most {perRoot} a root) by the sign of their teacher values' difference, weighted by its size; read with the Standard heuristic's utilities, no planner.");
        string name = string.Create(CultureInfo.InvariantCulture, $"apprentice-v{version}");
        File.WriteAllText(Path.Combine(destination, name + ".json"), JsonSerializer.Serialize(new ApprenticeDocument(1, name,
            ApprenticeFeatures.Names, sides[PlayerSide.Corp], sides[PlayerSide.Runner], provenance), FitJsonOptions) + "\n");
        File.WriteAllText(Path.Combine(destination, name + ".report.json"), JsonSerializer.Serialize(
            new { schemaVersion = 1, target = "apprentice", suite = suiteDirectory, teacher = labels.Values.FirstOrDefault()?.TeacherName, pairsPerRoot = perRoot, trees, sides = report }, FitJsonOptions) + "\n");
        output.WriteLine($"ai-fit-evaluation apprentice rows={rows.Length} output={destination}");
        return 0;
    }
}
