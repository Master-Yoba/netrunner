using System.Globalization;
using System.Text.Json;

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
        Validate(state, null, null);
    }

    /// <summary>
    /// Validates against an explicitly composed behavior catalog.  The
    /// catalog is never stored in state; this overload lets an injected
    /// engine session validate the same opaque definition IDs against its own
    /// immutable registration set.
    /// </summary>
    public static void Validate(GameState state, IBehaviorCatalog? behaviorCatalog)
    {
        Validate(state, behaviorCatalog, null);
    }

    /// <summary>Validates against an optional printed-card metadata catalog.</summary>
    public static void Validate(GameState state, IRuntimeCardCatalog runtimeCardCatalog)
    {
        Validate(state, null, runtimeCardCatalog);
    }

    public static void Validate(
        GameState state,
        IBehaviorCatalog? behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        IReadOnlyList<StateInvariantViolation> violations = Collect(state, behaviorCatalog, runtimeCardCatalog);
        if (violations.Count > 0)
        {
            throw new StateInvariantException(violations);
        }
    }

    public static bool TryValidate(
        GameState state,
        out IReadOnlyList<StateInvariantViolation> violations)
    {
        return TryValidate(state, null, null, out violations);
    }

    public static bool TryValidate(
        GameState state,
        IBehaviorCatalog? behaviorCatalog,
        out IReadOnlyList<StateInvariantViolation> violations)
    {
        return TryValidate(state, behaviorCatalog, null, out violations);
    }

    public static bool TryValidate(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog,
        out IReadOnlyList<StateInvariantViolation> violations)
    {
        return TryValidate(state, null, runtimeCardCatalog, out violations);
    }

    public static bool TryValidate(
        GameState state,
        IBehaviorCatalog? behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        out IReadOnlyList<StateInvariantViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(state);
        violations = Collect(state, behaviorCatalog, runtimeCardCatalog);
        return violations.Count == 0;
    }

    public static IReadOnlyList<StateInvariantViolation> Collect(GameState state)
    {
        return Collect(state, null, null);
    }

    public static IReadOnlyList<StateInvariantViolation> Collect(
        GameState state,
        IBehaviorCatalog? behaviorCatalog)
    {
        return Collect(state, behaviorCatalog, null);
    }

    public static IReadOnlyList<StateInvariantViolation> Collect(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        return Collect(state, null, runtimeCardCatalog);
    }

    public static IReadOnlyList<StateInvariantViolation> Collect(
        GameState state,
        IBehaviorCatalog? behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool enforceCatalogCompatibility = behaviorCatalog is not null;
        behaviorCatalog ??= CardBehaviorSpecCatalog.Default;

        var violations = new List<StateInvariantViolation>();
        ValidateMetadata(state, behaviorCatalog, enforceCatalogCompatibility, runtimeCardCatalog, violations);
        ValidatePlayers(state, violations);
        ValidateZonesAndCards(state, behaviorCatalog, violations);
        ValidateServers(state, violations);
        ValidateTurnAndOutcome(state, violations);
        ValidateRunState(state, behaviorCatalog, violations);
        ValidateRulesState(state, behaviorCatalog, runtimeCardCatalog, violations);
        ValidateResolutionAndDecision(
            state,
            behaviorCatalog,
            runtimeCardCatalog,
            enforceCatalogCompatibility,
            violations);
        return violations.AsReadOnly();
    }

    private static void ValidateMetadata(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        bool enforceCatalogCompatibility,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
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

        if (string.IsNullOrWhiteSpace(state.BehaviorRegistryVersion))
        {
            Add(violations, "metadata.missing", "behaviorRegistryVersion", "Behavior-registry version must be non-empty.");
        }
        else if (enforceCatalogCompatibility
            && !string.Equals(state.BehaviorRegistryVersion, behaviorCatalog.Version, StringComparison.Ordinal))
        {
            Add(
                violations,
                "metadata.behavior-version-mismatch",
                "behaviorRegistryVersion",
                $"Behavior-registry version '{state.BehaviorRegistryVersion}' does not match the supplied catalog version '{behaviorCatalog.Version}'.");
        }

        if (state.BehaviorCatalogFingerprint is not null
            && !DeterministicHash.IsSha256Hex(state.BehaviorCatalogFingerprint))
        {
            Add(
                violations,
                "metadata.invalid-fingerprint",
                "behaviorCatalogFingerprint",
                "Behavior-catalog fingerprints must be lowercase SHA-256 hex.");
        }
        else if (enforceCatalogCompatibility
            && state.BehaviorCatalogFingerprint is not null
            && !string.Equals(state.BehaviorCatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            Add(
                violations,
                "metadata.behavior-fingerprint-mismatch",
                "behaviorCatalogFingerprint",
                $"Behavior-catalog fingerprint '{state.BehaviorCatalogFingerprint}' does not match the supplied catalog fingerprint '{behaviorCatalog.Fingerprint}'.");
        }

        bool hasRuntimeCardCatalogVersion = state.RuntimeCardCatalogVersion is not null;
        bool hasRuntimeCardCatalogFingerprint = state.RuntimeCardCatalogFingerprint is not null;
        if (hasRuntimeCardCatalogVersion != hasRuntimeCardCatalogFingerprint)
        {
            Add(
                violations,
                "metadata.runtime-card-pin-incomplete",
                "runtimeCardCatalog",
                "Runtime card-catalog version and fingerprint must either both be supplied or both be omitted.");
        }

        if (hasRuntimeCardCatalogVersion
            && string.IsNullOrWhiteSpace(state.RuntimeCardCatalogVersion))
        {
            Add(
                violations,
                "metadata.missing",
                "runtimeCardCatalogVersion",
                "Runtime card-catalog version must be non-empty when supplied.");
        }

        if (hasRuntimeCardCatalogFingerprint
            && !DeterministicHash.IsSha256Hex(state.RuntimeCardCatalogFingerprint))
        {
            Add(
                violations,
                "metadata.invalid-fingerprint",
                "runtimeCardCatalogFingerprint",
                "Runtime card-catalog fingerprints must be lowercase SHA-256 hex.");
        }

        if (runtimeCardCatalog is not null
            && hasRuntimeCardCatalogVersion
            && !string.Equals(state.RuntimeCardCatalogVersion, runtimeCardCatalog.Version, StringComparison.Ordinal))
        {
            Add(
                violations,
                "metadata.runtime-card-version-mismatch",
                "runtimeCardCatalogVersion",
                $"Runtime card-catalog version '{state.RuntimeCardCatalogVersion}' does not match the supplied catalog version '{runtimeCardCatalog.Version}'.");
        }

        if (runtimeCardCatalog is not null
            && hasRuntimeCardCatalogFingerprint
            && !string.Equals(state.RuntimeCardCatalogFingerprint, runtimeCardCatalog.Fingerprint, StringComparison.Ordinal))
        {
            Add(
                violations,
                "metadata.runtime-card-fingerprint-mismatch",
                "runtimeCardCatalogFingerprint",
                $"Runtime card-catalog fingerprint '{state.RuntimeCardCatalogFingerprint}' does not match the supplied catalog fingerprint '{runtimeCardCatalog.Fingerprint}'.");
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

    private static void ValidateZonesAndCards(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        List<StateInvariantViolation> violations)
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

            // CR 26.03 8.1.1: an agenda, asset, upgrade, or piece of ice is
            // unrezzed if it is installed and facedown — a rezzed card is
            // therefore always faceup.
            if (card.Rezzed && !card.FaceUp)
            {
                Add(
                    violations,
                    "cards.rezzed-facedown",
                    path,
                    $"Card '{card.Id}' is rezzed but facedown; a rezzed installed card is faceup (CR 26.03 8.1.1).");
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
                    && (
                        (ToyCardBehaviorCatalog.TryGet(card.DefinitionId, out ToyCardBehavior? behavior)
                            && behavior.Kind == ToyCardKind.CorpAgenda)
                        || behaviorCatalog.TryGetAgenda(card.DefinitionId, out _));
                if (zone.OwnerSide.HasValue && card.OwnerSide != zone.OwnerSide.Value && !isStolenCorpAgenda)
                {
                    Add(
                        violations,
                        "cards.owner-zone-mismatch",
                        path,
                        $"Card '{card.Id}' owner '{card.OwnerSide}' does not match zone owner '{zone.OwnerSide}'.");
                }

                if (zone.Kind == ZoneKind.RunnerHeap && !card.FaceUp)
                {
                    Add(
                        violations,
                        "cards.runner-heap-facedown",
                        path,
                        $"Runner heap card '{card.Id}' must be faceup after entering the public heap.");
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
            ZoneKind.CorpDeck or ZoneKind.CorpHand or ZoneKind.CorpDiscard or ZoneKind.CorpScore or ZoneKind.CorpPlayArea => PlayerSide.Corp,
            ZoneKind.RunnerStack or ZoneKind.RunnerGrip or ZoneKind.RunnerPlayArea or ZoneKind.RunnerHeap or ZoneKind.RunnerRig or ZoneKind.RunnerScore => PlayerSide.Runner,
            ZoneKind.ServerIce or ZoneKind.ServerRoot => PlayerSide.Corp,
            ZoneKind.BehaviorSetAside => null,
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

        if (zone.Kind == ZoneKind.BehaviorSetAside && zone.OwnerSide.HasValue)
        {
            Add(
                violations,
                "zones.owner-mismatch",
                $"{path}.ownerSide",
                "A behavior set-aside zone must not have an owner side.");
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

        foreach (ZoneKind playAreaKind in new[] { ZoneKind.CorpPlayArea, ZoneKind.RunnerPlayArea })
        {
            int playAreaCount = state.Zones.Count(zone => zone is not null && zone.Kind == playAreaKind);
            if (playAreaCount > 1)
            {
                Add(
                    violations,
                    "zones.required-kind-count",
                    "zones",
                    $"Found {playAreaCount} '{playAreaKind}' zones; at most one is allowed.");
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

            if (!Enum.IsDefined(turn.Step))
            {
                Add(violations, "turn.invalid-step", "turn.step", $"'{turn.Step}' is not a supported CR turn step.");
            }

            if (turn.Step is TurnStep.CorpDraw or TurnStep.CorpAction or TurnStep.CorpDiscard
                && turn.ActiveSide != PlayerSide.Corp)
            {
                Add(violations, "turn.step-side-mismatch", "turn.activeSide", "A Corp turn step requires Corp to be the active side.");
            }

            if (turn.Step is TurnStep.RunnerAction or TurnStep.RunnerDiscard
                && turn.ActiveSide != PlayerSide.Runner)
            {
                Add(violations, "turn.step-side-mismatch", "turn.activeSide", "A Runner turn step requires Runner to be the active side.");
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

            if (turn.History is not null && turn.History.Version != 1)
            {
                Add(violations, "turn.unsupported-history-version", "turn.history.version", "Only turn-history version 1 is supported.");
            }

            if (turn.History is not null)
            {
                ValidateTurnFacts(turn.History.CorpThisTurn, PlayerSide.Corp, "turn.history.corpThisTurn", violations);
                ValidateTurnFacts(turn.History.CorpLastTurn, PlayerSide.Corp, "turn.history.corpLastTurn", violations);
                ValidateTurnFacts(turn.History.RunnerThisTurn, PlayerSide.Runner, "turn.history.runnerThisTurn", violations);
                ValidateTurnFacts(turn.History.RunnerLastTurn, PlayerSide.Runner, "turn.history.runnerLastTurn", violations);
            }
            else if (turn.Step != TurnStep.None)
            {
                Add(violations, "turn.history-missing", "turn.history", "A non-legacy turn step requires explicit current/last-turn facts.");
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

    private static void ValidateRunState(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        List<StateInvariantViolation> violations)
    {
        RunState? run = state.RunState;
        bool hasRunFrame = state.ResolutionStack.Any(static frame => frame is RunTimingFrame);
        ValidateTransientPlayAreas(state, violations);
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

        // The initiating source and active event are the same card identity.
        // Once the event is played it must leave RunnerGrip, remain faceup in
        // the single public RunnerPlayArea for the nested run, and be owned by
        // the Runner. These checks deliberately validate both IDs and the
        // physical card/zone topology so a restored or scenario-authored
        // state cannot smuggle an unregistered event into an active run.
        if (run.InitiatingSource != run.ActiveRunEventId)
        {
            Add(
                violations,
                "run.active-event-source-mismatch",
                $"{path}.activeRunEventId",
                "InitiatingSource and ActiveRunEventId must identify the same card.");
        }

        if (run.ActiveRunEventId is { } activeEventId)
        {
            CardInstanceState? activeEvent = state.Cards.FirstOrDefault(card => card?.Id == activeEventId);
            if (activeEvent is null)
            {
                Add(
                    violations,
                    "run.active-event-unknown-card",
                    $"{path}.activeRunEventId",
                    $"Active run event '{activeEventId}' does not exist.");
            }
            else
            {
                if (activeEvent.OwnerSide != PlayerSide.Runner)
                {
                    Add(
                        violations,
                        "run.active-event-owner",
                        $"{path}.activeRunEventId",
                        "The active run event must be Runner-owned.");
                }

                if (!behaviorCatalog.TryGetRunEvent(activeEvent.DefinitionId, out _))
                {
                    Add(
                        violations,
                        "run.active-event-unregistered",
                        $"{path}.activeRunEventId",
                        $"Active run event definition '{activeEvent.DefinitionId}' is not registered as a run event.");
                }

                ZoneState? activeZone = state.Zones.FirstOrDefault(zone => zone?.Id == activeEvent.ZoneId);
                if (activeZone is null
                    || activeZone.Kind != ZoneKind.RunnerPlayArea
                    || activeZone.OwnerSide != PlayerSide.Runner
                    || activeZone.ServerId is not null)
                {
                    Add(
                        violations,
                        "run.active-event-zone",
                        $"{path}.activeRunEventId",
                        "The active run event must be in the Runner's public play area.");
                }
                else
                {
                    if (!activeZone.CardIds.Contains(activeEventId))
                    {
                        Add(
                            violations,
                            "run.active-event-membership",
                            $"{path}.activeRunEventId",
                            "The active run event must be registered in its Runner play-area zone.");
                    }

                    if (activeZone.CardIds.Count != 1)
                    {
                        Add(
                            violations,
                            "run.active-event-zone-count",
                            $"zones[{activeZone.Id}].cardIds",
                            "The active Runner play area must contain exactly one event card.");
                    }
                }

                if (!activeEvent.FaceUp)
                {
                    Add(
                        violations,
                        "run.active-event-facedown",
                        $"{path}.activeRunEventId",
                        "The active run event must be faceup while its nested run resolves.");
                }
            }
        }
        else
        {
            foreach (ZoneState? populatedPlayArea in state.Zones.Where(zone =>
                zone is not null
                && zone.Kind == ZoneKind.RunnerPlayArea
                && zone.CardIds.Count > 0))
            {
                Add(
                    violations,
                    "run.play-area-without-event",
                    $"zones[{populatedPlayArea!.Id}].cardIds",
                    "A populated Runner play area requires ActiveRunEventId.");
            }
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

            if (run.Step is RunTimingStep.Success
                or RunTimingStep.BreachBoundary
                or RunTimingStep.BreachCandidateSelection
                or RunTimingStep.BreachAccessAbility
                && run.CurrentIceIndex >= 0)
            {
                Add(violations, "run.success-before-server", $"{path}.currentIceIndex", "Success and breach may occur only after the run reaches the server.");
            }
        }

        ValidateRunIdList(run.BreachCandidates, "breachCandidates");
        ValidateRunIdList(run.ChosenAccessCards, "chosenAccessCards");
        ValidateRunIdList(run.AccessedCards, "accessedCards");

        foreach (CardInstanceId accessed in run.AccessedCards)
        {
            if (!run.ChosenAccessCards.Contains(accessed))
            {
                Add(violations, "run.accessed-without-choice", $"{path}.accessedCards", $"Accessed card '{accessed}' was never chosen as a breach candidate.");
                break;
            }
        }

        foreach (CardInstanceId candidate in run.BreachCandidates)
        {
            if (run.ChosenAccessCards.Contains(candidate))
            {
                Add(violations, "run.candidate-already-chosen", $"{path}.breachCandidates", $"Chosen candidate '{candidate}' still remains in the candidate list.");
                break;
            }
        }

        if (run.Step == RunTimingStep.BreachAccessAbility
            && (run.ChosenAccessCards.Count == 0 || run.CurrentAccessCard is not null))
        {
            Add(violations, "run.missing-access-card", $"{path}.step", "The access ability step requires a completed access of a chosen card.");
        }
        else if (run.Step == RunTimingStep.BreachCandidateSelection && run.CurrentAccessCard is not null)
        {
            Add(violations, "run.unexpected-access-card", $"{path}.step", "The candidate selection step must not have a card mid-access.");
        }

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

        ValidateRunStackTopology(state, run, violations);

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

    private static void ValidateRunStackTopology(
        GameState state,
        RunState run,
        List<StateInvariantViolation> violations)
    {
        bool standalone = state.ResolutionStack.Count == 1
            && state.ResolutionStack[0] is RunTimingFrame standaloneFrame
            && standaloneFrame.RunId == run.Id
            && standaloneFrame.Step == run.Step;
        if (standalone)
        {
            return;
        }

        bool nested = state.ResolutionStack.Count == 2
            && state.ResolutionStack[0] is TurnFrame turnFrame
            && state.ResolutionStack[1] is RunTimingFrame nestedFrame
            && nestedFrame.RunId == run.Id
            && nestedFrame.Step == run.Step;
        if (!nested)
        {
            Add(
                violations,
                "run.stack-topology",
                "resolutionStack",
                "A live run must be either one standalone RunTimingFrame or an exact Runner TurnFrame/RunTimingFrame nesting.");
            return;
        }

        TurnFrame parent = (TurnFrame)state.ResolutionStack[0];
        if (parent.ActiveSide != PlayerSide.Runner
            || state.Turn.ActiveSide != PlayerSide.Runner)
        {
            Add(
                violations,
                "run.nested-parent-side",
                "resolutionStack[0].activeSide",
                "A nested basic run requires the active Runner turn and Runner parent frame.");
        }

        if (parent.Stage != TurnFrameStage.AwaitingAction)
        {
            Add(
                violations,
                "run.nested-parent-stage",
                "resolutionStack[0].stage",
                "A nested basic run requires an AwaitingAction parent turn frame.");
        }

        if (parent.TurnNumber != state.Turn.TurnNumber)
        {
            Add(
                violations,
                "run.nested-parent-turn",
                "resolutionStack[0].turnNumber",
                "A nested basic run parent turn number must match the active turn.");
        }

        if (run.InitiatingSource is not null || run.ActiveRunEventId is not null)
        {
            Add(
                violations,
                "run.nested-source",
                "runState.initiatingSource",
                "A basic run nested under a real turn must not carry a card source or active run event.");
        }
    }

    /// <summary>
    /// Play areas are lazy transient zones. A populated area must be owned by
    /// exactly one matching ordinary-play frame while no run is active; Corp
    /// never has a second lifecycle for this zone. Runner run-event state is
    /// deliberately checked by <see cref="ValidateRunState"/> below, so a
    /// Runner ordinary-play frame is always a conflict once a run exists.
    /// </summary>
    private static void ValidateTransientPlayAreas(
        GameState state,
        List<StateInvariantViolation> violations)
    {
        foreach ((ZoneKind Kind, PlayerSide Side) in new[]
        {
            (ZoneKind.CorpPlayArea, PlayerSide.Corp),
            (ZoneKind.RunnerPlayArea, PlayerSide.Runner),
        })
        {
            ZoneState[] zones = state.Zones
                .Where(zone => zone is not null && zone.Kind == Kind)
                .Select(zone => zone!)
                .ToArray();

            if (Side == PlayerSide.Runner && state.RunState is not null)
            {
                for (int index = 0; index < state.ResolutionStack.Count; index++)
                {
                    if (state.ResolutionStack[index] is not CardPlayFrame frame || frame.Side != Side)
                    {
                        continue;
                    }

                    Add(
                        violations,
                        "run.card-play-frame-conflict",
                        $"resolutionStack[{index}]",
                        "A Runner CardPlayFrame cannot coexist with live RunState; the Runner play area is reserved for the active run event.");
                }

                continue;
            }

            CardPlayFrame[] frames = state.ResolutionStack
                .OfType<CardPlayFrame>()
                .Where(frame => frame.Side == Side)
                .ToArray();

            foreach (ZoneState zone in zones.Where(zone => zone.CardIds.Count > 0))
            {
                int matchingFrames = frames.Count(frame => frame.PlayAreaZoneId == zone.Id);
                if (matchingFrames == 1)
                {
                    continue;
                }

                if (Side == PlayerSide.Runner)
                {
                    Add(
                        violations,
                        "run.active-event-without-state",
                        $"zones[{zone.Id}].cardIds",
                        "A populated Runner play area requires exactly one matching active Runner CardPlayFrame when no run is active.");
                }
                else
                {
                    Add(
                        violations,
                        "resolution.card-play-area-without-frame",
                        $"zones[{zone.Id}].cardIds",
                        "A populated Corp play area requires exactly one matching active Corp CardPlayFrame.");
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
        if (encounter.Version != 3)
        {
            Add(violations, "run.encounter-unsupported-version", $"{path}.encounter.version", "Only encounter state version 3 is supported.");
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
        if (run.Step is RunTimingStep.JackOutDecision
            or RunTimingStep.BreachCandidateSelection
            or RunTimingStep.BreachAccessAbility)
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
        || step == RunTimingStep.JackOutDecision
        || step == RunTimingStep.BreachCandidateSelection
        || step == RunTimingStep.BreachAccessAbility;

    private static RunCrPhase ExpectedRunPhase(RunTimingStep step) => step switch
    {
        RunTimingStep.InitiationPaidWindow => RunCrPhase.Initiation,
        RunTimingStep.Approach or RunTimingStep.ApproachPaidWindow => RunCrPhase.Approach,
        RunTimingStep.EncounterInitialization or RunTimingStep.EncounterPaidWindow or RunTimingStep.Encounter => RunCrPhase.Encounter,
        RunTimingStep.MovementPaidWindow or RunTimingStep.JackOutDecision or RunTimingStep.MoveInward or RunTimingStep.AfterMovePaidWindow => RunCrPhase.Movement,
        RunTimingStep.Success
            or RunTimingStep.BreachBoundary
            or RunTimingStep.BreachCandidateSelection
            or RunTimingStep.BreachAccessAbility => RunCrPhase.Success,
        RunTimingStep.RunEnd => RunCrPhase.RunEnd,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown run timing step."),
    };

    private static void ValidateRulesState(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
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

                if (runtimeCardCatalog is not null)
                {
                    if (!runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? runtimeSpec))
                    {
                        Add(
                            violations,
                            "rules.runtime-program-metadata-missing",
                            $"{programPath}.cardId",
                            $"Runtime metadata for registered program '{card.DefinitionId}' is unavailable.");
                    }
                    else
                    {
                        if (runtimeSpec.Side != PlayerSide.Runner
                            || runtimeSpec.CardClass != RuntimeCardClass.Program)
                        {
                            Add(
                                violations,
                                "rules.runtime-program-class-mismatch",
                                $"{programPath}.cardId",
                                "A registered program must resolve to a Runner Program in the runtime catalog.");
                        }

                        if (program.MemoryCost != runtimeSpec.MemoryUnits)
                        {
                            Add(
                                violations,
                                "rules.runtime-program-memory-mismatch",
                                $"{programPath}.memoryCost",
                                $"Registered memory {program.MemoryCost} must equal the authoritative runtime memory {runtimeSpec.MemoryUnits} for '{card.DefinitionId}'.");
                        }
                    }
                }
            }

            calculatedMemory.TryGetValue(program.Owner, out long used);
            try
            {
                calculatedMemory[program.Owner] = checked(used + program.MemoryCost);
            }
            catch (OverflowException)
            {
                // Keep invariant collection diagnostic-only even when a
                // forged RulesState contains enough extreme memory entries to
                // overflow the aggregate.  The state is invalid; recording a
                // saturated total lets the remaining checks continue without
                // throwing from the collector itself.
                calculatedMemory[program.Owner] = long.MaxValue;
                Add(
                    violations,
                    "rules.memory-overflow",
                    $"{programPath}.memoryCost",
                    "The checked sum of registered program memory overflowed.");
            }
        }

        // Once a runtime catalog is injected, every installed Runner Program
        // must have one matching registration.  Otherwise a forged/missing
        // RulesState entry could make the install reducer under-count memory
        // while still appearing internally self-consistent.
        if (runtimeCardCatalog is not null)
        {
            foreach (CardInstanceState card in state.Cards)
            {
                ZoneState? zone = state.Zones.FirstOrDefault(candidate => candidate?.Id == card.ZoneId);
                if (card.OwnerSide != PlayerSide.Runner
                    || zone?.Kind != ZoneKind.RunnerRig
                    || !runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? runtimeSpec)
                    || runtimeSpec.CardClass != RuntimeCardClass.Program)
                {
                    continue;
                }

                if (!programIds.Contains(card.Id))
                {
                    Add(
                        violations,
                        "rules.missing-runtime-program-registration",
                        $"{path}.programs",
                        $"Installed Runner Program '{card.Id}' is missing its runtime memory registration.");
                }
            }
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


            int effectiveMemory = EffectiveMemoryLimit(state, behaviorCatalog, player.PlayerId);
            if (player.UsedMemory > effectiveMemory)
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

    private static void ValidateResolutionAndDecision(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        bool enforceCatalogCompatibility,
        List<StateInvariantViolation> violations)
    {
        var playerIds = new HashSet<PlayerId>(
            state.Players.Where(static player => player is not null).Select(static player => player!.Id));
        var cardIds = new HashSet<CardInstanceId>(
            state.Cards.Where(static card => card is not null).Select(static card => card!.Id));
        var cardsById = state.Cards
            .Where(static card => card is not null)
            .GroupBy(static card => card!.Id)
            .ToDictionary(static group => group.Key, static group => group.First()!);

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
                case TurnFrame turnFrame:
                    PlayerState? turnPlayer = state.Players.FirstOrDefault(player => player?.Side == turnFrame.ActiveSide);
                    if (turnPlayer is null)
                    {
                        Add(violations, "resolution.unknown-player", $"{path}.activeSide", $"Turn frame side '{turnFrame.ActiveSide}' has no player.");
                    }

                    if (state.Turn.ActiveSide != turnFrame.ActiveSide)
                    {
                        Add(violations, "resolution.turn-side-mismatch", $"{path}.activeSide", "Turn frame side must match the active turn side.");
                    }

                    if (state.Turn.TurnNumber != turnFrame.TurnNumber)
                    {
                        Add(violations, "resolution.turn-number-mismatch", $"{path}.turnNumber", "Turn frame number must match the active turn number.");
                    }

                    if (state.Turn.Step != TurnStep.None && state.Turn.Step != turnFrame.Step)
                    {
                        Add(violations, "resolution.turn-step-mismatch", $"{path}.stage", "Turn frame stage must match the active turn step.");
                    }

                    if (state.Turn.Step != TurnStep.None && state.Turn.History is null)
                    {
                        Add(violations, "resolution.turn-history-missing", $"{path}", "A real turn frame requires explicit current/last-turn facts.");
                    }

                    if (turnFrame.Stage is TurnFrameStage.BeforeTurnPriority
                        or TurnFrameStage.ActionPriority
                        or TurnFrameStage.DiscardPriority)
                    {
                        if (turnFrame.PriorityHolder.HasValue && !IsPlayerSide(turnFrame.PriorityHolder.Value))
                        {
                            Add(violations, "resolution.invalid-priority-holder", $"{path}.priorityHolder", "Turn priority holder must be a valid player side.");
                        }

                        if (turnFrame.PriorityHolder.HasValue
                            && turnFrame.PriorityHolder.Value != PlayerSide.Corp
                            && turnFrame.PriorityHolder.Value != PlayerSide.Runner)
                        {
                            Add(violations, "resolution.invalid-priority-holder", $"{path}.priorityHolder", "Turn priority holder must be Corp or Runner.");
                        }
                    }

                    if (turnFrame.Stage == TurnFrameStage.ActionPhaseEnd
                        && turnPlayer is not null
                        && turnPlayer.Clicks > 0
                        && !turnFrame.ForcedActionEnd)
                    {
                        Add(violations, "resolution.turn-action-end-with-clicks", $"{path}.stage", "An action phase cannot end with clicks remaining unless a direct effect forced it.");
                    }

                    break;
                case TurnDiscardFrame turnDiscard:
                    RequirePlayer(playerIds, turnDiscard.Owner, $"{path}.owner", violations);
                    PlayerState? discardOwner = state.Players.FirstOrDefault(player => player?.Id == turnDiscard.Owner);
                    if (index == 0 || state.ResolutionStack[index - 1] is not TurnFrame { Stage: TurnFrameStage.DiscardSelection } parentTurn)
                    {
                        Add(violations, "resolution.turn-discard-parent-missing", path, "A turn discard prompt requires a parent turn frame in discard selection.");
                    }
                    else
                    {
                        PlayerState? owner = state.Players.FirstOrDefault(player => player?.Id == turnDiscard.Owner);
                        if (owner is null || owner.Side != parentTurn.ActiveSide)
                        {
                            Add(violations, "resolution.turn-discard-owner-mismatch", $"{path}.owner", "Discard owner must be the active turn side.");
                        }
                    }

                    ZoneKind discardHandKind = discardOwner?.Side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
                    ZoneId? discardHandId = discardOwner is null
                        ? null
                        : state.Zones.FirstOrDefault(zone => zone?.Kind == discardHandKind && zone.ServerId is null)?.Id;
                    foreach (CardInstanceId candidate in turnDiscard.Candidates)
                    {
                        CardInstanceState? card = state.Cards.FirstOrDefault(value => value?.Id == candidate);
                        if (card is null)
                        {
                            Add(violations, "resolution.unknown-card-option", $"{path}.candidates", $"Discard candidate '{candidate}' does not identify a card in the state.");
                        }
                        else if (discardHandId is null || card.ZoneId != discardHandId.Value)
                        {
                            Add(violations, "resolution.stale-discard-candidate", $"{path}.candidates", $"Discard candidate '{candidate}' is not in the owner's hand.");
                        }
                    }

                    break;
                case InstallFrame install:
                    RequirePlayer(playerIds, install.Owner, $"{path}.owner", violations);
                    if (index != state.ResolutionStack.Count - 1)
                    {
                        Add(violations, "resolution.install-not-top", path, "An install selection frame must remain the top frame while its decision is pending.");
                    }

                    PlayerState? installOwner = state.Players.FirstOrDefault(player => player?.Id == install.Owner);
                    if (install.InitiationKind == InstallInitiationKind.BehaviorEffect)
                    {
                        ValidateBehaviorEffectInstallFrame(
                            state,
                            install,
                            index,
                            path,
                            installOwner,
                            behaviorCatalog,
                            runtimeCardCatalog,
                            violations);
                        break;
                    }

                    if (installOwner?.Side == PlayerSide.Corp)
                    {
                        ValidateCorpInstallFrame(
                            state,
                            install,
                            index,
                            path,
                            installOwner,
                            runtimeCardCatalog,
                            violations);
                        break;
                    }

                    if (installOwner is not null && installOwner.Side != PlayerSide.Runner)
                    {
                        Add(
                            violations,
                            "resolution.install-owner-side",
                            $"{path}.owner",
                            "B1a install frames are Runner-only; the frame owner must be the Runner.");
                    }

                    if (index == 0 || state.ResolutionStack[index - 1] is not TurnFrame { Stage: TurnFrameStage.AwaitingAction } installParentTurn)
                    {
                        Add(violations, "resolution.install-parent-missing", path, "An install frame requires a parent turn action frame.");
                    }
                    else
                    {
                        if (installParentTurn.ActiveSide != PlayerSide.Runner)
                        {
                            Add(
                                violations,
                                "resolution.install-parent-side",
                                $"{path}.parent",
                                "B1a install frames require a Runner AwaitingAction parent turn frame.");
                        }

                        if (installOwner is null || installParentTurn.ActiveSide != installOwner.Side)
                        {
                            Add(violations, "resolution.install-owner-mismatch", $"{path}.owner", "Install owner must be the active side of its parent turn frame.");
                        }
                    }

                    ZoneState? installSource = state.Zones.FirstOrDefault(zone => zone?.Id == install.SourceZoneId);
                    ZoneState? runnerGrip = state.Zones.FirstOrDefault(
                        zone => zone?.Kind == ZoneKind.RunnerGrip
                            && zone.ServerId is null);
                    if (install.SourceZoneKind != ZoneKind.RunnerGrip
                        || installSource is null
                        || installSource.Kind != ZoneKind.RunnerGrip
                        || installSource.OwnerSide != PlayerSide.Runner
                        || installSource.ServerId is not null
                        || runnerGrip is null
                        || install.SourceZoneId != runnerGrip.Id)
                    {
                        Add(
                            violations,
                            "resolution.install-source-mismatch",
                            $"{path}.sourceZoneId",
                            "B1a install frames must capture the canonical Runner Grip as their source zone.");
                    }

                    ZoneState? runnerRig = state.Zones.FirstOrDefault(
                        zone => zone?.Kind == ZoneKind.RunnerRig
                            && zone.ServerId is null);
                    if (install.DestinationKind != InstallDestinationKind.RunnerRig)
                    {
                        Add(
                            violations,
                            "resolution.install-destination-kind",
                            $"{path}.destinationKind",
                            "B1a install frames must target the Runner Rig.");
                    }

                    if (install.ServerId is not null || install.CreateNewRemote)
                    {
                        Add(
                            violations,
                            "resolution.install-destination-remote",
                            $"{path}.destinationKind",
                            "B1a Runner install frames must not identify a Corp server or create a remote.");
                    }

                    if (runnerRig is null)
                    {
                        Add(
                            violations,
                            "resolution.install-destination-missing",
                            $"{path}.destinationZoneId",
                            "B1a install frames require the canonical Runner Rig destination zone.");
                    }
                    else if (install.DestinationZoneId != runnerRig.Id)
                    {
                        Add(
                            violations,
                            "resolution.install-destination-mismatch",
                            $"{path}.destinationZoneId",
                            "B1a install frames must retain the current canonical Runner Rig zone ID.");
                    }

                    CardInstanceState? installCard = state.Cards.FirstOrDefault(card => card?.Id == install.CardId);
                    CardRuntimeSpec? installSpecForProjection = null;
                    if (installCard is null)
                    {
                        Add(violations, "resolution.unknown-install-card", $"{path}.cardId", $"Install card '{install.CardId}' does not exist.");
                    }
                    else
                    {
                        if (installCard.DefinitionId != install.DefinitionId)
                        {
                            Add(violations, "resolution.install-definition-mismatch", path, "Install frame definition must match its physical card.");
                        }

                        if (installCard.ZoneId != install.SourceZoneId)
                        {
                            Add(violations, "resolution.stale-install-card", $"{path}.sourceZoneId", "Install card must remain in its frozen source zone.");
                        }

                        if (installCard.OwnerSide != PlayerSide.Runner)
                        {
                            Add(
                                violations,
                                "resolution.install-card-owner-side",
                                $"{path}.cardId",
                                "B1a install frames may only install a Runner-owned card from the Runner Grip.");
                        }

                        if (runtimeCardCatalog is not null)
                        {
                            if (!runtimeCardCatalog.TryGet(installCard.DefinitionId, out CardRuntimeSpec? runtimeSpec))
                            {
                                Add(
                                    violations,
                                    "resolution.install-runtime-card-missing",
                                    $"{path}.cardId",
                                    $"Runtime metadata for install card '{installCard.DefinitionId}' is unavailable.");
                            }
                            else
                            {
                                installSpecForProjection = runtimeSpec;
                                if (runtimeSpec.Side != PlayerSide.Runner
                                    || !IsRunnerInstallableRuntimeClass(runtimeSpec.CardClass))
                                {
                                    Add(
                                        violations,
                                        "resolution.install-runtime-class-invalid",
                                        $"{path}.cardClass",
                                        "B1a install frames require a Runner Program, Hardware, or Resource runtime class.");
                                }

                                if (install.CardClass != runtimeSpec.CardClass)
                                {
                                    Add(
                                        violations,
                                        "resolution.install-runtime-class-mismatch",
                                        $"{path}.cardClass",
                                        $"Install frame class '{install.CardClass}' must match runtime class '{runtimeSpec.CardClass}' for '{installCard.DefinitionId}'.");
                                }
                            }
                        }
                    }

                    if (runnerRig is not null
                        && runtimeCardCatalog is not null
                        && installSpecForProjection is
                        { Side: PlayerSide.Runner } runnerInstallSpec)
                    {
                        if (runnerInstallSpec.CardClass != RuntimeCardClass.Program
                            && (runnerInstallSpec.Unique || runnerInstallSpec.Console)
                            && !AllActiveRunnerRigCardsKnown(state, runtimeCardCatalog, runnerRig))
                        {
                            Add(
                                violations,
                                "resolution.install-runtime-candidate-missing",
                                $"{path}.candidateReplacementCardIds",
                                "A unique or Console Runner install cannot classify every active Rig card from authoritative runtime metadata.");
                        }

                        CardInstanceId[] expectedCandidates = RunnerInstallCandidateIds(
                            state,
                            runtimeCardCatalog,
                            runnerRig,
                            runnerInstallSpec);
                        if (!install.CandidateReplacementCardIds.SequenceEqual(expectedCandidates))
                        {
                            Add(
                                violations,
                                "resolution.install-candidates-mismatch",
                                $"{path}.candidateReplacementCardIds",
                                "Runner install candidates must equal installed Programs plus active uniqueness/Console conflicts, or the exact conflict set for non-Programs.");
                        }

                        CardInstanceId[] expectedRequired = ActiveRunnerInstallConflictIds(
                            state,
                            runtimeCardCatalog,
                            runnerInstallSpec);
                        if (!install.RequiredReplacementCardIds.SequenceEqual(expectedRequired))
                        {
                            Add(
                                violations,
                                "resolution.install-required-mismatch",
                                $"{path}.requiredReplacementCardIds",
                                "Runner install required replacements must equal the exact active uniqueness/Console conflict set.");
                        }

                        if (install.CandidateReplacementDefinitionIds is not null)
                        {
                            CardDefinitionId[] expectedDefinitions = expectedCandidates
                                .Select(cardId => state.Cards.FirstOrDefault(card => card?.Id == cardId)?.DefinitionId)
                                .Where(definitionId => definitionId.HasValue)
                                .Select(definitionId => definitionId!.Value)
                                .ToArray();
                            if (!install.CandidateReplacementDefinitionIds.SequenceEqual(expectedDefinitions))
                            {
                                Add(
                                    violations,
                                    "resolution.install-candidate-definitions-mismatch",
                                    $"{path}.candidateReplacementDefinitionIds",
                                    "Runner install replacement definitions must match the current installed Programs plus active conflicts.");
                            }
                        }
                    }

                    foreach (CardInstanceId candidate in install.CandidateReplacementCardIds)
                    {
                        CardInstanceState? candidateCard = state.Cards.FirstOrDefault(card => card?.Id == candidate);
                        if (candidateCard is null)
                        {
                            Add(violations, "resolution.unknown-install-candidate", $"{path}.candidateReplacementCardIds", $"Install candidate '{candidate}' does not exist.");
                        }
                    }

                    break;
                case CardPlayFrame cardPlay:
                    ValidateCardPlayFrame(
                        state,
                        cardPlay,
                        index,
                        path,
                        behaviorCatalog,
                        runtimeCardCatalog,
                        violations);
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
                case BehaviorResolutionFrame behaviorFrame:
                    RequirePlayer(playerIds, behaviorFrame.Controller, $"{path}.controller", violations);
                    if (!behaviorFrame.ActingPlayer.HasValue)
                    {
                        Add(violations, "resolution.behavior-acting-player-missing", $"{path}.actingPlayer", "A behavior frame must retain its acting-player provenance.");
                    }
                    else
                    {
                        RequirePlayer(playerIds, behaviorFrame.ActingPlayer.Value, $"{path}.actingPlayer", violations);
                    }

                    if (behaviorFrame.ParentBehaviorId is not null)
                    {
                        if (!StableIdentifier.TryValidate(behaviorFrame.ParentBehaviorId))
                        {
                            Add(violations, "resolution.invalid-parent-behavior", $"{path}.parentBehaviorId", "A behavior parent identity must be stable.");
                        }
                        else if (index == 0
                            || !string.Equals(
                                state.ResolutionStack[index - 1].BehaviorId,
                                behaviorFrame.ParentBehaviorId,
                                StringComparison.Ordinal))
                        {
                            Add(violations, "resolution.behavior-parent-missing", path, "A nested behavior frame must remain immediately above its captured parent frame.");
                        }
                    }

                    if ((behaviorFrame.ParentBehaviorId is null) != (behaviorFrame.ParentFrameIdentity is null))
                    {
                        Add(
                            violations,
                            "resolution.behavior-parent-missing",
                            path,
                            "Behavior parent ID and parent frame identity must be present together.");
                    }

                    if (!BehaviorFrameIdentity.TryValidateIssued(behaviorFrame.FrameIdentity, out _))
                    {
                        Add(violations, "resolution.behavior-frame-identity", $"{path}.frameIdentity", "A behavior frame must retain an engine-issued frame identity.");
                    }

                    if (behaviorFrame.SourceZoneId is null || behaviorFrame.SourceZoneKind is null)
                    {
                        Add(violations, "resolution.behavior-source-location-missing", path, "A behavior frame must retain its invocation source location.");
                    }
                    else
                    {
                        ZoneState? sourceZone = state.Zones.FirstOrDefault(zone => zone?.Id == behaviorFrame.SourceZoneId.Value);
                        if (sourceZone is null)
                        {
                            Add(violations, "resolution.behavior-source-location-missing", $"{path}.sourceZoneId", "Behavior source location does not identify a zone in the state.");
                        }
                        else
                        {
                            if (sourceZone.Kind != behaviorFrame.SourceZoneKind.Value)
                            {
                                Add(violations, "resolution.behavior-source-location-mismatch", path, "Behavior source location kind does not match the captured invocation context.");
                            }

                            if (cardIds.Contains(behaviorFrame.SourceCardId)
                                && state.Cards.Single(card => card.Id == behaviorFrame.SourceCardId).ZoneId != sourceZone.Id)
                            {
                                Add(violations, "resolution.behavior-source-location-mismatch", $"{path}.sourceZoneId", "Behavior source card no longer occupies its captured invocation location.");
                            }
                        }
                    }

                    if (behaviorFrame.ParentFrameIdentity is not null)
                    {
                        if (index == 0)
                        {
                            Add(violations, "resolution.behavior-parent-missing", path, "A behavior parent identity requires an immediately preceding parent frame.");
                        }
                        else
                        {
                            string? actualParentIdentity = null;
                            try
                            {
                                actualParentIdentity = BehaviorFrameIdentity.Compute(state.ResolutionStack[index - 1]);
                            }
                            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
                            {
                                // The parent frame's own diagnostics are more
                                // useful than duplicating a serializer failure.
                            }

                            if (!string.Equals(
                                    behaviorFrame.ParentFrameIdentity,
                                    actualParentIdentity,
                                    StringComparison.Ordinal))
                            {
                                Add(violations, "resolution.behavior-parent-context-mismatch", path, "Behavior parent identity does not match the exact active parent frame context.");
                            }
                        }
                    }

                    if (!cardIds.Contains(behaviorFrame.SourceCardId))
                    {
                        Add(violations, "resolution.unknown-behavior-source", $"{path}.sourceCardId", $"Behavior source card '{behaviorFrame.SourceCardId}' does not exist.");
                    }
                    else if (state.Cards.Single(card => card.Id == behaviorFrame.SourceCardId).DefinitionId != behaviorFrame.DefinitionId)
                    {
                        Add(violations, "resolution.behavior-source-definition-mismatch", path, "Behavior source card definition must match the frame definition.");
                    }

                    if (!string.Equals(behaviorFrame.CatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
                    {
                        Add(violations, "resolution.behavior-catalog-mismatch", $"{path}.catalogFingerprint", "Behavior frame catalog fingerprint must match the injected catalog.");
                    }
                    else if (behaviorCatalog.TryGet(behaviorFrame.DefinitionId, out CardBehaviorDefinition? definition))
                    {
                        if (!string.Equals(behaviorFrame.DefinitionFingerprint, behaviorCatalog.GetDefinitionFingerprint(behaviorFrame.DefinitionId), StringComparison.Ordinal))
                        {
                            Add(violations, "resolution.behavior-definition-mismatch", $"{path}.definitionFingerprint", "Behavior frame definition fingerprint must match the injected catalog.");
                        }

                        var derivedInstructions = definition.Instructions.ToList();
                        foreach (BehaviorContinuationStep step in behaviorFrame.Continuation)
                        {
                            if (!BehaviorContinuation.TryApply(derivedInstructions, step, out string? continuationError))
                            {
                                Add(
                                    violations,
                                    "resolution.behavior-continuation-mismatch",
                                    $"{path}.continuation",
                                    continuationError ?? "Behavior continuation is not reachable from the catalog.");
                                break;
                            }
                        }

                        if (behaviorFrame.InstructionCursor < 0
                            || behaviorFrame.InstructionCursor > derivedInstructions.Count)
                        {
                            Add(violations, "resolution.behavior-cursor-invalid", $"{path}.instructionCursor", "Behavior cursor is outside the catalog-derived continuation.");
                        }

                        ValidateBehaviorContinuationBindings(
                            behaviorFrame,
                            definition.Instructions,
                            cardsById,
                            $"{path}",
                            violations);
                        if (behaviorFrame.Choice is BehaviorChoiceMaterialization materialized)
                        {
                            ValidateBehaviorChoiceMaterialization(
                                state,
                                behaviorCatalog,
                                runtimeCardCatalog,
                                behaviorFrame,
                                materialized,
                                derivedInstructions,
                                cardsById,
                                path,
                                violations);
                        }
                    }
                    else
                    {
                        Add(violations, "resolution.unknown-behavior-definition", $"{path}.definitionId", $"Behavior definition '{behaviorFrame.DefinitionId}' is not registered.");
                    }

                    var selectedSetAsideIds = new HashSet<CardInstanceId>();
                    foreach (BehaviorSetAsideCard selected in behaviorFrame.SetAside)
                    {
                        if (!cardIds.Contains(selected.CardId))
                        {
                            Add(violations, "resolution.unknown-behavior-selection", $"{path}.setAside", $"Behavior selection '{selected.CardId}' does not exist.");
                        }
                        else if (behaviorFrame.SetAsideZoneId is null
                            || state.Cards.Single(card => card.Id == selected.CardId).ZoneId != behaviorFrame.SetAsideZoneId.Value)
                        {
                            Add(violations, "resolution.stale-behavior-selection", $"{path}.setAside", $"Behavior selection '{selected.CardId}' is no longer in its frozen set-aside zone.");
                        }

                        if (!selectedSetAsideIds.Add(selected.CardId))
                        {
                            Add(violations, "resolution.duplicate-behavior-selection", $"{path}.setAside", $"Behavior selection '{selected.CardId}' occurs more than once.");
                        }

                        if (!StableIdentifier.TryValidate(selected.OriginalZoneId.Value))
                        {
                            Add(violations, "id.invalid", $"{path}.setAside", "A behavior selection original zone ID must be stable.");
                        }
                        else
                        {
                            ZoneState? originalZone = state.Zones.FirstOrDefault(zone => zone?.Id == selected.OriginalZoneId);
                            if (originalZone is null || originalZone.Kind == ZoneKind.BehaviorSetAside)
                            {
                                Add(violations, "resolution.behavior-selection-source-invalid", $"{path}.setAside", "A behavior selection must retain an existing non-transient original zone.");
                            }
                        }

                        if (selected.OriginalIndex < -1)
                        {
                            Add(violations, "resolution.invalid-behavior-selection-index", $"{path}.setAside", "A behavior selection original index must be -1 or nonnegative.");
                        }
                    }

                    if (behaviorFrame.SetAside.Count > 0)
                    {
                        if (behaviorFrame.SetAsideZoneId is null)
                        {
                            Add(violations, "resolution.behavior-set-aside-zone-missing", $"{path}.setAsideZoneId", "Selected behavior cards require a physical set-aside zone.");
                        }
                        else
                        {
                            ZoneState? setAsideZone = state.Zones.FirstOrDefault(zone => zone?.Id == behaviorFrame.SetAsideZoneId.Value);
                            if (setAsideZone is null || setAsideZone.Kind != ZoneKind.BehaviorSetAside)
                            {
                                Add(violations, "resolution.behavior-set-aside-zone-invalid", $"{path}.setAsideZoneId", "Behavior selections must point to a behavior set-aside zone.");
                            }
                            else if (!setAsideZone.CardIds.ToHashSet().SetEquals(selectedSetAsideIds))
                            {
                                Add(violations, "resolution.behavior-set-aside-membership", $"{path}.setAside", "The physical behavior set-aside zone must contain exactly the frame's selected cards.");
                            }
                        }
                    }
                    else if (behaviorFrame.SetAsideZoneId is not null)
                    {
                        Add(violations, "resolution.behavior-set-aside-zone-unexpected", $"{path}.setAsideZoneId", "An empty behavior selection must not retain a set-aside zone.");
                    }

                    break;
                case EffectResolutionFrame effectFrame:
                    RequirePlayer(playerIds, effectFrame.Controller, $"{path}.controller", violations);
                    ValidateTurnBoundaryFrameContext(
                        state,
                        effectFrame,
                        index,
                        path,
                        violations);
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
            ValidatePendingDecision(
                state,
                pending,
                playerIds,
                cardIds,
                cardsById,
                behaviorCatalog,
                runtimeCardCatalog,
                enforceCatalogCompatibility,
                violations);
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

    private static void ValidateTurnBoundaryFrameContext(
        GameState state,
        EffectResolutionFrame frame,
        int index,
        string path,
        List<StateInvariantViolation> violations)
    {
        TurnBoundaryEffect[] boundaries = frame.Effects.Instructions
            .OfType<TurnBoundaryEffect>()
            .ToArray();
        if (boundaries.Length == 0)
        {
            return;
        }

        if (boundaries.Length != frame.Effects.Instructions.Count)
        {
            Add(
                violations,
                "resolution.mixed-turn-boundary-effects",
                $"{path}.effects.instructions",
                "Turn-boundary checkpoints may contain only turn-boundary instructions.");
            return;
        }

        if (index == 0 || state.ResolutionStack[index - 1] is not TurnFrame parent)
        {
            Add(
                violations,
                "resolution.turn-boundary-parent-missing",
                path,
                "A turn-boundary occurrence requires a matching parent turn frame.");
            return;
        }

        if (boundaries.Any(boundary => boundary.Target != frame.Controller))
        {
            Add(
                violations,
                "resolution.turn-boundary-controller-mismatch",
                $"{path}.effects.instructions",
                "A turn-boundary occurrence target must match its effect controller.");
        }

        PlayerSide? targetSide = state.Players
            .FirstOrDefault(player => player.Id == frame.Controller)
            ?.Side;
        if (!targetSide.HasValue)
        {
            return;
        }

        foreach (TurnBoundaryEffect boundary in boundaries)
        {
            bool validContext = boundary.Kind switch
            {
                OccurrenceKind.TurnBegin =>
                    targetSide == parent.ActiveSide
                    && (parent.ActiveSide == PlayerSide.Corp
                        && parent.Stage == TurnFrameStage.MandatoryDraw
                        || parent.ActiveSide == PlayerSide.Runner
                        && parent.Stage == TurnFrameStage.ActionPriority)
                    && boundary.TurnNumber == parent.TurnNumber,
                OccurrenceKind.TurnEnd =>
                    targetSide != parent.ActiveSide
                    && parent.Stage == TurnFrameStage.GainAllottedClicks
                    && boundary.TurnNumber == (targetSide == PlayerSide.Corp
                        ? parent.TurnNumber
                        : parent.TurnNumber - 1),
                _ => false,
            };
            if (!validContext)
            {
                Add(
                    violations,
                    "resolution.turn-boundary-context-mismatch",
                    $"{path}.effects.instructions",
                    $"Turn boundary '{boundary.Kind}' does not match the parent turn cursor context.");
            }
        }
    }

    private static void ValidatePendingDecision(
        GameState state,
        PendingDecision pending,
        HashSet<PlayerId> playerIds,
        HashSet<CardInstanceId> cardIds,
        Dictionary<CardInstanceId, CardInstanceState> cardsById,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        bool enforceCatalogCompatibility,
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
                case TurnFrame turnTimingFrame
                    when turnTimingFrame.Stage is TurnFrameStage.BeforeTurnPriority
                        or TurnFrameStage.ActionPriority
                        or TurnFrameStage.DiscardPriority:
                    if (pending.Kind != DecisionKind.TurnTiming)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A turn priority window requires a TurnTiming decision.");
                    }

                    PlayerId? timingOwner = state.Players.FirstOrDefault(player => player?.Side == turnTimingFrame.PriorityHolder)?.Id;
                    if (timingOwner is null || pending.Owner != timingOwner.Value)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Turn priority decision owner must match the materialized priority holder.");
                    }

                    break;
                case TurnFrame turnActionFrame when turnActionFrame.Stage == TurnFrameStage.AwaitingAction:
                    if (pending.Kind != DecisionKind.TurnAction)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A turn action stage requires a TurnAction decision.");
                    }

                    PlayerId? actionOwner = state.Players.FirstOrDefault(player => player?.Side == turnActionFrame.ActiveSide)?.Id;
                    if (actionOwner is null || pending.Owner != actionOwner.Value)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Turn action decision owner must be the active side's player.");
                    }

                    break;
                case TurnDiscardFrame discardFrame:
                    if (pending.Kind != DecisionKind.TurnDiscard)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A turn discard frame requires a TurnDiscard decision.");
                    }

                    if (pending.Owner != discardFrame.Owner)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Turn discard decision owner must match its frame.");
                    }

                    break;
                case InstallFrame installFrame:
                    if (pending.Kind != DecisionKind.InstallChoice)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "An install frame requires an InstallChoice decision.");
                    }

                    if (pending.Owner != installFrame.Owner)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Install decision owner must match its frame.");
                    }

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
                case BehaviorResolutionFrame behaviorFrame when behaviorFrame.Choice is null:
                    Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A behavior frame requires a materialized choice while a decision is pending.");
                    break;
                case BehaviorResolutionFrame behaviorFrame:
                    if (pending.Kind != DecisionKind.BehaviorChoice)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A behavior choice frame requires a BehaviorChoice decision.");
                    }
                    else if (pending.Owner != behaviorFrame.Choice!.Owner)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "Behavior decision owner must match the materialized choice owner.");
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
                    else if (topFrame is TurnDiscardFrame discardPrompt
                        && (!discardPrompt.Candidates.Contains(cardChoice.CardId)
                            || discardPrompt.Selected.Contains(cardChoice.CardId)))
                    {
                        Add(violations, "decision.card-action-not-offered", $"{actionPath}.parameters.cardId", "Discard action is not one of the remaining materialized frame options.");
                    }
                    else if (topFrame is ChooseCardFrame && !cardChoices.Add(cardChoice.CardId))
                    {
                        Add(violations, "decision.duplicate-card-choice", $"{actionPath}.parameters.cardId", $"Card choice '{cardChoice.CardId}' occurs more than once.");
                    }

                    break;
                case (ActionKind.ChooseBehavior, BehaviorChoiceParameters behaviorChoice):
                    if (pending.Kind != DecisionKind.BehaviorChoice)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Behavior choices require a BehaviorChoice decision.");
                    }

                    if (topFrame is BehaviorResolutionFrame behaviorFrame && behaviorFrame.Choice is BehaviorChoiceMaterialization materialized)
                    {
                        if (!string.Equals(materialized.Key, behaviorChoice.ChoiceKey, StringComparison.Ordinal))
                        {
                            Add(violations, "decision.behavior-choice-key-mismatch", $"{actionPath}.parameters.choiceKey", "Behavior action key does not match the materialized choice.");
                        }

                        if (behaviorChoice.OptionKeys.Count > 1
                            || (behaviorChoice.CardIds.Count > 0 && behaviorChoice.OptionKeys.Count > 0))
                        {
                            Add(violations, "decision.behavior-choice-payload", $"{actionPath}.parameters", "Behavior action must select one option or a card set, never both.");
                        }

                        if (!behaviorChoice.FinalizeSelection
                            && (behaviorChoice.OptionKeys.Count != 0
                                || behaviorChoice.CardIds.Count != 1
                                || materialized.SelectedCardIds.Contains(behaviorChoice.CardIds[0])))
                        {
                            Add(
                                violations,
                                "decision.behavior-incremental-payload",
                                $"{actionPath}.parameters",
                                "An incremental behavior action must add exactly one previously unselected card.");
                        }

                        if (behaviorChoice.FinalizeSelection
                            && materialized.SelectedCardIds.Count > 0
                            && materialized.CandidateCardIds.Count > 0
                            && behaviorChoice.CardIds.Count > 0
                            && !behaviorChoice.CardIds.SequenceEqual(materialized.SelectedCardIds))
                        {
                            Add(
                                violations,
                                "decision.behavior-selection-mismatch",
                                $"{actionPath}.parameters.cardIds",
                                "A final incremental behavior action must submit the frozen partial selection.");
                        }

                        if (behaviorChoice.OptionKeys.Any(key => !materialized.OptionKeys.Contains(key, StringComparer.Ordinal)))
                        {
                            Add(violations, "decision.behavior-option-not-offered", $"{actionPath}.parameters.optionKeys", "Behavior option is not one of the frozen materialized options.");
                        }

                        if (behaviorChoice.CardIds.Any(id => !materialized.CandidateCardIds.Contains(id)))
                        {
                            Add(violations, "decision.behavior-card-not-offered", $"{actionPath}.parameters.cardIds", "Behavior card is not one of the frozen materialized candidates.");
                        }
                    }

                    break;
                case (ActionKind.UseAbility, AbilityActionParameters ability):
                    ValidateAbilityAction(
                        state,
                        pending,
                        ability,
                        cardIds,
                        cardsById,
                        behaviorCatalog,
                        enforceCatalogCompatibility,
                        actionPath,
                        violations);
                    break;
                case (ActionKind.PlayCard, PlayCardParameters playCard):
                    if (pending.Kind != DecisionKind.TurnAction)
                    {
                        Add(
                            violations,
                            "decision.action-kind-mismatch",
                            $"{actionPath}.kind",
                            "Play-card actions require a TurnAction decision.");
                    }

                    if (!cardIds.Contains(playCard.CardId))
                    {
                        Add(
                            violations,
                            "decision.unknown-card-action",
                            $"{actionPath}.parameters.cardId",
                            $"Play action references unknown card '{playCard.CardId}'.");
                    }

                    if (playCard.CreditCost < 0)
                    {
                        Add(
                            violations,
                            "decision.invalid-play-cost",
                            $"{actionPath}.parameters.creditCost",
                            "Play action credit cost must not be negative.");
                    }

                    break;
                case (ActionKind.StartRun, RunServerParameters runServer):
                    if (pending.Kind != DecisionKind.TurnAction)
                    {
                        Add(
                            violations,
                            "decision.action-kind-mismatch",
                            $"{actionPath}.kind",
                            "Basic-run actions require a TurnAction decision.");
                    }

                    if (!state.Servers.Any(server => server?.Id == runServer.ServerId))
                    {
                        Add(
                            violations,
                            "decision.unknown-server-action",
                            $"{actionPath}.parameters.serverId",
                            $"Basic-run action references unknown server '{runServer.ServerId}'.");
                    }

                    break;
                case (ActionKind.AdvanceCard, AdvanceCardParameters advanceCard):
                    if (pending.Kind != DecisionKind.TurnAction)
                    {
                        Add(
                            violations,
                            "decision.advance-action-kind-mismatch",
                            $"{actionPath}.kind",
                            "Real advance actions require a TurnAction decision.");
                    }

                    if (!cardIds.Contains(advanceCard.CardId))
                    {
                        Add(
                            violations,
                            "decision.unknown-advance-target",
                            $"{actionPath}.parameters.cardId",
                            $"Advance action references unknown card '{advanceCard.CardId}'.");
                    }

                    if (advanceCard.CreditCost != 1)
                    {
                        Add(
                            violations,
                            "decision.advance-cost-mismatch",
                            $"{actionPath}.parameters.creditCost",
                            "A real advance action must carry exactly one credit of cost.");
                    }

                    break;
                case (ActionKind.RemoveTag, RemoveTagParameters removeTag):
                    if (pending.Kind != DecisionKind.TurnAction)
                    {
                        Add(
                            violations,
                            "decision.remove-tag-action-kind-mismatch",
                            $"{actionPath}.kind",
                            "Remove-tag actions require a TurnAction decision.");
                    }

                    if (removeTag.ClickCost != RealGameRules.RemoveTagClickCost)
                    {
                        Add(
                            violations,
                            "decision.remove-tag-click-cost-mismatch",
                            $"{actionPath}.parameters.clickCost",
                            "A remove-tag action must carry exactly one click of cost.");
                    }

                    if (removeTag.CreditCost != RealGameRules.RemoveTagCreditCost)
                    {
                        Add(
                            violations,
                            "decision.remove-tag-credit-cost-mismatch",
                            $"{actionPath}.parameters.creditCost",
                            "A remove-tag action must carry exactly two credits of cost.");
                    }

                    break;
                case (ActionKind.TrashTaggedResource, TrashTaggedResourceParameters trashTaggedResource):
                    if (pending.Kind != DecisionKind.TurnAction)
                    {
                        Add(
                            violations,
                            "decision.trash-tagged-resource-action-kind-mismatch",
                            $"{actionPath}.kind",
                            "Tagged-resource trash actions require a TurnAction decision.");
                    }

                    if (!cardIds.Contains(trashTaggedResource.CardId))
                    {
                        Add(
                            violations,
                            "decision.unknown-tagged-resource-target",
                            $"{actionPath}.parameters.cardId",
                            $"Tagged-resource trash action references unknown card '{trashTaggedResource.CardId}'.");
                    }

                    if (trashTaggedResource.ClickCost != RealGameRules.TrashTaggedResourceClickCost)
                    {
                        Add(
                            violations,
                            "decision.trash-tagged-resource-click-cost-mismatch",
                            $"{actionPath}.parameters.clickCost",
                            "A tagged-resource trash action must carry exactly one click of cost.");
                    }

                    if (trashTaggedResource.CreditCost != RealGameRules.TrashTaggedResourceCreditCost)
                    {
                        Add(
                            violations,
                            "decision.trash-tagged-resource-credit-cost-mismatch",
                            $"{actionPath}.parameters.creditCost",
                            "A tagged-resource trash action must carry exactly two credits of cost.");
                    }

                    break;
                case (ActionKind.PurgeVirusCounters, PurgeVirusCountersParameters purgeVirusCounters):
                    if (pending.Kind != DecisionKind.TurnAction)
                    {
                        Add(
                            violations,
                            "decision.purge-virus-counters-action-kind-mismatch",
                            $"{actionPath}.kind",
                            "Virus-counter purge actions require a TurnAction decision.");
                    }

                    if (purgeVirusCounters.ClickCost != RealGameRules.PurgeVirusCountersClickCost)
                    {
                        Add(
                            violations,
                            "decision.purge-virus-counters-click-cost-mismatch",
                            $"{actionPath}.parameters.clickCost",
                            "A virus-counter purge action must carry exactly three clicks of cost.");
                    }

                    break;
                case (ActionKind.ScoreAgenda, ScoreAgendaParameters scoreAgenda):
                    if (pending.Kind != DecisionKind.TurnTiming)
                    {
                        Add(
                            violations,
                            "decision.score-action-kind-mismatch",
                            $"{actionPath}.kind",
                            "Real score actions require a TurnTiming decision.");
                    }

                    if (!cardIds.Contains(scoreAgenda.CardId))
                    {
                        Add(
                            violations,
                            "decision.unknown-score-target",
                            $"{actionPath}.parameters.cardId",
                            $"Score action references unknown card '{scoreAgenda.CardId}'.");
                    }

                    if (scoreAgenda.AgendaPoints <= 0)
                    {
                        Add(
                            violations,
                            "decision.score-points-invalid",
                            $"{actionPath}.parameters.agendaPoints",
                            "Score action agenda points must be positive.");
                    }

                    if (scoreAgenda.AdvancementRequirement <= 0)
                    {
                        Add(
                            violations,
                            "decision.score-requirement-invalid",
                            $"{actionPath}.parameters.advancementRequirement",
                            "Score action advancement requirement must be positive.");
                    }

                    break;
                case (ActionKind.RezCard, RezCardParameters rezCard):
                    if (pending.Kind is not (DecisionKind.TurnTiming or DecisionKind.RunTiming))
                    {
                        Add(
                            violations,
                            "decision.rez-action-kind-mismatch",
                            $"{actionPath}.kind",
                            "Runtime-catalog-backed rez actions require a TurnTiming or RunTiming decision.");
                    }

                    if (!cardIds.Contains(rezCard.CardId))
                    {
                        Add(
                            violations,
                            "decision.unknown-rez-target",
                            $"{actionPath}.parameters.cardId",
                            $"Rez action references unknown card '{rezCard.CardId}'.");
                    }

                    if (rezCard.CreditCost < 0)
                    {
                        Add(
                            violations,
                            "decision.rez-cost-invalid",
                            $"{actionPath}.parameters.creditCost",
                            "Rez action credit cost must not be negative.");
                    }

                    break;
                case (ActionKind.InstallCard, InstallCardParameters install):
                    ValidateInstallAction(
                        state,
                        runtimeCardCatalog,
                        pending,
                        topFrame,
                        install,
                        actionPath,
                        violations);
                    break;
                case (ActionKind.GainCredit or ActionKind.DrawCard or ActionKind.EndTurn, NoActionParameters):
                    if (pending.Kind != DecisionKind.ToyTurn
                        && pending.Kind != DecisionKind.TurnAction)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Gain/draw actions require a turn action decision.");
                    }

                    if (pending.Kind == DecisionKind.TurnAction && action.Kind == ActionKind.EndTurn)
                    {
                        Add(violations, "decision.illegal-end-turn", $"{actionPath}.kind", "EndTurn is not legal while a turn action decision remains active.");
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
                case (ActionKind.PassPriority or ActionKind.JackOut or ActionKind.ContinueRun or ActionKind.NoAccessAction, NoActionParameters):
                    if (pending.Kind != DecisionKind.RunTiming
                        && pending.Kind != DecisionKind.TurnTiming)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Priority actions require a timing decision.");
                    }

                    if (pending.Kind == DecisionKind.TurnTiming && action.Kind != ActionKind.PassPriority)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Turn timing decisions permit only PassPriority.");
                    }

                    break;
                case (ActionKind.ChooseBreachCandidate, BreachCandidateParameters breachCandidate):
                    if (pending.Kind != DecisionKind.RunTiming)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Run timing actions require a RunTiming decision.");
                    }

                    if (breachCandidate.CardId is not null && !cardIds.Contains(breachCandidate.CardId.Value))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.cardId", $"Run action references unknown card '{breachCandidate.CardId}'.");
                    }

                    break;
                case (ActionKind.TrashAccessedCard, CardChoiceParameters trashAccessed):
                    // Basic access trash is a payment choice. A plain
                    // CardChoiceParameters payload cannot preserve the
                    // Runner's selected wallet/hosted-credit division and is
                    // therefore not an authoritative run action.
                    Add(
                        violations,
                        "decision.action-parameters-mismatch",
                        $"{actionPath}.parameters",
                        "TrashAccessedCard requires RunCardPaymentParameters with the selected hosted-credit portion.");
                    break;
                case (ActionKind.TrashAccessedCard, RunCardPaymentParameters trashAccessed):
                    if (pending.Kind != DecisionKind.RunTiming)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Run timing actions require a RunTiming decision.");
                    }

                    if (!cardIds.Contains(trashAccessed.CardId))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.cardId", $"Run action references unknown card '{trashAccessed.CardId}'.");
                    }

                    if (trashAccessed.HostedCredits < 0)
                    {
                        Add(violations, "decision.invalid-payment", $"{actionPath}.parameters.hostedCredits", "Hosted payment credits must not be negative.");
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
                case (ActionKind.BreakSubroutines, RunBreakParameters breakSubroutines):
                    if (pending.Kind != DecisionKind.RunTiming)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Run timing actions require a RunTiming decision.");
                    }

                    if (!cardIds.Contains(breakSubroutines.BreakerId))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.breakerId", $"Run action references unknown breaker '{breakSubroutines.BreakerId}'.");
                    }

                    if (breakSubroutines.Quantity < 1)
                    {
                        Add(violations, "decision.invalid-break-quantity", $"{actionPath}.parameters.quantity", $"The break quantity '{breakSubroutines.Quantity}' must be at least 1.");
                    }

                    if (breakSubroutines.HostedCredits < 0)
                    {
                        Add(violations, "decision.invalid-payment", $"{actionPath}.parameters.hostedCredits", "Hosted payment credits must not be negative.");
                    }

                    break;
                case (ActionKind.PumpBreaker, RunBreakerParameters pumpBreaker):
                    if (pending.Kind != DecisionKind.RunTiming)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Run timing actions require a RunTiming decision.");
                    }

                    if (!cardIds.Contains(pumpBreaker.BreakerId))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.breakerId", $"Run action references unknown breaker '{pumpBreaker.BreakerId}'.");
                    }

                    if (pumpBreaker.HostedCredits < 0)
                    {
                        Add(violations, "decision.invalid-payment", $"{actionPath}.parameters.hostedCredits", "Hosted payment credits must not be negative.");
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
                case (ActionKind.PlayCard, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "PlayCard requires PlayCardParameters.");
                    break;
                case (ActionKind.StartRun, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "StartRun requires RunServerParameters.");
                    break;
                case (ActionKind.AdvanceCard, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "AdvanceCard requires AdvanceCardParameters.");
                    break;
                case (ActionKind.RemoveTag, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "RemoveTag requires RemoveTagParameters.");
                    break;
                case (ActionKind.TrashTaggedResource, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "TrashTaggedResource requires TrashTaggedResourceParameters.");
                    break;
                case (ActionKind.PurgeVirusCounters, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "PurgeVirusCounters requires PurgeVirusCountersParameters.");
                    break;
                case (ActionKind.RezCard, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "RezCard requires RezCardParameters.");
                    break;
                case (ActionKind.ScoreAgenda, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "ScoreAgenda requires ScoreAgendaParameters in a real timing window or CardChoiceParameters in a toy turn.");
                    break;
                case (ActionKind.ChooseBehavior, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "ChooseBehavior requires BehaviorChoiceParameters.");
                    break;
                case (ActionKind.UseAbility, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "UseAbility requires AbilityActionParameters.");
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
                || (pending.Kind == DecisionKind.TurnTiming
                    && action.Kind is not (ActionKind.PassPriority
                        or ActionKind.RezCard
                        or ActionKind.ScoreAgenda
                        or ActionKind.UseAbility))
                || (pending.Kind == DecisionKind.TurnAction
                    && action.Kind is not (ActionKind.GainCredit
                        or ActionKind.DrawCard
                        or ActionKind.InstallCard
                        or ActionKind.PlayCard
                        or ActionKind.StartRun
                        or ActionKind.AdvanceCard
                        or ActionKind.RemoveTag
                        or ActionKind.TrashTaggedResource
                        or ActionKind.PurgeVirusCounters
                        or ActionKind.UseAbility))
                || (pending.Kind == DecisionKind.InstallChoice
                    && action.Kind != ActionKind.InstallCard)
                || (pending.Kind == DecisionKind.TurnDiscard
                    && action.Kind != ActionKind.ChooseCard)
                || (pending.Kind == DecisionKind.RunTiming
                    && action.Kind is not (ActionKind.PassPriority
                        or ActionKind.RezCard
                        or ActionKind.RezIce
                        or ActionKind.BreakSubroutines
                        or ActionKind.PumpBreaker
                        or ActionKind.JackOut
                        or ActionKind.ContinueRun
                        or ActionKind.ChooseBreachCandidate
                        or ActionKind.TrashAccessedCard
                        or ActionKind.NoAccessAction
                        or ActionKind.UseAbility))
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
                        or ActionKind.DeclineReplacement))
                || (pending.Kind == DecisionKind.BehaviorChoice
                    && action.Kind != ActionKind.ChooseBehavior))
            {
                Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", $"Action kind '{action.Kind}' does not match decision kind '{pending.Kind}'.");
            }
        }

        if (pending.Kind == DecisionKind.InstallChoice
            && topFrame is InstallFrame installChoiceFrame)
        {
            ValidateInstallChoiceActions(
                state,
                behaviorCatalog,
                runtimeCardCatalog,
                pending,
                installChoiceFrame,
                path,
                violations);
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

        if (pending.Kind == DecisionKind.TurnAction
            && topFrame is TurnFrame { Stage: TurnFrameStage.AwaitingAction } turnAction)
        {
            ValidateTurnActions(state, behaviorCatalog, runtimeCardCatalog, pending, turnAction, path, violations);
        }

        if (pending.Kind == DecisionKind.TurnTiming
            && topFrame is TurnFrame
            {
                Stage: TurnFrameStage.BeforeTurnPriority
                    or TurnFrameStage.ActionPriority
                    or TurnFrameStage.DiscardPriority,
            } turnTiming)
        {
            ValidateTurnTimingActions(state, runtimeCardCatalog, pending, turnTiming, path, violations);
        }

        if (pending.Kind == DecisionKind.TurnDiscard
            && topFrame is TurnDiscardFrame turnDiscard)
        {
            ValidateTurnDiscardActions(pending, turnDiscard, path, violations);
        }

        if (pending.Kind == DecisionKind.RunTiming
            && topFrame is RunTimingFrame runTiming
            && state.RunState is not null)
        {
            ValidateRunActions(state, pending, state.RunState, runTiming, behaviorCatalog, runtimeCardCatalog, path, violations);
        }

        if (pending.Kind == DecisionKind.MemoryCleanup
            && topFrame is MemoryCleanupFrame memoryCleanup)
        {
            ValidateMemoryCleanupActions(state, behaviorCatalog, pending, memoryCleanup, path, violations);
        }

        if (topFrame is EffectCheckpointFrame checkpoint)
        {
            ValidateCheckpointActions(pending, checkpoint, path, violations);
        }
    }

    private static void ValidateAbilityAction(
        GameState state,
        PendingDecision pending,
        AbilityActionParameters ability,
        HashSet<CardInstanceId> cardIds,
        Dictionary<CardInstanceId, CardInstanceState> cardsById,
        IBehaviorCatalog behaviorCatalog,
        bool enforceCatalogCompatibility,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (ability.Activation == BehaviorAbilityActivation.ClickAction)
        {
            if (pending.Kind != DecisionKind.TurnAction)
            {
                Add(
                    violations,
                    "decision.ability-action-kind-mismatch",
                    $"{path}.kind",
                    "ClickAction abilities require a TurnAction decision.");
            }
        }
        else if (ability.Activation == BehaviorAbilityActivation.PaidAbility)
        {
            if (pending.Kind is not (DecisionKind.TurnTiming or DecisionKind.RunTiming))
            {
                Add(
                    violations,
                    "decision.ability-action-kind-mismatch",
                    $"{path}.kind",
                    "PaidAbility activations require a TurnTiming or RunTiming decision.");
            }
        }

        if (!StableIdentifier.TryValidate(ability.SourceCardId.Value))
        {
            Add(
                violations,
                "id.invalid",
                $"{path}.parameters.sourceCardId",
                "Ability source card ID is empty or invalid.");
        }

        if (!StableIdentifier.TryValidate(ability.DefinitionId.Value))
        {
            Add(
                violations,
                "id.invalid",
                $"{path}.parameters.definitionId",
                "Ability definition ID is empty or invalid.");
        }

        if (!StableIdentifier.TryValidate(ability.AbilityKey))
        {
            Add(
                violations,
                "id.invalid",
                $"{path}.parameters.abilityKey",
                "Ability key is empty or invalid.");
        }

        if (!StableIdentifier.TryValidate(ability.SourceZoneId.Value))
        {
            Add(
                violations,
                "id.invalid",
                $"{path}.parameters.sourceZoneId",
                "Ability source zone ID is empty or invalid.");
        }

        if (!Enum.IsDefined(ability.SourceZoneKind))
        {
            Add(
                violations,
                "decision.invalid-ability-source-zone-kind",
                $"{path}.parameters.sourceZoneKind",
                "Ability source zone kind is not a supported value.");
        }

        CardInstanceState? sourceCard = null;
        if (!cardIds.Contains(ability.SourceCardId)
            || !cardsById.TryGetValue(ability.SourceCardId, out sourceCard))
        {
            Add(
                violations,
                "decision.unknown-ability-source",
                $"{path}.parameters.sourceCardId",
                $"Ability action references unknown source card '{ability.SourceCardId}'.");
        }
        else
        {
            if (sourceCard.DefinitionId != ability.DefinitionId)
            {
                Add(
                    violations,
                    "decision.ability-source-definition-mismatch",
                    $"{path}.parameters.definitionId",
                    "Ability source card definition must match the action definition.");
            }

            PlayerState? owner = state.Players.FirstOrDefault(player => player?.Id == pending.Owner);
            if (owner is not null && owner.Side != sourceCard.OwnerSide)
            {
                Add(
                    violations,
                    "decision.ability-source-owner-mismatch",
                    $"{path}.parameters.sourceCardId",
                    "Ability source card owner must match the pending decision owner.");
            }

            if (sourceCard.ZoneId != ability.SourceZoneId)
            {
                Add(
                    violations,
                    "decision.ability-source-zone-mismatch",
                    $"{path}.parameters.sourceZoneId",
                    "Ability source card must still occupy its captured source zone.");
            }
        }

        ZoneState? sourceZone = state.Zones.FirstOrDefault(zone => zone?.Id == ability.SourceZoneId);
        if (sourceZone is null)
        {
            Add(
                violations,
                "decision.unknown-ability-source-zone",
                $"{path}.parameters.sourceZoneId",
                $"Ability action references unknown source zone '{ability.SourceZoneId}'.");
        }
        else
        {
            if (sourceZone.Kind != ability.SourceZoneKind)
            {
                Add(
                    violations,
                    "decision.ability-source-zone-kind-mismatch",
                    $"{path}.parameters.sourceZoneKind",
                    "Ability source zone kind must match the captured source zone.");
            }

            if (!sourceZone.CardIds.Contains(ability.SourceCardId))
            {
                Add(
                    violations,
                    "decision.ability-source-membership-mismatch",
                    $"{path}.parameters.sourceCardId",
                    "Ability source card must be a member of its captured source zone.");
            }
        }

        var targetIds = new HashSet<CardInstanceId>();
        if (ability.TargetCardIds is null)
        {
            Add(
                violations,
                "decision.missing-ability-targets",
                $"{path}.parameters.targetCardIds",
                "Ability actions must retain a target snapshot.");
        }
        else
        {
            for (int index = 0; index < ability.TargetCardIds.Count; index++)
            {
                CardInstanceId targetId = ability.TargetCardIds[index];
                string targetPath = $"{path}.parameters.targetCardIds[{index}]";
                if (!StableIdentifier.TryValidate(targetId.Value))
                {
                    Add(violations, "id.invalid", targetPath, "Ability target card ID is empty or invalid.");
                }
                else if (!cardIds.Contains(targetId))
                {
                    Add(
                        violations,
                        "decision.unknown-ability-target",
                        targetPath,
                        $"Ability action references unknown target card '{targetId}'.");
                }

                if (!targetIds.Add(targetId))
                {
                    Add(
                        violations,
                        "decision.duplicate-ability-target",
                        targetPath,
                        $"Ability target card '{targetId}' occurs more than once.");
                }
            }
        }

        if (!DeterministicHash.IsSha256Hex(ability.CatalogFingerprint))
        {
            Add(
                violations,
                "decision.invalid-ability-catalog-fingerprint",
                $"{path}.parameters.catalogFingerprint",
                "Ability actions must retain a lowercase SHA-256 catalog fingerprint.");
        }

        if (!DeterministicHash.IsSha256Hex(ability.DefinitionFingerprint))
        {
            Add(
                violations,
                "decision.invalid-ability-definition-fingerprint",
                $"{path}.parameters.definitionFingerprint",
                "Ability actions must retain a lowercase SHA-256 definition fingerprint.");
        }

        if (state.BehaviorCatalogFingerprint is not null
            && !string.Equals(
                ability.CatalogFingerprint,
                state.BehaviorCatalogFingerprint,
                StringComparison.Ordinal))
        {
            Add(
                violations,
                "decision.ability-catalog-state-mismatch",
                $"{path}.parameters.catalogFingerprint",
                "Ability action catalog fingerprint must match the state catalog pin.");
        }

        if (enforceCatalogCompatibility
            && !string.Equals(ability.CatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            Add(
                violations,
                "decision.ability-catalog-mismatch",
                $"{path}.parameters.catalogFingerprint",
                "Ability action catalog fingerprint must match the injected catalog.");
        }

        ValidateAbilityActionCosts(ability.Costs, $"{path}.parameters.costs", violations);
        bool hasClickCost = ability.Costs is not null
            && ability.Costs.Any(cost => cost is not null && cost.Kind == BehaviorCostKind.Clicks);
        bool hasLoseClickCost = ability.Costs is not null
            && ability.Costs.Any(cost => cost is not null && cost.Kind == BehaviorCostKind.LoseClick);
        int clickCostCount = ability.Costs?.Count(cost => cost is not null && cost.Kind == BehaviorCostKind.Clicks) ?? 0;
        int clickAmount = ability.Costs?
            .SingleOrDefault(cost => cost is not null && cost.Kind == BehaviorCostKind.Clicks)?.Amount ?? 0;
        if (ability.Activation == BehaviorAbilityActivation.ClickAction
            && (ability.Timing != BehaviorAbilityTiming.ActionPhase
                || clickCostCount != 1
                || clickAmount != 1
                || hasLoseClickCost))
        {
            Add(
                violations,
                "decision.invalid-ability-combination",
                path,
                "A ClickAction ability requires exactly one Click cost of one and action-phase timing.");
        }
        else if (ability.Activation == BehaviorAbilityActivation.PaidAbility && hasClickCost)
        {
            Add(
                violations,
                "decision.invalid-ability-combination",
                path,
                "A PaidAbility action may not carry an action Click cost.");
        }

        if (ability.Usage is null)
        {
            Add(
                violations,
                "decision.missing-ability-usage",
                $"{path}.parameters.usage",
                "Ability actions must retain an exact usage snapshot.");
        }
        else
        {
            if (!Enum.IsDefined(ability.Usage.Limit))
            {
                Add(
                    violations,
                    "decision.invalid-ability-usage-limit",
                    $"{path}.parameters.usage.limit",
                    "Ability usage limit is not a supported value.");
            }

            if (ability.Usage.UsesThisTurn < 0
                || (ability.Usage.Limit == BehaviorAbilityUsageLimit.OncePerTurn
                    && ability.Usage.UsesThisTurn > 1))
            {
                Add(
                    violations,
                    "decision.invalid-ability-usage",
                    $"{path}.parameters.usage.usesThisTurn",
                    "Ability usage count is outside its declared limit.");
            }
        }

        if (!enforceCatalogCompatibility)
        {
            return;
        }

        CardBehaviorDefinition? definition = null;
        bool definitionFound = false;
        try
        {
            definitionFound = behaviorCatalog.TryGet(ability.DefinitionId, out definition!);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            Add(
                violations,
                "decision.ability-definition-lookup-failed",
                $"{path}.parameters.definitionId",
                $"Ability definition lookup failed: {exception.Message}");
        }

        if (!definitionFound || definition is null)
        {
            Add(
                violations,
                "decision.unknown-ability-definition",
                $"{path}.parameters.definitionId",
                $"Ability definition '{ability.DefinitionId}' is not registered in the injected catalog.");
            return;
        }

        if (definition.DefinitionId != ability.DefinitionId)
        {
            Add(
                violations,
                "decision.ability-definition-mismatch",
                $"{path}.parameters.definitionId",
                "Ability definition must match the injected catalog registration.");
        }

        try
        {
            string expectedDefinitionFingerprint = behaviorCatalog.GetDefinitionFingerprint(ability.DefinitionId);
            if (!string.Equals(ability.DefinitionFingerprint, expectedDefinitionFingerprint, StringComparison.Ordinal))
            {
                Add(
                    violations,
                    "decision.ability-definition-mismatch",
                    $"{path}.parameters.definitionFingerprint",
                    "Ability definition fingerprint must match the injected catalog definition.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            Add(
                violations,
                "decision.ability-definition-fingerprint-failed",
                $"{path}.parameters.definitionFingerprint",
                $"Ability definition fingerprint lookup failed: {exception.Message}");
        }

        BehaviorPaidAbility? declaration = definition.PaidAbilities
            .SingleOrDefault(candidate => string.Equals(candidate.Key, ability.AbilityKey, StringComparison.Ordinal));
        if (declaration is null)
        {
            Add(
                violations,
                "decision.unknown-ability-key",
                $"{path}.parameters.abilityKey",
                $"Ability key '{ability.AbilityKey}' is not declared by definition '{ability.DefinitionId}'.");
            return;
        }

        if (ability.Timing != declaration.Timing)
        {
            Add(
                violations,
                "decision.ability-timing-mismatch",
                $"{path}.parameters.timing",
                "Ability timing must match the declared ability shape.");
        }

        if (ability.Activation != declaration.Activation)
        {
            Add(
                violations,
                "decision.ability-activation-mismatch",
                $"{path}.parameters.activation",
                "Ability activation must match the declared ability shape.");
        }

        if (ability.Usage is not null && ability.Usage.Limit != declaration.UsageLimit)
        {
            Add(
                violations,
                "decision.ability-usage-limit-mismatch",
                $"{path}.parameters.usage.limit",
                "Ability usage limit must match the declared ability shape.");
        }

        if (ability.Costs is null || !ability.Costs.SequenceEqual(declaration.Costs))
        {
            Add(
                violations,
                "decision.ability-costs-mismatch",
                $"{path}.parameters.costs",
                "Ability costs must match the declared ability shape.");
        }
    }

    private static void ValidateAbilityActionCosts(
        IReadOnlyList<BehaviorCost>? costs,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (costs is null)
        {
            Add(violations, "decision.missing-ability-costs", path, "Ability actions must retain a cost snapshot.");
            return;
        }

        for (int index = 0; index < costs.Count; index++)
        {
            BehaviorCost? cost = costs[index];
            string costPath = $"{path}[{index}]";
            if (cost is null)
            {
                Add(violations, "decision.null-ability-cost", costPath, "Ability costs must not contain null entries.");
                continue;
            }

            if (!Enum.IsDefined(cost.Kind))
            {
                Add(violations, "decision.invalid-ability-cost-kind", $"{costPath}.kind", "Ability cost kind is not a supported value.");
                continue;
            }

            bool invalidAmount = cost.Kind switch
            {
                BehaviorCostKind.Credits => cost.Amount < 0,
                BehaviorCostKind.Clicks
                    or BehaviorCostKind.LoseClick
                    or BehaviorCostKind.Counter
                    or BehaviorCostKind.Tags
                    or BehaviorCostKind.Additional => cost.Amount <= 0,
                BehaviorCostKind.TrashSelf => cost.Amount != 1,
                _ => false,
            };
            if (invalidAmount)
            {
                Add(violations, "decision.invalid-ability-cost-amount", $"{costPath}.amount", "Ability cost amount is outside its declared rules.");
            }

            if (cost.Kind == BehaviorCostKind.Counter
                && !StableIdentifier.TryValidate(cost.CounterName))
            {
                Add(violations, "decision.invalid-ability-counter-cost", $"{costPath}.counterName", "Counter costs must carry a stable counter name.");
            }

            if (cost.Kind == BehaviorCostKind.Additional
                && !StableIdentifier.TryValidate(cost.AdditionalKey))
            {
                Add(violations, "decision.invalid-ability-additional-cost", $"{costPath}.additionalKey", "Additional costs must carry a stable additional-cost key.");
            }

            if (cost.Kind != BehaviorCostKind.Counter && cost.CounterName is not null)
            {
                Add(violations, "decision.invalid-ability-counter-cost", $"{costPath}.counterName", "Only counter costs may carry a counter name.");
            }

            if (cost.Kind != BehaviorCostKind.Additional && cost.AdditionalKey is not null)
            {
                Add(violations, "decision.invalid-ability-additional-cost", $"{costPath}.additionalKey", "Only additional costs may carry an additional-cost key.");
            }
        }
    }

    private static void ValidateCardPlayFrame(
        GameState state,
        CardPlayFrame frame,
        int index,
        string path,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
    {
        PlayerState? owner = state.Players.FirstOrDefault(player => player?.Id == frame.Owner);
        if (owner is null)
        {
            Add(
                violations,
                "resolution.card-play-owner-missing",
                $"{path}.owner",
                $"Card-play owner '{frame.Owner}' is not one of the players in the state.");
        }
        else if (owner.Side != frame.Side)
        {
            Add(
                violations,
                "resolution.card-play-owner-mismatch",
                $"{path}.owner",
                "Card-play owner side must match the frozen card-play side.");
        }

        TurnFrame? parent = index > 0 ? state.ResolutionStack[index - 1] as TurnFrame : null;
        if (parent is null)
        {
            Add(
                violations,
                "resolution.card-play-parent-missing",
                path,
                "A CardPlayFrame requires an immediately preceding TurnFrame in AwaitingAction.");
        }
        else
        {
            if (parent.Stage != TurnFrameStage.AwaitingAction)
            {
                Add(
                    violations,
                    "resolution.card-play-parent-stage",
                    $"{path}.parent",
                    "A CardPlayFrame requires an AwaitingAction parent turn frame.");
            }

            if (parent.ActiveSide != frame.Side || state.Turn.ActiveSide != frame.Side)
            {
                Add(
                    violations,
                    "resolution.card-play-parent-side",
                    $"{path}.side",
                    "A CardPlayFrame side must match both its active parent turn and the state active side.");
            }

            if (parent.TurnNumber != state.Turn.TurnNumber)
            {
                Add(
                    violations,
                    "resolution.card-play-parent-turn",
                    $"{path}.parent.turnNumber",
                    "A CardPlayFrame parent turn number must match the current turn number.");
            }
        }

        BehaviorResolutionFrame? child = index + 1 < state.ResolutionStack.Count
            ? state.ResolutionStack[index + 1] as BehaviorResolutionFrame
            : null;
        if (child is null)
        {
            Add(
                violations,
                "resolution.card-play-child-missing",
                path,
                "A CardPlayFrame requires an immediately following BehaviorResolutionFrame child.");
        }
        else if (index + 1 != state.ResolutionStack.Count - 1
            && !(index + 2 == state.ResolutionStack.Count - 1
                && state.ResolutionStack[index + 2] is InstallFrame
                {
                    InitiationKind: InstallInitiationKind.BehaviorEffect,
                    ParentFrameIdentity: not null,
                } behaviorInstall
                && ParentIdentityMatches(child, behaviorInstall.ParentFrameIdentity)))
        {
            Add(
                violations,
                "resolution.card-play-child-not-top",
                $"{path}.child",
                "The matching CardPlayFrame behavior child must remain the top resolution frame while it resolves.");
        }

        if (!IsPlayerSide(frame.Side))
        {
            return;
        }

        ZoneKind sourceKind = frame.Side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneKind discardKind = frame.Side == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap;
        ZoneKind playAreaKind = frame.Side == PlayerSide.Corp ? ZoneKind.CorpPlayArea : ZoneKind.RunnerPlayArea;

        ZoneState[] sourceMatches = state.Zones
            .Where(zone => zone is not null && zone.Kind == sourceKind)
            .Select(zone => zone!)
            .ToArray();
        ZoneState? source = sourceMatches.Length == 1 ? sourceMatches[0] : null;
        if (sourceMatches.Length != 1
            || source!.OwnerSide != frame.Side
            || source.ServerId is not null
            || frame.SourceZoneId != source.Id
            || frame.SourceZoneKind != sourceKind)
        {
            Add(
                violations,
                "resolution.card-play-source-mismatch",
                $"{path}.sourceZoneId",
                $"Card-play source must be the unique canonical {sourceKind} zone owned by {frame.Side} and match its frozen ID/kind.");
        }

        ZoneState[] discardMatches = state.Zones
            .Where(zone => zone is not null && zone.Kind == discardKind)
            .Select(zone => zone!)
            .ToArray();
        ZoneState? discard = discardMatches.Length == 1 ? discardMatches[0] : null;
        if (discardMatches.Length != 1
            || discard!.OwnerSide != frame.Side
            || discard.ServerId is not null
            || frame.DiscardZoneId != discard.Id)
        {
            Add(
                violations,
                "resolution.card-play-discard-mismatch",
                $"{path}.discardZoneId",
                $"Card-play discard must be the unique canonical {discardKind} zone owned by {frame.Side} and match its frozen ID.");
        }

        ZoneState[] playAreaMatches = state.Zones
            .Where(zone => zone is not null && zone.Kind == playAreaKind)
            .Select(zone => zone!)
            .ToArray();
        ZoneState? playArea = playAreaMatches.Length == 1 ? playAreaMatches[0] : null;
        if (playAreaMatches.Length != 1
            || playArea!.OwnerSide != frame.Side
            || playArea.ServerId is not null
            || frame.PlayAreaZoneId != playArea.Id)
        {
            Add(
                violations,
                "resolution.card-play-area-mismatch",
                $"{path}.playAreaZoneId",
                $"Card-play play area must be the unique serverless {playAreaKind} zone owned by {frame.Side} and match its frozen ID.");
        }

        if (playArea is not null)
        {
            if (playArea.CardIds.Count != 1)
            {
                Add(
                    violations,
                    "resolution.card-play-area-count",
                    $"zones[{playArea.Id}].cardIds",
                    "A CardPlayFrame play area must contain exactly one card.");
            }
            else if (playArea.CardIds[0] != frame.CardId)
            {
                Add(
                    violations,
                    "resolution.card-play-area-card-mismatch",
                    $"zones[{playArea.Id}].cardIds",
                    "A CardPlayFrame play area must contain exactly its frozen card.");
            }
        }

        CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate?.Id == frame.CardId);
        if (card is null)
        {
            Add(
                violations,
                "resolution.card-play-card-missing",
                $"{path}.cardId",
                $"Card-play card '{frame.CardId}' does not exist.");
        }
        else
        {
            if (card.DefinitionId != frame.DefinitionId)
            {
                Add(
                    violations,
                    "resolution.card-play-definition-mismatch",
                    $"{path}.definitionId",
                    "Card-play frame definition must match its physical card.");
            }

            if (card.OwnerSide != frame.Side)
            {
                Add(
                    violations,
                    "resolution.card-play-card-owner-mismatch",
                    $"{path}.cardId",
                    "Card-play card owner must match the frozen card-play side.");
            }

            if (card.ZoneId != frame.PlayAreaZoneId)
            {
                Add(
                    violations,
                    "resolution.card-play-card-zone-mismatch",
                    $"{path}.cardId",
                    "Card-play card must occupy the frozen play-area zone while its child resolves.");
            }

            if (!card.FaceUp)
            {
                Add(
                    violations,
                    "resolution.card-play-card-facedown",
                    $"{path}.cardId",
                    "A card resolving from a CardPlayFrame must be faceup.");
            }

            if (card.Rezzed)
            {
                Add(
                    violations,
                    "resolution.card-play-card-rezzed",
                    $"{path}.cardId",
                    "A card resolving from a CardPlayFrame must be unrezzed.");
            }
        }

        CardRuntimeSpec? runtimeSpec = null;
        if (runtimeCardCatalog is null)
        {
            Add(
                violations,
                "resolution.card-play-runtime-missing",
                path,
                "CardPlayFrame validation requires an injected runtime card catalog.");
        }
        else
        {
            bool found = false;
            try
            {
                found = runtimeCardCatalog.TryGet(frame.DefinitionId, out runtimeSpec!);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                Add(
                    violations,
                    "resolution.card-play-runtime-failed",
                    $"{path}.definitionId",
                    $"Runtime metadata lookup failed: {exception.Message}");
            }

            if (!found || runtimeSpec is null)
            {
                Add(
                    violations,
                    "resolution.card-play-runtime-missing",
                    $"{path}.definitionId",
                    $"Runtime metadata for card-play definition '{frame.DefinitionId}' is unavailable.");
            }
            else
            {
                if (runtimeSpec.DefinitionId != frame.DefinitionId)
                {
                    Add(
                        violations,
                        "resolution.card-play-runtime-definition-mismatch",
                        $"{path}.definitionId",
                        "Card-play runtime definition must match the frozen frame definition.");
                }

                if (runtimeSpec.Side != frame.Side)
                {
                    Add(
                        violations,
                        "resolution.card-play-runtime-side-mismatch",
                        $"{path}.side",
                        "Card-play runtime side must match the frozen frame side.");
                }

                if (runtimeSpec.CardClass != frame.CardClass)
                {
                    Add(
                        violations,
                        "resolution.card-play-runtime-class-mismatch",
                        $"{path}.cardClass",
                        "Card-play runtime class must match the frozen frame class.");
                }

                if (runtimeSpec.PlayCost != frame.CreditCost)
                {
                    Add(
                        violations,
                        "resolution.card-play-runtime-cost-mismatch",
                        $"{path}.creditCost",
                        "Card-play credit cost must match the runtime printed PlayCost.");
                }
            }
        }

        CardBehaviorDefinition? definition = null;
        bool definitionFound = false;
        try
        {
            definitionFound = behaviorCatalog.TryGet(frame.DefinitionId, out definition!);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            Add(
                violations,
                "resolution.card-play-definition-lookup-failed",
                $"{path}.definitionId",
                $"Behavior definition lookup failed: {exception.Message}");
        }

        if (!definitionFound || definition is null)
        {
            Add(
                violations,
                "resolution.card-play-definition-missing",
                $"{path}.definitionId",
                $"Behavior definition for card-play definition '{frame.DefinitionId}' is unavailable.");
        }
        else
        {
            if (definition.DefinitionId != frame.DefinitionId)
            {
                Add(
                    violations,
                    "resolution.card-play-definition-mismatch",
                    $"{path}.definitionId",
                    "Card-play behavior definition must match the frozen frame definition.");
            }

            if (definition.Controller != frame.Side)
            {
                Add(
                    violations,
                    "resolution.card-play-definition-controller-mismatch",
                    $"{path}.side",
                    "Card-play behavior definition controller must match the frozen frame side.");
            }

            string? expectedDefinitionFingerprint = null;
            try
            {
                expectedDefinitionFingerprint = behaviorCatalog.GetDefinitionFingerprint(frame.DefinitionId);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                Add(
                    violations,
                    "resolution.card-play-definition-fingerprint-failed",
                    $"{path}.definitionFingerprint",
                    $"Behavior definition fingerprint lookup failed: {exception.Message}");
            }

            if (expectedDefinitionFingerprint is not null
                && !string.Equals(frame.DefinitionFingerprint, expectedDefinitionFingerprint, StringComparison.Ordinal))
            {
                Add(
                    violations,
                    "resolution.card-play-definition-fingerprint-mismatch",
                    $"{path}.definitionFingerprint",
                    "Card-play definition fingerprint must match the injected behavior catalog.");
            }

            int creditCost = 0;
            bool costOverflowed = false;
            foreach (BehaviorCost behaviorCost in definition.Costs)
            {
                if (behaviorCost.Kind != BehaviorCostKind.Credits)
                {
                    Add(
                        violations,
                        "resolution.card-play-cost-kind",
                        $"{path}.creditCost",
                        "Every up-front CardPlayFrame behavior cost must be a Credits cost.");
                }

                if (behaviorCost.Amount < 0)
                {
                    Add(
                        violations,
                        "resolution.card-play-cost-negative",
                        $"{path}.creditCost",
                        "Card-play behavior costs must be nonnegative.");
                    continue;
                }

                try
                {
                    creditCost = checked(creditCost + behaviorCost.Amount);
                }
                catch (OverflowException)
                {
                    costOverflowed = true;
                    Add(
                        violations,
                        "resolution.card-play-cost-overflow",
                        $"{path}.creditCost",
                        "The checked sum of CardPlayFrame behavior credit costs overflowed.");
                    break;
                }
            }

            if (!costOverflowed && creditCost != frame.CreditCost)
            {
                Add(
                    violations,
                    "resolution.card-play-cost-mismatch",
                    $"{path}.creditCost",
                    "Card-play credit cost must equal the checked sum of every up-front Credits behavior cost.");
            }
        }

        if (!string.Equals(frame.CatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            Add(
                violations,
                "resolution.card-play-catalog-fingerprint-mismatch",
                $"{path}.catalogFingerprint",
                "Card-play catalog fingerprint must match the injected behavior catalog.");
        }

        if (child is null)
        {
            return;
        }

        if (child.DefinitionId != frame.DefinitionId)
        {
            Add(
                violations,
                "resolution.card-play-child-definition-mismatch",
                $"{path}.child.definitionId",
                "Card-play behavior child definition must match the parent frame definition.");
        }

        if (child.SourceCardId != frame.CardId)
        {
            Add(
                violations,
                "resolution.card-play-child-card-mismatch",
                $"{path}.child.sourceCardId",
                "Card-play behavior child source card must match the parent frame card.");
        }

        if (child.Controller != frame.Owner)
        {
            Add(
                violations,
                "resolution.card-play-child-controller-mismatch",
                $"{path}.child.controller",
                "Card-play behavior child controller must match the parent frame owner.");
        }

        if (child.ActingPlayer != frame.Owner)
        {
            Add(
                violations,
                "resolution.card-play-child-acting-player-mismatch",
                $"{path}.child.actingPlayer",
                "Card-play behavior child acting player must match the parent frame owner.");
        }

        if (child.SourceZoneId != frame.PlayAreaZoneId
            || child.SourceZoneKind != playAreaKind)
        {
            Add(
                violations,
                "resolution.card-play-child-source-mismatch",
                $"{path}.child.sourceZoneId",
                "Card-play behavior child source location must match the side play area.");
        }

        if (!string.Equals(child.ParentBehaviorId, ResolutionBehaviorIds.CardPlay, StringComparison.Ordinal)
            || !string.Equals(child.ParentBehaviorId, frame.BehaviorId, StringComparison.Ordinal))
        {
            Add(
                violations,
                "resolution.card-play-child-parent-mismatch",
                $"{path}.child.parentBehaviorId",
                "Card-play behavior child parent behavior ID must be CardPlay.");
        }

        string? expectedParentIdentity = null;
        try
        {
            expectedParentIdentity = BehaviorFrameIdentity.Compute(frame);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException or OverflowException)
        {
            Add(
                violations,
                "resolution.card-play-parent-identity-failed",
                $"{path}.child.parentFrameIdentity",
                $"Card-play parent identity could not be serialized safely: {exception.Message}");
        }

        if (expectedParentIdentity is not null
            && !string.Equals(child.ParentFrameIdentity, expectedParentIdentity, StringComparison.Ordinal))
        {
            Add(
                violations,
                "resolution.card-play-child-parent-identity-mismatch",
                $"{path}.child.parentFrameIdentity",
                "Card-play behavior child parent frame identity must match the exact serialized CardPlayFrame.");
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
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (frame.RunId != run.Id || frame.Step != run.Step)
        {
            return;
        }

        var expected = new List<(ActionKind Kind, CardInstanceId? Target, int Quantity, int HostedCredits)>();
        IReadOnlyList<RezCardParameters> expectedRuntimeRezes = [];
        switch (run.Step)
        {
            case RunTimingStep.InitiationPaidWindow:
            case RunTimingStep.ApproachPaidWindow:
            case RunTimingStep.EncounterPaidWindow:
            case RunTimingStep.MovementPaidWindow:
            case RunTimingStep.AfterMovePaidWindow:
                expected.Add((ActionKind.PassPriority, null, 0, 0));
                if (runtimeCardCatalog is not null)
                {
                    try
                    {
                        expectedRuntimeRezes = GameEngine.MaterializeRezCardParameters(state, runtimeCardCatalog);
                    }
                    catch (Exception exception) when (exception is ResolutionFailureException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or ArgumentException
                        or OverflowException)
                    {
                        Add(
                            violations,
                            "run.rez-materialization-failed",
                            $"{path}.legalActions",
                            $"Runtime rez actions could not be materialized from the authoritative state: {exception.Message}");
                    }
                }
                else if (run.Step == RunTimingStep.ApproachPaidWindow
                    && run.Priority is { PriorityHolder: PlayerSide.Corp, CorpMayRezApproachedIce: true }
                    && run.CurrentIceId.HasValue)
                {
                    CardInstanceState approachedIce = state.GetCard(run.CurrentIceId.Value);
                    if (behaviorCatalog.TryGetIce(approachedIce.DefinitionId, out IceBehaviorSpec? approachedSpec))
                    {
                        PlayerState? corp = state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Corp);
                        if (corp is not null && corp.Credits >= approachedSpec.RezCost)
                        {
                            expected.Add((ActionKind.RezIce, run.CurrentIceId.Value, 0, 0));
                        }
                    }
                }

                if (run.Step == RunTimingStep.EncounterPaidWindow
                    && run.Priority is { PriorityHolder: PlayerSide.Runner }
                    && run.Encounter is { Status: RunEncounterStatus.Initialized })
                {
                    ServerState? attackedServer = state.Servers.FirstOrDefault(server => server?.Id == run.AttackedServer);

                    // Mirrors the engine's encounter offer: every registered rig
                    // breaker materializes break quantities (named-subtype match,
                    // sufficient current strength, per-activation BreakLimit,
                    // affordability) and a pump (affordability only), in
                    // stored zone/card order; there is no persistent server
                    // attachment.
                    if (attackedServer is not null
                        && behaviorCatalog.TryGetIce(
                            state.GetCard(run.Encounter.IceId).DefinitionId,
                            out IceBehaviorSpec? expectedIceSpec))
                    {
                        int iceStrength = EncounterStrength.Ice(expectedIceSpec, attackedServer.Kind);
                        int unbrokenCount = run.Encounter.Subroutines.Count(subroutine => !subroutine.Broken);
                        // Run-scoped hosted credits (Overclock) are spendable
                        // during the run, so they count toward the offer. Each
                        // legal wallet/hosted division is a distinct action.
                        PlayerState? runnerPlayer = state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Runner);

                        foreach (ZoneState? rigZone in state.Zones)
                        {
                            if (rigZone is null || rigZone.Kind != ZoneKind.RunnerRig || rigZone.OwnerSide != PlayerSide.Runner)
                            {
                                continue;
                            }

                            foreach (CardInstanceId cardId in rigZone.CardIds)
                            {
                                CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate?.Id == cardId);
                                if (card is null
                                    || !behaviorCatalog.TryGetIcebreaker(
                                        card.DefinitionId,
                                        out IcebreakerBehaviorSpec? expectedBreakerSpec))
                                {
                                    continue;
                                }

                                if (runnerPlayer is not null
                                    && expectedIceSpec.IceType == expectedBreakerSpec.InterfaceType
                                    && EncounterStrength.Breaker(expectedBreakerSpec, run.LingeringEffects, cardId) >= iceStrength
                                    && (long)runnerPlayer.Credits + run.RunCredits >= expectedBreakerSpec.BreakCost)
                                {
                                    for (int quantity = 1;
                                        quantity <= Math.Min(expectedBreakerSpec.BreakLimit, unbrokenCount);
                                        quantity++)
                                    {
                                        foreach (int hostedCredits in LegalHostedCreditSplits(
                                            runnerPlayer,
                                            run,
                                            expectedBreakerSpec.BreakCost))
                                        {
                                            expected.Add((
                                                ActionKind.BreakSubroutines,
                                                cardId,
                                                quantity,
                                                hostedCredits));
                                        }
                                    }
                                }

                                if (runnerPlayer is not null
                                    && (long)runnerPlayer.Credits + run.RunCredits >= expectedBreakerSpec.PumpCost)
                                {
                                    foreach (int hostedCredits in LegalHostedCreditSplits(
                                        runnerPlayer,
                                        run,
                                        expectedBreakerSpec.PumpCost))
                                    {
                                        expected.Add((
                                            ActionKind.PumpBreaker,
                                            cardId,
                                            0,
                                            hostedCredits));
                                    }
                                }
                            }
                        }
                    }
                }

                break;
            case RunTimingStep.JackOutDecision:
                expected.Add((ActionKind.JackOut, null, 0, 0));
                expected.Add((ActionKind.ContinueRun, null, 0, 0));
                break;
            case RunTimingStep.BreachCandidateSelection:
                foreach (CardInstanceId candidate in run.BreachCandidates)
                {
                    expected.Add((ActionKind.ChooseBreachCandidate, candidate, 0, 0));
                }

                ServerState? breachServer = state.Servers.FirstOrDefault(server => server?.Id == run.AttackedServer);
                ZoneState? hiddenZone = breachServer?.Kind switch
                {
                    ServerKind.Headquarters => state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.CorpHand && zone.ServerId is null),
                    ServerKind.RAndD => state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.CorpDeck && zone.ServerId is null),
                    _ => null,
                };
                if (breachServer is not null
                    && RunState.HiddenBreachCandidate(breachServer.Kind, hiddenZone, run.RandomAccessLimit) is not null)
                {
                    expected.Add((ActionKind.ChooseBreachCandidate, null, 0, 0));
                }

                break;
            case RunTimingStep.BreachAccessAbility:
                if (run.ChosenAccessCards.Count > 0)
                {
                    CardInstanceId accessedCardId = run.ChosenAccessCards[^1];
                    CardInstanceState accessedCard = state.GetCard(accessedCardId);

                    // CR 7.2.2/7.1.5: an accessed agenda has no mid-access
                    // ability in the M2 card set (agendas have no trash cost),
                    // so the window offers the pass only; CR 7.2.3 then steals
                    // the agenda automatically on progression. Mirror the
                    // engine's basic-trash offer for every other accessed card.
                    bool accessedAgenda = behaviorCatalog.TryGetAgenda(accessedCard.DefinitionId, out _);
                    if (!accessedAgenda)
                    {
                        // CR 7.1.5/7.1.5b: mirrors the engine's basic-trash offer —
                        // the accessed card has a registered asset spec with a
                        // trash cost the Runner can pay, and is not already in
                        // the Corp discard.
                        ZoneState? discardZone = state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.CorpDiscard && zone.ServerId is null);
                        if (discardZone is not null
                            && accessedCard.ZoneId != discardZone.Id
                            && behaviorCatalog.TryGetAsset(accessedCard.DefinitionId, out AssetBehaviorSpec? assetSpec))
                        {
                            PlayerState? trashRunner = state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Runner);
                            if (trashRunner is not null && (long)trashRunner.Credits + run.RunCredits >= assetSpec.TrashCost)
                            {
                                foreach (int hostedCredits in LegalHostedCreditSplits(
                                    trashRunner,
                                    run,
                                    assetSpec.TrashCost))
                                {
                                    expected.Add((
                                        ActionKind.TrashAccessedCard,
                                        accessedCardId,
                                        0,
                                        hostedCredits));
                                }
                            }
                        }
                    }
                }

                expected.Add((ActionKind.NoAccessAction, null, 0, 0));
                break;
            default:
                Add(violations, "run.unexpected-prompt", $"{path}.legalActions", $"Run step '{run.Step}' must not have a pending decision.");
                return;
        }

        var actual = new List<(ActionKind Kind, CardInstanceId? Target, int Quantity, int HostedCredits)>();
        var actualRuntimeRezes = new List<RezCardParameters>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            if (action.Kind == ActionKind.UseAbility)
            {
                continue;
            }

            switch (action.Kind, action.Parameters)
            {
                case (ActionKind.PassPriority, NoActionParameters):
                case (ActionKind.JackOut, NoActionParameters):
                case (ActionKind.ContinueRun, NoActionParameters):
                    actual.Add((action.Kind, null, 0, 0));
                    break;
                case (ActionKind.RezIce, RunIceParameters rez):
                    actual.Add((action.Kind, rez.IceId, 0, 0));
                    break;
                case (ActionKind.RezCard, RezCardParameters rez):
                    actualRuntimeRezes.Add(rez);
                    break;
                case (ActionKind.BreakSubroutines, RunBreakParameters breakSubroutinesAction):
                    actual.Add((
                        action.Kind,
                        breakSubroutinesAction.BreakerId,
                        breakSubroutinesAction.Quantity,
                        breakSubroutinesAction.HostedCredits));
                    break;
                case (ActionKind.PumpBreaker, RunBreakerParameters pumpBreaker):
                    actual.Add((action.Kind, pumpBreaker.BreakerId, 0, pumpBreaker.HostedCredits));
                    break;
                case (ActionKind.ChooseBreachCandidate, BreachCandidateParameters breachCandidate):
                    actual.Add((action.Kind, breachCandidate.CardId, 0, 0));
                    break;
                case (ActionKind.NoAccessAction, NoActionParameters):
                    actual.Add((action.Kind, null, 0, 0));
                    break;
                case (ActionKind.TrashAccessedCard, RunCardPaymentParameters trash):
                    actual.Add((action.Kind, trash.CardId, 0, trash.HostedCredits));
                    break;
            }
        }

        var expectedCounts = expected.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        var actualCounts = actual.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        foreach (((ActionKind Kind, CardInstanceId? Target, int Quantity, int HostedCredits) spec, int count) in expectedCounts)
        {
            actualCounts.TryGetValue(spec, out int actualCount);
            if (actualCount != count)
            {
                Add(
                    violations,
                    actualCount == 0 ? "run.missing-action" : "run.action-count-mismatch",
                    $"{path}.legalActions",
                    $"Run timing decision requires exactly {count} '{spec.Kind}' action(s) for target '{spec.Target?.ToString() ?? "none"}', "
                    + $"quantity {spec.Quantity}, hosted credits {spec.HostedCredits}, but contains {actualCount}.");
            }
        }

        foreach (((ActionKind Kind, CardInstanceId? Target, int Quantity, int HostedCredits) spec, int count) in actualCounts)
        {
            if (!expectedCounts.TryGetValue(spec, out int expectedCount))
            {
                Add(
                    violations,
                    "run.extra-action",
                    $"{path}.legalActions",
                    $"Run timing decision contains unoffered '{spec.Kind}' action for target '{spec.Target?.ToString() ?? "none"}', "
                    + $"quantity {spec.Quantity}, hosted credits {spec.HostedCredits}.");
            }
            else if (count > expectedCount)
            {
                Add(
                    violations,
                    "run.duplicate-action",
                    $"{path}.legalActions",
                    $"Run timing decision contains {count} copies of '{spec.Kind}' for target '{spec.Target?.ToString() ?? "none"}', "
                    + $"quantity {spec.Quantity}, hosted credits {spec.HostedCredits}, expected {expectedCount}.");
            }
        }

        ValidateRezActionBijection(
            expectedRuntimeRezes,
            actualRuntimeRezes,
            path,
            violations,
            missingCode: "run.missing-rez-action",
            extraCode: "run.extra-rez-action",
            duplicateCode: "run.duplicate-rez-action",
            costMismatchCode: "run.rez-cost-mismatch",
            windowName: "Run timing");

        if (pending.Owner != RunDecisionOwner(state, run))
        {
            Add(violations, "run.decision-owner-mismatch", $"{path}.owner", "Run timing decision owner does not match the current priority/Runner decision owner.");
        }

        static IEnumerable<int> LegalHostedCreditSplits(
            PlayerState runner,
            RunState run,
            int amount)
        {
            int minimumHosted = Math.Max(0, amount - runner.Credits);
            int maximumHosted = Math.Min(amount, run.RunCredits);
            for (int hosted = minimumHosted; hosted <= maximumHosted; hosted++)
            {
                yield return hosted;
            }
        }
    }

    private static void ValidateRezActionBijection(
        IReadOnlyList<RezCardParameters> expected,
        List<RezCardParameters> actual,
        string path,
        List<StateInvariantViolation> violations,
        string missingCode,
        string extraCode,
        string duplicateCode,
        string costMismatchCode,
        string windowName)
    {
        if (actual.Count != expected.Count)
        {
            Add(
                violations,
                actual.Count < expected.Count ? missingCode : extraCode,
                $"{path}.legalActions",
                $"{windowName} decision contains {actual.Count} runtime rez action(s), expected {expected.Count}.");
        }

        for (int index = 0; index < actual.Count; index++)
        {
            RezCardParameters candidate = actual[index];
            if (actual.Take(index).Any(previous => RezParametersMatch(previous, candidate)))
            {
                Add(
                    violations,
                    duplicateCode,
                    $"{path}.legalActions",
                    $"Runtime rez action for '{candidate.CardId}' at cost {candidate.CreditCost} is duplicated.");
            }

            RezCardParameters? expectedTarget = expected.FirstOrDefault(item => item.CardId == candidate.CardId);
            if (expectedTarget is not null && expectedTarget.CreditCost != candidate.CreditCost)
            {
                Add(
                    violations,
                    costMismatchCode,
                    $"{path}.legalActions",
                    $"Runtime rez action for '{candidate.CardId}' carries frozen cost {candidate.CreditCost}, expected {expectedTarget.CreditCost}.");
            }

            if (!expected.Any(item => RezParametersMatch(item, candidate)))
            {
                Add(
                    violations,
                    extraCode,
                    $"{path}.legalActions",
                    $"{windowName} decision contains an unmaterialized runtime rez action for '{candidate.CardId}'.");
            }
        }

        foreach (RezCardParameters candidate in expected)
        {
            if (!actual.Any(item => RezParametersMatch(item, candidate)))
            {
                Add(
                    violations,
                    missingCode,
                    $"{path}.legalActions",
                    $"{windowName} decision is missing the materialized runtime rez action for '{candidate.CardId}'.");
            }
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

    private static bool IsRunnerInstallableRuntimeClass(RuntimeCardClass cardClass) =>
        cardClass is RuntimeCardClass.Program
            or RuntimeCardClass.Hardware
            or RuntimeCardClass.Resource;

    private static bool IsCorpInstallableRuntimeClass(RuntimeCardClass cardClass) =>
        cardClass is RuntimeCardClass.Agenda
            or RuntimeCardClass.Asset
            or RuntimeCardClass.Upgrade
            or RuntimeCardClass.Ice;

    private static void ValidateBehaviorEffectInstallFrame(
        GameState state,
        InstallFrame install,
        int index,
        string path,
        PlayerState? installOwner,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
    {
        if (installOwner is null || installOwner.Side != PlayerSide.Runner)
        {
            Add(
                violations,
                "resolution.install-owner-side",
                $"{path}.owner",
                "Behavior-effect install frames must be owned by the Runner.");
        }

        BehaviorResolutionFrame? behaviorParent = index > 0
            ? state.ResolutionStack[index - 1] as BehaviorResolutionFrame
            : null;
        if (behaviorParent is null)
        {
            Add(
                violations,
                "resolution.install-parent-missing",
                path,
                "A behavior-effect install frame requires an immediately preceding BehaviorResolutionFrame.");
            return;
        }

        if (index < 2
            || state.ResolutionStack[index - 2] is not CardPlayFrame
            || index < 3
            || state.ResolutionStack[index - 3] is not TurnFrame)
        {
            Add(
                violations,
                "resolution.install-parent-topology",
                path,
                "A behavior-effect install frame requires the [TurnFrame, CardPlayFrame, BehaviorResolutionFrame, InstallFrame] topology.");
        }

        if (!ParentIdentityMatches(behaviorParent, install.ParentFrameIdentity))
        {
            Add(
                violations,
                "resolution.install-parent-identity-mismatch",
                $"{path}.parentFrameIdentity",
                "Behavior-effect install parent identity must match the exact serialized behavior frame.");
        }

        if (installOwner is not null && behaviorParent.Controller != installOwner.Id)
        {
            Add(
                violations,
                "resolution.install-owner-mismatch",
                $"{path}.owner",
                "Behavior-effect install owner must match the behavior controller.");
        }

        if (runtimeCardCatalog is null)
        {
            Add(
                violations,
                "resolution.install-runtime-missing",
                $"{path}.cardId",
                "Behavior-effect installs require an injected runtime card catalog.");
        }

        if (!behaviorCatalog.TryGet(behaviorParent.DefinitionId, out CardBehaviorDefinition? definition))
        {
            Add(
                violations,
                "resolution.unknown-behavior-definition",
                $"{path}.parent.definitionId",
                $"Behavior definition '{behaviorParent.DefinitionId}' is unavailable for install validation.");
            return;
        }

        List<BehaviorInstruction> replay = definition.Instructions.ToList();
        BehaviorTarget? originTarget = null;
        IReadOnlyList<CardInstanceId>? originSelection = null;
        foreach (BehaviorContinuationStep step in behaviorParent.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
            {
                continue;
            }

            BehaviorInstruction sourceInstruction = replay[step.InstructionCursor];
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection && step.CardIds.Count > 0)
            {
                BehaviorTarget? target = sourceInstruction.Kind == BehaviorInstructionKind.Search
                    ? sourceInstruction.Target
                    : sourceInstruction.Kind == BehaviorInstructionKind.Choose
                        ? sourceInstruction.Choice?.Target
                        : null;
                BehaviorBinding? chosen = behaviorParent.Bindings.SingleOrDefault(
                    binding => string.Equals(binding.Key, "chosen", StringComparison.Ordinal));
                if (chosen is not null
                    && chosen.CardIds.SequenceEqual(step.CardIds))
                {
                    originTarget = target;
                    originSelection = step.CardIds;
                }
            }

            if (!BehaviorContinuation.TryApply(replay, step, out _))
            {
                break;
            }
        }

        BehaviorInstruction? currentInstruction = behaviorParent.InstructionCursor >= 0
            && behaviorParent.InstructionCursor < replay.Count
            ? replay[behaviorParent.InstructionCursor]
            : null;
        if (currentInstruction is not
            {
                Kind: BehaviorInstructionKind.InstallCard,
                Target: { Kind: BehaviorTargetKind.ChosenCard } chosenTarget,
                SourceZone: BehaviorZone.Stack,
                DestinationZone: BehaviorZone.Rig,
            })
        {
            Add(
                violations,
                "resolution.install-instruction-mismatch",
                path,
                "Behavior-effect install cursor must identify a chosen Runner Stack card installed to the Runner Rig.");
            return;
        }

        if (chosenTarget.Selector is not null
            && !string.Equals(chosenTarget.Selector, "chosen", StringComparison.Ordinal))
        {
            Add(
                violations,
                "resolution.install-binding-mismatch",
                $"{path}.cardId",
                "Behavior-effect installs must consume the current chosen binding.");
        }

        BehaviorBinding? chosenBinding = behaviorParent.Bindings.SingleOrDefault(
            binding => string.Equals(binding.Key, "chosen", StringComparison.Ordinal));
        if (chosenBinding is null || chosenBinding.CardIds.Count != 1)
        {
            Add(
                violations,
                "resolution.install-binding-missing",
                $"{path}.cardId",
                "Behavior-effect installs require exactly one current chosen binding.");
        }

        CardInstanceState? installCard = state.Cards.FirstOrDefault(card => card?.Id == install.CardId);
        ZoneState? runnerStack = state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.RunnerStack && zone.ServerId is null);
        ZoneState? runnerRig = state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.RunnerRig && zone.ServerId is null);
        if (runnerStack is null
            || install.SourceZoneKind != ZoneKind.RunnerStack
            || install.SourceZoneId != runnerStack.Id
            || installCard is null
            || installCard.ZoneId != runnerStack.Id
            || !runnerStack.CardIds.Contains(install.CardId)
            || installCard.OwnerSide != PlayerSide.Runner)
        {
            Add(
                violations,
                "resolution.install-source-mismatch",
                $"{path}.sourceZoneId",
                "Behavior-effect install cards must remain Runner-owned in the canonical Runner Stack.");
        }

        if (install.CardClass != RuntimeCardClass.Program
            || install.DestinationKind != InstallDestinationKind.RunnerRig
            || install.ServerId is not null
            || install.CreateNewRemote
            || runnerRig is null
            || install.DestinationZoneId != runnerRig.Id)
        {
            Add(
                violations,
                "resolution.install-destination-mismatch",
                path,
                "Behavior-effect installs must target the canonical Runner Rig as a Program.");
        }

        if (chosenBinding is not null
            && (chosenBinding.CardIds.Count != 1 || chosenBinding.CardIds[0] != install.CardId))
        {
            Add(
                violations,
                "resolution.install-binding-mismatch",
                $"{path}.cardId",
                "Behavior-effect install card must equal the current chosen binding.");
        }

        CardRuntimeSpec? installSpecForProjection = null;
        if (installCard is not null && runtimeCardCatalog is not null)
        {
            if (!runtimeCardCatalog.TryGet(installCard.DefinitionId, out CardRuntimeSpec? spec))
            {
                Add(
                    violations,
                    "resolution.install-runtime-card-missing",
                    $"{path}.cardId",
                    $"Runtime metadata for behavior install card '{installCard.DefinitionId}' is unavailable.");
            }
            else
            {
                installSpecForProjection = spec;
                if (spec.Side != PlayerSide.Runner || spec.CardClass != RuntimeCardClass.Program)
                {
                    Add(
                        violations,
                        "resolution.install-runtime-class-invalid",
                        $"{path}.cardClass",
                        "Behavior-effect installs require Runner Program runtime metadata.");
                }

                if (install.CardClass != spec.CardClass
                    || install.InstallCost != (spec.InstallCost ?? 0))
                {
                    Add(
                        violations,
                        "resolution.install-runtime-mismatch",
                        path,
                        "Behavior-effect install class and cost must match authoritative runtime metadata.");
                }
            }
        }

        if (runnerRig is not null)
        {
            if (runtimeCardCatalog is not null
                && !AllRuntimeCardsKnown(state, runtimeCardCatalog, runnerRig))
            {
                Add(
                    violations,
                    "resolution.install-runtime-candidate-missing",
                    $"{path}.candidateReplacementCardIds",
                    "Behavior-effect installs cannot classify every physical Runner Rig card from authoritative runtime metadata.");
            }

            CardInstanceId[] conflicts = installSpecForProjection is
            { Side: PlayerSide.Runner, CardClass: RuntimeCardClass.Program }
                && runtimeCardCatalog is not null
                ? ActiveRunnerInstallConflictIds(state, runtimeCardCatalog, installSpecForProjection)
                : [];
            CardInstanceId[] expectedCandidates = installSpecForProjection is
            { Side: PlayerSide.Runner, CardClass: RuntimeCardClass.Program }
                && runtimeCardCatalog is not null
                ? RunnerInstallCandidateIds(state, runtimeCardCatalog, runnerRig, installSpecForProjection)
                : [];
            if (!install.CandidateReplacementCardIds.SequenceEqual(expectedCandidates))
            {
                Add(
                    violations,
                    "resolution.install-candidates-mismatch",
                    $"{path}.candidateReplacementCardIds",
                    "Behavior-effect install candidates must equal the current installed Runner Programs plus active uniqueness/Console conflicts.");
            }

            if (!install.RequiredReplacementCardIds.SequenceEqual(conflicts))
            {
                Add(
                    violations,
                    "resolution.install-required-mismatch",
                    $"{path}.requiredReplacementCardIds",
                    "Behavior-effect install required replacements must equal the active uniqueness/Console conflicts.");
            }

            CardDefinitionId[] expectedCandidateDefinitions = expectedCandidates
                .Select(cardId => state.Cards.Single(card => card.Id == cardId).DefinitionId)
                .ToArray();
            if (install.CandidateReplacementDefinitionIds is null)
            {
                Add(
                    violations,
                    "resolution.install-candidate-definitions-missing",
                    $"{path}.candidateReplacementDefinitionIds",
                    "Behavior-effect install frames must retain the frozen replacement definition snapshot.");
            }
            else if (!install.CandidateReplacementDefinitionIds.SequenceEqual(expectedCandidateDefinitions))
            {
                Add(
                    violations,
                    "resolution.install-candidate-definitions-mismatch",
                    $"{path}.candidateReplacementDefinitionIds",
                    "Behavior-effect install replacement definitions must match the current installed Runner Programs plus active uniqueness/Console conflicts.");
            }
        }

        if (originTarget is null
            || originTarget.Kind is not (BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards)
            || originTarget.Zone != BehaviorZone.Stack
            || originSelection is null
            || !originSelection.SequenceEqual([install.CardId]))
        {
            Add(
                violations,
                "resolution.install-origin-missing",
                $"{path}.cardId",
                "Behavior-effect install binding must originate in a reachable Stack search or choice continuation.");
        }
        else if (runtimeCardCatalog is not null
            && !BehaviorCardMatches(state, behaviorCatalog, runtimeCardCatalog, install.CardId, originTarget.Filter))
        {
            Add(
                violations,
                "resolution.install-origin-stale",
                $"{path}.cardId",
                "Behavior-effect install card no longer matches its originating Stack selector.");
        }

    }

    /// <summary>
    /// Validates the Corp branch of the serialized install cursor. Runner
    /// frames retain the original B1a checks below; Corp frames bind to HQ,
    /// an existing ServerRoot or ServerIce, and the exact physical zone that
    /// supplied the frozen bounded candidate list.
    /// </summary>
    private static void ValidateCorpInstallFrame(
        GameState state,
        InstallFrame install,
        int index,
        string path,
        PlayerState installOwner,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
    {
        if (index != state.ResolutionStack.Count - 1)
        {
            Add(violations, "resolution.install-not-top", path, "An install selection frame must remain the top frame while its decision is pending.");
        }

        TurnFrame? parentTurn = index > 0
            ? state.ResolutionStack[index - 1] as TurnFrame
            : null;
        if (parentTurn is null || parentTurn.Stage != TurnFrameStage.AwaitingAction)
        {
            Add(violations, "resolution.install-parent-missing", path, "A Corp install frame requires a parent Corp turn action frame.");
        }
        else
        {
            if (parentTurn.ActiveSide != PlayerSide.Corp)
            {
                Add(violations, "resolution.install-parent-side", $"{path}.parent", "Corp install frames require a Corp AwaitingAction parent turn frame.");
            }

            if (parentTurn.ActiveSide != installOwner.Side)
            {
                Add(violations, "resolution.install-owner-mismatch", $"{path}.owner", "Install owner must be the active side of its parent turn frame.");
            }
        }

        ZoneState? source = state.Zones.FirstOrDefault(zone => zone?.Id == install.SourceZoneId);
        ZoneState? corpHand = state.Zones.FirstOrDefault(
            zone => zone?.Kind == ZoneKind.CorpHand
                && zone.ServerId is null);
        bool sourceMatches = install.SourceZoneKind == ZoneKind.CorpHand
            && source is not null
            && source.Kind == ZoneKind.CorpHand
            && source.OwnerSide == PlayerSide.Corp
            && source.ServerId is null
            && corpHand is not null
            && source.Id == corpHand.Id;
        if (!sourceMatches)
        {
            Add(
                violations,
                "resolution.install-source-mismatch",
                $"{path}.sourceZoneId",
                "Corp install frames must capture the canonical Corp HQ as their source zone.");
        }

        bool existingRootSelector = install.DestinationKind == InstallDestinationKind.ServerRoot
            && install.ServerId.HasValue
            && !install.CreateNewRemote;
        bool existingIceSelector = install.DestinationKind == InstallDestinationKind.ServerIce
            && install.ServerId.HasValue
            && !install.CreateNewRemote;
        if (!existingRootSelector && !existingIceSelector)
        {
            Add(
                violations,
                "resolution.install-destination-kind",
                $"{path}.destinationKind",
                "Corp install frames must target an existing ServerRoot or ServerIce zone.");
        }

        if (install.CreateNewRemote || !install.ServerId.HasValue)
        {
            Add(
                violations,
                "resolution.install-destination-remote",
                $"{path}.destinationKind",
                "Corp install frames must not create a new remote or omit their existing server identity.");
        }

        ServerState? server = install.ServerId is { } serverId
            ? state.Servers.FirstOrDefault(candidate => candidate?.Id == serverId)
            : null;
        bool iceFrame = existingIceSelector;
        ZoneState? destination = server is null
            ? null
            : state.Zones.FirstOrDefault(zone => zone?.Id == (iceFrame ? server.IceZoneId : server.RootZoneId));
        bool rootMatches = server is not null
            && destination is not null
            && destination.Kind == (iceFrame ? ZoneKind.ServerIce : ZoneKind.ServerRoot)
            && destination.OwnerSide == PlayerSide.Corp
            && destination.ServerId == server.Id;
        if (server is null || destination is null)
        {
            Add(
                violations,
                "resolution.install-destination-missing",
                $"{path}.destinationZoneId",
                "Corp install frames require the current physical server destination.");
        }

        if (!rootMatches)
        {
            Add(
                violations,
                "resolution.install-destination-mismatch",
                $"{path}.destinationZoneId",
                "Corp install frames must retain the exact physical server zone bound to their server identity.");
        }
        else if (install.DestinationZoneId != destination!.Id)
        {
            Add(
                violations,
                "resolution.install-destination-mismatch",
                $"{path}.destinationZoneId",
                "Corp install frames must retain the exact physical server zone ID.");
        }

        CardInstanceState? installCard = state.Cards.FirstOrDefault(card => card?.Id == install.CardId);
        CardRuntimeSpec? installSpec = null;
        bool hasInstallSpec = false;
        if (installCard is null)
        {
            Add(violations, "resolution.unknown-install-card", $"{path}.cardId", $"Install card '{install.CardId}' does not exist.");
        }
        else
        {
            if (installCard.DefinitionId != install.DefinitionId)
            {
                Add(violations, "resolution.install-definition-mismatch", path, "Install frame definition must match its physical card.");
            }

            if (installCard.ZoneId != install.SourceZoneId)
            {
                Add(violations, "resolution.stale-install-card", $"{path}.sourceZoneId", "Install card must remain in its frozen Corp HQ source zone.");
            }

            if (installCard.OwnerSide != PlayerSide.Corp)
            {
                Add(
                    violations,
                    "resolution.install-card-owner-side",
                    $"{path}.cardId",
                    "Corp install frames may only install a Corp-owned card from Corp HQ.");
            }

            if (runtimeCardCatalog is not null)
            {
                if (!runtimeCardCatalog.TryGet(installCard.DefinitionId, out installSpec))
                {
                    Add(
                        violations,
                        "resolution.install-runtime-card-missing",
                        $"{path}.cardId",
                        $"Runtime metadata for install card '{installCard.DefinitionId}' is unavailable.");
                }
                else
                {
                    hasInstallSpec = true;
                    if (installSpec.Side != PlayerSide.Corp
                        || !IsCorpInstallableRuntimeClass(installSpec.CardClass))
                    {
                        Add(
                            violations,
                            "resolution.install-runtime-class-invalid",
                            $"{path}.cardClass",
                            "Corp install frames require a Corp Agenda, Asset, Upgrade, or Ice runtime class.");
                    }

                    if (install.CardClass != installSpec.CardClass)
                    {
                        Add(
                            violations,
                            "resolution.install-runtime-class-mismatch",
                            $"{path}.cardClass",
                            $"Install frame class '{install.CardClass}' must match runtime class '{installSpec.CardClass}' for '{installCard.DefinitionId}'.");
                    }

                    bool destinationClassMatches = installSpec.CardClass == RuntimeCardClass.Ice
                        ? iceFrame
                        : !iceFrame && installSpec.CardClass is (RuntimeCardClass.Agenda or RuntimeCardClass.Asset or RuntimeCardClass.Upgrade);
                    if (!destinationClassMatches)
                    {
                        Add(
                            violations,
                            "resolution.install-destination-kind",
                            $"{path}.destinationKind",
                            "Corp install destination kind must correspond to the incoming runtime card class.");
                    }

                    if (!iceFrame)
                    {
                        int expectedCost = installSpec.InstallCost ?? 0;
                        if (install.InstallCost != expectedCost)
                        {
                            Add(
                                violations,
                                "resolution.install-cost-mismatch",
                                $"{path}.installCost",
                                $"Corp install frame cost {install.InstallCost} must match the authoritative printed cost {expectedCost}.");
                        }
                    }

                    if (server is not null
                        && !iceFrame
                        && installSpec.CardClass is (RuntimeCardClass.Agenda or RuntimeCardClass.Asset)
                        && server.Kind != ServerKind.Remote)
                    {
                        Add(
                            violations,
                            "resolution.install-destination-kind",
                            $"{path}.destinationKind",
                            "Corp Agendas and Assets may target only a Remote ServerRoot.");
                    }
                }
            }
        }

        if (destination is not null)
        {
            if (destination.CardIds.Count == 0)
            {
                Add(
                    violations,
                    "resolution.install-empty-root",
                    $"{path}.destinationZoneId",
                    "A Corp install selection frame is only valid for an occupied physical server zone.");
            }

            CardInstanceId[] expectedCandidates = iceFrame
                ? destination.CardIds.ToArray()
                : destination.CardIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
            if (!install.CandidateReplacementCardIds.SequenceEqual(expectedCandidates))
            {
                Add(
                    violations,
                    "resolution.install-candidates-mismatch",
                    $"{path}.candidateReplacementCardIds",
                    "Corp install candidates must equal the stable ordered IDs of the frozen physical destination zone.");
            }

            if (expectedCandidates.Length > 0)
            {
                if (install.CandidateReplacementDefinitionIds is null)
                {
                    Add(
                        violations,
                        "resolution.install-candidate-definitions-missing",
                        $"{path}.candidateReplacementDefinitionIds",
                        "Corp install frames must freeze the runtime definition of every physical candidate.");
                }
                else
                {
                    CardDefinitionId[] expectedDefinitions = expectedCandidates
                        .Select(id => state.Cards.FirstOrDefault(card => card?.Id == id)?.DefinitionId)
                        .Where(id => id.HasValue)
                        .Select(id => id!.Value)
                        .ToArray();
                    if (!install.CandidateReplacementDefinitionIds.SequenceEqual(expectedDefinitions))
                    {
                        Add(
                            violations,
                            "resolution.install-candidate-definitions-mismatch",
                            $"{path}.candidateReplacementDefinitionIds",
                            "Corp install frames must retain the runtime definitions captured for the physical destination zone.");
                    }
                }
            }

            var candidateSpecs = new Dictionary<CardInstanceId, CardRuntimeSpec>();
            foreach (CardInstanceId candidateId in expectedCandidates)
            {
                CardInstanceState? candidate = state.Cards.FirstOrDefault(card => card?.Id == candidateId);
                if (candidate is null)
                {
                    Add(violations, "resolution.unknown-install-candidate", $"{path}.candidateReplacementCardIds", $"Install candidate '{candidateId}' does not exist.");
                    continue;
                }

                if (candidate.ZoneId != destination.Id)
                {
                    Add(
                        violations,
                        "resolution.stale-install-candidate",
                        $"{path}.candidateReplacementCardIds",
                        $"Install candidate '{candidateId}' is no longer in the frozen physical destination zone.");
                }

                if (runtimeCardCatalog is null)
                {
                    continue;
                }

                if (!runtimeCardCatalog.TryGet(candidate.DefinitionId, out CardRuntimeSpec? candidateSpec))
                {
                    Add(
                        violations,
                        "resolution.install-runtime-candidate-missing",
                        $"{path}.candidateReplacementCardIds",
                        $"Runtime metadata for destination candidate '{candidate.DefinitionId}' is unavailable.");
                }
                else
                {
                    candidateSpecs[candidateId] = candidateSpec;
                    if (iceFrame
                        && (candidateSpec.Side != PlayerSide.Corp
                            || candidateSpec.CardClass != RuntimeCardClass.Ice))
                    {
                        Add(
                            violations,
                            "resolution.install-runtime-candidate-class-invalid",
                            $"{path}.candidateReplacementCardIds",
                            $"ServerIce candidate '{candidate.DefinitionId}' must have Corp Ice runtime metadata.");
                    }
                }
            }

            if (hasInstallSpec && candidateSpecs.Count == expectedCandidates.Length)
            {
                CardInstanceId[] expectedRequired = iceFrame
                    ? []
                    : installSpec!.CardClass switch
                    {
                        RuntimeCardClass.Agenda or RuntimeCardClass.Asset => expectedCandidates
                            .Where(id => candidateSpecs[id].CardClass is RuntimeCardClass.Agenda or RuntimeCardClass.Asset)
                            .ToArray(),
                        RuntimeCardClass.Upgrade when IsRegion(installSpec) => expectedCandidates
                            .Where(id => candidateSpecs[id].CardClass == RuntimeCardClass.Upgrade
                                && IsRegion(candidateSpecs[id]))
                            .ToArray(),
                        _ => [],
                    };
                if (!install.RequiredReplacementCardIds.SequenceEqual(expectedRequired))
                {
                    Add(
                        violations,
                        "resolution.install-required-mismatch",
                        $"{path}.requiredReplacementCardIds",
                        "Corp required replacements must equal the authoritative coexistence or Region set.");
                }
            }

            if (hasInstallSpec && iceFrame)
            {
                int selectedProtectors = install.SelectedReplacementCardIds
                    .Count(id => destination.CardIds.Contains(id));
                int expectedCost = Math.Max(0, destination.CardIds.Count - selectedProtectors);
                if (install.InstallCost != expectedCost)
                {
                    Add(
                        violations,
                        "resolution.install-cost-mismatch",
                        $"{path}.installCost",
                        $"Corp ICE install frame cost {install.InstallCost} must equal the remaining protector count {expectedCost}.");
                }
            }
        }

        if (install.SelectedReplacementCardIds
            .Distinct()
            .Count() != install.SelectedReplacementCardIds.Count)
        {
            Add(
                violations,
                "resolution.duplicate-install-selection",
                $"{path}.selectedReplacementCardIds",
                "Corp install selections must be unique.");
        }

        if (install.SelectedReplacementCardIds.Any(id => !install.CandidateReplacementCardIds.Contains(id)))
        {
            Add(
                violations,
                "resolution.install-selected-not-offered",
                $"{path}.selectedReplacementCardIds",
                "Corp install selections must be frozen candidates.");
        }

        foreach (CardInstanceId selected in install.SelectedReplacementCardIds)
        {
            CardInstanceState? selectedCard = state.Cards.FirstOrDefault(card => card?.Id == selected);
            if (selectedCard is null)
            {
                continue;
            }

            if (destination is null || selectedCard.ZoneId != destination.Id)
            {
                Add(
                    violations,
                    "resolution.stale-install-selection",
                    $"{path}.selectedReplacementCardIds",
                    $"Selected replacement '{selected}' is no longer in the frozen physical destination zone.");
            }
        }

        // Keep the old B1a diagnostics for an unmistakably forged Corp frame
        // carrying Runner-owned/runtime data.  This preserves the useful
        // migration error for snapshots produced before side-specific frame
        // contracts existed while leaving valid Corp frames unaffected.
        CardRuntimeSpec? legacySpec = installCard is null || runtimeCardCatalog is null
            ? null
            : runtimeCardCatalog.TryGet(installCard.DefinitionId, out CardRuntimeSpec? resolved) ? resolved : null;
        bool legacyRunnerPayload = installCard?.OwnerSide == PlayerSide.Runner
            || legacySpec?.Side == PlayerSide.Runner
            || install.SourceZoneKind == ZoneKind.RunnerGrip
            || install.DestinationKind == InstallDestinationKind.RunnerRig;
        if (legacyRunnerPayload)
        {
            Add(
                violations,
                "resolution.install-owner-side",
                $"{path}.owner",
                "B1a install frames are Runner-only; the frame owner must be the Runner.");
            if (parentTurn is not null && parentTurn.ActiveSide == PlayerSide.Corp)
            {
                Add(
                    violations,
                    "resolution.install-parent-side",
                    $"{path}.parent",
                    "B1a install frames require a Runner AwaitingAction parent turn frame.");
            }

            Add(
                violations,
                "resolution.install-destination-kind",
                $"{path}.destinationKind",
                "B1a install frames must target the Runner Rig.");
            Add(
                violations,
                "resolution.install-destination-mismatch",
                $"{path}.destinationZoneId",
                "B1a install frames must retain the current canonical Runner Rig zone ID.");
        }
    }

    private static bool IsRegion(CardRuntimeSpec spec) =>
        spec.Subtypes.Any(subtype => string.Equals(subtype, "region", StringComparison.OrdinalIgnoreCase));

    private static bool ParentIdentityMatches(
        ResolutionFrame parent,
        string? capturedIdentity)
    {
        if (!StableIdentifier.TryValidate(capturedIdentity))
        {
            return false;
        }

        try
        {
            return string.Equals(
                capturedIdentity,
                BehaviorFrameIdentity.Compute(parent),
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static void ValidateInstallAction(
        GameState state,
        IRuntimeCardCatalog? runtimeCardCatalog,
        PendingDecision pending,
        ResolutionFrame? topFrame,
        InstallCardParameters action,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (pending.Kind is not (DecisionKind.TurnAction or DecisionKind.InstallChoice))
        {
            Add(violations, "decision.action-kind-mismatch", $"{path}.kind", "Install actions require a real turn action or install-choice decision.");
        }

        if (runtimeCardCatalog is null)
        {
            Add(violations, "decision.install-catalog-missing", $"{path}.parameters", "Install actions require an injected runtime card catalog.");
        }
        else
        {
            CardInstanceState? runtimeCard = state.Cards.FirstOrDefault(candidate => candidate?.Id == action.CardId);
            if (runtimeCard is not null && !runtimeCardCatalog.TryGet(runtimeCard.DefinitionId, out _))
            {
                // The authoritative reducer reports the card-specific diagnostic;
                // this structural check only rejects a clearly unknown runtime
                // definition without attempting to infer class from card text.
                Add(violations, "decision.install-runtime-card-missing", $"{path}.parameters.cardId", $"Runtime metadata for '{runtimeCard.DefinitionId}' is unavailable.");
            }
        }

        if (action.CandidateReplacementCardIds
            .Distinct()
            .Count() != action.CandidateReplacementCardIds.Count)
        {
            Add(violations, "decision.install-duplicate-candidate", $"{path}.parameters.candidateReplacementCardIds", "Install replacement candidates must be unique.");
        }

        if (action.SelectedReplacementCardIds
            .Distinct()
            .Count() != action.SelectedReplacementCardIds.Count)
        {
            Add(violations, "decision.install-duplicate-selection", $"{path}.parameters.selectedReplacementCardIds", "Install replacement selections must be unique.");
        }

        if (action.SelectedReplacementCardIds.Any(id => !action.CandidateReplacementCardIds.Contains(id)))
        {
            Add(violations, "decision.install-selected-not-offered", $"{path}.parameters.selectedReplacementCardIds", "Install selections must be frozen candidates.");
        }

        if (topFrame is InstallFrame frame)
        {
            if (action.CardId != frame.CardId
                || action.DestinationKind != frame.DestinationKind
                || action.ServerId != frame.ServerId
                || action.CreateNewRemote != frame.CreateNewRemote
                || !action.CandidateReplacementCardIds.SequenceEqual(frame.CandidateReplacementCardIds)
                || !action.RequiredReplacementCardIds.SequenceEqual(frame.RequiredReplacementCardIds))
            {
                Add(violations, "decision.install-frame-mismatch", $"{path}.parameters", "Install action does not match the frozen install frame.");
            }

            if (action.FinalizeSelection)
            {
                if (!action.SelectedReplacementCardIds.SequenceEqual(frame.SelectedReplacementCardIds))
                {
                    Add(violations, "decision.install-selection-mismatch", $"{path}.parameters.selectedReplacementCardIds", "A final install action must submit the frame's exact selected set.");
                }
            }
            else
            {
                IReadOnlyList<CardInstanceId> selected = frame.SelectedReplacementCardIds;
                if (action.SelectedReplacementCardIds.Count != selected.Count + 1
                    || !action.SelectedReplacementCardIds.Take(selected.Count).SequenceEqual(selected)
                    || action.SelectedReplacementCardIds.Skip(selected.Count).Any(id => selected.Contains(id)))
                {
                    Add(violations, "decision.install-incremental-selection", $"{path}.parameters.selectedReplacementCardIds", "An incremental install action must append one new frozen candidate.");
                }
            }

            int expectedActionCost = frame.CardClass == RuntimeCardClass.Ice
                && frame.DestinationKind == InstallDestinationKind.ServerIce
                && state.Zones.FirstOrDefault(zone => zone?.Id == frame.DestinationZoneId) is { } iceZone
                ? Math.Max(
                    0,
                    iceZone.CardIds.Count
                        - (action.FinalizeSelection
                            ? frame.SelectedReplacementCardIds
                            : action.SelectedReplacementCardIds)
                            .Count(id => iceZone.CardIds.Contains(id)))
                : frame.InstallCost;
            if (action.CreditCost != expectedActionCost)
            {
                Add(
                    violations,
                    "decision.install-cost-mismatch",
                    $"{path}.parameters.creditCost",
                    "Install action cost must match the authoritative dynamic cost for the current selection.");
            }
        }
        else if (pending.Kind != DecisionKind.TurnAction)
        {
            Add(violations, "decision.install-frame-missing", $"{path}.parameters", "Install-choice actions require a matching install frame.");
        }
    }

    private static void ValidateInstallChoiceActions(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        PendingDecision pending,
        InstallFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (runtimeCardCatalog is null)
        {
            return;
        }

        IReadOnlyList<InstallCardParameters> expected;
        try
        {
            expected = GameEngine.MaterializeInstallChoiceParameters(
                state,
                frame,
                behaviorCatalog,
                runtimeCardCatalog);
        }
        catch (Exception exception) when (exception is ResolutionFailureException or InvalidOperationException or KeyNotFoundException)
        {
            Add(
                violations,
                "decision.install-materialization-failed",
                $"{path}.legalActions",
                $"Install-choice continuations could not be materialized from the authoritative frame and state: {exception.Message}");
            return;
        }

        var actual = pending.LegalActions
            .Select((action, index) => (Action: action, Index: index))
            .Where(static value => value.Action is not null
                && value.Action.Kind == ActionKind.InstallCard
                && value.Action.Parameters is InstallCardParameters)
            .Select(static value => (
                Parameters: (InstallCardParameters)value.Action!.Parameters,
                Index: value.Index))
            .ToArray();

        if (actual.Length != expected.Count)
        {
            Add(
                violations,
                actual.Length < expected.Count
                    ? "decision.missing-install-continuation"
                    : "decision.extra-install-continuation",
                $"{path}.legalActions",
                $"Install-choice decision contains {actual.Length} typed continuation(s), expected exactly {expected.Count}.");
        }

        for (int index = 0; index < actual.Length; index++)
        {
            (InstallCardParameters parameters, int actionIndex) = actual[index];
            if (actual.Take(index).Any(previous => InstallParametersMatch(previous.Parameters, parameters)))
            {
                Add(
                    violations,
                    "decision.duplicate-install-continuation",
                    $"{path}.legalActions[{actionIndex}].parameters",
                    "Install-choice continuations must be semantically unique regardless of ActionId.");
            }

            if (!expected.Any(expectedParameters => InstallParametersMatch(parameters, expectedParameters)))
            {
                Add(
                    violations,
                    "decision.extra-install-continuation",
                    $"{path}.legalActions[{actionIndex}].parameters",
                    "Install-choice decision contains a continuation that the authoritative frame does not materialize.");
            }
        }

        foreach (InstallCardParameters expectedParameters in expected)
        {
            if (!actual.Any(value => InstallParametersMatch(value.Parameters, expectedParameters)))
            {
                Add(
                    violations,
                    "decision.missing-install-continuation",
                    $"{path}.legalActions",
                    $"Install-choice decision is missing the materialized continuation for selected set [{string.Join(", ", expectedParameters.SelectedReplacementCardIds)}] ({(expectedParameters.FinalizeSelection ? "finalize" : "incremental")}).");
            }
        }
    }

    private static void ValidateTurnActions(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        PendingDecision pending,
        TurnFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        PlayerState? player = state.Players.FirstOrDefault(candidate => candidate?.Side == frame.ActiveSide);
        if (player is null)
        {
            return;
        }

        var expected = new HashSet<ActionKind>();
        if (player.Clicks > 0)
        {
            expected.Add(ActionKind.GainCredit);
            ZoneKind deckKind = frame.ActiveSide == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack;
            ZoneState? deck = state.Zones.FirstOrDefault(zone => zone?.Kind == deckKind && zone.ServerId is null);
            if (deck is not null && deck.CardIds.Count > 0)
            {
                expected.Add(ActionKind.DrawCard);
            }
        }

        var actual = new HashSet<ActionKind>();
        var actualInstallParameters = new List<InstallCardParameters>();
        var actualPlayParameters = new List<PlayCardParameters>();
        var actualRunParameters = new List<RunServerParameters>();
        var actualAdvanceParameters = new List<AdvanceCardParameters>();
        var actualRemoveTagParameters = new List<RemoveTagParameters>();
        var actualTrashTaggedResourceParameters = new List<TrashTaggedResourceParameters>();
        var actualPurgeVirusCountersParameters = new List<PurgeVirusCountersParameters>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            if (action.Kind == ActionKind.UseAbility)
            {
                continue;
            }

            if (action.Kind == ActionKind.InstallCard
                && action.Parameters is InstallCardParameters install)
            {
                actualInstallParameters.Add(install);
                continue;
            }

            if (action.Kind == ActionKind.PlayCard
                && action.Parameters is PlayCardParameters play)
            {
                actualPlayParameters.Add(play);
                continue;
            }

            if (action.Kind == ActionKind.StartRun
                && action.Parameters is RunServerParameters run)
            {
                actualRunParameters.Add(run);
                continue;
            }

            if (action.Kind == ActionKind.AdvanceCard
                && action.Parameters is AdvanceCardParameters advance)
            {
                actualAdvanceParameters.Add(advance);
                continue;
            }

            if (action.Kind == ActionKind.RemoveTag
                && action.Parameters is RemoveTagParameters removeTag)
            {
                actualRemoveTagParameters.Add(removeTag);
                continue;
            }

            if (action.Kind == ActionKind.TrashTaggedResource
                && action.Parameters is TrashTaggedResourceParameters trashTaggedResource)
            {
                actualTrashTaggedResourceParameters.Add(trashTaggedResource);
                continue;
            }

            if (action.Kind == ActionKind.PurgeVirusCounters
                && action.Parameters is PurgeVirusCountersParameters purgeVirusCounters)
            {
                actualPurgeVirusCountersParameters.Add(purgeVirusCounters);
                continue;
            }

            if (action.Parameters is not NoActionParameters
                || action.Kind is not (ActionKind.GainCredit or ActionKind.DrawCard))
            {
                Add(violations, "decision.turn-action-parameters", $"{path}.legalActions", "Turn action decisions contain only typed gain-credit, draw-card, supported install, play, or basic-run actions.");
                continue;
            }

            if (!actual.Add(action.Kind))
            {
                Add(violations, "decision.duplicate-turn-action", $"{path}.legalActions", $"Turn action '{action.Kind}' is duplicated.");
            }
        }

        foreach (ActionKind required in expected)
        {
            if (!actual.Contains(required))
            {
                Add(violations, "decision.missing-turn-action", $"{path}.legalActions", $"Turn action decision is missing '{required}'.");
            }
        }

        foreach (ActionKind offered in actual)
        {
            if (!expected.Contains(offered))
            {
                Add(violations, "decision.extra-turn-action", $"{path}.legalActions", $"Turn action decision contains unoffered '{offered}'.");
            }
        }

        IReadOnlyList<InstallCardParameters> expectedInstalls = [];
        IReadOnlyList<PlayCardParameters> expectedPlays = [];
        IReadOnlyList<RunServerParameters> expectedRuns = GameEngine.MaterializeRunServerParameters(state);
        IReadOnlyList<AdvanceCardParameters> expectedAdvances = [];
        IReadOnlyList<RemoveTagParameters> expectedRemoveTags = [];
        IReadOnlyList<TrashTaggedResourceParameters> expectedTrashTaggedResources = [];
        IReadOnlyList<PurgeVirusCountersParameters> expectedPurgeVirusCounters = [];
        if (runtimeCardCatalog is not null)
        {
            try
            {
                expectedInstalls = frame.ActiveSide == PlayerSide.Runner
                    ? GameEngine.MaterializeRunnerInstallParameters(
                        state,
                        behaviorCatalog,
                        runtimeCardCatalog)
                    : GameEngine.MaterializeCorpInstallParameters(
                        state,
                        behaviorCatalog,
                        runtimeCardCatalog);
            }
            catch (Exception exception) when (exception is ResolutionFailureException or InvalidOperationException or KeyNotFoundException)
            {
                Add(
                    violations,
                    "decision.install-materialization-failed",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} install actions could not be materialized from the authoritative state: {exception.Message}");
            }

            try
            {
                expectedPlays = GameEngine.MaterializePlayCardParameters(
                    state,
                    behaviorCatalog,
                    runtimeCardCatalog);
            }
            catch (Exception exception) when (exception is ResolutionFailureException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
            {
                Add(
                    violations,
                    "decision.play-materialization-failed",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} play actions could not be materialized from the authoritative state: {exception.Message}");
            }

            try
            {
                expectedAdvances = GameEngine.MaterializeAdvanceCardParameters(
                    state,
                    runtimeCardCatalog);
            }
            catch (Exception exception) when (exception is ResolutionFailureException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
            {
                Add(
                    violations,
                    "decision.advance-materialization-failed",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} advance actions could not be materialized from the authoritative state: {exception.Message}");
            }
        }

        try
        {
            expectedRemoveTags = GameEngine.MaterializeRemoveTagParameters(state);
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException
            or OverflowException)
        {
            Add(
                violations,
                "decision.remove-tag-materialization-failed",
                $"{path}.legalActions",
                "Runner remove-tag actions could not be materialized from the authoritative state: " + exception.Message);
        }

        if (runtimeCardCatalog is not null)
        {
            try
            {
                expectedTrashTaggedResources = GameEngine.MaterializeTrashTaggedResourceParameters(
                    state,
                    runtimeCardCatalog);
            }
            catch (Exception exception) when (exception is ResolutionFailureException
                or InvalidOperationException
                or KeyNotFoundException
                or ArgumentException
                or OverflowException)
            {
                Add(
                    violations,
                    "decision.trash-tagged-resource-materialization-failed",
                    $"{path}.legalActions",
                    "Tagged-resource trash actions could not be materialized from the authoritative state: " + exception.Message);
            }
        }

        try
        {
            expectedPurgeVirusCounters = GameEngine.MaterializePurgeVirusCountersParameters(state);
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException
            or OverflowException)
        {
            Add(
                violations,
                "decision.purge-virus-counters-materialization-failed",
                $"{path}.legalActions",
                "Virus-counter purge actions could not be materialized from the authoritative state: " + exception.Message);
        }

        if (actualInstallParameters.Count != expectedInstalls.Count)
        {
            Add(
                violations,
                actualInstallParameters.Count < expectedInstalls.Count
                    ? "decision.missing-install-action"
                    : "decision.extra-install-action",
                $"{path}.legalActions",
                $"{frame.ActiveSide} turn action decision contains {actualInstallParameters.Count} install action(s), expected {expectedInstalls.Count}.");
        }

        foreach (InstallCardParameters expectedInstall in expectedInstalls)
        {
            if (!actualInstallParameters.Any(actualInstall => InstallParametersMatch(actualInstall, expectedInstall)))
            {
                Add(
                    violations,
                    "decision.missing-install-action",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} turn action decision is missing the materialized install action for '{expectedInstall.CardId}'.");
            }
        }

        foreach (InstallCardParameters actualInstall in actualInstallParameters)
        {
            if (!expectedInstalls.Any(expectedInstall => InstallParametersMatch(actualInstall, expectedInstall)))
            {
                Add(
                    violations,
                    "decision.extra-install-action",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} turn action decision contains an unmaterialized install action for '{actualInstall.CardId}'.");
            }
        }

        if (actualPlayParameters.Count != expectedPlays.Count)
        {
            Add(
                violations,
                actualPlayParameters.Count < expectedPlays.Count
                    ? "decision.missing-play-action"
                    : "decision.extra-play-action",
                $"{path}.legalActions",
                $"{frame.ActiveSide} turn action decision contains {actualPlayParameters.Count} play action(s), expected {expectedPlays.Count}.");
        }

        for (int index = 0; index < actualPlayParameters.Count; index++)
        {
            PlayCardParameters actualPlay = actualPlayParameters[index];
            if (actualPlayParameters
                .Take(index)
                .Any(previous => PlayParametersMatch(previous, actualPlay)))
            {
                Add(
                    violations,
                    "decision.duplicate-play-action",
                    $"{path}.legalActions",
                    $"Play action for '{actualPlay.CardId}' at cost {actualPlay.CreditCost} is duplicated.");
            }

            if (!expectedPlays.Any(expectedPlay => PlayParametersMatch(actualPlay, expectedPlay)))
            {
                Add(
                    violations,
                    "decision.extra-play-action",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} turn action decision contains an unmaterialized play action for '{actualPlay.CardId}'.");
            }
        }

        foreach (PlayCardParameters expectedPlay in expectedPlays)
        {
            if (!actualPlayParameters.Any(actualPlay => PlayParametersMatch(actualPlay, expectedPlay)))
            {
                Add(
                    violations,
                    "decision.missing-play-action",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} turn action decision is missing the materialized play action for '{expectedPlay.CardId}'.");
            }
        }

        if (actualRunParameters.Count != expectedRuns.Count)
        {
            Add(
                violations,
                actualRunParameters.Count < expectedRuns.Count
                    ? "decision.missing-run-action"
                    : "decision.extra-run-action",
                $"{path}.legalActions",
                $"{frame.ActiveSide} turn action decision contains {actualRunParameters.Count} basic-run action(s), expected {expectedRuns.Count}.");
        }

        for (int index = 0; index < actualRunParameters.Count; index++)
        {
            RunServerParameters actualRun = actualRunParameters[index];
            if (actualRunParameters
                .Take(index)
                .Any(previous => previous.ServerId == actualRun.ServerId))
            {
                Add(
                    violations,
                    "decision.duplicate-run-action",
                    $"{path}.legalActions",
                    $"Basic-run action for server '{actualRun.ServerId}' is duplicated.");
            }

            if (!expectedRuns.Any(expectedRun => expectedRun.ServerId == actualRun.ServerId))
            {
                Add(
                    violations,
                    "decision.extra-run-action",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} turn action decision contains an unmaterialized basic-run action for '{actualRun.ServerId}'.");
            }
        }

        foreach (RunServerParameters expectedRun in expectedRuns)
        {
            if (!actualRunParameters.Any(actualRun => actualRun.ServerId == expectedRun.ServerId))
            {
                Add(
                    violations,
                    "decision.missing-run-action",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} turn action decision is missing the materialized basic-run action for '{expectedRun.ServerId}'.");
            }
        }

        if (actualAdvanceParameters.Count != expectedAdvances.Count)
        {
            Add(
                violations,
                actualAdvanceParameters.Count < expectedAdvances.Count
                    ? "decision.missing-advance-action"
                    : "decision.extra-advance-action",
                $"{path}.legalActions",
                $"{frame.ActiveSide} turn action decision contains {actualAdvanceParameters.Count} advance action(s), expected {expectedAdvances.Count}.");
        }

        for (int index = 0; index < actualAdvanceParameters.Count; index++)
        {
            AdvanceCardParameters actualAdvance = actualAdvanceParameters[index];
            if (actualAdvanceParameters
                .Take(index)
                .Any(previous => AdvanceParametersMatch(previous, actualAdvance)))
            {
                Add(
                    violations,
                    "decision.duplicate-advance-action",
                    $"{path}.legalActions",
                    $"Advance action for '{actualAdvance.CardId}' at cost {actualAdvance.CreditCost} is duplicated.");
            }

            if (!expectedAdvances.Any(expectedAdvance => AdvanceParametersMatch(actualAdvance, expectedAdvance)))
            {
                Add(
                    violations,
                    "decision.extra-advance-action",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} turn action decision contains an unmaterialized advance action for '{actualAdvance.CardId}'.");
            }
        }

        foreach (AdvanceCardParameters expectedAdvance in expectedAdvances)
        {
            if (!actualAdvanceParameters.Any(actualAdvance => AdvanceParametersMatch(actualAdvance, expectedAdvance)))
            {
                Add(
                    violations,
                    "decision.missing-advance-action",
                    $"{path}.legalActions",
                    $"{frame.ActiveSide} turn action decision is missing the materialized advance action for '{expectedAdvance.CardId}'.");
            }
        }

        ValidateRemoveTagActionBijection(
            expectedRemoveTags,
            actualRemoveTagParameters,
            path,
            violations);

        ValidateTrashTaggedResourceActionBijection(
            expectedTrashTaggedResources,
            actualTrashTaggedResourceParameters,
            path,
            violations);

        ValidatePurgeVirusCountersActionBijection(
            expectedPurgeVirusCounters,
            actualPurgeVirusCountersParameters,
            path,
            violations);
    }

    private static void ValidateRemoveTagActionBijection(
        IReadOnlyList<RemoveTagParameters> expected,
        List<RemoveTagParameters> actual,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (actual.Count != expected.Count)
        {
            Add(
                violations,
                actual.Count < expected.Count
                    ? "decision.missing-remove-tag-action"
                    : "decision.extra-remove-tag-action",
                $"{path}.legalActions",
                $"Turn action decision contains {actual.Count} remove-tag action(s), expected {expected.Count}.");
        }

        for (int index = 0; index < actual.Count; index++)
        {
            RemoveTagParameters candidate = actual[index];
            if (actual.Take(index).Any(previous => RemoveTagParametersMatch(previous, candidate)))
            {
                Add(
                    violations,
                    "decision.duplicate-remove-tag-action",
                    $"{path}.legalActions",
                    "Remove-tag actions must be semantically unique.");
            }

            if (!expected.Any(issued => RemoveTagParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.extra-remove-tag-action",
                    $"{path}.legalActions",
                    "Turn action decision contains an unmaterialized remove-tag action.");
            }
        }

        foreach (RemoveTagParameters issued in expected)
        {
            if (!actual.Any(candidate => RemoveTagParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.missing-remove-tag-action",
                    $"{path}.legalActions",
                    "Turn action decision is missing the materialized remove-tag action.");
            }
        }
    }

    private static void ValidateTrashTaggedResourceActionBijection(
        IReadOnlyList<TrashTaggedResourceParameters> expected,
        List<TrashTaggedResourceParameters> actual,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (actual.Count != expected.Count)
        {
            Add(
                violations,
                actual.Count < expected.Count
                    ? "decision.missing-trash-tagged-resource-action"
                    : "decision.extra-trash-tagged-resource-action",
                $"{path}.legalActions",
                $"Turn action decision contains {actual.Count} tagged-resource trash action(s), expected {expected.Count}.");
        }

        for (int index = 0; index < actual.Count; index++)
        {
            TrashTaggedResourceParameters candidate = actual[index];
            if (actual.Take(index).Any(previous => TrashTaggedResourceParametersMatch(previous, candidate)))
            {
                Add(
                    violations,
                    "decision.duplicate-trash-tagged-resource-action",
                    $"{path}.legalActions",
                    $"Tagged-resource trash action for '{candidate.CardId}' is duplicated.");
            }

            if (!expected.Any(issued => TrashTaggedResourceParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.extra-trash-tagged-resource-action",
                    $"{path}.legalActions",
                    $"Turn action decision contains an unmaterialized tagged-resource trash action for '{candidate.CardId}'.");
            }
        }

        foreach (TrashTaggedResourceParameters issued in expected)
        {
            if (!actual.Any(candidate => TrashTaggedResourceParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.missing-trash-tagged-resource-action",
                    $"{path}.legalActions",
                    $"Turn action decision is missing the materialized tagged-resource trash action for '{issued.CardId}'.");
            }
        }
    }

    private static void ValidatePurgeVirusCountersActionBijection(
        IReadOnlyList<PurgeVirusCountersParameters> expected,
        List<PurgeVirusCountersParameters> actual,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (actual.Count != expected.Count)
        {
            Add(
                violations,
                actual.Count < expected.Count
                    ? "decision.missing-purge-virus-counters-action"
                    : "decision.extra-purge-virus-counters-action",
                $"{path}.legalActions",
                $"Turn action decision contains {actual.Count} virus-counter purge action(s), expected {expected.Count}.");
        }

        for (int index = 0; index < actual.Count; index++)
        {
            PurgeVirusCountersParameters candidate = actual[index];
            if (actual.Take(index).Any(previous => PurgeVirusCountersParametersMatch(previous, candidate)))
            {
                Add(
                    violations,
                    "decision.duplicate-purge-virus-counters-action",
                    $"{path}.legalActions",
                    "Virus-counter purge actions must be semantically unique.");
            }

            if (!expected.Any(issued => PurgeVirusCountersParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.extra-purge-virus-counters-action",
                    $"{path}.legalActions",
                    "Turn action decision contains an unmaterialized virus-counter purge action.");
            }
        }

        foreach (PurgeVirusCountersParameters issued in expected)
        {
            if (!actual.Any(candidate => PurgeVirusCountersParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.missing-purge-virus-counters-action",
                    $"{path}.legalActions",
                    "Turn action decision is missing the materialized virus-counter purge action.");
            }
        }
    }

    private static void ValidateTurnTimingActions(
        GameState state,
        IRuntimeCardCatalog? runtimeCardCatalog,
        PendingDecision pending,
        TurnFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        int passCount = pending.LegalActions.Count(action =>
            action is not null
            && action.Kind == ActionKind.PassPriority
            && action.Parameters is NoActionParameters);
        if (passCount != 1)
        {
            Add(
                violations,
                passCount == 0 ? "decision.missing-pass-action" : "decision.duplicate-pass-action",
                $"{path}.legalActions",
                $"A turn timing decision must contain exactly one typed PassPriority action; found {passCount}.");
        }

        var actualScores = new List<ScoreAgendaParameters>();
        var actualRezes = new List<RezCardParameters>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            if (action.Kind == ActionKind.ScoreAgenda
                && action.Parameters is ScoreAgendaParameters score)
            {
                actualScores.Add(score);
            }
            else if (action.Kind == ActionKind.RezCard
                && action.Parameters is RezCardParameters rez)
            {
                actualRezes.Add(rez);
            }
        }

        IReadOnlyList<RezCardParameters> expectedRezes = [];
        if (runtimeCardCatalog is not null)
        {
            try
            {
                expectedRezes = GameEngine.MaterializeRezCardParameters(state, runtimeCardCatalog);
            }
            catch (Exception exception) when (exception is ResolutionFailureException
                or InvalidOperationException
                or KeyNotFoundException
                or ArgumentException
                or OverflowException)
            {
                Add(
                    violations,
                    "decision.rez-materialization-failed",
                    $"{path}.legalActions",
                    $"Runtime rez actions could not be materialized from the authoritative state: {exception.Message}");
            }
        }

        ValidateRezActionBijection(
            expectedRezes,
            actualRezes,
            path,
            violations,
            missingCode: "decision.missing-rez-action",
            extraCode: "decision.extra-rez-action",
            duplicateCode: "decision.duplicate-rez-action",
            costMismatchCode: "decision.rez-cost-mismatch",
            windowName: "Turn timing");

        IReadOnlyList<ScoreAgendaParameters> expectedScores = [];
        if (runtimeCardCatalog is not null)
        {
            try
            {
                expectedScores = GameEngine.MaterializeScoreAgendaParameters(state, runtimeCardCatalog);
            }
            catch (Exception exception) when (exception is ResolutionFailureException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
            {
                Add(
                    violations,
                    "decision.score-materialization-failed",
                    $"{path}.legalActions",
                    $"Corp score actions could not be materialized from the authoritative state: {exception.Message}");
            }
        }

        if (actualScores.Count != expectedScores.Count)
        {
            Add(
                violations,
                actualScores.Count < expectedScores.Count
                    ? "decision.missing-score-action"
                    : "decision.extra-score-action",
                $"{path}.legalActions",
                $"Turn timing decision contains {actualScores.Count} score action(s), expected {expectedScores.Count}.");
        }

        for (int index = 0; index < actualScores.Count; index++)
        {
            ScoreAgendaParameters actual = actualScores[index];
            if (actualScores
                .Take(index)
                .Any(previous => ScoreParametersMatch(previous, actual)))
            {
                Add(
                    violations,
                    "decision.duplicate-score-action",
                    $"{path}.legalActions",
                    $"Score action for '{actual.CardId}' is duplicated.");
            }

            if (!expectedScores.Any(expected => ScoreParametersMatch(actual, expected)))
            {
                Add(
                    violations,
                    "decision.extra-score-action",
                    $"{path}.legalActions",
                    $"Turn timing decision contains an unmaterialized score action for '{actual.CardId}'.");
            }
        }

        foreach (ScoreAgendaParameters expected in expectedScores)
        {
            if (!actualScores.Any(actual => ScoreParametersMatch(actual, expected)))
            {
                Add(
                    violations,
                    "decision.missing-score-action",
                    $"{path}.legalActions",
                    $"Turn timing decision is missing the materialized score action for '{expected.CardId}'.");
            }
        }
    }

    private static bool PlayParametersMatch(
        PlayCardParameters actual,
        PlayCardParameters expected) =>
        actual.CardId == expected.CardId
        && actual.CreditCost == expected.CreditCost;

    private static bool AdvanceParametersMatch(
        AdvanceCardParameters actual,
        AdvanceCardParameters expected) =>
        actual.CardId == expected.CardId
        && actual.CreditCost == expected.CreditCost;

    private static bool RemoveTagParametersMatch(
        RemoveTagParameters actual,
        RemoveTagParameters expected) =>
        actual.ClickCost == expected.ClickCost
        && actual.CreditCost == expected.CreditCost;

    private static bool TrashTaggedResourceParametersMatch(
        TrashTaggedResourceParameters actual,
        TrashTaggedResourceParameters expected) =>
        actual.CardId == expected.CardId
        && actual.ClickCost == expected.ClickCost
        && actual.CreditCost == expected.CreditCost;

    private static bool PurgeVirusCountersParametersMatch(
        PurgeVirusCountersParameters actual,
        PurgeVirusCountersParameters expected) =>
        actual.ClickCost == expected.ClickCost;

    private static bool ScoreParametersMatch(
        ScoreAgendaParameters actual,
        ScoreAgendaParameters expected) =>
        actual.CardId == expected.CardId
        && actual.AgendaPoints == expected.AgendaPoints
        && actual.AdvancementRequirement == expected.AdvancementRequirement;

    private static bool RezParametersMatch(
        RezCardParameters actual,
        RezCardParameters expected) =>
        actual.CardId == expected.CardId
        && actual.CreditCost == expected.CreditCost;

    private static bool InstallParametersMatch(
        InstallCardParameters actual,
        InstallCardParameters expected) =>
        actual.CardId == expected.CardId
        && actual.DestinationKind == expected.DestinationKind
        && actual.ServerId == expected.ServerId
        && actual.CreateNewRemote == expected.CreateNewRemote
        && actual.CreditCost == expected.CreditCost
        && actual.FinalizeSelection == expected.FinalizeSelection
        && actual.CandidateReplacementCardIds.SequenceEqual(expected.CandidateReplacementCardIds)
        && actual.RequiredReplacementCardIds.SequenceEqual(expected.RequiredReplacementCardIds)
        && actual.SelectedReplacementCardIds.SequenceEqual(expected.SelectedReplacementCardIds);

    private static void ValidateTurnDiscardActions(
        PendingDecision pending,
        TurnDiscardFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        var expected = frame.Candidates
            .Where(cardId => !frame.Selected.Contains(cardId))
            .ToArray();
        var actual = new HashSet<CardInstanceId>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            if (action.Kind != ActionKind.ChooseCard || action.Parameters is not CardChoiceParameters choice)
            {
                Add(violations, "decision.turn-discard-parameters", $"{path}.legalActions", "Turn discard decisions contain only typed card choices.");
                continue;
            }

            if (!expected.Contains(choice.CardId))
            {
                Add(violations, "decision.extra-discard-action", $"{path}.legalActions", $"Discard card '{choice.CardId}' is not an unselected materialized candidate.");
            }

            if (!actual.Add(choice.CardId))
            {
                Add(violations, "decision.duplicate-discard-action", $"{path}.legalActions", $"Discard card '{choice.CardId}' is offered more than once.");
            }
        }

        if (pending.LegalActions.Count != expected.Length)
        {
            Add(violations, "decision.discard-action-count-mismatch", $"{path}.legalActions", $"Discard decision must offer exactly {expected.Length} remaining candidates.");
        }

        foreach (CardInstanceId cardId in expected)
        {
            if (!actual.Contains(cardId))
            {
                Add(violations, "decision.missing-discard-action", $"{path}.legalActions", $"Discard decision is missing candidate '{cardId}'.");
            }
        }
    }

    private static void ValidateMemoryCleanupActions(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
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

        if (owner.UsedMemory <= EffectiveMemoryLimit(state, behaviorCatalog, frame.Owner))
        {
            Add(violations, "resolution.unneeded-memory-cleanup", path, "Memory cleanup is only valid while used memory exceeds the effective memory limit.");
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
        || frame is TurnFrame
        {
            Stage: TurnFrameStage.BeforeTurnPriority
                or TurnFrameStage.ActionPriority
                or TurnFrameStage.AwaitingAction
                or TurnFrameStage.DiscardPriority,
        }
        || frame is TurnDiscardFrame
        || frame is InstallFrame
        || frame is MemoryCleanupFrame
        || frame is BehaviorResolutionFrame { Choice: not null }
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

    private static void ValidateBehaviorContinuationBindings(
        BehaviorResolutionFrame frame,
        IReadOnlyList<BehaviorInstruction> rootInstructions,
        Dictionary<CardInstanceId, CardInstanceState> cardsById,
        string path,
        List<StateInvariantViolation> violations)
    {
        var selectionsByKey = new Dictionary<string, IReadOnlyList<CardInstanceId>>(StringComparer.Ordinal);
        IReadOnlyList<CardInstanceId>? lastNonEmptySelection = null;
        var replay = rootInstructions.ToList();
        for (int index = 0; index < frame.Continuation.Count; index++)
        {
            BehaviorContinuationStep step = frame.Continuation[index];
            string stepPath = $"{path}.continuation[{index}]";
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
            {
                // Reachability/cursor diagnostics are emitted by the caller;
                // do not dereference an invalid forged path here.
                continue;
            }

            BehaviorInstruction instruction = replay[step.InstructionCursor];
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection)
            {
                bool targetChoice = instruction.Kind == BehaviorInstructionKind.Search
                    ? instruction.Target is not null
                    : instruction.Kind == BehaviorInstructionKind.Choose
                        && instruction.Choice is not null
                        && instruction.Choice.Target is not null;
                if (!targetChoice)
                {
                    Add(
                        violations,
                        "resolution.behavior-selection-not-target",
                        stepPath,
                        "A card-selection continuation must resolve a catalog-declared target choice.");
                }

                CardInstanceId[] selected = step.CardIds.ToArray();
                foreach (CardInstanceId cardId in selected)
                {
                    if (!cardsById.ContainsKey(cardId))
                    {
                        Add(
                            violations,
                            "resolution.unknown-behavior-continuation-card",
                            $"{stepPath}.cardIds",
                            $"Behavior continuation selection references unknown card '{cardId}'.");
                    }
                }

                selectionsByKey[step.Key] = selected;
                if (selected.Length > 0)
                {
                    lastNonEmptySelection = selected;
                }
                else if (instruction.Kind == BehaviorInstructionKind.Search)
                {
                    // An authenticated fail-to-find supersedes any earlier
                    // search result. The engine clears the corresponding
                    // binding and canonical chosen value, so the invariant
                    // must not demand a stale chosen binding merely because
                    // an older continuation selected a card.
                    lastNonEmptySelection = null;
                }
            }

            if (!BehaviorContinuation.TryApply(replay, step, out _))
            {
                // The main frame validator reports the precise unreachable
                // continuation; stop replaying this auxiliary binding check.
                break;
            }
        }

        foreach (BehaviorBinding binding in frame.Bindings)
        {
            if (string.Equals(binding.Key, "chosen", StringComparison.Ordinal))
            {
                if (lastNonEmptySelection is null)
                {
                    Add(
                        violations,
                        "resolution.behavior-binding-unreachable",
                        $"{path}.bindings",
                        "The chosen behavior binding has no reachable nonempty card-selection continuation.");
                }
                else if (!binding.CardIds.SequenceEqual(lastNonEmptySelection))
                {
                    Add(
                        violations,
                        "resolution.behavior-binding-mismatch",
                        $"{path}.bindings",
                        "The chosen behavior binding does not match the latest reachable card-selection continuation.");
                }

                continue;
            }

            if (!selectionsByKey.TryGetValue(binding.Key, out IReadOnlyList<CardInstanceId>? selectedCards))
            {
                Add(
                    violations,
                    "resolution.behavior-binding-unreachable",
                    $"{path}.bindings",
                    $"Behavior binding '{binding.Key}' is not produced by a reachable catalog choice continuation.");
            }
            else if (!binding.CardIds.SequenceEqual(selectedCards))
            {
                Add(
                    violations,
                    "resolution.behavior-binding-mismatch",
                    $"{path}.bindings",
                    $"Behavior binding '{binding.Key}' does not match its reachable card-selection continuation.");
            }
        }

        if (lastNonEmptySelection is not null
            && !frame.Bindings.Any(binding => string.Equals(binding.Key, "chosen", StringComparison.Ordinal)))
        {
            Add(
                violations,
                "resolution.behavior-binding-missing",
                $"{path}.bindings",
                "A nonempty card-selection continuation must retain the chosen binding until consumed.");
        }
    }

    private static int EffectiveMemoryLimit(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        PlayerId playerId)
    {
        PlayerRulesState? rules = state.RulesState?.Players
            .FirstOrDefault(player => player?.PlayerId == playerId);
        PlayerState? player = state.Players.FirstOrDefault(value => value?.Id == playerId);
        if (rules is null || player is null)
        {
            return 0;
        }

        try
        {
            return checked(rules.BaseMemory + BehaviorStaticModifierProjection.Sum(
                behaviorCatalog,
                state.Cards.Where(card => card is not null).Select(card => card!).ToArray(),
                state.Zones.Where(zone => zone is not null).Select(zone => zone!).ToArray(),
                player.Side,
                BehaviorStaticModifierKind.MemoryUnits));
        }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
        {
            return int.MaxValue;
        }
    }

    private static void ValidateBehaviorChoiceMaterialization(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        BehaviorResolutionFrame frame,
        BehaviorChoiceMaterialization choice,
        List<BehaviorInstruction> derivedInstructions,
        Dictionary<CardInstanceId, CardInstanceState> cardsById,
        string path,
        List<StateInvariantViolation> violations)
    {
        string choicePath = $"{path}.choice";
        if (frame.InstructionCursor < 0 || frame.InstructionCursor >= derivedInstructions.Count)
        {
            return;
        }

        BehaviorInstruction instruction = derivedInstructions[frame.InstructionCursor];
        BehaviorChoiceSpec? specification = instruction.Kind == BehaviorInstructionKind.Choose
            ? instruction.Choice
            : null;
        BehaviorTarget? target = instruction.Kind == BehaviorInstructionKind.Search
            ? instruction.Target
            : specification?.Target;
        string expectedKey = instruction.Kind == BehaviorInstructionKind.Search
            ? StableIdentifier.TryValidate(instruction.Argument)
                ? instruction.Argument!
                : $"search-{frame.InstructionCursor.ToString(CultureInfo.InvariantCulture)}"
            : specification?.Key ?? string.Empty;
        if (!string.Equals(choice.Key, expectedKey, StringComparison.Ordinal))
        {
            Add(violations, "resolution.behavior-choice-key-mismatch", $"{choicePath}.key", "Materialized behavior choice key does not match the catalog instruction at the cursor.");
        }

        if (specification is not null)
        {
            PlayerId? expectedOwner = ResolveBehaviorChooser(state, frame, specification.Chooser);
            if (expectedOwner.HasValue && choice.Owner != expectedOwner.Value)
            {
                Add(violations, "resolution.behavior-choice-owner-mismatch", $"{choicePath}.owner", "Materialized behavior choice owner does not match its catalog chooser.");
            }
        }
        else if (instruction.Kind == BehaviorInstructionKind.Search)
        {
            PlayerId expectedOwner = frame.Controller;
            if (choice.Owner != expectedOwner)
            {
                Add(violations, "resolution.behavior-choice-owner-mismatch", $"{choicePath}.owner", "Search choice owner must be the behavior controller.");
            }
        }

        if (instruction.Kind == BehaviorInstructionKind.Search)
        {
            bool constrainedSingleCardSearch = IsConstrainedSingleCardSearch(instruction);
            BehaviorChoiceMode expectedMode = BehaviorChoiceMode.Mandatory;
            BehaviorChoiceCardinalityKind expectedCardinality = constrainedSingleCardSearch
                ? BehaviorChoiceCardinalityKind.UpTo
                : BehaviorChoiceCardinalityKind.Exactly;
            if (choice.Mode != expectedMode)
            {
                Add(
                    violations,
                    "resolution.behavior-search-mode-mismatch",
                    $"{choicePath}.mode",
                    "A materialized Search choice must be mandatory.");
            }

            if (choice.CardinalityKind != expectedCardinality)
            {
                Add(
                    violations,
                    "resolution.behavior-search-cardinality-mismatch",
                    $"{choicePath}.cardinalityKind",
                    $"This Search requires '{expectedCardinality}' cardinality.");
            }

            if (choice.CardinalityCount != 1)
            {
                Add(
                    violations,
                    "resolution.behavior-search-count-mismatch",
                    $"{choicePath}.cardinalityCount",
                    "A materialized Search choice must have a cardinality count of one.");
            }
        }

        if (target is null)
        {
            if (choice.CandidateCardIds.Count > 0 || choice.SelectedCardIds.Count > 0)
            {
                Add(violations, "resolution.behavior-choice-target-missing", choicePath, "A mode choice without a target must not carry card candidates.");
            }
        }
        else
        {
            CardInstanceId[] expectedCandidates = MaterializeBehaviorCandidates(
                state,
                behaviorCatalog,
                runtimeCardCatalog,
                target,
                instruction.Kind == BehaviorInstructionKind.Search || target.Kind is BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards
                    ? frame.SourceCardId
                    : null);
            if (!choice.CandidateCardIds.SequenceEqual(expectedCandidates))
            {
                Add(violations, "resolution.behavior-choice-candidates-mismatch", $"{choicePath}.candidateCardIds", "Materialized behavior candidates no longer match the catalog target and current state.");
            }
        }

        if (choice.CandidateCardIds.Distinct().Count() != choice.CandidateCardIds.Count)
        {
            Add(violations, "resolution.duplicate-behavior-candidate", $"{choicePath}.candidateCardIds", "Materialized behavior candidates must be distinct.");
        }

        if (choice.SelectedCardIds.Any(cardId => !choice.CandidateCardIds.Contains(cardId))
            || choice.SelectedCardIds.Distinct().Count() != choice.SelectedCardIds.Count)
        {
            Add(violations, "resolution.behavior-choice-selection-invalid", $"{choicePath}.selectedCardIds", "Materialized behavior selection must be a distinct subset of frozen candidates.");
        }

        foreach (CardInstanceId cardId in choice.CandidateCardIds)
        {
            if (!cardsById.ContainsKey(cardId))
            {
                Add(violations, "resolution.unknown-behavior-candidate", $"{choicePath}.candidateCardIds", $"Behavior candidate '{cardId}' does not exist in the state.");
            }
        }

        if (instruction.Kind == BehaviorInstructionKind.Choose && specification is not null)
        {
            string[] expectedOptions = specification.Options
                .Where(option => !IsBehaviorInstallOption(option)
                    || IsBehaviorInstallOptionAvailable(
                        state,
                        behaviorCatalog,
                        runtimeCardCatalog,
                        frame,
                        option))
                .Select(option => option.Key)
                .ToArray();
            if (!choice.OptionKeys.SequenceEqual(expectedOptions, StringComparer.Ordinal))
            {
                Add(violations, "resolution.behavior-choice-options-mismatch", $"{choicePath}.optionKeys", "Materialized behavior options do not match the catalog declaration.");
            }
        }
        else if (choice.OptionKeys.Count > 0)
        {
            Add(violations, "resolution.behavior-choice-options-unexpected", $"{choicePath}.optionKeys", "Search choices cannot carry option keys.");
        }

        if (choice.CardinalityKind == BehaviorChoiceCardinalityKind.Exactly
            && choice.CardinalityCount is not { } exact)
        {
            Add(violations, "resolution.behavior-choice-cardinality-invalid", choicePath, "Exactly choices require a count.");
        }
        else if (choice.CardinalityKind == BehaviorChoiceCardinalityKind.UpTo
            && choice.CardinalityCount is not { } upTo)
        {
            Add(violations, "resolution.behavior-choice-cardinality-invalid", choicePath, "Up-to choices require a count.");
        }
    }

    private static bool IsConstrainedSingleCardSearch(BehaviorInstruction instruction) =>
        instruction.Target is { Kind: BehaviorTargetKind.ZoneCard }
        && instruction.SourceZone is BehaviorZone.Deck or BehaviorZone.Stack
        && instruction.Filter is not null
        && !BehaviorContractStructure.AreEqual(instruction.Filter, BehaviorCardFilter.Any);

    private static PlayerId? ResolveBehaviorChooser(
        GameState state,
        BehaviorResolutionFrame frame,
        BehaviorChooser chooser)
    {
        PlayerSide? side = chooser switch
        {
            BehaviorChooser.Corp => PlayerSide.Corp,
            BehaviorChooser.Runner => PlayerSide.Runner,
            BehaviorChooser.Controller => state.Players.FirstOrDefault(player => player?.Id == frame.Controller)?.Side,
            BehaviorChooser.ActingPlayer => frame.ActingPlayer.HasValue
                ? state.Players.FirstOrDefault(player => player?.Id == frame.ActingPlayer.Value)?.Side
                : null,
            BehaviorChooser.Opponent => state.Players.FirstOrDefault(player => player?.Id == frame.Controller)?.Side == PlayerSide.Corp
                ? PlayerSide.Runner
                : PlayerSide.Corp,
            _ => null,
        };
        return side.HasValue
            ? state.Players.FirstOrDefault(player => player?.Side == side.Value)?.Id
            : null;
    }

    private static CardInstanceId[] MaterializeBehaviorCandidates(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        BehaviorTarget target,
        CardInstanceId? excludedCardId)
    {
        if (target.Kind is BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards)
        {
            ZoneState? zone = FindBehaviorZone(state, target.Zone!.Value);
            return zone is null
                ? []
                : zone.CardIds
                    .Where(cardId => !excludedCardId.HasValue || cardId != excludedCardId.Value)
                    .Where(cardId => BehaviorCardMatches(state, behaviorCatalog, runtimeCardCatalog, cardId, target.Filter))
                    .ToArray();
        }

        if (target.Kind is BehaviorTargetKind.InstalledCard or BehaviorTargetKind.InstalledCardNotThisTurn)
        {
            PlayerSide? owner = target.Filter?.OwnerSide;
            return state.Cards
                .Where(card => card is not null && IsInstalledCard(state, card.Id)
                    && (owner is null || card.OwnerSide == owner.Value)
                    && (target.Kind != BehaviorTargetKind.InstalledCardNotThisTurn
                        || !IsInstalledThisTurn(state, card.Id, owner))
                    && BehaviorCardMatches(state, behaviorCatalog, runtimeCardCatalog, card.Id, target.Filter))
                .OrderBy(card => card!.Id.Value, StringComparer.Ordinal)
                .Select(card => card!.Id)
                .ToArray();
        }

        return [];
    }

    private static ZoneState? FindBehaviorZone(GameState state, BehaviorZone zone) => zone switch
    {
        BehaviorZone.Deck => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.CorpDeck && value.ServerId is null),
        BehaviorZone.Headquarters => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.CorpHand && value.ServerId is null),
        BehaviorZone.Archives => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.CorpDiscard && value.ServerId is null),
        BehaviorZone.Grip => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.RunnerGrip && value.ServerId is null),
        BehaviorZone.Stack => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.RunnerStack && value.ServerId is null),
        BehaviorZone.Heap => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.RunnerHeap && value.ServerId is null),
        BehaviorZone.Rig => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.RunnerRig && value.ServerId is null),
        BehaviorZone.PlayArea => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.RunnerPlayArea && value.ServerId is null),
        BehaviorZone.ScoreArea => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.CorpScore && value.ServerId is null),
        BehaviorZone.ServerRoot => state.Servers
            .Where(server => server.Kind == ServerKind.Remote)
            .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
            .Select(server => state.Zones.FirstOrDefault(zoneState => zoneState.Id == server.RootZoneId))
            .FirstOrDefault(value => value is not null),
        BehaviorZone.ServerIce => state.Servers
            .Where(server => server.Kind == ServerKind.Remote)
            .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
            .Select(server => state.Zones.FirstOrDefault(zoneState => zoneState.Id == server.IceZoneId))
            .FirstOrDefault(value => value is not null),
        _ => null,
    };

    private static bool IsInstalledCard(GameState state, CardInstanceId cardId)
    {
        CardInstanceState? card = state.Cards.FirstOrDefault(value => value?.Id == cardId);
        ZoneState? zone = card is null
            ? null
            : state.Zones.FirstOrDefault(value => value?.Id == card.ZoneId);
        return zone?.Kind is ZoneKind.RunnerRig or ZoneKind.ServerIce or ZoneKind.ServerRoot;
    }

    private static bool IsInstalledThisTurn(GameState state, CardInstanceId cardId, PlayerSide? owner)
    {
        IEnumerable<TurnFactsState?> facts = owner.HasValue
            ? [state.Turn.History?.ThisTurn(owner.Value)]
            : [state.Turn.History?.CorpThisTurn, state.Turn.History?.RunnerThisTurn];
        return facts.Any(current => current?.InstalledCardIds.Contains(cardId) == true);
    }

    private static bool BehaviorCardMatches(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        CardInstanceId cardId,
        BehaviorCardFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        CardInstanceState? card = state.Cards.FirstOrDefault(value => value?.Id == cardId);
        if (card is null
            || (filter.DefinitionId.HasValue && card.DefinitionId != filter.DefinitionId.Value)
            || (filter.OwnerSide.HasValue && card.OwnerSide != filter.OwnerSide.Value))
        {
            return false;
        }

        if (runtimeCardCatalog is not null)
        {
            if (!runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? runtimeSpec))
            {
                return false;
            }

            if (filter.Subtype is not null
                && !runtimeSpec.Subtypes.Contains(filter.Subtype, StringComparer.Ordinal))
            {
                return false;
            }

            return filter.CardType switch
            {
                BehaviorCardType.Any => true,
                BehaviorCardType.Agenda => runtimeSpec.CardClass == RuntimeCardClass.Agenda,
                BehaviorCardType.Asset => runtimeSpec.CardClass == RuntimeCardClass.Asset,
                BehaviorCardType.Event => runtimeSpec.CardClass == RuntimeCardClass.Event,
                BehaviorCardType.Hardware => runtimeSpec.CardClass == RuntimeCardClass.Hardware,
                BehaviorCardType.Ice => runtimeSpec.CardClass == RuntimeCardClass.Ice,
                BehaviorCardType.Icebreaker => runtimeSpec.CardClass == RuntimeCardClass.Program
                    && runtimeSpec.Subtypes.Contains("icebreaker", StringComparer.Ordinal),
                BehaviorCardType.Identity => runtimeSpec.CardClass == RuntimeCardClass.Identity,
                BehaviorCardType.Operation => runtimeSpec.CardClass == RuntimeCardClass.Operation,
                BehaviorCardType.Resource => runtimeSpec.CardClass == RuntimeCardClass.Resource,
                BehaviorCardType.Upgrade => runtimeSpec.CardClass == RuntimeCardClass.Upgrade,
                _ => false,
            };
        }

        if (filter.Subtype is not null || filter.CardType == BehaviorCardType.Any)
        {
            return filter.Subtype is null;
        }

        return filter.CardType switch
        {
            BehaviorCardType.Ice => behaviorCatalog.TryGetIce(card.DefinitionId, out _),
            BehaviorCardType.Icebreaker => behaviorCatalog.TryGetIcebreaker(card.DefinitionId, out _),
            BehaviorCardType.Asset => behaviorCatalog.TryGetAsset(card.DefinitionId, out _),
            BehaviorCardType.Agenda => behaviorCatalog.TryGetAgenda(card.DefinitionId, out _),
            BehaviorCardType.Event => behaviorCatalog.TryGetRunEvent(card.DefinitionId, out _)
                || behaviorCatalog.TryGetCardType(card.DefinitionId, out BehaviorCardType eventClassified)
                && eventClassified == filter.CardType,
            _ => behaviorCatalog.TryGetCardType(card.DefinitionId, out BehaviorCardType classified)
                && classified == filter.CardType,
        };
    }

    private static bool IsBehaviorInstallOption(BehaviorChoiceOption option) =>
        option.Instructions.Count == 1
        && option.Instructions[0].Kind == BehaviorInstructionKind.InstallCard;

    private static bool IsBehaviorInstallOptionAvailable(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        BehaviorResolutionFrame frame,
        BehaviorChoiceOption option)
    {
        if (!IsBehaviorInstallOption(option)
            || runtimeCardCatalog is null)
        {
            return !IsBehaviorInstallOption(option);
        }

        try
        {
            BehaviorInstruction instruction = option.Instructions[0];
            if (instruction.Target is not { Kind: BehaviorTargetKind.ChosenCard } target
                || instruction.SourceZone != BehaviorZone.Stack
                || instruction.DestinationZone != BehaviorZone.Rig
                || (target.Selector is not null
                    && !string.Equals(target.Selector, "chosen", StringComparison.Ordinal)))
            {
                return false;
            }

            string bindingKey = StableIdentifier.TryValidate(target.Selector)
                ? target.Selector!
                : "chosen";
            BehaviorBinding? binding = frame.Bindings.SingleOrDefault(
                candidate => string.Equals(candidate.Key, bindingKey, StringComparison.Ordinal));
            if (binding is null || binding.CardIds.Count != 1)
            {
                return false;
            }

            CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate?.Id == binding.CardIds[0]);
            ZoneState? stack = state.Zones.FirstOrDefault(
                zone => zone?.Kind == ZoneKind.RunnerStack
                    && zone.OwnerSide == PlayerSide.Runner
                    && zone.ServerId is null);
            ZoneState? rig = state.Zones.FirstOrDefault(
                zone => zone?.Kind == ZoneKind.RunnerRig
                    && zone.OwnerSide == PlayerSide.Runner
                    && zone.ServerId is null);
            if (card is null
                || stack is null
                || rig is null
                || card.OwnerSide != PlayerSide.Runner
                || card.ZoneId != stack.Id
                || !stack.CardIds.Contains(card.Id)
                || !runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? incoming)
                || incoming.Side != PlayerSide.Runner
                || incoming.CardClass != RuntimeCardClass.Program)
            {
                return false;
            }

            // BuildBehaviorInstallPlan rejects the complete physical Rig when
            // even one card has no authoritative runtime metadata.  Mirror
            // that gate before deriving the narrower Program candidate list.
            if (!AllRuntimeCardsKnown(state, runtimeCardCatalog, rig))
            {
                return false;
            }

            // A chosen binding is not, by itself, proof that the install
            // instruction may consume the card.  The engine requires a
            // reachable catalog Search/Choose continuation whose target is
            // the canonical Runner Stack and whose selected IDs are exactly
            // the binding being installed.
            if (!HasReachableBehaviorInstallOrigin(
                    state,
                    behaviorCatalog,
                    runtimeCardCatalog,
                    frame,
                    bindingKey,
                    binding.CardIds))
            {
                return false;
            }

            CardInstanceId[] conflicts = ActiveRunnerInstallConflictIds(
                state,
                runtimeCardCatalog,
                incoming);
            CardInstanceId[] candidates = RunnerInstallCandidateIds(
                state,
                runtimeCardCatalog,
                rig,
                incoming);
            if (conflicts.Any(conflict => !candidates.Contains(conflict)))
            {
                return false;
            }
            if (!CanBehaviorInstallProgram(
                    state,
                    behaviorCatalog,
                    runtimeCardCatalog,
                    card,
                    incoming,
                    candidates))
            {
                return false;
            }

            PlayerState? runner = state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Runner);
            return runner is not null && runner.Credits >= (incoming.InstallCost ?? 0);
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or OverflowException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException)
        {
            // Option materialization is a diagnostic projection.  Any
            // malformed state/catalog arithmetic must make the executable
            // install option unavailable, never escape Collect as an error.
            return false;
        }
    }

    private static CardInstanceId[] ActiveRunnerInstallConflictIds(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog,
        CardRuntimeSpec incoming)
    {
        ZoneState? rig = state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.RunnerRig
            && zone.OwnerSide == PlayerSide.Runner
            && zone.ServerId is null);
        if (rig is null || (!incoming.Unique && !incoming.Console))
        {
            return [];
        }

        return rig.CardIds
            .Select(cardId => state.Cards.FirstOrDefault(card => card?.Id == cardId))
            .Where(card => card is not null
                && card.OwnerSide == PlayerSide.Runner
                && card.ZoneId == rig.Id
                && card.FaceUp
                && runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? current)
                && ((incoming.Unique
                        && incoming.UniquenessKey is not null
                        && current.Unique
                        && current.UniquenessKey is not null
                        && string.Equals(current.UniquenessKey, incoming.UniquenessKey, StringComparison.Ordinal))
                    || (incoming.Console && current.Console)))
            .Select(card => card!.Id)
            .Distinct()
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static CardInstanceId[] RunnerInstallCandidateIds(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog,
        ZoneState rig,
        CardRuntimeSpec incoming)
    {
        CardInstanceId[] conflicts = ActiveRunnerInstallConflictIds(
            state,
            runtimeCardCatalog,
            incoming);
        if (incoming.CardClass != RuntimeCardClass.Program)
        {
            return conflicts;
        }

        return rig.CardIds
            .Select(cardId => state.Cards.FirstOrDefault(card => card?.Id == cardId))
            .Where(card => card is not null
                && card.OwnerSide == PlayerSide.Runner
                && card.ZoneId == rig.Id
                && runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? candidateSpec)
                && candidateSpec.CardClass == RuntimeCardClass.Program)
            .Select(card => card!.Id)
            .Concat(conflicts)
            .Distinct()
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool AllRuntimeCardsKnown(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog,
        ZoneState rig)
    {
        foreach (CardInstanceId cardId in rig.CardIds)
        {
            CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate?.Id == cardId);
            if (card is null || !runtimeCardCatalog.TryGet(card.DefinitionId, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllActiveRunnerRigCardsKnown(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog,
        ZoneState rig)
    {
        foreach (CardInstanceId cardId in rig.CardIds)
        {
            CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate?.Id == cardId);
            if (card is not null
                && card.OwnerSide == PlayerSide.Runner
                && card.ZoneId == rig.Id
                && card.FaceUp
                && !runtimeCardCatalog.TryGet(card.DefinitionId, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasReachableBehaviorInstallOrigin(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog,
        BehaviorResolutionFrame frame,
        string bindingKey,
        IReadOnlyList<CardInstanceId> cardIds)
    {
        if (!behaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            return false;
        }

        List<BehaviorInstruction> replay = definition.Instructions.ToList();
        BehaviorTarget? originatingTarget = null;
        IReadOnlyList<CardInstanceId>? originatingSelection = null;
        foreach (BehaviorContinuationStep step in frame.Continuation)
        {
            if (step.InstructionCursor < 0 || step.InstructionCursor >= replay.Count)
            {
                return false;
            }

            BehaviorInstruction sourceInstruction = replay[step.InstructionCursor];
            if (step.Kind == BehaviorContinuationKind.ChoiceSelection
                && step.CardIds.Count > 0
                && (string.Equals(step.Key, bindingKey, StringComparison.Ordinal)
                    || (bindingKey == "chosen"
                        && frame.Bindings.Any(binding =>
                            string.Equals(binding.Key, step.Key, StringComparison.Ordinal)
                            && binding.CardIds.SequenceEqual(cardIds)))))
            {
                originatingTarget = sourceInstruction.Kind switch
                {
                    BehaviorInstructionKind.Search => sourceInstruction.Target,
                    BehaviorInstructionKind.Choose => sourceInstruction.Choice?.Target,
                    _ => null,
                };
                originatingSelection = step.CardIds;
            }

            if (!BehaviorContinuation.TryApply(replay, step, out _))
            {
                return false;
            }
        }

        return originatingTarget is not null
            && originatingTarget.Kind is BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards
            && originatingTarget.Zone == BehaviorZone.Stack
            && originatingSelection is not null
            && originatingSelection.SequenceEqual(cardIds)
            && MaterializeBehaviorCandidates(
                    state,
                    behaviorCatalog,
                    runtimeCardCatalog,
                    originatingTarget,
                    excludedCardId: null)
                .Contains(cardIds[0]);
    }

    private static bool CanBehaviorInstallProgram(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog,
        CardInstanceState incoming,
        CardRuntimeSpec incomingSpec,
        IReadOnlyList<CardInstanceId> candidates)
    {
        PlayerState? runner = state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Runner);
        PlayerRulesState? rules = runner is null
            ? null
            : state.RulesState?.Players.FirstOrDefault(value => value?.PlayerId == runner.Id);
        int used = rules?.UsedMemory ?? state.Cards
            .Where(card => card?.OwnerSide == PlayerSide.Runner
                && IsInstalledCard(state, card.Id)
                && runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? current)
                && current.CardClass == RuntimeCardClass.Program)
            .Sum(card => runtimeCardCatalog.GetSpec(card!.DefinitionId).MemoryUnits);
        int freed = 0;
        int removedStatic = 0;
        foreach (CardInstanceId candidateId in candidates)
        {
            CardInstanceState? candidate = state.Cards.FirstOrDefault(card => card?.Id == candidateId);
            if (candidate is null || !runtimeCardCatalog.TryGet(candidate.DefinitionId, out CardRuntimeSpec? spec))
            {
                return false;
            }

            freed = checked(freed + spec.MemoryUnits);
            if (candidate.FaceUp && behaviorCatalog.TryGet(candidate.DefinitionId, out CardBehaviorDefinition? definition))
            {
                removedStatic = checked(removedStatic + definition.StaticModifiers
                    .Where(modifier => modifier.Kind == BehaviorStaticModifierKind.MemoryUnits)
                    .Sum(modifier => modifier.Delta));
            }
        }

        int currentStatic = BehaviorStaticModifierProjection.Sum(
            behaviorCatalog,
            state.Cards.Where(card => card is not null).Select(card => card!).ToArray(),
            state.Zones.Where(zone => zone is not null).Select(zone => zone!).ToArray(),
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits);
        int incomingStatic = behaviorCatalog.TryGet(incoming.DefinitionId, out CardBehaviorDefinition? incomingDefinition)
            ? incomingDefinition.StaticModifiers
                .Where(modifier => modifier.Kind == BehaviorStaticModifierKind.MemoryUnits)
                .Sum(modifier => modifier.Delta)
            : 0;
        int effective = checked((rules?.BaseMemory ?? 4) + currentStatic - removedStatic + incomingStatic);
        return checked(used - freed + incomingSpec.MemoryUnits) <= effective;
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

            if (frame is BehaviorResolutionFrame behaviorFrame)
            {
                Observe(behaviorFrame.FrameIdentity, "behavior-frame");
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

    private static void ValidateTurnFacts(
        TurnFactsState? facts,
        PlayerSide expectedSide,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (facts is null)
        {
            return;
        }

        if (facts.Version != 1)
        {
            Add(violations, "turn.unsupported-facts-version", $"{path}.version", "Only turn-facts version 1 is supported.");
        }

        if (facts.Side != expectedSide)
        {
            Add(violations, "turn.facts-side-mismatch", $"{path}.side", $"Turn facts must belong to '{expectedSide}'.");
        }

        if (facts.TurnNumber < 1)
        {
            Add(violations, "turn.facts-invalid-number", $"{path}.turnNumber", "Turn facts must identify a positive turn number.");
        }

        if (facts.ClicksAllotted < 0 || facts.ClicksLost < 0)
        {
            Add(violations, "turn.facts-negative-clicks", path, "Turn facts click counts must be nonnegative.");
        }

        int expectedClicks = expectedSide == PlayerSide.Corp ? 3 : 4;
        if (facts.ClicksAllotted != expectedClicks)
        {
            Add(violations, "turn.facts-allotment-mismatch", $"{path}.clicksAllotted", $"'{expectedSide}' turn facts must record {expectedClicks} allotted clicks.");
        }

        if (facts.MandatoryDrawPerformed && expectedSide != PlayerSide.Corp)
        {
            Add(violations, "turn.runner-mandatory-draw", $"{path}.mandatoryDrawPerformed", "Runner turns do not have a mandatory draw phase.");
        }

        if (facts.MandatoryDrawPerformed && !facts.TurnBegan)
        {
            Add(violations, "turn.draw-before-begin", $"{path}.mandatoryDrawPerformed", "A mandatory draw cannot be complete before formal turn begin.");
        }

        if (facts.ActionPhaseEnded && !facts.TurnBegan)
        {
            Add(violations, "turn.action-before-begin", $"{path}.actionPhaseEnded", "An action phase cannot end before formal turn begin.");
        }

        if (facts.DiscardPhaseEnded && !facts.ActionPhaseEnded)
        {
            Add(violations, "turn.discard-before-action-end", $"{path}.discardPhaseEnded", "A discard phase cannot end before the action phase.");
        }

        if (facts.SuccessfulRun && expectedSide != PlayerSide.Runner)
        {
            Add(violations, "turn.corp-successful-run", $"{path}.successfulRun", "Only Runner turn facts may record a successful run.");
        }

        var installed = new HashSet<CardInstanceId>();
        for (int index = 0; index < facts.InstalledCardIds.Count; index++)
        {
            CardInstanceId cardId = facts.InstalledCardIds[index];
            if (!StableIdentifier.TryValidate(cardId.Value))
            {
                Add(violations, "id.invalid", $"{path}.installedCardIds[{index}]", "Installed card ID must be non-empty and stable.");
            }
            else if (!installed.Add(cardId))
            {
                Add(violations, "turn.duplicate-installed-card", $"{path}.installedCardIds[{index}]", $"Installed card '{cardId}' is duplicated.");
            }
        }
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
