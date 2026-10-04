using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Observation-only lifecycle delivery at authoritative action boundaries.</summary>
public static class AgentLifecycleHost
{
    public static ulong DerivePolicySeed(ulong matchSeed, PlayerSide side)
    {
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            "netrunner/offline-policy-seed/v1/" + side + "/" + matchSeed.ToString(CultureInfo.InvariantCulture)));
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    public static void Observe(IGameAgent agent, GameState state, IBehaviorCatalog behavior)
    {
        if (agent is not IAgentLifecycle lifecycle) return;
        long started = ProfileTimings.Start();
        Observation observation = ObservationBuilder.Build(state, lifecycle.ControlledSide, behavior);
        ProfileTimings.Stop("match.observe.observation", started);
        started = ProfileTimings.Start();
        lifecycle.Observe(observation, state.ActionSequence);
        ProfileTimings.Stop("match.observe.agent", started);
    }

    public static void Accepted(IGameAgent agent, ActionSubmission submission, GameState state, IBehaviorCatalog behavior)
    {
        if (agent is not IAgentLifecycle lifecycle) return;
        if (state.GetPlayer(submission.Actor).Side == lifecycle.ControlledSide)
            lifecycle.CommitDecision(submission.DecisionId, submission.Revision, submission.ActionId, state.ActionSequence);
        Observe(agent, state, behavior);
    }

    public static void Discard(IGameAgent agent)
    {
        if (agent is IAgentLifecycle lifecycle) lifecycle.DiscardDecision();
    }

    public static void ValidateSeat(IGameAgent agent, PlayerSide side)
    {
        if (agent is IAgentLifecycle lifecycle && lifecycle.ControlledSide != side)
            throw new ArgumentException("Agent lifecycle is assigned to the wrong seat.", nameof(agent));
    }
}
