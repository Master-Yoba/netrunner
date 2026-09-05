using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class M2ProofTests
{
    // Pinned from a green engine run (M2 milestone proof, slice 4; re-pinned
    // by M2 rework U5: the breaker is never attached to the server — the
    // pumped Cleaver (card:5) breaks Palisade, then the installed Buzzsaw
    // (card:6) breaks both Whitespace subroutines, so the Whitespace loss
    // never fires and the run-scoped pool (5) is fully exhausted by the
    // pump (2) and the three breaks (1 each); the wallet paid the Overclock
    // cost (1) and the Nico trash cost (2) only). U6 keeps these pins stable:
    // the agenda access now yields the 7.2.2 mid-access window (pass only),
    // and the mandatory steal (7.2.3) resolves automatically on progression
    // with the same AgendaStolen event, so the event stream, the terminal
    // state, and the 32-decision count are unchanged (only the checkpoint
    // state hash at the agenda mid-access decision differs). Slice 4.4 moved
    // schema 8 -> 9. Slice 4.5 adds the serialized approach occurrence
    // (schema 9 -> 10), then targeted run-event play and breach provenance
    // (schema 10 -> 11), so state hashes are intentionally re-pinned. The
    // explicit 6.9.4g server-approach event is now part of the run stream.
    private const string ExpectedFinalStateHash =
        "ef71cd24f83e50a17f201af377c80dceb8edc8ccb233c7c7e344833ce9d4585a";
    private const string ExpectedEventStreamHash =
        "33af2b049fbad324ed5ff0ee130740c3d65c03f2cb1196100afbdc73e9f0e67c";

    private static M2ScenarioDocument LoadScenario()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "M2Scenarios", "representative-run.json");
        return EngineJson.Deserialize<M2ScenarioDocument>(File.ReadAllText(path));
    }

    private static RunPlayContract Contract(M2ScenarioDocument scenario)
    {
        M2ScenarioRunSpec run = scenario.Run
            ?? throw new InvalidOperationException("the curated scenario has no run contract.");
        return new RunPlayContract(run.Server, run.Source, run.Breaker!, run.MaxPumps);
    }

    private static M2ProofResult RunContractAgentProof()
    {
        M2ScenarioDocument scenario = LoadScenario();
        return M2ProofRunner.Run(scenario, new RunContractAgent(Contract(scenario)), "contract-agent");
    }

    /// <summary>Stream indexes of every trace entry of <paramref name="kind"/>, optionally filtered on the detail text.</summary>
    private static List<int> Occurrences(
        IReadOnlyList<RunTimingTraceEntry> entries,
        string kind,
        string? detailContains = null)
    {
        var indexes = new List<int>();
        for (int index = 0; index < entries.Count; index++)
        {
            RunTimingTraceEntry entry = entries[index];
            if (entry.EventKind == kind
                && (detailContains is null || entry.Detail.Contains(detailContains, StringComparison.Ordinal)))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    [Fact]
    public void BothPoliciesProduceByteIdenticalCuratedRuns()
    {
        M2ScenarioDocument scenario = LoadScenario();
        RunPlayContract contract = Contract(scenario);

        M2ProofResult scripted =
            M2ProofRunner.Run(scenario, new ScriptedHumanRunPolicy(contract), "scripted-human");
        M2ProofResult agent = M2ProofRunner.Run(scenario, new RunContractAgent(contract), "contract-agent");

        Assert.Equal(scripted.FinalStateHash, agent.FinalStateHash);
        Assert.Equal(scripted.EventStreamHash, agent.EventStreamHash);
        Assert.Equal(ExpectedFinalStateHash, scripted.FinalStateHash);
        Assert.Equal(ExpectedEventStreamHash, scripted.EventStreamHash);
        Assert.True(scripted.Checkpoints.Count > 0);
        Assert.Equal(scripted.Checkpoints.Count, agent.Checkpoints.Count);
        Assert.Equal(scripted.Trace.Entries.Count, agent.Trace.Entries.Count);
        Assert.Equal(scenario.ExpectedInitialStateHash, scripted.InitialStateHash);
        Assert.Equal(scenario.ExpectedInitialStateHash, agent.InitialStateHash);
    }

    [Fact]
    public void CuratedRunSatisfiesTheExitGateOrdering()
    {
        M2ProofResult result = RunContractAgentProof();
        IReadOnlyList<RunTimingTraceEntry> entries = result.Trace.Entries;

        // Approach/rez of Palisade (card:2, outermost), the encounter where
        // the pumped rig breaker Cleaver (card:5) breaks both of its
        // subroutines, and completion.
        int rezPalisade = Assert.Single(Occurrences(entries, EngineEventKinds.IceRezzed, "card:2"));
        List<int> encounterInitialized = Occurrences(entries, EngineEventKinds.EncounterInitialized);
        Assert.Equal(2, encounterInitialized.Count);
        int encounterPalisade = encounterInitialized[0];
        int pumped = Assert.Single(Occurrences(entries, EngineEventKinds.BreakerPumped, "card:5"));
        int brokenPalisade = Assert.Single(Occurrences(entries, EngineEventKinds.SubroutinesBroken, "card:5"));
        List<int> encounterCompleted = Occurrences(entries, EngineEventKinds.EncounterCompleted);
        Assert.Equal(2, encounterCompleted.Count);
        int firstEncounterCompleted = encounterCompleted[0];

        // Approach/rez of Whitespace (card:1), whose two subroutines are both
        // broken by the second installed breaker, Buzzsaw (card:6).
        int rezWhitespace = Assert.Single(Occurrences(entries, EngineEventKinds.IceRezzed, "card:1"));
        int encounterWhitespace = encounterInitialized[1];
        List<int> brokenWhitespace = Occurrences(entries, EngineEventKinds.SubroutinesBroken, "card:6");
        Assert.Equal(2, brokenWhitespace.Count);
        int secondEncounterCompleted = encounterCompleted[1];

        // No breaker is ever attached to a server (U5): the removed
        // 'breaker-attached' event kind must not appear anywhere in the trace.
        Assert.Empty(Occurrences(entries, "breaker-attached"));

        // Success, then the two-candidate breach access: card:3 is stolen,
        // card:4 is trashed, and the run ends as the last traced event.
        int runSuccessful = Assert.Single(Occurrences(entries, EngineEventKinds.RunSuccessful));
        int breachStarted = Assert.Single(Occurrences(entries, EngineEventKinds.BreachStarted));
        int chosenCardThree = Assert.Single(Occurrences(entries, EngineEventKinds.BreachCandidateChosen, "card:3"));
        int accessedCardThree = Assert.Single(Occurrences(entries, EngineEventKinds.CardAccessed, "card:3"));
        int agendaStolen = Assert.Single(Occurrences(entries, EngineEventKinds.AgendaStolen));
        int chosenCardFour = Assert.Single(Occurrences(entries, EngineEventKinds.BreachCandidateChosen, "card:4"));
        int accessedCardFour = Assert.Single(Occurrences(entries, EngineEventKinds.CardAccessed, "card:4"));
        int accessedCardTrashed = Assert.Single(Occurrences(entries, EngineEventKinds.AccessedCardTrashed));
        int runEnded = Assert.Single(Occurrences(entries, EngineEventKinds.RunEnded));

        Assert.True(rezPalisade < encounterPalisade, $"ice-rezzed ({rezPalisade}) must precede encounter-initialized ({encounterPalisade}).");
        Assert.True(encounterPalisade < pumped, $"encounter-initialized ({encounterPalisade}) must precede breaker-pumped ({pumped}).");
        Assert.True(pumped < brokenPalisade, $"breaker-pumped ({pumped}) must precede subroutines-broken ({brokenPalisade}).");
        Assert.True(brokenPalisade < firstEncounterCompleted, $"subroutines-broken ({brokenPalisade}) must precede encounter-completed ({firstEncounterCompleted}).");
        Assert.True(firstEncounterCompleted < rezWhitespace, $"encounter-completed ({firstEncounterCompleted}) must precede the second ice-rezzed ({rezWhitespace}).");
        Assert.True(rezWhitespace < encounterWhitespace, $"second ice-rezzed ({rezWhitespace}) must precede the second encounter-initialized ({encounterWhitespace}).");
        Assert.True(encounterWhitespace < brokenWhitespace[0], $"second encounter-initialized ({encounterWhitespace}) must precede the first Whitespace subroutines-broken ({brokenWhitespace[0]}). ");
        Assert.True(brokenWhitespace[0] < brokenWhitespace[1], $"the first Whitespace subroutines-broken ({brokenWhitespace[0]}) must precede the second ({brokenWhitespace[1]}).");
        Assert.True(brokenWhitespace[1] < secondEncounterCompleted, $"the second Whitespace subroutines-broken ({brokenWhitespace[1]}) must precede its encounter-completed ({secondEncounterCompleted}).");
        Assert.True(secondEncounterCompleted < runSuccessful, $"encounter-completed ({secondEncounterCompleted}) must precede run-successful ({runSuccessful}).");
        Assert.True(runSuccessful < breachStarted, $"run-successful ({runSuccessful}) must precede breach-started ({breachStarted}).");
        Assert.True(breachStarted < chosenCardThree, $"breach-started ({breachStarted}) must precede the first breach-candidate-chosen ({chosenCardThree}).");
        Assert.True(chosenCardThree < accessedCardThree, $"breach-candidate-chosen card:3 ({chosenCardThree}) must precede its card-accessed ({accessedCardThree}).");
        Assert.True(accessedCardThree < agendaStolen, $"card-accessed card:3 ({accessedCardThree}) must precede agenda-stolen ({agendaStolen}).");
        Assert.True(agendaStolen < chosenCardFour, $"agenda-stolen ({agendaStolen}) must precede the second breach-candidate-chosen ({chosenCardFour}).");
        Assert.True(chosenCardFour < accessedCardFour, $"breach-candidate-chosen card:4 ({chosenCardFour}) must precede its card-accessed ({accessedCardFour}).");
        Assert.True(accessedCardFour < accessedCardTrashed, $"card-accessed card:4 ({accessedCardFour}) must precede accessed-card-trashed ({accessedCardTrashed}).");
        Assert.True(accessedCardTrashed < runEnded, $"accessed-card-trashed ({accessedCardTrashed}) must precede run-ended ({runEnded}).");
        Assert.Equal(entries.Count - 1, runEnded);

        // CR 26.03 1.10.3c/1.10.4 run-scoped credit ledger: the pool starts
        // at 5 with the run event's hosted credits (no ledger event is emitted
        // at pool establishment). Both policies choose the largest legal
        // hosted portion for each materialized payment, draining the pool by
        // the Cleaver pump (2), the Palisade break (1), and the two Whitespace
        // breaks (1 each); it never goes negative and expires at run end.
        var creditLedger = result.FrozenEvents
            .Where(@event => @event.Kind == EngineEventKinds.RunCreditsAdjusted)
            .Select(@event => ((RunCreditsAdjustedEventPayload)@event.Payload).RunCreditsAfter)
            .ToArray();
        Assert.Equal([3, 2, 1, 0], creditLedger);

        GameState finalState = result.FinalState;
        Assert.Null(finalState.RunState);
        Assert.Null(finalState.PendingDecision);
        PlayerState runner = finalState.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = finalState.Players.Single(player => player.Side == PlayerSide.Corp);
        Assert.Equal(2, runner.AgendaPoints);
        Assert.Equal(5, corp.Credits);
        // CR 26.03 1.10.3b/1.10.3c ledger: the wallet paid the Overclock cost
        // (1) and the Nico trash cost (2) only — both Whitespace subroutines
        // were broken, so its printed credit loss never fired, and the
        // hosted pool paid the pump (2) and the three breaks (3) and expired
        // empty. 15 - 1 - 2 = 12.
        Assert.Equal(12, runner.Credits);
    }

    [Fact]
    public void SaveLoadSucceedsAtEveryYieldedDecision()
    {
        M2ProofResult result = RunContractAgentProof();
        M2ScenarioDocument scenario = LoadScenario();
        BehaviorCatalog behaviorCatalog = scenario.CreateBehaviorCatalog(GatewayBehaviorCatalog.Create());

        Assert.Equal(result.ReplayActions.Count, result.Checkpoints.Count);
        Assert.Equal(
            result.Checkpoints.Count,
            result.Checkpoints.Select(checkpoint => checkpoint.DecisionId).Distinct().Count());
        Assert.True(ReplayVerifier.Verify(result.ReplayArtifact, behaviorCatalog).Success);
    }

    [Fact]
    public async Task ProofFailsWhenAPolicySelectsAnUnofferedAction()
    {
        M2ScenarioDocument scenario = LoadScenario();

        M2ProofException exception = await Assert.ThrowsAsync<M2ProofException>(
            async () => M2ProofRunner.Run(scenario, new UnofferedActionAgent(), "unoffered-action"));
        Assert.Contains("no-such-action", exception.Message);
    }

    private sealed class UnofferedActionAgent : IGameAgent
    {
        public ValueTask<ActionId> ChooseActionAsync(
            Observation observation,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new ActionId("no-such-action"));
    }
}
