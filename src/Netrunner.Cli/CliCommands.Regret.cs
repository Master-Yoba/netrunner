using System.Collections.Immutable;
using System.Globalization;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Netrunner.Match.Regret;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// Bot plan S1, the regret suite:
    /// ai-regret roots GAMES-DIR --output SUITE-DIR [--turn-roots N] [--run-roots N] [--labelled N] [--seed S] [--workers N]
    /// ai-regret label SUITE-DIR [--samples N] [--horizon game|TURNS] [--evaluation N] [--seed S] [--workers N]
    ///   [--labels FILE] [--repeat N --repeat-seed S]
    /// ai-regret score SUITE-DIR --version V[,V...] [--profile P] [--split all|held-out|train] [--labels FILE]
    ///   [--blunders N] [--bootstrap N] [--workers N] [--json]
    /// </summary>
    private static int RunAiRegret(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length < 3) throw new ArgumentException("ai-regret needs roots, label or score, and a directory.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("The regret suite needs previously provisioned official content embedded at build time.");
            var options = RegretOptions.Parse(args);
            return args[1] switch
            {
                "roots" => RegretRootsCommand(args[2], options, output, error),
                "label" => RegretLabelCommand(args[2], options, output, error),
                "score" => RegretScoreCommand(args[2], options, output),
                "findings" => RegretFindingsCommand(args[2], options, output),
                _ => throw new ArgumentException($"Unknown ai-regret command '{args[1]}'."),
            };
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            error.WriteLine($"ai-regret failed: {exception.Message}");
            return 2;
        }
    }

    private sealed record RegretOptions(string? Output, int TurnRoots, int RunRoots, int Labelled, ulong Seed, int Workers, int Samples,
        int HorizonTurns, int Evaluation, string LabelsFile, int Repeat, ulong RepeatSeed, string[] Versions, string Profile, string Split,
        int Blunders, int Bootstrap, bool Json, bool Missing = false, string? Manifest = null, string? Fixtures = null, string? Content = null)
    {
        public static RegretOptions Parse(string[] args)
        {
            var options = new RegretOptions(null, 7, 2, 6, 1, 1, 32, 0, 4, RegretSuite.LabelsFile, 0, 2, [], "standard", "all", 0, 200, false);
            for (int index = 3; index < args.Length; index++)
            {
                string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
                int Int(string name, int minimum, int maximum) => ProfileInt(Next(), name, minimum, maximum);
                options = args[index] switch
                {
                    "--output" => options with { Output = Next() },
                    "--turn-roots" => options with { TurnRoots = Int("--turn-roots", 0, 1000) },
                    "--run-roots" => options with { RunRoots = Int("--run-roots", 0, 1000) },
                    "--labelled" => options with { Labelled = Int("--labelled", 2, 64) },
                    "--seed" => options with { Seed = ulong.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture) },
                    "--workers" => options with { Workers = Int("--workers", 1, 64) },
                    "--samples" => options with { Samples = Int("--samples", 1, 4096) },
                    "--horizon" => options with { HorizonTurns = Next() is "game" ? 0 : ProfileInt(args[index], "--horizon", 1, 1000) },
                    "--evaluation" => options with { Evaluation = Int("--evaluation", 1, 4) },
                    "--labels" => options with { LabelsFile = Next() },
                    "--repeat" => options with { Repeat = Int("--repeat", 1, 100_000) },
                    "--repeat-seed" => options with { RepeatSeed = ulong.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture) },
                    "--version" => options with { Versions = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
                    "--profile" => options with { Profile = Next() },
                    "--split" => options with
                    {
                        Split = Next() is var split && split is "all" or "held-out" or "train" ? split
                        : throw new ArgumentException("--split requires all, held-out or train.")
                    },
                    "--blunders" => options with { Blunders = Int("--blunders", 0, 10_000) },
                    "--bootstrap" => options with { Bootstrap = Int("--bootstrap", 0, 100_000) },
                    "--json" => options with { Json = true },
                    "--missing" => options with { Missing = true },
                    "--manifest" => options with { Manifest = Next() },
                    "--fixtures" => options with { Fixtures = Next() },
                    "--content" => options with { Content = Next() },
                    _ => throw new ArgumentException($"Unsupported ai-regret argument '{args[index]}'."),
                };
            }
            return options;
        }
    }

    private static int RegretRootsCommand(string gamesDirectory, RegretOptions options, TextWriter output, TextWriter error)
    {
        string destination = options.Output ?? throw new ArgumentException("ai-regret roots needs --output SUITE-DIR.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("--output must name a new directory.");
        if (!Directory.Exists(gamesDirectory)) throw new ArgumentException($"No saved games in '{gamesDirectory}'.");
        IReadOnlyList<(LadderSavedGame Game, FullMatchDefinition Definition)> saved = LadderGameArchive.ReadAll(gamesDirectory, options.Workers);
        // One game in five is held out, by game, in the order of the games' ids.
        RegretGame[] games = [.. saved.OrderBy(entry => entry.Game.Game.Id, StringComparer.Ordinal).AsParallel().AsOrdered()
            .WithDegreeOfParallelism(options.Workers)
            .Select((entry, index) => RegretGames.FromSaved(entry.Game, entry.Definition, index % 5 == 0 ? "held-out" : "train", "ladder"))];
        var sampling = new RegretSampling(options.TurnRoots, options.RunRoots, options.Labelled, Seed: options.Seed);
        RegretRoot[] roots = [.. ForGames(games, options.Workers, error, "roots", game => RegretRoots.Sample(game, sampling)).SelectMany(found => found)];
        new RegretSuite([.. games], [.. roots], []).Save(destination);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret roots games={games.Length} roots={roots.Length} turn={roots.Count(root => root.Kind == RegretRoots.TurnKind)} run={roots.Count(root => root.Kind == RegretRoots.RunKind)} held-out={roots.Count(root => root.Split == "held-out")} output={destination}"));
        return 0;
    }

    private static int RegretLabelCommand(string suiteDirectory, RegretOptions options, TextWriter output, TextWriter error)
    {
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        var started = System.Diagnostics.Stopwatch.StartNew();
        if (options.Repeat > 0)
        {
            ImmutableArray<RegretLabel> first = RegretSuite.ReadLines<RegretLabel>(Path.Combine(suiteDirectory, options.LabelsFile));
            var random = new DeterministicRandom(options.RepeatSeed);
            HashSet<string> chosen = [.. first.Select(label => label.RootId).OrderBy(_ => random.NextUInt64()).Take(options.Repeat)];
            ImmutableArray<RegretLabel> again = Label(suite, chosen, options with { Seed = options.RepeatSeed }, error);
            string repeated = Path.Combine(suiteDirectory, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(options.LabelsFile)) + "-repeat.jsonl.gz");
            RegretSuite.WriteLines(repeated, again.OrderBy(label => label.RootId, StringComparer.Ordinal));
            RegretNoise noise = RegretReport.Noise(first, again);
            File.WriteAllText(Path.ChangeExtension(Path.ChangeExtension(repeated, null), ".noise.json"),
                System.Text.Json.JsonSerializer.Serialize(noise, RegretSuite.Json) + "\n");
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"ai-regret noise roots={noise.Roots} best-disagreement={noise.BestDisagreement:F3} median-se={noise.MedianStandardError:F3} seconds={started.Elapsed.TotalSeconds:F0}"));
            return 0;
        }
        string labelsPath = Path.Combine(suiteDirectory, options.LabelsFile);
        // --missing labels only the roots the file has no label for, and keeps the rest.
        ImmutableArray<RegretLabel> kept = options.Missing && File.Exists(labelsPath) ? RegretSuite.ReadLines<RegretLabel>(labelsPath) : [];
        HashSet<string> labelled = [.. kept.Select(label => label.RootId)];
        ImmutableArray<RegretLabel> labels = Label(suite, [.. suite.Roots.Select(root => root.Id).Where(id => !labelled.Contains(id))], options, error);
        RegretSuite.WriteLines(labelsPath, kept.Concat(labels).OrderBy(label => label.RootId, StringComparer.Ordinal));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret label roots={labels.Length} samples={options.Samples} horizon={(options.HorizonTurns == 0 ? "game" : options.HorizonTurns.ToString(CultureInfo.InvariantCulture))} seconds={started.Elapsed.TotalSeconds:F0} file={options.LabelsFile}"));
        return 0;
    }

    private static ImmutableArray<RegretLabel> Label(RegretSuite suite, HashSet<string> rootIds, RegretOptions options, TextWriter error)
    {
        var settings = new TeacherSettings(options.Samples, options.Seed, options.HorizonTurns, options.Evaluation);
        var byGame = suite.Roots.Where(root => rootIds.Contains(root.Id)).GroupBy(root => root.GameId).ToDictionary(group => group.Key, group => group.ToList());
        RegretGame[] games = [.. suite.Games.Where(game => byGame.ContainsKey(game.Id))];
        return [.. ForGames(games, options.Workers, error, "label", game => RegretLabels.Label(game, byGame[game.Id], settings)).SelectMany(found => found)];
    }

    private static int RegretScoreCommand(string suiteDirectory, RegretOptions options, TextWriter output)
    {
        if (options.Versions.Length == 0) throw new ArgumentException("ai-regret score needs --version V[,V...].");
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        var labels = RegretSuite.ReadLines<RegretLabel>(Path.Combine(suiteDirectory, options.LabelsFile)).ToDictionary(label => label.RootId);
        RegretRoot[] roots = [.. suite.Roots.Where(root => options.Split == "all" || root.Split == options.Split)];
        var byGame = roots.GroupBy(root => root.GameId).ToDictionary(group => group.Key, group => group.ToList());
        var results = new List<(string Version, PlayerSide Side, ImmutableArray<RegretChoice> Choices, TimeSpan Elapsed)>();
        foreach (string version in options.Versions)
        {
            PlayerSide side = OfflinePolicies.IsSupported(PlayerSide.Corp, version) ? PlayerSide.Corp
                : OfflinePolicies.IsSupported(PlayerSide.Runner, version) ? PlayerSide.Runner
                : throw new ArgumentException($"Unknown policy version '{version}'.");
            var started = System.Diagnostics.Stopwatch.StartNew();
            RegretGame[] games = [.. suite.Games.Where(game => byGame.TryGetValue(game.Id, out var list) && list.Any(root => root.Side == side))];
            ImmutableArray<RegretChoice> choices = [.. ForGames(games, options.Workers, TextWriter.Null, "score",
                    game => RegretScores.Choose(game, byGame[game.Id], labels, (definition, seat) => seat == side ? Scored(definition, seat, options.Profile, version) : null))
                .SelectMany(found => found).OrderBy(choice => choice.RootId, StringComparer.Ordinal)];
            results.Add((version, side, choices, started.Elapsed));
        }
        RegretComparison[] comparisons = [.. results.SelectMany((first, index) => results.Skip(index + 1).Where(second => second.Side == first.Side)
            .Select(second => RegretReport.Compare(first.Version, first.Choices, second.Version, second.Choices, options.Bootstrap, options.Seed)))];
        var report = results.Select(result => new
        {
            version = result.Version,
            side = result.Side,
            profile = options.Profile,
            seconds = Math.Round(result.Elapsed.TotalSeconds, 1),
            unlabelled = result.Choices.Count(choice => choice.Regret is null),
            summaries = new[] { RegretReport.Summarize("all", result.Choices, options.Bootstrap, options.Seed) }
                .Concat(result.Choices.GroupBy(choice => choice.Kind).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => RegretReport.Summarize("kind:" + group.Key, [.. group], options.Bootstrap, options.Seed)))
                .Concat(result.Choices.GroupBy(choice => choice.Split).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => RegretReport.Summarize("split:" + group.Key, [.. group], options.Bootstrap, options.Seed)))
                .ToArray(),
            blunders = result.Choices.Where(choice => choice.Regret is not null).OrderByDescending(choice => choice.Regret)
                .ThenBy(choice => choice.RootId, StringComparer.Ordinal).Take(options.Blunders)
                .Select(choice => new { choice, values = labels[choice.RootId].Values }).ToArray(),
        }).ToArray();
        if (options.Json)
        {
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = 1, kind = "ai-regret-score", split = options.Split, report, comparisons },
                RegretSuite.Json));
            return 0;
        }
        foreach (var entry in report)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{entry.version} ({entry.side}, {entry.profile}) {entry.seconds}s unlabelled={entry.unlabelled}"));
            foreach (RegretSummary summary in entry.summaries)
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {summary.Slice,-16} roots={summary.Roots,5} labelled={summary.Labelled,5} regret={summary.MeanRegret:F4} [{summary.Lower:F4}, {summary.Upper:F4}] agree={summary.Agreement:F3} within-noise={summary.AgreementWithinNoise:F3}"));
            foreach (var blunder in entry.blunders)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  blunder {blunder.choice.RootId} regret={blunder.choice.Regret:F3} chose \"{blunder.choice.ChosenDescription}\" ({blunder.choice.Reason})"));
                foreach (RegretValue value in blunder.values.OrderByDescending(value => value.Mean))
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"      {value.Mean:F3} ±{value.StandardError:F3} {value.Group}"));
            }
        }
        foreach (RegretComparison comparison in comparisons)
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"compare {comparison.First} − {comparison.Second}: roots={comparison.Roots} regret difference={comparison.MeanDifference:F4} [{comparison.Lower:F4}, {comparison.Upper:F4}]{(comparison.Separated ? " separated" : "")}"));
        return 0;
    }

    /// <summary>
    /// Adds the findings manifest's saved decisions to a suite as held-out roots, replacing any it added before. A finding
    /// whose game is not of the suite's pool (<c>--content</c>) is skipped and reported.
    /// </summary>
    private static int RegretFindingsCommand(string suiteDirectory, RegretOptions options, TextWriter output)
    {
        string manifestPath = options.Manifest ?? throw new ArgumentException("ai-regret findings needs --manifest FILE.");
        string fixtures = options.Fixtures ?? throw new ArgumentException("ai-regret findings needs --fixtures DIR.");
        HashSet<string> pool = [.. RulesFuzzRunner.ContentIds(options.Content ?? throw new ArgumentException("ai-regret findings needs --content POOL."))];
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        using System.Text.Json.JsonDocument manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        var findings = manifest.RootElement.GetProperty("findings").EnumerateArray().Select(entry => (
            Fixture: entry.GetProperty("fixture").GetString()!, Sequence: entry.GetProperty("sequence").GetInt64(),
            Side: entry.GetProperty("side").GetString()!, Test: entry.GetProperty("test").GetString()!)).ToList();
        var games = new List<RegretGame>();
        var roots = new List<RegretRoot>();
        var sampling = new RegretSampling(Labelled: options.Labelled, Seed: options.Seed);
        foreach (var fixture in findings.GroupBy(finding => finding.Fixture).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            (LadderSavedGame saved, FullMatchDefinition definition) = LadderGameArchive.Read(Path.Combine(fixtures, fixture.Key)).Single();
            if (!pool.Contains(saved.Game.ContentId))
            {
                output.WriteLine($"ai-regret findings skipped {fixture.Key}: {saved.Game.ContentId} is not of the pool");
                continue;
            }
            RegretGame game = RegretGames.FromSaved(saved, definition, "held-out", "finding") with { Id = "finding-" + fixture.Key };
            games.Add(game);
            foreach (var finding in fixture)
            {
                RegretRoot? root = RegretRoots.At(game, new HashSet<long> { finding.Sequence }, sampling, ["finding", finding.Test]).SingleOrDefault();
                if (root is null || !string.Equals(root.Side.ToString(), finding.Side, StringComparison.OrdinalIgnoreCase))
                    output.WriteLine($"ai-regret findings skipped {fixture.Key}#{finding.Sequence}: {(root is null ? "fewer than two distinct choices" : "not the " + finding.Side + "'s decision")}");
                else roots.Add(root);
            }
        }
        HashSet<string> added = [.. games.Select(game => game.Id)];
        new RegretSuite([.. suite.Games.Where(game => !added.Contains(game.Id)).Concat(games)],
            [.. suite.Roots.Where(root => !added.Contains(root.GameId)).Concat(roots)], []).Save(suiteDirectory);
        output.WriteLine($"ai-regret findings games={games.Count} roots={roots.Count} of {findings.Count}");
        return 0;
    }

    /// <summary>The scored version's bot for a seat, keeping its reasons.</summary>
    private static OfflineAgent Scored(FullMatchDefinition definition, PlayerSide side, string profile, string version)
    {
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp, profile, version);
        var scored = new OfflineAgent(agent.Configuration, OfflinePolicies.Create(agent.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), null, captureDiagnostics: true);
        if (agent.Checkpoint.Knowledge.OpponentList is { } list) scored.KnowOpponentList(list.Select(entry => (entry.DefinitionId, entry.Quantity)));
        return scored;
    }

    /// <summary>Runs <paramref name="work"/> over the games on <paramref name="workers"/> threads, in the games' order, reporting progress.</summary>
    private static T[] ForGames<T>(RegretGame[] games, int workers, TextWriter error, string what, Func<RegretGame, T> work)
    {
        var results = new T[games.Length];
        int done = 0;
        var started = System.Diagnostics.Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.For(0, games.Length, new ParallelOptions { MaxDegreeOfParallelism = workers }, index =>
        {
            results[index] = work(games[index]);
            int finished = Interlocked.Increment(ref done);
            if (finished % 50 == 0 || finished == games.Length)
                lock (error) error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ai-regret {what} {finished}/{games.Length} {started.Elapsed.TotalSeconds:F0}s"));
        });
        return results;
    }
}
