using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Cli;

/// <summary>
/// The scripted-human M2 policy: an explicit, ordered script of the curated
/// run's decisions, written the way a human would plan the run step by step.
/// Unlike the rule-based <see cref="RunContractAgent"/>, each planned step is
/// a (context, action) pair consumed strictly in order: the next planned step
/// is never consulted until the current one has matched and fired, and any
/// decision the plan does not cover is handled by the default (continue at a
/// jack-out decision, pass elsewhere). Both policies consume only the
/// filtered observation and issued action IDs, so a byte-identical result is
/// the proof that the observation boundary is sufficient for both ways of
/// driving the engine.
/// </summary>
public sealed class ScriptedHumanRunPolicy : IGameAgent
{
    private readonly RunPlayContract _contract;
    private readonly List<(Func<Observation, bool> Context, Func<Observation, LegalAction?> Select)> _steps;
    private int _stepIndex;
    private int _pumpsUsed;

    public ScriptedHumanRunPolicy(RunPlayContract contract)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _steps = new List<(Func<Observation, bool>, Func<Observation, LegalAction?>)>
        {
            // Step 1: approach Palisade (the outermost remote ICE) — the Corp
            // rezzes it when the rez is offered.
            (observation => Offered(observation, ActionKind.RezIce),
                observation => Find(observation, ActionKind.RezIce)),
            // Step 2: encounter Palisade — pump the planned Cleaver once so it
            // reaches the remote-strengthened barrier (3 + 1 pump >= 2 + 2
            // remote). The pump is encounter-scoped; the Cleaver stays in the
            // rig and no breaker is attached to the server.
            (observation => BreakerPumpOffered(observation, _contract.Breaker) && _pumpsUsed < _contract.MaxPumpsPerRun,
                observation => TakePump(observation, _contract.Breaker)),
            // Step 3: break Palisade's end-the-run subroutine with the Cleaver.
            (observation => BreakerBreakOffered(observation, _contract.Breaker),
                observation => FindBreak(observation, _contract.Breaker)),
            // Step 4: approach Whitespace — the Corp rezzes it.
            (observation => Offered(observation, ActionKind.RezIce),
                observation => Find(observation, ActionKind.RezIce)),
            // Step 5: encounter Whitespace — the Cleaver does not interface
            // with code gates, so the other installed breaker (Buzzsaw) breaks
            // the first subroutine (lose 3 credits). Each break action gets a
            // fresh per-activation BreakLimit.
            (observation => Offered(observation, ActionKind.BreakSubroutines),
                observation => Find(observation, ActionKind.BreakSubroutines)),
            // Step 6: break the remaining subroutine (end the run at 6 or
            // fewer credits) with the same Buzzsaw.
            (observation => Offered(observation, ActionKind.BreakSubroutines),
                observation => Find(observation, ActionKind.BreakSubroutines)),
            // Step 7: breach — take the first offered candidate (Offworld Office).
            (observation => Offered(observation, ActionKind.ChooseBreachCandidate),
                observation => Find(observation, ActionKind.ChooseBreachCandidate)),
            // Step 8: pass the mid-access window for the accessed agenda —
            // CR 7.2.3 steals it automatically on progression (no mid-access
            // ability is modeled for agendas in the M2 card set).
            (observation => Offered(observation, ActionKind.NoAccessAction),
                observation => Find(observation, ActionKind.NoAccessAction)),
            // Step 9: take the remaining breach candidate (Nico Campaign).
            (observation => Offered(observation, ActionKind.ChooseBreachCandidate),
                observation => Find(observation, ActionKind.ChooseBreachCandidate)),
            // Step 10: trash the accessed asset for its printed cost.
            (observation => Offered(observation, ActionKind.TrashAccessedCard),
                observation => Find(observation, ActionKind.TrashAccessedCard)),
        };
    }

    public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        // Only the head of the plan is consulted for the current decision.
        if (_stepIndex < _steps.Count)
        {
            (Func<Observation, bool> context, Func<Observation, LegalAction?> select) = _steps[_stepIndex];
            if (context(observation))
            {
                LegalAction? selected = select(observation);
                if (selected is not null)
                {
                    _stepIndex++;
                    return ValueTask.FromResult(selected.Id);
                }
            }
        }

        // Default plan: the run continues to completion, and every window the
        // plan does not spell out is passed.
        LegalAction? continueRun = Find(observation, ActionKind.ContinueRun);
        if (continueRun is not null)
        {
            return ValueTask.FromResult(continueRun.Id);
        }

        LegalAction? pass = Find(observation, ActionKind.PassPriority);
        if (pass is null)
        {
            throw new InvalidOperationException(
                "The scripted human reached a decision that is neither planned nor a pass/continue choice.");
        }

        return ValueTask.FromResult(pass.Id);
    }

    private static bool Offered(Observation observation, ActionKind kind) =>
        observation.LegalActions.Any(action => action.Kind == kind);

    private static LegalAction? Find(Observation observation, ActionKind kind) =>
        PreferPaymentDivision(observation.LegalActions.Where(action => action.Kind == kind));

    private static bool BreakerPumpOffered(Observation observation, string breakerId) =>
        observation.LegalActions.Any(
            action => action.Kind == ActionKind.PumpBreaker
                && (action.Parameters as RunBreakerParameters)?.BreakerId.Value == breakerId);

    private static bool BreakerBreakOffered(Observation observation, string breakerId) =>
        observation.LegalActions.Any(
            action => action.Kind == ActionKind.BreakSubroutines
                && (action.Parameters as RunBreakParameters)?.BreakerId.Value == breakerId);

    private static LegalAction? FindBreak(Observation observation, string? breakerId)
    {
        if (breakerId is null)
        {
            return Find(observation, ActionKind.BreakSubroutines);
        }

        return PreferPaymentDivision(observation.LegalActions.Where(
            action => action.Kind == ActionKind.BreakSubroutines
                && (action.Parameters as RunBreakParameters)?.BreakerId.Value == breakerId));
    }

    private LegalAction? TakePump(Observation observation, string breakerId)
    {
        LegalAction? pump = PreferPaymentDivision(observation.LegalActions.Where(
            action => action.Kind == ActionKind.PumpBreaker
                && (action.Parameters as RunBreakerParameters)?.BreakerId.Value == breakerId));
        if (pump is null)
        {
            return null;
        }

        _pumpsUsed++;
        return pump;
    }

    private static LegalAction? PreferPaymentDivision(IEnumerable<LegalAction> actions) =>
        actions
            .OrderByDescending(HostedCredits)
            .ThenBy(action => action.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    private static int HostedCredits(LegalAction action) => action.Parameters switch
    {
        RunBreakParameters value => value.HostedCredits,
        RunBreakerParameters value => value.HostedCredits,
        RunCardPaymentParameters value => value.HostedCredits,
        _ => 0,
    };
}
