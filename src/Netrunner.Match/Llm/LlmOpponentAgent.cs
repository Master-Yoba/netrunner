using System.Diagnostics;
using System.Globalization;
using System.Text;
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
    private readonly IStrategicDecider _decider;
    private readonly ICardMetadataResolver _metadata;
    private readonly IBehaviorCatalog _behavior;
    private readonly List<(int Turn, string Choice, string Reason)> _recent = [];
    private ActionId? _advice;

    public LlmOpponentAgent(OfflineAgent heuristic, IStrategicDecider decider, ICardMetadataResolver metadata, IBehaviorCatalog behavior)
    {
        _heuristic = heuristic ?? throw new ArgumentNullException(nameof(heuristic));
        _decider = decider ?? throw new ArgumentNullException(nameof(decider));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
    }

    /// <summary>The LLM opponent for one seat, asking a model over an OpenAI-compatible chat.</summary>
    public static LlmOpponentAgent Create(PlayerSide side, FullMatchDefinition definition, ILlmChatClient client, LlmSampling sampling,
        RulesIndex? rules = null, int maxToolCalls = 3) =>
        Create(side, definition, new ModelDecider(client, sampling, rules, maxToolCalls));

    /// <summary>The opponent for one seat around the standard heuristic of that seat (with its scores kept).</summary>
    public static LlmOpponentAgent Create(PlayerSide side, FullMatchDefinition definition, IStrategicDecider decider)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var standard = (OfflineAgent)RatingLadder.CreateAgent("standard", side, definition);
        var heuristic = new OfflineAgent(standard.Configuration, OfflinePolicies.Create(standard.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), null, captureDiagnostics: true);
        // The match's own metadata: on an Elevation pool the Gateway data alone left Elevation cards untitled in the
        // LLM's views (bot loop 1: Sonnet named them by code).
        return new LlmOpponentAgent(heuristic, decider, definition.Metadata, definition.Behavior);
    }

    public PlayerSide ControlledSide => _heuristic.ControlledSide;

    public List<LlmOpponentDecision> Decisions { get; } = [];

    /// <summary>Called with each decision as it is made (progress for a long run).</summary>
    public Action<LlmOpponentDecision>? Decided { get; set; }

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
        StrategicVerdict verdict = await _decider.DecideAsync(ControlledSide, request, options.Count, cancellationToken).ConfigureAwait(false);
        watch.Stop();
        (int? choice, string? reason, string? failure, int toolCalls) = (verdict.Option is >= 1 && verdict.Option <= options.Count ? verdict.Option : null,
            verdict.Reason, verdict.Failure, verdict.ToolCalls);

        string outcome = choice is not null ? "model" : failure is not null ? "failed" : "invalid";
        int chosen = choice ?? advised;
        var made = new LlmOpponentDecision(view.Turn.TurnNumber ?? 0, decision.Prompt, options.Count, advised, chosen, outcome,
            reason ?? failure, toolCalls, watch.Elapsed.TotalSeconds);
        Decisions.Add(made);
        Decided?.Invoke(made);
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
        text.AppendLine(CultureInfo.InvariantCulture, $"The heuristic would choose option {advised}.");
        return text.ToString();
    }

    /// <summary>Reads {"option": n, "reason": "..."} from an answer; null unless n is one of the options.</summary>
    public static (int? Option, string? Reason) Parse(string? content, int options) => ModelDecider.Parse(content, options);
}
