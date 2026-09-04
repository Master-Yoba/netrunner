using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Adversarial coverage for the Slice 4.5 Core trust boundaries. These tests
/// exercise detached payload construction and the structural behavior filter
/// without relying on card titles or presentation text.
/// </summary>
public sealed class Slice45CoreHardeningTests
{
    [Theory]
    [InlineData(IceInstallDestinationRule.Normal, IceInstallCostRule.IgnoreAllCosts)]
    [InlineData(IceInstallDestinationRule.DirectlyInward, IceInstallCostRule.Normal)]
    public void InstallSubroutineRejectsUnsupportedDestinationCostPair(
        IceInstallDestinationRule destinationRule,
        IceInstallCostRule costRule)
    {
        Assert.Throws<ArgumentException>(() => new InstallCardSubroutineSpec(
            destinationRule,
            costRule,
            iceOnly: destinationRule == IceInstallDestinationRule.DirectlyInward));
    }

    [Fact]
    public void IceSubroutineChoiceRejectsExtraneousCardAndPaymentFields()
    {
        const string frame = "frame:ice-choice";
        CardInstanceId card = new("card:choice-target");

        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.PayCredits,
            cardId: card,
            creditCost: 1));
        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.TakeTag,
            cardId: card));
        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.Decline,
            cardId: card));
        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.Decline,
            creditCost: 1));
        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.TakeTag,
            hostedCredits: 1));
    }

    [Fact]
    public void IceSubroutineInstallChoiceRequiresCorpServerDestinationShape()
    {
        const string frame = "frame:ice-choice-install";
        CardInstanceId card = new("card:corp-install");
        ServerId server = new("server:remote");

        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.InstallCard,
            cardId: card,
            destinationKind: InstallDestinationKind.RunnerRig));
        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.InstallCard,
            cardId: card,
            destinationKind: InstallDestinationKind.ServerIce));
        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.InstallCard,
            cardId: card,
            destinationKind: InstallDestinationKind.ServerRoot,
            serverId: server,
            createNewRemote: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IceSubroutineChoiceParameters(
            frame,
            IceSubroutineChoiceKind.InstallCard,
            cardId: card,
            destinationKind: (InstallDestinationKind)999,
            serverId: server));
    }

    [Fact]
    public void RestrictionFilterBlocksOnlyAuthenticatedCorpCardTrashDeclarations()
    {
        BehaviorPaidAbility unrelated = new(
            "ability:gain-credit",
            BehaviorAbilityTiming.BreachAccessPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            costs: [],
            conditions: [],
            targets: [],
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
            BehaviorAbilityUsageLimit.Unlimited);
        BehaviorTarget accessedCard = new(BehaviorTargetKind.AccessedCard);
        BehaviorPaidAbility trashAccessedCard = new(
            "ability:trash-accessed",
            BehaviorAbilityTiming.BreachAccessPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            costs: [],
            conditions: [],
            targets: [accessedCard],
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: accessedCard)],
            BehaviorAbilityUsageLimit.Unlimited);
        BehaviorTarget corpCards = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            new BehaviorCardFilter(ownerSide: PlayerSide.Corp));
        BehaviorPaidAbility corpTrashAdditionalCost = new(
            "ability:trash-corp-cost",
            BehaviorAbilityTiming.BreachAccessPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            costs:
            [
                new BehaviorCost(
                    BehaviorCostKind.Additional,
                    amount: 1,
                    additionalKey: "cost:corp-card",
                    additionalTarget: corpCards,
                    additionalKind: BehaviorAdditionalCostKind.TrashCards),
            ],
            conditions: [],
            targets: [],
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
            BehaviorAbilityUsageLimit.Unlimited);

        Assert.False(GameEngine.IsAbilityBlockedByRunRestriction(unrelated));
        Assert.True(GameEngine.IsAbilityBlockedByRunRestriction(trashAccessedCard));
        Assert.True(GameEngine.IsAbilityBlockedByRunRestriction(corpTrashAdditionalCost));
    }

    [Fact]
    public void ResolvedSubroutineLedgerEntryRoundTripsAsStructuralData()
    {
        RunResolvedSubroutineLedgerEntry entry = new(
            new RunId("run:ledger"),
            new EncounterId("encounter:run:ledger:ice"),
            new CardInstanceId("card:ledger-ice"),
            new CardDefinitionId("card-def:ledger-ice"),
            new SubroutineId("subroutine:encounter:run:ledger:ice:0"),
            ordinal: 0);

        string json = EngineJson.Serialize(entry);
        RunResolvedSubroutineLedgerEntry restored = EngineJson.Deserialize<RunResolvedSubroutineLedgerEntry>(json);
        Assert.Equal(entry, restored);
        Assert.Equal(json, EngineJson.Serialize(restored));
    }
}
