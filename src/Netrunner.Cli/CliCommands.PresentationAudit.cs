using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// presentation-audit [--games N] [--seed S] [--content developer|official]
    /// [--action-cap N] [--workers 1..64] [--captions N] [--output new-file] [--json]. Plays the
    /// fuzz plan's games through the coordinator from both seats and checks snap
    /// mismatches, caption leaks and, on official content, beat catalog coverage (UI plan U3); --captions
    /// prints the first game's first N captions from each seat for review. Exit 0
    /// means clean, 1 means a defect, 2 means invalid arguments.
    /// </summary>
    private static int RunPresentationAudit(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        int games = 20;
        ulong seed = 1;
        string content = "developer";
        int actionCap = 400;
        int workers = 1;
        int captions = 0;
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
                    case "--games" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out games) || games is < 1 or > 10_000)
                            throw new ArgumentException("--games requires an integer from 1 to 10000.");
                        break;
                    case "--seed" when index + 1 < args.Length:
                        if (!ulong.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                            throw new ArgumentException("--seed requires an unsigned integer.");
                        break;
                    case "--content" when index + 1 < args.Length:
                        content = args[++index];
                        if (content is not ("developer" or "official"))
                            throw new ArgumentException("--content requires 'developer' or 'official'.");
                        break;
                    case "--action-cap" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out actionCap) || actionCap is < 1 or > 5000)
                            throw new ArgumentException("--action-cap requires an integer from 1 to 5000.");
                        break;
                    case "--workers" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out workers) || workers is < 1 or > 64)
                            throw new ArgumentException("--workers requires an integer from 1 to 64.");
                        break;
                    case "--captions" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out captions) || captions is < 1 or > 400)
                            throw new ArgumentException("--captions requires an integer from 1 to 400.");
                        break;
                    case "--output" when index + 1 < args.Length:
                        destination = args[++index];
                        if (File.Exists(destination) || Directory.Exists(destination))
                            throw new ArgumentException("--output must name a new file; existing evidence is never overwritten.");
                        break;
                    default: throw new ArgumentException($"Unsupported presentation-audit argument '{args[index]}'.");
                }
            }
            if (content == "official" && CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("An official audit needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message = exception.Message }));
            else error.WriteLine($"presentation-audit failed: {exception.Message}");
            return 2;
        }

        IReadOnlyList<FuzzGameCase> cases = RulesFuzzRunner.Plan(games, seed,
            content == "developer" ? [RulesFuzzRunner.DeveloperContentId] : RulesFuzzRunner.OfficialContentIds);
        IReadOnlyList<PresentationAuditGame> results = PresentationAudit.RunAsync(cases, workers, actionCap).GetAwaiter().GetResult();
        var kinds = new SortedDictionary<PresentationEventKind, int>();
        foreach (PresentationAuditGame game in results)
            foreach ((PresentationEventKind kind, int count) in game.BeatKinds) kinds[kind] = kinds.GetValueOrDefault(kind) + count;
        PresentationEventKind[] missing = [.. PresentationAudit.Catalog.Where(kind => !kinds.ContainsKey(kind))];
        int mismatches = results.Sum(game => game.Mismatches.Count), leaks = results.Sum(game => game.CaptionLeaks.Count);
        int failures = results.Count(game => game.Failure is not null);
        // Developer content has no damage, tags or agendas to score, so only official content must show the whole catalog.
        bool clean = mismatches == 0 && leaks == 0 && failures == 0 && (content == "developer" || missing.Length == 0);
        string summary = EngineJson.Serialize(new
        {
            schemaVersion = 1,
            kind = "presentation-audit",
            status = clean ? "ok" : "defects",
            content,
            seed,
            games = cases.Count,
            seats = results.Count,
            terminal = results.Count(game => game.Terminal),
            publications = results.Sum(game => game.Publications),
            beats = results.Sum(game => game.Beats),
            snapMismatches = mismatches,
            captionLeaks = leaks,
            failures,
            beatKinds = kinds.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            missingCatalogKinds = missing.Select(kind => kind.ToString()),
            defects = results.Where(game => game.Mismatches.Count + game.CaptionLeaks.Count > 0 || game.Failure is not null)
                .Select(game => new
                {
                    id = game.Case.Id,
                    gameSeed = game.Case.GameSeed,
                    viewer = game.Viewer.ToString(),
                    failure = game.Failure,
                    mismatches = game.Mismatches.Take(10),
                    captionLeaks = game.CaptionLeaks.Take(10),
                }),
        });
        if (destination is not null) File.WriteAllText(destination, summary);
        if (json)
        {
            output.WriteLine(summary);
        }
        else
        {
            output.WriteLine(clean ? "presentation-audit=ok" : "presentation-audit=defects");
            output.WriteLine($"content={content} seed={seed} games={cases.Count} seats={results.Count} "
                + $"terminal={results.Count(game => game.Terminal)} beats={results.Sum(game => game.Beats)}");
            output.WriteLine($"snap-mismatches={mismatches} caption-leaks={leaks} failures={failures}");
            output.WriteLine("beat-kinds " + string.Join(" ", kinds.Select(pair => $"{pair.Key}={pair.Value}")));
            foreach (PresentationAuditGame game in results.Take(2))
                foreach (string caption in game.Captions.Take(captions)) output.WriteLine($"caption {game.Viewer}: {caption}");
            if (missing.Length > 0) (content == "developer" ? output : error).WriteLine("missing catalog beats: " + string.Join(", ", missing));
            foreach (PresentationAuditGame game in results.Where(game => game.Failure is not null))
                error.WriteLine($"failure {game.Case.Id} {game.Viewer}: {game.Failure}");
            foreach (PresentationAuditGame game in results)
                foreach (string line in game.Mismatches.Concat(game.CaptionLeaks).Take(5))
                    error.WriteLine($"defect {game.Case.Id} {game.Viewer} {line}");
        }
        return clean ? 0 : 1;
    }
}
