using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunFullMatch(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            ulong seed = 42;
            string? replayPath = null;
            string? fixturePath = null;
            int index = 1;
            if (args[0] == "simulate")
            {
                if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("simulate requires a versioned simulation fixture path.");
                }
                fixturePath = args[index++];
            }
            for (; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json": break;
                    case "--seed" when fixturePath is null && index + 1 < args.Length && ulong.TryParse(args[index + 1], out seed):
                        index++;
                        break;
                    case "--replay-out" when index + 1 < args.Length:
                        replayPath = args[++index];
                        break;
                    default: throw new ArgumentException($"Unsupported full-match argument '{args[index]}'.");
                }
            }
            DeveloperMatchCards developer = DeveloperMatchCards.Create();
            SimulationFixtureDocument fixture = fixturePath is null ? FullMatchDefinition.DeveloperFixture(developer, seed)
                : SimulationFixtureJson.Parse(File.ReadAllText(fixturePath));
            bool isDeveloper = fixture.BehaviorRegistryVersion == DeveloperMatchCards.BehaviorVersion;
            FullMatchResult result = FullMatchRunner.RunSimulationAsync(fixture,
                isDeveloper ? developer.Data : CardDataResource.LoadEmbedded(),
                isDeveloper ? developer.Behavior : GatewayBehaviorCatalog.Create()).GetAwaiter().GetResult();
            if (replayPath is not null)
            {
                File.WriteAllText(replayPath, EngineJson.Serialize(result.Replay));
            }
            output.WriteLine(EngineJson.Serialize(new
            {
                status = "ok",
                fixtureId = fixture.FixtureId,
                seed = fixture.Seed,
                developerOnly = isDeveloper,
                winner = result.Outcome.Winner,
                reason = result.Outcome.Reason,
                actionCount = result.ActionCount,
                finalStateHash = result.FinalStateHash,
                replayVerified = true,
                replayPath,
            }));
            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            error.WriteLine($"full-match failed: {exception.Message}");
            return 1;
        }
    }
}
