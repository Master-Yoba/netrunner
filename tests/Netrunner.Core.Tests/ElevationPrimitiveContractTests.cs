using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Contract tests for the EL.2 primitives: their validation, serialization
/// and fingerprints. The behaviors are exercised through the Elevation cards
/// and probe definitions in <c>Netrunner.Cards.Tests/Elevation</c>.
/// </summary>
public sealed class ElevationPrimitiveContractTests
{
    [Fact]
    public void TurnEventConditionsNameATypedEventAndCount()
    {
        BehaviorCondition first = BehaviorCondition.TurnEventCountIs(TurnEventKind.RunBegan, 1, PlayerSide.Runner);

        Assert.Equal(nameof(TurnEventKind.RunBegan), first.Fact);
        Assert.Equal(1, first.Value);
        Assert.Equal(PlayerSide.Runner, first.SubjectSide);
        Assert.Throws<ArgumentException>(() =>
            new BehaviorCondition(BehaviorConditionKind.TurnEventCountIs, fact: "not-an-event", value: 1));
        Assert.Throws<ArgumentException>(() =>
            new BehaviorCondition(BehaviorConditionKind.TurnEventCountIs, fact: nameof(TurnEventKind.RunBegan)));
    }

    [Fact]
    public void TurnFactsKeepDeferredOccurrencesAndNextTurnClicksThroughJson()
    {
        var facts = new TurnFactsState(
            1, PlayerSide.Corp, 3, 3, 0, true, true, false, false,
            deferredOccurrences: [new DeferredOccurrence(OccurrenceKind.FirstTagRemovalThisTurn, PlayerSide.Runner)],
            nextTurnExtraClicks: 1);

        TurnFactsState restored = EngineJson.Deserialize<TurnFactsState>(EngineJson.Serialize(facts));

        DeferredOccurrence deferred = Assert.Single(restored.DeferredOccurrences!);
        Assert.Equal(OccurrenceKind.FirstTagRemovalThisTurn, deferred.Kind);
        Assert.Equal(1, restored.NextTurnExtraClicks);
        Assert.Equal(EngineJson.Serialize(facts), EngineJson.Serialize(restored));
    }

    [Fact]
    public void EmptyElevationTurnFactsKeepTheirHistoricalBytes()
    {
        string json = EngineJson.Serialize(new TurnFactsState(PlayerSide.Runner, 2, 4));

        Assert.DoesNotContain("deferredOccurrences", json, StringComparison.Ordinal);
        Assert.DoesNotContain("nextTurnExtraClicks", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyATrashedCardOccurrenceRecordsWhetherItsSubjectWasRezzed()
    {
        var player = new PlayerId("player:1");
        var card = new CardInstanceId("card:1");

        Assert.True(new GameplayOccurrenceEffect(OccurrenceKind.AccessedCardTrashed, player, card, subjectWasRezzed: true)
            .SubjectWasRezzed);
        Assert.Throws<ArgumentException>(() =>
            new GameplayOccurrenceEffect(OccurrenceKind.CardAccessed, player, card, subjectWasRezzed: true));
    }

    [Fact]
    public void HostedCreditUsesForResourceInstallsNameTheirSubtypes()
    {
        Assert.Throws<ArgumentException>(() =>
            new BehaviorHostedCreditUse(BehaviorHostedCreditUseKind.InstallResource));
        Assert.Equal(
            new BehaviorHostedCreditUse(BehaviorHostedCreditUseKind.InstallResource, ["job"]),
            new BehaviorHostedCreditUse(BehaviorHostedCreditUseKind.InstallResource, ["job"]));
    }

    [Fact]
    public void ElevationDeclarationsChangeTheDefinitionFingerprint()
    {
        var id = new CardDefinitionId("probe-card");
        string Fingerprint(CardBehaviorDefinition definition) =>
            new BehaviorCatalogBuilder("probe").Add(definition).Build().GetDefinitionFingerprint(id);

        string plain = Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Corp));
        Assert.NotEqual(plain, Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Corp, runnerCannotTrash: true)));
        Assert.NotEqual(plain, Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Corp, recurringCredits: 2)));
        Assert.NotEqual(plain, Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Corp, rootAssetTrashCostIncrease: 2)));
        Assert.NotEqual(plain, Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Corp,
            hostedCreditUses: [new BehaviorHostedCreditUse(BehaviorHostedCreditUseKind.RezInServer)])));
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(id, PlayerSide.Runner, runnerCannotTrash: true));
    }

    [Fact]
    public void TriggerFacesAreFingerprintedAndSelfScopedForfeitsBearACard()
    {
        BehaviorTrigger Trigger(BehaviorFace? face) => new(
            "probe-trigger", OccurrenceKind.ActionPhaseEnded, TriggerMode.Mandatory, BehaviorTriggerScope.Controller, null,
            [new BehaviorInstruction(BehaviorInstructionKind.FlipSelf)], face: face);
        var id = new CardDefinitionId("probe-card");
        string Fingerprint(BehaviorTrigger trigger) => new BehaviorCatalogBuilder("probe")
            .Add(new CardBehaviorDefinition(id, PlayerSide.Corp, triggers: [trigger])).Build().GetDefinitionFingerprint(id);

        Assert.NotEqual(Fingerprint(Trigger(null)), Fingerprint(Trigger(BehaviorFace.Back)));
        _ = new BehaviorTrigger("forfeit", OccurrenceKind.AgendaForfeited, TriggerMode.Mandatory, BehaviorTriggerScope.Self, null,
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 4)]);
        Assert.Throws<ArgumentException>(() => new BehaviorTrigger("run-began", OccurrenceKind.RunBegan, TriggerMode.Mandatory,
            BehaviorTriggerScope.Self, null, [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]));
    }

    [Fact]
    public void PaidAbilitiesUsableFromTheRunnerScoreAreaAreFingerprinted()
    {
        BehaviorPaidAbility Ability(bool fromRunnerScoreArea) => new(
            "probe-ability", BehaviorAbilityTiming.ActionPhase, BehaviorAbilityActivation.ClickAction,
            [new BehaviorCost(BehaviorCostKind.Clicks, 1)], [], [],
            [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1)],
            BehaviorAbilityUsageLimit.Unlimited, fromRunnerScoreArea);
        var id = new CardDefinitionId("probe-card");
        string Fingerprint(BehaviorPaidAbility ability) => new BehaviorCatalogBuilder("probe")
            .Add(new CardBehaviorDefinition(id, PlayerSide.Corp, paidAbilities: [ability])).Build().GetDefinitionFingerprint(id);

        Assert.NotEqual(Fingerprint(Ability(false)), Fingerprint(Ability(true)));
        Assert.NotEqual(Ability(false), Ability(true));
    }

    [Theory]
    [InlineData(BehaviorInstructionKind.TrashCard, "trash")]
    [InlineData(BehaviorInstructionKind.AdvanceCard, "advance")]
    [InlineData(BehaviorInstructionKind.InstallCard, "installCard")]
    [InlineData(BehaviorInstructionKind.ShuffleIntoDeck, "shuffleIntoDeck")]
    [InlineData(BehaviorInstructionKind.FlipSelf, "flipSelf")]
    public void InstructionKindsKeepTheirHistoricalJsonNames(BehaviorInstructionKind kind, string name)
    {
        Assert.Equal($"\"{name}\"", EngineJson.Serialize(kind));
        Assert.Equal(kind, EngineJson.Deserialize<BehaviorInstructionKind>($"\"{name}\""));
        Assert.Equal(BehaviorInstructionKind.TrashCard, EngineJson.Deserialize<BehaviorInstructionKind>("\"trashCard\""));
    }

    [Fact]
    public void TurnFactsKeepHandSizeDiscardsAndFreeInstallsThroughJson()
    {
        var facts = new TurnFactsState(
            1, PlayerSide.Runner, 3, 4, 0, true, false, true, true,
            handSizeDiscardIds: [new CardInstanceId("card:7")],
            freeInstallIds: [new CardInstanceId("card:9")]);

        TurnFactsState restored = EngineJson.Deserialize<TurnFactsState>(EngineJson.Serialize(facts));

        Assert.Equal(new CardInstanceId("card:7"), Assert.Single(restored.HandSizeDiscardIds!));
        Assert.Equal(new CardInstanceId("card:9"), Assert.Single(restored.FreeInstallIds!));
        string empty = EngineJson.Serialize(new TurnFactsState(PlayerSide.Runner, 2, 4));
        Assert.DoesNotContain("handSizeDiscardIds", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("freeInstallIds", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ZoneAndHostingDeclarationsChangeTheDefinitionFingerprint()
    {
        var id = new CardDefinitionId("probe-card");
        string Fingerprint(CardBehaviorDefinition definition) =>
            new BehaviorCatalogBuilder("probe").Add(definition).Build().GetDefinitionFingerprint(id);

        Assert.NotEqual(
            Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Runner)),
            Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Runner, hostedCardsPlayAsGrip: true)));
        Assert.NotEqual(
            Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Corp)),
            Fingerprint(new CardBehaviorDefinition(id, PlayerSide.Corp,
                rezRequirements: [BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Headquarters, 3)])));
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(id, PlayerSide.Corp, hostedCardsPlayAsGrip: true));
    }

    [Fact]
    public void ZoneSelectorsNameTheirSubsetOfTheZone()
    {
        BehaviorTarget top = BehaviorTarget.TopOfZone(BehaviorZone.Stack, 3);
        BehaviorTarget hosted = BehaviorTarget.HostedOnSource(PlayerSide.Runner, BehaviorCardType.Program);

        Assert.Equal(3, top.TopCount);
        Assert.False(top.IsHostedOnSource);
        Assert.True(hosted.IsHostedOnSource);
        Assert.Equal(BehaviorZone.HostedFaceUp, hosted.Zone);
        Assert.Null(hosted.TopCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => BehaviorTarget.TopOfZone(BehaviorZone.Deck, 0));
        Assert.Throws<ArgumentException>(() => BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Grip, -1));
    }

    [Fact]
    public void RunAndIceDeclarationsChangeTheirFingerprints()
    {
        var id = new CardDefinitionId("probe-card");
        string Definition(CardBehaviorDefinition definition) =>
            new BehaviorCatalogBuilder("probe").Add(definition).Build().GetDefinitionFingerprint(id);
        string Ice(IceBehaviorSpec spec) =>
            new BehaviorCatalogBuilder("probe").AddIce([spec]).Build().Fingerprint;
        string RunEvent(RunEventBehaviorSpec spec) =>
            new BehaviorCatalogBuilder("probe").AddRunEvent([spec]).Build().Fingerprint;

        Assert.NotEqual(Definition(new CardBehaviorDefinition(id, PlayerSide.Corp)),
            Definition(new CardBehaviorDefinition(id, PlayerSide.Corp, rezForfeitDiscount: 10)));
        Assert.NotEqual(Definition(new CardBehaviorDefinition(id, PlayerSide.Runner)),
            Definition(new CardBehaviorDefinition(id, PlayerSide.Runner, hostIcebreakerStrength: 1)));
        Assert.NotEqual(Definition(new CardBehaviorDefinition(id, PlayerSide.Runner, hosting: new HostingBehaviorSpec())),
            Definition(new CardBehaviorDefinition(id, PlayerSide.Runner, hosting: new HostingBehaviorSpec(hostGainsEveryIceType: true))));
        Assert.NotEqual(Ice(new IceBehaviorSpec(id, 3, IceType.Barrier, 3, [new EndRunSubroutineSpec()], [])),
            Ice(new IceBehaviorSpec(id, 3, IceType.Barrier, 3, [new EndRunSubroutineSpec()], [], breakableOnlyBySubtype: "fracter")));
        string plainEvent = RunEvent(new RunEventBehaviorSpec(id, 0, 0));
        Assert.NotEqual(plainEvent, RunEvent(new RunEventBehaviorSpec(id, 0, 0, additionalClicks: 1)));
        Assert.NotEqual(plainEvent, RunEvent(new RunEventBehaviorSpec(id, 0, 0, redirectApproachTo: ServerKind.Headquarters)));
        Assert.NotEqual(plainEvent, RunEvent(new RunEventBehaviorSpec(id, 0, 0, preventFirstEndRun: true)));
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(id, PlayerSide.Runner, rezForfeitDiscount: 10));
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(id, PlayerSide.Corp, hostIcebreakerStrength: 1));
    }

    [Fact]
    public void BreakRestrictionsAndGainedSubtypesShapeTheInterface()
    {
        var ice = new IceBehaviorSpec(new CardDefinitionId("probe-ice"), 3, IceType.Barrier, 3, [new EndRunSubroutineSpec()], [],
            breakableOnlyBySubtype: "fracter");
        var fracter = new IcebreakerBehaviorSpec(new CardDefinitionId("probe-fracter"), 2, IceType.Barrier, 1, 1, 1, 1);
        var decoder = new IcebreakerBehaviorSpec(new CardDefinitionId("probe-decoder"), 2, IceType.CodeGate, 1, 1, 1, 1);
        var ai = new IcebreakerBehaviorSpec(new CardDefinitionId("probe-ai"), 2, IceType.Any, 1, 1, 1, 1);

        Assert.True(IceInterfaceMatcher.CanInterface(ice, false, fracter, ["icebreaker", "fracter"]));
        Assert.False(IceInterfaceMatcher.CanInterface(ice, false, ai, ["icebreaker", "ai"]));
        Assert.False(IceInterfaceMatcher.CanInterface(ice, true, decoder, ["icebreaker", "decoder"]));
        var plain = new IceBehaviorSpec(new CardDefinitionId("probe-plain"), 3, IceType.Barrier, 3, [new EndRunSubroutineSpec()], []);
        Assert.False(IceInterfaceMatcher.CanInterface(plain, false, decoder, ["decoder"]));
        Assert.True(IceInterfaceMatcher.CanInterface(plain, true, decoder, ["decoder"]));
    }

    [Fact]
    public void IceAbilitiesTheRunnerUsesCostAClickOrCredits()
    {
        _ = new EncounteredBioroidAbilitySpec("probe-credit-break", new BehaviorCost(BehaviorCostKind.Credits, 3));
        // VP-BIOROID-MULTI-BREAK allows two lost clicks (Ansel 2.0), and no more.
        _ = new EncounteredBioroidAbilitySpec("probe-two-clicks", new BehaviorCost(BehaviorCostKind.LoseClick, 2), breakCount: 2);
        Assert.Throws<ArgumentException>(() =>
            new EncounteredBioroidAbilitySpec("probe-three-clicks", new BehaviorCost(BehaviorCostKind.LoseClick, 3)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EncounteredBioroidAbilitySpec("probe-three-breaks", new BehaviorCost(BehaviorCostKind.LoseClick, 2), breakCount: 3));
        var single = new RunBioroidAbilityParameters(new CardInstanceId("card:1"), new SubroutineId("subroutine:1"),
            "probe-click-break", new BehaviorCost(BehaviorCostKind.LoseClick, 1), 0);
        Assert.DoesNotContain("secondSubroutine", EngineJson.Serialize(single), StringComparison.Ordinal);
        var parameters = new RunBioroidAbilityParameters(new CardInstanceId("card:1"), new SubroutineId("subroutine:1"),
            "probe-credit-break", new BehaviorCost(BehaviorCostKind.Credits, 3), 0);
        Assert.Equal(3, EngineJson.Deserialize<RunBioroidAbilityParameters>(EngineJson.Serialize(parameters)).Cost.Amount);
    }

    [Fact]
    public void ForfeitRezActionsAndRunCursorFlagsKeepTheirJsonShape()
    {
        var plain = new RezCardParameters(new CardInstanceId("card:1"), 14);
        var forfeit = new RezCardParameters(new CardInstanceId("card:1"), 4, forfeitAgendaId: new CardInstanceId("card:2"));

        Assert.DoesNotContain("forfeitAgendaId", EngineJson.Serialize(plain), StringComparison.Ordinal);
        Assert.Equal(new CardInstanceId("card:2"),
            EngineJson.Deserialize<RezCardParameters>(EngineJson.Serialize(forfeit)).ForfeitAgendaId);
        Assert.Throws<ArgumentException>(() => new RezCardParameters(new CardInstanceId("card:1"), 4,
            new CostQuote(4), new CardInstanceId("card:2")));
        Assert.Throws<ArgumentException>(() => new GameplayOccurrenceEffect(
            OccurrenceKind.IceApproached, new PlayerId("player:1"), serverId: new ServerId("server:1")));
    }

    [Fact]
    public void ElevationConditionsNameTheirSubject()
    {
        Assert.Equal(PlayerSide.Corp, BehaviorCondition.CreditsAtLeast(PlayerSide.Corp, 15).SubjectSide);
        Assert.Throws<ArgumentException>(() => new BehaviorCondition(BehaviorConditionKind.CreditsAtLeast, value: 15));
        Assert.Equal("bioroid", BehaviorCondition.RezzedSubtypeInAttackedServer("bioroid").Fact);
        Assert.Equal(2, BehaviorCondition.SourceHostsAtLeast(2).Value);
    }
}
