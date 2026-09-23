using System.Collections.Immutable;
using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class RunnerEncounterPlannerTests
{
    [Fact]
    public async Task DoesNotPumpWhenTheSubsequentRequiredBreakCannotBePaid()
    {
        var fixture = new PlannerFixture(credits: 3, iceStrength: 5, [new EndRunSubroutineSpec()]);
        fixture.Breaker("decoder", strength: 1, pumpCost: 1, pumpStrength: 1, breakCost: 1);
        fixture.Action("pump", ActionKind.PumpBreaker, new RunBreakerParameters(new("card:decoder")));
        Assert.Equal("action:pass", await fixture.Choose());
    }

    [Fact]
    public async Task CounterReductionIsChosenOnlyWhenItEnablesTheRemainingBreaks()
    {
        var fixture = new PlannerFixture(credits: 2, iceStrength: 5, [new EndRunSubroutineSpec(), new EndRunSubroutineSpec()]);
        fixture.Breaker("decoder", strength: 3, pumpCost: 3, pumpStrength: 2, breakCost: 1);
        CardDefinitionId virus = new("test:reducer");
        var behavior = new CardBehaviorDefinition(virus, PlayerSide.Runner, paidAbilities:
        [new("reduce", BehaviorAbilityTiming.EncounterPaidWindow, BehaviorAbilityActivation.PaidAbility,
            [new(BehaviorCostKind.Counter, 1, counterName: "virus")], [], [],
            [new(BehaviorInstructionKind.ModifyEncounteredIceStrength, -1, argument: "test-strength")], BehaviorAbilityUsageLimit.Unlimited)]);
        fixture.Card("reducer", new(new(virus, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1), behavior, null, null, null, null, null, [], true),
            [new("virus", CardCounterPurpose.Virus, 2, null)]);
        fixture.Action("reduce", ActionKind.UseAbility, new AbilityActionParameters(new("card:reducer"), virus, "reduce", BehaviorAbilityTiming.EncounterPaidWindow,
            BehaviorAbilityActivation.PaidAbility, [new(BehaviorCostKind.Counter, 1, counterName: "virus")], [],
            new(BehaviorAbilityUsageLimit.Unlimited, 0), new string('0', 64), new string('0', 64), new("zone:rig"), ZoneKind.RunnerRig));
        Assert.Equal("action:reduce", await fixture.Choose());
        fixture.Counters("reducer", 1);
        Assert.Equal("action:pass", await fixture.Choose());
    }

    [Fact]
    public async Task VirusCountersOnOtherCardsCannotFinanceOrdinaryBreaks()
    {
        var fixture = new PlannerFixture(credits: 0, iceStrength: 4, [new EndRunSubroutineSpec()]);
        fixture.Breaker("decoder", strength: 3, pumpCost: 0, pumpStrength: 1, breakCost: 1);
        fixture.Action("pump", ActionKind.PumpBreaker, new RunBreakerParameters(new("card:decoder")));
        CardDefinitionId virus = new("test:savings");
        fixture.Card("savings", new(new(virus, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1), null, null, null, null, null, null, [], true),
            [new("virus", CardCounterPurpose.Virus, 50, null)]);
        Assert.Equal("action:pass", await fixture.Choose());
    }

    [Theory]
    [InlineData(1, "action:pass")]
    [InlineData(2, "action:break-first")]
    public async Task HostedBreakPlansCannotSpendMoreCountersThanTheirOwnSourceHas(int counters, string expected)
    {
        var fixture = new PlannerFixture(credits: 0, iceStrength: 8, [new EndRunSubroutineSpec(), new EndRunSubroutineSpec()]);
        CardDefinitionId virus = new("test:hosted");
        var behavior = new CardBehaviorDefinition(virus, PlayerSide.Runner, hosting: new HostingBehaviorSpec(breakHostSubroutine: true));
        fixture.Card("hosted", new(new(virus, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1), behavior, null, null, null, null, null, [], true),
            [new("virus", CardCounterPurpose.Virus, counters, null)], host: "card:ice");
        fixture.Action("break-first", ActionKind.BreakSubroutines, new RunBreakParameters(new("card:hosted"), 1, subroutineOrdinals: [0]));
        fixture.Action("break-second", ActionKind.BreakSubroutines, new RunBreakParameters(new("card:hosted"), 1, subroutineOrdinals: [1]));
        Assert.Equal(expected, await fixture.Choose());
    }

    [Fact]
    public async Task KeepsDisposableCoverageWhenAnEquivalentPersistentBreakerExists()
    {
        var fixture = new PlannerFixture(credits: 2, iceStrength: 2, [new EndRunSubroutineSpec()]);
        fixture.Breaker("disposable", 2, 1, 1, 1, disposable: true);
        fixture.Breaker("persistent", 2, 1, 1, 1);
        fixture.Action("break-disposable", ActionKind.BreakSubroutines, new RunBreakParameters(new("card:disposable"), 1, subroutineOrdinals: [0]));
        fixture.Action("break-persistent", ActionKind.BreakSubroutines, new RunBreakParameters(new("card:persistent"), 1, subroutineOrdinals: [0]));
        Assert.Equal("action:break-persistent", await fixture.Choose());
    }

    [Fact]
    public async Task ExpiringRunCreditsWinEvenInsideTheEasyNearTieRange()
    {
        var fixture = new PlannerFixture(credits: 2, iceStrength: 2, [new EndRunSubroutineSpec()], runCredits: 1);
        fixture.Breaker("decoder", 2, 1, 1, 1);
        fixture.Action("break-personal", ActionKind.BreakSubroutines, new RunBreakParameters(new("card:decoder"), 1, subroutineOrdinals: [0]));
        fixture.Action("break-expiring", ActionKind.BreakSubroutines, new RunBreakParameters(new("card:decoder"), 1, hostedCredits: 1, subroutineOrdinals: [0]));
        for (ulong seed = 1; seed <= 20; seed++) Assert.Equal("action:break-expiring", await fixture.Choose("easy", seed));
    }

    private sealed class PlannerFixture : ICardEvaluationCatalog
    {
        private readonly Dictionary<CardDefinitionId, CardEvaluationDescriptor> _definitions = [];
        private readonly List<AgentCard> _rig = [];
        private readonly List<AgentAction> _actions = [];
        private readonly int _credits, _strength, _runCredits;
        private readonly IceSubroutineSpec[] _subroutines;
        public PlannerFixture(int credits, int iceStrength, IceSubroutineSpec[] subroutines, int runCredits = 0)
        {
            _credits = credits; _strength = iceStrength; _subroutines = subroutines; _runCredits = runCredits;
            CardDefinitionId id = new("test:ice");
            _definitions[id] = new(new(id, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 3), null,
                new(id, iceStrength, IceType.CodeGate, 3, subroutines, []), null, null, null, null, [], false);
            Action("pass", ActionKind.PassPriority, new NoActionParameters());
        }
        public string Version => "test-planner-v1";
        public string Fingerprint => "test-planner";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => _definitions.Keys;
        public bool TryGet(CardDefinitionId id, out CardEvaluationDescriptor descriptor) => _definitions.TryGetValue(id, out descriptor!);
        public void Card(string key, CardEvaluationDescriptor definition, ImmutableArray<AgentCounter> counters = default, string? host = null)
        {
            _definitions[definition.Printed.DefinitionId] = definition;
            _rig.Add(new() { Id = "card:" + key, DefinitionId = definition.Printed.DefinitionId.Value, Counters = counters.IsDefault ? [] : counters, HostCardId = host });
        }
        public void Breaker(string key, int strength, int pumpCost, int pumpStrength, int breakCost, bool disposable = false)
        {
            CardDefinitionId id = new("test:" + key);
            Card(key, new(new(id, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 2, memoryUnits: 1), null, null,
                new(id, strength, IceType.CodeGate, breakCost, 1, pumpCost, pumpStrength, runEndCleanup: disposable ? new("test-cleanup") : null), null, null, null, [], false));
        }
        public void Counters(string key, int count)
        {
            int index = _rig.FindIndex(card => card.Id == "card:" + key);
            _rig[index] = _rig[index] with { Counters = [new("virus", CardCounterPurpose.Virus, count, null)] };
        }
        public void Action(string key, ActionKind kind, ActionParameters parameters) => _actions.Add(new(new("action:" + key), kind, parameters));
        public async Task<string> Choose(string profile = "standard", ulong seed = 54)
        {
            Observation observation = ObservationBuilder.Build(GameState.CreateNew(54), PlayerSide.Runner);
            AgentFeatures baseline = ObservationReader.Read(observation);
            AgentCard ice = new() { Id = "card:ice", DefinitionId = "test:ice", Rezzed = true };
            var decision = new ObservationDecision(new("decision:encounter"), 1, new(baseline.Self.Id), PlayerSide.Runner, DecisionKind.RunTiming, "ignored");
            AgentRun run = new()
            {
                Id = "run:test",
                AttackedServer = "server:hq",
                CurrentIce = ice,
                CurrentIceIndex = 0,
                Step = RunTimingStep.EncounterPaidWindow,
                RunCredits = _runCredits,
                Breach = new([], null, null, [], [], RunAccessState.NotStarted),
                Encounter = new()
                {
                    Id = "encounter:test",
                    Ice = ice,
                    Subroutines = _subroutines.Select((_, ordinal) => new AgentSubroutine(ordinal, false, false)).ToImmutableArray(),
                    StrengthQuote = new() { EffectiveValue = _strength },
                    BreakerStrengths = _rig.Where(card => _definitions[new(card.DefinitionId)].Icebreaker is not null)
                        .Select(card => new AgentBreakerStrength(card.Id, new() { EffectiveValue = _definitions[new(card.DefinitionId)].Icebreaker!.BaseStrength })).ToImmutableArray(),
                },
            };
            AgentBoard board = baseline.Board with
            {
                Run = run,
                RunnerRig = _rig.ToImmutableArray(),
                Servers = [new("server:hq", ServerKind.Headquarters, [ice], [])],
                Players = baseline.Board.Players with { Runner = baseline.Self with { Credits = _credits, Clicks = 0 } },
            };
            observation = observation with
            {
                DecisionId = decision.Id.Value,
                Decision = decision,
                PublicState = EngineJson.Deserialize<Dictionary<string, object?>>(EngineJson.Serialize(board)),
                LegalActions = _actions.Select(action => new LegalAction(action.Id, action.Kind, "ignore labels", action.Parameters)).ToArray(),
            };
            var agent = new OfflineAgent(RunnerPolicyProfiles.Create(profile, seed), new RunnerHeuristicPolicy(), this, captureDiagnostics: true);
            agent.Observe(observation, 0);
            string result = (await agent.ChooseActionAsync(observation)).Value;
            Assert.False(agent.LastEvaluation!.BudgetExhausted);
            return result;
        }
    }
}
