using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// engine-bench [--content developer|official] [--games N] [--seed S] [--json]; --profile-game and --profile-plan
    /// profile games and planner decisions instead (<see cref="RunEngineProfile"/>).
    /// Reports mean Apply time per action in Authoritative and Simulation mode
    /// over the same recorded positions. Build in Release for real numbers.
    /// </summary>
    private static int RunEngineBench(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Contains("--profile-game", StringComparer.Ordinal) || args.Contains("--profile-plan", StringComparer.Ordinal))
            return RunEngineProfile(args, output, error);
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string content = "official";
        int games = 10;
        ulong seed = 1;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json": break;
                    case "--content" when index + 1 < args.Length:
                        content = args[++index];
                        if (content is not ("developer" or "official")) throw new ArgumentException("--content requires 'developer' or 'official'.");
                        break;
                    case "--games" when index + 1 < args.Length:
                        if (!int.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out games) || games is < 1 or > 1000)
                            throw new ArgumentException("--games requires an integer from 1 to 1000.");
                        break;
                    case "--seed" when index + 1 < args.Length:
                        if (!ulong.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                            throw new ArgumentException("--seed requires an unsigned integer.");
                        break;
                    default: throw new ArgumentException($"Unsupported engine-bench argument '{args[index]}'.");
                }
            }

            if (content == "official" && CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("The official engine benchmark needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            error.WriteLine($"engine-bench failed: {exception.Message}");
            return 2;
        }

        EngineBenchmarkResult result = EngineBenchmark.Run(
            content == "developer" ? RulesFuzzRunner.DeveloperContentId : RulesFuzzRunner.OfficialContentIds[0], games, seed);
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new
            {
                schemaVersion = 1,
                kind = "engine-bench",
                content,
                games = result.Games,
                actions = result.Actions,
                authoritativeMicroseconds = result.AuthoritativeMicroseconds,
                simulationMicroseconds = result.SimulationMicroseconds,
                speedup = result.Speedup,
            }));
        }
        else
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"engine-bench content={content} games={result.Games} actions={result.Actions}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"authoritative={result.AuthoritativeMicroseconds:F1}us simulation={result.SimulationMicroseconds:F1}us speedup={result.Speedup:F2}x"));
        }

        return 0;
    }
}
