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
        Assert.Equal("1.17.3", rules.Rule("1.17.3c")?.Number);
        Assert.Null(rules.Rule("1.17.3z"));
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
        Assert.Contains("== turn 1, Corp active. Corp 5 credits, 0 points, HQ [", dossier.DecisionLog(), StringComparison.Ordinal);
        Assert.Contains("grip 5 cards", dossier.DecisionLog(), StringComparison.Ordinal);
        Assert.Contains("(Runner #", dossier.DecisionLog(), StringComparison.Ordinal);
        // A trigger choice names its source card, not just the trigger's catalog id.
        Assert.Contains(dossier.Decisions, decision => decision.Kind == "TriggerChoice");
        Assert.DoesNotContain(dossier.Decisions, decision => decision.Kind == "TriggerChoice" && decision.Chosen.StartsWith("UseTrigger (", StringComparison.Ordinal));

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

    [Fact]
    public async Task ASecondLookCanRefuteAFindingFromTheGameHistory()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", "seamless-launch-nothing")));
        GameReviewDossier dossier = await GameReviewDossier.BuildAsync(saved, definition, PlayerSide.Corp);
        string history = dossier.History(200, 230, "Seamless Launch");
        Assert.Contains("#222 turn", history, StringComparison.Ordinal);
        Assert.Contains("Corp: PlayCard Seamless Launch", history, StringComparison.Ordinal);
        Assert.Contains("No actions", dossier.History(200, 230, "No Such Card"), StringComparison.Ordinal);

        var model = new ScriptedModel(
            new LlmChatMessage("assistant", ""),
            new LlmChatMessage("assistant", """
                {"findings": [
                  {"sequence": 222, "category": "wasted-card", "confidence": "high", "claim": "No agenda to advance.", "citations": [{"kind": "card", "ref": "Seamless Launch"}]},
                  {"sequence": 223, "category": "made-up", "confidence": "low", "claim": "The Runner ran Remote 1 three times.", "citations": [{"kind": "card", "ref": "Ansel 1.0"}]}
                ]}
                """),
            new LlmChatMessage("assistant", """{"verdict": "upheld", "reason": "Nothing to advance."}"""),
            new LlmChatMessage("assistant", null, [new("h1", "history", "{\"from\":\"200\",\"to\":\"240\",\"containing\":\"Seamless\"}")]),
            new LlmChatMessage("assistant", """{"verdict": "refuted", "reason": "No such runs."}"""));
        GameReview review = await new LlmGameReviewer(model, RulesIndex.Load(RulesRoot), new LlmSampling(0.6)).ReviewAsync(dossier, check: true);

        Assert.Null(review.Failure);
        Assert.Equal(("upheld", "high"), (review.Findings[0].Check, review.Findings[0].Confidence));
        Assert.Equal(("refuted", "No such runs."), (review.Findings[1].Check, review.Findings[1].CheckReason));
        Assert.Equal(1, review.ToolCalls);
        Assert.StartsWith("#2", model.ToolAnswers["h1"], StringComparison.Ordinal);
        Assert.Contains(review.Transcript, message => message.Content?.StartsWith("Your last message was empty", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task AnAnswerBeforeTheRequiredToolCallsIsSentBackOnce()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", "seamless-launch-nothing")));
        GameReviewDossier dossier = await GameReviewDossier.BuildAsync(saved, definition, PlayerSide.Corp);
        var model = new ScriptedModel(
            new LlmChatMessage("assistant", """{"findings": [{"sequence": 222, "category": "x", "claim": "Unchecked.", "citations": []}]}"""),
            new LlmChatMessage("assistant", null, [new("t1", "card_text", "{\"title\":\"Seamless Launch\"}")]),
            new LlmChatMessage("assistant", """{"findings": []}"""));
        GameReview review = await new LlmGameReviewer(model, RulesIndex.Load(RulesRoot), new LlmSampling(0.6), minToolCalls: 3).ReviewAsync(dossier);

        // Pushed back once, it looks one card up and withdraws the finding; its second answer stands.
        Assert.Null(review.Failure);
        Assert.Empty(review.Findings);
        Assert.Equal(1, review.ToolCalls);
        Assert.Single(review.Transcript, message => message.Content?.StartsWith("Before you answer, check each finding", StringComparison.Ordinal) == true);
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
