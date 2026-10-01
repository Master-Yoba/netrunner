using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

/// <summary>A6: the Hard tier and the personalities, on the versions that have them.</summary>
public sealed class DifficultyTierTests
{
    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public void HardPlansAtLeastAsMuchAsStandardAndEasyPlansLeast(PlayerSide side)
    {
        OfflineAgentConfiguration easy = OfflinePolicies.Configure(side, "easy", 1);
        OfflineAgentConfiguration standard = OfflinePolicies.Configure(side, "standard", 1);
        OfflineAgentConfiguration hard = OfflinePolicies.Configure(side, "hard", 1);

        Assert.Equal(standard.PolicyVersion, hard.PolicyVersion);
        Assert.Equal(standard.FeatureVersion, hard.FeatureVersion);
        Assert.Equal(0, hard.NearTieTolerance);
        int Rollouts(OfflineAgentConfiguration configuration) =>
            configuration.Parameters.GetValueOrDefault("plan-samples") * configuration.Parameters.GetValueOrDefault("plan-width");
        // The Runner's Hard plans and its Standard doesn't; the Corp's Hard plans as its Standard does, since no
        // planner setting measured stronger.
        if (side == PlayerSide.Runner) Assert.True(Rollouts(hard) > Rollouts(standard), $"Runner Hard {Rollouts(hard)} rollouts, Standard {Rollouts(standard)}.");
        else Assert.Equal(Rollouts(standard), Rollouts(hard));
        Assert.True(Rollouts(standard) >= Rollouts(easy));
        Assert.Equal(0, Rollouts(easy));
        Assert.Equal(hard.PolicyVersion, OfflinePolicies.Create(hard).Version);
    }

    [Fact]
    public void StandardKeepsItsParametersSoFrozenGamesReplay()
    {
        // Hard and the personalities add parameters only to their own configurations.
        Assert.False(OfflinePolicies.Configure(PlayerSide.Runner, "standard", 1).Parameters.ContainsKey("plan-samples"));
        Assert.Equal(9, OfflinePolicies.Configure(PlayerSide.Corp, "standard", 1).Parameters.Count);
        Assert.Equal(4, OfflinePolicies.Configure(PlayerSide.Runner, "standard", 1).Parameters.Count);
        Assert.DoesNotContain(OfflinePolicies.Configure(PlayerSide.Corp, "standard", 1).Parameters.Keys, key => key == AgentPersonalities.ParameterKey);
    }

    [Fact]
    public void OlderVersionsHaveNoHardTier()
    {
        Assert.Throws<ArgumentException>(() => OfflinePolicies.Configure(PlayerSide.Corp, "hard", 1, CorpHeuristicPolicy.V18));
        Assert.Throws<ArgumentException>(() => OfflinePolicies.Configure(PlayerSide.Runner, "hard", 1, RunnerHeuristicPolicy.V17));
        OfflineAgentConfiguration old = OfflinePolicies.Configure(PlayerSide.Corp, "standard", 1, CorpHeuristicPolicy.V18);
        Assert.Throws<ArgumentException>(() => CorpPolicyProfiles.WithPersonality(old, AgentPersonality.Rush));
    }

    [Theory]
    [InlineData(PlayerSide.Corp, AgentPersonality.Glacier)]
    [InlineData(PlayerSide.Corp, AgentPersonality.Rush)]
    [InlineData(PlayerSide.Corp, AgentPersonality.AssetSpam)]
    [InlineData(PlayerSide.Runner, AgentPersonality.Aggressive)]
    [InlineData(PlayerSide.Runner, AgentPersonality.RigBuilder)]
    public void APersonalityIsOneParameterOnItsOwnSide(PlayerSide side, AgentPersonality personality)
    {
        foreach (string profile in new[] { "standard", "hard" })
        {
            OfflineAgentConfiguration plain = OfflinePolicies.Configure(side, profile, 3);
            OfflineAgentConfiguration styled = With(plain, personality);

            Assert.Equal(personality, AgentPersonalities.Of(styled));
            Assert.Equal(plain.Parameters.Count + 1, styled.Parameters.Count);
            Assert.Equal(AgentPersonality.Balanced, AgentPersonalities.Of(With(styled, AgentPersonality.Balanced)));
            Assert.Equal(plain.Parameters.Count, With(styled, AgentPersonality.Balanced).Parameters.Count);
            Assert.Contains(personality, AgentPersonalities.For(side));
        }
        PlayerSide other = side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        Assert.Throws<ArgumentException>(() => With(OfflinePolicies.Configure(other, "standard", 3), personality));
        Assert.DoesNotContain(personality, AgentPersonalities.For(other));
    }

    [Fact]
    public void PersonalitiesShiftTheirNamedEvaluationTerms()
    {
        IReadOnlyList<string> names = EvaluationFeatures.NamesFor(3);
        double[] none = new double[names.Count];
        double[] scored = Set(names, "agenda-self", 2);
        double[] iced = Set(names, "ice-installed", 4);
        double[] rigged = Set(names, "breaker-types", 3);

        Assert.Equal(0, AgentPersonalities.Shift(AgentPersonality.Rush, none, names));
        Assert.True(AgentPersonalities.Shift(AgentPersonality.Rush, scored, names) > 0);
        Assert.True(AgentPersonalities.Shift(AgentPersonality.Glacier, iced, names) > 0);
        Assert.True(AgentPersonalities.Shift(AgentPersonality.Rush, iced, names) < 0);
        Assert.True(AgentPersonalities.Shift(AgentPersonality.RigBuilder, rigged, names) > 0);
        Assert.True(AgentPersonalities.Shift(AgentPersonality.Aggressive, scored, names) > 0);
        Assert.Equal(0, AgentPersonalities.Shift(AgentPersonality.Balanced, scored, names));
    }

    [Fact]
    public void PersonalityKeysRoundTrip()
    {
        foreach (AgentPersonality personality in Enum.GetValues<AgentPersonality>())
            Assert.Equal(personality, AgentPersonalities.Keys[AgentPersonalities.Key(personality)]);
    }

    private static OfflineAgentConfiguration With(OfflineAgentConfiguration configuration, AgentPersonality personality) =>
        configuration.Side == PlayerSide.Corp
            ? CorpPolicyProfiles.WithPersonality(configuration, personality)
            : RunnerPolicyProfiles.WithPersonality(configuration, personality);

    private static double[] Set(IReadOnlyList<string> names, string name, double value)
    {
        double[] values = new double[names.Count];
        values[names.ToList().IndexOf(name)] = value;
        return values;
    }
}
