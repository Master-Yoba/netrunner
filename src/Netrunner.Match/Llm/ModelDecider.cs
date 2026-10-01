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

/// <summary>
/// Asks a model over an OpenAI-compatible chat, one conversation per decision, with card and rules tools. An
/// answer that isn't one of the options gets one retry; then the verdict has no option and the heuristic's stands.
/// </summary>
public sealed class ModelDecider(ILlmChatClient client, LlmSampling sampling, RulesIndex? rules = null, int maxToolCalls = 3) : IStrategicDecider
{
    private readonly int _maxToolCalls = maxToolCalls;

    public static IReadOnlyList<LlmTool> Tools { get; } =
    [
        new("card_text", "The printed text of any card by title: type, costs, strength, points, rules text.",
            ToolSchema("title", "The card's title.")),
        new("rules_lookup", "Searches the Netrunner comprehensive rules by rule number or a few words.",
            ToolSchema("query", "A rule number or a few words.")),
    ];

    public async Task<StrategicVerdict> DecideAsync(PlayerSide side, string request, int options, CancellationToken cancellationToken)
    {
        var messages = new List<LlmChatMessage>
        {
            LlmChatMessage.System(SystemPrompt(side)),
            LlmChatMessage.User(request + "Answer with {\"option\": <number>, \"reason\": \"one sentence\"}."),
        };
        int toolCalls = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            (string? answer, int used, string? error) = await ConverseAsync(messages, cancellationToken).ConfigureAwait(false);
            toolCalls += used;
            if (error is not null) return new StrategicVerdict(null, null, error, toolCalls);
            (int? choice, string? reason) = Parse(answer, options);
            if (choice is not null) return new StrategicVerdict(choice, reason, null, toolCalls);
            messages.Add(LlmChatMessage.User($"That wasn't a valid answer. Reply with only {{\"option\": <a number from 1 to {options}>, \"reason\": \"...\"}}."));
        }
        return new StrategicVerdict(null, null, null, toolCalls);
    }

    private async Task<(string? Answer, int ToolCalls, string? Failure)> ConverseAsync(List<LlmChatMessage> messages, CancellationToken cancellationToken)
    {
        int toolCalls = 0;
        for (int round = 0; round <= _maxToolCalls; round++)
        {
            LlmChatResponse response;
            try
            {
                response = await client.CompleteAsync(new LlmChatRequest(messages, toolCalls < _maxToolCalls ? Tools : [], sampling), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return (null, toolCalls, exception.Message);
            }
            messages.Add(response.Message);
            if (response.Message.ToolCalls is not { Count: > 0 } calls) return (response.Message.Content, toolCalls, null);
            foreach (LlmToolCall call in calls)
            {
                toolCalls++;
                messages.Add(LlmChatMessage.Tool(call.Id, Answer(call)));
            }
        }
        return (null, toolCalls, "The model never answered.");
    }

    private string Answer(LlmToolCall call)
    {
        string argument;
        try
        {
            JsonObject arguments = JsonNode.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments)?.AsObject() ?? [];
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

    private static string SystemPrompt(PlayerSide side) => $$"""
        You play Android: Netrunner (Null Signal Games' comprehensive rules) as the {{side}}, against a bot. The Corp
        wins with 7 agenda points scored, or when the Runner can't take damage; the Runner wins with 7 agenda points
        stolen, or when R&D is empty when the Corp must draw.
        Each message shows the board as you see it, the cards in view, and your legal options for the next click.
        A heuristic bot scored the options; it is a fair baseline that makes known mistakes. Take its choice when it
        is right, and another option when you see a better plan. Think about the whole turn, not only this click.
        You may look up cards and rules with the tools.
        - Corp: protect HQ and R&D early; put agendas only where the Runner can't break in this turn; keep credits
          to rez the ICE that matters; score when you can; a remote is for an agenda or an asset.
        - Runner: build economy and breakers; run where agendas are likely and you can get in; don't run into ICE
          you can't break with the credits you have; trash assets that pay the Corp when it's worth it.
        Answer with one JSON object and nothing else: {"option": <number>, "reason": "one sentence"}.
        """;

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
