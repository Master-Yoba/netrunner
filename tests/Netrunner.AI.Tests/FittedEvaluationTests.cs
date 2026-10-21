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

    [Fact]
    public void UnitWeightsFitAsNoneAndASoftTargetIsRecovered()
    {
        // S4.2: the teacher's value is a soft target, and the outcome rows carry a weight.
        var samples = Enumerable.Range(0, 200).Select(index => new double[] { index % 10, index % 3 }).ToList();
        var soft = Enumerable.Repeat(0.7, samples.Count).ToList();
        LogisticSideModel plain = LogisticSideModel.Fit(samples, soft);
        LogisticSideModel weighted = LogisticSideModel.Fit(samples, soft, sampleWeights: [.. Enumerable.Repeat(1.0, samples.Count)]);

        Assert.Equal(plain.Weights, weighted.Weights);
        Assert.Equal(0.7, plain.Probability([4, 1]), 2);
        // A light outcome row moves the fit far less than a full one.
        var outcomes = soft.Append(1).ToList();
        var withRow = samples.Append([4, 1]).ToList();
        double light = LogisticSideModel.Fit(withRow, outcomes, sampleWeights: [.. soft.Select(_ => 1.0), 0.2]).Probability([4, 1]);
        double full = LogisticSideModel.Fit(withRow, outcomes).Probability([4, 1]);
        Assert.True(light > 0.7 && light < full, $"light {light}, full {full}");
        Assert.Throws<ArgumentException>(() => LogisticSideModel.Fit(samples, soft, sampleWeights: [1.0]));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 4)]
    [InlineData(7, 5)]
    [InlineData(8, 4)]
    public void TheTeacherFitsReadTheirFeatureSets(int version, int featureSet)
    {
        // S4.2: version 5 is set 5 fitted to the teacher; version 6 is set 4's features refitted to it.
        Assert.Equal(featureSet, FittedEvaluation.FeatureSetOf(version));
        FittedEvaluationDocument document = FittedEvaluation.DocumentFor(version);
        Assert.Equal(EvaluationFeatures.NamesFor(featureSet), document.FeatureNames);
        foreach (LogisticSideModel side in new[] { document.Corp, document.Runner })
            Assert.Equal(EvaluationFeatures.NamesFor(featureSet).Count, side.Weights.Length);
    }
}
