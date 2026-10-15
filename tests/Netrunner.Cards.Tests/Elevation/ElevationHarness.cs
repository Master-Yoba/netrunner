using System.Text;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests.Elevation;

/// <summary>
/// Shared fixtures for Elevation card and primitive tests: the Gateway +
/// Elevation behavior catalog, a runtime catalog compiled from text-free test
/// metadata (so tests run in the synthetic lane), and a declarative board.
/// </summary>
internal static class ElevationHarness
{
    /// <summary>A Runner event and a Corp operation with no behavior, used as hand and deck filler.</summary>
    internal const string RunnerFiller = "90001";
    internal const string CorpFiller = "90002";
    internal const string CorpFillerAgenda = "90003";
    internal const string CorpFillerIce = "90004";
    internal const string CorpFillerAsset = "90005";
    internal const string RunnerFillerProgram = "90006";
    internal const string RunnerFillerResource = "90007";
    internal const string RunnerFillerHardware = "90008";

    /// <summary>VP.2 probes: test-only cards with printed stealth subtypes, and a Corp upgrade.</summary>
    internal const string StealthHardware = "90009";
    internal const string StealthRunEvent = "90010";
    internal const string CorpProbeUpgrade = "90011";
    internal const string CorpProbeOperation = "90012";

    /// <summary>VP.2b probes: costs and values.</summary>
    internal const string ProbeReverbIce = "90013";
    internal const string ProbeHypeUpgrade = "90014";
    internal const string ProbeTailgateEvent = "90015";
    internal const string ProbeDoubleDiscountAsset = "90016";
    internal const string ProbeDoubleOperation = "90017";
    internal const string ProbeReallocOperation = "90018";
    internal const string ProbeFloodOperation = "90019";
    internal const string ProbeCentralsEvent = "90020";
    internal const string ProbeIceRezzedEvent = "90021";
    internal const string ProbeLuanaAsset = "90022";
    internal const string ProbeSellOutEvent = "90023";

    /// <summary>VP.2c probes: the primitives VP.3's binding found missing.</summary>
    internal const string ProbeBadPublicityOperation = "90024";
    internal const string ProbeBarrierResource = "90025";
    internal const string ProbeCaveatOperation = "90026";
    internal const string ProbeDiveEvent = "90027";
    internal const string ProbeTouchstoneResource = "90028";
    internal const string ProbeStowawayProgram = "90029";
    internal const string ProbePublicAgenda = "90030";
    internal const string ProbeGrubberIce = "90031";
    internal const string ProbeFloodAdvanceOperation = "90032";

    /// <summary>VP.4: the run and ICE probes (see <see cref="VantagePointProbe"/>).</summary>
    internal const string ProbeVertigoIce = "90033";
    internal const string ProbeSipaProgram = "90034";
    internal const string ProbeTailorHardware = "90035";
    internal const string ProbeEzamIce = "90036";
    internal const string ProbeStickAndPokeResource = "90037";
    internal const string ProbeLionsmaneIce = "90038";
    internal const string ProbeFlagshipUpgrade = "90039";
    internal const string ProbeLampadesProgram = "90040";
    internal const string ProbePerfectRecallUpgrade = "90041";
    internal const string ProbeCentralUpgrade = "90042";
    internal const string ProbeTagDamageOperation = "90043";
    internal const string ProbeSourceServerUpgrade = "90044";
    internal const string ProbeSpendAdvancementAsset = "90045";
    internal const string ProbeBounceIce = "90046";
    internal const string ProbeSeekerIce = "90047";
    internal const string ProbeLetheIce = "90048";

    /// <summary>The neutral Gateway identities a match board gets by default.</summary>
    internal const string CorpNeutralIdentity = "30077";

    internal const string RunnerNeutralIdentity = "30076";

    private static readonly Lazy<BehaviorCatalog> LazyBehavior = new(ElevationBehaviorCatalog.Create);

    private static readonly Lazy<RuntimeCardCatalog> LazyRuntime = new(CreateRuntime);

    private static readonly Lazy<BehaviorCatalog> LazyProbe = new(CreateProbe);

    private static readonly Lazy<BehaviorCatalog> LazyVantagePointProbe = new(CreateVantagePointProbe);

    private static readonly Lazy<BehaviorCatalog> LazyStartup = new(VantagePointBehaviorCatalog.Create);

    /// <summary>The Startup catalog: Gateway, Elevation and the registered Vantage Point cards.</summary>
    public static BehaviorCatalog Startup => LazyStartup.Value;

    public static BehaviorCatalog Behavior => LazyBehavior.Value;

    /// <summary>
    /// The Elevation catalog plus test-only probe behaviors on the filler
    /// event (<see cref="RunnerFiller"/>: "trash 1 installed ice") and the
    /// filler operation (<see cref="CorpFiller"/>: "forfeit an agenda"), for
    /// primitives no printed card in the pool exercises yet.
    /// </summary>
    public static BehaviorCatalog Probe => LazyProbe.Value;

    /// <summary>
    /// VP.2: the Elevation catalog plus test-only probe behaviors for Vantage Point primitives no registered card uses yet:
    /// the filler operation (<see cref="CorpFiller"/>) takes 2 bad publicity, and the filler asset
    /// (<see cref="CorpFillerAsset"/>) gains a credit the first time each turn the Corp takes bad publicity. The stealth
    /// hardware (<see cref="StealthHardware"/>) has an access ability costing 1 credit from stealth cards that draws a card;
    /// the stealth run event (<see cref="StealthRunEvent"/>) carries 2 credits and locks the pool; the Corp upgrade
    /// (<see cref="CorpProbeUpgrade"/>) gains the Corp a credit when the Runner spends credits from outside the pool during a
    /// run on its server, and once a turn when the Corp advances a card. VP.2 step 3: the filler agenda is worth 1 point in
    /// the Runner's score area and costs a click more to steal; the filler asset makes every steal cost 2 credits more; the
    /// filler resource is a score cost as Word on the Street's (-1, can't be forfeited, agendas installed this turn); the
    /// probe operation is playable once the Corp scored an agenda it didn't install this turn, and becomes a 2-point
    /// agenda (Myōshu). VP.2 step 4: the cost and value probes (90013 to 90023); VP.2c: the probes 90024 to 90032 for the primitives VP.3's
    /// binding found missing, the probe Luana's trash cost, and the filler
    /// ICE as a 1-strength barrier ending the run. VP.4: the probes 90033 to 90046 for the run and ICE primitives.
    /// </summary>
    public static BehaviorCatalog VantagePointProbe => LazyVantagePointProbe.Value;

    /// <summary>The catalog a state was built with, by fingerprint.</summary>
    public static BehaviorCatalog CatalogFor(GameState state) =>
        string.Equals(state.BehaviorCatalogFingerprint, Probe.Fingerprint, StringComparison.Ordinal) ? Probe
        : string.Equals(state.BehaviorCatalogFingerprint, VantagePointProbe.Fingerprint, StringComparison.Ordinal) ? VantagePointProbe
        : string.Equals(state.BehaviorCatalogFingerprint, Startup.Fingerprint, StringComparison.Ordinal) ? Startup
        : Behavior;

    private static BehaviorCatalog CreateVantagePointProbe() =>
        new BehaviorCatalogBuilder("vantage-point-probe", Behavior)
            .RecordTurnEvents(TurnEventKind.AgendaScored, TurnEventKind.AgendaScoredNotInstalledThisTurn,
                TurnEventKind.IceRezzed, TurnEventKind.DoubleOperationPlayed)
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeReverbIce),
                PlayerSide.Corp,
                costModifiers:
                [
                    BehaviorCostModifier.Always(BehaviorCostModifierKind.SelfRezCreditCost, -1, "probe-reverb",
                        new BehaviorCardFilter(BehaviorCardType.Ice, rezzed: false)),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeHypeUpgrade),
                PlayerSide.Corp,
                costModifiers:
                [
                    new BehaviorCostModifier(BehaviorCostModifierKind.SelfRezCreditCost, -2,
                        BehaviorCondition.TurnEventCountAtLeast(TurnEventKind.AgendaScoredOrStolen, 1), "probe-hype"),
                ]))
            .AddRunEvent(new RunEventBehaviorSpec(new CardDefinitionId(ProbeTailgateEvent), 3, 0,
                allowedTargetServers: [ServerKind.Headquarters], costLoweredPerIceOn: ServerKind.Headquarters))
            .Add(new CardBehaviorDefinition(new CardDefinitionId(ProbeDoubleDiscountAsset), PlayerSide.Corp, firstDoubleOperationClickDiscount: 1))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeDoubleOperation),
                PlayerSide.Corp,
                costs: [new BehaviorCost(BehaviorCostKind.Clicks, 1)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeReallocOperation),
                PlayerSide.Corp,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                        "chosen",
                        BehaviorChooser.Controller,
                        BehaviorChoiceMode.Mandatory,
                        BehaviorChoiceCardinality.Exactly(1),
                        target: new BehaviorTarget(
                            BehaviorTargetKind.InstalledCard,
                            filter: new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp, rezzed: true)))),
                    new BehaviorInstruction(BehaviorInstructionKind.GainCredits,
                        amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.ChosenCardsPrintedRezCost, 1)),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeFloodOperation),
                PlayerSide.Corp,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.GainCredits,
                        amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.ProtectedRemotesWithRootCards, 1)),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeCentralsEvent),
                PlayerSide.Runner,
                conditions: [new BehaviorCondition(BehaviorConditionKind.SuccessfulRunOnEveryCentralThisTurn)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 5)]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeIceRezzedEvent),
                PlayerSide.Runner,
                conditions: [BehaviorCondition.TurnEventCountAtLeast(TurnEventKind.IceRezzed, 1)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 5)]))
            .Add(new CardBehaviorDefinition(new CardDefinitionId(ProbeLuanaAsset), PlayerSide.Corp, returnsHostedBadPublicity: true))
            .AddAsset(new AssetBehaviorSpec(new CardDefinitionId(ProbeLuanaAsset), trashCost: 3))
            .AddIce(new IceBehaviorSpec(new CardDefinitionId(CorpFillerIce), 1, IceType.Barrier, 1, [new EndRunSubroutineSpec()], []))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeFloodAdvanceOperation),
                PlayerSide.Corp,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                        "probe-flood-target",
                        BehaviorChooser.Controller,
                        BehaviorChoiceMode.Mandatory,
                        BehaviorChoiceCardinality.Exactly(1),
                        target: new BehaviorTarget(
                            BehaviorTargetKind.InstalledCard,
                            filter: new BehaviorCardFilter(BehaviorCardType.Advanceable, ownerSide: PlayerSide.Corp)))),
                    new BehaviorInstruction(BehaviorInstructionKind.AdvanceCard,
                        target: new BehaviorTarget(BehaviorTargetKind.ChosenCard, selector: "probe-flood-target"),
                        amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.ProtectedRemotesWithRootCards, 1)),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeBadPublicityOperation),
                PlayerSide.Corp,
                conditions: [BehaviorCondition.BadPublicityAtLeast(1)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeBarrierResource),
                PlayerSide.Runner,
                paidAbilities:
                [
                    new BehaviorPaidAbility(
                        "probe-barrier-strength",
                        BehaviorAbilityTiming.EncounterPaidWindow,
                        BehaviorAbilityActivation.PaidAbility,
                        [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                        [BehaviorCondition.EncounteringSubtype("barrier")],
                        [],
                        [new BehaviorInstruction(BehaviorInstructionKind.ModifyEncounteredIceStrength, amount: -1, argument: "probe-barrier-strength")],
                        BehaviorAbilityUsageLimit.Unlimited),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeCaveatOperation),
                PlayerSide.Corp,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainAllottedClicksNextTurn, amount: -1, recipient: BehaviorPlayerSelector.Runner)]))
            .AddRunEvent(new RunEventBehaviorSpec(new CardDefinitionId(ProbeDiveEvent), 0, 0,
                removedFromGameAfterRun: true, targetMustBeProtected: true))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeTouchstoneResource),
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger("probe-first-event", OccurrenceKind.FirstEventPlayedThisTurn, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null, [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeStowawayProgram),
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger("probe-hosted-success", OccurrenceKind.RunSuccessful, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null, [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 2)],
                        requireSourceOnAttackedServer: true),
                ],
                hosting: new HostingBehaviorSpec()))
            .AddAgenda(new AgendaBehaviorSpec(new CardDefinitionId(ProbePublicAgenda), 2, installsFaceUp: true))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbePublicAgenda),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-public-advanced", OccurrenceKind.CardAdvanced, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self, null, [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]),
                ]))
            .AddIce(new IceBehaviorSpec(new CardDefinitionId(ProbeGrubberIce), 1, IceType.Barrier, 0, [new EndRunSubroutineSpec()], []))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeGrubberIce),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-rez-on-hq", OccurrenceKind.CardRezzed, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self, null, [new BehaviorInstruction(BehaviorInstructionKind.TakeBadPublicity, amount: 1)],
                        serverKindRequirement: ServerKind.Headquarters),
                ]))
            .AddIce(new IceBehaviorSpec(new CardDefinitionId(ProbeVertigoIce), 1, IceType.CodeGate, 0, [], []))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeVertigoIce),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-vertigo-passed", OccurrenceKind.IcePassed, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self, null,
                        [new BehaviorInstruction(BehaviorInstructionKind.RestrictRun, argument: nameof(RunRestrictionKind.CannotStealOrTrashCorpCards))],
                        requirement: BehaviorCondition.ClicksAtMost(2, PlayerSide.Runner)),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeSipaProgram),
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger("probe-sipa-passed", OccurrenceKind.IcePassed, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null, [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
                        oncePerTurn: true, condition: BehaviorTriggerCondition.SubjectOutermostFullyBroken),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeTailorHardware),
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger("probe-tailor-broken", OccurrenceKind.SubroutineBroken, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null, [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
                        oncePerTurn: true, condition: BehaviorTriggerCondition.SubjectStrengthAtMostZero),
                ],
                allIceStrength: -1))
            .AddIce(new IceBehaviorSpec(new CardDefinitionId(ProbeEzamIce), 1, IceType.CodeGate, 0,
                [new BehaviorSubroutineSpec("probe-ezam-strength")], []))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeEzamIce),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-ezam-strength", OccurrenceKind.IceSubroutine, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null,
                        [new BehaviorInstruction(BehaviorInstructionKind.ModifyAllIceStrengthForRun, amount: 1)]),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeStickAndPokeResource),
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger("probe-stick-and-poke", OccurrenceKind.IceEncountered, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null, [new BehaviorInstruction(BehaviorInstructionKind.EncounteredIceGainsSubroutine)],
                        oncePerTurn: true),
                ],
                gainedIceSubroutine: new DamageSubroutineSpec(DamageKind.Net, 1, DamageSubroutineFollowUp.RunnerDrawsOne)))
            .AddIce(new IceBehaviorSpec(new CardDefinitionId(ProbeLionsmaneIce), 1, IceType.Sentry, 0,
                [new BehaviorSubroutineSpec("probe-lionsmane-jack")], []))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeLionsmaneIce),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-lionsmane-jack", OccurrenceKind.IceSubroutine, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null,
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                                "probe-lionsmane-choice",
                                BehaviorChooser.Runner,
                                BehaviorChoiceMode.Mode,
                                BehaviorChoiceCardinality.Exactly(1),
                                options:
                                [
                                    new BehaviorChoiceOption("suffer-damage", [new BehaviorInstruction(BehaviorInstructionKind.DealDamage,
                                        amount: 2, damageKind: DamageKind.Net, recipient: BehaviorPlayerSelector.Runner)]),
                                    new BehaviorChoiceOption("jack-out", [new BehaviorInstruction(BehaviorInstructionKind.JackOut)]),
                                ])),
                        ]),
                    new BehaviorTrigger("probe-lionsmane-ended", OccurrenceKind.EncounterEnded, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self, null, [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeFlagshipUpgrade),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-flagship-unsuccessful", OccurrenceKind.RunEnded, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Any, null, [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
                        runOutcomeRequirement: RunOutcomeRequirement.Unsuccessful),
                ],
                runsCannotSucceed: true,
                runAccessLimitBesidesSelf: 1))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeLampadesProgram),
                PlayerSide.Runner,
                paidAbilities:
                [
                    new BehaviorPaidAbility(
                        "probe-lampades-trash",
                        BehaviorAbilityTiming.BreachAccessPaidWindow,
                        BehaviorAbilityActivation.PaidAbility,
                        [new BehaviorCost(BehaviorCostKind.Credits, 0, accessedCardPrintedCost: true)],
                        [],
                        [new BehaviorTarget(BehaviorTargetKind.AccessedCard)],
                        [new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: new BehaviorTarget(BehaviorTargetKind.AccessedCard))],
                        BehaviorAbilityUsageLimit.Unlimited),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbePerfectRecallUpgrade),
                PlayerSide.Corp,
                paidAbilities:
                [
                    new BehaviorPaidAbility(
                        "probe-recall-name",
                        BehaviorAbilityTiming.AnyPaidWindow,
                        BehaviorAbilityActivation.PaidAbility,
                        [],
                        [new BehaviorCondition(BehaviorConditionKind.RunInProgress)],
                        [],
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                                "probe-recall-card",
                                BehaviorChooser.Controller,
                                BehaviorChoiceMode.Mandatory,
                                BehaviorChoiceCardinality.Exactly(1),
                                target: BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, reveal: true))),
                            new BehaviorInstruction(BehaviorInstructionKind.RestrictRun,
                                argument: nameof(RunRestrictionKind.CannotStealOrTrashCorpCards),
                                target: new BehaviorTarget(BehaviorTargetKind.ChosenCard, selector: "probe-recall-card")),
                        ],
                        BehaviorAbilityUsageLimit.OncePerTurn),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeCentralUpgrade),
                PlayerSide.Corp,
                installServerKinds: [ServerKind.Headquarters, ServerKind.RAndD, ServerKind.Archives]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeTagDamageOperation),
                PlayerSide.Corp,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.DealDamage, damageKind: DamageKind.Meat, recipient: BehaviorPlayerSelector.Runner,
                        damageAmountOperand: new BehaviorDamageAmountOperand(BehaviorDamageAmountKind.RunnerTags)),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeSourceServerUpgrade),
                PlayerSide.Corp,
                paidAbilities:
                [
                    new BehaviorPaidAbility(
                        "probe-source-server",
                        BehaviorAbilityTiming.AnyPaidWindow,
                        BehaviorAbilityActivation.PaidAbility,
                        [],
                        [new BehaviorCondition(BehaviorConditionKind.RunAgainstSourceServer)],
                        [],
                        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
                        BehaviorAbilityUsageLimit.OncePerTurn),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeSpendAdvancementAsset),
                PlayerSide.Corp,
                paidAbilities:
                [
                    new BehaviorPaidAbility(
                        "probe-spend-advancement",
                        BehaviorAbilityTiming.ActionPhase,
                        BehaviorAbilityActivation.ClickAction,
                        [new BehaviorCost(BehaviorCostKind.Clicks, 1)],
                        [BehaviorCondition.SourceAdvancementAtLeast(1)],
                        [],
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.RemoveAdvancementCounters, amount: 1,
                                target: new BehaviorTarget(BehaviorTargetKind.Self)),
                            new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 2),
                        ],
                        BehaviorAbilityUsageLimit.Unlimited),
                ]))
            .AddIce(new IceBehaviorSpec(new CardDefinitionId(ProbeBounceIce), 1, IceType.Sentry, 0,
                [new BehaviorSubroutineSpec("probe-bounce")], []))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeBounceIce),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-bounce", OccurrenceKind.IceSubroutine, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null,
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                                "probe-bounce-card",
                                BehaviorChooser.Controller,
                                BehaviorChoiceMode.Mandatory,
                                BehaviorChoiceCardinality.Exactly(1),
                                target: new BehaviorTarget(BehaviorTargetKind.InstalledCard,
                                    filter: new BehaviorCardFilter(BehaviorCardType.Any, ownerSide: PlayerSide.Runner)))),
                            new BehaviorInstruction(BehaviorInstructionKind.AddToHand, sourceZone: BehaviorZone.Rig,
                                destinationZone: BehaviorZone.Grip,
                                target: new BehaviorTarget(BehaviorTargetKind.ChosenCard, selector: "probe-bounce-card")),
                        ]),
                ]))
            .AddIce(new IceBehaviorSpec(new CardDefinitionId(ProbeSeekerIce), 1, IceType.CodeGate, 0,
                [new BehaviorSubroutineSpec("probe-seeker-virus")], []))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeSeekerIce),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-seeker-virus", OccurrenceKind.IceSubroutine, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller, null,
                        [new BehaviorInstruction(BehaviorInstructionKind.AddCounter, amount: 1, argument: "virus")]),
                    new BehaviorTrigger("probe-seeker-ended", OccurrenceKind.EncounterEnded, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self, null,
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.PurgeVirusCounters),
                            new BehaviorInstruction(BehaviorInstructionKind.Derez, target: new BehaviorTarget(BehaviorTargetKind.Self)),
                        ],
                        requirement: new BehaviorCondition(BehaviorConditionKind.CounterAtLeast, fact: "virus", value: 1)),
                ]))
            .AddIce(new IceBehaviorSpec(new CardDefinitionId(ProbeLetheIce), 1, IceType.Barrier, 0, [new EndRunSubroutineSpec()], []))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeLetheIce),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger("probe-lethe-broken", OccurrenceKind.IceFullyBroken, TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self, null,
                        [new BehaviorInstruction(BehaviorInstructionKind.AddTags, amount: 1, recipient: BehaviorPlayerSelector.Runner)]),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(ProbeSellOutEvent),
                PlayerSide.Runner,
                costs:
                [
                    new BehaviorCost(
                        BehaviorCostKind.Additional,
                        amount: 1,
                        additionalKey: "probe-sell-out-trash",
                        additionalTarget: BehaviorTarget.CardsInZone(
                            BehaviorZone.Rig,
                            new BehaviorCardFilter(BehaviorCardType.Resource, ownerSide: PlayerSide.Runner)),
                        additionalKind: BehaviorAdditionalCostKind.TrashCards),
                ],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 4)]))
            .AddAgenda(new AgendaBehaviorSpec(new CardDefinitionId(CorpFillerAgenda), 2, runnerScoreAreaPoints: 1, stealClicks: 1))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(RunnerFillerResource),
                PlayerSide.Runner,
                scoreCostAsAgenda: new BehaviorScoreCostAsAgenda(-1, CannotForfeit: true, OnlyAgendasInstalledThisTurn: true)))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(CorpProbeOperation),
                PlayerSide.Corp,
                conditions: [BehaviorCondition.TurnEventCountAtLeast(TurnEventKind.AgendaScoredNotInstalledThisTurn, 1)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.AddSelfToScoreAreaAsAgenda, amount: 2)]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(CorpFiller),
                PlayerSide.Corp,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.TakeBadPublicity, amount: 2)]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(CorpFillerAsset),
                PlayerSide.Corp,
                stealCredits: 2,
                triggers:
                [
                    new BehaviorTrigger(
                        "probe-first-bad-publicity",
                        OccurrenceKind.FirstBadPublicityThisTurn,
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Controller,
                        null,
                        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(StealthHardware),
                PlayerSide.Runner,
                paidAbilities:
                [
                    new BehaviorPaidAbility(
                        "probe-stealth-access",
                        BehaviorAbilityTiming.BreachAccessPaidWindow,
                        BehaviorAbilityActivation.PaidAbility,
                        [new BehaviorCost(BehaviorCostKind.Credits, 1, creditSourceSubtype: "stealth")],
                        [],
                        [],
                        [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1)],
                        BehaviorAbilityUsageLimit.Unlimited),
                ]))
            .AddRunEvent(new RunEventBehaviorSpec(new CardDefinitionId(StealthRunEvent), 0, 2, locksCreditPool: true))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(CorpProbeUpgrade),
                PlayerSide.Corp,
                triggers:
                [
                    new BehaviorTrigger(
                        "probe-outside-credits",
                        OccurrenceKind.CreditsSpentFromOutsidePool,
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Any,
                        null,
                        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
                        requireSourceOnAttackedServer: true),
                    new BehaviorTrigger(
                        "probe-advanced",
                        OccurrenceKind.CardAdvanced,
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Any,
                        null,
                        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
                        oncePerTurn: true),
                ]))
            .Build();

    private static BehaviorCatalog CreateProbe() =>
        new BehaviorCatalogBuilder("elevation-probe", Behavior)
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(RunnerFiller),
                PlayerSide.Runner,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                        "probe-ice",
                        BehaviorChooser.Controller,
                        BehaviorChoiceMode.Mandatory,
                        BehaviorChoiceCardinality.Exactly(1),
                        target: new BehaviorTarget(
                            BehaviorTargetKind.InstalledCard,
                            filter: new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp)))),
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(CorpFiller),
                PlayerSide.Corp,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                        "probe-forfeit",
                        BehaviorChooser.Controller,
                        BehaviorChoiceMode.Mandatory,
                        BehaviorChoiceCardinality.Exactly(1),
                        target: BehaviorTarget.CardInZone(BehaviorZone.ScoreArea,
                            new BehaviorCardFilter(BehaviorCardType.Agenda, ownerSide: PlayerSide.Corp)))),
                    new BehaviorInstruction(BehaviorInstructionKind.ForfeitAgenda, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
                ]))
            .Build();

    public static RuntimeCardCatalog Runtime => LazyRuntime.Value;

    private static RuntimeCardCatalog CreateRuntime()
    {
        CardDataSet pool = CardDataSet.Combine(
        [
            Pack("sg-test", ElevationTestMetadata.Gateway),
            Pack("elev-test", ElevationTestMetadata.Elevation),
            Pack("vp-test", ElevationTestMetadata.VantagePoint),
            Pack("fixture-test",
            [
                new(RunnerFiller, "Filler Event", "runner", "neutral-runner", "event", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(CorpFiller, "Filler Operation", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(CorpFillerAgenda, "Filler Agenda", "corp", "neutral-corp", "agenda", [], 3, 3, false, null, null, null, null, 2, 3, null, null, null, null),
                new(CorpFillerIce, "Filler Ice", "corp", "neutral-corp", "ice", ["barrier"], 3, 3, false, 1, 1, null, null, null, null, 0, null, null, null),
                new(CorpFillerAsset, "Filler Asset", "corp", "neutral-corp", "asset", [], 3, 3, false, 0, null, null, 1, null, null, 0, null, null, null),
                new(RunnerFillerProgram, "Filler Program", "runner", "neutral-runner", "program", [], 3, 3, false, 1, null, 1, null, null, null, 0, null, null, null),
                new(RunnerFillerResource, "Filler Resource", "runner", "neutral-runner", "resource", [], 3, 3, false, 1, null, null, null, null, null, 0, null, null, null),
                new(RunnerFillerHardware, "Filler Hardware", "runner", "neutral-runner", "hardware", [], 3, 3, false, 1, null, null, null, null, null, 0, null, null, null),
                new(StealthHardware, "Stealth Hardware", "runner", "neutral-runner", "hardware", ["Stealth"], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(StealthRunEvent, "Stealth Run Event", "runner", "neutral-runner", "event", ["Run", "Stealth"], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(CorpProbeUpgrade, "Probe Upgrade", "corp", "neutral-corp", "upgrade", [], 3, 3, false, 0, null, null, 2, null, null, 0, null, null, null),
                new(CorpProbeOperation, "Probe Operation", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeReverbIce, "Probe Reverb", "corp", "neutral-corp", "ice", ["Barrier"], 3, 3, false, 4, 2, null, null, null, null, 0, null, null, null),
                new(ProbeHypeUpgrade, "Probe Hype", "corp", "neutral-corp", "upgrade", [], 3, 3, false, 3, null, null, 2, null, null, 0, null, null, null),
                new(ProbeTailgateEvent, "Probe Tailgate", "runner", "neutral-runner", "event", ["Run"], 3, 3, false, 3, null, null, null, null, null, 0, null, null, null),
                new(ProbeDoubleDiscountAsset, "Probe Synchrocyclotron", "corp", "neutral-corp", "asset", [], 3, 3, false, 0, null, null, 3, null, null, 0, null, null, null),
                new(ProbeDoubleOperation, "Probe Double", "corp", "neutral-corp", "operation", ["Double"], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeReallocOperation, "Probe realloc", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeFloodOperation, "Probe Flood", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeCentralsEvent, "Probe Chain", "runner", "neutral-runner", "event", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeIceRezzedEvent, "Probe Underdome", "runner", "neutral-runner", "event", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeLuanaAsset, "Probe Luana", "corp", "neutral-corp", "asset", [], 3, 3, false, 1, null, null, 3, null, null, 0, null, null, null),
                new(ProbeSellOutEvent, "Probe Sell Out", "runner", "neutral-runner", "event", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeBadPublicityOperation, "Probe Scapegoat", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeBarrierResource, "Probe Corsair", "runner", "neutral-runner", "resource", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeCaveatOperation, "Probe Caveat", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeDiveEvent, "Probe Dive", "runner", "neutral-runner", "event", ["Run"], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeTouchstoneResource, "Probe Touchstone", "runner", "neutral-runner", "resource", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeStowawayProgram, "Probe Stowaway", "runner", "neutral-runner", "program", ["Trojan"], 3, 3, false, 0, null, 1, null, null, null, 0, null, null, null),
                new(ProbePublicAgenda, "Probe Public Agenda", "corp", "neutral-corp", "agenda", ["Public"], 3, 3, false, null, null, null, null, 2, 3, null, null, null, null),
                new(ProbeGrubberIce, "Probe Grubber", "corp", "neutral-corp", "ice", ["Barrier"], 3, 3, false, 0, 1, null, null, null, null, 0, null, null, null),
                new(ProbeFloodAdvanceOperation, "Probe Flood Advance", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeVertigoIce, "Probe Vertigo", "corp", "neutral-corp", "ice", ["Code Gate"], 3, 3, false, 0, 1, null, null, null, null, 0, null, null, null),
                new(ProbeSipaProgram, "Probe Sipa", "runner", "neutral-runner", "program", [], 3, 3, false, 0, null, 1, null, null, null, 0, null, null, null),
                new(ProbeTailorHardware, "Probe Tailor", "runner", "neutral-runner", "hardware", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeEzamIce, "Probe ezaM", "corp", "neutral-corp", "ice", ["Code Gate"], 3, 3, false, 0, 1, null, null, null, null, 0, null, null, null),
                new(ProbeStickAndPokeResource, "Probe Stick and Poke", "runner", "neutral-runner", "resource", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeLionsmaneIce, "Probe Lionsmane", "corp", "neutral-corp", "ice", ["Sentry"], 3, 3, false, 0, 1, null, null, null, null, 0, null, null, null),
                new(ProbeFlagshipUpgrade, "Probe Flagship", "corp", "neutral-corp", "upgrade", [], 3, 3, false, 0, null, null, 4, null, null, 0, null, null, null),
                new(ProbeLampadesProgram, "Probe Lampades", "runner", "neutral-runner", "program", [], 3, 3, false, 0, null, 1, null, null, null, 0, null, null, null),
                new(ProbePerfectRecallUpgrade, "Probe Perfect Recall", "corp", "neutral-corp", "upgrade", [], 3, 3, false, 0, null, null, 3, null, null, 0, null, null, null),
                new(ProbeCentralUpgrade, "Probe Central Upgrade", "corp", "neutral-corp", "upgrade", [], 3, 3, false, 0, null, null, 2, null, null, 0, null, null, null),
                new(ProbeTagDamageOperation, "Probe Tag Damage", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(ProbeSourceServerUpgrade, "Probe Event Horizon", "corp", "neutral-corp", "upgrade", [], 3, 3, false, 0, null, null, 2, null, null, 0, null, null, null),
                new(ProbeSpendAdvancementAsset, "Probe Sacrifice Zone", "corp", "neutral-corp", "asset", [], 3, 3, false, 0, null, null, 2, null, null, 0, null, null, null),
                new(ProbeBounceIce, "Probe Bounce", "corp", "neutral-corp", "ice", ["Sentry"], 3, 3, false, 0, 1, null, null, null, null, 0, null, null, null),
                new(ProbeSeekerIce, "Probe Knowledge Seeker", "corp", "neutral-corp", "ice", ["Code Gate"], 3, 3, false, 0, 1, null, null, null, null, 0, null, null, null),
                new(ProbeLetheIce, "Probe Lethe", "corp", "neutral-corp", "ice", ["Barrier"], 3, 3, false, 0, 1, null, null, null, null, 0, null, null, null),
            ]),
        ]);
        return GatewayRuntimeCardCatalog.Create(pool);
    }

    private static CardDataSet Pack(string packCode, IReadOnlyList<MetadataRow> rows)
    {
        NormalizedCardDefinition[] cards = rows
            .Select(row => new NormalizedCardDefinition(
                row.Code, row.Title, row.Side, row.Faction, row.Type, row.Subtypes, string.Empty, packCode,
                row.Quantity, row.DeckLimit, row.Unique, row.Cost, row.Strength, row.MemoryCost, row.TrashCost,
                row.AgendaPoints, row.AdvancementRequirement, row.InfluenceCost, row.MinimumDeckSize,
                row.InfluenceLimit, row.BaseLink))
            .OrderBy(static card => card.Title, StringComparer.Ordinal)
            .ThenBy(static card => card.Code, StringComparer.Ordinal)
            .ToArray();
        byte[] bytes = CardDataJson.SerializeCards(cards);
        var manifest = new CardDataManifest(
            CardDataValidator.CurrentManifestSchemaVersion,
            "https://example.invalid/elevation-test",
            CardDataValidator.OfficialCommitSha,
            "2026-10-02",
            "test-v1",
            CardDataValidator.CurrentNormalizedSchemaVersion,
            CardDataJson.ComputeSha256Hex(bytes),
            "TEST.md",
            "NOASSERTION",
            $"test/{packCode}",
            packCode,
            packCode,
            cards.Length);
        return CardDataResource.LoadBytes(bytes, CardDataJson.SerializeManifest(manifest), Encoding.UTF8.GetBytes("test"));
    }

    public static ApplyResult Apply(GameState state, LegalAction action)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            CatalogFor(state),
            Runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
    }

    public static ApplyResult Apply(GameState state, Func<LegalAction, bool> select) =>
        Apply(state, Assert.Single(Assert.IsType<PendingDecision>(state.PendingDecision).LegalActions, action => select(action)));

    /// <summary>
    /// Takes the "do nothing" step of the pending decision: pass priority,
    /// decline an optional trigger, continue the run, take the first breach
    /// candidate, or finish an access without acting.
    /// </summary>
    public static GameState Pass(GameState state) => PassResult(state).State;

    /// <summary>The pass <see cref="Pass"/> makes, with its events.</summary>
    public static ApplyResult PassResult(GameState state)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        ActionKind[] order =
        [
            ActionKind.PassPriority,
            ActionKind.DeclineTrigger,
            ActionKind.DeclinePrevention,
            ActionKind.DeclineReplacement,
            ActionKind.ContinueRun,
            ActionKind.NoAccessAction,
            ActionKind.ChooseBreachCandidate,
            ActionKind.OrderTriggers,
        ];
        LegalAction? action = order
            .Select(kind => decision.LegalActions.FirstOrDefault(candidate => candidate.Kind == kind))
            .FirstOrDefault(static candidate => candidate is not null);
        if (action is null && decision.Kind == DecisionKind.InstallChoice)
        {
            // Install without trashing anything: finalize the empty selection.
            action = decision.LegalActions.FirstOrDefault(static candidate =>
                candidate.Parameters is InstallCardParameters { FinalizeSelection: true, SelectedReplacementCardIds.Count: 0 });
        }
        Assert.True(action is not null,
            $"No pass action for {decision.Kind} '{decision.Prompt}': "
            + string.Join(", ", decision.LegalActions.Select(static candidate => candidate.Kind)));
        return Apply(state, action!);
    }

    /// <summary>Passes priority and declines optional triggers until the stop condition holds.</summary>
    public static GameState PassUntil(GameState state, Func<GameState, bool> stop, int limit = 40)
    {
        for (int count = 0; !stop(state); count++)
        {
            Assert.True(count < limit, $"Expected boundary was not reached; pending {state.PendingDecision?.Kind}: {state.PendingDecision?.Prompt}");
            state = Pass(state);
        }

        return state;
    }

    public static GameState ToAction(GameState state) =>
        PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.TurnAction);

    public static GameState RoundTrip(GameState state)
    {
        string json = EngineJson.Serialize(state);
        BehaviorCatalog behavior = CatalogFor(state);
        GameState restored = EngineJson.DeserializeState(json, behavior, Runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Empty(StateInvariantValidator.Collect(restored, behavior, Runtime));
        return restored;
    }

    public static CardInstanceId Card(GameState state, string code) =>
        Assert.Single(state.Cards, card => card.DefinitionId.Value == code).Id;

    public static CardInstanceState CardState(GameState state, string code) =>
        Assert.Single(state.Cards, card => card.DefinitionId.Value == code);

    public static ZoneKind ZoneOf(GameState state, string code) =>
        state.Zones.Single(zone => zone.Id == CardState(state, code).ZoneId).Kind;

    public static PlayerState Player(GameState state, PlayerSide side) => state.GetPlayerBySide(side);

    public static PlayerRulesState Rules(GameState state, PlayerSide side) =>
        state.RulesState!.Players.Single(player => player.PlayerId == state.GetPlayerBySide(side).Id);

    public static int Counter(GameState state, string code, string counter) =>
        state.RulesState!.Counters
            .Where(value => value.CardId == Card(state, code) && value.CounterName == counter)
            .Sum(static value => value.Count);

    /// <summary>Starts a run on a board server and passes until the stop condition or the run ends.</summary>
    public static GameState RunTo(GameState state, int server, Func<GameState, bool> stop)
    {
        ServerId target = ElevationBoard.ServerId(state, server);
        state = Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == target).State;
        return PassUntil(state, value => stop(value) || value.RunState is null);
    }

    public static bool InEncounter(GameState state) =>
        state.RunState is { Step: RunTimingStep.EncounterPaidWindow, Priority.PriorityHolder: PlayerSide.Runner };

    public static bool CanBreak(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Any(action => action.Kind == ActionKind.BreakSubroutines
            && action.Parameters is RunBreakParameters breaking && breaking.BreakerId == Card(state, code)) == true;

    public static GameState ChooseCards(GameState state, params CardInstanceId[] cards) =>
        Apply(state, action => action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.SequenceEqual(cards)).State;

    public static GameState ChooseOption(GameState state, string option) =>
        Apply(state, action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.SequenceEqual([option])).State;

    public static bool HasTrigger(GameState state) =>
        state.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.UseTrigger) == true;

    public static GameState UseTrigger(GameState state) =>
        Apply(state, static action => action.Kind == ActionKind.UseTrigger).State;

    /// <summary>Passes until a trigger is offered, without declining any.</summary>
    public static GameState PassToTrigger(GameState state) =>
        PassUntil(state, HasTrigger);

    /// <summary>
    /// Takes the basic gain-a-credit action until the active player is out of
    /// clicks, then passes until a trigger or choice waits or the turn moves on.
    /// </summary>
    public static GameState SpendRemainingClicks(GameState state)
    {
        PlayerSide active = state.Turn.ActiveSide;
        for (int step = 0; step < 60; step++)
        {
            if (HasTrigger(state)
                || state.PendingDecision?.Kind is DecisionKind.BehaviorChoice or DecisionKind.BehaviorInstallChoice
                || state.Turn.ActiveSide != active)
            {
                return state;
            }

            state = state.PendingDecision?.Kind == DecisionKind.TurnAction && Player(state, active).Clicks > 0
                ? Apply(state, static action => action.Kind == ActionKind.GainCredit).State
                : Pass(state);
        }

        throw new InvalidOperationException("The turn did not move on.");
    }

    public static GameState PlayFirst(GameState state, string code) =>
        Apply(state, Actions(state).First(action => action.Kind == ActionKind.PlayCard
            && action.Parameters is PlayCardParameters play && state.Cards.Single(card => card.Id == play.CardId).DefinitionId.Value == code)).State;

    public static IReadOnlyList<LegalAction> Actions(GameState state) =>
        Assert.IsType<PendingDecision>(state.PendingDecision).LegalActions;
}

/// <summary>Where a card starts on a test board.</summary>
internal enum Place
{
    CorpDeck,
    Hq,
    Archives,
    ArchivesFaceUp,
    CorpScore,
    CorpIdentity,
    Stack,
    Grip,
    Heap,
    Rig,
    RunnerScore,
    RunnerIdentity,
    Root,
    Ice,
}

/// <summary>One card on a test board. Server 0..2 are HQ, R&amp;D and Archives; 3 and up are remotes.</summary>
internal sealed record Placement(
    string Code,
    Place Where,
    int Server = 3,
    bool Rezzed = false,
    int Advancement = 0,
    IReadOnlyDictionary<string, int>? Counters = null,
    bool FaceUp = false);

/// <summary>
/// A declarative test board. Unless stated, both identities are absent, each
/// player has 5 credits, and the active player is at the start of their
/// action phase with full clicks.
/// </summary>
internal sealed class ElevationBoard
{
    public const int Hq = 0;
    public const int Rd = 1;
    public const int ArchivesServer = 2;
    public const int Remote = 3;

    private readonly List<Placement> _placements = [];

    public ElevationBoard(PlayerSide active = PlayerSide.Corp, int turnNumber = 2)
    {
        Active = active;
        TurnNumber = turnNumber;
    }

    public PlayerSide Active { get; }

    public int TurnNumber { get; }

    public int CorpCredits { get; set; } = 5;

    public int RunnerCredits { get; set; } = 5;

    public int? CorpClicks { get; set; }

    public int? RunnerClicks { get; set; }

    public int Tags { get; set; }

    /// <summary>VP-BAD-PUBLICITY: the Corp's bad publicity at the start.</summary>
    public int BadPublicity { get; set; }

    public int CorpPoints { get; set; }

    public int RunnerPoints { get; set; }

    public int CorpDeckFiller { get; set; } = 5;

    public int StackFiller { get; set; } = 5;

    public int GripFiller { get; set; }

    public int HqFiller { get; set; }

    public int RemoteServers { get; set; } = 1;

    public ElevationBoard With(params Placement[] placements)
    {
        _placements.AddRange(placements);
        return this;
    }

    public ElevationBoard Add(string code, Place where, int server = Remote, bool rezzed = false, int advancement = 0,
        IReadOnlyDictionary<string, int>? counters = null, bool faceUp = false)
    {
        _placements.Add(new Placement(code, where, server, rezzed, advancement, counters, faceUp));
        return this;
    }

    /// <summary>Builds the state, ready for <see cref="GameEngine.BeginTurn"/> or a run.</summary>
    public GameState Build(ulong seed = 35000, BehaviorCatalog? catalog = null)
    {
        BehaviorCatalog behavior = catalog ?? ElevationHarness.Behavior;
        RuntimeCardCatalog runtime = ElevationHarness.Runtime;
        GameState baseline = GameState.CreateNew(seed, behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint, runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        var allocator = new StableIdAllocator(baseline.Allocator);
        List<ServerState> servers = baseline.Servers.ToList();
        List<ZoneState> zones = baseline.Zones.ToList();
        // A board with an identity is a setup-created match: both sides need an
        // identity (the neutral Gateway ones by default) and no remote may be empty.
        bool corpIdentity = _placements.Any(static p => p.Where == Place.CorpIdentity);
        bool runnerIdentity = _placements.Any(static p => p.Where == Place.RunnerIdentity);
        bool match = corpIdentity || runnerIdentity;
        List<Placement> placements = _placements.ToList();
        if (match && !corpIdentity)
        {
            placements.Add(new Placement(ElevationHarness.CorpNeutralIdentity, Place.CorpIdentity));
        }

        if (match && !runnerIdentity)
        {
            placements.Add(new Placement(ElevationHarness.RunnerNeutralIdentity, Place.RunnerIdentity));
        }

        int neededRemotes = placements.Where(static p => p.Where is Place.Root or Place.Ice)
            .Select(static p => p.Server - 2).DefaultIfEmpty(0).Max();
        int remoteCount = match ? neededRemotes : Math.Max(RemoteServers, neededRemotes);
        for (int index = 0; index < remoteCount; index++)
        {
            ServerId remote = allocator.NextServerId();
            ZoneId ice = allocator.NextZoneId();
            ZoneId root = allocator.NextZoneId();
            servers.Add(new ServerState(remote, ServerKind.Remote, ice, root));
            zones.Add(new ZoneState(ice, ZoneKind.ServerIce, PlayerSide.Corp, remote));
            zones.Add(new ZoneState(root, ZoneKind.ServerRoot, PlayerSide.Corp, remote));
        }

        var cards = new List<CardInstanceState>();
        var counters = new List<CardCounterState>();
        IEnumerable<Placement> all = placements
            .Concat(Enumerable.Repeat(new Placement(ElevationHarness.CorpFiller, Place.CorpDeck), CorpDeckFiller))
            .Concat(Enumerable.Repeat(new Placement(ElevationHarness.RunnerFiller, Place.Stack), StackFiller))
            .Concat(Enumerable.Repeat(new Placement(ElevationHarness.RunnerFiller, Place.Grip), GripFiller))
            .Concat(Enumerable.Repeat(new Placement(ElevationHarness.CorpFiller, Place.Hq), HqFiller));
        foreach (Placement placement in all)
        {
            CardRuntimeSpec spec = runtime.GetSpec(new CardDefinitionId(placement.Code));
            ZoneState zone = ZoneFor(placement, zones, servers, allocator);
            bool faceUp = placement.Where switch
            {
                Place.Rig or Place.CorpScore or Place.RunnerScore or Place.CorpIdentity or Place.RunnerIdentity
                    or Place.Heap or Place.ArchivesFaceUp => true,
                Place.Root or Place.Ice => placement.Rezzed || placement.FaceUp,
                _ => false,
            };
            var card = new CardInstanceState(allocator.NextCardInstanceId(), new CardDefinitionId(placement.Code), zone.Id,
                spec.Side, faceUp: faceUp, rezzed: placement.Rezzed, advancementCounters: placement.Advancement);
            cards.Add(card);
            foreach ((string name, int count) in placement.Counters ?? new Dictionary<string, int>())
            {
                // "declared-credit" seeds a declared-use hosted pool (recurring credits, Open Market).
                counters.Add(name switch
                {
                    "credit" => CardCounterState.CreateHostedCredit(card.Id, count),
                    "declared-credit" => CardCounterState.CreateHostedCredit(card.Id, count, HostedCreditSpendRestriction.DeclaredUseOnly),
                    // VP-STEALTH-CREDITS: hosted credits that may be spent during runs (Methuselah, Touchstone).
                    "run-credit" => CardCounterState.CreateHostedCredit(card.Id, count, HostedCreditSpendRestriction.RunCost),
                    "any-credit" => CardCounterState.CreateHostedCredit(card.Id, count, HostedCreditSpendRestriction.AnyCost),
                    _ => new CardCounterState(card.Id, name, count),
                });
            }
        }

        zones = zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
            cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToList();
        PlayerState[] players = baseline.Players.Select(player => new PlayerState(
            player.Id,
            player.Side,
            player.Side == PlayerSide.Corp ? CorpCredits : RunnerCredits,
            player.Side == PlayerSide.Corp ? CorpClicks ?? 3 : RunnerClicks ?? 4,
            player.Side == PlayerSide.Corp ? CorpPoints : RunnerPoints)).ToArray();
        PlayerId runnerId = players.Single(static player => player.Side == PlayerSide.Runner).Id;
        ZoneId rig = zones.Single(static zone => zone.Kind == ZoneKind.RunnerRig).Id;
        ProgramMemoryState[] programs = cards
            .Where(card => card.ZoneId == rig && runtime.GetSpec(card.DefinitionId).CardClass == RuntimeCardClass.Program)
            .Select(card => new ProgramMemoryState(card.Id, runnerId, runtime.GetSpec(card.DefinitionId).MemoryUnits))
            .ToArray();
        RulesState rules = new(1, players.Select(player => new PlayerRulesState(player.Id, 5, 0,
            player.Side == PlayerSide.Runner ? Tags : 0, 4,
            player.Side == PlayerSide.Runner ? programs.Sum(static program => program.MemoryCost) : 0)).ToArray(),
            programs, counters, [], badPublicity: BadPublicity);
        int allotted = Active == PlayerSide.Corp ? 3 : 4;
        TurnFactsState facts = new(1, Active, TurnNumber, allotted, 0, true, Active == PlayerSide.Corp, false, false);
        // During the Runner's turn the Corp's facts are those of its finished turn.
        TurnHistoryState history = Active == PlayerSide.Corp
            ? new TurnHistoryState(1, facts, null, null, null)
            : new TurnHistoryState(1, new TurnFactsState(1, PlayerSide.Corp, TurnNumber, 3, 0, true, true, true, true),
                null, facts, null);
        var turn = new TurnState(
            Active,
            Active == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner,
            TurnNumber,
            Active == PlayerSide.Corp ? TurnStep.CorpAction : TurnStep.RunnerAction,
            history);
        return new GameState(baseline.SchemaVersion, baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion,
            players, cards, zones, servers, turn, baseline.Outcome, baseline.RandomState, allocator.State,
            initialSeed: baseline.InitialSeed, rulesState: rules, behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint, runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
    }

    /// <summary>Builds the board and opens the active player's action phase.</summary>
    public GameState Begin(ulong seed = 35000, BehaviorCatalog? catalog = null)
    {
        GameState initial = Build(seed, catalog);
        GameState state = GameEngine.BeginTurn(initial, Active, ElevationHarness.CatalogFor(initial), ElevationHarness.Runtime);
        return ElevationHarness.ToAction(state);
    }

    public static ServerId ServerId(GameState state, int server) => server switch
    {
        Hq => state.Servers.Single(static value => value.Kind == ServerKind.Headquarters).Id,
        Rd => state.Servers.Single(static value => value.Kind == ServerKind.RAndD).Id,
        ArchivesServer => state.Servers.Single(static value => value.Kind == ServerKind.Archives).Id,
        _ => state.Servers.Where(static value => value.Kind == ServerKind.Remote).ElementAt(server - Remote).Id,
    };

    private static ZoneState ZoneFor(Placement placement, List<ZoneState> zones, List<ServerState> servers,
        StableIdAllocator allocator)
    {
        ZoneKind? kind = placement.Where switch
        {
            Place.CorpDeck => ZoneKind.CorpDeck,
            Place.Hq => ZoneKind.CorpHand,
            Place.Archives or Place.ArchivesFaceUp => ZoneKind.CorpDiscard,
            Place.CorpScore => ZoneKind.CorpScore,
            Place.Stack => ZoneKind.RunnerStack,
            Place.Grip => ZoneKind.RunnerGrip,
            Place.Heap => ZoneKind.RunnerHeap,
            Place.Rig => ZoneKind.RunnerRig,
            Place.RunnerScore => ZoneKind.RunnerScore,
            _ => null,
        };
        if (kind is { } fixedKind)
        {
            return zones.Single(zone => zone.Kind == fixedKind);
        }

        if (placement.Where is Place.CorpIdentity or Place.RunnerIdentity)
        {
            ZoneKind identity = placement.Where == Place.CorpIdentity ? ZoneKind.CorpIdentity : ZoneKind.RunnerIdentity;
            ZoneState? existing = zones.FirstOrDefault(zone => zone.Kind == identity);
            if (existing is null)
            {
                existing = new ZoneState(allocator.NextZoneId(), identity,
                    identity == ZoneKind.CorpIdentity ? PlayerSide.Corp : PlayerSide.Runner);
                zones.Add(existing);
            }

            return existing;
        }

        ServerState server = placement.Server switch
        {
            Hq => servers.Single(static value => value.Kind == ServerKind.Headquarters),
            Rd => servers.Single(static value => value.Kind == ServerKind.RAndD),
            ArchivesServer => servers.Single(static value => value.Kind == ServerKind.Archives),
            _ => servers.Where(static value => value.Kind == ServerKind.Remote).ElementAt(placement.Server - Remote),
        };
        ZoneId zoneId = placement.Where == Place.Root ? server.RootZoneId : server.IceZoneId;
        return zones.Single(zone => zone.Id == zoneId);
    }
}
