namespace Netrunner.Core.Tests;

/// <summary>
/// Immutable fixture behavior declarations. Tests that exercise these cards
/// inject <see cref="FixtureBehaviorCatalog.Catalog"/> explicitly; loading
/// the test assembly no longer mutates a process-wide Core registry.
/// </summary>
internal static class FixtureIceBehaviorSpecs
{
    public static readonly CardDefinitionId Plain = new("fixture:ice-plain");
    public static readonly CardDefinitionId TwoSubroutines = new("fixture:ice-two-subroutines");
    public static readonly CardDefinitionId RezCost = new("fixture:ice-rez-cost");
    public static readonly CardDefinitionId Strong = new("fixture:ice-strong");
    public static readonly CardDefinitionId LoseCreditsOnly = new("fixture:ice-lose-credits");
    public static readonly CardDefinitionId EndRunFirst = new("fixture:ice-end-run-first");
    public static readonly CardDefinitionId EndRunIfCredits = new("fixture:ice-end-run-if-credits");
    public static readonly CardDefinitionId ThreeSubroutines = new("fixture:ice-three-subroutines");
    public static readonly CardDefinitionId CodeGateLoseCredits = new("fixture:ice-codegate-lose-credits");
    public static readonly CardDefinitionId SentryLoseCredits = new("fixture:ice-sentry-lose-credits");
    public static readonly CardDefinitionId BreakerBarrier = new("fixture:breaker-barrier");
    public static readonly CardDefinitionId BreakerCodeGate = new("fixture:breaker-codgate");
    public static readonly CardDefinitionId BreakerSentry = new("fixture:breaker-sentry");
    public static readonly CardDefinitionId BreakerAny = new("fixture:breaker-any");

    private static readonly IceBehaviorSpec PlainSpec = new(
        Plain,
        strength: 0,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines: [],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec TwoSubroutinesSpec = new(
        TwoSubroutines,
        strength: 0,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines:
        [
            new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
            new EndRunSubroutineSpec(),
        ],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec RezCostSpec = new(
        RezCost,
        strength: 0,
        iceType: IceType.Barrier,
        rezCost: 2,
        subroutines: [],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec StrongSpec = new(
        Strong,
        strength: 4,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines: [new EndRunSubroutineSpec()],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec LoseCreditsOnlySpec = new(
        LoseCreditsOnly,
        strength: 0,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2)],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec EndRunFirstSpec = new(
        EndRunFirst,
        strength: 0,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines:
        [
            new EndRunSubroutineSpec(),
            new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
        ],
        strengthModifiers: []);

    // Three subroutines let a break limit of 2 leave one unbroken for the
    // encounter-resolution tests.
    private static readonly IceBehaviorSpec ThreeSubroutinesSpec = new(
        ThreeSubroutines,
        strength: 0,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines:
        [
            new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
            new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
            new EndRunSubroutineSpec(),
        ],
        strengthModifiers: []);

    // A code gate carrying the credit-loss subroutine lets the code gate rig
    // breaker encounter a subtype it is eligible to break.
    private static readonly IceBehaviorSpec CodeGateLoseCreditsSpec = new(
        CodeGateLoseCredits,
        strength: 0,
        iceType: IceType.CodeGate,
        rezCost: 0,
        subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 3)],
        strengthModifiers: []);

    // Mirrors Whitespace's printed subroutine economics.
    private static readonly IceBehaviorSpec EndRunIfCreditsSpec = new(
        EndRunIfCredits,
        strength: 0,
        iceType: IceType.CodeGate,
        rezCost: 0,
        subroutines:
        [
            new LoseCreditsSubroutineSpec(PlayerSide.Runner, 3),
            new EndRunIfCreditsAtMostSubroutineSpec(PlayerSide.Runner, 6),
        ],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec SentryLoseCreditsSpec = new(
        SentryLoseCredits,
        strength: 0,
        iceType: IceType.Sentry,
        rezCost: 0,
        subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)],
        strengthModifiers: []);

    private static readonly IcebreakerBehaviorSpec BreakerBarrierSpec = new(
        BreakerBarrier,
        baseStrength: 3,
        interfaceType: IceType.Barrier,
        breakCost: 1,
        breakLimit: 2,
        pumpCost: 2,
        pumpStrength: 1);

    private static readonly IcebreakerBehaviorSpec BreakerCodeGateSpec = new(
        BreakerCodeGate,
        baseStrength: 3,
        interfaceType: IceType.CodeGate,
        breakCost: 1,
        breakLimit: 2,
        pumpCost: 2,
        pumpStrength: 1);

    private static readonly IcebreakerBehaviorSpec BreakerSentrySpec = new(
        BreakerSentry,
        baseStrength: 3,
        interfaceType: IceType.Sentry,
        breakCost: 1,
        breakLimit: 2,
        pumpCost: 2,
        pumpStrength: 1);

    private static readonly IcebreakerBehaviorSpec BreakerAnySpec = new(
        BreakerAny,
        baseStrength: 3,
        interfaceType: IceType.Any,
        breakCost: 1,
        breakLimit: 2,
        pumpCost: 2,
        pumpStrength: 1);

    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } =
    [
        PlainSpec,
        TwoSubroutinesSpec,
        RezCostSpec,
        StrongSpec,
        LoseCreditsOnlySpec,
        EndRunFirstSpec,
        ThreeSubroutinesSpec,
        CodeGateLoseCreditsSpec,
        EndRunIfCreditsSpec,
        SentryLoseCreditsSpec,
    ];

    public static IReadOnlyList<IcebreakerBehaviorSpec> IcebreakerSpecs { get; } =
    [
        BreakerBarrierSpec,
        BreakerCodeGateSpec,
        BreakerSentrySpec,
        BreakerAnySpec,
    ];
}
