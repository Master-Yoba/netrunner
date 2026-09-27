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
