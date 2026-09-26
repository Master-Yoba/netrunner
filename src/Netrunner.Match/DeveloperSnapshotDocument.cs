using System.Text.Json.Serialization;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Private developer artifact, separate from player saves. Raw imports begin a new replay root.</summary>
public sealed record DeveloperSnapshotDocument(int SchemaVersion, string ArtifactKind, PlayerSide HumanSide,
    string Origin, GameSnapshot Snapshot, ReplayArtifact Replay, SoloAgentState Opponent, string IntegrityHash)
{
    public const string Kind = "developer-snapshot";
    public const string ImportedRoot = "imported-scenario-root";
    public const string SessionRoot = "session-replay";

    [JsonIgnore]
    public string EngineStateHash => DeterministicHash.ForState(Snapshot.ToState());
    [JsonIgnore]
    public string PolicyStateHash => BenchmarkHash.Value(Opponent);

    public static DeveloperSnapshotDocument Create(PlayerSide humanSide, string origin, GameSnapshot snapshot,
        ReplayArtifact replay, SoloAgentState opponent) => new(1, Kind, humanSide, origin, snapshot, replay, opponent,
            Hash(humanSide, origin, snapshot, replay, opponent));

    public static DeveloperSnapshotDocument Import(GameSnapshot snapshot, PlayerSide humanSide,
        IBehaviorCatalog behavior, IRuntimeCardCatalog runtime)
    {
        GameState state = GameState.FromSnapshot(snapshot, behavior, runtime);
        PlayerSide side = humanSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        ulong seed = AgentLifecycleHost.DerivePolicySeed(state.InitialSeed, side);
        OfflineAgentConfiguration configuration = OfflinePolicies.Configure(side, "standard", seed);
        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, side, behavior), configuration.FeatureVersion);
        OfflineAgentCheckpoint checkpoint = OfflineAgentCheckpoint.Initial(configuration) with
        {
            ObservationCursor = state.ActionSequence,
            ObservationDigest = ObservationReader.Digest(features),
            Knowledge = AgentKnowledge.Empty.Observe(features, state.ActionSequence),
        };
        var opponent = new SoloAgentState("offline", OfflineAgentConfiguration.CurrentAgentVersion, side,
            configuration, checkpoint, null);
        var replay = new ReplayArtifact(snapshot, state.InitialSeed, [], expectedFinalStateHash: DeterministicHash.ForState(state));
        DeveloperSnapshotDocument result = Create(humanSide, ImportedRoot, snapshot, replay, opponent);
        result.Validate(behavior, runtime);
        return result;
    }

    public GameState Validate(IBehaviorCatalog behavior, IRuntimeCardCatalog runtime, bool verifyReplay = true)
    {
        if (SchemaVersion != 1 || ArtifactKind != Kind || !Enum.IsDefined(HumanSide)
            || Origin is not (ImportedRoot or SessionRoot) || Snapshot is null || Replay is null || Opponent is null
            || IntegrityHash != Hash(HumanSide, Origin, Snapshot, Replay, Opponent))
            throw new InvalidOperationException("Unsupported or corrupt developer snapshot.");
        GameState state = GameState.FromSnapshot(Snapshot, behavior, runtime);
        Replay.Validate(behavior, runtime);
        if (Opponent.Side == HumanSide || state.BehaviorRegistryVersion != behavior.Version
            || state.BehaviorCatalogFingerprint != behavior.Fingerprint || state.RuntimeCardCatalogVersion != runtime.Version
            || state.RuntimeCardCatalogFingerprint != runtime.Fingerprint
            || Replay.InitialSnapshot.ActionSequence + Replay.Actions.Count != state.ActionSequence
            || Replay.ExpectedFinalStateHash != DeterministicHash.ForState(state))
            throw new InvalidOperationException("Developer snapshot catalog pins, replay endpoint or seats disagree.");
        Opponent.ValidateBoundary(state, behavior, runtime);
        if (verifyReplay)
        {
            ReplayVerificationResult result = ReplayVerifier.Verify(Replay, behavior, runtime);
            if (!result.Success || result.FinalStateHash != DeterministicHash.ForState(state))
                throw new InvalidOperationException($"Developer replay verification failed: {result.Message}");
        }
        return state;
    }

    private static string Hash(PlayerSide humanSide, string origin, GameSnapshot snapshot, ReplayArtifact replay,
        SoloAgentState opponent) => BenchmarkHash.Value(new { SchemaVersion = 1, ArtifactKind = Kind, humanSide, origin, snapshot, replay, opponent });
}
