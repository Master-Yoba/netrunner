using System.Collections.Immutable;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Fixed held-out seeds and pairings; policy changes never regenerate the seed set.</summary>
public static class BenchmarkSuites
{
    public static AiBenchmarkManifest WithCorpProfile(AiBenchmarkManifest baseline, string profile, string? policyVersion = null)
    {
        if (baseline.ContentMode != "official") throw new ArgumentException("Gateway profiles require official content.");
        var result = baseline with
        {
            SuiteId = "corp-" + profile + "-" + baseline.SuiteId,
            Games = baseline.Games.Select(game => game with
            {
                Corp = BenchmarkAgentSpec.Offline(CorpPolicyProfiles.Create(profile, game.Corp.PolicySeed, policyVersion ?? CorpHeuristicPolicy.PolicyVersion)),
                ComparisonGroup = game.Runner.Kind == "offline" ? "offline-self-play" : "candidate-corp",
            }).ToImmutableArray(),
        };
        result.Validate();
        return result;
    }

    public static TacticalBenchmarkManifest WithCorpProfile(TacticalBenchmarkManifest baseline, string profile, string? policyVersion = null)
    {
        var result = baseline with
        {
            SuiteId = "corp-" + profile + "-" + baseline.SuiteId,
            Positions = baseline.Positions.Where(position => position.Metadata.Side == PlayerSide.Corp)
                .Select(position => position with
                {
                    Agent = BenchmarkAgentSpec.Offline(CorpPolicyProfiles.Create(profile, position.Agent.PolicySeed, policyVersion ?? CorpHeuristicPolicy.PolicyVersion)),
                }).ToImmutableArray(),
        };
        result.Validate();
        return result;
    }

    public static AiBenchmarkManifest WithRunnerProfile(AiBenchmarkManifest baseline, string profile, string? policyVersion = null)
    {
        if (baseline.ContentMode != "official") throw new ArgumentException("Gateway profiles require official content.");
        var result = baseline with
        {
            SuiteId = "runner-" + profile + "-" + baseline.SuiteId,
            Games = baseline.Games.Select(game => game with
            {
                Runner = BenchmarkAgentSpec.Offline(RunnerPolicyProfiles.Create(profile, game.Runner.PolicySeed, policyVersion ?? RunnerHeuristicPolicy.PolicyVersion)),
                ComparisonGroup = game.Corp.Kind == "offline" ? "offline-self-play" : "candidate-runner",
            }).ToImmutableArray(),
        };
        result.Validate();
        return result;
    }

    public static TacticalBenchmarkManifest WithRunnerProfile(TacticalBenchmarkManifest baseline, string profile, string? policyVersion = null)
    {
        var result = baseline with
        {
            SuiteId = "runner-" + profile + "-" + baseline.SuiteId,
            Positions = baseline.Positions.Where(position => position.Metadata.Side == PlayerSide.Runner)
                .Select(position => position with
                {
                    Agent = BenchmarkAgentSpec.Offline(RunnerPolicyProfiles.Create(profile, position.Agent.PolicySeed, policyVersion ?? RunnerHeuristicPolicy.PolicyVersion)),
                }).ToImmutableArray(),
        };
        result.Validate();
        return result;
    }

    public static AiBenchmarkManifest CreateContract(AiBenchmarkRunner runner, string suite)
    {
        if (suite is not ("smoke" or "full" or "developer"))
            throw new ArgumentException("Suite must be smoke, full, or developer.", nameof(suite));
        string mode = suite == "developer" ? "developer" : "official";
        var games = ImmutableArray.CreateBuilder<BenchmarkGameSpec>();
        if (mode == "developer")
        {
            for (int index = 1; index <= 20; index++)
                games.Add(runner.CreateRow($"developer-{index:D4}", mode, "developer-corp", "developer-runner", (ulong)index));
        }
        else
        {
            ulong[] seeds = suite == "smoke" ? [1, 42, 123, 47042, 8675309]
                : Enumerable.Range(0, 250).Select(index => 5000001UL + (ulong)index * 104729UL).ToArray();
            foreach (string corp in new[] { "basic", "expanded" })
                foreach (string runnerPreset in new[] { "basic", "expanded" })
                    for (int index = 0; index < seeds.Length; index++)
                        games.Add(runner.CreateRow($"{corp}-{runnerPreset}-{index + 1:D4}", mode,
                            $"system-gateway-{corp}-corp", $"system-gateway-{runnerPreset}-runner", seeds[index]));
        }
        return new(1, AiBenchmarkManifest.ArtifactKind, $"contract-{suite}-v1", mode, runner.Pins, new(), games.ToImmutable());
    }

    public static AiBenchmarkManifest Compare(AiBenchmarkManifest seedSuite, string suiteId,
        Func<PlayerSide, ulong, BenchmarkAgentSpec> candidate, Func<PlayerSide, ulong, BenchmarkAgentSpec> baseline)
    {
        seedSuite.Validate();
        var games = ImmutableArray.CreateBuilder<BenchmarkGameSpec>();
        foreach (BenchmarkGameSpec game in seedSuite.Games)
        {
            foreach (PlayerSide candidateSide in new[] { PlayerSide.Corp, PlayerSide.Runner })
            {
                string group = candidateSide == PlayerSide.Corp ? "candidate-corp" : "candidate-runner";
                games.Add(game with
                {
                    Id = game.Id + "-" + group,
                    ComparisonGroup = group,
                    Corp = (candidateSide == PlayerSide.Corp ? candidate : baseline)(PlayerSide.Corp, game.Corp.PolicySeed),
                    Runner = (candidateSide == PlayerSide.Runner ? candidate : baseline)(PlayerSide.Runner, game.Runner.PolicySeed),
                });
            }
        }
        var result = seedSuite with { SuiteId = suiteId, Games = games.ToImmutable() };
        result.Validate();
        return result;
    }
}
