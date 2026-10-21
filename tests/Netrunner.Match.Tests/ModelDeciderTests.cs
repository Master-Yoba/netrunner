using System.Net;
using System.Text.Json.Nodes;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match.Llm;
using Netrunner.Match.Review;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class ModelDeciderTests
{
    private static readonly string RulesRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "vendor", "rules");

    private const string Request = "Board.\nCards in view:\nSample Ice: [subroutine] Trace 3. If successful, give the Runner 1 tag.\n"
        + "Your options now:\n1. Gain 1 credit\n2. Run HQ\nThe heuristic would choose option 1.\n";

    [Fact]
    public async Task TheClientSendsTheThinkingBudgetAndTheAnswerSchema()
    {
        var handler = new RecordingHandler("""{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"{}"}}]}""");
        using var client = new OpenAiCompatibleChatClient(
            new OpenAiCompatibleOptions(new Uri("http://model.test/v1"), "qwen", null, TimeSpan.FromSeconds(5)), new HttpClient(handler));
        var schema = new JsonObject { ["type"] = "object" };
        await client.CompleteAsync(new LlmChatRequest([LlmChatMessage.User("hi")], [], new LlmSampling(ThinkingBudget: 600), schema));

        JsonObject body = JsonNode.Parse(handler.Bodies.Single())!.AsObject();
        Assert.Equal(600, (int)body["thinking_budget_tokens"]!);
        Assert.Equal("json_schema", (string?)body["response_format"]!["type"]);
        Assert.Equal("object", (string?)body["response_format"]!["json_schema"]!["schema"]!["type"]);
        Assert.Null(body["tools"]);
    }

    [Fact]
    public async Task AStructuredAnswerCanLookUpARuleOnceThenMustChoose()
    {
        var model = new ScriptedModel("{\"rules_lookup\": \"trace\"}", "{\"option\": 2, \"reason\": \"the run is worth it\"}");
        var decider = new ModelDecider(model, new LlmSampling(), RulesIndex.Load(RulesRoot),
            new ModelDeciderSettings(MaxToolCalls: 1, CardText: false, StructuredAnswer: true));

        StrategicVerdict verdict = await decider.DecideAsync(PlayerSide.Runner, Request, 2, CancellationToken.None);

        Assert.Equal(2, verdict.Option);
        Assert.Equal(1, verdict.ToolCalls);
        Assert.All(model.Requests, request => Assert.Empty(request.Tools));
        // The first answer may be a lookup; once it is spent, only an option between 1 and 2 fits the schema.
        Assert.NotNull(model.Requests[0].AnswerSchema!["anyOf"]);
        Assert.Equal(2, (int)model.Requests[1].AnswerSchema!["properties"]!["option"]!["maximum"]!);
        Assert.StartsWith("rules_lookup: ", model.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        string system = model.Requests[0].Messages[0].Content!;
        Assert.Contains("{\"rules_lookup\": \"...\"}", system, StringComparison.Ordinal);
        Assert.DoesNotContain("card_text", system, StringComparison.Ordinal);
        Assert.Contains(LlmRulesPrimer.Text, system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRulesForMechanicsInViewComeWithTheRequest()
    {
        var model = new ScriptedModel("{\"option\": 1, \"reason\": \"economy\"}");
        await new ModelDecider(model, new LlmSampling(), RulesIndex.Load(RulesRoot), new ModelDeciderSettings(MaxToolCalls: 0))
            .DecideAsync(PlayerSide.Runner, Request, 2, CancellationToken.None);

        string user = model.Requests[0].Messages[1].Content!;
        Assert.Contains("Rules for mechanics in view:", user, StringComparison.Ordinal);
        Assert.Contains("10.8.2 ", user, StringComparison.Ordinal);
        Assert.DoesNotContain("10.12.1 ", user, StringComparison.Ordinal);
        Assert.Equal("", LlmRulesPrimer.RulesFor("1. Gain 1 credit\n", RulesIndex.Load(RulesRoot)));
    }

    [Fact]
    public async Task ADecisionPastItsDeadlineFailsSoTheHeuristicsChoiceStands()
    {
        var decider = new ModelDecider(new SilentModel(), new LlmSampling(), null,
            new ModelDeciderSettings(MaxToolCalls: 0, Deadline: TimeSpan.FromMilliseconds(50)));

        StrategicVerdict verdict = await decider.DecideAsync(PlayerSide.Corp, Request, 2, CancellationToken.None);

        Assert.Null(verdict.Option);
        Assert.Equal("No answer within 0 s.", verdict.Failure);
    }

    [Fact]
    public async Task TheCallersCancellationIsNotADeadline()
    {
        var decider = new ModelDecider(new SilentModel(), new LlmSampling(), null,
            new ModelDeciderSettings(MaxToolCalls: 0, Deadline: TimeSpan.FromMinutes(5)));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decider.DecideAsync(PlayerSide.Corp, Request, 2, cancel.Token));
    }

    /// <summary>Answers with the given contents in turn and keeps every request.</summary>
    private sealed class ScriptedModel(params string[] answers) : ILlmChatClient
    {
        public List<LlmChatRequest> Requests { get; } = [];

        public Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request with { Messages = [.. request.Messages] });
            return Task.FromResult(new LlmChatResponse(new LlmChatMessage("assistant", answers[Requests.Count - 1]), "stop", null));
        }
    }

    /// <summary>Never answers until cancelled, like a model still thinking.</summary>
    private sealed class SilentModel : ILlmChatClient
    {
        public async Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class RecordingHandler(string reply) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }
}
