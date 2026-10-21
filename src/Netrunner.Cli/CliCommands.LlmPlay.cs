using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match;
using Netrunner.Match.Llm;
using Netrunner.Match.Review;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-llm-play --output new-dir [--side corp|runner|both] [--games N] [--seed S] [--opponent ENTRANT]
    /// [--temperature T] [--top-p P] [--top-k K] [--reasoning low|medium|xhigh] [--max-tool-calls N]
    /// [--tools none|rules|all] [--thinking-budget TOKENS] [--structured-answer] [--deadline-seconds S]
    /// [--parallel N] [--model NAME] [--rules-root DIR]. The LLM opponent (Wave 4.5, P3) plays Gateway games
    /// against a bot, N at a time (one per server slot; one by default). Each game is saved as
    /// <c>ai-ladder --save-games</c> saves it, with the LLM's decisions beside it, so ai-lint and ai-llm-review
    /// read them. Exit 0 when every game finished without a stall or an error, 1 otherwise, 2 for invalid arguments.
    /// </summary>
    private static int RunAiLlmPlay(string[] args, TextWriter output, TextWriter error)
    {
        string destination = "", opponent = "standard", rulesRoot = Path.Combine("data", "vendor", "rules");
        string? sides = "both", model = null, reasoning = "low";
        int games = 2, maxToolCalls = 3, firstGame = 0, parallel = 1;
        int? thinkingBudget = null;
        bool cardText = true, structured = false;
        TimeSpan? deadline = null;
        ulong seed = 1;
        double? temperature = 0.6, topP = 0.95;
        int? topK = 20;
        OpenAiCompatibleOptions options;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
                switch (args[index])
                {
                    case "--side":
                        sides = Next();
                        if (sides is not ("corp" or "runner" or "both")) throw new ArgumentException("--side requires corp, runner or both.");
                        break;
                    case "--games": games = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--first-game": firstGame = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--seed": seed = ulong.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--opponent": opponent = Next(); break;
                    case "--temperature": temperature = double.Parse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture); break;
                    case "--top-p": topP = double.Parse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture); break;
                    case "--top-k": topK = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--reasoning": reasoning = Next(); break;
                    case "--max-tool-calls": maxToolCalls = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--tools":
                        switch (Next())
                        {
                            case "none": maxToolCalls = 0; break;
                            case "rules": cardText = false; break;
                            case "all": cardText = true; break;
                            default: throw new ArgumentException("--tools requires none, rules or all.");
                        }
                        break;
                    case "--thinking-budget": thinkingBudget = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--structured-answer": structured = true; break;
                    case "--deadline-seconds":
                        deadline = TimeSpan.FromSeconds(double.Parse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture));
                        break;
                    case "--parallel": parallel = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--model": model = Next(); break;
                    case "--rules-root": rulesRoot = Next(); break;
                    case "--output":
                        destination = Next();
                        if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("--output must name a new directory.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-llm-play argument '{args[index]}'.");
                }
            }
            if (destination.Length == 0) throw new ArgumentException("ai-llm-play requires --output <new directory>.");
            if (games is < 1 or > 1000) throw new ArgumentException("--games requires 1 to 1000.");
            if (parallel is < 1 or > 16) throw new ArgumentException("--parallel requires 1 to 16.");
            if (!RatingLadder.EntrantOrder.Contains(opponent)) throw new ArgumentException($"Unknown opponent entrant '{opponent}'.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-llm-play needs previously provisioned official content embedded at build time.");
            options = OpenAiCompatibleChatClient.FromEnvironment()
                ?? throw new ArgumentException("Set NETRUNNER_LLM_BASE_URL and NETRUNNER_LLM_MODEL (for example in .env).");
            if (model is not null) options = options with { Model = model };
        }
        catch (Exception exception) when (IsCommandFailure(exception) || exception is FormatException or OverflowException)
        {
            error.WriteLine($"ai-llm-play failed: {exception.Message}");
            return 2;
        }

        Directory.CreateDirectory(destination);
        var sampling = new LlmSampling(temperature, topP, topK, null, reasoning, ThinkingBudget: thinkingBudget);
        var settings = new ModelDeciderSettings(maxToolCalls, cardText, structured, deadline);
        RulesIndex? rules = File.Exists(Path.Combine(rulesRoot, "index.json")) ? RulesIndex.Load(rulesRoot) : null;
        using var client = new OpenAiCompatibleChatClient(options);
        IReadOnlyList<string> contents = RulesFuzzRunner.ContentIds("gateway");
        TextWriter progress = TextWriter.Synchronized(error);
        var played = new (LadderGameResult Result, List<LlmOpponentDecision> Decisions, string Line)[games];
        Parallel.ForEachAsync(Enumerable.Range(0, games), new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (slot, cancellationToken) =>
        {
            int number = firstGame + slot;
            // Both sides alternate, and the deals walk the Gateway pairings.
            bool llmCorp = sides == "corp" || (sides == "both" && number % 2 == 0);
            string content = contents[number % contents.Count];
            ulong gameSeed = RulesFuzzRunner.DeriveGameSeed(seed, number);
            var game = new LadderGame(llmCorp ? $"llm-vs-{opponent}-{content}-{number + 1:D3}" : $"{opponent}-vs-llm-{content}-{number + 1:D3}",
                content, gameSeed, llmCorp ? "llm" : opponent, llmCorp ? opponent : "llm");
            LlmOpponentAgent? agent = null;
            DateTime started = DateTime.UtcNow;
            LadderGameResult result = await RatingLadder.PlayAsync(game, RatingLadder.DefaultActionCap, saved => LadderGameArchive.Write(destination, saved),
                (entrant, side, definition) => entrant == "llm"
                    ? agent = WithProgress(LlmOpponentAgent.Create(side, definition, client, sampling, rules, settings), game.Id, progress)
                    : RatingLadder.CreateAgent(entrant, side, definition), cancellationToken: cancellationToken).ConfigureAwait(false);
            List<LlmOpponentDecision> decisions = agent?.Decisions ?? [];
            File.WriteAllText(Path.Combine(destination, game.Id.Replace('~', '_') + ".llm.json"),
                EngineJson.Serialize(new { schemaVersion = 1, game = game.Id, model = options.Model, sampling, settings, decisions }));
            string winner = result.Error is not null ? "error" : result.Capped ? "capped" : result.CorpScore == 1 ? "Corp" : "Runner";
            played[slot] = (result, decisions, FormattableString.Invariant(
                $"ai-llm-play {game.Id}: LLM {(llmCorp ? "Corp" : "Runner")}, winner {winner}, {result.Actions} actions, {decisions.Count} asked, {decisions.Count(decision => decision.Outcome == "model" && decision.Chosen != decision.Advised)} overrode the heuristic, {decisions.Count(decision => decision.Outcome == "invalid")} invalid, {decisions.Count(decision => decision.Outcome == "failed")} failed, {(DateTime.UtcNow - started).TotalSeconds:0}s{(result.Error is null ? "" : $": {result.Error}")}"));
        }).GetAwaiter().GetResult();

        int failures = 0, asked = 0, invalid = 0, failed = 0, overrode = 0;
        double seconds = 0;
        foreach ((LadderGameResult result, List<LlmOpponentDecision> decisions, string line) in played)
        {
            if (result.Capped || result.Error is not null) failures++;
            asked += decisions.Count;
            invalid += decisions.Count(decision => decision.Outcome == "invalid");
            failed += decisions.Count(decision => decision.Outcome == "failed");
            overrode += decisions.Count(decision => decision.Outcome == "model" && decision.Chosen != decision.Advised);
            seconds += decisions.Sum(decision => decision.Seconds);
            output.WriteLine(line);
        }
        output.WriteLine(FormattableString.Invariant(
            $"ai-llm-play games={games} stalls-or-errors={failures} asked={asked} overrode={overrode} invalid={invalid} failed={failed} model-seconds={seconds:0} output={Path.GetFullPath(destination)}"));
        return failures == 0 ? 0 : 1;

        static LlmOpponentAgent WithProgress(LlmOpponentAgent agent, string id, TextWriter error)
        {
            agent.Decided = decision => error.WriteLine(FormattableString.Invariant(
                $"  {id} turn {decision.Turn}: option {decision.Chosen} of {decision.Options} (heuristic {decision.Advised}), {decision.Outcome}, {decision.Seconds:0.0}s{(decision.Reason is { Length: > 0 } reason ? $": {reason}" : "")}"));
            return agent;
        }
    }
}
