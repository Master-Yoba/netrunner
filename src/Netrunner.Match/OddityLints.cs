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
        new("unique-replaces-own-copy", PlayerSide.Corp, "A unique card installed over its own copy."),
        new("unrezzed-ice-runner-profits", PlayerSide.Corp, "Approached ICE left unrezzed, though affordable, on a server the Runner has already run successfully twice."),
        new("advanceable-ice-idle", PlayerSide.Corp, "Advanceable ICE installed for three Corp turns or more without a counter."),
        new("archives-farmed", PlayerSide.Corp, "The Runner's third successful run on an Archives the Corp has left without ICE, with ICE in HQ to put there."),
        new("central-open-remotes-grow", PlayerSide.Corp, "HQ or R&D without ICE at the end of a Corp turn while two or more remotes hold cards."),
        new("counters-on-inert-card", PlayerSide.Corp, "Advancement counters placed on a card that neither is an agenda nor can be advanced (Seamless Launch on Palisade)."),
        new("own-agenda-trashed-by-install", PlayerSide.Corp, "The Corp trashed its own installed agenda by installing over it (a deliberate denial or hiding is possible; triage it)."),
        new("archives-shuffle-empty", PlayerSide.Corp, "An ability that shuffles Archives into R&D used with Archives empty (Spin Doctor)."),
        new("rd-rerun-top-seen", PlayerSide.Runner, "The Runner ran R&D again in a turn when its top card is the one it already accessed there."),
        new("runner-repeats-known-trap", PlayerSide.Runner, "The Runner accessed the same installed trap (a card that deals damage on access) a second time."),
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
            RunEnded(before, after, sequence, turn);
            if (before.Turn.ActiveSide == PlayerSide.Corp && after.Turn.ActiveSide != PlayerSide.Corp) CorpTurnEnded(after, sequence, turn);
        }

        public void End(GameState state)
        {
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

    /// <summary>Card titles for readable findings, when the card data is available.</summary>
    private static readonly Dictionary<string, string> CardTitles = LoadTitles();

    private static Dictionary<string, string> LoadTitles()
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (Netrunner.Cards.NormalizedCardDefinition card in Netrunner.Cards.CardDataResource.LoadEmbedded().Cards)
                titles[card.Code] = card.Title;
        }
        catch (InvalidOperationException)
        {
        }
        return titles;
    }
}
