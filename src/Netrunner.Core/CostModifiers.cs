using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// Typed credit-cost values that a behavior declaration may modify. Keeping
/// the value typed prevents a card from silently changing a different cost
/// merely because it happens to be in the same timing window.
/// </summary>
public enum BehaviorCostModifierKind
{
    RunnerInstallCreditCost,
    IcebreakerBreakCreditCost,
    IceRezCreditCost,
}

/// <summary>
/// A typed run-source declaration for a credit adjustment to ICE rez costs.
/// The declaration is copied onto the immutable run state when a run event
/// starts a run; it is never re-read from the physical source while the run
/// is in progress.
/// </summary>
public sealed record BehaviorRunRezCostModifier
{
    [JsonConstructor]
    public BehaviorRunRezCostModifier(int delta, string reasonKey)
    {
        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "A run rez-cost modifier delta must be nonzero.");
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException(
                "A run rez-cost modifier reason key must be a stable identifier.",
                nameof(reasonKey));
        }

        Delta = delta;
        ReasonKey = reasonKey;
    }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string ReasonKey { get; private set; }
}

/// <summary>
/// A data-only conditional adjustment owned by one card behavior definition.
/// The source card is supplied by the engine when the modifier is projected;
/// this declaration never scans cards or titles itself.
/// </summary>
public sealed record BehaviorCostModifier
{
    [JsonConstructor]
    public BehaviorCostModifier(
        BehaviorCostModifierKind kind,
        int delta,
        BehaviorCondition condition,
        string reasonKey)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == BehaviorCostModifierKind.IceRezCreditCost)
        {
            throw new ArgumentException(
                "ICE rez-cost modifiers are run-scoped declarations on RunEventBehaviorSpec.",
                nameof(kind));
        }

        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "A cost modifier delta must be nonzero.");
        }

        ArgumentNullException.ThrowIfNull(condition);
        if (condition.Kind != BehaviorConditionKind.SuccessfulRunThisTurn
            || condition.Fact is not null
            || condition.Value is not null
            || condition.SubjectSide is not null)
        {
            throw new ArgumentException(
                "This checkpoint supports only the explicit SuccessfulRunThisTurn condition for cost modifiers.",
                nameof(condition));
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException("A cost modifier reason key must be a stable identifier.", nameof(reasonKey));
        }

        Kind = kind;
        Delta = delta;
        Condition = condition;
        ReasonKey = reasonKey;
    }

    public BehaviorCostModifier(
        BehaviorCostModifierKind kind,
        int delta,
        string reasonKey)
        : this(kind, delta, BehaviorCondition.SuccessfulRunThisTurn(), reasonKey)
    {
    }

    public BehaviorCostModifierKind Kind { get; }

    public int Delta { get; }

    public BehaviorCondition Condition { get; }

    public string ReasonKey { get; }
}

/// <summary>
/// One detached, ordered explanation for a calculated credit cost.  A
/// definition identity identifies the declaration while the physical source
/// identity binds it to the card instance that caused this quote.
/// </summary>
public sealed record CostAdjustment
{
    [JsonConstructor]
    public CostAdjustment(
        BehaviorCostModifierKind kind,
        int delta,
        string reasonKey,
        CardDefinitionId? sourceDefinitionId = null,
        CardInstanceId? sourceCardId = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "A cost adjustment delta must be nonzero.");
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException("A cost adjustment reason key must be a stable identifier.", nameof(reasonKey));
        }

        if (sourceDefinitionId is { } definition
            && !StableIdentifier.TryValidate(definition.Value))
        {
            throw new ArgumentException("A cost adjustment source definition must be a stable identifier.", nameof(sourceDefinitionId));
        }

        if (sourceCardId is { } card
            && !StableIdentifier.TryValidate(card.Value))
        {
            throw new ArgumentException("A cost adjustment source card must be a stable identifier.", nameof(sourceCardId));
        }

        Kind = kind;
        Delta = delta;
        ReasonKey = reasonKey;
        SourceDefinitionId = sourceDefinitionId;
        SourceCardId = sourceCardId;
    }

    public BehaviorCostModifierKind Kind { get; }

    public int Delta { get; }

    public string ReasonKey { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardDefinitionId? SourceDefinitionId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardInstanceId? SourceCardId { get; }
}

/// <summary>
/// Immutable checked calculation of a credit cost.  <see cref="BaseCost"/>
/// and the ordered adjustments are authoritative; the effective value is
/// validated against them at construction and is clamped at zero.
/// </summary>
public sealed class CostQuote : IEquatable<CostQuote>
{
    [JsonConstructor]
    public CostQuote(
        int baseCost,
        int effectiveCost,
        IReadOnlyList<CostAdjustment>? adjustments = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseCost);
        ArgumentOutOfRangeException.ThrowIfNegative(effectiveCost);

        CostAdjustment[] copy = (adjustments ?? []).ToArray();
        if (copy.Any(static adjustment => adjustment is null))
        {
            throw new ArgumentException("Cost quote adjustments must not contain null entries.", nameof(adjustments));
        }

        int expected = CalculateEffective(baseCost, copy);
        if (effectiveCost != expected)
        {
            throw new ArgumentException(
                $"The effective cost {effectiveCost} does not match the checked quote calculation {expected}.",
                nameof(effectiveCost));
        }

        BaseCost = baseCost;
        EffectiveCost = effectiveCost;
        Adjustments = new ReadOnlyCollection<CostAdjustment>(copy);
    }

    public CostQuote(int baseCost, IReadOnlyList<CostAdjustment>? adjustments = null)
        : this(baseCost, CalculateEffective(baseCost, adjustments ?? []), adjustments)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int BaseCost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int EffectiveCost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CostAdjustment> Adjustments { get; private set; }

    /// <summary>Alias used by observation projections that display reasons.</summary>
    [JsonIgnore]
    public IReadOnlyList<CostAdjustment> Reasons => Adjustments;

    public static CostQuote From(
        int baseCost,
        IEnumerable<CostAdjustment>? adjustments = null) =>
        new(baseCost, (adjustments ?? []).ToArray());

    public bool Equals(CostQuote? other) =>
        other is not null
        && BaseCost == other.BaseCost
        && EffectiveCost == other.EffectiveCost
        && Adjustments.SequenceEqual(other.Adjustments);

    public override bool Equals(object? obj) => Equals(obj as CostQuote);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BaseCost);
        hash.Add(EffectiveCost);
        foreach (CostAdjustment adjustment in Adjustments)
        {
            hash.Add(adjustment);
        }

        return hash.ToHashCode();
    }

    private static int CalculateEffective(int baseCost, IReadOnlyList<CostAdjustment> adjustments)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseCost);
        long total = baseCost;
        foreach (CostAdjustment adjustment in adjustments)
        {
            ArgumentNullException.ThrowIfNull(adjustment);
            total = checked(total + adjustment.Delta);
        }

        if (total > int.MaxValue || total < int.MinValue)
        {
            throw new OverflowException("Cost quote adjustments exceed the supported integer range.");
        }

        return Math.Max(0, (int)total);
    }
}

/// <summary>
/// Projects the narrow self-cost declarations against current formal turn
/// facts.  This helper is intentionally internal: callers receive only a
/// detached quote, and no reducer can use a live declaration after issuance.
/// </summary>
public static partial class GameEngine
{
    private static bool CostQuotesEqual(CostQuote? left, CostQuote? right) =>
        left is null ? right is null : left.Equals(right);

    internal static CostQuote? ProjectCostQuote(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        CardInstanceId sourceCardId,
        BehaviorCostModifierKind kind,
        int baseCost)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        CardInstanceState? source = state.Cards.FirstOrDefault(card => card.Id == sourceCardId);
        if (source is null)
        {
            return null;
        }

        return CostModifierProjection.ForSource(
            WorkingState.From(state, behaviorCatalog),
            source,
            kind,
            baseCost);
    }

    /// <summary>
    /// Projects the authoritative run-scoped ICE rez quote for an installed
    /// target. Reducers and invariant reconstruction use this boundary so
    /// they cannot reimplement the run adjustment arithmetic independently.
    /// </summary>
    internal static CostQuote? ProjectIceRezQuote(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        CardInstanceId targetCardId,
        int baseCost)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        CardInstanceState? target = state.Cards.FirstOrDefault(card => card.Id == targetCardId);
        return target is null
            ? null
            : CostModifierProjection.IceRez(
                WorkingState.From(state, behaviorCatalog),
                target,
                baseCost);
    }

    private static class CostModifierProjection
    {
        internal static CostQuote? RunnerInstall(
            WorkingState working,
            CardInstanceState source,
            int baseCost) =>
            IsInZone(working, source, ZoneKind.RunnerGrip)
                ? ForSource(working, source, BehaviorCostModifierKind.RunnerInstallCreditCost, baseCost)
                : null;

        internal static CostQuote? IcebreakerBreak(
            WorkingState working,
            CardInstanceState source,
            int baseCost) =>
            IsInZone(working, source, ZoneKind.RunnerRig)
                && source.FaceUp
                ? ForSource(working, source, BehaviorCostModifierKind.IcebreakerBreakCreditCost, baseCost)
                : null;

        /// <summary>
        /// Projects the immutable run-start ICE rez adjustments against one
        /// physically installed Corp ICE. The run state is the sole source
        /// of these adjustments; the initiating card's current zone and
        /// definition are not consulted after the snapshot was created.
        /// </summary>
        internal static CostQuote? IceRez(
            WorkingState working,
            CardInstanceState target,
            int baseCost)
        {
            ArgumentNullException.ThrowIfNull(working);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentOutOfRangeException.ThrowIfNegative(baseCost);

            if (working.RunState is not { RezCostModifiers: { Count: > 0 } modifiers }
                || target.OwnerSide != PlayerSide.Corp
                || target.Rezzed)
            {
                return null;
            }

            ZoneState? zone = working.Zones.FirstOrDefault(candidate => candidate.Id == target.ZoneId);
            if (zone is null
                || zone.Kind != ZoneKind.ServerIce
                || zone.OwnerSide != PlayerSide.Corp
                || zone.ServerId is null
                || !zone.CardIds.Contains(target.Id))
            {
                return null;
            }

            CostAdjustment[] adjustments = modifiers
                .Select(static modifier => modifier.ToCostAdjustment())
                .OrderBy(static adjustment => adjustment.SourceDefinitionId?.Value, StringComparer.Ordinal)
                .ThenBy(static adjustment => adjustment.SourceCardId?.Value, StringComparer.Ordinal)
                .ThenBy(static adjustment => adjustment.ReasonKey, StringComparer.Ordinal)
                .ThenBy(static adjustment => adjustment.Delta)
                .ToArray();
            return new CostQuote(baseCost, adjustments);
        }

        private static bool IsInZone(
            WorkingState working,
            CardInstanceState source,
            ZoneKind expectedKind)
        {
            ZoneState? zone = working.Zones.FirstOrDefault(candidate => candidate.Id == source.ZoneId);
            return zone is not null
                && zone.Kind == expectedKind
                && zone.OwnerSide == PlayerSide.Runner
                && zone.CardIds.Contains(source.Id);
        }

        internal static CostQuote? ForSource(
            WorkingState working,
            CardInstanceState source,
            BehaviorCostModifierKind kind,
            int baseCost)
        {
            ArgumentNullException.ThrowIfNull(working);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(baseCost);

            if (source.OwnerSide != PlayerSide.Runner)
            {
                return null;
            }

            if (!working.BehaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition))
            {
                return null;
            }

            if (!IsSuccessfulRunThisTurn(working))
            {
                return null;
            }

            CostAdjustment[] adjustments = (definition.CostModifiers ?? [])
                .Where(modifier => modifier.Kind == kind && ConditionMatches(working, modifier.Condition))
                .Select(modifier => new CostAdjustment(
                    modifier.Kind,
                    modifier.Delta,
                    modifier.ReasonKey,
                    source.DefinitionId,
                    source.Id))
                .ToArray();
            return adjustments.Length == 0
                ? null
                : new CostQuote(baseCost, adjustments);
        }

        private static bool ConditionMatches(WorkingState working, BehaviorCondition condition) =>
            condition.Kind == BehaviorConditionKind.SuccessfulRunThisTurn
            && IsSuccessfulRunThisTurn(working);

        private static bool IsSuccessfulRunThisTurn(WorkingState working)
        {
            if (working.Turn.ActiveSide != PlayerSide.Runner
                || working.Turn.Phase != TurnPhase.Runner
                || working.Turn.Step is not (TurnStep.RunnerAction or TurnStep.RunnerDiscard)
                || working.Turn.History?.RunnerThisTurn is not { } facts
                || facts.TurnNumber != working.Turn.TurnNumber
                || !facts.TurnBegan)
            {
                return false;
            }

            return facts.SuccessfulRun;
        }
    }
}
