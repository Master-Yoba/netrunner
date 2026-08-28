using System.Globalization;

namespace Netrunner.Core;

public sealed record StateInvariantViolation(
    string Code,
    string Path,
    string Message)
{
    public override string ToString() => $"{Code} at {Path}: {Message}";
}

public sealed class StateInvariantException : InvalidOperationException
{
    public StateInvariantException(IReadOnlyList<StateInvariantViolation> violations)
        : base(BuildMessage(violations))
    {
        ArgumentNullException.ThrowIfNull(violations);
        Violations = new System.Collections.ObjectModel.ReadOnlyCollection<StateInvariantViolation>(violations.ToArray());
    }

    public IReadOnlyList<StateInvariantViolation> Violations { get; }

    private static string BuildMessage(IReadOnlyList<StateInvariantViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        if (violations.Count == 0)
        {
            return "The game state is invalid.";
        }

        return $"The game state is invalid ({violations.Count} violation(s)): "
            + string.Join("; ", violations.Select(static violation => violation.ToString()));
    }
}

/// <summary>
/// Checks all structural guarantees required before a state can enter the
/// reducer or be exposed as a restorable snapshot.
/// </summary>
public static class StateInvariantValidator
{
    public static void Validate(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        IReadOnlyList<StateInvariantViolation> violations = Collect(state);
        if (violations.Count > 0)
        {
            throw new StateInvariantException(violations);
        }
    }

    public static bool TryValidate(
        GameState state,
        out IReadOnlyList<StateInvariantViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(state);
        violations = Collect(state);
        return violations.Count == 0;
    }

    public static IReadOnlyList<StateInvariantViolation> Collect(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var violations = new List<StateInvariantViolation>();
        ValidateMetadata(state, violations);
        ValidatePlayers(state, violations);
        ValidateZonesAndCards(state, violations);
        ValidateServers(state, violations);
        ValidateTurnAndOutcome(state, violations);
        ValidateResolutionAndDecision(state, violations);
        return violations.AsReadOnly();
    }

    private static void ValidateMetadata(GameState state, List<StateInvariantViolation> violations)
    {
        if (state.SchemaVersion != GameState.CurrentSchemaVersion)
        {
            Add(
                violations,
                "schema.unsupported",
                "schemaVersion",
                $"Schema version {state.SchemaVersion} is not supported; expected {GameState.CurrentSchemaVersion}.");
        }

        if (!StableIdentifier.TryValidate(state.GameId.Value))
        {
            Add(violations, "id.invalid", "gameId", "Game ID is empty or contains whitespace/control characters.");
        }

        if (string.IsNullOrWhiteSpace(state.RulesVersion))
        {
            Add(violations, "metadata.missing", "rulesVersion", "Rules version must be non-empty.");
        }

        if (string.IsNullOrWhiteSpace(state.CardDataVersion))
        {
            Add(violations, "metadata.missing", "cardDataVersion", "Card-data version must be non-empty.");
        }

        if (state.Allocator is null || state.Allocator.NextSequence == 0)
        {
            Add(violations, "allocator.invalid", "allocator.nextSequence", "The next ID allocation sequence must be nonzero.");
        }

        if (state.DecisionRevision < 0)
        {
            Add(violations, "decision.invalid-revision", "decisionRevision", "The decision revision must not be negative.");
        }

        if (state.EventSequence < 0)
        {
            Add(violations, "events.invalid-sequence", "eventSequence", "The event sequence must not be negative.");
        }

        if (state.ActionSequence < 0)
        {
            Add(violations, "actions.invalid-sequence", "actionSequence", "The action sequence must not be negative.");
        }

        ValidateAllocatorPosition(state, violations);
    }

    private static void ValidatePlayers(GameState state, List<StateInvariantViolation> violations)
    {
        if (state.Players.Count != 2)
        {
            Add(
                violations,
                "players.count",
                "players",
                $"Exactly two players (one Corp and one Runner) are required; found {state.Players.Count}.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var sides = new HashSet<PlayerSide>();
        for (int index = 0; index < state.Players.Count; index++)
        {
            PlayerState? player = state.Players[index];
            string path = $"players[{index}]";
            if (player is null)
            {
                Add(violations, "players.null", path, "Player entries must not be null.");
                continue;
            }

            if (!StableIdentifier.TryValidate(player.Id.Value))
            {
                Add(violations, "id.invalid", $"{path}.id", "Player ID is empty or invalid.");
            }
            else if (!ids.Add(player.Id.Value))
            {
                Add(violations, "players.duplicate-id", $"{path}.id", $"Player ID '{player.Id}' is duplicated.");
            }

            if (!IsPlayerSide(player.Side))
            {
                Add(violations, "players.invalid-side", $"{path}.side", $"'{player.Side}' is not a valid player side.");
            }
            else if (!sides.Add(player.Side))
            {
                Add(violations, "players.duplicate-side", $"{path}.side", $"There is more than one '{player.Side}' player.");
            }

            if (player.Credits < 0)
            {
                Add(violations, "players.negative-credits", $"{path}.credits", "Credits must not be negative.");
            }

            if (player.Clicks < 0)
            {
                Add(violations, "players.negative-clicks", $"{path}.clicks", "Clicks must not be negative.");
            }

            if (player.AgendaPoints < 0)
            {
                Add(violations, "players.negative-agenda-points", $"{path}.agendaPoints", "Agenda points must not be negative.");
            }
        }

        if (!sides.Contains(PlayerSide.Corp))
        {
            Add(violations, "players.missing-corp", "players", "Exactly one Corp player is required.");
        }

        if (!sides.Contains(PlayerSide.Runner))
        {
            Add(violations, "players.missing-runner", "players", "Exactly one Runner player is required.");
        }
    }

    private static void ValidateZonesAndCards(GameState state, List<StateInvariantViolation> violations)
    {
        var zonesById = new Dictionary<string, ZoneState>(StringComparer.Ordinal);
        for (int index = 0; index < state.Zones.Count; index++)
        {
            ZoneState? zone = state.Zones[index];
            string path = $"zones[{index}]";
            if (zone is null)
            {
                Add(violations, "zones.null", path, "Zone entries must not be null.");
                continue;
            }

            if (!StableIdentifier.TryValidate(zone.Id.Value))
            {
                Add(violations, "id.invalid", $"{path}.id", "Zone ID is empty or invalid.");
            }
            else if (!zonesById.TryAdd(zone.Id.Value, zone))
            {
                Add(violations, "zones.duplicate-id", $"{path}.id", $"Zone ID '{zone.Id}' is duplicated.");
            }

            ValidateZoneSemantics(zone, path, violations);
        }

        ValidateRequiredZones(state, violations);

        var cardsById = new Dictionary<string, CardInstanceState>(StringComparer.Ordinal);
        for (int index = 0; index < state.Cards.Count; index++)
        {
            CardInstanceState? card = state.Cards[index];
            string path = $"cards[{index}]";
            if (card is null)
            {
                Add(violations, "cards.null", path, "Card entries must not be null.");
                continue;
            }

            if (!StableIdentifier.TryValidate(card.Id.Value))
            {
                Add(violations, "id.invalid", $"{path}.id", "Card instance ID is empty or invalid.");
            }
            else if (!cardsById.TryAdd(card.Id.Value, card))
            {
                Add(violations, "cards.duplicate-id", $"{path}.id", $"Card instance ID '{card.Id}' is duplicated.");
            }

            if (!StableIdentifier.TryValidate(card.DefinitionId.Value))
            {
                Add(violations, "id.invalid", $"{path}.definitionId", "Card definition ID is empty or invalid.");
            }

            if (!StableIdentifier.TryValidate(card.ZoneId.Value))
            {
                Add(violations, "id.invalid", $"{path}.zoneId", "Card zone ID is empty or invalid.");
            }

            if (card.AdvancementCounters < 0)
            {
                Add(violations, "cards.negative-counters", $"{path}.advancementCounters", "Advancement counters must not be negative.");
            }

            if (!IsPlayerSide(card.OwnerSide))
            {
                Add(violations, "cards.invalid-owner", $"{path}.ownerSide", "Card owner must be Corp or Runner.");
            }
        }

        var memberships = new Dictionary<string, (int Count, string ZoneId)>(StringComparer.Ordinal);
        for (int index = 0; index < state.Zones.Count; index++)
        {
            ZoneState? zone = state.Zones[index];
            if (zone is null)
            {
                continue;
            }

            var idsInZone = new HashSet<string>(StringComparer.Ordinal);
            for (int cardIndex = 0; cardIndex < zone.CardIds.Count; cardIndex++)
            {
                CardInstanceId cardId = zone.CardIds[cardIndex];
                string cardKey = cardId.Value ?? string.Empty;
                string path = $"zones[{index}].cardIds[{cardIndex}]";
                if (!StableIdentifier.TryValidate(cardId.Value))
                {
                    Add(violations, "id.invalid", path, "Zone card reference is empty or invalid.");
                }

                if (!idsInZone.Add(cardKey))
                {
                    Add(violations, "cards.duplicate-membership", path, $"Card '{cardId}' occurs more than once in zone '{zone.Id}'.");
                }

                if (!memberships.TryGetValue(cardKey, out (int Count, string ZoneId) membership))
                {
                    memberships[cardKey] = (1, zone.Id.Value ?? string.Empty);
                }
                else
                {
                    memberships[cardKey] = (membership.Count + 1, membership.ZoneId);
                    Add(violations, "cards.multiple-zones", path, $"Card '{cardId}' is present in more than one zone.");
                }

                if (!cardsById.TryGetValue(cardKey, out CardInstanceState? card))
                {
                    Add(violations, "zones.unknown-card", path, $"Zone '{zone.Id}' references unknown card '{cardId}'.");
                    continue;
                }

                if (card.ZoneId != zone.Id)
                {
                    Add(
                        violations,
                        "cards.zone-reference-mismatch",
                        path,
                        $"Card '{card.Id}' points to zone '{card.ZoneId}', but is listed in '{zone.Id}'.");
                }

                bool isStolenCorpAgenda = zone.Kind == ZoneKind.RunnerScore
                    && card.OwnerSide == PlayerSide.Corp
                    && ToyCardBehaviorCatalog.TryGet(card.DefinitionId, out ToyCardBehavior? behavior)
                    && behavior.Kind == ToyCardKind.CorpAgenda;
                if (zone.OwnerSide.HasValue && card.OwnerSide != zone.OwnerSide.Value && !isStolenCorpAgenda)
                {
                    Add(
                        violations,
                        "cards.owner-zone-mismatch",
                        path,
                        $"Card '{card.Id}' owner '{card.OwnerSide}' does not match zone owner '{zone.OwnerSide}'.");
                }
            }
        }

        foreach ((string cardId, CardInstanceState card) in cardsById)
        {
            if (!memberships.TryGetValue(cardId, out (int Count, string ZoneId) membership))
            {
                Add(violations, "cards.unzoned", $"cards[{cardId}].zoneId", $"Card '{card.Id}' is not present in any zone.");
            }
            else if (membership.Count != 1)
            {
                Add(violations, "cards.membership-count", $"cards[{cardId}].zoneId", $"Card '{card.Id}' must belong to exactly one zone.");
            }
        }
    }

    private static void ValidateZoneSemantics(
        ZoneState zone,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (!Enum.IsDefined(zone.Kind))
        {
            Add(violations, "zones.invalid-kind", $"{path}.kind", $"'{zone.Kind}' is not a supported zone kind.");
            return;
        }

        PlayerSide? expectedOwner = zone.Kind switch
        {
            ZoneKind.CorpDeck or ZoneKind.CorpHand or ZoneKind.CorpDiscard or ZoneKind.CorpScore => PlayerSide.Corp,
            ZoneKind.RunnerStack or ZoneKind.RunnerGrip or ZoneKind.RunnerHeap or ZoneKind.RunnerRig or ZoneKind.RunnerScore => PlayerSide.Runner,
            ZoneKind.ServerIce or ZoneKind.ServerRoot => PlayerSide.Corp,
            _ => null,
        };

        if (zone.OwnerSide != expectedOwner)
        {
            Add(
                violations,
                "zones.owner-mismatch",
                $"{path}.ownerSide",
                $"Zone kind '{zone.Kind}' must be owned by '{expectedOwner?.ToString() ?? "none"}', but is '{zone.OwnerSide?.ToString() ?? "none"}'.");
        }

        bool isServerZone = zone.Kind is ZoneKind.ServerIce or ZoneKind.ServerRoot;
        if (isServerZone && !zone.ServerId.HasValue)
        {
            Add(violations, "zones.server-missing", $"{path}.serverId", "A server ICE/root zone must reference a server.");
        }
        else if (!isServerZone && zone.ServerId.HasValue)
        {
            Add(violations, "zones.server-unexpected", $"{path}.serverId", "Only server ICE/root zones may reference a server.");
        }

        if (zone.ServerId.HasValue && !StableIdentifier.TryValidate(zone.ServerId.Value.Value))
        {
            Add(violations, "id.invalid", $"{path}.serverId", "Server ID reference is empty or invalid.");
        }
    }

    private static void ValidateRequiredZones(GameState state, List<StateInvariantViolation> violations)
    {
        ZoneKind[] requiredKinds =
        [
            ZoneKind.CorpDeck,
            ZoneKind.CorpHand,
            ZoneKind.CorpDiscard,
            ZoneKind.CorpScore,
            ZoneKind.RunnerStack,
            ZoneKind.RunnerGrip,
            ZoneKind.RunnerHeap,
            ZoneKind.RunnerRig,
            ZoneKind.RunnerScore,
        ];

        foreach (ZoneKind kind in requiredKinds)
        {
            int count = state.Zones.Count(zone => zone is not null && zone.Kind == kind);
            if (count != 1)
            {
                Add(
                    violations,
                    "zones.required-kind-count",
                    "zones",
                    $"Found {count} '{kind}' zones; exactly one is required.");
            }
        }
    }

    private static void ValidateServers(GameState state, List<StateInvariantViolation> violations)
    {
        var serversById = new Dictionary<string, ServerState>(StringComparer.Ordinal);
        var centralKinds = new Dictionary<ServerKind, ServerState>();
        var zoneReferences = new Dictionary<string, (ServerId ServerId, string Role)>(StringComparer.Ordinal);
        var zonesById = new Dictionary<string, ZoneState>(StringComparer.Ordinal);
        foreach (ZoneState? zone in state.Zones)
        {
            if (zone is not null)
            {
                _ = zonesById.TryAdd(zone.Id.Value ?? string.Empty, zone);
            }
        }

        for (int index = 0; index < state.Servers.Count; index++)
        {
            ServerState? server = state.Servers[index];
            string path = $"servers[{index}]";
            if (server is null)
            {
                Add(violations, "servers.null", path, "Server entries must not be null.");
                continue;
            }

            if (!StableIdentifier.TryValidate(server.Id.Value))
            {
                Add(violations, "id.invalid", $"{path}.id", "Server ID is empty or invalid.");
            }
            else if (!serversById.TryAdd(server.Id.Value, server))
            {
                Add(violations, "servers.duplicate-id", $"{path}.id", $"Server ID '{server.Id}' is duplicated.");
            }

            if (!Enum.IsDefined(server.Kind))
            {
                Add(violations, "servers.invalid-kind", $"{path}.kind", $"'{server.Kind}' is not a supported server kind.");
            }
            else if (server.Kind is ServerKind.Headquarters or ServerKind.RAndD or ServerKind.Archives)
            {
                if (!centralKinds.TryAdd(server.Kind, server))
                {
                    Add(violations, "servers.duplicate-central", $"{path}.kind", $"Central server kind '{server.Kind}' is duplicated.");
                }
            }

            ValidateServerZone(
                server,
                server.IceZoneId,
                ZoneKind.ServerIce,
                "iceZoneId",
                path,
                zonesById,
                zoneReferences,
                violations);
            ValidateServerZone(
                server,
                server.RootZoneId,
                ZoneKind.ServerRoot,
                "rootZoneId",
                path,
                zonesById,
                zoneReferences,
                violations);
        }

        foreach (ServerKind kind in new[] { ServerKind.Headquarters, ServerKind.RAndD, ServerKind.Archives })
        {
            if (!centralKinds.ContainsKey(kind))
            {
                Add(violations, "servers.missing-central", "servers", $"Exactly one '{kind}' central server is required.");
            }
        }

        foreach (ZoneState? zone in state.Zones)
        {
            if (zone is null || zone.Kind is not (ZoneKind.ServerIce or ZoneKind.ServerRoot))
            {
                continue;
            }

            string path = $"zones[{zone.Id}]";
            if (!zone.ServerId.HasValue)
            {
                continue;
            }

            if (!serversById.TryGetValue(zone.ServerId.Value.Value ?? string.Empty, out ServerState? server))
            {
                Add(
                    violations,
                    "zones.unknown-server",
                    $"{path}.serverId",
                    $"Zone '{zone.Id}' references unknown server '{zone.ServerId}'.");
                continue;
            }

            bool declared = zone.Kind == ZoneKind.ServerIce
                ? zone.Id == server.IceZoneId
                : zone.Id == server.RootZoneId;
            if (!declared)
            {
                Add(
                    violations,
                    "zones.unreferenced-server-zone",
                    path,
                    $"Server zone '{zone.Id}' is not the declared {zone.Kind} zone for server '{server.Id}'.");
            }
        }
    }

    private static void ValidateServerZone(
        ServerState server,
        ZoneId zoneId,
        ZoneKind expectedKind,
        string propertyName,
        string serverPath,
        Dictionary<string, ZoneState> zonesById,
        Dictionary<string, (ServerId ServerId, string Role)> zoneReferences,
        List<StateInvariantViolation> violations)
    {
        string path = $"{serverPath}.{propertyName}";
        if (!StableIdentifier.TryValidate(zoneId.Value))
        {
            Add(violations, "id.invalid", path, "Server zone ID is empty or invalid.");
            return;
        }

        if (!zonesById.TryGetValue(zoneId.Value, out ZoneState? zone))
        {
            Add(violations, "servers.unknown-zone", path, $"Server '{server.Id}' references unknown zone '{zoneId}'.");
            return;
        }

        if (zone.Kind != expectedKind)
        {
            Add(violations, "servers.zone-kind-mismatch", path, $"Zone '{zoneId}' must be '{expectedKind}', but is '{zone.Kind}'.");
        }

        if (zone.OwnerSide != PlayerSide.Corp)
        {
            Add(violations, "servers.zone-owner-mismatch", path, $"Server zone '{zoneId}' must be Corp-owned.");
        }

        if (zone.ServerId != server.Id)
        {
            Add(violations, "servers.zone-reference-mismatch", path, $"Zone '{zoneId}' references server '{zone.ServerId}', not '{server.Id}'.");
        }

        if (!zoneReferences.TryAdd(zoneId.Value, (server.Id, propertyName)))
        {
            Add(violations, "servers.zone-reused", path, $"Server zone '{zoneId}' is reused by multiple server references.");
        }
    }

    private static void ValidateTurnAndOutcome(GameState state, List<StateInvariantViolation> violations)
    {
        TurnState turn = state.Turn;
        if (turn is null)
        {
            Add(violations, "turn.missing", "turn", "Turn state is required.");
        }
        else
        {
            if (!IsPlayerSide(turn.ActiveSide))
            {
                Add(violations, "turn.invalid-side", "turn.activeSide", $"'{turn.ActiveSide}' is not a valid active side.");
            }

            if (!Enum.IsDefined(turn.Phase))
            {
                Add(violations, "turn.invalid-phase", "turn.phase", $"'{turn.Phase}' is not a supported turn phase.");
            }

            if (turn.Phase == TurnPhase.Corp && turn.ActiveSide != PlayerSide.Corp)
            {
                Add(violations, "turn.phase-side-mismatch", "turn.activeSide", "Corp phase requires Corp to be the active side.");
            }

            if (turn.Phase == TurnPhase.Runner && turn.ActiveSide != PlayerSide.Runner)
            {
                Add(violations, "turn.phase-side-mismatch", "turn.activeSide", "Runner phase requires Runner to be the active side.");
            }

            if (turn.TurnNumber < 1)
            {
                Add(violations, "turn.invalid-number", "turn.turnNumber", "Turn number must be positive.");
            }
        }

        GameOutcome outcome = state.Outcome;
        if (outcome is null)
        {
            Add(violations, "outcome.missing", "outcome", "Outcome state is required.");
            return;
        }

        if (!Enum.IsDefined(outcome.Kind))
        {
            Add(violations, "outcome.invalid-kind", "outcome.kind", $"'{outcome.Kind}' is not a supported outcome.");
        }

        bool ongoing = outcome.Kind == GameOutcomeKind.Ongoing;
        if (ongoing && outcome.Winner.HasValue)
        {
            Add(violations, "outcome.ongoing-winner", "outcome.winner", "An ongoing game must not have a winner.");
        }

        if (ongoing && outcome.Reason.HasValue)
        {
            Add(violations, "outcome.ongoing-reason", "outcome.reason", "An ongoing game must not have a win reason.");
        }

        if (!ongoing && !outcome.Winner.HasValue)
        {
            Add(violations, "outcome.missing-winner", "outcome.winner", "A terminal game must identify its winner.");
        }

        if (!ongoing && !outcome.Reason.HasValue)
        {
            Add(violations, "outcome.missing-reason", "outcome.reason", "A terminal game must identify its win reason.");
        }

        if (outcome.Winner.HasValue && !IsPlayerSide(outcome.Winner.Value))
        {
            Add(violations, "outcome.invalid-winner", "outcome.winner", "Outcome winner must be Corp or Runner.");
        }

        if (outcome.Reason.HasValue && !Enum.IsDefined(outcome.Reason.Value))
        {
            Add(violations, "outcome.invalid-reason", "outcome.reason", $"'{outcome.Reason}' is not a supported win reason.");
        }

        if (outcome.Reason == GameWinReason.DeckOut && outcome.Winner != PlayerSide.Runner)
        {
            Add(violations, "outcome.deck-out-winner", "outcome.winner", "DeckOut must identify Runner as the winner in the M1 rules model.");
        }

        if (turn is not null && turn.Phase == TurnPhase.GameOver && ongoing)
        {
            Add(violations, "turn.outcome-mismatch", "turn.phase", "GameOver phase requires a terminal outcome.");
        }

        if (turn is not null && turn.Phase != TurnPhase.GameOver && !ongoing)
        {
            Add(violations, "turn.outcome-mismatch", "turn.phase", "A terminal outcome requires GameOver phase.");
        }
    }

    private static void ValidateResolutionAndDecision(GameState state, List<StateInvariantViolation> violations)
    {
        var playerIds = new HashSet<PlayerId>(
            state.Players.Where(static player => player is not null).Select(static player => player!.Id));
        var cardIds = new HashSet<CardInstanceId>(
            state.Cards.Where(static card => card is not null).Select(static card => card!.Id));

        for (int index = 0; index < state.ResolutionStack.Count; index++)
        {
            ResolutionFrame? frame = state.ResolutionStack[index];
            string path = $"resolutionStack[{index}]";
            foreach (StateInvariantViolation violation in ResolutionFrameValidator.Collect(frame, path))
            {
                violations.Add(violation);
            }

            switch (frame)
            {
                case ChooseNumberThenAdjustCreditsFrame chooseThenAdjust:
                    RequirePlayer(playerIds, chooseThenAdjust.Owner, $"{path}.owner", violations);
                    RequirePlayer(playerIds, chooseThenAdjust.Target, $"{path}.target", violations);
                    if (chooseThenAdjust.Stage == NestedNumberStage.OuterPrompt)
                    {
                        if (index != state.ResolutionStack.Count - 1)
                        {
                            Add(
                                violations,
                                "resolution.outer-prompt-stack",
                                path,
                                "An outer-prompt frame must be the top frame on the resolution stack.");
                        }
                    }
                    else if (chooseThenAdjust.Stage == NestedNumberStage.InnerPrompt)
                    {
                        if (index + 1 >= state.ResolutionStack.Count)
                        {
                            Add(
                                violations,
                                "resolution.child-missing",
                                path,
                                "A prompted nested number parent must be immediately followed by its child frame.");
                        }
                        else if (state.ResolutionStack[index + 1] is not ChooseNumberFrame child)
                        {
                            Add(
                                violations,
                                "resolution.child-missing",
                                path,
                                "A prompted nested number parent must be immediately followed by its child frame.");
                        }
                        else if (child.Owner != chooseThenAdjust.Owner
                            || child.Minimum != chooseThenAdjust.Minimum
                            || child.Maximum != chooseThenAdjust.Maximum
                            || child.Continuation != ResolutionContinuation.Complete
                            || child.AdjustmentTarget.HasValue
                            || child.CreditsPerChoice != 0)
                        {
                            Add(
                                violations,
                                "resolution.child-mismatch",
                                $"{path}",
                                "The nested number child must match its parent frame arguments.");
                        }
                    }

                    break;
                case ChooseNumberFrame chooseNumber:
                    RequirePlayer(playerIds, chooseNumber.Owner, $"{path}.owner", violations);
                    if (chooseNumber.AdjustmentTarget.HasValue)
                    {
                        RequirePlayer(playerIds, chooseNumber.AdjustmentTarget.Value, $"{path}.adjustmentTarget", violations);
                    }

                    break;
                case ChooseCardFrame chooseCard:
                    RequirePlayer(playerIds, chooseCard.Owner, $"{path}.owner", violations);
                    for (int optionIndex = 0; optionIndex < chooseCard.CardOptions.Count; optionIndex++)
                    {
                        CardInstanceId cardId = chooseCard.CardOptions[optionIndex];
                        if (!cardIds.Contains(cardId))
                        {
                            Add(
                                violations,
                                "resolution.unknown-card-option",
                                $"{path}.cardOptions[{optionIndex}]",
                                $"Card option '{cardId}' does not identify a card in the state.");
                        }
                    }

                    break;
                case AdjustCreditsFrame adjustCredits:
                    RequirePlayer(playerIds, adjustCredits.Target, $"{path}.target", violations);
                    break;
                case ToyTurnFrame toyTurn:
                    if (!playerIds.Contains(state.Players.FirstOrDefault(player => player?.Side == toyTurn.ActiveSide)?.Id ?? default))
                    {
                        Add(violations, "resolution.unknown-player", $"{path}.activeSide", $"Toy turn frame side '{toyTurn.ActiveSide}' has no player.");
                    }

                    if (state.Turn.ActiveSide != toyTurn.ActiveSide)
                    {
                        Add(violations, "resolution.turn-side-mismatch", $"{path}.activeSide", "Toy turn frame side must match the active turn side.");
                    }

                    if (toyTurn.Stage == ToyTurnStage.CorpTurnStartDraw && toyTurn.ActiveSide != PlayerSide.Corp)
                    {
                        Add(violations, "resolution.invalid-stage", $"{path}.stage", "A Corp start-draw stage must belong to Corp.");
                    }

                    if (toyTurn.Stage == ToyTurnStage.RunnerTurnStart && toyTurn.ActiveSide != PlayerSide.Runner)
                    {
                        Add(violations, "resolution.invalid-stage", $"{path}.stage", "A Runner turn-start stage must belong to Runner.");
                    }

                    break;
            }
        }

        PendingDecision? pending = state.PendingDecision;
        if (pending is null)
        {
            if (state.ResolutionStack.Any(IsPromptFrame))
            {
                Add(
                    violations,
                    "resolution.pending-missing",
                    "resolutionStack",
                    "A prompt frame must have a matching pending decision.");
            }
        }
        else
        {
            ValidatePendingDecision(state, pending, playerIds, cardIds, violations);
        }

        if (state.Outcome.Kind == GameOutcomeKind.Won)
        {
            if (pending is not null)
            {
                Add(violations, "outcome.pending-decision", "pendingDecision", "A terminal game must not expose a pending decision.");
            }

            if (state.ResolutionStack.Count > 0)
            {
                Add(violations, "outcome.resolution-frames", "resolutionStack", "A terminal game must not retain resolution frames.");
            }

        }
    }

    private static void ValidatePendingDecision(
        GameState state,
        PendingDecision pending,
        HashSet<PlayerId> playerIds,
        HashSet<CardInstanceId> cardIds,
        List<StateInvariantViolation> violations)
    {
        const string path = "pendingDecision";
        if (!StableIdentifier.TryValidate(pending.Id.Value))
        {
            Add(violations, "id.invalid", $"{path}.id", "Decision ID is empty or invalid.");
        }

        if (pending.Revision <= 0)
        {
            Add(violations, "decision.invalid-revision", $"{path}.revision", "Decision revision must be positive.");
        }
        else if (pending.Revision != state.DecisionRevision)
        {
            Add(
                violations,
                "decision.revision-mismatch",
                $"{path}.revision",
                $"Pending revision {pending.Revision} must match state decision revision {state.DecisionRevision}.");
        }

        if (!StableIdentifier.TryValidate(pending.Owner.Value))
        {
            Add(violations, "id.invalid", $"{path}.owner", "Decision owner ID is empty or invalid.");
        }
        else if (!playerIds.Contains(pending.Owner))
        {
            Add(
                violations,
                "decision.unknown-owner",
                $"{path}.owner",
                $"Decision owner '{pending.Owner}' is not one of the players in the state.");
        }

        if (!Enum.IsDefined(pending.Kind))
        {
            Add(violations, "decision.invalid-kind", $"{path}.kind", $"'{pending.Kind}' is not a supported decision kind.");
        }

        if (string.IsNullOrWhiteSpace(pending.Prompt))
        {
            Add(violations, "decision.empty-prompt", $"{path}.prompt", "A pending decision prompt must be non-empty.");
        }

        var actionIds = new HashSet<ActionId>();
        if (pending.LegalActions.Count == 0)
        {
            Add(violations, "decision.empty-actions", $"{path}.legalActions", "A pending decision must contain at least one legal action.");
        }

        ResolutionFrame? topFrame = state.ResolutionStack.Count == 0 ? null : state.ResolutionStack[^1];
        if (!IsPromptFrame(topFrame))
        {
            Add(
                violations,
                "resolution.pending-frame-mismatch",
                $"{path}",
                "A pending decision must correspond to the top prompt frame on the resolution stack.");
        }
        else
        {
            switch (topFrame)
            {
                case ChooseNumberThenAdjustCreditsFrame outerNumber
                    when outerNumber.Stage == NestedNumberStage.OuterPrompt
                    && pending.Kind != DecisionKind.ChooseNumber:
                    Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "An outer number frame requires a ChooseNumber decision.");
                    break;
                case ChooseNumberThenAdjustCreditsFrame outerOwnerFrame
                    when outerOwnerFrame.Stage == NestedNumberStage.OuterPrompt
                    && pending.Owner != outerOwnerFrame.Owner:
                    Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Decision owner must match the outer number-choice frame owner.");
                    break;
                case ChooseNumberFrame numberFrame when pending.Kind != DecisionKind.ChooseNumber:
                    Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A number-choice frame requires a ChooseNumber decision.");
                    break;
                case ChooseCardFrame cardFrame when pending.Kind != DecisionKind.ChooseCard:
                    Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A card-choice frame requires a ChooseCard decision.");
                    break;
                case ToyTurnFrame toyTurnOwner when toyTurnOwner.Stage == ToyTurnStage.AwaitingAction
                    && pending.Kind != DecisionKind.ToyTurn:
                    Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A toy-turn frame requires a ToyTurn decision.");
                    break;
                case ChooseNumberFrame numberOwnerFrame when pending.Owner != numberOwnerFrame.Owner:
                    Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Decision owner must match the top number-choice frame owner.");
                    break;
                case ChooseCardFrame cardOwnerFrame when pending.Owner != cardOwnerFrame.Owner:
                    Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Decision owner must match the top card-choice frame owner.");
                    break;
                case ToyTurnFrame toyOwnerFrame when toyOwnerFrame.Stage == ToyTurnStage.AwaitingAction
                    && pending.Owner != state.Players.FirstOrDefault(player => player?.Side == toyOwnerFrame.ActiveSide)?.Id:
                    Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Toy-turn decision owner must be the active side's player.");
                    break;
            }
        }

        var numberChoices = new HashSet<int>();
        var cardChoices = new HashSet<CardInstanceId>();
        for (int index = 0; index < pending.LegalActions.Count; index++)
        {
            LegalAction? action = pending.LegalActions[index];
            string actionPath = $"{path}.legalActions[{index}]";
            if (action is null)
            {
                Add(violations, "decision.null-action", actionPath, "Legal actions must not contain null entries.");
                continue;
            }

            if (!StableIdentifier.TryValidate(action.Id.Value))
            {
                Add(violations, "id.invalid", $"{actionPath}.id", "Action ID is empty or invalid.");
            }
            else if (!actionIds.Add(action.Id))
            {
                Add(violations, "decision.duplicate-action", $"{actionPath}.id", $"Action ID '{action.Id}' occurs more than once.");
            }

            if (!Enum.IsDefined(action.Kind) || action.Kind == ActionKind.Legacy)
            {
                Add(violations, "decision.invalid-action-kind", $"{actionPath}.kind", "Authoritative decisions may contain only registered typed action kinds.");
            }

            if (string.IsNullOrWhiteSpace(action.Description))
            {
                Add(violations, "decision.empty-action-description", $"{actionPath}.description", "A legal action description must be non-empty.");
            }

            if (action.Parameters is null)
            {
                Add(violations, "decision.missing-action-parameters", $"{actionPath}.parameters", "Every legal action must contain typed parameters.");
                continue;
            }

            switch (action.Kind, action.Parameters)
            {
                case (ActionKind.ChooseNumber, NumberChoiceParameters numberChoice):
                    if (TryGetNumberRange(topFrame, out int minimum, out int maximum))
                    {
                        if (numberChoice.Value < minimum || numberChoice.Value > maximum)
                        {
                            Add(violations, "decision.action-out-of-range", $"{actionPath}.parameters.value", "Number action is outside the prompt frame range.");
                        }
                        else if (!numberChoices.Add(numberChoice.Value))
                        {
                            Add(violations, "decision.duplicate-choice", $"{actionPath}.parameters.value", $"Number choice '{numberChoice.Value}' occurs more than once.");
                        }
                    }

                    break;
                case (ActionKind.ChooseCard, CardChoiceParameters cardChoice):
                    if (!cardIds.Contains(cardChoice.CardId))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.cardId", $"Card action references unknown card '{cardChoice.CardId}'.");
                    }

                    if (topFrame is ChooseCardFrame chooseCard && !chooseCard.CardOptions.Contains(cardChoice.CardId))
                    {
                        Add(violations, "decision.card-action-not-offered", $"{actionPath}.parameters.cardId", "Card action is not one of the materialized frame options.");
                    }
                    else if (topFrame is ChooseCardFrame && !cardChoices.Add(cardChoice.CardId))
                    {
                        Add(violations, "decision.duplicate-card-choice", $"{actionPath}.parameters.cardId", $"Card choice '{cardChoice.CardId}' occurs more than once.");
                    }

                    break;
                case (ActionKind.GainCredit or ActionKind.DrawCard or ActionKind.EndTurn, NoActionParameters):
                    if (pending.Kind != DecisionKind.ToyTurn)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Toy actions require a ToyTurn decision.");
                    }

                    break;
                case (ActionKind.InstallCard or ActionKind.AdvanceAgenda or ActionKind.ScoreAgenda or ActionKind.StealAgenda, CardChoiceParameters cardChoice):
                    if (pending.Kind != DecisionKind.ToyTurn)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Toy actions require a ToyTurn decision.");
                    }

                    if (!cardIds.Contains(cardChoice.CardId))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.cardId", $"Card action references unknown card '{cardChoice.CardId}'.");
                    }

                    break;
                case (ActionKind.ChooseNumber, _):
                case (ActionKind.ChooseCard, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", $"Action kind '{action.Kind}' has the wrong typed parameter payload.");
                    break;
                default:
                    Add(violations, "decision.invalid-action-kind", $"{actionPath}.kind", $"Action kind '{action.Kind}' is not supported.");
                    break;
            }

            if ((pending.Kind == DecisionKind.ChooseNumber && action.Kind != ActionKind.ChooseNumber)
                || (pending.Kind == DecisionKind.ChooseCard && action.Kind != ActionKind.ChooseCard)
                || (pending.Kind == DecisionKind.ToyTurn
                    && action.Kind is not (ActionKind.GainCredit
                        or ActionKind.DrawCard
                        or ActionKind.InstallCard
                        or ActionKind.AdvanceAgenda
                        or ActionKind.ScoreAgenda
                        or ActionKind.StealAgenda
                        or ActionKind.EndTurn)))
            {
                Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", $"Action kind '{action.Kind}' does not match decision kind '{pending.Kind}'.");
            }
        }

        if (TryGetNumberRange(topFrame, out int numberMinimum, out int numberMaximum))
        {
            ValidateNumberActionBijection(
                pending,
                numberChoices,
                numberMinimum,
                numberMaximum,
                path,
                violations);
        }

        if (topFrame is ChooseCardFrame cardPromptFrame)
        {
            ValidateCardActionBijection(pending, cardChoices, cardPromptFrame.CardOptions, path, violations);
        }

        if (pending.Kind == DecisionKind.ToyTurn
            && topFrame is ToyTurnFrame { Stage: ToyTurnStage.AwaitingAction } toyTurn)
        {
            ValidateToyActions(state, pending, toyTurn, path, violations);
        }
    }

    private static void ValidateToyActions(
        GameState state,
        PendingDecision pending,
        ToyTurnFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        PlayerState? player = state.Players.FirstOrDefault(candidate => candidate?.Side == frame.ActiveSide);
        if (player is null)
        {
            return;
        }

        ZoneKind handKind = frame.ActiveSide == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneState? hand = state.Zones.FirstOrDefault(zone => zone?.Kind == handKind && zone.ServerId is null);
        ZoneState? stack = state.Zones.FirstOrDefault(zone => zone?.Kind ==
            (frame.ActiveSide == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack)
            && zone.ServerId is null);
        ZoneState? remoteRoot = state.Servers
            .Where(server => server?.Kind == ServerKind.Remote)
            .OrderBy(server => server!.Id.Value, StringComparer.Ordinal)
            .Select(server => state.Zones.FirstOrDefault(zone => zone?.Id == server!.RootZoneId))
            .FirstOrDefault(zone => zone is not null);

        if (hand is null || stack is null)
        {
            return;
        }

        IReadOnlyList<ToyActionSpec> expected = ToyActionRules.Expected(
            frame.ActiveSide,
            player,
            hand,
            stack,
            remoteRoot,
            state.Cards);
        var actual = new List<ToyActionSpec>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            if (action.Kind is ActionKind.GainCredit
                or ActionKind.DrawCard
                or ActionKind.EndTurn)
            {
                if (action.Parameters is NoActionParameters)
                {
                    actual.Add(new ToyActionSpec(action.Kind, null));
                }
                else
                {
                    Add(violations, "decision.action-parameters-mismatch", $"{path}.legalActions", $"Toy action '{action.Kind}' requires a no-parameter payload.");
                }
            }
            else if (action.Kind is ActionKind.InstallCard
                or ActionKind.AdvanceAgenda
                or ActionKind.ScoreAgenda
                or ActionKind.StealAgenda)
            {
                if (action.Parameters is CardChoiceParameters cardChoice)
                {
                    actual.Add(new ToyActionSpec(action.Kind, cardChoice.CardId));
                }
                else
                {
                    Add(violations, "decision.action-parameters-mismatch", $"{path}.legalActions", $"Toy action '{action.Kind}' requires a typed card target.");
                }
            }
        }

        var expectedCounts = expected.GroupBy(spec => spec).ToDictionary(group => group.Key, group => group.Count());
        var actualCounts = actual.GroupBy(spec => spec).ToDictionary(group => group.Key, group => group.Count());
        foreach ((ToyActionSpec spec, int count) in expectedCounts)
        {
            actualCounts.TryGetValue(spec, out int actualCount);
            if (actualCount != count)
            {
                Add(
                    violations,
                    actualCount == 0 ? "decision.missing-toy-action" : "decision.toy-action-count",
                    $"{path}.legalActions",
                    $"Toy decision requires exactly {count} '{spec.Kind}' action(s) for target '{spec.Target?.ToString() ?? "none"}', but contains {actualCount}.");
            }
        }

        foreach ((ToyActionSpec spec, int actualCount) in actualCounts)
        {
            if (!expectedCounts.TryGetValue(spec, out int expectedCount))
            {
                Add(
                    violations,
                    "decision.extra-toy-action",
                    $"{path}.legalActions",
                    $"Toy decision contains unoffered '{spec.Kind}' action for target '{spec.Target?.ToString() ?? "none"}'.");
            }
            else if (actualCount > expectedCount)
            {
                Add(
                    violations,
                    "decision.duplicate-toy-action",
                    $"{path}.legalActions",
                    $"Toy decision contains {actualCount} copies of '{spec.Kind}' for target '{spec.Target?.ToString() ?? "none"}', expected {expectedCount}.");
            }
        }
    }

    private static bool IsPromptFrame(ResolutionFrame? frame) =>
        frame is ChooseNumberFrame
        || frame is ChooseNumberThenAdjustCreditsFrame { Stage: NestedNumberStage.OuterPrompt }
        || frame is ChooseCardFrame
        || frame is ToyTurnFrame { Stage: ToyTurnStage.AwaitingAction };

    private static bool TryGetNumberRange(ResolutionFrame? frame, out int minimum, out int maximum)
    {
        switch (frame)
        {
            case ChooseNumberFrame chooseNumber:
                minimum = chooseNumber.Minimum;
                maximum = chooseNumber.Maximum;
                return true;
            case ChooseNumberThenAdjustCreditsFrame
            {
                Stage: NestedNumberStage.OuterPrompt,
                Minimum: var outerMinimum,
                Maximum: var outerMaximum,
            }:
                minimum = outerMinimum;
                maximum = outerMaximum;
                return true;
            default:
                minimum = default;
                maximum = default;
                return false;
        }
    }

    private static void ValidateNumberActionBijection(
        PendingDecision pending,
        HashSet<int> choices,
        int minimum,
        int maximum,
        string path,
        List<StateInvariantViolation> violations)
    {
        long expectedCount = (long)maximum - minimum + 1;
        if (pending.LegalActions.Count != expectedCount)
        {
            Add(
                violations,
                "decision.action-count-mismatch",
                $"{path}.legalActions",
                $"Number decision must contain exactly {expectedCount} actions, but contains {pending.LegalActions.Count}.");
        }

        // ResolutionFrameValidator reports these malformed ranges. Do not
        // expand an attacker-controlled or corrupted range while producing
        // diagnostics for the pending decision.
        if (expectedCount <= 0 || expectedCount > 65)
        {
            return;
        }

        for (long value = minimum; value <= maximum; value++)
        {
            if (!choices.Contains((int)value))
            {
                Add(
                    violations,
                    "decision.missing-choice",
                    $"{path}.legalActions",
                    $"Number decision is missing choice '{value}'.");
            }
        }
    }

    private static void ValidateCardActionBijection(
        PendingDecision pending,
        HashSet<CardInstanceId> choices,
        IReadOnlyList<CardInstanceId> options,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (pending.LegalActions.Count != options.Count)
        {
            Add(
                violations,
                "decision.action-count-mismatch",
                $"{path}.legalActions",
                $"Card decision must contain exactly {options.Count} actions, but contains {pending.LegalActions.Count}.");
        }

        foreach (CardInstanceId cardId in options)
        {
            if (!choices.Contains(cardId))
            {
                Add(
                    violations,
                    "decision.missing-card-choice",
                    $"{path}.legalActions",
                    $"Card decision is missing option '{cardId}'.");
            }
        }
    }

    private static void RequirePlayer(
        HashSet<PlayerId> playerIds,
        PlayerId playerId,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (!playerIds.Contains(playerId))
        {
            Add(violations, "resolution.unknown-player", path, $"Resolution frame references unknown player '{playerId}'.");
        }
    }

    private static void ValidateAllocatorPosition(GameState state, List<StateInvariantViolation> violations)
    {
        if (state.Allocator is null)
        {
            return;
        }

        ulong maximumIssuedSequence = 0;
        bool foundIssuedSequence = false;

        Observe(state.GameId.Value, "game");
        foreach (CardInstanceState? card in state.Cards)
        {
            if (card is not null)
            {
                Observe(card.Id.Value, "card");
            }
        }

        foreach (PlayerState? player in state.Players)
        {
            if (player is not null)
            {
                Observe(player.Id.Value, "player");
            }
        }

        foreach (ServerState? server in state.Servers)
        {
            if (server is not null)
            {
                Observe(server.Id.Value, "server");
            }
        }

        foreach (ZoneState? zone in state.Zones)
        {
            if (zone is not null)
            {
                Observe(zone.Id.Value, "zone");
            }
        }

        if (state.PendingDecision is not null)
        {
            Observe(state.PendingDecision.Id.Value, "decision");
            foreach (LegalAction? action in state.PendingDecision.LegalActions)
            {
                if (action is not null)
                {
                    Observe(action.Id.Value, "action");
                }
            }
        }

        if (foundIssuedSequence && maximumIssuedSequence == ulong.MaxValue)
        {
            Add(
                violations,
                "allocator.exhausted",
                "allocator.nextSequence",
                "An allocator-issued ID uses the maximum UInt64 sequence; no strictly greater sequence can be restored.");
        }
        else if (foundIssuedSequence && state.Allocator.NextSequence <= maximumIssuedSequence)
        {
            Add(
                violations,
                "allocator.behind",
                "allocator.nextSequence",
                $"Next sequence {state.Allocator.NextSequence} must be greater than the maximum issued sequence {maximumIssuedSequence}.");
        }

        void Observe(string? value, string prefix)
        {
            if (!TryParseIssuedSequence(value, prefix, out ulong sequence))
            {
                return;
            }

            if (!foundIssuedSequence || sequence > maximumIssuedSequence)
            {
                maximumIssuedSequence = sequence;
                foundIssuedSequence = true;
            }
        }
    }

    private static bool TryParseIssuedSequence(string? value, string prefix, out ulong sequence)
    {
        sequence = 0;
        if (value is null)
        {
            return false;
        }

        string marker = prefix + ":";
        if (!value.StartsWith(marker, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = value.AsSpan(marker.Length);
        if (digits.IsEmpty)
        {
            return false;
        }

        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9')
            {
                return false;
            }
        }

        return ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
    }

    private static bool IsPlayerSide(PlayerSide side) =>
        side is PlayerSide.Corp or PlayerSide.Runner;

    private static void Add(
        List<StateInvariantViolation> violations,
        string code,
        string path,
        string message) =>
        violations.Add(new StateInvariantViolation(code, path, message));
}
