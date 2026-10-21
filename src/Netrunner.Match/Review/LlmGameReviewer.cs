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

    /// <summary>The second look's verdict, "upheld" or "refuted", when one ran; null otherwise.</summary>
    public string? Check { get; init; }

    public string? CheckReason { get; init; }
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
/// <param name="minToolCalls">Tool calls the review should make before it answers: some models judge the log
/// without looking anything up, and get rules and cards wrong. An earlier answer is sent back once.</param>
public sealed class LlmGameReviewer(ILlmChatClient client, RulesIndex rules, LlmSampling sampling, int maxToolCalls = 60,
    int minToolCalls = 0)
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
        new("history", "Both players' actions between two sequence numbers (passes left out): runs, accesses, installs, rezzes, steals, trashes. Optionally only those mentioning some text, such as a card title or 'Remote 2'.",
            Schema(("from", "The first sequence number."), ("to", "The last sequence number."), ("containing", "Optional text the action must mention; empty for all."))),
    ];

    /// <summary>
    /// Reviews one bot's play in a game. With <paramref name="check"/>, each finding then gets a second look in a
    /// fresh conversation that tries to refute it from the game itself, since the first pass sometimes reports
    /// runs or cards that weren't there.
    /// </summary>
    public async Task<GameReview> ReviewAsync(GameReviewDossier dossier, Action<string>? progress = null, bool check = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        var messages = new List<LlmChatMessage> { LlmChatMessage.System(SystemPrompt(dossier)), LlmChatMessage.User(dossier.DecisionLog()) };
        Conversation review = await ConverseAsync(messages, dossier, maxToolCalls, progress, cancellationToken, minToolCalls)
            .ConfigureAwait(false);
        int toolCalls = review.ToolCalls, prompt = review.PromptTokens, completion = review.CompletionTokens;
        GameReview Result(IReadOnlyList<ReviewFinding> findings, string? failure) =>
            new(dossier.Game.Game.Id, dossier.Side, dossier.Entrant, findings, toolCalls, prompt, completion, failure) { Transcript = messages };
        if (review.Failure is not null) return Result([], review.Failure);
        (IReadOnlyList<ReviewFinding> found, string? parseFailure) = ParseFindings(review.Answer, dossier);
        if (!check || parseFailure is not null) return Result(found, parseFailure);
        var checkedFindings = new List<ReviewFinding>();
        foreach (ReviewFinding finding in found)
        {
            var second = new List<LlmChatMessage> { LlmChatMessage.System(CheckPrompt(dossier)), LlmChatMessage.User(CheckRequest(finding, dossier)) };
            Conversation look = await ConverseAsync(second, dossier, Math.Max(1, maxToolCalls / 3), progress, cancellationToken).ConfigureAwait(false);
            toolCalls += look.ToolCalls;
            prompt += look.PromptTokens;
            completion += look.CompletionTokens;
            messages.Add(LlmChatMessage.User($"(Second look at the finding at #{finding.Sequence}.)"));
            messages.AddRange(second.Skip(1));
            (string verdict, string reason) = ParseCheck(look.Answer);
            checkedFindings.Add(finding with { Check = look.Failure is null ? verdict : "unchecked", CheckReason = look.Failure ?? reason });
        }
        return Result(checkedFindings, null);
    }

    private sealed record Conversation(string? Answer, int ToolCalls, int PromptTokens, int CompletionTokens, string? Failure);

    /// <summary>Runs the tool loop until the model answers without a tool call; past the budget it must answer.</summary>
    private async Task<Conversation> ConverseAsync(List<LlmChatMessage> messages, GameReviewDossier dossier, int budget,
        Action<string>? progress, CancellationToken cancellationToken, int required = 0)
    {
        int toolCalls = 0, prompt = 0, completion = 0, nudges = 0;
        bool pushedBack = false;
        for (int round = 0; round <= budget + nudges; round++)
        {
            IReadOnlyList<LlmTool> tools = toolCalls < budget ? Tools : [];
            LlmChatResponse response;
            try
            {
                response = await client.CompleteAsync(new LlmChatRequest(messages, tools, sampling), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                return new Conversation(null, toolCalls, prompt, completion, exception.Message);
            }
            prompt += response.Usage?.PromptTokens ?? 0;
            completion += response.Usage?.CompletionTokens ?? 0;
            messages.Add(response.Message);
            if (response.Message.ToolCalls is not { Count: > 0 } calls)
            {
                // The model sometimes ends a turn having only thought; ask once or twice for the answer itself.
                if (string.IsNullOrWhiteSpace(response.Message.Content) && nudges < 2)
                {
                    nudges++;
                    messages.Add(LlmChatMessage.User("Your last message was empty. Give your answer now, as the JSON object asked for."));
                    continue;
                }
                if (toolCalls < Math.Min(required, budget) && !pushedBack)
                {
                    // Tool calls asked for (a model that answers from the log alone); one push back, then its answer stands.
                    pushedBack = true;
                    nudges++;
                    messages.Add(LlmChatMessage.User(
                        "Before you answer, check each finding with the tools: card_text for every card it names, history or position for "
                        + "every run, access or board state it relies on, and rules_lookup for every rule. Drop what doesn't hold, then answer again."));
                    continue;
                }
                return new Conversation(response.Message.Content, toolCalls, prompt, completion, null);
            }
            progress?.Invoke(string.Join(", ", calls.Select(call => $"{call.Name} {call.Arguments}")));
            foreach (LlmToolCall call in calls)
            {
                toolCalls++;
                messages.Add(LlmChatMessage.Tool(call.Id, Answer(call, dossier)));
            }
        }
        return new Conversation(null, toolCalls, prompt, completion, "The model never gave its answer.");
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
        long Number(string name, long fallback) =>
            long.TryParse(Text(name).TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : fallback;
        long Sequence() => long.TryParse(Text("sequence").TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : -1;
        return call.Name switch
        {
            "rules_lookup" => rules.Search(Text("query")) is { Count: > 0 } found
                ? string.Join("\n\n", found.Select(rule => $"{rule.Number} ({rule.Section}): {rule.Text}"))
                : "No rule matched; try other words or a rule number.",
            "card_text" => GameReviewDossier.CardText(Text("title")),
            "position" => dossier.Position(Sequence()),
            "decision" => dossier.DecisionDetail(Sequence()),
            "history" => dossier.History(Number("from", 0), Number("to", long.MaxValue), Text("containing")),
            _ => $"There is no tool called '{call.Name}'.",
        };
    }

    /// <summary>
    /// Reads the model's answer: a JSON object with a "findings" array, possibly inside a code fence.
    /// Each finding must name one of the reviewed bot's decisions; its citations are checked against the
    /// rules index and the card data.
    /// </summary>
    public (IReadOnlyList<ReviewFinding> Findings, string? Failure) ParseFindings(string? content, GameReviewDossier dossier) =>
        ParseFindings(content, dossier, rules);

    /// <summary>Reads a findings answer from any reviewer, with its citations checked against <paramref name="rules"/>.</summary>
    public static (IReadOnlyList<ReviewFinding> Findings, string? Failure) ParseFindings(string? content, GameReviewDossier dossier, RulesIndex rules)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        ArgumentNullException.ThrowIfNull(rules);
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

    /// <summary>
    /// What the reviewer is told about timing and competent play; shared by every reviewer (the local model's
    /// tools, Codex's files) so their findings compare.
    /// </summary>
    public const string Knowledge = """
        Timing basics, since a finding that gets them wrong is wasted:
        - The Corp rezzes ICE only when the Runner approaches it; unrezzed ICE nobody approaches costs nothing, and
          declining to rez at an approach is the choice to judge. Assets and upgrades can be rezzed in most windows.
        - Each "Pass" at a timing window is only declining to rez or use an ability there, not a turn spent.
        - Cards leave play: before you claim a card stayed somewhere, check the turn lines or the position.
        - The card_text answer lists "unique" only for unique cards.

        Some of what competent players know:
        - Corp: HQ and R&D need ICE early, since the Runner can win from centrals alone. A remote is for an agenda
          or an asset; ICE on a remote with nothing to protect costs tempo. An upgrade belongs in the server it
          works in, and a unique card doesn't replace its own copy. ICE the Runner keeps getting past unrezzed is
          ICE to rez. A card that can be advanced gains nothing idle for turns. Money is for rezzing and scoring.
        - Runner: build economy and breakers before running into rezzed ICE it can't break; don't run on a server
          whose ICE it can't get through with the money it has; don't access a known trap twice; don't re-run R&D
          for a top card it has already seen; pressure the servers where agendas are likely.
        These are guidelines, not rules: the board decides. Check the position before you report a finding, and
        check with history any run or access your claim relies on.
        A fault the bot repeats is one finding, at its first occurrence, with the repetition in the claim.
        """;

    /// <summary>The findings answer, and the rule that every finding cites what it relies on.</summary>
    public const string AnswerFormat = """
        Answer with one JSON object and nothing else:
        {"findings": [{"sequence": 123, "category": "short-kebab-case", "confidence": "high|medium|low",
          "claim": "what is wrong and why, in one or two sentences",
          "citations": [{"kind": "rule", "ref": "7.5.2"}, {"kind": "card", "ref": "Card Title"}]}]}
        Every finding cites the rules or card texts it relies on. Report what you judge likely wrong, with your
        confidence; an empty findings array is a fine answer when you find nothing.
        """;

    private static string SystemPrompt(GameReviewDossier dossier) => $$"""
        You review a game of Android: Netrunner (Null Signal Games' comprehensive rules v26.03) played by two bots.
        Review only the {{dossier.Side}} bot. The user message lists its decisions: "#sequence turn N kind: chose ...",
        and below each the options it weighed, "*" for the one it took, with its score and the heuristic's reason.
        Each turn opens with a line "== turn N" giving the board at that point, and the opponent's actions that
        change the board appear in brackets, "(Runner #412: ...)", between the reviewed bot's decisions.

        Find plays a competent player would not make: a rule it seems to misread, a card used where it gains
        nothing, an obvious better play missed, and above all a pattern of play that loses games. You may not know
        every rule or card: look them up with the tools rather than guess.
        - rules_lookup(query): the comprehensive rules by number or words.
        - card_text(title): a card's printed text.
        - position(sequence): the whole board before that action (the review is offline; nothing is hidden).
        - decision(sequence): every option at that decision with the bot's scores and reasons.
        - history(from, to, containing): both players' actions in a range, such as every run on a server.

        {{Knowledge}}

        {{AnswerFormat}}
        """;

    private static string CheckPrompt(GameReviewDossier dossier) => $$"""
        You check one claimed mistake by the {{dossier.Side}} bot in a game of Android: Netrunner (Null Signal Games'
        comprehensive rules v26.03). Another reviewer made the claim and is sometimes wrong: it reports runs that never
        happened, cards that had already left play, or card properties the card doesn't have.
        Check every fact the claim relies on with the tools: history(from, to, containing) for what happened,
        position(sequence) for the board, decision(sequence) for the options, card_text and rules_lookup.
        Refute the claim if a fact it relies on is false, or if the play was reasonable in that position.
        Uphold it only if its facts hold and the play was a real mistake.
        Answer with one JSON object and nothing else: {"verdict": "upheld" or "refuted", "reason": "one or two sentences"}.
        """;

    private static string CheckRequest(ReviewFinding finding, GameReviewDossier dossier) =>
        $"The claim, about decision #{finding.Sequence} ({finding.Category}): {finding.Claim}\n"
        + $"It cites: {string.Join(", ", finding.Citations.Select(citation => $"{citation.Kind} {citation.Reference}"))}.\n\n"
        + $"The decision:\n{dossier.DecisionDetail(finding.Sequence)}\nThe board before it:\n{dossier.Position(finding.Sequence)}";

    /// <summary>Reads a second look's verdict; anything but a clear "refuted" is kept as "upheld" for the person to triage.</summary>
    public static (string Verdict, string Reason) ParseCheck(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return ("unchecked", "The second look gave no answer.");
        int start = content.IndexOf('{', StringComparison.Ordinal), end = content.LastIndexOf('}');
        try
        {
            if (start >= 0 && end > start && JsonNode.Parse(content[start..(end + 1)]) is JsonObject answer)
                return ((string?)answer["verdict"] == "refuted" ? "refuted" : "upheld", (string?)answer["reason"] ?? "");
        }
        catch (JsonException)
        {
        }
        return ("unchecked", content.Length > 300 ? content[..300] : content);
    }

    private static JsonObject Schema(params (string Name, string Description)[] properties) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(properties.Select(property => KeyValuePair.Create(property.Name,
            (JsonNode?)new JsonObject { ["type"] = "string", ["description"] = property.Description }))),
        ["required"] = new JsonArray([.. properties.Where(property => !property.Description.StartsWith("Optional", StringComparison.Ordinal))
            .Select(property => (JsonNode)property.Name)]),
    };
}
