using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match.Regret;

/// <summary>
/// What became of a card the Corp installed in a remote: whether the run model said the Runner could get in at its next
/// turn (and the card's advancement and whether it was rezzed then), the Runner's runs on that server while it was there,
/// and where it went (scored, stolen, trashed, or still installed when the record ended).
/// </summary>
public sealed record RemoteFate(string Source, string Definition, bool? Breakable, int UnknownIce, int Runs, string Outcome, int Turns,
    int Advancement = 0, bool Rezzed = false, int RunsNextTurn = 0, RemoteRunView? View = null, int Ice = 0);

/// <summary>
/// The Runner's own view of running the remote at its next turn (Runner v28's diagnosis): its best run on the server and
/// the action it chose instead, each with the heuristic's score and reason.
/// </summary>
public sealed record RemoteRunView(int? RunScore, string? RunReason, int ChosenScore, string ChosenReason, string ChosenKind);

/// <summary>
/// The S1.5 follow-up's exposure count: does the bot Runner punish an agenda left in a remote? Over saved games, and over
/// the teacher's play-outs of a candidate install. With every root install followed (Runner v28's base rate), it also
/// says what a face-down card in a remote the Runner can get into turns out to be.
/// </summary>
public static class RemoteExposure
{
    public const string Scored = "scored";
    public const string Stolen = "stolen";
    public const string Trashed = "trashed";
    public const string Installed = "installed";

    /// <summary>
    /// Follows <paramref name="card"/> through <paramref name="states"/>, the states after its install in order: the
    /// Runner's runs on its server until it leaves, and where it went.
    /// </summary>
    public static RemoteFate Follow(string source, IEnumerable<GameState> states, CardInstanceId card, FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(states);
        var tracker = new Tracker(source, card, definition);
        foreach (GameState state in states)
            if (!tracker.See(state)) break;
        return tracker.Fate;
    }

    /// <summary>Follows one installed agenda state by state, keeping only what the fate needs.</summary>
    private sealed class Tracker(string source, CardInstanceId card, FullMatchDefinition definition)
    {
        private ServerId? _server;
        private string _definition = "";
        private RunId? _lastRun;
        private int _runs, _startTurn, _turns, _unknownIce, _advancement, _runsNextTurn, _ice, _nextTurn = -1;
        private bool? _breakable;
        private bool _rezzed;
        private string? _outcome;
        private bool _ended;

        public RemoteFate Fate => new(source, _definition, _breakable, _unknownIce, _runs,
            _outcome ?? (_server is null ? "never-installed" : _ended ? "installed-at-end" : Installed), _turns, _advancement, _rezzed, _runsNextTurn, View, _ice);

        /// <summary>The server, once the card is in it.</summary>
        public ServerId? Server => _server;

        /// <summary>Whether the last state seen was the Runner's next turn action, where the run model was asked.</summary>
        public bool AtNextTurn { get; private set; }

        public RemoteRunView? View { get; set; }

        /// <returns>Whether the agenda is still in its remote, so later states matter.</returns>
        public bool See(GameState state)
        {
            AtNextTurn = false;
            if (_outcome is not null) return false;
            _ended = state.Outcome.Kind != GameOutcomeKind.Ongoing;
            CardInstanceState? instance = state.Cards.FirstOrDefault(entry => entry.Id == card);
            if (instance is null)
            {
                _outcome = Trashed;
                return false;
            }
            ZoneState zone = state.GetZone(instance.ZoneId);
            if (_server is null)
            {
                // The install may take more than one decision (a replacement choice); it starts once the card is in a root.
                if (zone.Kind != ZoneKind.ServerRoot || zone.ServerId is not { } placed) return true;
                _server = placed;
                _definition = instance.DefinitionId.Value;
                _startTurn = state.Turn.TurnNumber;
            }
            _turns = state.Turn.TurnNumber - _startTurn;
            if (zone.Kind != ZoneKind.ServerRoot || zone.ServerId != _server)
            {
                _outcome = zone.Kind switch
                {
                    ZoneKind.CorpScore => Scored,
                    ZoneKind.RunnerScore => Stolen,
                    _ => Trashed,
                };
                return false;
            }
            if (state.RunState is { } run && run.Id != _lastRun)
            {
                _lastRun = run.Id;
                if (run.AttackedServer == _server && run.InitiatingSide == PlayerSide.Runner)
                {
                    _runs++;
                    if (state.Turn.TurnNumber == _nextTurn && state.Turn.ActiveSide == PlayerSide.Runner) _runsNextTurn++;
                }
            }
            // The run model's view at the Runner's first turn action after the install: can it get in with its clicks and credits?
            if (_breakable is null && state.RunState is null && state.PendingDecision is { Kind: DecisionKind.TurnAction } pending
                && state.GetPlayer(pending.Owner).Side == PlayerSide.Runner && state.Turn.ActiveSide == PlayerSide.Runner)
            {
                Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime);
                RunForecast forecast = RunModel.Forecast(observation, new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior),
                    _server.Value.Value, Math.Max(0, state.GetPlayer(pending.Owner).Clicks - 1));
                _breakable = forecast.CanGetIn;
                _unknownIce = forecast.UnknownIce;
                _advancement = instance.AdvancementCounters;
                _nextTurn = state.Turn.TurnNumber;
                _ice = state.Zones.Where(entry => entry.Kind == ZoneKind.ServerIce && entry.ServerId == _server).Sum(entry => entry.CardIds.Count);
                AtNextTurn = true;
                _rezzed = instance.Rezzed;
            }
            return !_ended;
        }
    }

    /// <summary>
    /// The card a Corp install action puts in a remote's root: an agenda, or with <paramref name="anyCard"/> any card;
    /// null otherwise.
    /// </summary>
    public static CardInstanceId? RootInstalled(GameState state, LegalAction action, ICardEvaluationCatalog catalog, bool anyCard = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(catalog);
        if (action.Parameters is not InstallCardParameters { DestinationKind: InstallDestinationKind.ServerRoot } install) return null;
        if (install.ServerId is { } existing && state.Servers.FirstOrDefault(server => server.Id == existing)?.Kind != ServerKind.Remote) return null;
        CardInstanceState? card = state.Cards.FirstOrDefault(entry => entry.Id == install.CardId);
        return card is not null && (anyCard || catalog.TryGet(card.DefinitionId, out CardEvaluationDescriptor descriptor) && descriptor.Agenda is not null)
            ? install.CardId : null;
    }

    /// <summary>
    /// Every agenda (or with <paramref name="anyCard"/>, every card) the Corp installed in a remote in a suite game, followed
    /// to its end. With <paramref name="explainWith"/> (a Runner version), that Runner, having watched the game, says at the
    /// Runner's next turn how it scores a run on the remote and what it would choose instead.
    /// </summary>
    public static IReadOnlyList<RemoteFate> FromGame(RegretGame game, bool anyCard = false, string? explainWith = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        FullMatchDefinition definition = RegretGames.Definition(game);
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        OfflineAgent? runner = explainWith is null ? null : SavedDecision.Agent(definition, PlayerSide.Runner, explainWith, diagnostics: true);
        var fates = new List<RemoteFate>();
        var active = new List<Tracker>();
        int index = 0;
        foreach (GameState state in GamePositions(game, definition))
        {
            if (runner is not null) AgentLifecycleHost.Observe(runner, state, definition.Behavior);
            active.RemoveAll(tracker =>
            {
                bool going = tracker.See(state);
                if (runner is not null && tracker.AtNextTurn) tracker.View = Explain(runner, state, tracker.Server!.Value, definition);
                if (going) return false;
                fates.Add(tracker.Fate);
                return true;
            });
            if (index < game.Choices.Length && state.PendingDecision is { } pending && state.GetPlayer(pending.Owner).Side == PlayerSide.Corp
                && RootInstalled(state, pending.LegalActions[game.Choices[index]], catalog, anyCard) is { } card)
                active.Add(new Tracker($"{game.Id}#{state.ActionSequence + 1}", card, definition));
            index++;
        }
        fates.AddRange(active.Select(tracker => tracker.Fate));
        return fates;
    }

    /// <summary>
    /// The teacher's play-outs of a group that installs an agenda in a remote, at a root: the teacher (the latest Hard bot
    /// of the side, having watched the game) plays the group's first action on its samples, then the heuristic, as the
    /// one-step value does; each play-out's agenda is followed. Empty when the group installs no agenda in a remote.
    /// </summary>
    public static IReadOnlyList<RemoteFate> FromRoot(RegretGame game, RegretRoot root, string group, TeacherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(settings);
        FullMatchDefinition definition = RegretGames.Definition(game);
        var teacher = RatingLadder.CreateAgent(RegretLabels.DefaultTeacher, root.Side, definition) as OfflineAgent
            ?? throw new InvalidOperationException("The Hard bot is not an offline agent.");
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        var fates = new List<RemoteFate>();
        foreach (RegretPosition position in RegretGames.Positions(game, definition))
        {
            AgentLifecycleHost.Observe(teacher, position.State, definition.Behavior);
            if (position.Sequence != root.Sequence) continue;
            var action = new ActionId(root.Candidates.Single(candidate => candidate.Group == group).ActionIds[0]);
            LegalAction legal = position.State.PendingDecision!.LegalActions.Single(entry => entry.Id == action);
            if (RootInstalled(position.State, legal, catalog) is not { } card) break;
            var trackers = new SortedDictionary<int, Tracker>();
            RolloutTeacher.Trace(teacher, ObservationBuilder.Build(position.State, root.Side, definition.Behavior, definition.Runtime), action,
                settings with { Seed = settings.Seed ^ RegretRoots.StableHash(root.Id) }, (sample, state) =>
                {
                    if (!trackers.TryGetValue(sample, out Tracker? tracker)) trackers[sample] = tracker = new Tracker(root.Id, card, definition);
                    tracker.See(state);
                });
            fates.AddRange(trackers.Values.Select(tracker => tracker.Fate));
            break;
        }
        return fates;
    }

    private static RemoteRunView Explain(OfflineAgent runner, GameState state, ServerId server, FullMatchDefinition definition)
    {
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime);
        ActionId chosen = runner.ChooseActionAsync(observation).AsTask().GetAwaiter().GetResult();
        AgentEvaluation evaluation = runner.LastEvaluation!;
        runner.DiscardDecision();
        var actions = state.PendingDecision!.LegalActions.ToDictionary(action => action.Id);
        AgentCandidate? run = evaluation.Candidates
            .Where(candidate => actions[candidate.ActionId].Parameters is RunServerParameters target && target.ServerId == server)
            .OrderByDescending(candidate => candidate.Utility.Priority).ThenByDescending(candidate => candidate.Utility.Score).FirstOrDefault();
        AgentCandidate pick = evaluation.Candidates.First(candidate => candidate.ActionId == chosen);
        return new RemoteRunView(run?.Utility.Score, run?.Utility.Reason, pick.Utility.Score, pick.Utility.Reason, actions[chosen].Kind.ToString());
    }

    /// <summary>The state before every decision of a game, then the final state.</summary>
    private static IEnumerable<GameState> GamePositions(RegretGame game, FullMatchDefinition definition)
    {
        GameState state = definition.Start().State;
        foreach (int choice in game.Choices)
        {
            yield return state;
            PendingDecision pending = state.PendingDecision!;
            state = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pending.LegalActions[choice].Id),
                definition.Behavior, definition.Runtime).State;
        }
        yield return state;
    }
}
