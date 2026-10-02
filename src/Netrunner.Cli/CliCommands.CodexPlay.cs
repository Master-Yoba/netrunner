using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-codex-play --output new-dir [--side corp|runner|both] [--games N] [--first-game N] [--seed S]
    /// [--opponent ENTRANT] [--model gpt-6.1-sol] [--reasoning low|medium|high] [--codex PATH] [--timeout-minutes N].
    /// Codex plays Gateway games against the bot, one session a game, through the ai-mcp-game server: it makes
    /// the strategic choices, the heuristic the rest of its seat. Each game is saved in its own directory as
    /// ai-ladder --save-games saves it, so ai-lint and the reviews read them. It sends the boards Codex sees to
    /// OpenAI. Exit 0 when every game finished, 1 otherwise, 2 for invalid arguments.
    /// </summary>
    private static int RunAiCodexPlay(string[] args, TextWriter output, TextWriter error)
    {
        string destination = "", opponent = "standard", model = "gpt-6.1-sol", reasoning = "medium", codex = "codex", sides = "both";
        int games = 1, firstGame = 0, timeoutMinutes = 120;
        ulong seed = 1;
        bool printDeals = false;
        string? printPrompt = null;
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
                    case "--model": model = Next(); break;
                    case "--reasoning":
                        reasoning = Next();
                        if (reasoning is not ("low" or "medium" or "high" or "xhigh")) throw new ArgumentException("--reasoning requires low, medium, high or xhigh.");
                        break;
                    case "--codex": codex = Next(); break;
                    case "--timeout-minutes": timeoutMinutes = int.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture); break;
                    // For other players of the same deals (scripts/ai-claude-play.sh): the games' contents and seats, or the prompt.
                    case "--print-deals": printDeals = true; break;
                    case "--print-prompt":
                        printPrompt = Next();
                        if (printPrompt is not ("corp" or "runner")) throw new ArgumentException("--print-prompt requires corp or runner.");
                        break;
                    case "--output":
                        destination = Next();
                        if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("--output must name a new directory.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-codex-play argument '{args[index]}'.");
                }
            }
            if (destination.Length == 0 && !printDeals && printPrompt is null) throw new ArgumentException("ai-codex-play requires --output <new directory>.");
            if (games is < 1 or > 1000) throw new ArgumentException("--games requires 1 to 1000.");
            if (!RatingLadder.EntrantOrder.Contains(opponent)) throw new ArgumentException($"Unknown opponent entrant '{opponent}'.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-codex-play needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (IsCommandFailure(exception) || exception is FormatException or OverflowException)
        {
            error.WriteLine($"ai-codex-play failed: {exception.Message}");
            return 2;
        }

        if (printPrompt is not null)
        {
            output.WriteLine(CodexPlayPrompt(printPrompt));
            return 0;
        }
        if (printDeals)
        {
            IReadOnlyList<string> deals = RulesFuzzRunner.ContentIds("gateway");
            for (int number = firstGame + 1; number <= firstGame + games; number++)
                output.WriteLine(FormattableString.Invariant(
                    $"{number:D3} {deals[(number - 1) % deals.Count]} {(sides == "corp" || (sides == "both" && number % 2 == 1) ? "corp" : "runner")}"));
            return 0;
        }

        Directory.CreateDirectory(destination);
        string host = Environment.ProcessPath ?? "dotnet";
        string cli = typeof(CliCommands).Assembly.Location;
        string rulesRoot = Path.GetFullPath(Path.Combine("data", "vendor", "rules"));
        IReadOnlyList<string> contents = RulesFuzzRunner.ContentIds("gateway");
        int failures = 0;
        for (int number = firstGame + 1; number <= firstGame + games; number++)
        {
            bool corp = sides == "corp" || (sides == "both" && number % 2 == 1);
            string side = corp ? "corp" : "runner";
            string content = contents[(number - 1) % contents.Count];
            string gameDirectory = Path.GetFullPath(Path.Combine(destination, $"game-{number:D3}"));
            string[] server = [cli, "ai-mcp-game", "--content", content, "--seed", seed.ToString(CultureInfo.InvariantCulture),
                "--number", number.ToString(CultureInfo.InvariantCulture), "--side", side, "--opponent", opponent,
                "--rules-root", rulesRoot, "--output", gameDirectory];
            DateTime started = DateTime.UtcNow;
            string workspace = Path.GetFullPath(Path.Combine(destination, $"game-{number:D3}.workspace"));
            string eventsPath = Path.Combine(destination, $"game-{number:D3}.events.jsonl");
            (string? summary, CodexUsage usage, string? failure) = (null, CodexUsage.None, null);
            // OpenAI sometimes reports the model at capacity mid-game; the game is played again from the start.
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (Directory.Exists(gameDirectory)) Directory.Delete(gameDirectory, recursive: true);
                Directory.CreateDirectory(workspace);
                (summary, usage, failure) = RunCodexGame(codex, model, reasoning, workspace, host, server,
                    CodexPlayPrompt(side), eventsPath, TimeSpan.FromMinutes(timeoutMinutes));
                if (failure is null || !File.ReadAllText(eventsPath).Contains("at capacity", StringComparison.Ordinal)) break;
                error.WriteLine($"ai-codex-play game {number:D3}: the model was at capacity (attempt {attempt}); trying again in a minute.");
                if (attempt < 3) Thread.Sleep(TimeSpan.FromMinutes(1));
            }
            // The server finishes the game with the heuristic once Codex has gone; give it a moment to write.
            string resultPath = Path.Combine(gameDirectory, "result.json");
            for (int wait = 0; wait < 120 && !File.Exists(resultPath); wait++) Thread.Sleep(500);
            JsonObject? result = File.Exists(resultPath) ? JsonNode.Parse(File.ReadAllText(resultPath)) as JsonObject : null;
            JsonArray decisions = result?["decisions"] as JsonArray ?? [];
            int asked = decisions.Count, byCodex = decisions.Count(decision => (string?)decision?["outcome"] == "model");
            int overrode = decisions.Count(decision => (string?)decision?["outcome"] == "model" && (int?)decision?["chosen"] != (int?)decision?["advised"]);
            File.WriteAllText(Path.Combine(destination, $"game-{number:D3}.codex.json"), EngineJson.Serialize(new
            {
                schemaVersion = 1,
                model,
                reasoning,
                usage,
                failure,
                summary,
            }));
            if (failure is not null || result is null || (bool?)result["capped"] == true || result["error"] is JsonValue) failures++;
            output.WriteLine(FormattableString.Invariant(
                $"ai-codex-play game {number:D3} {content}: Codex {side}, winner {(string?)result?["winner"] ?? "unknown"}, {asked} choices, {byCodex} by Codex ({overrode} against the heuristic), {asked - byCodex} by the heuristic, {(int?)result?["invalidChoices"] ?? 0} invalid, {usage.InputTokens} input ({usage.CachedInputTokens} cached) + {usage.OutputTokens} output tokens, {(DateTime.UtcNow - started).TotalMinutes:0.0} min{(failure is null ? "" : $", {failure}")}"));
        }
        output.WriteLine($"ai-codex-play games={games} failures={failures} output={Path.GetFullPath(destination)}");
        return failures == 0 ? 0 : 1;
    }

    private static string CodexPlayPrompt(string side) => $$"""
        You are playing a game of Android: Netrunner (Null Signal Games' comprehensive rules) as the {{side}}, against a
        heuristic bot, through the netrunner-game MCP tools. Play the whole game:
        1. Call get_choice to see your first decision: the board as you see it, the cards in view, and your numbered
           options with the heuristic's scores and reasons.
        2. Call choose(option, reason) with the option you want; it plays it and returns your next decision.
        3. Repeat until a tool reports that the game is over. Then reply with a short summary of how it went.
        You make the turn's actions (installs, runs, advances, plays, scoring, drawing, credits); the heuristic handles
        breaking, paying and priority windows for you. The heuristic's scores are a fair baseline with known mistakes:
        take its choice when it is right and another when you see a better plan. Plan the whole turn, remember what you
        have seen this game, and use card_text and rules_lookup when you are unsure. Don't use the shell.
        The Corp wins with 7 agenda points scored, or when the Runner can't take damage; the Runner wins with 7 agenda
        points stolen, or when R&D is empty when the Corp must draw.
        """;

    /// <summary>Runs one Codex session with the game server; returns its final message, its usage, or why it failed.</summary>
    private static (string? Summary, CodexUsage Usage, string? Failure) RunCodexGame(string codex, string model, string reasoning,
        string workspace, string host, string[] server, string prompt, string eventsPath, TimeSpan timeout)
    {
        string last = Path.Combine(workspace, "last-message.txt");
        static string Toml(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
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
            "--ephemeral", "--json", "-C", workspace, "-o", last,
            "-c", $"mcp_servers.netrunner-game.command={Toml(host)}",
            "-c", $"mcp_servers.netrunner-game.args=[{string.Join(", ", server.Select(Toml))}]",
            "-c", "mcp_servers.netrunner-game.startup_timeout_sec=120",
            "-c", "mcp_servers.netrunner-game.tool_timeout_sec=300",
            "-",
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
}
