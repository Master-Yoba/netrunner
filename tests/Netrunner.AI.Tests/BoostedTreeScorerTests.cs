using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

/// <summary>Bot plan S4.2c: the boosted scalar trees, trained with the pairwise gradient.</summary>
public sealed class BoostedTreeScorerTests
{
    [Fact]
    public void TheTreesLearnAnOrderNoLinearScoreCanAndRepeatExactly()
    {
        // A state is better when exactly one of its two features is high: no weighted sum orders that.
        var random = new DeterministicRandom(4);
        double[][] states = [.. Enumerable.Range(0, 600).Select(_ => new[] { random.NextInt(1000) / 1000.0, random.NextInt(1000) / 1000.0 })];
        static double Value(double[] state) => (state[0] > 0.5) ^ (state[1] > 0.5) ? 0.8 : 0.2;
        var pairs = new List<(int, int)>();
        var weights = new List<double>();
        for (int index = 0; index + 1 < states.Length; index += 2)
        {
            double difference = Value(states[index]) - Value(states[index + 1]);
            if (difference == 0) continue;
            pairs.Add(difference > 0 ? (index, index + 1) : (index + 1, index));
            weights.Add(Math.Abs(difference));
        }
        IReadOnlyList<(double, double)> Calibration(BoostedTreeScorer model) => [.. states.Select(state => (model.Score(state), Value(state)))];

        BoostedTreeScorer model = BoostedTreeScorer.Fit(states, pairs, weights, Calibration, trees: 60);

        int right = 0, compared = 0;
        for (int i = 0; i < states.Length; i += 7)
            for (int j = i + 1; j < states.Length; j += 11)
            {
                double truth = Value(states[i]) - Value(states[j]);
                if (truth == 0) continue;
                compared++;
                if ((model.Score(states[i]) - model.Score(states[j])) * truth > 0) right++;
            }
        Assert.True(right > 0.9 * compared, $"{right} of {compared}");
        // The calibration keeps the score's order and maps it into a probability.
        Assert.True(model.Probability([0.9, 0.1]) > model.Probability([0.9, 0.9]));
        Assert.InRange(model.Probability([0.1, 0.9]), 0.001, 0.999);
        Assert.Equal(model.Trees.SelectMany(tree => tree), BoostedTreeScorer.Fit(states, pairs, weights, Calibration, trees: 60).Trees.SelectMany(tree => tree));
    }
}
