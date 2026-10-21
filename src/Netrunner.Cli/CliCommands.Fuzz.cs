using System.Collections.Concurrent;
using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// fuzz [--games N | --games-per-pairing N] [--seed S] [--content developer|official|...] [--action-cap N]
    /// [--workers 1..64] [--verify-replays-every N] [--differential] [--output new-dir] [--json]. --verify-replays-every
    /// verifies only every Nth finished game's replay (default 1, every game; a capped game always). --differential
    /// also checks Simulation-mode Apply against every authoritative action. --games-per-pairing plays N games of
    /// each deck pairing with each seat mix (the plan cycles both, so any count is spread evenly). Exit 0 means no defects,
    /// 1 means at least one defect, 2 means invalid arguments.
    /// </summary>
    private static int RunFuzz(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        int games = 50;
        int? gamesPerPairing = null;
        ulong seed = 1;
        string content = "developer";
        int actionCap = RulesFuzzRunner.DefaultActionCap;
        int workers = 1;
        int verifyReplaysEvery = 1;
        bool differential = false;
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
                    case "--differential": differential = true; break;
                    case "--games" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out games) || games is < 1 or > 100_000)
                            throw new ArgumentException("--games requires an integer from 1 to 100000.");
                        break;
                    case "--games-per-pairing" when index + 1 < args.Length:
                        gamesPerPairing = int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int perPairing)
                            && perPairing is >= 1 and <= 1_000 ? perPairing : throw new ArgumentException("--games-per-pairing requires an integer from 1 to 1000.");
                        break;
                    case "--seed" when index + 1 < args.Length:
                        if (!ulong.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                            throw new ArgumentException("--seed requires an unsigned integer.");
                        break;
                    case "--content" when index + 1 < args.Length:
                        content = args[++index];
                        if (!RulesFuzzRunner.ContentNames.Contains(content, StringComparer.Ordinal))
                            throw new ArgumentException($"--content requires one of {string.Join(", ", RulesFuzzRunner.ContentNames)}.");
                        break;
                    case "--action-cap" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out actionCap)
                            || actionCap is < 1 or > SimulationFixtureDocument.MaximumActionCap)
                            throw new ArgumentException($"--action-cap requires an integer from 1 to {SimulationFixtureDocument.MaximumActionCap}.");
                        break;
                    case "--workers" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out workers) || workers is < 1 or > 64)
                            throw new ArgumentException("--workers requires an integer from 1 to 64.");
                        break;
                    case "--output" when index + 1 < args.Length:
                        destination = args[++index];
                        break;
                    case "--verify-replays-every" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out verifyReplaysEvery)
                            || verifyReplaysEvery is < 1 or > 1_000)
                            throw new ArgumentException("--verify-replays-every requires an integer from 1 to 1000.");
                        break;
                    default: throw new ArgumentException($"Unsupported fuzz argument '{args[index]}'.");
                }
            }

            if (gamesPerPairing is not null && seen.Contains("--games"))
                throw new ArgumentException("--games and --games-per-pairing are alternatives.");
            if (content != "developer" && CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("Official fuzzing needs previously provisioned official content embedded at build time.");
            if (gamesPerPairing is { } perPairingGames)
            {
                games = perPairingGames * RulesFuzzRunner.ContentIds(content).Count * RulesFuzzRunner.Pairings.Count;
                if (games > 100_000) throw new ArgumentException("--games-per-pairing plans more than 100000 games for this content.");
            }
            if (destination is not null)
            {
                if (Directory.Exists(destination) || File.Exists(destination))
                    throw new ArgumentException("--output must name a new directory; existing evidence is never overwritten.");
                Directory.CreateDirectory(destination);
            }
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            WriteFuzzFailure(json, exception.Message, output, error);
            return 2;
        }

        IReadOnlyList<FuzzGameCase> cases = RulesFuzzRunner.Plan(games, seed,
            RulesFuzzRunner.ContentIds(content), verifyReplaysEvery);
        var results = new ConcurrentDictionary<string, FuzzGameResult>(StringComparer.Ordinal);
        Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = workers },
            async (fuzzCase, cancellation) =>
                results[fuzzCase.Id] = await RulesFuzzRunner.RunAsync(fuzzCase, actionCap, differential, cancellation).ConfigureAwait(false))
            .GetAwaiter().GetResult();

        FuzzGameResult[] ordered = cases.Select(fuzzCase => results[fuzzCase.Id]).ToArray();
        FuzzGameResult[] defects = ordered.Where(result => result.IsDefect).ToArray();
        var written = new List<string>();
        string? writeError = null;
        if (destination is not null)
        {
            try
            {
                foreach (FuzzGameResult defect in defects) written.Add(WriteFuzzDefect(destination, defect));
                File.WriteAllText(Path.Combine(destination, "summary.json"), FuzzSummaryJson(ordered, seed, content, actionCap));
            }
            catch (Exception exception) when (IsCommandFailure(exception))
            {
                writeError = exception.Message;
            }
        }

        if (json)
        {
            output.WriteLine(FuzzSummaryJson(ordered, seed, content, actionCap, destination, writeError));
        }
        else
        {
            output.WriteLine(defects.Length == 0 ? "fuzz=ok" : "fuzz=defects");
            output.WriteLine($"content={content} seed={seed} games={ordered.Length} action-cap={actionCap} replays-verified={ordered.Count(ReplayVerified)}");
            foreach (IGrouping<FuzzGameStatus, FuzzGameResult> group in ordered.GroupBy(result => result.Status).OrderBy(group => group.Key))
                output.WriteLine($"status {group.Key}={group.Count()}");
            output.WriteLine($"mean-actions={ordered.Average(result => result.ActionCount).ToString("F1", CultureInfo.InvariantCulture)}");
            foreach (FuzzGameResult defect in defects)
                error.WriteLine($"defect {defect.Case.Id} seed={defect.Case.GameSeed} {defect.Status} at action {defect.ActionCount}: {defect.Message}");
            foreach (string path in written) output.WriteLine($"fixture={path}");
            if (writeError is not null) error.WriteLine($"Could not write fuzz evidence: {writeError}");
        }

        return defects.Length == 0 && writeError is null ? 0 : 1;
    }

    private static string WriteFuzzDefect(string destination, FuzzGameResult defect)
    {
        string directory = Path.Combine(destination, defect.Case.Id);
        Directory.CreateDirectory(directory);
        if (defect.Failure is { } failure)
        {
            File.WriteAllText(Path.Combine(directory, "replay-prefix.json"), EngineJson.Serialize(failure.ReplayPrefix));
            File.WriteAllText(Path.Combine(directory, "state-before.json"), EngineJson.Serialize(failure.StateBefore));
        }
        File.WriteAllText(Path.Combine(directory, "failure.json"), EngineJson.Serialize(new
        {
            schemaVersion = 1,
            kind = "rules-fuzz-failure",
            fuzzCase = defect.Case,
            status = defect.Status.ToString(),
            actionCount = defect.ActionCount,
            message = defect.Message,
            failingSubmission = defect.Failure?.FailingSubmission,
            failingActionKind = defect.Failure?.FailingActionKind?.ToString(),
            rejectionCode = defect.Failure?.RejectionCode,
        }));
        return directory;
    }

    // A finished game's replay is verified when its case asks (every Nth game at a per-slice size); a capped one always.
    private static bool ReplayVerified(FuzzGameResult result) =>
        result.Status == FuzzGameStatus.Capped || (result.Status == FuzzGameStatus.Terminal && result.Case.VerifyReplay);

    private static string FuzzSummaryJson(FuzzGameResult[] results, ulong seed, string content, int actionCap,
        string? destination = null, string? writeError = null) => EngineJson.Serialize(new
        {
            schemaVersion = 1,
            kind = "rules-fuzz-summary",
            status = results.Any(result => result.IsDefect) || writeError is not null ? "defects" : "ok",
            content,
            seed,
            actionCap,
            games = results.Length,
            replaysVerified = results.Count(ReplayVerified),
            statuses = results.GroupBy(result => result.Status).OrderBy(group => group.Key)
                .ToDictionary(group => group.Key.ToString(), group => group.Count()),
            meanActions = results.Average(result => result.ActionCount),
            output = destination,
            writeError,
            results = results.Select(result => new
            {
                id = result.Case.Id,
                contentId = result.Case.ContentId,
                gameSeed = result.Case.GameSeed,
                corp = result.Case.Corp.ToString(),
                runner = result.Case.Runner.ToString(),
                status = result.Status.ToString(),
                actions = result.ActionCount,
                winner = result.Outcome?.Winner?.ToString(),
                reason = result.Outcome?.Reason?.ToString(),
                message = result.Message,
            }),
        });

    private static void WriteFuzzFailure(bool json, string message, TextWriter output, TextWriter error)
    {
        if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message }));
        else error.WriteLine($"fuzz failed: {message}");
    }
}
