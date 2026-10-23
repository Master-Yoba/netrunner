using System.Text.RegularExpressions;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match.Llm;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>A save of a game against the LLM opponent keeps its bot and a mark; a load wraps the bot in the LLM again.</summary>
public sealed partial class LlmOpponentSaveTests
{
    [Fact]
    public async Task AnLlmOpponentsSaveRestoresTheLlmOpponentWhenOneCanBeMade()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(174);
        var model = new HeuristicModel();
        await using var original = new MatchCoordinator(definition, LlmPlayerOpponent.Create(definition, PlayerSide.Corp, model),
            new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
        await original.StartAsync();
        var human = new FullGameContractAgent(PlayerSide.Corp, definition.Runtime);
        for (int step = 0; step < 200 && model.Asked == 0; step++)
        {
            MatchSnapshot snapshot = await AtHuman(original);
            if (snapshot.IsTerminal) break;
            Assert.True((await original.SubmitHumanActionAsync(await human.ChooseActionAsync(snapshot.Observation))).Accepted);
        }
        Assert.True(model.Asked > 0, "The model was never asked.");
        await AtHuman(original);

        SoloSaveDocument save = EngineJson.Deserialize<SoloSaveDocument>(EngineJson.Serialize(await original.CaptureSaveAsync("Against the LLM")));
        save.Validate(definition.Behavior, definition.Runtime);

        Assert.True(save.Payload.Opponent.LlmOpponent);
        Assert.Equal("offline", save.Payload.Opponent.Kind);
        Assert.True(save.Preview.LlmOpponent);
        // With a way to make an LLM opponent, the load asks the model again; without, the bot plays on.
        int askedBefore = model.Asked;
        await using (var resumed = new MatchCoordinator(definition, save, new MatchCoordinatorOptions
        {
            HumanSide = PlayerSide.Corp,
            RestoreLlmOpponent = (bot, match) => LlmPlayerOpponent.Restore(bot, match, model),
        }))
        {
            await PlayOn(resumed, human);
        }
        Assert.True(model.Asked > askedBefore, "The restored opponent never asked the model.");
        askedBefore = model.Asked;
        await using (var heuristic = new MatchCoordinator(definition, save, new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp }))
        {
            await PlayOn(heuristic, human);
        }
        Assert.Equal(askedBefore, model.Asked);
    }

    [Fact]
    public async Task ABotsSaveHasNoLlmMark()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(174);
        await using var coordinator = new MatchCoordinator(definition, OfflineMatchFactory.CreatePlayerOpponent(definition, PlayerSide.Corp),
            new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
        await coordinator.StartAsync();
        await AtHuman(coordinator);

        SoloSaveDocument save = await coordinator.CaptureSaveAsync("Against the bot");

        Assert.False(save.Payload.Opponent.LlmOpponent);
        Assert.DoesNotContain("llmOpponent", EngineJson.Serialize(save), StringComparison.Ordinal);
    }

    /// <summary>Plays the human's side for a while from a paused, resumed match.</summary>
    private static async Task PlayOn(MatchCoordinator coordinator, FullGameContractAgent human)
    {
        await coordinator.StartAsync();
        await coordinator.ContinueAsync();
        for (int step = 0; step < 60; step++)
        {
            MatchSnapshot snapshot = await AtHuman(coordinator);
            if (snapshot.IsTerminal) return;
            Assert.True((await coordinator.SubmitHumanActionAsync(await human.ChooseActionAsync(snapshot.Observation))).Accepted);
        }
    }

    private static async Task<MatchSnapshot> AtHuman(MatchCoordinator coordinator)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            MatchSnapshot? snapshot = coordinator.CurrentSnapshot;
            if (snapshot?.Feedback?.IsError == true) throw new InvalidOperationException(snapshot.Feedback.Message);
            if (snapshot is { IsTerminal: true } or { AwaitingHumanDecision: true }) return snapshot;
            await Task.Delay(5, timeout.Token);
        }
    }

    /// <summary>Answers the bot's own option, at once, and counts the questions.</summary>
    private sealed partial class HeuristicModel : ILlmChatClient
    {
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asked);
            string option = Advised().Match(request.Messages[^1].Content ?? "").Groups[1].Value;
            return Task.FromResult(new LlmChatResponse(new LlmChatMessage("assistant", $"{{\"option\": {option}, \"reason\": \"the bot's\"}}"),
                "stop", null));
        }

        [GeneratedRegex(@"The heuristic would choose option (\d+)")]
        private static partial Regex Advised();
    }
}
