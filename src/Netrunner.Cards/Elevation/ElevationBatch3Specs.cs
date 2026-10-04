using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// EL.7 Batch 3: runs, ICE and encounters. CR-26.03 citations are in
/// <c>data/elevation/batch-registry.json</c>.
/// </summary>
public static class ElevationBatch3Specs
{
    private static readonly BehaviorCardFilter CorpCard = new(ownerSide: PlayerSide.Corp);

    // 35079 — EL-REZ-DURING-RUN; EL-PURGE.
    private static readonly IceBehaviorSpec FlyswatterIce = new(
        ElevationCardCodes.Flyswatter,
        strength: 0,
        IceType.CodeGate,
        rezCost: 2,
        [new EndRunSubroutineSpec()],
        []);

    private static readonly CardBehaviorDefinition Flyswatter = new(
        ElevationCardCodes.Flyswatter,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "flyswatter-purge",
                OccurrenceKind.CardRezzed,
                BehaviorTriggerScope.Self,
                [new BehaviorInstruction(BehaviorInstructionKind.PurgeVirusCounters)],
                onAttackedServer: true),
        ]);

    // 35041 — EL-REZ-DURING-RUN; EL-INSTALLED-SUBTYPE; a bioroid break ability.
    private static readonly IceBehaviorSpec BumiIce = new(
        ElevationCardCodes.Bumi,
        strength: 3,
        IceType.Sentry,
        rezCost: 3,
        [
            new TrashInstalledRunnerCardSubroutineSpec(RuntimeCardClass.Program),
            new DamageSubroutineSpec(DamageKind.Core, 1),
        ],
        [],
        encounteredBioroidAbilities: [new EncounteredBioroidAbilitySpec("bumi-click-break")]);

    private static readonly CardBehaviorDefinition Bumi = new(
        ElevationCardCodes.Bumi,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "bumi-trash-trojan",
                OccurrenceKind.CardRezzed,
                BehaviorTriggerScope.Self,
                [
                    ChooseCards(
                        "bumi-trojan",
                        Installed(new BehaviorCardFilter(BehaviorCardType.Program, subtype: "trojan", ownerSide: PlayerSide.Runner)),
                        BehaviorChoiceCardinality.UpTo(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("bumi-trojan")),
                ],
                TriggerMode.Optional,
                onAttackedServer: true),
        ]);

    // 35052 — EL-CR-9.8-subroutines.
    private static readonly IceBehaviorSpec EmpiricistIce = new(
        ElevationCardCodes.Empiricist,
        strength: 5,
        IceType.Sentry,
        rezCost: 7,
        [
            new BehaviorSubroutineSpec("empiricist-draw"),
            new BehaviorSubroutineSpec("empiricist-damage-tag"),
            new DamageSubroutineSpec(DamageKind.Net, 2),
        ],
        []);

    private static readonly CardBehaviorDefinition Empiricist = new(
        ElevationCardCodes.Empiricist,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine(
                "empiricist-draw",
                [
                    Draw(1),
                    ChooseCards("empiricist-top", BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, CorpCard), BehaviorChoiceCardinality.UpTo(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.PlaceOnTopOfDeck, target: ChosenBy("empiricist-top")),
                ]),
            Subroutine("empiricist-damage-tag", [NetDamage(1), GiveTags(1)]),
        ]);

    // 35074 — the printed subroutines; the forfeit payment is EL-REZ-COST-PAYMENT.
    private static readonly IceBehaviorSpec BiawakIce = new(
        ElevationCardCodes.Biawak,
        strength: 6,
        IceType.Sentry,
        rezCost: 14,
        [
            new TrashInstalledRunnerCardSubroutineSpec(RuntimeCardClass.Program, endRunAlternative: true),
            new TrashInstalledRunnerCardSubroutineSpec(RuntimeCardClass.Resource, endRunAlternative: true),
            new EndRunSubroutineSpec(),
        ],
        []);

    private static readonly CardBehaviorDefinition Biawak = new(
        ElevationCardCodes.Biawak,
        PlayerSide.Corp,
        rezForfeitDiscount: 10);

    // 35002 — EL-INSTALLED-THAT-CARD-TRIGGER: the install is keyed, and the
    // damage follows only when a card was installed.
    private static readonly CardBehaviorDefinition Topan = new(
        ElevationCardCodes.Topan,
        PlayerSide.Runner,
        paidAbilities:
        [
            ClickAbility(
                "topan-install",
                [Click()],
                [
                    InstallFrom(BehaviorZone.Grip, PlayerSide.Runner, optional: false, discount: 2, key: "topan-installed"),
                    Branch(BehaviorCondition.Chose("topan-installed"),
                        [new BehaviorInstruction(BehaviorInstructionKind.DealDamage, amount: 1, damageKind: DamageKind.Meat)]),
                ],
                BehaviorAbilityUsageLimit.OncePerTurn,
                conditions: [BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Grip, 1)]),
        ]);

    // 35001 — EL-SUBROUTINE-RESOLVED-FACT. The Corp chooses the HQ card.
    private static readonly CardBehaviorDefinition RyoPhoenixOno = new(
        ElevationCardCodes.RyoPhoenixOno,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "ryo-success",
                OccurrenceKind.RunSuccessful,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Controller,
                null,
                [
                    Gain(1),
                    // "The Corp trashes 1 card from HQ": the Corp chooses which, but must trash one (bot loop 1).
                    Branch(BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Headquarters, 1),
                    [
                        ChooseCards("ryo-hq", BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, CorpCard),
                            BehaviorChoiceCardinality.Exactly(1), BehaviorChooser.Corp),
                        new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("ryo-hq")),
                    ]),
                ],
                oncePerTurn: true,
                condition: BehaviorTriggerCondition.SubroutineResolvedThisRun),
        ]);

    // 35012 — the Corp rezzes a piece of ICE; EL-BEHAVIOR-INSTALL from the grip.
    private static readonly CardBehaviorDefinition BarryBazWong = new(
        ElevationCardCodes.BarryBazWong,
        PlayerSide.Runner,
        triggers:
        [
            Trigger(
                "barry-rez-install",
                OccurrenceKind.CardRezzed,
                BehaviorTriggerScope.Any,
                [InstallFrom(BehaviorZone.Grip, PlayerSide.Runner, BehaviorCardType.ResourceOrHardware)],
                TriggerMode.Optional,
                subject: new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp),
                occurrenceController: PlayerSide.Corp),
        ]);

    // 35064 — EL-RUNNER-ICE-ABILITY (a credit break only the Runner uses); EL-THREAT.
    private static readonly IceBehaviorSpec NPotIce = new(
        ElevationCardCodes.NPot,
        strength: 4,
        IceType.CodeGate,
        rezCost: 4,
        [
            new EndRunSubroutineSpec(),
            new BehaviorSubroutineSpec("npot-threat-2"),
            new BehaviorSubroutineSpec("npot-threat-4"),
        ],
        [],
        encounteredBioroidAbilities: [new EncounteredBioroidAbilitySpec("npot-credit-break", Credits(3))]);

    private static readonly CardBehaviorDefinition NPot = new(
        ElevationCardCodes.NPot,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("npot-threat-2", [Branch(BehaviorCondition.ThreatLevelAtLeast(2), [new BehaviorInstruction(BehaviorInstructionKind.EndRun)])]),
            Subroutine("npot-threat-4", [Branch(BehaviorCondition.ThreatLevelAtLeast(4), [new BehaviorInstruction(BehaviorInstructionKind.EndRun)])]),
        ]);

    // 35054 — EL-BREAK-RESTRICTION; the Runner chooses damage or the run's end.
    private static readonly IceBehaviorSpec SemakSamunIce = new(
        ElevationCardCodes.SemakSamun,
        strength: 3,
        IceType.Barrier,
        rezCost: 3,
        [new BehaviorSubroutineSpec("semak-samun-damage-or-end")],
        [],
        breakableOnlyBySubtype: "fracter");

    private static readonly CardBehaviorDefinition SemakSamun = new(
        ElevationCardCodes.SemakSamun,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine(
                "semak-samun-damage-or-end",
                [
                    ChooseOne(
                        "semak-samun-choice",
                        BehaviorChooser.Runner,
                        // The Runner suffers it (rules 10.4.1): the Corp doesn't do this damage (AU Co. gains nothing).
                        Option("suffer-damage", [new BehaviorInstruction(BehaviorInstructionKind.DealDamage, amount: 3,
                            damageKind: DamageKind.Net, recipient: BehaviorPlayerSelector.Runner)]),
                        Option("end-run", [new BehaviorInstruction(BehaviorInstructionKind.EndRun)])),
                ]),
        ]);

    // 35042 — EL-CONDITIONAL-STRENGTH; an install subroutine.
    private static readonly IceBehaviorSpec ScatterFieldIce = new(
        ElevationCardCodes.ScatterField,
        strength: 0,
        IceType.CodeGate,
        rezCost: 3,
        [new InstallCardSubroutineSpec(headquartersOnly: true), new EndRunSubroutineSpec()],
        [],
        valueModifiers:
        [
            new BehaviorValueModifier(
                BehaviorValueKind.Strength,
                BehaviorValueSourceKind.OnlyIceProtectingServer,
                BehaviorValueOperation.Add,
                4,
                "scatter-field-only-ice"),
        ]);

    // 35030 — installed only on a piece of ICE; EL-ICE-GAINS-SUBTYPE.
    private static readonly CardBehaviorDefinition Chromatophores = new(
        ElevationCardCodes.Chromatophores,
        PlayerSide.Runner,
        hosting: new HostingBehaviorSpec(hostGainsEveryIceType: true));

    // 35048 — EL-SCORE-AREA-ABILITY; EL-MOVE-RUNNER.
    private static readonly CardBehaviorDefinition Proprionegation = new(
        ElevationCardCodes.Proprionegation,
        PlayerSide.Corp,
        triggers: [Score("proprionegation-score", [AddCounter(AgendaCounter, 1)])],
        paidAbilities:
        [
            PaidAbility(
                "proprionegation-move",
                BehaviorAbilityTiming.AnyPaidWindow,
                [CounterCost(AgendaCounter)],
                [new BehaviorInstruction(BehaviorInstructionKind.MoveRunnerToServer, argument: nameof(ServerKind.Archives))],
                conditions: [new BehaviorCondition(BehaviorConditionKind.RunInProgress)]),
        ]);

    // 35019 — EL-DEREZ.
    private static readonly CardBehaviorDefinition MaglectricRapid = new(
        ElevationCardCodes.MaglectricRapid,
        PlayerSide.Runner,
        triggers:
        [
            Trigger(
                "maglectric-derez",
                OccurrenceKind.RunSuccessful,
                BehaviorTriggerScope.Controller,
                [
                    ChooseCards(
                        "maglectric-target",
                        Installed(new BehaviorCardFilter(ownerSide: PlayerSide.Corp, rezzed: true)),
                        BehaviorChoiceCardinality.Exactly(1)),
                    TrashSelf(),
                    new BehaviorInstruction(BehaviorInstructionKind.Derez, target: ChosenBy("maglectric-target")),
                ],
                TriggerMode.Optional,
                server: ServerKind.Headquarters),
        ]);

    // 35003 — EL-COPY-OF-ACCESSED; EL-RUN-EVENT-RUN-END. A successful
    // Archives run accesses every card there, so the copies are of the
    // faceup cards in Archives.
    private static readonly CardBehaviorDefinition CharmOffensive = new(
        ElevationCardCodes.CharmOffensive,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "charm-offensive-run-end",
                OccurrenceKind.RunEnded,
                TriggerMode.Optional,
                BehaviorTriggerScope.Self,
                null,
                [
                    ChooseCards(
                        "charm-offensive-copy",
                        new BehaviorTarget(
                            BehaviorTargetKind.InstalledCard,
                            selector: BehaviorTarget.CopyOfArchivesSelector,
                            filter: new BehaviorCardFilter(ownerSide: PlayerSide.Corp, rezzed: true)),
                        BehaviorChoiceCardinality.UpTo(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("charm-offensive-copy")),
                ],
                runOutcomeRequirement: RunOutcomeRequirement.Successful),
        ]);

    // 35021 — EL-REZ-COST-MODIFIER; EL-BYPASS.
    private static readonly CardBehaviorDefinition FransofiaWard = new(
        ElevationCardCodes.FransofiaWard,
        PlayerSide.Runner,
        triggers:
        [
            Trigger(
                "fransofia-ward-bypass",
                OccurrenceKind.IceEncountered,
                BehaviorTriggerScope.Controller,
                [TrashSelf(), new BehaviorInstruction(BehaviorInstructionKind.BypassEncounteredIce)],
                TriggerMode.Optional,
                requirement: BehaviorCondition.CreditsAtLeast(PlayerSide.Corp, 15)),
        ],
        costModifiers:
        [
            BehaviorCostModifier.Always(BehaviorCostModifierKind.IceRezCreditCost, delta: 1, reasonKey: "fransofia-ward-rez"),
        ]);

    // 35045 — EL-BEHAVIOR-INSTALL (ICE from HQ, 1 less); EL-MOVE-UPGRADE.
    private static readonly CardBehaviorDefinition MerciaB4LL4RD = new(
        ElevationCardCodes.MerciaB4LL4RD,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "mercia-install",
                OccurrenceKind.ActionPhaseEnded,
                BehaviorTriggerScope.Controller,
                [
                    InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp, BehaviorCardType.Ice, discount: 1, key: "mercia-ice"),
                    Branch(BehaviorCondition.Chose("mercia-ice"),
                        [new BehaviorInstruction(BehaviorInstructionKind.MoveSourceToServerOf, target: ChosenBy("mercia-ice"))]),
                ],
                TriggerMode.Optional,
                occurrenceController: PlayerSide.Corp),
        ]);

    // 35035 — EL-ATTACKED-SERVER-TRASH-COST (a rezzed bioroid there is trashed,
    // then the run ends); EL-ABILITY-END-RUN.
    private static readonly CardBehaviorDefinition LEOConstruction = new(
        ElevationCardCodes.LEOConstruction,
        PlayerSide.Corp,
        paidAbilities:
        [
            PaidAbility(
                "leo-end-run",
                BehaviorAbilityTiming.AnyPaidWindow,
                [],
                [
                    ChooseCards(
                        "leo-bioroid",
                        new BehaviorTarget(
                            BehaviorTargetKind.InstalledCard,
                            selector: BehaviorTarget.AttackedServerSelector,
                            filter: new BehaviorCardFilter(subtype: "bioroid", ownerSide: PlayerSide.Corp, rezzed: true)),
                        BehaviorChoiceCardinality.Exactly(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("leo-bioroid")),
                    new BehaviorInstruction(BehaviorInstructionKind.EndRun),
                ],
                BehaviorAbilityUsageLimit.OncePerTurn,
                conditions: [BehaviorCondition.RezzedSubtypeInAttackedServer("bioroid")]),
        ]);

    private static readonly BehaviorCardFilter CorpIce = new(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp);

    // 35056 — EL-ICE-APPROACHED; EL-SWAP-APPROACHED-ICE.
    private static readonly CardBehaviorDefinition MitraAman = new(
        ElevationCardCodes.MitraAman,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "mitra-aman-swap",
                OccurrenceKind.IceApproached,
                BehaviorTriggerScope.Any,
                [
                    TrashSelf(),
                    Gain(3),
                    ChooseOne(
                        "mitra-aman-from",
                        BehaviorChooser.Corp,
                        Option("headquarters",
                        [
                            ChooseCards("mitra-aman-hq", BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, CorpIce), BehaviorChoiceCardinality.UpTo(1)),
                            new BehaviorInstruction(BehaviorInstructionKind.SwapApproachedIce, target: ChosenBy("mitra-aman-hq")),
                        ]),
                        Option("archives",
                        [
                            ChooseCards("mitra-aman-archives", BehaviorTarget.CardsInZone(BehaviorZone.Archives, CorpIce), BehaviorChoiceCardinality.UpTo(1)),
                            new BehaviorInstruction(BehaviorInstructionKind.SwapApproachedIce, target: ChosenBy("mitra-aman-archives")),
                        ]),
                        Option("no-swap", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)])),
                ],
                TriggerMode.Optional,
                onAttackedServer: true),
        ]);

    // 35027 — EL-MOD-HOST; EL-STRENGTH-PERSISTENCE.
    private static readonly CardBehaviorDefinition GamedragonPro = new(
        ElevationCardCodes.GamedragonPro,
        PlayerSide.Runner,
        costs: [Credits(2)],
        triggers:
        [
            Trigger("gamedragon-install-host", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self,
                HostOnIcebreaker("gamedragon-install-breaker"), TriggerMode.Optional),
            Trigger("gamedragon-turn-host", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller,
                HostOnIcebreaker("gamedragon-turn-breaker"), TriggerMode.Optional),
        ],
        hostIcebreakerStrength: 1);

    private static BehaviorInstruction[] HostOnIcebreaker(string key) =>
    [
        ChooseCards(
            key,
            new BehaviorTarget(
                BehaviorTargetKind.InstalledCard,
                selector: BehaviorTarget.NotAiSelector,
                filter: new BehaviorCardFilter(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner)),
            BehaviorChoiceCardinality.UpTo(1)),
        new BehaviorInstruction(BehaviorInstructionKind.HostSourceOnTarget, target: ChosenBy(key)),
    ];

    private static readonly BehaviorStaticModifier PlusOneMemory = new(BehaviorStaticModifierKind.MemoryUnits, delta: 1);

    // 35018 — EL-HOSTED-FACEUP; EL-RANDOM; EL-ACCESS-OUTSIDE-RUN. The access
    // steals an agenda and offers the trash at its cost; "when accessed"
    // abilities do not fire, and only the Runner uses the ability.
    private static readonly CardBehaviorDefinition Detente = new(
        ElevationCardCodes.Detente,
        PlayerSide.Runner,
        costs: [Credits(3)],
        staticModifiers: [PlusOneMemory],
        triggers:
        [
            Trigger(
                "detente-host",
                OccurrenceKind.RunSuccessful,
                BehaviorTriggerScope.Controller,
                [
                    new BehaviorInstruction(BehaviorInstructionKind.ChooseAtRandom, amount: 1, argument: "detente-hosted",
                        target: BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, CorpCard)),
                    new BehaviorInstruction(BehaviorInstructionKind.HostFaceUp, target: ChosenBy("detente-hosted")),
                ],
                TriggerMode.Optional,
                server: ServerKind.Headquarters,
                oncePerTurn: true,
                requirement: BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Headquarters, 1)),
        ],
        paidAbilities:
        [
            ClickAbility(
                "detente-access",
                [Click()],
                [
                    ChooseCards("detente-return", BehaviorTarget.HostedOnSource(PlayerSide.Corp), BehaviorChoiceCardinality.Exactly(2)),
                    new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: ChosenBy("detente-return"),
                        sourceZone: BehaviorZone.HostedFaceUp, destinationZone: BehaviorZone.Headquarters),
                    new BehaviorInstruction(BehaviorInstructionKind.ChooseAtRandom, amount: 1, argument: "detente-accessed",
                        target: BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, CorpCard)),
                    new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: ChosenBy("detente-accessed")),
                    new BehaviorInstruction(BehaviorInstructionKind.StealAgenda, target: ChosenBy("detente-accessed")),
                    .. Enumerable.Range(0, 8).Select(cost => Branch(
                        BehaviorCondition.ChosenTrashCostIs("detente-accessed", cost),
                        [
                            ChooseOne(
                                $"detente-trash-{cost}",
                                BehaviorChooser.Runner,
                                Option("trash", [new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("detente-accessed"))],
                                    costs: cost > 0 ? [Credits(cost)] : null),
                                Option("leave", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)])),
                        ])),
                ],
                conditions: [BehaviorCondition.SourceHostsAtLeast(2)]),
        ]);

    public static IReadOnlyList<RunEventBehaviorSpec> RunEventSpecs { get; } =
    [
        new(ElevationCardCodes.CharmOffensive, cost: 0, hostedCredits: 0, allowedTargetServers: [ServerKind.Archives]),
        // 35016 — Double; EL-CHANGE-ATTACKED-SERVER.
        new(ElevationCardCodes.MaintenanceAccess, cost: 0, hostedCredits: 0, allowedTargetServers: [ServerKind.Archives],
            additionalClicks: 1, redirectApproachTo: ServerKind.Headquarters),
        // 35005 — EL-PREVENT-END-RUN; EL-RANDOM-HQ-TRASH.
        new(ElevationCardCodes.Shred, cost: 1, hostedCredits: 0, preventFirstEndRun: true),
    ];

    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } =
    [
        new(ElevationCardCodes.Proprionegation, 2),
    ];

    // 35053 — EL-RESOLVE-OTHER-SUBROUTINE; EL-REZ-PAYING-LESS; an install
    // from Archives ignoring all costs.
    public static IceBehaviorSpec MycowebIce { get; } = new(
        ElevationCardCodes.Mycoweb,
        strength: 5,
        IceType.CodeGate,
        rezCost: 8,
        [
            new BehaviorSubroutineSpec("mycoweb-install"),
            new BehaviorSubroutineSpec("mycoweb-rez"),
            new BehaviorSubroutineSpec("mycoweb-sentry"),
            new BehaviorSubroutineSpec("mycoweb-code-gate"),
        ],
        []);

    /// <summary>
    /// Mycoweb's behavior: "resolve 1 subroutine on a rezzed sentry" and "on
    /// another rezzed code gate" become one option per subroutine of each such
    /// ICE in the pool, available while a rezzed copy is installed. Subroutines
    /// with no instruction equivalent (run restrictions, damage with a printed
    /// follow-up, inward installs) are not offered.
    /// </summary>
    public static CardBehaviorDefinition MycowebFor(IBehaviorCatalog pool) => new(
        ElevationCardCodes.Mycoweb,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("mycoweb-install",
                [InstallFrom(BehaviorZone.Archives, PlayerSide.Corp, BehaviorCardType.Ice, costRule: BehaviorInstallCostRule.IgnoreAllCosts)]),
            Subroutine(
                "mycoweb-rez",
                [
                    ChooseCards("mycoweb-rez-target",
                        Installed(new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp, rezzed: false)),
                        BehaviorChoiceCardinality.UpTo(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.RezPayingLess, amount: 2, target: ChosenBy("mycoweb-rez-target")),
                ]),
            Subroutine("mycoweb-sentry", [ResolveOtherSubroutine(pool, IceType.Sentry, "mycoweb-sentry-choice")]),
            Subroutine("mycoweb-code-gate", [ResolveOtherSubroutine(pool, IceType.CodeGate, "mycoweb-code-gate-choice")]),
        ]);

    private static BehaviorInstruction ResolveOtherSubroutine(IBehaviorCatalog pool, IceType type, string key)
    {
        var options = new List<BehaviorChoiceOption>();
        foreach (IceBehaviorSpec ice in pool.IceSpecs
            .Where(spec => spec.IceType == type)
            .OrderBy(static spec => spec.DefinitionId.Value, StringComparer.Ordinal))
        {
            for (int ordinal = 0; ordinal < ice.Subroutines.Count; ordinal++)
            {
                if (SubroutineInstructions(pool, ice.DefinitionId, ice.Subroutines[ordinal], $"{key}-{ice.DefinitionId.Value}-{ordinal}")
                    is { } instructions)
                {
                    options.Add(Option($"resolve-{ice.DefinitionId.Value}-{ordinal}", instructions,
                        BehaviorCondition.RezzedInstalledCopy(ice.DefinitionId)));
                }
            }
        }

        // With no such rezzed ICE there is nothing to resolve.
        options.Add(Option("none", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]));
        return ChooseOne(key, BehaviorChooser.Corp, [.. options]);
    }

    private static IReadOnlyList<BehaviorInstruction>? SubroutineInstructions(
        IBehaviorCatalog pool,
        CardDefinitionId ice,
        IceSubroutineSpec subroutine,
        string key) => subroutine switch
        {
            EndRunSubroutineSpec => [new BehaviorInstruction(BehaviorInstructionKind.EndRun)],
            DamageSubroutineSpec { FollowUp: DamageSubroutineFollowUp.None } damage =>
                [new BehaviorInstruction(BehaviorInstructionKind.DealDamage, amount: damage.Amount, damageKind: damage.Kind)],
            GiveTagsSubroutineSpec tags => [GiveTags(tags.Amount)],
            GainCreditsSubroutineSpec { Target: PlayerSide.Corp } gain => [Gain(gain.Amount)],
            LoseCreditsSubroutineSpec { Target: PlayerSide.Runner } lose => [RunnerLoses(lose.Amount)],
            EndRunIfCreditsAtMostSubroutineSpec { Target: PlayerSide.Runner } endIf =>
                [Branch(BehaviorCondition.CreditsAtLeast(PlayerSide.Runner, endIf.CreditLimit + 1),
                    [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                    [new BehaviorInstruction(BehaviorInstructionKind.EndRun)])],
            PayCreditsOrTagSubroutineSpec payOrTag =>
                [ChooseOne($"{key}-pay", BehaviorChooser.Runner,
                    Option("pay", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)], costs: [Credits(payOrTag.CreditAmount)]),
                    Option("tag", [GiveTags(payOrTag.TagAmount)]))],
            TrashInstalledRunnerCardSubroutineSpec trash => TrashInstructions(trash, key),
            InstallCardSubroutineSpec { DestinationRule: IceInstallDestinationRule.Normal } =>
                [InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp)],
            BehaviorSubroutineSpec behavior when pool.TryGet(ice, out CardBehaviorDefinition? definition) =>
                definition.Triggers.Single(trigger => trigger.Key == behavior.TriggerKey).Instructions,
            _ => null,
        };

    private static BehaviorInstruction[] TrashInstructions(TrashInstalledRunnerCardSubroutineSpec trash, string key)
    {
        BehaviorCardType type = trash.RequiredCardClass switch
        {
            RuntimeCardClass.Program => BehaviorCardType.Program,
            RuntimeCardClass.Hardware => BehaviorCardType.Hardware,
            RuntimeCardClass.Resource => BehaviorCardType.Resource,
            _ => BehaviorCardType.Any,
        };
        BehaviorTarget eligible = Installed(new BehaviorCardFilter(type, ownerSide: PlayerSide.Runner));
        BehaviorInstruction trashChosen = new(BehaviorInstructionKind.TrashCard, target: ChosenBy($"{key}-trash"));
        // As the printed subroutine resolves natively (EL.7 review): the trash needs a target, so a mandatory trash
        // takes exactly one eligible card, and "trash or end the run" ends the run unless a card is chosen.
        return trash.EndRunAlternative
            ?
            [
                ChooseCards($"{key}-trash", eligible, BehaviorChoiceCardinality.UpTo(1)),
                Branch(BehaviorCondition.Chose($"{key}-trash"), [trashChosen], [new BehaviorInstruction(BehaviorInstructionKind.EndRun)]),
            ]
            : [ChooseCards($"{key}-trash", eligible, BehaviorChoiceCardinality.Exactly(1)), trashChosen];
    }

    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } =
    [
        FlyswatterIce,
        BumiIce,
        EmpiricistIce,
        BiawakIce,
        NPotIce,
        SemakSamunIce,
        ScatterFieldIce,
    ];

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        Flyswatter,
        Bumi,
        Empiricist,
        Biawak,
        NPot,
        SemakSamun,
        Chromatophores,
        Proprionegation,
        MaglectricRapid,
        CharmOffensive,
        FransofiaWard,
        MerciaB4LL4RD,
        LEOConstruction,
        MitraAman,
        GamedragonPro,
        Detente,
        Topan,
        RyoPhoenixOno,
        BarryBazWong,
    ];
}
