using System.Globalization;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-offers &lt;games-directory&gt; [--entrant E] [--workers 1..64] [--json]. The misplay sweep: replays games saved by
    /// <c>ai-ladder --save-games</c> and reports, for each seat's deck, every kind of play it was offered (a card's install,
    /// play, rez, advance or ability, a run, a basic action) with how often it was offered and taken, its share of the seat's
    /// choices, and the seat's score in games it took it and in games it was offered but never took it. <c>--entrant</c> keeps
    /// the games entrant E played against itself. Plays offered in
    /// fewer than a quarter of a deck's games are left out of the text report. Exit 0 on success, 2 for invalid arguments.
    /// </summary>
    private static int RunAiOffers(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string directory;
        string? entrant = null;
        int workers = 1;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-offers requires a directory of saved ladder games.");
            directory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Games directory does not exist: {directory}");
            for (int index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json": break;
                    case "--entrant" when index + 1 < args.Length: entrant = args[++index]; break;
                    case "--workers" when index + 1 < args.Length:
                        workers = int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is >= 1 and <= 64
                            ? value : throw new ArgumentException("--workers requires an integer from 1 to 64.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-offers argument '{args[index]}'.");
                }
            }
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message = exception.Message }));
            else error.WriteLine($"ai-offers failed: {exception.Message}");
            return 2;
        }

        Dictionary<string, string> titles = CardTitles();
        string Title(CardDefinitionId definition) => titles.GetValueOrDefault(definition.Value, definition.Value);
        IReadOnlyList<OfferRow> rows = OfferProfile.Measure(LadderGameArchive.Read(directory)
            .Where(entry => entrant is null || (entry.Game.Game.Corp == entrant && entry.Game.Game.Runner == entrant)), Title, workers);
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "ok", directory, rows }));
            return 0;
        }
        output.WriteLine($"ai-offers rows={rows.Count} directory={directory}");
        foreach (IGrouping<(PlayerSide, string, string), OfferRow> seat in rows.GroupBy(row => (row.Side, row.Entrant, row.Deck)))
        {
            output.WriteLine($"{seat.Key.Item1} {seat.Key.Item2} {seat.Key.Item3} ({seat.First().Identity}) games={seat.First().Games}");
            foreach (OfferRow row in seat.Where(row => row.GamesOffered * 4 >= row.Games))
                output.WriteLine(FormattableString.Invariant(
                    $"  {row.Decision,-18} offered/game={(double)row.Offered / row.Games,6:0.0} taken/game={(double)row.Taken / row.Games,5:0.0} take={row.TakeRate,4:P0} share={row.Share,4:P0} score taken={row.ScoreWhenTaken,4:0.00} refused={row.ScoreWhenRefused,4:0.00} {row.Play}"));
        }
        return 0;
    }
}
