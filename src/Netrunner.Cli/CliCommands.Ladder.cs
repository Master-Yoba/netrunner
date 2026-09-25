using System.Collections.Concurrent;
using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-ladder [--content developer|official] [--entrants random,contract,easy,standard]
    /// [--seeds-per-pairing N] [--seed S] [--bootstrap N] [--action-cap N] [--workers 1..64]
    /// [--output new-dir] [--json]. Exit 0 when every seat separates in the expected
    /// order, 1 when it does not or a game failed, 2 for invalid arguments.
    /// </summary>
    private static int RunAiLadder(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string content = "official";
        string[] entrants = RatingLadder.EntrantOrder.ToArray();
        int seedsPerPairing = 5;
        ulong seed = 1;
        int bootstrap = 200;
        int actionCap = RatingLadder.DefaultActionCap;
        int workers = 1;
        string? destination = null;
        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 1; index < args.Length; index++)
            {
                if (args[index].StartsWith("--", StringComparison.Ordinal) && !seen.Add(args[index]))
                    throw new ArgumentException($"Duplicate argument '{args[index]}'.");
                switch (args[index])
                {
                    case "--json": break;
                    case "--content" when index + 1 < args.Length:
                        content = args[++index];
                        if (content is not ("developer" or "official"))
                            throw new ArgumentException("--content requires 'developer' or 'official'.");
                        break;
                    case "--entrants" when index + 1 < args.Length:
                        entrants = args[++index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (entrants.Length < 2 || entrants.Distinct(StringComparer.Ordinal).Count() != entrants.Length
                            || entrants.Any(entrant => !RatingLadder.EntrantOrder.Contains(entrant)))
                            throw new ArgumentException($"--entrants requires at least two distinct of {string.Join(',', RatingLadder.EntrantOrder)}.");
                        break;
                    case "--seeds-per-pairing" when index + 1 < args.Length:
                        seedsPerPairing = ReadInt(args[++index], "--seeds-per-pairing", 1, 1000);
                        break;
                    case "--seed" when index + 1 < args.Length:
                        if (!ulong.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                            throw new ArgumentException("--seed requires an unsigned integer.");
                        break;
                    case "--bootstrap" when index + 1 < args.Length:
                        bootstrap = ReadInt(args[++index], "--bootstrap", 0, 10_000);
                        break;
                    case "--action-cap" when index + 1 < args.Length:
                        actionCap = ReadInt(args[++index], "--action-cap", 1, SimulationFixtureDocument.MaximumActionCap);
                        break;
                    case "--workers" when index + 1 < args.Length:
                        workers = ReadInt(args[++index], "--workers", 1, 64);
                        break;
                    case "--output" when index + 1 < args.Length:
                        destination = args[++index];
                        break;
                    default: throw new ArgumentException($"Unsupported ai-ladder argument '{args[index]}'.");
                }
            }

            if (content == "official" && CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("The official ladder needs previously provisioned official content embedded at build time.");
            if (destination is not null)
            {
                if (Directory.Exists(destination) || File.Exists(destination))
                    throw new ArgumentException("--output must name a new directory; existing evidence is never overwritten.");
                Directory.CreateDirectory(destination);
            }
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message = exception.Message }));
            else error.WriteLine($"ai-ladder failed: {exception.Message}");
            return 2;
        }

        IReadOnlyList<LadderGame> games = RatingLadder.Plan(entrants, seedsPerPairing, seed,
            content == "developer" ? [RulesFuzzRunner.DeveloperContentId] : RulesFuzzRunner.OfficialContentIds);
        var results = new ConcurrentDictionary<string, LadderGameResult>(StringComparer.Ordinal);
        Parallel.ForEachAsync(games, new ParallelOptions { MaxDegreeOfParallelism = workers },
            async (game, cancellation) =>
                results[game.Id] = await RatingLadder.PlayAsync(game, actionCap, cancellation).ConfigureAwait(false))
            .GetAwaiter().GetResult();
        LadderGameResult[] ordered = games.Select(game => results[game.Id]).ToArray();
        LadderReport report = RatingLadder.Rate(ordered, bootstrap, seed);

        string reportJson = EngineJson.Serialize(new
        {
            schemaVersion = 1,
            kind = "ai-rating-ladder",
            status = report.Errors == 0 && report.Separations.All(separation => separation.Separated) ? "separated" : "not-separated",
            content,
            seed,
            seedsPerPairing,
            bootstrap,
            actionCap,
            entrants,
            games = report.Games,
            capped = report.Capped,
            errors = report.Errors,
            ratings = report.Ratings,
            differences = report.Differences,
            separations = report.Separations,
            pairings = ordered.GroupBy(result => (result.Game.Corp, result.Game.Runner))
                .Select(group => new
                {
                    corp = group.Key.Corp,
                    runner = group.Key.Runner,
                    games = group.Count(),
                    corpScore = group.Sum(result => result.CorpScore),
                    capped = group.Count(result => result.Capped),
                }),
            failures = ordered.Where(result => result.Error is not null)
                .Select(result => new { id = result.Game.Id, seed = result.Game.GameSeed, result.Error }),
            results = ordered.Select(result => new
            {
                id = result.Game.Id,
                contentId = result.Game.ContentId,
                gameSeed = result.Game.GameSeed,
                corp = result.Game.Corp,
                runner = result.Game.Runner,
                corpScore = result.CorpScore,
                capped = result.Capped,
                actions = result.Actions,
            }),
        });
        if (destination is not null)
        {
            File.WriteAllText(Path.Combine(destination, "ladder.json"), reportJson);
        }

        if (json)
        {
            output.WriteLine(reportJson);
        }
        else
        {
            output.WriteLine($"ai-ladder content={content} games={report.Games} capped={report.Capped} errors={report.Errors}");
            foreach (LadderRating rating in report.Ratings)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{rating.Side,-6} {rating.Entrant,-9} elo={rating.Elo,7:F0} 95%=[{rating.Lower:F0}, {rating.Upper:F0}] score={rating.ScoreRate:P0} games={rating.Games}"));
            }
            foreach (LadderDifference difference in report.Differences)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{difference.Side,-6} {difference.Stronger} - {difference.Weaker} = {difference.Elo:F0} 95%=[{difference.Lower:F0}, {difference.Upper:F0}]"));
            }
            foreach (LadderSeparation separation in report.Separations)
                output.WriteLine($"{separation.Side} order {string.Join(" < ", separation.Order)} separated={separation.Separated.ToString().ToLowerInvariant()}");
        }

        return report.Errors == 0 && report.Separations.All(separation => separation.Separated) ? 0 : 1;

        static int ReadInt(string text, string name, int minimum, int maximum) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= minimum && value <= maximum
                ? value
                : throw new ArgumentException($"{name} requires an integer from {minimum} to {maximum}.");
    }
}
