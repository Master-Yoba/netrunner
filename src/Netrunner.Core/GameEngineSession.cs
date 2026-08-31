namespace Netrunner.Core;

/// <summary>
/// Composition boundary for an authoritative engine match.  A session holds
/// one immutable behavior catalog and passes that same snapshot to every
/// reducer operation; no process-wide registration lookup is involved.
/// </summary>
public sealed class GameEngineSession
{
    public GameEngineSession(IBehaviorCatalog behaviorCatalog)
    {
        BehaviorCatalog = behaviorCatalog
            ?? throw new ArgumentNullException(nameof(behaviorCatalog));
    }

    public IBehaviorCatalog BehaviorCatalog { get; }

    public ApplyResult Apply(GameState state, ActionSubmission? submission) =>
        GameEngine.Apply(state, submission, BehaviorCatalog);

    public GameState BeginResolution(
        GameState state,
        ResolutionFrame frame,
        int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        GameEngine.BeginResolution(state, frame, BehaviorCatalog, maxResolutionSteps);

    public GameState StartResolution(
        GameState state,
        ResolutionFrame frame,
        int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        BeginResolution(state, frame, maxResolutionSteps);

    public GameState BeginRun(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        GameEngine.BeginRun(state, serverId, BehaviorCatalog, initiatingPlayer, initiatingSource);

    public GameEngine.RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        GameEngine.BeginRunWithEvents(state, serverId, BehaviorCatalog, initiatingPlayer, initiatingSource);
}

/// <summary>Short alias for callers that refer to a match engine session.</summary>
public sealed class EngineSession
{
    private readonly GameEngineSession _inner;

    public EngineSession(IBehaviorCatalog behaviorCatalog)
    {
        _inner = new GameEngineSession(behaviorCatalog);
    }

    public IBehaviorCatalog BehaviorCatalog => _inner.BehaviorCatalog;

    public ApplyResult Apply(GameState state, ActionSubmission? submission) => _inner.Apply(state, submission);

    public GameState BeginResolution(GameState state, ResolutionFrame frame, int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        _inner.BeginResolution(state, frame, maxResolutionSteps);

    public GameState BeginRun(GameState state, ServerId serverId, PlayerId? initiatingPlayer = null, CardInstanceId? initiatingSource = null) =>
        _inner.BeginRun(state, serverId, initiatingPlayer, initiatingSource);
}
