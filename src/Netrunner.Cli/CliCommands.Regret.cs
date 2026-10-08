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
    /// ai-regret label SUITE-DIR [--samples N] [--horizon game|TURNS] [--evaluation N] [--lines W] [--seed S] [--workers N]
    ///   [--labels FILE] [--repeat N --repeat-seed S] [--roots FILE] (with --repeat, the roots to repeat)
    ///   [--decide N --record N --extend-to N] (adaptive samples, S1.7: N deciding samples, discarded, choose how many to record)
    ///   [--decide N --record N --prune K] (pruning, S1.6: only the best K groups, and a targeted root's chosen ones, are recorded)
    ///   [--end-lines FILE [--end-lines-repeat ROOTS-FILE]] (S4.2b/c: a row where the root side's turn ends on every line played
    ///   to the horizon, with its sample and line; at the listed roots, a second play-out of each for the ceiling)
    ///   [--teacher hard-model|hard] (hard-model, the default, models the opponent by the live Standard version; hard by
    ///   the planner's pinned Runner v7 and Corp v8, as labels-v1 was made)
    /// ai-regret score SUITE-DIR --version V[,V...]|latest [--profile P] [--split all|held-out|train] [--labels FILE]
    ///   [--blunders N] [--bootstrap N] [--workers N] [--json] [--discordant A,B] [--label-missing]
    /// ai-regret findings SUITE-DIR --manifest FILE --fixtures DIR --content POOL [--labelled N]
    /// ai-regret dedupe SUITE-DIR
    /// ai-regret extend SUITE-DIR [--labelled N] [--workers N] (every meaningful group at every root, S1.4)
    /// ai-regret diverge SUITE-DIR|GAMES-DIR --versions A,B [--side S] [--games N] --output SUITE-DIR [--profile P] [--workers N] (S1.6: the
    ///   decisions where two versions choose differently, as roots of a new suite)
    /// ai-regret merge SUITE-DIR --from SUITE-DIR [--labels FILE] (folds a suite's games, roots and labels into another)
    /// ai-regret leaf-cost SUITE-DIR [--turn-roots GAMES] (S4.1: the leaf's cost per extraction, set 4 against set 5, one thread)
    /// ai-regret tag SUITE-DIR [--labels FILE] (writes each label's teacher version into it, S1.7)
    /// ai-regret core SUITE-DIR [--roots N] [--seed S] (marks a core of about N roots by deal, S1.7; score --core reads it)
    /// ai-regret exposure SUITE-DIR|GAMES-DIR [--all-roots] [--version RUNNER] [--workers N] [--json] (every agenda, or every card, the Corp installed in
    ///   a remote in the suite's games; with --version, that Runner says at its next turn how it scores the run)
    /// ai-regret exposure SUITE-DIR --roots FILE [--samples N] [--horizon TURNS] [--workers N] [--json] (the teacher's
    ///   play-outs of the groups FILE lists, "ROOT-ID GROUP" a line)
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
                "dedupe" => RegretDedupeCommand(args[2], options, output),
                "extend" => RegretExtendCommand(args[2], options, output, error),
                "exposure" => RegretExposureCommand(args[2], options, output, error),
                "core" => RegretCoreCommand(args[2], options, output),
                "tag" => RegretTagCommand(args[2], options, output),
                "leaf-cost" => RegretLeafCostCommand(args[2], options, output),
                "diverge" => RegretDivergeCommand(args[2], options, output, error),
                "merge" => RegretMergeCommand(args[2], options, output),
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
        int Blunders, int Bootstrap, bool Json, bool Missing = false, string? Manifest = null, string? Fixtures = null, string? Content = null,
        bool LabelMissing = false, bool ExtendPruned = false, int Lines = 0, string? RootsFile = null, int ScreenTurns = 1, string[]? Discordant = null,
        bool AllRoots = false, string TeacherEntrant = RegretLabels.DefaultTeacher, bool Core = false,
        int CoreRoots = 2000, int Decide = 0, int ExtendTo = 0, string[]? Pair = null, string? From = null, int Prune = 0,
        PlayerSide? Side = null, int Games = 0, string? EndLines = null, string? RepeatRoots = null)
    {
        public static RegretOptions Parse(string[] args)
        {
            var options = new RegretOptions(null, 7, 2, 0, 1, 1, 32, 0, 4, RegretSuite.LabelsFile, 0, 2, [], "standard", "all", 0, 200, false);
            for (int index = 3; index < args.Length; index++)
            {
                string Next() => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"{args[index]} needs a value.");
                int Int(string name, int minimum, int maximum) => ProfileInt(Next(), name, minimum, maximum);
                options = args[index] switch
                {
                    "--output" => options with { Output = Next() },
                    "--turn-roots" => options with { TurnRoots = Int("--turn-roots", 0, 1000) },
                    "--run-roots" => options with { RunRoots = Int("--run-roots", 0, 1000) },
                    "--labelled" => options with { Labelled = Int("--labelled", 0, 64) },
                    "--seed" => options with { Seed = ulong.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture) },
                    "--workers" => options with { Workers = Int("--workers", 1, 64) },
                    "--samples" => options with { Samples = Int("--samples", 1, 4096) },
                    "--horizon" => options with { HorizonTurns = Next() is "game" ? 0 : ProfileInt(args[index], "--horizon", 1, 1000) },
                    "--evaluation" => options with { Evaluation = Int("--evaluation", 1, 4) },
                    "--labels" => options with { LabelsFile = Next() },
                    "--repeat" => options with { Repeat = Int("--repeat", 1, 100_000) },
                    "--repeat-seed" => options with { RepeatSeed = ulong.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture) },
                    // "latest" is both sides' latest versions.
                    "--version" => options with
                    {
                        Versions = [.. Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .SelectMany(version => version == "latest"
                                ? new[] { CorpHeuristicPolicy.PolicyVersion, RunnerHeuristicPolicy.PolicyVersion } : [version])],
                    },
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
                    "--label-missing" => options with { LabelMissing = true },
                    "--extend-pruned" => options with { ExtendPruned = true },
                    "--all-roots" => options with { AllRoots = true },
                    "--core" => options with { Core = true },
                    "--versions" => options with
                    {
                        Pair = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: 2 } versions
                            ? versions : throw new ArgumentException("--versions requires two versions, A,B."),
                    },
                    "--from" => options with { From = Next() },
                    "--side" => options with
                    {
                        Side = Next() switch { "corp" => PlayerSide.Corp, "runner" => PlayerSide.Runner, _ => throw new ArgumentException("--side requires corp or runner.") },
                    },
                    // S1.7, adaptive samples: --decide 8 --record 8 --extend-to 32.
                    "--decide" => options with { Decide = Int("--decide", 1, 4096) },
                    "--record" => options with { Samples = Int("--record", 1, 4096) },
                    "--extend-to" => options with { ExtendTo = Int("--extend-to", 1, 4096) },
                    // S1.6, pruning: --decide 8 --record 24 --prune 3.
                    "--prune" => options with { Prune = Int("--prune", 1, 64) },
                    "--games" => options with { Games = Int("--games", 1, 1_000_000) },
                    "--end-lines" => options with { EndLines = Next() },
                    "--end-lines-repeat" => options with { RepeatRoots = Next() },
                    "--teacher" => options with
                    {
                        TeacherEntrant = Next() is var teacher && teacher is "hard" or "hard-model" ? teacher
                            : throw new ArgumentException("--teacher requires hard or hard-model."),
                    },
                    "--lines" => options with { Lines = Int("--lines", 0, 16) },
                    "--screen-turns" => options with { ScreenTurns = Int("--screen-turns", 0, 100) },
                    // Two versions of one side; they are scored if --version does not name them.
                    "--discordant" => options with
                    {
                        Discordant = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: 2 } pair
                            ? pair : throw new ArgumentException("--discordant requires two versions, A,B."),
                    },
                    // ai-regret core takes a count of roots; label and exposure a file of them.
                    "--roots" => args[1] == "core" ? options with { CoreRoots = Int("--roots", 1, 1_000_000) } : options with { RootsFile = Next() },
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
        // The split is set by deal once the roots are drawn (RegretSuite.Deduplicated).
        RegretGame[] games = [.. saved.OrderBy(entry => entry.Game.Game.Id, StringComparer.Ordinal).AsParallel().AsOrdered()
            .WithDegreeOfParallelism(options.Workers)
            .Select(entry => RegretGames.FromSaved(entry.Game, entry.Definition, "train", "ladder"))];
        var sampling = new RegretSampling(options.TurnRoots, options.RunRoots, options.Labelled, Seed: options.Seed);
        RegretRoot[] roots = [.. ForGames(games, options.Workers, error, "roots", game => RegretRoots.Sample(game, sampling)).SelectMany(found => found)];
        RegretSuite suite = new RegretSuite([.. games], [.. roots], []).Deduplicated();
        suite.Save(destination);
        roots = [.. suite.Roots];
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
            // --roots FILE repeats the roots it lists (an earlier repeat's, to measure a new teacher's floor on the same roots).
            HashSet<string> chosen = options.RootsFile is { } repeatRoots
                ? [.. File.ReadLines(repeatRoots).Select(line => line.Trim()).Where(line => line.Length > 0)
                    .Intersect(first.Select(label => label.RootId)).Take(options.Repeat)]
                : [.. first.Select(label => label.RootId).OrderBy(_ => random.NextUInt64()).Take(options.Repeat)];
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
        // S1.7: a label kept from before versions were written is tagged with the version its settings are.
        ImmutableArray<RegretLabel> kept = options.Missing && File.Exists(labelsPath)
            ? [.. RegretSuite.ReadLines<RegretLabel>(labelsPath).Select(label => label with { TeacherVersion = label.TeacherName })] : [];
        HashSet<string> labelled = [.. kept.Select(label => label.RootId)];
        // --roots FILE labels only the roots it lists, one id a line.
        HashSet<string>? listed = options.RootsFile is { } file
            ? [.. File.ReadLines(file).Select(line => line.Trim()).Where(line => line.Length > 0)] : null;
        HashSet<string> wanted = [.. suite.Roots.Select(root => root.Id).Where(id => !labelled.Contains(id) && (listed is null || listed.Contains(id)))];
        // S4.2b, --end-lines FILE: the rows of every line played to the horizon, kept with their roots' labels.
        string? endLinesPath = options.EndLines is { } endLinesFile ? Path.Combine(suiteDirectory, endLinesFile) : null;
        EndLineRow[] keptRows = endLinesPath is not null && options.Missing && File.Exists(endLinesPath)
            ? [.. RegretSuite.ReadLines<EndLineRow>(endLinesPath).Where(row => labelled.Contains(row.RootId))] : [];
        void Save(IEnumerable<(ImmutableArray<RegretLabel> Labels, List<EndLineRow> Rows)> finished)
        {
            var done = finished.ToArray();
            RegretSuite.WriteLines(labelsPath, kept.Concat(done.SelectMany(game => game.Labels)).OrderBy(label => label.RootId, StringComparer.Ordinal));
            if (endLinesPath is not null)
                RegretSuite.WriteLines(endLinesPath, keptRows.Concat(done.OrderBy(game => game.Labels.FirstOrDefault()?.RootId, StringComparer.Ordinal)
                    .SelectMany(game => game.Rows)));
        }
        // The files are rewritten at every progress line, so a stopped run resumes with --missing.
        var found = LabelWithRows(suite, wanted, options, error, Save, endLinesPath is not null);
        Save(found);
        ImmutableArray<RegretLabel> labels = [.. found.SelectMany(game => game.Labels)];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret label roots={labels.Length} unlabellable={wanted.Count - labels.Length} samples={options.Samples} horizon={(options.HorizonTurns == 0 ? "game" : options.HorizonTurns.ToString(CultureInfo.InvariantCulture))} lines={options.Lines} seconds={started.Elapsed.TotalSeconds:F0} file={options.LabelsFile}"));
        return 0;
    }

    private static (ImmutableArray<RegretLabel> Labels, List<EndLineRow> Rows)[] LabelWithRows(RegretSuite suite, HashSet<string> rootIds,
        RegretOptions options, TextWriter error, Action<IEnumerable<(ImmutableArray<RegretLabel> Labels, List<EndLineRow> Rows)>> save, bool rows)
    {
        var settings = new TeacherSettings(options.Samples, options.Seed, options.HorizonTurns, options.Evaluation, Lines: options.Lines,
            ScreenTurns: options.ScreenTurns, Decide: options.Decide, ExtendTo: options.ExtendTo, Prune: options.Prune);
        var byGame = suite.Roots.Where(root => rootIds.Contains(root.Id)).GroupBy(root => root.GameId).ToDictionary(group => group.Key, group => group.ToList());
        RegretGame[] games = [.. suite.Games.Where(game => byGame.ContainsKey(game.Id))];
        // S4.2c, --end-lines-repeat FILE: the roots (one id a line) whose rows also get the ceiling's second play-out.
        HashSet<string>? repeat = options.RepeatRoots is { } file
            ? [.. File.ReadLines(file).Select(line => line.Trim()).Where(line => line.Length > 0)] : null;
        return ForGames(games, options.Workers, error, "label", game =>
        {
            var found = new List<EndLineRow>();
            return (RegretLabels.Label(game, byGame[game.Id], settings, options.TeacherEntrant, rows ? found : null, repeat), found);
        }, finished => save(finished));
    }

    private static ImmutableArray<RegretLabel> Label(RegretSuite suite, HashSet<string> rootIds, RegretOptions options, TextWriter error,
        Action<IEnumerable<RegretLabel>>? save = null)
    {
        var settings = new TeacherSettings(options.Samples, options.Seed, options.HorizonTurns, options.Evaluation, Lines: options.Lines,
            ScreenTurns: options.ScreenTurns, Decide: options.Decide, ExtendTo: options.ExtendTo, Prune: options.Prune);
        var byGame = suite.Roots.Where(root => rootIds.Contains(root.Id)).GroupBy(root => root.GameId).ToDictionary(group => group.Key, group => group.ToList());
        RegretGame[] games = [.. suite.Games.Where(game => byGame.ContainsKey(game.Id))];
        return [.. ForGames(games, options.Workers, error, "label", game => RegretLabels.Label(game, byGame[game.Id], settings, options.TeacherEntrant),
            save is null ? null : finished => save(finished.SelectMany(found => found))).SelectMany(found => found)];
    }

    private static int RegretScoreCommand(string suiteDirectory, RegretOptions options, TextWriter output)
    {
        if (options.Discordant is { } pair) options = options with { Versions = [.. options.Versions.Union(pair, StringComparer.Ordinal)] };
        if (options.Versions.Length == 0) throw new ArgumentException("ai-regret score needs --version V[,V...].");
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        var labels = RegretSuite.ReadLines<RegretLabel>(Path.Combine(suiteDirectory, options.LabelsFile)).ToDictionary(label => label.RootId);
        RegretRoot[] roots = [.. suite.Roots.Where(root => (options.Split == "all" || root.Split == options.Split)
            && (!options.Core || root.Categories.Contains(RegretSuite.CoreCategory)))];
        // S1.7: a paired comparison is valid root by root under one teacher, so a root whose values come from two
        // teachers (a group labelled on demand by another) is refused, not compared.
        foreach (RegretRoot root in roots)
            if (labels.TryGetValue(root.Id, out RegretLabel? rooted)) rooted.EnsureOneTeacher();
        string teacher = string.Join(", ", roots.Where(root => labels.ContainsKey(root.Id)).GroupBy(root => labels[root.Id].TeacherName)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"teacher {group.Key} ({group.Count()} roots)")));
        var rootsById = roots.ToDictionary(root => root.Id);
        var byGame = roots.GroupBy(root => root.GameId).ToDictionary(group => group.Key, group => group.ToList());
        var results = new List<(string Version, PlayerSide Side, ImmutableArray<RegretChoice> Choices, TimeSpan Elapsed)>();
        foreach (string version in options.Versions)
        {
            // S4.2: a ladder entrant (hard-set5) is scored on the side --side names; a version on its own side.
            PlayerSide side = Entrant(version, options) is { } seat ? seat
                : OfflinePolicies.IsSupported(PlayerSide.Corp, version) ? PlayerSide.Corp
                : OfflinePolicies.IsSupported(PlayerSide.Runner, version) ? PlayerSide.Runner
                : throw new ArgumentException($"Unknown policy version '{version}'.");
            var started = System.Diagnostics.Stopwatch.StartNew();
            RegretGame[] games = [.. suite.Games.Where(game => byGame.TryGetValue(game.Id, out var list) && list.Any(root => root.Side == side))];
            ImmutableArray<RegretChoice> choices = [.. ForGames(games, options.Workers, TextWriter.Null, "score",
                    game => RegretScores.Choose(game, byGame[game.Id], labels, (definition, seat) => seat == side ? Scored(definition, seat, options.Profile, version, options) : null))
                .SelectMany(found => found).OrderBy(choice => choice.RootId, StringComparer.Ordinal)];
            results.Add((version, side, choices, started.Elapsed));
        }
        if (options.LabelMissing && LabelMissing(suiteDirectory, suite, labels, results.SelectMany(result => result.Choices), options, output) > 0)
            return RegretScoreCommand(suiteDirectory, options with { LabelMissing = false }, output);
        if (options.ExtendPruned && ExtendPruned(suiteDirectory, suite, labels, results.SelectMany(result => result.Choices), options, output) > 0)
            return RegretScoreCommand(suiteDirectory, options with { ExtendPruned = false }, output);
        RegretComparison[] comparisons = [.. results.SelectMany((first, index) => results.Skip(index + 1).Where(second => second.Side == first.Side)
            .Select(second => RegretReport.Compare(first.Version, first.Choices, second.Version, second.Choices, options.Bootstrap, options.Seed)))];
        var report = results.Select(result => new
        {
            version = result.Version,
            side = result.Side,
            profile = options.Profile,
            seconds = Math.Round(result.Elapsed.TotalSeconds, 1),
            unlabelled = result.Choices.Count(choice => choice.Regret is null),
            pruned = result.Choices.Count(choice => choice.Pruned),
            summaries = new[] { RegretReport.Summarize("all", result.Choices, options.Bootstrap, options.Seed) }
                .Concat(result.Choices.GroupBy(choice => choice.Kind).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => RegretReport.Summarize("kind:" + group.Key, [.. group], options.Bootstrap, options.Seed)))
                .Concat(result.Choices.GroupBy(choice => choice.Split).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => RegretReport.Summarize("split:" + group.Key, [.. group], options.Bootstrap, options.Seed)))
                .ToArray(),
            blunders = result.Choices.Where(choice => choice.Regret is not null).OrderByDescending(choice => choice.Regret)
                .ThenBy(choice => choice.RootId, StringComparer.Ordinal).Take(options.Blunders)
                .Select(choice => new
                {
                    choice,
                    values = labels[choice.RootId].Values.Select(value => new
                    {
                        value,
                        description = rootsById[choice.RootId].Candidates.First(candidate => candidate.Group == value.Group).Description,
                    }).ToArray(),
                }).ToArray(),
        }).ToArray();
        RegretDiscordance? discordance = null;
        if (options.Discordant is [string first, string second])
        {
            var a = results.Single(result => result.Version == first);
            var b = results.Single(result => result.Version == second);
            if (a.Side != b.Side) throw new ArgumentException("--discordant needs two versions of one side.");
            discordance = RegretReport.Discordance(first, a.Choices, second, b.Choices, rootsById, labels, options.Bootstrap, options.Seed);
        }
        if (options.Json)
        {
            // Regret is comparable only within one labelling, so the report names its labels and their teacher.
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                kind = "ai-regret-score",
                split = options.Split,
                labels = options.LabelsFile,
                teacher,
                report,
                comparisons,
                discordance
            }, RegretSuite.Json));
            return 0;
        }
        foreach (var entry in report)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{entry.version} ({entry.side}, {entry.profile}) {entry.seconds}s unlabelled={entry.unlabelled} pruned-choices={entry.pruned} labels={options.LabelsFile} {teacher}{(options.Core ? " core" : "")}"));
            foreach (RegretSummary summary in entry.summaries)
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {summary.Slice,-16} roots={summary.Roots,5} labelled={summary.Labelled,5} regret={summary.MeanRegret:F4} [{summary.Lower:F4}, {summary.Upper:F4}] agree={summary.Agreement:F3} within-noise={summary.AgreementWithinNoise:F3}"));
            foreach (var blunder in entry.blunders)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  blunder {blunder.choice.RootId} regret={blunder.choice.Regret:F3} chose \"{blunder.choice.ChosenDescription}\" ({blunder.choice.Reason})"));
                foreach (var line in blunder.values.OrderByDescending(line => line.value.Mean))
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"      {line.value.Mean:F3} ±{line.value.StandardError:F3} {line.description[..Math.Min(line.description.Length, 110)]}"));
            }
        }
        foreach (RegretComparison comparison in comparisons)
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"compare {comparison.First} − {comparison.Second}: roots={comparison.Roots} regret difference={comparison.MeanDifference:F4} [{comparison.Lower:F4}, {comparison.Upper:F4}]{(comparison.Separated ? " separated" : "")} ({teacher})"));
        if (discordance is not null) WriteDiscordance(discordance, $"{options.LabelsFile}, {teacher}", output);
        return 0;
    }

    /// <summary>
    /// The S1.5 follow-up: does the bot Runner punish an agenda left in a remote? Every agenda the Corp installed in a
    /// remote in the suite's games, or the teacher's play-outs of the listed groups at their roots, followed until it is
    /// scored, stolen or trashed; split by whether the run model let the Runner in at its next turn.
    /// </summary>
    private static int RegretExposureCommand(string suiteDirectory, RegretOptions options, TextWriter output, TextWriter error)
    {
        // A directory of saved ladder games is read as a suite of games without roots (Runner v28's ladders).
        RegretSuite suite = File.Exists(Path.Combine(suiteDirectory, RegretSuite.GamesFile)) ? RegretSuite.Load(suiteDirectory)
            : new RegretSuite([.. LadderGameArchive.ReadAll(suiteDirectory, options.Workers)
                .Select(entry => RegretGames.FromSaved(entry.Game, entry.Definition, "train", "ladder"))], [], []);
        RemoteFate[] fates;
        if (options.RootsFile is { } file)
        {
            var wanted = File.ReadLines(file).Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1]);
            RegretRoot[] roots = [.. suite.Roots.Where(root => wanted.ContainsKey(root.Id))];
            var byGame = roots.GroupBy(root => root.GameId).ToDictionary(group => group.Key, group => group.ToList());
            var settings = new TeacherSettings(options.Samples, options.Seed, options.HorizonTurns, options.Evaluation);
            fates = [.. ForGames([.. suite.Games.Where(game => byGame.ContainsKey(game.Id))], options.Workers, error, "exposure",
                game => byGame[game.Id].SelectMany(root => RemoteExposure.FromRoot(game, root, wanted[root.Id], settings)).ToArray()).SelectMany(found => found)];
        }
        else fates = [.. ForGames([.. suite.Games], options.Workers, error, "exposure", game => RemoteExposure.FromGame(game, options.AllRoots,
            options.Versions.FirstOrDefault()))
            .SelectMany(found => found)];
        if (options.Json)
        {
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(fates, RegretSuite.Json));
            return 0;
        }
        output.WriteLine($"ai-regret exposure agendas={fates.Length} roots={fates.Select(fate => fate.Source).Distinct().Count()}");
        foreach (var group in fates.GroupBy(fate => fate.Breakable switch { true => "breakable", false => "not breakable", null => "no Runner turn" })
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            int count = group.Count();
            string Share(string outcome) => string.Create(CultureInfo.InvariantCulture,
                $"{outcome}={group.Count(fate => fate.Outcome == outcome)} ({group.Count(fate => fate.Outcome == outcome) / (double)count:P0})");
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {group.Key}: {count}, run at least once {group.Count(fate => fate.Runs > 0) / (double)count:P0}, runs {group.Average(fate => fate.Runs):F2} an agenda; ") +
                string.Join(" ", group.Select(fate => fate.Outcome).Distinct().Order(StringComparer.Ordinal).Select(Share)));
        }
        return 0;
    }

    /// <summary>
    /// Bot plan S1.7: marks the suite's core, about <c>--roots</c> roots drawn by deal so a held-out deal is wholly in or
    /// out, keeping the suite's held-out share. A marking, not a run: the core keeps the labels it has. Absolute regret,
    /// the noise floor and the drift are quoted on it; a teacher change relabels it. Any earlier marking is replaced.
    /// </summary>
    /// <summary>
    /// Bot plan S1.7: writes into each label of a file the teacher version its settings name today, so a later policy bump
    /// (which renames what a setting means) does not rename labels made before it. A label already tagged keeps its tag.
    /// </summary>
    /// <summary>
    /// Bot plan S1.6: the decisions of the games where two versions of one side choose different groups, written as a new
    /// suite's roots (deduplicated by position, held out by deal) to be labelled and merged. Labels nothing.
    /// </summary>
    private static int RegretDivergeCommand(string source, RegretOptions options, TextWriter output, TextWriter error)
    {
        if (options.Pair is not [string first, string second]) throw new ArgumentException("ai-regret diverge needs --versions A,B.");
        string destination = options.Output ?? throw new ArgumentException("ai-regret diverge needs --output SUITE-DIR.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new ArgumentException("--output must name a new directory.");
        PlayerSide side = Entrant(first, options) is { } seat ? seat
            : OfflinePolicies.IsSupported(PlayerSide.Corp, first) ? PlayerSide.Corp
            : OfflinePolicies.IsSupported(PlayerSide.Runner, first) ? PlayerSide.Runner : throw new ArgumentException($"Unknown policy version '{first}'.");
        if (Entrant(second, options) is null && !OfflinePolicies.IsSupported(side, second)) throw new ArgumentException("--versions needs two versions of one side.");
        RegretGame[] games = File.Exists(Path.Combine(source, RegretSuite.GamesFile))
            ? [.. RegretSuite.Load(source).Games]
            : [.. LadderGameArchive.ReadAll(source, options.Workers).Select(entry => RegretGames.FromSaved(entry.Game, entry.Definition, "train", "ladder"))];
        // --games N: every k-th game, so a sizing run or a costly pair (S4.2's planners) spans the deals evenly.
        if (options.Games > 0 && options.Games < games.Length)
            games = [.. Enumerable.Range(0, options.Games).Select(index => games[(int)((long)index * games.Length / options.Games)])];
        var started = System.Diagnostics.Stopwatch.StartNew();
        RegretRoot[] roots = [.. ForGames(games, options.Workers, error, "diverge",
            game => RegretRoots.Diverge(game, first, second, side, (definition, seat, version) => Scored(definition, seat, options.Profile, version, options),
                new RegretSampling(Seed: options.Seed))).SelectMany(found => found)];
        HashSet<string> used = [.. roots.Select(root => root.GameId)];
        RegretSuite suite = new RegretSuite([.. games.Where(game => used.Contains(game.Id))], [.. roots], []).Deduplicated();
        suite.Save(destination);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret diverge {first},{second} games={games.Length} roots={suite.Roots.Length} (before deduplication {roots.Length}) " +
            $"turn={suite.Roots.Count(root => root.Kind == RegretRoots.TurnKind)} run={suite.Roots.Count(root => root.Kind == RegretRoots.RunKind)} " +
            $"held-out={suite.Roots.Count(root => root.Split == "held-out")} groups={suite.Roots.Sum(root => root.Candidates.Length)} seconds={started.Elapsed.TotalSeconds:F0} output={destination}"));
        return 0;
    }

    /// <summary>
    /// Bot plan S1.6: folds another suite into this one with its provenance: its games and roots are added, and a root
    /// already here keeps its labels and gains the other's groups and categories (a position two version pairs both
    /// found); the other's labels of new roots join this suite's labels file.
    /// </summary>
    private static int RegretMergeCommand(string suiteDirectory, RegretOptions options, TextWriter output)
    {
        string from = options.From ?? throw new ArgumentException("ai-regret merge needs --from SUITE-DIR.");
        RegretSuite suite = RegretSuite.Load(suiteDirectory), other = RegretSuite.Load(from);
        var roots = suite.Roots.ToDictionary(root => root.Id, StringComparer.Ordinal);
        int added = 0, joined = 0;
        foreach (RegretRoot root in other.Roots)
            if (roots.TryGetValue(root.Id, out RegretRoot? existing))
            {
                roots[root.Id] = existing with
                {
                    Candidates = [.. existing.Candidates, .. root.Candidates.Where(candidate => existing.Candidates.All(had => had.Group != candidate.Group))],
                    Categories = [.. existing.Categories.Union(root.Categories, StringComparer.Ordinal)],
                };
                joined++;
            }
            else
            {
                roots[root.Id] = root;
                added++;
            }
        HashSet<string> games = [.. suite.Games.Select(game => game.Id)];
        new RegretSuite([.. suite.Games, .. other.Games.Where(game => !games.Contains(game.Id))], [.. roots.Values], []).Save(suiteDirectory);
        string labelsPath = Path.Combine(suiteDirectory, options.LabelsFile), otherLabels = Path.Combine(from, options.LabelsFile);
        int labelled = 0;
        if (File.Exists(otherLabels))
        {
            var labels = (File.Exists(labelsPath) ? RegretSuite.ReadLines<RegretLabel>(labelsPath) : []).ToDictionary(label => label.RootId, StringComparer.Ordinal);
            foreach (RegretLabel label in RegretSuite.ReadLines<RegretLabel>(otherLabels))
                if (labels.TryAdd(label.RootId, label)) labelled++;
            RegretSuite.WriteLines(labelsPath, labels.Values.OrderBy(label => label.RootId, StringComparer.Ordinal));
        }
        output.WriteLine($"ai-regret merge from={from} roots-added={added} roots-joined={joined} labels-added={labelled} file={options.LabelsFile}");
        return 0;
    }

    /// <summary>
    /// Bot plan S4.1: what the planner's leaf costs per extraction on real positions: the observation's build and read,
    /// then evaluation set 4's features and set 5's (with the teacher's knowledge), each timed alone on one thread over the
    /// first games of a suite (<c>--turn-roots</c> of them), both sides at every decision.
    /// </summary>
    private static int RegretLeafCostCommand(string suiteDirectory, RegretOptions options, TextWriter output)
    {
        RegretGame[] games = [.. RegretSuite.Load(suiteDirectory).Games.Where(game => game.Source == "ladder").Take(options.TurnRoots)];
        long observeTicks = 0, set4Ticks = 0, set5Ticks = 0;
        int positions = 0;
        foreach ((RegretGame game, int index) in games.Select((game, index) => (game, index)))
        {
            FullMatchDefinition definition = RegretGames.Definition(game);
            var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
            var agents = new[] { PlayerSide.Corp, PlayerSide.Runner }.ToDictionary(side => side,
                side => (OfflineAgent)RatingLadder.CreateAgent(RegretLabels.DefaultTeacher, side, definition));
            foreach (RegretPosition position in RegretGames.Positions(game, definition))
            {
                foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, position.State, definition.Behavior);
                foreach ((PlayerSide side, OfflineAgent agent) in agents)
                {
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(position.State, side, definition.Behavior),
                        agent.Configuration.FeatureVersion);
                    long observed = System.Diagnostics.Stopwatch.GetTimestamp();
                    EvaluationFeatures.Extract(features, catalog, 4);
                    long four = System.Diagnostics.Stopwatch.GetTimestamp();
                    EvaluationFeatures.Extract(features, catalog, 5, agent.Checkpoint.Knowledge);
                    long five = System.Diagnostics.Stopwatch.GetTimestamp();
                    // The first game warms the code up and is not counted.
                    if (index == 0) continue;
                    observeTicks += observed - start;
                    set4Ticks += four - observed;
                    set5Ticks += five - four;
                    positions++;
                }
            }
        }
        double Micro(long ticks) => positions == 0 ? 0 : ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / positions;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret leaf-cost games={games.Length - 1} positions={positions} observation={Micro(observeTicks):F1}us set4={Micro(set4Ticks):F1}us set5={Micro(set5Ticks):F1}us leaf4={Micro(observeTicks + set4Ticks):F1}us leaf5={Micro(observeTicks + set5Ticks):F1}us"));
        return 0;
    }

    private static int RegretTagCommand(string suiteDirectory, RegretOptions options, TextWriter output)
    {
        string path = Path.Combine(suiteDirectory, options.LabelsFile);
        RegretLabel[] labels = [.. RegretSuite.ReadLines<RegretLabel>(path).Select(label => label with { TeacherVersion = label.TeacherName })];
        RegretSuite.WriteLines(path, labels);
        output.WriteLine($"ai-regret tag file={options.LabelsFile} labels={labels.Length} " + string.Join(" ",
            labels.GroupBy(label => label.TeacherVersion).Select(group => $"{group.Key}={group.Count()}")));
        return 0;
    }

    private static int RegretCoreCommand(string suiteDirectory, RegretOptions options, TextWriter output)
    {
        RegretSuite marked = RegretSuite.Load(suiteDirectory).WithCore(options.CoreRoots, options.Seed);
        new RegretSuite(marked.Games, marked.Roots, []).Save(suiteDirectory);
        RegretRoot[] core = [.. marked.Roots.Where(root => root.Categories.Contains(RegretSuite.CoreCategory))];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret core roots={core.Length} held-out={core.Count(root => root.Split == "held-out")} train={core.Count(root => root.Split != "held-out")} games={core.Select(root => root.GameId).Distinct().Count()} seed={options.Seed}"));
        return 0;
    }

    /// <summary>The discordant view: where two versions choose differently, what each chose and what the teacher made of it.</summary>
    private static void WriteDiscordance(RegretDiscordance discordance, string labelsFile, TextWriter output)
    {
        static string KindOf(string group) => group[..group.IndexOf('|', StringComparison.Ordinal)];
        static string Interval(RegretComparison comparison) => string.Create(CultureInfo.InvariantCulture,
            $"roots={comparison.Roots} regret difference={comparison.MeanDifference:F4} [{comparison.Lower:F4}, {comparison.Upper:F4}]{(comparison.Separated ? " separated" : "")}");
        ImmutableArray<RegretDiscord> discords = discordance.Discords;
        output.WriteLine($"discordant {discordance.First} − {discordance.Second} (labels {labelsFile}): roots={discords.Length} " +
            string.Join(" ", discords.GroupBy(discord => discord.Side + ":" + discord.Kind).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key}={group.Count()}")));
        output.WriteLine("  by chosen kinds: " + string.Join(" ", discords
            .GroupBy(discord => $"{KindOf(discord.FirstChosen)}/{KindOf(discord.SecondChosen)}")
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => string.Create(CultureInfo.InvariantCulture,
                $"{group.Key}={group.Count()} ({group.Average(discord => discord.FirstRegret - discord.SecondRegret):+0.000;-0.000})"))));
        foreach (var category in discords.SelectMany(discord => discord.Categories.Select(category => (category, discord)))
            .GroupBy(entry => entry.category).OrderBy(group => group.Key, StringComparer.Ordinal))
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  category {category.Key}: roots={category.Count()} mean difference={category.Average(entry => entry.discord.FirstRegret - entry.discord.SecondRegret):F4}"));
        output.WriteLine("  over the discordant roots: " + Interval(discordance.Discordant));
        output.WriteLine("  over every root:           " + Interval(discordance.All));
        foreach (RegretDiscord discord in discords)
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {discord.RootId} {discord.Kind}: {discordance.First} {discord.FirstValue:F3} (regret {discord.FirstRegret:F3}) \"{Short(discord.FirstDescription)}\" | {discordance.Second} {discord.SecondValue:F3} (regret {discord.SecondRegret:F3}) \"{Short(discord.SecondDescription)}\""));
    }

    private static string Short(string description) => description.Length <= 90 ? description : description[..90] + "…";

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
        // Every earlier finding goes, including those the manifest no longer lists, with its labels in every labels file:
        // the findings are relabelled from their current fixtures (label --missing).
        HashSet<string> replaced = [.. suite.Games.Where(game => game.Source == "finding").Select(game => game.Id).Concat(games.Select(game => game.Id))];
        new RegretSuite([.. suite.Games.Where(game => !replaced.Contains(game.Id)).Concat(games)],
            [.. suite.Roots.Where(root => !replaced.Contains(root.GameId)).Concat(roots)], []).Save(suiteDirectory);
        HashSet<string> gamesOf = [.. replaced];
        foreach (string file in Directory.EnumerateFiles(suiteDirectory, "labels*.jsonl.gz").Order(StringComparer.Ordinal))
            RegretSuite.WriteLines(file, RegretSuite.ReadLines<RegretLabel>(file)
                .Where(label => !gamesOf.Contains(label.RootId[..label.RootId.LastIndexOf('#')])).ToArray());
        output.WriteLine($"ai-regret findings games={games.Count} roots={roots.Count} of {findings.Count}");
        return 0;
    }

    /// <summary>
    /// S1.4: labels the groups the scored versions chose that have no value, with each root's own teacher settings and
    /// seed, and writes them into the suite and its labels file; returns how many were labelled.
    /// </summary>
    private static int LabelMissing(string suiteDirectory, RegretSuite suite, Dictionary<string, RegretLabel> labels,
        IEnumerable<RegretChoice> choices, RegretOptions options, TextWriter output)
    {
        var rootsById = suite.Roots.ToDictionary(root => root.Id);
        var wanted = choices.Where(choice => choice.Regret is null && choice.Missing is not null && labels.ContainsKey(choice.RootId))
            .GroupBy(choice => choice.RootId)
            .Select(group => (Root: rootsById[group.Key], Label: labels[group.Key],
                Groups: (IReadOnlyList<RegretCandidate>)[.. group.Select(choice => choice.Missing!).DistinctBy(candidate => candidate.Group)
                    .Where(candidate => !labels[group.Key].Values.Any(value => value.Group == candidate.Group))]))
            .Where(request => request.Groups.Count > 0).ToList();
        if (wanted.Count == 0) return 0;
        var byGame = wanted.GroupBy(request => request.Root.GameId).ToDictionary(group => group.Key, group => group.ToList());
        RegretGame[] games = [.. suite.Games.Where(game => byGame.ContainsKey(game.Id))];
        var found = ForGames(games, options.Workers, TextWriter.Null, "label-missing",
                game => RegretLabels.LabelGroups(game, [.. byGame[game.Id].Select(request => (request.Root, request.Label, request.Groups))]))
            .SelectMany(values => values).ToDictionary(pair => pair.Key, pair => pair.Value);
        var groupsByRoot = wanted.ToDictionary(request => request.Root.Id, request => request.Groups);
        RegretRoot[] roots = [.. suite.Roots.Select(root => groupsByRoot.TryGetValue(root.Id, out var added)
            ? root with { Candidates = [.. root.Candidates, .. added.Where(candidate => root.Candidates.All(had => had.Group != candidate.Group))] }
            : root)];
        new RegretSuite(suite.Games, [.. roots], []).Save(suiteDirectory);
        RegretLabel[] merged = [.. labels.Values.Select(label => found.TryGetValue(label.RootId, out var added)
            ? label with { Values = [.. label.Values, .. added] } : label).OrderBy(label => label.RootId, StringComparer.Ordinal)];
        RegretSuite.WriteLines(Path.Combine(suiteDirectory, options.LabelsFile), merged);
        int count = found.Values.Sum(values => values.Length);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret label-missing roots={found.Count} groups={count} of {wanted.Sum(request => request.Groups.Count)} wanted"));
        return count;
    }

    /// <summary>
    /// S4.5: a group a scored version chose that pruning left at its deciding samples is labelled again on the root's
    /// recording samples (its own settings and seed, as <see cref="LabelMissing"/> labels), and replaces its value in the
    /// labels file, so a comparison reads every choice on labels as good as any other choice's; returns how many.
    /// </summary>
    private static int ExtendPruned(string suiteDirectory, RegretSuite suite, Dictionary<string, RegretLabel> labels,
        IEnumerable<RegretChoice> choices, RegretOptions options, TextWriter output)
    {
        var rootsById = suite.Roots.ToDictionary(root => root.Id);
        var wanted = choices.Where(choice => choice.Pruned && choice.Chosen is not null && labels.ContainsKey(choice.RootId))
            .GroupBy(choice => choice.RootId)
            .Select(group => (Root: rootsById[group.Key], Label: labels[group.Key],
                Groups: (IReadOnlyList<RegretCandidate>)[.. group.Select(choice => choice.Chosen!).Distinct(StringComparer.Ordinal)
                    .Select(chosen => rootsById[group.Key].Candidates.First(candidate => candidate.Group == chosen))]))
            .ToList();
        if (wanted.Count == 0) return 0;
        var byGame = wanted.GroupBy(request => request.Root.GameId).ToDictionary(group => group.Key, group => group.ToList());
        RegretGame[] games = [.. suite.Games.Where(game => byGame.ContainsKey(game.Id))];
        var found = ForGames(games, options.Workers, TextWriter.Null, "extend-pruned",
                game => RegretLabels.LabelGroups(game, [.. byGame[game.Id].Select(request => (request.Root, request.Label, request.Groups))]))
            .SelectMany(values => values).ToDictionary(pair => pair.Key, pair => pair.Value);
        RegretLabel[] merged = [.. labels.Values.Select(label => found.TryGetValue(label.RootId, out var extended)
            ? label with
            {
                Values = [.. label.Values.Select(value => extended.FirstOrDefault(again => again.Group == value.Group) ?? value)],
            }
            : label).OrderBy(label => label.RootId, StringComparer.Ordinal)];
        RegretSuite.WriteLines(Path.Combine(suiteDirectory, options.LabelsFile), merged);
        int count = found.Values.Sum(values => values.Length);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret extend-pruned roots={found.Count} groups={count} of {wanted.Sum(request => request.Groups.Count)} wanted"));
        return count;
    }

    /// <summary>
    /// S1.4: gives every root of a suite its meaningful groups (the heuristic's band rule), keeping the groups and labels it
    /// had; the new groups are labelled by a relabel or on demand (<c>score --label-missing</c>).
    /// </summary>
    private static int RegretExtendCommand(string suiteDirectory, RegretOptions options, TextWriter output, TextWriter error)
    {
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        var sampling = new RegretSampling(Labelled: options.Labelled, Seed: options.Seed);
        var byGame = suite.Roots.GroupBy(root => root.GameId).ToDictionary(group => group.Key, group => group.ToList());
        RegretGame[] games = [.. suite.Games.Where(game => byGame.ContainsKey(game.Id))];
        RegretRoot[] roots = [.. ForGames(games, options.Workers, error, "extend", game => RegretRoots.Extend(game, byGame[game.Id], sampling))
            .SelectMany(found => found)];
        new RegretSuite(suite.Games, [.. roots], []).Save(suiteDirectory);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret extend roots={roots.Length} groups={suite.Roots.Sum(root => root.Candidates.Length)}->{roots.Sum(root => root.Candidates.Length)}"));
        return 0;
    }

    /// <summary>Applies <see cref="RegretSuite.Deduplicated"/> to a suite and its labels files in place.</summary>
    private static int RegretDedupeCommand(string suiteDirectory, RegretOptions options, TextWriter output)
    {
        RegretSuite suite = RegretSuite.Load(suiteDirectory);
        RegretSuite deduplicated = suite.Deduplicated();
        deduplicated.Save(suiteDirectory);
        HashSet<string> kept = [.. deduplicated.Roots.Select(root => root.Id)];
        foreach (string file in Directory.EnumerateFiles(suiteDirectory, "labels*.jsonl.gz").Order(StringComparer.Ordinal))
            RegretSuite.WriteLines(file, RegretSuite.ReadLines<RegretLabel>(file).Where(label => kept.Contains(label.RootId)).ToArray());
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret dedupe roots={suite.Roots.Length}->{deduplicated.Roots.Length} held-out={deduplicated.Roots.Count(root => root.Split == "held-out")}"));
        return 0;
    }

    /// <summary>The scored version's bot for a seat, keeping its reasons.</summary>
    private static OfflineAgent Scored(FullMatchDefinition definition, PlayerSide side, string profile, string version, RegretOptions? options = null) =>
        options is not null && Entrant(version, options) is not null
            ? RatingLadder.CreateAgent(version, side, definition) as OfflineAgent ?? throw new ArgumentException($"'{version}' is not an offline agent.")
            : SavedDecision.Agent(definition, side, version, profile, diagnostics: true);

    /// <summary>The side a ladder entrant is scored on (S4.2: <c>--side</c>), or null when the name is a policy version.</summary>
    private static PlayerSide? Entrant(string name, RegretOptions options) =>
        OfflinePolicies.IsSupported(PlayerSide.Corp, name) || OfflinePolicies.IsSupported(PlayerSide.Runner, name) ? null
        : options.Side ?? throw new ArgumentException($"'{name}' is a ladder entrant: name its side with --side corp|runner.");

    /// <summary>Runs <paramref name="work"/> over the games on <paramref name="workers"/> threads, in the games' order, reporting progress.</summary>
    /// <param name="save">Called with every finished game's result at each progress line, so a long run that is
    /// stopped keeps what it finished.</param>
    private static T[] ForGames<T>(RegretGame[] games, int workers, TextWriter error, string what, Func<RegretGame, T> work,
        Action<IReadOnlyList<T>>? save = null)
    {
        var results = new T[games.Length];
        var finishedResults = new System.Collections.Concurrent.ConcurrentQueue<T>();
        int done = 0;
        var started = System.Diagnostics.Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.For(0, games.Length, new ParallelOptions { MaxDegreeOfParallelism = workers }, index =>
        {
            results[index] = work(games[index]);
            finishedResults.Enqueue(results[index]);
            int finished = Interlocked.Increment(ref done);
            if (finished % 50 == 0 || finished == games.Length)
                lock (error)
                {
                    save?.Invoke([.. finishedResults]);
                    error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ai-regret {what} {finished}/{games.Length} {started.Elapsed.TotalSeconds:F0}s"));
                }
        });
        return results;
    }
}
