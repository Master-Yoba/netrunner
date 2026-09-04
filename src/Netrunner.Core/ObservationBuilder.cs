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
        return Build(state, viewer, BehaviorCatalog.Empty);
    }

    /// <summary>
    /// Builds the same viewer-scoped observation using the immutable behavior
    /// catalog composed by the match. This keeps effective static projections
    /// (including maximum hand size) derived without mutating base rules.
    /// </summary>
    public static Observation Build(
        GameState state,
        PlayerSide viewer,
        IBehaviorCatalog behaviorCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
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

        Dictionary<string, object?> publicState = BuildPublicState(state, viewer, accessVisible, behaviorCatalog);
        Dictionary<string, object?> privateState = BuildPrivateState(state, viewer);

        PendingDecision? pending = state.PendingDecision;
        string decisionId = pending?.Id.Value ?? "";
        // PendingDecision owns the authoritative action objects. Once a
        // frozen cost quote is present, project a fresh immutable copy so an
        // adapter cannot retain the engine's quote payload across a state
        // transition. Leave legacy unmodified action lists untouched to
        // preserve their historical DTO equality/byte shape.
        bool hasCostQuote = pending?.LegalActions.Any(action => action.Parameters switch
        {
            InstallCardParameters install => install.CostQuote is not null,
            RunBreakParameters breakParameters => breakParameters.CostQuote is not null,
            _ => false,
        }) == true;
        IReadOnlyList<LegalAction> legalActions = pending is null
            || state.GetPlayer(pending.Owner).Side != viewer
            ? []
            : hasCostQuote
                ? pending.Copy().LegalActions
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

    private static Dictionary<string, object?> BuildPublicState(
        GameState state,
        PlayerSide viewer,
        IReadOnlySet<CardInstanceId> accessVisible,
        IBehaviorCatalog behaviorCatalog)
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
                ["corp"] = PlayerView(state, corp, behaviorCatalog),
                ["runner"] = PlayerView(state, runner, behaviorCatalog),
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
            ["corpDiscard"] = ZoneCards(state, ZoneKind.CorpDiscard).Select(card => CardView(state, card)).ToArray(),
            ["runnerHeap"] = ZoneCards(state, ZoneKind.RunnerHeap).Select(card => CardView(state, card)).ToArray(),
            ["runnerRig"] = ZoneCards(state, ZoneKind.RunnerRig).Select(card => CardView(state, card)).ToArray(),
            ["corpScore"] = ZoneCards(state, ZoneKind.CorpScore).Select(card => CardView(state, card)).ToArray(),
            ["runnerScore"] = ZoneCards(state, ZoneKind.RunnerScore).Select(card => CardView(state, card)).ToArray(),
        };

        // Real CR turns carry an explicit step/facts projection. Keep these
        // additive and conditional so M1/M2 observation goldens retain their
        // original byte shape while viewers still receive this/last-turn
        // facts without any private card or allocator data.
        if (state.Turn.Step != TurnStep.None)
        {
            ((Dictionary<string, object?>)publicState["turn"]!)["step"] = state.Turn.Step;
        }

        if (state.Turn.History is TurnHistoryState history)
        {
            publicState["turnFacts"] = new Dictionary<string, object?>
            {
                ["corp"] = TurnFactsView(state, viewer, accessVisible, history.CorpThisTurn, history.CorpLastTurn),
                ["runner"] = TurnFactsView(state, viewer, accessVisible, history.RunnerThisTurn, history.RunnerLastTurn),
            };
        }

        // Keep the lazy play-area topology absent from ordinary M1
        // observations. Once an event-run has created the zone, expose its
        // public contents explicitly to both viewers.
        if (state.Zones.Any(zone => zone.Kind == ZoneKind.RunnerPlayArea && zone.ServerId is null))
        {
            publicState["runnerPlayArea"] = ZoneCards(state, ZoneKind.RunnerPlayArea)
                .Select(card => CardView(state, card))
                .ToArray();
        }

        // Keep the lazy Corp play-area topology absent from ordinary
        // observations. Once an Operation play has created the zone, expose
        // its public contents explicitly to both viewers.
        if (state.Zones.Any(zone => zone.Kind == ZoneKind.CorpPlayArea && zone.ServerId is null))
        {
            publicState["corpPlayArea"] = ZoneCards(state, ZoneKind.CorpPlayArea)
                .Select(card => CardView(state, card))
                .ToArray();
        }

        // Removed-from-game zones are lazy so the default topology and its
        // observation golden remain unchanged. Once either side's zone exists,
        // it is a public zone and is projected to both viewers.
        if (state.Zones.Any(zone => zone.Kind == ZoneKind.CorpRemovedFromGame && zone.ServerId is null))
        {
            publicState["corpRemovedFromGame"] = ZoneCards(state, ZoneKind.CorpRemovedFromGame)
                .Select(card => CardView(state, card))
                .ToArray();
        }

        if (state.Zones.Any(zone => zone.Kind == ZoneKind.RunnerRemovedFromGame && zone.ServerId is null))
        {
            publicState["runnerRemovedFromGame"] = ZoneCards(state, ZoneKind.RunnerRemovedFromGame)
                .Select(card => CardView(state, card))
                .ToArray();
        }

        RunState? run = state.RunState;
        if (run is not null)
        {
            publicState["run"] = RunView(state, run, viewer, accessVisible);
        }

        return publicState;
    }

    private static Dictionary<string, object?> TurnFactsView(
        GameState state,
        PlayerSide viewer,
        IReadOnlySet<CardInstanceId> accessVisible,
        TurnFactsState? thisTurn,
        TurnFactsState? lastTurn) =>
        new()
        {
            ["thisTurn"] = thisTurn is null ? null : FactsView(state, viewer, accessVisible, thisTurn),
            ["lastTurn"] = lastTurn is null ? null : FactsView(state, viewer, accessVisible, lastTurn),
        };

    private static Dictionary<string, object?> FactsView(
        GameState state,
        PlayerSide viewer,
        IReadOnlySet<CardInstanceId> accessVisible,
        TurnFactsState facts) =>
        new()
        {
            ["side"] = facts.Side,
            ["turnNumber"] = facts.TurnNumber,
            ["clicksAllotted"] = facts.ClicksAllotted,
            ["clicksLost"] = facts.ClicksLost,
            ["turnBegan"] = facts.TurnBegan,
            ["mandatoryDrawPerformed"] = facts.MandatoryDrawPerformed,
            ["actionPhaseEnded"] = facts.ActionPhaseEnded,
            ["discardPhaseEnded"] = facts.DiscardPhaseEnded,
            ["installedCardIds"] = facts.InstalledCardIds
                .Where(cardId => IsFactCardVisible(state, cardId, viewer, accessVisible))
                .Select(static cardId => cardId.Value)
                .ToArray(),
            ["successfulRun"] = facts.SuccessfulRun,
            ["attemptedRunServerIds"] = facts.AttemptedRunServerIds
                .Select(static serverId => serverId.Value)
                .ToArray(),
            ["successfulRunServerIds"] = facts.SuccessfulRunServerIds
                .Select(static serverId => serverId.Value)
                .ToArray(),
            ["accessedCardCount"] = facts.AccessedCardOccurrenceIds.Count,
            ["accessedCardIds"] = facts.AccessedCardOccurrenceIds
                .Where(cardId => IsFactCardVisible(state, cardId, viewer, accessVisible))
                .Select(static cardId => cardId.Value)
                .ToArray(),
            ["trashedCardCount"] = facts.TrashedCardOccurrenceIds.Count,
            ["trashedCardIds"] = facts.TrashedCardOccurrenceIds
                .Where(cardId => IsFactCardVisible(state, cardId, viewer, accessVisible))
                .Select(static cardId => cardId.Value)
                .ToArray(),
            ["agendaTurnChanges"] = facts.AgendaTurnChanges
                .Select(static change => (object?)new Dictionary<string, object?>
                {
                    ["cardId"] = change.CardId.Value,
                    ["beneficiarySide"] = change.BeneficiarySide,
                    ["points"] = change.Points,
                    ["kind"] = change.Kind,
                })
                .ToArray(),
            ["tagTurnChanges"] = facts.TagTurnChanges
                .Select(static change => (object?)new Dictionary<string, object?>
                {
                    ["affectedPlayerId"] = change.AffectedPlayerId.Value,
                    ["delta"] = change.Delta,
                })
                .ToArray(),
            ["usedAbilityKeys"] = facts.UsedAbilityKeys
                .Where(use => IsFactCardVisible(state, use.SourceCardId, viewer, accessVisible))
                .Select(static use => (object?)new Dictionary<string, object?>
                {
                    ["sourceCardId"] = use.SourceCardId.Value,
                    ["abilityKey"] = use.AbilityKey,
                })
                .ToArray(),
            ["firstXUseKeys"] = facts.FirstXUseKeys
                .Where(use => IsFactCardVisible(state, use.SourceCardId, viewer, accessVisible))
                .Select(static use => (object?)new Dictionary<string, object?>
                {
                    ["sourceCardId"] = use.SourceCardId.Value,
                    ["key"] = use.Key,
                    ["count"] = use.Count,
                })
                .ToArray(),
        };

    private static bool IsFactCardVisible(
        GameState state,
        CardInstanceId cardId,
        PlayerSide viewer,
        IReadOnlySet<CardInstanceId> accessVisible) =>
        state.Cards.FirstOrDefault(card => card.Id == cardId) is { } card
        && IsVisibleTo(card, state.GetZone(card.ZoneId), viewer, accessVisible);

    private static Dictionary<string, object?> ServerView(GameState state, ServerState server, PlayerSide viewer, IReadOnlySet<CardInstanceId> accessVisible)
    {
        ZoneState iceZone = state.GetZone(server.IceZoneId);
        ZoneState rootZone = state.GetZone(server.RootZoneId);

        var ice = new List<object?>(iceZone.CardIds.Count);
        foreach (CardInstanceId iceCardId in iceZone.CardIds)
        {
            CardInstanceState iceCard = state.GetCard(iceCardId);
            ice.Add(IsVisibleTo(iceCard, iceZone, viewer, accessVisible) ? CardView(state, iceCard) : null);
        }

        return new Dictionary<string, object?>
        {
            ["id"] = server.Id.Value,
            ["kind"] = server.Kind,
            ["ice"] = ice,
            ["root"] = rootZone.CardIds
                .Select(state.GetCard)
                .Select(card => IsVisibleTo(card, rootZone, viewer, accessVisible)
                    ? CardView(state, card)
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
            run.RandomAccessLimit,
            run.AccessedCards);

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

        var view = new Dictionary<string, object?>
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

        // Keep the M1/M2 run-view byte shape unchanged when no D1 modifier
        // was snapshotted. The list itself is detached for every observer.
        if (run.BreachAccessModifiers is { } modifiers)
        {
            view["breachAccessModifiers"] = modifiers
                .Select(modifier => RunBreachAccessModifierView(state, modifier, viewer, accessVisible))
                .ToArray();
        }

        return view;
    }

    private static Dictionary<string, object?> RunBreachAccessModifierView(
        GameState state,
        RunBreachAccessModifier modifier,
        PlayerSide viewer,
        IReadOnlySet<CardInstanceId> accessVisible)
    {
        CardInstanceState? source = state.Cards.FirstOrDefault(card => card.Id == modifier.SourceCardId);
        bool sourceVisible = source is not null
            && IsVisibleTo(source, state.GetZone(source.ZoneId), viewer, accessVisible);
        return new Dictionary<string, object?>
        {
            ["sourceCardId"] = sourceVisible ? modifier.SourceCardId.Value : null,
            ["attackedServerId"] = modifier.AttackedServerId.Value,
            ["delta"] = modifier.Delta,
            ["reasonKey"] = modifier.ReasonKey,
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
                        faceDownIce.Add(PrivateCardView(state, iceCard));
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
                        faceDownArchives.Add(PrivateCardView(state, discardCard));
                    }
                }
            }

            return new Dictionary<string, object?>
            {
                ["hand"] = PrivateZoneCards(state, ZoneKind.CorpHand).Select(card => PrivateCardView(state, card)).ToArray(),
                ["deck"] = PrivateZoneCards(state, ZoneKind.CorpDeck).Select(card => PrivateCardView(state, card)).ToArray(),
                ["faceDownIce"] = faceDownIce,
                ["faceDownArchives"] = faceDownArchives,
            };
        }

        return new Dictionary<string, object?>
        {
            ["grip"] = PrivateZoneCards(state, ZoneKind.RunnerGrip).Select(card => PrivateCardView(state, card)).ToArray(),
            ["stack"] = PrivateZoneCards(state, ZoneKind.RunnerStack).Select(card => PrivateCardView(state, card)).ToArray(),
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

    private static Dictionary<string, object?> PlayerView(
        GameState state,
        PlayerState player,
        IBehaviorCatalog behaviorCatalog)
    {
        var view = new Dictionary<string, object?>
        {
            ["id"] = player.Id.Value,
            ["credits"] = player.Credits,
            ["clicks"] = player.Clicks,
            ["agendaPoints"] = player.AgendaPoints,
        };

        // Keep the M1 observation golden unchanged when the optional M2
        // rules aggregate is absent. Once it is present, expose only the
        // viewer-safe derived limit; base damage and hidden card contents are
        // not leaked through this public projection.
        if (state.RulesState?.Players.SingleOrDefault(value => value.PlayerId == player.Id) is { } rules)
        {
            int modifier = BehaviorStaticModifierProjection.Sum(
                behaviorCatalog,
                state.Cards,
                state.Zones,
                player.Side,
                BehaviorStaticModifierKind.MaximumHandSize);
            view["maximumHandSize"] = Math.Max(0, checked(rules.MaximumHandSize + modifier));
            view["tags"] = Math.Max(0, rules.Tags);
        }

        return view;
    }

    private static Dictionary<string, object?> CardView(GameState state, CardInstanceState card)
    {
        var view = new Dictionary<string, object?>
        {
            ["id"] = card.Id.Value,
            ["definitionId"] = card.DefinitionId.Value,
            ["rezzed"] = card.Rezzed,
            ["advancementCounters"] = card.AdvancementCounters,
        };
        AddCounters(view, state, card.Id);
        return view;
    }

    private static Dictionary<string, object?> PrivateCardView(GameState state, CardInstanceState card)
    {
        var view = new Dictionary<string, object?>
        {
            ["id"] = card.Id.Value,
            ["definitionId"] = card.DefinitionId.Value,
        };
        AddCounters(view, state, card.Id);
        return view;
    }

    /// <summary>
    /// Adds the optional typed-counter projection without changing the shape
    /// of cards that have no rules counters. Counter order is canonicalized by
    /// name so observations do not depend on reducer insertion order.
    /// </summary>
    private static void AddCounters(
        Dictionary<string, object?> view,
        GameState state,
        CardInstanceId cardId)
    {
        CardCounterState[] counters = state.RulesState?.Counters
            .Where(counter => counter.CardId == cardId)
            .OrderBy(counter => counter.CounterName, StringComparer.Ordinal)
            .ToArray()
            ?? [];
        if (counters.Length == 0)
        {
            return;
        }

        view["counters"] = counters.Select(counter => (object?)new Dictionary<string, object?>
        {
            ["counterName"] = counter.CounterName,
            ["purpose"] = counter.Purpose,
            ["count"] = counter.Count,
            ["spendRestriction"] = counter.SpendRestriction,
        }).ToArray();
    }

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
        return IsVisibleTo(card, zone, viewer, accessVisible) ? CardView(state, card) : null;
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
