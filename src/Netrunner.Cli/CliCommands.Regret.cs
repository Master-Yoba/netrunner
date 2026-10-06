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
    ///   [--teacher hard|hard-model] (hard-model's rollouts model the opponent by the live Standard version)
    /// ai-regret score SUITE-DIR --version V[,V...]|latest [--profile P] [--split all|held-out|train] [--labels FILE]
    ///   [--blunders N] [--bootstrap N] [--workers N] [--json] [--discordant A,B] [--label-missing]
    /// ai-regret findings SUITE-DIR --manifest FILE --fixtures DIR --content POOL [--labelled N]
    /// ai-regret dedupe SUITE-DIR
    /// ai-regret extend SUITE-DIR [--labelled N] [--workers N] (every meaningful group at every root, S1.4)
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
        bool LabelMissing = false, int Lines = 0, string? RootsFile = null, int ScreenTurns = 1, string[]? Discordant = null,
        bool AllRoots = false, string TeacherEntrant = "hard")
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
                    "--all-roots" => options with { AllRoots = true },
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
                    "--roots" => options with { RootsFile = Next() },
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
        ImmutableArray<RegretLabel> kept = options.Missing && File.Exists(labelsPath) ? RegretSuite.ReadLines<RegretLabel>(labelsPath) : [];
        HashSet<string> labelled = [.. kept.Select(label => label.RootId)];
        // --roots FILE labels only the roots it lists, one id a line.
        HashSet<string>? listed = options.RootsFile is { } file
            ? [.. File.ReadLines(file).Select(line => line.Trim()).Where(line => line.Length > 0)] : null;
        HashSet<string> wanted = [.. suite.Roots.Select(root => root.Id).Where(id => !labelled.Contains(id) && (listed is null || listed.Contains(id)))];
        // The file is rewritten at every progress line, so a stopped run resumes with --missing.
        ImmutableArray<RegretLabel> labels = Label(suite, wanted, options, error, finished =>
            RegretSuite.WriteLines(labelsPath, kept.Concat(finished).OrderBy(label => label.RootId, StringComparer.Ordinal)));
        RegretSuite.WriteLines(labelsPath, kept.Concat(labels).OrderBy(label => label.RootId, StringComparer.Ordinal));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"ai-regret label roots={labels.Length} unlabellable={wanted.Count - labels.Length} samples={options.Samples} horizon={(options.HorizonTurns == 0 ? "game" : options.HorizonTurns.ToString(CultureInfo.InvariantCulture))} lines={options.Lines} seconds={started.Elapsed.TotalSeconds:F0} file={options.LabelsFile}"));
        return 0;
    }

    private static ImmutableArray<RegretLabel> Label(RegretSuite suite, HashSet<string> rootIds, RegretOptions options, TextWriter error,
        Action<IEnumerable<RegretLabel>>? save = null)
    {
        var settings = new TeacherSettings(options.Samples, options.Seed, options.HorizonTurns, options.Evaluation, Lines: options.Lines,
            ScreenTurns: options.ScreenTurns);
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
        RegretRoot[] roots = [.. suite.Roots.Where(root => options.Split == "all" || root.Split == options.Split)];
        var rootsById = roots.ToDictionary(root => root.Id);
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
        if (options.LabelMissing && LabelMissing(suiteDirectory, suite, labels, results.SelectMany(result => result.Choices), options, output) > 0)
            return RegretScoreCommand(suiteDirectory, options with { LabelMissing = false }, output);
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
            string teacher = labels.Values.FirstOrDefault(label => label.Lines > 1) is { } lined
                ? $"line teacher, beam {lined.Lines}, {lined.ScreenTurns}-turn screen" : "one-step teacher";
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
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{entry.version} ({entry.side}, {entry.profile}) {entry.seconds}s unlabelled={entry.unlabelled}"));
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
                $"compare {comparison.First} − {comparison.Second}: roots={comparison.Roots} regret difference={comparison.MeanDifference:F4} [{comparison.Lower:F4}, {comparison.Upper:F4}]{(comparison.Separated ? " separated" : "")}"));
        if (discordance is not null) WriteDiscordance(discordance, options.LabelsFile, output);
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
    private static OfflineAgent Scored(FullMatchDefinition definition, PlayerSide side, string profile, string version) =>
        SavedDecision.Agent(definition, side, version, profile, diagnostics: true);

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
