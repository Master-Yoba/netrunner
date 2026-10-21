using System.Text.Json;
using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class FullGameContractAgentTests
{
    [Fact]
    public async Task Slice47ActionClassesSelectIssuedIdsAcrossSerialization()
    {
        CardInstanceId card = new("card:action-source");
        (PlayerSide Side, DecisionKind Decision, ActionKind Kind, ActionParameters Parameters)[] cases =
        [
            (PlayerSide.Runner, DecisionKind.TurnAction, ActionKind.PlayCard, new PlayCardParameters(card, 2)),
            (PlayerSide.Runner, DecisionKind.TurnAction, ActionKind.StartRun, new RunServerParameters(new ServerId("server:hq"))),
            (PlayerSide.Corp, DecisionKind.TurnAction, ActionKind.AdvanceCard, new AdvanceCardParameters(card, 1)),
            (PlayerSide.Corp, DecisionKind.TurnTiming, ActionKind.ScoreAgenda, new ScoreAgendaParameters(card, 2, 4)),
            (PlayerSide.Runner, DecisionKind.TurnAction, ActionKind.RemoveTag, new RemoveTagParameters(1, 2)),
            (PlayerSide.Corp, DecisionKind.TurnAction, ActionKind.TrashTaggedResource, new TrashTaggedResourceParameters(card, 1, 2)),
            (PlayerSide.Corp, DecisionKind.TurnAction, ActionKind.PurgeVirusCounters, new PurgeVirusCountersParameters(3)),
            (PlayerSide.Runner, DecisionKind.ChooseNumber, ActionKind.ChooseNumber, new NumberChoiceParameters(2)),
            (PlayerSide.Runner, DecisionKind.ChooseCard, ActionKind.ChooseCard, new CardChoiceParameters(card)),
        ];
        foreach (var value in cases)
        {
            FullGameContractAgent agent = new(value.Side);
            LegalAction issued = Action("action-class", value.Kind, value.Parameters);
            LegalAction restored = EngineJson.Deserialize<LegalAction>(EngineJson.Serialize(issued));
            Assert.Equal(issued.Id, await agent.ChooseActionAsync(ObservationFor(value.Side, value.Decision, restored)));
        }
        FullGameContractAgent preventionAgent = new(PlayerSide.Runner);
        LegalAction one = Action("prevent-one", ActionKind.UsePrevention,
            new PreventionChoiceParameters(new InterruptId("interrupt:damage"), 1));
        LegalAction three = Action("prevent-three", ActionKind.UsePrevention,
            new PreventionChoiceParameters(new InterruptId("interrupt:damage"), 3));
        Assert.Equal(three.Id, await preventionAgent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.InterruptChoice, one, three)));
    }

    [Fact]
    public async Task HostedInstallChoiceAndExactSubroutineBreakRemainDeterministic()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction highHost = Action("host-high", ActionKind.InstallCard,
            new InstallCardParameters(new CardInstanceId("card:trojan"), InstallDestinationKind.RunnerRig,
                null, false, 2, finalizeSelection: false, hostCardId: new CardInstanceId("card:host-b")));
        LegalAction lowHost = Action("host-low", ActionKind.InstallCard,
            new InstallCardParameters(new CardInstanceId("card:trojan"), InstallDestinationKind.RunnerRig,
                null, false, 2, finalizeSelection: false, hostCardId: new CardInstanceId("card:host-a")));
        Assert.Equal(lowHost.Id, await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.InstallChoice, highHost, lowHost)));
        LegalAction highSubroutine = Action("break-a", ActionKind.BreakSubroutines,
            new RunBreakParameters(new CardInstanceId("card:trojan"), 1, subroutineId: new SubroutineId("subroutine:b")));
        LegalAction lowSubroutine = Action("break-z", ActionKind.BreakSubroutines,
            new RunBreakParameters(new CardInstanceId("card:trojan"), 1, subroutineId: new SubroutineId("subroutine:a")));
        Assert.Equal(lowSubroutine.Id, await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.RunTiming, highSubroutine, lowSubroutine)));
        Assert.Equal(lowSubroutine.Id, await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.RunTiming, lowSubroutine, highSubroutine)));
    }

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
        Assert.Equal(5, FullGameContractAgentVersion.SchemaVersion);
        Assert.Equal("full-game-contract-agent-v6", FullGameContractAgentVersion.AgentVersion);
        Assert.Equal("full-game-contract-policy-v6", FullGameContractAgentVersion.PolicyVersion);
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
    public async Task BioroidAbilitySelectionUsesTypedSubroutineOrderAndIsJsonNeutral()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction laterSubroutine = Action(
            "bioroid-later",
            ActionKind.UseBioroidAbility,
            new RunBioroidAbilityParameters(
                new CardInstanceId("ice:encountered"),
                new SubroutineId("subroutine:encounter:1"),
                "bioroid-break",
                subroutineOrdinal: 1),
            "break the first subroutine now");
        LegalAction earlierSubroutine = Action(
            "bioroid-earlier",
            ActionKind.UseBioroidAbility,
            new RunBioroidAbilityParameters(
                new CardInstanceId("ice:encountered"),
                new SubroutineId("subroutine:encounter:0"),
                "bioroid-break",
                subroutineOrdinal: 0),
            "end the run instead");
        LegalAction pass = Action(
            "bioroid-pass",
            ActionKind.PassPriority,
            new NoActionParameters(),
            "pass and do something unrelated");

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.RunTiming, laterSubroutine, pass, earlierSubroutine));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.RunTiming, earlierSubroutine, pass, laterSubroutine));
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(
                JsonSerializer.Serialize(new[] { pass, laterSubroutine, earlierSubroutine }))
            ?? throw new InvalidOperationException("The serialized bioroid payload was empty.");
        ActionId roundTrip = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.RunTiming, restored));

        Assert.Equal(earlierSubroutine.Id, selected);
        Assert.Equal(selected, reordered);
        Assert.Equal(selected, roundTrip);
        Assert.Contains(ActionKind.UseBioroidAbility, agent.Policy.RunActionOrder);
    }

    [Fact]
    public async Task BioroidAbilitySelectionCanPreferHighestTypedOrdinal()
    {
        FullGameContractAgent agent = new(new FullGameContractPolicy(
            PlayerSide.Runner,
            bioroidSubroutinePreference: BioroidSubroutinePreference.HighestOrdinalFirst));
        LegalAction lower = Action(
            "bioroid-low",
            ActionKind.UseBioroidAbility,
            new RunBioroidAbilityParameters(
                new CardInstanceId("ice:encountered"),
                new SubroutineId("opaque:target:z"),
                "bioroid-break",
                subroutineOrdinal: 0));
        LegalAction higher = Action(
            "bioroid-high",
            ActionKind.UseBioroidAbility,
            new RunBioroidAbilityParameters(
                new CardInstanceId("ice:encountered"),
                new SubroutineId("opaque:target:a"),
                "bioroid-break",
                subroutineOrdinal: 2));

        ActionId selected = await agent.ChooseActionAsync(ObservationFor(
            PlayerSide.Runner,
            DecisionKind.RunTiming,
            lower,
            higher));

        Assert.Equal(higher.Id, selected);
        Assert.Equal(
            BioroidSubroutinePreference.HighestOrdinalFirst,
            agent.Policy.BioroidSubroutinePreference);
    }

    [Fact]
    public async Task BioroidAbilityPayloadMustBeTypedAndIssuedOnlyInRunWindow()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction malformed = Action(
            "malformed-bioroid",
            ActionKind.UseBioroidAbility,
            new NoActionParameters(),
            "this description must not repair the payload");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.RunTiming, malformed)));

        LegalAction valid = Action(
            "bioroid-wrong-window",
            ActionKind.UseBioroidAbility,
            new RunBioroidAbilityParameters(
                new CardInstanceId("ice:encountered"),
                new SubroutineId("subroutine:encounter:0"),
                "bioroid-break"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, valid)));
    }

    [Theory]
    [InlineData(DecisionKind.IceSubroutineChoice)]
    [InlineData(DecisionKind.BehaviorChoice)]
    public async Task CorpScoresOnlyPublicInstalledTargetsBeforeSemanticIds(DecisionKind kind)
    {
        CardInstanceId cheapId = new("card:a-cheap");
        CardInstanceId expensiveId = new("card:z-expensive");
        var cheapDefinition = new CardDefinitionId("fixture:cheap-program");
        var expensiveDefinition = new CardDefinitionId("fixture:expensive-program");
        IRuntimeCardCatalog runtime = new RuntimeCardCatalog("fixture:target-scores",
            new CardRuntimeSpec(cheapDefinition, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1, memoryUnits: 1),
            new CardRuntimeSpec(expensiveDefinition, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 8, memoryUnits: 2));
        LegalAction Target(CardInstanceId id) => kind == DecisionKind.IceSubroutineChoice
            ? Action(id.Value, ActionKind.ResolveIceSubroutine,
                new IceSubroutineChoiceParameters("frame:score", IceSubroutineChoiceKind.TrashInstalledRunnerCard, cardId: id))
            : Action(id.Value, ActionKind.ChooseBehavior, new BehaviorChoiceParameters("retribution-target", [id]));
        LegalAction cheap = Target(cheapId);
        LegalAction expensive = Target(expensiveId);
        var publicState = new Dictionary<string, object?>
        {
            ["runnerRig"] = new[]
            {
                new Dictionary<string, object?> { ["id"] = cheapId.Value, ["definitionId"] = cheapDefinition.Value },
                new Dictionary<string, object?> { ["id"] = expensiveId.Value, ["definitionId"] = expensiveDefinition.Value },
            },
        };
        FullGameContractAgent agent = new(PlayerSide.Corp, runtime);
        Observation observation = ObservationFor(PlayerSide.Corp, kind, cheap, expensive) with { PublicState = publicState };
        Assert.Equal(expensive.Id, await agent.ChooseActionAsync(observation));
        Assert.Equal(expensive.Id, await agent.ChooseActionAsync(observation with { LegalActions = [expensive, cheap] }));
        // The wire representation can contain JsonElement instead of CLR dictionaries.
        var wire = new Dictionary<string, object?> { ["runnerRig"] = JsonSerializer.SerializeToElement(publicState["runnerRig"]) };
        Assert.Equal(expensive.Id, await agent.ChooseActionAsync(observation with { PublicState = wire }));
        // Omitted/redacted definitions cannot be recovered from a private field.
        var redacted = new Dictionary<string, object?>
        {
            ["runnerRig"] = new[]
            { new Dictionary<string, object?> { ["id"] = expensiveId.Value } }
        };
        Assert.Equal(cheap.Id, await agent.ChooseActionAsync(observation with { PublicState = redacted }));
    }

    [Theory]
    [InlineData(PlayerSide.Runner, IceSubroutineChoiceKind.JackOut, IceSubroutineChoiceKind.ContinueRun)]
    [InlineData(PlayerSide.Corp, IceSubroutineChoiceKind.TrashInstalledRunnerCard, IceSubroutineChoiceKind.EndRun)]
    public async Task Batch5IceChoicesUseTypedStablePreferences(
        PlayerSide side, IceSubroutineChoiceKind preferred, IceSubroutineChoiceKind fallback)
    {
        FullGameContractAgent agent = new(side);
        LegalAction first = Action("preferred", ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters("frame:batch5", preferred,
                cardId: preferred == IceSubroutineChoiceKind.TrashInstalledRunnerCard
                    ? new CardInstanceId("card:program") : null), "opaque label");
        LegalAction second = Action("fallback", ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters("frame:batch5", fallback), "opaque label");
        Assert.Equal(first.Id, await agent.ChooseActionAsync(ObservationFor(side, DecisionKind.IceSubroutineChoice, second, first)));
        Assert.Equal(first.Id, await agent.ChooseActionAsync(ObservationFor(side, DecisionKind.IceSubroutineChoice, first, second)));
        Assert.Equal(second.Id, await agent.ChooseActionAsync(ObservationFor(side, DecisionKind.IceSubroutineChoice, second)));
    }

    [Fact]
    public async Task IceSubroutineChoicePrefersTypedBranchesForEachActingSide()
    {
        FullGameContractAgent runnerAgent = new(PlayerSide.Runner);
        LegalAction tagFallback = Action(
            "subroutine-tag",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:subroutine",
                IceSubroutineChoiceKind.TakeTag),
            "take a tag");
        LegalAction pay = Action(
            "subroutine-pay",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:subroutine",
                IceSubroutineChoiceKind.PayCredits,
                creditCost: 4,
                hostedCredits: 2),
            "pay four credits");
        ActionId runnerSelected = await runnerAgent.ChooseActionAsync(
            ObservationFor(
                PlayerSide.Runner,
                DecisionKind.IceSubroutineChoice,
                tagFallback,
                pay));
        ActionId runnerReordered = await runnerAgent.ChooseActionAsync(
            ObservationFor(
                PlayerSide.Runner,
                DecisionKind.IceSubroutineChoice,
                pay,
                tagFallback));

        Assert.Equal(pay.Id, runnerSelected);
        Assert.Equal(runnerSelected, runnerReordered);

        FullGameContractAgent corpAgent = new(PlayerSide.Corp);
        LegalAction install = Action(
            "subroutine-install",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:subroutine",
                IceSubroutineChoiceKind.InstallCard,
                cardId: new CardInstanceId("card:ice"),
                destinationKind: InstallDestinationKind.ServerIce,
                serverId: new ServerId("server:remote"),
                creditCost: 0),
            "install an ICE");
        LegalAction trash = Action(
            "subroutine-trash",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:subroutine",
                IceSubroutineChoiceKind.TrashInstalledRunnerCard,
                cardId: new CardInstanceId("card:runner-program")),
            "trash an installed Runner card");
        LegalAction decline = Action(
            "subroutine-decline",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:subroutine",
                IceSubroutineChoiceKind.Decline),
            "decline the install");

        ActionId selected = await corpAgent.ChooseActionAsync(
            ObservationFor(
                PlayerSide.Corp,
                DecisionKind.IceSubroutineChoice,
                decline,
                install,
                trash));
        ActionId reordered = await corpAgent.ChooseActionAsync(
            ObservationFor(
                PlayerSide.Corp,
                DecisionKind.IceSubroutineChoice,
                trash,
                install,
                decline));
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(
                JsonSerializer.Serialize(new[] { decline, trash, install }))
            ?? throw new InvalidOperationException("The serialized ICE subroutine payload was empty.");
        ActionId roundTrip = await corpAgent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.IceSubroutineChoice, restored));

        Assert.Equal(trash.Id, selected);
        Assert.Equal(selected, reordered);
        Assert.Equal(selected, roundTrip);
    }

    [Fact]
    public async Task IceSubroutineChoiceRejectsBranchesOwnedByTheOtherSide()
    {
        FullGameContractAgent corpAgent = new(PlayerSide.Corp);
        LegalAction runnerPayment = Action(
            "subroutine-wrong-owner",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:wrong-owner",
                IceSubroutineChoiceKind.PayCredits,
                creditCost: 4));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await corpAgent.ChooseActionAsync(
                ObservationFor(
                    PlayerSide.Corp,
                    DecisionKind.IceSubroutineChoice,
                    runnerPayment)));
    }

    [Fact]
    public async Task IceSubroutineChoiceOrderingUsesCardAndDestinationFieldsWithinNormalBranches()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction laterCard = Action(
            "subroutine-install-z",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:install",
                IceSubroutineChoiceKind.InstallCard,
                cardId: new CardInstanceId("card:z"),
                destinationKind: InstallDestinationKind.ServerIce,
                serverId: new ServerId("server:remote"),
                creditCost: 0));
        LegalAction earlierCard = Action(
            "subroutine-install-a",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:install",
                IceSubroutineChoiceKind.InstallCard,
                cardId: new CardInstanceId("card:a"),
                destinationKind: InstallDestinationKind.ServerIce,
                serverId: new ServerId("server:remote"),
                creditCost: 0));

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.IceSubroutineChoice, laterCard, earlierCard));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.IceSubroutineChoice, earlierCard, laterCard));

        Assert.Equal(earlierCard.Id, selected);
        Assert.Equal(selected, reordered);
    }

    [Fact]
    public async Task IceSubroutineChoicePrefersExistingServersThenMinimizesWalletCost()
    {
        FullGameContractAgent corpAgent = new(PlayerSide.Corp);
        LegalAction expensive = Action(
            "subroutine-install-expensive",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:install-cost",
                IceSubroutineChoiceKind.InstallCard,
                cardId: new CardInstanceId("card:corp-install"),
                destinationKind: InstallDestinationKind.ServerIce,
                serverId: new ServerId("server:a"),
                creditCost: 4));
        LegalAction cheap = Action(
            "subroutine-install-cheap",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:install-cost",
                IceSubroutineChoiceKind.InstallCard,
                cardId: new CardInstanceId("card:corp-install"),
                destinationKind: InstallDestinationKind.ServerIce,
                serverId: new ServerId("server:z"),
                creditCost: 1));

        Assert.Equal(
            cheap.Id,
            await corpAgent.ChooseActionAsync(ObservationFor(
                PlayerSide.Corp,
                DecisionKind.IceSubroutineChoice,
                expensive,
                cheap)));

        LegalAction freeNewRemote = Action(
            "subroutine-install-new-remote",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:install-cost",
                IceSubroutineChoiceKind.InstallCard,
                cardId: new CardInstanceId("card:corp-install"),
                destinationKind: InstallDestinationKind.ServerIce,
                createNewRemote: true,
                creditCost: 0));
        Assert.Equal(
            cheap.Id,
            await corpAgent.ChooseActionAsync(ObservationFor(
                PlayerSide.Corp,
                DecisionKind.IceSubroutineChoice,
                freeNewRemote,
                cheap)));

        FullGameContractAgent runnerAgent = new(PlayerSide.Runner);
        LegalAction walletOnly = Action(
            "subroutine-wallet-only",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:pay-split",
                IceSubroutineChoiceKind.PayCredits,
                creditCost: 4));
        LegalAction hostedSplit = Action(
            "subroutine-hosted-split",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:pay-split",
                IceSubroutineChoiceKind.PayCredits,
                creditCost: 4,
                hostedCredits: 3));

        Assert.Equal(
            hostedSplit.Id,
            await runnerAgent.ChooseActionAsync(ObservationFor(
                PlayerSide.Runner,
                DecisionKind.IceSubroutineChoice,
                walletOnly,
                hostedSplit)));
    }

    [Fact]
    public async Task IceSubroutineChoiceRejectsMalformedTypedPayloads()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction wrongPayload = Action(
            "subroutine-wrong-payload",
            ActionKind.ResolveIceSubroutine,
            new NoActionParameters());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.IceSubroutineChoice, wrongPayload)));

        // Impossible destination shapes now fail before they can reach an
        // observation or policy boundary.
        Assert.Throws<ArgumentException>(() => new IceSubroutineChoiceParameters(
                "frame:invalid",
                IceSubroutineChoiceKind.InstallCard,
                cardId: new CardInstanceId("card:ice"),
                destinationKind: InstallDestinationKind.RunnerRig,
                createNewRemote: true));

        LegalAction invalidHostedFallback = Action(
            "subroutine-invalid-hosted",
            ActionKind.ResolveIceSubroutine,
            new IceSubroutineChoiceParameters(
                "frame:invalid-hosted",
                IceSubroutineChoiceKind.TakeTag),
            "fallback");
        // The valid constructor defaults hosted credits to zero. The typed
        // field is nevertheless covered by the JSON round-trip/selection
        // test above; this action checks malformed action IDs independently.
        LegalAction duplicateId = new(
            invalidHostedFallback.Id,
            ActionKind.ResolveIceSubroutine,
            "duplicate id",
            new IceSubroutineChoiceParameters(
                "frame:invalid-hosted-2",
                IceSubroutineChoiceKind.TakeTag));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(
                    PlayerSide.Corp,
                    DecisionKind.IceSubroutineChoice,
                    invalidHostedFallback,
                    duplicateId)));
    }

    [Fact]
    public async Task CorpInstallChoiceUsesTypedDestinationAndIsOrderNeutral()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        CardInstanceId requiredReplacement = new("card:corp-required");
        LegalAction laterCard = Action(
            "corp-install-z",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:corp-z"),
                InstallDestinationKind.ServerIce,
                new ServerId("server:remote"),
                createNewRemote: false,
                creditCost: 0,
                candidateReplacementCardIds: [requiredReplacement],
                requiredReplacementCardIds: [requiredReplacement],
                selectedReplacementCardIds: [requiredReplacement],
                finalizeSelection: true),
            "install this other Corp card");
        LegalAction earlierCard = Action(
            "corp-install-a",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:corp-a"),
                InstallDestinationKind.ServerRoot,
                new ServerId("server:remote"),
                createNewRemote: false,
                creditCost: 0,
                candidateReplacementCardIds: [requiredReplacement],
                requiredReplacementCardIds: [requiredReplacement],
                selectedReplacementCardIds: [requiredReplacement],
                finalizeSelection: true),
            "install the preferred Corp card");

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.InstallChoice, laterCard, earlierCard));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.InstallChoice, earlierCard, laterCard));
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(
                JsonSerializer.Serialize(new[] { laterCard, earlierCard }))
            ?? throw new InvalidOperationException("The serialized Corp install payload was empty.");
        ActionId roundTrip = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.InstallChoice, restored));

        Assert.Equal(earlierCard.Id, selected);
        Assert.Equal(selected, reordered);
        Assert.Equal(selected, roundTrip);
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
    public async Task BoundedAbilityCanCoexistWithOtherSupportedTypedActionClasses()
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
            new RunServerParameters(new ServerId("server:hq")));
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
    public async Task TriggerChoicePrefersUseAndIsNeutralToActionOrderAndJson()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction decline = Action(
            "trigger-decline-z",
            ActionKind.DeclineTrigger,
            new TriggerChoiceParameters(new TriggerId("trigger:z")),
            "decline the trigger");
        LegalAction useLater = Action(
            "trigger-use-z",
            ActionKind.UseTrigger,
            new TriggerChoiceParameters(new TriggerId("trigger:z")),
            "use the trigger");
        LegalAction useEarlier = Action(
            "trigger-use-a",
            ActionKind.UseTrigger,
            new TriggerChoiceParameters(new TriggerId("trigger:a")),
            "use the other trigger");

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TriggerChoice, decline, useLater, useEarlier));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TriggerChoice, useEarlier, decline, useLater));
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(
                JsonSerializer.Serialize(new[] { useLater, decline, useEarlier }))
            ?? throw new InvalidOperationException("The serialized trigger payload was empty.");
        ActionId roundTrip = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.TriggerChoice, restored));

        Assert.Equal(useEarlier.Id, selected);
        Assert.Equal(selected, reordered);
        Assert.Equal(selected, roundTrip);
    }

    [Fact]
    public async Task BehaviorChoicePrefersStructuredPrivateCardCostAndPreservesIssuedPayload()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        CardInstanceId first = new("card:hq-a");
        CardInstanceId second = new("card:hq-b");
        LegalAction fallback = Action(
            "alternative-fallback",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("anoetic-choice", "decline"),
            "decline and continue");
        LegalAction pay = Action(
            "alternative-pay",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters(
                "anoetic-choice",
                ["pay-and-trash-hq"],
                [first, second]),
            "pay and trash two hidden cards");

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.BehaviorChoice, fallback, pay));
        LegalAction selectedAction = Assert.Single(
            new[] { fallback, pay },
            action => action.Id == selected);
        BehaviorChoiceParameters payload = Assert.IsType<BehaviorChoiceParameters>(selectedAction.Parameters);

        Assert.Equal(pay.Id, selected);
        Assert.Equal(["pay-and-trash-hq"], payload.OptionKeys);
        Assert.Equal([first, second], payload.CardIds);
        Assert.True(payload.FinalizeSelection);

        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(
                JsonSerializer.Serialize(new[] { fallback, pay }))
            ?? throw new InvalidOperationException("The serialized behavior payload was empty.");
        Assert.Equal(selected, await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, DecisionKind.BehaviorChoice, restored)));
    }

    [Fact]
    public async Task StructuredBehaviorAlternativesUseFirstMaterializedDeclaration()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction first = Action(
            "action:100",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("manegarm-choice", "spend-clicks"),
            "spend two clicks");
        LegalAction fallback = Action(
            "action:101",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("manegarm-choice", "end-run"),
            "end the run");
        Assert.Equal(
            first.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.BehaviorChoice, fallback, first)));

        LegalAction takeTag = Action(
            "action:200",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("funhouse-choice", "take-tag"),
            "take a tag");
        LegalAction endRun = Action(
            "action:201",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters("funhouse-choice", "end-run"),
            "end the run");
        Assert.Equal(
            takeTag.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.BehaviorChoice, endRun, takeTag)));
    }

    [Fact]
    public async Task StructuredBehaviorOptionsUseNumericIssuedActionOrder()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction actionNine = new(
            new ActionId("action:9"),
            ActionKind.ChooseBehavior,
            "action nine",
            new BehaviorChoiceParameters("structured-choice", "option-nine"));
        LegalAction actionTen = new(
            new ActionId("action:10"),
            ActionKind.ChooseBehavior,
            "action ten",
            new BehaviorChoiceParameters("structured-choice", "option-ten"));

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.BehaviorChoice, actionTen, actionNine));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.BehaviorChoice, actionNine, actionTen));
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(
                JsonSerializer.Serialize(new[] { actionTen, actionNine }))
            ?? throw new InvalidOperationException("The serialized behavior payload was empty.");
        ActionId roundTrip = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.BehaviorChoice, restored));

        Assert.Equal(actionNine.Id, selected);
        Assert.Equal(selected, reordered);
        Assert.Equal(selected, roundTrip);
    }

    [Fact]
    public async Task RunContractStructuredBehaviorOptionsUseNumericIssuedActionOrder()
    {
        RunContractAgent agent = new(new RunPlayContract("server:remote", "source:test", "breaker:test"));
        LegalAction actionNine = new(
            new ActionId("action:9"),
            ActionKind.ChooseBehavior,
            "action nine",
            new BehaviorChoiceParameters("structured-choice", "option-nine"));
        LegalAction actionTen = new(
            new ActionId("action:10"),
            ActionKind.ChooseBehavior,
            "action ten",
            new BehaviorChoiceParameters("structured-choice", "option-ten"));

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.BehaviorChoice, actionTen, actionNine));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.BehaviorChoice, actionNine, actionTen));
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(
                JsonSerializer.Serialize(new[] { actionTen, actionNine }))
            ?? throw new InvalidOperationException("The serialized behavior payload was empty.");
        ActionId roundTrip = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.BehaviorChoice, restored));

        Assert.Equal(actionNine.Id, selected);
        Assert.Equal(selected, reordered);
        Assert.Equal(selected, roundTrip);
    }

    [Fact]
    public async Task TriggerAndBehaviorDuplicateOrMalformedPayloadsFailClosed()
    {
        FullGameContractAgent agent = new(PlayerSide.Corp);
        LegalAction duplicateActionId = Action(
            "duplicate",
            ActionKind.UseTrigger,
            new TriggerChoiceParameters(new TriggerId("trigger:1")));
        LegalAction sameActionId = new(
            duplicateActionId.Id,
            ActionKind.DeclineTrigger,
            "different action",
            new TriggerChoiceParameters(new TriggerId("trigger:1")));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.TriggerChoice, duplicateActionId, sameActionId)));

        LegalAction duplicateOption = Action(
            "duplicate-option",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters(
                "malformed-choice",
                ["same", "same"],
                [new CardInstanceId("card:a")]));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.BehaviorChoice, duplicateOption)));

        LegalAction duplicateCard = Action(
            "duplicate-card",
            ActionKind.ChooseBehavior,
            new BehaviorChoiceParameters(
                "malformed-choice",
                ["pay"],
                [new CardInstanceId("card:a"), new CardInstanceId("card:a")]));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, DecisionKind.BehaviorChoice, duplicateCard)));
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
    public async Task InstallChoicePrefersFinalAndProgressesFreshNonFinalObservations()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        CardInstanceId firstCandidate = new("card:candidate-a");
        CardInstanceId secondCandidate = new("card:candidate-b");
        CardInstanceId[] candidates = [firstCandidate, secondCandidate];

        LegalAction initial = Action(
            "install-initial",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:incoming"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 2,
                candidateReplacementCardIds: candidates,
                requiredReplacementCardIds: [firstCandidate],
                selectedReplacementCardIds: [],
                finalizeSelection: false));
        Assert.Equal(
            initial.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.TurnAction, initial)));

        LegalAction chooseFirst = Action(
            "install-choose-a",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:incoming"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 2,
                candidateReplacementCardIds: candidates,
                requiredReplacementCardIds: [firstCandidate],
                selectedReplacementCardIds: [firstCandidate],
                finalizeSelection: false));
        LegalAction chooseSecond = Action(
            "install-choose-b",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:incoming"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 2,
                candidateReplacementCardIds: candidates,
                requiredReplacementCardIds: [firstCandidate],
                selectedReplacementCardIds: [secondCandidate],
                finalizeSelection: false));

        ActionId firstSelection = await agent.ChooseActionAsync(
            ObservationFor(
                PlayerSide.Runner,
                DecisionKind.InstallChoice,
                chooseSecond,
                chooseFirst));
        ActionId reorderedSelection = await agent.ChooseActionAsync(
            ObservationFor(
                PlayerSide.Runner,
                DecisionKind.InstallChoice,
                chooseFirst,
                chooseSecond));
        Assert.Equal(chooseFirst.Id, firstSelection);
        Assert.Equal(firstSelection, reorderedSelection);

        LegalAction final = Action(
            "install-final",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:incoming"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 2,
                candidateReplacementCardIds: candidates,
                requiredReplacementCardIds: [firstCandidate],
                selectedReplacementCardIds: [firstCandidate],
                finalizeSelection: true));
        LegalAction laterContinuation = Action(
            "install-later",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:incoming"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 2,
                candidateReplacementCardIds: candidates,
                requiredReplacementCardIds: [firstCandidate],
                selectedReplacementCardIds: [firstCandidate, secondCandidate],
                finalizeSelection: false));

        string payload = JsonSerializer.Serialize(new[] { laterContinuation, final });
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(payload)
            ?? throw new InvalidOperationException("The serialized install-choice payload was empty.");
        Assert.Equal(
            final.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.InstallChoice, restored)));
    }

    [Fact]
    public async Task InstallChoiceRejectsMalformedContinuationPayloads()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        CardInstanceId candidate = new("card:candidate");
        LegalAction missingSelection = Action(
            "install-missing-selection",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:incoming"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 0,
                candidateReplacementCardIds: [candidate],
                requiredReplacementCardIds: [candidate],
                selectedReplacementCardIds: [],
                finalizeSelection: false));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.InstallChoice, missingSelection)));

        LegalAction incompleteFinal = Action(
            "install-incomplete-final",
            ActionKind.InstallCard,
            new InstallCardParameters(
                new CardInstanceId("card:incoming"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 0,
                candidateReplacementCardIds: [candidate],
                requiredReplacementCardIds: [candidate],
                selectedReplacementCardIds: [],
                finalizeSelection: true));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.InstallChoice, incompleteFinal)));
    }

    [Fact]
    public async Task InterruptChoicePrefersUseReplacementAndUsesStableTypedIds()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction decline = Action(
            "decline-replacement",
            ActionKind.DeclineReplacement,
            new ReplacementChoiceParameters(new InterruptId("interrupt:a")));
        LegalAction useLater = Action(
            "use-replacement-z",
            ActionKind.UseReplacement,
            new ReplacementChoiceParameters(new InterruptId("interrupt:z")));
        LegalAction useEarlier = Action(
            "use-replacement-a",
            ActionKind.UseReplacement,
            new ReplacementChoiceParameters(new InterruptId("interrupt:a")));

        ActionId selected = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.InterruptChoice, decline, useLater, useEarlier));
        ActionId reordered = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.InterruptChoice, useEarlier, decline, useLater));
        string payload = JsonSerializer.Serialize(new[] { useLater, decline, useEarlier });
        LegalAction[] restored = JsonSerializer.Deserialize<LegalAction[]>(payload)
            ?? throw new InvalidOperationException("The serialized interrupt-choice payload was empty.");
        ActionId roundTrip = await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.InterruptChoice, restored));

        Assert.Equal(useEarlier.Id, selected);
        Assert.Equal(selected, reordered);
        Assert.Equal(selected, roundTrip);
    }

    [Fact]
    public async Task MandatoryInterruptChoiceAndMalformedReplacementFailClosed()
    {
        FullGameContractAgent agent = new(PlayerSide.Runner);
        LegalAction mandatoryUse = Action(
            "mandatory-use",
            ActionKind.UseReplacement,
            new ReplacementChoiceParameters(new InterruptId("interrupt:mandatory")));
        Assert.Equal(
            mandatoryUse.Id,
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.InterruptChoice, mandatoryUse)));

        LegalAction malformed = Action(
            "malformed-use",
            ActionKind.UseReplacement,
            new NoActionParameters());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ChooseActionAsync(
                ObservationFor(PlayerSide.Runner, DecisionKind.InterruptChoice, malformed)));

        LegalAction prevention = Action(
            "prevention-only",
            ActionKind.UsePrevention,
            new PreventionChoiceParameters(new InterruptId("interrupt:prevention"), 1));
        Assert.Equal(prevention.Id, await agent.ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, DecisionKind.InterruptChoice, prevention)));
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
        LegalAction useTrigger = Action(
            "trigger-use",
            ActionKind.UseTrigger,
            new TriggerChoiceParameters(new TriggerId("trigger:1")));
        LegalAction declineTrigger = Action(
            "trigger-decline",
            ActionKind.DeclineTrigger,
            new TriggerChoiceParameters(new TriggerId("trigger:1")));
        Assert.Equal(
            useTrigger.Id,
            await agent.ChooseActionAsync(
                ObservationFor(
                    PlayerSide.Corp,
                    DecisionKind.TriggerChoice,
                    declineTrigger,
                    useTrigger)));
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
