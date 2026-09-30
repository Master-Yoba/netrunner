using Xunit;

namespace Netrunner.AI.Tests;

/// <summary>A5: the fitter and the committed fitted evaluation.</summary>
public sealed class FittedEvaluationTests
{
    [Fact]
    public void TheFitterRecoversARelationshipAndRepeatsExactly()
    {
        // Wins become likelier with the first feature and don't depend on the second.
        var samples = new List<double[]>();
        var outcomes = new List<double>();
        for (int index = 0; index < 400; index++)
        {
            double signal = index % 20 - 10;
            samples.Add([signal, index % 7]);
            outcomes.Add(signal + (index % 3 - 1) * 3 > 0 ? 1 : 0);
        }
        LogisticSideModel model = LogisticSideModel.Fit(samples, outcomes);
        Assert.True(model.Weights[0] > 1, $"signal weight {model.Weights[0]}");
        Assert.True(Math.Abs(model.Weights[1]) < 0.3, $"noise weight {model.Weights[1]}");
        Assert.True(model.Probability([8, 3]) > 0.9 && model.Probability([-8, 3]) < 0.1);
        Assert.Equal(model.Weights, LogisticSideModel.Fit(samples, outcomes).Weights);
        Assert.True(LogisticSideModel.LogLoss(model.Probability, samples, outcomes) < LogisticSideModel.LogLoss(_ => 0.5, samples, outcomes));
    }

    [Fact]
    public void TheCommittedEvaluationMatchesTheFeatureExtractor()
    {
        FittedEvaluationDocument document = FittedEvaluation.Document;
        Assert.Equal(EvaluationFeatures.Names, document.FeatureNames);
        foreach (LogisticSideModel side in new[] { document.Corp, document.Runner })
        {
            Assert.Equal(EvaluationFeatures.Names.Count, side.Weights.Length);
            Assert.Equal(side.Weights.Length, side.Mean.Length);
            Assert.All(side.Deviation, value => Assert.True(value > 0));
        }
    }
}
