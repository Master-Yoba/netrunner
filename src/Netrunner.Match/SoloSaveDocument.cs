using System.Reflection;
using System.Text.Json.Serialization;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record SoloSaveMetadata(string SaveId, string Name, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    string BuildVersion, PlayerSide HumanSide);

/// <summary>Private, self-contained session payload; only allowlisted previews cross to the save browser.</summary>
public sealed record SoloSavePayload(MatchRequest Request, GameSnapshot Snapshot, ReplayArtifact Replay, SoloAgentState Opponent);

public sealed record SoloSaveDocument(int SchemaVersion, string ArtifactKind, SoloSaveMetadata Metadata,
    SoloSavePayload Payload, string EngineStateHash, string PolicyStateHash, string IntegrityHash)
{
    public const int CurrentSchemaVersion = 1;
    public const string Kind = "solo-save";

    public static SoloSaveDocument Create(SoloSaveMetadata metadata, SoloSavePayload payload)
    {
        string engine = DeterministicHash.ForState(payload.Snapshot.ToState());
        string policy = BenchmarkHash.Value(payload.Opponent);
        return new(CurrentSchemaVersion, Kind, metadata, payload, engine, policy,
            Hash(CurrentSchemaVersion, Kind, metadata, payload, engine, policy));
    }

    public static SoloSaveMetadata NewMetadata(string name, PlayerSide humanSide, string? saveId = null,
        DateTimeOffset? createdUtc = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new(saveId ?? Guid.NewGuid().ToString("N"), name, createdUtc ?? now, now,
            typeof(SoloSaveDocument).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown", humanSide);
    }

    public void ValidateIntegrity()
    {
        if (SchemaVersion != CurrentSchemaVersion || ArtifactKind != Kind || Metadata is null || Payload is null
            || Metadata.SaveId is null || Metadata.SaveId.Length != 32 || !Guid.TryParseExact(Metadata.SaveId, "N", out _)
            || string.IsNullOrWhiteSpace(Metadata.Name) || Metadata.Name.Length > 120 || Metadata.Name.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(Metadata.BuildVersion) || !Enum.IsDefined(Metadata.HumanSide)
            || Metadata.CreatedUtc == default || Metadata.UpdatedUtc < Metadata.CreatedUtc
            || Metadata.CreatedUtc.Offset != TimeSpan.Zero || Metadata.UpdatedUtc.Offset != TimeSpan.Zero
            || Payload.Request is null || Payload.Snapshot is null || Payload.Replay is null || Payload.Opponent is null)
            throw new InvalidOperationException("Unsupported or malformed solo save.");
        if (!DeterministicHash.IsSha256Hex(EngineStateHash) || !DeterministicHash.IsSha256Hex(PolicyStateHash)
            || !DeterministicHash.IsSha256Hex(IntegrityHash)
            || EngineStateHash != DeterministicHash.ForState(Payload.Snapshot.ToState())
            || PolicyStateHash != BenchmarkHash.Value(Payload.Opponent)
            || IntegrityHash != Hash(SchemaVersion, ArtifactKind, Metadata, Payload, EngineStateHash, PolicyStateHash))
            throw new InvalidOperationException("The solo save checksum does not match its contents.");
    }

    public GameState Validate(IBehaviorCatalog behavior, IRuntimeCardCatalog runtime, bool verifyReplay = true)
    {
        ValidateIntegrity();
        GameState state = GameState.FromSnapshot(Payload.Snapshot, behavior, runtime);
        Payload.Replay.Validate(behavior, runtime);
        GameState initial = Payload.Replay.InitialSnapshot.ToState();
        MatchRequest request = Payload.Request;
        if (request.SchemaVersion != MatchRequest.CurrentSchemaVersion || request.Seed != state.InitialSeed
            || request.RulesVersion != state.RulesVersion || request.CardDataVersion != state.CardDataVersion
            || initial.ActionSequence != 0 || initial.InitialSeed != state.InitialSeed
            || initial.RuntimeCardCatalogVersion != state.RuntimeCardCatalogVersion
            || initial.RuntimeCardCatalogFingerprint != state.RuntimeCardCatalogFingerprint
            || state.RuntimeCardCatalogVersion != runtime.Version || state.RuntimeCardCatalogFingerprint != runtime.Fingerprint
            || state.BehaviorRegistryVersion != behavior.Version || state.BehaviorCatalogFingerprint != behavior.Fingerprint
            || Payload.Opponent.Side == Metadata.HumanSide
            || Payload.Replay.ExpectedFinalStateHash != EngineStateHash
            || Payload.Replay.Actions.Count != state.ActionSequence
            || (Payload.Replay.Actions.Count > 0 && Payload.Replay.Actions[^1].ExpectedStateHash != EngineStateHash))
            throw new InvalidOperationException("Solo save pins, replay endpoint or seat configuration disagree.");
        Payload.Opponent.ValidateBoundary(state, behavior, runtime);
        if (verifyReplay)
        {
            GameState setup = GameEngine.CreateMatch(request.Seed, request.CorpDeck, request.RunnerDeck,
                request.CardDataVersion, behavior, runtime).State;
            if (DeterministicHash.ForState(setup) != DeterministicHash.ForState(initial))
                throw new InvalidOperationException("The saved replay root does not match its setup request.");
            ReplayVerificationResult verified = ReplayVerifier.Verify(Payload.Replay, behavior, runtime);
            if (!verified.Success || verified.FinalStateHash != EngineStateHash)
                throw new InvalidOperationException($"Saved replay verification failed: {verified.Message}");
        }
        return state;
    }

    [JsonIgnore]
    public SoloSavePreview Preview => new(Metadata.SaveId, Metadata.Name, Metadata.HumanSide,
        Payload.Request.Seed, Payload.Request.CorpDeck.PresetId ?? "custom", Payload.Request.RunnerDeck.PresetId ?? "custom",
        Payload.Opponent.Configuration?.ProfileId ?? "contract-baseline", Payload.Snapshot.Turn.TurnNumber,
        Payload.Snapshot.Turn.Phase.ToString(), Metadata.UpdatedUtc, Payload.Snapshot.Outcome.Kind != GameOutcomeKind.Ongoing,
        Payload.Opponent.Configuration is { } configuration ? AgentPersonalities.Of(configuration) : AgentPersonality.Balanced);

    private static string Hash(int schema, string kind, SoloSaveMetadata metadata, SoloSavePayload payload,
        string engineStateHash, string policyStateHash) =>
        BenchmarkHash.Value(new { schema, kind, metadata, payload, engineStateHash, policyStateHash });
}

public sealed record SoloSavePreview(string SaveId, string Name, PlayerSide HumanSide, ulong Seed,
    string CorpDeck, string RunnerDeck, string Difficulty, int Turn, string Phase, DateTimeOffset UpdatedUtc, bool IsTerminal,
    AgentPersonality Personality = AgentPersonality.Balanced);
