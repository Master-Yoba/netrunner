using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Viewer-safe standard-tag state for one player.</summary>
public sealed record TagStateViewModel(PlayerSide Side, int? Count)
{
    public bool IsKnown => Count.HasValue;

    public bool IsTagged => Count is > 0;

    public string Label => IsTagged ? "TAGGED" : "CLEAR";

    public string Summary => Count is { } count
        ? $@"TAGS · {count.ToString(CultureInfo.InvariantCulture)} · {Label}"
        : "TAGS · unavailable";
}

/// <summary>
/// Damage data copied from Core's pending instruction or resolved event.
/// Match never estimates damage, discard count, or flatline risk.
/// </summary>
public sealed record DamagePresentationViewModel(
    DamageKind Kind,
    int Amount,
    string TargetId,
    string? SourceCardId = null,
    bool? FlatlineRisk = null,
    bool Flatlined = false,
    int CardsTrashed = 0,
    int? CoreDamageAfter = null,
    int? TrashedCardPrintedCost = null,
    bool? ConditionalEndRun = null)
{
    public bool IsFlatlineRisk => FlatlineRisk == true || Flatlined;

    public bool IsFlatlineRiskKnown => FlatlineRisk.HasValue || Flatlined;

    public bool IsResolved => Flatlined || CardsTrashed > 0 || CoreDamageAfter.HasValue;

    public string Summary
    {
        get
        {
            string source = SourceCardId is { Length: > 0 }
                ? $@" · SOURCE {SourceCardId}"
                : string.Empty;
            string target = TargetId.Length == 0 ? "UNKNOWN TARGET" : TargetId;
            string risk = !IsFlatlineRiskKnown
                ? " · FLATLINE RISK UNAVAILABLE"
                : IsFlatlineRisk
                    ? " · FLATLINE RISK"
                    : " · NO FLATLINE";
            string result = CardsTrashed > 0
                ? $@" · {CardsTrashed.ToString(CultureInfo.InvariantCulture)} CARD(S) TRASHED"
                : string.Empty;
            string core = CoreDamageAfter is { } after
                ? $@" · CORE DAMAGE {after.ToString(CultureInfo.InvariantCulture)}"
                : string.Empty;
            string printedCost = TrashedCardPrintedCost is { } cost
                ? $@" · TRASHED CARD PRINTED COST {cost.ToString(CultureInfo.InvariantCulture)}"
                : string.Empty;
            string conditionalEndRun = ConditionalEndRun switch
            {
                true => " · CONDITIONAL END RUN",
                false => " · CONDITIONAL CONTINUE RUN",
                _ => string.Empty,
            };
            string flatline = Flatlined ? " · FLATLINED" : string.Empty;
            return $@"DAMAGE · {Kind.ToString().ToUpperInvariant()} · {Amount.ToString(CultureInfo.InvariantCulture)}"
                + $@" · TARGET {target}{source}{risk}{result}{core}{printedCost}{conditionalEndRun}{flatline}";
        }
    }

    public static DamagePresentationViewModel FromEvent(DamageDealtEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new DamagePresentationViewModel(
            payload.Kind,
            payload.Amount,
            payload.Target.Value,
            payload.SourceCardId?.Value,
            payload.FlatlineRisk,
            payload.Flatlined,
            payload.CardsTrashed,
            payload.CoreDamageAfter,
            payload.TrashedCardPrintedCost,
            payload.ConditionalEndRun);
    }
}

/// <summary>
/// Match-local display projection of one Core conditional value quote. The
/// quoted terms remain typed so Godot can render them without recalculating.
/// </summary>
public sealed record ConditionalValueViewModel(
    string Key,
    BehaviorValueKind ValueKind,
    int BaseValue,
    int EffectiveValue,
    IReadOnlyList<ValueCalculationTermPresentationViewModel> Terms)
{
    public bool IsModified => BaseValue != EffectiveValue || Terms.Count > 0;

    public string Summary
    {
        get
        {
            string terms = Terms.Count == 0
                ? string.Empty
                : $@" · {string.Join(" ", Terms.Select(static term => term.Summary))}";
            return $@"{Key.ToUpperInvariant()} · {ValueKind.ToString().ToUpperInvariant()} · "
                + $@"{BaseValue.ToString(CultureInfo.InvariantCulture)} → "
                + $@"{EffectiveValue.ToString(CultureInfo.InvariantCulture)}{terms}";
        }
    }

    public static ConditionalValueViewModel From(string key, ValueQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ValueCalculationTermPresentationViewModel[] terms = quote.Terms
            .Select(static term => new ValueCalculationTermPresentationViewModel(
                term.Source,
                term.Operation,
                term.Delta,
                term.ReasonKey,
                term.SourceDefinitionId,
                term.SourceCardId,
                term.Quantity,
                term.UnitDelta,
                term.VariableKey))
            .ToArray();
        return new ConditionalValueViewModel(
            key,
            quote.ValueKind,
            quote.BaseValue,
            quote.EffectiveValue,
            new ReadOnlyCollection<ValueCalculationTermPresentationViewModel>(terms));
    }
}

/// <summary>Explicitly marks an action as an unless payment/fallback option.</summary>
public sealed record Slice46ActionPresentationViewModel(
    Slice45ActionPresentationViewModel Action,
    bool IsUnlessChoice,
    bool IsPayAlternative,
    bool IsFallback)
{
    public ActionId ActionId => Action.ActionId;

    public ActionKind ActionKind => Action.ActionKind;

    public string? ChoiceKey => Action.ChoiceKey;

    public string? OptionKey => Action.OptionKey;

    public IceSubroutineChoiceKind? IceChoice => Action.IceChoice;

    public bool IsTyped => Action.IsTyped;

    public string Description => Action.Description;

    public string? TargetCardId => Action.TargetCardId;

    public string? TargetServerId => Action.TargetServerId;

    public IReadOnlyList<Slice45BehaviorCostViewModel> Costs => Action.Costs;

    public string? CostSummary => Action.CostSummary;

    public int OptionOrder => Action.OptionOrder;

    public string PresentationLabel
    {
        get
        {
            if (!IsUnlessChoice)
            {
                return Action.PresentationLabel;
            }

            string marker = IsPayAlternative
                ? "UNLESS PAY"
                : IsFallback
                    ? "UNLESS FALLBACK"
                    : "UNLESS OPTION";
            return $@"{marker} · {Action.PresentationLabel}";
        }
    }

    public string OrderedPresentationLabel => OptionOrder > 0
        ? $@"OPTION {OptionOrder.ToString(CultureInfo.InvariantCulture)} · {PresentationLabel}"
        : PresentationLabel;
}

/// <summary>Typed Slice 4.6 choice surface used by Match and Godot.</summary>
public sealed record Slice46DecisionViewModel(
    bool IsAvailable,
    string? Diagnostic,
    Slice45DecisionSurface Surface,
    DecisionKind? DecisionKind,
    PlayerSide? ActingPlayer,
    string? Prompt,
    IReadOnlyList<Slice46ActionPresentationViewModel> Options,
    bool JackOutAfterDamage)
{
    public bool IsProminent => IsAvailable && Options.Count > 0;

    public bool HasUnlessChoice => Options.Any(static option => option.IsUnlessChoice);

    public static Slice46DecisionViewModel Unavailable(string? diagnostic = null) => new(
        false,
        diagnostic ?? "Slice 4.6 decision projection unavailable.",
        Slice45DecisionSurface.None,
        null,
        null,
        null,
        Array.Empty<Slice46ActionPresentationViewModel>(),
        false);
}

/// <summary>
/// Projects the explicit Slice 4.6 observation fields. The pending damage
/// field is one object or null; conditional values are one typed array.
/// </summary>
public static class Slice46ObservationProjector
{
    public static IReadOnlyList<DamagePresentationViewModel> ReadDamageInstructions(
        IReadOnlyDictionary<string, object?> publicState)
    {
        ArgumentNullException.ThrowIfNull(publicState);
        if (!publicState.TryGetValue("pendingDamage", out object? raw)
            || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return Array.Empty<DamagePresentationViewModel>();
        }

        return TryReadDamage(raw, out DamagePresentationViewModel? damage)
            && damage is not null
            ? new ReadOnlyCollection<DamagePresentationViewModel>([damage])
            : Array.Empty<DamagePresentationViewModel>();
    }

    public static IReadOnlyList<ConditionalValueViewModel> ReadConditionalValues(
        IReadOnlyDictionary<string, object?> publicState)
    {
        ArgumentNullException.ThrowIfNull(publicState);
        if (!publicState.TryGetValue("conditionalValues", out object? raw)
            || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return Array.Empty<ConditionalValueViewModel>();
        }

        IReadOnlyList<object?>? entries = ReadArray(raw);
        if (entries is null)
        {
            return Array.Empty<ConditionalValueViewModel>();
        }

        var values = new List<ConditionalValueViewModel>(entries.Count);
        foreach (object? entry in entries)
        {
            if (TryReadConditionalValue(entry, out ConditionalValueViewModel? value)
                && value is not null)
            {
                values.Add(value);
            }
        }

        return new ReadOnlyCollection<ConditionalValueViewModel>(values);
    }

    private static bool TryReadDamage(
        object? raw,
        out DamagePresentationViewModel? damage)
    {
        damage = null;
        IReadOnlyDictionary<string, object?>? map = ReadObject(raw);
        if (map is null
            || !TryReadEnum(map, "kind", out DamageKind kind)
            || !TryReadInt(map, "amount", out int amount)
            || !TryReadString(map, "targetId", out string targetId))
        {
            return false;
        }

        string? sourceCardId = ReadNullableString(map, "sourceCardId");
        bool? flatlineRisk = ReadNullableBool(map, "flatlineRisk");
        damage = new DamagePresentationViewModel(
            kind,
            amount,
            targetId,
            sourceCardId,
            flatlineRisk);
        return true;
    }

    private static bool TryReadConditionalValue(
        object? raw,
        out ConditionalValueViewModel? value)
    {
        value = null;
        IReadOnlyDictionary<string, object?>? map = ReadObject(raw);
        if (map is null
            || !TryReadString(map, "key", out string key)
            || !TryReadEnum(map, "valueKind", out BehaviorValueKind valueKind)
            || !TryReadInt(map, "baseValue", out int baseValue)
            || !TryReadInt(map, "effectiveValue", out int effectiveValue)
            || !TryReadTerms(map, out ValueCalculationTermPresentationViewModel[] terms))
        {
            return false;
        }

        value = new ConditionalValueViewModel(
            key,
            valueKind,
            baseValue,
            effectiveValue,
            new ReadOnlyCollection<ValueCalculationTermPresentationViewModel>(terms));
        return true;
    }

    private static bool TryReadTerms(
        IReadOnlyDictionary<string, object?> map,
        out ValueCalculationTermPresentationViewModel[] terms)
    {
        terms = [];
        if (!map.TryGetValue("terms", out object? raw)
            || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return true;
        }

        IReadOnlyList<object?>? entries = ReadArray(raw);
        if (entries is null)
        {
            return false;
        }

        var parsed = new List<ValueCalculationTermPresentationViewModel>(entries.Count);
        foreach (object? entry in entries)
        {
            IReadOnlyDictionary<string, object?>? term = ReadObject(entry);
            if (term is null
                || !TryReadEnum(term, "source", out BehaviorValueSourceKind source)
                || !TryReadEnum(term, "operation", out BehaviorValueOperation operation)
                || !TryReadInt(term, "delta", out int delta)
                || !TryReadString(term, "reasonKey", out string reasonKey))
            {
                return false;
            }

            CardDefinitionId? definition = ReadNullableString(term, "sourceDefinitionId") is { } definitionId
                ? new CardDefinitionId(definitionId)
                : null;
            CardInstanceId? card = ReadNullableString(term, "sourceCardId") is { } cardId
                ? new CardInstanceId(cardId)
                : null;
            parsed.Add(new ValueCalculationTermPresentationViewModel(
                source,
                operation,
                delta,
                reasonKey,
                definition,
                card,
                ReadNullableInt(term, "quantity"),
                ReadNullableInt(term, "unitDelta"),
                ReadNullableString(term, "variableKey")));
        }

        terms = parsed.ToArray();
        return true;
    }

    private static IReadOnlyDictionary<string, object?>? ReadObject(object? raw)
    {
        if (raw is IReadOnlyDictionary<string, object?> map)
        {
            return map;
        }

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            return element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
        }

        return null;
    }

    private static IReadOnlyList<object?>? ReadArray(object raw)
    {
        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(item => (object?)item.Clone())
                .ToArray();
        }

        if (raw is IReadOnlyList<object?> list)
        {
            return list;
        }

        if (raw is IEnumerable<object?> values)
        {
            return values.ToArray();
        }

        return null;
    }

    private static bool TryReadString(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out string value)
    {
        value = ReadNullableString(map, key) ?? string.Empty;
        return value.Length > 0;
    }

    private static string? ReadNullableString(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? raw)
            || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return null;
        }

        if (raw is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        }

        return raw is string text
            ? text
            : raw is CardInstanceId cardId
                ? cardId.Value
                : raw is CardDefinitionId definitionId
                    ? definitionId.Value
                    : null;
    }

    private static bool TryReadInt(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out int value)
    {
        int? parsed = ReadNullableInt(map, key);
        value = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    private static int? ReadNullableInt(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? raw)
            || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return null;
        }

        if (raw is int integer)
        {
            return integer;
        }

        if (raw is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
        {
            return (int)longValue;
        }

        return raw is JsonElement element
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out int jsonValue)
            ? jsonValue
            : null;
    }

    private static bool? ReadNullableBool(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? raw)
            || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return null;
        }

        if (raw is bool boolean)
        {
            return boolean;
        }

        return raw is JsonElement element
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;
    }

    private static bool TryReadEnum<TEnum>(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out TEnum value)
        where TEnum : struct, Enum
    {
        object? raw = map.TryGetValue(key, out object? candidate) ? candidate : null;
        if (raw is TEnum typed && Enum.IsDefined(typed))
        {
            value = typed;
            return true;
        }

        string? text = ReadNullableString(map, key);
        if (text is not null && Enum.TryParse(text, true, out value) && Enum.IsDefined(value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

/// <summary>Projects Slice 4.6 typed choices and labels for clients.</summary>
public static class Slice46PresentationProjector
{
    public static Slice46DecisionViewModel Present(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Slice45DecisionViewModel baseDecision = Slice45PresentationProjector.Present(view);
        if (!baseDecision.IsAvailable)
        {
            return Slice46DecisionViewModel.Unavailable(baseDecision.Diagnostic);
        }

        var groups = baseDecision.Options
            .GroupBy(static option => option.ChoiceKey ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var options = new List<Slice46ActionPresentationViewModel>(baseDecision.Options.Count);
        foreach (Slice45ActionPresentationViewModel option in baseDecision.Options)
        {
            Slice45ActionPresentationViewModel[] group = groups[option.ChoiceKey ?? string.Empty];
            bool hasFallback = group.Any(static candidate => candidate.IsFallback);
            bool hasPaid = group.Any(static candidate => candidate.Costs.Count > 0)
                || group.Any(static candidate => candidate.IceChoice == IceSubroutineChoiceKind.PayCredits);
            bool isIceUnless = group.Any(static candidate => candidate.IceChoice == IceSubroutineChoiceKind.PayCredits)
                && group.Any(static candidate => candidate.IceChoice is IceSubroutineChoiceKind.TakeTag or IceSubroutineChoiceKind.Decline);
            bool isUnless = (hasFallback && hasPaid) || isIceUnless;
            bool isFallback = option.IsFallback || (isUnless && option.ActionKind is ActionKind.DeclineTrigger);
            bool isPayAlternative = isUnless
                && !isFallback
                && (option.Costs.Count > 0 || option.IceChoice == IceSubroutineChoiceKind.PayCredits);
            options.Add(new Slice46ActionPresentationViewModel(option, isUnless, isPayAlternative, isFallback));
        }

        return new Slice46DecisionViewModel(
            true,
            null,
            baseDecision.Surface,
            baseDecision.DecisionKind,
            baseDecision.ActingPlayer,
            baseDecision.Prompt,
            new ReadOnlyCollection<Slice46ActionPresentationViewModel>(options),
            false);
    }

    public static Slice46DecisionViewModel Present(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Slice46DecisionViewModel decision = Present(snapshot.View);
        bool damageResolved = snapshot.Events.Any(static @event =>
            @event.Payload is DamageDealtEventPayload);
        bool jackOut = damageResolved
            && snapshot.View.LegalActions.Any(static action =>
                action.Parameters is IceSubroutineChoiceParameters choice
                && choice.Choice == IceSubroutineChoiceKind.JackOut);
        return decision with { JackOutAfterDamage = jackOut };
    }

    public static string Label(LegalActionViewModel action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Parameters is RemoveTagParameters remove)
        {
            return $@"REMOVE TAG · COST · {Pluralize(remove.ClickCost, "click")} + "
                + $@"{Pluralize(remove.CreditCost, "credit")}";
        }

        if (action.Parameters is TrashTaggedResourceParameters trash)
        {
            return $@"TRASH TAGGED RESOURCE · TARGET {trash.CardId.Value} · COST · "
                + $@"{Pluralize(trash.ClickCost, "click")} + {Pluralize(trash.CreditCost, "credit")}";
        }

        return Slice45PresentationProjector.Label(action);
    }

    public static string DescribeDamage(DamageDealtEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return DamagePresentationViewModel.FromEvent(payload).Summary;
    }

    public static string DescribeTags(TagsAdjustedEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string direction = payload.Delta > 0 ? "GAIN" : payload.Delta < 0 ? "REMOVAL" : "CHANGE";
        string state = payload.TagsAfter > 0 ? "TAGGED" : "CLEAR";
        string delta = payload.Delta > 0
            ? $@"+{payload.Delta.ToString(CultureInfo.InvariantCulture)}"
            : payload.Delta.ToString(CultureInfo.InvariantCulture);
        return $@"TAG {direction} · {payload.Target.Value} · {delta} · TOTAL "
            + $@"{payload.TagsAfter.ToString(CultureInfo.InvariantCulture)} · {state}.";
    }

    private static string Pluralize(int amount, string singular) =>
        $@"{amount.ToString(CultureInfo.InvariantCulture)} {singular}{(amount == 1 ? string.Empty : "s")}";
}
