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
        ValidateRunState(state, violations);
        ValidateRulesState(state, violations);
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

        if (outcome.Reason == GameWinReason.Flatline && outcome.Winner != PlayerSide.Corp)
        {
            Add(violations, "outcome.flatline-winner", "outcome.winner", "Flatline must identify Corp as the winner.");
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

    private static void ValidateRunState(GameState state, List<StateInvariantViolation> violations)
    {
        RunState? run = state.RunState;
        bool hasRunFrame = state.ResolutionStack.Any(static frame => frame is RunTimingFrame);
        if (run is null)
        {
            if (hasRunFrame)
            {
                Add(violations, "run.frame-without-state", "resolutionStack", "A run timing frame cannot exist without live run state.");
            }

            return;
        }

        const string path = "runState";
        if (run.Version != 1)
        {
            Add(violations, "run.unsupported-version", $"{path}.version", $"Run state version {run.Version} is not supported; expected 1.");
        }

        if (!StableIdentifier.TryValidate(run.Id.Value))
        {
            Add(violations, "id.invalid", $"{path}.id", "Run ID is empty or invalid.");
        }

        if (!StableIdentifier.TryValidate(run.AttackedServer.Value))
        {
            Add(violations, "id.invalid", $"{path}.attackedServer", "Attacked server ID is empty or invalid.");
        }
        else if (!state.Servers.Any(server => server?.Id == run.AttackedServer))
        {
            Add(violations, "run.unknown-server", $"{path}.attackedServer", $"Run references unknown server '{run.AttackedServer}'.");
        }

        if (!IsPlayerSide(run.InitiatingSide))
        {
            Add(violations, "run.invalid-initiating-side", $"{path}.initiatingSide", "Run initiating side must be Corp or Runner.");
        }
        else if (run.InitiatingSide != PlayerSide.Runner)
        {
            Add(violations, "run.invalid-initiating-side", $"{path}.initiatingSide", "This slice supports only Runner-initiated runs.");
        }

        PlayerState? initiatingPlayer = state.Players.FirstOrDefault(player => player?.Id == run.InitiatingPlayer);
        if (initiatingPlayer is null)
        {
            Add(violations, "run.unknown-initiating-player", $"{path}.initiatingPlayer", $"Run references unknown player '{run.InitiatingPlayer}'.");
        }
        else if (initiatingPlayer.Side != run.InitiatingSide)
        {
            Add(violations, "run.initiating-player-side", $"{path}.initiatingPlayer", "Run initiating player side must match initiatingSide.");
        }

        if (!Enum.IsDefined(run.Phase))
        {
            Add(violations, "run.invalid-phase", $"{path}.phase", $"'{run.Phase}' is not a supported CR run phase.");
        }

        if (!Enum.IsDefined(run.Step))
        {
            Add(violations, "run.invalid-step", $"{path}.step", $"'{run.Step}' is not a supported CR 6.9 timing step.");
        }
        else if (run.Phase != ExpectedRunPhase(run.Step))
        {
            Add(violations, "run.phase-step-mismatch", $"{path}.phase", $"Phase '{run.Phase}' does not match timing step '{run.Step}'.");
        }

        if (run.Successful && run.Unsuccessful)
        {
            Add(violations, "run.conflicting-result", $"{path}", "A run cannot be successful and unsuccessful at the same time.");
        }

        if (run.Step == RunTimingStep.RunEnd && !run.EndReason.HasValue)
        {
            Add(violations, "run.missing-end-reason", $"{path}.endReason", "Run-end cleanup requires an explicit end reason.");
        }

        if (run.Step != RunTimingStep.RunEnd && run.EndReason.HasValue)
        {
            Add(violations, "run.unexpected-end-reason", $"{path}.endReason", "A live run before cleanup must not have an end reason.");
        }

        if (run.EndReason.HasValue && !Enum.IsDefined(run.EndReason.Value))
        {
            Add(violations, "run.invalid-end-reason", $"{path}.endReason", $"'{run.EndReason}' is not a supported run-end reason.");
        }

        if (run.RandomAccessLimit < 0)
        {
            Add(violations, "run.negative-access-limit", $"{path}.randomAccessLimit", "Random access limit must be nonnegative.");
        }

        if (run.RunCredits < 0)
        {
            Add(violations, "run.negative-credits", $"{path}.runCredits", "Run credits must be nonnegative.");
        }

        if (!Enum.IsDefined(run.AccessState))
        {
            Add(violations, "run.invalid-access-state", $"{path}.accessState", $"'{run.AccessState}' is not a supported access state.");
        }

        ServerState? server = state.Servers.FirstOrDefault(candidate => candidate?.Id == run.AttackedServer);
        ZoneState? iceZone = server is null
            ? null
            : state.Zones.FirstOrDefault(zone => zone?.Id == server.IceZoneId);
        if (iceZone is null)
        {
            Add(violations, "run.missing-ice-zone", $"{path}.attackedServer", "A run requires the attacked server's ICE zone.");
        }
        else
        {
            if (run.CurrentIceIndex < -1 || run.CurrentIceIndex >= iceZone.CardIds.Count)
            {
                Add(violations, "run.invalid-ice-index", $"{path}.currentIceIndex", "Current ICE index must be -1 or an index in the attacked server's ordered ICE zone.");
            }

            if (run.CurrentIceIndex < 0)
            {
                if (run.CurrentIceId.HasValue)
                {
                    Add(violations, "run.unexpected-current-ice", $"{path}.currentIceId", "A run at the server must not have a current ICE ID.");
                }
            }
            else if (!run.CurrentIceId.HasValue || iceZone.CardIds[run.CurrentIceIndex] != run.CurrentIceId.Value)
            {
                Add(violations, "run.ice-position-mismatch", $"{path}.currentIceId", "Current ICE ID must match the ordered ICE zone position.");
            }

            if (run.Step is RunTimingStep.Success or RunTimingStep.BreachBoundary
                && run.CurrentIceIndex >= 0)
            {
                Add(violations, "run.success-before-server", $"{path}.currentIceIndex", "Success and breach may occur only after the run reaches the server.");
            }
        }

        ValidateRunIdList(run.BreachCandidates, "breachCandidates");
        ValidateRunIdList(run.ChosenAccessCards, "chosenAccessCards");
        ValidateRunIdList(run.AccessedCards, "accessedCards");

        var effectIds = new HashSet<RunEffectId>();
        for (int index = 0; index < run.LingeringEffects.Count; index++)
        {
            RunLingeringEffect? effect = run.LingeringEffects[index];
            string effectPath = $"{path}.lingeringEffects[{index}]";
            if (effect is null)
            {
                Add(violations, "run.null-effect", effectPath, "Lingering effects must not contain null entries.");
                continue;
            }

            if (effect.Version != 1)
            {
                Add(violations, "run.effect-unsupported-version", $"{effectPath}.version", "Only lingering-effect version 1 is supported.");
            }

            if (!StableIdentifier.TryValidate(effect.Id.Value))
            {
                Add(violations, "id.invalid", $"{effectPath}.id", "Lingering effect ID is empty or invalid.");
            }
            else if (!effectIds.Add(effect.Id))
            {
                Add(violations, "run.duplicate-effect", $"{effectPath}.id", $"Lingering effect ID '{effect.Id}' is duplicated.");
            }

            if (!StableIdentifier.TryValidate(effect.BehaviorId))
            {
                Add(violations, "run.invalid-effect-behavior", $"{effectPath}.behaviorId", "Lingering effect behavior ID must be non-empty and stable.");
            }

            if (!Enum.IsDefined(effect.Expiry))
            {
                Add(violations, "run.invalid-effect-expiry", $"{effectPath}.expiry", $"'{effect.Expiry}' is not supported inside RunState.");
            }
        }

        if (run.Priority is null)
        {
            if (IsRunPriorityStep(run.Step))
            {
                Add(violations, "run.missing-priority", $"{path}.priority", "Each run paid window requires priority state.");
            }
        }
        else
        {
            ValidateRunPriority(run, path, violations);
        }

        if (run.Encounter is not null)
        {
            ValidateRunEncounter(state, run, path, violations);
        }

        int runFrameCount = state.ResolutionStack.Count(frame => frame is RunTimingFrame timing && timing.RunId == run.Id);
        if (runFrameCount != 1)
        {
            Add(violations, "run.frame-count", "resolutionStack", $"Live run '{run.Id}' requires exactly one matching RunTimingFrame; found {runFrameCount}.");
        }

        void ValidateRunIdList(IReadOnlyList<CardInstanceId> ids, string property)
        {
            var seen = new HashSet<CardInstanceId>();
            for (int index = 0; index < ids.Count; index++)
            {
                CardInstanceId id = ids[index];
                string idPath = $"{path}.{property}[{index}]";
                if (!StableIdentifier.TryValidate(id.Value))
                {
                    Add(violations, "id.invalid", idPath, "Run card ID is empty or invalid.");
                }
                else if (!seen.Add(id))
                {
                    Add(violations, "run.duplicate-card", idPath, $"Run card ID '{id}' occurs more than once.");
                }

                if (!state.Cards.Any(card => card?.Id == id))
                {
                    Add(violations, "run.unknown-card", idPath, $"Run references unknown card '{id}'.");
                }
            }
        }
    }

    private static void ValidateRunPriority(RunState run, string path, List<StateInvariantViolation> violations)
    {
        RunPriorityState priority = run.Priority!;
        if (priority.Version != 1)
        {
            Add(violations, "run.priority-unsupported-version", $"{path}.priority.version", "Only priority state version 1 is supported.");
        }

        if (!Enum.IsDefined(priority.Window))
        {
            Add(violations, "run.priority-invalid-window", $"{path}.priority.window", $"'{priority.Window}' is not supported.");
        }

        if (!IsPlayerSide(priority.PriorityHolder))
        {
            Add(violations, "run.priority-invalid-holder", $"{path}.priority.priorityHolder", "Priority holder must be Corp or Runner.");
        }

        if (priority.ConsecutivePasses is < 0 or > 1)
        {
            Add(violations, "run.priority-invalid-passes", $"{path}.priority.consecutivePasses", "A live paid window may have zero or one consecutive pass.");
        }

        if (!IsRunPriorityStep(run.Step))
        {
            Add(violations, "run.unexpected-priority", $"{path}.priority", "Priority state is only valid during a paid run window.");
            return;
        }

        RunPriorityWindowKind expectedWindow = run.Step switch
        {
            RunTimingStep.InitiationPaidWindow => RunPriorityWindowKind.InitiationPaid,
            RunTimingStep.ApproachPaidWindow => RunPriorityWindowKind.ApproachPaid,
            RunTimingStep.EncounterPaidWindow => RunPriorityWindowKind.EncounterPaid,
            RunTimingStep.MovementPaidWindow => RunPriorityWindowKind.MovementPaid,
            RunTimingStep.AfterMovePaidWindow => RunPriorityWindowKind.AfterMovePaid,
            _ => priority.Window,
        };
        if (priority.Window != expectedWindow)
        {
            Add(violations, "run.priority-window-mismatch", $"{path}.priority.window", "Priority window does not match the run timing step.");
        }

        bool expectedApproachedRez = run.Step == RunTimingStep.ApproachPaidWindow
            && run.CurrentIceId.HasValue;
        if (priority.CorpMayRezApproachedIce && !expectedApproachedRez)
        {
            Add(violations, "run.invalid-approach-rez-capability", $"{path}.priority.corpMayRezApproachedIce", "Approached-ICE rez permission is only legal in the approach paid window.");
        }

        bool expectedNonIceRez = run.Step is RunTimingStep.InitiationPaidWindow
            or RunTimingStep.ApproachPaidWindow
            or RunTimingStep.AfterMovePaidWindow;
        if (priority.CorpMayRezNonIce && !expectedNonIceRez)
        {
            Add(violations, "run.invalid-non-ice-rez-capability", $"{path}.priority.corpMayRezNonIce", "Non-ICE rez permission is not legal in this run window.");
        }
    }

    private static void ValidateRunEncounter(GameState state, RunState run, string path, List<StateInvariantViolation> violations)
    {
        RunEncounterState encounter = run.Encounter!;
        if (encounter.Version != 1)
        {
            Add(violations, "run.encounter-unsupported-version", $"{path}.encounter.version", "Only encounter state version 1 is supported.");
        }

        if (!StableIdentifier.TryValidate(encounter.Id.Value))
        {
            Add(violations, "id.invalid", $"{path}.encounter.id", "Encounter ID is empty or invalid.");
        }

        if (!StableIdentifier.TryValidate(encounter.IceId.Value))
        {
            Add(violations, "id.invalid", $"{path}.encounter.iceId", "Encounter ICE ID is empty or invalid.");
        }

        if (!Enum.IsDefined(encounter.Status))
        {
            Add(violations, "run.encounter-invalid-status", $"{path}.encounter.status", $"'{encounter.Status}' is not supported.");
        }

        if (run.CurrentIceId != encounter.IceId)
        {
            Add(violations, "run.encounter-ice-mismatch", $"{path}.encounter.iceId", "Encounter ICE must be the current approached ICE.");
        }

        var subroutineIds = new HashSet<SubroutineId>();
        var ordinals = new HashSet<int>();
        for (int index = 0; index < encounter.Subroutines.Count; index++)
        {
            RunSubroutineState? subroutine = encounter.Subroutines[index];
            string subroutinePath = $"{path}.encounter.subroutines[{index}]";
            if (subroutine is null)
            {
                Add(violations, "run.null-subroutine", subroutinePath, "Encounter subroutines must not contain null entries.");
                continue;
            }

            if (subroutine.Version != 1)
            {
                Add(violations, "run.subroutine-unsupported-version", $"{subroutinePath}.version", "Only subroutine state version 1 is supported.");
            }

            if (!StableIdentifier.TryValidate(subroutine.Id.Value))
            {
                Add(violations, "id.invalid", $"{subroutinePath}.id", "Subroutine ID is empty or invalid.");
            }
            else if (!subroutineIds.Add(subroutine.Id))
            {
                Add(violations, "run.duplicate-subroutine", $"{subroutinePath}.id", $"Subroutine ID '{subroutine.Id}' is duplicated.");
            }

            if (subroutine.Ordinal < 0 || !ordinals.Add(subroutine.Ordinal))
            {
                Add(violations, "run.invalid-subroutine-ordinal", $"{subroutinePath}.ordinal", "Subroutine ordinals must be unique and nonnegative.");
            }

            if (subroutine.Broken && subroutine.Resolved)
            {
                Add(violations, "run.subroutine-state", subroutinePath, "A broken subroutine cannot also be unresolved.");
            }
        }

        if ((run.Step is RunTimingStep.EncounterPaidWindow or RunTimingStep.Encounter)
            && encounter.Status != RunEncounterStatus.Initialized)
        {
            Add(violations, "run.encounter-status-mismatch", $"{path}.encounter.status", "Encounter paid/initialization timing requires an initialized encounter.");
        }
    }

    private static PlayerId RunDecisionOwner(GameState state, RunState run)
    {
        if (run.Step == RunTimingStep.JackOutDecision)
        {
            return run.InitiatingPlayer;
        }

        RunPriorityState priority = run.Priority
            ?? throw new InvalidOperationException("A run priority decision requires priority state.");
        return state.Players.Single(player => player.Side == priority.PriorityHolder).Id;
    }

    private static bool IsRunPriorityStep(RunTimingStep step) => step is
        RunTimingStep.InitiationPaidWindow
        or RunTimingStep.ApproachPaidWindow
        or RunTimingStep.EncounterPaidWindow
        or RunTimingStep.MovementPaidWindow
        or RunTimingStep.AfterMovePaidWindow;

    private static bool IsRunPromptStep(RunTimingStep step) => IsRunPriorityStep(step)
        || step == RunTimingStep.JackOutDecision;

    private static RunCrPhase ExpectedRunPhase(RunTimingStep step) => step switch
    {
        RunTimingStep.InitiationPaidWindow => RunCrPhase.Initiation,
        RunTimingStep.Approach or RunTimingStep.ApproachPaidWindow => RunCrPhase.Approach,
        RunTimingStep.EncounterInitialization or RunTimingStep.EncounterPaidWindow or RunTimingStep.Encounter => RunCrPhase.Encounter,
        RunTimingStep.MovementPaidWindow or RunTimingStep.JackOutDecision or RunTimingStep.MoveInward or RunTimingStep.AfterMovePaidWindow => RunCrPhase.Movement,
        RunTimingStep.Success or RunTimingStep.BreachBoundary => RunCrPhase.Success,
        RunTimingStep.RunEnd => RunCrPhase.RunEnd,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown run timing step."),
    };

    private static void ValidateRulesState(GameState state, List<StateInvariantViolation> violations)
    {
        RulesState? rules = state.RulesState;
        if (rules is null)
        {
            return;
        }

        const string path = "rulesState";
        if (rules.Version != 1)
        {
            Add(violations, "rules.unsupported-version", $"{path}.version", "Only M2 rules state version 1 is supported.");
        }

        var statePlayers = new Dictionary<PlayerId, PlayerState>();
        foreach (PlayerState? player in state.Players)
        {
            if (player is not null)
            {
                _ = statePlayers.TryAdd(player.Id, player);
            }
        }
        var rulePlayerIds = new HashSet<PlayerId>();
        var calculatedMemory = new Dictionary<PlayerId, long>();
        for (int index = 0; index < rules.Players.Count; index++)
        {
            PlayerRulesState? player = rules.Players[index];
            string playerPath = $"{path}.players[{index}]";
            if (player is null)
            {
                Add(violations, "rules.null-player", playerPath, "Rules player entries must not be null.");
                continue;
            }

            RequireStableId(player.PlayerId.Value, $"{playerPath}.playerId", "player", violations);
            if (!rulePlayerIds.Add(player.PlayerId))
            {
                Add(violations, "rules.duplicate-player", $"{playerPath}.playerId", $"Rules state contains duplicate player '{player.PlayerId}'.");
            }

            if (!statePlayers.ContainsKey(player.PlayerId))
            {
                Add(violations, "rules.unknown-player", $"{playerPath}.playerId", $"Rules state references unknown player '{player.PlayerId}'.");
            }

            if (player.BaseHandSize < 0)
            {
                Add(violations, "rules.negative-hand-size", $"{playerPath}.baseHandSize", "Base hand size must not be negative.");
            }

            if (player.CoreDamage < 0)
            {
                Add(violations, "rules.negative-core-damage", $"{playerPath}.coreDamage", "Core damage must not be negative.");
            }

            if (player.Tags < 0)
            {
                Add(violations, "rules.negative-tags", $"{playerPath}.tags", "Tags must not be negative.");
            }

            if (player.BaseMemory < 0)
            {
                Add(violations, "rules.negative-base-memory", $"{playerPath}.baseMemory", "Base memory must not be negative.");
            }

            if (player.UsedMemory < 0)
            {
                Add(violations, "rules.negative-used-memory", $"{playerPath}.usedMemory", "Used memory must not be negative.");
            }

            calculatedMemory[player.PlayerId] = 0;
        }

        foreach (PlayerState? player in state.Players)
        {
            if (player is not null && !rulePlayerIds.Contains(player.Id))
            {
                Add(violations, "rules.missing-player", $"{path}.players", $"Rules state has no entry for player '{player.Id}'.");
            }
        }

        var cardsById = new Dictionary<CardInstanceId, CardInstanceState>();
        foreach (CardInstanceState? card in state.Cards)
        {
            if (card is not null)
            {
                _ = cardsById.TryAdd(card.Id, card);
            }
        }
        var programIds = new HashSet<CardInstanceId>();
        for (int index = 0; index < rules.Programs.Count; index++)
        {
            ProgramMemoryState? program = rules.Programs[index];
            string programPath = $"{path}.programs[{index}]";
            if (program is null)
            {
                Add(violations, "rules.null-program", programPath, "Program memory entries must not be null.");
                continue;
            }

            RequireStableId(program.CardId.Value, $"{programPath}.cardId", "card", violations);
            RequireStableId(program.Owner.Value, $"{programPath}.owner", "player", violations);
            if (!programIds.Add(program.CardId))
            {
                Add(violations, "rules.duplicate-program", $"{programPath}.cardId", $"Program '{program.CardId}' is registered more than once.");
            }

            if (program.MemoryCost < 0)
            {
                Add(violations, "rules.negative-program-memory", $"{programPath}.memoryCost", "Program memory cost must not be negative.");
            }

            if (!statePlayers.TryGetValue(program.Owner, out PlayerState? owner))
            {
                Add(violations, "rules.unknown-program-owner", $"{programPath}.owner", $"Program '{program.CardId}' references unknown player '{program.Owner}'.");
            }
            else if (owner.Side != PlayerSide.Runner)
            {
                Add(violations, "rules.program-owner-side", $"{programPath}.owner", "Only the Runner may own installed program memory.");
            }

            if (!cardsById.TryGetValue(program.CardId, out CardInstanceState? card))
            {
                Add(violations, "rules.unknown-program-card", $"{programPath}.cardId", $"Program memory references unknown card '{program.CardId}'.");
            }
            else
            {
                if (card.OwnerSide != PlayerSide.Runner)
                {
                    Add(violations, "rules.program-card-owner", $"{programPath}.cardId", "A registered program must be Runner-owned.");
                }

                ZoneState? zone = state.Zones.FirstOrDefault(candidate => candidate?.Id == card.ZoneId);
                if (zone?.Kind != ZoneKind.RunnerRig)
                {
                    Add(violations, "rules.program-not-installed", $"{programPath}.cardId", "A registered program must be in the Runner rig.");
                }
            }

            calculatedMemory.TryGetValue(program.Owner, out long used);
            calculatedMemory[program.Owner] = checked(used + program.MemoryCost);
        }

        foreach (PlayerRulesState? player in rules.Players)
        {
            if (player is null)
            {
                continue;
            }

            calculatedMemory.TryGetValue(player.PlayerId, out long calculated);
            if (calculated != player.UsedMemory)
            {
                Add(
                    violations,
                    "rules.used-memory-mismatch",
                    $"{path}.players[{player.PlayerId}].usedMemory",
                    $"Used memory {player.UsedMemory} must equal the sum of registered programs ({calculated}).");
            }


            if (player.UsedMemory > player.BaseMemory)
            {
                bool hasCleanup = state.ResolutionStack.Count >= 2
                    && state.ResolutionStack[^1] is MemoryCleanupFrame cleanup
                    && cleanup.Owner == player.PlayerId
                    && state.ResolutionStack[^2] is EffectResolutionFrame
                    {
                        Stage: EffectResolutionStage.AwaitingMemoryCleanup,
                    };
                if (!hasCleanup)
                {
                    Add(
                        violations,
                        "rules.memory-over-limit-without-cleanup",
                        $"{path}.players[{player.PlayerId}].usedMemory",
                        "Memory may exceed its limit only while the matching mandatory cleanup decision is active.");
                }
            }
        }

        var counterKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < rules.Counters.Count; index++)
        {
            CardCounterState? counter = rules.Counters[index];
            string counterPath = $"{path}.counters[{index}]";
            if (counter is null)
            {
                Add(violations, "rules.null-counter", counterPath, "Counter entries must not be null.");
                continue;
            }

            RequireStableId(counter.CardId.Value, $"{counterPath}.cardId", "card", violations);
            RequireStableId(counter.CounterName, $"{counterPath}.counterName", "counter", violations);
            string key = $"{counter.CardId.Value}\u001f{counter.CounterName}";
            if (!counterKeys.Add(key))
            {
                Add(violations, "rules.duplicate-counter", counterPath, $"Counter '{counter.CounterName}' on card '{counter.CardId}' is duplicated.");
            }

            if (!cardsById.ContainsKey(counter.CardId))
            {
                Add(violations, "rules.unknown-counter-card", $"{counterPath}.cardId", $"Counter references unknown card '{counter.CardId}'.");
            }

            if (counter.Count <= 0)
            {
                Add(violations, "rules.nonpositive-counter", $"{counterPath}.count", "Stored counter entries must be positive; zero-count entries are omitted.");
            }
        }

        var effectIds = new HashSet<RunEffectId>();
        for (int index = 0; index < rules.LingeringEffects.Count; index++)
        {
            GameLingeringEffect? effect = rules.LingeringEffects[index];
            string effectPath = $"{path}.lingeringEffects[{index}]";
            if (effect is null)
            {
                Add(violations, "rules.null-lingering-effect", effectPath, "Lingering effects must not be null.");
                continue;
            }

            if (effect.Version != 1)
            {
                Add(violations, "rules.unsupported-lingering-version", $"{effectPath}.version", "Only lingering effect version 1 is supported.");
            }

            RequireStableId(effect.EffectId.Value, $"{effectPath}.effectId", "effect", violations);
            if (!effectIds.Add(effect.EffectId))
            {
                Add(violations, "rules.duplicate-lingering-effect", $"{effectPath}.effectId", $"Lingering effect '{effect.EffectId}' is duplicated.");
            }

            RequireStableId(effect.BehaviorId, $"{effectPath}.behaviorId", "behavior", violations);
            if (effect.SourceCardId.HasValue && !cardsById.ContainsKey(effect.SourceCardId.Value))
            {
                Add(violations, "rules.unknown-lingering-source", $"{effectPath}.sourceCardId", $"Lingering effect references unknown card '{effect.SourceCardId}'.");
            }

            if (!Enum.IsDefined(effect.Expiry))
            {
                Add(violations, "rules.invalid-lingering-expiry", $"{effectPath}.expiry", $"'{effect.Expiry}' is not a supported lingering-effect expiry.");
            }
        }
    }

    private static void RequireStableId(
        string? value,
        string path,
        string kind,
        List<StateInvariantViolation> violations)
    {
        if (!StableIdentifier.TryValidate(value))
        {
            Add(violations, "id.invalid", path, $"{kind} ID must be non-empty and stable.");
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
                case RunTimingFrame runTiming:
                    if (state.RunState is null)
                    {
                        Add(violations, "run.frame-without-state", $"{path}.runId", "A run timing frame requires live run state.");
                    }
                    else
                    {
                        if (runTiming.RunId != state.RunState.Id)
                        {
                            Add(violations, "run.frame-id-mismatch", $"{path}.runId", "Run timing frame ID must match live run state.");
                        }

                        if (runTiming.Step != state.RunState.Step)
                        {
                            Add(violations, "run.frame-step-mismatch", $"{path}.step", "Run timing frame step must match live run state.");
                        }
                    }

                    break;
                case EffectResolutionFrame effectFrame:
                    RequirePlayer(playerIds, effectFrame.Controller, $"{path}.controller", violations);
                    if (effectFrame.Stage == EffectResolutionStage.AwaitingMemoryCleanup)
                    {
                        if (index + 1 >= state.ResolutionStack.Count
                            || state.ResolutionStack[index + 1] is not MemoryCleanupFrame)
                        {
                            Add(
                                violations,
                                "resolution.memory-cleanup-child-missing",
                                path,
                                "An effect frame awaiting memory cleanup must be immediately followed by a cleanup child frame.");
                        }
                    }
                    else if (index + 1 < state.ResolutionStack.Count
                        && state.ResolutionStack[index + 1] is MemoryCleanupFrame)
                    {
                        Add(
                            violations,
                            "resolution.unexpected-memory-cleanup-child",
                            path,
                            "A memory cleanup child is only valid while its parent effect frame is awaiting cleanup.");
                    }

                    break;
                case EffectCheckpointFrame checkpoint:
                    RequirePlayer(playerIds, checkpoint.Controller, $"{path}.controller", violations);
                    if (checkpoint.Stage == EffectCheckpointStage.AwaitingChild)
                    {
                        if (index + 1 >= state.ResolutionStack.Count
                            || state.ResolutionStack[index + 1] is not EffectResolutionFrame)
                        {
                            Add(
                                violations,
                                "resolution.checkpoint-child-missing",
                                path,
                                "An effect checkpoint awaiting a trigger or replacement child must be immediately followed by an effect-resolution frame.");
                        }
                    }
                    else if (index + 1 < state.ResolutionStack.Count
                        && state.ResolutionStack[index + 1] is EffectResolutionFrame)
                    {
                        Add(
                            violations,
                            "resolution.unexpected-checkpoint-child",
                            path,
                            "An effect-resolution child is only valid while its checkpoint is awaiting a child.");
                    }

                    if (checkpoint.Stage == EffectCheckpointStage.TriggerChoice
                        && checkpoint.TriggerCursor < checkpoint.TriggerQueue.Count)
                    {
                        RequirePlayer(
                            playerIds,
                            checkpoint.TriggerQueue[checkpoint.TriggerCursor].Controller,
                            $"{path}.triggerQueue[{checkpoint.TriggerCursor}].controller",
                            violations);
                    }

                    if (checkpoint.Stage == EffectCheckpointStage.TriggerOrdering
                        && checkpoint.TriggerCursor < checkpoint.TriggerQueue.Count)
                    {
                        RequirePlayer(
                            playerIds,
                            checkpoint.TriggerQueue[checkpoint.TriggerCursor].Controller,
                            $"{path}.triggerQueue[{checkpoint.TriggerCursor}].controller",
                            violations);
                    }

                    if (checkpoint.Stage == EffectCheckpointStage.InterruptChoice
                        && checkpoint.InterruptCursor < checkpoint.InterruptQueue.Count)
                    {
                        RequirePlayer(
                            playerIds,
                            checkpoint.InterruptQueue[checkpoint.InterruptCursor].Controller,
                            $"{path}.interruptQueue[{checkpoint.InterruptCursor}].controller",
                            violations);
                    }

                    break;
                case MemoryCleanupFrame memoryFrame:
                    if (index != state.ResolutionStack.Count - 1)
                    {
                        Add(
                            violations,
                            "resolution.memory-cleanup-not-top",
                            path,
                            "A memory cleanup frame must be the top frame while its decision is pending.");
                    }

                    if (index == 0
                        || state.ResolutionStack[index - 1] is not EffectResolutionFrame)
                    {
                            Add(
                                violations,
                                "resolution.memory-cleanup-parent-missing",
                                path,
                                "A memory cleanup frame must have an effect-resolution parent.");
                        }
                    else if (state.ResolutionStack[index - 1] is EffectResolutionFrame parent
                        && parent.Stage != EffectResolutionStage.AwaitingMemoryCleanup)
                    {
                        Add(
                            violations,
                            "resolution.memory-cleanup-parent-stage",
                            path,
                            "A memory cleanup frame requires its parent to be in the awaiting-cleanup stage.");
                    }

                    RequirePlayer(playerIds, memoryFrame.Owner, $"{path}.owner", violations);
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
                case MemoryCleanupFrame memoryCleanupFrame when pending.Kind != DecisionKind.MemoryCleanup:
                    Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A memory cleanup frame requires a MemoryCleanup decision.");
                    break;
                case MemoryCleanupFrame memoryCleanupOwnerFrame when pending.Owner != memoryCleanupOwnerFrame.Owner:
                    Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Memory cleanup decision owner must match its frame.");
                    break;
                case EffectCheckpointFrame triggerCheckpoint when triggerCheckpoint.Stage == EffectCheckpointStage.TriggerChoice:
                    if (pending.Kind != DecisionKind.TriggerChoice)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A trigger choice checkpoint requires a TriggerChoice decision.");
                    }
                    else if (triggerCheckpoint.TriggerCursor < triggerCheckpoint.TriggerQueue.Count
                        && pending.Owner != triggerCheckpoint.TriggerQueue[triggerCheckpoint.TriggerCursor].Controller)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Trigger choice owner must match the current trigger invocation.");
                    }

                    break;
                case EffectCheckpointFrame orderingCheckpoint when orderingCheckpoint.Stage == EffectCheckpointStage.TriggerOrdering:
                    if (pending.Kind != DecisionKind.TriggerOrdering)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A trigger ordering checkpoint requires a TriggerOrdering decision.");
                    }
                    else if (orderingCheckpoint.TriggerCursor < orderingCheckpoint.TriggerQueue.Count
                        && pending.Owner != orderingCheckpoint.TriggerQueue[orderingCheckpoint.TriggerCursor].Controller)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Trigger ordering owner must match the current controller group.");
                    }

                    break;
                case EffectCheckpointFrame interruptCheckpoint when interruptCheckpoint.Stage == EffectCheckpointStage.InterruptChoice:
                    if (pending.Kind != DecisionKind.InterruptChoice)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "An interrupt checkpoint requires an InterruptChoice decision.");
                    }
                    else if (interruptCheckpoint.InterruptCursor < interruptCheckpoint.InterruptQueue.Count
                        && pending.Owner != interruptCheckpoint.InterruptQueue[interruptCheckpoint.InterruptCursor].Controller)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Interrupt choice owner must match the current interrupt invocation.");
                    }

                    break;
                case RunTimingFrame runFrame:
                    if (state.RunState is null
                        || runFrame.RunId != state.RunState.Id
                        || runFrame.Step != state.RunState.Step)
                    {
                        Add(violations, "run.pending-frame-mismatch", path, "Run timing decision requires a matching live run cursor.");
                    }
                    else
                    {
                        if (pending.Kind != DecisionKind.RunTiming)
                        {
                            Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A run timing frame requires a RunTiming decision.");
                        }

                        PlayerId expectedOwner = RunDecisionOwner(state, state.RunState);
                        if (pending.Owner != expectedOwner)
                        {
                            Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", $"Run timing decision owner must be '{expectedOwner}'.");
                        }
                    }

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
                case (ActionKind.PassPriority or ActionKind.JackOut or ActionKind.ContinueRun, NoActionParameters):
                    if (pending.Kind != DecisionKind.RunTiming)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Run timing actions require a RunTiming decision.");
                    }

                    break;
                case (ActionKind.TrashProgram, CardChoiceParameters trashProgram):
                    if (pending.Kind != DecisionKind.MemoryCleanup)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "TrashProgram actions require a MemoryCleanup decision.");
                    }

                    if (!cardIds.Contains(trashProgram.CardId))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.cardId", $"Trash action references unknown card '{trashProgram.CardId}'.");
                    }

                    break;
                case (ActionKind.RezIce, RunIceParameters runIce):
                    if (pending.Kind != DecisionKind.RunTiming)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Run timing actions require a RunTiming decision.");
                    }

                    if (!cardIds.Contains(runIce.IceId))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.iceId", $"Run action references unknown ICE '{runIce.IceId}'.");
                    }

                    break;
                case (ActionKind.UseTrigger or ActionKind.DeclineTrigger, TriggerChoiceParameters triggerChoice):
                    if (pending.Kind != DecisionKind.TriggerChoice)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Trigger actions require a TriggerChoice decision.");
                    }
                    else if (topFrame is EffectCheckpointFrame triggerFrame
                        && (triggerFrame.Stage != EffectCheckpointStage.TriggerChoice
                            || triggerFrame.TriggerCursor >= triggerFrame.TriggerQueue.Count
                            || triggerFrame.TriggerQueue[triggerFrame.TriggerCursor].Id != triggerChoice.TriggerId))
                    {
                        Add(violations, "decision.trigger-action-not-offered", $"{actionPath}.parameters.triggerId", "Trigger action does not identify the current materialized trigger.");
                    }

                    break;
                case (ActionKind.OrderTriggers, TriggerOrderParameters orderTriggers):
                    if (pending.Kind != DecisionKind.TriggerOrdering)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Trigger ordering actions require a TriggerOrdering decision.");
                    }
                    else if (topFrame is EffectCheckpointFrame orderingFrame
                        && (orderingFrame.Stage != EffectCheckpointStage.TriggerOrdering
                            || orderingFrame.TriggerCursor >= orderingFrame.TriggerQueue.Count
                            || !orderingFrame.TriggerQueue.Skip(orderingFrame.TriggerCursor)
                                .Where(invocation => invocation.Controller == pending.Owner)
                                .Any(invocation => invocation.Id == orderTriggers.TriggerId)))
                    {
                        Add(violations, "decision.trigger-order-not-offered", $"{actionPath}.parameters.triggerId", "Trigger ordering action does not identify a remaining trigger controlled by this decision owner.");
                    }

                    break;
                case (ActionKind.UsePrevention, PreventionChoiceParameters usePrevention):
                    if (pending.Kind != DecisionKind.InterruptChoice)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Prevention actions require an InterruptChoice decision.");
                    }
                    else if (topFrame is EffectCheckpointFrame preventionFrame
                        && (preventionFrame.Stage != EffectCheckpointStage.InterruptChoice
                            || preventionFrame.InterruptCursor >= preventionFrame.InterruptQueue.Count
                            || preventionFrame.InterruptQueue[preventionFrame.InterruptCursor].Id != usePrevention.InterruptId
                            || preventionFrame.InterruptQueue[preventionFrame.InterruptCursor].Kind != InterruptKind.Prevention))
                    {
                        Add(violations, "decision.interrupt-action-not-offered", $"{actionPath}.parameters.interruptId", "Prevention action does not identify the current materialized prevention interrupt.");
                    }

                    break;
                case (ActionKind.DeclinePrevention, PreventionChoiceParameters declinePrevention):
                    if (pending.Kind != DecisionKind.InterruptChoice)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Prevention actions require an InterruptChoice decision.");
                    }
                    else if (topFrame is EffectCheckpointFrame declinePreventionFrame
                        && (declinePreventionFrame.Stage != EffectCheckpointStage.InterruptChoice
                            || declinePreventionFrame.InterruptCursor >= declinePreventionFrame.InterruptQueue.Count
                            || declinePreventionFrame.InterruptQueue[declinePreventionFrame.InterruptCursor].Id != declinePrevention.InterruptId
                            || declinePreventionFrame.InterruptQueue[declinePreventionFrame.InterruptCursor].Kind != InterruptKind.Prevention))
                    {
                        Add(violations, "decision.interrupt-action-not-offered", $"{actionPath}.parameters.interruptId", "Prevention action does not identify the current materialized prevention interrupt.");
                    }

                    break;
                case (ActionKind.UseReplacement, ReplacementChoiceParameters useReplacement):
                    if (pending.Kind != DecisionKind.InterruptChoice)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Replacement actions require an InterruptChoice decision.");
                    }
                    else if (topFrame is EffectCheckpointFrame replacementFrame
                        && (replacementFrame.Stage != EffectCheckpointStage.InterruptChoice
                            || replacementFrame.InterruptCursor >= replacementFrame.InterruptQueue.Count
                            || replacementFrame.InterruptQueue[replacementFrame.InterruptCursor].Id != useReplacement.InterruptId
                            || replacementFrame.InterruptQueue[replacementFrame.InterruptCursor].Kind != InterruptKind.Replacement))
                    {
                        Add(violations, "decision.interrupt-action-not-offered", $"{actionPath}.parameters.interruptId", "Replacement action does not identify the current materialized replacement interrupt.");
                    }

                    break;
                case (ActionKind.DeclineReplacement, ReplacementChoiceParameters declineReplacement):
                    if (pending.Kind != DecisionKind.InterruptChoice)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Replacement actions require an InterruptChoice decision.");
                    }
                    else if (topFrame is EffectCheckpointFrame declineReplacementFrame
                        && (declineReplacementFrame.Stage != EffectCheckpointStage.InterruptChoice
                            || declineReplacementFrame.InterruptCursor >= declineReplacementFrame.InterruptQueue.Count
                            || declineReplacementFrame.InterruptQueue[declineReplacementFrame.InterruptCursor].Id != declineReplacement.InterruptId
                            || declineReplacementFrame.InterruptQueue[declineReplacementFrame.InterruptCursor].Kind != InterruptKind.Replacement))
                    {
                        Add(violations, "decision.interrupt-action-not-offered", $"{actionPath}.parameters.interruptId", "Replacement action does not identify the current materialized replacement interrupt.");
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
                        or ActionKind.EndTurn))
                || (pending.Kind == DecisionKind.RunTiming
                    && action.Kind is not (ActionKind.PassPriority
                        or ActionKind.RezIce
                        or ActionKind.JackOut
                        or ActionKind.ContinueRun))
                || (pending.Kind == DecisionKind.MemoryCleanup
                    && action.Kind != ActionKind.TrashProgram)
                || (pending.Kind == DecisionKind.TriggerChoice
                    && action.Kind is not (ActionKind.UseTrigger or ActionKind.DeclineTrigger))
                || (pending.Kind == DecisionKind.TriggerOrdering
                    && action.Kind != ActionKind.OrderTriggers)
                || (pending.Kind == DecisionKind.InterruptChoice
                    && action.Kind is not (ActionKind.UsePrevention
                        or ActionKind.DeclinePrevention
                        or ActionKind.UseReplacement
                        or ActionKind.DeclineReplacement)))
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

        if (pending.Kind == DecisionKind.RunTiming
            && topFrame is RunTimingFrame runTiming
            && state.RunState is not null)
        {
            ValidateRunActions(state, pending, state.RunState, runTiming, path, violations);
        }

        if (pending.Kind == DecisionKind.MemoryCleanup
            && topFrame is MemoryCleanupFrame memoryCleanup)
        {
            ValidateMemoryCleanupActions(state, pending, memoryCleanup, path, violations);
        }

        if (topFrame is EffectCheckpointFrame checkpoint)
        {
            ValidateCheckpointActions(pending, checkpoint, path, violations);
        }
    }

    private static void ValidateCheckpointActions(
        PendingDecision pending,
        EffectCheckpointFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        var expected = new List<(ActionKind Kind, string? Target, int Amount)>();
        switch (frame.Stage)
        {
            case EffectCheckpointStage.TriggerChoice when frame.TriggerCursor < frame.TriggerQueue.Count:
                TriggerInvocation trigger = frame.TriggerQueue[frame.TriggerCursor];
                expected.Add((ActionKind.UseTrigger, trigger.Id.Value, 0));
                if (trigger.Mode == TriggerMode.Optional)
                {
                    expected.Add((ActionKind.DeclineTrigger, trigger.Id.Value, 0));
                }

                break;
            case EffectCheckpointStage.TriggerOrdering when frame.TriggerCursor < frame.TriggerQueue.Count:
                PlayerId controller = frame.TriggerQueue[frame.TriggerCursor].Controller;
                foreach (TriggerInvocation remaining in frame.TriggerQueue
                    .Skip(frame.TriggerCursor)
                    .Where(invocation => invocation.Controller == controller))
                {
                    expected.Add((ActionKind.OrderTriggers, remaining.Id.Value, 0));
                }

                break;
            case EffectCheckpointStage.InterruptChoice when frame.InterruptCursor < frame.InterruptQueue.Count:
                InterruptInvocation interrupt = frame.InterruptQueue[frame.InterruptCursor];
                if (interrupt.Kind == InterruptKind.Prevention)
                {
                    int maximum = Math.Min(
                        interrupt.MaximumPreventionAmount,
                        frame.CurrentEffect is DamageEffect damage ? damage.Amount : 0);
                    for (int amount = 1; amount <= maximum; amount++)
                    {
                        expected.Add((ActionKind.UsePrevention, interrupt.Id.Value, amount));
                    }

                    if (!interrupt.Mandatory)
                    {
                        expected.Add((ActionKind.DeclinePrevention, interrupt.Id.Value, 0));
                    }
                }
                else if (interrupt.Kind == InterruptKind.Replacement)
                {
                    expected.Add((ActionKind.UseReplacement, interrupt.Id.Value, 0));
                    if (!interrupt.Mandatory)
                    {
                        expected.Add((ActionKind.DeclineReplacement, interrupt.Id.Value, 0));
                    }
                }

                break;
        }

        var actual = new List<(ActionKind Kind, string? Target, int Amount)>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            switch (action.Kind, action.Parameters)
            {
                case (ActionKind.UseTrigger or ActionKind.DeclineTrigger, TriggerChoiceParameters triggerChoice):
                    actual.Add((action.Kind, triggerChoice.TriggerId.Value, 0));
                    break;
                case (ActionKind.OrderTriggers, TriggerOrderParameters order):
                    actual.Add((action.Kind, order.TriggerId.Value, 0));
                    break;
                case (ActionKind.UsePrevention or ActionKind.DeclinePrevention, PreventionChoiceParameters prevention):
                    actual.Add((action.Kind, prevention.InterruptId.Value, prevention.Amount));
                    break;
                case (ActionKind.UseReplacement or ActionKind.DeclineReplacement, ReplacementChoiceParameters replacement):
                    actual.Add((action.Kind, replacement.InterruptId.Value, 0));
                    break;
            }
        }

        var expectedCounts = expected
            .GroupBy(value => value)
            .ToDictionary(group => group.Key, group => group.Count());
        var actualCounts = actual
            .GroupBy(value => value)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (((ActionKind Kind, string? Target, int Amount) spec, int count) in expectedCounts)
        {
            actualCounts.TryGetValue(spec, out int actualCount);
            if (actualCount != count)
            {
                Add(
                    violations,
                    actualCount == 0 ? "decision.missing-checkpoint-action" : "decision.checkpoint-action-count-mismatch",
                    $"{path}.legalActions",
                    $"Checkpoint decision requires exactly {count} '{spec.Kind}' action(s) for '{spec.Target ?? "none"}' amount {spec.Amount}, but contains {actualCount}.");
            }
        }

        foreach (((ActionKind Kind, string? Target, int Amount) spec, int count) in actualCounts)
        {
            if (!expectedCounts.TryGetValue(spec, out int expectedCount))
            {
                Add(
                    violations,
                    "decision.extra-checkpoint-action",
                    $"{path}.legalActions",
                    $"Checkpoint decision contains unoffered '{spec.Kind}' action for '{spec.Target ?? "none"}' amount {spec.Amount}.");
            }
            else if (count > expectedCount)
            {
                Add(
                    violations,
                    "decision.duplicate-checkpoint-action",
                    $"{path}.legalActions",
                    $"Checkpoint decision contains {count} copies of '{spec.Kind}' for '{spec.Target ?? "none"}' amount {spec.Amount}, expected {expectedCount}.");
            }
        }
    }

    private static void ValidateRunActions(
        GameState state,
        PendingDecision pending,
        RunState run,
        RunTimingFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (frame.RunId != run.Id || frame.Step != run.Step)
        {
            return;
        }

        var expected = new List<(ActionKind Kind, CardInstanceId? Target)>();
        switch (run.Step)
        {
            case RunTimingStep.InitiationPaidWindow:
            case RunTimingStep.ApproachPaidWindow:
            case RunTimingStep.EncounterPaidWindow:
            case RunTimingStep.MovementPaidWindow:
            case RunTimingStep.AfterMovePaidWindow:
                expected.Add((ActionKind.PassPriority, null));
                if (run.Step == RunTimingStep.ApproachPaidWindow
                    && run.Priority is { PriorityHolder: PlayerSide.Corp, CorpMayRezApproachedIce: true }
                    && run.CurrentIceId.HasValue)
                {
                    expected.Add((ActionKind.RezIce, run.CurrentIceId.Value));
                }

                break;
            case RunTimingStep.JackOutDecision:
                expected.Add((ActionKind.JackOut, null));
                expected.Add((ActionKind.ContinueRun, null));
                break;
            default:
                Add(violations, "run.unexpected-prompt", $"{path}.legalActions", $"Run step '{run.Step}' must not have a pending decision.");
                return;
        }

        var actual = new List<(ActionKind Kind, CardInstanceId? Target)>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            switch (action.Kind, action.Parameters)
            {
                case (ActionKind.PassPriority, NoActionParameters):
                case (ActionKind.JackOut, NoActionParameters):
                case (ActionKind.ContinueRun, NoActionParameters):
                    actual.Add((action.Kind, null));
                    break;
                case (ActionKind.RezIce, RunIceParameters rez):
                    actual.Add((action.Kind, rez.IceId));
                    break;
            }
        }

        var expectedCounts = expected.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        var actualCounts = actual.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        foreach (((ActionKind Kind, CardInstanceId? Target) spec, int count) in expectedCounts)
        {
            actualCounts.TryGetValue(spec, out int actualCount);
            if (actualCount != count)
            {
                Add(
                    violations,
                    actualCount == 0 ? "run.missing-action" : "run.action-count-mismatch",
                    $"{path}.legalActions",
                    $"Run timing decision requires exactly {count} '{spec.Kind}' action(s) for target '{spec.Target?.ToString() ?? "none"}', but contains {actualCount}.");
            }
        }

        foreach (((ActionKind Kind, CardInstanceId? Target) spec, int count) in actualCounts)
        {
            if (!expectedCounts.TryGetValue(spec, out int expectedCount))
            {
                Add(violations, "run.extra-action", $"{path}.legalActions", $"Run timing decision contains unoffered '{spec.Kind}' action for target '{spec.Target?.ToString() ?? "none"}'.");
            }
            else if (count > expectedCount)
            {
                Add(violations, "run.duplicate-action", $"{path}.legalActions", $"Run timing decision contains {count} copies of '{spec.Kind}' for target '{spec.Target?.ToString() ?? "none"}', expected {expectedCount}.");
            }
        }

        if (pending.Owner != RunDecisionOwner(state, run))
        {
            Add(violations, "run.decision-owner-mismatch", $"{path}.owner", "Run timing decision owner does not match the current priority/Runner decision owner.");
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

    private static void ValidateMemoryCleanupActions(
        GameState state,
        PendingDecision pending,
        MemoryCleanupFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        RulesState? rules = state.RulesState;
        if (rules is null)
        {
            Add(violations, "rules.missing", $"{path}.legalActions", "Memory cleanup requires initialized M2 rules state.");
            return;
        }

        var expected = frame.Candidates
            .GroupBy(candidate => candidate.CardId)
            .ToDictionary(group => group.Key, group => group.Count());
        var actual = new Dictionary<CardInstanceId, int>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action?.Parameters is not CardChoiceParameters choice)
            {
                continue;
            }

            actual.TryGetValue(choice.CardId, out int count);
            actual[choice.CardId] = count + 1;
            if (!expected.ContainsKey(choice.CardId))
            {
                Add(violations, "decision.extra-memory-action", $"{path}.legalActions", $"Program '{choice.CardId}' is not a materialized cleanup candidate.");
            }
        }

        if (pending.LegalActions.Count != expected.Values.Sum())
        {
            Add(
                violations,
                "decision.memory-action-count-mismatch",
                $"{path}.legalActions",
                $"Memory cleanup must contain exactly {expected.Values.Sum()} typed program actions, but contains {pending.LegalActions.Count}.");
        }

        foreach ((CardInstanceId cardId, int count) in expected)
        {
            actual.TryGetValue(cardId, out int actualCount);
            if (actualCount == 0)
            {
                Add(violations, "decision.missing-memory-action", $"{path}.legalActions", $"Memory cleanup is missing program '{cardId}'.");
            }
            else if (actualCount != count)
            {
                Add(violations, "decision.duplicate-memory-action", $"{path}.legalActions", $"Memory cleanup contains {actualCount} actions for program '{cardId}', expected {count}.");
            }
        }

        foreach ((CardInstanceId cardId, int count) in actual)
        {
            if (count > 1 && expected.ContainsKey(cardId))
            {
                Add(violations, "decision.duplicate-memory-action", $"{path}.legalActions", $"Memory cleanup contains duplicate program '{cardId}'.");
            }
        }

        PlayerRulesState? owner = rules.Players.SingleOrDefault(player => player.PlayerId == frame.Owner);
        if (owner is null)
        {
            Add(violations, "rules.unknown-player", $"{path}.owner", $"Memory cleanup owner '{frame.Owner}' has no rules state.");
            return;
        }

        if (owner.UsedMemory <= owner.BaseMemory)
        {
            Add(violations, "resolution.unneeded-memory-cleanup", path, "Memory cleanup is only valid while used memory exceeds base memory.");
        }

        var installed = new Dictionary<CardInstanceId, ProgramMemoryState>();
        foreach (ProgramMemoryState? program in rules.Programs)
        {
            if (program is not null && program.Owner == frame.Owner)
            {
                _ = installed.TryAdd(program.CardId, program);
            }
        }
        foreach (ProgramMemoryState candidate in frame.Candidates)
        {
            if (!installed.TryGetValue(candidate.CardId, out ProgramMemoryState? current))
            {
                Add(violations, "resolution.stale-memory-candidate", $"{path}.candidates", $"Program '{candidate.CardId}' is no longer registered.");
            }
            else if (current != candidate)
            {
                Add(violations, "resolution.memory-candidate-mismatch", $"{path}.candidates", $"Program '{candidate.CardId}' does not match its registered memory entry.");
            }
        }
    }

    private static bool IsPromptFrame(ResolutionFrame? frame) =>
        frame is ChooseNumberFrame
        || frame is ChooseNumberThenAdjustCreditsFrame { Stage: NestedNumberStage.OuterPrompt }
        || frame is ChooseCardFrame
        || frame is ToyTurnFrame { Stage: ToyTurnStage.AwaitingAction }
        || frame is MemoryCleanupFrame
        || frame is EffectCheckpointFrame
            {
                Stage: EffectCheckpointStage.InterruptChoice
                    or EffectCheckpointStage.TriggerChoice
                    or EffectCheckpointStage.TriggerOrdering,
            }
        || frame is RunTimingFrame runTiming && IsRunPromptStep(runTiming.Step);

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

        if (state.RunState is not null)
        {
            Observe(state.RunState.Id.Value, "run");
        }

        foreach (ResolutionFrame? frame in state.ResolutionStack)
        {
            if (frame is RunTimingFrame runTiming)
            {
                Observe(runTiming.RunId.Value, "run");
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
