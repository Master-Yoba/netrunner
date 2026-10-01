using System.Globalization;
using System.Text;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match;
using Netrunner.Match.Review;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-llm-review &lt;games-directory&gt; --side corp|runner [--entrant NAME] [--game ID]... [--games N] [--skip N]
    /// [--temperature T] [--top-p P] [--top-k K] [--seed S] [--reasoning low|medium|high] [--max-tool-calls N]
    /// [--no-check] [--rules-root DIR] --output new-dir. The LLM game reviewer (Wave 4.5, P2) over games saved by
    /// <c>ai-ladder --save-games</c>, with the model from NETRUNNER_LLM_BASE_URL and NETRUNNER_LLM_MODEL
    /// (and NETRUNNER_LLM_API_KEY). Writes one review per game and a triage sheet. Exit 0 when every
    /// review finished, 1 when one failed, 2 for invalid arguments.
    /// </summary>
    private static int RunAiLlmReview(string[] args, TextWriter output, TextWriter error)
    {
        string directory, destination = "", rulesRoot = Path.Combine("data", "vendor", "rules");
        PlayerSide side = PlayerSide.Corp;
        var only = new HashSet<string>(StringComparer.Ordinal);
        bool check = true;
        string? entrant = null, reasoning = Environment.GetEnvironmentVariable("NETRUNNER_LLM_REASONING_EFFORT") ?? "medium";
        int games = 5, skip = 0, maxToolCalls = 60;
        double? temperature = 0.6, topP = 0.95;
        int? topK = 20, seed = 1;
        OpenAiCompatibleOptions options;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-llm-review requires a directory of saved ladder games.");
            directory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Games directory does not exist: {directory}");
            bool sideGiven = false;
            for (int index = 2; index < args.Length; index++)
            {
                string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
                switch (args[index])
                {
                    case "--side":
                        side = Next() switch
                        {
                            "corp" => PlayerSide.Corp,
                            "runner" => PlayerSide.Runner,
                            _ => throw new ArgumentException("--side requires corp or runner."),
                        };
                        sideGiven = true;
                        break;
                    case "--entrant": entrant = Next(); break;
                    case "--game": only.Add(Next()); break;
                    case "--no-check": check = false; break;
                    case "--games": games = ParseInt(Next(), "--games", 1, 1000); break;
                    case "--skip": skip = ParseInt(Next(), "--skip", 0, 100_000); break;
                    case "--max-tool-calls": maxToolCalls = ParseInt(Next(), "--max-tool-calls", 0, 500); break;
                    case "--temperature": temperature = ParseDouble(Next(), "--temperature", 0, 2); break;
                    case "--top-p": topP = ParseDouble(Next(), "--top-p", 0, 1); break;
                    case "--top-k": topK = ParseInt(Next(), "--top-k", 0, 1000); break;
                    case "--seed": seed = ParseInt(Next(), "--seed", 0, int.MaxValue); break;
                    case "--reasoning":
                        reasoning = Next();
                        if (reasoning is not ("low" or "medium" or "high" or "xhigh")) throw new ArgumentException("--reasoning requires low, medium, high or xhigh.");
                        break;
                    case "--rules-root": rulesRoot = Next(); break;
                    case "--output":
                        destination = Next();
                        if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("--output must name a new directory.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-llm-review argument '{args[index]}'.");
                }
            }
            if (!sideGiven) throw new ArgumentException("ai-llm-review requires --side corp or --side runner.");
            if (destination.Length == 0) throw new ArgumentException("ai-llm-review requires --output <new directory>.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-llm-review needs previously provisioned official content embedded at build time.");
            options = OpenAiCompatibleChatClient.FromEnvironment()
                ?? throw new ArgumentException("Set NETRUNNER_LLM_BASE_URL and NETRUNNER_LLM_MODEL (for example in .env).");
            if (!File.Exists(Path.Combine(rulesRoot, "index.json"))) throw new ArgumentException($"No rules index under {rulesRoot}.");
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            error.WriteLine($"ai-llm-review failed: {exception.Message}");
            return 2;
        }

        Directory.CreateDirectory(destination);
        var sampling = new LlmSampling(temperature, topP, topK, seed, reasoning);
        RulesIndex rules = RulesIndex.Load(rulesRoot);
        using var client = new OpenAiCompatibleChatClient(options);
        var reviewer = new LlmGameReviewer(client, rules, sampling, maxToolCalls);
        var reviews = new List<GameReview>();
        foreach ((LadderSavedGame game, FullMatchDefinition definition) in LadderGameArchive.Read(directory)
            .Where(entry => entrant is null || (side == PlayerSide.Corp ? entry.Game.Game.Corp : entry.Game.Game.Runner) == entrant)
            .Where(entry => only.Count == 0 || only.Contains(entry.Game.Game.Id))
            .Skip(skip).Take(games))
        {
            DateTime started = DateTime.UtcNow;
            GameReviewDossier dossier = GameReviewDossier.BuildAsync(game, definition, side).GetAwaiter().GetResult();
            GameReview review = reviewer.ReviewAsync(dossier, calls => error.WriteLine($"  {game.Game.Id}: {calls}"), check).GetAwaiter().GetResult();
            reviews.Add(review);
            string name = Path.Combine(destination, SafeName(game.Game.Id));
            File.WriteAllText(name + ".review.json",
                EngineJson.Serialize(new { schemaVersion = 1, review, sampling, model = options.Model, decisions = dossier.Decisions.Count }));
            File.WriteAllText(name + ".transcript.json", EngineJson.Serialize(review.Transcript));
            File.WriteAllText(name + ".decisions.txt", dossier.DecisionLog());
            output.WriteLine(FormattableString.Invariant(
                $"ai-llm-review {game.Game.Id} {side}: {review.Findings.Count} findings ({review.Findings.Count(finding => finding.Verified)} cited, {review.Findings.Count(finding => finding.Check == "refuted")} refuted), {review.ToolCalls} tool calls, {review.PromptTokens}+{review.CompletionTokens} tokens, {(DateTime.UtcNow - started).TotalSeconds:0}s{(review.Failure is null ? "" : $", failed: {review.Failure}")}"));
        }
        File.WriteAllText(Path.Combine(destination, "triage.md"), TriageSheet(reviews, options.Model, sampling));
        output.WriteLine($"ai-llm-review games={reviews.Count} findings={reviews.Sum(review => review.Findings.Count)} failures={reviews.Count(review => review.Failure is not null)} output={Path.GetFullPath(destination)}");
        return reviews.Any(review => review.Failure is not null) ? 1 : 0;

        static int ParseInt(string text, string name, int minimum, int maximum) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= minimum && value <= maximum
                ? value : throw new ArgumentException($"{name} requires an integer from {minimum} to {maximum}.");
        static double ParseDouble(string text, string name, double minimum, double maximum) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value >= minimum && value <= maximum
                ? value : throw new ArgumentException($"{name} requires a number from {minimum} to {maximum}.");
        static string SafeName(string id) =>
            string.Concat(id.Select(character => char.IsLetterOrDigit(character) || character is '-' or '.' ? character : '_'));
    }

    /// <summary>A sheet to triage the findings: one row each, with a verdict column to fill in.</summary>
    private static string TriageSheet(IReadOnlyList<GameReview> reviews, string model, LlmSampling sampling)
    {
        var text = new StringBuilder();
        text.AppendLine("# LLM review triage");
        text.AppendLine();
        text.AppendLine(FormattableString.Invariant($"Model `{model}`, temperature {sampling.Temperature}, top-p {sampling.TopP}, top-k {sampling.TopK}, seed {sampling.Seed}, reasoning {sampling.ReasoningEffort}."));
        text.AppendLine();
        text.AppendLine("Verdicts: confirmed, wrong, duplicate, unsure.");
        text.AppendLine();
        text.AppendLine("A second look tries to refute each finding from the game; refuted ones are listed last.");
        text.AppendLine();
        text.AppendLine("| # | Game | Side | Seq | Confidence | Category | Claim | Cites | Second look | Verdict |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        int number = 0;
        foreach (ReviewFinding finding in reviews.SelectMany(review => review.Findings).OrderBy(finding => finding.Check == "refuted"))
            text.AppendLine(FormattableString.Invariant(
                $"| {++number} | {finding.GameId} | {finding.Side} | {finding.Sequence} | {finding.Confidence} | {finding.Category} | {finding.Claim.Replace("|", "/", StringComparison.Ordinal)} | {string.Join(", ", finding.Citations.Select(citation => $"{citation.Kind} {citation.Reference}{(citation.Resolved ? "" : " (unresolved)")}"))}{(finding.Verified ? "" : " — unverified")} | {finding.Check ?? "none"}{(finding.CheckReason is { Length: > 0 } reason ? ": " + reason.Replace("|", "/", StringComparison.Ordinal) : "")} | |"));
        return text.ToString();
    }
}
