using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Trusted coordinator adapter. Raw events are inspected here, never forwarded to the
/// history UI. Names start from the previous viewer observation and are admitted from
/// the new observation only at an explicit public reveal. No hidden identity lookup.
/// </summary>
internal sealed class MatchHistoryProjector
{
    private readonly Dictionary<string, string> _serverNames = new(StringComparer.Ordinal);
    private int _remoteNumber;

    public IReadOnlyList<MatchHistoryEntry> Project(MatchViewModel before, MatchViewModel after,
        GameState state, IReadOnlyList<EngineEvent> events, PlayerSide actor, LegalAction action)
    {
        ActionKind kind = action.Kind;
        foreach (ServerViewModel server in before.Servers.Concat(after.Servers))
        {
            if (!_serverNames.ContainsKey(server.Id))
                _serverNames[server.Id] = server.Kind == ServerKind.Remote ? $"Remote {++_remoteNumber}" : server.Kind switch
                {
                    ServerKind.Headquarters => "HQ",
                    ServerKind.RAndD => "R&D",
                    ServerKind.Archives => "Archives",
                    _ => "server",
                };
        }
        var known = Cards(before).Where(card => card.IsKnown).GroupBy(card => card.Id)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var newlyVisible = Cards(after).Where(card => card.IsKnown).GroupBy(card => card.Id)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var mentioned = new Dictionary<string, CardViewModel>(StringComparer.Ordinal);
        var details = new List<string>();
        // The side each detail describes, parallel to details. History groups
        // entries by subject, not by the side whose action caused the events.
        var detailSubjects = new List<PlayerSide?>();
        var moves = new List<string>();
        string? summary = null;
        PlayerSide? summarySubject = null;
        // A card effect resolved by this action, promoted when nothing else summarizes it.
        string? effect = null;
        bool installed = events.Any(e => e.Payload is CardInstalledEventPayload);
        foreach (EngineEvent entry in events)
        {
            switch (entry.Payload)
            {
                case CardRezzedEventPayload rez:
                    Reveal(rez.CardId.Value);
                    // Both typed rez payloads describe Corp cards. The action
                    // actor may be Runner when a run/ability causes an
                    // automatic Corp rez, so do not attribute this fact to
                    // the submitted action owner.
                    Summary($"{PlayerSide.Corp} rezzes {Name(rez.CardId.Value)}, paying {rez.CreditCost} credits.", PlayerSide.Corp);
                    break;
                case IceRezzedEventPayload rez:
                    Reveal(rez.IceId.Value);
                    Summary($"{PlayerSide.Corp} rezzes {Name(rez.IceId.Value)}.", PlayerSide.Corp);
                    break;
                case CardAccessedEventPayload access:
                    Reveal(access.CardId.Value);
                    Summary($"Runner accesses {Name(access.CardId.Value)}.", PlayerSide.Runner);
                    break;
                case CardPlayStartedEventPayload play:
                    Reveal(play.CardId.Value);
                    Summary($"{play.Side} plays {Name(play.CardId.Value)}, paying {play.Cost} credits.", play.Side);
                    break;
                case CardMovedEventPayload move:
                    ZoneKind? destinationKind = ZoneKindFor(move.ToZoneId, move.ToZoneKind);
                    // Public knowledge of a card is not a tracker through private movement,
                    // including a later draw/trash in this same accepted action.
                    if (destinationKind is null || RetiresKnowledge(destinationKind.Value, before.Viewer))
                        known.Remove(move.CardId.Value);
                    if (destinationKind is ZoneKind.CorpDiscard or ZoneKind.RunnerHeap)
                    {
                        string pile = destinationKind == ZoneKind.CorpDiscard ? "Archives" : "Heap";
                        // A hidden discard stays unnamed even if a later observation reveals it.
                        moves.Add($"{Name(move.CardId.Value)} → {pile}");
                    }
                    break;
                case CardInstalledEventPayload install:
                    // Runner installs become public; Corp installs do not reveal their identity.
                    if (install.Side == PlayerSide.Runner) Reveal(install.CardId.Value);
                    string where = ZoneName(install.ToZoneId);
                    Summary($"{install.Side} installs {Name(install.CardId.Value)} in {where}.", install.Side);
                    if (moves.Count > 0)
                        Summary($"{install.Side} replaces a card in {where}: {string.Join("; ", moves)}; installs {Name(install.CardId.Value)}.", install.Side);
                    break;
                case CardDerezzedEventPayload derez:
                    Detail($"{Name(derez.CardId.Value)} is derezzed.", PlayerSide.Corp);
                    break;
                case BehaviorCardAdvancedEventPayload advanced:
                    // Advancement counters are public, as in AdvanceSummary.
                    string placed = $"Corp places {advanced.Amount} advancement {Counters(advanced.Amount)} on "
                        + $"{Name(advanced.CardId.Value)}{Where(advanced.CardId.Value)} ({advanced.AdvancementCounters} total).";
                    effect ??= placed;
                    Detail(placed, PlayerSide.Corp);
                    break;
                case HostedCreditPoolChangedEventPayload pool when known.ContainsKey(pool.CardId.Value):
                    Detail($"{Name(pool.CardId.Value)}: {pool.CountAfter} hosted credits remaining.", null);
                    break;
                case IceApproachedEventPayload approach:
                    Detail($"Runner approaches {Name(approach.IceId.Value)}.", PlayerSide.Runner);
                    break;
                case SubroutinesBrokenEventPayload broken:
                    Detail($"Runner breaks {broken.BrokenCount} subroutine(s) with {Name(broken.BreakerId.Value)}.", PlayerSide.Runner);
                    break;
                case RunInitiatedEventPayload run:
                    Summary($"{run.InitiatingSide} runs {ServerName(run.ServerId.Value)}.", run.InitiatingSide);
                    break;
                case RunEndedEventPayload run:
                    Detail($"Run ends: {Words(run.Reason.ToString())}.", PlayerSide.Runner);
                    break;
                case TurnStartedEventPayload turn:
                    Detail($"{turn.ActiveSide} turn {turn.TurnNumber} begins.", turn.ActiveSide);
                    break;
                case TurnEndedEventPayload turn:
                    Detail($"{turn.Side} turn {turn.TurnNumber} ends.", turn.Side);
                    break;
                case TurnClicksAllottedEventPayload clicks:
                    Detail($"{clicks.Side} receives {clicks.Clicks} clicks.", clicks.Side);
                    break;
                case TurnPhaseStartedEventPayload phase:
                    Detail($"{phase.Side} begins {Words(phase.Step.ToString())}.", phase.Side);
                    break;
                case TurnPhaseEndedEventPayload phase:
                    Detail($"{phase.Side} ends {Words(phase.Step.ToString())}.", phase.Side);
                    break;
                case TurnClicksLostEventPayload lost when lost.ClicksLost > 0:
                    Detail($"{lost.Side} loses {lost.ClicksLost} click(s).", lost.Side);
                    break;
                case AgendaScoredEventPayload score:
                    Reveal(score.AgendaId.Value);
                    Summary($"Corp scores {Name(score.AgendaId.Value)}.", PlayerSide.Corp);
                    break;
                case AgendaStolenEventPayload steal:
                    Reveal(steal.AgendaId.Value);
                    Summary($"Runner steals {Name(steal.AgendaId.Value)}.", PlayerSide.Runner);
                    break;
                case ClickSpentEventPayload click:
                    Detail($"{click.Side} spends a click; {click.ClicksRemaining} remaining.", click.Side);
                    break;
                case CardDrawnEventPayload drawn:
                    Detail($"{drawn.Side} draws a card.", drawn.Side);
                    break;
                case TurnCardDiscardedEventPayload discarded:
                    // The discard is observable, but the physical card may
                    // remain hidden from this viewer. Name only an identity
                    // already admitted to the viewer-scoped cache.
                    Detail($"{discarded.Side} discards {Name(discarded.CardId.Value)}.", discarded.Side);
                    break;
                case AccessedCardTrashedEventPayload trashed:
                    Reveal(trashed.CardId.Value);
                    Detail($"Runner trashes {Name(trashed.CardId.Value)} from access, paying {trashed.TrashCost} credits.", PlayerSide.Runner);
                    break;
                case BehaviorCardsShuffledEventPayload shuffled:
                    // The typed event is identity-free public evidence. Keep
                    // the history wording free of card IDs and the payload's
                    // count, which is not needed to explain the action.
                    Detail(ShuffleSummary(shuffled.DestinationZoneId), ShuffleSubject(shuffled.DestinationZoneId));
                    break;
                case CreditsAdjustedEventPayload credits:
                    PlayerSide side = state.GetPlayer(credits.Target).Side;
                    Detail(credits.Delta >= 0
                        ? $"{side} gains {credits.Delta} {Credits(credits.Delta)} ({credits.CreditsAfter} total)."
                        : $"{side} loses {-credits.Delta} {Credits(-credits.Delta)} ({credits.CreditsAfter} total).", side);
                    break;
                case DamageDealtEventPayload damage:
                    Detail($"Runner takes {damage.Amount} {Words(damage.Kind.ToString())} damage.", PlayerSide.Runner);
                    break;
                case ServerApproachedEventPayload approached:
                    Detail($"Runner reaches {ServerName(approached.ServerId.Value)}.", PlayerSide.Runner);
                    break;
                case RunMovedEventPayload moved:
                    Detail("Runner advances through the run.", PlayerSide.Runner);
                    break;
                case RunSuccessfulEventPayload successful:
                    Summary($"Runner completes a successful run on {ServerName(successful.ServerId.Value)}.", PlayerSide.Runner);
                    break;
            }
        }
        if (!installed)
        {
            foreach (string move in moves) Detail(move, null);
        }
        if (summary is null && events.Count == 0 && kind == ActionKind.InstallCard)
            return Array.Empty<MatchHistoryEntry>(); // Nested staging did not install anything.
        if (summary is null && effect is not null)
        {
            int index = details.IndexOf(effect);
            details.RemoveAt(index);
            detailSubjects.RemoveAt(index);
            Summary(effect, PlayerSide.Corp);
        }
        if (summary is null)
        {
            summarySubject = actor;
            summary = kind switch
            {
                ActionKind.PassPriority => $"{actor} passes priority.",
                ActionKind.GainCredit => $"{actor} spends a click to gain a credit.",
                ActionKind.DrawCard => $"{actor} spends a click to draw a card.",
                ActionKind.NoAccessAction => ContinueAccessSummary(),
                ActionKind.UseAbility when action.Parameters is AbilityActionParameters ability => $"{actor} uses {Name(ability.SourceCardId.Value)}.",
                ActionKind.EndTurn => $"{actor} ends their turn.",
                ActionKind.ChooseNumber when before.Turn.Phase == TurnPhase.Setup => $"{actor} resolves their opening-hand choice.",
                ActionKind.AdvanceCard or ActionKind.AdvanceAgenda when action.Parameters is AdvanceCardParameters advance
                    => AdvanceSummary(advance.CardId.Value),
                ActionKind.PumpBreaker when action.Parameters is RunBreakerParameters pump => $"{actor} boosts {Name(pump.BreakerId.Value)}.",
                ActionKind.TrashTaggedResource when action.Parameters is TrashTaggedResourceParameters trash
                    => $"{actor} trashes {Name(trash.CardId.Value)}.",
                ActionKind.JackOut => $"{actor} jacks out.",
                ActionKind.ContinueRun => $"{actor} continues the run.",
                ActionKind.RemoveTag => $"{actor} removes a tag.",
                ActionKind.PurgeVirusCounters => $"{actor} purges virus counters.",
                ActionKind.UseTrigger => $"{actor} uses an optional effect.",
                ActionKind.DeclineTrigger => $"{actor} declines an optional effect.",
                ActionKind.OrderTriggers => $"{actor} orders simultaneous effects.",
                ActionKind.ChooseBreachCandidate => $"{actor} chooses the next card to access.",
                ActionKind.ChooseCard or ActionKind.ChooseBehavior or ActionKind.ChooseNumber
                    when before.PlayArea.FirstOrDefault(card => card.IsKnown) is { } resolving
                    => $"{actor} makes a choice for {Name(resolving.Id)}.",
                ActionKind.ChooseCard or ActionKind.ChooseBehavior or ActionKind.ChooseNumber => $"{actor} makes a choice.",
                _ => $"{actor}: {Words(kind.ToString())}.",
            };
        }
        if (after.Outcome.IsTerminal && after.Outcome.Winner is { } winner)
            Detail($"Match complete: {winner} wins ({after.Outcome.Reason}).", winner);

        if (kind == ActionKind.PassPriority
            && string.Equals(summary, $"{actor} passes priority.", StringComparison.Ordinal))
        {
            if (details.Count == 0)
                return Array.Empty<MatchHistoryEntry>();

            // A pass can also complete a timing window and emit a draw, turn,
            // or run transition. Promote the first meaningful effect so the
            // entry remains useful without retaining the pass wording.
            summary = details[0];
            summarySubject = detailSubjects[0] ?? actor;
        }

        return [new MatchHistoryEntry($"action-{state.ActionSequence}", state.ActionSequence,
            before.Turn.TurnNumber ?? 0, summarySubject ?? actor, summary, details.AsReadOnly())
        {
            Cards = mentioned.Values.Select((card, index) => card with
            {
                Id = $"history-{state.ActionSequence}-{index}", HostCardId = null,
                FirstXUses = card.FirstXUses
                    .Select(use => use with { SourceCardId = $"history-{state.ActionSequence}-{index}" })
                    .ToArray(),
            }).ToArray(),
        }];

        void Summary(string text, PlayerSide subject)
        {
            summary = text;
            summarySubject = subject;
        }
        void Detail(string text, PlayerSide? subject)
        {
            details.Add(text);
            detailSubjects.Add(subject);
        }
        string AdvanceSummary(string id)
        {
            // Advancement counters are public; identity and requirement are not.
            CardViewModel? card = after.Servers.SelectMany(candidate => candidate.Root.Concat(candidate.Ice))
                .FirstOrDefault(candidate => candidate?.Id == id);
            string counters = card is null ? string.Empty
                : $" ({card.AdvancementCounters} advancement {Counters(card.AdvancementCounters)})";
            return $"{actor} advances {Name(id)}{Where(id)}{counters}.";
        }
        string Where(string id)
        {
            ServerViewModel? server = after.Servers.FirstOrDefault(candidate =>
                candidate.Root.Concat(candidate.Ice).Any(card => card?.Id == id));
            return server is null ? string.Empty : $" in {ServerName(server.Id)}";
        }
        void Reveal(string id)
        {
            if (newlyVisible.TryGetValue(id, out CardViewModel? card)) known[id] = card;
        }
        string Name(string id)
        {
            if (!known.TryGetValue(id, out CardViewModel? card)) return "a face-down card";
            mentioned[id] = card;
            return card.Title;
        }
        string ContinueAccessSummary()
        {
            CardViewModel? accessed = before.Run?.Access.Accessed is { Count: > 0 } accesses
                ? accesses[^1]
                : null;
            return accessed is null
                ? "Runner takes no action during access."
                : $"Runner continues after accessing {Name(accessed.Id)}.";
        }
        string ServerName(string id) => _serverNames.GetValueOrDefault(id, "server");
        string ZoneName(ZoneId id)
        {
            ZoneState? zone = state.Zones.FirstOrDefault(candidate => candidate.Id == id);
            if (zone is null)
            {
                return "the play area";
            }

            return zone.ServerId is { } server ? ServerName(server.Value) : zone.Kind switch
            {
                ZoneKind.RunnerRig => "the rig",
                _ => "the play area",
            };
        }

        ZoneKind? ZoneKindFor(ZoneId id, ZoneKind? provenance = null) =>
            provenance ?? state.Zones.FirstOrDefault(candidate => candidate.Id == id)?.Kind;

        PlayerSide? ShuffleSubject(ZoneId destinationId) => ZoneKindFor(destinationId) switch
        {
            ZoneKind.CorpDeck => PlayerSide.Corp,
            ZoneKind.RunnerStack => PlayerSide.Runner,
            _ => null,
        };

        string ShuffleSummary(ZoneId destinationId) => ZoneKindFor(destinationId) switch
        {
            ZoneKind.CorpDeck => "Corp shuffles R&D.",
            ZoneKind.RunnerStack => "Runner shuffles their stack.",
            _ => "Cards are shuffled.",
        };
    }

    private static bool RetiresKnowledge(ZoneKind destination, PlayerSide viewer) =>
        destination is ZoneKind.CorpDeck or ZoneKind.RunnerStack or ZoneKind.BehaviorSetAside
        || destination == ZoneKind.CorpHand && viewer != PlayerSide.Corp
        || destination == ZoneKind.RunnerGrip && viewer != PlayerSide.Runner;

    private static IEnumerable<CardViewModel> Cards(MatchViewModel view)
    {
        IEnumerable<CardViewModel?> runCards = view.Run is { } run
            ? new CardViewModel?[] { run.CurrentIce, run.Encounter?.Ice, run.InitiatingSource }
                .Concat(run.Access.Candidates)
                .Concat(run.Access.Chosen)
                .Concat(run.Access.Accessed)
            : Array.Empty<CardViewModel?>();
        return view.Servers.SelectMany(server => server.Ice.Concat(server.Root)).OfType<CardViewModel>()
            .Concat(view.RunnerRig).Concat(view.CorpHand.VisibleCards).Concat(view.RunnerHand.VisibleCards)
            .Concat(view.CorpScore).Concat(view.RunnerScore).Concat(view.CorpDiscard).Concat(view.RunnerHeap)
            .Concat(view.PlayArea)
            .Concat(new[] { view.CorpIdentity, view.RunnerIdentity }.OfType<CardViewModel>())
            .Concat(runCards.OfType<CardViewModel>());
    }

    private static string Credits(int amount) => amount == 1 ? "credit" : "credits";
    private static string Counters(int amount) => amount == 1 ? "counter" : "counters";

    private static string Words(string value) => string.Concat(value.Select((c, i) =>
        i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
