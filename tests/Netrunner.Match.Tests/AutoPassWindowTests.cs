using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class AutoPassWindowTests
{
    [Theory]
    [InlineData(true, false, true, ActionKind.PassPriority, ActionKind.PumpBreaker, ActionKind.UseAbility)]
    [InlineData(true, true, false, ActionKind.PassPriority, ActionKind.PumpBreaker)]
    [InlineData(true, true, false, ActionKind.PassPriority, ActionKind.BreakSubroutines)]
    [InlineData(true, false, false, ActionKind.PassPriority, ActionKind.RezIce)]
    [InlineData(false, false, false, ActionKind.PassPriority, ActionKind.ScoreAgenda)]
    [InlineData(false, false, false, ActionKind.JackOut, ActionKind.ContinueRun)]
    public void OnlyWindowsWithoutARealChoicePass(bool inRun, bool encountering, bool optional, params ActionKind[] kinds)
    {
        LegalAction[] actions = [.. kinds.Select((kind, index) => Action(kind, index))];

        Assert.Equal(optional, AutoPassPolicy.OptionalWindow(actions, new PriorityWindow(inRun, encountering)));
    }

    [Fact]
    public void ARezStopsThePlayerOnlyWhenItMatters()
    {
        LegalAction[] rez = [Action(ActionKind.PassPriority, 0),
            new(new ActionId("action:rez"), ActionKind.RezCard, "Rez", new RezCardParameters(new CardInstanceId("card:7"), 1))];

        // Between the Runner's actions or the Corp's own: pass.
        Assert.True(AutoPassPolicy.OptionalWindow(rez, new PriorityWindow(false, false)));
        // The Corp's own action phase, shown as part of its turn: stop.
        Assert.False(AutoPassPolicy.OptionalWindow(rez, new PriorityWindow(false, false) { OwnActionPhase = true }));
        // The last windows before the Corp's turn, when assets are usually rezzed: stop.
        Assert.False(AutoPassPolicy.OptionalWindow(rez, new PriorityWindow(false, false) { BeforeOwnTurn = true }));
        // A run on its server, before the ICE is passed: pass; once past it: stop.
        var run = new PriorityWindow(true, false) { AttackedServerCards = new HashSet<string>(["card:7"]) };
        Assert.True(AutoPassPolicy.OptionalWindow(rez, run));
        Assert.False(AutoPassPolicy.OptionalWindow(rez, run with { PastIce = true }));
        // A run on another server: pass.
        Assert.True(AutoPassPolicy.OptionalWindow(rez, run with { PastIce = true, AttackedServerCards = new HashSet<string>() }));
        // ICE the Runner approaches: always stop (playtest, 27 September: Diviner on R&D was never offered).
        Assert.False(AutoPassPolicy.OptionalWindow(rez, run with { AttackedServerIce = new HashSet<string>(["card:7"]) }));
    }

    [Fact]
    public void TheApproachedIcesRezWindowReachesThePlayer()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Playtest, 27 September: the Runner ran R&D past an unrezzed Diviner because
        // auto-pass took the approach window's rez. Every run window with an ICE rez must stop.
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("expanded-expanded", 5119211749545799463);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-corp-ice-rez-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        int iceWindows = 0;
        foreach (ReplayAction action in replay.Actions)
        {
            PendingDecision decision = state.PendingDecision!;
            if (state.RunState is { } run && state.GetPlayer(decision.Owner).Side == PlayerSide.Corp
                && decision.LegalActions.Any(candidate => candidate.Parameters is RezCardParameters rez
                    && state.Zones.Any(zone => zone.ServerId == run.AttackedServer && zone.Kind == ZoneKind.ServerIce && zone.CardIds.Contains(rez.CardId))))
            {
                iceWindows++;
                Assert.False(AutoPassPolicy.OptionalWindow(decision.LegalActions, PriorityWindow.From(state, PlayerSide.Corp, definition.Behavior)));
            }
            ApplyResult applied = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }
        Assert.True(iceWindows > 0, "The replay has no ICE rez window.");
    }

    [Fact]
    public async Task AutoPassSkipsNoRezTheStandardCorpWouldMake()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Audit (27 September): in Standard self-play, every window where the Corp bot
        // rezzes must stop a human too, unless the card is outside the server being run
        // (its rez can wait for a later stop, such as the one before the Corp's turn).
        var missed = new List<string>();
        Task<(FullMatchDefinition, ReplayArtifact)>[] games = Enumerable.Range(0, 24).Select(index => Task.Run(async () =>
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(
                RulesFuzzRunner.OfficialContentIds[index % RulesFuzzRunner.OfficialContentIds.Count], RulesFuzzRunner.DeriveGameSeed(777, index));
            FullMatchResult result = await FullMatchRunner.RunAsync(definition, 3000,
                RatingLadder.CreateAgent("standard", PlayerSide.Corp, definition), RatingLadder.CreateAgent("standard", PlayerSide.Runner, definition));
            return (definition, result.Replay);
        })).ToArray();
        foreach ((FullMatchDefinition definition, ReplayArtifact replay) in await Task.WhenAll(games))
        {
            GameState state = replay.InitialSnapshot.ToState();
            foreach (ReplayAction action in replay.Actions)
            {
                PendingDecision decision = state.PendingDecision!;
                if (state.GetPlayer(decision.Owner).Side == PlayerSide.Corp
                    && decision.LegalActions.Single(candidate => candidate.Id == action.Submission.ActionId).Parameters is RezCardParameters rez)
                {
                    PriorityWindow window = PriorityWindow.From(state, PlayerSide.Corp, definition.Behavior);
                    bool elsewhere = window.InRun && !window.AttackedServerCards.Contains(rez.CardId.Value);
                    if (AutoPassPolicy.OptionalWindow(decision.LegalActions, window) && !elsewhere)
                        missed.Add($"{rez.CardId} at action {action.Sequence} ({state.Turn.ActiveSide} {state.Turn.Step}, run step {state.RunState?.Step})");
                }
                state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
            }
        }
        Assert.Empty(missed);
    }

    [Theory]
    [InlineData("30058", RezTiming.RunOnServer)] // AMAZE Amusements: a run on its server ends
    [InlineData("30042", RezTiming.RunOnServer)] // Manegarm Skunkworks: its server is approached
    [InlineData("30050", RezTiming.RunOnServer)] // Anoetic Void: its server is approached
    [InlineData("30045", RezTiming.Never)] // Urtica Cipher: works unrezzed
    [InlineData("30037", RezTiming.Anytime)] // Nico Campaign: when your turn begins
    [InlineData("30061", RezTiming.Anytime)] // Clearinghouse: when your turn begins
    [InlineData("30071", RezTiming.Anytime)] // Regolith Mining License
    [InlineData("30053", RezTiming.Anytime)] // Spin Doctor
    [InlineData("30066", RezTiming.Anytime)] // Malapert Data Vault: when you score
    public void RezTimingFollowsWhatTheCardDoes(string code, RezTiming expected)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(RulesFuzzRunner.OfficialContentIds[0], 1);
        Assert.Equal(expected, RezTimingClassifier.Classify(definition.Behavior, new CardDefinitionId(code)));
    }

    [Fact]
    public void ARunOnlyRezStopsOnlyDuringARunOnItsServer()
    {
        LegalAction[] rez = [Action(ActionKind.PassPriority, 0),
            new(new ActionId("action:rez"), ActionKind.RezCard, "Rez", new RezCardParameters(new CardInstanceId("card:7"), 1))];
        var runOnly = new Dictionary<string, RezTiming> { ["card:7"] = RezTiming.RunOnServer };
        var never = new Dictionary<string, RezTiming> { ["card:7"] = RezTiming.Never };
        // Not in the Corp's own turn or before it (playtest, 27 September: the AMAZE nag).
        Assert.True(AutoPassPolicy.OptionalWindow(rez, new PriorityWindow(false, false) { OwnActionPhase = true, RezTimings = runOnly }));
        Assert.True(AutoPassPolicy.OptionalWindow(rez, new PriorityWindow(false, false) { BeforeOwnTurn = true, RezTimings = runOnly }));
        // During a run on its server, once past the ICE: stop.
        var run = new PriorityWindow(true, false) { PastIce = true, AttackedServerCards = new HashSet<string>(["card:7"]), RezTimings = runOnly };
        Assert.False(AutoPassPolicy.OptionalWindow(rez, run));
        // A card that works unrezzed never stops.
        Assert.True(AutoPassPolicy.OptionalWindow(rez, run with { RezTimings = never }));
        Assert.True(AutoPassPolicy.OptionalWindow(rez, new PriorityWindow(false, false) { OwnActionPhase = true, RezTimings = never }));
    }

    [Fact]
    public void OnlyASolePassIsAlwaysPassed()
    {
        Assert.True(AutoPassPolicy.OnlyPass([Action(ActionKind.PassPriority, 0)]));
        Assert.False(AutoPassPolicy.OnlyPass([Action(ActionKind.PassPriority, 0), Action(ActionKind.UseAbility, 1)]));
    }

    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public async Task OptionalWindowsNeverReachThePlayerUnlessTheyAskToStop(PlayerSide human)
    {
        var shown = new List<(bool Optional, bool Requested)>();
        foreach (ulong seed in new ulong[] { 11, 12, 13, 14 })
        {
            FuzzGameCase fuzzCase = RulesFuzzRunner.Plan(1, seed, Content)[0];
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(fuzzCase.ContentId, fuzzCase.GameSeed);
            await using var coordinator = new MatchCoordinator(definition, new RandomLegalAgent(seed),
                new MatchCoordinatorOptions { HumanSide = human, AutoPassPriority = true })
            { AutoPassWindows = true };
            await coordinator.StartAsync();
            var agent = new RandomLegalAgent(seed + 100);
            for (int count = 0; count < 200; count++)
            {
                // Ask to stop now and then; the request holds only the next optional window.
                bool requested = count % 7 == 3;
                if (requested) coordinator.StopAtNextWindow();
                Observation observation = await coordinator.GetReadyObservationAsync(human).WaitAsync(TimeSpan.FromSeconds(20));
                if (coordinator.CurrentSnapshot is not { } snapshot || snapshot.IsTerminal) break;
                bool optional = !AutoPassPolicy.OnlyPass(observation.LegalActions)
                    && AutoPassPolicy.OptionalWindow(observation.LegalActions, PriorityWindow.From(snapshot.View));
                shown.Add((optional, requested));
                Assert.False(AutoPassPolicy.OnlyPass(observation.LegalActions));
                coordinator.StopAtNextWindow(false);
                Assert.True((await coordinator.SubmitHumanActionAsync(await agent.ChooseActionAsync(observation))).Accepted);
            }
        }

        Assert.NotEmpty(shown);
        Assert.DoesNotContain(shown, value => value.Optional && !value.Requested);
    }

    private static IReadOnlyList<string> Content => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode
        ? RulesFuzzRunner.OfficialContentIds
        : [RulesFuzzRunner.DeveloperContentId];

    private static LegalAction Action(ActionKind kind, int index) =>
        new(new ActionId($"action:{index}"), kind, kind.ToString(), new NoActionParameters());
}
