using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// One explicitly compiled, versioned ICE behavior spec. Every value is
/// hand-copied from the printed card text; card text is never parsed or
/// interpreted here or in the catalog.
/// </summary>
public sealed record IceBehaviorSpec
{
    public IceBehaviorSpec(
        CardDefinitionId definitionId,
        int strength,
        IceType iceType,
        int rezCost,
        IReadOnlyList<IceSubroutineSpec> subroutines,
        IReadOnlyList<IceStrengthModifierSpec> strengthModifiers,
        IReadOnlyList<BehaviorValueModifier>? valueModifiers = null)
    {
        DefinitionId = definitionId;
        Strength = strength;
        IceType = iceType;
        RezCost = rezCost;
        ArgumentNullException.ThrowIfNull(subroutines);
        ArgumentNullException.ThrowIfNull(strengthModifiers);
        Subroutines = new ReadOnlyCollection<IceSubroutineSpec>(subroutines.ToArray());
        StrengthModifiers = new ReadOnlyCollection<IceStrengthModifierSpec>(strengthModifiers.ToArray());
        ValueModifiers = FreezeValueModifiers(valueModifiers);
        if (strength < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative base strength.", nameof(strength));
        }

        if (rezCost < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative rez cost.", nameof(rezCost));
        }

        if (iceType is IceType.Any || !Enum.IsDefined(iceType))
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must declare a concrete ICE subtype; '{iceType}' is not a valid ICE subtype.",
                nameof(iceType));
        }
    }

    public CardDefinitionId DefinitionId { get; }

    /// <summary>Base printed strength.</summary>
    public int Strength { get; }

    /// <summary>The ICE type this card is, for icebreaker interface matching.</summary>
    public IceType IceType { get; }

    /// <summary>Rez cost in credits.</summary>
    public int RezCost { get; }

    /// <summary>Printed subroutines, outermost-to-innermost as printed.</summary>
    public IReadOnlyList<IceSubroutineSpec> Subroutines { get; }

    public IReadOnlyList<IceStrengthModifierSpec> StrengthModifiers { get; }

    /// <summary>
    /// Generic value declarations owned by this ICE. The nullable shape keeps
    /// legacy M2 catalog JSON/fingerprints unchanged when no declaration is
    /// present; the legacy <see cref="StrengthModifiers"/> list remains an
    /// adapter consumed by the shared strength projector.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorValueModifier>? ValueModifiers { get; }

    private static ReadOnlyCollection<BehaviorValueModifier>? FreezeValueModifiers(
        IReadOnlyList<BehaviorValueModifier>? valueModifiers)
    {
        if (valueModifiers is null or { Count: 0 })
        {
            return null;
        }

        BehaviorValueModifier[] copy = valueModifiers.ToArray();
        if (copy.Any(static modifier => modifier is null))
        {
            throw new ArgumentException(
                "A value modifier declaration list must not contain null entries.",
                nameof(valueModifiers));
        }

        return new ReadOnlyCollection<BehaviorValueModifier>(copy);
    }
}

/// <summary>One printed ICE subroutine, in printed order.</summary>
public abstract record IceSubroutineSpec;

/// <summary>"End the run."</summary>
public sealed record EndRunSubroutineSpec : IceSubroutineSpec;

/// <summary>"The Runner loses N [credit]."</summary>
public sealed record LoseCreditsSubroutineSpec : IceSubroutineSpec
{
    public LoseCreditsSubroutineSpec(PlayerSide target, int amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentException("The credit amount must be positive.", nameof(amount));
        }

        Target = target;
        Amount = amount;
    }

    public PlayerSide Target { get; }

    public int Amount { get; }
}

/// <summary>"If the Runner has N [credit] or less, end the run."</summary>
public sealed record EndRunIfCreditsAtMostSubroutineSpec : IceSubroutineSpec
{
    public EndRunIfCreditsAtMostSubroutineSpec(PlayerSide target, int creditLimit)
    {
        if (creditLimit < 0)
        {
            throw new ArgumentException("The credit limit must not be negative.", nameof(creditLimit));
        }

        Target = target;
        CreditLimit = creditLimit;
    }

    public PlayerSide Target { get; }

    public int CreditLimit { get; }
}

/// <summary>The condition under which a strength modifier applies.</summary>
public enum IceStrengthModifierCondition
{
    ProtectingRemoteServer,
}

/// <summary>A conditional adjustment to the printed strength.</summary>
public sealed record IceStrengthModifierSpec
{
    public IceStrengthModifierSpec(int delta, IceStrengthModifierCondition condition)
    {
        if (delta == 0)
        {
            throw new ArgumentException("The strength delta must be nonzero.", nameof(delta));
        }

        Delta = delta;
        Condition = condition;
    }

    public int Delta { get; }

    public IceStrengthModifierCondition Condition { get; }
}

/// <summary>The ICE types an icebreaker can interface with.</summary>
public enum IceType
{
    Barrier,
    CodeGate,
    Sentry,
    Any,
}

/// <summary>
/// One explicitly compiled, versioned icebreaker behavior spec. Every value is
/// hand-copied from the printed card text; card text is never parsed or
/// interpreted here or in the catalog.
/// </summary>
public sealed record IcebreakerBehaviorSpec
{
    public IcebreakerBehaviorSpec(
        CardDefinitionId definitionId,
        int baseStrength,
        IceType interfaceType,
        int breakCost,
        int breakLimit,
        int pumpCost,
        int pumpStrength,
        IReadOnlyList<BehaviorValueModifier>? valueModifiers = null,
        BehaviorDynamicPumpDeclaration? dynamicPump = null)
    {
        DefinitionId = definitionId;
        BaseStrength = baseStrength;
        InterfaceType = interfaceType;
        BreakCost = breakCost;
        BreakLimit = breakLimit;
        PumpCost = pumpCost;
        PumpStrength = pumpStrength;
        ValueModifiers = FreezeValueModifiers(valueModifiers);
        DynamicPump = dynamicPump;
        if (baseStrength < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative base strength.", nameof(baseStrength));
        }

        if (breakCost < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative break cost.", nameof(breakCost));
        }

        if (breakLimit < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative break limit.", nameof(breakLimit));
        }

        if (pumpCost < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative pump cost.", nameof(pumpCost));
        }

        if (dynamicPump is null && pumpStrength <= 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must have a positive pump strength.", nameof(pumpStrength));
        }

        if (dynamicPump is not null && pumpStrength != 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' uses a dynamic pump and must not declare a fake printed pump strength.",
                nameof(pumpStrength));
        }

        if (!Enum.IsDefined(interfaceType))
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must declare a concrete or wildcard icebreaker interface; '{interfaceType}' is invalid.",
                nameof(interfaceType));
        }
    }

    public CardDefinitionId DefinitionId { get; }

    /// <summary>Base printed strength.</summary>
    public int BaseStrength { get; }

    /// <summary>The ICE type this breaker interfaces with.</summary>
    public IceType InterfaceType { get; }

    /// <summary>Cost in credits to interface with the ICE.</summary>
    public int BreakCost { get; }

    /// <summary>Maximum subroutines broken per interface use.</summary>
    public int BreakLimit { get; }

    /// <summary>Cost in credits to pump.</summary>
    public int PumpCost { get; }

    /// <summary>Strength added per pump.</summary>
    public int PumpStrength { get; }

    /// <summary>Generic strength declarations owned by this breaker.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorValueModifier>? ValueModifiers { get; }

    /// <summary>
    /// Optional activation-time dynamic pump. It is intentionally separate
    /// from passive value declarations: the engine snapshots its quantity
    /// into an encounter adjustment when the pump ability is paid.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorDynamicPumpDeclaration? DynamicPump { get; }

    private static ReadOnlyCollection<BehaviorValueModifier>? FreezeValueModifiers(
        IReadOnlyList<BehaviorValueModifier>? valueModifiers)
    {
        if (valueModifiers is null or { Count: 0 })
        {
            return null;
        }

        BehaviorValueModifier[] copy = valueModifiers.ToArray();
        if (copy.Any(static modifier => modifier is null))
        {
            throw new ArgumentException(
                "A value modifier declaration list must not contain null entries.",
                nameof(valueModifiers));
        }

        return new ReadOnlyCollection<BehaviorValueModifier>(copy);
    }
}

/// <summary>
/// One explicitly compiled, versioned asset behavior spec. Every value is
/// hand-copied from the printed card text; card text is never parsed or
/// interpreted here or in the catalog.
/// </summary>
public sealed record AssetBehaviorSpec
{
    public AssetBehaviorSpec(CardDefinitionId definitionId, int trashCost)
    {
        DefinitionId = definitionId;
        TrashCost = trashCost;
        if (trashCost < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative trash cost.", nameof(trashCost));
        }
    }

    public CardDefinitionId DefinitionId { get; }

    /// <summary>Cost in credits for the Runner to trash this asset.</summary>
    public int TrashCost { get; }
}

/// <summary>
/// One explicitly compiled, versioned agenda behavior spec. Every value is
/// hand-copied from the printed card text; card text is never parsed or
/// interpreted here or in the catalog.
/// </summary>
public sealed record AgendaBehaviorSpec
{
    public AgendaBehaviorSpec(CardDefinitionId definitionId, int agendaPoints)
    {
        DefinitionId = definitionId;
        AgendaPoints = agendaPoints;
        if (agendaPoints <= 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must have positive agenda points.", nameof(agendaPoints));
        }
    }

    public CardDefinitionId DefinitionId { get; }

    /// <summary>Agenda points granted when the agenda is scored.</summary>
    public int AgendaPoints { get; }
}

/// <summary>
/// One explicitly compiled, versioned run event behavior spec. Every value is
/// hand-copied from the printed card text; card text is never parsed or
/// interpreted here or in the catalog. A run event starts a run and places
/// hosted credits on the event that are spendable only during that run.
/// </summary>
public sealed record RunEventBehaviorSpec
{
    public RunEventBehaviorSpec(CardDefinitionId definitionId, int cost, int hostedCredits)
    {
        DefinitionId = definitionId;
        Cost = cost;
        HostedCredits = hostedCredits;
        if (cost < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative event cost.", nameof(cost));
        }

        if (hostedCredits < 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must not have a negative hosted credit amount.", nameof(hostedCredits));
        }
    }

    public CardDefinitionId DefinitionId { get; }

    /// <summary>Event cost in credits.</summary>
    public int Cost { get; }

    /// <summary>Credits placed on the event; spendable only during the run.</summary>
    public int HostedCredits { get; }
}
