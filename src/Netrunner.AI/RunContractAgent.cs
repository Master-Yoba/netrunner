using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// The serialized contract of a curated M2 run: where the run starts, what
/// initiates it, the preferred installed icebreaker, and how much pumping
/// the contract allows across the run. The proof coordinators derive this
/// from the scenario document; the agent itself only sees these values and
/// the issued observation.
///
/// The breaker is a preference, not a persistent attachment: CR 26.03 6.5
/// has no "attach breaker to server" concept, so the agent uses the
/// preferred breaker whenever it is eligible and offered, and otherwise falls
/// back to the first offered eligible breaker (the engine materializes
/// offers in rig position order).
/// </summary>
public sealed record RunPlayContract(
    string Server,
    string Source,
    string Breaker,
    int MaxPumpsPerRun)
{
    public RunPlayContract(string server, string source, string breaker)
        : this(server, source, breaker, 1)
    {
    }
}

/// <summary>
/// The deterministic M2 contract agent. It consumes only the filtered
/// observation (issued legal action IDs) plus the <see cref="RunPlayContract"/>
/// and applies a fixed rule set:
///
/// - the Corp rezzes any approached ICE it is offered;
/// - the Runner breaks every offered subroutine quantity, preferring the
///   contracted breaker when it is offered (the breaker is an encounter-scoped
///   preference, never a persistent attachment);
/// - the Runner pumps while the contracted per-run pump budget remains,
///   preferring the contracted breaker when it is offered;
/// - the Runner continues at every jack-out decision;
/// - the Runner takes the first offered breach candidate, passes the
///   mid-access window for an accessed agenda (the engine then steals it
///   automatically, CR 26.03 7.2.3), and trashes accessed cards when the
///   action is offered.
///
/// The instance is stateful only by its per-run pump counter; a fresh
/// instance per proof run keeps the policy a pure function of (contract,
/// observation history), so identical seeds and contracts always produce
/// byte-identical traces.
/// </summary>
public sealed class RunContractAgent : IGameAgent
{
    private readonly RunPlayContract _contract;
    private int _pumpsUsed;

    public RunContractAgent(RunPlayContract contract)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
    }

    public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        IReadOnlyList<LegalAction> actions = observation.LegalActions;

        LegalAction? pass = actions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority);

        // CR 8.1/6.9.2b: rez the approached ICE whenever the Corp is offered
        // the rez inside the approach window.
        LegalAction? rez = actions.FirstOrDefault(action => action.Kind == ActionKind.RezIce);
        if (rez is not null)
        {
            return ValueTask.FromResult(rez.Id);
        }

        // CR 6.5/9.8: break every subroutine the engine offers (the offer
        // already respects subtype, strength, and the breaker's per-activation
        // BreakLimit), preferring the contracted breaker while it is offered.
        LegalAction? breakSubroutines = PreferPaymentDivision(
            actions.Where(action => action.Kind == ActionKind.BreakSubroutines
                && (action.Parameters as RunBreakParameters)?.BreakerId.Value == _contract.Breaker))
            ?? PreferPaymentDivision(actions.Where(action => action.Kind == ActionKind.BreakSubroutines));
        if (breakSubroutines is not null)
        {
            return ValueTask.FromResult(breakSubroutines.Id);
        }

        // CR 6.5/9.8: pump while the contracted per-run budget remains,
        // preferring the contracted breaker while it is offered.
        LegalAction? pump = PreferPaymentDivision(
            actions.Where(action => action.Kind == ActionKind.PumpBreaker
                && (action.Parameters as RunBreakerParameters)?.BreakerId.Value == _contract.Breaker))
            ?? PreferPaymentDivision(actions.Where(action => action.Kind == ActionKind.PumpBreaker));
        if (pump is not null && _pumpsUsed < _contract.MaxPumpsPerRun)
        {
            _pumpsUsed++;
            return ValueTask.FromResult(pump.Id);
        }

        // CR 7.5.4: take the first offered breach candidate (the engine offers
        // candidates in root order).
        LegalAction? candidate = actions.FirstOrDefault(action => action.Kind == ActionKind.ChooseBreachCandidate);
        if (candidate is not null)
        {
            return ValueTask.FromResult(candidate.Id);
        }

        // CR 7.2: trash accessed cards when offered; otherwise pass the
        // mid-access window (an accessed agenda is stolen automatically on
        // progression, CR 7.2.3).
        LegalAction? trash = PreferPaymentDivision(
            actions.Where(action => action.Kind == ActionKind.TrashAccessedCard));
        if (trash is not null)
        {
            return ValueTask.FromResult(trash.Id);
        }

        LegalAction? noAccessAction = actions.FirstOrDefault(action => action.Kind == ActionKind.NoAccessAction);
        if (noAccessAction is not null)
        {
            return ValueTask.FromResult(noAccessAction.Id);
        }

        // CR 6.9.4c: the curated run continues to completion.
        LegalAction? continueRun = actions.FirstOrDefault(action => action.Kind == ActionKind.ContinueRun);
        if (continueRun is not null)
        {
            return ValueTask.FromResult(continueRun.Id);
        }

        if (pass is null)
        {
            throw new InvalidOperationException(
                "The contract agent found no pass action in a run window; the observation is malformed.");
        }

        return ValueTask.FromResult(pass.Id);
    }

    /// <summary>
    /// Payment choices are part of the materialized action contract. The
    /// contract agent chooses the largest hosted portion first, then the
    /// stable action id, so equal observations always produce the same proof
    /// while still respecting the Runner's explicit split choice.
    /// </summary>
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
