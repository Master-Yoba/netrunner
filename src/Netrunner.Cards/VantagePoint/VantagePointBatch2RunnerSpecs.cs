using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// VP.5 Runner cards: runs, ICE and access. CR-26.03 citations are in <c>data/vantage-point/batch-registry.json</c>.
/// </summary>
public static class VantagePointBatch2RunnerSpecs
{
    private const string Stealth = "stealth";

    private static readonly BehaviorStaticModifier PlusOneMemory = new(BehaviorStaticModifierKind.MemoryUnits, delta: 1);

    private static readonly BehaviorCardFilter CorpIce = new(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp);

    // 36001 — VP-CR-9.6-conditional. VP-RUN-FACTS: successful runs on HQ, R&D and Archives this turn.
    private static readonly CardBehaviorDefinition ChainReaction = new(
        VantagePointCardCodes.ChainReaction,
        PlayerSide.Runner,
        costs: [Credits(1)],
        conditions: [new BehaviorCondition(BehaviorConditionKind.SuccessfulRunOnEveryCentralThisTurn)],
        instructions:
        [
            ChooseCards("chain-reaction-corp", Installed(new BehaviorCardFilter(ownerSide: PlayerSide.Corp)), BehaviorChoiceCardinality.AsManyAsAble(2)),
            new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("chain-reaction-corp")),
            ChooseCards("chain-reaction-runner", Installed(new BehaviorCardFilter(ownerSide: PlayerSide.Runner)), BehaviorChoiceCardinality.AsManyAsAble(1),
                BehaviorChooser.Corp),
            new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("chain-reaction-runner")),
        ]);

    // 36003 — VP-CR-6.5.7-pass-and-break; VP-CR-1.9-counters. VP-ALL-ICE-STRENGTH and VP-SUBROUTINE-BROKEN.
    private static readonly CardBehaviorDefinition TheTungstenTailor = new(
        VantagePointCardCodes.TheTungstenTailor,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("tungsten-tailor-broken", OccurrenceKind.SubroutineBroken, BehaviorTriggerScope.Controller, [Gain(1)],
                oncePerTurn: true, condition: BehaviorTriggerCondition.SubjectStrengthAtMostZero),
        ],
        allIceStrength: -1);

    // 36005 — VP-CR-1.10.4-credit-sources; VP-CR-7.1-access-abilities; VP-CR-1.9-counters. VP-ACCESS-PAID-ABILITY, paid
    // only with credits from stealth cards.
    private static readonly CardBehaviorDefinition Lampades = new(
        VantagePointCardCodes.Lampades,
        PlayerSide.Runner,
        triggers: [Trigger("lampades-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self, [AddCounter(PowerCounter, 3)])],
        paidAbilities:
        [
            PaidAbility(
                "lampades-trash",
                BehaviorAbilityTiming.BreachAccessPaidWindow,
                [CounterCost(PowerCounter), new BehaviorCost(BehaviorCostKind.Credits, 0, creditSourceSubtype: Stealth, accessedCardPrintedCost: true)],
                [new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: new BehaviorTarget(BehaviorTargetKind.AccessedCard))],
                targets: [new BehaviorTarget(BehaviorTargetKind.AccessedCard)]),
        ]);

    // 36008 — VP-CR-9.8-subroutines; VP-CR-10.4-damage. VP-ICE-GAINS-SUBROUTINE, before the ICE's other subroutines.
    private static readonly CardBehaviorDefinition StickAndPoke = new(
        VantagePointCardCodes.StickAndPoke,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("stick-and-poke-encounter", OccurrenceKind.IceEncountered, BehaviorTriggerScope.Controller,
                [new BehaviorInstruction(BehaviorInstructionKind.EncounteredIceGainsSubroutine)], oncePerTurn: true),
        ],
        gainedIceSubroutine: new DamageSubroutineSpec(DamageKind.Net, 1, DamageSubroutineFollowUp.RunnerDrawsOne));

    // 36010 — VP-CR-10.6-bad-publicity; VP-CR-8.1.3-derez. VP-ENDED-RUN-SERVER. Removed from the game; a server
    // protected by ICE.
    private static readonly RunEventBehaviorSpec KompromatRun = new(
        VantagePointCardCodes.Kompromat, cost: 2, hostedCredits: 0, removedFromGameAfterRun: true, targetMustBeProtected: true);

    private static readonly CardBehaviorDefinition Kompromat = new(
        VantagePointCardCodes.Kompromat,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "kompromat-run-end",
                OccurrenceKind.RunEnded,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                null,
                [
                    ChooseOne(
                        "kompromat-unless",
                        BehaviorChooser.Corp,
                        Option("derez",
                        [
                            ChooseCards("kompromat-ice",
                                new BehaviorTarget(BehaviorTargetKind.InstalledCard, BehaviorTarget.AttackedServerSelector,
                                    filter: new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp, rezzed: true)),
                                BehaviorChoiceCardinality.Exactly(1), BehaviorChooser.Corp),
                            new BehaviorInstruction(BehaviorInstructionKind.Derez, target: ChosenBy("kompromat-ice")),
                        ]),
                        Option("bad-publicity", [new BehaviorInstruction(BehaviorInstructionKind.TakeBadPublicity, amount: 1)])),
                ],
                runOutcomeRequirement: RunOutcomeRequirement.Successful),
        ]);

    // 36014 — VP-CR-10.5-tags; VP-CR-9.5-paid-abilities. A console (one per player). VP-OPPONENT-ABILITY: only the
    // Corp trashes it, and only while the Runner is tagged.
    private static BehaviorTrigger RotaryBreach(string key, ServerKind server) =>
        Trigger(key, OccurrenceKind.RunSuccessful, BehaviorTriggerScope.Controller,
            [GiveTags(1), new BehaviorInstruction(BehaviorInstructionKind.AddBreachAccess, amount: 1, argument: "rotary-access")],
            TriggerMode.Optional,
            server: server);

    private static readonly CardBehaviorDefinition Rotary = new(
        VantagePointCardCodes.Rotary,
        PlayerSide.Runner,
        staticModifiers: [PlusOneMemory],
        triggers: [RotaryBreach("rotary-hq", ServerKind.Headquarters), RotaryBreach("rotary-rd", ServerKind.RAndD)],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "rotary-trash",
                BehaviorAbilityTiming.ActionPhase,
                BehaviorAbilityActivation.ClickAction,
                [Click(), Credits(2)],
                [BehaviorCondition.Tagged(PlayerSide.Runner)],
                [],
                [TrashSelf()],
                BehaviorAbilityUsageLimit.Unlimited,
                usedByOpponent: true),
        ]);

    // 36015 — VP-CR-1.10.4-credit-sources; VP-CR-9.5-paid-abilities. VP-PAID-REDIRECT, paid only with credits from stealth
    // cards.
    private static BehaviorChoiceOption BakerRedirect(string key, ServerKind server) =>
        Option(key, [new BehaviorInstruction(BehaviorInstructionKind.ChangeAttackedServer, argument: server.ToString())],
            costs: [new BehaviorCost(BehaviorCostKind.Credits, 1, creditSourceSubtype: Stealth)]);

    private static readonly CardBehaviorDefinition Baker = new(
        VantagePointCardCodes.Baker,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("baker-redirect", OccurrenceKind.ServerWouldBeApproached, BehaviorTriggerScope.Controller,
                [ChooseOne("baker-server", BehaviorChooser.Controller, BakerRedirect("hq", ServerKind.Headquarters),
                    BakerRedirect("rd", ServerKind.RAndD), Decline())],
                condition: BehaviorTriggerCondition.RunStartedBySource),
        ],
        paidAbilities:
        [
            ClickAbility("baker-run", [Click()],
                [new BehaviorInstruction(BehaviorInstructionKind.StartRun, runOperand: new BehaviorRunOperand(ServerKind.Archives))],
                BehaviorAbilityUsageLimit.OncePerTurn),
        ]);

    // 36018 — VP-CR-1.10.4-credit-sources; VP-CR-9.6-conditional. VP-STEALTH-CREDITS, VP-POOL-LOCK and VP-RUN-AFTER-RUN.
    private static readonly RunEventBehaviorSpec AircheckRun = new(
        VantagePointCardCodes.Aircheck, cost: 1, hostedCredits: 4,
        allowedTargetServers: [ServerKind.Headquarters, ServerKind.RAndD], locksCreditPool: true);

    private static readonly CardBehaviorDefinition Aircheck = new(
        VantagePointCardCodes.Aircheck,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "aircheck-run-again",
                OccurrenceKind.RunEnded,
                TriggerMode.Optional,
                BehaviorTriggerScope.Self,
                null,
                [
                    ChooseCards("aircheck-server", new BehaviorTarget(BehaviorTargetKind.InstalledCard, BehaviorTarget.RemoteServerSelector),
                        BehaviorChoiceCardinality.Exactly(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.RunAfterRun, target: ChosenBy("aircheck-server")),
                ],
                runOutcomeRequirement: RunOutcomeRequirement.Successful),
        ]);

    // 36019 — VP-CR-8.7-search; VP-CR-8.5-install; VP-CR-9.6-conditional. VP-RETURN-AT-RUN-END.
    private static readonly RunEventBehaviorSpec BetaBuildRun = new(VantagePointCardCodes.BetaBuild, cost: 3, hostedCredits: 0);

    private static readonly CardBehaviorDefinition BetaBuild = new(
        VantagePointCardCodes.BetaBuild,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("beta-build-install", OccurrenceKind.RunBegan, BehaviorTriggerScope.Controller,
            [
                new BehaviorInstruction(BehaviorInstructionKind.InstallCard, argument: "beta-build-program",
                    target: BehaviorTarget.CardInZone(BehaviorZone.Stack,
                        new BehaviorCardFilter(BehaviorCardType.Program, ownerSide: PlayerSide.Runner, excludedSubtype: "virus")),
                    sourceZone: BehaviorZone.Stack, costRule: BehaviorInstallCostRule.IgnoreAllCosts),
                new BehaviorInstruction(BehaviorInstructionKind.RememberForRun, argument: "beta-build-program", target: ChosenBy("beta-build-program")),
            ]),
            Trigger("beta-build-return", OccurrenceKind.RunEnded, BehaviorTriggerScope.Self,
                [new BehaviorInstruction(BehaviorInstructionKind.PlaceOnTopOfDeck,
                    target: new BehaviorTarget(BehaviorTargetKind.RememberedCard, selector: "beta-build-program"))]),
        ]);

    // 36023 — VP-CR-6.5.7-pass-and-break; VP-CR-8.8-swap. VP-ICE-PASSED and VP-SWAP-WITH: the passed ICE, after fully
    // breaking it, with another installed piece of ICE.
    private static readonly CardBehaviorDefinition Sipa = new(
        VantagePointCardCodes.Sipa,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("sipa-swap", OccurrenceKind.IcePassed, BehaviorTriggerScope.Controller,
            [
                ChooseCards("sipa-other", Installed(CorpIce), BehaviorChoiceCardinality.Exactly(1)),
                new BehaviorInstruction(BehaviorInstructionKind.SwapInstalledIce, argument: "trigger-subject", target: ChosenBy("sipa-other")),
            ],
                TriggerMode.Optional,
                oncePerTurn: true,
                condition: BehaviorTriggerCondition.SubjectOutermostFullyBroken),
        ]);

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        ChainReaction, TheTungstenTailor, Lampades, StickAndPoke, Kompromat, Rotary, Baker, Aircheck, BetaBuild, Sipa,
    ];

    public static IReadOnlyList<RunEventBehaviorSpec> RunEventSpecs { get; } = [KompromatRun, AircheckRun, BetaBuildRun];
}
