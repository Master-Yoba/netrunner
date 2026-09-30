using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match.Review;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The LLM game reviewer (Wave 4.5, P2) with a scripted model: it answers the tools from the game, the rules
/// and the card data, and keeps a finding only on one of the reviewed bot's decisions, verified only when a
/// citation resolves. No test reaches a model server.
/// </summary>
public sealed class LlmGameReviewerTests
{
    private static readonly string RulesRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "vendor", "rules");

    [Fact]
    public void TheRulesIndexFindsARuleByNumberAndByWords()
    {
        RulesIndex rules = RulesIndex.Load(RulesRoot);
        Assert.True(rules.Count > 500);
        Assert.Contains("turn all cards in the Corp's discard pile faceup", rules.Rule("7.5.2")!.Text, StringComparison.Ordinal);
        Assert.Contains(rules.Search("breached server is Archives faceup"), rule => rule.Number == "7.5.2");
    }

    [Fact]
    public void AnOpenAiResponseIsRead()
    {
        LlmChatResponse response = OpenAiCompatibleChatClient.Parse("""
            {"choices":[{"finish_reason":"tool_calls","message":{"role":"assistant","content":null,
              "tool_calls":[{"id":"a1","type":"function","function":{"name":"card_text","arguments":"{\"title\":\"Palisade\"}"}}]}}],
             "usage":{"prompt_tokens":10,"completion_tokens":3}}
            """);
        LlmToolCall call = Assert.Single(response.Message.ToolCalls!);
        Assert.Equal(("a1", "card_text", "{\"title\":\"Palisade\"}"), (call.Id, call.Name, call.Arguments));
        Assert.Equal(new LlmUsage(10, 3), response.Usage);
    }

    [Fact]
    public async Task AReviewAnswersToolsAndKeepsOnlyGroundedFindings()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", "seamless-launch-nothing")));
        GameReviewDossier dossier = await GameReviewDossier.BuildAsync(saved, definition, PlayerSide.Corp);
        Assert.Contains(dossier.Decisions, decision => decision.Sequence == 222 && decision.Chosen.Contains("Seamless Launch", StringComparison.Ordinal));
        // Saved when "standard" was Corp v18; v19 no longer plays this Seamless Launch, and the dossier says so.
        Assert.Contains(dossier.Decisions, decision => decision.Sequence == 222 && decision.Diverged);
        Assert.Contains("(the bot now chooses differently)", dossier.DecisionLog(), StringComparison.Ordinal);
        Assert.Contains("== turn 1, Corp active. Corp 5 credits", dossier.DecisionLog(), StringComparison.Ordinal);

        var model = new ScriptedModel(
            new LlmChatMessage("assistant", null, [new("c1", "decision", "{\"sequence\":\"222\"}"), new("c2", "card_text", "{\"title\":\"Seamless Launch\"}")]),
            new LlmChatMessage("assistant", null, [new("c3", "position", "{\"sequence\":222}"), new("c4", "rules_lookup", "{\"query\":\"advance\"}")]),
            new LlmChatMessage("assistant", """
                ```json
                {"findings": [
                  {"sequence": 222, "category": "wasted-card", "claim": "No agenda to advance.", "citations": [{"kind": "card", "ref": "Seamless Launch"}, {"kind": "rule", "ref": "1.1.1"}]},
                  {"sequence": 223, "category": "made-up", "claim": "Cites nothing real.", "citations": [{"kind": "rule", "ref": "99.9"}]},
                  {"sequence": 99999, "category": "not-a-decision", "claim": "Past the end.", "citations": []}
                ]}
                ```
                """));
        GameReview review = await new LlmGameReviewer(model, RulesIndex.Load(RulesRoot), new LlmSampling(0.6)).ReviewAsync(dossier);

        Assert.Null(review.Failure);
        Assert.Equal(4, review.ToolCalls);
        Assert.Contains("Seamless Launch", model.ToolAnswers["c2"], StringComparison.Ordinal);
        Assert.Contains("Corp:", model.ToolAnswers["c3"], StringComparison.Ordinal);
        Assert.Contains("* chosen", model.ToolAnswers["c1"], StringComparison.Ordinal);
        ReviewFinding grounded = Assert.Single(review.Findings, finding => finding.Sequence == 222);
        Assert.True(grounded.Verified);
        Assert.DoesNotContain(review.Findings, finding => finding.Sequence == 99999);
        Assert.All(review.Findings.Where(finding => finding.Sequence == 223), finding => Assert.False(finding.Verified));
    }

    /// <summary>A model that answers with the next scripted message and records what the tools returned.</summary>
    private sealed class ScriptedModel(params LlmChatMessage[] script) : ILlmChatClient
    {
        private int _next;

        public Dictionary<string, string> ToolAnswers { get; } = [];

        public Task<LlmChatResponse> CompleteAsync(LlmChatRequest request, CancellationToken cancellationToken = default)
        {
            foreach (LlmChatMessage message in request.Messages.Where(message => message.Role == "tool"))
                ToolAnswers[message.ToolCallId!] = message.Content!;
            return Task.FromResult(new LlmChatResponse(script[Math.Min(_next++, script.Length - 1)], "stop", new LlmUsage(100, 10)));
        }
    }
}
