using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match.Llm;

namespace Netrunner.Match;

public sealed partial class MatchCoordinator
{
    private readonly SoloSaveDocument? _resumeSave;
    private GameSnapshot? _replayInitial;
    private readonly List<ReplayAction> _replayActions = [];
    private SoloAgentState? _committedAgent;
    private IReadOnlyList<MatchHistoryEntry>? _restoredHistory;

    public MatchCoordinator(FullMatchDefinition definition, SoloSaveDocument save, MatchCoordinatorOptions? options = null)
        : this(null, definition, RestoreOpponent(definition, save, options),
            options ?? new MatchCoordinatorOptions { HumanSide = save.Metadata.HumanSide }, save)
    {
    }

    /// <summary>The saved bot, wrapped again in the LLM opponent when the save was one's and the options can make one.</summary>
    private static IGameAgent RestoreOpponent(FullMatchDefinition definition, SoloSaveDocument save, MatchCoordinatorOptions? options)
    {
        IGameAgent restored = save.Payload.Opponent.Restore(definition.Behavior, definition.Runtime);
        return save.Payload.Opponent.LlmOpponent && restored is OfflineAgent bot
            && options?.RestoreLlmOpponent?.Invoke(bot, definition) is { } llm ? llm : restored;
    }

    /// <summary>Captures a detached document and leaves the session paused at that exact boundary.</summary>
    public Task<SoloSaveDocument> CaptureSaveAsync(string name, string? saveId = null, DateTimeOffset? createdUtc = null,
        CancellationToken cancellationToken = default) =>
        Enqueue(new CaptureSaveCommand(name, saveId, createdUtc, pause: true), cancellationToken);

    /// <summary>Captures the latest committed state without interrupting paced presentation or bot thinking.</summary>
    public Task<SoloSaveDocument> CaptureAutoSaveAsync(string name, string saveId, DateTimeOffset? createdUtc = null,
        CancellationToken cancellationToken = default) =>
        Enqueue(new CaptureSaveCommand(name, saveId, createdUtc, pause: false), cancellationToken);

    private void InitializePersistence(GameState state, ReplayArtifact? replay = null)
    {
        if (_fullMatch is null && _developerRoot is null) return;
        _replayInitial = replay?.InitialSnapshot ?? state.CreateSnapshot(_behaviorCatalog, _runtimeCardCatalog!);
        if (replay is not null) _replayActions.AddRange(replay.Actions);
        CacheCommittedAgent();
    }

    private void CacheCommittedAgent()
    {
        if (_corpAgent is OfflineAgent or FullGameContractAgent or LlmOpponentAgent)
            _committedAgent = SoloAgentState.Capture(_corpAgent);
    }

    private void HandleCaptureSave(CaptureSaveCommand command)
    {
        try
        {
            if (_state is null || _fullMatch is null || _replayInitial is null || _committedAgent is null)
                throw new InvalidOperationException("Only started full matches with a supported persistent agent can be saved.");
            if (_traceFault is not null) throw new InvalidOperationException("A faulted session cannot be saved.", _traceFault);
            SoloSaveMetadata metadata = SoloSaveDocument.NewMetadata(command.Name, _options.HumanSide,
                command.SaveId, command.CreatedUtc, PlayFormats.FormatOf(_fullMatch.Request, _behaviorCatalog));
            var replay = new ReplayArtifact(_replayInitial, _state.InitialSeed, _replayActions,
                expectedFinalStateHash: DeterministicHash.ForState(_state));
            var payload = new SoloSavePayload(_fullMatch.Request, _state.CreateSnapshot(_behaviorCatalog, _runtimeCardCatalog!),
                replay, _committedAgent);
            SoloSaveDocument document = SoloSaveDocument.Create(metadata, payload);
            document.Validate(_behaviorCatalog, _runtimeCardCatalog!, verifyReplay: false);
            if (command.Pause)
            {
                PauseAtBoundary();
                Publish([], null, thinking: false);
            }
            command.Complete(document);
        }
        catch (Exception exception) { command.Fail(exception); }
    }

    private void RestoreSave()
    {
        SoloSaveDocument save = _resumeSave!;
        if (MatchRequestJson.Serialize(save.Payload.Request) != MatchRequestJson.Serialize(_fullMatch!.Request))
            throw new InvalidOperationException("The saved MatchRequest does not match the supplied match definition.");
        _state = save.Validate(_behaviorCatalog, _runtimeCardCatalog!);
        InitializePersistence(_state, save.Payload.Replay);
        RestoreHistory(save.Payload.Replay);
        InitializeSessionTrace(_state);
        _recoveredPaused = true;
    }

    private void RestoreHistory(ReplayArtifact replay)
    {
        GameState state = replay.InitialSnapshot.ToState();
        var entries = new List<MatchHistoryEntry>();
        foreach (ReplayAction recorded in replay.Actions)
        {
            PlayerSide actor = state.GetPlayer(recorded.Submission.Actor).Side;
            LegalAction action = state.PendingDecision!.LegalActions.Single(candidate => candidate.Id == recorded.Submission.ActionId);
            MatchViewModel before = View(state);
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, _behaviorCatalog, _runtimeCardCatalog!);
            if (!applied.Accepted) throw new InvalidOperationException("Saved history replay was rejected.");
            state = applied.State;
            entries.AddRange(_historyProjector.Project(before, View(state), state, applied.Events, actor, action));
            foreach (EngineEvent entry in applied.Events)
                if (entry.Payload is RunEndedEventPayload ended)
                    _runOutcome = new(ended.RunId.Value, ended.InitiatingSide, ended.Reason, ended.Successful);
        }
        _restoredHistory = entries.AsReadOnly();

        MatchViewModel View(GameState value) => ObservationViewMapper.Build(
            ObservationBuilder.Build(value, _options.HumanSide, _behaviorCatalog), _metadataResolver, _behaviorCatalog,
            handSizeProjection: null, presentationContext: MatchPresentationProjector.Build(value, _options.HumanSide,
                _behaviorCatalog, _runtimeCardCatalog));
    }

    private sealed class CaptureSaveCommand(string name, string? saveId, DateTimeOffset? createdUtc, bool pause)
        : CoordinatorCommand<SoloSaveDocument>
    {
        public string Name { get; } = name;
        public string? SaveId { get; } = saveId;
        public DateTimeOffset? CreatedUtc { get; } = createdUtc;
        public bool Pause { get; } = pause;
    }
}
