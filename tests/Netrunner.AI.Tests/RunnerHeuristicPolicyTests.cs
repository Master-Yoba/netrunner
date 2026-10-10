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
    }
}
