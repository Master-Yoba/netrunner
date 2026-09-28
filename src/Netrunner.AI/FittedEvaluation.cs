using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>One side's logistic model: standardized features, weights and a bias.</summary>
public sealed record LogisticSideModel(
    [property: JsonRequired] double[] Mean,
    [property: JsonRequired] double[] Deviation,
    [property: JsonRequired] double[] Weights,
    [property: JsonRequired] double Bias)
{
    public double Probability(IReadOnlyList<double> features)
    {
        double logit = Bias;
        for (int index = 0; index < Weights.Length; index++)
            logit += Weights[index] * (features[index] - Mean[index]) / Deviation[index];
        return 1 / (1 + Math.Exp(-logit));
    }

    /// <summary>
    /// Deterministic ridge-regularized logistic regression by batch gradient
    /// descent on standardized features (A5). Small, linear and reproducible:
    /// the same samples always give the same weights.
    /// </summary>
    public static LogisticSideModel Fit(IReadOnlyList<double[]> samples, IReadOnlyList<double> outcomes, double ridge = 0.01,
        int iterations = 4000, double learningRate = 0.5)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (samples.Count == 0 || samples.Count != outcomes.Count) throw new ArgumentException("Samples and outcomes must pair up.");
        int width = samples[0].Length;
        double[] mean = new double[width], deviation = new double[width];
        for (int feature = 0; feature < width; feature++)
        {
            mean[feature] = samples.Average(sample => sample[feature]);
            double variance = samples.Average(sample => Math.Pow(sample[feature] - mean[feature], 2));
            deviation[feature] = variance > 1e-12 ? Math.Sqrt(variance) : 1;
        }
        double[][] standardized = samples.Select(sample => sample.Select((value, feature) => (value - mean[feature]) / deviation[feature]).ToArray()).ToArray();
        double[] weights = new double[width];
        double bias = 0;
        double[] gradient = new double[width];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            Array.Clear(gradient);
            double biasGradient = 0;
            for (int sample = 0; sample < standardized.Length; sample++)
            {
                double logit = bias;
                for (int feature = 0; feature < width; feature++) logit += weights[feature] * standardized[sample][feature];
                double residual = outcomes[sample] - 1 / (1 + Math.Exp(-logit));
                biasGradient += residual;
                for (int feature = 0; feature < width; feature++) gradient[feature] += residual * standardized[sample][feature];
            }
            bias += learningRate * biasGradient / standardized.Length;
            for (int feature = 0; feature < width; feature++)
                weights[feature] += learningRate * (gradient[feature] / standardized.Length - ridge * weights[feature]);
        }
        return new(mean, deviation, weights, bias);
    }

    /// <summary>Mean log loss of the model's predictions (lower is better).</summary>
    public static double LogLoss(Func<double[], double> predict, IReadOnlyList<double[]> samples, IReadOnlyList<double> outcomes)
    {
        ArgumentNullException.ThrowIfNull(predict);
        double total = 0;
        for (int index = 0; index < samples.Count; index++)
        {
            double probability = Math.Clamp(predict(samples[index]), 1e-6, 1 - 1e-6);
            total -= outcomes[index] * Math.Log(probability) + (1 - outcomes[index]) * Math.Log(1 - probability);
        }
        return total / samples.Count;
    }
}

/// <summary>The versioned fitted evaluation document under <c>data/ai</c>.</summary>
public sealed record FittedEvaluationDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Version,
    [property: JsonRequired] IReadOnlyList<string> FeatureNames,
    [property: JsonRequired] LogisticSideModel Corp,
    [property: JsonRequired] LogisticSideModel Runner,
    string? Provenance = null);

/// <summary>
/// A5: the position evaluation fitted to self-play outcomes: the observing
/// side's probability of winning, from <see cref="EvaluationFeatures"/>. The
/// planner compares positions on its log-odds.
/// </summary>
public static class FittedEvaluation
{
    private const string ResourceName = "Netrunner.AI.Data.ai.evaluation-v1.json";
    private static readonly Lazy<FittedEvaluationDocument> Embedded = new(Load);

    public static FittedEvaluationDocument Document => Embedded.Value;

    public static double WinProbability(AgentFeatures features, ICardEvaluationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(features);
        AgentOutcome outcome = features.Board.Outcome;
        if (outcome.Kind != GameOutcomeKind.Ongoing) return outcome.Winner == features.Side ? 1 : 0;
        LogisticSideModel model = features.Side == PlayerSide.Corp ? Document.Corp : Document.Runner;
        return model.Probability(EvaluationFeatures.Extract(features, catalog));
    }

    /// <summary>The win probability as a planner score: log-odds in thousandths, with wins and losses at the extremes.</summary>
    public static long Score(AgentFeatures features, ICardEvaluationCatalog catalog)
    {
        double probability = Math.Clamp(WinProbability(features, catalog), 1e-6, 1 - 1e-6);
        return (long)Math.Round(Math.Log(probability / (1 - probability)) * 1000);
    }

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static FittedEvaluationDocument Parse(string json) =>
        JsonSerializer.Deserialize<FittedEvaluationDocument>(json, ReadOptions)
        ?? throw new InvalidOperationException("The fitted evaluation document is empty.");

    private static FittedEvaluationDocument Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The fitted evaluation resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        FittedEvaluationDocument document = Parse(reader.ReadToEnd());
        if (!document.FeatureNames.SequenceEqual(EvaluationFeatures.Names))
            throw new InvalidOperationException("The fitted evaluation's features don't match the feature extractor; refit it.");
        return document;
    }
}
