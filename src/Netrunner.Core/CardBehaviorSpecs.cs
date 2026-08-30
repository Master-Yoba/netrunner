using System.Collections.ObjectModel;

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
        IReadOnlyList<IceStrengthModifierSpec> strengthModifiers)
    {
        DefinitionId = definitionId;
        Strength = strength;
        IceType = iceType;
        RezCost = rezCost;
        ArgumentNullException.ThrowIfNull(subroutines);
        ArgumentNullException.ThrowIfNull(strengthModifiers);
        Subroutines = new ReadOnlyCollection<IceSubroutineSpec>(subroutines.ToArray());
        StrengthModifiers = new ReadOnlyCollection<IceStrengthModifierSpec>(strengthModifiers.ToArray());
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
        int pumpStrength)
    {
        DefinitionId = definitionId;
        BaseStrength = baseStrength;
        InterfaceType = interfaceType;
        BreakCost = breakCost;
        BreakLimit = breakLimit;
        PumpCost = pumpCost;
        PumpStrength = pumpStrength;
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

        if (pumpStrength <= 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must have a positive pump strength.", nameof(pumpStrength));
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

/// <summary>
/// Build-versioned catalog of explicitly compiled card behavior specs.
/// Registration is structural: an identical re-registration is a no-op, a
/// conflicting one throws, and unregistered definitions fail closed in
/// <see cref="GetIce"/> and <see cref="GetIcebreaker"/>.
/// </summary>
public static class CardBehaviorSpecCatalog
{
    public const string Version = "gateway-m2-3";

    private static readonly Dictionary<CardDefinitionId, IceBehaviorSpec> Registered = new();

    private static readonly Dictionary<CardDefinitionId, IcebreakerBehaviorSpec> RegisteredBreakers = new();

    private static readonly Dictionary<CardDefinitionId, AssetBehaviorSpec> RegisteredAssets = new();

    private static readonly Dictionary<CardDefinitionId, AgendaBehaviorSpec> RegisteredAgendas = new();

    private static readonly Dictionary<CardDefinitionId, RunEventBehaviorSpec> RegisteredRunEvents = new();

    // The catalog is process-wide static state; test classes and the CLI
    // may register concurrently, so every read and write takes the gate.
    private static readonly object Gate = new();

    /// <summary>The ICE definition IDs currently registered, in registration order.</summary>
    public static IReadOnlyCollection<CardDefinitionId> DefinitionIds
    {
        get
        {
            lock (Gate)
            {
                return Registered.Keys.ToArray();
            }
        }
    }

    public static void RegisterIce(IceBehaviorSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        lock (Gate)
        {
            if (Registered.TryGetValue(spec.DefinitionId, out IceBehaviorSpec? existing))
            {
                if (!StructurallyEqual(existing, spec))
                {
                    throw new InvalidOperationException(
                        $"Card definition '{spec.DefinitionId}' is already registered "
                        + "with a different ICE behavior spec.");
                }

                return;
            }

            Registered.Add(spec.DefinitionId, spec);
        }
    }

    public static void RegisterIce(IReadOnlyCollection<IceBehaviorSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        foreach (IceBehaviorSpec spec in specs)
        {
            RegisterIce(spec);
        }
    }

    public static bool TryGetIce(CardDefinitionId definitionId, out IceBehaviorSpec spec)
    {
        lock (Gate)
        {
            return Registered.TryGetValue(definitionId, out spec!);
        }
    }

    public static IceBehaviorSpec GetIce(CardDefinitionId definitionId) =>
        TryGetIce(definitionId, out IceBehaviorSpec? spec)
            ? spec
            : throw new ArgumentException(
                $"Card definition '{definitionId}' is not registered in the ICE behavior spec catalog.",
                nameof(definitionId));

    public static void RegisterIcebreaker(IcebreakerBehaviorSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        lock (Gate)
        {
            if (RegisteredBreakers.TryGetValue(spec.DefinitionId, out IcebreakerBehaviorSpec? existing))
            {
                if (!StructurallyEqual(existing, spec))
                {
                    throw new InvalidOperationException(
                        $"Card definition '{spec.DefinitionId}' is already registered "
                        + "with a different icebreaker behavior spec.");
                }

                return;
            }

            RegisteredBreakers.Add(spec.DefinitionId, spec);
        }
    }

    public static void RegisterIcebreaker(IReadOnlyCollection<IcebreakerBehaviorSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        foreach (IcebreakerBehaviorSpec spec in specs)
        {
            RegisterIcebreaker(spec);
        }
    }

    public static bool TryGetIcebreaker(CardDefinitionId definitionId, out IcebreakerBehaviorSpec spec)
    {
        lock (Gate)
        {
            return RegisteredBreakers.TryGetValue(definitionId, out spec!);
        }
    }

    public static IcebreakerBehaviorSpec GetIcebreaker(CardDefinitionId definitionId) =>
        TryGetIcebreaker(definitionId, out IcebreakerBehaviorSpec? spec)
            ? spec
            : throw new ArgumentException(
                $"Card definition '{definitionId}' is not registered in the icebreaker behavior spec catalog.",
                nameof(definitionId));

    public static void RegisterAsset(AssetBehaviorSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        lock (Gate)
        {
            if (RegisteredAssets.TryGetValue(spec.DefinitionId, out AssetBehaviorSpec? existing))
            {
                if (!StructurallyEqual(existing, spec))
                {
                    throw new InvalidOperationException(
                        $"Card definition '{spec.DefinitionId}' is already registered "
                        + "with a different asset behavior spec.");
                }

                return;
            }

            RegisteredAssets.Add(spec.DefinitionId, spec);
        }
    }

    public static void RegisterAsset(IReadOnlyCollection<AssetBehaviorSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        foreach (AssetBehaviorSpec spec in specs)
        {
            RegisterAsset(spec);
        }
    }

    public static bool TryGetAsset(CardDefinitionId definitionId, out AssetBehaviorSpec spec)
    {
        lock (Gate)
        {
            return RegisteredAssets.TryGetValue(definitionId, out spec!);
        }
    }

    public static AssetBehaviorSpec GetAsset(CardDefinitionId definitionId) =>
        TryGetAsset(definitionId, out AssetBehaviorSpec? spec)
            ? spec
            : throw new ArgumentException(
                $"Card definition '{definitionId}' is not registered in the asset behavior spec catalog.",
                nameof(definitionId));

    public static void RegisterAgenda(AgendaBehaviorSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        lock (Gate)
        {
            if (RegisteredAgendas.TryGetValue(spec.DefinitionId, out AgendaBehaviorSpec? existing))
            {
                if (!StructurallyEqual(existing, spec))
                {
                    throw new InvalidOperationException(
                        $"Card definition '{spec.DefinitionId}' is already registered "
                        + "with a different agenda behavior spec.");
                }

                return;
            }

            RegisteredAgendas.Add(spec.DefinitionId, spec);
        }
    }

    public static void RegisterAgenda(IReadOnlyCollection<AgendaBehaviorSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        foreach (AgendaBehaviorSpec spec in specs)
        {
            RegisterAgenda(spec);
        }
    }

    public static bool TryGetAgenda(CardDefinitionId definitionId, out AgendaBehaviorSpec spec)
    {
        lock (Gate)
        {
            return RegisteredAgendas.TryGetValue(definitionId, out spec!);
        }
    }

    public static AgendaBehaviorSpec GetAgenda(CardDefinitionId definitionId) =>
        TryGetAgenda(definitionId, out AgendaBehaviorSpec? spec)
            ? spec
            : throw new ArgumentException(
                $"Card definition '{definitionId}' is not registered in the agenda behavior spec catalog.",
                nameof(definitionId));

    public static void RegisterRunEvent(RunEventBehaviorSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        lock (Gate)
        {
            if (RegisteredRunEvents.TryGetValue(spec.DefinitionId, out RunEventBehaviorSpec? existing))
            {
                if (!StructurallyEqual(existing, spec))
                {
                    throw new InvalidOperationException(
                        $"Card definition '{spec.DefinitionId}' is already registered "
                        + "with a different run event behavior spec.");
                }

                return;
            }

            RegisteredRunEvents.Add(spec.DefinitionId, spec);
        }
    }

    public static void RegisterRunEvent(IReadOnlyCollection<RunEventBehaviorSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        foreach (RunEventBehaviorSpec spec in specs)
        {
            RegisterRunEvent(spec);
        }
    }

    public static bool TryGetRunEvent(CardDefinitionId definitionId, out RunEventBehaviorSpec spec)
    {
        lock (Gate)
        {
            return RegisteredRunEvents.TryGetValue(definitionId, out spec!);
        }
    }

    public static RunEventBehaviorSpec GetRunEvent(CardDefinitionId definitionId) =>
        TryGetRunEvent(definitionId, out RunEventBehaviorSpec? spec)
            ? spec
            : throw new ArgumentException(
                $"Card definition '{definitionId}' is not registered in the run event behavior spec catalog.",
                nameof(definitionId));

    /// <summary>
    /// Structural comparison over scalar fields and the ordered list
    /// contents; spec records holding <see cref="IReadOnlyList{T}"/> do not
    /// deep-compare through their default equality.
    /// </summary>
    private static bool StructurallyEqual(IceBehaviorSpec left, IceBehaviorSpec right) =>
        left.DefinitionId == right.DefinitionId
        && left.Strength == right.Strength
        && left.IceType == right.IceType
        && left.RezCost == right.RezCost
        && SequenceEquals(left.Subroutines, right.Subroutines)
        && SequenceEquals(left.StrengthModifiers, right.StrengthModifiers);

    private static bool StructurallyEqual(IcebreakerBehaviorSpec left, IcebreakerBehaviorSpec right) =>
        left.DefinitionId == right.DefinitionId
        && left.BaseStrength == right.BaseStrength
        && left.InterfaceType == right.InterfaceType
        && left.BreakCost == right.BreakCost
        && left.BreakLimit == right.BreakLimit
        && left.PumpCost == right.PumpCost
        && left.PumpStrength == right.PumpStrength;

    private static bool StructurallyEqual(AssetBehaviorSpec left, AssetBehaviorSpec right) =>
        left.DefinitionId == right.DefinitionId
        && left.TrashCost == right.TrashCost;

    private static bool StructurallyEqual(AgendaBehaviorSpec left, AgendaBehaviorSpec right) =>
        left.DefinitionId == right.DefinitionId
        && left.AgendaPoints == right.AgendaPoints;

    private static bool StructurallyEqual(RunEventBehaviorSpec left, RunEventBehaviorSpec right) =>
        left.DefinitionId == right.DefinitionId
        && left.Cost == right.Cost
        && left.HostedCredits == right.HostedCredits;

    private static bool SequenceEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
        where T : notnull
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
