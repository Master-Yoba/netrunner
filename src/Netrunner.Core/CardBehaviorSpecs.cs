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
        IReadOnlyList<BehaviorValueModifier>? valueModifiers = null,
        IReadOnlyList<EncounteredBioroidAbilitySpec>? encounteredBioroidAbilities = null,
        string? breakableOnlyBySubtype = null)
    {
        DefinitionId = definitionId;
        if (breakableOnlyBySubtype is not null && string.IsNullOrWhiteSpace(breakableOnlyBySubtype))
        {
            throw new ArgumentException("A break restriction names a breaker subtype.", nameof(breakableOnlyBySubtype));
        }

        BreakableOnlyBySubtype = breakableOnlyBySubtype;
        Strength = strength;
        IceType = iceType;
        RezCost = rezCost;
        ArgumentNullException.ThrowIfNull(subroutines);
        ArgumentNullException.ThrowIfNull(strengthModifiers);
        Subroutines = new ReadOnlyCollection<IceSubroutineSpec>(subroutines.ToArray());
        StrengthModifiers = new ReadOnlyCollection<IceStrengthModifierSpec>(strengthModifiers.ToArray());
        ValueModifiers = FreezeValueModifiers(valueModifiers);
        EncounteredBioroidAbilities = FreezeEncounteredBioroidAbilities(encounteredBioroidAbilities);
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

    /// <summary>
    /// Optional Runner interface abilities printed on an encountered bioroid
    /// ICE. The engine materializes these as dedicated encounter actions;
    /// they are deliberately not generic installed-card abilities or
    /// icebreaker declarations.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EncounteredBioroidAbilitySpec>? EncounteredBioroidAbilities { get; }

    /// <summary>
    /// EL-BREAK-RESTRICTION: the printed subroutines can be broken only by an
    /// icebreaker with this subtype (Semak-samun: "except using a fracter").
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BreakableOnlyBySubtype { get; }

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

    private static ReadOnlyCollection<EncounteredBioroidAbilitySpec>? FreezeEncounteredBioroidAbilities(
        IReadOnlyList<EncounteredBioroidAbilitySpec>? abilities)
    {
        if (abilities is null or { Count: 0 })
        {
            return null;
        }

        EncounteredBioroidAbilitySpec[] copy = abilities.ToArray();
        if (copy.Any(static ability => ability is null))
        {
            throw new ArgumentException(
                "Encountered-bioroid ability declarations must not contain null entries.",
                nameof(abilities));
        }

        if (copy.Select(static ability => ability.Key).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException(
                "Encountered-bioroid ability declarations must not repeat an ability key.",
                nameof(abilities));
        }

        return new ReadOnlyCollection<EncounteredBioroidAbilitySpec>(copy);
    }
}

/// <summary>
/// A typed ability that a Runner may use while an ICE carrying the
/// declaration is encountered. This contract is intentionally narrower than
/// a generic paid ability: the ability pays a typed lose-click cost and names
/// one exact unbroken encounter subroutine to break.
/// </summary>
public sealed record EncounteredBioroidAbilitySpec
{
    [JsonConstructor]
    public EncounteredBioroidAbilitySpec(
        string key,
        BehaviorCost cost,
        int breakCount = 1)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException(
                "An encountered-bioroid ability key must be a stable identifier.",
                nameof(key));
        }

        ArgumentNullException.ThrowIfNull(cost);
        if (!IsSupportedCost(cost))
        {
            throw new ArgumentException(
                "An encountered-ICE break ability costs exactly one typed lost click or a positive number of credits.",
                nameof(cost));
        }

        if (breakCount != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(breakCount),
                "An encountered-bioroid ability must break exactly one subroutine.");
        }

        Key = key;
        Cost = cost;
        BreakCount = breakCount;
    }

    public EncounteredBioroidAbilitySpec(string key)
        : this(key, new BehaviorCost(BehaviorCostKind.LoseClick, 1))
    {
    }

    /// <summary>
    /// A lost click (bioroids), or credits (EL-RUNNER-ICE-ABILITY: N-Pot's
    /// "3[credit]: Break 1 subroutine on this ice. Only the Runner can use this ability").
    /// </summary>
    public static bool IsSupportedCost(BehaviorCost cost) =>
        cost.Kind == BehaviorCostKind.LoseClick && cost.Amount == 1
        || cost.Kind == BehaviorCostKind.Credits && cost.Amount > 0;

    [JsonInclude]
    [JsonRequired]
    public string Key { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorCost Cost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int BreakCount { get; private set; }
}

/// <summary>One printed ICE subroutine, in printed order.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(EndRunSubroutineSpec), "end-run")]
[JsonDerivedType(typeof(LoseCreditsSubroutineSpec), "lose-credits")]
[JsonDerivedType(typeof(EndRunIfCreditsAtMostSubroutineSpec), "end-run-if-credits-at-most")]
[JsonDerivedType(typeof(TrashInstalledRunnerCardSubroutineSpec), "trash-installed-runner-card")]
[JsonDerivedType(typeof(InstallCardSubroutineSpec), "install-card")]
[JsonDerivedType(typeof(RunRestrictionSubroutineSpec), "run-restriction")]
[JsonDerivedType(typeof(PayCreditsOrTagSubroutineSpec), "pay-credits-or-tag")]
[JsonDerivedType(typeof(DamageSubroutineSpec), "damage")]
[JsonDerivedType(typeof(GainCreditsSubroutineSpec), "gain-credits")]
[JsonDerivedType(typeof(GiveTagsSubroutineSpec), "give-tags")]
[JsonDerivedType(typeof(BehaviorSubroutineSpec), "behavior")]
public abstract record IceSubroutineSpec;

/// <summary>
/// EL-GENERIC-SUBROUTINE: a subroutine whose effect is the instruction list
/// of a trigger on the ICE's own behavior definition, declared with
/// <see cref="OccurrenceKind.IceSubroutine"/>. It resolves as a behavior child
/// of the encounter with the ICE as its source and the Corp as controller.
/// </summary>
public sealed record BehaviorSubroutineSpec : IceSubroutineSpec
{
    public BehaviorSubroutineSpec(string triggerKey)
    {
        if (!StableIdentifier.TryValidate(triggerKey))
        {
            throw new ArgumentException("A behavior subroutine names a stable trigger key.", nameof(triggerKey));
        }

        TriggerKey = triggerKey;
    }

    public string TriggerKey { get; }
}

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

/// <summary>
/// A Corp-selected installed Runner-card trash subroutine.  A null class
/// filter means any installed Runner card; a concrete class is used by later
/// printed effects such as program-only trash.  The engine resolves the
/// physical target through its issued action ID, never through a title/code
/// branch.
/// </summary>
public sealed record TrashInstalledRunnerCardSubroutineSpec : IceSubroutineSpec
{
    [JsonConstructor]
    public TrashInstalledRunnerCardSubroutineSpec(RuntimeCardClass? requiredCardClass = null, bool endRunAlternative = false)
    {
        if (requiredCardClass is RuntimeCardClass.Ice
            or RuntimeCardClass.Asset
            or RuntimeCardClass.Upgrade
            or RuntimeCardClass.Agenda
            or RuntimeCardClass.Operation
            or RuntimeCardClass.Event
            or RuntimeCardClass.Identity)
        {
            throw new ArgumentException(
                "An installed Runner-card trash filter may target only Runner program, hardware, or resource cards.",
                nameof(requiredCardClass));
        }

        RequiredCardClass = requiredCardClass;
        EndRunAlternative = endRunAlternative;
    }

    /// <summary>Optional Runner runtime class filter; null means any class.</summary>
    [JsonInclude]
    [JsonRequired]
    public RuntimeCardClass? RequiredCardClass { get; private set; }

    public bool EndRunAlternative { get; }
}

/// <summary>Destination rule for a card-installing ICE subroutine.</summary>
public enum IceInstallDestinationRule
{
    Normal,
    DirectlyInward,
}

/// <summary>Cost rule for a card-installing ICE subroutine.</summary>
public enum IceInstallCostRule
{
    Normal,
    IgnoreAllCosts,
}

/// <summary>
/// A Corp card-install subroutine.  Source-zone and destination legality are
/// resolved from the authenticated runtime catalog and the live topology;
/// this declaration only records the printed restrictions.
/// </summary>
public sealed record InstallCardSubroutineSpec : IceSubroutineSpec
{
    [JsonConstructor]
    public InstallCardSubroutineSpec(
        IceInstallDestinationRule destinationRule = IceInstallDestinationRule.Normal,
        IceInstallCostRule costRule = IceInstallCostRule.Normal,
        bool iceOnly = false,
        bool headquartersOnly = false)
    {
        if (!Enum.IsDefined(destinationRule))
        {
            throw new ArgumentOutOfRangeException(nameof(destinationRule));
        }

        if (!Enum.IsDefined(costRule))
        {
            throw new ArgumentOutOfRangeException(nameof(costRule));
        }

        // The executable vocabulary deliberately has exactly two supported
        // contracts.  Keeping destination and cost as independent enum
        // fields is useful for projection, but accepting the other Cartesian
        // combinations would describe semantics for which the reducer has no
        // authenticated implementation.
        if ((destinationRule, costRule) is not
            (IceInstallDestinationRule.Normal, IceInstallCostRule.Normal)
            and not
            (IceInstallDestinationRule.DirectlyInward, IceInstallCostRule.IgnoreAllCosts))
        {
            throw new ArgumentException(
                "ICE install subroutines support only Normal+Normal or DirectlyInward+IgnoreAllCosts.",
                nameof(costRule));
        }

        if (destinationRule == IceInstallDestinationRule.DirectlyInward
            && !iceOnly)
        {
            throw new ArgumentException(
                "A directly inward install must be restricted to ICE.",
                nameof(iceOnly));
        }

        DestinationRule = destinationRule;
        CostRule = costRule;
        IceOnly = iceOnly;
        HeadquartersOnly = headquartersOnly;
    }

    [JsonInclude]
    [JsonRequired]
    public IceInstallDestinationRule DestinationRule { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IceInstallCostRule CostRule { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool IceOnly { get; private set; }

    /// <summary>"Install 1 card from HQ" (Scatter Field), where Ansel 1.0's installs from HQ or Archives.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HeadquartersOnly { get; private set; }

    // The behavior fingerprint prints each subroutine: HeadquartersOnly appears only when set, so every other install
    // subroutine (Ansel 1.0, Brân 1.0) keeps its fingerprint and the games saved with it.
    protected override bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("DestinationRule = ").Append(DestinationRule).Append(", CostRule = ").Append(CostRule)
            .Append(", IceOnly = ").Append(IceOnly);
        if (HeadquartersOnly) builder.Append(", HeadquartersOnly = True");
        return true;
    }
}

/// <summary>Typed restriction applied for the remainder of the active run.</summary>
public enum RunRestrictionKind
{
    CannotStealOrTrashCorpCards,
}

/// <summary>One printed run-scoped restriction subroutine.</summary>
public sealed record RunRestrictionSubroutineSpec : IceSubroutineSpec
{
    [JsonConstructor]
    public RunRestrictionSubroutineSpec(RunRestrictionKind restriction)
    {
        if (!Enum.IsDefined(restriction))
        {
            throw new ArgumentOutOfRangeException(nameof(restriction));
        }

        Restriction = restriction;
    }

    [JsonInclude]
    [JsonRequired]
    public RunRestrictionKind Restriction { get; private set; }
}

/// <summary>
/// A Runner choice subroutine that allows paying a fixed credit amount;
/// declining applies the typed tag fallback.  It is intentionally distinct
/// from a generic triggered "unless" choice because this frame resolves in
/// the printed ICE-subroutine sequence.
/// </summary>
public sealed record PayCreditsOrTagSubroutineSpec : IceSubroutineSpec
{
    [JsonConstructor]
    public PayCreditsOrTagSubroutineSpec(int creditAmount, int tagAmount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(creditAmount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tagAmount);

        CreditAmount = creditAmount;
        TagAmount = tagAmount;
    }

    [JsonInclude]
    [JsonRequired]
    public int CreditAmount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TagAmount { get; private set; }
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
        BehaviorDynamicPumpDeclaration? dynamicPump = null,
        IcebreakerRunEndCleanupSpec? runEndCleanup = null)
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
        RunEndCleanup = runEndCleanup;
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

        // A breaker without a printed credit pump (Hantu) declares pump cost
        // and strength 0; any other credit pump must add strength.
        if (dynamicPump is null && pumpStrength < 0
            || dynamicPump is null && pumpStrength == 0 && pumpCost != 0)
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

    /// <summary>Whether the breaker has a printed credit pump.</summary>
    [JsonIgnore]
    public bool HasPump => DynamicPump is not null || PumpStrength > 0;

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

    /// <summary>
    /// Optional typed run-end cleanup declaration. It identifies the
    /// behavior declaration that may arm a run-scoped cleanup effect; it does
    /// not arm or execute anything by itself.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IcebreakerRunEndCleanupSpec? RunEndCleanup { get; }

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
/// Authenticated declaration for a run-end cleanup created by an
/// icebreaker ability. The source card and declaration are retained in the
/// run effect so cleanup can fail closed if the source later moves zones.
/// </summary>
public sealed record IcebreakerRunEndCleanupSpec
{
    [JsonConstructor]
    public IcebreakerRunEndCleanupSpec(string behaviorId)
    {
        if (!StableIdentifier.TryValidate(behaviorId))
        {
            throw new ArgumentException(
                "An icebreaker run-end cleanup behavior ID must be a stable identifier.",
                nameof(behaviorId));
        }

        BehaviorId = behaviorId;
    }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }
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
    public AgendaBehaviorSpec(CardDefinitionId definitionId, int agendaPoints, int? runnerScoreAreaPoints = null, int stealClicks = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stealClicks);
        StealClicks = stealClicks;
        DefinitionId = definitionId;
        AgendaPoints = agendaPoints;
        if (runnerScoreAreaPoints is <= 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must be worth positive agenda points in the Runner's score area.", nameof(runnerScoreAreaPoints));
        }

        RunnerScoreAreaPoints = runnerScoreAreaPoints;
        if (agendaPoints <= 0)
        {
            throw new ArgumentException(
                $"Card '{definitionId}' must have positive agenda points.", nameof(agendaPoints));
        }
    }

    public CardDefinitionId DefinitionId { get; }

    /// <summary>Agenda points granted when the agenda is scored.</summary>
    public int AgendaPoints { get; }

    /// <summary>
    /// VP-AGENDA-POINTS-MODIFIER: what the agenda is worth while in the Runner's score area, when that differs (Let Them
    /// Dream: one less). Null for an ordinary agenda.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RunnerScoreAreaPoints { get; }

    /// <summary>VP-SCORE-STEAL-COST: the clicks the Runner spends as an additional cost to steal it (Méliès City Luxury Line).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int StealClicks { get; }

    /// <summary>The points the agenda is worth in a side's score area.</summary>
    public int PointsIn(PlayerSide scoreAreaSide) =>
        scoreAreaSide == PlayerSide.Runner && RunnerScoreAreaPoints is { } runnerPoints ? runnerPoints : AgendaPoints;
}

/// <summary>
/// One explicitly compiled, versioned run event behavior spec. Every value is
/// hand-copied from the printed card text; card text is never parsed or
/// interpreted here or in the catalog. A run event starts a run and places
/// hosted credits on the event that are spendable only during that run.
/// </summary>
public sealed record RunEventBehaviorSpec
{
    public RunEventBehaviorSpec(
        CardDefinitionId definitionId,
        int cost,
        int hostedCredits,
        IReadOnlyList<BehaviorRunRezCostModifier>? rezCostModifiers = null,
        IReadOnlyList<ServerKind>? allowedTargetServers = null,
        BehaviorRunSuccessDraw? successDraw = null,
        BehaviorRunBreachAccessModifier? breachAccessModifier = null,
        int additionalClicks = 0,
        ServerKind? redirectApproachTo = null,
        bool preventFirstEndRun = false,
        bool locksCreditPool = false)
    {
        PreventFirstEndRun = preventFirstEndRun;
        LocksCreditPool = locksCreditPool;
        DefinitionId = definitionId;
        Cost = cost;
        HostedCredits = hostedCredits;
        ArgumentOutOfRangeException.ThrowIfNegative(additionalClicks);
        AdditionalClicks = additionalClicks;
        if (redirectApproachTo is not (null or ServerKind.Headquarters or ServerKind.RAndD or ServerKind.Archives))
        {
            throw new ArgumentException("A run redirect names a central server.", nameof(redirectApproachTo));
        }

        RedirectApproachTo = redirectApproachTo;
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

        RezCostModifiers = FreezeRezCostModifiers(rezCostModifiers);
        AllowedTargetServers = FreezeAllowedTargetServers(allowedTargetServers);
        SuccessDraw = successDraw;
        BreachAccessModifier = breachAccessModifier;
    }

    public CardDefinitionId DefinitionId { get; }

    /// <summary>Event cost in credits.</summary>
    public int Cost { get; }

    /// <summary>Credits placed on the event; spendable only during the run.</summary>
    public int HostedCredits { get; }

    /// <summary>
    /// Optional run-duration ICE rez-cost declarations. The nullable shape
    /// preserves legacy event JSON and fingerprints when no modifier exists.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorRunRezCostModifier>? RezCostModifiers { get; }

    /// <summary>
    /// Optional central servers that this event may attack. A populated list
    /// is an allow-list, not a hint; only Headquarters and R&amp;D are valid
    /// targets for this run-event contract. Null means that the event keeps
    /// the legacy unrestricted-server behavior.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ServerKind>? AllowedTargetServers { get; }

    /// <summary>
    /// Optional draw performed after this event's run is declared successful.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorRunSuccessDraw? SuccessDraw { get; }

    /// <summary>
    /// Optional additional access applied when this event's attacked server
    /// is breached.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorRunBreachAccessModifier? BreachAccessModifier { get; }

    /// <summary>Double: the clicks spent as an additional cost to play this event (Maintenance Access).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AdditionalClicks { get; }

    /// <summary>
    /// EL-CHANGE-ATTACKED-SERVER: when the Runner would approach the attacked
    /// server, the attacked server becomes this central server instead, and
    /// the Runner approaches it (Maintenance Access: Archives, then HQ).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerKind? RedirectApproachTo { get; }

    /// <summary>
    /// EL-PREVENT-END-RUN: the first time the Corp would end this run, it ends
    /// only if the Corp reveals and trashes X random HQ cards, X being the
    /// cards in the attacked server's root (Shred).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PreventFirstEndRun { get; }

    /// <summary>
    /// VP-POOL-LOCK: while the event is active the Runner can neither spend nor lose credits from their pool, only the
    /// event's hosted credits and other credits from outside the pool (Aircheck).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LocksCreditPool { get; }

    private static ReadOnlyCollection<BehaviorRunRezCostModifier>? FreezeRezCostModifiers(
        IReadOnlyList<BehaviorRunRezCostModifier>? modifiers)
    {
        if (modifiers is null or { Count: 0 })
        {
            return null;
        }

        BehaviorRunRezCostModifier[] copy = modifiers.ToArray();
        if (copy.Any(static modifier => modifier is null))
        {
            throw new ArgumentException(
                "Run event rez-cost modifier declarations must not contain null entries.",
                nameof(modifiers));
        }

        if (copy
            .GroupBy(static modifier => (modifier.Delta, modifier.ReasonKey))
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Run event rez-cost modifier declarations must not repeat a delta and reason key.",
                nameof(modifiers));
        }

        return new ReadOnlyCollection<BehaviorRunRezCostModifier>(copy);
    }

    private static ReadOnlyCollection<ServerKind>? FreezeAllowedTargetServers(
        IReadOnlyList<ServerKind>? servers)
    {
        if (servers is null or { Count: 0 })
        {
            return null;
        }

        ServerKind[] copy = servers.ToArray();
        if (copy.Any(static server => server is not (ServerKind.Headquarters or ServerKind.RAndD or ServerKind.Archives)))
        {
            throw new ArgumentException(
                "Run event target declarations may name only central servers.",
                nameof(servers));
        }

        if (copy.Distinct().Count() != copy.Length)
        {
            throw new ArgumentException(
                "Run event target declarations must not repeat a server.",
                nameof(servers));
        }

        return new ReadOnlyCollection<ServerKind>(copy);
    }
}

/// <summary>The printed continuation after damage finishes its checkpoint.</summary>
public enum DamageSubroutineFollowUp
{
    None,
    EndRunIfTrashedCostOdd,
    OfferJackOut,
}

public sealed record DamageSubroutineSpec : IceSubroutineSpec
{
    public DamageSubroutineSpec(DamageKind kind, int amount, DamageSubroutineFollowUp followUp = DamageSubroutineFollowUp.None)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(followUp)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        Kind = kind;
        Amount = amount;
        FollowUp = followUp;
    }

    public DamageKind Kind { get; }
    public int Amount { get; }
    public DamageSubroutineFollowUp FollowUp { get; }
}

public sealed record GainCreditsSubroutineSpec : IceSubroutineSpec
{
    public GainCreditsSubroutineSpec(PlayerSide target, int amount)
    {
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        Target = target;
        Amount = amount;
    }

    public PlayerSide Target { get; }
    public int Amount { get; }
}

public sealed record GiveTagsSubroutineSpec : IceSubroutineSpec
{
    public GiveTagsSubroutineSpec(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        Amount = amount;
    }

    public int Amount { get; }
}
