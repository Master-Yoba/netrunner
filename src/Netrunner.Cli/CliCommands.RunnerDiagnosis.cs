using System.Collections.Concurrent;
using System.Globalization;
using Netrunner.Cards;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-runner-diagnosis &lt;games-directory&gt; [--workers 1..64] [--examples N]. Why the Runner loses (docs/BOT_IMPROVEMENT_PLAN.md):
    /// replays games saved by <c>ai-ladder --save-games</c> and reports, per Runner entrant and per Runner deck, its win
    /// rate, its losses by reason (the Corp's agendas, a flatline the Corp dealt, a flatline from its own action, and the
    /// cards behind those), runs per turn and credits at its turn starts. Exit 0 on success, 2 for invalid arguments.
    /// </summary>
    private static int RunAiRunnerDiagnosis(string[] args, TextWriter output, TextWriter error)
    {
        string directory;
        int workers = 1;
        int examples = 0;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("ai-runner-diagnosis requires a directory of saved ladder games.");
            directory = Path.GetFullPath(args[1]);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Games directory does not exist: {directory}");
            for (int index = 2; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--workers" when index + 1 < args.Length:
                        workers = int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is >= 1 and <= 64
                            ? value : throw new ArgumentException("--workers requires an integer from 1 to 64.");
                        break;
                    case "--examples" when index + 1 < args.Length:
                        examples = int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count is >= 0 and <= 1000
                            ? count : throw new ArgumentException("--examples requires an integer from 0 to 1000.");
                        break;
                    default: throw new ArgumentException($"Unsupported ai-runner-diagnosis argument '{args[index]}'.");
                }
            }
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-runner-diagnosis needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            error.WriteLine($"ai-runner-diagnosis failed: {exception.Message}");
            return 2;
        }

        var samples = new ConcurrentBag<RunnerGameSample>();
        Parallel.ForEach(LadderGameArchive.ReadAll(directory, workers), new ParallelOptions { MaxDegreeOfParallelism = workers },
            entry => samples.Add(RunnerDiagnosis.Measure(entry.Game, entry.Definition)));
        output.WriteLine($"ai-runner-diagnosis games={samples.Count} directory={directory}");
        foreach (RunnerDiagnosisRow row in RunnerDiagnosis.Summarize(samples))
            output.WriteLine(FormattableString.Invariant(
                $"{row.Entrant,-9} {row.RunnerDeck ?? "(all)",-40} games={row.Games,4} won={row.WinRate,5:P0} lost: agendas={row.LostToAgendas,5:P0} corp-flatline={row.LostToCorpFlatline,5:P0} self-flatline={row.LostToSelfFlatline,5:P0} capped={row.Capped,4:P0} | turns={row.Turns,5:0.0} runs/turn={row.RunsPerTurn,4:0.00} credits={row.CreditsAtTurnStart,4:0.0} points={row.RunnerPoints,4:0.0}")
                + (row.SelfFlatlineSources.Count == 0 ? "" : " | self-flatline by " + string.Join(", ", row.SelfFlatlineSources.Select(source => $"{source.Source} x{source.Count}"))));
        // A few self-flatlines per source, to replay the decision (game id and the fatal action's sequence).
        foreach (IGrouping<string, RunnerGameSample> source in samples.Where(sample => sample.SelfFlatline)
            .GroupBy(sample => sample.FinalRunnerSource ?? "?").OrderByDescending(group => group.Count()))
            foreach (RunnerGameSample sample in source.OrderBy(sample => sample.GameId, StringComparer.Ordinal).Take(examples))
                output.WriteLine(FormattableString.Invariant($"example {source.Key} {sample.GameId} sequence={sample.FinalSequence}"));
        return 0;
    }
}
