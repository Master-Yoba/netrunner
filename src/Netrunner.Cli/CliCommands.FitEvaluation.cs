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
    /// ai-fit-evaluation [--content official|gateway|both] [--games N] [--holdout-games N] [--seed S]
    /// [--workers 1..64] --output file.json. A5: plays Standard self-play, takes each side's view
    /// at the start of every turn, labels it with who won, fits one logistic model per side, and
    /// reports log loss on held-out seeds for the fitted model and for the hand evaluation
    /// (calibrated on the same training games). Exit 0 when the fitted model is better on both sides.
    /// </summary>
    private static int RunAiFitEvaluation(string[] args, TextWriter output, TextWriter error)
    {
        string content = "both";
        int games = 400, holdoutGames = 120, workers = 1, featureVersion = 1;
        ulong seed = 1;
        string? destination = null;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--content" when index + 1 < args.Length:
                        content = args[++index];
                        if (content is not ("official" or "gateway" or "both")) throw new ArgumentException("--content requires official, gateway or both.");
                        break;
                    case "--games" when index + 1 < args.Length: games = int.Parse(args[++index], CultureInfo.InvariantCulture); break;
                    case "--holdout-games" when index + 1 < args.Length: holdoutGames = int.Parse(args[++index], CultureInfo.InvariantCulture); break;
                    case "--seed" when index + 1 < args.Length: seed = ulong.Parse(args[++index], CultureInfo.InvariantCulture); break;
                    case "--workers" when index + 1 < args.Length: workers = Math.Clamp(int.Parse(args[++index], CultureInfo.InvariantCulture), 1, 64); break;
                    case "--output" when index + 1 < args.Length: destination = args[++index]; break;
                    case "--feature-version" when index + 1 < args.Length:
                        featureVersion = int.Parse(args[++index], CultureInfo.InvariantCulture);
                        if (featureVersion is not (1 or 2)) throw new ArgumentException("--feature-version requires 1 or 2.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-fit-evaluation argument '{args[index]}'.");
                }
            }
            if (destination is null) throw new ArgumentException("--output is required.");
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
            _ => [.. RulesFuzzRunner.OfficialContentIds, .. RulesFuzzRunner.GatewayContentIds],
        };
        // Held-out seeds come from a separate base seed, so no training game repeats.
        Samples training = Collect(pool, games, seed, workers, featureVersion);
        Samples holdout = Collect(pool, holdoutGames, seed + 1_000_003, workers, featureVersion);
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
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{side,-6} train={x.Count} holdout={hx.Count} logloss fitted={fittedLoss:F4} hand={handLoss:F4} base-rate={baseLoss:F4} accuracy={accuracy:P1}"));
            report.Add(new { side = side.ToString().ToLowerInvariant(), train = x.Count, holdout = hx.Count, fittedLoss, handLoss, baseLoss, accuracy });
        }
        var document = new FittedEvaluationDocument(1, $"evaluation-v{featureVersion}", EvaluationFeatures.NamesFor(featureVersion), models[PlayerSide.Corp], models[PlayerSide.Runner],
            string.Create(CultureInfo.InvariantCulture,
                $"Fitted by ai-fit-evaluation (feature set {featureVersion}) on {games} Standard self-play games (content {content}, seed {seed}); held out {holdoutGames} games on another seed."));
        File.WriteAllText(destination, JsonSerializer.Serialize(document, FitJsonOptions) + "\n");
        File.WriteAllText(Path.ChangeExtension(destination, ".report.json"),
            JsonSerializer.Serialize(new { schemaVersion = 1, featureVersion, content, games, holdoutGames, seed, sides = report }, FitJsonOptions) + "\n");
        output.WriteLine($"ai-fit-evaluation {(better ? "better" : "not-better")} output={destination}");
        return better ? 0 : 1;
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
