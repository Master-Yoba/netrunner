using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

public sealed class ToyGoalAgentTests
{
    [Fact]
    public async Task CorpScoreUsesTypedPriorityAndCardIdOrdinalTieBreaking()
    {
        CardInstanceId lowCard = new("card:10");
        CardInstanceId highCard = new("card:20");
        LegalAction[] actions =
        [
            CardAction(ActionKind.InstallCard, highCard, "install high"),
            CardAction(ActionKind.ScoreAgenda, highCard, "score high"),
            NoTargetAction(ActionKind.GainCredit, "gain"),
            CardAction(ActionKind.ScoreAgenda, lowCard, "score low"),
            CardAction(ActionKind.AdvanceAgenda, lowCard, "advance"),
            NoTargetAction(ActionKind.EndTurn, "end"),
        ];

        ActionId selected = await new ToyGoalAgent(ToyGoal.CorpScore).ChooseActionAsync(
            ObservationFor(PlayerSide.Corp, actions));

        Assert.Equal(actions[3].Id, selected);
    }

    [Fact]
    public async Task CorpScoreFallsBackThroughAdvanceInstallGainAndEndTurn()
    {
        CardInstanceId target = new("card:1");
        Assert.Equal(
            ActionKind.AdvanceAgenda,
            await SelectedKind(
                ToyGoal.CorpScore,
                PlayerSide.Corp,
                CardAction(ActionKind.AdvanceAgenda, target, "advance"),
                CardAction(ActionKind.InstallCard, target, "install"),
                NoTargetAction(ActionKind.GainCredit, "gain"),
                NoTargetAction(ActionKind.EndTurn, "end")));

        Assert.Equal(
            ActionKind.InstallCard,
            await SelectedKind(
                ToyGoal.CorpScore,
                PlayerSide.Corp,
                CardAction(ActionKind.InstallCard, target, "install"),
                NoTargetAction(ActionKind.GainCredit, "gain"),
                NoTargetAction(ActionKind.EndTurn, "end")));

        Assert.Equal(
            ActionKind.GainCredit,
            await SelectedKind(
                ToyGoal.CorpScore,
                PlayerSide.Corp,
                NoTargetAction(ActionKind.GainCredit, "gain"),
                NoTargetAction(ActionKind.EndTurn, "end")));

        Assert.Equal(
            ActionKind.EndTurn,
            await SelectedKind(
                ToyGoal.CorpScore,
                PlayerSide.Corp,
                NoTargetAction(ActionKind.EndTurn, "end")));
    }

    [Fact]
    public async Task CorpViewerUsesOnlyEndTurnForRunnerStealAndDeckOut()
    {
        CardInstanceId target = new("card:1");
        LegalAction install = CardAction(ActionKind.InstallCard, target, "install");
        LegalAction gain = NoTargetAction(ActionKind.GainCredit, "gain");
        LegalAction end = NoTargetAction(ActionKind.EndTurn, "end");

        Assert.Equal(
            install.Id,
            await new ToyGoalAgent(ToyGoal.RunnerSteal).ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, install, gain, end)));
        Assert.Equal(
            end.Id,
            await new ToyGoalAgent(ToyGoal.DeckOut).ChooseActionAsync(
                ObservationFor(PlayerSide.Corp, install, gain, end)));
    }

    [Fact]
    public async Task RunnerStealPrefersStealThenEndTurnAndSortsTargets()
    {
        CardInstanceId lowCard = new("card:2");
        CardInstanceId highCard = new("card:9");
        LegalAction[] actions =
        [
            CardAction(ActionKind.StealAgenda, highCard, "high steal"),
            NoTargetAction(ActionKind.EndTurn, "end"),
            CardAction(ActionKind.StealAgenda, lowCard, "low steal"),
            NoTargetAction(ActionKind.DrawCard, "draw"),
        ];

        ActionId selected = await new ToyGoalAgent(ToyGoal.RunnerSteal).ChooseActionAsync(
            ObservationFor(PlayerSide.Runner, actions));

        Assert.Equal(actions[2].Id, selected);
        Assert.Equal(
            actions[1].Id,
            await new ToyGoalAgent(ToyGoal.RunnerSteal).ChooseActionAsync(
                ObservationFor(
                    PlayerSide.Runner,
                    NoTargetAction(ActionKind.DrawCard, "draw"),
                    actions[1])));
    }

    [Fact]
    public async Task DeckOutAlwaysEndsTurnForEitherViewer()
    {
        LegalAction end = NoTargetAction(ActionKind.EndTurn, "end");
        LegalAction gain = NoTargetAction(ActionKind.GainCredit, "gain");
        CardInstanceId target = new("card:1");
        LegalAction steal = CardAction(ActionKind.StealAgenda, target, "steal");

        foreach (PlayerSide side in Enum.GetValues<PlayerSide>())
        {
            ActionId selected = await new ToyGoalAgent(ToyGoal.DeckOut).ChooseActionAsync(
                ObservationFor(side, gain, steal, end));
            Assert.Equal(end.Id, selected);
        }
    }

    [Fact]
    public async Task AgentReturnsOnlyAnOfferedActionIdAndIgnoresDescriptionsAndStatePayloads()
    {
        CardInstanceId target = new("card:3");
        LegalAction first = CardAction(ActionKind.InstallCard, target, "description one");
        LegalAction second = NoTargetAction(ActionKind.EndTurn, "description two");
        Observation firstObservation = ObservationFor(PlayerSide.Corp, first, second);
        Observation secondObservation = new(
            PlayerSide.Corp,
            "a different decision identifier that must not matter",
            [
                new LegalAction(
                    first.Id,
                    ActionKind.InstallCard,
                    "a completely different description",
                    new CardChoiceParameters(target)),
                new LegalAction(
                    second.Id,
                    ActionKind.EndTurn,
                    "another description",
                    new NoActionParameters()),
            ],
            new ThrowingReadOnlyDictionary(),
            new ThrowingReadOnlyDictionary());

        ActionId firstSelected = await new ToyGoalAgent(ToyGoal.CorpScore).ChooseActionAsync(firstObservation);
        ActionId secondSelected = await new ToyGoalAgent(ToyGoal.CorpScore).ChooseActionAsync(secondObservation);

        Assert.Equal(firstSelected, secondSelected);
        Assert.Contains(
            secondSelected,
            secondObservation.LegalActions.Select(action => action.Id));
    }

    [Fact]
    public async Task UnsupportedOrEmptyDecisionsFailLoudlyWithoutDrawFallback()
    {
        Observation empty = ObservationFor(PlayerSide.Corp);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new ToyGoalAgent(ToyGoal.CorpScore).ChooseActionAsync(empty));

        Observation drawOnly = ObservationFor(
            PlayerSide.Runner,
            NoTargetAction(ActionKind.DrawCard, "draw"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new ToyGoalAgent(ToyGoal.DeckOut).ChooseActionAsync(drawOnly));
    }

    [Fact]
    public async Task CancellationIsHonoredBeforeAndAfterSelection()
    {
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        Observation observation = ObservationFor(
            PlayerSide.Corp,
            NoTargetAction(ActionKind.EndTurn, "end"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new ToyGoalAgent(ToyGoal.CorpScore).ChooseActionAsync(observation, canceled.Token));
    }

    private static async Task<ActionKind> SelectedKind(
        ToyGoal goal,
        PlayerSide viewer,
        params LegalAction[] actions)
    {
        ActionId selected = await new ToyGoalAgent(goal).ChooseActionAsync(ObservationFor(viewer, actions));
        return actions.Single(action => action.Id == selected).Kind;
    }

    private static Observation ObservationFor(PlayerSide viewer, params LegalAction[] actions) =>
        new(
            viewer,
            "decision:ignored",
            actions,
            new Dictionary<string, object?>
            {
                ["unrelated"] = new object(),
                ["description"] = new ThrowingObject(),
            },
            new Dictionary<string, object?>
            {
                ["private"] = new object(),
            });

    private static LegalAction NoTargetAction(ActionKind kind, string description) =>
        new(
            new ActionId($"action:{kind.ToString().ToLowerInvariant()}:{SafeIdPart(description)}"),
            kind,
            description,
            new NoActionParameters());

    private static LegalAction CardAction(ActionKind kind, CardInstanceId cardId, string description) =>
        new(
            new ActionId($"action:{kind.ToString().ToLowerInvariant()}:{cardId.Value}:{SafeIdPart(description)}"),
            kind,
            description,
            new CardChoiceParameters(cardId));

    private static string SafeIdPart(string value) =>
        value.Replace(' ', '-').Replace(':', '_');

    private sealed class ThrowingObject
    {
        public override string ToString() => throw new InvalidOperationException("This value must not be inspected.");
    }

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
