using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunFullMatch(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Skip(1).Any(static argument => argument == "--json");
        string? replayPath = null;
        try
        {
            ulong seed = 42;
            string? fixturePath = null;
            string? presetKind = null;
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
                    case "--seed":
                        if (fixturePath is not null)
                        {
                            throw new ArgumentException("--seed is not supported together with a simulation fixture.");
                        }

                        if (index + 1 >= args.Length || !ulong.TryParse(args[index + 1], out seed))
                        {
                            throw new ArgumentException("--seed requires an unsigned integer.");
                        }

                        index++;
                        break;
                    case "--preset":
                        if (args[0] != "full-match")
                        {
                            throw new ArgumentException("--preset is supported only by the full-match command.");
                        }

                        if (index + 1 >= args.Length
                            || args[index + 1] is not ("basic" or "expanded"))
                        {
                            throw new ArgumentException("--preset requires 'basic' or 'expanded'.");
                        }

                        if (presetKind is not null)
                        {
                            throw new ArgumentException("--preset may be specified only once.");
                        }

                        presetKind = args[++index];
                        break;
                    case "--replay-out" when index + 1 < args.Length:
                        replayPath = args[++index];
                        break;
                    default: throw new ArgumentException($"Unsupported full-match argument '{args[index]}'.");
                }
            }
            if (presetKind is not null && fixturePath is not null)
            {
                throw new ArgumentException("--preset cannot be combined with a simulation fixture.");
            }

            FullMatchResult result;
            bool isDeveloper;
            string fixtureId;
            if (presetKind is not null)
            {
                result = RunOfficialPreset(presetKind, seed);
                isDeveloper = false;
                fixtureId = $"system-gateway-{presetKind}";
            }
            else
            {
                DeveloperMatchCards developer = DeveloperMatchCards.Create();
                SimulationFixtureDocument fixture = fixturePath is null
                    ? FullMatchDefinition.DeveloperFixture(developer, seed)
                    : SimulationFixtureJson.Parse(File.ReadAllText(fixturePath));
                isDeveloper = fixture.BehaviorRegistryVersion == DeveloperMatchCards.BehaviorVersion;
                result = FullMatchRunner.RunSimulationAsync(fixture,
                    isDeveloper ? developer.Data : CardDataResource.LoadEmbedded(),
                    isDeveloper ? developer.Behavior : GatewayBehaviorCatalog.Create()).GetAwaiter().GetResult();
                fixtureId = fixture.FixtureId;
            }
            ulong resultSeed = result.Replay.Seed;
            if (replayPath is not null)
            {
                File.WriteAllText(replayPath, EngineJson.Serialize(result.Replay));
            }

            if (json)
            {
                output.WriteLine(EngineJson.Serialize(new
                {
                    status = "ok",
                    fixtureId,
                    seed = resultSeed,
                    preset = presetKind,
                    developerOnly = isDeveloper,
                    winner = result.Outcome.Winner,
                    reason = result.Outcome.Reason,
                    actionCount = result.ActionCount,
                    finalStateHash = result.FinalStateHash,
                    replayVerified = true,
                    replayPath,
                }));
            }
            else
            {
                output.WriteLine("full-match=ok");
                output.WriteLine($"fixture-id={fixtureId}");
                output.WriteLine($"seed={resultSeed}");
                if (presetKind is not null)
                {
                    output.WriteLine($"preset={presetKind}");
                }
                output.WriteLine($"developer-only={isDeveloper.ToString().ToLowerInvariant()}");
                output.WriteLine($"winner={result.Outcome.Winner?.ToString() ?? "unknown"}");
                output.WriteLine($"reason={result.Outcome.Reason?.ToString() ?? "unknown"}");
                output.WriteLine($"action-count={result.ActionCount}");
                output.WriteLine($"final-state-hash={result.FinalStateHash}");
                output.WriteLine("replay-verified=true");
                if (replayPath is not null)
                {
                    output.WriteLine($"replay-path={replayPath}");
                }
            }

            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            string? diagnosticWriteError = null;
            if (exception is FullMatchStallException stall && replayPath is not null)
            {
                try
                {
                    File.WriteAllText(replayPath, EngineJson.Serialize(stall.Replay));
                }
                catch (Exception writeException) when (IsCommandFailure(writeException))
                {
                    diagnosticWriteError = writeException.Message;
                }
            }

            if (json)
            {
                if (exception is FullMatchStallException stalled)
                {
                    output.WriteLine(EngineJson.Serialize(new
                    {
                        status = "failed",
                        message = exception.Message,
                        seed = stalled.Replay.Seed,
                        actionCount = stalled.Replay.Actions.Count,
                        actionCap = stalled.ActionCap,
                        stateHash = stalled.Replay.ExpectedFinalStateHash,
                        replayPath,
                        diagnosticWriteError,
                    }));
                }
                else
                {
                    output.WriteLine(EngineJson.Serialize(new
                    {
                        status = "failed",
                        message = exception.Message,
                    }));
                }
            }
            else
            {
                error.WriteLine($"full-match failed: {exception.Message}");
                if (diagnosticWriteError is not null)
                {
                    error.WriteLine($"Could not write diagnostic replay: {diagnosticWriteError}");
                }
            }

            return 1;
        }
    }

    private static FullMatchResult RunOfficialPreset(string presetKind, ulong seed)
    {
        if (!string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "M4 official full-match presets are unavailable because the authenticated System Gateway card snapshot "
                + "is not embedded. Provision it with './scripts/update-card-data.sh --commit "
                + $"{CardDataValidator.OfficialCommitSha} --retrieval-date 2026-08-28' and rebuild with "
                + "-p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official.");
        }

        CardDataSet data = CardDataResource.LoadEmbedded();
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        OfficialPresetCatalog presets = OfficialPresetCatalog.Embedded();
        MatchRequest request = MatchRequestComposer.ComposeOfficial(
            $"system-gateway-{presetKind}-corp",
            $"system-gateway-{presetKind}-runner",
            data,
            seed,
            presets,
            behaviorCatalog: behavior);
        FullMatchDefinition definition = FullMatchDefinition.FromRequest(request, data, behavior);
        return FullMatchRunner.RunAsync(
            definition,
            actionCap: 4000).GetAwaiter().GetResult();
    }
}
