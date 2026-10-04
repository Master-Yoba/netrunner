using System.Globalization;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// Bot plan S0.1: where a ladder game's and a planner decision's time goes. Build in Release for real numbers.
    /// engine-bench --profile-game [--content POOL] [--profile standard|hard|...] [--games N] [--seed S]
    ///   [--rollout-roots N] [--rollout-samples N] [--json]
    /// engine-bench --profile-plan GAME-DIR --sequence N [--profile hard|...] [--repeats N] [--rollout-samples N] [--json]
    /// </summary>
    private static int RunEngineProfile(string[] args, TextWriter output, TextWriter error)
    {
        bool json = false;
        string? planGame = null, content = "gateway-elevation", profile = null;
        int games = 1, rolloutRoots = 8, rolloutSamples = -1, repeats = 10;
        long sequence = -1;
        ulong seed = 1;
        bool gameMode = false;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--json": json = true; break;
                    case "--profile-game": gameMode = true; break;
                    case "--profile-plan" when index + 1 < args.Length: planGame = args[++index]; break;
                    case "--content" when index + 1 < args.Length: content = args[++index]; break;
                    case "--profile" when index + 1 < args.Length: profile = args[++index]; break;
                    case "--games" when index + 1 < args.Length: games = ProfileInt(args[++index], "--games", 1, 1000); break;
                    case "--seed" when index + 1 < args.Length:
                        if (!ulong.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                            throw new ArgumentException("--seed requires an unsigned integer.");
                        break;
                    case "--rollout-roots" when index + 1 < args.Length: rolloutRoots = ProfileInt(args[++index], "--rollout-roots", 0, 100); break;
                    case "--rollout-samples" when index + 1 < args.Length: rolloutSamples = ProfileInt(args[++index], "--rollout-samples", 0, 256); break;
                    case "--repeats" when index + 1 < args.Length: repeats = ProfileInt(args[++index], "--repeats", 1, 1000); break;
                    case "--sequence" when index + 1 < args.Length: sequence = ProfileInt(args[++index], "--sequence", 0, int.MaxValue); break;
                    default: throw new ArgumentException($"Unsupported engine-bench argument '{args[index]}'.");
                }
            }
            if (gameMode == (planGame is not null)) throw new ArgumentException("Pass exactly one of --profile-game and --profile-plan.");
            if (planGame is not null && sequence < 0) throw new ArgumentException("--profile-plan requires --sequence N.");

            if (gameMode)
            {
                GameProfile result = MatchProfiler.ProfileGamesAsync(RulesFuzzRunner.ContentIds(content), seed, profile ?? "standard", games,
                    rolloutRoots, rolloutSamples < 0 ? 4 : rolloutSamples).GetAwaiter().GetResult();
                WriteGameProfile(result, content, seed, json, output);
            }
            else
            {
                (LadderSavedGame saved, FullMatchDefinition definition) = LadderGameArchive.Read(planGame!).FirstOrDefault();
                if (saved is null) throw new ArgumentException($"No saved game in '{planGame}'.");
                PlanProfile result = MatchProfiler.ProfilePlanAsync(saved, definition, sequence, profile ?? "hard", repeats,
                    rolloutSamples < 0 ? 8 : rolloutSamples).GetAwaiter().GetResult();
                WritePlanProfile(result, json, output);
            }
            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            error.WriteLine($"engine-bench failed: {exception.Message}");
            return 2;
        }
    }

    private static int ProfileInt(string value, string option, int minimum, int maximum) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed >= minimum && parsed <= maximum
            ? parsed : throw new ArgumentException($"{option} requires an integer from {minimum} to {maximum}.");

    private static void WriteGameProfile(GameProfile result, string content, ulong seed, bool json, TextWriter output)
    {
        double total = result.Elapsed.TotalMilliseconds;
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new
            {
                schemaVersion = 1,
                kind = "engine-profile-game",
                content,
                seed,
                profile = result.Profile,
                games = result.Games,
                actions = result.Actions,
                elapsedMilliseconds = total,
                segments = SegmentRows(result.Segments, result.Actions),
                gameRollouts = RolloutRow(result.GameRollouts),
                rolloutSegments = SegmentRows(result.RolloutSegments, result.GameRollouts.Steps),
            }));
            return;
        }
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"engine-bench --profile-game content={content} profile={result.Profile} games={result.Games} actions={result.Actions} elapsed={total:F0}ms ({total * 1000 / Math.Max(1, result.Actions):F0}us/action)"));
        WriteSegments(result.Segments, total, result.Actions, "action", output);
        WriteRollouts(result.GameRollouts, result.RolloutSegments, output);
    }

    private static void WritePlanProfile(PlanProfile result, bool json, TextWriter output)
    {
        double total = result.Elapsed.TotalMilliseconds * result.Repeats;
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new
            {
                schemaVersion = 1,
                kind = "engine-profile-plan",
                game = result.GameId,
                sequence = result.Sequence,
                side = result.Side.ToString(),
                profile = result.Profile,
                chosen = result.Chosen,
                repeats = result.Repeats,
                decisionMilliseconds = result.Elapsed.TotalMilliseconds,
                segments = SegmentRows(result.Segments, result.Repeats),
                gameRollouts = RolloutRow(result.GameRollouts),
                rolloutSegments = SegmentRows(result.RolloutSegments, result.GameRollouts.Steps),
            }));
            return;
        }
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"engine-bench --profile-plan game={result.GameId} sequence={result.Sequence} side={result.Side} profile={result.Profile} chosen={result.Chosen} decision={result.Elapsed.TotalMilliseconds:F2}ms (mean of {result.Repeats})"));
        WriteSegments(result.Segments, total, result.Repeats, "decision", output);
        WriteRollouts(result.GameRollouts, result.RolloutSegments, output);
    }

    private static object[] SegmentRows(IReadOnlyList<ProfileSegment> segments, long per) => [.. segments.Select(segment => new
    {
        name = segment.Name,
        count = segment.Count,
        totalMilliseconds = segment.Elapsed.TotalMilliseconds,
        meanMicroseconds = segment.MeanMicroseconds,
        microsecondsPerUnit = per == 0 ? 0 : segment.Elapsed.TotalMicroseconds / per,
    })];

    private static object RolloutRow(GameRolloutCost cost) => new
    {
        rollouts = cost.Rollouts,
        finished = cost.Finished,
        meanSteps = cost.MeanSteps,
        meanMilliseconds = cost.MeanMilliseconds,
        microsecondsPerStep = cost.MicrosecondsPerStep,
    };

    private static void WriteSegments(IReadOnlyList<ProfileSegment> segments, double totalMilliseconds, long per, string unit, TextWriter output)
    {
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {"segment",-28} {"count",9} {"total ms",10} {"mean us",9} {"us/" + unit,10} {"share",6}"));
        foreach (ProfileSegment segment in segments)
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {segment.Name,-28} {segment.Count,9} {segment.Elapsed.TotalMilliseconds,10:F1} {segment.MeanMicroseconds,9:F1} {segment.Elapsed.TotalMicroseconds / Math.Max(1, per),10:F1} {100 * segment.Elapsed.TotalMilliseconds / Math.Max(1e-9, totalMilliseconds),5:F1}%"));
    }

    private static void WriteRollouts(GameRolloutCost cost, IReadOnlyList<ProfileSegment> segments, TextWriter output)
    {
        if (cost.Rollouts == 0) return;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"whole-game rollouts={cost.Rollouts} finished={cost.Finished} mean-steps={cost.MeanSteps:F0} mean={cost.MeanMilliseconds:F1}ms per-step={cost.MicrosecondsPerStep:F1}us"));
        WriteSegments(segments, cost.Elapsed.TotalMilliseconds, cost.Steps, "step", output);
    }
}
