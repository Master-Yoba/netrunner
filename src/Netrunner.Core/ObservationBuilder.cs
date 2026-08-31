namespace Netrunner.Core;

/// <summary>
/// The only production path from authoritative <see cref="GameState"/> to the
/// agent/client <see cref="Observation"/> DTO (M2 "Observation boundary").
///
/// The builder projects an explicit, viewer-scoped view: the viewer's own
/// private zones, every card the viewer may know (own cards or faceup cards
/// in public zones), public board/run/encounter data, and the pending
/// decision's legal actions only when the viewer owns that decision. During a
/// live breach, the cards the Runner chose to access are additionally visible
/// to both viewers for the remainder of that breach only (CR 26.03 7.3.1a).
/// Archives discard cards are flipped faceup authoritatively at breach start
/// (CR 26.03 7.5.2), so that visibility persists after the run. It never
/// serializes the state and never exposes the opponent's grip/stack/hand/deck,
/// face-down ICE to the non-owner, the Corp's facedown Archives cards to the
/// Runner (CR 26.03 4.4.6c: the Corp alone may look through them), hidden
/// breach candidates, RNG or allocator state, event/allocator sequences,
/// schemas, or an opponent's decision revision. The viewer-owned decision
/// revision is exposed only through the typed <see cref="Observation.Decision"/>
/// metadata required to submit an issued action.
///
/// The view is built with a fixed key order, so the same state and viewer
/// always serialize to byte-identical JSON; the negative tests pin that
/// shape exactly.
/// </summary>
public static class ObservationBuilder
{
    public static Observation Build(GameState state, PlayerSide viewer)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(viewer))
        {
            throw new ArgumentOutOfRangeException(nameof(viewer), viewer, "Unknown observer side.");
        }

        // CR 26.03 7.3.1a: breach access visibility is carried by the run
        // state, not by the cards' faceup flags. Every card the Runner chose
        // to access stays visible to both viewers for the remainder of that
        // breach. Archives discard cards are flipped faceup by the breach
        // reducer, so they do not need temporary visibility here.
        HashSet<CardInstanceId> accessVisible = BuildAccessVisibleSet(state);

        Dictionary<string, object?> publicState = BuildPublicState(state, viewer, accessVisible);
        Dictionary<string, object?> privateState = BuildPrivateState(state, viewer);

        PendingDecision? pending = state.PendingDecision;
        string decisionId = pending?.Id.Value ?? "";
        IReadOnlyList<LegalAction> legalActions = pending is null
            || state.GetPlayer(pending.Owner).Side != viewer
            ? []
            : pending.LegalActions;

        return new Observation(viewer, decisionId, legalActions, publicState, privateState)
        {
            Decision = pending is null
                || state.GetPlayer(pending.Owner).Side != viewer
                ? null
                : new ObservationDecision(
                    pending.Id,
                    pending.Revision,
                    pending.Owner,
                    state.GetPlayer(pending.Owner).Side,
                    pending.Kind,
                    pending.Prompt),
        };
    }

    /// <summary>
    /// The cards a live run's breach has revealed for the remainder of that
    /// breach (CR 26.03 7.3.1a): every card the Runner chose to access.
    /// Archives discard visibility is represented by authoritative FaceUp
    /// state and persists after the run. Empty when no run is active.
    /// </summary>
    private static HashSet<CardInstanceId> BuildAccessVisibleSet(GameState state)
    {
        RunState? run = state.RunState;
        if (run is null)
        {
            return new HashSet<CardInstanceId>();
        }

        return new HashSet<CardInstanceId>(run.ChosenAccessCards);
    }

    private static Dictionary<string, object?> BuildPublicState(GameState state, PlayerSide viewer, IReadOnlySet<CardInstanceId> accessVisible)
    {
        PlayerState corp = state.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState runner = state.Players.Single(player => player.Side == PlayerSide.Runner);

        var publicState = new Dictionary<string, object?>
        {
            ["turn"] = new Dictionary<string, object?>
            {
                ["activeSide"] = state.Turn.ActiveSide,
                ["phase"] = state.Turn.Phase,
                ["turnNumber"] = state.Turn.TurnNumber,
            },
            ["players"] = new Dictionary<string, object?>
            {
                ["corp"] = PlayerView(corp),
                ["runner"] = PlayerView(runner),
            },
            ["outcome"] = new Dictionary<string, object?>
            {
                ["kind"] = state.Outcome.Kind,
                ["winner"] = state.Outcome.Winner,
                ["reason"] = state.Outcome.Reason,
            },
            ["servers"] = state.Servers.Select(server => ServerView(state, server, viewer, accessVisible)).ToArray(),
            ["hiddenZoneCounts"] = new Dictionary<string, object?>
            {
                ["corpHand"] = ZoneCount(state, ZoneKind.CorpHand),
                ["corpDeck"] = ZoneCount(state, ZoneKind.CorpDeck),
                ["runnerGrip"] = ZoneCount(state, ZoneKind.RunnerGrip),
                ["runnerStack"] = ZoneCount(state, ZoneKind.RunnerStack),
            },
            ["corpDiscard"] = ZoneCards(state, ZoneKind.CorpDiscard).Select(card => CardView(card)).ToArray(),
            ["runnerHeap"] = ZoneCards(state, ZoneKind.RunnerHeap).Select(card => CardView(card)).ToArray(),
            ["runnerRig"] = ZoneCards(state, ZoneKind.RunnerRig).Select(card => CardView(card)).ToArray(),
            ["corpScore"] = ZoneCards(state, ZoneKind.CorpScore).Select(card => CardView(card)).ToArray(),
            ["runnerScore"] = ZoneCards(state, ZoneKind.RunnerScore).Select(card => CardView(card)).ToArray(),
        };

        // Keep the lazy play-area topology absent from ordinary M1
        // observations. Once an event-run has created the zone, expose its
        // public contents explicitly to both viewers.
        if (state.Zones.Any(zone => zone.Kind == ZoneKind.RunnerPlayArea && zone.ServerId is null))
        {
            publicState["runnerPlayArea"] = ZoneCards(state, ZoneKind.RunnerPlayArea)
                .Select(card => CardView(card))
                .ToArray();
        }

        RunState? run = state.RunState;
        if (run is not null)
        {
            publicState["run"] = RunView(state, run, viewer, accessVisible);
        }

        return publicState;
    }

    private static Dictionary<string, object?> ServerView(GameState state, ServerState server, PlayerSide viewer, IReadOnlySet<CardInstanceId> accessVisible)
    {
        ZoneState iceZone = state.GetZone(server.IceZoneId);
        ZoneState rootZone = state.GetZone(server.RootZoneId);

        var ice = new List<object?>(iceZone.CardIds.Count);
        foreach (CardInstanceId iceCardId in iceZone.CardIds)
        {
            CardInstanceState iceCard = state.GetCard(iceCardId);
            ice.Add(IsVisibleTo(iceCard, iceZone, viewer, accessVisible) ? CardView(iceCard) : null);
        }

        return new Dictionary<string, object?>
        {
            ["id"] = server.Id.Value,
            ["kind"] = server.Kind,
            ["ice"] = ice,
            ["root"] = rootZone.CardIds
                .Select(state.GetCard)
                .Select(card => IsVisibleTo(card, rootZone, viewer, accessVisible)
                    ? CardView(card)
                    : null)
                .ToArray(),
        };
    }

    private static Dictionary<string, object?> RunView(GameState state, RunState run, PlayerSide viewer, IReadOnlySet<CardInstanceId> accessVisible)
    {
        ServerState attackedServer = state.GetServer(run.AttackedServer);
        ZoneState? hiddenZone = attackedServer.Kind switch
        {
            ServerKind.Headquarters => state.GetZone(ZoneKind.CorpHand),
            ServerKind.RAndD => state.GetZone(ZoneKind.CorpDeck),
            _ => null,
        };
        BreachHiddenCandidateSource? hiddenSource = RunState.HiddenBreachCandidate(
            attackedServer.Kind,
            hiddenZone,
            run.RandomAccessLimit);

        var breach = new Dictionary<string, object?>
        {
            ["candidates"] = run.BreachCandidates
                .Select(id => CandidateView(state, id, viewer, accessVisible))
                .ToArray(),
            ["randomAccessLimit"] = run.RandomAccessLimit,
            ["hiddenCandidateSource"] = hiddenSource,
            ["chosen"] = run.ChosenAccessCards.Select(id => CandidateView(state, id, viewer, accessVisible)).ToArray(),
            ["accessed"] = run.AccessedCards.Select(id => CandidateView(state, id, viewer, accessVisible)).ToArray(),
            ["accessState"] = run.AccessState,
        };

        return new Dictionary<string, object?>
        {
            ["id"] = run.Id.Value,
            ["attackedServer"] = run.AttackedServer.Value,
            ["initiatingSide"] = run.InitiatingSide,
            // The initiating card is routed through the same visibility rule as
            // access candidates and current ICE: a faceup source (installed
            // card, or the active run event in the Runner play area) renders
            // the full card view for both viewers; a facedown one renders
            // null (CR 26.03 8.6.7: a played run event is faceup while active).
            ["initiatingSource"] = run.InitiatingSource is null
                ? null
                : CandidateView(state, run.InitiatingSource.Value, viewer, accessVisible),
            ["phase"] = run.Phase,
            ["step"] = run.Step,
            ["currentIceIndex"] = run.CurrentIceIndex,
            ["currentIce"] = run.CurrentIceId is { } currentIce ? CandidateView(state, currentIce, viewer, accessVisible) : null,
            ["successful"] = run.Successful,
            ["unsuccessful"] = run.Unsuccessful,
            ["endReason"] = run.EndReason,
            ["priority"] = run.Priority is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["window"] = run.Priority.Window,
                    ["holder"] = run.Priority.PriorityHolder,
                    ["consecutivePasses"] = run.Priority.ConsecutivePasses,
                    ["corpMayRezApproachedIce"] = run.Priority.CorpMayRezApproachedIce,
                    ["corpMayRezNonIce"] = run.Priority.CorpMayRezNonIce,
                },
            ["encounter"] = run.Encounter is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["id"] = run.Encounter.Id.Value,
                    ["ice"] = run.Encounter.IceId is { } encounterIce ? CandidateView(state, encounterIce, viewer, accessVisible) : null,
                    ["status"] = run.Encounter.Status,
                    ["subroutines"] = run.Encounter.Subroutines.Select(subroutine =>
                        new Dictionary<string, object?>
                        {
                            ["ordinal"] = subroutine.Ordinal,
                            ["broken"] = subroutine.Broken,
                            ["resolved"] = subroutine.Resolved,
                        }).ToArray(),
                },
            ["breach"] = breach,
            ["runCredits"] = run.RunCredits,
            ["lingeringEffects"] = run.LingeringEffects.Select(effect =>
                new Dictionary<string, object?>
                {
                    ["id"] = effect.Id.Value,
                    ["behaviorId"] = effect.BehaviorId,
                    ["sourceCard"] = effect.SourceCardId is { } sourceCard ? CandidateView(state, sourceCard, viewer, accessVisible) : null,
                    ["expiry"] = effect.Expiry,
                }).ToArray(),
        };
    }

    private static Dictionary<string, object?> BuildPrivateState(GameState state, PlayerSide viewer)
    {
        if (viewer == PlayerSide.Corp)
        {
            var faceDownIce = new List<Dictionary<string, object?>>();
            foreach (ServerState server in state.Servers)
            {
                ZoneState iceZone = state.GetZone(server.IceZoneId);
                foreach (CardInstanceId iceCardId in iceZone.CardIds)
                {
                    CardInstanceState iceCard = state.GetCard(iceCardId);
                    if (iceCard.OwnerSide == PlayerSide.Corp && !iceCard.FaceUp)
                    {
                        faceDownIce.Add(PrivateCardView(iceCard));
                    }
                }
            }

            // CR 26.03 4.4.6c: the facedown cards in Archives (the Corp's
            // discard) are visible only to the Corp; faceup trash is already
            // in the public corpDiscard list, so only the facedown remainder
            // is listed here (the same pattern as faceDownIce).
            var faceDownArchives = new List<Dictionary<string, object?>>();
            ZoneState? corpDiscard = state.Zones.FirstOrDefault(zone => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
            if (corpDiscard is not null)
            {
                foreach (CardInstanceId discardCardId in corpDiscard.CardIds)
                {
                    CardInstanceState discardCard = state.GetCard(discardCardId);
                    if (discardCard.OwnerSide == PlayerSide.Corp && !discardCard.FaceUp)
                    {
                        faceDownArchives.Add(PrivateCardView(discardCard));
                    }
                }
            }

            return new Dictionary<string, object?>
            {
                ["hand"] = PrivateZoneCards(state, ZoneKind.CorpHand).Select(card => PrivateCardView(card)).ToArray(),
                ["deck"] = PrivateZoneCards(state, ZoneKind.CorpDeck).Select(card => PrivateCardView(card)).ToArray(),
                ["faceDownIce"] = faceDownIce,
                ["faceDownArchives"] = faceDownArchives,
            };
        }

        return new Dictionary<string, object?>
        {
            ["grip"] = PrivateZoneCards(state, ZoneKind.RunnerGrip).Select(card => PrivateCardView(card)).ToArray(),
            ["stack"] = PrivateZoneCards(state, ZoneKind.RunnerStack).Select(card => PrivateCardView(card)).ToArray(),
        };
    }

    /// <summary>
    /// A card is known to the viewer when it is the viewer's own card, a
    /// faceup card (the engine only turns a card faceup when it is revealed
    /// to both players: installed cards, rezzed ICE, flipped trash, scored
    /// agendas, the active run event in the Runner play area), or a card the active run's
    /// breach has revealed for the remainder of that breach (CR 26.03 7.3.1a).
    /// Hidden pools (hands, decks, grips, stacks) hold face-down cards, so
    /// they are known only through the owner's private view.
    /// </summary>
    private static bool IsVisibleTo(CardInstanceState card, ZoneState zone, PlayerSide viewer, IReadOnlySet<CardInstanceId> accessVisible)
    {
        return card.OwnerSide == viewer || card.FaceUp || accessVisible.Contains(card.Id);
    }

    private static Dictionary<string, object?> PlayerView(PlayerState player) => new()
    {
        ["id"] = player.Id.Value,
        ["credits"] = player.Credits,
        ["clicks"] = player.Clicks,
        ["agendaPoints"] = player.AgendaPoints,
    };

    private static Dictionary<string, object?> CardView(CardInstanceState card) => new()
    {
        ["id"] = card.Id.Value,
        ["definitionId"] = card.DefinitionId.Value,
        ["rezzed"] = card.Rezzed,
        ["advancementCounters"] = card.AdvancementCounters,
    };

    private static Dictionary<string, object?> PrivateCardView(CardInstanceState card) => new()
    {
        ["id"] = card.Id.Value,
        ["definitionId"] = card.DefinitionId.Value,
    };

    /// <summary>
    /// The card view for a run-referenced card (candidates, ICE, sources):
    /// the card's own visibility — or the active run's temporary breach
    /// visibility — decides whether the viewer sees its identity or only that
    /// a card occupies that slot.
    /// </summary>
    private static Dictionary<string, object?>? CandidateView(GameState state, CardInstanceId cardId, PlayerSide viewer, IReadOnlySet<CardInstanceId> accessVisible)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState zone = state.GetZone(card.ZoneId);
        return IsVisibleTo(card, zone, viewer, accessVisible) ? CardView(card) : null;
    }

    private static int ZoneCount(GameState state, ZoneKind kind)
    {
        ZoneState? zone = state.Zones.FirstOrDefault(value => value.Kind == kind && value.ServerId is null);
        return zone?.CardIds.Count ?? 0;
    }

    /// <summary>Every card in a private pool; the owner sees all of them.</summary>
    private static CardInstanceState[] PrivateZoneCards(GameState state, ZoneKind kind)
    {
        ZoneState? zone = state.Zones.FirstOrDefault(value => value.Kind == kind && value.ServerId is null);
        return zone is null
            ? []
            : zone.CardIds.Select(state.GetCard).ToArray();
    }

    /// <summary>Only the revealed (faceup) cards of a public zone.</summary>
    private static CardInstanceState[] ZoneCards(GameState state, ZoneKind kind)
    {
        ZoneState? zone = state.Zones.FirstOrDefault(value => value.Kind == kind && value.ServerId is null);
        return zone is null
            ? []
            : zone.CardIds.Select(state.GetCard).Where(card => card.FaceUp).ToArray();
    }
}
