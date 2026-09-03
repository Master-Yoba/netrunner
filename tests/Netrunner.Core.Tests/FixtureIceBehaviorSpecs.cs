using System.Runtime.CompilerServices;

namespace Netrunner.Core.Tests;

/// <summary>
/// Fixture ICE behavior specs registered into the shared catalog at test
/// assembly load, so Core tests can exercise registered-ICE encounter
/// initialization without referencing the Cards project.
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

    [ModuleInitializer]
    internal static void Initialize()
    {
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            Plain,
            strength: 0,
            iceType: IceType.Barrier,
            rezCost: 0,
            subroutines: Array.Empty<IceSubroutineSpec>(),
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            TwoSubroutines,
            strength: 0,
            iceType: IceType.Barrier,
            rezCost: 0,
            subroutines: new IceSubroutineSpec[]
            {
                new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
                new EndRunSubroutineSpec(),
            },
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            RezCost,
            strength: 0,
            iceType: IceType.Barrier,
            rezCost: 2,
            subroutines: Array.Empty<IceSubroutineSpec>(),
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            Strong,
            strength: 4,
            iceType: IceType.Barrier,
            rezCost: 0,
            subroutines: new IceSubroutineSpec[] { new EndRunSubroutineSpec() },
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            LoseCreditsOnly,
            strength: 0,
            iceType: IceType.Barrier,
            rezCost: 0,
            subroutines: new IceSubroutineSpec[]
            {
                new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
            },
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            EndRunFirst,
            strength: 0,
            iceType: IceType.Barrier,
            rezCost: 0,
            subroutines: new IceSubroutineSpec[]
            {
                new EndRunSubroutineSpec(),
                new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
            },
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        // Three subroutines so a break limit of 2 can be exhausted with an
        // unbroken subroutine left for the encounter-resolution tests.
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            ThreeSubroutines,
            strength: 0,
            iceType: IceType.Barrier,
            rezCost: 0,
            subroutines: new IceSubroutineSpec[]
            {
                new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
                new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
                new EndRunSubroutineSpec(),
            },
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        // A code gate carrying the credit-loss subroutine so the code gate
        // rig breaker can encounter a subtype it is eligible to break.
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            CodeGateLoseCredits,
            strength: 0,
            iceType: IceType.CodeGate,
            rezCost: 0,
            subroutines: new IceSubroutineSpec[]
            {
                new LoseCreditsSubroutineSpec(PlayerSide.Runner, 3),
            },
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        // Mirrors Whitespace's printed subroutine economics: lose 3 credits,
        // then end the run if the Runner has 6 or fewer credits.
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            EndRunIfCredits,
            strength: 0,
            iceType: IceType.CodeGate,
            rezCost: 0,
            subroutines: new IceSubroutineSpec[]
            {
                new LoseCreditsSubroutineSpec(PlayerSide.Runner, 3),
                new EndRunIfCreditsAtMostSubroutineSpec(PlayerSide.Runner, 6),
            },
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            SentryLoseCredits,
            strength: 0,
            iceType: IceType.Sentry,
            rezCost: 0,
            subroutines: new IceSubroutineSpec[]
            {
                new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1),
            },
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>()));
        CardBehaviorSpecCatalog.RegisterIcebreaker(new IcebreakerBehaviorSpec(
            BreakerBarrier,
            baseStrength: 3,
            interfaceType: IceType.Barrier,
            breakCost: 1,
            breakLimit: 2,
            pumpCost: 2,
            pumpStrength: 1));
        CardBehaviorSpecCatalog.RegisterIcebreaker(new IcebreakerBehaviorSpec(
            BreakerCodeGate,
            baseStrength: 3,
            interfaceType: IceType.CodeGate,
            breakCost: 1,
            breakLimit: 2,
            pumpCost: 2,
            pumpStrength: 1));
        CardBehaviorSpecCatalog.RegisterIcebreaker(new IcebreakerBehaviorSpec(
            BreakerSentry,
            baseStrength: 3,
            interfaceType: IceType.Sentry,
            breakCost: 1,
            breakLimit: 2,
            pumpCost: 2,
            pumpStrength: 1));
        CardBehaviorSpecCatalog.RegisterIcebreaker(new IcebreakerBehaviorSpec(
            BreakerAny,
            baseStrength: 3,
            interfaceType: IceType.Any,
            breakCost: 1,
            breakLimit: 2,
            pumpCost: 2,
            pumpStrength: 1));
    }
}
