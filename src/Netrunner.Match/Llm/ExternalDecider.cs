using Netrunner.Core;

namespace Netrunner.Match.Llm;

/// <summary>A strategic decision waiting for an outside player: the request text and how many options it has.</summary>
public sealed record PendingChoice(PlayerSide Side, string Request, int Options);

/// <summary>
/// Strategic choices made by a player outside the process, such as Codex through the MCP game server. The game
/// waits in <see cref="DecideAsync"/> until the player answers; the player waits in <see cref="NextAsync"/> for the
/// next choice, or for the end of the game. A player that leaves hands every later choice to the heuristic.
/// </summary>
public sealed class ExternalDecider : IStrategicDecider
{
    private readonly object _gate = new();
    private TaskCompletionSource<PendingChoice?> _ready = NewReady();
    private TaskCompletionSource<StrategicVerdict>? _answer;
    private bool _left;

    /// <summary>The choice waiting now, if any.</summary>
    public PendingChoice? Pending { get; private set; }

    public Task<StrategicVerdict> DecideAsync(PlayerSide side, string request, int options, CancellationToken cancellationToken)
    {
        TaskCompletionSource<StrategicVerdict> answer;
        lock (_gate)
        {
            if (_left) return Task.FromResult(new StrategicVerdict(null, null, "The player left."));
            answer = _answer = new TaskCompletionSource<StrategicVerdict>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending = new PendingChoice(side, request, options);
            _ready.TrySetResult(Pending);
        }
        cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
        return answer.Task;
    }

    /// <summary>The next choice for the player, or null when the game is over.</summary>
    public Task<PendingChoice?> NextAsync()
    {
        lock (_gate) return _ready.Task;
    }

    /// <summary>Answers the waiting choice with an option the caller has checked; false when nothing waits.</summary>
    public bool Answer(int option, string? reason)
    {
        lock (_gate)
        {
            if (_answer is not { } answer) return false;
            _answer = null;
            Pending = null;
            _ready = NewReady();
            answer.TrySetResult(new StrategicVerdict(option, reason, null));
            return true;
        }
    }

    /// <summary>The game is over: a player waiting for its next choice gets none.</summary>
    public void Finish()
    {
        lock (_gate) _ready.TrySetResult(null);
    }

    /// <summary>The player has gone: the waiting choice and every later one fall to the heuristic.</summary>
    public void Leave()
    {
        lock (_gate)
        {
            _left = true;
            Pending = null;
            _answer?.TrySetResult(new StrategicVerdict(null, null, "The player left."));
            _answer = null;
        }
    }

    private static TaskCompletionSource<PendingChoice?> NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
