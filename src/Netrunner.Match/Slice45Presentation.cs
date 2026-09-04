using System.Collections.ObjectModel;
using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Optional typed run cues reserved for Slice 4.5. The current Core
/// observation does not publish these values, so each field is nullable and
/// remains null until an observation explicitly carries the corresponding
/// typed value.
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
    public string Summary => Kind switch
    {
        BehaviorCostKind.Credits => Pluralize(Amount, "credit"),
        BehaviorCostKind.Clicks => Pluralize(Amount, "click"),
        BehaviorCostKind.Additional when AdditionalKind == BehaviorAdditionalCostKind.TrashCards =>
            $"{Pluralize(Amount, "private card")} to trash",
        BehaviorCostKind.Additional => Pluralize(Amount, "additional cost"),
        _ => "cost unavailable",
    };

    private static string Pluralize(int amount, string singular) =>
        $"{amount} {singular}{(amount == 1 ? string.Empty : "s")}";
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
    public bool IsFallback => IsOptionalSkip || Semantic == BehaviorChoiceOptionSemantic.Fallback;

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

    public string OptionLabel => OptionKey is not null
        ? $"OPTION · {OptionKey}"
        : IsOptionalSkip
            ? "OPTION · SKIP"
            : ActionKind switch
            {
                ActionKind.UseTrigger => "TRIGGER · USE",
                ActionKind.DeclineTrigger => "TRIGGER · DECLINE",
                ActionKind.PlayCard => "PLAY CARD",
                _ => ActionKind.ToString().ToUpperInvariant(),
            };

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

            if (TargetServerId is { } serverId)
            {
                parts.Add($"TARGET SERVER · {serverId}");
            }

            return string.Join(" · ", parts);
        }
    }
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
            _ => [],
        };
        if (allowed.Length == 0)
        {
            return Slice45DecisionViewModel.Unavailable(
                "Slice 4.5 decision projection unavailable: the decision has no typed choice actions.");
        }

        var options = new List<Slice45ActionPresentationViewModel>(view.LegalActions.Count);
        foreach (LegalActionViewModel action in view.LegalActions)
        {
            if (!Enum.IsDefined(action.Kind) || !allowed.Contains(action.Kind))
            {
                continue;
            }

            if (!TryProjectAction(action, decision.OwnerSide, out Slice45ActionPresentationViewModel? projected)
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

        BehaviorChoiceSpec[] choices = FindChoiceSpecs(behaviorCatalog)
            .Where(candidate => string.Equals(candidate.Key, choice.ChoiceKey, StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        if (choices.Length != 1)
        {
            return null;
        }

        BehaviorChoiceSpec specification = choices[0];
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
                .Select(static cost => new Slice45BehaviorCostViewModel(
                    cost.Kind,
                    cost.Amount,
                    cost.AdditionalKind,
                    cost.AdditionalKey))
                .ToArray()
            : [];
        return new Slice45BehaviorChoiceOptionViewModel(
            specification.Key,
            option?.Key,
            specification.Mode,
            option?.Semantic,
            new ReadOnlyCollection<Slice45BehaviorCostViewModel>(costs));
    }

    /// <summary>
    /// Reads a future-compatible optional cue envelope. Unknown, missing, or
    /// malformed values remain null; current observations therefore render no
    /// speculative restriction/bioroid/Mayfly state.
    /// </summary>
    public static Slice45CueViewModel ReadCues(IReadOnlyDictionary<string, object?> publicState)
    {
        ArgumentNullException.ThrowIfNull(publicState);
        if (!publicState.TryGetValue("slice45Cues", out object? raw)
            || !TryReadMap(raw, out IReadOnlyDictionary<string, object?>? map)
            || map is null)
        {
            return Slice45CueViewModel.Unavailable;
        }

        return new Slice45CueViewModel(
            ReadNullableBool(map, "runRestrictionActive"),
            ReadNullableBool(map, "bioroidAbilityReady"),
            ReadNullableBool(map, "mayflyBreakArmed"));
    }

    /// <summary>Gets the typed server target of a run-event play action.</summary>
    public static string? GetTargetServerId(ActionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters is PlayCardParameters { TargetServerId: { } target }
            && ServerId.TryParse(target.Value, out ServerId parsed)
            ? parsed.Value
            : null;
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

        string? targetServerId = GetTargetServerId(action.Parameters);
        if (targetServerId is null)
        {
            if (action.Parameters is RunBioroidAbilityParameters bioroid)
            {
                return $"USE BIOROID · BREAK {bioroid.SubroutineId.Value} · LOSE {bioroid.ClickCost} CLICK";
            }

            return $"{action.Kind}: {action.Description}";
        }

        return $"{action.Kind.ToString().ToUpperInvariant()} · TARGET SERVER · {targetServerId}";
    }

    private static bool TryProjectAction(
        LegalActionViewModel action,
        PlayerSide actingPlayer,
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
                    choice is not null);
                return true;

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
                    true);
                return true;

            default:
                return false;
        }
    }

    private static Slice45DecisionSurface DetermineSurface(
        MatchViewModel view,
        DecisionViewModel decision)
    {
        if (decision.Kind is not (DecisionKind.BehaviorChoice or DecisionKind.TriggerChoice)
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

    private static bool IsStableIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character));

    private static BehaviorChoiceSpec[] FindChoiceSpecs(IBehaviorCatalog catalog)
    {
        var choices = new List<BehaviorChoiceSpec>();
        foreach (CardBehaviorDefinition definition in catalog.GenericSpecs)
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

    private static bool? ReadNullableBool(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        if (value is bool boolean)
        {
            return boolean;
        }

        if (value is JsonElement element
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        return null;
    }
}
