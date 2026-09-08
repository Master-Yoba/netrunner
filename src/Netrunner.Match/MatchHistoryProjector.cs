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
        var moves = new List<string>();
        string? summary = null;
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
                    summary = $"{PlayerSide.Corp} rezzes {Name(rez.CardId.Value)}, paying {rez.CreditCost} credits.";
                    break;
                case IceRezzedEventPayload rez:
                    Reveal(rez.IceId.Value);
                    summary = $"{PlayerSide.Corp} rezzes {Name(rez.IceId.Value)}.";
                    break;
                case CardAccessedEventPayload access:
                    Reveal(access.CardId.Value);
                    summary = $"Runner accesses {Name(access.CardId.Value)}.";
                    break;
                case CardPlayStartedEventPayload play:
                    Reveal(play.CardId.Value);
                    summary = $"{play.Side} plays {Name(play.CardId.Value)}, paying {play.Cost} credits.";
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
                    summary = $"{install.Side} installs {Name(install.CardId.Value)} in {where}.";
                    if (moves.Count > 0)
                        summary = $"{install.Side} replaces a card in {where}: {string.Join("; ", moves)}; installs {Name(install.CardId.Value)}.";
                    break;
                case CardDerezzedEventPayload derez:
                    details.Add($"{Name(derez.CardId.Value)} is derezzed.");
                    break;
                case HostedCreditPoolChangedEventPayload pool when known.ContainsKey(pool.CardId.Value):
                    details.Add($"{Name(pool.CardId.Value)}: {pool.CountAfter} hosted credits remaining.");
                    break;
                case IceApproachedEventPayload approach:
                    details.Add($"Runner approaches {Name(approach.IceId.Value)}.");
                    break;
                case SubroutinesBrokenEventPayload broken:
                    details.Add($"Runner breaks {broken.BrokenCount} subroutine(s) with {Name(broken.BreakerId.Value)}.");
                    break;
                case RunInitiatedEventPayload run:
                    summary = $"{run.InitiatingSide} runs {ServerName(run.ServerId.Value)}.";
                    break;
                case RunEndedEventPayload run:
                    details.Add($"Run ends: {Words(run.Reason.ToString())}.");
                    break;
                case TurnStartedEventPayload turn:
                    details.Add($"{turn.ActiveSide} turn {turn.TurnNumber} begins.");
                    break;
                case TurnEndedEventPayload turn:
                    details.Add($"{turn.Side} turn {turn.TurnNumber} ends.");
                    break;
                case TurnClicksAllottedEventPayload clicks:
                    details.Add($"{clicks.Side} receives {clicks.Clicks} clicks.");
                    break;
                case TurnPhaseStartedEventPayload phase:
                    details.Add($"{phase.Side} begins {Words(phase.Step.ToString())}.");
                    break;
                case TurnPhaseEndedEventPayload phase:
                    details.Add($"{phase.Side} ends {Words(phase.Step.ToString())}.");
                    break;
                case TurnClicksLostEventPayload lost when lost.ClicksLost > 0:
                    details.Add($"{lost.Side} loses {lost.ClicksLost} click(s).");
                    break;
                case AgendaScoredEventPayload score:
                    Reveal(score.AgendaId.Value);
                    summary = $"Corp scores {Name(score.AgendaId.Value)}.";
                    break;
                case AgendaStolenEventPayload steal:
                    Reveal(steal.AgendaId.Value);
                    summary = $"Runner steals {Name(steal.AgendaId.Value)}.";
                    break;
                case ClickSpentEventPayload click:
                    details.Add($"{click.Side} spends a click; {click.ClicksRemaining} remaining.");
                    break;
                case CardDrawnEventPayload drawn:
                    details.Add($"{drawn.Side} draws a card.");
                    break;
                case TurnCardDiscardedEventPayload discarded:
                    // The discard is observable, but the physical card may
                    // remain hidden from this viewer. Name only an identity
                    // already admitted to the viewer-scoped cache.
                    details.Add($"{discarded.Side} discards {Name(discarded.CardId.Value)}.");
                    break;
                case AccessedCardTrashedEventPayload trashed:
                    Reveal(trashed.CardId.Value);
                    details.Add($"Runner trashes {Name(trashed.CardId.Value)} from access, paying {trashed.TrashCost} credits.");
                    break;
                case BehaviorCardsShuffledEventPayload shuffled:
                    // The typed event is identity-free public evidence. Keep
                    // the history wording free of card IDs and the payload's
                    // count, which is not needed to explain the action.
                    details.Add(ShuffleSummary(shuffled.DestinationZoneId));
                    break;
                case CreditsAdjustedEventPayload credits:
                    PlayerSide side = state.GetPlayer(credits.Target).Side;
                    details.Add($"{side}: {(credits.Delta >= 0 ? "+" : "")}{credits.Delta} credits ({credits.CreditsAfter} total).");
                    break;
                case DamageDealtEventPayload damage:
                    details.Add($"Damage: {damage.Amount} {Words(damage.Kind.ToString())}.");
                    break;
                case ServerApproachedEventPayload approached:
                    details.Add($"Runner reaches {ServerName(approached.ServerId.Value)}.");
                    break;
                case RunMovedEventPayload moved:
                    details.Add("Runner advances through the run.");
                    break;
                case RunSuccessfulEventPayload successful:
                    summary = $"Runner completes a successful run on {ServerName(successful.ServerId.Value)}.";
                    break;
            }
        }
        if (!installed) details.AddRange(moves);
        if (summary is null && events.Count == 0 && kind == ActionKind.InstallCard)
            return Array.Empty<MatchHistoryEntry>(); // Nested staging did not install anything.
        summary ??= kind switch
        {
            ActionKind.PassPriority => $"{actor} passes priority.",
            ActionKind.GainCredit => $"{actor} spends a click to gain a credit.",
            ActionKind.DrawCard => $"{actor} spends a click to draw a card.",
            ActionKind.NoAccessAction => ContinueAccessSummary(),
            ActionKind.UseAbility when action.Parameters is AbilityActionParameters ability => $"{actor} uses {Name(ability.SourceCardId.Value)}.",
            ActionKind.EndTurn => $"{actor} ends their turn.",
            ActionKind.ChooseNumber when before.Turn.Phase == TurnPhase.Setup => $"{actor} resolves their opening-hand choice.",
            _ => $"{actor}: {Words(kind.ToString())}.",
        };
        if (after.Outcome.IsTerminal)
            details.Add($"Match complete: {after.Outcome.Winner} wins ({after.Outcome.Reason}).");

        if (kind == ActionKind.PassPriority
            && string.Equals(summary, $"{actor} passes priority.", StringComparison.Ordinal))
        {
            if (details.Count == 0)
                return Array.Empty<MatchHistoryEntry>();

            // A pass can also complete a timing window and emit a draw, turn,
            // or run transition. Promote the first meaningful effect so the
            // entry remains useful without retaining the pass wording.
            summary = details[0];
        }

        return [new MatchHistoryEntry($"action-{state.ActionSequence}", state.ActionSequence,
            before.Turn.TurnNumber ?? 0, actor, summary, details.AsReadOnly())
        {
            Cards = mentioned.Values.Select((card, index) => card with
            {
                Id = $"history-{state.ActionSequence}-{index}", HostCardId = null,
                FirstXUses = card.FirstXUses
                    .Select(use => use with { SourceCardId = $"history-{state.ActionSequence}-{index}" })
                    .ToArray(),
            }).ToArray(),
        }];

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
            .Concat(new[] { view.CorpIdentity, view.RunnerIdentity }.OfType<CardViewModel>())
            .Concat(runCards.OfType<CardViewModel>());
    }

    private static string Words(string value) => string.Concat(value.Select((c, i) =>
        i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
