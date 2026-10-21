using System.Text.Json.Serialization;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// A node of a regression tree, stored flat: a split (<paramref name="Feature"/> at or below <paramref name="Threshold"/>
/// goes to <paramref name="Left"/>, else to <paramref name="Right"/>), or a leaf (<paramref name="Feature"/> −1) adding
/// <paramref name="Value"/> to the score.
/// </summary>
public sealed record TreeNode(int Feature, double Threshold, int Left, int Right, double Value);

/// <summary>
/// Bot plan S4.2c, the nonlinear fallback: a scalar scorer <c>g(features)</c>, a sum of small regression trees trained with
/// the pairwise gradient (RankNet: the loss is on <c>g(f_A) − g(f_B)</c> of two end states of one root on one sample, the
/// model is on one state), so the planner still calls it one state at a time. A monotone calibration maps the score to a
/// win probability: the isotonic fit's block levels at their mean scores, interpolated between them, so the order of
/// the score is kept and states seldom tie.
/// </summary>
public sealed record BoostedTreeScorer(
    [property: JsonRequired] TreeNode[][] Trees,
    [property: JsonRequired] double[] CalibrationScores,
    [property: JsonRequired] double[] CalibrationProbabilities)
{
    public double Score(IReadOnlyList<double> features)
    {
        double score = 0;
        foreach (TreeNode[] tree in Trees)
        {
            int at = 0;
            while (tree[at].Feature >= 0) at = features[tree[at].Feature] <= tree[at].Threshold ? tree[at].Left : tree[at].Right;
            score += tree[at].Value;
        }
        return score;
    }

    public double Probability(IReadOnlyList<double> features) => Calibrated(Score(features));

    /// <summary>The calibration's probability at a score: interpolated between its points, flat past the ends.</summary>
    public double Calibrated(double score)
    {
        double[] scores = CalibrationScores, probabilities = CalibrationProbabilities;
        if (scores.Length == 0) return 0.5;
        if (score <= scores[0]) return probabilities[0];
        if (score >= scores[^1]) return probabilities[^1];
        int at = Array.BinarySearch(scores, score);
        if (at >= 0) return probabilities[at];
        int right = ~at, left = right - 1;
        double share = (score - scores[left]) / (scores[right] - scores[left]);
        return probabilities[left] + share * (probabilities[right] - probabilities[left]);
    }

    /// <summary>
    /// Fits the trees to <paramref name="pairs"/> (indexes into <paramref name="states"/>: the first is the better of
    /// the two, by <paramref name="weights"/>, the size of their values' difference) by Newton boosting on the pairwise
    /// logistic loss, with histogram splits over up to <paramref name="bins"/> quantile thresholds a feature; then the
    /// calibration on <paramref name="calibration"/> (a score's state and its value). Deterministic.
    /// </summary>
    public static BoostedTreeScorer Fit(IReadOnlyList<double[]> states, IReadOnlyList<(int Better, int Worse)> pairs, IReadOnlyList<double> weights,
        Func<BoostedTreeScorer, IReadOnlyList<(double Score, double Value)>> calibration, int trees = 200, int depth = 3,
        double learningRate = 0.1, double lambda = 1, int bins = 64, double minimumHessian = 1e-3)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(calibration);
        if (states.Count == 0 || pairs.Count == 0 || pairs.Count != weights.Count) throw new ArgumentException("States, pairs and weights must pair up.");
        int width = states[0].Length, count = states.Count;
        // Quantile thresholds a feature, and every state's bin under them.
        double[][] thresholds = new double[width][];
        byte[][] binned = new byte[width][];
        for (int feature = 0; feature < width; feature++)
        {
            double[] sorted = [.. states.Select(state => state[feature]).Order()];
            var cuts = new List<double>();
            for (int cut = 1; cut < bins; cut++)
            {
                double value = sorted[(int)((long)cut * (sorted.Length - 1) / bins)];
                if (cuts.Count == 0 || value > cuts[^1]) cuts.Add(value);
            }
            if (cuts.Count > 0 && cuts[^1] >= sorted[^1]) cuts.RemoveAt(cuts.Count - 1);
            thresholds[feature] = [.. cuts];
            binned[feature] = new byte[count];
            for (int index = 0; index < count; index++)
            {
                int at = Array.BinarySearch(thresholds[feature], states[index][feature]);
                binned[feature][index] = (byte)(at >= 0 ? at : ~at);
            }
        }
        double[] score = new double[count], gradient = new double[count], hessian = new double[count];
        var forest = new List<TreeNode[]>(trees);
        for (int round = 0; round < trees; round++)
        {
            Array.Clear(gradient);
            Array.Clear(hessian);
            for (int pair = 0; pair < pairs.Count; pair++)
            {
                (int better, int worse) = pairs[pair];
                double margin = score[better] - score[worse];
                double wrong = 1 / (1 + Math.Exp(margin));
                double weight = weights[pair];
                gradient[better] -= weight * wrong;
                gradient[worse] += weight * wrong;
                double curvature = weight * wrong * (1 - wrong);
                hessian[better] += curvature;
                hessian[worse] += curvature;
            }
            TreeNode[] tree = Grow(binned, thresholds, gradient, hessian, depth, lambda, minimumHessian, learningRate);
            forest.Add(tree);
            for (int index = 0; index < count; index++)
            {
                int at = 0;
                while (tree[at].Feature >= 0) at = binned[tree[at].Feature][index] <= Bin(thresholds[tree[at].Feature], tree[at].Threshold) ? tree[at].Left : tree[at].Right;
                score[index] += tree[at].Value;
            }
        }
        var model = new BoostedTreeScorer([.. forest], [], []);
        (double[] scores, double[] probabilities) = Isotonic(calibration(model));
        return model with { CalibrationScores = scores, CalibrationProbabilities = probabilities };
    }

    private static int Bin(double[] thresholds, double threshold)
    {
        int at = Array.BinarySearch(thresholds, threshold);
        return at >= 0 ? at : ~at;
    }

    /// <summary>One tree, level by level: each node takes the split of best gain over every feature's histogram.</summary>
    private static TreeNode[] Grow(byte[][] binned, double[][] thresholds, double[] gradient, double[] hessian, int depth, double lambda,
        double minimumHessian, double learningRate)
    {
        int width = binned.Length, count = gradient.Length;
        var nodes = new List<TreeNode>();
        int[] at = new int[count];
        var open = new List<int> { 0 };
        nodes.Add(new TreeNode(-1, 0, -1, -1, 0));
        for (int level = 0; level <= depth; level++)
        {
            var sums = open.ToDictionary(node => node, _ => (G: 0.0, H: 0.0));
            for (int index = 0; index < count; index++)
                if (sums.TryGetValue(at[index], out var sum)) sums[at[index]] = (sum.G + gradient[index], sum.H + hessian[index]);
            if (level == depth)
            {
                foreach (int node in open)
                    nodes[node] = nodes[node] with { Value = -learningRate * sums[node].G / (sums[node].H + lambda) };
                break;
            }
            // Histograms of gradient and hessian by node, feature and bin.
            var histograms = open.ToDictionary(node => node, _ => new (double G, double H)[width][]);
            foreach (int node in open)
                for (int feature = 0; feature < width; feature++) histograms[node][feature] = new (double, double)[thresholds[feature].Length + 1];
            for (int index = 0; index < count; index++)
            {
                if (!histograms.TryGetValue(at[index], out var histogram)) continue;
                for (int feature = 0; feature < width; feature++)
                {
                    ref (double G, double H) cell = ref histogram[feature][binned[feature][index]];
                    cell.G += gradient[index];
                    cell.H += hessian[index];
                }
            }
            var next = new List<int>();
            var split = new Dictionary<int, (int Feature, int Bin)>();
            foreach (int node in open)
            {
                (double total, double totalHessian) = sums[node];
                double parent = total * total / (totalHessian + lambda), best = 1e-9;
                (int Feature, int Bin)? choice = null;
                for (int feature = 0; feature < width; feature++)
                {
                    double g = 0, h = 0;
                    (double G, double H)[] histogram = histograms[node][feature];
                    for (int bin = 0; bin < histogram.Length - 1; bin++)
                    {
                        g += histogram[bin].G;
                        h += histogram[bin].H;
                        double gRight = total - g, hRight = totalHessian - h;
                        if (h < minimumHessian || hRight < minimumHessian) continue;
                        double gain = g * g / (h + lambda) + gRight * gRight / (hRight + lambda) - parent;
                        if (gain > best) (best, choice) = (gain, (feature, bin));
                    }
                }
                if (choice is not { } chosen)
                {
                    nodes[node] = nodes[node] with { Value = -learningRate * total / (totalHessian + lambda) };
                    continue;
                }
                int left = nodes.Count, right = left + 1;
                nodes.Add(new TreeNode(-1, 0, -1, -1, 0));
                nodes.Add(new TreeNode(-1, 0, -1, -1, 0));
                nodes[node] = new TreeNode(chosen.Feature, thresholds[chosen.Feature][chosen.Bin], left, right, 0);
                split[node] = chosen;
                next.Add(left);
                next.Add(right);
            }
            for (int index = 0; index < count; index++)
                if (split.TryGetValue(at[index], out var chosen))
                    at[index] = binned[chosen.Feature][index] <= chosen.Bin ? nodes[at[index]].Left : nodes[at[index]].Right;
            if (next.Count == 0) break;
            open = next;
        }
        return [.. nodes];
    }

    /// <summary>The isotonic fit by pool-adjacent-violators: each block's mean score and level, clamped to [0.001, 0.999].</summary>
    internal static (double[] Scores, double[] Probabilities) Isotonic(IReadOnlyList<(double Score, double Value)> rows)
    {
        var blocks = new List<(double ScoreSum, double ValueSum, int Count)>();
        foreach ((double score, double value) in rows.OrderBy(row => row.Score))
        {
            blocks.Add((score, value, 1));
            while (blocks.Count > 1 && blocks[^2].ValueSum / blocks[^2].Count > blocks[^1].ValueSum / blocks[^1].Count)
            {
                var last = blocks[^1];
                blocks.RemoveAt(blocks.Count - 1);
                blocks[^1] = (blocks[^1].ScoreSum + last.ScoreSum, blocks[^1].ValueSum + last.ValueSum, blocks[^1].Count + last.Count);
            }
        }
        var scores = new List<double>();
        var probabilities = new List<double>();
        foreach ((double scoreSum, double valueSum, int count) in blocks)
        {
            double mean = scoreSum / count;
            if (scores.Count > 0 && mean <= scores[^1]) continue;
            scores.Add(mean);
            probabilities.Add(Math.Clamp(valueSum / count, 0.001, 0.999));
        }
        return ([.. scores], [.. probabilities]);
    }
}

/// <summary>The versioned boosted-tree evaluation document under <c>data/ai</c> (bot plan S4.2c).</summary>
public sealed record TreeEvaluationDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Version,
    [property: JsonRequired] IReadOnlyList<string> FeatureNames,
    [property: JsonRequired] BoostedTreeScorer Corp,
    [property: JsonRequired] BoostedTreeScorer Runner,
    string? Provenance = null)
{
    public BoostedTreeScorer For(PlayerSide side) => side == PlayerSide.Corp ? Corp : Runner;
}
