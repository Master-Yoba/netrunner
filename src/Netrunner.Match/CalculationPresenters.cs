using System.Collections.ObjectModel;
using System.Globalization;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// A detached value term prepared for display. The raw typed fields are kept
/// alongside the readable label so clients can choose their own layout
/// without rebuilding the calculation from rules text.
/// </summary>
public sealed record ValueCalculationTermPresentationViewModel(
    BehaviorValueSourceKind Source,
    BehaviorValueOperation Operation,
    int Delta,
    string ReasonKey,
    CardDefinitionId? SourceDefinitionId,
    CardInstanceId? SourceCardId,
    int? Quantity,
    int? UnitDelta,
    string? VariableKey)
{
    public string ReasonLabel => CalculationReasonLabels.ForValue(ReasonKey);

    public string Summary => FormatDelta(Delta) + " (" + ReasonLabel + FormatQuantity() + ")";

    private string FormatQuantity() => Quantity is { } quantity
        && Source is BehaviorValueSourceKind.PerInstalledCard or BehaviorValueSourceKind.Variable
        ? $": {quantity.ToString(CultureInfo.InvariantCulture)}"
        : string.Empty;

    private static string FormatDelta(int delta) => delta switch
    {
        > 0 => $"+{delta.ToString(CultureInfo.InvariantCulture)}",
        _ => delta.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Match-owned readable projection of a Core <see cref="ValueQuote"/>. The
/// quote remains available on the encounter/breaker view for typed consumers;
/// this model only adds localized, fail-safe text.
/// </summary>
public sealed record StrengthBreakdownViewModel(
    int BaseStrength,
    int EffectiveStrength,
    IReadOnlyList<ValueCalculationTermPresentationViewModel> Terms)
{
    public string Summary => Terms.Count == 0 && EffectiveStrength == BaseStrength
        ? $"STR {BaseStrength.ToString(CultureInfo.InvariantCulture)}"
        : Terms.Count == 0
            ? $"STR {BaseStrength.ToString(CultureInfo.InvariantCulture)} · CURRENT STR {EffectiveStrength.ToString(CultureInfo.InvariantCulture)}"
        : $"STR {BaseStrength.ToString(CultureInfo.InvariantCulture)} "
            + string.Join(" ", Terms.Select(static term => term.Summary));

    public static StrengthBreakdownViewModel From(ValueQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        if (quote.ValueKind != BehaviorValueKind.Strength)
        {
            throw new ArgumentException(
                "A strength breakdown requires a strength value quote.",
                nameof(quote));
        }

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
        return new StrengthBreakdownViewModel(
            quote.BaseValue,
            quote.EffectiveValue,
            new ReadOnlyCollection<ValueCalculationTermPresentationViewModel>(terms));
    }
}

/// <summary>Detached readable projection of one typed cost adjustment.</summary>
public sealed record CostAdjustmentPresentationViewModel(
    BehaviorCostModifierKind Kind,
    int Delta,
    string ReasonKey,
    CardDefinitionId? SourceDefinitionId,
    CardInstanceId? SourceCardId)
{
    public string ReasonLabel => CalculationReasonLabels.ForCost(ReasonKey);
}

/// <summary>
/// Match-owned readable projection of a Core <see cref="CostQuote"/>. Base,
/// effective, and adjustment order all come directly from the checked quote.
/// </summary>
public sealed record CostQuotePresentationViewModel(
    string ActionLabel,
    int BaseCost,
    int EffectiveCost,
    IReadOnlyList<CostAdjustmentPresentationViewModel> Adjustments)
{
    public bool IsModified => BaseCost != EffectiveCost || Adjustments.Count > 0;

    public string Summary => !IsModified
        ? $"{ActionLabel} {BaseCost.ToString(CultureInfo.InvariantCulture)}"
        : $"{ActionLabel} {BaseCost.ToString(CultureInfo.InvariantCulture)} → "
            + $"{EffectiveCost.ToString(CultureInfo.InvariantCulture)} ("
            + string.Join("; ", Adjustments.Select(static adjustment => adjustment.ReasonLabel))
            + ")";
}

/// <summary>Stable Match-side labels for known typed calculation reasons.</summary>
public static class CalculationReasonLabels
{
    private static readonly Dictionary<string, string> ValueLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["echelon-installed-icebreakers"] = "per installed icebreaker",
            ["unity-installed-icebreakers"] = "per installed icebreaker",
            ["leech-encounter-ice-strength"] = "Leech encounter strength",
            ["ice-strength-protecting-remote"] = "protecting remote",
            ["icebreaker-pump"] = "encounter pump",
        };

    private static readonly Dictionary<string, string> CostLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tread-lightly-ice-rez-cost"] = "Tread Lightly this run",
            ["dzmz-first-program-install"] = "DZMZ first program install this turn",
            ["carmen-successful-run-install"] = "successful run this turn",
            ["marjanah-successful-run-break"] = "successful run this turn",
            ["successful-run-this-turn"] = "successful run this turn",
        };

    public static string ForValue(string reasonKey)
    {
        ArgumentNullException.ThrowIfNull(reasonKey);
        return ValueLabels.TryGetValue(reasonKey, out string? label)
            ? label
            : reasonKey;
    }

    public static string ForCost(string reasonKey)
    {
        ArgumentNullException.ThrowIfNull(reasonKey);
        if (CostLabels.TryGetValue(reasonKey, out string? label))
        {
            return label;
        }

        return reasonKey;
    }
}

/// <summary>Builds a detached cost presentation from one issued action quote.</summary>
public static class CostQuotePresenter
{
    public static CostQuotePresentationViewModel Present(
        ActionKind actionKind,
        ActionParameters parameters,
        CostQuote quote)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(quote);
        CostAdjustmentPresentationViewModel[] adjustments = quote.Adjustments
            .Select(static adjustment => new CostAdjustmentPresentationViewModel(
                adjustment.Kind,
                adjustment.Delta,
                adjustment.ReasonKey,
                adjustment.SourceDefinitionId,
                adjustment.SourceCardId))
            .ToArray();
        return new CostQuotePresentationViewModel(
            ActionLabel(actionKind),
            quote.BaseCost,
            quote.EffectiveCost,
            new ReadOnlyCollection<CostAdjustmentPresentationViewModel>(adjustments));
    }

    public static string Describe(
        ActionKind actionKind,
        ActionParameters parameters,
        CostQuote quote) => Present(actionKind, parameters, quote).Summary;

    private static string ActionLabel(ActionKind actionKind) => actionKind switch
    {
        ActionKind.InstallCard => "install",
        ActionKind.RezCard or ActionKind.RezIce => "rez",
        ActionKind.BreakSubroutines => "break",
        _ => actionKind.ToString().ToLowerInvariant(),
    };
}

/// <summary>Readable non-colour note for one typed run breach modifier.</summary>
public static class BreachAccessModifierPresenter
{
    public static string Describe(RunBreachAccessModifierViewModel modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        string delta = modifier.Delta switch
        {
            > 0 => $"+{modifier.Delta.ToString(CultureInfo.InvariantCulture)}",
            _ => modifier.Delta.ToString(CultureInfo.InvariantCulture),
        };
        bool knownFirstHqReason = string.Equals(
            modifier.ReasonKey,
            "docklands-pass-first-hq-breach",
            StringComparison.Ordinal);
        if (knownFirstHqReason)
        {
            return $"{delta} first HQ breach this turn";
        }

        if (modifier.SourceKind == RunBreachAccessModifierSourceKind.FirstHeadquartersBreach)
        {
            return $"{delta} first HQ breach this turn ({modifier.ReasonKey})";
        }

        // Unknown reason keys remain visible rather than disappearing. The
        // source card ID is intentionally omitted: the observation may redact
        // it and a note never needs hidden-card provenance.
        return $"{delta} ({modifier.ReasonKey})";
    }
}
