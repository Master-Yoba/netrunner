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

    [Fact]
    public async Task AShortlistOffersOnlyTheHeuristicsBestOptions()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        var decider = new LastOptionDecider();
        LlmOpponentAgent? runner = null;
        var game = new LadderGame("standard-vs-llm-test", "gateway-quick-and-dirty~gateway-party-hard", 7UL, "standard", "llm");
        LadderGameResult result = await RatingLadder.PlayAsync(game, RatingLadder.DefaultActionCap, _ => { },
            (entrant, side, definition) => entrant == "llm"
                ? runner = LlmOpponentAgent.Create(side, definition, decider, shortlist: 3)
                : RatingLadder.CreateAgent(entrant, side, definition));

        Assert.Null(result.Error);
        Assert.NotNull(runner);
        Assert.NotEmpty(decider.Offered);
        Assert.All(decider.Offered, options => Assert.InRange(options, 2, 4));
        // The decisions record positions among every legal option, as without a shortlist.
        Assert.Contains(runner.Decisions, decision => decision.Options > 4);
        Assert.All(runner.Decisions, decision => Assert.InRange(decision.Chosen, 1, decision.Options));
        Assert.Contains(runner.Decisions, decision => decision.Chosen != decision.Advised);
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmOpponentAgent.Create(Netrunner.Core.PlayerSide.Runner,
            RulesFuzzRunner.CreateDefinition("gateway-quick-and-dirty~gateway-party-hard", 7UL), decider, shortlist: 1));
    }

    [Fact]
    public async Task ToolArgumentsThatArentAnObjectGetAToolErrorAndTheDecisionGoesOn()
    {
        var model = new ToolCallingModel("[]", "\"Hedge Fund\"", "42", "{not json", "null");
        StrategicVerdict verdict = await new ModelDecider(model, new LlmSampling(), settings: new ModelDeciderSettings(MaxToolCalls: 5))
            .DecideAsync(Netrunner.Core.PlayerSide.Corp, "Options:\n1. Click for a credit\n", 1, CancellationToken.None);

        Assert.Equal(1, verdict.Option);
        Assert.Null(verdict.Failure);
        Assert.Equal(5, verdict.ToolCalls);
        Assert.Equal(
            ["The arguments must be a JSON object.", "The arguments must be a JSON object.", "The arguments must be a JSON object.",
                "The arguments weren't valid JSON.", "There is no tool called 'look'."],
            model.ToolReplies);
    }

    /// <summary>Always picks the last option offered, and keeps how many there were.</summary>
    private sealed class LastOptionDecider : IStrategicDecider
    {
        public List<int> Offered { get; } = [];

        public Task<StrategicVerdict> DecideAsync(Netrunner.Core.PlayerSide side, string request, int options, CancellationToken cancellationToken)
        {
            Offered.Add(options);
            return Task.FromResult(new StrategicVerdict(options, "last", null));
        }
    }

    /// <summary>Calls the tool "look" once with each given argument text, then answers option 1.</summary>
    private sealed class ToolCallingModel(params string[] arguments) : ILlmChatClient
    {
        private int _calls;

        public List<string> ToolReplies { get; } = [];

        public Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Messages[^1].Role == "tool") ToolReplies.Add(request.Messages[^1].Content!);
            LlmChatMessage message = _calls < arguments.Length
                ? new LlmChatMessage("assistant", null, [new LlmToolCall($"call-{_calls}", "look", arguments[_calls++])])
                : new LlmChatMessage("assistant", "{\"option\": 1, \"reason\": \"done looking\"}");
            return Task.FromResult(new LlmChatResponse(message, "stop", new LlmUsage(10, 5)));
        }
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
