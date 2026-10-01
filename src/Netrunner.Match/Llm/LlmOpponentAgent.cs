using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.LLM;
using Netrunner.Match.Review;

namespace Netrunner.Match.Llm;

/// <summary>One strategic decision the LLM opponent was asked, and what came of it.</summary>
/// <param name="Outcome">"model" (the model's option), "invalid" or "failed" (the heuristic's, after a bad answer or no answer).</param>
public sealed record LlmOpponentDecision(int Turn, string Prompt, int Options, int Advised, int Chosen, string Outcome, string? Reason,
    int ToolCalls, double Seconds);

/// <summary>
/// The LLM opponent (Wave 4.5 P3): a headless agent whose strategic choices, the turn's actions (installs,
/// runs, advances, plays, scoring), come from a model, while the heuristic handles breaking, paying and the
/// priority windows. Its purpose is play the heuristic never shows; its games feed the lints and reviews.
/// </summary>
/// <remarks>
/// It reads only its own observation (through <see cref="ObservationViewMapper"/>), so it passes the same
/// leak boundary as the offline agents, and it returns only an action the engine issued. A model answer
/// that isn't one of the numbered options gets one retry; after that, or when the model doesn't answer, the
/// heuristic's choice stands. The heuristic is asked every decision, so its memory stays in step and its
/// scores reach the model as advice; when the model picks another option, the heuristic's choice is discarded.
/// </remarks>
public sealed class LlmOpponentAgent : IAgentLifecycle
{
    private readonly OfflineAgent _heuristic;
    private readonly ILlmChatClient _client;
    private readonly LlmSampling _sampling;
    private readonly ICardMetadataResolver _metadata;
    private readonly IBehaviorCatalog _behavior;
    private readonly RulesIndex? _rules;
    private readonly int _maxToolCalls;
    private readonly List<(int Turn, string Choice, string Reason)> _recent = [];
    private ActionId? _advice;

    public LlmOpponentAgent(OfflineAgent heuristic, ILlmChatClient client, LlmSampling sampling, ICardMetadataResolver metadata,
        IBehaviorCatalog behavior, RulesIndex? rules = null, int maxToolCalls = 3)
    {
        _heuristic = heuristic ?? throw new ArgumentNullException(nameof(heuristic));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sampling = sampling ?? throw new ArgumentNullException(nameof(sampling));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        _rules = rules;
        _maxToolCalls = maxToolCalls;
    }

    /// <summary>The LLM opponent for one seat, around the standard heuristic of that seat (with its scores kept).</summary>
    public static LlmOpponentAgent Create(PlayerSide side, FullMatchDefinition definition, ILlmChatClient client, LlmSampling sampling,
        RulesIndex? rules = null, int maxToolCalls = 3)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var standard = (OfflineAgent)RatingLadder.CreateAgent("standard", side, definition);
        var heuristic = new OfflineAgent(standard.Configuration, OfflinePolicies.Create(standard.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), null, captureDiagnostics: true);
        return new LlmOpponentAgent(heuristic, client, sampling, new CardCatalogMetadataResolver(CardDataResource.LoadEmbedded()),
            definition.Behavior, rules, maxToolCalls);
    }

    public PlayerSide ControlledSide => _heuristic.ControlledSide;

    public List<LlmOpponentDecision> Decisions { get; } = [];

    public static IReadOnlyList<LlmTool> Tools { get; } =
    [
        new("card_text", "The printed text of any card by title: type, costs, strength, points, rules text.",
            ToolSchema("title", "The card's title.")),
        new("rules_lookup", "Searches the Netrunner comprehensive rules by rule number or a few words.",
            ToolSchema("query", "A rule number or a few words.")),
    ];

    public async ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ActionId advice = await _heuristic.ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false);
        _advice = advice;
        if (observation.Decision is not { Kind: DecisionKind.TurnAction } decision || observation.LegalActions.Count < 2) return advice;

        MatchViewModel view = ObservationViewMapper.Build(observation, _metadata, _behavior);
        Func<LegalAction?, string?> describe = HumanGameReview.Describer(view);
        IReadOnlyList<LegalAction> options = observation.LegalActions;
        int advised = options.ToList().FindIndex(action => action.Id == advice) + 1;
        Dictionary<ActionId, AgentUtility> scores = _heuristic.LastEvaluation?.Candidates.ToDictionary(candidate => candidate.ActionId, candidate => candidate.Utility)
            ?? [];
        string request = Request(view, options, describe, scores, advised);

        var watch = Stopwatch.StartNew();
        var messages = new List<LlmChatMessage> { LlmChatMessage.System(SystemPrompt(ControlledSide)), LlmChatMessage.User(request) };
        int toolCalls = 0;
        (int? choice, string? reason, string? failure) = (null, null, null);
        for (int attempt = 0; attempt < 2 && choice is null; attempt++)
        {
            (string? answer, int used, string? error) = await ConverseAsync(messages, cancellationToken).ConfigureAwait(false);
            toolCalls += used;
            if (error is not null) { failure = error; break; }
            (choice, reason) = Parse(answer, options.Count);
            if (choice is null)
                messages.Add(LlmChatMessage.User($"That wasn't a valid answer. Reply with only {{\"option\": <a number from 1 to {options.Count}>, \"reason\": \"...\"}}."));
        }
        watch.Stop();

        string outcome = choice is not null ? "model" : failure is not null ? "failed" : "invalid";
        int chosen = choice ?? advised;
        Decisions.Add(new LlmOpponentDecision(view.Turn.TurnNumber ?? 0, decision.Prompt, options.Count, advised, chosen, outcome,
            reason ?? failure, toolCalls, watch.Elapsed.TotalSeconds));
        if (choice is not null)
        {
            _recent.Add((view.Turn.TurnNumber ?? 0, describe(options[chosen - 1]) ?? options[chosen - 1].Description, reason ?? ""));
            if (_recent.Count > 6) _recent.RemoveAt(0);
        }
        return options[chosen - 1].Id;
    }

    public void Observe(Observation observation, long actionSequence) => _heuristic.Observe(observation, actionSequence);

    public void CommitDecision(DecisionId decisionId, long revision, ActionId actionId, long actionSequence)
    {
        // The heuristic commits only its own choice; another one is the model's, and the heuristic's is dropped.
        if (_advice == actionId) _heuristic.CommitDecision(decisionId, revision, actionId, actionSequence);
        else _heuristic.DiscardDecision();
        _advice = null;
    }

    public void DiscardDecision()
    {
        _heuristic.DiscardDecision();
        _advice = null;
    }

    private string Request(MatchViewModel view, IReadOnlyList<LegalAction> options, Func<LegalAction?, string?> describe,
        Dictionary<ActionId, AgentUtility> scores, int advised)
    {
        var text = new StringBuilder();
        text.Append(LlmBoardText.Board(view));
        text.AppendLine();
        text.AppendLine("Cards in view:");
        text.Append(LlmBoardText.Glossary(view));
        if (_recent.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Your recent choices:");
            foreach ((int turn, string choice, string reason) in _recent)
                text.AppendLine(CultureInfo.InvariantCulture, $"- turn {turn}: {choice}{(reason.Length > 0 ? $" ({reason})" : "")}");
        }
        text.AppendLine();
        text.AppendLine("Your options now (the heuristic's score and reason in brackets):");
        for (int index = 0; index < options.Count; index++)
        {
            string score = scores.TryGetValue(options[index].Id, out AgentUtility? utility) ? $" [{utility.Score}: {utility.Reason}]" : "";
            text.AppendLine(CultureInfo.InvariantCulture, $"{index + 1}. {describe(options[index]) ?? options[index].Description}{score}");
        }
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"The heuristic would choose option {advised}. Answer with {{\"option\": <number>, \"reason\": \"one sentence\"}}.");
        return text.ToString();
    }

    private async Task<(string? Answer, int ToolCalls, string? Failure)> ConverseAsync(List<LlmChatMessage> messages, CancellationToken cancellationToken)
    {
        int toolCalls = 0;
        for (int round = 0; round <= _maxToolCalls; round++)
        {
            LlmChatResponse response;
            try
            {
                response = await _client.CompleteAsync(new LlmChatRequest(messages, toolCalls < _maxToolCalls ? Tools : [], _sampling), cancellationToken)
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
            "rules_lookup" when _rules is not null => _rules.Search(argument) is { Count: > 0 } found
                ? string.Join("\n\n", found.Select(rule => $"{rule.Number}: {rule.Text}"))
                : "No rule matched.",
            _ => $"There is no tool called '{call.Name}'.",
        };
    }

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
}
