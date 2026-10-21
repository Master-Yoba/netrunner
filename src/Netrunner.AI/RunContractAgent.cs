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
        cancellationToken.ThrowIfCancellationRequested();

        if (observation.LegalActions is null)
        {
            throw new InvalidOperationException("The run contract agent received no legal-action list.");
        }

        EnsureActionShapes(observation.LegalActions);
        IReadOnlyList<LegalAction> actions = observation.LegalActions;

        // Reactions are checkpoint decisions, not replacement paid windows.
        // Core normally issues only UseTrigger/DeclineTrigger here; accepting
        // the typed actions directly also lets the contract progress through
        // an optional approach reaction without parsing its prompt.
        LegalAction? trigger = SelectTriggerAction(actions);
        if (trigger is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(trigger.Id);
        }

        // A trigger child may yield a structured BehaviorChoice before the
        // run timing frame resumes. Choose an existing materialized option or
        // target action; this is the common path for Manegarm, Anoetic Void,
        // and Funhouse. The private card IDs on an Anoetic action are retained
        // exactly because the agent returns only that issued ActionId.
        LegalAction? behaviorChoice = SelectBehaviorChoice(actions);
        if (behaviorChoice is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(behaviorChoice.Id);
        }

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

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(pass.Id);
    }

    private static void EnsureActionShapes(IReadOnlyList<LegalAction> actions)
    {
        var actionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (LegalAction? action in actions)
        {
            if (action is null
                || !HasStableId(action.Id.Value)
                || !actionIds.Add(action.Id.Value))
            {
                throw new InvalidOperationException(
                    "The run contract agent received a missing or duplicate engine-issued ActionId.");
            }

            bool valid = action.Kind switch
            {
                ActionKind.PassPriority
                    or ActionKind.NoAccessAction
                    or ActionKind.ContinueRun
                    or ActionKind.JackOut => action.Parameters is NoActionParameters,
                ActionKind.UseTrigger
                    or ActionKind.DeclineTrigger => action.Parameters is TriggerChoiceParameters trigger
                    && HasStableId(trigger.TriggerId.Value),
                ActionKind.ChooseBehavior => action.Parameters is BehaviorChoiceParameters behavior
                    && HasValidBehaviorChoiceParameters(behavior),
                ActionKind.RezIce => action.Parameters is RunIceParameters ice
                    && HasStableId(ice.IceId.Value),
                ActionKind.BreakSubroutines => action.Parameters is RunBreakParameters runBreak
                    && HasStableId(runBreak.BreakerId.Value)
                    && runBreak.Quantity > 0
                    && runBreak.HostedCredits >= 0,
                ActionKind.PumpBreaker => action.Parameters is RunBreakerParameters breaker
                    && HasStableId(breaker.BreakerId.Value)
                    && breaker.HostedCredits >= 0,
                ActionKind.ChooseBreachCandidate => action.Parameters is BreachCandidateParameters candidate
                    && ((candidate.CardId is { } card
                        && HasStableId(card.Value)
                        && candidate.Source is null)
                        || (candidate.CardId is null
                            && candidate.Source is { } source
                            && Enum.IsDefined(source))),
                ActionKind.TrashAccessedCard => action.Parameters is RunCardPaymentParameters payment
                    && HasStableId(payment.CardId.Value)
                    && payment.HostedCredits >= 0,
                _ => true,
            };

            if (!valid)
            {
                throw new InvalidOperationException(
                    $"The run contract agent received malformed parameters for '{action.Kind}'.");
            }
        }
    }

    private static LegalAction? SelectTriggerAction(IEnumerable<LegalAction> actions)
    {
        LegalAction[] use = actions
            .Where(static action => action.Kind == ActionKind.UseTrigger)
            .OrderBy(TriggerKey, StringComparer.Ordinal)
            .ThenBy(static action => action.Id.Value, StringComparer.Ordinal)
            .ToArray();
        if (use.Length > 0)
        {
            return use[0];
        }

        return actions
            .Where(static action => action.Kind == ActionKind.DeclineTrigger)
            .OrderBy(TriggerKey, StringComparer.Ordinal)
            .ThenBy(static action => action.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static LegalAction? SelectBehaviorChoice(IEnumerable<LegalAction> actions)
    {
        LegalAction[] choices = actions
            .Where(static action => action.Kind == ActionKind.ChooseBehavior)
            .OrderBy(BehaviorChoiceOrderKey, StringComparer.Ordinal)
            .ThenBy(SemanticKey, StringComparer.Ordinal)
            .ThenBy(static action => action.Id.Value, StringComparer.Ordinal)
            .ToArray();
        return choices.FirstOrDefault();
    }

    private static bool HasValidBehaviorChoiceParameters(BehaviorChoiceParameters? behavior)
    {
        if (behavior is null
            || !HasStableId(behavior.ChoiceKey)
            || behavior.OptionKeys is null
            || behavior.CardIds is null
            || behavior.OptionKeys.Count > 1
            || behavior.OptionKeys.Any(option => !HasStableId(option))
            || behavior.CardIds.Any(card => !HasStableId(card.Value))
            || behavior.OptionKeys.Distinct(StringComparer.Ordinal).Count() != behavior.OptionKeys.Count
            || behavior.CardIds.Distinct().Count() != behavior.CardIds.Count)
        {
            return false;
        }

        if (!behavior.FinalizeSelection)
        {
            return behavior.OptionKeys.Count == 0
                && behavior.CardIds.Count == 1;
        }

        return behavior.OptionKeys.Count == 0
            || behavior.CardIds.Count > 0
            || behavior.OptionKeys.Count == 1;
    }

    private static string BehaviorChoiceOrderKey(LegalAction action)
    {
        BehaviorChoiceParameters behavior = (BehaviorChoiceParameters)action.Parameters;
        int rank = behavior.OptionKeys.Count > 0 && behavior.CardIds.Count > 0
            ? 0
            : behavior.OptionKeys.Count > 0
                ? 1
                : behavior.CardIds.Count > 0
                    ? 2
                    : 3;
        int gainPreference = behavior.OptionKeys.Any(static option =>
            string.Equals(option, "gain", StringComparison.Ordinal))
            ? 0
            : 1;
        return $"{rank}:{gainPreference}:"
            + (behavior.OptionKeys.Count > 0
                ? AgentActionOrdering.IssuedOrderKey(action.Id)
                : $"semantic:{SemanticKey(action)}");
    }

    private static string TriggerKey(LegalAction action) =>
        $"trigger:{((TriggerChoiceParameters)action.Parameters).TriggerId.Value}";

    private static string SemanticKey(LegalAction action) => action.Parameters switch
    {
        BehaviorChoiceParameters behavior => BehaviorKey(behavior),
        _ => action.Kind.ToString(),
    };

    private static string BehaviorKey(BehaviorChoiceParameters behavior)
    {
        string options = string.Join(
            ",",
            behavior.OptionKeys.OrderBy(static value => value, StringComparer.Ordinal));
        string cards = string.Join(
            ",",
            behavior.CardIds
                .Select(static card => card.Value)
                .OrderBy(static value => value, StringComparer.Ordinal));
        return $"behavior:{behavior.ChoiceKey}:options:{options}:cards:{cards}:final:{behavior.FinalizeSelection}";
    }

    private static bool HasStableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(static character =>
            !char.IsControl(character) && !char.IsWhiteSpace(character));
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
