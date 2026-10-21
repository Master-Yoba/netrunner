using System.Collections.Concurrent;
using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-style &lt;games-directory&gt; [--workers 1..64] [--json]. The style check of AI plan A6: replays games
    /// saved by <c>ai-ladder --save-games</c> and reports each entrant's mean play style per side (first agenda
    /// point, points by turn 8, ICE and remotes at turn 6, assets installed; runs by turn 6, first run, rig at
    /// turn 6). Exit 0 on success, 2 for invalid arguments.
    /// </summary>
    private static int RunAiStyle(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string directory;
        int workers = 1;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-style requires a directory of saved ladder games.");
            directory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Games directory does not exist: {directory}");
            for (int index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json": break;
                    case "--workers" when index + 1 < args.Length:
                        workers = int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is >= 1 and <= 64
                            ? value : throw new ArgumentException("--workers requires an integer from 1 to 64.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-style argument '{args[index]}'.");
                }
            }
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-style needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message = exception.Message }));
            else error.WriteLine($"ai-style failed: {exception.Message}");
            return 2;
        }

        var samples = new ConcurrentBag<PlayStyleSample>();
        int games = 0;
        Parallel.ForEach(LadderGameArchive.Read(directory), new ParallelOptions { MaxDegreeOfParallelism = workers }, entry =>
        {
            Interlocked.Increment(ref games);
            foreach (PlayStyleSample sample in PlayStyle.Measure(entry.Game, entry.Definition)) samples.Add(sample);
        });
        IReadOnlyList<PlayStyleSummary> rows = PlayStyle.Summarize(samples);
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "ok", directory, games, rows }));
            return 0;
        }
        output.WriteLine($"ai-style games={games} directory={directory}");
        foreach (PlayStyleSummary row in rows)
            output.WriteLine(row.Side == PlayerSide.Corp
                ? FormattableString.Invariant(
                    $"Corp   {row.Entrant,-14} games={row.Games,4} first-point-turn={row.FirstPointTurn,5:0.00} scored-by-t6={row.ScoredByTurn6,5:P0} points-by-t8={row.PointsByTurn8,4:0.00} ice-t6={row.IceAtTurn6,5:0.00} remotes-t6={row.RemotesAtTurn6,4:0.00} assets={row.AssetsInstalled,4:0.00}")
                : FormattableString.Invariant(
                    $"Runner {row.Entrant,-14} games={row.Games,4} first-point-turn={row.FirstPointTurn,5:0.00} scored-by-t6={row.ScoredByTurn6,5:P0} points-by-t8={row.PointsByTurn8,4:0.00} runs-t6={row.RunsByTurn6,5:0.00} first-run-turn={row.FirstRunTurn,5:0.00} rig-t6={row.RigAtTurn6,4:0.00}"));
        return 0;
    }
}
