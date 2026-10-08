using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Bot plan S4.5: one side's apprentice, a scalar scorer <c>g(root, candidate)</c> learned from the teacher's order of a
/// root's candidates: a linear model over <see cref="ApprenticeFeatures.Combine"/>'s terms (its logit is the score), or
/// boosted trees over the candidate's and the root's terms side by side.
/// </summary>
public sealed record ApprenticeSide(LogisticSideModel? Linear, BoostedTreeScorer? Trees)
{
    public double Score(double[] candidate, double[] root)
    {
        if (Linear is { } linear)
        {
            double[] values = ApprenticeFeatures.Combine(candidate, root);
            double logit = linear.Bias;
            for (int index = 0; index < linear.Weights.Length; index++)
                logit += linear.Weights[index] * (values[index] - linear.Mean[index]) / linear.Deviation[index];
            return logit;
        }
        return Trees is { } trees ? trees.Score([.. candidate, .. root]) : throw new InvalidOperationException("The apprentice has no model.");
    }
}

/// <summary>The versioned apprentice document under <c>data/ai</c> (bot plan S4.5).</summary>
public sealed record ApprenticeDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Version,
    [property: JsonRequired] IReadOnlyList<string> CandidateNames,
    [property: JsonRequired] ApprenticeSide Corp,
    [property: JsonRequired] ApprenticeSide Runner,
    string? Provenance = null)
{
    public ApprenticeSide For(PlayerSide side) => side == PlayerSide.Corp ? Corp : Runner;
}

/// <summary>
/// The apprentice at the root (bot plan S4.5, behind the profile parameter <c>apprentice 1</c>): at a turn action, the
/// heuristic's candidates of its top priority band are re-ordered by the apprentice's score; priorities and vetoes are
/// untouched, so the heuristic stays the generator and never ranks up a vetoed action.
/// </summary>
public static class Apprentice
{
    public const string ParameterKey = "apprentice";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<ApprenticeDocument> V1 = new(() =>
    {
        const string name = "Netrunner.AI.Data.ai.apprentice-v1.json";
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded {name}.");
        return Parse(new StreamReader(stream).ReadToEnd());
    });

    /// <summary>The committed apprentice.</summary>
    public static ApprenticeDocument Document => V1.Value;

    public static ApprenticeDocument Parse(string json)
    {
        ApprenticeDocument document = JsonSerializer.Deserialize<ApprenticeDocument>(json, ReadOptions)
            ?? throw new InvalidOperationException("The apprentice document is empty.");
        if (!document.CandidateNames.SequenceEqual(ApprenticeFeatures.Names))
            throw new InvalidOperationException("The apprentice document's terms are not the extractor's.");
        return document;
    }

    public static bool Applies(OfflineAgentConfiguration configuration, AgentFeatures features) =>
        configuration.Parameters.GetValueOrDefault(ParameterKey) == 1 && features.Decision?.Kind == DecisionKind.TurnAction
        && features.Actions.Length > 1;

    /// <summary>The candidates with the top band's scores replaced by the apprentice's, in millionths, its order kept.</summary>
    public static ImmutableArray<AgentCandidate> Reorder(AgentFeatures features, ImmutableArray<AgentCandidate> candidates,
        AgentKnowledge knowledge, ICardEvaluationCatalog catalog, ApprenticeDocument? document = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ApprenticeSide model = (document ?? Document).For(features.Side);
        double[] root = EvaluationFeatures.Extract(features, catalog, 5, knowledge);
        int top = candidates.Max(candidate => candidate.Utility.Priority);
        var actions = features.Actions.ToDictionary(action => action.Id);
        return [.. candidates.Select(candidate => candidate.Utility.Priority != top ? candidate : candidate with
        {
            Utility = candidate.Utility with
            {
                Score = (int)Math.Clamp(Math.Round(model.Score(ApprenticeFeatures.Extract(features, actions[candidate.ActionId], candidates, catalog, knowledge),
                    root) * 1_000_000), int.MinValue / 2, int.MaxValue / 2),
                Reason = $"apprentice ({candidate.Utility.Reason})",
            },
        })];
    }
}
