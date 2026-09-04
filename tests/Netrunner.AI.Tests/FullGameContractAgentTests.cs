using System.Text.Json;
using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class FullGameContractAgentTests
{
    [Fact]
    public void PolicyIsVersionedTypedAndDefensivelyCopied()
    {
        ActionKind[] turnOrder = [ActionKind.DrawCard, ActionKind.GainCredit];
        ActionKind[] runOrder = [ActionKind.JackOut, ActionKind.ContinueRun, ActionKind.PassPriority];
        FullGameContractPolicy policy = new(PlayerSide.Runner, turnActionOrder: turnOrder, runActionOrder: runOrder);

        turnOrder[0] = ActionKind.EndTurn;
        runOrder[0] = ActionKind.EndTurn;

        Assert.Equal(FullGameContractAgentVersion.SchemaVersion, policy.SchemaVersion);
        Assert.Equal(FullGameContractAgentVersion.PolicyVersion, policy.PolicyVersion);
        Assert.Equal(3, FullGameContractAgentVersion.SchemaVersion);
        Assert.Equal("full-game-contract-agent-v3", FullGameContractAgentVersion.AgentVersion);
        Assert.Equal("full-game-contract-policy-v3", FullGameContractAgentVersion.PolicyVersion);
        Assert.Equal(PlayerSide.Runner, policy.ControlledSide);
        Assert.Equal(ActionKind.DrawCard, policy.TurnActionOrder[0]);
        Assert.Equal(ActionKind.JackOut, policy.RunActionOrder[0]);

        FullGameContractAgent agent = new(policy);
        Assert.Equal(FullGameContractAgentVersion.AgentVersion, agent.AgentVersion);
        Assert.Same(policy, agent.Policy);
    }

    [Fact]
    public async Task TurnActionDispatchUsesTypedGainAndDrawPriorityAndNeverEndsTurn()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction gain = Action("gain", ActionKind.GainCredit, new NoActionParameters());
        LegalAction draw = Action("draw", ActionKind.DrawCard, new NoActionParameters());
        LegalAction end = Action("end", ActionKind.EndTurn, new NoActionParameters());

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction, end, draw, gain));

        Assert.Equal(gain.Id, selected);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction, end)));

        Assert.Equal(
            draw.Id,
            await agent.ChooseActionAsync(ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction, draw)));
    }

    [Fact]
    public async Task BoundedClickAbilityPrecedesEconomyActions()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction ability = Action(
            "click-ability",
            ActionKind.UseAbility,
            AbilityParameters(
                "click-ability",
                timing: BehaviorAbilityTiming.ActionPhase,
                activation: BehaviorAbilityActivation.ClickAction,
                costs: [new BehaviorCost(BehaviorCostKind.Clicks)]));
        LegalAction gain = Action("gain-after-ability", ActionKind.GainCredit, new NoActionParameters());
        LegalAction draw = Action("draw-after-ability", ActionKind.DrawCard, new NoActionParameters());

        Assert.Equal(
            ability.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, draw, gain, ability)));
    }

    [Fact]
    public async Task PaidLoseClickAbilityIsBoundedAndPreferredInRunTiming()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction ability = Action(
            "paid-lose-click",
            ActionKind.UseAbility,
            AbilityParameters(
                "paid-lose-click",
                timing: BehaviorAbilityTiming.AnyPaidWindow,
                activation: BehaviorAbilityActivation.PaidAbility,
                costs: [new BehaviorCost(BehaviorCostKind.LoseClick)]));
        LegalAction pass = Action("run-pass", ActionKind.PassPriority, new NoActionParameters());

        Assert.Equal(
            ability.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.RunTiming, pass, ability)));
    }

    [Fact]
    public async Task UnusedOncePerTurnNonclickAbilityIsPreferredAndExhaustedOneFallsBack()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction available = Action(
            "once-available",
            ActionKind.UseAbility,
            AbilityParameters(
                "once-available",
                timing: BehaviorAbilityTiming.AnyPaidWindow,
                activation: BehaviorAbilityActivation.PaidAbility,
                costs: [new BehaviorCost(BehaviorCostKind.Credits)],
                usage: new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 0)));
        LegalAction pass = Action("turn-pass", ActionKind.PassPriority, new NoActionParameters());

        Assert.Equal(
            available.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.TurnTiming, pass, available)));

        LegalAction exhausted = Action(
            "once-exhausted",
            ActionKind.UseAbility,
            AbilityParameters(
                "once-exhausted",
                timing: BehaviorAbilityTiming.AnyPaidWindow,
                activation: BehaviorAbilityActivation.PaidAbility,
                costs: [new BehaviorCost(BehaviorCostKind.Credits)],
                usage: new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 1)));
        Assert.Equal(
            pass.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.TurnTiming, exhausted, pass)));
    }

    [Fact]
    public async Task UnlimitedNonclickAbilityIsIgnoredInFavorOfSupportedFallback()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction unlimited = Action(
            "unlimited-nonclick",
            ActionKind.UseAbility,
            AbilityParameters(
                "unlimited-nonclick",
                timing: BehaviorAbilityTiming.AnyPaidWindow,
                activation: BehaviorAbilityActivation.PaidAbility,
                costs: [new BehaviorCost(BehaviorCostKind.Credits)],
                usage: new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0)));
        LegalAction pass = Action("unlimited-pass", ActionKind.PassPriority, new NoActionParameters());

        Assert.Equal(
            pass.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.TurnTiming, unlimited, pass)));
    }

    [Fact]
    public async Task AbilitySelectionIsDeterministicAcrossActionOrderDescriptionsAndJsonRoundtrip()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction first = Action(
            "ability-first",
            ActionKind.UseAbility,
            AbilityParameters(
                "ability-first",
                timing: BehaviorAbilityTiming.AnyPaidWindow,
                activation: BehaviorAbilityActivation.PaidAbility,
                costs: [new BehaviorCost(BehaviorCostKind.Credits)],
                usage: new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 0)),
            "description one");
        LegalAction second = Action(
            "ability-second",
            ActionKind.UseAbility,
            AbilityParameters(
                "ability-second",
                timing: BehaviorAbilityTiming.AnyPaidWindow,
                activation: BehaviorAbilityActivation.PaidAbility,
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 2)],
                usage: new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 0)),
            "description two");
        LegalAction pass = Action("ability-pass", ActionKind.PassPriority, new NoActionParameters());

        ActionId before = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TurnTiming, second, pass, first));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TurnTiming, first, second, pass));
        string payload = JsonSerializer.Serialize(new[] { second, pass, first });
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(payload)
            ?? throw new InvalidOperationException("The serialized ability payload was empty.");
        ActionId roundTrip = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TurnTiming, restored));

        Assert.Equal(first.Id, before);
        Assert.Equal(before, reordered);
        Assert.Equal(before, roundTrip);
    }

    [Fact]
    public async Task BoundedAbilityCanCoexistWithUnsupportedTypedActionClasses()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction ability = Action(
            "ability-with-install",
            ActionKind.UseAbility,
            AbilityParameters(
                "ability-with-install",
                timing: BehaviorAbilityTiming.ActionPhase,
                activation: BehaviorAbilityActivation.ClickAction,
                costs: [new BehaviorCost(BehaviorCostKind.Clicks)]));
        LegalAction install = Action(
            "opaque-install",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:install"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 0));
        LegalAction startRun = Action(
            "opaque-start-run",
            ActionKind.StartRun,
            new NoActionParameters());
        LegalAction gain = Action("ability-gain", ActionKind.GainCredit, new NoActionParameters());

        Assert.Equal(
            ability.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, install, gain, startRun, ability)));
    }

    [Fact]
    public async Task MalformedAbilityPayloadFailsClosedEvenWithFallback()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        // AbilityActionParameters itself is a detached snapshot and permits
        // construction before its parent decision is known. The agent must
        // reject this impossible click-action/AnyPaidWindow combination.
        LegalAction malformed = Action(
            "malformed-ability",
            ActionKind.UseAbility,
            AbilityParameters(
                "malformed-ability",
                timing: BehaviorAbilityTiming.AnyPaidWindow,
                activation: BehaviorAbilityActivation.ClickAction,
                costs: [new BehaviorCost(BehaviorCostKind.Clicks)]));
        LegalAction gain = Action("malformed-gain", ActionKind.GainCredit, new NoActionParameters());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, malformed, gain)));

        LegalAction unsupportedParameters = Action(
            "unsupported-ability-parameters",
            ActionKind.UseAbility,
            new NoActionParameters());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, unsupportedParameters, gain)));
    }

    [Fact]
    public async Task SemanticTieBreakingIsIndependentOfLegalActionOrder()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction high = Action(
            "discard-high",
            ActionKind.ChooseCard,
            new CardChoiceParameters(new CardInstanceId("card:20")));
        LegalAction low = Action(
            "discard-low",
            ActionKind.ChooseCard,
            new CardChoiceParameters(new CardInstanceId("card:10")));

        ActionId first = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.TurnDiscard, high, low));
        ActionId replay = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.TurnDiscard, low, high));

        Assert.Equal(low.Id, first);
        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task StableActionIdBreaksIdenticalTypedSemanticKeys()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction laterId = Action("z", ActionKind.GainCredit, new NoActionParameters());
        LegalAction earlierId = Action("a", ActionKind.GainCredit, new NoActionParameters());

        ActionId first = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction, laterId, earlierId));
        ActionId replay = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction, earlierId, laterId));

        Assert.Equal(earlierId.Id, first);
        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task BehaviorChoiceDispatchUsesOptionKeysAndStableTieBreaking()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction draw = Action(
            "behavior-draw",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("planogram", "draw"));
        LegalAction gain = Action(
            "behavior-gain",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("planogram", "gain"));

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.BehaviorChoice, draw, gain));
        Assert.Equal(gain.Id, selected);

        LegalAction duplicate = Action(
            "behavior-gain-duplicate",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("planogram", "gain"));
        Assert.Equal(
            gain.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.BehaviorChoice, duplicate, gain)));

        LegalAction targetHigh = Action(
            "behavior-target-high",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters(
                "target",
                [new CardInstanceId("card:20")]));
        LegalAction targetLow = Action(
            "behavior-target-low",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters(
                "target",
                [new CardInstanceId("card:10")]));
        Assert.Equal(
            targetLow.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.BehaviorChoice, targetHigh, targetLow)));
    }

    [Fact]
    public async Task ActionDescriptionsAreNotPolicyInput()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction gain = Action(
            "gain",
            ActionKind.GainCredit,
            new NoActionParameters(),
            "draw three cards now");
        LegalAction draw = Action(
            "draw",
            ActionKind.DrawCard,
            new NoActionParameters(),
            "gain nine credits instead");

        ActionId first = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction, draw, gain));

        LegalAction changedGain = Action(
            "gain",
            ActionKind.GainCredit,
            new NoActionParameters(),
            "this description now says draw");
        LegalAction changedDraw = Action(
            "draw",
            ActionKind.DrawCard,
            new NoActionParameters(),
            "this description now says gain");
        ActionId replay = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction, changedGain, changedDraw));

        Assert.Equal(gain.Id, first);
        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task InstallUtilityPrefersTheCurrentDiscountedTypedQuote()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        CostQuote discountedQuote = new(
            5,
            [new CostAdjustment(
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                -2,
                "dzmz-first-program-install",
                new CardDefinitionId("definition:dzmz"),
                new CardInstanceId("source:dzmz"))]);
        LegalAction unmodified = Action(
            "a-unmodified",
            ActionKind.InstallCard,
            InstallParameters("unmodified", 4),
            "install the expensive alternative");
        LegalAction discounted = Action(
            "z-discounted",
            ActionKind.InstallCard,
            InstallParameters("discounted", discountedQuote.EffectiveCost, discountedQuote),
            "this description is intentionally misleading");

        Assert.Equal(
            discounted.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, unmodified, discounted)));
    }

    [Fact]
    public async Task InstallUtilityDoesNotRetainAConsumedFirstXBonus()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        CostQuote firstInstallQuote = new(
            5,
            [new CostAdjustment(
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                -2,
                "dzmz-first-program-install",
                new CardDefinitionId("definition:dzmz"),
                new CardInstanceId("source:dzmz"))]);
        LegalAction discounted = Action(
            "dzmz-discounted",
            ActionKind.InstallCard,
            InstallParameters("dzmz", firstInstallQuote.EffectiveCost, firstInstallQuote),
            "first observation");
        LegalAction alternative = Action(
            "alternative",
            ActionKind.InstallCard,
            InstallParameters("alternative", 4),
            "first observation alternative");

        Assert.Equal(
            discounted.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, alternative, discounted)));

        // Core consumes the First-X source at the accepted install boundary.
        // The next observation therefore carries no quote for the same card;
        // the stateless agent must rank the newly issued effective costs, not
        // remember the previous observation's discount.
        LegalAction unmodifiedDzmz = Action(
            "dzmz-unmodified",
            ActionKind.InstallCard,
            InstallParameters("dzmz", 5),
            "second observation");
        LegalAction currentAlternative = Action(
            "current-alternative",
            ActionKind.InstallCard,
            InstallParameters("alternative", 4),
            "second observation alternative");

        Assert.Equal(
            currentAlternative.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, unmodifiedDzmz, currentAlternative)));
    }

    [Fact]
    public async Task InstallSelectionIsNeutralAcrossOrderDescriptionsAndJsonRoundtrip()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        CostQuote discountedQuote = new(
            3,
            [new CostAdjustment(
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                -1,
                "typed-discount",
                new CardDefinitionId("definition:source"),
                new CardInstanceId("source:source"))]);
        LegalAction first = Action(
            "install-first",
            ActionKind.InstallCard,
            InstallParameters("first", 2, discountedQuote),
            "description one");
        LegalAction second = Action(
            "install-second",
            ActionKind.InstallCard,
            InstallParameters("second", 3),
            "description two");

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, second, first));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, first, second));
        string payload = JsonSerializer.Serialize(new[] { second, first });
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(payload)
            ?? throw new InvalidOperationException("The serialized install payload was empty.");
        ActionId roundTrip = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, restored));

        Assert.Equal(first.Id, selected);
        Assert.Equal(selected, reordered);
        Assert.Equal(selected, roundTrip);
        Assert.Contains(restored, action => action.Id == roundTrip);
    }

    [Fact]
    public async Task ForgedOrMalformedInstallPayloadFailsClosed()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        CostQuote wrongQuoteKind = new(
            3,
            [new CostAdjustment(
                BehaviorCostModifierKind.IceRezCreditCost,
                -1,
                "forged-install-quote",
                new CardDefinitionId("definition:source"),
                new CardInstanceId("source:source"))]);
        LegalAction forgedQuote = Action(
            "forged-quote",
            ActionKind.InstallCard,
            InstallParameters("forged", wrongQuoteKind.EffectiveCost, wrongQuoteKind));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, forgedQuote)));

        LegalAction invalidSelection = Action(
            "invalid-selection",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:invalid-selection"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 0,
                candidateReplacementCardIds: [new CardInstanceId("card:candidate")],
                requiredReplacementCardIds: [new CardInstanceId("card:not-a-candidate")],
                selectedReplacementCardIds: [],
                finalizeSelection: false));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, invalidSelection)));
    }

    [Fact]
    public async Task SelectingAnExistingActionIdIsNeutralAcrossTypedSerialization()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction high = Action(
            "high",
            ActionKind.ChooseCard,
            new CardChoiceParameters(new CardInstanceId("card:20")));
        LegalAction low = Action(
            "low",
            ActionKind.ChooseCard,
            new CardChoiceParameters(new CardInstanceId("card:10")));

        ActionId before = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.TurnDiscard, high, low));
        string payload = JsonSerializer.Serialize(new[] { high, low });
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(payload)
            ?? throw new InvalidOperationException("The serialized legal-action payload was empty.");
        ActionId after = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.TurnDiscard, restored));

        Assert.Equal(before, after);
        Assert.Contains(restored, action => action.Id == after);
    }

    [Fact]
    public async Task RunWindowDispatchCoversEveryCurrentlyMaterializedRunAction()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);

        Assert.Equal(
            "issued:pass",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action("pass", ActionKind.PassPriority, new NoActionParameters())))).Value);
        Assert.Equal(
            "issued:rez",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action("rez", ActionKind.RezIce, new RunIceParameters(new CardInstanceId("ice:1")))))).Value);
        Assert.Equal(
            "issued:break",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action(
                    "break",
                    ActionKind.BreakSubroutines,
                    new RunBreakParameters(new CardInstanceId("breaker:1"), 1))))).Value);
        Assert.Equal(
            "issued:pump",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action(
                    "pump",
                    ActionKind.PumpBreaker,
                    new RunBreakerParameters(new CardInstanceId("breaker:1")))))).Value);
        Assert.Equal(
            "issued:breach",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action(
                    "breach",
                    ActionKind.ChooseBreachCandidate,
                    new BreachCandidateParameters(new CardInstanceId("card:1")))))).Value);
        Assert.Equal(
            "issued:trash",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action(
                    "trash",
                    ActionKind.TrashAccessedCard,
                    new RunCardPaymentParameters(new CardInstanceId("card:1")))))).Value);
        Assert.Equal(
            "issued:no-access",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action("no-access", ActionKind.NoAccessAction, new NoActionParameters())))).Value);
        Assert.Equal(
            "issued:continue",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action("continue", ActionKind.ContinueRun, new NoActionParameters())))).Value);
        Assert.Equal(
            "issued:jack-out",
            (await agent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.RunTiming,
                Action("jack-out", ActionKind.JackOut, new NoActionParameters())))).Value);

        LegalAction lowerBreaker = Action(
            "break-low",
            ActionKind.BreakSubroutines,
            new RunBreakParameters(new CardInstanceId("breaker:1"), 1));
        LegalAction higherBreaker = Action(
            "break-high",
            ActionKind.BreakSubroutines,
            new RunBreakParameters(new CardInstanceId("breaker:2"), 1));
        Assert.Equal(
            lowerBreaker.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.RunTiming, higherBreaker, lowerBreaker)));
    }

    [Fact]
    public async Task TurnTimingUsesPassAndDoesNotSelectAnEndTurnOffer()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction pass = Action("pass", ActionKind.PassPriority, new NoActionParameters());
        LegalAction end = Action("end", ActionKind.EndTurn, new NoActionParameters());

        Assert.Equal(
            pass.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.TurnTiming, end, pass)));
    }

    [Fact]
    public async Task OwnerAndDecisionMetadataAreCheckedBeforeDispatch()
    {
        FullGameContractAgent corpAgent = new(PlayerSide.Corp);
        LegalAction gain = Action("gain", ActionKind.GainCredit, new NoActionParameters());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await corpAgent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, gain)));

        Observation mismatchedOwner = ObservationFor(
            PlayerSide.Corp,
            DecisionKind.TurnAction,
            [gain],
            ownerSide: PlayerSide.Runner);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await corpAgent.ChooseActionAsync(mismatchedOwner));

        Observation mismatchedDecisionId = ObservationFor(
            PlayerSide.Corp,
            DecisionKind.TurnAction,
            [gain],
            ownerSide: PlayerSide.Corp,
            decisionId: "decision:other",
            typedDecisionId: "decision:turn-action");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await corpAgent.ChooseActionAsync(mismatchedDecisionId));
    }

    [Fact]
    public async Task UnsupportedOrEmptyDecisionsFailClosed()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(
                    PlayerSide.Corp,
                    DecisionKind.TurnAction,
                    Action("legacy", ActionKind.Legacy, new LegacyActionParameters("draw")))));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(
                    PlayerSide.Corp,
                    DecisionKind.TriggerChoice,
                    Action("trigger", ActionKind.UseTrigger, new TriggerChoiceParameters(new TriggerId("trigger:1"))))));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(
                    PlayerSide.Corp,
                    DecisionKind.TurnAction,
                    Action("future-install", ActionKind.InstallCard, new CardChoiceParameters(new CardInstanceId("card:1"))))));

        Observation withoutDecision = new(
            PlayerSide.Corp,
            "decision:turn-action",
            [Action("gain", ActionKind.GainCredit, new NoActionParameters())],
            new ThrowingReadOnlyDictionary(),
            new ThrowingReadOnlyDictionary());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(withoutDecision));
    }

    [Fact]
    public async Task AgentReadsOnlyTypedObservationSurfaceAndReturnsIssuedActionId()
    {
        LegalAction issued = Action("engine-issued-id", ActionKind.GainCredit, new NoActionParameters());
        Observation observation = ObservationFor(PlayerSide.Corp, DecisionKind.TurnAction, issued);

        ActionId selected = await new FullGameContractAgent(PlayerSide.Corp).ChooseActionAsync(observation);

        Assert.Equal(issued.Id, selected);
        Assert.DoesNotContain(
            typeof(FullGameContractAgent).GetMethods()
                .SelectMany(method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(GameState));
    }

    [Fact]
    public async Task CancellationIsHonoredBeforeSelection()
    {
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new FullGameContractAgent(PlayerSide.Corp).ChooseActionAsync(
                ObservationFor(
                    PlayerSide.Corp,
                    DecisionKind.TurnAction,
                    Action("gain", ActionKind.GainCredit, new NoActionParameters())),
                canceled.Token));
    }

    [Fact]
    public void PolicyRejectsEndTurnAndDuplicateOrUnsupportedPriorityEntries()
    {
        FullGameContractPolicy defaultPolicy = FullGameContractPolicy.Default(PlayerSide.Corp);
        Assert.Equal(ActionKind.UseAbility, defaultPolicy.TurnActionOrder[0]);
        Assert.Equal(ActionKind.UseAbility, defaultPolicy.RunActionOrder[0]);

        FullGameContractPolicy configured = new(
            PlayerSide.Corp,
            turnActionOrder: [ActionKind.UseAbility, ActionKind.GainCredit],
            runActionOrder: [ActionKind.UseAbility, ActionKind.PassPriority]);
        Assert.Contains(ActionKind.UseAbility, configured.TurnActionOrder);
        Assert.Contains(ActionKind.UseAbility, configured.RunActionOrder);

        Assert.Throws<ArgumentException>(() => new FullGameContractPolicy(
            PlayerSide.Corp,
            runActionOrder: [ActionKind.EndTurn]));
        Assert.Throws<ArgumentException>(() => new FullGameContractPolicy(
            PlayerSide.Corp,
            turnActionOrder: [ActionKind.GainCredit, ActionKind.GainCredit]));
        Assert.Throws<ArgumentException>(() => new FullGameContractPolicy(
            PlayerSide.Corp,
            turnActionOrder: [ActionKind.UseAbility, ActionKind.UseAbility]));
        Assert.Throws<ArgumentException>(() => new FullGameContractPolicy(
            PlayerSide.Corp,
            runActionOrder: [ActionKind.UseAbility, ActionKind.UseAbility]));
        Assert.Throws<ArgumentException>(() => new FullGameContractPolicy(
            PlayerSide.Corp,
            runActionOrder: [ActionKind.UseTrigger]));
    }

    private static AbilityActionParameters AbilityParameters(
        string id,
        BehaviorAbilityTiming timing,
        BehaviorAbilityActivation activation,
        IReadOnlyList<BehaviorCost> costs,
        AbilityUsageSnapshot? usage = null,
        IReadOnlyList<CardInstanceId>? targetCardIds = null,
        IReadOnlyList<AbilityAdditionalCostSelection>? additionalCostSelections = null) =>
        new(
            new CardInstanceId("source:" + id),
            new CardDefinitionId("definition:" + id),
            "ability:" + id,
            timing,
            activation,
            costs,
            targetCardIds ?? [],
            usage ?? new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.Unlimited, 0),
            "0000000000000000000000000000000000000000000000000000000000000000",
            "1111111111111111111111111111111111111111111111111111111111111111",
            new ZoneId("zone:" + id),
            ZoneKind.RunnerRig,
            additionalCostSelections);

    private static Observation ObservationFor(
        PlayerSide viewer,
        DecisionKind kind,
        params LegalAction[] actions) =>
        ObservationFor(viewer, kind, actions, ownerSide: viewer, decisionId: "decision:" + kind);

    private static Observation ObservationFor(
        PlayerSide viewer,
        DecisionKind kind,
        LegalAction[] actions,
        PlayerSide ownerSide,
        string? decisionId = null,
        string? typedDecisionId = null)
    {
        string actualDecisionId = decisionId ?? "decision:" + kind;
        DecisionId typedId = new(typedDecisionId ?? actualDecisionId);
        PlayerId owner = new(ownerSide == PlayerSide.Corp ? "player:corp" : "player:runner");
        return new Observation(
            viewer,
            actualDecisionId,
            actions,
            new ThrowingReadOnlyDictionary(),
            new ThrowingReadOnlyDictionary())
        {
            Decision = new ObservationDecision(
                typedId,
                1,
                owner,
                ownerSide,
                kind,
                "ignored")
        };
    }

    private static LegalAction Action(string id, ActionKind kind, ActionParameters parameters) =>
        Action(id, kind, parameters, "description is not policy input");

    private static LegalAction Action(
        string id,
        ActionKind kind,
        ActionParameters parameters,
        string description) =>
        new(new ActionId("issued:" + id), kind, description, parameters);

    private static InstallCardParameters InstallParameters(
        string id,
        int creditCost,
        CostQuote? costQuote = null) =>
        new(
            new CardInstanceId("card:" + id),
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            creditCost,
            costQuote: costQuote);

    private sealed class ThrowingReadOnlyDictionary : IReadOnlyDictionary<string, object?>
    {
        public object? this[string key] => throw new InvalidOperationException("The agent must not inspect state.");

        public IEnumerable<string> Keys => throw new InvalidOperationException("The agent must not inspect state.");

        public IEnumerable<object?> Values => throw new InvalidOperationException("The agent must not inspect state.");

        public int Count => throw new InvalidOperationException("The agent must not inspect state.");

        public bool ContainsKey(string key) => throw new InvalidOperationException("The agent must not inspect state.");

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            throw new InvalidOperationException("The agent must not inspect state.");

        public bool TryGetValue(string key, out object? value) =>
            throw new InvalidOperationException("The agent must not inspect state.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
