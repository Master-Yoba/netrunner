using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Developer proof failure with a resumable snapshot and the actions that reached it.</summary>
public sealed class FullMatchStallException : InvalidOperationException
{
    public FullMatchStallException(string reason, int actionCap, GameSnapshot snapshot, ReplayArtifact replay)
        : base($"{reason} seed={replay.Seed} actions={replay.Actions.Count} cap={actionCap} "
            + $"stateHash={replay.ExpectedFinalStateHash}.")
    {
        ActionCap = actionCap;
        Snapshot = snapshot;
        Replay = replay;
    }

    public int ActionCap { get; }

    public GameSnapshot Snapshot { get; }

    public ReplayArtifact Replay { get; }
}
