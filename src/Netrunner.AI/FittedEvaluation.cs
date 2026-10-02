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
    private static readonly Lazy<FittedEvaluationDocument> V1 = new(() => Load(1));
    private static readonly Lazy<FittedEvaluationDocument> V2 = new(() => Load(2));

    /// <summary>The first fitted evaluation (A5), which Corp v9 and Runner v8 plan with.</summary>
    public static FittedEvaluationDocument Document => V1.Value;

    public static FittedEvaluationDocument DocumentFor(int version) => version switch
    {
        1 => V1.Value,
        // Sets 3 and 4 correct one feature of set 2 and keep its fitted weights.
        2 or 3 or 4 => V2.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(version), "Unknown fitted evaluation."),
    };

    public static double WinProbability(AgentFeatures features, ICardEvaluationCatalog catalog, int version = 1)
    {
        ArgumentNullException.ThrowIfNull(features);
        AgentOutcome outcome = features.Board.Outcome;
        if (outcome.Kind != GameOutcomeKind.Ongoing) return outcome.Winner == features.Side ? 1 : 0;
        FittedEvaluationDocument document = DocumentFor(version);
        LogisticSideModel model = features.Side == PlayerSide.Corp ? document.Corp : document.Runner;
        return model.Probability(EvaluationFeatures.Extract(features, catalog, version));
    }

    /// <summary>The win probability as a planner score: log-odds in thousandths, with wins and losses at the extremes.</summary>
    public static long Score(AgentFeatures features, ICardEvaluationCatalog catalog, int version = 1)
    {
        double probability = Math.Clamp(WinProbability(features, catalog, version), 1e-6, 1 - 1e-6);
        return (long)Math.Round(Math.Log(probability / (1 - probability)) * 1000);
    }

    /// <summary>The planner score with a personality's shift of its named terms (A6); Balanced is the plain score.</summary>
    public static long Score(AgentFeatures features, ICardEvaluationCatalog catalog, int version, AgentPersonality personality)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (personality == AgentPersonality.Balanced || version == 1 || features.Board.Outcome.Kind != GameOutcomeKind.Ongoing)
            return Score(features, catalog, version);
        FittedEvaluationDocument document = DocumentFor(version);
        LogisticSideModel model = features.Side == PlayerSide.Corp ? document.Corp : document.Runner;
        double[] values = EvaluationFeatures.Extract(features, catalog, version);
        double probability = Math.Clamp(model.Probability(values), 1e-6, 1 - 1e-6);
        return (long)Math.Round(Math.Log(probability / (1 - probability)) * 1000)
            + AgentPersonalities.Shift(personality, values, EvaluationFeatures.NamesFor(version));
    }

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static FittedEvaluationDocument Parse(string json) =>
        JsonSerializer.Deserialize<FittedEvaluationDocument>(json, ReadOptions)
        ?? throw new InvalidOperationException("The fitted evaluation document is empty.");

    private static FittedEvaluationDocument Load(int version)
    {
        string resource = $"Netrunner.AI.Data.ai.evaluation-v{version}.json";
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The fitted evaluation resource '{resource}' is missing.");
        using var reader = new StreamReader(stream);
        FittedEvaluationDocument document = Parse(reader.ReadToEnd());
        if (!document.FeatureNames.SequenceEqual(EvaluationFeatures.NamesFor(version)))
            throw new InvalidOperationException("The fitted evaluation's features don't match the feature extractor; refit it.");
        return document;
    }
}
