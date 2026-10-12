using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class RunnerHeuristicPolicyTests
{
    [Fact]
    public async Task BudgetFallbackIsMarkedAndMalformedIssuedActionsStillFail()
    {
        OfflineAgentConfiguration standard = RunnerPolicyProfiles.Create("standard", 54);
        var constrained = OfflineAgentConfiguration.Create(PlayerSide.Runner, RunnerHeuristicPolicy.PolicyVersion, "standard", 54,
            workBudget: 1, parameters: standard.Parameters);
        Observation observation = Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()));
        var agent = new OfflineAgent(constrained, new RunnerHeuristicPolicy(), new EmptyCatalog(), captureDiagnostics: true);
        agent.Observe(observation, 0);
        Assert.Equal(new ActionId("action:credit"), await agent.ChooseActionAsync(observation));
        Assert.True(agent.LastEvaluation!.BudgetExhausted);
        Assert.True(Assert.Single(agent.LastEvaluation.Candidates).Utility.UsedFallback);

        Observation malformed = Decision(new LegalAction(new("action:bad"), ActionKind.InstallCard, "ignored", new NoActionParameters()));
        var rejected = new OfflineAgent(constrained, new RunnerHeuristicPolicy(), new EmptyCatalog(), captureDiagnostics: true);
        rejected.Observe(malformed, 0);
        await Assert.ThrowsAsync<ArgumentException>(async () => await rejected.ChooseActionAsync(malformed));
        Assert.Equal(0, rejected.Checkpoint.CommittedDecisionCount);
    }

    [Fact]
    public void ProfilesPinSideVersionAndAllowedParameters()
    {
        OfflineAgentConfiguration standard = RunnerPolicyProfiles.Create("standard", 54);
        OfflineAgentConfiguration easy = RunnerPolicyProfiles.Create("easy", 54);
        RunnerPolicyProfiles.Validate(standard);
        RunnerPolicyProfiles.Validate(easy);
        Assert.NotEqual(standard.Digest, easy.Digest);
        Assert.True(easy.WorkBudget < standard.WorkBudget);
        RunnerPolicyProfiles.Validate(RunnerPolicyProfiles.Create("hard", 54));
        Assert.Throws<ArgumentException>(() => RunnerPolicyProfiles.Create("nightmare", 54));
        Assert.Throws<ArgumentException>(() => RunnerPolicyProfiles.Validate(OfflineAgentConfiguration.Create(PlayerSide.Corp,
            RunnerHeuristicPolicy.PolicyVersion, "standard", 54, parameters: standard.Parameters)));
        Assert.Throws<ArgumentException>(() => RunnerPolicyProfiles.Validate(OfflineAgentConfiguration.Create(PlayerSide.Runner,
            RunnerHeuristicPolicy.PolicyVersion, "standard", 54, parameters: [new("unknown", 100)])));
    }

    [Theory]
    [InlineData(9, "action:credit")]
    [InlineData(10, "action:run")]
    public async Task ConditionalEndRunUsesCreditBalanceAfterEarlierLoss(int credits, string expected)
    {
        CardDefinitionId iceId = new("test:conditional-ice");
        CardDefinitionId agendaId = new("test:winning-agenda");
        var catalog = new ScenarioCatalog(
            new(new(iceId, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 2), null,
                new(iceId, 0, IceType.CodeGate, 2,
                    [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 3), new EndRunIfCreditsAtMostSubroutineSpec(PlayerSide.Runner, 6)], []),
                null, null, null, null, [], false),
            new(new(agendaId, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3), null,
                null, null, null, null, new(agendaId, 2), [], false));
        Observation observation = Decision(
            new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()),
            new LegalAction(new("action:run"), ActionKind.StartRun, "ignored", new RunServerParameters(new("server:hq"))));
        AgentBoard baseline = ObservationReader.Read(observation).Board;
        AgentBoard board = baseline with
        {
            Servers = [new("server:hq", ServerKind.Headquarters,
                [new AgentCard { Id = "card:ice", DefinitionId = iceId.Value, Rezzed = true }],
                [new AgentCard { Id = "card:agenda", DefinitionId = agendaId.Value }])],
            Players = baseline.Players with { Runner = baseline.Players.Runner with { Credits = credits, AgendaPoints = 5 } },
        };
        observation = observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
        var agent = new OfflineAgent(RunnerPolicyProfiles.Create("standard", 54), new RunnerHeuristicPolicy(), catalog);
        agent.Observe(observation, 0);

        Assert.Equal(new ActionId(expected), await agent.ChooseActionAsync(observation));
    }

    /// <summary>
    /// Runner v28: a face-down card in a remote is worth the agenda it may be, likelier behind ICE (the Corp protects its
    /// agendas and leaves assets and ambushes naked). v27 values both at the flat 190 and runs neither for it.
    /// </summary>
    [Fact]
    public async Task V28ValuesAProtectedFaceDownRemoteCardAsTheAgendaItMayBe()
    {
        CardDefinitionId iceId = new("test:taxing-ice"), agendaId = new("test:agenda"), assetId = new("test:asset");
        var catalog = new ScenarioCatalog(
            new(new(iceId, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 1), null,
                new(iceId, 0, IceType.CodeGate, 1, [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)], []), null, null, null, null, [], false),
            new(new(agendaId, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3), null,
                null, null, null, null, new(agendaId, 2), [], false),
            new(new(assetId, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 1, trashCost: 2), null, null, null, null, null, null, [], false));
        Observation observation = Decision(
            new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()),
            new LegalAction(new("action:protected"), ActionKind.StartRun, "ignored", new RunServerParameters(new("server:protected"))),
            new LegalAction(new("action:naked"), ActionKind.StartRun, "ignored", new RunServerParameters(new("server:naked"))));
        AgentBoard baseline = ObservationReader.Read(observation).Board;
        AgentBoard board = baseline with
        {
            Servers = [
                new("server:protected", ServerKind.Remote, [new AgentCard { Id = "card:ice", DefinitionId = iceId.Value, Rezzed = true }], [null]),
                new("server:naked", ServerKind.Remote, [], [null])],
            Players = baseline.Players with { Runner = baseline.Players.Runner with { Credits = 5 } },
        };
        observation = observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
        async Task<(int Protected, int Naked)> Scores(string version)
        {
            var agent = new OfflineAgent(RunnerPolicyProfiles.Create("standard", 54, version), new RunnerHeuristicPolicy(version), catalog,
                captureDiagnostics: true);
            agent.Observe(observation, 0);
            await agent.ChooseActionAsync(observation);
            int Score(string action) => agent.LastEvaluation!.Candidates.Single(candidate => candidate.ActionId.Value == action).Utility.Score;
            return (Score("action:protected"), Score("action:naked"));
        }

        (int protectedBefore, int nakedBefore) = await Scores(RunnerHeuristicPolicy.V27);
        (int protectedAfter, int nakedAfter) = await Scores(RunnerHeuristicPolicy.V28);

        // Half the pool's root cards are agendas; behind ICE that is three in four, in a naked remote about one in eleven.
        Assert.InRange(protectedAfter - protectedBefore, 400, 600);
        Assert.InRange(nakedAfter - nakedBefore, 0, 100);
    }

    /// <summary>
    /// Runner v30, the review of #132: Phật's damage on a steal is skipped only when the Corp's smallest agenda would win;
    /// at 5 points a one-pointer (Greenmail) takes the Runner to 6 and the damage fires.
    /// </summary>
    [Theory]
    [InlineData(5, 1, false)]
    [InlineData(5, 2, true)]
    [InlineData(6, 1, true)]
    [InlineData(4, 2, false)]
    public void AStealWinsOnlyWhenTheSmallestAgendaReachesSeven(int points, int smallest, bool wins) =>
        Assert.Equal(wins, RunnerInference.StealWins(points, smallest));

    /// <summary>
    /// Runner v31, the review of #133: a known agenda whose ability tags the Runner when stolen (Tomorrow's Headline) adds
    /// that tag to the run's clean-up; an ability that fires only when the Corp scores it adds none.
    /// </summary>
    [Fact]
    public void AKnownAgendasOnStealTagCountsTowardTheRunsTags()
    {
        CardDefinitionId headline = new("test:headline"), scoredOnly = new("test:scored-only");
        CardBehaviorDefinition Tagging(CardDefinitionId id, PlayerSide? controller) => new(id, PlayerSide.Corp, triggers:
            [new BehaviorTrigger("tag", OccurrenceKind.AgendaScoredOrStolen, TriggerMode.Mandatory, BehaviorTriggerScope.Self, null,
                [new BehaviorInstruction(BehaviorInstructionKind.AddTags, 1, recipient: BehaviorPlayerSelector.Runner)],
                occurrenceControllerSide: controller)]);
        CardEvaluationDescriptor Agenda(CardDefinitionId id, PlayerSide? controller) =>
            new(new(id, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3), Tagging(id, controller),
                null, null, null, null, new(id, 2), [], false);
        var catalog = new ScenarioCatalog(Agenda(headline, null), Agenda(scoredOnly, PlayerSide.Corp));
        Observation observation = Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()));
        AgentBoard baseline = ObservationReader.Read(observation).Board;
        AgentBoard board = baseline with
        {
            Servers = [
                new("server:headline", ServerKind.Remote, [], [new AgentCard { Id = "card:headline", DefinitionId = headline.Value }]),
                new("server:scored-only", ServerKind.Remote, [], [new AgentCard { Id = "card:scored-only", DefinitionId = scoredOnly.Value }])],
        };
        observation = observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
        AgentFeatures features = ObservationReader.Read(observation);
        var context = new HeuristicContext(features, catalog, RunnerPolicyProfiles.Create("standard", 54, RunnerHeuristicPolicy.V31), 26);

        Assert.Equal(1, RunnerInference.StealTags(context, features.Board.Servers[0]));
        Assert.Equal(0, RunnerInference.StealTags(context, features.Board.Servers[1]));
    }

    /// <summary>
    /// Runner v31, the review of #133: every tag choice (Funhouse's take a tag or end the run) prices the tag with what the
    /// Corp's punishers can take for it: against a list holding a Bigger Picture-like transfer, a tag on 5 credits costs the
    /// 5 credits twice over; v30 prices it at the flat risk.
    /// </summary>
    [Fact]
    public void ATagIsPricedByTheCreditsAPunisherTransfers()
    {
        CardDefinitionId transfer = new("test:transfer");
        var choice = new BehaviorChoiceSpec("transfer-mode", BehaviorChooser.Controller, BehaviorChoiceMode.Mode, BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption("give-tag", [new BehaviorInstruction(BehaviorInstructionKind.AddTags, 1, recipient: BehaviorPlayerSelector.Runner)]),
            new BehaviorChoiceOption("remove-1",
                [new BehaviorInstruction(BehaviorInstructionKind.LoseCredits, 5, recipient: BehaviorPlayerSelector.Runner)],
                condition: BehaviorCondition.RunnerTagsAtLeast(1))]);
        var catalog = new ScenarioCatalog(new CardEvaluationDescriptor(new(transfer, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 0),
            new CardBehaviorDefinition(transfer, PlayerSide.Corp, conditions: [BehaviorCondition.Tagged(PlayerSide.Runner)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]),
            null, null, null, null, null, [], false));
        Observation observation = Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()));
        AgentBoard baseline = ObservationReader.Read(observation).Board;
        AgentBoard board = baseline with { Players = baseline.Players with { Runner = baseline.Players.Runner with { Credits = 5 } } };
        observation = observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
        AgentFeatures features = ObservationReader.Read(observation);
        AgentKnowledge knowledge = AgentKnowledge.Empty with { OpponentList = [new AgentListEntry(transfer.Value, 1)] };
        int Risk(string version, int generation) => RunnerValues.TagRisk(
            new HeuristicContext(features, catalog, RunnerPolicyProfiles.Create("standard", 54, version), generation) { Knowledge = knowledge });

        Assert.Equal(2 * 5 * 110, Risk(RunnerHeuristicPolicy.V31, 26) - Risk(RunnerHeuristicPolicy.V30, 25));
    }

    /// <summary>
    /// Runner v32 (bot plan H3 batch 4, ZW05, LE15): an advanced face-down card in a remote is worth the agenda an advanced card
    /// likely is, with the score its steal denies (R06): where the only advanceable card is a 2-point agenda (500 and 180 a
    /// point), 190 + 670 × 1.5; v30 valued it at 190 and 200 a counter.
    /// </summary>
    [Fact]
    public void AnAdvancedRemoteCardIsWorthTheAgendaItLikelyIs()
    {
        CardDefinitionId agenda = new("test:agenda"), asset = new("test:asset");
        // An asset beside it, which can't be advanced, keeps the face-down card's identity unknown.
        var catalog = new ScenarioCatalog(
            new CardEvaluationDescriptor(new(agenda, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3),
                null, null, null, null, null, new(agenda, 2), [], false),
            new CardEvaluationDescriptor(new(asset, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 1, trashCost: 2),
                null, null, null, null, null, null, [], false));
        Observation observation = Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()));
        AgentBoard baseline = ObservationReader.Read(observation).Board;
        AgentBoard board = baseline with { Servers = [new("server:remote", ServerKind.Remote, [], [null])] };
        observation = observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
        AgentFeatures read = ObservationReader.Read(observation);
        AgentFeatures features = read with
        {
            Board = read.Board with { HiddenAdvancements = [new AgentHiddenAdvancement("server:remote", "root", 0, 1)] },
        };
        int Value(string version, int generation) => RunnerValues.Server(
            new HeuristicContext(features, catalog, RunnerPolicyProfiles.Create("standard", 54, version), generation), features.Board.Servers[0]);

        Assert.Equal(190 + 200, Value(RunnerHeuristicPolicy.V30, 25));
        Assert.Equal(190 + (int)((860 - 190) * 1.5), Value(RunnerHeuristicPolicy.V32, 27));
    }

    /// <summary>
    /// Runner v32, the review of #134: what the tracker believes of the slot counts as it stands, and the prior covers only the
    /// rest: a slot half believed to be a known trap (an Urtica seen in HQ) has half the agenda chance of an untracked one.
    /// </summary>
    [Fact]
    public void AnAdvancedRemoteCardsAgendaChanceTakesTheTrackersBeliefOfTheSlot()
    {
        CardDefinitionId agenda = new("test:agenda"), trap = new("test:trap");
        var catalog = new ScenarioCatalog(
            new CardEvaluationDescriptor(new(agenda, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3),
                null, null, null, null, null, new(agenda, 2), [], false),
            new CardEvaluationDescriptor(new(trap, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 0, trashCost: 2),
                new CardBehaviorDefinition(trap, PlayerSide.Corp, canBeAdvanced: true), null, null, null, null, null, [], false));
        Observation observation = Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()));
        AgentBoard baseline = ObservationReader.Read(observation).Board;
        AgentBoard board = baseline with { Servers = [new("server:remote", ServerKind.Remote, [], [null])] };
        observation = observation with { PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)) };
        AgentFeatures features = ObservationReader.Read(observation);
        double Chance(CardTracker? tracker) => RunnerInference.AdvancedRootAgenda(
            new HeuristicContext(features, catalog, RunnerPolicyProfiles.Create("standard", 54, RunnerHeuristicPolicy.V32), 27)
            {
                Knowledge = AgentKnowledge.Empty with { Tracker = tracker },
            }, features.Board.Servers[0], 0).Chance;

        double untracked = Chance(null);
        var halfTrap = new CardTracker([new TrackedCard(trap.Value, TrackedPlace.Slot, 0.5, 1, ServerId: "server:remote", Zone: "root", Index: 0)], null);
        Assert.InRange(untracked, 0.01, 0.99);
        Assert.Equal(0.5 * untracked, Chance(halfTrap), 6);
    }

    /// <summary>
    /// Runner v33 (NE15) and the review of #135: an asset holding credits is worth only what it holds after its next payout, a
    /// campaign's at the Corp's turn begin as much as a paid withdrawal: on its last 3 nothing is denied; on 9, the 6 after it.
    /// </summary>
    [Theory]
    [InlineData(3, 60)]
    [InlineData(9, 160 + 6 * 90)]
    public void AHostedCreditAssetIsWorthWhatItHoldsAfterItsNextPayout(int hosted, int worth)
    {
        CardDefinitionId campaign = new("test:campaign");
        var catalog = new ScenarioCatalog(new CardEvaluationDescriptor(
            new(campaign, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 1, trashCost: 2),
            new CardBehaviorDefinition(campaign, PlayerSide.Corp, triggers:
                [new BehaviorTrigger("payout", OccurrenceKind.TurnBegin, TriggerMode.Mandatory, BehaviorTriggerScope.Controller, null,
                    [new BehaviorInstruction(BehaviorInstructionKind.TakeHostedCredits, 3)])]),
            null, null, null, null, null, [], false));
        AgentFeatures features = ObservationReader.Read(Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters())));
        var card = new AgentCard
        {
            Id = "card:campaign",
            DefinitionId = campaign.Value,
            Rezzed = true,
            Counters = [new AgentCounter("credit", CardCounterPurpose.HostedCredit, hosted, null)],
        };

        Assert.Equal(worth, RunnerValues.Trash(new HeuristicContext(features, catalog, RunnerPolicyProfiles.Create("standard", 54, RunnerHeuristicPolicy.V33), 28), card));
    }

    /// <summary>
    /// Corp v33, the review of #137: the layer an agenda waits for counts as affordable only with what the cards in play add
    /// to its rez (Fransofia Ward's +1): a rezzed upgrade raising ICE rez costs counts, an unrezzed one does not.
    /// </summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void AnIceRezModifierInPlayCounts(bool rezzed, int modifier)
    {
        CardDefinitionId ward = new("test:ward");
        var catalog = new ScenarioCatalog(new CardEvaluationDescriptor(
            new(ward, PlayerSide.Corp, RuntimeCardClass.Upgrade, rezCost: 1, trashCost: 2),
            new CardBehaviorDefinition(ward, PlayerSide.Corp,
                costModifiers: [new BehaviorCostModifier(BehaviorCostModifierKind.IceRezCreditCost, 1, null, "ward")]),
            null, null, null, null, null, [], false));
        Observation observation = Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters()));
        AgentFeatures read = ObservationReader.Read(observation);
        AgentFeatures features = read with
        {
            Board = read.Board with
            {
                Servers = [new("server:remote", ServerKind.Remote, [], [new AgentCard { Id = "card:ward", DefinitionId = ward.Value, Rezzed = rezzed }])],
            },
        };

        Assert.Equal(modifier, CorpHeuristicPolicy.IceRezModifier(new HeuristicContext(features, catalog,
            CorpPolicyProfiles.Create("standard", 54, CorpHeuristicPolicy.V33), 25)));
    }

    /// <summary>
    /// The review of #138: Corp v34's flood threshold is three agendas in a five-card HQ, and a larger HQ (Precision Design,
    /// Superconducting Hub) raises it by its extra size.
    /// </summary>
    [Theory]
    [InlineData(5, 3, true)]
    [InlineData(6, 3, false)]
    [InlineData(6, 4, true)]
    [InlineData(7, 4, false)]
    public void TheFloodedDrawThresholdFollowsTheHqSize(int maximumHandSize, int agendas, bool flooded)
    {
        CardDefinitionId agendaId = new("test:agenda");
        var catalog = new ScenarioCatalog(new CardEvaluationDescriptor(
            new(agendaId, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3),
            null, null, null, null, null, new(agendaId, 2), [], false));
        AgentFeatures read = ObservationReader.Read(Decision(new LegalAction(new("action:draw"), ActionKind.DrawCard, "ignored", new NoActionParameters())));
        AgentFeatures features = read with
        {
            Side = PlayerSide.Corp,
            PrivateCards = new AgentPrivateCards
            {
                Hand = [.. Enumerable.Range(0, agendas).Select(index => new AgentCard { Id = $"card:agenda-{index}", DefinitionId = agendaId.Value })],
            },
            Board = read.Board with
            {
                Players = read.Board.Players with { Corp = read.Board.Players.Corp with { MaximumHandSize = maximumHandSize } },
            },
        };

        var context = new HeuristicContext(features, catalog, CorpPolicyProfiles.Create("standard", 54, CorpHeuristicPolicy.V34), 25);
        Assert.Equal(flooded, CorpHeuristicPolicy.FloodedDraw(context) > 0);
    }

    /// <summary>
    /// Bot plan H3 batch 10: a punishing operation in the Corp identity's faction is evidence of one (Measured Response in a
    /// Weyland deck); a neutral card, or one of another faction, isn't.
    /// </summary>
    [Theory]
    [InlineData("weyland-consortium", "weyland-consortium", true)]
    [InlineData("jinteki", "weyland-consortium", false)]
    [InlineData("neutral-corp", "neutral-corp", false)]
    public void ACardOfTheCorpIdentitysFactionIsInFaction(string identityFaction, string cardFaction, bool inFaction)
    {
        CardDefinitionId identity = new("test:identity");
        CardDefinitionId operation = new("test:operation");
        var catalog = new ScenarioCatalog(
            new CardEvaluationDescriptor(new(identity, PlayerSide.Corp, RuntimeCardClass.Identity), null, null, null, null, null, null, [], false),
            new CardEvaluationDescriptor(new(operation, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 5), null, null, null, null, null, null, [], false))
        {
            Factions = { [identity.Value] = identityFaction, [operation.Value] = cardFaction },
        };
        AgentFeatures read = ObservationReader.Read(Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters())));
        AgentFeatures features = read with { Board = read.Board with { CorpIdentity = [new AgentCard { Id = "card:identity", DefinitionId = identity.Value }] } };
        var context = new HeuristicContext(features, catalog, RunnerPolicyProfiles.Create("standard", 54, RunnerHeuristicPolicy.V34), HeuristicContext.FactionGeneration);

        catalog.TryGet(operation, out CardEvaluationDescriptor card);
        Assert.Equal(inFaction, RunnerInference.InCorpFaction(context, card));
    }

    /// <summary>
    /// Bot plan H3 batch 10: three different damage-dealing Corp cards seen make a damage deck, which may splash a punishing
    /// operation (PT Untaian's Measured Response, out of faction); one or two are common in any deck.
    /// </summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void ThreeDamageCardsSeenMakeADamageDeck(int seen, bool damageDeck)
    {
        CardEvaluationDescriptor[] ice = [.. Enumerable.Range(0, 4).Select(index => new CardDefinitionId($"test:damage-ice-{index}")).Select(id =>
            new CardEvaluationDescriptor(new(id, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 2), null,
                new(id, 0, IceType.Sentry, 2, [new DamageSubroutineSpec(DamageKind.Net, 1)], []), null, null, null, null, [], false))];
        var catalog = new ScenarioCatalog(ice);
        AgentFeatures read = ObservationReader.Read(Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters())));
        AgentFeatures features = read with
        {
            Board = read.Board with
            {
                Servers = [new("server:hq", ServerKind.Headquarters,
                    [.. ice.Take(seen).Select((card, index) => new AgentCard { Id = $"card:ice-{index}", DefinitionId = card.Printed.DefinitionId.Value, Rezzed = true })], [])],
            },
        };
        var context = new HeuristicContext(features, catalog, RunnerPolicyProfiles.Create("standard", 54, RunnerHeuristicPolicy.V34), HeuristicContext.FactionGeneration);

        Assert.Equal(damageDeck, RunnerInference.DamageDeck(context));
    }

    /// <summary>
    /// The review of #139: Runner v34's grip floor counts what a played card draws and the clicks it loses, so a draw event
    /// (VRcation's four) isn't priced as a card spent, and Creative Commission's lost click isn't a click to draw with.
    /// </summary>
    [Theory]
    [InlineData(BehaviorInstructionKind.Draw, 4, true, 6, 2)]
    [InlineData(BehaviorInstructionKind.LoseClicks, 1, true, 2, 1)]
    [InlineData(BehaviorInstructionKind.Draw, 4, false, 2, 2)]
    public void TheGripFloorCountsAPlayedCardsDrawsAndLostClicks(BehaviorInstructionKind kind, int amount, bool played, int grip, int clicks)
    {
        CardDefinitionId eventId = new("test:event");
        var catalog = new ScenarioCatalog(new CardEvaluationDescriptor(new(eventId, PlayerSide.Runner, RuntimeCardClass.Event, playCost: 0),
            new CardBehaviorDefinition(eventId, PlayerSide.Runner, instructions: [new BehaviorInstruction(kind, amount: amount)]),
            null, null, null, null, null, [], false));
        AgentFeatures read = ObservationReader.Read(Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters())));
        AgentFeatures features = read with
        {
            PrivateCards = new AgentPrivateCards
            {
                Grip = [new AgentCard { Id = "card:event", DefinitionId = eventId.Value }, new AgentCard { Id = "card:a", DefinitionId = "test:a" }, new AgentCard { Id = "card:b", DefinitionId = "test:b" }],
            },
            Board = read.Board with { Players = read.Board.Players with { Runner = read.Board.Players.Runner with { Clicks = 3 } } },
        };
        var context = new HeuristicContext(features, catalog, RunnerPolicyProfiles.Create("standard", 54, RunnerHeuristicPolicy.V34), HeuristicContext.FactionGeneration);

        Assert.Equal((grip, clicks), RunnerHeuristicPolicy.AfterPlaying(context, "card:event", played));
    }

    /// <summary>
    /// Bot plan H3 batch 12 (MATCHUP_NEBULA.md's thresholds): Nanomanagement on 3 clicks and 7 credits leaves four clicks
    /// and 3 credits, enough to install and advance a three-advancement agenda; on 6 credits it isn't, and a two-advancement
    /// agenda scores on 3 clicks without it.
    /// </summary>
    [Theory]
    [InlineData(3, 7, 3, 800)]
    [InlineData(3, 6, 3, 0)]
    [InlineData(3, 7, 2, 0)]
    public void NanomanagementIsWorthTheAgendaItLetsTheCorpScore(int clicks, int credits, int requirement, int worth)
    {
        CardDefinitionId agendaId = new("test:agenda");
        CardDefinitionId nanoId = new("test:nanomanagement");
        var nano = new CardBehaviorDefinition(nanoId, PlayerSide.Corp, instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainClicks, amount: 2)]);
        var catalog = new ScenarioCatalog(
            new CardEvaluationDescriptor(new(agendaId, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: requirement),
                null, null, null, null, null, new(agendaId, 2), [], false),
            new CardEvaluationDescriptor(new(nanoId, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 4), nano, null, null, null, null, null, [], false));
        AgentFeatures read = ObservationReader.Read(Decision(new LegalAction(new("action:credit"), ActionKind.GainCredit, "ignored", new NoActionParameters())));
        AgentFeatures features = read with
        {
            Side = PlayerSide.Corp,
            PrivateCards = new AgentPrivateCards
            {
                Hand = [new AgentCard { Id = "card:agenda", DefinitionId = agendaId.Value }, new AgentCard { Id = "card:nano", DefinitionId = nanoId.Value }],
            },
            Board = read.Board with
            {
                Players = read.Board.Players with { Corp = read.Board.Players.Corp with { Clicks = clicks, Credits = credits } },
            },
        };
        var context = new HeuristicContext(features, catalog, CorpPolicyProfiles.Create("standard", 54, CorpHeuristicPolicy.V36), 25);

        Assert.Equal(worth, CorpHeuristicPolicy.FastAdvance(context, nano, 4));
    }

    private static Observation Decision(params LegalAction[] actions)
    {
        GameState state = GameState.CreateNew(54);
        var decision = new ObservationDecision(new("decision:runner-policy"), 1, state.Players.Single(player => player.Side == PlayerSide.Runner).Id,
            PlayerSide.Runner, DecisionKind.TurnAction, "ignored");
        return ObservationBuilder.Build(state, PlayerSide.Runner) with { DecisionId = decision.Id.Value, Decision = decision, LegalActions = actions };
    }

    private sealed class EmptyCatalog : ICardEvaluationCatalog
    {
        public string Version => "test-v1";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => [];
        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) { descriptor = null!; return false; }
    }

    private sealed class ScenarioCatalog(params CardEvaluationDescriptor[] descriptors) : ICardEvaluationCatalog
    {
        private readonly Dictionary<CardDefinitionId, CardEvaluationDescriptor> _definitions = descriptors
            .ToDictionary(descriptor => descriptor.Printed.DefinitionId);
        public string Version => "test-v1";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => _definitions.Keys;
        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) =>
            _definitions.TryGetValue(definitionId, out descriptor!);
        public Dictionary<string, string> Factions { get; } = [];
        public string? FactionOf(CardDefinitionId definitionId) => Factions.GetValueOrDefault(definitionId.Value);
    }
}
