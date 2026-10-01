using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match;
using Netrunner.Match.Review;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-codex-review &lt;games-directory&gt; --side corp|runner --output new-dir [--entrant NAME] [--game ID]...
    /// [--games N] [--skip N] [--model gpt-6.1-sol] [--reasoning low|medium|high] [--codex PATH] [--timeout-minutes N]
    /// [--rules-root DIR]. The P2 review by the Codex CLI instead of a local model: each game's decision log,
    /// decisions, boards, history, the card texts and the rules go into a workspace that Codex reads in a
    /// read-only sandbox, and its answer is held to the findings schema. Unlike ai-llm-review it sends the game
    /// and the card texts to OpenAI. Writes the same reviews and triage sheet, with Codex's token usage.
    /// Exit 0 when every review finished, 1 when one failed, 2 for invalid arguments.
    /// </summary>
    private static int RunAiCodexReview(string[] args, TextWriter output, TextWriter error)
    {
        string directory, destination = "", rulesRoot = Path.Combine("data", "vendor", "rules"), model = "gpt-6.1-sol", codex = "codex";
        string reasoning = "medium";
        PlayerSide side = PlayerSide.Corp;
        string? entrant = null;
        var only = new HashSet<string>(StringComparer.Ordinal);
        int games = 5, skip = 0, timeoutMinutes = 40;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-codex-review requires a directory of saved ladder games.");
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
                    case "--games": games = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--skip": skip = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--model": model = Next(); break;
                    case "--reasoning":
                        reasoning = Next();
                        if (reasoning is not ("low" or "medium" or "high" or "xhigh")) throw new ArgumentException("--reasoning requires low, medium, high or xhigh.");
                        break;
                    case "--codex": codex = Next(); break;
                    case "--timeout-minutes": timeoutMinutes = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--rules-root": rulesRoot = Next(); break;
                    case "--output":
                        destination = Next();
                        if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("--output must name a new directory.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-codex-review argument '{args[index]}'.");
                }
            }
            if (!sideGiven) throw new ArgumentException("ai-codex-review requires --side corp or --side runner.");
            if (destination.Length == 0) throw new ArgumentException("ai-codex-review requires --output <new directory>.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-codex-review needs previously provisioned official content embedded at build time.");
            if (!File.Exists(Path.Combine(rulesRoot, "index.json"))) throw new ArgumentException($"No rules index under {rulesRoot}.");
        }
        catch (Exception exception) when (IsCommandFailure(exception) || exception is FormatException or OverflowException)
        {
            error.WriteLine($"ai-codex-review failed: {exception.Message}");
            return 2;
        }

        Directory.CreateDirectory(destination);
        RulesIndex rules = RulesIndex.Load(rulesRoot);
        string cards = GameReviewDossier.AllCardTexts();
        var reviews = new List<GameReview>();
        foreach ((LadderSavedGame game, FullMatchDefinition definition) in LadderGameArchive.Read(directory)
            .Where(entry => entrant is null || (side == PlayerSide.Corp ? entry.Game.Game.Corp : entry.Game.Game.Runner) == entrant)
            .Where(entry => only.Count == 0 || only.Contains(entry.Game.Game.Id))
            .Skip(skip).Take(games))
        {
            DateTime started = DateTime.UtcNow;
            GameReviewDossier dossier = GameReviewDossier.BuildAsync(game, definition, side).GetAwaiter().GetResult();
            string name = Path.Combine(destination, SafeCodexName(game.Game.Id));
            string workspace = Path.GetFullPath(name + ".workspace");
            WriteWorkspace(workspace, dossier, cards, rulesRoot);
            (string? answer, CodexUsage usage, string? failure) = RunCodex(codex, model, reasoning, workspace, CodexPrompt(dossier),
                name + ".events.jsonl", TimeSpan.FromMinutes(timeoutMinutes));
            (IReadOnlyList<ReviewFinding> findings, string? parseFailure) = failure is null
                ? LlmGameReviewer.ParseFindings(answer, dossier, rules) : ([], null);
            var review = new GameReview(game.Game.Id, side, dossier.Entrant, findings, usage.Commands, (int)Math.Min(usage.InputTokens, int.MaxValue),
                (int)Math.Min(usage.OutputTokens, int.MaxValue),
                failure ?? parseFailure);
            reviews.Add(review);
            File.WriteAllText(name + ".review.json", EngineJson.Serialize(new
            {
                schemaVersion = 1,
                review,
                reviewer = "codex",
                model,
                reasoning,
                usage,
                decisions = dossier.Decisions.Count,
            }));
            File.WriteAllText(name + ".decisions.txt", dossier.DecisionLog());
            output.WriteLine(FormattableString.Invariant(
                $"ai-codex-review {game.Game.Id} {side}: {findings.Count} findings ({findings.Count(finding => finding.Verified)} cited), {usage.Commands} commands, {usage.InputTokens} input ({usage.CachedInputTokens} cached) + {usage.OutputTokens} output ({usage.ReasoningTokens} reasoning) tokens, {(DateTime.UtcNow - started).TotalSeconds:0}s{(review.Failure is null ? "" : $", failed: {review.Failure}")}"));
        }
        File.WriteAllText(Path.Combine(destination, "triage.md"), TriageSheet(reviews, $"codex {model} ({reasoning})", new LlmSampling()));
        output.WriteLine(FormattableString.Invariant(
            $"ai-codex-review games={reviews.Count} findings={reviews.Sum(review => review.Findings.Count)} failures={reviews.Count(review => review.Failure is not null)} input-tokens={reviews.Sum(review => (long)review.PromptTokens)} output-tokens={reviews.Sum(review => (long)review.CompletionTokens)} output={Path.GetFullPath(destination)}"));
        return reviews.Any(review => review.Failure is not null) ? 1 : 0;
    }

    /// <summary>Codex's token use for one review, summed over its turns, and the shell commands it ran.</summary>
    private sealed record CodexUsage(long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningTokens, int Commands)
    {
        public static CodexUsage None { get; } = new(0, 0, 0, 0, 0);
    }

    private static void WriteWorkspace(string workspace, GameReviewDossier dossier, string cards, string rulesRoot)
    {
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "decisions.txt"), dossier.DecisionLog());
        File.WriteAllText(Path.Combine(workspace, "decision-details.txt"), dossier.AllDecisionDetails());
        File.WriteAllText(Path.Combine(workspace, "positions.txt"), dossier.AllPositions());
        File.WriteAllText(Path.Combine(workspace, "history.txt"), dossier.History(0, long.MaxValue, null, int.MaxValue));
        File.WriteAllText(Path.Combine(workspace, "cards.txt"), cards);
        string rules = Path.Combine(workspace, "rules");
        Directory.CreateDirectory(rules);
        foreach (string file in Directory.GetFiles(Path.Combine(rulesRoot, "chapters"), "*.md"))
            File.Copy(file, Path.Combine(rules, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(workspace, "findings.schema.json"), FindingsSchema);
    }

    private static string CodexPrompt(GameReviewDossier dossier) => $$"""
        You review a game of Android: Netrunner (Null Signal Games' comprehensive rules v26.03) played by two bots.
        Review only the {{dossier.Side}} bot. The files in this directory hold the game; read them with shell commands
        (grep, sed, head) and change nothing:
        - decisions.txt: the bot's decisions, "#sequence turn N kind: chose ...", and below each the options it weighed,
          "*" for the one it took, with its score and the heuristic's reason. Each turn opens with a line "== turn N"
          giving the board and the {{dossier.Side}}'s hand; the opponent's board-changing actions appear in brackets,
          "(Runner #412: ...)", between the decisions.
        - decision-details.txt: every option at every decision, with the bot's priority, score and reason.
        - positions.txt: the whole board before each decision ("Before #N: ..."); the review is offline, nothing is hidden.
        - history.txt: both players' actions in order (passes left out): runs, accesses, installs, rezzes, steals, trashes.
        - cards.txt: every card's printed text, one per line ("unique" is listed only for unique cards).
        - rules/: the comprehensive rules, one file per chapter; rules are numbered like "**7.5.2.**".

        Find plays a competent player would not make: a rule it seems to misread, a card used where it gains
        nothing, an obvious better play missed, and above all a pattern of play that loses games. Look up cards and
        rules rather than guess, check the board in positions.txt before you report a finding, and check any run or
        access your claim relies on in history.txt.

        {{LlmGameReviewer.Knowledge}}

        Your final answer is the findings object the output schema describes. "sequence" is the decision's number
        from decisions.txt. Every finding cites the rule numbers or card titles it relies on. Report what you judge
        likely wrong, with your confidence; an empty findings array is a fine answer when you find nothing.
        """;

    /// <summary>The findings answer as a strict JSON schema (every field required, nothing extra).</summary>
    private const string FindingsSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["findings"],
          "properties": {
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["sequence", "category", "confidence", "claim", "citations"],
                "properties": {
                  "sequence": { "type": "integer" },
                  "category": { "type": "string" },
                  "confidence": { "type": "string", "enum": ["high", "medium", "low"] },
                  "claim": { "type": "string" },
                  "citations": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "additionalProperties": false,
                      "required": ["kind", "ref"],
                      "properties": {
                        "kind": { "type": "string", "enum": ["rule", "card"] },
                        "ref": { "type": "string" }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    /// <summary>Runs <c>codex exec</c> read-only in the workspace; returns its last message, its usage, or why it failed.</summary>
    private static (string? Answer, CodexUsage Usage, string? Failure) RunCodex(string codex, string model, string reasoning, string workspace,
        string prompt, string eventsPath, TimeSpan timeout)
    {
        string last = Path.Combine(workspace, "last-message.txt");
        var start = new ProcessStartInfo(codex)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workspace,
        };
        foreach (string argument in new[]
        {
            "exec", "-m", model, "-c", $"model_reasoning_effort=\"{reasoning}\"", "-s", "read-only", "--skip-git-repo-check",
            "--ephemeral", "--json", "-C", workspace, "--output-schema", Path.Combine(workspace, "findings.schema.json"), "-o", last, "-",
        })
            start.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("codex did not start.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (null, CodexUsage.None, $"codex could not start: {exception.Message}");
        }
        using (process)
        {
            process.StandardInput.Write(prompt);
            process.StandardInput.Close();
            Task<string> events = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                File.WriteAllText(eventsPath, events.Wait(TimeSpan.FromSeconds(10)) ? events.Result : "");
                return (null, CodexUsage.None, $"codex took longer than {timeout.TotalMinutes:0} minutes.");
            }
            string jsonl = events.GetAwaiter().GetResult();
            File.WriteAllText(eventsPath, jsonl);
            CodexUsage usage = ReadUsage(jsonl);
            if (process.ExitCode != 0)
                return (null, usage, $"codex exited with {process.ExitCode}: {errors.GetAwaiter().GetResult().Trim().Split('\n').LastOrDefault()}");
            return (File.Exists(last) ? File.ReadAllText(last) : null, usage, null);
        }
    }

    private static CodexUsage ReadUsage(string jsonl)
    {
        long input = 0, cached = 0, outputTokens = 0, reasoningTokens = 0;
        int commands = 0;
        foreach (string line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }
            string? type = (string?)node?["type"];
            if (type == "turn.completed" && node?["usage"] is JsonObject usage)
            {
                input += (long?)usage["input_tokens"] ?? 0;
                cached += (long?)usage["cached_input_tokens"] ?? 0;
                outputTokens += (long?)usage["output_tokens"] ?? 0;
                reasoningTokens += (long?)usage["reasoning_output_tokens"] ?? 0;
            }
            else if (type == "item.completed" && (string?)node?["item"]?["type"] == "command_execution") commands++;
        }
        return new CodexUsage(input, cached, outputTokens, reasoningTokens, commands);
    }

    private static string SafeCodexName(string id) =>
        string.Concat(id.Select(character => char.IsLetterOrDigit(character) || character is '-' or '.' ? character : '_'));
}
