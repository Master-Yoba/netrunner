namespace Netrunner.Core;

/// <summary>
/// Composition boundary for an authoritative engine match.  A session holds
/// one immutable behavior catalog and passes that same snapshot to every
/// reducer operation; no process-wide registration lookup is involved.
/// </summary>
public sealed class GameEngineSession
{
    public GameEngineSession(IBehaviorCatalog behaviorCatalog)
        : this(behaviorCatalog, null)
    {
    }

    public GameEngineSession(
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        BehaviorCatalog = behaviorCatalog
            ?? throw new ArgumentNullException(nameof(behaviorCatalog));
        RuntimeCardCatalog = runtimeCardCatalog;
    }

    public IBehaviorCatalog BehaviorCatalog { get; }

    public IRuntimeCardCatalog? RuntimeCardCatalog { get; }

    public ApplyResult Apply(GameState state, ActionSubmission? submission) =>
        RuntimeCardCatalog is null
            ? GameEngine.Apply(state, submission, BehaviorCatalog)
            : GameEngine.Apply(state, submission, BehaviorCatalog, RuntimeCardCatalog);

    public GameState BeginResolution(
        GameState state,
        ResolutionFrame frame,
        int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        RuntimeCardCatalog is null
            ? GameEngine.BeginResolution(state, frame, BehaviorCatalog, maxResolutionSteps)
            : GameEngine.BeginResolution(state, frame, BehaviorCatalog, RuntimeCardCatalog, maxResolutionSteps);

    public GameState StartResolution(
        GameState state,
        ResolutionFrame frame,
        int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        BeginResolution(state, frame, maxResolutionSteps);

    public GameState BeginTurn(
        GameState state,
        PlayerSide side,
        int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        RuntimeCardCatalog is null
            ? GameEngine.BeginTurn(state, side, BehaviorCatalog, maxResolutionSteps)
            : GameEngine.BeginTurn(state, side, BehaviorCatalog, RuntimeCardCatalog, maxResolutionSteps);

    public GameEngine.TurnStartResult BeginTurnWithEvents(
        GameState state,
        PlayerSide side,
        int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        RuntimeCardCatalog is null
            ? GameEngine.BeginTurnWithEvents(state, side, BehaviorCatalog, maxResolutionSteps)
            : GameEngine.BeginTurnWithEvents(state, side, BehaviorCatalog, RuntimeCardCatalog, maxResolutionSteps);

    public GameEngine.TurnStartResult BeginTurnWithEvents(
        GameState state,
        int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        RuntimeCardCatalog is null
            ? GameEngine.BeginTurnWithEvents(state, state.Turn.ActiveSide, BehaviorCatalog, maxResolutionSteps)
            : GameEngine.BeginTurnWithEvents(state, state.Turn.ActiveSide, BehaviorCatalog, RuntimeCardCatalog, maxResolutionSteps);

    public GameState StartTurn(
        GameState state,
        PlayerSide side,
        int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        BeginTurn(state, side, maxResolutionSteps);

    public GameState BeginRun(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        RuntimeCardCatalog is null
            ? GameEngine.BeginRun(state, serverId, BehaviorCatalog, initiatingPlayer, initiatingSource)
            : GameEngine.BeginRun(state, serverId, BehaviorCatalog, RuntimeCardCatalog, initiatingPlayer, initiatingSource);

    public GameEngine.RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        RuntimeCardCatalog is null
            ? GameEngine.BeginRunWithEvents(state, serverId, BehaviorCatalog, initiatingPlayer, initiatingSource)
            : GameEngine.BeginRunWithEvents(state, serverId, BehaviorCatalog, RuntimeCardCatalog, initiatingPlayer, initiatingSource);
}

/// <summary>Short alias for callers that refer to a match engine session.</summary>
public sealed class EngineSession
{
    private readonly GameEngineSession _inner;

    public EngineSession(IBehaviorCatalog behaviorCatalog)
    {
        _inner = new GameEngineSession(behaviorCatalog);
    }

    public EngineSession(
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        _inner = new GameEngineSession(behaviorCatalog, runtimeCardCatalog);
    }

    public IBehaviorCatalog BehaviorCatalog => _inner.BehaviorCatalog;

    public IRuntimeCardCatalog? RuntimeCardCatalog => _inner.RuntimeCardCatalog;

    public ApplyResult Apply(GameState state, ActionSubmission? submission) => _inner.Apply(state, submission);

    public GameState BeginResolution(GameState state, ResolutionFrame frame, int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        _inner.BeginResolution(state, frame, maxResolutionSteps);

    public GameState BeginTurn(GameState state, PlayerSide side, int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        _inner.BeginTurn(state, side, maxResolutionSteps);

    public GameEngine.TurnStartResult BeginTurnWithEvents(GameState state, PlayerSide side, int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        _inner.BeginTurnWithEvents(state, side, maxResolutionSteps);

    public GameEngine.TurnStartResult BeginTurnWithEvents(GameState state, int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        _inner.BeginTurnWithEvents(state, maxResolutionSteps);

    public GameState StartTurn(GameState state, PlayerSide side, int maxResolutionSteps = GameEngine.DefaultResolutionStepBudget) =>
        _inner.StartTurn(state, side, maxResolutionSteps);

    public GameState BeginRun(GameState state, ServerId serverId, PlayerId? initiatingPlayer = null, CardInstanceId? initiatingSource = null) =>
        _inner.BeginRun(state, serverId, initiatingPlayer, initiatingSource);

    public GameEngine.RunStartResult BeginRunWithEvents(
        GameState state,
        ServerId serverId,
        PlayerId? initiatingPlayer = null,
        CardInstanceId? initiatingSource = null) =>
        _inner.BeginRunWithEvents(state, serverId, initiatingPlayer, initiatingSource);
}
