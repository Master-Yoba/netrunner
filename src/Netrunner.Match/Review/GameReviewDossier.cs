using System.Globalization;
using System.Text;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match.Review;

/// <summary>An option the bot weighed at a decision: what it was, its score and the heuristic's reason.</summary>
public sealed record ReviewOption(string Action, int Priority, int Score, string Reason, bool Chosen);

/// <summary>One of the reviewed bot's decisions, with the options it weighed.</summary>
public sealed record ReviewDecision(long Sequence, int Turn, string Kind, string Prompt, string Chosen, IReadOnlyList<ReviewOption> Options, bool Diverged);

/// <summary>
/// What the LLM reviewer (Wave 4.5, P2) reads about one saved game: the reviewed bot's decisions with
/// the alternatives and the heuristic's scores and reasons (the bot is asked again at each of its
/// decisions, with diagnostics on), and the full board at any point. Offline, nothing is hidden.
/// </summary>
public sealed class GameReviewDossier
{
    private readonly Dictionary<long, GameState> _states = [];
    private readonly Dictionary<long, ReviewDecision> _decisions = [];
    private readonly HashSet<long> _diverged = [];
    private readonly SortedDictionary<long, string> _opponent = [];

    /// <summary>The opponent's actions the log shows: what changes the board or the score, not passes and breaks.</summary>
    private static readonly HashSet<ActionKind> OpponentShown =
    [
        ActionKind.StartRun, ActionKind.InstallCard, ActionKind.PlayCard, ActionKind.RezCard, ActionKind.RezIce,
        ActionKind.AdvanceAgenda, ActionKind.AdvanceCard, ActionKind.ScoreAgenda, ActionKind.StealAgenda, ActionKind.JackOut,
        ActionKind.TrashAccessedCard, ActionKind.TrashProgram, ActionKind.ChooseBreachCandidate, ActionKind.UseAbility,
        ActionKind.RemoveTag, ActionKind.TrashTaggedResource,
    ];

    public LadderSavedGame Game { get; }

    public PlayerSide Side { get; }

    public string Entrant => Side == PlayerSide.Corp ? Game.Game.Corp : Game.Game.Runner;

    public IReadOnlyList<ReviewDecision> Decisions => [.. _decisions.Values.OrderBy(decision => decision.Sequence)];

    private GameReviewDossier(LadderSavedGame game, PlayerSide side)
    {
        Game = game;
        Side = side;
    }

    /// <summary>Replays the game, asking the side's bot again at each of its decisions with more than one option.</summary>
    public static async Task<GameReviewDossier> BuildAsync(LadderSavedGame saved, FullMatchDefinition definition, PlayerSide side,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);
        var dossier = new GameReviewDossier(saved, side);
        IGameAgent created = RatingLadder.CreateAgent(side == PlayerSide.Corp ? saved.Game.Corp : saved.Game.Runner, side, definition);
        OfflineAgent? agent = created is OfflineAgent offline
            ? new OfflineAgent(offline.Configuration, OfflinePolicies.Create(offline.Configuration),
                new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), null, captureDiagnostics: true)
            : null;
        GameState state = saved.Replay.InitialSnapshot.ToState();
        if (agent is not null) AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            dossier._states[recorded.Sequence] = state;
            PendingDecision pending = state.PendingDecision!;
            bool mine = state.GetPlayer(pending.Owner).Side == side;
            if (!mine && pending.LegalActions.FirstOrDefault(action => action.Id == recorded.Submission.ActionId) is { } theirs
                && OpponentShown.Contains(theirs.Kind))
                dossier._opponent[recorded.Sequence] = Describe(state, theirs);
            if (mine)
            {
                // The agent sees every own decision, as in the match it played, so its memory and commits stay in step.
                LegalAction chosen = pending.LegalActions.Single(action => action.Id == recorded.Submission.ActionId);
                IReadOnlyList<ReviewOption> options = [];
                bool diverged = false;
                if (agent is not null)
                {
                    ActionId choice = await agent.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime), cancellationToken)
                        .ConfigureAwait(false);
                    diverged = choice != chosen.Id;
                    options = [.. (agent.LastEvaluation?.Candidates ?? [])
                        .OrderByDescending(candidate => candidate.Utility.Priority).ThenByDescending(candidate => candidate.Utility.Score)
                        .Select(candidate => new ReviewOption(Describe(state, pending.LegalActions.Single(action => action.Id == candidate.ActionId)),
                            candidate.Utility.Priority, candidate.Utility.Score, candidate.Utility.Reason, candidate.ActionId == chosen.Id))];
                    if (diverged) AgentLifecycleHost.Discard(agent);
                }
                if (diverged) dossier._diverged.Add(recorded.Sequence);
                if (pending.LegalActions.Count > 1)
                    dossier._decisions[recorded.Sequence] = new ReviewDecision(recorded.Sequence, state.Turn.TurnNumber, pending.Kind.ToString(),
                        pending.Prompt, Describe(state, chosen), options, diverged);
            }
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) break;
            state = applied.State;
            if (agent is not null)
            {
                if (dossier._diverged.Contains(recorded.Sequence)) AgentLifecycleHost.Observe(agent, state, definition.Behavior);
                else AgentLifecycleHost.Accepted(agent, recorded.Submission, state, definition.Behavior);
            }
        }
        dossier._states[saved.Replay.Actions.Count + 1L] = state;
        return dossier;
    }

    /// <summary>The decision list the reviewer starts from: one line per decision, the options below it.</summary>
    public string DecisionLog(int optionsPerDecision = 4)
    {
        var text = new StringBuilder();
        int turn = -1;
        var opponent = new Queue<KeyValuePair<long, string>>(_opponent);
        PlayerSide other = Side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        foreach (ReviewDecision decision in Decisions)
        {
            while (opponent.TryPeek(out KeyValuePair<long, string> action) && action.Key < decision.Sequence)
                text.AppendLine(CultureInfo.InvariantCulture, $"  ({other} #{opponent.Dequeue().Key}: {action.Value})");
            if (decision.Turn != turn)
            {
                // Each turn opens with the board, so most decisions read without a position call.
                turn = decision.Turn;
                GameState state = _states[decision.Sequence];
                text.AppendLine(CultureInfo.InvariantCulture, $"== turn {turn}, {state.Turn.ActiveSide} active. {Summary(state)}");
            }
            text.Append(CultureInfo.InvariantCulture, $"#{decision.Sequence} turn {decision.Turn} {decision.Kind}: chose {decision.Chosen}");
            if (decision.Diverged) text.Append(" (the bot now chooses differently)");
            text.AppendLine();
            foreach (ReviewOption option in decision.Options.Take(optionsPerDecision))
                text.AppendLine(CultureInfo.InvariantCulture, $"    {(option.Chosen ? "*" : "-")} {option.Action}  [{option.Score}: {option.Reason}]");
        }
        while (opponent.TryDequeue(out KeyValuePair<long, string> action))
            text.AppendLine(CultureInfo.InvariantCulture, $"  ({other} #{action.Key}: {action.Value})");
        return text.ToString();
    }

    /// <summary>The <c>decision</c> tool: the prompt, every option with its score and reason.</summary>
    public string DecisionDetail(long sequence)
    {
        if (!_decisions.TryGetValue(sequence, out ReviewDecision? decision)) return $"No decision of the {Side} bot at #{sequence}.";
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"#{decision.Sequence} turn {decision.Turn} {decision.Kind} ({decision.Prompt}). The bot chose: {decision.Chosen}.");
        foreach (ReviewOption option in decision.Options)
            text.AppendLine(CultureInfo.InvariantCulture, $"{(option.Chosen ? "* chosen" : "- option")}: {option.Action} — priority {option.Priority}, score {option.Score}, reason {option.Reason}");
        return text.ToString();
    }

    /// <summary>The <c>position</c> tool: the whole board before the action at <paramref name="sequence"/>.</summary>
    public string Position(long sequence)
    {
        if (!_states.TryGetValue(sequence, out GameState? state)) return $"No position at #{sequence}.";
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Before #{sequence}: turn {state.Turn.TurnNumber}, {state.Turn.ActiveSide} active ({state.Turn.Phase}, {state.Turn.Step}).");
        PlayerState corp = state.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState runner = state.Players.Single(player => player.Side == PlayerSide.Runner);
        text.AppendLine(CultureInfo.InvariantCulture, $"Corp: {corp.Credits} credits, {corp.Clicks} clicks, {corp.AgendaPoints} agenda points; HQ: {Cards(state, ZoneKind.CorpHand)}; R&D: {state.GetZone(ZoneKind.CorpDeck).CardIds.Count} cards (top: {Top(state)}); Archives: {Cards(state, ZoneKind.CorpDiscard)}; score area: {Cards(state, ZoneKind.CorpScore)}.");
        foreach ((string name, string ice, string root) in Servers(state))
            text.AppendLine(CultureInfo.InvariantCulture, $"  {name}: ICE outermost first [{ice}] root [{root}]");
        text.AppendLine(CultureInfo.InvariantCulture, $"Runner: {runner.Credits} credits, {runner.Clicks} clicks, {runner.AgendaPoints} agenda points, {state.RulesState?.Players.FirstOrDefault(player => player.PlayerId == runner.Id)?.Tags ?? 0} tags; grip: {Cards(state, ZoneKind.RunnerGrip)}; rig: {string.Join(", ", state.GetZone(ZoneKind.RunnerRig).CardIds.Select(id => Installed(state, id)))}; heap: {state.GetZone(ZoneKind.RunnerHeap).CardIds.Count} cards; stack: {state.GetZone(ZoneKind.RunnerStack).CardIds.Count}; score area: {Cards(state, ZoneKind.RunnerScore)}.");
        if (state.RunState is { } run)
            text.AppendLine(CultureInfo.InvariantCulture, $"Run on {state.Servers.Single(server => server.Id == run.AttackedServer).Kind} at {run.Step}, ICE position {run.CurrentIceIndex} (-1 is the server){(run.Successful ? ", successful" : "")}.");
        return text.ToString();
    }

    /// <summary>One line of the board: credits, hand sizes, agenda points, and each server's ICE and root.</summary>
    private static string Summary(GameState state)
    {
        PlayerState corp = state.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState runner = state.Players.Single(player => player.Side == PlayerSide.Runner);
        string servers = string.Join("; ", Servers(state).Select(server => $"{server.Name} [{server.Ice}] root [{server.Root}]"));
        return string.Create(CultureInfo.InvariantCulture,
            $"Corp {corp.Credits} credits, {corp.AgendaPoints} points, HQ {state.GetZone(ZoneKind.CorpHand).CardIds.Count} cards; Runner {runner.Credits} credits, {runner.AgendaPoints} points, rig: {string.Join(", ", state.GetZone(ZoneKind.RunnerRig).CardIds.Select(id => Title(state, id)))}. Servers (ICE outermost first): {servers}.");
    }

    private static IEnumerable<(string Name, string Ice, string Root)> Servers(GameState state)
    {
        int remote = 0;
        foreach (ServerState server in state.Servers)
        {
            string name = server.Kind == ServerKind.Remote ? $"Remote {++remote}" : server.Kind.ToString();
            string ice = string.Join(", ", state.Zones.Where(zone => zone.ServerId == server.Id && zone.Kind == ZoneKind.ServerIce)
                .SelectMany(zone => zone.CardIds).Reverse().Select(id => Installed(state, id)));
            string root = string.Join(", ", state.Zones.Where(zone => zone.ServerId == server.Id && zone.Kind == ZoneKind.ServerRoot)
                .SelectMany(zone => zone.CardIds).Select(id => Installed(state, id)));
            if (server.Kind == ServerKind.Remote && ice.Length == 0 && root.Length == 0) continue;
            yield return (name, ice, root);
        }
    }

    /// <summary>The <c>card_text</c> tool: a card's printed text, type, costs and subtypes.</summary>
    public static string CardText(string titleOrCode)
    {
        NormalizedCardDefinition? card = CardDataResource.LoadEmbedded().Cards.FirstOrDefault(value =>
            value.Code == titleOrCode.Trim() || string.Equals(value.Title, titleOrCode.Trim(), StringComparison.OrdinalIgnoreCase));
        card ??= CardDataResource.LoadEmbedded().Cards.FirstOrDefault(value => value.Title.Contains(titleOrCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (card is null) return $"No card called '{titleOrCode}'.";
        var parts = new List<string> { $"{card.Title} ({card.Code}), {card.Side} {card.Faction} {card.Type}" };
        if (card.Subtypes.Count > 0) parts.Add("subtypes: " + string.Join(", ", card.Subtypes));
        if (card.Cost is { } cost) parts.Add($"cost {cost}");
        if (card.Strength is { } strength) parts.Add($"strength {strength}");
        if (card.TrashCost is { } trash) parts.Add($"trash cost {trash}");
        if (card.AgendaPoints is { } points) parts.Add($"{points} agenda points, advancement requirement {card.AdvancementRequirement}");
        if (card.MemoryCost is { } memory) parts.Add($"memory {memory}");
        if (card.Uniqueness) parts.Add("unique");
        return string.Join("; ", parts) + ". Text: " + card.Text;
    }

    public static bool CardExists(string title) =>
        CardDataResource.LoadEmbedded().Cards.Any(card => string.Equals(card.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Describe(GameState state, LegalAction action)
    {
        string description = action.Description;
        IEnumerable<CardInstanceId> cards = action.Parameters switch
        {
            PlayCardParameters play => [play.CardId],
            InstallCardParameters install => [install.CardId, .. install.RequiredReplacementCardIds, .. install.SelectedReplacementCardIds],
            AdvanceCardParameters advance => [advance.CardId],
            RezCardParameters rez => [rez.CardId],
            AbilityActionParameters ability => [ability.SourceCardId],
            ScoreAgendaParameters score => [score.CardId],
            BehaviorChoiceParameters choice => choice.CardIds,
            BreachCandidateParameters { CardId: { } card } => [card],
            RunCardPaymentParameters payment => [payment.CardId],
            IceSubroutineChoiceParameters { CardId: { } card } => [card],
            _ => [],
        };
        string where = action.Parameters switch
        {
            InstallCardParameters { CreateNewRemote: true } => " into a new remote",
            InstallCardParameters { ServerId: { } server } => $" into {ServerName(state, server)}",
            RunServerParameters run => $" on {ServerName(state, run.ServerId)}",
            _ => "",
        };
        string named = string.Join(", ", cards.Distinct().Select(id => Title(state, id)));
        return $"{action.Kind}{(named.Length > 0 ? $" {named}" : "")}{where} ({description})";
    }

    private static string ServerName(GameState state, ServerId id)
    {
        ServerState? server = state.Servers.FirstOrDefault(value => value.Id == id);
        if (server is null) return id.Value;
        if (server.Kind != ServerKind.Remote) return server.Kind.ToString();
        int number = state.Servers.Where(value => value.Kind == ServerKind.Remote).TakeWhile(value => value.Id != id).Count() + 1;
        return $"Remote {number}";
    }

    private static string Installed(GameState state, CardInstanceId id)
    {
        CardInstanceState card = state.GetCard(id);
        return $"{Title(state, id)}{(card.Rezzed ? " (rezzed)" : card.FaceUp ? "" : " (unrezzed)")}{(card.AdvancementCounters > 0 ? $" +{card.AdvancementCounters} advancement" : "")}";
    }

    private static string Cards(GameState state, ZoneKind zone) =>
        state.GetZone(zone).CardIds is { Count: > 0 } ids ? string.Join(", ", ids.Select(id => Title(state, id))) : "none";

    private static string Top(GameState state) => state.GetZone(ZoneKind.CorpDeck).CardIds is { Count: > 0 } deck ? Title(state, deck[0]) : "empty";

    private static string Title(GameState state, CardInstanceId id)
    {
        string code = state.GetCard(id).DefinitionId.Value;
        return CardDataResource.LoadEmbedded().Cards.FirstOrDefault(card => card.Code == code)?.Title ?? code;
    }
}
