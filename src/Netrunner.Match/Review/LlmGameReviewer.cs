using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Netrunner.Core;
using Netrunner.LLM;

namespace Netrunner.Match.Review;

/// <summary>A source a finding relies on: a comprehensive rule by number, or a card by title.</summary>
public sealed record ReviewCitation(string Kind, string Reference, bool Resolved);

/// <summary>
/// A play the reviewer thinks is wrong: where, whose, what kind of fault, the claim, and what it relied on.
/// A finding whose citations don't resolve to a real rule or card is kept as unverified.
/// </summary>
public sealed record ReviewFinding(string GameId, PlayerSide Side, string Entrant, long Sequence, string Category, string Claim,
    IReadOnlyList<ReviewCitation> Citations, string Confidence = "medium")
{
    public bool Verified => Citations.Any(citation => citation.Resolved);
}

/// <summary>What one review produced, and what it cost.</summary>
public sealed record GameReview(string GameId, PlayerSide Side, string Entrant, IReadOnlyList<ReviewFinding> Findings, int ToolCalls,
    int PromptTokens, int CompletionTokens, string? Failure)
{
    /// <summary>The whole conversation, to spot-check what a finding rests on; saved apart from the review.</summary>
    [JsonIgnore]
    public IReadOnlyList<LlmChatMessage> Transcript { get; init; } = [];
}

/// <summary>
/// The LLM game reviewer (Wave 4.5, P2): a model reads one bot's decisions from a self-play game and
/// reports plays that look wrong. It can't be assumed to know Netrunner, so it gets tools instead of a
/// long prompt: the comprehensive rules, card texts, the board at any point, and each decision's options
/// with the bot's scores and reasons. LLM runs never gate a bot version; the tests they lead to do.
/// </summary>
public sealed class LlmGameReviewer(ILlmChatClient client, RulesIndex rules, LlmSampling sampling, int maxToolCalls = 60)
{
    public static IReadOnlyList<LlmTool> Tools { get; } =
    [
        new("rules_lookup", "Searches the Netrunner comprehensive rules. Give a rule number such as '7.5.2' or a few words; returns the matching rules with their numbers.",
            Schema(("query", "A rule number or a few words to search for."))),
        new("card_text", "The printed text of a card: type, subtypes, costs, strength, agenda points and rules text.",
            Schema(("title", "The card's title (or its code)."))),
        new("position", "The whole board just before the action with this sequence number: both players' credits, clicks, hands, servers with their ICE and root cards, the rig, and any run.",
            Schema(("sequence", "The action's sequence number, as in the decision log."))),
        new("decision", "One of the reviewed bot's decisions in full: every legal option with the bot's priority, score and reason.",
            Schema(("sequence", "The decision's sequence number."))),
    ];

    public async Task<GameReview> ReviewAsync(GameReviewDossier dossier, Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        var messages = new List<LlmChatMessage> { LlmChatMessage.System(SystemPrompt(dossier)), LlmChatMessage.User(dossier.DecisionLog()) };
        int toolCalls = 0, prompt = 0, completion = 0;
        for (int round = 0; round <= maxToolCalls; round++)
        {
            // Past the tool budget, the model must answer with what it has.
            IReadOnlyList<LlmTool> tools = toolCalls < maxToolCalls ? Tools : [];
            LlmChatResponse response;
            try
            {
                response = await client.CompleteAsync(new LlmChatRequest(messages, tools, sampling), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                return new GameReview(dossier.Game.Game.Id, dossier.Side, dossier.Entrant, [], toolCalls, prompt, completion, exception.Message)
                { Transcript = messages };
            }
            prompt += response.Usage?.PromptTokens ?? 0;
            completion += response.Usage?.CompletionTokens ?? 0;
            messages.Add(response.Message);
            if (response.Message.ToolCalls is not { Count: > 0 } calls)
            {
                (IReadOnlyList<ReviewFinding> findings, string? failure) = ParseFindings(response.Message.Content, dossier);
                return new GameReview(dossier.Game.Game.Id, dossier.Side, dossier.Entrant, findings, toolCalls, prompt, completion, failure)
                { Transcript = messages };
            }
            progress?.Invoke(string.Join(", ", calls.Select(call => $"{call.Name} {call.Arguments}")));
            foreach (LlmToolCall call in calls)
            {
                toolCalls++;
                messages.Add(LlmChatMessage.Tool(call.Id, Answer(call, dossier)));
            }
        }
        return new GameReview(dossier.Game.Game.Id, dossier.Side, dossier.Entrant, [], toolCalls, prompt, completion, "The model never gave its findings.")
        { Transcript = messages };
    }

    /// <summary>Answers one tool call from the dossier, the rules and the card data.</summary>
    public string Answer(LlmToolCall call, GameReviewDossier dossier)
    {
        ArgumentNullException.ThrowIfNull(call);
        JsonObject arguments;
        try
        {
            arguments = JsonNode.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments)?.AsObject() ?? [];
        }
        catch (JsonException)
        {
            return "The arguments weren't valid JSON.";
        }
        string Text(string name) => arguments[name] is JsonValue value ? value.ToString() : "";
        long Sequence() => long.TryParse(Text("sequence").TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : -1;
        return call.Name switch
        {
            "rules_lookup" => rules.Search(Text("query")) is { Count: > 0 } found
                ? string.Join("\n\n", found.Select(rule => $"{rule.Number} ({rule.Section}): {rule.Text}"))
                : "No rule matched; try other words or a rule number.",
            "card_text" => GameReviewDossier.CardText(Text("title")),
            "position" => dossier.Position(Sequence()),
            "decision" => dossier.DecisionDetail(Sequence()),
            _ => $"There is no tool called '{call.Name}'.",
        };
    }

    /// <summary>
    /// Reads the model's answer: a JSON object with a "findings" array, possibly inside a code fence.
    /// Each finding must name one of the reviewed bot's decisions; its citations are checked against the
    /// rules index and the card data.
    /// </summary>
    public (IReadOnlyList<ReviewFinding> Findings, string? Failure) ParseFindings(string? content, GameReviewDossier dossier)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        if (string.IsNullOrWhiteSpace(content)) return ([], "The model's answer was empty.");
        int start = content.IndexOf('{', StringComparison.Ordinal), end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return ([], "The model's answer held no JSON object.");
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content[start..(end + 1)]);
        }
        catch (JsonException exception)
        {
            return ([], $"The model's answer wasn't valid JSON: {exception.Message}");
        }
        if (root?["findings"] is not JsonArray array) return ([], "The model's answer had no findings array.");
        HashSet<long> decisions = [.. dossier.Decisions.Select(decision => decision.Sequence)];
        var findings = new List<ReviewFinding>();
        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject finding) continue;
            long sequence = finding["sequence"] is JsonValue number && number.TryGetValue(out long parsed) ? parsed
                : long.TryParse(((string?)finding["sequence"])?.TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out long text) ? text : -1;
            if (!decisions.Contains(sequence)) continue;
            IReadOnlyList<ReviewCitation> citations = [.. (finding["citations"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(citation => ((string?)citation["kind"] ?? "", (string?)citation["ref"] ?? (string?)citation["reference"] ?? ""))
                .Where(pair => pair.Item2.Length > 0)
                .Select(pair => new ReviewCitation(pair.Item1, pair.Item2, pair.Item1 switch
                {
                    "rule" => rules.Rule(pair.Item2) is not null,
                    "card" => GameReviewDossier.CardExists(pair.Item2),
                    _ => false,
                }))];
            findings.Add(new ReviewFinding(dossier.Game.Game.Id, dossier.Side, dossier.Entrant, sequence,
                (string?)finding["category"] ?? "other", (string?)finding["claim"] ?? "", citations,
                (string?)finding["confidence"] is "high" or "low" ? (string)finding["confidence"]! : "medium"));
        }
        return (findings, null);
    }

    private static string SystemPrompt(GameReviewDossier dossier) => $$"""
        You review a game of Android: Netrunner (Null Signal Games' comprehensive rules v26.03) played by two bots.
        Review only the {{dossier.Side}} bot. The user message lists its decisions: "#sequence turn N kind: chose ...",
        and below each the options it weighed, "*" for the one it took, with its score and the heuristic's reason.
        Each turn opens with a line "== turn N" giving the board at that point.

        Find plays a competent player would not make: a rule it seems to misread, a card used where it gains
        nothing, an obvious better play missed, and above all a pattern of play that loses games. You may not know
        every rule or card: look them up with the tools rather than guess.
        - rules_lookup(query): the comprehensive rules by number or words.
        - card_text(title): a card's printed text.
        - position(sequence): the whole board before that action (the review is offline; nothing is hidden).
        - decision(sequence): every option at that decision with the bot's scores and reasons.

        Some of what competent players know:
        - Corp: HQ and R&D need ICE early, since the Runner can win from centrals alone. A remote is for an agenda
          or an asset; ICE on a remote with nothing to protect costs tempo. An upgrade belongs in the server it
          works in, and a unique card doesn't replace its own copy. ICE the Runner keeps getting past unrezzed is
          ICE to rez. A card that can be advanced gains nothing idle for turns. Money is for rezzing and scoring.
        - Runner: build economy and breakers before running into rezzed ICE it can't break; don't run on a server
          whose ICE it can't get through with the money it has; don't access a known trap twice; don't re-run R&D
          for a top card it has already seen; pressure the servers where agendas are likely.
        These are guidelines, not rules: the board decides. Check the position before you report a finding.
        A fault the bot repeats is one finding, at its first occurrence, with the repetition in the claim.

        Answer with one JSON object and nothing else:
        {"findings": [{"sequence": 123, "category": "short-kebab-case", "confidence": "high|medium|low",
          "claim": "what is wrong and why, in one or two sentences",
          "citations": [{"kind": "rule", "ref": "7.5.2"}, {"kind": "card", "ref": "Card Title"}]}]}
        Every finding cites the rules or card texts it relies on. Report what you judge likely wrong, with your
        confidence; an empty findings array is a fine answer when you find nothing.
        """;

    private static JsonObject Schema(params (string Name, string Description)[] properties) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(properties.Select(property => KeyValuePair.Create(property.Name,
            (JsonNode?)new JsonObject { ["type"] = "string", ["description"] = property.Description }))),
        ["required"] = new JsonArray([.. properties.Select(property => (JsonNode)property.Name)]),
    };
}
