using System.Globalization;
using System.Text.Json;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match.Regret;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// Bot plan S4.4: ai-regret horizon-curve SUITE-DIR --reference GAME-ROWS --horizon NAME=ROWS ... [--draws N] [--output FILE].
    /// For each horizon (each rows file's play-out values; and the turn's end scored by the general fit and by the pool's
    /// fit, from the reference's own end states), the planner's-view accuracy of k samples (k = 4, 8, 16, 32) against the
    /// game's-end reference: two lines of a core root ordered by their mean over k samples (common random numbers), credited
    /// when they agree with the order of their game's-end means, gap-weighted, over the pairs the reference counts (more
    /// than twice the paired standard error apart). Two designs: (a) k drawn from all 32 samples against the 32-sample
    /// reference, as the step reads it (the game's-end row shares its samples with the reference, so it reads high, and at
    /// k = 32 it is 1 by construction); (b) k drawn from one half of the samples against the reference of the other half
    /// (k up to 16), both halves in turn, which no row can read high. Draws are averaged; intervals resample roots.
    /// </summary>
    private static int RegretHorizonCurveCommand(string suiteDirectory, TextWriter output, string[] args)
    {
        string? reference = null, destination = null;
        int draws = 16;
        var horizons = new List<(string Name, string Path)>();
        for (int index = 3; index < args.Length; index++)
        {
            string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
            switch (args[index])
            {
                case "--reference": reference = Next(); break;
                case "--horizon":
                    string value = Next();
                    int at = value.IndexOf('=', StringComparison.Ordinal);
                    horizons.Add(at > 0 ? (value[..at], value[(at + 1)..]) : throw new ArgumentException($"'{value}' is not NAME=ROWS."));
                    break;
                case "--draws": draws = Math.Clamp(int.Parse(Next(), CultureInfo.InvariantCulture), 1, 1000); break;
                case "--output": destination = Next(); break;
                default: throw new ArgumentException($"Unsupported ai-regret horizon-curve argument '{args[index]}'.");
            }
        }
        if (reference is null) throw new ArgumentException("ai-regret horizon-curve needs --reference GAME-ROWS.");
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        Func<string, State[]> load = CoreStates(suite);
        State[] game = load(reference);
        int setFour = EvaluationFeatures.NamesFor(4).Count;
        // Each source: its value of a line on a sample, by root.
        var sources = new List<(string Name, Dictionary<(string Root, string Line, int Sample), double> Values)>();
        Dictionary<(string, string, int), double> Index(IEnumerable<State> rows, Func<State, double> value)
        {
            var values = new Dictionary<(string, string, int), double>();
            foreach (State row in rows) values[(row.RootId, row.Line, row.Sample)] = value(row);
            return values;
        }
        LogisticSideModel Of(int version, PlayerSide side, string? behavior = null) =>
            side == PlayerSide.Corp ? FittedEvaluation.DocumentFor(version, behavior).Corp : FittedEvaluation.DocumentFor(version, behavior).Runner;
        sources.Add(("turn's end, general fit", Index(game, row => row.Ended == EndGame ? row.Value : Of(2, row.Side).Probability(row.Features[..setFour]))));
        sources.Add(("turn's end, pool's fit", Index(game, row => row.Ended == EndGame ? row.Value : Of(4, row.Side, row.Behavior).Probability(row.Features[..setFour]))));
        foreach ((string name, string path) in horizons) sources.Add((name, Index(load(path), row => row.Value)));
        sources.Add(("game's end", Index(game, row => row.Value)));
        int[] ks = [4, 8, 16, 32];
        var report = new List<object>();
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            // Per root: its lines, its samples, and every source's values on them.
            var roots = game.Where(row => row.Side == side).GroupBy(row => row.RootId, StringComparer.Ordinal).OrderBy(root => root.Key, StringComparer.Ordinal)
                .Select(root =>
                {
                    string[] lines = [.. root.Select(row => row.Line).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
                    int[] samples = [.. root.Select(row => row.Sample).Distinct().Order()];
                    double[][][] values = [.. sources.Select(source => lines.Select(line => samples.Select(sample =>
                        source.Values.TryGetValue((root.Key, line, sample), out double value) ? value : double.NaN).ToArray()).ToArray())];
                    return (Id: root.Key, Lines: lines, Samples: samples, Values: values);
                })
                .Where(root => root.Lines.Length > 1 && root.Samples.Length >= 8).ToArray();
            int game_ = sources.Count - 1;
            // tallies[design][source][k] : per root (credit, weight)
            var tallies = new Dictionary<(string Design, int Source, int K), Dictionary<string, Tally>>();
            Dictionary<string, Tally> TallyOf(string design, int source, int k)
            {
                if (!tallies.TryGetValue((design, source, k), out var tally)) tallies[(design, source, k)] = tally = new(StringComparer.Ordinal);
                return tally;
            }
            int countedAll = 0, countedHalves = 0;
            foreach (var root in roots)
            {
                var random = new DeterministicRandom(RegretRootsHash(root.Id));
                int n = root.Samples.Length;
                double Mean(double[] values, IReadOnlyList<int> subset)
                {
                    double sum = 0;
                    foreach (int at in subset) sum += values[at];
                    return sum / subset.Count;
                }
                // The reference on a set of sample positions: each counted pair's direction and gap.
                List<(int I, int J, double Mean, double Gap)> Counted(IReadOnlyList<int> positions)
                {
                    var counted = new List<(int, int, double, double)>();
                    double[][] reference = root.Values[game_];
                    for (int i = 0; i < root.Lines.Length; i++)
                        for (int j = i + 1; j < root.Lines.Length; j++)
                        {
                            double[] differences = [.. positions.Select(at => reference[i][at] - reference[j][at])];
                            if (differences.Any(double.IsNaN)) continue;
                            double mean = differences.Average();
                            if (mean == 0) continue;
                            double error = Math.Sqrt(differences.Sum(value => (value - mean) * (value - mean)) / (differences.Length - 1) / differences.Length);
                            if (Math.Abs(mean) > 2 * error) counted.Add((i, j, mean, Math.Abs(mean)));
                        }
                    return counted;
                }
                void Score(string design, IReadOnlyList<(int I, int J, double Mean, double Gap)> pairs, IReadOnlyList<int> pool, int k)
                {
                    if (pairs.Count == 0 || k > pool.Count) return;
                    for (int source = 0; source < sources.Count; source++)
                    {
                        double credit = 0, weight = 0;
                        for (int draw = 0; draw < (k == pool.Count ? 1 : draws); draw++)
                        {
                            List<int> subset = [.. pool];
                            random.Shuffle(subset);
                            subset = subset.Take(k).ToList();
                            double[][] values = root.Values[source];
                            foreach ((int i, int j, double mean, double gap) in pairs)
                            {
                                double a = Mean(values[i], subset), b = Mean(values[j], subset);
                                if (double.IsNaN(a) || double.IsNaN(b)) continue;
                                credit += gap * Credit(mean, a - b);
                                weight += gap;
                            }
                        }
                        Tally tally = TallyOf(design, source, k).GetValueOrDefault(root.Id);
                        TallyOf(design, source, k)[root.Id] = new Tally(tally.Credit + credit, tally.Weight + weight);
                    }
                }
                int[] all = [.. Enumerable.Range(0, n)];
                List<(int, int, double, double)> counted = Counted(all);
                countedAll += counted.Count;
                foreach (int k in ks) Score("a", counted, all, Math.Min(k, n));
                int[] first = [.. all.Take(n / 2)], second = [.. all.Skip(n / 2)];
                foreach ((int[] estimate, int[] judge) in new[] { (first, second), (second, first) })
                {
                    List<(int, int, double, double)> halfCounted = Counted(judge);
                    countedHalves += halfCounted.Count;
                    foreach (int k in ks.Where(k => k <= estimate.Length)) Score("b", halfCounted, estimate, k);
                }
            }
            string[] ids = [.. roots.Select(root => root.Id)];
            var cells = new List<object>();
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{side}: {ids.Length} roots; pairs counted on all 32 samples {countedAll}, on halves {countedHalves}"));
            foreach (string design in new[] { "a", "b" })
            {
                output.WriteLine(design == "a" ? "  (a) k of all samples against the 32-sample reference" : "  (b) k of one half against the other half's reference");
                for (int source = 0; source < sources.Count; source++)
                {
                    var row = new List<string>();
                    foreach (int k in ks)
                    {
                        if (!tallies.TryGetValue((design, source, k), out var tally) || tally.Count == 0) { row.Add("     -      "); continue; }
                        (double mean, double lower, double upper) = Bootstrap(ids, id => (tally.GetValueOrDefault(id).Credit, tally.GetValueOrDefault(id).Weight));
                        row.Add(string.Create(CultureInfo.InvariantCulture, $"{mean:0.000} [{lower:0.000}, {upper:0.000}]"));
                        cells.Add(new { design, horizon = sources[source].Name, k, mean, lower, upper });
                    }
                    output.WriteLine($"    {sources[source].Name,-28} " + string.Join("  ", ks.Select((k, at) => string.Create(CultureInfo.InvariantCulture, $"k={k}: {row[at]}"))));
                }
            }
            report.Add(new { side = side.ToString().ToLowerInvariant(), roots = ids.Length, countedAll, countedHalves, cells });
        }
        if (destination is not null)
            File.WriteAllText(destination, JsonSerializer.Serialize(new { schemaVersion = 1, reference, horizons = sources.Select(source => source.Name), draws, sides = report },
                FitJsonOptions) + "\n");
        return 0;
    }

    /// <summary>A root's seed for its draws: FNV-1a of its id.</summary>
    private static ulong RegretRootsHash(string id)
    {
        ulong hash = 14695981039346656037;
        foreach (char character in id) hash = (hash ^ character) * 1099511628211;
        return hash;
    }
}
