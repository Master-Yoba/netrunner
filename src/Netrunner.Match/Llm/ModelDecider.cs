using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match.Review;

namespace Netrunner.Match.Llm;

/// <summary>A strategic decision's answer: the option (1-based), why, or why there is none.</summary>
public sealed record StrategicVerdict(int? Option, string? Reason, string? Failure, int ToolCalls = 0);

/// <summary>
/// Who makes the LLM opponent's strategic choices: a model over chat (<see cref="ModelDecider"/>), or a player
/// outside the process, such as Codex through the MCP game server. The agent validates the option either way.
/// </summary>
public interface IStrategicDecider
{
    Task<StrategicVerdict> DecideAsync(PlayerSide side, string request, int options, CancellationToken cancellationToken);
}

/// <summary>How a <see cref="ModelDecider"/> asks for a decision.</summary>
/// <param name="MaxToolCalls">Lookups per decision; 0 offers none.</param>
/// <param name="CardText">Whether to offer the card text lookup; the request already holds every card in view.</param>
/// <param name="StructuredAnswer">
/// Constrains every answer with a JSON schema: {"option", "reason"}, or a lookup while lookups remain. Lookups are
/// then answers rather than tool calls, since llama.cpp can't constrain an answer and offer tools in one request,
/// and a model whose thinking budget runs out can't go on thinking in its answer.
/// </param>
/// <param name="Deadline">The longest a decision may take; past it the decision fails and the heuristic's choice stands.</param>
/// <param name="Defer">
/// Makes the heuristic's choice the default, to leave only for a gain the model can name: free overrides lost more
/// games than they won against the heuristic alone (the latency sweep's 16 Gateway deals).
/// </param>
public sealed record ModelDeciderSettings(int MaxToolCalls = 3, bool CardText = true, bool StructuredAnswer = false, TimeSpan? Deadline = null,
    bool Defer = false);

/// <summary>
/// Asks a model over an OpenAI-compatible chat, one conversation per decision, with a rules summary, the rules for
/// mechanics in view, and card and rules lookups. An answer that isn't one of the options gets one retry; then the
/// verdict has no option and the heuristic's stands.
/// </summary>
public sealed class ModelDecider(ILlmChatClient client, LlmSampling sampling, RulesIndex? rules = null, ModelDeciderSettings? settings = null)
    : IStrategicDecider
{
    private static readonly LlmTool CardTextTool = new("card_text",
        "The printed text of any card by title: type, costs, strength, points, rules text.", ToolSchema("title", "The card's title."));

    private static readonly LlmTool RulesTool = new("rules_lookup",
        "Searches the Netrunner comprehensive rules by rule number or a few words.", ToolSchema("query", "A rule number or a few words."));

    private readonly ModelDeciderSettings _settings = settings ?? new();

    /// <summary>The lookups offered: card text unless turned off, and the rules when there are rules to search.</summary>
    public IReadOnlyList<LlmTool> Tools { get; } = (settings ?? new()).MaxToolCalls == 0 ? []
        : [.. new[] { (settings ?? new()).CardText ? CardTextTool : null, rules is null ? null : RulesTool }.OfType<LlmTool>()];

    public async Task<StrategicVerdict> DecideAsync(PlayerSide side, string request, int options, CancellationToken cancellationToken)
    {
        var messages = new List<LlmChatMessage>
        {
            LlmChatMessage.System(SystemPrompt(side, Lookups(), _settings.Defer)),
            LlmChatMessage.User(request + LlmRulesPrimer.RulesFor(request, rules) + "Answer with {\"option\": <number>, \"reason\": \"one sentence\"}."),
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_settings.Deadline is { } limit) deadline.CancelAfter(limit);
        int toolCalls = 0;
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                (string? answer, int used, string? error) = await ConverseAsync(messages, options, deadline.Token).ConfigureAwait(false);
                toolCalls += used;
                if (error is not null) return new StrategicVerdict(null, null, error, toolCalls);
                (int? choice, string? reason) = Parse(answer, options);
                if (choice is not null) return new StrategicVerdict(choice, reason, null, toolCalls);
                messages.Add(LlmChatMessage.User($"That wasn't a valid answer. Reply with only {{\"option\": <a number from 1 to {options}>, \"reason\": \"...\"}}."));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _settings.Deadline is { } passed)
        {
            return new StrategicVerdict(null, null, FormattableString.Invariant($"No answer within {passed.TotalSeconds:0} s."), toolCalls);
        }
        return new StrategicVerdict(null, null, null, toolCalls);
    }

    private async Task<(string? Answer, int ToolCalls, string? Failure)> ConverseAsync(List<LlmChatMessage> messages, int options,
        CancellationToken cancellationToken)
    {
        int toolCalls = 0;
        for (int round = 0; round <= _settings.MaxToolCalls; round++)
        {
            IReadOnlyList<LlmTool> offered = toolCalls < _settings.MaxToolCalls ? Tools : [];
            LlmChatRequest chat = _settings.StructuredAnswer
                ? new LlmChatRequest(messages, [], sampling, AnswerSchema(options, offered))
                : new LlmChatRequest(messages, offered, sampling);
            LlmChatResponse response;
            try
            {
                response = await client.CompleteAsync(chat, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return (null, toolCalls, exception.Message);
            }
            messages.Add(response.Message);
            if (_settings.StructuredAnswer)
            {
                if (Lookup(response.Message.Content, offered) is not { } lookup) return (response.Message.Content, toolCalls, null);
                toolCalls++;
                messages.Add(LlmChatMessage.User($"{lookup.Name}: {Answer(lookup)}"));
                continue;
            }
            if (response.Message.ToolCalls is not { Count: > 0 } calls) return (response.Message.Content, toolCalls, null);
            foreach (LlmToolCall call in calls)
            {
                toolCalls++;
                messages.Add(LlmChatMessage.Tool(call.Id, Answer(call)));
            }
        }
        return (null, toolCalls, "The model never answered.");
    }

    /// <summary>A lookup asked for in a structured answer, as {"rules_lookup": "..."}, if one was offered.</summary>
    private static LlmToolCall? Lookup(string? content, IReadOnlyList<LlmTool> offered)
    {
        if (offered.Count == 0 || string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            if (JsonNode.Parse(content) is not JsonObject answer) return null;
            return offered.FirstOrDefault(tool => answer[tool.Name] is JsonValue) is { } asked
                ? new LlmToolCall(asked.Name, asked.Name, new JsonObject { ["value"] = answer[asked.Name]!.DeepClone() }.ToJsonString())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The answer's shape: an option and a reason, or one of the lookups still offered.</summary>
    private static JsonObject AnswerSchema(int options, IReadOnlyList<LlmTool> lookups)
    {
        var choice = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["option"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = options },
                ["reason"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("option", "reason"),
            ["additionalProperties"] = false,
        };
        if (lookups.Count == 0) return choice;
        return new JsonObject
        {
            ["anyOf"] = new JsonArray([choice, .. lookups.Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { [tool.Name] = new JsonObject { ["type"] = "string", ["description"] = tool.Description } },
                ["required"] = new JsonArray(tool.Name),
                ["additionalProperties"] = false,
            })]),
        };
    }

    private string Answer(LlmToolCall call)
    {
        string argument;
        try
        {
            JsonNode? parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
            if (parsed is not null and not JsonObject) return "The arguments must be a JSON object.";
            JsonObject arguments = parsed as JsonObject ?? [];
            argument = (arguments.FirstOrDefault().Value as JsonValue)?.ToString() ?? "";
        }
        catch (JsonException)
        {
            return "The arguments weren't valid JSON.";
        }
        return call.Name switch
        {
            "card_text" => GameReviewDossier.CardText(argument),
            "rules_lookup" when rules is not null => rules.Search(argument) is { Count: > 0 } found
                ? string.Join("\n\n", found.Select(rule => $"{rule.Number}: {rule.Text}"))
                : "No rule matched.",
            _ => $"There is no tool called '{call.Name}'.",
        };
    }

    /// <summary>How the system prompt offers lookups: none, as tools, or as answers when answers are structured.</summary>
    private string Lookups() => Tools.Count == 0 ? ""
        : _settings.StructuredAnswer
            ? "When the summary below and the cards in view don't settle a rule or a card, answer "
              + string.Join(" or ", Tools.Select(tool => $"{{\"{tool.Name}\": \"...\"}}"))
              + " instead; every lookup keeps your opponent waiting.\n"
            : "Look up a card or a rule with the tools only when the summary below and the cards in view don't settle it; "
              + "every lookup keeps your opponent waiting.\n";

    private static string SystemPrompt(PlayerSide side, string lookups, bool defer) => $$"""
        You play Android: Netrunner (Null Signal Games' comprehensive rules) as the {{side}}, against a bot. The Corp
        wins with 7 agenda points scored, or when the Runner can't take damage; the Runner wins with 7 agenda points
        stolen, or when R&D is empty when the Corp must draw.
        Each message shows the board as you see it, the cards in view, and your legal options for the next click.
        {{(defer ? Deferring : Advising)}} Think about the whole turn, not only this click.
        {{lookups}}- Corp: protect HQ and R&D early; put agendas only where the Runner can't break in this turn; keep credits
          to rez the ICE that matters; score when you can; a remote is for an agenda or an asset.
        - Runner: build economy and breakers; run where agendas are likely and you can get in; don't run into ICE
          you can't break with the credits you have; trash assets that pay the Corp when it's worth it.
        Answer with one JSON object and nothing else: {"option": <number>, "reason": "one sentence"}.

        {{LlmRulesPrimer.Text}}
        """;

    private const string Advising = "A heuristic bot scored the options; it is a fair baseline that makes known mistakes. "
        + "Take its choice when it is right, and another option when you see a better plan.";

    private const string Deferring = "A heuristic bot scored the options and its choice is your default: it plays soundly "
        + "over a whole game. Choose another option only for a gain you can name that it misses, such as an agenda you can "
        + "steal or score this turn, a loss you avoid, or a card or rule its reason gets wrong; otherwise answer with its option.";

    private static JsonObject ToolSchema(string name, string description) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { [name] = new JsonObject { ["type"] = "string", ["description"] = description } },
        ["required"] = new JsonArray(name),
    };

    /// <summary>Reads {"option": n, "reason": "..."} from an answer; null unless n is one of the options.</summary>
    public static (int? Option, string? Reason) Parse(string? content, int options)
    {
        if (string.IsNullOrWhiteSpace(content)) return (null, null);
        int start = content.IndexOf('{', StringComparison.Ordinal), end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return (null, null);
        try
        {
            if (JsonNode.Parse(content[start..(end + 1)]) is not JsonObject answer) return (null, null);
            int? option = answer["option"] is JsonValue value && value.TryGetValue(out int number) ? number
                : int.TryParse((string?)answer["option"], NumberStyles.None, CultureInfo.InvariantCulture, out int text) ? text : null;
            return option is >= 1 && option <= options ? (option, (string?)answer["reason"]) : (null, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return (null, null);
        }
    }
}
