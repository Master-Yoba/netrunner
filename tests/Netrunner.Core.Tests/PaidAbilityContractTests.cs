using Xunit;

namespace Netrunner.Core.Tests;

public sealed class PaidAbilityContractTests
{
    private const string CatalogFingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DefinitionFingerprint = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly CardDefinitionId DefinitionId = new("test:ability-definition");
    private static readonly CardInstanceId SourceCardId = new("card:ability-source");
    private static readonly PlayerId Controller = new("player:ability-controller");
    private static readonly ZoneId SourceZoneId = new("zone:ability-source");
    private const ZoneKind SourceZoneKind = ZoneKind.RunnerRig;

    [Fact]
    public void DeclarationRejectsInvalidCombinationAndCollections()
    {
        Assert.Throws<ArgumentException>(() => Ability(
            timing: BehaviorAbilityTiming.AnyPaidWindow,
            activation: BehaviorAbilityActivation.ClickAction,
            costs: []));

        Assert.Throws<ArgumentException>(() => Ability(
            timing: BehaviorAbilityTiming.ActionPhase,
            activation: BehaviorAbilityActivation.PaidAbility,
            costs: [new BehaviorCost(BehaviorCostKind.Clicks)]));

        Assert.Throws<ArgumentException>(() => Ability(
            timing: BehaviorAbilityTiming.ActionPhase,
            activation: BehaviorAbilityActivation.PaidAbility,
            targets: [new BehaviorTarget(BehaviorTargetKind.AccessedCard)]));

        Assert.Throws<ArgumentNullException>(() => new BehaviorPaidAbility(
            "ability:null-costs",
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.PaidAbility,
            null!,
            [],
            [],
            [],
            BehaviorAbilityUsageLimit.Unlimited));

        Assert.Throws<ArgumentException>(() => new BehaviorPaidAbility(
            "ability:null-entry",
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.PaidAbility,
            [null!],
            [],
            [],
            [],
            BehaviorAbilityUsageLimit.Unlimited));

        Assert.Throws<ArgumentException>(() => Ability(
            costs:
            [
                new BehaviorCost(BehaviorCostKind.TrashSelf),
                new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame),
            ]));
    }

    [Fact]
    public void CostAmountsFollowThePaidAbilityContract()
    {
        Assert.Equal(0, new BehaviorCost(BehaviorCostKind.Credits, 0).Amount);

        foreach (BehaviorCost cost in new[]
        {
            new BehaviorCost(BehaviorCostKind.Clicks, 1),
            new BehaviorCost(BehaviorCostKind.LoseClick, 1),
            new BehaviorCost(BehaviorCostKind.Counter, 1, counterName: "counter"),
            new BehaviorCost(BehaviorCostKind.Tags, 1),
            new BehaviorCost(BehaviorCostKind.HostedCredits, 1),
            new BehaviorCost(BehaviorCostKind.Additional, 1, additionalKey: "additional"),
            new BehaviorCost(BehaviorCostKind.TrashSelf, 1),
            new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame, 1),
        })
        {
            Assert.True(cost.Amount > 0 || cost.Kind == BehaviorCostKind.Credits);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.Clicks, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.LoseClick, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.Counter, 0, counterName: "counter"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.Tags, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.HostedCredits, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.HostedCredits, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.Additional, 0, additionalKey: "additional"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.TrashSelf, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.TrashSelf, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame, 2));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.HostedCredits,
            1,
            counterName: "counter"));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.HostedCredits,
            1,
            additionalKey: "additional"));
    }

    [Fact]
    public void DeclarationDetachesAllCollectionsAndDefinitionRejectsDuplicateKeys()
    {
        var costs = new List<BehaviorCost> { new(BehaviorCostKind.Credits, 2) };
        var conditions = new List<BehaviorCondition> { new(BehaviorConditionKind.FactEquals, fact: "fact") };
        var targets = new List<BehaviorTarget> { new(BehaviorTargetKind.Self) };
        var instructions = new List<BehaviorInstruction> { new(BehaviorInstructionKind.NoOp) };
        BehaviorPaidAbility declaration = Ability(
            costs: costs,
            conditions: conditions,
            targets: targets,
            instructions: instructions);

        costs.Clear();
        conditions.Clear();
        targets.Clear();
        instructions.Clear();

        Assert.Single(declaration.Costs);
        Assert.Single(declaration.Conditions);
        Assert.Single(declaration.Targets);
        Assert.Single(declaration.Instructions);

        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(
            DefinitionId,
            PlayerSide.Runner,
            paidAbilities: [declaration, declaration]));
    }

    [Fact]
    public void EqualityAndCatalogFingerprintIncludeEveryAbilityField()
    {
        BehaviorPaidAbility baseline = Ability(
            costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
            conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "fact")],
            targets: [new BehaviorTarget(BehaviorTargetKind.Self)],
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        CardBehaviorDefinition baselineDefinition = Definition(baseline);
        BehaviorCatalog baselineCatalog = new BehaviorCatalogBuilder("test:ability-fingerprint")
            .Add(baselineDefinition)
            .Build();

        BehaviorPaidAbility[] variants =
        [
            Ability(
                key: "ability:other",
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "fact")],
                targets: [new BehaviorTarget(BehaviorTargetKind.Self)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            Ability(
                timing: BehaviorAbilityTiming.AnyPaidWindow,
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "fact")],
                targets: [new BehaviorTarget(BehaviorTargetKind.Self)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            Ability(
                activation: BehaviorAbilityActivation.ClickAction,
                costs: [new BehaviorCost(BehaviorCostKind.Clicks)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "fact")],
                targets: [new BehaviorTarget(BehaviorTargetKind.Self)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            Ability(
                usageLimit: BehaviorAbilityUsageLimit.OncePerTurn,
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "fact")],
                targets: [new BehaviorTarget(BehaviorTargetKind.Self)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            Ability(
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 2)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "fact")],
                targets: [new BehaviorTarget(BehaviorTargetKind.Self)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            Ability(
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "other")],
                targets: [new BehaviorTarget(BehaviorTargetKind.Self)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            Ability(
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "fact")],
                targets: [new BehaviorTarget(BehaviorTargetKind.Opponent)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            Ability(
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.FactEquals, fact: "fact")],
                targets: [new BehaviorTarget(BehaviorTargetKind.Self)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]),
        ];

        foreach (BehaviorPaidAbility variant in variants)
        {
            Assert.NotEqual(baseline, variant);
            BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:ability-fingerprint")
                .Add(Definition(variant))
                .Build();
            Assert.NotEqual(baselineCatalog.Fingerprint, catalog.Fingerprint);
        }
    }

    [Fact]
    public void AddingAnAbilityChangesOnlyItsOwningDefinitionFingerprint()
    {
        CardDefinitionId otherDefinitionId = new("test:ability-other-definition");
        CardBehaviorDefinition ownerWithoutAbility = new(DefinitionId, PlayerSide.Runner);
        CardBehaviorDefinition ownerWithAbility = Definition(Ability());
        CardBehaviorDefinition other = new(otherDefinitionId, PlayerSide.Runner);

        BehaviorCatalog before = new BehaviorCatalogBuilder("test:ability-fingerprint")
            .Add(ownerWithoutAbility)
            .Add(other)
            .Build();
        BehaviorCatalog after = new BehaviorCatalogBuilder("test:ability-fingerprint")
            .Add(ownerWithAbility)
            .Add(other)
            .Build();

        Assert.Equal(before.Version, after.Version);
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
        Assert.NotEqual(
            before.GetDefinitionFingerprint(DefinitionId),
            after.GetDefinitionFingerprint(DefinitionId));
        Assert.Equal(
            before.GetDefinitionFingerprint(otherDefinitionId),
            after.GetDefinitionFingerprint(otherDefinitionId));
    }

    [Fact]
    public void ActionAndPendingDecisionRoundTripAndCloneWithoutSharedCollections()
    {
        var costs = new List<BehaviorCost> { new(BehaviorCostKind.HostedCredits, 2) };
        var targets = new List<CardInstanceId> { new("card:ability-target") };
        AbilityActionParameters action = new(
            SourceCardId,
            DefinitionId,
            "ability:action",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            costs,
            targets,
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind);
        LegalAction legal = new(new ActionId("action:ability"), ActionKind.UseAbility, "Use ability", action);
        PendingDecision pending = new(
            new DecisionId("decision:ability"),
            1,
            Controller,
            DecisionKind.TurnAction,
            "Choose an ability",
            [legal]);

        costs.Add(new BehaviorCost(BehaviorCostKind.Tags));
        targets.Add(new CardInstanceId("card:detached"));
        Assert.Single(action.Costs);
        Assert.Single(action.TargetCardIds);

        string json = EngineJson.Serialize(pending);
        PendingDecision restored = EngineJson.Deserialize<PendingDecision>(json);
        AbilityActionParameters restoredAction = Assert.IsType<AbilityActionParameters>(
            Assert.Single(restored.LegalActions).Parameters);
        Assert.Equal(action.SourceCardId, restoredAction.SourceCardId);
        Assert.Equal(action.DefinitionId, restoredAction.DefinitionId);
        Assert.Equal(action.SourceZoneId, restoredAction.SourceZoneId);
        Assert.Equal(action.SourceZoneKind, restoredAction.SourceZoneKind);
        Assert.Equal(action.AbilityKey, restoredAction.AbilityKey);
        Assert.Equal(action.Timing, restoredAction.Timing);
        Assert.Equal(action.Activation, restoredAction.Activation);
        Assert.Equal(action.Costs, restoredAction.Costs);
        Assert.Equal(action.TargetCardIds, restoredAction.TargetCardIds);
        Assert.Equal(action.Usage, restoredAction.Usage);
        Assert.Equal(action.CatalogFingerprint, restoredAction.CatalogFingerprint);
        Assert.Equal(action.DefinitionFingerprint, restoredAction.DefinitionFingerprint);
        Assert.NotSame(action, restoredAction);
        Assert.Equal(json, EngineJson.Serialize(restored));
    }

    [Fact]
    public void FrameRoundTripAndCloneWithoutSharedCollections()
    {
        var costs = new List<BehaviorCost> { new(BehaviorCostKind.HostedCredits) };
        var targets = new List<CardInstanceId> { new("card:ability-target") };
        AbilityUsageSnapshot usage = new(BehaviorAbilityUsageLimit.OncePerTurn, 1);
        AbilityFrame frame = new(
            1,
            ResolutionBehaviorIds.Ability,
            DefinitionId,
            SourceCardId,
            "ability:frame",
            Controller,
            BehaviorAbilityTiming.EncounterPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            costs,
            targets,
            usage,
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind,
            AbilityLifecycleStage.AwaitingChild,
            "parent:ability");

        costs.Add(new BehaviorCost(BehaviorCostKind.Credits));
        targets.Add(new CardInstanceId("card:detached"));
        AbilityFrame clone = Assert.IsType<AbilityFrame>(ResolutionFrameCloner.Copy(frame));
        AbilityFrame restored = Assert.IsType<AbilityFrame>(
            EngineJson.Deserialize<ResolutionFrame>(EngineJson.Serialize<ResolutionFrame>(frame)));

        Assert.Single(frame.Costs);
        Assert.Single(frame.TargetCardIds);
        Assert.NotSame(frame, clone);
        Assert.NotSame(frame.Costs, clone.Costs);
        Assert.Equal(frame.DefinitionId, clone.DefinitionId);
        Assert.Equal(frame.SourceCardId, clone.SourceCardId);
        Assert.Equal(frame.SourceZoneId, clone.SourceZoneId);
        Assert.Equal(frame.SourceZoneKind, clone.SourceZoneKind);
        Assert.Equal(frame.AbilityKey, clone.AbilityKey);
        Assert.Equal(frame.Controller, clone.Controller);
        Assert.Equal(frame.Timing, clone.Timing);
        Assert.Equal(frame.Activation, clone.Activation);
        Assert.Equal(frame.Stage, clone.Stage);
        Assert.Equal(frame.Costs, clone.Costs);
        Assert.Equal(frame.TargetCardIds, clone.TargetCardIds);
        Assert.Equal(frame.Usage, clone.Usage);
        Assert.Equal(frame.CatalogFingerprint, clone.CatalogFingerprint);
        Assert.Equal(frame.DefinitionFingerprint, clone.DefinitionFingerprint);
        Assert.Equal(frame.ParentFrameIdentity, clone.ParentFrameIdentity);
        Assert.Equal(frame.DefinitionId, restored.DefinitionId);
        Assert.Equal(frame.SourceCardId, restored.SourceCardId);
        Assert.Equal(frame.SourceZoneId, restored.SourceZoneId);
        Assert.Equal(frame.SourceZoneKind, restored.SourceZoneKind);
        Assert.Equal(frame.AbilityKey, restored.AbilityKey);
        Assert.Equal(frame.Controller, restored.Controller);
        Assert.Equal(frame.Timing, restored.Timing);
        Assert.Equal(frame.Activation, restored.Activation);
        Assert.Equal(frame.Stage, restored.Stage);
        Assert.Equal(frame.Costs, restored.Costs);
        Assert.Equal(frame.TargetCardIds, restored.TargetCardIds);
        Assert.Equal(frame.Usage, restored.Usage);
        Assert.Equal(frame.CatalogFingerprint, restored.CatalogFingerprint);
        Assert.Equal(frame.DefinitionFingerprint, restored.DefinitionFingerprint);
        Assert.Equal(frame.ParentFrameIdentity, restored.ParentFrameIdentity);
    }

    [Fact]
    public void AdditionalCostSelectionsAreFrozenRoundTripAndLegacyEmptyFormsNormalize()
    {
        BehaviorTarget gripTarget = BehaviorTarget.CardsInZone(
            BehaviorZone.Grip,
            new BehaviorCardFilter(ownerSide: PlayerSide.Runner));
        BehaviorCost trashCost = new(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards",
            additionalTarget: gripTarget,
            additionalKind: BehaviorAdditionalCostKind.TrashCards);
        CardInstanceId accessedCardId = new("card:ability-accessed");
        CardInstanceId[] expectedCostCards =
        [
            new CardInstanceId("card:ability-cost-a"),
            new CardInstanceId("card:ability-cost-b"),
        ];
        var mutableCostCards = expectedCostCards.ToList();
        var mutableSelections = new List<AbilityAdditionalCostSelection>
        {
            new(0, "trash-cards", mutableCostCards),
        };
        AbilityActionParameters action = new(
            SourceCardId,
            DefinitionId,
            "ability:structured-action",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [trashCost],
            [accessedCardId],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind,
            mutableSelections);

        mutableCostCards.Add(new CardInstanceId("card:detached"));
        mutableSelections.Clear();
        AbilityAdditionalCostSelection actionSelection = Assert.Single(
            action.AdditionalCostSelectionsOrEmpty);
        Assert.Equal(0, actionSelection.CostIndex);
        Assert.Equal("trash-cards", actionSelection.CostKey);
        Assert.Equal(expectedCostCards, actionSelection.CardIds);

        string actionJson = EngineJson.Serialize(action);
        AbilityActionParameters restoredAction = EngineJson.Deserialize<AbilityActionParameters>(actionJson);
        Assert.Equal(actionJson, EngineJson.Serialize(restoredAction));
        AbilityAdditionalCostSelection restoredActionSelection = Assert.Single(
            restoredAction.AdditionalCostSelectionsOrEmpty);
        Assert.Equal(actionSelection.CostIndex, restoredActionSelection.CostIndex);
        Assert.Equal(actionSelection.CostKey, restoredActionSelection.CostKey);
        Assert.Equal(actionSelection.CardIds, restoredActionSelection.CardIds);

        var frameCostCards = new List<CardInstanceId>(expectedCostCards);
        var frameSelections = new List<AbilityAdditionalCostSelection>
        {
            new(0, "trash-cards", frameCostCards),
        };
        AbilityFrame frame = new(
            1,
            ResolutionBehaviorIds.Ability,
            DefinitionId,
            SourceCardId,
            "ability:structured-frame",
            Controller,
            BehaviorAbilityTiming.BreachAccessPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [trashCost],
            [accessedCardId],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind,
            AbilityLifecycleStage.AwaitingChild,
            parentFrameIdentity: "parent:ability",
            additionalCostSelections: frameSelections);
        frameCostCards.Add(new CardInstanceId("card:frame-detached"));
        frameSelections.Clear();
        AbilityAdditionalCostSelection frameSelection = Assert.Single(
            frame.AdditionalCostSelectionsOrEmpty);
        Assert.Equal(expectedCostCards, frameSelection.CardIds);

        string frameJson = EngineJson.Serialize<ResolutionFrame>(frame);
        AbilityFrame restoredFrame = Assert.IsType<AbilityFrame>(
            EngineJson.Deserialize<ResolutionFrame>(frameJson));
        Assert.Equal(frameJson, EngineJson.Serialize<ResolutionFrame>(restoredFrame));
        AbilityAdditionalCostSelection restoredFrameSelection = Assert.Single(
            restoredFrame.AdditionalCostSelectionsOrEmpty);
        Assert.Equal(frameSelection.CostIndex, restoredFrameSelection.CostIndex);
        Assert.Equal(frameSelection.CostKey, restoredFrameSelection.CostKey);
        Assert.Equal(frameSelection.CardIds, restoredFrameSelection.CardIds);

        AbilityActionParameters omittedAction = new(
            SourceCardId,
            DefinitionId,
            "ability:legacy-empty",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind);
        AbilityActionParameters emptyAction = new(
            SourceCardId,
            DefinitionId,
            "ability:legacy-empty",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind,
            []);
        Assert.Null(omittedAction.AdditionalCostSelections);
        Assert.Null(emptyAction.AdditionalCostSelections);
        Assert.Equal(EngineJson.Serialize(omittedAction), EngineJson.Serialize(emptyAction));
        Assert.Null(EngineJson.Deserialize<AbilityActionParameters>(
            EngineJson.Serialize(omittedAction)).AdditionalCostSelections);

        AbilityFrame omittedFrame = new(
            1,
            ResolutionBehaviorIds.Ability,
            DefinitionId,
            SourceCardId,
            "ability:legacy-frame",
            Controller,
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind,
            AbilityLifecycleStage.Complete);
        AbilityFrame emptyFrame = new(
            1,
            ResolutionBehaviorIds.Ability,
            DefinitionId,
            SourceCardId,
            "ability:legacy-frame",
            Controller,
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind,
            AbilityLifecycleStage.Complete,
            additionalCostSelections: []);
        Assert.Null(omittedFrame.AdditionalCostSelections);
        Assert.Null(emptyFrame.AdditionalCostSelections);
        Assert.Equal(
            EngineJson.Serialize<ResolutionFrame>(omittedFrame),
            EngineJson.Serialize<ResolutionFrame>(emptyFrame));
    }

    [Fact]
    public void ConstructorsRequireExactPinsAndUsageAndValidatorRejectsStaleShape()
    {
        Assert.Throws<ArgumentException>(() => new AbilityActionParameters(
            SourceCardId,
            DefinitionId,
            "ability:bad-catalog",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0),
            "bad",
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind));

        Assert.Throws<ArgumentNullException>(() => new AbilityActionParameters(
            SourceCardId,
            DefinitionId,
            "ability:bad-usage",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [],
            null!,
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind));

        AbilityFrame stale = new(
            1,
            ResolutionBehaviorIds.Ability,
            DefinitionId,
            SourceCardId,
            "ability:stale",
            Controller,
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.ClickAction,
            [new BehaviorCost(BehaviorCostKind.Clicks), new BehaviorCost(BehaviorCostKind.Clicks)],
            [],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            SourceZoneId,
            SourceZoneKind,
            AbilityLifecycleStage.PayCosts);

        Assert.Contains(
            ResolutionFrameValidator.Collect(stale),
            violation => violation.Code == "resolution.invalid-ability-combination");
    }

    [Fact]
    public void StateValidatorBindsAbilityActionsToDecisionState()
    {
        GameState seed = GameState.CreateNew(17);
        ZoneState hand = seed.GetZone(ZoneKind.CorpHand);
        AbilityActionParameters validAction = new(
            SourceCardId,
            DefinitionId,
            "ability:state",
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.ClickAction,
            [new BehaviorCost(BehaviorCostKind.Clicks)],
            [],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0),
            CatalogFingerprint,
            DefinitionFingerprint,
            hand.Id,
            hand.Kind);

        GameState valid = BuildAbilityState(seed, validAction);
        Assert.DoesNotContain(
            StateInvariantValidator.Collect(valid),
            violation => violation.Code.StartsWith("decision.ability-", StringComparison.Ordinal));

        GameState wrongPayload = BuildAbilityState(
            seed,
            validAction,
            parameters: new NoActionParameters());
        Assert.Contains(
            StateInvariantValidator.Collect(wrongPayload),
            violation => violation.Code == "decision.action-parameters-mismatch");

        GameState wrongDecision = BuildAbilityState(
            seed,
            validAction,
            decisionKind: DecisionKind.TurnTiming);
        Assert.Contains(
            StateInvariantValidator.Collect(wrongDecision),
            violation => violation.Code == "decision.ability-action-kind-mismatch");

        AbilityActionParameters badZone = new(
            validAction.SourceCardId,
            validAction.DefinitionId,
            validAction.AbilityKey,
            validAction.Timing,
            validAction.Activation,
            validAction.Costs,
            validAction.TargetCardIds,
            validAction.Usage,
            validAction.CatalogFingerprint,
            validAction.DefinitionFingerprint,
            seed.GetZone(ZoneKind.CorpDeck).Id,
            ZoneKind.CorpDeck);
        IReadOnlyList<StateInvariantViolation> badZoneViolations = StateInvariantValidator.Collect(
            BuildAbilityState(seed, badZone));
        Assert.Contains(
            badZoneViolations,
            violation => violation.Code == "decision.ability-source-zone-mismatch");
        Assert.Contains(
            badZoneViolations,
            violation => violation.Code == "decision.ability-source-membership-mismatch");

        AbilityActionParameters badPin = new(
            validAction.SourceCardId,
            validAction.DefinitionId,
            validAction.AbilityKey,
            validAction.Timing,
            validAction.Activation,
            validAction.Costs,
            validAction.TargetCardIds,
            validAction.Usage,
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            validAction.DefinitionFingerprint,
            validAction.SourceZoneId,
            validAction.SourceZoneKind);
        Assert.Contains(
            StateInvariantValidator.Collect(
                BuildAbilityState(seed, badPin, behaviorCatalogFingerprint: CatalogFingerprint)),
            violation => violation.Code == "decision.ability-catalog-state-mismatch");

        AbilityActionParameters unknownTarget = new(
            validAction.SourceCardId,
            validAction.DefinitionId,
            validAction.AbilityKey,
            validAction.Timing,
            validAction.Activation,
            validAction.Costs,
            [new CardInstanceId("card:ability-unknown")],
            validAction.Usage,
            validAction.CatalogFingerprint,
            validAction.DefinitionFingerprint,
            validAction.SourceZoneId,
            validAction.SourceZoneKind);
        Assert.Contains(
            StateInvariantValidator.Collect(BuildAbilityState(seed, unknownTarget)),
            violation => violation.Code == "decision.unknown-ability-target");
    }

    [Fact]
    public void StateValidatorChecksInjectedAbilityDefinitionShapeAndPins()
    {
        BehaviorPaidAbility declaration = Ability(
            key: "ability:state",
            timing: BehaviorAbilityTiming.AnyPaidWindow,
            activation: BehaviorAbilityActivation.PaidAbility,
            costs: [new BehaviorCost(BehaviorCostKind.Credits)]);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:ability-state-catalog")
            .Add(new CardBehaviorDefinition(DefinitionId, PlayerSide.Corp, paidAbilities: [declaration]))
            .Build();
        GameState seed = GameState.CreateNew(
            19,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState hand = seed.GetZone(ZoneKind.CorpHand);
        AbilityActionParameters action = new(
            SourceCardId,
            DefinitionId,
            declaration.Key,
            declaration.Timing,
            declaration.Activation,
            declaration.Costs,
            [],
            new AbilityUsageSnapshot(declaration.UsageLimit, 0),
            catalog.Fingerprint,
            catalog.GetDefinitionFingerprint(DefinitionId),
            seed.Servers.Single(value => value.Kind == ServerKind.Headquarters).RootZoneId,
            ZoneKind.ServerRoot);

        Assert.Empty(StateInvariantValidator.Collect(
            BuildAbilityState(seed, action, DecisionKind.TurnTiming, activePaidSource: true),
            catalog));

        AbilityActionParameters wrongShape = new(
            action.SourceCardId,
            action.DefinitionId,
            action.AbilityKey,
            action.Timing,
            action.Activation,
            [],
            action.TargetCardIds,
            action.Usage,
            action.CatalogFingerprint,
            action.DefinitionFingerprint,
            action.SourceZoneId,
            action.SourceZoneKind);
        Assert.Contains(
            StateInvariantValidator.Collect(
                BuildAbilityState(seed, wrongShape, DecisionKind.TurnTiming, activePaidSource: true),
                catalog),
            violation => violation.Code == "decision.ability-costs-mismatch");
    }

    private static CardBehaviorDefinition Definition(BehaviorPaidAbility ability) =>
        new(DefinitionId, PlayerSide.Runner, paidAbilities: [ability]);

    private static GameState BuildAbilityState(
        GameState seed,
        AbilityActionParameters action,
        DecisionKind decisionKind = DecisionKind.TurnAction,
        ActionParameters? parameters = null,
        string? behaviorRegistryVersion = null,
        string? behaviorCatalogFingerprint = null,
        bool activePaidSource = false)
    {
        PlayerId owner = seed.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        ZoneState hand = seed.GetZone(ZoneKind.CorpHand);
        ServerState headquarters = seed.Servers.Single(value => value.Kind == ServerKind.Headquarters);
        ZoneState sourceZone = activePaidSource
            ? seed.GetZone(headquarters.RootZoneId)
            : hand;
        CardInstanceState sourceCard = new(
            action.SourceCardId,
            action.DefinitionId,
            sourceZone.Id,
            PlayerSide.Corp,
            faceUp: activePaidSource,
            rezzed: activePaidSource);
        ZoneState[] zones = seed.Zones
            .Select(zone => zone.Id == sourceZone.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(action.SourceCardId).ToArray())
                : zone)
            .ToArray();
        LegalAction ability = new(
            new ActionId("action:ability-state"),
            ActionKind.UseAbility,
            "Use ability",
            parameters ?? action);
        LegalAction[] actions = decisionKind == DecisionKind.TurnAction
            ? [
                ability,
                new LegalAction(
                    new ActionId("action:ability-gain"),
                    ActionKind.GainCredit,
                    "Gain credit",
                    new NoActionParameters()),
                new LegalAction(
                    new ActionId("action:ability-purge"),
                    ActionKind.PurgeVirusCounters,
                    "Purge viruses",
                    new PurgeVirusCountersParameters(RealGameRules.PurgeVirusCountersClickCost)),
            ]
            : activePaidSource
                ? [
                    new LegalAction(
                        new ActionId("action:ability-pass"),
                        ActionKind.PassPriority,
                        "Pass priority",
                        new NoActionParameters()),
                    ability,
                ]
                : [ability];
        PendingDecision pending = new(
            new DecisionId("decision:ability-state"),
            1,
            owner,
            decisionKind,
            "Choose an ability",
            actions);
        TurnState turn = activePaidSource
            ? new TurnState(
                PlayerSide.Corp,
                TurnPhase.Corp,
                seed.Turn.TurnNumber,
                TurnStep.CorpDraw,
                new TurnHistoryState(
                    1,
                    new TurnFactsState(
                        1,
                        PlayerSide.Corp,
                        seed.Turn.TurnNumber,
                        3,
                        0,
                        turnBegan: true,
                        mandatoryDrawPerformed: true,
                        actionPhaseEnded: false,
                        discardPhaseEnded: false),
                    null,
                    null,
                    null))
            : seed.Turn;
        TurnFrame frame = activePaidSource
            ? new TurnFrame(
                1,
                ResolutionBehaviorIds.Turn,
                PlayerSide.Corp,
                seed.Turn.TurnNumber,
                TurnFrameStage.BeforeTurnPriority,
                TurnPriorityWindowKind.PaidRezAndScore,
                PlayerSide.Corp)
            : new TurnFrame(PlayerSide.Corp, seed.Turn.TurnNumber, TurnFrameStage.AwaitingAction);
        return new GameState(
            seed.SchemaVersion,
            seed.GameId,
            seed.RulesVersion,
            seed.CardDataVersion,
            seed.Players,
            [sourceCard],
            zones,
            seed.Servers,
            turn,
            seed.Outcome,
            seed.RandomState,
            seed.Allocator,
            decisionRevision: 1,
            eventSequence: seed.EventSequence,
            resolutionStack: [frame],
            pendingDecision: pending,
            actionSequence: seed.ActionSequence,
            initialSeed: seed.InitialSeed,
            runState: seed.RunState,
            rulesState: seed.RulesState,
            behaviorRegistryVersion: behaviorRegistryVersion ?? seed.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: behaviorCatalogFingerprint ?? seed.BehaviorCatalogFingerprint);
    }

    private static BehaviorPaidAbility Ability(
        string key = "ability:test",
        BehaviorAbilityTiming timing = BehaviorAbilityTiming.ActionPhase,
        BehaviorAbilityActivation activation = BehaviorAbilityActivation.PaidAbility,
        BehaviorAbilityUsageLimit usageLimit = BehaviorAbilityUsageLimit.Unlimited,
        IReadOnlyList<BehaviorCost>? costs = null,
        IReadOnlyList<BehaviorCondition>? conditions = null,
        IReadOnlyList<BehaviorTarget>? targets = null,
        IReadOnlyList<BehaviorInstruction>? instructions = null) =>
        new(
            key,
            timing,
            activation,
            costs ?? [],
            conditions ?? [],
            targets ?? [],
            instructions ?? [],
            usageLimit);
}
