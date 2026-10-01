using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Netrunner.Match.Llm;
using Netrunner.Match.Review;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-mcp-game --content ID --seed S --side corp|runner --output new-dir [--number N] [--opponent ENTRANT]
    /// [--rules-root DIR]. One game against the bot, played by an outside player over the Model Context Protocol
    /// on stdin and stdout (Codex, through ai-codex-play). The player makes the strategic choices with the
    /// get_choice and choose tools; the heuristic plays the rest of its seat. When the player leaves, the
    /// heuristic finishes the game. The game is saved as ai-ladder --save-games saves it, with the decisions
    /// beside it in result.json.
    /// </summary>
    private static int RunAiMcpGame(string[] args, TextWriter output, TextWriter error)
    {
        string destination = "", content = "", opponent = "standard", rulesRoot = Path.Combine("data", "vendor", "rules");
        ulong seed = 1;
        int number = 1;
        PlayerSide side = PlayerSide.Corp;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
                switch (args[index])
                {
                    case "--content": content = Next(); break;
                    case "--seed": seed = ulong.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--number": number = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    case "--opponent": opponent = Next(); break;
                    case "--rules-root": rulesRoot = Next(); break;
                    case "--side":
                        side = Next() switch
                        {
                            "corp" => PlayerSide.Corp,
                            "runner" => PlayerSide.Runner,
                            _ => throw new ArgumentException("--side requires corp or runner."),
                        };
                        break;
                    case "--output":
                        destination = Next();
                        if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("--output must name a new directory.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-mcp-game argument '{args[index]}'.");
                }
            }
            if (destination.Length == 0 || content.Length == 0) throw new ArgumentException("ai-mcp-game requires --content and --output.");
            if (!RulesFuzzRunner.ContentIds("gateway").Contains(content)) throw new ArgumentException($"Unknown Gateway content '{content}'.");
            if (!RatingLadder.EntrantOrder.Contains(opponent)) throw new ArgumentException($"Unknown opponent entrant '{opponent}'.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-mcp-game needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (IsCommandFailure(exception) || exception is FormatException or OverflowException)
        {
            error.WriteLine($"ai-mcp-game failed: {exception.Message}");
            return 2;
        }

        Directory.CreateDirectory(destination);
        RulesIndex? rules = File.Exists(Path.Combine(rulesRoot, "index.json")) ? RulesIndex.Load(rulesRoot) : null;
        bool corp = side == PlayerSide.Corp;
        var game = new LadderGame(corp ? $"codex-vs-{opponent}-{content}-{number:D3}" : $"{opponent}-vs-codex-{content}-{number:D3}",
            content, RulesFuzzRunner.DeriveGameSeed(seed, number - 1), corp ? "codex" : opponent, corp ? opponent : "codex");
        var decider = new ExternalDecider();
        LlmOpponentAgent? agent = null;
        FullMatchDefinition? played = null;
        string ending = "";
        int invalid = 0;
        string? final = null;
        Task<LadderGameResult> play = Task.Run(async () =>
        {
            LadderGameResult result = await RatingLadder.PlayAsync(game, RatingLadder.DefaultActionCap, saved =>
                {
                    LadderGameArchive.Write(destination, saved);
                    if (played is not null) ending = Ending(saved, played);
                },
                (entrant, seat, definition) =>
                {
                    played = definition;
                    return entrant == "codex"
                        ? agent = WithProgressFile(LlmOpponentAgent.Create(seat, definition, decider), Path.Combine(destination, "progress.log"))
                        : RatingLadder.CreateAgent(entrant, seat, definition);
                }).ConfigureAwait(false);
            string winner = result.Error is not null ? "none (error)" : result.Capped ? "none (action cap)" : result.CorpScore == 1 ? "the Corp" : "the Runner";
            final = $"The game is over: {winner} won{ending} after {result.Actions} actions{(result.Error is null ? "" : $" ({result.Error})")}. You played the {side}.";
            File.WriteAllText(Path.Combine(destination, "result.json"), EngineJson.Serialize(new
            {
                schemaVersion = 1,
                game = game.Id,
                side,
                winner,
                corpScore = result.CorpScore,
                capped = result.Capped,
                error = result.Error,
                actions = result.Actions,
                invalidChoices = invalid,
                decisions = agent?.Decisions ?? [],
            }));
            decider.Finish();
            return result;
        });

        // A client that exits may stop its server with SIGTERM: the heuristic then finishes the game and it is saved.
        using var stopping = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,
            signal =>
            {
                signal.Cancel = true;
                decider.Leave();
                play.Wait(TimeSpan.FromMinutes(2));
                Environment.Exit(0);
            });

        // The Model Context Protocol over stdio: one JSON-RPC message per line, nothing else on stdout.
        using var stdin = new StreamReader(Console.OpenStandardInput());
        while (stdin.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? message;
            try
            {
                message = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }
            if (message?["id"] is not JsonNode id) continue; // a notification: nothing to answer
            string method = (string?)message["method"] ?? "";
            JsonNode? result = method switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = (string?)message["params"]?["protocolVersion"] ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "netrunner-game", ["version"] = "1" },
                },
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = McpTools() },
                "tools/call" => CallTool(message["params"] as JsonObject),
                _ => null,
            };
            JsonObject reply = result is not null
                ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }
                : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Unknown method '{method}'." } };
            output.WriteLine(reply.ToJsonString());
            output.Flush();
        }

        // The player has gone: the heuristic finishes the game.
        decider.Leave();
        play.GetAwaiter().GetResult();
        return 0;

        JsonObject CallTool(JsonObject? call)
        {
            string name = (string?)call?["name"] ?? "";
            JsonObject arguments = call?["arguments"] as JsonObject ?? [];
            string Text(string key) => arguments[key] is JsonValue value ? value.ToString() : "";
            switch (name)
            {
                case "get_choice":
                    return ToolText(Wait());
                case "choose":
                    if (decider.Pending is not { } pending) return ToolText(final ?? "No choice is waiting; call get_choice.", isError: final is null);
                    int option = arguments["option"] is JsonValue value && value.TryGetValue(out int parsed) ? parsed
                        : int.TryParse(Text("option"), NumberStyles.None, CultureInfo.InvariantCulture, out int text) ? text : 0;
                    if (option < 1 || option > pending.Options)
                    {
                        invalid++;
                        return ToolText($"Option {Text("option")} isn't one of 1 to {pending.Options}; nothing was played. Choose again.", isError: true);
                    }
                    decider.Answer(option, Text("reason") is { Length: > 0 } reason ? reason : null);
                    return ToolText(Wait());
                case "card_text":
                    return ToolText(GameReviewDossier.CardText(Text("title")));
                case "rules_lookup":
                    return ToolText(rules?.Search(Text("query")) is { Count: > 0 } found
                        ? string.Join("\n\n", found.Select(rule => $"{rule.Number}: {rule.Text}")) : "No rule matched.");
                default:
                    return ToolText($"There is no tool called '{name}'.", isError: true);
            }
        }

        // The next choice once the bot and the heuristic have played up to it, or the result at the end.
        string Wait()
        {
            Task<PendingChoice?> next = decider.NextAsync();
            if (!next.Wait(TimeSpan.FromMinutes(2))) return "The game is still running; call get_choice again.";
            return next.Result is { } choice
                ? choice.Request + "\nPlay one with choose(option, reason)."
                : final ?? "The game is over.";
        }
    }

    /// <summary>How the game ended, replayed from its record: the win's reason, the agenda points and the turn.</summary>
    private static string Ending(LadderSavedGame saved, FullMatchDefinition definition)
    {
        GameState state = saved.Replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) break;
            state = applied.State;
        }
        int corp = state.Players.Single(player => player.Side == PlayerSide.Corp).AgendaPoints;
        int runner = state.Players.Single(player => player.Side == PlayerSide.Runner).AgendaPoints;
        string reason = state.Outcome.Reason switch
        {
            GameWinReason.AgendaPoints => " on agenda points",
            GameWinReason.DeckOut => " because R&D ran out",
            GameWinReason.Flatline => " by flatline",
            _ => "",
        };
        return FormattableString.Invariant($"{reason} on turn {state.Turn.TurnNumber} (agenda points: Corp {corp}, Runner {runner})");
    }

    /// <summary>One line a decision in the game's directory, to follow a long game (stdout is the protocol's).</summary>
    private static LlmOpponentAgent WithProgressFile(LlmOpponentAgent agent, string path)
    {
        agent.Decided = decision => File.AppendAllText(path, FormattableString.Invariant(
            $"{DateTime.UtcNow:HH:mm:ss} turn {decision.Turn}: option {decision.Chosen} of {decision.Options} (heuristic {decision.Advised}), {decision.Outcome}, {decision.Seconds:0}s{(decision.Reason is { Length: > 0 } reason ? $": {reason}" : "")}{Environment.NewLine}"));
        return agent;
    }

    private static JsonObject ToolText(string text, bool isError = false) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static JsonArray McpTools()
    {
        JsonObject Schema(params (string Name, string Type, string Description, bool Required)[] properties) => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(properties.Select(property => KeyValuePair.Create(property.Name,
                (JsonNode?)new JsonObject { ["type"] = property.Type, ["description"] = property.Description }))),
            ["required"] = new JsonArray([.. properties.Where(property => property.Required).Select(property => (JsonNode)property.Name)]),
        };
        JsonObject Tool(string name, string description, JsonObject schema) =>
            new() { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };
        return
        [
            Tool("get_choice", "Your next decision: the board as you see it, the cards in view, and your numbered options with the heuristic bot's scores. Waits while the bot plays. At the end it reports the result.",
                Schema()),
            Tool("choose", "Plays one of the numbered options of the waiting decision, then returns your next decision (or the result).",
                Schema(("option", "integer", "The option's number.", true), ("reason", "string", "Why, in one sentence.", true))),
            Tool("card_text", "The printed text of any card by title.", Schema(("title", "string", "The card's title.", true))),
            Tool("rules_lookup", "Searches the Netrunner comprehensive rules by rule number or a few words.",
                Schema(("query", "string", "A rule number or a few words.", true))),
        ];
    }
}
