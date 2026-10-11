using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>One lint: an id, the side whose play it judges, and what it looks for.</summary>
public sealed record OddityLint(string Id, PlayerSide Side, string Description);

/// <summary>A play a lint flagged: which game, whose play, when, and what it saw.</summary>
public sealed record OddityFinding(string LintId, string GameId, string ContentId, PlayerSide Side, string Entrant, long Sequence, int Turn, string Detail);

/// <summary>
/// Oddity lints (Wave 4.5, P1): deterministic detectors for plays that look wrong, run over saved
/// self-play games. The ladder only measures wins, and both seats run the same heuristic, so a fault
/// only a different player would exploit never costs a game; these catch the faults we know about.
/// They read the full state (nothing is hidden offline), so they can check what a bot could not see,
/// such as whether R&amp;D's top card is the one the Runner already accessed.
/// </summary>
public static class OddityLints
{
    public static IReadOnlyList<OddityLint> All { get; } =
    [
        new("ice-on-empty-remote", PlayerSide.Corp, "ICE installed on a remote that still has nothing in its root when the Corp's turn ends, with no agenda or asset in HQ to put there."),
        new("upgrade-cannot-fire", PlayerSide.Corp, "An upgrade that only works when an agenda is scored from its server, installed in a central server."),
        new("unique-replaces-own-copy", PlayerSide.Corp, "A unique card installed over its own copy, or a unique upgrade beside its own copy, where only one can be active."),
        new("unrezzed-ice-runner-profits", PlayerSide.Corp, "Approached ICE left unrezzed, though affordable, on a server the Runner has already run successfully twice."),
        new("advanceable-ice-idle", PlayerSide.Corp, "Advanceable ICE installed for three Corp turns or more without a counter."),
        new("archives-farmed", PlayerSide.Corp, "The Runner's third successful run on an Archives the Corp has left without ICE, with ICE in HQ to put there."),
        new("central-open-remotes-grow", PlayerSide.Corp, "HQ or R&D without ICE at the end of a Corp turn while two or more remotes hold cards."),
        new("counters-on-inert-card", PlayerSide.Corp, "Advancement counters placed on a card that neither is an agenda nor can be advanced (Seamless Launch on Palisade)."),
        new("own-agenda-trashed-by-install", PlayerSide.Corp, "The Corp trashed its own installed agenda by installing over it (a deliberate denial or hiding is possible; triage it)."),
        new("archives-shuffle-empty", PlayerSide.Corp, "An ability that shuffles Archives into R&D used with Archives empty (Spin Doctor)."),
        new("rd-rerun-top-seen", PlayerSide.Runner, "The Runner ran R&D again in a turn when its top card is the one it already accessed there."),
        new("runner-repeats-known-trap", PlayerSide.Runner, "The Runner accessed the same installed trap (a card that deals damage on access) a second time."),
        // Bot plan H1: the strategy guide's mistakes (docs/STRATEGY_FUNDAMENTALS.md), each named by its rule.
        new("runner-banks-credits", PlayerSide.Runner, "R11: the Runner ended a turn on 10 credits or more without starting a run."),
        new("runner-credit-over-draw-early", PlayerSide.Runner, "R12: in the first three turns, a basic credit with 5 credits or more and 4 cards or fewer in the grip, where a draw was legal."),
        new("runner-runs-on-thin-grip", PlayerSide.Runner, "R18: a run started with 2 cards or fewer in the grip against a Corp whose deck deals damage."),
        new("runner-ends-turn-tagged", PlayerSide.Runner, "R19: the Runner ended a turn tagged."),
        new("runner-flatlined", PlayerSide.Runner, "R18: the Runner lost to a flatline."),
        new("remote-scored-uncontested", PlayerSide.Runner, "R06: the Corp scored an agenda from a remote the Runner never ran in the turns it was installed."),
        new("agenda-stolen-from-remote", PlayerSide.Corp, "C01, C02: the Runner stole an agenda from a remote."),
        new("agenda-score-delayed", PlayerSide.Corp, "C05: the Corp ended its turn with an agenda it could score."),
        new("draw-into-hq-flood", PlayerSide.Corp, "C13: a basic draw with three agendas or more in HQ."),
        new("agenda-unprotected-at-turn-end", PlayerSide.Corp, "C02: an agenda in a remote at the end of the Corp's turn with no rezzed ICE and none the Corp can afford to rez."),
        new("decorative-remote-ice", PlayerSide.Corp, "C08: a second or later ICE installed on a remote holding no agenda and no advanced card, with no agenda in HQ."),
        new("corp-keeps-flooded-opening", PlayerSide.Corp, "C23: the Corp kept a first hand holding three agendas or more where it could mulligan."),
    ];

    /// <summary>Replays one saved game and returns what the lints found, in game order.</summary>
    public static IReadOnlyList<OddityFinding> Run(LadderSavedGame saved, FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);
        var walk = new Walk(saved, definition);
        GameState state = saved.Replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            LegalAction? chosen = state.PendingDecision?.LegalActions.FirstOrDefault(action => action.Id == recorded.Submission.ActionId);
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) break;
            if (chosen is not null) walk.Step(state, chosen, recorded, applied.State);
            state = applied.State;
        }
        walk.End(state);
        return walk.Findings;
    }

    private sealed class Walk(LadderSavedGame saved, FullMatchDefinition definition)
    {
        public List<OddityFinding> Findings { get; } = [];
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
        private readonly List<(ServerId Server, CardInstanceId Ice)> _remoteIceThisTurn = [];
        private readonly Dictionary<ServerId, int> _successfulRuns = [];
        private readonly Dictionary<CardInstanceId, int> _iceInstalledTurn = [];
        private readonly HashSet<CardInstanceId> _accessedTraps = [];
        private (RunId Run, ServerId Server)? _declinedRez;
        private (int Turn, CardInstanceId Card)? _rdTopSeen;

        public void Step(GameState before, LegalAction chosen, ReplayAction recorded, GameState after)
        {
            PlayerSide actor = before.GetPlayer(recorded.Submission.Actor).Side;
            long sequence = recorded.Sequence;
            int turn = before.Turn.TurnNumber;

            switch (chosen.Parameters)
            {
                case InstallCardParameters install when actor == PlayerSide.Corp && install.FinalizeSelection:
                    InstalledByCorp(before, install, sequence, turn);
                    break;
                case AbilityActionParameters ability when actor == PlayerSide.Corp:
                    UsedCorpAbility(before, ability, sequence, turn);
                    break;
                case RunServerParameters run when actor == PlayerSide.Runner
                    && Server(before, run.ServerId)?.Kind == ServerKind.RAndD
                    && _rdTopSeen is { } seen && seen.Turn == turn && Top(before) == seen.Card:
                    Report("rd-rerun-top-seen", PlayerSide.Runner, sequence, turn, $"R&D's top card is still {Title(before, seen.Card)}, which the Runner accessed there this turn.");
                    break;
                case BreachCandidateParameters { CardId: { } accessed } when actor == PlayerSide.Runner && IsAccessTrap(before, accessed):
                    if (!_accessedTraps.Add(accessed))
                        Report("runner-repeats-known-trap", PlayerSide.Runner, sequence, turn, $"accessed {Title(before, accessed)} again.", once: false);
                    break;
            }

            // Approached ICE the Corp could have rezzed and didn't: the rez of the run's current ICE is an
            // ordinary card rez in the engine's windows.
            if (actor == PlayerSide.Corp && before.RunState is { CurrentIceId: { } approached } active && !IsRezOf(chosen, approached)
                && before.PendingDecision!.LegalActions.Any(action => IsRezOf(action, approached)))
                _declinedRez = (active.Id, active.AttackedServer);

            CountersOnInertCards(before, after, actor, sequence, turn);
            Guide(before, chosen, actor, after, sequence, turn);
            RunEnded(before, after, sequence, turn);
            if (before.Turn.ActiveSide == PlayerSide.Corp && after.Turn.ActiveSide != PlayerSide.Corp) CorpTurnEnded(after, sequence, turn);
        }

        public void End(GameState state)
        {
            if (state.Outcome is { Kind: GameOutcomeKind.Won, Winner: PlayerSide.Corp, Reason: GameWinReason.Flatline })
                Report("runner-flatlined", PlayerSide.Runner, state.ActionSequence, state.Turn.TurnNumber,
                    $"flatlined on turn {state.Turn.TurnNumber} at {Player(state, PlayerSide.Corp).AgendaPoints} to {Player(state, PlayerSide.Runner).AgendaPoints}"
                    + (_flatlinedBy is { } by ? $", {by}." : "."));
            foreach ((CardInstanceId ice, int installed) in _iceInstalledTurn)
            {
                CardInstanceState? card = state.Cards.FirstOrDefault(value => value.Id == ice);
                if (card is null || !IsInstalledIce(state, ice) || card.AdvancementCounters > 0) continue;
                // A turn number covers one Corp turn and one Runner turn.
                if ((state.Turn.TurnNumber - installed) >= 3)
                    Report("advanceable-ice-idle", PlayerSide.Corp, state.ActionSequence, state.Turn.TurnNumber,
                        $"{Title(state, ice)} sat unadvanced from turn {installed} to the end.", key: $"advanceable-ice-idle:{ice.Value}");
            }
        }

        private void InstalledByCorp(GameState before, InstallCardParameters install, long sequence, int turn)
        {
            CardInstanceState card = before.GetCard(install.CardId);
            if (!definition.Runtime.TryGet(card.DefinitionId, out CardRuntimeSpec spec)) return;
            ServerState? server = install.ServerId is { } id ? Server(before, id) : null;
            if (spec.CardClass == RuntimeCardClass.Ice)
            {
                if (install.CreateNewRemote || server?.Kind == ServerKind.Remote)
                    _remoteIceThisTurn.Add((install.ServerId ?? default, install.CardId));
                if (definition.Behavior.TryGet(card.DefinitionId, out CardBehaviorDefinition? behavior) && behavior.CanBeAdvanced)
                    _iceInstalledTurn[install.CardId] = turn;
            }
            if (spec.CardClass == RuntimeCardClass.Upgrade && server is { Kind: not ServerKind.Remote }
                && definition.Behavior.TryGet(card.DefinitionId, out CardBehaviorDefinition? upgrade)
                && upgrade.Triggers.Count > 0
                && upgrade.Triggers.All(trigger => trigger.Occurrence == OccurrenceKind.AgendaScoredOrStolen && trigger.RequireSourceOnAttackedServer))
                Report("upgrade-cannot-fire", PlayerSide.Corp, sequence, turn, $"{Title(before, install.CardId)} installed in {server.Kind}.", once: false);
            IEnumerable<CardInstanceId> replaced = install.RequiredReplacementCardIds.Concat(install.SelectedReplacementCardIds).Distinct();
            // LLM review (Wave 4.5 P2): a second unique upgrade beside the first gains nothing either (Malapert Data
            // Vault, Anoetic Void). A unique asset can: Spin Doctor's rez draws, after the first copy's ability is used.
            if (spec.Unique && spec.CardClass == RuntimeCardClass.Upgrade && before.Zones.Where(zone => zone.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce).SelectMany(zone => zone.CardIds)
                    .Any(other => other != install.CardId && !replaced.Contains(other) && before.GetCard(other).DefinitionId == card.DefinitionId))
                Report("unique-replaces-own-copy", PlayerSide.Corp, sequence, turn, $"{Title(before, install.CardId)} beside its own installed copy.", once: false);
            foreach (CardInstanceId old in replaced)
            {
                CardInstanceState oldCard = before.GetCard(old);
                if (!definition.Runtime.TryGet(oldCard.DefinitionId, out CardRuntimeSpec oldSpec)) continue;
                if (spec.Unique && oldCard.DefinitionId == card.DefinitionId)
                    Report("unique-replaces-own-copy", PlayerSide.Corp, sequence, turn, $"{Title(before, install.CardId)} over its own copy.", once: false);
                if (oldSpec.CardClass == RuntimeCardClass.Agenda)
                    Report("own-agenda-trashed-by-install", PlayerSide.Corp, sequence, turn,
                        $"{Title(before, install.CardId)} installed over {Title(before, old)} ({oldCard.AdvancementCounters} counters){(before.RunState is { } run ? $" during a run on {Server(before, run.AttackedServer)?.Kind}" : "")}.", once: false);
            }
        }

        private void UsedCorpAbility(GameState before, AbilityActionParameters ability, long sequence, int turn)
        {
            CardInstanceState source = before.GetCard(ability.SourceCardId);
            if (!definition.Behavior.TryGet(source.DefinitionId, out CardBehaviorDefinition? behavior)) return;
            BehaviorPaidAbility? declared = behavior.PaidAbilities.FirstOrDefault(value => value.Key == ability.AbilityKey);
            bool shufflesArchives = declared?.Instructions.Any(instruction => instruction.Kind == BehaviorInstructionKind.ShuffleIntoDeck
                && instruction.SourceZone == BehaviorZone.Archives) == true;
            if (shufflesArchives && before.GetZone(ZoneKind.CorpDiscard).CardIds.Count == 0)
                Report("archives-shuffle-empty", PlayerSide.Corp, sequence, turn, $"{Title(before, ability.SourceCardId)} with Archives empty.", once: false);
        }

        private void CountersOnInertCards(GameState before, GameState after, PlayerSide actor, long sequence, int turn)
        {
            if (actor != PlayerSide.Corp) return;
            foreach (CardInstanceState card in after.Cards)
            {
                CardInstanceState? previous = before.Cards.FirstOrDefault(value => value.Id == card.Id);
                if (previous is null || card.AdvancementCounters <= previous.AdvancementCounters) continue;
                if (!definition.Runtime.TryGet(card.DefinitionId, out CardRuntimeSpec spec) || spec.CardClass == RuntimeCardClass.Agenda) continue;
                if (definition.Behavior.TryGet(card.DefinitionId, out CardBehaviorDefinition? behavior) && behavior.CanBeAdvanced) continue;
                Report("counters-on-inert-card", PlayerSide.Corp, sequence, turn,
                    $"{card.AdvancementCounters - previous.AdvancementCounters} counters on {Title(after, card.Id)} ({spec.CardClass}).", once: false);
            }
        }

        private void RunEnded(GameState before, GameState after, long sequence, int turn)
        {
            if (before.RunState is not { } run || after.RunState is not null && after.RunState.Id == run.Id) return;
            bool successful = run.Successful || before.RunState.AccessState != RunAccessState.NotStarted;
            if (successful)
            {
                _successfulRuns[run.AttackedServer] = _successfulRuns.GetValueOrDefault(run.AttackedServer) + 1;
                if (_declinedRez is { } declined && declined.Run == run.Id && _successfulRuns[run.AttackedServer] >= 3)
                    Report("unrezzed-ice-runner-profits", PlayerSide.Corp, sequence, turn,
                        $"ICE on {Server(before, run.AttackedServer)?.Kind} left unrezzed; the Runner's run there #{_successfulRuns[run.AttackedServer]} succeeded.",
                        key: $"unrezzed-ice-runner-profits:{run.AttackedServer.Value}");
                if (Server(before, run.AttackedServer)?.Kind == ServerKind.Archives && _successfulRuns[run.AttackedServer] >= 3
                    && before.Zones.Where(zone => zone.ServerId == run.AttackedServer && zone.Kind == ZoneKind.ServerIce).All(zone => zone.CardIds.Count == 0))
                {
                    int iceInHq = before.GetZone(ZoneKind.CorpHand).CardIds.Count(id => definition.Runtime.TryGet(before.GetCard(id).DefinitionId, out CardRuntimeSpec spec)
                        && spec.CardClass == RuntimeCardClass.Ice);
                    // The first ICE on a server costs nothing to install; without ICE in HQ there was nothing to put there.
                    if (iceInHq > 0)
                        Report("archives-farmed", PlayerSide.Corp, sequence, turn,
                        $"successful run #{_successfulRuns[run.AttackedServer]} on an Archives without ICE ({iceInHq} ICE in HQ, {before.Players.Single(player => player.Side == PlayerSide.Corp).Credits} credits).");
                }
                if (Server(before, run.AttackedServer)?.Kind == ServerKind.RAndD && run.AccessedCards.Count == 1)
                    _rdTopSeen = (turn, run.AccessedCards[0]);
            }
            if (_declinedRez?.Run == run.Id) _declinedRez = null;
        }

        private void CorpTurnEnded(GameState after, long sequence, int turn)
        {
            // A remote protected before its agenda or asset goes in is a plan (a glacier stacks its scoring
            // remote first); with nothing in HQ to put there, it's ICE spent on nothing.
            bool rootInHand = after.GetZone(ZoneKind.CorpHand).CardIds.Any(id => definition.Runtime.TryGet(after.GetCard(id).DefinitionId, out CardRuntimeSpec spec)
                && spec.CardClass is RuntimeCardClass.Agenda or RuntimeCardClass.Asset);
            foreach ((ServerId id, CardInstanceId ice) in rootInHand ? [] : _remoteIceThisTurn)
            {
                ServerState? server = after.Servers.FirstOrDefault(value => value.Id == id)
                    ?? after.Servers.FirstOrDefault(value => value.Kind == ServerKind.Remote
                        && after.Zones.Any(zone => zone.ServerId == value.Id && zone.Kind == ZoneKind.ServerIce && zone.CardIds.Contains(ice)));
                if (server is null) continue;
                bool empty = after.Zones.Where(zone => zone.ServerId == server.Id && zone.Kind == ZoneKind.ServerRoot).All(zone => zone.CardIds.Count == 0);
                if (empty)
                    Report("ice-on-empty-remote", PlayerSide.Corp, sequence, turn, $"{Title(after, ice)} protects an empty remote.", once: false);
            }
            _remoteIceThisTurn.Clear();
            int remotes = after.Servers.Count(server => server.Kind == ServerKind.Remote
                && after.Zones.Any(zone => zone.ServerId == server.Id && zone.Kind == ZoneKind.ServerRoot && zone.CardIds.Count > 0));
            if (remotes < 2) return;
            foreach (ServerState central in after.Servers.Where(server => server.Kind is ServerKind.Headquarters or ServerKind.RAndD))
                if (after.Zones.Where(zone => zone.ServerId == central.Id && zone.Kind == ZoneKind.ServerIce).All(zone => zone.CardIds.Count == 0))
                    Report("central-open-remotes-grow", PlayerSide.Corp, sequence, turn, $"{central.Kind} has no ICE with {remotes} remotes in use.",
                        key: $"central-open-remotes-grow:{central.Kind}");
        }

        // Bot plan H1: the Runner turns counted so far, when each remote agenda went in, and every run's server and turn.
        private int _runnerTurns;
        private bool _ranThisTurn;
        private bool? _corpDealsDamage;
        private string? _flatlinedBy;
        private readonly Dictionary<CardInstanceId, (ServerId Server, int RunnerTurn)> _remoteAgendas = [];
        private readonly List<(ServerId Server, int RunnerTurn)> _runs = [];
        // Batch 4: each tracked remote agenda as the Runner's latest turn began: its counters and its server's ICE, rezzed of all.
        private readonly Dictionary<CardInstanceId, (int Counters, int Rezzed, int Ice)> _agendaAtRunnerTurn = [];
        private int _runnerCreditsAtTurn;
        private bool _snapshotPending;
        // Batch 6: the Corp's credits as the current run began.
        private int _corpCreditsAtRun;

        /// <summary>Bot plan H1: the strategy guide's mistakes (docs/STRATEGY_FUNDAMENTALS.md).</summary>
        private void Guide(GameState before, LegalAction chosen, PlayerSide actor, GameState after, long sequence, int turn)
        {
            if (before.Turn.ActiveSide != PlayerSide.Runner && after.Turn.ActiveSide == PlayerSide.Runner)
            {
                _runnerTurns++;
                _ranThisTurn = false;
                _snapshotPending = true;
            }
            // The review of #134: what the Runner faces is read at its first turn action, after its turn-begin income
            // (Smartware Distributor, Open Market) and the window before the turn, where ICE may still be rezzed.
            if (_snapshotPending && actor == PlayerSide.Runner && before.Turn.ActiveSide == PlayerSide.Runner
                && before.PendingDecision?.Kind == DecisionKind.TurnAction)
            {
                _snapshotPending = false;
                _runnerCreditsAtTurn = Player(before, PlayerSide.Runner).Credits;
                foreach ((CardInstanceId agenda, (ServerId server, _)) in _remoteAgendas)
                {
                    if (RootServer(before, agenda) is null) continue;
                    IReadOnlyList<CardInstanceId> ice = Zone(before, server, ZoneKind.ServerIce);
                    _agendaAtRunnerTurn[agenda] = (before.GetCard(agenda).AdvancementCounters, ice.Count(id => before.GetCard(id).Rezzed), ice.Count);
                }
            }
            if (before.RunState is null && after.RunState is { } started)
            {
                _runs.Add((started.AttackedServer, _runnerTurns));
                _ranThisTurn = true;
                _corpCreditsAtRun = Player(before, PlayerSide.Corp).Credits;
                int grip = before.GetZone(ZoneKind.RunnerGrip).CardIds.Count;
                _corpDealsDamage ??= before.Cards.Where(card => card.OwnerSide == PlayerSide.Corp).Select(card => card.DefinitionId).Distinct()
                    .Any(id => DealsDamage(definition.Behavior.TryGet(id, out CardBehaviorDefinition? behavior) ? behavior : null,
                        definition.Behavior.TryGetIce(id, out IceBehaviorSpec ice) ? ice : null));
                if (grip <= 2 && _corpDealsDamage == true)
                    Report("runner-runs-on-thin-grip", PlayerSide.Runner, sequence, turn,
                        $"a run on {Server(after, started.AttackedServer)?.Kind} with {grip} cards in the grip.", once: false);
            }
            if (before.Outcome.Kind == GameOutcomeKind.Ongoing && after.Outcome is { Reason: GameWinReason.Flatline })
                _flatlinedBy = FlatlineCause(before, chosen, actor);
            PlayerState runner = Player(before, PlayerSide.Runner);
            int hqAgendas = before.GetZone(ZoneKind.CorpHand).CardIds.Count(id => Class(before, id) == RuntimeCardClass.Agenda);
            switch (chosen.Kind)
            {
                case ActionKind.GainCredit when actor == PlayerSide.Runner && turn <= 3 && runner.Credits >= 5
                    && before.GetZone(ZoneKind.RunnerGrip).CardIds.Count <= 4
                    && before.PendingDecision!.LegalActions.Any(action => action.Kind == ActionKind.DrawCard):
                    Report("runner-credit-over-draw-early", PlayerSide.Runner, sequence, turn,
                        $"a credit on {runner.Credits} with {before.GetZone(ZoneKind.RunnerGrip).CardIds.Count} cards.", once: false);
                    break;
                case ActionKind.DrawCard when actor == PlayerSide.Corp && hqAgendas >= 3:
                    Report("draw-into-hq-flood", PlayerSide.Corp, sequence, turn, $"a draw with {hqAgendas} agendas in HQ.", once: false);
                    break;
                case ActionKind.ChooseNumber when actor == PlayerSide.Corp && before.PendingDecision!.Kind == DecisionKind.Setup
                    && chosen.Parameters is NumberChoiceParameters { Value: 0 } && hqAgendas >= 3
                    && before.PendingDecision.LegalActions.Any(action => action.Parameters is NumberChoiceParameters { Value: 1 }):
                    Report("corp-keeps-flooded-opening", PlayerSide.Corp, sequence, turn, $"kept a first hand with {hqAgendas} agendas.", once: false);
                    break;
                case ActionKind.EndTurn when actor == PlayerSide.Corp
                    && before.PendingDecision!.LegalActions.Any(action => action.Kind == ActionKind.ScoreAgenda):
                    Report("agenda-score-delayed", PlayerSide.Corp, sequence, turn, "the turn ended with a scorable agenda.", once: false);
                    break;
            }
            if (chosen.Parameters is ScoreAgendaParameters score && _remoteAgendas.TryGetValue(score.CardId, out var installed)
                && _runnerTurns > installed.RunnerTurn
                && !_runs.Any(run => run.Server == installed.Server && run.RunnerTurn > installed.RunnerTurn))
            {
                string ran = string.Join(" ", _runs.Where(run => run.RunnerTurn == _runnerTurns).Select(run => Server(before, run.Server)?.Kind.ToString() ?? "?"));
                string seen = _agendaAtRunnerTurn.TryGetValue(score.CardId, out var at)
                    ? $"; at the Runner's last turn: {_runnerCreditsAtTurn} credits, {at.Counters} counters, ICE {at.Rezzed}/{at.Ice} rezzed, ran {(ran.Length == 0 ? "nothing" : ran)}"
                    : "";
                Report("remote-scored-uncontested", PlayerSide.Runner, sequence, turn,
                    $"{Title(before, score.CardId)} scored after {_runnerTurns - installed.RunnerTurn} Runner turns without a run on its remote{seen}.", once: false);
            }
            // The review of #136: every agenda that reaches a remote's root, by an install action or a card's effect (Humanoid
            // Resources), from when it got there.
            foreach (ServerState remote in after.Servers.Where(server => server.Kind == ServerKind.Remote))
                foreach (CardInstanceId card in Zone(after, remote.Id, ZoneKind.ServerRoot))
                    if (!_remoteAgendas.ContainsKey(card) && Class(after, card) == RuntimeCardClass.Agenda)
                        _remoteAgendas[card] = (remote.Id, _runnerTurns);
            if (actor == PlayerSide.Corp && chosen.Parameters is InstallCardParameters { FinalizeSelection: true } install)
            {
                RuntimeCardClass? installing = Class(before, install.CardId);
                ServerState? into = RootServer(after, install.CardId) ?? (install.ServerId is { } id ? Server(after, id) : null);
                if (installing == RuntimeCardClass.Ice && into is { Kind: ServerKind.Remote } && hqAgendas == 0
                    && Zone(before, into.Id, ZoneKind.ServerIce).Count >= 1
                    && !Zone(before, into.Id, ZoneKind.ServerRoot).Any(id => Class(before, id) == RuntimeCardClass.Agenda
                        || before.GetCard(id).AdvancementCounters > 0))
                    Report("decorative-remote-ice", PlayerSide.Corp, sequence, turn,
                        $"{Title(before, install.CardId)} as ICE #{Zone(before, into.Id, ZoneKind.ServerIce).Count + 1} on a remote of {Zone(before, into.Id, ZoneKind.ServerRoot).Count} cards.",
                        once: false);
            }
            foreach (CardInstanceId stolen in after.GetZone(ZoneKind.RunnerScore).CardIds.Except(before.GetZone(ZoneKind.RunnerScore).CardIds))
                if (RootServer(before, stolen) is { Kind: ServerKind.Remote } remote)
                {
                    // Batch 6: how long it sat, and the ICE the Corp left unrezzed though it could pay for it as the run began.
                    IReadOnlyList<CardInstanceId> ice = Zone(before, remote.Id, ZoneKind.ServerIce);
                    int[] unrezzed = [.. ice.Where(id => !before.GetCard(id).Rezzed)
                        .Select(id => definition.Runtime.TryGet(before.GetCard(id).DefinitionId, out CardRuntimeSpec spec) ? spec.RezCost ?? 0 : 0).Order()];
                    int funds = _corpCreditsAtRun, affordable = 0;
                    foreach (int cost in unrezzed)
                        if (cost <= funds) { funds -= cost; affordable++; }
                    string sat = _remoteAgendas.TryGetValue(stolen, out var placed) ? $"; installed {_runnerTurns - placed.RunnerTurn} Runner turns before" : "";
                    Report("agenda-stolen-from-remote", PlayerSide.Corp, sequence, turn,
                        $"{Title(before, stolen)} ({before.GetCard(stolen).AdvancementCounters} counters) behind {ice.Count(id => before.GetCard(id).Rezzed)} rezzed of {ice.Count} ICE{sat}; the Corp had {_corpCreditsAtRun} credits as the run began, {unrezzed.Length} ICE left unrezzed, {affordable} of them affordable.",
                        once: false);
                }
            if (before.Turn.ActiveSide == PlayerSide.Runner && after.Turn.ActiveSide != PlayerSide.Runner)
            {
                PlayerState ended = Player(after, PlayerSide.Runner);
                if (ended.Credits >= 10 && !_ranThisTurn)
                    Report("runner-banks-credits", PlayerSide.Runner, sequence, turn, $"the turn ended on {ended.Credits} credits without a run.", once: false);
                if (Tags(after) > 0)
                    Report("runner-ends-turn-tagged", PlayerSide.Runner, sequence, turn, $"the turn ended with {Tags(after)} tags.", once: false);
            }
            if (before.Turn.ActiveSide == PlayerSide.Corp && after.Turn.ActiveSide != PlayerSide.Corp)
            {
                int credits = Player(after, PlayerSide.Corp).Credits;
                foreach (ServerState remote in after.Servers.Where(server => server.Kind == ServerKind.Remote))
                {
                    if (!Zone(after, remote.Id, ZoneKind.ServerRoot).Any(id => Class(after, id) == RuntimeCardClass.Agenda)) continue;
                    IReadOnlyList<CardInstanceId> ice = Zone(after, remote.Id, ZoneKind.ServerIce);
                    bool defended = ice.Any(id => after.GetCard(id).Rezzed)
                        || ice.Any(id => definition.Runtime.TryGet(after.GetCard(id).DefinitionId, out CardRuntimeSpec spec) && (spec.RezCost ?? 0) <= credits);
                    if (!defended)
                        Report("agenda-unprotected-at-turn-end", PlayerSide.Corp, sequence, turn,
                            $"an agenda behind {ice.Count} unrezzed ICE with {credits} credits.", once: false);
                }
            }
        }

        /// <summary>What was resolving when the flatline came: an operation in play, the ICE encountered, or the cards accessed.</summary>
        private static string FlatlineCause(GameState before, LegalAction chosen, PlayerSide actor)
        {
            var parts = new List<string> { $"{actor} {chosen.Kind}", $"grip {before.GetZone(ZoneKind.RunnerGrip).CardIds.Count}" };
            if (before.Zones.FirstOrDefault(zone => zone.Kind == ZoneKind.CorpPlayArea) is { CardIds.Count: > 0 } played)
                parts.Add("playing " + string.Join(" and ", played.CardIds.Select(id => Title(before, id))));
            if (before.RunState is { } run)
            {
                parts.Add($"run on {Server(before, run.AttackedServer)?.Kind}");
                if (run.CurrentIceId is { } ice) parts.Add("at " + Title(before, ice));
                if (run.AccessedCards.Count > 0) parts.Add("accessed " + string.Join(" and ", run.AccessedCards.Select(id => Title(before, id))));
                if (chosen.Parameters is BreachCandidateParameters { CardId: { } accessing }) parts.Add("accessing " + Title(before, accessing));
            }
            return string.Join(", ", parts);
        }

        private RuntimeCardClass? Class(GameState state, CardInstanceId id) =>
            definition.Runtime.TryGet(state.GetCard(id).DefinitionId, out CardRuntimeSpec spec) ? spec.CardClass : null;

        private static PlayerState Player(GameState state, PlayerSide side) => state.Players.Single(player => player.Side == side);

        private static int Tags(GameState state) =>
            state.RulesState?.Players.SingleOrDefault(player => player.PlayerId == Player(state, PlayerSide.Runner).Id)?.Tags ?? 0;

        private static IReadOnlyList<CardInstanceId> Zone(GameState state, ServerId server, ZoneKind kind) =>
            [.. state.Zones.Where(zone => zone.ServerId == server && zone.Kind == kind).SelectMany(zone => zone.CardIds)];

        private static ServerState? RootServer(GameState state, CardInstanceId id) => state.Zones
            .FirstOrDefault(zone => zone.Kind == ZoneKind.ServerRoot && zone.CardIds.Contains(id)) is { ServerId: { } server } ? Server(state, server) : null;

        private void Report(string lint, PlayerSide side, long sequence, int turn, string detail, bool once = true, string? key = null)
        {
            if (once && !_reported.Add(key ?? lint)) return;
            string entrant = side == PlayerSide.Corp ? saved.Game.Corp : saved.Game.Runner;
            Findings.Add(new OddityFinding(lint, saved.Game.Id, saved.Game.ContentId, side, entrant, sequence, turn, detail));
        }

        private bool IsAccessTrap(GameState state, CardInstanceId id) =>
            definition.Behavior.TryGet(state.GetCard(id).DefinitionId, out CardBehaviorDefinition? behavior)
            && behavior.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.CardAccessed
                && trigger.Instructions.Any(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage))
            && state.Zones.Any(zone => zone.Kind == ZoneKind.ServerRoot && zone.CardIds.Contains(id));

        private static bool IsRezOf(LegalAction action, CardInstanceId card) => action.Parameters switch
        {
            RezCardParameters rez => rez.CardId == card,
            RunIceParameters ice => action.Kind == ActionKind.RezIce && ice.IceId == card,
            _ => false,
        };

        private static bool IsInstalledIce(GameState state, CardInstanceId id) =>
            state.Zones.Any(zone => zone.Kind == ZoneKind.ServerIce && zone.CardIds.Contains(id));

        private static CardInstanceId? Top(GameState state) =>
            state.GetZone(ZoneKind.CorpDeck).CardIds is { Count: > 0 } deck ? deck[0] : null;

        private static ServerState? Server(GameState state, ServerId id) => state.Servers.FirstOrDefault(server => server.Id == id);

        private static string Title(GameState state, CardInstanceId id) =>
            CardTitles.TryGetValue(state.GetCard(id).DefinitionId.Value, out string? title) ? title : state.GetCard(id).DefinitionId.Value;
    }

    /// <summary>
    /// Bot plan H1 (<c>runner-runs-on-thin-grip</c>): whether a Corp card deals damage, by its behavior or, for native
    /// ICE (Karunā, Tithe, Diviner), by its ICE specification's subroutines, which are not card behaviors.
    /// </summary>
    internal static bool DealsDamage(CardBehaviorDefinition? behavior, IceBehaviorSpec? ice) =>
        behavior is not null && DealsDamage(behavior) || ice?.Subroutines.Any(subroutine => subroutine is DamageSubroutineSpec) == true;

    private static bool DealsDamage(CardBehaviorDefinition behavior) =>
        behavior.Instructions.Concat(behavior.Triggers.SelectMany(trigger => trigger.Instructions))
            .Concat(behavior.PaidAbilities.SelectMany(ability => ability.Instructions))
            .Any(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage
                || instruction.Choice?.Options.Any(option => option.Instructions.Any(inner => inner.Kind == BehaviorInstructionKind.DealDamage)) == true);

    /// <summary>Card titles for readable findings, when the card data is available.</summary>
    private static readonly Dictionary<string, string> CardTitles = LoadTitles();

    private static Dictionary<string, string> LoadTitles()
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (Netrunner.Cards.NormalizedCardDefinition card in Netrunner.Cards.CardDataResource.LoadEmbeddedPool().Cards)
                titles[card.Code] = card.Title;
        }
        catch (InvalidOperationException)
        {
        }
        return titles;
    }
}
