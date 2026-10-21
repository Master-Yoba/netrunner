using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Seeded rules-fuzzing agent. It picks among the issued legal actions with a
/// configurable bias away from pass-like actions, so games exercise cards and
/// timing windows rather than passing through them. It reads only the issued
/// action kinds and never scores positions.
/// </summary>
public sealed class RandomLegalAgent : IGameAgent
{
    public const int DefaultActiveWeight = 4;

    private readonly int _activeWeight;
    private readonly DeterministicRandom _random;

    public RandomLegalAgent(ulong seed, int activeWeight = DefaultActiveWeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(activeWeight);
        _random = new DeterministicRandom(seed);
        _activeWeight = activeWeight;
    }

    /// <summary>Serializable RNG cursor, so a fuzz game can be resumed exactly.</summary>
    public ulong RandomState => _random.State;

    public ValueTask<ActionId> ChooseActionAsync(
        Observation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<LegalAction> actions = observation.LegalActions;
        if (actions.Count == 0)
        {
            throw new InvalidOperationException("The engine requested a decision with no legal actions.");
        }

        int total = 0;
        foreach (LegalAction action in actions)
        {
            total += Weight(action.Kind);
        }

        int roll = _random.NextInt(total);
        foreach (LegalAction action in actions)
        {
            roll -= Weight(action.Kind);
            if (roll < 0)
            {
                return ValueTask.FromResult(action.Id);
            }
        }

        throw new InvalidOperationException("Weighted selection did not choose an issued action.");
    }

    public static bool IsPassLike(ActionKind kind) => kind is ActionKind.PassPriority or ActionKind.EndTurn
        or ActionKind.JackOut or ActionKind.DeclineTrigger or ActionKind.DeclinePrevention
        or ActionKind.DeclineReplacement;

    private int Weight(ActionKind kind) => IsPassLike(kind) ? 1 : _activeWeight;
}
