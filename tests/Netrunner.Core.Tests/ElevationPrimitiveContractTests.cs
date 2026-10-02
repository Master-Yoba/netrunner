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
}
