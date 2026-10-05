using System.Collections.Immutable;
using System.Globalization;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Netrunner.Match.Puzzles;
using Netrunner.Match.Regret;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// Bot plan S2, the puzzle suite:
    /// ai-puzzles run SET-DIR [--corp V] [--runner V] [--profile P] [--split all|tuning|held-out] [--workers N] [--json]
    /// ai-puzzles import SET-DIR SAVED-GAME-FILE --id ID [--source TEXT]
    /// ai-puzzles show SET-DIR --game ID --sequence N [--teacher SAMPLES] [--horizon game|TURNS] [--json]
    /// ai-puzzles import SET-DIR --suite SUITE-DIR --game ID (a regret suite's game)
    /// ai-puzzles find SUITE-DIR --side corp|runner [--turn-start true] [--hand DEF,...] [--installed DEF,...]
    ///   [--legal KIND[:DEF],...] [--limit N] [--workers N]
    /// </summary>
    private static int RunAiPuzzles(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length < 3) throw new ArgumentException("ai-puzzles needs run, import or show, and a set directory.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("The puzzle suite needs previously provisioned official content embedded at build time.");
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var positional = new List<string>();
            for (int index = 3; index < args.Length; index++)
            {
                if (args[index] == "--json") options["--json"] = "";
                else if (args[index].StartsWith("--", StringComparison.Ordinal))
                    options[args[index]] = index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
                else positional.Add(args[index]);
            }
            return args[1] switch
            {
                "run" => PuzzlesRun(args[2], options, output, error),
                "import" => PuzzlesImport(args[2], positional, options, output),
                "show" => PuzzlesShow(args[2], options, output),
                "find" => PuzzlesFind(args[2], options, output),
                _ => throw new ArgumentException($"Unknown ai-puzzles command '{args[1]}'."),
            };
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            error.WriteLine($"ai-puzzles failed: {exception.Message}");
            return 2;
        }
    }

    private static int PuzzlesRun(string directory, Dictionary<string, string> options, TextWriter output, TextWriter error)
    {
        PuzzleSet set = PuzzleSet.Load(directory);
        string profile = options.GetValueOrDefault("--profile", "standard");
        string corp = options.GetValueOrDefault("--corp", CorpHeuristicPolicy.PolicyVersion);
        string runner = options.GetValueOrDefault("--runner", RunnerHeuristicPolicy.PolicyVersion);
        string split = options.GetValueOrDefault("--split", "all");
        if (split is not ("all" or "tuning" or "held-out")) throw new ArgumentException("--split requires all, tuning or held-out.");
        int workers = options.TryGetValue("--workers", out string? count) ? ProfileInt(count, "--workers", 1, 64) : 1;
        var games = set.Games.ToDictionary(game => game.Id);
        Puzzle[] puzzles = [.. set.Puzzles.Where(puzzle => split == "all" || puzzle.Split == split)];
        var results = new PuzzleResult[puzzles.Length];
        var started = System.Diagnostics.Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.For(0, puzzles.Length, new ParallelOptions { MaxDegreeOfParallelism = workers }, index =>
            results[index] = PuzzleRunner.Run(puzzles[index], games[puzzles[index].GameId],
                (definition, side) => SavedDecision.Agent(definition, side, side == PlayerSide.Corp ? corp : runner, profile, diagnostics: true)));
        var categories = results.GroupBy(result => result.Puzzle.Category).OrderBy(group => Puzzle.Categories.ToList().IndexOf(group.Key))
            .Select(group => new
            {
                category = group.Key,
                passed = group.Count(result => result.Passed),
                total = group.Count(),
                negativeControls = group.Count(result => result.Puzzle.NegativeControl),
            }).ToArray();
        if (options.ContainsKey("--json"))
        {
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                kind = "ai-puzzles",
                corp,
                runner,
                profile,
                split,
                passed = results.Count(result => result.Passed),
                total = results.Length,
                categories,
                results = results.Select(result => new
                {
                    id = result.Puzzle.Id,
                    category = result.Puzzle.Category,
                    kind = result.Puzzle.Kind,
                    split = result.Puzzle.Split,
                    passed = result.Passed,
                    detail = result.Detail,
                    reason = result.Reason,
                }),
            }, RegretSuite.Json));
            return 0;
        }
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-puzzles corp={corp} runner={runner} profile={profile} split={split} passed={results.Count(result => result.Passed)}/{results.Length} {started.Elapsed.TotalSeconds:F0}s"));
        foreach (var category in categories)
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {category.category,-18} {category.passed,3}/{category.total,-3} (negative controls {category.negativeControls})"));
        foreach (PuzzleResult result in results.Where(result => !result.Passed))
            output.WriteLine($"  FAIL {result.Puzzle.Id} [{result.Puzzle.Category}, {result.Puzzle.Kind}] {result.Puzzle.Title}: {result.Detail} ({result.Reason})");
        return 0;
    }

    private static int PuzzlesImport(string directory, List<string> positional, Dictionary<string, string> options, TextWriter output)
    {
        PuzzleSet set = File.Exists(Path.Combine(directory, PuzzleSet.PuzzlesFile)) ? PuzzleSet.Load(directory) : new PuzzleSet([], []);
        if (options.TryGetValue("--suite", out string? suiteDirectory))
        {
            string wanted = options.GetValueOrDefault("--game") ?? throw new ArgumentException("ai-puzzles import --suite needs --game ID.");
            RegretGame copied = RegretSuite.Load(suiteDirectory).Games.SingleOrDefault(game => game.Id == wanted)
                ?? throw new ArgumentException($"The suite has no game '{wanted}'.");
            new PuzzleSet([.. set.Games.Where(existing => existing.Id != wanted).Append(copied)], set.Puzzles).Save(directory);
            output.WriteLine($"ai-puzzles import {wanted}: {copied.ContentId}, {copied.Choices.Length} actions");
            return 0;
        }
        if (positional.Count != 1) throw new ArgumentException("ai-puzzles import needs one saved game file.");
        string id = options.GetValueOrDefault("--id") ?? throw new ArgumentException("ai-puzzles import needs --id ID.");
        (LadderSavedGame saved, FullMatchDefinition definition) = LadderGameArchive.ReadFile(positional[0]);
        RegretGame game = RegretGames.FromSaved(saved, definition, "held-out", options.GetValueOrDefault("--source", "puzzle")) with { Id = id };
        new PuzzleSet([.. set.Games.Where(existing => existing.Id != id).Append(game)], set.Puzzles).Save(directory);
        output.WriteLine($"ai-puzzles import {id}: {saved.Game.ContentId}, {game.Choices.Length} actions");
        return 0;
    }

    /// <summary>An authoring aid: the decisions of a regret suite's games where a situation holds.</summary>
    private static int PuzzlesFind(string suiteDirectory, Dictionary<string, string> options, TextWriter output)
    {
        string[] List(string name) => options.TryGetValue(name, out string? value)
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        PlayerSide side = options.GetValueOrDefault("--side") switch
        {
            "corp" => PlayerSide.Corp,
            "runner" => PlayerSide.Runner,
            _ => throw new ArgumentException("ai-puzzles find needs --side corp or runner."),
        };
        ActionMatch[] legal = [.. List("--legal").Select(entry => entry.Split(':') is var parts && parts.Length == 2
            ? new ActionMatch(Enum.Parse<ActionKind>(parts[0], ignoreCase: true), new System.Text.Json.Nodes.JsonObject { ["cardId"] = "def:" + parts[1] })
            : new ActionMatch(Enum.Parse<ActionKind>(parts[0], ignoreCase: true)))];
        var situation = new PuzzleSituation(side, options.GetValueOrDefault("--turn-start") == "true", List("--hand"), List("--installed"), legal);
        int limit = options.TryGetValue("--limit", out string? count) ? ProfileInt(count, "--limit", 1, 100_000) : 50;
        int workers = options.TryGetValue("--workers", out string? threads) ? ProfileInt(threads, "--workers", 1, 64) : 1;
        RegretGame[] games = [.. RegretSuite.Load(suiteDirectory).Games];
        var found = ForGames(games, workers, TextWriter.Null, "find", game => PuzzleFinder.Find(game, situation)
            .Select(position => (game.Id, position.Sequence, position.State.Turn.TurnNumber,
                position.State.Players.Single(player => player.Side == side).Credits)).Take(3).ToArray());
        foreach (var hit in found.SelectMany(hits => hits).Take(limit))
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{hit.Id} #{hit.Sequence} turn {hit.TurnNumber} credits {hit.Credits}"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ai-puzzles find games-with-hits={found.Count(hits => hits.Length > 0)}/{games.Length}"));
        return 0;
    }

    /// <summary>An authoring aid: a root's position and its distinct choices, with the teacher's values when asked.</summary>
    private static int PuzzlesShow(string directory, Dictionary<string, string> options, TextWriter output)
    {
        PuzzleSet set = PuzzleSet.Load(directory);
        string id = options.GetValueOrDefault("--game") ?? throw new ArgumentException("ai-puzzles show needs --game ID.");
        long sequence = ProfileInt(options.GetValueOrDefault("--sequence") ?? throw new ArgumentException("ai-puzzles show needs --sequence N."),
            "--sequence", 1, int.MaxValue);
        RegretGame game = set.Games.SingleOrDefault(entry => entry.Id == id) ?? throw new ArgumentException($"No game '{id}'.");
        FullMatchDefinition definition = RegretGames.Definition(game);
        RegretPosition position = RegretGames.Positions(game, definition).FirstOrDefault(entry => entry.Sequence == sequence);
        if (position.State is null) throw new ArgumentException($"Game '{id}' has no action {sequence}.");
        GameState state = position.State;
        PendingDecision pending = state.PendingDecision!;
        PlayerSide side = state.GetPlayer(pending.Owner).Side;
        PlayerState player = state.Players.Single(entry => entry.Side == side);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{game.Id} #{sequence}: {side} {pending.Kind} turn {state.Turn.TurnNumber} ({state.Turn.ActiveSide}) credits={player.Credits} clicks={player.Clicks} \"{pending.Prompt}\""));
        ZoneKind hand = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        output.WriteLine("  hand: " + string.Join(" ", state.Cards.Where(card => state.GetZone(card.ZoneId).Kind == hand)
            .Select(card => card.DefinitionId.Value).Order(StringComparer.Ordinal)));
        output.WriteLine("  servers: " + string.Join(" ", state.Servers.Where(server => server.Exists)
            .Select(server => $"{server.Id.Value}={server.Kind}")));
        IReadOnlyList<RegretCandidate> groups = RegretRoots.Group(state, pending);
        IReadOnlyList<TeacherValue>? values = null;
        if (options.TryGetValue("--teacher", out string? samples))
        {
            string horizon = options.GetValueOrDefault("--horizon", "4");
            values = RolloutTeacher.Evaluate(Watched(game, definition, side, sequence),
                ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime),
                [.. groups.Select(group => new ActionId(group.ActionIds[0]))],
                new TeacherSettings(ProfileInt(samples, "--teacher", 1, 4096), 1, horizon == "game" ? 0 : ProfileInt(horizon, "--horizon", 1, 100)));
        }
        if (options.ContainsKey("--json"))
        {
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                game = game.Id,
                sequence,
                side,
                groups = groups.Select((group, index) => new
                {
                    kind = group.Kind,
                    parameters = RegretRoots.Parameters(state, pending.LegalActions.First(action => action.Id.Value == group.ActionIds[0])),
                    mean = values?[index].Mean,
                    standardError = values?[index].StandardError,
                }),
            }, RegretSuite.Json));
            return 0;
        }
        for (int index = 0; index < groups.Count; index++)
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {(values is null ? "" : $"{values[index].Mean:F3} ±{values[index].StandardError:F3} ")}{groups[index].Kind} {groups[index].Description}"));
        return 0;
    }

    /// <summary>The latest Hard bot of a side, having watched a game to (and including) a decision.</summary>
    private static OfflineAgent Watched(RegretGame game, FullMatchDefinition definition, PlayerSide side, long sequence)
    {
        OfflineAgent teacher = RatingLadder.CreateAgent("hard", side, definition) as OfflineAgent
            ?? throw new InvalidOperationException("The Hard bot is not an offline agent.");
        foreach (RegretPosition position in RegretGames.Positions(game, definition).TakeWhile(position => position.Sequence <= sequence))
            AgentLifecycleHost.Observe(teacher, position.State, definition.Behavior);
        return teacher;
    }
}
