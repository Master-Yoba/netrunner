using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Contract-test agent only. It establishes the observation/legal-action boundary
/// before game-specific heuristic scoring is introduced.
/// </summary>
public sealed class FirstLegalAgent : IGameAgent
{
    public ValueTask<ActionId> ChooseActionAsync(
        Observation observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (observation.LegalActions.Count == 0)
        {
            throw new InvalidOperationException("The engine requested a decision with no legal actions.");
        }

        LegalAction action = observation.LegalActions[0];
        return ValueTask.FromResult(action.Id);
    }
}
