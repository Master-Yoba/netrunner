using System.Collections.Concurrent;
using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-lint &lt;games-directory&gt; [--workers 1..64] [--examples N] [--output new-file] [--json]. Runs the
    /// oddity lints (Wave 4.5, P1) over games saved by <c>ai-ladder --save-games</c> and reports, per lint,
    /// how often each entrant made the play, per game it played on that side. Exit 0 on success, 2 for
    /// invalid arguments.
    /// </summary>
    private static int RunAiLint(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string directory;
        int workers = 1, examples = 3;
        string? destination = null;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-lint requires a directory of saved ladder games.");
            directory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Games directory does not exist: {directory}");
            for (int index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json": break;
                    case "--workers" when index + 1 < args.Length: workers = ReadInt(args[++index], "--workers", 1, 64); break;
                    case "--examples" when index + 1 < args.Length: examples = ReadInt(args[++index], "--examples", 0, 100); break;
                    case "--output" when index + 1 < args.Length:
                        destination = args[++index];
                        if (File.Exists(destination)) throw new ArgumentException("--output must name a new file.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-lint argument '{args[index]}'.");
                }
            }
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-lint needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message = exception.Message }));
            else error.WriteLine($"ai-lint failed: {exception.Message}");
            return 2;
        }

        var findings = new ConcurrentBag<OddityFinding>();
        var sides = new ConcurrentBag<(PlayerSide Side, string Entrant)>();
        int games = 0;
        Parallel.ForEach(LadderGameArchive.Read(directory), new ParallelOptions { MaxDegreeOfParallelism = workers }, entry =>
        {
            Interlocked.Increment(ref games);
            sides.Add((PlayerSide.Corp, entry.Game.Game.Corp));
            sides.Add((PlayerSide.Runner, entry.Game.Game.Runner));
            foreach (OddityFinding finding in OddityLints.Run(entry.Game, entry.Definition)) findings.Add(finding);
        });

        var played = sides.GroupBy(pair => pair).ToDictionary(group => group.Key, group => group.Count());
        var rows = OddityLints.All.SelectMany(lint => played.Keys.Where(key => key.Side == lint.Side).OrderBy(key => key.Entrant, StringComparer.Ordinal)
            .Select(key =>
            {
                OddityFinding[] hits = [.. findings.Where(finding => finding.LintId == lint.Id && finding.Entrant == key.Entrant && finding.Side == key.Side)
                    .OrderBy(finding => finding.GameId, StringComparer.Ordinal).ThenBy(finding => finding.Sequence)];
                return new
                {
                    lint = lint.Id,
                    side = lint.Side.ToString(),
                    entrant = key.Entrant,
                    games = played[key],
                    findings = hits.Length,
                    gamesWithFinding = hits.Select(finding => finding.GameId).Distinct(StringComparer.Ordinal).Count(),
                    perGame = Math.Round(hits.Length / (double)played[key], 3),
                    examples = hits.Take(examples).Select(finding => $"{finding.GameId} #{finding.Sequence} t{finding.Turn}: {finding.Detail}").ToArray(),
                };
            })).ToArray();

        string report = json
            ? EngineJson.Serialize(new
            {
                schemaVersion = 1,
                status = "ok",
                directory,
                games,
                lints = OddityLints.All.Select(lint => new { lint.Id, side = lint.Side.ToString(), lint.Description }),
                rows,
                findings = findings.OrderBy(finding => finding.GameId, StringComparer.Ordinal).ThenBy(finding => finding.Sequence),
            })
            : string.Join(Environment.NewLine, new[] { $"ai-lint games={games} findings={findings.Count} directory={directory}" }
                .Concat(rows.Select(row => FormattableString.Invariant(
                    $"{row.lint,-30} {row.side,-6} {row.entrant,-16} {row.findings,5} in {row.gamesWithFinding,4}/{row.games,-4} games  {row.perGame,6:0.000}/game")
                    + string.Concat(row.examples.Select(example => Environment.NewLine + "    " + example)))));
        if (destination is not null) File.WriteAllText(destination, report);
        output.WriteLine(report);
        return 0;

        static int ReadInt(string text, string name, int minimum, int maximum) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= minimum && value <= maximum
                ? value : throw new ArgumentException($"{name} requires an integer from {minimum} to {maximum}.");
    }
}
