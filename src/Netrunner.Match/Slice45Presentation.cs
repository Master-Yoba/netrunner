using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Optional typed run cues reserved for Slice 4.5. Each field remains nullable
/// so older or malformed observation envelopes fail closed instead of causing
/// the presentation layer to reconstruct authoritative run state.
/// </summary>
public sealed record Slice45CueViewModel(
    bool? RunRestrictionActive,
    bool? BioroidAbilityReady,
    bool? MayflyBreakArmed)
{
    public static Slice45CueViewModel Unavailable { get; } = new(null, null, null);

    public bool HasAnyValue => RunRestrictionActive.HasValue
        || BioroidAbilityReady.HasValue
        || MayflyBreakArmed.HasValue;

    /// <summary>Compatibility name for the typed no-steal/no-trash cue.</summary>
    public bool? NoStealOrTrashActive => RunRestrictionActive;

    /// <summary>Compatibility name for the observation's legal bioroid cue.</summary>
    public bool? BioroidAbilityLegal => BioroidAbilityReady;

    /// <summary>Compatibility name for the observation's armed Mayfly cue.</summary>
    public bool? MayflyRunEndArmed => MayflyBreakArmed;
}

/// <summary>Whether the current engine-issued decision belongs in run focus.</summary>
public enum Slice45DecisionSurface
{
    None,
    Approach,
    Encounter,
}

/// <summary>
/// Match-owned copy of one typed behavior cost. It deliberately omits the
/// catalog's target descriptor so a hidden card identity can only come from
/// the viewer's engine-issued action payload.
/// </summary>
public sealed record Slice45BehaviorCostViewModel(
    BehaviorCostKind Kind,
    int Amount,
    BehaviorAdditionalCostKind? AdditionalKind,
    string? AdditionalKey)
{
    /// <summary>
    /// Counter names and structured additional targets are retained as typed
    /// values. A renderer may show a stable generic label, but it must not
    /// reconstruct either field from a card title or rules text.
    /// </summary>
    public string? CounterName { get; init; }

    public BehaviorTarget? AdditionalTarget { get; init; }

    public string Summary => Kind switch
    {
        BehaviorCostKind.Credits => Pluralize(Amount, "credit"),
        BehaviorCostKind.Clicks => Pluralize(Amount, "click"),
        BehaviorCostKind.LoseClick => $"lose {Pluralize(Amount, "click")}",
        BehaviorCostKind.Tags => Pluralize(Amount, "tag"),
        BehaviorCostKind.Counter when IsStableIdentifier(CounterName) =>
            $"{Pluralize(Amount, "counter")} · {CounterName}",
        BehaviorCostKind.Counter => "counter cost unavailable",
        BehaviorCostKind.HostedCredits => Pluralize(Amount, "hosted credit"),
        BehaviorCostKind.TrashSelf => "trash this card",
        BehaviorCostKind.RemoveSelfFromGame => "remove this card from game",
        BehaviorCostKind.Additional when AdditionalKind == BehaviorAdditionalCostKind.TrashCards =>
            $"{Pluralize(Amount, "private card")} to trash",
        BehaviorCostKind.Additional when IsStableIdentifier(AdditionalKey) =>
            $"{Pluralize(Amount, "additional cost")} · {AdditionalKey}",
        BehaviorCostKind.Additional => "additional cost unavailable",
        _ => "cost unavailable",
    };

    private static string Pluralize(int amount, string singular) =>
        $"{amount.ToString(CultureInfo.InvariantCulture)} {singular}{(amount == 1 ? string.Empty : "s")}";

    private static bool IsStableIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character));
}

/// <summary>
/// Typed metadata for the option represented by one engine-issued
/// <see cref="BehaviorChoiceParameters"/> action. Null metadata means the
/// catalog did not resolve the legacy choice; it is never reconstructed from
/// the action description.
/// </summary>
public sealed record Slice45BehaviorChoiceOptionViewModel(
    string ChoiceKey,
    string? OptionKey,
    BehaviorChoiceMode? Mode,
    BehaviorChoiceOptionSemantic? Semantic,
    IReadOnlyList<Slice45BehaviorCostViewModel> Costs)
{
    public string? EffectLabel { get; init; }
    public bool IsOptionalSkip => OptionKey is null && Mode == BehaviorChoiceMode.Optional;

    public string? SemanticLabel => IsOptionalSkip
        ? "FALLBACK · SKIP"
        : Semantic switch
        {
            BehaviorChoiceOptionSemantic.Normal => "NORMAL",
            BehaviorChoiceOptionSemantic.Fallback => "FALLBACK",
            _ => null,
        };

    public string? CostSummary => Costs.Count == 0
        ? null
        : string.Join(" + ", Costs.Select(static cost => cost.Summary));
}

/// <summary>
/// A detached action row for a prominent Slice 4.5 decision. All target and
/// option labels are derived from typed action/catalog fields. Description is
/// retained for diagnostics but is not used to infer semantics.
/// </summary>
public sealed record Slice45ActionPresentationViewModel(
    ActionId ActionId,
    ActionKind ActionKind,
    PlayerSide ActingPlayer,
    string? ChoiceKey,
    string? OptionKey,
    IReadOnlyList<string> VisibleCardIds,
    IReadOnlyList<Slice45BehaviorCostViewModel> Costs,
    BehaviorChoiceOptionSemantic? Semantic,
    bool IsOptionalSkip,
    string? TargetServerId,
    string Description,
    bool IsTyped)
{
    /// <summary>One-based position in the exact engine-issued option list.</summary>
    public int OptionOrder { get; init; }

    /// <summary>Typed physical target, when the issued action carries one.</summary>
    public string? TargetCardId { get; init; }

    /// <summary>Typed ICE-subroutine branch, when this is such an action.</summary>
    public IceSubroutineChoiceKind? IceChoice { get; init; }

    /// <summary>Total credit amount carried by an ICE choice.</summary>
    public int? CreditCost { get; init; }

    /// <summary>Hosted-credit amount carried by an ICE payment choice.</summary>
    public int? HostedCredits { get; init; }

    /// <summary>Typed destination carried by an ICE install choice.</summary>
    public InstallDestinationKind? InstallDestination { get; init; }

    public bool CreateNewRemote { get; init; }

    public bool IsFallback => IsOptionalSkip || Semantic == BehaviorChoiceOptionSemantic.Fallback;

    public bool IsTypedIceFallback => IceChoice is IceSubroutineChoiceKind.TakeTag
        or IceSubroutineChoiceKind.Decline;

    public string? SemanticLabel => IsOptionalSkip
        ? "FALLBACK · SKIP"
        : Semantic switch
        {
            BehaviorChoiceOptionSemantic.Normal => "NORMAL",
            BehaviorChoiceOptionSemantic.Fallback => "FALLBACK",
            _ when IsTypedIceFallback => "FALLBACK",
            _ => null,
        };

    public string? CostSummary
    {
        get
        {
            if (CreditCost is not { } totalCredits)
            {
                return Costs.Count == 0
                    ? null
                    : string.Join(" + ", Costs.Select(static cost => cost.Summary));
            }

            if (HostedCredits is not { } hostedCredits)
            {
                return Pluralize(totalCredits, "credit");
            }

            int personalCredits = totalCredits - hostedCredits;
            return $"{Pluralize(totalCredits, "credit")} "
                + $"(hosted {Pluralize(hostedCredits, "credit")} + "
                + $"personal {Pluralize(personalCredits, "credit")})";
        }
    }

    public string OptionLabel => OptionKey is not null
        ? $"OPTION · {OptionKey}"
        : IsOptionalSkip
            ? "OPTION · SKIP"
            : IceChoice switch
            {
                IceSubroutineChoiceKind.TrashInstalledRunnerCard => "OPTION · TRASH INSTALLED RUNNER CARD",
                IceSubroutineChoiceKind.InstallCard => "OPTION · INSTALL CARD",
                IceSubroutineChoiceKind.PayCredits => "OPTION · PAY CREDITS",
                IceSubroutineChoiceKind.TakeTag => "OPTION · TAKE TAG",
                IceSubroutineChoiceKind.JackOut => "OPTION · JACK OUT",
                IceSubroutineChoiceKind.ContinueRun => "OPTION · CONTINUE RUN",
                IceSubroutineChoiceKind.EndRun => "OPTION · END RUN",
                IceSubroutineChoiceKind.Decline => "OPTION · DECLINE",
                _ => ActionKind switch
                {
                    ActionKind.UseTrigger => "TRIGGER · USE",
                    ActionKind.DeclineTrigger => "TRIGGER · DECLINE",
                    ActionKind.PlayCard => "PLAY CARD",
                    _ => ActionKind.ToString().ToUpperInvariant(),
                },
            };

    /// <summary>
    /// Includes the one-based issued order only when a decision projector has
    /// supplied it. The older label remains stable for non-decision callers.
    /// </summary>
    public string OrderedPresentationLabel => OptionOrder > 0
        ? $"OPTION {OptionOrder.ToString(CultureInfo.InvariantCulture)} · {PresentationLabel}"
        : PresentationLabel;

    /// <summary>Stable renderer label assembled from typed fields only.</summary>
    public string PresentationLabel
    {
        get
        {
            List<string> parts = [OptionLabel];
            if (SemanticLabel is { } semantic)
            {
                parts.Add(semantic);
            }

            if (CostSummary is { } cost)
            {
                parts.Add($"COST · {cost}");
            }

            if (VisibleCardIds.Count > 0)
            {
                parts.Add($"CARDS · {string.Join(", ", VisibleCardIds)}");
            }

            if (TargetCardId is { } cardId
                && !VisibleCardIds.Contains(cardId, StringComparer.Ordinal))
            {
                parts.Add($"TARGET CARD · {cardId}");
            }

            if (TargetServerId is { } serverId)
            {
                parts.Add($"TARGET SERVER · {serverId}");
            }

            if (CreateNewRemote)
            {
                parts.Add("TARGET SERVER · NEW REMOTE");
            }

            if (InstallDestination is { } destination)
            {
                parts.Add($"DESTINATION · {destination.ToString().ToUpperInvariant()}");
            }

            return string.Join(" · ", parts);
        }
    }

    private static string Pluralize(int amount, string singular) =>
        $"{amount.ToString(CultureInfo.InvariantCulture)} {singular}{(amount == 1 ? string.Empty : "s")}";
}

/// <summary>Projection of the active approach/encounter choice for Match and Godot.</summary>
public sealed record Slice45DecisionViewModel(
    bool IsAvailable,
    string? Diagnostic,
    Slice45DecisionSurface Surface,
    DecisionKind? DecisionKind,
    PlayerSide? ActingPlayer,
    string? Prompt,
    IReadOnlyList<Slice45ActionPresentationViewModel> Options)
{
    public bool IsProminent => IsAvailable && Surface != Slice45DecisionSurface.None && Options.Count > 0;

    public static Slice45DecisionViewModel Unavailable(string? diagnostic = null) => new(
        false,
        diagnostic ?? "Slice 4.5 decision projection unavailable.",
        Slice45DecisionSurface.None,
        null,
        null,
        null,
        Array.Empty<Slice45ActionPresentationViewModel>());
}

/// <summary>
/// Builds the typed Slice 4.5 action/focus surface from Match view models.
/// This is the only place that knows how a behavior-choice catalog option is
/// projected; Godot receives the result and only renders it.
/// </summary>
public static class Slice45PresentationProjector
{
    public static Slice45DecisionViewModel Present(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        DecisionViewModel? decision = view.Decision;
        if (decision is null)
        {
            return Slice45DecisionViewModel.Unavailable(
                "Slice 4.5 decision projection unavailable: no decision is issued.");
        }

        if (!Enum.IsDefined(decision.Kind) || !Enum.IsDefined(decision.OwnerSide))
        {
            return Slice45DecisionViewModel.Unavailable(
                "Slice 4.5 decision projection unavailable: the issued decision enum is unsupported.");
        }

        Slice45DecisionSurface surface = DetermineSurface(view, decision);
        if (surface == Slice45DecisionSurface.None)
        {
            return new Slice45DecisionViewModel(
                false,
                null,
                surface,
                decision.Kind,
                decision.OwnerSide,
                decision.Prompt,
                Array.Empty<Slice45ActionPresentationViewModel>());
        }

        ActionKind[] allowed = decision.Kind switch
        {
            DecisionKind.BehaviorChoice => [ActionKind.ChooseBehavior],
            DecisionKind.TriggerChoice => [ActionKind.UseTrigger, ActionKind.DeclineTrigger],
            DecisionKind.IceSubroutineChoice => [ActionKind.ResolveIceSubroutine],
            _ => [],
        };
        if (allowed.Length == 0)
        {
            return Slice45DecisionViewModel.Unavailable(
                "Slice 4.5 decision projection unavailable: the decision has no typed choice actions.");
        }

        var options = new List<Slice45ActionPresentationViewModel>(view.LegalActions.Count);
        for (int index = 0; index < view.LegalActions.Count; index++)
        {
            LegalActionViewModel action = view.LegalActions[index];
            if (!Enum.IsDefined(action.Kind) || !allowed.Contains(action.Kind))
            {
                continue;
            }

            if (!TryProjectAction(
                    action,
                    decision.OwnerSide,
                    index + 1,
                    out Slice45ActionPresentationViewModel? projected)
                || projected is null)
            {
                return Slice45DecisionViewModel.Unavailable(
                    "Slice 4.5 decision projection unavailable: an issued choice payload is malformed.");
            }

            options.Add(projected);
        }

        if (options.Count == 0)
        {
            return Slice45DecisionViewModel.Unavailable(
                "Slice 4.5 decision projection unavailable: no valid engine-issued options are visible.");
        }

        return new Slice45DecisionViewModel(
            true,
            null,
            surface,
            decision.Kind,
            decision.OwnerSide,
            decision.Prompt,
            new ReadOnlyCollection<Slice45ActionPresentationViewModel>(options));
    }

    /// <summary>
    /// Resolves typed catalog metadata for a behavior-choice action. The
    /// method is public because ObservationViewMapper attaches the immutable
    /// result to each action before it crosses into Godot.
    /// </summary>
    public static Slice45BehaviorChoiceOptionViewModel? ResolveChoiceMetadata(
        ActionParameters parameters,
        IBehaviorCatalog behaviorCatalog)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        if (parameters is not BehaviorChoiceParameters choice
            || !IsStableIdentifier(choice.ChoiceKey)
            || choice.OptionKeys.Count > 1)
        {
            return null;
        }

        BehaviorChoiceSpec[] choices = FindChoiceSpecs(behaviorCatalog.GenericSpecs)
            .Where(candidate => string.Equals(candidate.Key, choice.ChoiceKey, StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        if (choices.Length != 1)
        {
            return null;
        }

        BehaviorChoiceSpec specification = choices[0];
        if (!Enum.IsDefined(specification.Mode))
        {
            return null;
        }
        BehaviorChoiceOption? option = choice.OptionKeys.Count == 1
            ? specification.Options.SingleOrDefault(candidate =>
                string.Equals(candidate.Key, choice.OptionKeys[0], StringComparison.Ordinal))
            : null;
        if (choice.OptionKeys.Count == 1 && option is null)
        {
            return null;
        }

        Slice45BehaviorCostViewModel[] costs = option?.Costs is { } optionCosts
            ? optionCosts
                .Select(TryMapBehaviorCost)
                .Where(static cost => cost is not null)
                .Cast<Slice45BehaviorCostViewModel>()
                .ToArray()
            : [];
        if (option?.Costs is { } && costs.Length != option.Costs.Count)
        {
            return null;
        }

        if (option?.Semantic is { } semantic && !Enum.IsDefined(semantic))
        {
            return null;
        }
        PlayerSide[] controllers = behaviorCatalog.GenericSpecs
            .Where(definition => FindChoiceSpecs([definition]).Contains(specification))
            .Select(definition => definition.Controller).Distinct().ToArray();
        return new Slice45BehaviorChoiceOptionViewModel(
            specification.Key,
            option?.Key,
            specification.Mode,
            option?.Semantic,
            new ReadOnlyCollection<Slice45BehaviorCostViewModel>(costs))
        {
            EffectLabel = option is not null && controllers.Length == 1
                ? BehaviorEffectLabels.Describe(option.Instructions, controllers[0]) : null,
        };
    }

    /// <summary>
    /// Reads a future-compatible optional cue envelope. Unknown, missing, or
    /// malformed values remain null; current observations therefore render no
    /// speculative restriction/bioroid/Mayfly state.
    /// </summary>
    public static Slice45CueViewModel ReadCues(IReadOnlyDictionary<string, object?> publicState)
    {
        ArgumentNullException.ThrowIfNull(publicState);
        if (!TryReadCueMap(publicState, out IReadOnlyDictionary<string, object?>? map)
            || map is null)
        {
            return Slice45CueViewModel.Unavailable;
        }

        if (!TryReadCueBool(map, "runRestrictionActive", "noStealOrTrashActive", out bool? restriction)
            || !TryReadCueBool(map, "bioroidAbilityReady", "bioroidAbilityLegal", out bool? bioroid)
            || !TryReadCueBool(map, "mayflyBreakArmed", "mayflyRunEndArmed", out bool? mayfly))
        {
            return Slice45CueViewModel.Unavailable;
        }

        return new Slice45CueViewModel(restriction, bioroid, mayfly);
    }

    /// <summary>Gets the typed server target of a run-event play action.</summary>
    public static string? GetTargetServerId(ActionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters is PlayCardParameters { TargetServerId: { } target }
            && ServerId.TryParse(target.Value, out ServerId parsed))
        {
            return parsed.Value;
        }

        return parameters is IceSubroutineChoiceParameters
        {
            Choice: IceSubroutineChoiceKind.InstallCard,
            ServerId: { } installTarget,
        }
            && ServerId.TryParse(installTarget.Value, out ServerId parsedInstallTarget)
            ? parsedInstallTarget.Value
            : null;
    }

    /// <summary>Gets a typed physical target without inspecting card text.</summary>
    public static string? GetTargetCardId(ActionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters switch
        {
            CardChoiceParameters value when CardInstanceId.TryParse(value.CardId.Value, out _) => value.CardId.Value,
            InstallCardParameters value when CardInstanceId.TryParse(value.CardId.Value, out _) => value.CardId.Value,
            RezCardParameters value when CardInstanceId.TryParse(value.CardId.Value, out _) => value.CardId.Value,
            RunBioroidAbilityParameters value when CardInstanceId.TryParse(value.IceId.Value, out _) => value.IceId.Value,
            RunIceParameters value when CardInstanceId.TryParse(value.IceId.Value, out _) => value.IceId.Value,
            RunBreakerParameters value when CardInstanceId.TryParse(value.BreakerId.Value, out _) => value.BreakerId.Value,
            RunBreakParameters value when CardInstanceId.TryParse(value.BreakerId.Value, out _) => value.BreakerId.Value,
            RunCardPaymentParameters value when CardInstanceId.TryParse(value.CardId.Value, out _) => value.CardId.Value,
            BreachCandidateParameters { CardId: { } cardId } when CardInstanceId.TryParse(cardId.Value, out _) => cardId.Value,
            IceSubroutineChoiceParameters { CardId: { } cardId } when CardInstanceId.TryParse(cardId.Value, out _) => cardId.Value,
            _ => null,
        };
    }

    /// <summary>
    /// Provides a safe action label for renderers that also display ordinary
    /// turn actions, including typed Jailbreak target-server actions.
    /// </summary>
    public static string Label(LegalActionViewModel action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Parameters is BehaviorChoiceParameters behavior
            && IsWellFormedBehaviorChoice(behavior))
        {
            Slice45BehaviorChoiceOptionViewModel? choice = action.Slice45BehaviorChoice;
            var presentation = new Slice45ActionPresentationViewModel(
                action.Id,
                action.Kind,
                PlayerSide.Runner,
                behavior.ChoiceKey,
                behavior.OptionKeys.Count == 1 ? behavior.OptionKeys[0] : null,
                behavior.CardIds.Select(static card => card.Value).ToArray(),
                choice?.Costs ?? Array.Empty<Slice45BehaviorCostViewModel>(),
                choice?.Semantic,
                choice?.IsOptionalSkip == true,
                null,
                action.Description,
                choice is not null);
            return presentation.PresentationLabel;
        }

        if (action.Parameters is IceSubroutineChoiceParameters iceChoice
            && TryProjectIceChoice(
                action,
                IceChoiceOwner(iceChoice.Choice),
                0,
                iceChoice,
                out Slice45ActionPresentationViewModel? icePresentation)
            && icePresentation is not null)
        {
            return icePresentation.PresentationLabel;
        }

        string? targetServerId = GetTargetServerId(action.Parameters);
        if (targetServerId is null)
        {
            if (action.Parameters is RunBioroidAbilityParameters bioroid
                && TryDescribeBioroid(bioroid, out string? bioroidLabel)
                && bioroidLabel is not null)
            {
                return bioroidLabel;
            }

            return $"{action.Kind}: {action.Description}";
        }

        return $"{action.Kind.ToString().ToUpperInvariant()} · TARGET SERVER · {targetServerId}";
    }

    private static bool TryProjectAction(
        LegalActionViewModel action,
        PlayerSide actingPlayer,
        int optionOrder,
        out Slice45ActionPresentationViewModel? projected)
    {
        projected = null;
        switch (action.Parameters)
        {
            case BehaviorChoiceParameters behavior:
                if (action.Kind != ActionKind.ChooseBehavior
                    || !IsWellFormedBehaviorChoice(behavior))
                {
                    return false;
                }

                Slice45BehaviorChoiceOptionViewModel? choice = action.Slice45BehaviorChoice;
                if (choice is null)
                {
                    return false;
                }

                if (behavior.OptionKeys.Count == 1
                    && (choice is null
                        || !string.Equals(choice.OptionKey, behavior.OptionKeys[0], StringComparison.Ordinal)))
                {
                    // A catalog omission is compatible with old choices, but
                    // a mismatched typed metadata record is unsafe to render.
                    if (choice is not null)
                    {
                        return false;
                    }
                }

                projected = new Slice45ActionPresentationViewModel(
                    action.Id,
                    action.Kind,
                    actingPlayer,
                    behavior.ChoiceKey,
                    behavior.OptionKeys.Count == 1 ? behavior.OptionKeys[0] : null,
                    new ReadOnlyCollection<string>(behavior.CardIds.Select(static card => card.Value).ToArray()),
                    choice?.Costs ?? Array.Empty<Slice45BehaviorCostViewModel>(),
                    choice?.Semantic,
                    choice?.IsOptionalSkip == true,
                    null,
                    action.Description,
                    true)
                {
                    OptionOrder = optionOrder,
                };
                return true;

            case IceSubroutineChoiceParameters iceChoice
                when action.Kind == ActionKind.ResolveIceSubroutine:
                return TryProjectIceChoice(
                    action,
                    actingPlayer,
                    optionOrder,
                    iceChoice,
                    out projected);

            case TriggerChoiceParameters trigger
                when action.Kind is ActionKind.UseTrigger or ActionKind.DeclineTrigger:
                if (!TriggerId.TryParse(trigger.TriggerId.Value, out _))
                {
                    return false;
                }

                projected = new Slice45ActionPresentationViewModel(
                    action.Id,
                    action.Kind,
                    actingPlayer,
                    null,
                    null,
                    Array.Empty<string>(),
                    Array.Empty<Slice45BehaviorCostViewModel>(),
                    null,
                    false,
                    null,
                    action.Description,
                    true)
                {
                    OptionOrder = optionOrder,
                    Semantic = action.Kind == ActionKind.DeclineTrigger
                        ? BehaviorChoiceOptionSemantic.Fallback
                        : null,
                };
                return true;

            default:
                return false;
        }
    }

    private static Slice45DecisionSurface DetermineSurface(
        MatchViewModel view,
        DecisionViewModel decision)
    {
        if (decision.Kind is not (DecisionKind.BehaviorChoice
            or DecisionKind.TriggerChoice
            or DecisionKind.IceSubroutineChoice)
            || view.Run is not { } run)
        {
            return Slice45DecisionSurface.None;
        }

        if (run.Encounter is not null
            || run.Phase == RunCrPhase.Encounter
            || run.Step is RunTimingStep.EncounterInitialization
                or RunTimingStep.EncounterPaidWindow
                or RunTimingStep.Encounter)
        {
            return Slice45DecisionSurface.Encounter;
        }

        // ServerApproached is intentionally represented by the Success cursor
        // while its mandatory/optional occurrence is resolved. The
        // unsuccessful guard keeps a RunSuccessful draw reaction out of the
        // approach panel.
        if (!run.Successful
            && (run.Phase == RunCrPhase.Approach
                || run.Step is RunTimingStep.Approach
                    or RunTimingStep.ApproachPaidWindow
                    or RunTimingStep.ApproachServer
                    or RunTimingStep.Success))
        {
            return Slice45DecisionSurface.Approach;
        }

        return Slice45DecisionSurface.None;
    }

    private static bool IsWellFormedBehaviorChoice(BehaviorChoiceParameters choice)
    {
        if (!IsStableIdentifier(choice.ChoiceKey)
            || choice.OptionKeys.Count > 1
            || choice.OptionKeys.Any(option => !IsStableIdentifier(option))
            || choice.OptionKeys.Distinct(StringComparer.Ordinal).Count() != choice.OptionKeys.Count)
        {
            return false;
        }

        return choice.CardIds.All(card => CardInstanceId.TryParse(card.Value, out _));
    }

    private static Slice45BehaviorCostViewModel? TryMapBehaviorCost(BehaviorCost cost)
    {
        if (!Enum.IsDefined(cost.Kind)
            || (cost.Kind == BehaviorCostKind.Credits && cost.Amount < 0)
            || (cost.Kind != BehaviorCostKind.Credits && cost.Amount <= 0))
        {
            return null;
        }

        if (cost.Kind == BehaviorCostKind.Counter && !IsStableIdentifier(cost.CounterName))
        {
            return null;
        }

        if (cost.Kind == BehaviorCostKind.Additional
            && cost.AdditionalKind is null
            && !IsStableIdentifier(cost.AdditionalKey))
        {
            return null;
        }

        if (cost.AdditionalKind is { } additionalKind && !Enum.IsDefined(additionalKind))
        {
            return null;
        }

        return new Slice45BehaviorCostViewModel(
            cost.Kind,
            cost.Amount,
            cost.AdditionalKind,
            cost.AdditionalKey)
        {
            CounterName = cost.CounterName,
            AdditionalTarget = cost.AdditionalTarget,
        };
    }

    private static bool TryProjectIceChoice(
        LegalActionViewModel action,
        PlayerSide actingPlayer,
        int optionOrder,
        IceSubroutineChoiceParameters choice,
        out Slice45ActionPresentationViewModel? projected)
    {
        projected = null;
        if (action.Kind != ActionKind.ResolveIceSubroutine
            || !IsStableIdentifier(choice.FrameIdentity)
            || !Enum.IsDefined(choice.Choice)
            || actingPlayer != IceChoiceOwner(choice.Choice)
            || choice.CreditCost < 0
            || choice.HostedCredits < 0
            || choice.HostedCredits > choice.CreditCost)
        {
            return false;
        }

        string? targetCardId = choice.CardId is { } cardId
            && CardInstanceId.TryParse(cardId.Value, out _)
            ? cardId.Value
            : null;
        if (choice.CardId is not null && targetCardId is null)
        {
            return false;
        }

        string? targetServerId = null;
        if (choice.ServerId is { } serverId)
        {
            if (!ServerId.TryParse(serverId.Value, out ServerId parsedServer))
            {
                return false;
            }

            targetServerId = parsedServer.Value;
        }

        switch (choice.Choice)
        {
            case IceSubroutineChoiceKind.TrashInstalledRunnerCard:
                if (targetCardId is null
                    || choice.DestinationKind is not null
                    || targetServerId is not null
                    || choice.CreateNewRemote
                    || choice.CreditCost != 0
                    || choice.HostedCredits != 0)
                {
                    return false;
                }

                break;
            case IceSubroutineChoiceKind.InstallCard:
                if (targetCardId is null
                    || choice.DestinationKind is not { } destination
                    || !Enum.IsDefined(destination)
                    || choice.HostedCredits != 0
                    || (destination == InstallDestinationKind.RunnerRig
                        && (targetServerId is not null || choice.CreateNewRemote))
                    || (destination is InstallDestinationKind.ServerRoot or InstallDestinationKind.ServerIce
                        && (targetServerId is not null) == choice.CreateNewRemote))
                {
                    return false;
                }

                break;
            case IceSubroutineChoiceKind.PayCredits:
                if (choice.CardId is not null
                    || choice.DestinationKind is not null
                    || targetServerId is not null
                    || choice.CreateNewRemote
                    || choice.CreditCost <= 0)
                {
                    return false;
                }

                break;
            case IceSubroutineChoiceKind.TakeTag:
            case IceSubroutineChoiceKind.Decline:
            case IceSubroutineChoiceKind.JackOut:
            case IceSubroutineChoiceKind.ContinueRun:
            case IceSubroutineChoiceKind.EndRun:
                if (choice.CardId is not null
                    || choice.DestinationKind is not null
                    || targetServerId is not null
                    || choice.CreateNewRemote
                    || choice.CreditCost != 0
                    || choice.HostedCredits != 0)
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        Slice45BehaviorCostViewModel[] costs = choice.Choice switch
        {
            IceSubroutineChoiceKind.PayCredits when choice.HostedCredits > 0 =>
            [
                new Slice45BehaviorCostViewModel(
                    BehaviorCostKind.Credits,
                    choice.CreditCost - choice.HostedCredits,
                    null,
                    null),
                new Slice45BehaviorCostViewModel(
                    BehaviorCostKind.HostedCredits,
                    choice.HostedCredits,
                    null,
                    null),
            ],
            IceSubroutineChoiceKind.PayCredits =>
            [new Slice45BehaviorCostViewModel(
                BehaviorCostKind.Credits,
                choice.CreditCost,
                null,
                null)],
            IceSubroutineChoiceKind.InstallCard when choice.CreditCost > 0 =>
            [new Slice45BehaviorCostViewModel(
                BehaviorCostKind.Credits,
                choice.CreditCost,
                null,
                null)],
            IceSubroutineChoiceKind.InstallCard =>
            [new Slice45BehaviorCostViewModel(BehaviorCostKind.Credits, 0, null, null)],
            _ => [],
        };

        projected = new Slice45ActionPresentationViewModel(
            action.Id,
            action.Kind,
            actingPlayer,
            null,
            null,
            Array.Empty<string>(),
            new ReadOnlyCollection<Slice45BehaviorCostViewModel>(costs),
            choice.Choice is IceSubroutineChoiceKind.TakeTag or IceSubroutineChoiceKind.Decline
                ? BehaviorChoiceOptionSemantic.Fallback
                : BehaviorChoiceOptionSemantic.Normal,
            false,
            targetServerId,
            action.Description,
            true)
        {
            OptionOrder = optionOrder,
            TargetCardId = targetCardId,
            IceChoice = choice.Choice,
            CreditCost = choice.Choice is IceSubroutineChoiceKind.PayCredits or IceSubroutineChoiceKind.InstallCard
                ? choice.CreditCost
                : null,
            HostedCredits = choice.Choice == IceSubroutineChoiceKind.PayCredits
                ? choice.HostedCredits
                : null,
            InstallDestination = choice.DestinationKind,
            CreateNewRemote = choice.CreateNewRemote,
        };
        return true;
    }

    private static PlayerSide IceChoiceOwner(IceSubroutineChoiceKind choice) => choice switch
    {
        IceSubroutineChoiceKind.PayCredits
            or IceSubroutineChoiceKind.TakeTag
            or IceSubroutineChoiceKind.JackOut
            or IceSubroutineChoiceKind.ContinueRun => PlayerSide.Runner,
        IceSubroutineChoiceKind.TrashInstalledRunnerCard
            or IceSubroutineChoiceKind.InstallCard
            or IceSubroutineChoiceKind.EndRun
            or IceSubroutineChoiceKind.Decline => PlayerSide.Corp,
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };

    private static bool TryDescribeBioroid(
        RunBioroidAbilityParameters bioroid,
        out string? label)
    {
        label = null;
        if (!CardInstanceId.TryParse(bioroid.IceId.Value, out _)
            || !SubroutineId.TryParse(bioroid.SubroutineId.Value, out _)
            || !IsStableIdentifier(bioroid.AbilityKey)
            || bioroid.Cost is null
            || bioroid.Cost.Kind != BehaviorCostKind.LoseClick
            || bioroid.Cost.Amount != 1)
        {
            return false;
        }

        label = $"USE BIOROID · BREAK SUBROUTINE {((long)bioroid.SubroutineOrdinal + 1).ToString(CultureInfo.InvariantCulture)} · LOSE "
            + $"{bioroid.Cost.Amount.ToString(CultureInfo.InvariantCulture)} CLICK";
        return true;
    }

    private static bool IsStableIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character));

    private static BehaviorChoiceSpec[] FindChoiceSpecs(IEnumerable<CardBehaviorDefinition> definitions)
    {
        var choices = new List<BehaviorChoiceSpec>();
        foreach (CardBehaviorDefinition definition in definitions)
        {
            AddChoices(definition.Instructions, choices);
            foreach (BehaviorTrigger trigger in definition.Triggers)
            {
                AddChoices(trigger.Instructions, choices);
            }

            foreach (BehaviorPaidAbility ability in definition.PaidAbilities)
            {
                AddChoices(ability.Instructions, choices);
            }
        }

        return choices.ToArray();
    }

    private static void AddChoices(
        IReadOnlyList<BehaviorInstruction> instructions,
        List<BehaviorChoiceSpec> choices)
    {
        foreach (BehaviorInstruction instruction in instructions)
        {
            if (instruction.Choice is { } choice)
            {
                choices.Add(choice);
                foreach (BehaviorChoiceOption option in choice.Options)
                {
                    AddChoices(option.Instructions, choices);
                }
            }

            if (instruction.Branch is { } branch)
            {
                AddChoices(branch.WhenTrue, choices);
                AddChoices(branch.WhenFalse, choices);
            }
        }
    }

    private static bool TryReadMap(
        object? value,
        out IReadOnlyDictionary<string, object?>? map)
    {
        if (value is IReadOnlyDictionary<string, object?> dictionary)
        {
            map = dictionary;
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            try
            {
                map = JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText());
                return map is not null;
            }
            catch (JsonException)
            {
                // Optional UI cues are not authoritative; malformed values
                // simply remain unavailable.
            }
        }

        map = null;
        return false;
    }

    private static bool TryReadCueMap(
        IReadOnlyDictionary<string, object?> publicState,
        out IReadOnlyDictionary<string, object?>? map)
    {
        if (publicState.TryGetValue("slice45Cues", out object? raw))
        {
            if (TryReadMap(raw, out map) && map is not null)
            {
                return true;
            }

            map = null;
            return false;
        }

        // Core may attach the same public envelope to the live run so a
        // run-only observation can carry the cue without duplicating a
        // top-level key. This remains an observation field, never a value
        // inferred from RunState or card metadata.
        if (publicState.TryGetValue("run", out object? rawRun)
            && TryReadMap(rawRun, out IReadOnlyDictionary<string, object?>? runMap)
            && runMap is not null
            && runMap.TryGetValue("slice45Cues", out raw))
        {
            if (TryReadMap(raw, out map) && map is not null)
            {
                return true;
            }

            map = null;
            return false;
        }

        // Accept the explicitly named booleans directly on publicState for
        // forward-compatible Core observations, but never treat unrelated
        // public keys as a cue envelope.
        if (HasCueKey(publicState))
        {
            map = publicState;
            return true;
        }

        map = null;
        return false;
    }

    private static bool HasCueKey(IReadOnlyDictionary<string, object?> map) =>
        map.Keys.Any(static key => key is "runRestrictionActive"
            or "noStealOrTrashActive"
            or "bioroidAbilityReady"
            or "bioroidAbilityLegal"
            or "mayflyBreakArmed"
            or "mayflyRunEndArmed");

    private static bool TryReadCueBool(
        IReadOnlyDictionary<string, object?> map,
        string key,
        string alias,
        out bool? value)
    {
        if (map.ContainsKey(key))
        {
            return TryReadNullableBool(map, key, out value);
        }

        return TryReadNullableBool(map, alias, out value);
    }

    private static bool TryReadNullableBool(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out bool? value)
    {
        if (!map.TryGetValue(key, out object? raw) || raw is null)
        {
            value = null;
            return true;
        }

        if (raw is bool boolean)
        {
            value = boolean;
            return true;
        }

        if (raw is JsonElement element
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }

        value = null;
        return false;
    }
}
