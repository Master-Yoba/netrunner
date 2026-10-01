using Netrunner.Cards;
using Netrunner.LLM;
using Netrunner.Match.Llm;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class LlmOpponentAgentTests
{
    [Theory]
    [InlineData("{\"option\": 2, \"reason\": \"why\"}", 2)]
    [InlineData("```json\n{\"option\": \"3\"}\n```", 3)]
    [InlineData("{\"option\": 9}", null)]
    [InlineData("{\"option\": 0}", null)]
    [InlineData("I pick two", null)]
    [InlineData("", null)]
    public void AnAnswerMustNameOneOfTheOptions(string answer, int? option) =>
        Assert.Equal(option, LlmOpponentAgent.Parse(answer, 4).Option);

    [Fact]
    public async Task AGameWithBadAnswersFinishesOnTheEngineSActionsOnly()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        var model = new ScriptedModel();
        LlmOpponentAgent? corp = null;
        var game = new LadderGame("llm-vs-standard-test", "gateway-quick-and-dirty~gateway-party-hard", 7UL, "llm", "standard");
        LadderSavedGame? saved = null;
        LadderGameResult result = await RatingLadder.PlayAsync(game, RatingLadder.DefaultActionCap, value => saved = value,
            (entrant, side, definition) => entrant == "llm"
                ? corp = LlmOpponentAgent.Create(side, definition, model, new LlmSampling(0.6))
                : RatingLadder.CreateAgent(entrant, side, definition));

        Assert.Null(result.Error);
        Assert.False(result.Capped);
        Assert.NotNull(saved);
        Assert.NotNull(corp);
        // Every outcome came up: the model's option, a bad answer twice, and a server failure.
        Assert.Contains(corp.Decisions, decision => decision.Outcome == "model");
        Assert.Contains(corp.Decisions, decision => decision.Outcome == "invalid");
        Assert.Contains(corp.Decisions, decision => decision.Outcome == "failed");
        Assert.All(corp.Decisions, decision => Assert.InRange(decision.Chosen, 1, decision.Options));
        Assert.Contains(corp.Decisions, decision => decision.Outcome == "model" && decision.Chosen != decision.Advised);
        // The prompt holds the Corp's own view: its hand by name, the Runner's grip only counted.
        Assert.All(model.Requests, request => Assert.Matches(@"grip \d+ cards", request));
        Assert.Contains(model.Requests, request => request.Contains("HQ [", StringComparison.Ordinal));
    }

    /// <summary>
    /// Answers in a fixed cycle: the last option, a malformed answer and then the first option, an option
    /// out of range twice (the heuristic's choice stands), and a server error (likewise).
    /// </summary>
    private sealed class ScriptedModel : ILlmChatClient
    {
        private int _calls;

        public List<string> Requests { get; } = [];

        public Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            string prompt = request.Messages.Last(message => message.Role == "user").Content!;
            if (!prompt.StartsWith("That wasn't", StringComparison.Ordinal)) Requests.Add(prompt);
            int options = request.Messages.First(message => message.Role == "user").Content!
                .Split('\n').Count(line => line.Length > 2 && char.IsAsciiDigit(line[0]) && line.Contains(". ", StringComparison.Ordinal));
            string answer = (_calls++ % 6) switch
            {
                0 => $"{{\"option\": {options}, \"reason\": \"the last one\"}}",
                1 => "no idea",
                2 => "{\"option\": 1, \"reason\": \"the first one\"}",
                3 or 4 => "{\"option\": 999}",
                _ => throw new HttpRequestException("The server is down."),
            };
            return Task.FromResult(new LlmChatResponse(new LlmChatMessage("assistant", answer), "stop", new LlmUsage(10, 5)));
        }
    }
}
