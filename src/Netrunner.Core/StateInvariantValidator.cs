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
public static partial class StateInvariantValidator
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
        if (IsCertified(state, behaviorCatalog, runtimeCardCatalog)) return [];
        IBehaviorCatalog? requestedCatalog = behaviorCatalog;
        bool enforceCatalogCompatibility = behaviorCatalog is not null;
        behaviorCatalog ??= BehaviorCatalog.Empty;

        var violations = new List<StateInvariantViolation>();
        ValidateMetadata(state, behaviorCatalog, enforceCatalogCompatibility, runtimeCardCatalog, violations);
        ValidatePlayers(state, violations);
        ValidateZonesAndCards(state, behaviorCatalog, violations);
        ValidateHosting(state, behaviorCatalog, runtimeCardCatalog, violations);
        ValidateServers(state, violations);
        ValidateSetupAndIdentities(state, runtimeCardCatalog, violations);
        if (runtimeCardCatalog is not null)
        {
            ValidateRuntimeCardActivity(state, runtimeCardCatalog, violations);
        }
        ValidateTurnAndOutcome(state, runtimeCardCatalog, violations);
        ValidateRunState(state, behaviorCatalog, violations);
        ValidatePersistentRuns(state, behaviorCatalog, violations);
        ValidateRulesState(state, behaviorCatalog, runtimeCardCatalog, violations);
        ValidateResolutionAndDecision(
            state,
            behaviorCatalog,
            runtimeCardCatalog,
            enforceCatalogCompatibility,
            violations);
        if (violations.Count == 0) Certify(state, requestedCatalog, runtimeCardCatalog);
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
                        || behaviorCatalog.TryGetAgenda(card.DefinitionId, out _))
                    // VP-CARD-AS-AGENDA: a card added to a score area as an agenda may be the other side's (Word on the Street).
                    || zone.Kind is ZoneKind.CorpScore or ZoneKind.RunnerScore
                        && state.RulesState?.ScoredAsAgendas?.Any(entry => entry.CardId == card.Id) == true;
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

                if (zone.Kind is ZoneKind.CorpRemovedFromGame or ZoneKind.RunnerRemovedFromGame
                    && !card.FaceUp)
                {
                    Add(
                        violations,
                        "cards.removed-from-game-facedown",
                        path,
                        $"Removed-from-game card '{card.Id}' must be faceup.");
                }

                if (zone.Kind is ZoneKind.CorpHostedFaceUp or ZoneKind.RunnerHostedFaceUp
                    && (!card.FaceUp
                        || card.HostCardId is not { } faceUpHost
                        || !cardsById.TryGetValue(faceUpHost.Value, out CardInstanceState? host)
                        || state.Zones.FirstOrDefault(candidate => candidate?.Id == host.ZoneId)?.Kind != ZoneKind.RunnerRig))
                {
                    Add(
                        violations,
                        "cards.hosted-faceup",
                        path,
                        $"Hosted faceup card '{card.Id}' must be faceup and name an installed Runner host.");
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
            ZoneKind.CorpIdentity or ZoneKind.CorpDeck or ZoneKind.CorpHand or ZoneKind.CorpDiscard or ZoneKind.CorpScore or ZoneKind.CorpPlayArea or ZoneKind.CorpRemovedFromGame or ZoneKind.CorpHostedFaceUp => PlayerSide.Corp,
            ZoneKind.RunnerIdentity or ZoneKind.RunnerStack or ZoneKind.RunnerGrip or ZoneKind.RunnerPlayArea or ZoneKind.RunnerHeap or ZoneKind.RunnerRig or ZoneKind.RunnerScore or ZoneKind.RunnerRemovedFromGame or ZoneKind.RunnerHostedFaceUp => PlayerSide.Runner,
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

        foreach (ZoneKind removedKind in new[] { ZoneKind.CorpRemovedFromGame, ZoneKind.RunnerRemovedFromGame, ZoneKind.CorpHostedFaceUp, ZoneKind.RunnerHostedFaceUp })
        {
            int removedCount = state.Zones.Count(zone => zone is not null && zone.Kind == removedKind);
            if (removedCount > 1)
            {
                Add(
                    violations,
                    "zones.required-kind-count",
                    "zones",
                    $"Found {removedCount} '{removedKind}' zones; at most one is allowed.");
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

            ValidateServerLifecycle(state, server, path, violations);
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

    private static void ValidateRuntimeCardActivity(
        GameState state,
        IRuntimeCardCatalog runtimeCardCatalog,
        List<StateInvariantViolation> violations)
    {
        (CardInstanceState Card, CardRuntimeSpec Spec)[] activeCards = RuntimeCardActivity
            .EnumerateKnownActive(state.Cards, state.Zones, runtimeCardCatalog)
            .ToArray();

        foreach (var group in activeCards
            .Where(value => value.Spec.Unique && value.Spec.UniquenessKey is not null)
            .GroupBy(value => (value.Card.OwnerSide, UniquenessKey: value.Spec.UniquenessKey!)))
        {
            if (group.Count() <= 1)
            {
                continue;
            }

            string cardIds = string.Join(
                ", ",
                group.Select(value => value.Card.Id.Value).OrderBy(value => value, StringComparer.Ordinal));
            Add(
                violations,
                "cards.active-duplicate-uniqueness",
                "cards",
                $"Active {group.Key.OwnerSide} cards share uniqueness key '{group.Key.UniquenessKey}': {cardIds}.");
        }

        (CardInstanceState Card, CardRuntimeSpec Spec)[] activeConsoles = activeCards
            .Where(value => value.Card.OwnerSide == PlayerSide.Runner
                && value.Spec.Console
                && value.Spec.ConsoleKey is not null
                && StableIdentifier.TryValidate(value.Spec.ConsoleKey))
            .ToArray();
        if (activeConsoles.Length > 1)
        {
            string cardIds = string.Join(
                ", ",
                activeConsoles.Select(value => value.Card.Id.Value).OrderBy(value => value, StringComparer.Ordinal));
            Add(
                violations,
                "cards.active-duplicate-console",
                "cards",
                $"Runner has more than one active Console: {cardIds}.");
        }
    }

    private static void ValidateTurnAndOutcome(
        GameState state,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
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
                HashSet<CardInstanceId> knownCardIds = state.Cards
                    .Where(static card => card is not null && StableIdentifier.TryValidate(card.Id.Value))
                    .Select(static card => card!.Id)
                    .ToHashSet();
                Dictionary<PlayerId, PlayerSide> knownPlayers = state.Players
                    .Where(static player => player is not null && StableIdentifier.TryValidate(player.Id.Value))
                    .GroupBy(static player => player!.Id)
                    .ToDictionary(static group => group.Key, static group => group.First()!.Side);

                ValidateTurnFacts(
                    turn.History.CorpThisTurn,
                    PlayerSide.Corp,
                    "turn.history.corpThisTurn",
                    knownCardIds,
                    state.Cards,
                    state.Servers,
                    knownPlayers,
                    runtimeCardCatalog,
                    violations,
                    turn.History.CorpLastTurn is { } corpLast
                        ? corpLast.NextTurnExtraClicks
                        : (turn.History.RunnerThisTurn?.OpponentFirstTurnExtraClicks ?? 0)
                            + (turn.History.RunnerLastTurn?.OpponentFirstTurnExtraClicks ?? 0));
                ValidateTurnFacts(
                    turn.History.CorpLastTurn,
                    PlayerSide.Corp,
                    "turn.history.corpLastTurn",
                    knownCardIds,
                    state.Cards,
                    state.Servers,
                    knownPlayers,
                    runtimeCardCatalog,
                    violations,
                    extraAllottedClicks: null);
                ValidateTurnFacts(
                    turn.History.RunnerThisTurn,
                    PlayerSide.Runner,
                    "turn.history.runnerThisTurn",
                    knownCardIds,
                    state.Cards,
                    state.Servers,
                    knownPlayers,
                    runtimeCardCatalog,
                    violations,
                    turn.History.RunnerLastTurn is { } runnerLast
                        ? runnerLast.NextTurnExtraClicks
                        : (turn.History.CorpThisTurn?.OpponentFirstTurnExtraClicks ?? 0)
                            + (turn.History.CorpLastTurn?.OpponentFirstTurnExtraClicks ?? 0));
                ValidateTurnFacts(
                    turn.History.RunnerLastTurn,
                    PlayerSide.Runner,
                    "turn.history.runnerLastTurn",
                    knownCardIds,
                    state.Cards,
                    state.Servers,
                    knownPlayers,
                    runtimeCardCatalog,
                    violations,
                    extraAllottedClicks: null);
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
        bool hasRunFrame = state.ResolutionStack.Any(static frame =>
            frame is RunTimingFrame or IceSubroutineFrame);
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

        if (run.BadPublicityCredits < 0 || run.BadPublicityCredits > run.RunCredits)
        {
            Add(violations, "run.bad-publicity-credits", $"{path}.badPublicityCredits",
                "The bad publicity fund's share must lie between zero and the run credits.");
        }

        // A played run event uses the same card identity for both fields. An
        // installed click-run source is provenance only and deliberately has
        // no active run-event field; after initiation it may leave the Rig.
        bool installedRunSource = run.InitiatingSource.HasValue && !run.ActiveRunEventId.HasValue;

        if (run.BreachAccessModifiers is { } breachModifiers)
        {
            HashSet<(CardInstanceId Source, ServerId Server, string Reason, RunBreachAccessModifierSourceKind Kind)> seenModifiers = [];
            ServerState? attackedServer = state.Servers.FirstOrDefault(server => server?.Id == run.AttackedServer);
            TurnFactsState? activeFacts = state.Turn.History?.ThisTurn(state.Turn.ActiveSide);
            bool hasCurrentFormalFacts = activeFacts is not null
                && activeFacts.Side == state.Turn.ActiveSide
                && activeFacts.TurnNumber == state.Turn.TurnNumber
                && activeFacts.TurnBegan;
            bool isFirstHqBreach = hasCurrentFormalFacts
                && activeFacts!.HqBreachOccurrenceServerIds.Count == 1
                && activeFacts.HqBreachOccurrenceServerIds[^1] == run.AttackedServer;

            for (int index = 0; index < breachModifiers.Count; index++)
            {
                RunBreachAccessModifier? modifier = breachModifiers[index];
                string modifierPath = $"{path}.breachAccessModifiers[{index}]";
                if (modifier is null)
                {
                    Add(violations, "run.null-breach-modifier", modifierPath, "Breach access modifiers must not contain null entries.");
                    continue;
                }

                if (!seenModifiers.Add((
                        modifier.SourceCardId,
                        modifier.AttackedServerId,
                        modifier.ReasonKey,
                        modifier.SourceKind)))
                {
                    Add(
                        violations,
                        "run.duplicate-breach-modifier",
                        modifierPath,
                        "Breach access modifiers must not repeat a source, attacked server, and reason key.");
                }

                CardInstanceState? source = null;
                if (!StableIdentifier.TryValidate(modifier.SourceCardId.Value))
                {
                    Add(violations, "id.invalid", $"{modifierPath}.sourceCardId", "Breach modifier source card ID is empty or invalid.");
                }
                else
                {
                    source = state.Cards.FirstOrDefault(card => card?.Id == modifier.SourceCardId);
                    if (source is null)
                    {
                        Add(violations, "run.breach-modifier-unknown-source", $"{modifierPath}.sourceCardId", $"Breach modifier references unknown source card '{modifier.SourceCardId}'.");
                    }
                    else if (source.OwnerSide != PlayerSide.Runner)
                    {
                        Add(violations, "run.breach-modifier-source-owner", $"{modifierPath}.sourceCardId", "Breach modifier sources must be Runner-owned.");
                    }
                }

                if (modifier.AttackedServerId != run.AttackedServer)
                {
                    Add(
                        violations,
                        "run.breach-modifier-server-mismatch",
                        $"{modifierPath}.attackedServerId",
                        "Every breach access modifier must identify the run's attacked server.");
                }

                if (!StableIdentifier.TryValidate(modifier.AttackedServerId.Value))
                {
                    Add(violations, "id.invalid", $"{modifierPath}.attackedServerId", "Breach modifier attacked server ID is empty or invalid.");
                }
                else if (!state.Servers.Any(server => server?.Id == modifier.AttackedServerId))
                {
                    Add(violations, "run.breach-modifier-unknown-server", $"{modifierPath}.attackedServerId", $"Breach modifier references unknown server '{modifier.AttackedServerId}'.");
                }

                if (modifier.Delta <= 0)
                {
                    Add(violations, "run.breach-modifier-nonpositive", $"{modifierPath}.delta", "Breach modifier delta must be positive.");
                }

                if (!StableIdentifier.TryValidate(modifier.ReasonKey))
                {
                    Add(violations, "id.invalid", $"{modifierPath}.reasonKey", "Breach modifier reason key is empty or invalid.");
                }

                switch (modifier.SourceKind)
                {
                    case RunBreachAccessModifierSourceKind.InstalledRunSource:
                        if (!installedRunSource)
                        {
                            Add(
                                violations,
                                run.ActiveRunEventId.HasValue
                                    ? "run.breach-modifier-run-event"
                                    : "run.breach-modifier-no-source",
                                modifierPath,
                                "Legacy breach access modifiers require an installed Runner run source and are not valid on basic or run-event runs.");
                        }
                        else if (run.InitiatingSource is { } sourceId && modifier.SourceCardId != sourceId)
                        {
                            Add(
                                violations,
                                "run.breach-modifier-source-mismatch",
                                $"{modifierPath}.sourceCardId",
                                "Every legacy breach access modifier must identify the run's installed initiating source.");
                        }

                        if (source is not null
                            && attackedServer is not null
                            && (!behaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)
                                || !BehaviorRunRules.HasBreachAccessModifierDeclaration(
                                    definition,
                                    attackedServer.Kind,
                                    modifier.ReasonKey,
                                    modifier.Delta)))
                        {
                            Add(
                                violations,
                                "run.breach-modifier-undeclared",
                                modifierPath,
                                "Legacy breach access modifiers must match the installed source's declared reason and positive counter scale.");
                        }
                        break;

                    case RunBreachAccessModifierSourceKind.FirstHeadquartersBreach:
                        if (attackedServer?.Kind != ServerKind.Headquarters)
                        {
                            Add(
                                violations,
                                "run.breach-modifier-first-hq-server",
                                modifierPath,
                                "First-HQ breach access modifiers require an attacked Headquarters server.");
                        }

                        if (!isFirstHqBreach)
                        {
                            Add(
                                violations,
                                "run.breach-modifier-first-hq-occurrence",
                                modifierPath,
                                "First-HQ breach access modifiers require exactly the first HQ occurrence in the active formal turn.");
                        }

                        if (source is not null
                            && (!behaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? firstHqDefinition)
                                || !BehaviorRunRules.HasFirstHqBreachAccessModifierDeclaration(
                                    firstHqDefinition,
                                    modifier.ReasonKey,
                                    modifier.Delta)))
                        {
                            Add(
                                violations,
                                "run.breach-modifier-undeclared",
                                modifierPath,
                                "First-HQ breach access modifiers must match the source's exact declared reason and positive delta.");
                        }

                        if (hasCurrentFormalFacts
                            && activeFacts!.GetFirstXCount(modifier.SourceCardId, modifier.ReasonKey) != 1)
                        {
                            Add(
                                violations,
                                "run.breach-modifier-first-hq-unconsumed",
                                modifierPath,
                                "Every first-HQ breach access modifier must consume exactly one matching FirstX source key.");
                        }
                        break;

                    case RunBreachAccessModifierSourceKind.BehaviorEffect:
                        if (source is not null
                            && (!behaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? effectDefinition)
                                || !BehaviorRunRules.HasBehaviorBreachAccessDeclaration(
                                    effectDefinition,
                                    modifier.ReasonKey,
                                    modifier.Delta)))
                        {
                            Add(
                                violations,
                                "run.breach-modifier-undeclared",
                                modifierPath,
                                "Behavior breach access modifiers must match a declared additional-access instruction of the source.");
                        }
                        break;

                    case RunBreachAccessModifierSourceKind.RunEvent:
                        if (run.ActiveRunEventId is not { } activeRunEventId)
                        {
                            Add(
                                violations,
                                "run.breach-modifier-run-event",
                                modifierPath,
                                "Run-event breach access modifiers require an active run event source.");
                        }
                        else if (modifier.SourceCardId != activeRunEventId)
                        {
                            Add(
                                violations,
                                "run.breach-modifier-source-mismatch",
                                $"{modifierPath}.sourceCardId",
                                "Every run-event breach access modifier must identify the active run event source.");
                        }

                        if (source is not null
                            && (!behaviorCatalog.TryGetRunEvent(
                                    source.DefinitionId,
                                    out RunEventBehaviorSpec? runEventSpec)
                                || runEventSpec.BreachAccessModifier is not { } declared
                                || declared.Delta != modifier.Delta
                                || !string.Equals(
                                    declared.ReasonKey,
                                    modifier.ReasonKey,
                                    StringComparison.Ordinal)))
                        {
                            Add(
                                violations,
                                "run.breach-modifier-undeclared",
                                modifierPath,
                                "Run-event breach access modifiers must match the active event's exact declaration.");
                        }

                        if (source is not null
                            && !source.FaceUp)
                        {
                            Add(
                                violations,
                                "run.breach-modifier-source-facedown",
                                modifierPath,
                                "A run-event breach access modifier source must remain faceup while the run is active.");
                        }
                        break;

                    default:
                        Add(
                            violations,
                            "run.breach-modifier-source-kind",
                            $"{modifierPath}.sourceKind",
                            "Breach access modifiers must use a supported provenance source kind.");
                        break;
                }
            }

        }

        if (run.ActiveRunEventId is { } activeEventForModifier
            && state.Cards.FirstOrDefault(card => card?.Id == activeEventForModifier) is { } eventSourceForModifier
            && behaviorCatalog.TryGetRunEvent(eventSourceForModifier.DefinitionId, out RunEventBehaviorSpec? eventSpecForModifier)
            && eventSpecForModifier.BreachAccessModifier is { } declaredEventModifier
            && (run.BreachAccessModifiers is null
                || !run.BreachAccessModifiers.Any(modifier => modifier is not null
                    && modifier.SourceKind == RunBreachAccessModifierSourceKind.RunEvent
                    && modifier.SourceCardId == activeEventForModifier
                    && modifier.AttackedServerId == run.AttackedServer
                    && modifier.Delta == declaredEventModifier.Delta
                    && string.Equals(
                        modifier.ReasonKey,
                        declaredEventModifier.ReasonKey,
                        StringComparison.Ordinal))))
        {
            Add(
                violations,
                "run.missing-breach-access-modifier",
                $"{path}.breachAccessModifiers",
                "The active run event's declared breach access modifier must be snapshotted into the run state.");
        }

        ValidateRunRandomAccessLimit(state, run, path, violations);

        ValidateRunRezCostModifiers(state, behaviorCatalog, run, path, violations);

        ValidateResolvedSubroutineLedger(state, behaviorCatalog, run, path, violations);

        ValidateRunRestrictions(state, behaviorCatalog, run, path, violations);

        ValidateRunBehaviorRestrictions(state, behaviorCatalog, run, path, violations);

        ValidateRunGainedSubroutines(state, behaviorCatalog, run, path, violations);

        if (!installedRunSource && run.InitiatingSource != run.ActiveRunEventId)
        {
            Add(
                violations,
                "run.active-event-source-mismatch",
                $"{path}.activeRunEventId",
                "InitiatingSource and ActiveRunEventId must identify the same card unless the source is an installed click-run card.");
        }

        if (installedRunSource)
        {
            CardInstanceId sourceId = run.InitiatingSource!.Value;
            CardInstanceState? source = state.Cards.FirstOrDefault(card => card?.Id == sourceId);
            ZoneState? sourceZone = source is null
                ? null
                : state.Zones.FirstOrDefault(zone => zone?.Id == source.ZoneId);
            bool validInstalledSource = source is not null
                && source.OwnerSide == PlayerSide.Runner
                && sourceZone is not null
                && sourceZone.CardIds.Contains(sourceId)
                && behaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? sourceDefinition)
                && (state.Servers.FirstOrDefault(server => server?.Id == run.AttackedServer)?.Kind ?? (ServerKind)(-1)) is var attackedKind
                // As for a run event: the run may have been moved to Archives (Proprionegation).
                && (BehaviorRunRules.DefinitionHasStartRunForServer(sourceDefinition!, attackedKind) || attackedKind == ServerKind.Archives);
            if (!validInstalledSource)
            {
                Add(
                    violations,
                    "run.installed-source-invalid",
                    $"{path}.initiatingSource",
                    "A click-run source must remain a physical Runner-owned card with a matching click StartRun declaration for the attacked server.");
            }

            if (state.Zones.Any(zone => zone?.Kind == ZoneKind.RunnerPlayArea && zone.CardIds.Count > 0))
            {
                Add(
                    violations,
                    "run.play-area-without-event",
                    "zones",
                    "An installed click-run source must not populate the Runner play area.");
            }
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

                RunEventBehaviorSpec? activeRunEventSpec = null;
                if (!behaviorCatalog.TryGetRunEvent(activeEvent.DefinitionId, out activeRunEventSpec))
                {
                    Add(
                        violations,
                        "run.active-event-unregistered",
                        $"{path}.activeRunEventId",
                        $"Active run event definition '{activeEvent.DefinitionId}' is not registered as a run event.");
                }
                else
                {
                    ServerKind? attackedKind = state.Servers
                        .FirstOrDefault(candidate => candidate?.Id == run.AttackedServer)
                        ?.Kind;
                    // The attacked server may also have changed during the run: to the
                    // event's own redirect (Maintenance Access: HQ), or to Archives by a
                    // Runner move (Proprionegation).
                    if (activeRunEventSpec.AllowedTargetServers is { } allowed
                        && (attackedKind is null
                            || !allowed.Contains(attackedKind.Value)
                                && attackedKind != activeRunEventSpec.RedirectApproachTo
                                && attackedKind != ServerKind.Archives))
                    {
                        Add(
                            violations,
                            "run.active-event-target-invalid",
                            $"{path}.attackedServer",
                            "The active run event does not allow the run's attacked server.");
                    }

                    if (activeRunEventSpec.SuccessDraw is { } successDraw
                        && (!behaviorCatalog.TryGet(activeEvent.DefinitionId, out CardBehaviorDefinition? successDefinition)
                            || !BehaviorRunRules.IsRunEventSuccessDrawDefinition(successDefinition, successDraw)))
                    {
                        Add(
                            violations,
                            "run.active-event-success-draw-undeclared",
                            $"{path}.activeRunEventId",
                            "A run event success draw requires an exact authenticated generic draw binding.");
                    }
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
                if (!installedRunSource)
                {
                    Add(
                        violations,
                        "run.play-area-without-event",
                        $"zones[{populatedPlayArea!.Id}].cardIds",
                        "A populated Runner play area requires ActiveRunEventId.");
                }
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
            bool removedEncounterCursor = IsRemovedEncounterCursor(state, run, iceZone);
            // A destroyed remote retains the departed ICE cursor only while
            // its paid window/effect stack unwinds. It is provenance, not a
            // remaining installed protector or a legal interaction target.
            bool destroyedCursor = server is { Lifecycle: ServerLifecycle.Destroyed }
                && run.CurrentIceIndex >= 0
                && run.CurrentIceId is { } departedIce
                && state.Cards.Any(card => card.Id == departedIce
                    && card.OwnerSide == PlayerSide.Corp && card.ZoneId != iceZone.Id);
            if (!destroyedCursor && (run.CurrentIceIndex < -1 || run.CurrentIceIndex >= iceZone.CardIds.Count))
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
            // An index already reported as out of range can't be compared with the zone.
            else if (!removedEncounterCursor && !destroyedCursor && run.CurrentIceIndex < iceZone.CardIds.Count
                && (!run.CurrentIceId.HasValue || iceZone.CardIds[run.CurrentIceIndex] != run.CurrentIceId.Value))
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

            // A run-expiring effect with a physical source is the
            // icebreaker run-end-cleanup contract. Keep the source identity
            // authoritative and cross-check the behavior ID against the
            // injected catalog; a caller cannot forge a cleanup by merely
            // placing a source card and arbitrary string in RunState.
            // EL-STRENGTH-PERSISTENCE: a pump that lasts the run (GAMEDRAGON™ Pro)
            // is not a cleanup; its breaker hosts the mod that makes it last.
            // The pump's duration is fixed when it is made, so it stays if the mod leaves (EL.8); it ends when its
            // breaker leaves play.
            bool persistentPump = effect.Expiry == RunLingeringEffectExpiry.Run
                && effect.BehaviorId == EncounterStrength.IcebreakerPumpBehavior
                && effect.ValueAdjustment is null
                && effect.SourceCardId is { } pumpedBreaker
                && state.Cards.FirstOrDefault(card => card?.Id == pumpedBreaker) is { } breaker
                && state.Zones.Any(zone => zone?.Id == breaker.ZoneId && zone.Kind == ZoneKind.RunnerRig)
                && behaviorCatalog.TryGetIcebreaker(breaker.DefinitionId, out _);
            if (effect.Expiry == RunLingeringEffectExpiry.Run
                && !persistentPump
                && effect.SourceCardId is { } cleanupSourceId)
            {
                if (effect.Kind != RunLingeringEffectKind.IcebreakerRunEndCleanup)
                {
                    Add(
                        violations,
                        "run.cleanup-kind-missing",
                        $"{effectPath}.kind",
                        "A run-end cleanup with a physical source must carry the typed icebreaker cleanup kind.");
                }

                CardInstanceState? cleanupSource = state.Cards.FirstOrDefault(card => card?.Id == cleanupSourceId);
                if (cleanupSource is null)
                {
                    Add(
                        violations,
                        "run.cleanup-source-missing",
                        $"{effectPath}.sourceCardId",
                        $"Run-end cleanup source '{cleanupSourceId}' is not present in state.");
                }
                else if (cleanupSource.OwnerSide != PlayerSide.Runner
                    || !behaviorCatalog.TryGetIcebreaker(cleanupSource.DefinitionId, out IcebreakerBehaviorSpec? cleanupBreaker)
                    || cleanupBreaker.RunEndCleanup is not { } cleanupDeclaration
                    || cleanupDeclaration.BehaviorId != effect.BehaviorId)
                {
                    Add(
                        violations,
                        "run.cleanup-provenance",
                        $"{effectPath}.behaviorId",
                        "A run-end cleanup must match the exact run-end declaration of its Runner icebreaker source.");
                }
            }

            if (effect.ValueAdjustment is { } adjustment)
            {
                if (effect.Expiry != RunLingeringEffectExpiry.Encounter)
                {
                    Add(
                        violations,
                        "run.value-adjustment-expiry",
                        $"{effectPath}.valueAdjustment",
                        "Typed value adjustments must expire at encounter end.");
                }

                if (effect.SourceCardId != adjustment.SourceCardId)
                {
                    Add(
                        violations,
                        "run.value-adjustment-source",
                        $"{effectPath}.valueAdjustment.sourceCardId",
                        "A typed value adjustment source must match its enclosing effect source.");
                }

                if (!state.Cards.Any(card => card.Id == adjustment.SourceCardId))
                {
                    Add(
                        violations,
                        "run.value-adjustment-source-missing",
                        $"{effectPath}.valueAdjustment.sourceCardId",
                        $"Typed value adjustment source '{adjustment.SourceCardId}' is not present in state.");
                }

                if (!state.Cards.Any(card => card.Id == adjustment.TargetCardId))
                {
                    Add(
                        violations,
                        "run.value-adjustment-target-missing",
                        $"{effectPath}.valueAdjustment.targetCardId",
                        $"Typed value adjustment target '{adjustment.TargetCardId}' is not present in state.");
                }

                bool initializedEncounter = (run.Step is
                    RunTimingStep.Encounter or RunTimingStep.EncounterPaidWindow)
                    && run.Encounter is { Status: RunEncounterStatus.Initialized };
                bool targetMatchesEncounter = adjustment.Source switch
                {
                    // Encounter-scoped effects (for example a successful-run
                    // Leech reduction) always target the exact ICE that
                    // initialized this encounter. Their source may move after
                    // activation without changing the target.
                    BehaviorValueSourceKind.EncounterScoped => initializedEncounter
                        && run.Encounter!.IceId == adjustment.TargetCardId,
                    // A dynamic pump snapshots onto its activating breaker;
                    // an active Runner Rig breaker is the only executable
                    // target for this realized variable adjustment.
                    BehaviorValueSourceKind.Variable => initializedEncounter
                        && adjustment.SourceCardId == adjustment.TargetCardId
                        && state.Cards.FirstOrDefault(card => card.Id == adjustment.TargetCardId) is { } targetCard
                        && state.Zones.FirstOrDefault(zone => zone.Id == targetCard.ZoneId) is
                        { Kind: ZoneKind.RunnerRig, OwnerSide: PlayerSide.Runner } targetZone
                        && targetZone.CardIds.Contains(adjustment.TargetCardId)
                        && behaviorCatalog.TryGetIcebreaker(targetCard.DefinitionId, out _),
                    _ => false,
                };
                if (!targetMatchesEncounter)
                {
                    Add(
                        violations,
                        "run.value-adjustment-target",
                        $"{effectPath}.valueAdjustment.targetCardId",
                        "A typed run value adjustment must target the current initialized encounter ICE or its active breaker.");
                }

                if (adjustment.Source == BehaviorValueSourceKind.EncounterScoped)
                {
                    ValidateCatalogEncounterStrengthAdjustment(
                        state,
                        behaviorCatalog,
                        effect,
                        adjustment,
                        effectPath,
                        violations);
                }
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

        int runFrameCount = state.ResolutionStack.Count(frame => frame switch
        {
            RunTimingFrame timing => timing.RunId == run.Id,
            IceSubroutineFrame subroutine => subroutine.RunId == run.Id,
            _ => false,
        });
        if (runFrameCount != 1)
        {
            Add(violations, "run.frame-count", "resolutionStack", $"Live run '{run.Id}' requires exactly one matching RunTimingFrame; found {runFrameCount}.");
        }

        ValidateRunStackTopology(state, run, behaviorCatalog, violations);

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

    private static void ValidateCatalogEncounterStrengthAdjustment(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        RunLingeringEffect effect,
        RunValueAdjustment adjustment,
        string effectPath,
        List<StateInvariantViolation> violations)
    {
        if (!BehaviorEncounterStrengthContracts.HasEffectPrefix(effect.BehaviorId))
        {
            Add(
                violations,
                "run.value-adjustment-provenance-behavior",
                $"{effectPath}.behaviorId",
                "An encounter strength adjustment must carry a catalog behavior identity.");
            return;
        }

        CardInstanceState? source = state.Cards.FirstOrDefault(card => card?.Id == adjustment.SourceCardId);
        if (source is null)
        {
            Add(
                violations,
                "run.value-adjustment-provenance-definition",
                $"{effectPath}.valueAdjustment.sourceCardId",
                "A catalog encounter strength adjustment source card must remain present for provenance reconstruction.");
            return;
        }

        if (!behaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition))
        {
            Add(
                violations,
                "run.value-adjustment-provenance-definition",
                $"{effectPath}.behaviorId",
                "A catalog encounter strength adjustment requires its source definition in the injected catalog.");
            return;
        }

        bool hasEncounterStrengthAbility = definition.PaidAbilities.Any(static ability =>
            ability.Instructions.Any(static instruction =>
                instruction.Kind == BehaviorInstructionKind.ModifyEncounteredIceStrength));
        if (!hasEncounterStrengthAbility)
        {
            Add(
                violations,
                "run.value-adjustment-provenance-behavior",
                $"{effectPath}.behaviorId",
                "A catalog encounter strength adjustment must identify a definition with a typed encounter-strength ability.");
            return;
        }

        if (source.OwnerSide != PlayerSide.Runner
            || definition.Controller != source.OwnerSide)
        {
            Add(
                violations,
                "run.value-adjustment-provenance-controller",
                $"{effectPath}.valueAdjustment.sourceCardId",
                "A catalog encounter strength adjustment source must be a Runner card whose definition controller matches its owner.");
        }

        if (state.RunState?.Encounter?.IceId != adjustment.TargetCardId)
        {
            Add(
                violations,
                "run.value-adjustment-provenance-target",
                $"{effectPath}.valueAdjustment.targetCardId",
                "A catalog encounter strength adjustment must target the current encounter ICE.");
            return;
        }

        BehaviorPaidAbility[] matchingAbilities = definition.PaidAbilities
            .Where(ability => string.Equals(
                BehaviorEncounterStrengthContracts.EffectBehaviorId(definition.DefinitionId, ability.Key),
                effect.BehaviorId,
                StringComparison.Ordinal))
            .ToArray();
        if (matchingAbilities.Length != 1)
        {
            Add(
                violations,
                "run.value-adjustment-provenance-behavior",
                $"{effectPath}.behaviorId",
                "A catalog encounter strength adjustment must identify exactly one declared paid ability.");
            return;
        }

        BehaviorPaidAbility ability = matchingAbilities[0];
        if (!BehaviorEncounterStrengthContracts.MatchesEffect(
                definition,
                ability.Key,
                effect.BehaviorId,
                adjustment,
                out _,
                out BehaviorInstruction? instruction)
            || instruction is null)
        {
            Add(
                violations,
                "run.value-adjustment-provenance-declaration",
                $"{effectPath}.valueAdjustment",
                "A catalog encounter strength adjustment must match its paid-ability timing, instruction, delta, and reason.");
        }
    }

    private static void ValidateBehaviorEncounterStrengthInstruction(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        BehaviorResolutionFrame frame,
        BehaviorPaidAbility? abilityDeclaration,
        IReadOnlyList<BehaviorInstruction> instructions,
        int frameIndex,
        string path,
        List<StateInvariantViolation> violations)
    {
        BehaviorInstruction[] modifiers = instructions
            .Where(static instruction =>
                BehaviorEncounterStrengthContracts.IsStrengthInstruction(instruction.Kind))
            .ToArray();
        if (modifiers.Length == 0)
        {
            return;
        }

        bool validAbility = false;
        if (frame.AbilityKey is not null
            && frame.TriggerKey is null
            && abilityDeclaration is not null
            && modifiers.Length == 1
            && behaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition)
            && BehaviorEncounterStrengthContracts.TryGetExecutableInstruction(
                definition,
                frame.AbilityKey,
                out _,
                out BehaviorInstruction? declaredInstruction)
            && declaredInstruction is not null)
        {
            validAbility = BehaviorContractStructure.AreEqual(modifiers[0], declaredInstruction);
        }

        if (!validAbility)
        {
            Add(
                violations,
                "resolution.behavior-encounter-strength-instruction",
                path,
                "Encountered-ICE strength instructions must be the exact single instruction of a Runner encounter-paid ability.");
            return;
        }

        RunState? run = state.RunState;
        bool validRun = run is
        {
            Step: RunTimingStep.EncounterPaidWindow,
            Priority: { PriorityHolder: PlayerSide.Runner },
            Encounter: { Status: RunEncounterStatus.Initialized },
        };
        bool validParent = frameIndex >= 2
            && state.ResolutionStack[frameIndex - 1] is AbilityFrame abilityParent
            && state.ResolutionStack[frameIndex - 2] is RunTimingFrame runFrame
            && abilityParent.Stage == AbilityLifecycleStage.AwaitingChild
            && abilityParent.SourceCardId == frame.SourceCardId
            && abilityParent.DefinitionId == frame.DefinitionId
            && abilityParent.Controller == frame.Controller
            && run is not null
            && runFrame.RunId == run.Id
            && runFrame.Step == run.Step;
        PlayerState? controller = state.Players.FirstOrDefault(player => player?.Id == frame.Controller);
        CardInstanceState? source = state.Cards.FirstOrDefault(card => card?.Id == frame.SourceCardId);
        bool validSource = source is not null
            && source.DefinitionId == frame.DefinitionId
            && source.OwnerSide == PlayerSide.Runner
            && controller is not null
            && source.OwnerSide == controller.Side;
        CardInstanceState? target = run?.Encounter is { } encounter
            ? state.Cards.FirstOrDefault(card => card?.Id == encounter.IceId)
            : null;
        bool validTarget = target is not null
            && behaviorCatalog.TryGetIce(target.DefinitionId, out _);
        if (!validRun || !validParent || !validSource || !validTarget)
        {
            Add(
                violations,
                "resolution.behavior-encounter-strength-context",
                path,
                "Encountered-ICE strength instructions require the exact initialized encounter paid-window parent, Runner source/controller, and current ICE target.");
        }
    }

    private static void ValidateRunRandomAccessLimit(
        GameState state,
        RunState run,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (run.Step is not (RunTimingStep.BreachCandidateSelection
            or RunTimingStep.BreachAccessAbility
            or RunTimingStep.RunEnd))
        {
            return;
        }

        // A legacy hand-authored observation fixture may describe an
        // already-accessed hidden card without carrying modifier provenance.
        // There is no access arithmetic to reconstruct in that shape;
        // modifier-bearing runs are checked against the authoritative
        // hidden-pool cursor below.
        if (run.BreachAccessModifiers is null)
        {
            return;
        }

        ServerState? server = state.Servers.FirstOrDefault(candidate => candidate?.Id == run.AttackedServer);
        ZoneState? rootZone = server is null
            ? null
            : state.Zones.FirstOrDefault(zone => zone?.Id == server.RootZoneId);
        ZoneState? hiddenZone = server?.Kind switch
        {
            ServerKind.Headquarters => state.Zones.FirstOrDefault(zone =>
                zone?.Kind == ZoneKind.CorpHand
                && zone.OwnerSide == PlayerSide.Corp
                && zone.ServerId is null),
            ServerKind.RAndD => state.Zones.FirstOrDefault(zone =>
                zone?.Kind == ZoneKind.CorpDeck
                && zone.OwnerSide == PlayerSide.Corp
                && zone.ServerId is null),
            _ => null,
        };

        // The access cursor is decremented when a hidden candidate is chosen,
        // while the chosen card normally remains in the hidden zone until the
        // mid-access window resolves. If that window moves the card (for
        // example, an agenda is stolen into RunnerScore), reconstructing from
        // the current hidden-zone count alone loses the card's origin. A
        // chosen card that is still in the attacked root is definitely a root
        // candidate and did not consume the hidden cursor. A chosen card that
        // left both pools is ambiguous: it may have been a root candidate or
        // a hidden candidate. Keep that uncertainty explicit and validate the
        // authoritative cursor against every feasible origin assignment.
        int hiddenCount = hiddenZone?.CardIds.Count ?? 0;
        HashSet<CardInstanceId> hiddenIds = hiddenZone is null
            ? []
            : hiddenZone.CardIds.ToHashSet();
        HashSet<CardInstanceId> rootIds = rootZone is null
            ? []
            : rootZone.CardIds.ToHashSet();
        int hiddenChosen = run.ChosenAccessCards.Count(hiddenIds.Contains);
        int movedOutOfPools = hiddenZone is null
            ? 0
            : run.ChosenAccessCards.Count(cardId =>
                !hiddenIds.Contains(cardId) && !rootIds.Contains(cardId));

        long additionalRandomAccess = 0;
        if (run.BreachAccessModifiers is { } modifiers)
        {
            foreach (RunBreachAccessModifier? modifier in modifiers)
            {
                if (modifier is not null && modifier.AttackedServerId == run.AttackedServer)
                {
                    additionalRandomAccess += modifier.Delta;
                }
            }
        }

        static long ExpectedCursor(
            long currentHiddenCount,
            long hiddenChosenCount,
            long movedHiddenCount,
            long additionalAccess)
        {
            long originalHiddenCount = checked(currentHiddenCount + movedHiddenCount);
            long requested = (originalHiddenCount > 0 ? 1L : 0L) + additionalAccess;
            long initial = Math.Min(originalHiddenCount, Math.Max(0L, requested));
            return Math.Max(0L, initial - hiddenChosenCount - movedHiddenCount);
        }

        long expectedAtMinimum = ExpectedCursor(hiddenCount, hiddenChosen, 0, additionalRandomAccess);
        long expectedAtMaximum = ExpectedCursor(
            hiddenCount,
            hiddenChosen,
            movedOutOfPools,
            additionalRandomAccess);
        // EL.8: a card effect resolved during the breach may take unaccessed hidden cards away (Poétrï installing
        // from HQ); the engine clamps the cursor to the unaccessed cards left.
        long availableHidden = hiddenZone is null ? 0 : RunState.AvailableHiddenBreachCards(hiddenZone, run.AccessedCards).Count;
        long expectedMinimum = Math.Min(Math.Min(expectedAtMinimum, expectedAtMaximum), availableHidden);
        long expectedMaximum = Math.Max(expectedAtMinimum, expectedAtMaximum);
        if (run.RandomAccessLimit < expectedMinimum || run.RandomAccessLimit > expectedMaximum)
        {
            string expected = expectedMinimum == expectedMaximum
                ? expectedMinimum.ToString(CultureInfo.InvariantCulture)
                : $"[{expectedMinimum}, {expectedMaximum}]";
            Add(
                violations,
                "run.random-access-limit-mismatch",
                $"{path}.randomAccessLimit",
                $"Random access limit {run.RandomAccessLimit} does not match the authoritative breach reconstruction {expected}.");
        }
    }

    private static void ValidateRunRezCostModifiers(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        RunState run,
        string path,
        List<StateInvariantViolation> violations)
    {
        IReadOnlyList<RunRezCostModifier>? modifiers = run.RezCostModifiers;
        CardInstanceId? expectedSourceId = run.ActiveRunEventId;
        CardInstanceState? source = expectedSourceId is { } sourceId
            ? state.Cards.FirstOrDefault(card => card?.Id == sourceId)
            : null;
        RunEventBehaviorSpec? sourceSpec = source is not null
            && behaviorCatalog.TryGetRunEvent(source.DefinitionId, out RunEventBehaviorSpec? registered)
            ? registered
            : null;
        IReadOnlyList<BehaviorRunRezCostModifier> declarations = sourceSpec?.RezCostModifiers ?? [];

        if (modifiers is null)
        {
            if (declarations.Count > 0)
            {
                Add(
                    violations,
                    "run.missing-rez-cost-modifiers",
                    $"{path}.rezCostModifiers",
                    "The active run event's declared rez-cost modifiers must be snapshotted into the run state.");
            }

            return;
        }

        if (expectedSourceId is null)
        {
            Add(
                violations,
                "run.rez-cost-modifier-no-event",
                $"{path}.rezCostModifiers",
                "Run rez-cost modifiers require the active run event that declared them.");
        }

        var seen = new HashSet<(CardDefinitionId Definition, CardInstanceId Card, int Delta, string Reason)>();
        for (int index = 0; index < modifiers.Count; index++)
        {
            RunRezCostModifier? modifier = modifiers[index];
            string modifierPath = $"{path}.rezCostModifiers[{index}]";
            if (modifier is null)
            {
                Add(
                    violations,
                    "run.null-rez-cost-modifier",
                    modifierPath,
                    "Run rez-cost modifiers must not contain null entries.");
                continue;
            }

            if (!seen.Add((modifier.SourceDefinitionId, modifier.SourceCardId, modifier.Delta, modifier.ReasonKey)))
            {
                Add(
                    violations,
                    "run.duplicate-rez-cost-modifier",
                    modifierPath,
                    "Run rez-cost modifiers must not repeat a source, delta, and reason.");
            }

            if (expectedSourceId is { } activeSourceId && modifier.SourceCardId != activeSourceId)
            {
                Add(
                    violations,
                    "run.rez-cost-modifier-source-mismatch",
                    $"{modifierPath}.sourceCardId",
                    "Every run rez-cost modifier must identify the active run event source.");
            }

            if (source is not null && modifier.SourceDefinitionId != source.DefinitionId)
            {
                Add(
                    violations,
                    "run.rez-cost-modifier-definition-mismatch",
                    $"{modifierPath}.sourceDefinitionId",
                    "Every run rez-cost modifier must identify the active run event definition.");
            }

            if (sourceSpec is null
                || !declarations.Any(declaration => declaration.Delta == modifier.Delta
                    && string.Equals(declaration.ReasonKey, modifier.ReasonKey, StringComparison.Ordinal)))
            {
                Add(
                    violations,
                    "run.rez-cost-modifier-undeclared",
                    modifierPath,
                    "Run rez-cost modifiers must match a declaration on the active run event source.");
            }
        }

        foreach (BehaviorRunRezCostModifier declaration in declarations)
        {
            if (!modifiers.Any(modifier => modifier is not null
                && modifier.SourceDefinitionId == (source?.DefinitionId ?? default)
                && modifier.SourceCardId == (expectedSourceId ?? default)
                && modifier.Delta == declaration.Delta
                && string.Equals(modifier.ReasonKey, declaration.ReasonKey, StringComparison.Ordinal)))
            {
                Add(
                    violations,
                    "run.missing-rez-cost-modifier",
                    $"{path}.rezCostModifiers",
                    $"Run rez-cost modifier declaration '{declaration.ReasonKey}' is missing from the run snapshot.");
            }
        }
    }

    /// <summary>
    /// CR 8.5.10 permits a normal ICE install to trash the ICE currently being
    /// encountered. The run then retains a conceptual numeric boundary while
    /// the completed encounter's physical source is absent from the attacked
    /// ICE zone. Movement decrements this boundary to approach the next
    /// retained inward ICE; the newly installed ICE is outermost and is not
    /// revisited.
    /// </summary>
    private static bool IsRemovedEncounterCursor(
        GameState state,
        RunState run,
        ZoneState iceZone)
    {
        return run.CurrentIceId is null
            && run.CurrentIceIndex >= 0
            && run.Step is RunTimingStep.MovementPaidWindow
                or RunTimingStep.JackOutDecision
                or RunTimingStep.MoveInward
            && run.Encounter is { Status: RunEncounterStatus.Complete } encounter
            && !iceZone.CardIds.Contains(encounter.IceId)
            && state.Cards.Any(card => card.Id == encounter.IceId
                && card.OwnerSide == PlayerSide.Corp
                && card.ZoneId != iceZone.Id);
    }

    /// <summary>
    /// VP-CANNOT-STEAL-OR-TRASH: a card ability's restriction keeps its source card, with the catalogued definition, and
    /// names an existing definition when it names one; it never repeats.
    /// </summary>
    private static void ValidateRunBehaviorRestrictions(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        RunState run,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (run.BehaviorRestrictions is null)
        {
            return;
        }

        string restrictionPath = $"{path}.behaviorRestrictions";
        if (run.BehaviorRestrictions.Count == 0)
        {
            Add(violations, "run.behavior-restrictions-empty", restrictionPath, "An empty behavior restriction list is omitted.");
        }

        if (run.BehaviorRestrictions.Distinct().Count() != run.BehaviorRestrictions.Count)
        {
            Add(violations, "run.duplicate-behavior-restriction", restrictionPath, "Behavior restrictions must not repeat.");
        }

        foreach (RunBehaviorRestriction restriction in run.BehaviorRestrictions)
        {
            bool sourceKnown = state.Cards.Any(card => card?.Id == restriction.SourceCardId
                    && card.DefinitionId == restriction.SourceDefinitionId)
                && (behaviorCatalog.TryGet(restriction.SourceDefinitionId, out _)
                    || behaviorCatalog.TryGetIce(restriction.SourceDefinitionId, out _));
            bool namedKnown = restriction.NamedDefinitionId is not { } named
                || state.Cards.Any(card => card?.DefinitionId == named && card.OwnerSide == PlayerSide.Corp);
            if (!sourceKnown || !namedKnown)
            {
                Add(
                    violations,
                    "run.behavior-restriction-source-invalid",
                    restrictionPath,
                    "A behavior restriction must keep its catalogued source card, and name only a Corp card's definition.");
            }
        }
    }

    /// <summary>
    /// VP-ICE-GAINS-SUBROUTINE: each gained subroutine names a source that declares one, never repeats, and in the live
    /// encounter sits past the printed ordinals; every live ordinal past them is a gained one.
    /// </summary>
    private static void ValidateRunGainedSubroutines(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        RunState run,
        string path,
        List<StateInvariantViolation> violations)
    {
        string gainedPath = $"{path}.gainedSubroutines";
        IReadOnlyList<RunGainedSubroutine> gained = run.GainedSubroutines ?? [];
        if (run.GainedSubroutines is { Count: 0 })
        {
            Add(violations, "run.gained-subroutines-empty", gainedPath, "An empty gained subroutine list is omitted.");
        }

        if (gained.Select(static value => (value.EncounterId, value.Ordinal)).Distinct().Count() != gained.Count)
        {
            Add(violations, "run.duplicate-gained-subroutine", gainedPath, "A gained subroutine's encounter ordinal is unique.");
        }

        foreach (RunGainedSubroutine entry in gained)
        {
            if (!state.Cards.Any(card => card?.Id == entry.SourceCardId && card.DefinitionId == entry.SourceDefinitionId)
                || !behaviorCatalog.TryGet(entry.SourceDefinitionId, out CardBehaviorDefinition? source)
                || source.GainedIceSubroutine is null)
            {
                Add(violations, "run.gained-subroutine-source-invalid", gainedPath, "A gained subroutine keeps a source that declares it.");
            }
        }

        if (run.Encounter is not { } encounter
            || state.Cards.FirstOrDefault(card => card?.Id == encounter.IceId) is not { } ice
            || !behaviorCatalog.TryGetIce(ice.DefinitionId, out IceBehaviorSpec? iceSpec))
        {
            return;
        }

        foreach (RunSubroutineState? subroutine in encounter.Subroutines)
        {
            if (subroutine is null)
            {
                continue;
            }

            bool isGained = gained.Any(value => value.EncounterId == encounter.Id && value.Ordinal == subroutine.Ordinal);
            if (isGained == subroutine.Ordinal < iceSpec.Subroutines.Count)
            {
                Add(violations, "run.gained-subroutine-ordinal", gainedPath, "Only gained subroutines sit past the printed ordinals.");
            }
        }
    }

    private static void ValidateRunRestrictions(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        RunState run,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (run.RunRestrictions is null)
        {
            return;
        }

        const string cannotStealOrTrashReason = "ice-subroutine:cannot-steal-or-trash-corp-cards";
        var seen = new HashSet<(RunRestrictionKind Kind, CardInstanceId Ice, SubroutineId Subroutine, int Ordinal)>();
        foreach (RunRestriction? restriction in run.RunRestrictions)
        {
            string restrictionPath = $"{path}.runRestrictions";
            if (restriction is null)
            {
                Add(violations, "run.null-restriction", restrictionPath, "Run restrictions must not contain null entries.");
                continue;
            }

            if (!seen.Add((restriction.Kind, restriction.SourceIceId, restriction.SourceSubroutineId, restriction.SourceOrdinal)))
            {
                Add(violations, "run.duplicate-restriction", restrictionPath, "Run restrictions must not repeat the same source subroutine.");
            }

            CardInstanceState? sourceIce = state.Cards.FirstOrDefault(card => card?.Id == restriction.SourceIceId);
            if (sourceIce is null
                || sourceIce.OwnerSide != PlayerSide.Corp
                || sourceIce.DefinitionId != restriction.SourceIceDefinitionId)
            {
                Add(
                    violations,
                    "run.restriction-source-invalid",
                    restrictionPath,
                    "A run restriction must retain an existing Corp physical source card with the authenticated ICE definition. The source may leave play after resolution.");
            }

            IceBehaviorSpec? sourceSpec = null;
            if (!behaviorCatalog.TryGetIce(restriction.SourceIceDefinitionId, out sourceSpec)
                || restriction.SourceOrdinal < 0
                || restriction.SourceOrdinal >= sourceSpec.Subroutines.Count
                || sourceSpec.Subroutines[restriction.SourceOrdinal] is not RunRestrictionSubroutineSpec declared
                || declared.Restriction != restriction.Kind)
            {
                Add(
                    violations,
                    "run.restriction-undeclared",
                    restrictionPath,
                    "A run restriction must match the exact typed subroutine declaration on its source ICE.");
            }

            string expectedEncounterId = $"encounter:{run.Id.Value}:{restriction.SourceIceId.Value}";
            string expectedSubroutineId = $"subroutine:{expectedEncounterId}:{restriction.SourceOrdinal}";
            if (!string.Equals(restriction.ReasonKey, cannotStealOrTrashReason, StringComparison.Ordinal))
            {
                Add(
                    violations,
                    "run.restriction-reason-invalid",
                    restrictionPath,
                    "A run restriction must retain the registered provenance reason for its typed declaration.");
            }

            if (!string.Equals(restriction.SourceSubroutineId.Value, expectedSubroutineId, StringComparison.Ordinal))
            {
                Add(
                    violations,
                    "run.restriction-subroutine-identity",
                    restrictionPath,
                    "A run restriction must identify the deterministic subroutine of its run-bound source encounter and ordinal.");
            }

            RunResolvedSubroutineLedgerEntry? ledgerEntry = run.ResolvedSubroutineLedger?.SingleOrDefault(entry =>
                entry.RunId == run.Id
                && entry.IceId == restriction.SourceIceId
                && entry.IceDefinitionId == restriction.SourceIceDefinitionId
                && entry.SubroutineId == restriction.SourceSubroutineId
                && entry.Ordinal == restriction.SourceOrdinal);
            if (ledgerEntry is null)
            {
                Add(
                    violations,
                    "run.restriction-ledger-missing",
                    restrictionPath,
                    "A run restriction must match an exact persisted ledger entry for an unbroken resolved subroutine.");
            }

            // The immutable identity above is the retained proof once the
            // source ICE has moved or left play. While that source is still
            // the current encounter, also authenticate the live encounter
            // and require the printed subroutine to have resolved.
            if (run.Encounter is { } encounter
                && encounter.IceId == restriction.SourceIceId
                && (encounter.Id.Value != expectedEncounterId
                    || encounter.Subroutines.SingleOrDefault(subroutine => subroutine.Id == restriction.SourceSubroutineId) is not { } liveSubroutine
                    || liveSubroutine.Ordinal != restriction.SourceOrdinal
                    || !liveSubroutine.Resolved))
            {
                Add(
                    violations,
                    "run.restriction-subroutine-invalid",
                    restrictionPath,
                    "A restriction sourced by the current encounter must identify its deterministic, resolved printed subroutine.");
            }
        }
    }

    private static void ValidateResolvedSubroutineLedger(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        RunState run,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (run.ResolvedSubroutineLedger is null)
        {
            return;
        }

        var seen = new HashSet<(EncounterId Encounter, CardInstanceId Ice, SubroutineId Subroutine)>();
        for (int index = 0; index < run.ResolvedSubroutineLedger.Count; index++)
        {
            RunResolvedSubroutineLedgerEntry? entry = run.ResolvedSubroutineLedger[index];
            string entryPath = $"{path}.resolvedSubroutineLedger[{index}]";
            if (entry is null)
            {
                Add(violations, "run.null-resolved-subroutine-ledger-entry", entryPath, "Resolved-subroutine ledger entries must not be null.");
                continue;
            }

            if (!seen.Add((entry.EncounterId, entry.IceId, entry.SubroutineId)))
            {
                Add(violations, "run.duplicate-resolved-subroutine-ledger-entry", entryPath, "Resolved-subroutine ledger entries must be unique per encounter, ICE, and subroutine.");
            }

            if (entry.Version != 1)
            {
                Add(violations, "run.resolved-subroutine-ledger-version", entryPath, "Resolved-subroutine ledger entry version must be 1.");
            }

            if (entry.RunId != run.Id)
            {
                Add(violations, "run.resolved-subroutine-ledger-run-mismatch", entryPath, "Resolved-subroutine ledger entries must belong to the active run.");
            }

            string expectedEncounterId = $"encounter:{run.Id.Value}:{entry.IceId.Value}";
            string expectedSubroutineId = $"subroutine:{expectedEncounterId}:{entry.Ordinal}";
            if (!string.Equals(entry.EncounterId.Value, expectedEncounterId, StringComparison.Ordinal)
                || !string.Equals(entry.SubroutineId.Value, expectedSubroutineId, StringComparison.Ordinal))
            {
                Add(violations, "run.resolved-subroutine-ledger-identity", entryPath, "Resolved-subroutine ledger entries must retain the deterministic encounter and subroutine identity for this run.");
            }

            CardInstanceState? sourceIce = state.Cards.FirstOrDefault(card => card?.Id == entry.IceId);
            if (sourceIce is null
                || sourceIce.OwnerSide != PlayerSide.Corp
                || sourceIce.DefinitionId != entry.IceDefinitionId)
            {
                Add(violations, "run.resolved-subroutine-ledger-source-invalid", entryPath, "Resolved-subroutine ledger entries must retain the original Corp ICE definition for their physical source.");
                continue;
            }

            if (!behaviorCatalog.TryGetIce(entry.IceDefinitionId, out IceBehaviorSpec? iceSpec)
                || run.EncounterSubroutineSpec(behaviorCatalog, iceSpec, entry.EncounterId, entry.Ordinal) is null)
            {
                Add(violations, "run.resolved-subroutine-ledger-declaration-invalid", entryPath, "Resolved-subroutine ledger entries must point at a declared ICE subroutine.");
                continue;
            }

            if (run.Encounter is { } encounter
                && encounter.Id == entry.EncounterId)
            {
                RunSubroutineState? live = encounter.Subroutines.SingleOrDefault(subroutine => subroutine.Id == entry.SubroutineId);
                if (live is null
                    || live.Ordinal != entry.Ordinal
                    || live.Broken
                    || !live.Resolved
                    || encounter.IceId != entry.IceId)
                {
                    Add(violations, "run.resolved-subroutine-ledger-live-mismatch", entryPath, "A ledger entry for the live encounter must match an unbroken resolved encounter subroutine.");
                }
            }
        }
    }

    private static void ValidateRunStackTopology(
        GameState state,
        RunState run,
        IBehaviorCatalog behaviorCatalog,
        List<StateInvariantViolation> violations)
    {
        int[] matchingRunFrameIndexes = state.ResolutionStack
            .Select((frame, index) => (frame, index))
            .Where(value => value.frame switch
            {
                RunTimingFrame timing => timing.RunId == run.Id && timing.Step == run.Step,
                IceSubroutineFrame subroutine => subroutine.RunId == run.Id && run.Step == RunTimingStep.Encounter,
                _ => false,
            })
            .Select(value => value.index)
            .ToArray();
        int runFrameIndex = matchingRunFrameIndexes.Length == 1
            ? matchingRunFrameIndexes[0]
            : -1;
        bool standalone = runFrameIndex == 0
            && state.ResolutionStack[0] is RunTimingFrame or IceSubroutineFrame;
        bool nested = runFrameIndex == 1
            && state.ResolutionStack[0] is TurnFrame turnFrame
            && state.ResolutionStack[1] is RunTimingFrame or IceSubroutineFrame;
        if (!standalone && !nested)
        {
            Add(
                violations,
                "run.stack-topology",
                "resolutionStack",
                "A live run must begin with one standalone timing/ICE frame or an exact Runner TurnFrame/timing-frame nesting.");
            return;
        }

        if (standalone && run.InitiatingSource is not null && run.ActiveRunEventId is null)
        {
            Add(
                violations,
                "run.standalone-installed-source",
                "runState.initiatingSource",
                "An installed click-run source may be nested only under its real Runner turn frame.");
        }

        bool hasSuffix = state.ResolutionStack.Count > runFrameIndex + 1;
        if (!hasSuffix)
        {
            // A nested run with no suspended ability still needs its parent
            // turn checks below; a standalone run is complete at this frame.
            if (!nested)
            {
                return;
            }
        }
        else
        {
            // A2b may suspend an ability directly beneath the live run timing
            // frame. Production occurrence hooks may also suspend the exact
            // typed gameplay occurrence checkpoint after advancing the run
            // cursor. The effect/checkpoint validators prove the nested
            // suffix; this check only ensures no unrelated frame can be
            // smuggled into a run stack prefix.
            ResolutionFrame suffixRoot = state.ResolutionStack[runFrameIndex + 1];
            bool expectedGameplayOccurrence = suffixRoot is EffectResolutionFrame occurrenceFrame
                && IsExpectedRunGameplayOccurrenceFrame(state, occurrenceFrame, run);
            bool expectedIceTag = suffixRoot is EffectResolutionFrame tagFrame
                && IsExpectedRunTagEffectFrame(state, tagFrame, run, behaviorCatalog);
            bool expectedIceInstall = state.ResolutionStack[runFrameIndex] is IceSubroutineFrame
                && suffixRoot is InstallFrame;
            bool expectedIceDamage = state.ResolutionStack[runFrameIndex] is IceSubroutineFrame
            { Stage: IceSubroutineFrameStage.AwaitingDamage } damageParent
                && suffixRoot is EffectResolutionFrame damageChild
                && damageChild.Effects.Instructions.Count == 1
                && damageChild.Effects.Instructions[0] is DamageEffect damage
                && behaviorCatalog.TryGetIce(damageParent.IceDefinitionId, out IceBehaviorSpec? damageIce)
                && run.EncounterSubroutineSpec(behaviorCatalog, damageIce, damageParent.EncounterId, damageParent.Ordinal)
                    is DamageSubroutineSpec printedDamage
                && damage.Kind == printedDamage.Kind && damage.Amount == printedDamage.Amount
                && state.Players.Any(player => player.Side == PlayerSide.Runner && player.Id == damage.Target);
            // EL-GENERIC-SUBROUTINE: the behavior child of a generic
            // subroutine of the encountered ICE; the behavior validators own
            // its nested continuation.
            bool expectedIceBehavior = suffixRoot is BehaviorResolutionFrame subroutineBehavior
                && IsIceSubroutineTrigger(behaviorCatalog, subroutineBehavior)
                && run.Encounter?.IceId == subroutineBehavior.SourceCardId;
            if (suffixRoot is not AbilityFrame
                && !expectedGameplayOccurrence
                && !expectedIceInstall
                && !expectedIceDamage
                && !expectedIceTag
                && !expectedIceBehavior)
            {
                Add(
                    violations,
                    "run.stack-topology",
                    "resolutionStack",
                    "Only an ability lifecycle or exact production gameplay occurrence pipeline may follow the live run timing frame.");
                return;
            }

            if (!expectedGameplayOccurrence && !expectedIceInstall && !expectedIceDamage && !expectedIceTag
                && !expectedIceBehavior)
            {
                for (int index = runFrameIndex + 2; index < state.ResolutionStack.Count; index++)
                {
                    ResolutionFrame suffix = state.ResolutionStack[index];
                    if (suffix is BehaviorResolutionFrame behavior
                        && behavior.AbilityKey is not null
                        && index == runFrameIndex + 2)
                    {
                        continue;
                    }

                    // The existing behavior install validator owns its exact child
                    // shape. Permit only that narrow continuation after an ability
                    // child; all other nested cursors are invalid in this vertical.
                    if (suffix is InstallFrame install
                        && index == runFrameIndex + 3
                        && state.ResolutionStack[index - 1] is BehaviorResolutionFrame)
                    {
                        continue;
                    }

                    // EL.8: an access ability's behavior that trashes the accessed
                    // card (Gourmand) suspends the exact gameplay occurrence
                    // pipeline above it; the effect and checkpoint validators own
                    // that pipeline and everything above it.
                    if (suffix is EffectResolutionFrame abilityOccurrence
                        && state.ResolutionStack[index - 1] is BehaviorResolutionFrame
                        && IsExpectedRunGameplayOccurrenceFrame(state, abilityOccurrence, run, withinAccessAbility: true))
                    {
                        break;
                    }

                    // EL-BEHAVIOR-INSTALL: a zone install chosen by a paid
                    // ability mid-run (Madani), and its install child.
                    if (suffix is BehaviorInstallChoiceFrame
                        && index == runFrameIndex + 3
                        && state.ResolutionStack[index - 1] is BehaviorResolutionFrame
                        || suffix is InstallFrame
                        && index == runFrameIndex + 4
                        && state.ResolutionStack[index - 1] is BehaviorInstallChoiceFrame)
                    {
                        continue;
                    }

                    Add(
                        violations,
                        "run.stack-topology",
                        $"resolutionStack[{index}]",
                        "A run ability stack contains an unsupported nested resolution frame.");
                }
            }
        }

        if (!nested)
        {
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

        if (run.ActiveRunEventId is { } activeEventId)
        {
            if (run.InitiatingSource != activeEventId)
            {
                Add(
                    violations,
                    "run.nested-source",
                    "runState.initiatingSource",
                    "A real-turn run event must use the same source identity for InitiatingSource and ActiveRunEventId.");
            }
        }
        else if (run.InitiatingSource is { } installedSource)
        {
            CardInstanceState? source = state.Cards.FirstOrDefault(card => card?.Id == installedSource);
            ZoneState? sourceZone = source is null
                ? null
                : state.Zones.FirstOrDefault(zone => zone?.Id == source.ZoneId);
            bool validInstalledSource = source is not null
                && source.OwnerSide == PlayerSide.Runner
                && sourceZone is not null
                && sourceZone.CardIds.Contains(installedSource)
                && behaviorCatalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? sourceDefinition)
                && (state.Servers.FirstOrDefault(server => server?.Id == run.AttackedServer)?.Kind ?? (ServerKind)(-1)) is var attackedKind
                // As for a run event: the run may have been moved to Archives (Proprionegation).
                && (BehaviorRunRules.DefinitionHasStartRunForServer(sourceDefinition!, attackedKind) || attackedKind == ServerKind.Archives);
            if (!validInstalledSource)
            {
                Add(
                    violations,
                    "run.nested-source",
                    "runState.initiatingSource",
                    "A click-run source must remain a physical Runner-owned card with a matching click StartRun declaration.");
            }
        }
        else if (run.InitiatingSource is null)
        {
            // The ordinary real-turn basic run has no card provenance.
        }
    }

    private static bool IsExpectedRunGameplayOccurrenceFrame(
        GameState state,
        EffectResolutionFrame frame,
        RunState run,
        bool withinAccessAbility = false)
    {
        if (frame.Effects.Instructions.Count != 1
            || frame.Effects.Instructions[0] is not GameplayOccurrenceEffect occurrence
            || frame.Controller != occurrence.Controller
            || occurrence.Successful is not null)
        {
            return false;
        }

        return occurrence.Kind switch
        {
            OccurrenceKind.RunSuccessful => frame.Controller == run.InitiatingPlayer
                && run.Successful
                && run.Step == RunTimingStep.BreachBoundary
                && occurrence.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId == run.InitiatingSource
                && occurrence.ServerId == run.AttackedServer,
            OccurrenceKind.ServerApproached => run.Step == RunTimingStep.Success
                && !run.Successful
                && frame.Controller == run.InitiatingPlayer
                && occurrence.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is null
                && occurrence.ServerId == run.AttackedServer,
            OccurrenceKind.IceEncountered => run.Step == RunTimingStep.EncounterPaidWindow
                && frame.Controller == run.InitiatingPlayer
                && occurrence.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is { } encounteredIce
                && run.CurrentIceId == encounteredIce
                && run.Encounter is { Status: RunEncounterStatus.Initialized, IceId: var encounterIce }
                && encounterIce == encounteredIce
                && occurrence.ServerId == run.AttackedServer,
            OccurrenceKind.IceApproached => run.Step == RunTimingStep.ApproachPaidWindow
                && frame.Controller == run.InitiatingPlayer
                && occurrence.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is { } approachedIce
                && run.CurrentIceId == approachedIce
                && occurrence.ServerId == run.AttackedServer,
            OccurrenceKind.CardAccessed => run.Step == RunTimingStep.BreachAccessAbility
                && frame.Controller == run.InitiatingPlayer
                && occurrence.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is { } accessed
                && run.ChosenAccessCards.Count > 0
                && run.ChosenAccessCards[^1] == accessed
                && run.AccessedCards.Contains(accessed)
                && occurrence.ServerId == run.AttackedServer,
            OccurrenceKind.AccessedCardTrashed => run.InitiatingSide == PlayerSide.Runner
                && (run.Step is RunTimingStep.BreachCandidateSelection or RunTimingStep.RunEnd
                    || withinAccessAbility && run.Step == RunTimingStep.BreachAccessAbility)
                && frame.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is { } trashed
                && run.ChosenAccessCards.Contains(trashed)
                && run.AccessedCards.Contains(trashed)
                && (occurrence.ServerId is null || occurrence.ServerId == run.AttackedServer)
                && state.Zones.Any(zone => zone.Kind == ZoneKind.CorpDiscard && zone.CardIds.Contains(trashed)),
            OccurrenceKind.AgendaScoredOrStolen => run.InitiatingSide == PlayerSide.Runner
                && run.Successful
                && run.Step is RunTimingStep.BreachCandidateSelection or RunTimingStep.RunEnd
                && frame.Controller == run.InitiatingPlayer
                && occurrence.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is { } agenda
                && run.ChosenAccessCards.Contains(agenda)
                && run.AccessedCards.Contains(agenda)
                && state.Zones.Any(zone =>
                    zone.Kind == ZoneKind.RunnerScore
                    && zone.OwnerSide == PlayerSide.Runner
                    && zone.CardIds.Contains(agenda))
                && occurrence.ServerId == run.AttackedServer,
            OccurrenceKind.CardRezzed => IsExpectedRunCardRezzedOccurrence(state, run, occurrence),
            OccurrenceKind.RunBegan => run.Step == RunTimingStep.InitiationPaidWindow
                && frame.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is null
                && occurrence.ServerId == run.AttackedServer,
            // EL.2: an occurrence deferred to the end of the action that raised it (a forfeit to rez, an
            // emptied hosted pool, a first tag removal or operation), opened when the run's cursor resumes.
            // EL.9: also the first credit gain through an agenda or operation (Greenmail forfeited to rez
            // Biawak mid-run, for The Zwicky Group).
            OccurrenceKind.AgendaForfeited or OccurrenceKind.HostedCreditPoolEmptied
                or OccurrenceKind.FirstTagRemovalThisTurn or OccurrenceKind.FirstBadPublicityThisTurn
                or OccurrenceKind.CreditsSpentFromOutsidePool
                or OccurrenceKind.FirstOperationPlayedThisTurn
                or OccurrenceKind.FirstEventPlayedThisTurn
                or OccurrenceKind.FirstCardCreditGainThisTurn => occurrence.ServerId is null,
            // VP-ICE-PASSED, VP-ENCOUNTER-ENDED: the passed or bypassed ICE is the run's current ICE, as the run moves on
            // (or ends, for an encounter that ended the run).
            OccurrenceKind.IcePassed or OccurrenceKind.IceBypassed or OccurrenceKind.EncounterEnded =>
                run.Step is RunTimingStep.MovementPaidWindow or RunTimingStep.RunEnd
                && frame.Controller == run.InitiatingPlayer
                && occurrence.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is { } passedIce
                && run.CurrentIceId == passedIce
                && occurrence.ServerId == run.AttackedServer,
            // VP-SUBROUTINE-BROKEN, VP-ICE-PASSED (fully broken): raised in the encounter's paid window by a break.
            OccurrenceKind.SubroutineBroken or OccurrenceKind.IceFullyBroken => run.Step == RunTimingStep.EncounterPaidWindow
                && frame.Controller == run.InitiatingPlayer
                && occurrence.Controller == run.InitiatingPlayer
                && occurrence.SubjectCardId is { } brokenIce
                && run.Encounter?.IceId == brokenIce
                && occurrence.ServerId == run.AttackedServer,
            _ => false,
        };
    }

    private static bool IsExpectedRunCardRezzedOccurrence(
        GameState state,
        RunState run,
        GameplayOccurrenceEffect occurrence)
    {
        if (!IsRunPriorityStep(run.Step)
            || occurrence.Controller != state.Players.FirstOrDefault(player => player.Side == PlayerSide.Corp)?.Id
            || occurrence.SubjectCardId is not { } cardId
            || occurrence.ServerId is not { } serverId)
        {
            return false;
        }

        CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate.Id == cardId);
        if (card is null
            || card.OwnerSide != PlayerSide.Corp
            || !card.FaceUp
            || !card.Rezzed
            || !RuntimeCardActivity.IsActive(card, state.Zones))
        {
            return false;
        }

        ZoneState? installedZone = state.Zones.FirstOrDefault(zone => zone.Id == card.ZoneId);
        return installedZone?.ServerId == serverId;
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

        ZoneState? encounterIceZone = state.Servers
            .FirstOrDefault(server => server?.Id == run.AttackedServer) is { } attackedServer
            ? state.Zones.FirstOrDefault(zone => zone?.Id == attackedServer.IceZoneId)
            : null;
        bool removedEncounterCursor = encounterIceZone is not null
            && IsRemovedEncounterCursor(state, run, encounterIceZone);
        if (!removedEncounterCursor && run.CurrentIceId != encounter.IceId)
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
        RunTimingStep.MovementPaidWindow or RunTimingStep.JackOutDecision or RunTimingStep.MoveInward or RunTimingStep.AfterMovePaidWindow or RunTimingStep.ApproachServer => RunCrPhase.Movement,
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

            if (!Enum.IsDefined(counter.Purpose))
            {
                Add(
                    violations,
                    "rules.invalid-counter-purpose",
                    $"{counterPath}.purpose",
                    $"'{counter.Purpose}' is not a supported card-counter purpose.");
            }

            if (counter.SpendRestriction is { } spendRestriction
                && !Enum.IsDefined(spendRestriction))
            {
                Add(
                    violations,
                    "rules.invalid-counter-spend-restriction",
                    $"{counterPath}.spendRestriction",
                    $"'{spendRestriction}' is not a supported hosted-credit spend restriction.");
            }

            if (Enum.IsDefined(counter.Purpose)
                && (counter.SpendRestriction is null
                    || Enum.IsDefined(counter.SpendRestriction.Value))
                && !CardCounterState.IsValidPurposeNameRestriction(
                    counter.CounterName,
                    counter.Purpose,
                    counter.SpendRestriction))
            {
                Add(
                    violations,
                    "rules.invalid-counter-purpose-name-restriction",
                    counterPath,
                    "Counter purpose, name, and hosted-credit spend restriction do not form a valid combination.");
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

                    if (install.InitiationKind == InstallInitiationKind.BehaviorZoneInstall)
                    {
                        if (index == 0
                            || state.ResolutionStack[index - 1] is not BehaviorInstallChoiceFrame zoneParent
                            || !string.Equals(zoneParent.FrameIdentity, install.ParentFrameIdentity, StringComparison.Ordinal)
                            || zoneParent.Stage != BehaviorInstallChoiceStage.AwaitingInstall
                            || zoneParent.Owner != install.Owner)
                        {
                            Add(violations, "resolution.behavior-zone-install-parent", path, "A behavior zone install must sit on its awaiting choice frame.");
                        }

                        break;
                    }

                    if (install.InitiationKind is InstallInitiationKind.IceSubroutineNormal
                        or InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
                    {
                        ValidateIceSubroutineInstallFrame(
                            state,
                            install,
                            index,
                            path,
                            installOwner,
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
                    // EL-HOSTED-FACEUP: Bling's hosted cards install as if from the grip.
                    ZoneKind installSourceKind = install.SourceZoneKind == ZoneKind.RunnerHostedFaceUp
                        ? ZoneKind.RunnerHostedFaceUp
                        : ZoneKind.RunnerGrip;
                    ZoneState? runnerGrip = state.Zones.FirstOrDefault(
                        zone => zone?.Kind == installSourceKind
                            && zone.ServerId is null);
                    if (install.SourceZoneKind != installSourceKind
                        || installSource is null
                        || installSource.Kind != installSourceKind
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

                                CostQuote? expectedInstallQuote = GameEngine.ProjectCostQuote(
                                    state,
                                    behaviorCatalog,
                                    install.CardId,
                                    BehaviorCostModifierKind.RunnerInstallCreditCost,
                                    runtimeSpec.InstallCost ?? 0,
                                    runtimeCardCatalog);
                                int expectedInstallCost = expectedInstallQuote?.EffectiveCost ?? (runtimeSpec.InstallCost ?? 0);
                                if (install.InstallCost != expectedInstallCost)
                                {
                                    Add(
                                        violations,
                                        "resolution.install-cost-mismatch",
                                        $"{path}.installCost",
                                        "Runner install frame cost must match the authoritative base cost plus its current self-cost quote.");
                                }

                                if (install.CostQuote is null
                                    ? expectedInstallQuote is not null
                                    : !install.CostQuote.Equals(expectedInstallQuote))
                                {
                                    Add(
                                        violations,
                                        "resolution.install-cost-quote-mismatch",
                                        $"{path}.costQuote",
                                        "Runner install frame cost quote must match the authoritative source definition and current turn facts.");
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
                case BehaviorInstallChoiceFrame installChoiceFrame:
                    RequirePlayer(playerIds, installChoiceFrame.Owner, $"{path}.owner", violations);
                    if (index == 0
                        || state.ResolutionStack[index - 1] is not BehaviorResolutionFrame installChoiceParent
                        || !string.Equals(
                            installChoiceFrame.ParentFrameIdentity,
                            GameEngine.BehaviorFrameIdentityOf(installChoiceParent),
                            StringComparison.Ordinal))
                    {
                        Add(violations, "resolution.behavior-install-parent", path, "A behavior install choice must sit directly on the behavior frame that declared it.");
                    }

                    if (installChoiceFrame.Stage == BehaviorInstallChoiceStage.AwaitingChoice
                        && index != state.ResolutionStack.Count - 1)
                    {
                        Add(violations, "resolution.behavior-install-not-top", path, "A behavior install choice must be top while its decision is pending.");
                    }

                    break;
                case IceSubroutineFrame iceFrame:
                    if (state.RunState is null
                        || iceFrame.RunId != state.RunState.Id
                        || state.RunState.Step != RunTimingStep.Encounter
                        || state.RunState.Encounter is not { } activeEncounter
                        || activeEncounter.Id != iceFrame.EncounterId
                        || activeEncounter.IceId != iceFrame.IceId)
                    {
                        Add(
                            violations,
                            "resolution.subroutine-run-mismatch",
                            path,
                            "An ICE subroutine frame must match the live encounter run cursor.");
                    }

                    if (iceFrame.Stage == IceSubroutineFrameStage.AwaitingChoice
                        && index != state.ResolutionStack.Count - 1)
                    {
                        Add(
                            violations,
                            "resolution.subroutine-choice-not-top",
                            path,
                            "An ICE subroutine choice frame must remain top while its decision is pending.");
                    }

                    if (iceFrame.Stage == IceSubroutineFrameStage.AwaitingDamage)
                    {
                        if (iceFrame.Choices.Count != 0
                            || iceFrame.CatalogFingerprint != behaviorCatalog.Fingerprint
                            || index + 1 >= state.ResolutionStack.Count
                            || state.ResolutionStack[index + 1] is not EffectResolutionFrame)
                        {
                            Add(violations, "resolution.subroutine-damage-child-mismatch", path,
                                "A damage continuation requires its authenticated effect child and no pending choices.");
                        }
                    }

                    if (iceFrame.DamageTrashedCardId is { } damageVictim
                        && (!cardsById.TryGetValue(damageVictim, out CardInstanceState? victimCard)
                            || victimCard.OwnerSide != PlayerSide.Runner
                            || !behaviorCatalog.TryGetIce(iceFrame.IceDefinitionId, out IceBehaviorSpec? victimIce)
                            || state.RunState?.EncounterSubroutineSpec(behaviorCatalog, victimIce, iceFrame.EncounterId, iceFrame.Ordinal)
                                is not DamageSubroutineSpec))
                    {
                        Add(violations, "resolution.subroutine-damage-victim-mismatch", path,
                            "Only a damage subroutine may retain an existing Runner card as its actual damage victim.");
                    }

                    if (iceFrame.Stage == IceSubroutineFrameStage.AwaitingInstall
                        && (index == state.ResolutionStack.Count - 1
                            || state.ResolutionStack[index + 1] is not InstallFrame installChild
                            || installChild.ParentFrameIdentity != iceFrame.FrameIdentity))
                    {
                        Add(
                            violations,
                            "resolution.subroutine-install-child-missing",
                            path,
                            "An ICE subroutine awaiting install must be immediately followed by its authenticated InstallFrame child.");
                    }

                    break;
                case AbilityFrame abilityFrame:
                    ValidateAbilityFrame(
                        state,
                        abilityFrame,
                        index,
                        path,
                        playerIds,
                        cardIds,
                        cardsById,
                        behaviorCatalog,
                        runtimeCardCatalog,
                        violations);
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

                    bool awaitingHostedCreditOccurrence = false;
                    if (behaviorFrame.Stage == BehaviorResolutionStage.AwaitingOccurrence)
                    {
                        BehaviorOccurrenceContinuation? continuation = behaviorFrame.OccurrenceContinuation;
                        EffectResolutionFrame? occurrenceChild = index + 1 < state.ResolutionStack.Count
                            ? state.ResolutionStack[index + 1] as EffectResolutionFrame
                            : null;
                        GameplayOccurrenceEffect? occurrence = occurrenceChild?.Effects.Instructions.Count == 1
                            ? occurrenceChild.Effects.Instructions[0] as GameplayOccurrenceEffect
                            : null;
                        awaitingHostedCreditOccurrence = continuation is not null
                            && continuation.Kind == OccurrenceKind.HostedCreditPoolEmptied
                            && continuation.SourceCardId == behaviorFrame.SourceCardId
                            && continuation.FrameIdentity == behaviorFrame.FrameIdentity
                            && !continuation.SourceMovedAfterCompletion
                            && continuation.InstructionCursor == behaviorFrame.InstructionCursor
                            && occurrence is not null
                            && occurrence.Kind == OccurrenceKind.HostedCreditPoolEmptied
                            && occurrence.Controller == behaviorFrame.Controller
                            && occurrence.SubjectCardId == behaviorFrame.SourceCardId;
                        if (!awaitingHostedCreditOccurrence)
                        {
                            Add(
                                violations,
                                "resolution.behavior-occurrence-child-mismatch",
                                path,
                                "An awaiting behavior occurrence must retain the exact nested hosted-credit occurrence effect for its source card.");
                        }
                    }

                    if (behaviorFrame.AbilityKey is not null)
                    {
                        if (index == 0
                            || state.ResolutionStack[index - 1] is not AbilityFrame abilityParent
                            || abilityParent.Stage != AbilityLifecycleStage.AwaitingChild
                            || !string.Equals(
                                abilityParent.AbilityKey,
                                behaviorFrame.AbilityKey,
                                StringComparison.Ordinal)
                            || abilityParent.SourceCardId != behaviorFrame.SourceCardId
                            || abilityParent.DefinitionId != behaviorFrame.DefinitionId
                            || abilityParent.Controller != behaviorFrame.Controller)
                        {
                            Add(
                                violations,
                                "resolution.ability-child-parent-mismatch",
                                path,
                                "An ability behavior child must remain immediately above its exact awaiting ability parent.");
                        }
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

                            bool selfTrash = false;
                            bool selfRemove = false;
                            if (behaviorCatalog.TryGet(
                                    behaviorFrame.DefinitionId,
                                    out CardBehaviorDefinition? sourceDefinition))
                            {
                                IReadOnlyList<BehaviorCost> costs = behaviorFrame.AbilityKey is null
                                    ? sourceDefinition.Costs
                                    : sourceDefinition.PaidAbilities
                                        .SingleOrDefault(ability => string.Equals(
                                            ability.Key,
                                            behaviorFrame.AbilityKey,
                                            StringComparison.Ordinal))?.Costs
                                        ?? [];
                                selfTrash = costs.Any(cost => cost.Kind == BehaviorCostKind.TrashSelf);
                                selfRemove = costs.Any(cost => cost.Kind == BehaviorCostKind.RemoveSelfFromGame);
                            }
                            CardInstanceState? sourceCard = cardIds.Contains(behaviorFrame.SourceCardId)
                                ? state.Cards.Single(card => card.Id == behaviorFrame.SourceCardId)
                                : null;
                            ZoneState? currentSourceZone = sourceCard is null
                                ? null
                                : state.Zones.FirstOrDefault(zone => zone?.Id == sourceCard.ZoneId);
                            bool catalogTriggerSelfTrashReached = false;
                            if (behaviorFrame.TriggerKey is not null
                                && behaviorCatalog.TryGet(
                                    behaviorFrame.DefinitionId,
                                    out CardBehaviorDefinition? triggerSourceDefinition))
                            {
                                BehaviorTrigger? trigger = triggerSourceDefinition.Triggers
                                    .SingleOrDefault(candidate => string.Equals(
                                        candidate.Key,
                                        behaviorFrame.TriggerKey,
                                        StringComparison.Ordinal));
                                catalogTriggerSelfTrashReached = trigger is not null
                                    && BehaviorInstructionPath.HasReached(
                                        trigger.Instructions,
                                        behaviorFrame.Continuation,
                                        behaviorFrame.InstructionCursor,
                                        BehaviorInstructionKind.TrashSelf);
                                catalogTriggerSelfTrashReached |= trigger is not null
                                    && BehaviorInstructionPath.HasReached(
                                        trigger.Instructions,
                                        behaviorFrame.Continuation,
                                        behaviorFrame.InstructionCursor,
                                        instruction => instruction.Kind == BehaviorInstructionKind.DealDamage
                                            && instruction.DamageAmountOperand?.TrashSourceAsCost == true);
                                // The trigger's own install may replace its source (a second
                                // Pantograph trashes the first); the ability still finishes.
                                catalogTriggerSelfTrashReached |= trigger is not null
                                    && BehaviorInstructionPath.HasReached(
                                        trigger.Instructions,
                                        behaviorFrame.Continuation,
                                        behaviorFrame.InstructionCursor,
                                        BehaviorInstructionKind.InstallCard);
                            }

                            bool catalogTriggerDetachedSource = behaviorFrame.TriggerKey is not null
                                && behaviorFrame.CatalogTriggerSourceMovedBeforeStart;
                            PlayerSide? controllerSide = state.Players
                                .SingleOrDefault(player => player.Id == behaviorFrame.Controller)
                                ?.Side;
                            bool hostedCreditOccurrencePendingMovement = awaitingHostedCreditOccurrence
                                && sourceCard is not null
                                && currentSourceZone is not null
                                && controllerSide.HasValue
                                && sourceCard.OwnerSide == controllerSide.Value
                                && IsOwnerDiscard(currentSourceZone, sourceCard.OwnerSide);
                            bool hostedCreditSelfTrashDeclared = behaviorCatalog.TryGet(
                                    behaviorFrame.DefinitionId,
                                    out CardBehaviorDefinition? occurrenceDefinition)
                                && occurrenceDefinition is not null
                                && occurrenceDefinition.Triggers.Any(trigger =>
                                    trigger.Occurrence == OccurrenceKind.HostedCreditPoolEmptied
                                    && BehaviorInstructionPath.CanReach(
                                        trigger.Instructions,
                                        BehaviorInstructionKind.TrashSelf));
                            bool hostedCreditOccurrenceCompletedMovement = behaviorFrame.Stage == BehaviorResolutionStage.Execute
                                && behaviorFrame.OccurrenceContinuation is
                                {
                                    Kind: OccurrenceKind.HostedCreditPoolEmptied,
                                    SourceMovedAfterCompletion: true,
                                } occurrenceContinuation
                                && occurrenceContinuation.SourceCardId == behaviorFrame.SourceCardId
                                && occurrenceContinuation.FrameIdentity == behaviorFrame.FrameIdentity
                                && sourceCard is not null
                                && currentSourceZone is not null
                                && controllerSide.HasValue
                                && sourceCard.OwnerSide == controllerSide.Value
                                && IsOwnerDiscard(currentSourceZone, sourceCard.OwnerSide)
                                && state.RulesState is not null
                                && !state.RulesState.Counters.Any(counter =>
                                    counter.CardId == behaviorFrame.SourceCardId
                                    && counter.Purpose == CardCounterPurpose.HostedCredit)
                                && hostedCreditSelfTrashDeclared;
                            bool movedAfterPayment = (behaviorFrame.Stage == BehaviorResolutionStage.Execute
                                && ((behaviorFrame.TriggerKey is null
                                        && selfTrash
                                        && sourceCard is not null
                                        && currentSourceZone is not null
                                        && (sourceCard.OwnerSide == PlayerSide.Corp
                                            ? currentSourceZone.Kind == ZoneKind.CorpDiscard
                                            : currentSourceZone.Kind == ZoneKind.RunnerHeap))
                                    || (behaviorFrame.TriggerKey is null
                                        && selfRemove
                                        && sourceCard is not null
                                        && currentSourceZone is not null
                                        && (sourceCard.OwnerSide == PlayerSide.Corp
                                            ? currentSourceZone.Kind == ZoneKind.CorpRemovedFromGame
                                            : currentSourceZone.Kind == ZoneKind.RunnerRemovedFromGame))
                                    || (catalogTriggerSelfTrashReached
                                        && sourceCard is not null
                                        && currentSourceZone is not null
                                        && (sourceCard.OwnerSide == PlayerSide.Corp
                                            ? currentSourceZone.Kind == ZoneKind.CorpDiscard
                                            : currentSourceZone.Kind == ZoneKind.RunnerHeap))))
                                || hostedCreditOccurrencePendingMovement
                                || hostedCreditOccurrenceCompletedMovement;
                            bool dynamicCounterRetentionAuthorized = behaviorFrame.AbilityKey is not null
                                && (selfTrash || selfRemove)
                                && behaviorCatalog.TryGet(
                                    behaviorFrame.DefinitionId,
                                    out CardBehaviorDefinition? retainedCounterDefinition)
                                && retainedCounterDefinition.PaidAbilities
                                    .SingleOrDefault(ability => string.Equals(
                                        ability.Key,
                                        behaviorFrame.AbilityKey,
                                        StringComparison.Ordinal)) is { } retainedCounterAbility
                                && ContainsBehaviorDynamicAmount(retainedCounterAbility.Instructions);
                            bool movedSourceRetainsCounters = sourceCard is not null
                                && sourceCard.ZoneId != sourceZone.Id
                                && state.RulesState?.Counters.Any(counter => counter.CardId == sourceCard.Id) == true;
                            if (movedSourceRetainsCounters
                                && (!movedAfterPayment || !dynamicCounterRetentionAuthorized))
                            {
                                Add(
                                    violations,
                                    "resolution.behavior-source-counters-unauthorized",
                                    $"{path}.sourceCardId",
                                    "Counters may remain on a moved behavior source only for the exact paid self-movement ability whose catalog path contains a dynamic credit amount.");
                            }

                            if (sourceCard is not null
                                && sourceCard.ZoneId != sourceZone.Id
                                && state.RulesState?.ScoredAsAgendas?.Any(entry => entry.CardId == sourceCard.Id) != true
                                && !catalogTriggerDetachedSource
                                && !(behaviorFrame.TriggerKey is not null && behaviorFrame.CatalogTriggerSourceMovedAfterStart)
                                && !movedAfterPayment)
                            {
                                Add(violations, "resolution.behavior-source-location-mismatch", $"{path}.sourceZoneId", "Behavior source card no longer occupies its captured invocation location.");
                            }
                        }
                    }

                    if (behaviorFrame.TriggerKey is null
                        && behaviorFrame.CatalogTriggerSourceMovedBeforeStart)
                    {
                        Add(
                            violations,
                            "resolution.behavior-detached-source-unexpected",
                            path,
                            "Detached-source authorization is reserved for catalog trigger behavior children.");
                    }

                    if (behaviorFrame.CatalogTriggerSourceMovedAfterStart
                        && (behaviorFrame.TriggerKey is null
                            || behaviorFrame.Stage != BehaviorResolutionStage.Execute
                            || (behaviorFrame.InstructionCursor == 0 && behaviorFrame.Continuation.Count == 0)
                            || state.Cards.SingleOrDefault(card => card.Id == behaviorFrame.SourceCardId)?.ZoneId == behaviorFrame.SourceZoneId))
                    {
                        Add(
                            violations,
                            "resolution.behavior-moved-source-unexpected",
                            path,
                            "Moved-after-start authorization is reserved for a begun catalog trigger whose source left its zone.");
                    }

                    BehaviorBinding[] triggerSubjectBindings = behaviorFrame.Bindings
                        .Where(binding => string.Equals(binding.Key, "trigger-subject", StringComparison.Ordinal))
                        .ToArray();
                    if (behaviorFrame.TriggerKey is null)
                    {
                        if (triggerSubjectBindings.Length > 0)
                        {
                            Add(
                                violations,
                                "resolution.behavior-trigger-subject-unexpected",
                                $"{path}.bindings",
                                "The trigger-subject binding is reserved for catalog trigger children.");
                        }
                    }
                    else if (IsIceSubroutineTrigger(behaviorCatalog, behaviorFrame))
                    {
                        // A generic ICE subroutine runs directly above the run
                        // timing frame of its own ICE's encounter.
                        if (index == 0
                            || state.ResolutionStack[index - 1] is not RunTimingFrame
                            || state.RunState?.Encounter?.IceId != behaviorFrame.SourceCardId
                            || triggerSubjectBindings.Length > 0)
                        {
                            Add(
                                violations,
                                "resolution.trigger-child-parent-mismatch",
                                path,
                                "A generic ICE subroutine must remain immediately above the run timing frame encountering its ICE.");
                        }
                    }
                    else
                    {
                        TriggerInvocation? triggerInvocation = null;
                        if (index > 0
                            && state.ResolutionStack[index - 1] is EffectCheckpointFrame triggerParent
                            && triggerParent.Stage == EffectCheckpointStage.AwaitingChild
                            && triggerParent.ChildKind == EffectCheckpointChildKind.Trigger
                            && triggerParent.ChildTriggerId is { } childTriggerId)
                        {
                            TriggerInvocation[] matchingInvocations = triggerParent.TriggerQueue
                                .Where(invocation => invocation.Id == childTriggerId)
                                .Take(2)
                                .ToArray();
                            if (matchingInvocations.Length == 1)
                            {
                                triggerInvocation = matchingInvocations[0];
                            }
                        }

                        if (triggerInvocation is null
                            || !triggerInvocation.IsCatalogTrigger
                            || !string.Equals(triggerInvocation.TriggerKey, behaviorFrame.TriggerKey, StringComparison.Ordinal)
                            || triggerInvocation.SourceCardId != behaviorFrame.SourceCardId
                            || triggerInvocation.SourceDefinitionId != behaviorFrame.DefinitionId
                            || triggerInvocation.SourceZoneId != behaviorFrame.SourceZoneId
                            || triggerInvocation.Controller != behaviorFrame.Controller
                            || !string.Equals(triggerInvocation.CatalogFingerprint, behaviorFrame.CatalogFingerprint, StringComparison.Ordinal)
                            || !string.Equals(triggerInvocation.DefinitionFingerprint, behaviorFrame.DefinitionFingerprint, StringComparison.Ordinal))
                        {
                            Add(
                                violations,
                                "resolution.trigger-child-parent-mismatch",
                                path,
                                "A catalog trigger behavior child must remain immediately above its exact awaiting checkpoint invocation.");
                        }
                        else
                        {
                            CardInstanceId? subjectCardId = triggerInvocation.Occurrence.SourceCardId;
                            bool bindingMatches = subjectCardId is null
                                ? triggerSubjectBindings.Length == 0
                                : triggerSubjectBindings.Length == 1
                                    && triggerSubjectBindings[0].CardIds.Count == 1
                                    && triggerSubjectBindings[0].CardIds[0] == subjectCardId.Value;
                            if (!bindingMatches)
                            {
                                Add(
                                    violations,
                                    "resolution.behavior-trigger-subject-mismatch",
                                    $"{path}.bindings",
                                    "The trigger-subject binding must equal the exact checkpoint invocation subject.");
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

                        if (behaviorFrame.AbilityKey is not null && behaviorFrame.TriggerKey is not null)
                        {
                            Add(
                                violations,
                                "resolution.behavior-multiple-declarations",
                                path,
                                "A behavior frame cannot carry both an ability key and a trigger key.");
                        }

                        BehaviorPaidAbility? abilityDeclaration = behaviorFrame.AbilityKey is null
                            ? null
                            : definition.PaidAbilities.SingleOrDefault(ability => string.Equals(
                                ability.Key,
                                behaviorFrame.AbilityKey,
                                StringComparison.Ordinal));
                        if (behaviorFrame.AbilityKey is not null && abilityDeclaration is null)
                        {
                            Add(
                                violations,
                                "resolution.unknown-ability-key",
                                $"{path}.abilityKey",
                                $"Ability key '{behaviorFrame.AbilityKey}' is not declared by definition '{behaviorFrame.DefinitionId}'.");
                        }

                        BehaviorTrigger? triggerDeclaration = behaviorFrame.TriggerKey is null
                            ? null
                            : definition.Triggers.SingleOrDefault(trigger => string.Equals(
                                trigger.Key,
                                behaviorFrame.TriggerKey,
                                StringComparison.Ordinal));
                        if (behaviorFrame.TriggerKey is not null && triggerDeclaration is null)
                        {
                            Add(
                                violations,
                                "resolution.unknown-trigger-key",
                                $"{path}.triggerKey",
                                $"Trigger key '{behaviorFrame.TriggerKey}' is not declared by definition '{behaviorFrame.DefinitionId}'.");
                        }

                        if (behaviorFrame.TriggerKey is not null
                            && behaviorFrame.Stage is not (BehaviorResolutionStage.Execute or BehaviorResolutionStage.AwaitingOccurrence))
                        {
                            Add(
                                violations,
                                "resolution.trigger-stage-invalid",
                                $"{path}.stage",
                                "A catalog trigger behavior child must remain in Execute stage.");
                        }

                        IReadOnlyList<BehaviorInstruction> behaviorRoot =
                            triggerDeclaration?.Instructions
                            ?? abilityDeclaration?.Instructions
                            ?? definition.Instructions;
                        var derivedInstructions = behaviorRoot.ToList();
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

                        ValidateBehaviorEncounterStrengthInstruction(
                            state,
                            behaviorCatalog,
                            behaviorFrame,
                            abilityDeclaration,
                            derivedInstructions,
                            index,
                            path,
                            violations);

                        ValidateBehaviorContinuationBindings(
                            behaviorFrame,
                            behaviorRoot,
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
                    ValidateBasicDrawEffectFrameContext(
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
                    ValidateInstalledCatalogTriggerSources(checkpoint, behaviorCatalog, path, violations);
                    RequirePlayer(playerIds, checkpoint.Controller, $"{path}.controller", violations);
                    int catalogBasicDrawReplacementCount = 0;
                    for (int interruptIndex = 0; interruptIndex < checkpoint.InterruptQueue.Count; interruptIndex++)
                    {
                        InterruptInvocation interrupt = checkpoint.InterruptQueue[interruptIndex];
                        if (interrupt.IsCatalogInterrupt)
                        {
                            ValidateCatalogBasicDrawReplacementInvocation(
                                state,
                                behaviorCatalog,
                                checkpoint,
                                interrupt,
                                $"{path}.interruptQueue[{interruptIndex}]",
                                violations);
                            if (interrupt.Occurrence.Kind == OccurrenceKind.BasicDraw)
                            {
                                catalogBasicDrawReplacementCount++;
                            }
                        }
                    }

                    if (catalogBasicDrawReplacementCount > 1)
                    {
                        Add(
                            violations,
                            "resolution.basic-draw-replacement-stack",
                            $"{path}.interruptQueue",
                            "A basic-draw occurrence may retain at most one catalog replacement invocation.");
                    }

                    if (index > 0
                        && state.ResolutionStack[index - 1] is EffectResolutionFrame occurrenceParent
                        && occurrenceParent.Effects.Instructions.Count == 1
                        && occurrenceParent.Effects.Instructions[0] is GameplayOccurrenceEffect parentOccurrence
                        && checkpoint.Occurrence.Effect is GameplayOccurrenceEffect checkpointOccurrence
                        && (parentOccurrence.Kind != checkpointOccurrence.Kind
                            || parentOccurrence.Controller != checkpointOccurrence.Controller
                            || parentOccurrence.SubjectCardId != checkpointOccurrence.SubjectCardId
                            || parentOccurrence.ServerId != checkpointOccurrence.ServerId
                            || parentOccurrence.Successful != checkpointOccurrence.Successful
                            || !RunPersistentContext.Matches(parentOccurrence.RunContext, checkpointOccurrence.RunContext)))
                    {
                        Add(
                            violations,
                            "resolution.gameplay-occurrence-context-mismatch",
                            path,
                            "A gameplay occurrence checkpoint must retain the exact typed effect context from its parent effect frame.");
                    }

                    if (checkpoint.Stage == EffectCheckpointStage.AwaitingChild)
                    {
                        ResolutionFrame? child = index + 1 < state.ResolutionStack.Count
                            ? state.ResolutionStack[index + 1]
                            : null;
                        if (child is null)
                        {
                            Add(
                                violations,
                                "resolution.checkpoint-child-missing",
                                path,
                                "An effect checkpoint awaiting a trigger or replacement child must be immediately followed by its resolution child frame.");
                        }
                        else if (checkpoint.ChildKind == EffectCheckpointChildKind.Replacement)
                        {
                            if (child is not EffectResolutionFrame)
                            {
                                Add(
                                    violations,
                                    "resolution.checkpoint-child-kind-mismatch",
                                    path,
                                    "A replacement checkpoint must be followed by an EffectResolutionFrame child.");
                            }
                        }
                        else if (checkpoint.ChildKind == EffectCheckpointChildKind.Trigger)
                        {
                            TriggerInvocation[] matchingInvocations = checkpoint.ChildTriggerId is { } childTriggerId
                                ? checkpoint.TriggerQueue
                                    .Where(candidate => candidate.Id == childTriggerId)
                                    .Take(2)
                                    .ToArray()
                                : [];
                            if (matchingInvocations.Length != 1)
                            {
                                Add(
                                    violations,
                                    "resolution.checkpoint-trigger-child-missing",
                                    path,
                                    "A trigger checkpoint child must identify an invocation in its materialized trigger queue.");
                            }
                            else
                            {
                                TriggerInvocation invocation = matchingInvocations[0];
                                if (invocation.IsCatalogTrigger)
                                {
                                    if (child is not BehaviorResolutionFrame behaviorChild
                                        || !string.Equals(behaviorChild.TriggerKey, invocation.TriggerKey, StringComparison.Ordinal)
                                        || behaviorChild.SourceCardId != invocation.SourceCardId
                                        || behaviorChild.DefinitionId != invocation.SourceDefinitionId
                                        || behaviorChild.SourceZoneId != invocation.SourceZoneId
                                        || behaviorChild.SourceZoneKind != invocation.SourceZoneKind
                                        || behaviorChild.Controller != invocation.Controller
                                        || invocation.SourceOwnerSide is not { } sourceOwnerSide
                                        || state.Cards.FirstOrDefault(card => card?.Id == behaviorChild.SourceCardId)?.OwnerSide != sourceOwnerSide
                                        || !string.Equals(behaviorChild.CatalogFingerprint, invocation.CatalogFingerprint, StringComparison.Ordinal)
                                        || !string.Equals(behaviorChild.DefinitionFingerprint, invocation.DefinitionFingerprint, StringComparison.Ordinal))
                                    {
                                        Add(
                                            violations,
                                            "resolution.checkpoint-catalog-trigger-child-mismatch",
                                            path,
                                            "A catalog trigger checkpoint must be followed by a behavior child matching the exact materialized invocation.");
                                    }
                                }
                                else if (child is not EffectResolutionFrame)
                                {
                                    Add(
                                        violations,
                                        "resolution.checkpoint-legacy-trigger-child-mismatch",
                                        path,
                                        "A legacy trigger checkpoint must be followed by an EffectResolutionFrame child.");
                                }
                            }
                        }
                        else
                        {
                            Add(
                                violations,
                                "resolution.checkpoint-child-kind-mismatch",
                                path,
                                "An awaiting effect checkpoint must declare either a trigger or replacement child.");
                        }
                    }
                    else if (index + 1 < state.ResolutionStack.Count
                        && state.ResolutionStack[index + 1] is (EffectResolutionFrame or BehaviorResolutionFrame))
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
            // A turn/run frame may be suspended beneath an engine-owned
            // ability lifecycle (and its behavior child). Only the actual
            // top frame can own a pending decision; requiring every prompt
            // frame in the stack to do so rejects valid suspended parents.
            ResolutionFrame? topFrame = state.ResolutionStack.Count == 0
                ? null
                : state.ResolutionStack[^1];
            if (IsPromptFrame(topFrame))
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

    private static void ValidateBasicDrawEffectFrameContext(
        GameState state,
        EffectResolutionFrame frame,
        int index,
        string path,
        List<StateInvariantViolation> violations)
    {
        BasicDrawEffect[] draws = frame.Effects.Instructions
            .OfType<BasicDrawEffect>()
            .ToArray();
        if (draws.Length == 0)
        {
            return;
        }

        if (draws.Length != 1 || frame.Effects.Instructions.Count != 1)
        {
            Add(
                violations,
                "resolution.basic-draw-bundle-shape",
                $"{path}.effects.instructions",
                "A typed basic draw must be the sole instruction in its effect transaction.");
            return;
        }

        BasicDrawEffect draw = draws[0];
        PlayerState? controller = state.Players.FirstOrDefault(player => player?.Id == frame.Controller);
        if (controller is null || controller.Side != draw.Side)
        {
            Add(
                violations,
                "resolution.basic-draw-controller-mismatch",
                $"{path}.controller",
                "A typed basic draw controller must be the player who performs the draw.");
        }

        if (!draw.IsReplacement)
        {
            if (draw.OccurrenceId is not { } occurrenceId)
            {
                Add(
                    violations,
                    "resolution.basic-draw-occurrence-missing",
                    $"{path}.effects.instructions[0].occurrenceId",
                    "A real basic draw must retain its materialized occurrence ID.");
            }
            else
            {
                TurnFactsState? facts = state.Turn.History?.ThisTurn(draw.Side);
                bool hasCurrentFacts = facts is not null
                    && facts.Side == draw.Side
                    && facts.TurnNumber == state.Turn.TurnNumber
                    && facts.TurnBegan
                    && facts.BasicDrawOccurrenceIds.Contains(occurrenceId);
                if (!hasCurrentFacts)
                {
                    Add(
                        violations,
                        "turn.basic-draw-ledger-mismatch",
                        $"{path}.effects.instructions[0].occurrenceId",
                        "A real basic draw must retain its accepted occurrence in the current formal-turn ledger.");
                }
                else
                {
                    bool isFirst = facts!.BasicDrawOccurrenceIds[0] == occurrenceId;
                    if (draw.IsFirstBasicDraw != isFirst)
                    {
                        Add(
                            violations,
                            "turn.basic-draw-first-flag-mismatch",
                            $"{path}.effects.instructions[0].isFirstBasicDraw",
                            "The first-basic-draw flag must agree with the first entry in the global formal-turn ledger.");
                    }
                }
            }

            if (frame.SourceOccurrence is not null)
            {
                Add(
                    violations,
                    "resolution.basic-draw-source-occurrence-unexpected",
                    $"{path}.sourceOccurrence",
                    "A real basic draw must not carry a replacement source occurrence.");
            }

            if (index == 0
                || state.ResolutionStack[index - 1] is not TurnFrame parent
                || parent.ActiveSide != draw.Side
                || parent.TurnNumber != state.Turn.TurnNumber
                || parent.Stage != TurnFrameStage.ActionPriority)
            {
                Add(
                    violations,
                    "resolution.basic-draw-parent-context-mismatch",
                    path,
                    "A real basic draw must be opened by the active turn action-priority frame.");
            }
        }
        else
        {
            if (frame.SourceOccurrence is not { } sourceOccurrence
                || sourceOccurrence.Kind != OccurrenceKind.BasicDraw
                || sourceOccurrence.Phase != OccurrencePhase.Imminent
                || sourceOccurrence.Effect is not BasicDrawEffect sourceDraw
                || sourceDraw.IsReplacement
                || !sourceDraw.IsFirstBasicDraw
                || sourceDraw.OccurrenceId is not { } sourceOccurrenceId
                || sourceOccurrenceId != sourceOccurrence.Id
                || sourceOccurrenceId != frame.SourceOccurrence.Id)
            {
                Add(
                    violations,
                    "resolution.basic-draw-replacement-source-context-mismatch",
                    $"{path}.sourceOccurrence",
                    "A replacement basic draw must retain the exact frozen first-draw occurrence context.");
            }

            if (index == 0
                || state.ResolutionStack[index - 1] is not EffectCheckpointFrame checkpoint
                || checkpoint.Stage != EffectCheckpointStage.AwaitingChild
                || checkpoint.ChildKind != EffectCheckpointChildKind.Replacement
                || checkpoint.ChildInterruptId is null
                || checkpoint.Occurrence.Kind != OccurrenceKind.BasicDraw
                || checkpoint.Occurrence.Phase != OccurrencePhase.Imminent
                || checkpoint.Occurrence.Id != frame.SourceOccurrence?.Id)
            {
                Add(
                    violations,
                    "resolution.basic-draw-replacement-parent-context-mismatch",
                    path,
                    "A replacement basic draw must be the child of its awaiting replacement checkpoint.");
            }

            if (draw.IsFirstBasicDraw || draw.OccurrenceId is not null)
            {
                Add(
                    violations,
                    "resolution.basic-draw-replacement-flags",
                    $"{path}.effects.instructions[0]",
                    "A replacement basic draw must not carry first-action or root-occurrence flags.");
            }

            if (frame.Controller != controller?.Id)
            {
                Add(
                    violations,
                    "resolution.basic-draw-replacement-controller-mismatch",
                    $"{path}.controller",
                    "A replacement basic draw controller must match its source occurrence controller.");
            }
        }
    }

    private static void ValidateAbilityFrame(
        GameState state,
        AbilityFrame frame,
        int index,
        string path,
        HashSet<PlayerId> playerIds,
        HashSet<CardInstanceId> cardIds,
        Dictionary<CardInstanceId, CardInstanceState> cardsById,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
    {
        RequirePlayer(playerIds, frame.Controller, $"{path}.controller", violations);

        if (index == 0)
        {
            Add(
                violations,
                "resolution.ability-parent-missing",
                path,
                "An ability frame requires an immediately preceding turn or run timing parent.");
            return;
        }

        ResolutionFrame parent = state.ResolutionStack[index - 1];
        if (frame.ParentFrameIdentity is null)
        {
            Add(
                violations,
                "resolution.ability-parent-missing",
                $"{path}.parentFrameIdentity",
                "An ability frame must retain its exact parent frame identity.");
        }
        else
        {
            try
            {
                if (!string.Equals(
                        frame.ParentFrameIdentity,
                        BehaviorFrameIdentity.Compute(parent),
                        StringComparison.Ordinal))
                {
                    Add(
                        violations,
                        "resolution.ability-parent-context-mismatch",
                        $"{path}.parentFrameIdentity",
                        "Ability parent identity does not match the exact active parent frame.");
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
            {
                Add(
                    violations,
                    "resolution.ability-parent-context-mismatch",
                    $"{path}.parentFrameIdentity",
                    $"Ability parent identity could not be recomputed: {exception.Message}");
            }
        }

        PlayerState? controller = state.Players.FirstOrDefault(player => player?.Id == frame.Controller);
        CardInstanceState? source = cardsById.GetValueOrDefault(frame.SourceCardId);
        if (source is null)
        {
            Add(
                violations,
                "resolution.unknown-ability-source",
                $"{path}.sourceCardId",
                $"Ability source card '{frame.SourceCardId}' does not exist.");
        }
        else
        {
            if (source.DefinitionId != frame.DefinitionId)
            {
                Add(
                    violations,
                    "resolution.ability-source-definition-mismatch",
                    path,
                    "Ability source card definition must match the frame definition.");
            }

            if (controller is not null && controller.Side != source.OwnerSide)
            {
                Add(
                    violations,
                    "resolution.ability-controller-mismatch",
                    $"{path}.controller",
                    "Ability controller must be the source card owner.");
            }
        }

        ZoneState? capturedZone = state.Zones.FirstOrDefault(zone => zone?.Id == frame.SourceZoneId);
        if (capturedZone is null)
        {
            Add(
                violations,
                "resolution.unknown-ability-source-zone",
                $"{path}.sourceZoneId",
                $"Ability source zone '{frame.SourceZoneId}' does not exist.");
        }
        else
        {
            if (capturedZone.Kind != frame.SourceZoneKind)
            {
                Add(
                    violations,
                    "resolution.ability-source-zone-kind-mismatch",
                    $"{path}.sourceZoneKind",
                    "Ability source zone kind must match the captured zone.");
            }

            if (!capturedZone.CardIds.Contains(frame.SourceCardId)
                && source?.ZoneId == frame.SourceZoneId)
            {
                Add(
                    violations,
                    "resolution.ability-source-membership-mismatch",
                    $"{path}.sourceCardId",
                    "Ability source card must be a member of its captured source zone.");
            }
        }

        BehaviorPaidAbility? declaration = null;
        if (!string.Equals(frame.CatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            Add(
                violations,
                "resolution.ability-catalog-mismatch",
                $"{path}.catalogFingerprint",
                "Ability frame catalog fingerprint must match the injected catalog.");
        }
        else if (behaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            try
            {
                if (!string.Equals(
                        frame.DefinitionFingerprint,
                        behaviorCatalog.GetDefinitionFingerprint(frame.DefinitionId),
                        StringComparison.Ordinal))
                {
                    Add(
                        violations,
                        "resolution.ability-definition-mismatch",
                        $"{path}.definitionFingerprint",
                        "Ability frame definition fingerprint must match the injected catalog.");
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                Add(
                    violations,
                    "resolution.ability-definition-fingerprint-failed",
                    $"{path}.definitionFingerprint",
                    $"Ability definition fingerprint lookup failed: {exception.Message}");
            }

            declaration = definition.PaidAbilities.SingleOrDefault(candidate => string.Equals(
                candidate.Key,
                frame.AbilityKey,
                StringComparison.Ordinal));
            if (declaration is null)
            {
                Add(
                    violations,
                    "resolution.unknown-ability-key",
                    $"{path}.abilityKey",
                    $"Ability key '{frame.AbilityKey}' is not declared by definition '{frame.DefinitionId}'.");
            }
        }
        else
        {
            Add(
                violations,
                "resolution.unknown-ability-definition",
                $"{path}.definitionId",
                $"Ability definition '{frame.DefinitionId}' is not registered.");
        }

        if (declaration is not null)
        {
            if (frame.Timing != declaration.Timing)
            {
                Add(violations, "resolution.ability-timing-mismatch", $"{path}.timing", "Ability timing must match the declaration.");
            }

            if (frame.Activation != declaration.Activation)
            {
                Add(violations, "resolution.ability-activation-mismatch", $"{path}.activation", "Ability activation must match the declaration.");
            }

            if (frame.Usage is null || frame.Usage.Limit != declaration.UsageLimit)
            {
                Add(violations, "resolution.ability-usage-mismatch", $"{path}.usage", "Ability usage snapshot must match the declaration.");
            }

            if (frame.Costs is null || !frame.Costs.SequenceEqual(declaration.Costs))
            {
                Add(violations, "resolution.ability-costs-mismatch", $"{path}.costs", "Ability costs must match the declaration.");
            }

            bool supportedTargetShape = declaration.Targets.Count == 0
                || declaration.Targets.Count == 1
                    && declaration.Targets[0].Kind == BehaviorTargetKind.AccessedCard;
            if (!supportedTargetShape)
            {
                Add(
                    violations,
                    "resolution.ability-targets-unsupported",
                    $"{path}.targetCardIds",
                    "Only one AccessedCard target is executable in this ability lifecycle.");
            }
            else if (declaration.Targets.Count == 0
                ? frame.TargetCardIds.Count != 0
                : !AbilityFrameAccessTargetMatches(state, frame, requireLiveSource: frame.Stage is
                    AbilityLifecycleStage.PayCosts
                        or AbilityLifecycleStage.CostCheckpoint
                        or AbilityLifecycleStage.Execute))
            {
                Add(
                    violations,
                    "resolution.ability-target-mismatch",
                    $"{path}.targetCardIds",
                    "Ability targets must retain exactly the current accessed card while live and its exact provenance after resolution.");
            }

            if (declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.Additional))
            {
                ValidateAbilityAdditionalCostSelections(
                    state,
                    frame.AdditionalCostSelections,
                    declaration,
                    path,
                    violations,
                    behaviorCatalog,
                    runtimeCardCatalog,
                    requireLiveSources: frame.Stage == AbilityLifecycleStage.PayCosts);
            }
        }

        if (!Enum.IsDefined(frame.Stage))
        {
            return;
        }

        bool paymentAccepted = frame.Stage != AbilityLifecycleStage.PayCosts;
        bool selfTrash = declaration?.Costs.Any(static cost => cost.Kind == BehaviorCostKind.TrashSelf) == true;
        bool selfRemove = declaration?.Costs.Any(static cost => cost.Kind == BehaviorCostKind.RemoveSelfFromGame) == true;
        if (source is not null && source.ZoneId != frame.SourceZoneId)
        {
            ZoneState? currentZone = state.Zones.FirstOrDefault(zone => zone?.Id == source.ZoneId);
            bool validLifecycleZone = paymentAccepted
                && ((selfTrash
                        && currentZone is not null
                        && (source.OwnerSide == PlayerSide.Runner
                            ? currentZone.Kind == ZoneKind.RunnerHeap
                            : currentZone.Kind == ZoneKind.CorpDiscard))
                    || (selfRemove
                        && currentZone is not null
                        && (source.OwnerSide == PlayerSide.Runner
                            ? currentZone.Kind == ZoneKind.RunnerRemovedFromGame
                            : currentZone.Kind == ZoneKind.CorpRemovedFromGame)));
            behaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? lifecycleDefinition);
            validLifecycleZone |= AbilityLifecycleRules.IsEmptyPoolSelfTrash(lifecycleDefinition, declaration, frame,
                source, currentZone, state.RulesState, state.Turn)
                || AbilityLifecycleRules.IsSelfPlay(declaration, frame, source, currentZone);
            if (!validLifecycleZone || (selfTrash && selfRemove))
            {
                Add(
                    violations,
                    "resolution.ability-source-location-mismatch",
                    $"{path}.sourceZoneId",
                    "Ability source may move only to its owner discard or removed-from-game zone after its declared self-movement payment.");
            }

            bool retainsSourceCounters = state.RulesState?.Counters.Any(counter => counter.CardId == source.Id) == true;
            bool dynamicCounterRetentionAuthorized = paymentAccepted
                && (selfTrash || selfRemove)
                && declaration is not null
                && ContainsBehaviorDynamicAmount(declaration.Instructions);
            if (retainsSourceCounters && (!validLifecycleZone || !dynamicCounterRetentionAuthorized))
            {
                Add(
                    violations,
                    "resolution.ability-source-counters-unauthorized",
                    $"{path}.sourceCardId",
                    "Counters may remain on a moved ability source only after its exact self-movement payment and only when the catalog ability path contains a dynamic credit amount.");
            }
        }

        if (frame.Stage == AbilityLifecycleStage.PayCosts
            && source is not null
            && capturedZone is not null
            && !IsActiveAbilitySource(state, source, capturedZone))
        {
            Add(
                violations,
                "resolution.ability-source-inactive",
                $"{path}.sourceCardId",
                "An ability may begin payment only from an active face-up source.");
        }

        if (controller is not null)
        {
            PlayerSide activeSide = state.Turn.ActiveSide;
            TurnFactsState? facts = state.Turn.History?.ThisTurn(activeSide);
            if (facts is null
                || facts.Side != activeSide
                || facts.TurnNumber != state.Turn.TurnNumber)
            {
                Add(violations, "resolution.ability-turn-facts-missing", path, "Ability lifecycle requires current turn facts.");
            }
            else if (frame.Usage is not null)
            {
                bool used = facts.HasUsedAbility(frame.SourceCardId, frame.AbilityKey);
                if (frame.Usage.Limit == BehaviorAbilityUsageLimit.OncePerTurn
                    && ((!paymentAccepted && used)
                        || (paymentAccepted && !used)
                        || frame.Usage.UsesThisTurn != 0))
                {
                    Add(violations, "resolution.ability-usage-mismatch", $"{path}.usage", "Ability usage snapshot does not match current turn facts.");
                }

                if (frame.Usage.Limit == BehaviorAbilityUsageLimit.Unlimited
                    && frame.Usage.UsesThisTurn != 0)
                {
                    Add(violations, "resolution.ability-usage-mismatch", $"{path}.usage", "Unlimited abilities must retain zero uses.");
                }
            }
        }

        bool parentMatches = false;
        if (parent is TurnFrame turn)
        {
            bool click = frame.Activation == BehaviorAbilityActivation.ClickAction;
            parentMatches = state.RunState is null
                && turn.ActiveSide == state.Turn.ActiveSide
                && state.Turn.ActiveSide == turn.ActiveSide
                && state.Turn.TurnNumber == turn.TurnNumber
                && (click
                    ? turn.ActiveSide == controller?.Side
                        && turn.Stage == TurnFrameStage.AwaitingAction
                        : turn.Stage is (TurnFrameStage.BeforeTurnPriority
                        or TurnFrameStage.ActionPriority
                        or TurnFrameStage.DiscardPriority)
                        && turn.AllowsPaidAbilities
                        && turn.PriorityHolder == controller?.Side);
        }
        else if (parent is RunTimingFrame runFrame && state.RunState is { } run)
        {
            parentMatches = run.Step == RunTimingStep.BreachAccessAbility
                ? frame.Activation == BehaviorAbilityActivation.PaidAbility
                    && (frame.Timing is BehaviorAbilityTiming.AnyPaidWindow
                        or BehaviorAbilityTiming.BreachAccessPaidWindow)
                    && runFrame.RunId == run.Id
                    && runFrame.Step == run.Step
                    && run.Priority is null
                    && controller?.Side == PlayerSide.Runner
                : frame.Activation == BehaviorAbilityActivation.PaidAbility
                    && IsRunPriorityStep(run.Step)
                    && runFrame.RunId == run.Id
                    && runFrame.Step == run.Step
                    && run.Priority is not null
                    && state.Players.FirstOrDefault(player => player.Side == run.Priority.PriorityHolder)?.Id == frame.Controller
                    && (frame.Timing == BehaviorAbilityTiming.AnyPaidWindow
                        || frame.Timing == (run.Step switch
                        {
                            RunTimingStep.InitiationPaidWindow => BehaviorAbilityTiming.InitiationPaidWindow,
                            RunTimingStep.ApproachPaidWindow => BehaviorAbilityTiming.ApproachPaidWindow,
                            RunTimingStep.EncounterPaidWindow => BehaviorAbilityTiming.EncounterPaidWindow,
                            RunTimingStep.MovementPaidWindow => BehaviorAbilityTiming.MovementPaidWindow,
                            RunTimingStep.AfterMovePaidWindow => BehaviorAbilityTiming.AfterMovePaidWindow,
                            _ => (BehaviorAbilityTiming?)null,
                        }));
        }

        if (!parentMatches)
        {
            Add(violations, "resolution.ability-parent-context-mismatch", path, "Ability frame parent is not the matching active paid/action window.");
        }

        if (frame.Stage == AbilityLifecycleStage.AwaitingChild)
        {
            if (index + 1 >= state.ResolutionStack.Count
                || state.ResolutionStack[index + 1] is not BehaviorResolutionFrame child
                || !string.Equals(child.AbilityKey, frame.AbilityKey, StringComparison.Ordinal)
                || child.ParentFrameIdentity is null)
            {
                Add(violations, "resolution.ability-child-missing", path, "An ability awaiting child must have its exact behavior child immediately above it.");
            }
        }
        else if (index + 1 < state.ResolutionStack.Count
            && state.ResolutionStack[index + 1] is BehaviorResolutionFrame { AbilityKey: not null })
        {
            Add(violations, "resolution.unexpected-ability-child", path, "An ability behavior child is only valid while its ability parent awaits the child.");
        }
    }

    private static bool IsActiveAbilitySource(
        GameState state,
        CardInstanceState source,
        ZoneState zone)
    {
        if (GameEngine.IsIdentityOrScoreAbilityZone(zone))
        {
            return GameEngine.IsIdentityOrScoreAbilitySource(source, zone);
        }

        if (source.OwnerSide == PlayerSide.Runner)
        {
            return zone.Kind == ZoneKind.RunnerRig
                && zone.OwnerSide == PlayerSide.Runner
                && zone.ServerId is null
                && state.Zones.Count(candidate => candidate.Kind == ZoneKind.RunnerRig
                    && candidate.OwnerSide == PlayerSide.Runner
                    && candidate.ServerId is null) == 1
                && source.FaceUp;
        }

        return source.OwnerSide == PlayerSide.Corp
            && zone.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce
            && zone.OwnerSide == PlayerSide.Corp
            && zone.ServerId is { } serverId
            && state.Servers.Any(server => server.Id == serverId
                && (zone.Kind == ZoneKind.ServerRoot
                    ? server.RootZoneId == zone.Id
                    : server.IceZoneId == zone.Id))
            && source.FaceUp
            && source.Rezzed;
    }

    private static bool AbilityFrameAccessTargetMatches(
        GameState state,
        AbilityFrame frame,
        bool requireLiveSource)
    {
        if (frame.TargetCardIds.Count != 1
            || state.RunState is not { } run
            || run.Step != RunTimingStep.BreachAccessAbility
            || run.ChosenAccessCards.Count == 0
            || run.ChosenAccessCards[^1] != frame.TargetCardIds[0]
            || !run.AccessedCards.Contains(frame.TargetCardIds[0]))
        {
            return false;
        }

        return !requireLiveSource || IsLiveAccessCard(state, run, frame.TargetCardIds[0]);
    }

    /// <summary>
    /// State-only counterpart of the reducer's shared access-source guard.
    /// It is intentionally identical in topology: hidden central source or
    /// the attacked server root. Archives is admitted only for a caller that
    /// does not require a still-trashable source; score, RFG, and another root
    /// are never admitted.
    /// </summary>
    private static bool IsLiveAccessCard(
        GameState state,
        RunState run,
        CardInstanceId cardId,
        bool requireTrashableSource = true)
    {
        CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate?.Id == cardId);
        ServerState? server = state.Servers.FirstOrDefault(candidate => candidate?.Id == run.AttackedServer);
        ZoneState? zone = card is null
            ? null
            : state.Zones.FirstOrDefault(candidate => candidate?.Id == card.ZoneId
                && candidate.CardIds.Contains(cardId));
        if (card is null
            || card.OwnerSide != PlayerSide.Corp
            || server is null
            || zone is null
            || (zone.ServerId is not null && zone.ServerId != server.Id)
            || zone.OwnerSide != PlayerSide.Corp)
        {
            return false;
        }

        bool inAccessSource = zone.Kind == ZoneKind.ServerRoot
            ? zone.Id == server.RootZoneId
            : server.Kind switch
            {
                ServerKind.Headquarters => zone.Kind == ZoneKind.CorpHand && zone.ServerId is null,
                ServerKind.RAndD => zone.Kind == ZoneKind.CorpDeck && zone.ServerId is null,
                ServerKind.Archives => zone.Kind == ZoneKind.CorpDiscard && zone.ServerId is null,
                _ => false,
            };
        return inAccessSource
            && (!requireTrashableSource || zone.Kind != ZoneKind.CorpDiscard);
    }

    private static void ValidateAbilityAdditionalCostSelections(
        GameState state,
        IReadOnlyList<AbilityAdditionalCostSelection>? selectionSnapshot,
        BehaviorPaidAbility declaration,
        string path,
        List<StateInvariantViolation> violations,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        bool requireLiveSources)
    {
        (BehaviorCost Cost, int Index)[] additional = declaration.Costs
            .Select((cost, index) => (Cost: cost, Index: index))
            .Where(value => value.Cost.Kind == BehaviorCostKind.Additional)
            .ToArray();
        AbilityAdditionalCostSelection[] selections = (selectionSnapshot ?? []).ToArray();
        if (selections.Length != additional.Length)
        {
            Add(
                violations,
                "resolution.ability-additional-cost-selection-mismatch",
                $"{path}.additionalCostSelections",
                "A structured additional cost requires exactly one selection entry per declaration cost.");
            return;
        }

        var used = new HashSet<CardInstanceId>();
        foreach ((BehaviorCost cost, int index) in additional)
        {
            AbilityAdditionalCostSelection[] matches = selections
                .Where(value => value.CostIndex == index)
                .ToArray();
            AbilityAdditionalCostSelection? selection = matches.Length == 1 ? matches[0] : null;
            if (selection is null
                || !string.Equals(selection.CostKey, cost.AdditionalKey, StringComparison.Ordinal))
            {
                Add(
                    violations,
                    "resolution.ability-additional-cost-selection-mismatch",
                    $"{path}.additionalCostSelections",
                    $"Selection for additional cost index {index} does not match its stable declaration key.");
                continue;
            }

            if (cost.AdditionalKind != BehaviorAdditionalCostKind.TrashCards
                || cost.AdditionalTarget is not
                {
                    Kind: BehaviorTargetKind.ZoneCards,
                    Zone: { } sourceZone,
                    Filter: not null,
                    Reveal: false,
                }
                || cost.AdditionalTarget.Filter.OwnerSide is not { } ownerSide)
            {
                Add(
                    violations,
                    "resolution.invalid-ability-additional-cost",
                    $"{path}.costs[{index}]",
                    "TrashCards requires a ZoneCards target with an explicit owner and reveal=false.");
                continue;
            }

            if (selection.CardIds.Count != cost.Amount
                || selection.CardIds.Distinct().Count() != selection.CardIds.Count)
            {
                Add(
                    violations,
                    "resolution.ability-additional-cost-selection-mismatch",
                    $"{path}.additionalCostSelections",
                    $"Selection for additional cost index {index} must contain exactly {cost.Amount} distinct cards.");
                continue;
            }

            BehaviorTarget additionalTarget = cost.AdditionalTarget!;
            BehaviorCardFilter filter = additionalTarget.Filter!;
            ZoneKind expectedZoneKind;
            try
            {
                expectedZoneKind = BehaviorZoneKind(sourceZone, ownerSide);
            }
            catch (ArgumentOutOfRangeException)
            {
                Add(
                    violations,
                    "resolution.invalid-ability-additional-cost",
                    $"{path}.costs[{index}]",
                    "The additional-cost source zone is not supported.");
                continue;
            }

            ZoneState? source = state.Zones.FirstOrDefault(zone => zone.Kind == expectedZoneKind
                && (expectedZoneKind is ZoneKind.ServerRoot or ZoneKind.ServerIce
                    ? zone.OwnerSide == ownerSide
                    : zone.ServerId is null));
            foreach (CardInstanceId cardId in selection.CardIds)
            {
                bool unique = used.Add(cardId);
                CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate?.Id == cardId);
                bool stable = StableIdentifier.TryValidate(cardId.Value);
                bool exists = card is not null;
                bool inSource = !requireLiveSources || source is not null
                    && source.CardIds.Contains(cardId)
                    && card?.ZoneId == source.Id
                    && card.OwnerSide == ownerSide;
                bool matchesFilter = !requireLiveSources
                    || BehaviorCardMatches(state, behaviorCatalog, runtimeCardCatalog, cardId, filter);
                if (!stable || !exists || !unique || !inSource || !matchesFilter)
                {
                    Add(
                        violations,
                        "resolution.ability-additional-cost-selection-stale",
                        $"{path}.additionalCostSelections",
                        !stable || !exists
                            ? $"Selected additional-cost card '{cardId}' is unknown or has an invalid stable identifier."
                            : !unique
                                ? $"Selected additional-cost card '{cardId}' is reused by multiple costs."
                                : !inSource
                                    ? $"Selected additional-cost card '{cardId}' is no longer in its owned source zone."
                                    : $"Selected additional-cost card '{cardId}' no longer matches its declared filter.");
                }
            }
        }
    }

    private static ZoneKind BehaviorZoneKind(BehaviorZone zone, PlayerSide ownerSide) => zone switch
    {
        BehaviorZone.Deck => ownerSide == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack,
        BehaviorZone.Headquarters => ZoneKind.CorpHand,
        BehaviorZone.Archives => ZoneKind.CorpDiscard,
        BehaviorZone.Grip => ZoneKind.RunnerGrip,
        BehaviorZone.Stack => ZoneKind.RunnerStack,
        BehaviorZone.Heap => ZoneKind.RunnerHeap,
        BehaviorZone.Rig => ZoneKind.RunnerRig,
        BehaviorZone.PlayArea => ZoneKind.RunnerPlayArea,
        BehaviorZone.ScoreArea => ownerSide == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore,
        BehaviorZone.ServerIce => ZoneKind.ServerIce,
        BehaviorZone.ServerRoot => ZoneKind.ServerRoot,
        BehaviorZone.RemovedFromGame => ownerSide == PlayerSide.Corp
            ? ZoneKind.CorpRemovedFromGame
            : ZoneKind.RunnerRemovedFromGame,
        BehaviorZone.HostedFaceUp => ownerSide == PlayerSide.Corp
            ? ZoneKind.CorpHostedFaceUp
            : ZoneKind.RunnerHostedFaceUp,
        _ => throw new ArgumentOutOfRangeException(nameof(zone)),
    };

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
        ArgumentNullException.ThrowIfNull(pending);
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
        if (pending.LegalActions!.Count == 0)
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
                case IceSubroutineFrame iceFrame:
                    if (iceFrame.Stage != IceSubroutineFrameStage.AwaitingChoice)
                    {
                        Add(
                            violations,
                            "resolution.pending-stage-mismatch",
                            $"{path}.stage",
                            "An ICE subroutine install parent must remain beneath its install child and cannot own a pending choice.");
                        break;
                    }

                    if (pending is not null)
                    {
                        ValidateIceSubroutineChoiceOwner(
                            state,
                            iceFrame,
                            pending,
                            $"{path}.choices",
                            violations);
                    }

                    break;
                case BehaviorInstallChoiceFrame installChoice:
                    if (pending.Kind != DecisionKind.BehaviorInstallChoice)
                    {
                        Add(violations, "resolution.pending-kind-mismatch", $"{path}.kind", "A behavior install choice requires a BehaviorInstallChoice decision.");
                    }

                    if (pending.Owner != installChoice.Owner)
                    {
                        Add(violations, "resolution.pending-owner-mismatch", $"{path}.owner", "A behavior install decision belongs to its frame owner.");
                    }

                    int expectedInstallActions = installChoice.Choices.Count + (installChoice.Optional ? 1 : 0);
                    if (pending.LegalActions.Count != expectedInstallActions)
                    {
                        Add(violations, "decision.behavior-install-actions", $"{path}.legalActions", "A behavior install decision offers exactly its frozen choices and, when optional, a decline.");
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

        PendingDecision validatedPending = pending ?? throw new ArgumentNullException(nameof(pending));
        var numberChoices = new HashSet<int>();
        var cardChoices = new HashSet<CardInstanceId>();
        for (int index = 0; index < validatedPending.LegalActions!.Count; index++)
        {
            LegalAction? action = validatedPending.LegalActions[index];
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

                        if (behaviorChoice.OptionKeys.Count > 1)
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

                        if (behaviorChoice.OptionKeys.Count == 0
                            && behaviorChoice.CardIds.Any(id => !materialized.CandidateCardIds.Contains(id)))
                        {
                            Add(violations, "decision.behavior-card-not-offered", $"{actionPath}.parameters.cardIds", "Behavior card is not one of the frozen materialized candidates.");
                        }

                        if (!GameEngine.IsBehaviorChoiceActionLegal(
                                state,
                                behaviorCatalog,
                                runtimeCardCatalog,
                                behaviorChoice))
                        {
                            Add(
                                violations,
                                "decision.behavior-choice-payload-invalid",
                                $"{actionPath}.parameters",
                                "Behavior choice parameters do not satisfy the current catalog declaration and authenticated candidate/cost rules.");
                        }
                    }

                    break;
                case (ActionKind.InstallCard, BehaviorInstallChoiceParameters behaviorInstall):
                    if (pending.Kind != DecisionKind.BehaviorInstallChoice
                        || topFrame is not BehaviorInstallChoiceFrame offeringFrame
                        || offeringFrame.Stage != BehaviorInstallChoiceStage.AwaitingChoice
                        || !string.Equals(offeringFrame.FrameIdentity, behaviorInstall.FrameIdentity, StringComparison.Ordinal)
                        || (behaviorInstall.Choice is null
                            ? !offeringFrame.Optional
                            : !offeringFrame.Choices.Contains(behaviorInstall.Choice)))
                    {
                        Add(
                            violations,
                            "decision.behavior-install-action-not-offered",
                            $"{actionPath}.parameters",
                            "A behavior install action must be one of its frame's frozen choices, or a decline of an optional install.");
                    }

                    break;
                case (ActionKind.ResolveIceSubroutine, IceSubroutineChoiceParameters iceChoice):
                    if (pending.Kind != DecisionKind.IceSubroutineChoice)
                    {
                        Add(
                            violations,
                            "decision.action-kind-mismatch",
                            $"{actionPath}.kind",
                            "ICE subroutine actions require an IceSubroutineChoice decision.");
                    }
                    else if (topFrame is not IceSubroutineFrame iceFrame
                        || iceFrame.Stage != IceSubroutineFrameStage.AwaitingChoice
                        || !iceFrame.Choices.Contains(iceChoice))
                    {
                        Add(
                            violations,
                            "decision.ice-subroutine-action-not-offered",
                            $"{actionPath}.parameters",
                            "ICE subroutine action parameters must be one of the exact serialized frame choices.");
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
                        runtimeCardCatalog,
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

                    if (playCard.TargetServerId is { } targetServerId
                        && !state.Servers.Any(server => server?.Id == targetServerId))
                    {
                        Add(
                            violations,
                            "decision.unknown-play-target-server",
                            $"{actionPath}.parameters.targetServerId",
                            $"Run-event play action references unknown server '{targetServerId}'.");
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
                case (ActionKind.TrashAccessedCard or ActionKind.StealAgenda, RunCardPaymentParameters trashAccessed):
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
                case (ActionKind.UseBioroidAbility, RunBioroidAbilityParameters bioroidAbility):
                    if (pending.Kind != DecisionKind.RunTiming)
                    {
                        Add(violations, "decision.action-kind-mismatch", $"{actionPath}.kind", "Run timing actions require a RunTiming decision.");
                    }

                    if (!cardIds.Contains(bioroidAbility.IceId))
                    {
                        Add(violations, "decision.unknown-card-action", $"{actionPath}.parameters.iceId", $"Run action references unknown encountered ICE '{bioroidAbility.IceId}'.");
                    }

                    if (!StableIdentifier.TryValidate(bioroidAbility.SubroutineId.Value))
                    {
                        Add(violations, "decision.invalid-subroutine-action", $"{actionPath}.parameters.subroutineId", "A bioroid ability must identify a stable subroutine.");
                    }

                    if (!StableIdentifier.TryValidate(bioroidAbility.AbilityKey))
                    {
                        Add(violations, "decision.invalid-ability-action", $"{actionPath}.parameters.abilityKey", "A bioroid ability must identify a stable ability key.");
                    }

                    if (!EncounteredBioroidAbilitySpec.IsSupportedCost(bioroidAbility.Cost))
                    {
                        Add(violations, "decision.invalid-bioroid-cost", $"{actionPath}.parameters.cost", "A bioroid ability action must carry one typed lost click or a positive credit cost.");
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
                case (ActionKind.ResolveIceSubroutine, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "ResolveIceSubroutine requires IceSubroutineChoiceParameters.");
                    break;
                case (ActionKind.UseAbility, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "UseAbility requires AbilityActionParameters.");
                    break;
                case (ActionKind.UseBioroidAbility, _):
                    Add(violations, "decision.action-parameters-mismatch", $"{actionPath}.parameters", "UseBioroidAbility requires RunBioroidAbilityParameters.");
                    break;
                default:
                    Add(violations, "decision.invalid-action-kind", $"{actionPath}.kind", $"Action kind '{action.Kind}' is not supported.");
                    break;
            }

            if ((pending.Kind is DecisionKind.ChooseNumber or DecisionKind.Setup && action.Kind != ActionKind.ChooseNumber)
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
                || (pending.Kind is DecisionKind.InstallChoice or DecisionKind.BehaviorInstallChoice
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
                        or ActionKind.StealAgenda
                        or ActionKind.NoAccessAction
                        or ActionKind.UseAbility
                        or ActionKind.UseBioroidAbility))
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
                    && action.Kind != ActionKind.ChooseBehavior)
                || (pending.Kind == DecisionKind.IceSubroutineChoice
                    && action.Kind != ActionKind.ResolveIceSubroutine))
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
            ValidateTurnTimingActions(state, behaviorCatalog, runtimeCardCatalog, pending, turnTiming, path, violations);
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

        if (pending.Kind == DecisionKind.IceSubroutineChoice
            && topFrame is IceSubroutineFrame iceSubroutine)
        {
            ValidateIceSubroutineChoiceActions(
                state,
                behaviorCatalog,
                runtimeCardCatalog,
                pending,
                iceSubroutine,
                path,
                violations);
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

    private static void ValidateIceSubroutineChoiceOwner(
        GameState state,
        IceSubroutineFrame frame,
        PendingDecision pending,
        string path,
        List<StateInvariantViolation> violations)
    {
        PlayerId? expectedOwner = null;
        foreach (IceSubroutineChoiceParameters choice in frame.Choices)
        {
            PlayerSide choiceSide = choice.Choice is IceSubroutineChoiceKind.PayCredits
                or IceSubroutineChoiceKind.TakeTag
                or IceSubroutineChoiceKind.JackOut
                or IceSubroutineChoiceKind.ContinueRun
                ? PlayerSide.Runner
                : PlayerSide.Corp;
            PlayerId? choiceOwner = state.Players.FirstOrDefault(player => player?.Side == choiceSide)?.Id;
            if (choiceOwner is null)
            {
                Add(violations, "resolution.unknown-player", path, $"ICE subroutine choice owner side '{choiceSide}' has no player.");
                continue;
            }

            if (expectedOwner is null)
            {
                expectedOwner = choiceOwner.Value;
            }
            else if (expectedOwner.Value != choiceOwner.Value)
            {
                Add(
                    violations,
                    "resolution.mixed-subroutine-choice-owner",
                    path,
                    "An ICE subroutine frame must contain choices for exactly one decision owner.");
            }
        }

        if (expectedOwner is not null && pending.Owner != expectedOwner.Value)
        {
            Add(
                violations,
                "resolution.pending-owner-mismatch",
                "pendingDecision.owner",
                "ICE subroutine decision owner must match every materialized choice owner.");
        }
    }

    private static void ValidateIceSubroutineChoiceActions(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        PendingDecision pending,
        IceSubroutineFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (!string.Equals(frame.CatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            Add(
                violations,
                "resolution.subroutine-catalog-mismatch",
                $"resolutionStack[{state.ResolutionStack.Count - 1}].catalogFingerprint",
                "An ICE subroutine frame catalog fingerprint must match the active behavior catalog.");
        }

        if (state.RunState is not { } run
            || run.Id != frame.RunId
            || run.Step != RunTimingStep.Encounter
            || run.AttackedServer != frame.ServerId
            || run.Encounter is not { } encounter
            || encounter.Id != frame.EncounterId
            || encounter.IceId != frame.IceId
            || state.Servers.FirstOrDefault(server => server?.Id == run.AttackedServer) is not { } server
            || state.Zones.FirstOrDefault(zone => zone?.Id == server.IceZoneId) is not { } iceZone
            || iceZone.Id != frame.IceZoneId
            || !iceZone.CardIds.SequenceEqual(frame.IceZoneCardIds))
        {
            Add(
                violations,
                "resolution.subroutine-topology-mismatch",
                path,
                "An ICE subroutine frame must retain the exact active run encounter and ICE-zone topology.");
            return;
        }

        IReadOnlyList<IceSubroutineChoiceParameters> expected;
        try
        {
            expected = GameEngine.MaterializeIceSubroutineChoiceParameters(
                state,
                frame,
                behaviorCatalog,
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
                "resolution.subroutine-materialization-failed",
                $"{path}.legalActions",
                $"ICE subroutine choices could not be re-materialized from the authoritative state: {exception.Message}");
            return;
        }

        if (expected.Count == 0)
        {
            Add(
                violations,
                "resolution.empty-subroutine-choice-set",
                $"{path}.choices",
                "A yielded ICE subroutine frame must retain at least one authoritative choice.");
        }

        if (!frame.Choices.SequenceEqual(expected))
        {
            Add(
                violations,
                "resolution.subroutine-choice-set-mismatch",
                $"{path}.choices",
                "An ICE subroutine frame choice set does not match the authoritative catalog/state projection.");
        }

        IceSubroutineChoiceParameters[] actual = pending.LegalActions
            .Where(action => action is not null
                && action.Kind == ActionKind.ResolveIceSubroutine
                && action.Parameters is IceSubroutineChoiceParameters)
            .Select(action => (IceSubroutineChoiceParameters)action!.Parameters)
            .ToArray();
        if (actual.Length != frame.Choices.Count)
        {
            Add(
                violations,
                actual.Length < frame.Choices.Count
                    ? "resolution.missing-subroutine-action"
                    : "resolution.extra-subroutine-action",
                $"{path}.legalActions",
                $"ICE subroutine decision contains {actual.Length} typed action(s), expected exactly {frame.Choices.Count}.");
        }

        if (!actual.SequenceEqual(frame.Choices))
        {
            Add(
                violations,
                "resolution.subroutine-action-bijection",
                $"{path}.legalActions",
                "ICE subroutine legal actions must be an exact ordered bijection of the serialized frame choices.");
        }

        ValidateIceSubroutineChoiceOwner(state, frame, pending, $"{path}.choices", violations);
    }

    private static void ValidateAbilityAction(
        GameState state,
        PendingDecision pending,
        AbilityActionParameters ability,
        HashSet<CardInstanceId> cardIds,
        Dictionary<CardInstanceId, CardInstanceState> cardsById,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
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
                || clickAmount < 1
                || hasLoseClickCost))
        {
            Add(
                violations,
                "decision.invalid-ability-combination",
                path,
                "A ClickAction ability requires exactly one Click cost and action-phase timing.");
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

        bool supportedTargetShape = declaration.Targets.Count == 0
            || declaration.Targets.Count == 1
                && declaration.Targets[0].Kind == BehaviorTargetKind.AccessedCard;
        IReadOnlyList<CardInstanceId> actionTargetSnapshot = ability.TargetCardIds ?? [];
        if (!supportedTargetShape)
        {
            Add(
                violations,
                "decision.ability-targets-unsupported",
                $"{path}.parameters.targetCardIds",
                "Only one AccessedCard target is executable in this ability lifecycle.");
        }
        else if (declaration.Targets.Count == 0)
        {
            if (actionTargetSnapshot.Count != 0)
            {
                Add(
                    violations,
                    "decision.ability-target-mismatch",
                    $"{path}.parameters.targetCardIds",
                    "A no-target ability must carry an empty target snapshot.");
            }
        }
        else if (actionTargetSnapshot.Count != 1
            || state.RunState is not { Step: RunTimingStep.BreachAccessAbility } accessRun
            || !IsLiveAccessCard(state, accessRun, actionTargetSnapshot[0]))
        {
            Add(
                violations,
                "decision.ability-target-mismatch",
                $"{path}.parameters.targetCardIds",
                "An AccessedCard target must be exactly the current live access card.");
        }

        if (declaration.Costs.Any(static cost => cost.Kind == BehaviorCostKind.Additional))
        {
            ValidateAbilityAdditionalCostSelections(
                state,
                ability.AdditionalCostSelections,
                declaration,
                path,
                violations,
                behaviorCatalog,
                runtimeCardCatalog,
                requireLiveSources: true);
        }
        else if (ability.AdditionalCostSelections is { Count: > 0 })
        {
            Add(
                violations,
                "decision.ability-additional-cost-selection-mismatch",
                $"{path}.parameters.additionalCostSelections",
                "An ability without structured additional costs must not carry card selections.");
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
                    or BehaviorCostKind.HostedCredits
                    or BehaviorCostKind.Additional => cost.Amount <= 0,
                BehaviorCostKind.TrashSelf
                    or BehaviorCostKind.RemoveSelfFromGame => cost.Amount != 1,
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

            if (cost.AdditionalKind is { } additionalKind
                && !Enum.IsDefined(additionalKind))
            {
                Add(
                    violations,
                    "decision.invalid-ability-additional-cost",
                    $"{costPath}.additionalKind",
                    "Additional-cost operation is not a supported value.");
            }

            if (cost.Kind == BehaviorCostKind.Additional)
            {
                if (cost.AdditionalKind is null && cost.AdditionalTarget is not null)
                {
                    Add(
                        violations,
                        "decision.invalid-ability-additional-cost",
                        costPath,
                        "A structured additional-cost target requires a typed operation.");
                }

                if (cost.AdditionalKind == BehaviorAdditionalCostKind.TrashCards
                    && (cost.AdditionalTarget is not
                    {
                        Kind: BehaviorTargetKind.ZoneCards,
                        Filter: not null,
                        Reveal: false,
                    }
                    || cost.AdditionalTarget.Filter.OwnerSide is null))
                {
                    Add(
                        violations,
                        "decision.invalid-ability-additional-cost",
                        costPath,
                        "TrashCards requires a ZoneCards target with an explicit owner and reveal=false.");
                }
            }
            else
            {
                if (cost.AdditionalTarget is not null)
                {
                    Add(
                        violations,
                        "decision.invalid-ability-additional-cost",
                        $"{costPath}.additionalTarget",
                        "Only additional costs may carry an additional-cost target.");
                }

                if (cost.AdditionalKind is not null)
                {
                    Add(
                        violations,
                        "decision.invalid-ability-additional-cost",
                        $"{costPath}.additionalKind",
                        "Only additional costs may carry an additional-cost operation.");
                }
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

        if (costs.Count(static cost => cost is not null
                && cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame) > 1)
        {
            Add(
                violations,
                "decision.duplicate-ability-self-movement",
                path,
                "An ability may not declare more than one self-movement cost.");
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
        if (frame.IsNested)
        {
            // EL-NESTED-PLAY: the card is played from inside the behavior below.
            if (index == 0
                || state.ResolutionStack[index - 1] is not BehaviorResolutionFrame nestedParent
                || !string.Equals(GameEngine.BehaviorFrameIdentityOf(nestedParent), frame.ParentFrameIdentity, StringComparison.Ordinal))
            {
                Add(
                    violations,
                    "resolution.card-play-parent-missing",
                    path,
                    "A nested CardPlayFrame requires the behavior frame that played it immediately beneath it.");
            }
        }
        else if (parent is null)
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
                && ParentIdentityMatches(child, behaviorInstall.ParentFrameIdentity))
            // EL-BEHAVIOR-INSTALL: a behavior suspends beneath the zone-install
            // choice its own instruction opened (and that choice's install).
            && !(state.ResolutionStack[index + 2] is BehaviorInstallChoiceFrame zoneChoice
                && ParentIdentityMatches(child, zoneChoice.ParentFrameIdentity))
            // An executing behavior may suspend beneath the effect pipeline its own
            // instruction opened, while that effect's checkpoint resolves a trigger
            // (Public Trail's tag firing NBN: Reality Plus, Wave 4 C1). The effect
            // and checkpoint validators prove the nested suffix.
            && !(child.Stage == BehaviorResolutionStage.Execute
                && state.ResolutionStack[index + 2] is EffectResolutionFrame nestedEffect
                && nestedEffect.Controller == frame.Owner
                && nestedEffect.Stage != EffectResolutionStage.AwaitingMemoryCleanup)
            && !GameEngine.IsSuspendedForMemoryLoss(state.ResolutionStack, index + 1))
        {
            Add(
                violations,
                "resolution.card-play-child-not-top",
                $"{path}.child",
                "The matching CardPlayFrame behavior child must remain top or suspend beneath its supported install or memory-cleanup continuation.");
        }

        if (!IsPlayerSide(frame.Side))
        {
            return;
        }

        ZoneKind sourceKind = frame.Side == PlayerSide.Corp
            ? ZoneKind.CorpHand
            : frame.SourceZoneKind == ZoneKind.RunnerHostedFaceUp ? ZoneKind.RunnerHostedFaceUp : ZoneKind.RunnerGrip;
        ZoneKind discardKind = frame.Side == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap;
        ZoneKind playAreaKind = frame.Side == PlayerSide.Corp ? ZoneKind.CorpPlayArea : ZoneKind.RunnerPlayArea;

        ZoneState[] sourceMatches = state.Zones
            .Where(zone => zone is not null && zone.Kind == sourceKind)
            .Select(zone => zone!)
            .ToArray();
        ZoneState? source = sourceMatches.Length == 1 ? sourceMatches[0] : null;
        if (frame.IsNested
                ? !GameEngine.IsNestedPlaySourceKind(frame.Side, frame.SourceZoneKind)
                : sourceMatches.Length != 1
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
                // A "Double" card's additional click is paid with the action
                // and is not part of the frozen credit cost.
                if (behaviorCost.Kind == BehaviorCostKind.Clicks)
                {
                    continue;
                }

                if (behaviorCost.Kind != BehaviorCostKind.Credits)
                {
                    Add(
                        violations,
                        "resolution.card-play-cost-kind",
                        $"{path}.creditCost",
                        "Every up-front CardPlayFrame behavior cost must be a Credits or additional Clicks cost.");
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

    private static void ValidateCatalogBasicDrawReplacementInvocation(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        EffectCheckpointFrame checkpoint,
        InterruptInvocation invocation,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (invocation.Kind != InterruptKind.Replacement
            || !invocation.Mandatory
            || invocation.BehaviorId != ResolutionBehaviorIds.CardBehavior
            || !string.Equals(invocation.InterruptKey, invocation.ReplacementReasonKey, StringComparison.Ordinal))
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-kind",
                path,
                "A catalog basic-draw invocation must be a mandatory replacement.");
        }

        if (!StableIdentifier.TryValidate(invocation.InterruptKey)
            || !StableIdentifier.TryValidate(invocation.CatalogFingerprint)
            || !StableIdentifier.TryValidate(invocation.DefinitionFingerprint)
            || !StableIdentifier.TryValidate(invocation.ReplacementReasonKey))
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-provenance",
                path,
                "A catalog basic-draw invocation must retain stable interrupt, catalog, definition, and reason provenance.");
        }

        if (invocation.SourceCardId is not { } sourceCardId
            || invocation.SourceDefinitionId is not { } sourceDefinitionId
            || invocation.SourceZoneId is not { } sourceZoneId
            || invocation.SourceOwnerSide is not { } sourceOwnerSide
            || invocation.SourceZoneKind is not { } sourceZoneKind
            || invocation.BasicDrawCount is not { } drawCount
            || invocation.ReplacementReasonKey is not { } reasonKey)
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-source-missing",
                path,
                "A catalog basic-draw invocation must retain a complete source and replacement snapshot.");
            return;
        }

        RequirePlayer(
            new HashSet<PlayerId>(state.Players.Where(player => player is not null).Select(player => player!.Id)),
            invocation.Controller,
            $"{path}.controller",
            violations);
        if (!Enum.IsDefined(sourceOwnerSide) || !Enum.IsDefined(sourceZoneKind))
        {
            Add(violations, "resolution.basic-draw-replacement-source-invalid", path, "A catalog basic-draw source side and zone kind must be defined.");
        }

        CardInstanceState? source = state.Cards.FirstOrDefault(card => card?.Id == sourceCardId);
        if (source is null)
        {
            Add(violations, "resolution.basic-draw-replacement-source-missing", $"{path}.sourceCardId", "The catalog basic-draw source card does not exist.");
        }
        else
        {
            if (source.DefinitionId != sourceDefinitionId)
            {
                Add(violations, "resolution.basic-draw-replacement-definition-mismatch", path, "The catalog basic-draw source definition must match its physical card.");
            }

            if (source.OwnerSide != sourceOwnerSide)
            {
                Add(violations, "resolution.basic-draw-replacement-owner-mismatch", path, "The catalog basic-draw source owner must match its captured owner side.");
            }
        }

        ZoneState? sourceZone = state.Zones.FirstOrDefault(zone => zone?.Id == sourceZoneId);
        if (sourceZone is null || sourceZone.Kind != sourceZoneKind)
        {
            Add(violations, "resolution.basic-draw-replacement-zone-mismatch", $"{path}.sourceZoneId", "The catalog basic-draw source zone snapshot must retain its original zone kind.");
        }
        else if (!RuntimeCardActivity.IsActiveInstalledZone(
                     sourceOwnerSide,
                     sourceZone,
                     state.Zones,
                     state.Servers))
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-source-zone-inactive",
                $"{path}.sourceZoneId",
                "The catalog basic-draw source zone snapshot must be an active installed zone for its captured owner.");
        }

        if (!string.Equals(invocation.CatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            Add(violations, "resolution.basic-draw-replacement-catalog-mismatch", $"{path}.catalogFingerprint", "The catalog basic-draw invocation fingerprint must match the injected catalog.");
        }
        else if (behaviorCatalog.TryGet(sourceDefinitionId, out _)
                 && !string.Equals(
                     invocation.DefinitionFingerprint,
                     behaviorCatalog.GetDefinitionFingerprint(sourceDefinitionId),
                     StringComparison.Ordinal))
        {
            Add(violations, "resolution.basic-draw-replacement-definition-fingerprint-mismatch", $"{path}.definitionFingerprint", "The catalog basic-draw source definition fingerprint must match the injected catalog.");
        }

        if (!Enum.IsDefined(invocation.Occurrence.Kind)
            || invocation.Occurrence.Kind != OccurrenceKind.BasicDraw
            || invocation.Occurrence.Phase != OccurrencePhase.Imminent
            || invocation.Occurrence.Effect is not BasicDrawEffect basicDraw
            || basicDraw.IsReplacement
            || !basicDraw.IsFirstBasicDraw
            || basicDraw.OccurrenceId is not { } occurrenceId
            || occurrenceId != invocation.Occurrence.Id
            || invocation.BasicDrawCount != drawCount
            || invocation.ReplacementEffects.Instructions.Count != 1
            || invocation.ReplacementEffects.Instructions[0] is not BasicDrawEffect replacement
            || !replacement.IsReplacement
            || replacement.IsFirstBasicDraw
            || replacement.Side != basicDraw.Side
            || replacement.DrawCount != drawCount)
        {
            Add(violations, "resolution.basic-draw-replacement-effect-mismatch", path, "The catalog basic-draw invocation must retain the exact typed imminent and replacement effects.");
        }

        if (checkpoint.Occurrence.Id != invocation.Occurrence.Id)
        {
            Add(violations, "resolution.basic-draw-replacement-occurrence-mismatch", path, "The catalog basic-draw invocation occurrence must match its checkpoint.");
        }

        PlayerSide basicDrawSide = invocation.Occurrence.Effect is BasicDrawEffect drawEffect
            ? drawEffect.Side
            : sourceOwnerSide;
        if (sourceOwnerSide != basicDrawSide)
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-side-mismatch",
                path,
                "A catalog basic-draw source must belong to the side performing the draw.");
        }

        PlayerState? sourcePlayer = state.Players.FirstOrDefault(player => player?.Side == sourceOwnerSide);
        PlayerState? drawPlayer = state.Players.FirstOrDefault(player => player?.Side == basicDrawSide);
        if (sourcePlayer is null
            || invocation.Controller != sourcePlayer.Id
            || checkpoint.Controller != sourcePlayer.Id)
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-controller-mismatch",
                path,
                "A catalog basic-draw invocation controller must match the captured source owner and checkpoint controller.");
        }

        if (sourcePlayer is null
            || checkpoint.Occurrence.Controller != sourcePlayer.Id
            || checkpoint.Occurrence.Controller != checkpoint.Controller
            || invocation.Occurrence.Controller != checkpoint.Occurrence.Controller)
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-occurrence-controller-mismatch",
                path,
                "A catalog basic-draw invocation occurrence controller must match the source owner and checkpoint occurrence.");
        }

        if (drawPlayer is null
            || checkpoint.Occurrence.TargetPlayer != drawPlayer.Id
            || invocation.Occurrence.TargetPlayer != checkpoint.Occurrence.TargetPlayer)
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-target-mismatch",
                path,
                "A catalog basic-draw invocation target player must match the basic-draw side and checkpoint occurrence.");
        }

        TurnFactsState? facts = state.Turn.History?.ThisTurn(basicDrawSide);
        if (facts is null
            || facts.Side != basicDrawSide
            || facts.TurnNumber != state.Turn.TurnNumber
            || !facts.TurnBegan
            || facts.BasicDrawOccurrenceIds.Count == 0
            || facts.BasicDrawOccurrenceIds[0] != checkpoint.Occurrence.Id
            || !facts.BasicDrawOccurrenceIds.Contains(checkpoint.Occurrence.Id))
        {
            Add(
                violations,
                "turn.basic-draw-first-ledger-mismatch",
                path,
                "A first basic-draw replacement must retain the matching occurrence as the first global formal-turn ledger entry.");
        }

        if (facts is not null && facts.GetFirstXCount(sourceCardId, reasonKey) != 0)
        {
            Add(
                violations,
                "turn.basic-draw-first-x-already-consumed",
                path,
                "A pending basic-draw replacement source must not already be consumed.");
        }

        if (behaviorCatalog.TryGet(sourceDefinitionId, out CardBehaviorDefinition? definition)
            && definition.FirstBasicDrawReplacements is { Count: > 0 } declarations)
        {
            BehaviorFirstBasicDrawReplacement? declaration = declarations
                .SingleOrDefault(candidate => string.Equals(candidate.ReasonKey, reasonKey, StringComparison.Ordinal));
            if (declaration is null || declaration.DrawCount != drawCount)
            {
                Add(
                    violations,
                    "resolution.basic-draw-replacement-declaration-mismatch",
                    path,
                    "The materialized basic-draw replacement must match a declaration in the pinned source definition.");
            }
        }
        else
        {
            Add(
                violations,
                "resolution.basic-draw-replacement-declaration-missing",
                path,
                "The materialized basic-draw replacement source definition must declare its replacement.");
        }
    }

    private static void ValidateInstalledCatalogTriggerSources(
        EffectCheckpointFrame frame,
        IBehaviorCatalog? behaviorCatalog,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (behaviorCatalog is null)
        {
            return;
        }
        foreach (TriggerInvocation invocation in frame.TriggerQueue)
        {
            if (!invocation.IsCatalogTrigger || invocation.SourceDefinitionId is not { } sourceDefinition
                || !behaviorCatalog.TryGet(sourceDefinition, out CardBehaviorDefinition? definition))
            {
                continue;
            }
            BehaviorTrigger? declaration = definition.Triggers.SingleOrDefault(trigger => trigger.Key == invocation.TriggerKey);
            if (declaration?.RequireSourceInstalled == true
                && invocation.SourceZoneKind is not (ZoneKind.ServerRoot or ZoneKind.ServerIce or ZoneKind.RunnerRig))
            {
                Add(violations, "resolution.trigger-source-not-installed", path,
                    "An installed-only trigger must retain an installed source zone snapshot.");
            }
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

        var expected = new List<(ActionKind Kind, CardInstanceId? Target, int Quantity, int HostedCredits, string Targets)>();
        // VP-STEALTH-CREDITS, VP-POOL-LOCK: as the engine computes them (CreditSources): every credit outside the pool, and
        // no pool credits while the active run event locks the pool.
        int outsidePool = CreditSources.OutsidePool(run, state.Cards, state.Zones, state.RulesState, runtimeCardCatalog);
        bool poolLocked = run.EndReason is null
            && run.ActiveRunEventId is { } lockEventId
            && state.Cards.FirstOrDefault(card => card.Id == lockEventId) is { } lockEvent
            && behaviorCatalog.TryGetRunEvent(lockEvent.DefinitionId, out RunEventBehaviorSpec? lockSpec)
            && lockSpec.LocksCreditPool;
        int Wallet(PlayerState player) => poolLocked ? 0 : player.Credits;
        // Without the runtime card catalog an upgrade's printed trash cost is unknown, so its basic
        // trash may or may not be offered (GameEngine.AccessTrashCost).
        CardInstanceId? unknownTrashCost = null;
        // Pre-targeting saved decisions have no ordinal lists and retain their quantity-only envelope.
        bool exactTargets = pending.LegalActions.Any(action => action?.Parameters is RunBreakParameters { SubroutineOrdinals: not null });
        IReadOnlyList<RezCardParameters> expectedRuntimeRezes = [];
        var expectedRunRezes = new List<RunIceParameters>();
        var expectedBioroidAbilities = new List<RunBioroidAbilityParameters>();
        var expectedHostedBreaks = new HashSet<(CardInstanceId, SubroutineId)>();
        var actualHostedBreaks = new HashSet<(CardInstanceId, SubroutineId)>();
        switch (run.Step)
        {
            case RunTimingStep.InitiationPaidWindow:
            case RunTimingStep.ApproachPaidWindow:
            case RunTimingStep.EncounterPaidWindow:
            case RunTimingStep.MovementPaidWindow:
            case RunTimingStep.AfterMovePaidWindow:
                expected.Add((ActionKind.PassPriority, null, 0, 0, ""));
                if (runtimeCardCatalog is not null)
                {
                    try
                    {
                        expectedRuntimeRezes = GameEngine.MaterializeRezCardParameters(
                            state,
                            behaviorCatalog,
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
                            "run.rez-materialization-failed",
                            $"{path}.legalActions",
                            $"Runtime rez actions could not be materialized from the authoritative state: {exception.Message}");
                    }
                }
                else if (run.Step == RunTimingStep.ApproachPaidWindow
                    && state.Servers.Any(server => server.Id == run.AttackedServer && server.Exists)
                    && run.Priority is { PriorityHolder: PlayerSide.Corp, CorpMayRezApproachedIce: true }
                    && run.CurrentIceId.HasValue)
                {
                    CardInstanceState approachedIce = state.GetCard(run.CurrentIceId.Value);
                    if (behaviorCatalog.TryGetIce(approachedIce.DefinitionId, out IceBehaviorSpec? approachedSpec))
                    {
                        CostQuote? approachedCostQuote = GameEngine.ProjectIceRezQuote(
                            state,
                            behaviorCatalog,
                            run.CurrentIceId.Value,
                            approachedSpec.RezCost);
                        int approachedCost = approachedCostQuote?.EffectiveCost ?? approachedSpec.RezCost;
                        PlayerState? corp = state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Corp);
                        if (corp is not null && corp.Credits >= approachedCost)
                        {
                            expected.Add((ActionKind.RezIce, run.CurrentIceId.Value, 0, 0, ""));
                            expectedRunRezes.Add(new RunIceParameters(
                                run.CurrentIceId.Value,
                                approachedCostQuote));
                        }
                    }
                }

                if (run.Step == RunTimingStep.EncounterPaidWindow
                    && state.Servers.Any(server => server.Id == run.AttackedServer && server.Exists)
                    && run.Priority is { PriorityHolder: PlayerSide.Runner }
                    && run.Encounter is { Status: RunEncounterStatus.Initialized })
                {
                    ServerState? attackedServer = state.Servers.FirstOrDefault(server => server?.Id == run.AttackedServer);

                    // Mirrors the engine's encounter offer: every registered rig
                    // breaker materializes break quantities (interface match,
                    // sufficient current strength, per-activation BreakLimit,
                    // affordability) and a pump (affordability only), in
                    // stored zone/card order; there is no persistent server
                    // attachment.
                    if (attackedServer is not null
                        && behaviorCatalog.TryGetIce(
                            state.GetCard(run.Encounter.IceId).DefinitionId,
                            out IceBehaviorSpec? expectedIceSpec))
                    {
                        ValueQuote? expectedIceQuote = EncounterStrength.ProjectIceQuote(
                            state,
                            behaviorCatalog,
                            run.Encounter.IceId,
                            attackedServer.Kind);
                        if (expectedIceQuote is null)
                        {
                            Add(
                                violations,
                                "run.strength-quote-missing",
                                $"{path}.encounter",
                                "The encountered ICE has no authoritative strength quote.");
                            break;
                        }

                        int iceStrength = expectedIceQuote.EffectiveValue;
                        // Run-scoped hosted credits (Overclock) are spendable
                        // during the run, so they count toward the offer. Each
                        // legal wallet/hosted division is a distinct action.
                        PlayerState? runnerPlayer = state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Runner);

                        if (runnerPlayer is not null
                            && expectedIceSpec.EncounteredBioroidAbilities is { Count: > 0 } bioroidAbilities)
                        {
                            foreach (EncounteredBioroidAbilitySpec ability in bioroidAbilities)
                            {
                                if (ability.Cost.Kind == BehaviorCostKind.Credits
                                        ? (long)Wallet(runnerPlayer) + outsidePool < ability.Cost.Amount
                                        : runnerPlayer.Clicks < ability.Cost.Amount)
                                {
                                    continue;
                                }

                                foreach (RunSubroutineState subroutine in run.Encounter.Subroutines)
                                {
                                    if (subroutine.Broken || subroutine.Resolved)
                                    {
                                        continue;
                                    }

                                    expectedBioroidAbilities.Add(new RunBioroidAbilityParameters(
                                        run.Encounter.IceId,
                                        subroutine.Id,
                                        ability.Key,
                                        ability.Cost,
                                        subroutine.Ordinal));
                                }
                            }
                        }

                        foreach (CardInstanceState hosted in state.Cards)
                        {
                            if (GameEngine.CanBreakHostedCounter(state, behaviorCatalog, hosted, run))
                            {
                                foreach (RunSubroutineState subroutine in run.Encounter.Subroutines.Where(value => !value.Broken && !value.Resolved))
                                {
                                    expectedHostedBreaks.Add((hosted.Id, subroutine.Id));
                                }
                            }
                        }

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

                                ValueQuote? expectedBreakerQuote = EncounterStrength.ProjectBreakerQuote(
                                    state,
                                    behaviorCatalog,
                                    cardId);
                                if (runnerPlayer is not null
                                    && expectedBreakerQuote is not null
                                    && IceInterfaceMatcher.CanInterface(
                                        expectedIceSpec,
                                        GameEngine.IceGainsEveryType(state.Cards, behaviorCatalog, run.Encounter.IceId),
                                        expectedBreakerSpec,
                                        GameEngine.CardSubtypes(runtimeCardCatalog, card.DefinitionId))
                                    && expectedBreakerQuote.EffectiveValue >= iceStrength
                                    && (long)Wallet(runnerPlayer) + outsidePool >= (GameEngine.ProjectCostQuote(
                                        state,
                                        behaviorCatalog,
                                        cardId,
                                        BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                                        expectedBreakerSpec.BreakCost)?.EffectiveCost ?? expectedBreakerSpec.BreakCost))
                                {
                                    CostQuote? expectedBreakQuote = GameEngine.ProjectCostQuote(
                                        state,
                                        behaviorCatalog,
                                        cardId,
                                        BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                                        expectedBreakerSpec.BreakCost);
                                    int expectedBreakCost = expectedBreakQuote?.EffectiveCost ?? expectedBreakerSpec.BreakCost;
                                    int[] availableTargets = run.Encounter.Subroutines.Where(sub => !sub.Broken && !sub.Resolved)
                                        .Select(sub => sub.Ordinal).ToArray();
                                    foreach (IReadOnlyList<int> targets in SubroutineTargeting.Choices(availableTargets, expectedBreakerSpec.BreakLimit)
                                        .Where(targets => exactTargets || targets.SequenceEqual(availableTargets.Take(targets.Count))))
                                    {
                                        int quantity = targets.Count;
                                        foreach (int hostedCredits in LegalHostedCreditSplits(
                                            Wallet(runnerPlayer),
                                            outsidePool,
                                            expectedBreakCost))
                                        {
                                            expected.Add((
                                                ActionKind.BreakSubroutines,
                                                cardId,
                                                quantity,
                                                hostedCredits, exactTargets ? string.Join(",", targets) : ""));
                                        }
                                    }
                                }

                                bool dynamicPumpAvailable = expectedBreakerSpec.DynamicPump is null
                                    || EncounterStrength.TryResolveDynamicPump(
                                        expectedBreakerSpec,
                                        cardId,
                                        state.Cards,
                                        state.Zones,
                                        behaviorCatalog,
                                        out _,
                                        out _);
                                int expectedPumpCost = GameEngine.ProjectCostQuote(
                                    state,
                                    behaviorCatalog,
                                    cardId,
                                    BehaviorCostModifierKind.IcebreakerPumpCreditCost,
                                    expectedBreakerSpec.PumpCost)?.EffectiveCost ?? expectedBreakerSpec.PumpCost;
                                if (runnerPlayer is not null
                                    && expectedBreakerSpec.HasPump
                                    && dynamicPumpAvailable
                                    && (long)Wallet(runnerPlayer) + outsidePool >= expectedPumpCost)
                                {
                                    foreach (int hostedCredits in LegalHostedCreditSplits(
                                        Wallet(runnerPlayer),
                                        outsidePool,
                                        expectedPumpCost))
                                    {
                                        expected.Add((
                                            ActionKind.PumpBreaker,
                                            cardId,
                                            0,
                                            hostedCredits, ""));
                                    }
                                }
                            }
                        }
                    }
                }

                break;
            case RunTimingStep.JackOutDecision:
                expected.Add((ActionKind.JackOut, null, 0, 0, ""));
                expected.Add((ActionKind.ContinueRun, null, 0, 0, ""));
                break;
            case RunTimingStep.BreachCandidateSelection:
                foreach (CardInstanceId candidate in run.BreachCandidates)
                {
                    expected.Add((ActionKind.ChooseBreachCandidate, candidate, 0, 0, ""));
                }

                ServerState? breachServer = state.Servers.FirstOrDefault(server => server?.Id == run.AttackedServer);
                ZoneState? hiddenZone = breachServer?.Kind switch
                {
                    ServerKind.Headquarters => state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.CorpHand && zone.ServerId is null),
                    ServerKind.RAndD => state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.CorpDeck && zone.ServerId is null),
                    _ => null,
                };
                if (breachServer is not null
                    && RunState.HiddenBreachCandidate(
                        breachServer.Kind,
                        hiddenZone,
                        run.RandomAccessLimit,
                        run.AccessedCards) is not null)
                {
                    expected.Add((ActionKind.ChooseBreachCandidate, null, 0, 0, ""));
                }

                break;
            case RunTimingStep.BreachAccessAbility:
                if (run.ChosenAccessCards.Count > 0)
                {
                    CardInstanceId accessedCardId = run.ChosenAccessCards[^1];
                    CardInstanceState? accessedCard = state.Cards.FirstOrDefault(
                        candidate => candidate?.Id == accessedCardId);
                    bool currentAccess = accessedCard is not null
                        && IsLiveAccessCard(state, run, accessedCardId, requireTrashableSource: false);
                    bool trashableAccess = currentAccess
                        && IsLiveAccessCard(state, run, accessedCardId, requireTrashableSource: true);

                    // The basic trash is available only while the exact
                    // accessed card remains in the attacked server's source
                    // zone. A moved/discarded/replaced card leaves only the
                    // no-action path; the ability bijection below separately
                    // materializes structured access abilities for both
                    // agendas and non-agendas.
                    bool accessedAgenda = currentAccess
                        && behaviorCatalog.TryGetAgenda(accessedCard!.DefinitionId, out _);
                    if (trashableAccess
                        && !accessedAgenda
                        && !run.ForbidsStealOrTrash(accessedCard!.DefinitionId))
                    {
                        if (runtimeCardCatalog is null && !behaviorCatalog.TryGetAsset(accessedCard!.DefinitionId, out _))
                        {
                            unknownTrashCost = accessedCardId;
                        }
                        else if (GameEngine.AccessTrashPayment(state, behaviorCatalog, runtimeCardCatalog, accessedCardId) is { } trashPayment)
                        {
                            int trashCost = trashPayment.Remaining;
                            PlayerState? trashRunner = state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Runner);
                            if (trashRunner is not null && (long)Wallet(trashRunner) + outsidePool >= trashCost)
                            {
                                foreach (int hostedCredits in LegalHostedCreditSplits(
                                    Wallet(trashRunner),
                                    outsidePool,
                                    trashCost))
                                {
                                    expected.Add((
                                        ActionKind.TrashAccessedCard,
                                        accessedCardId,
                                        0,
                                        hostedCredits, ""));
                                }
                            }
                        }
                    }
                }

                // VP-SCORE-STEAL-COST: as the engine offers it (StealCosts).
                if (run.ChosenAccessCards.Count > 0
                    && state.Cards.FirstOrDefault(candidate => candidate?.Id == run.ChosenAccessCards[^1]) is { } stealCandidate
                    && IsLiveAccessCard(state, run, stealCandidate.Id, requireTrashableSource: false)
                    && !run.ForbidsStealOrTrash(stealCandidate.DefinitionId)
                    && behaviorCatalog.TryGetAgenda(stealCandidate.DefinitionId, out _)
                    && StealCosts.For(state.Cards, state.Zones, behaviorCatalog, stealCandidate.DefinitionId) is var stealCost
                    && (stealCost.Clicks > 0 || stealCost.Credits > 0)
                    && state.Players.FirstOrDefault(player => player?.Side == PlayerSide.Runner) is { } stealer
                    && stealer.Clicks >= stealCost.Clicks
                    && (long)Wallet(stealer) + outsidePool >= stealCost.Credits)
                {
                    foreach (int hostedCredits in LegalHostedCreditSplits(Wallet(stealer), outsidePool, stealCost.Credits))
                    {
                        expected.Add((ActionKind.StealAgenda, stealCandidate.Id, 0, hostedCredits, ""));
                    }
                }

                expected.Add((ActionKind.NoAccessAction, null, 0, 0, ""));
                break;
            default:
                Add(violations, "run.unexpected-prompt", $"{path}.legalActions", $"Run step '{run.Step}' must not have a pending decision.");
                return;
        }

        var actual = new List<(ActionKind Kind, CardInstanceId? Target, int Quantity, int HostedCredits, string Targets)>();
        var actualRuntimeRezes = new List<RezCardParameters>();
        var actualRunRezes = new List<RunIceParameters>();
        var actualBioroidAbilities = new List<RunBioroidAbilityParameters>();
        var actualAbilities = new List<AbilityActionParameters>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            if (action.Kind == ActionKind.UseAbility)
            {
                if (action.Parameters is AbilityActionParameters ability)
                {
                    actualAbilities.Add(ability);
                }

                continue;
            }

            if (action.Kind == ActionKind.UseBioroidAbility)
            {
                if (action.Parameters is RunBioroidAbilityParameters bioroidAbility)
                {
                    actualBioroidAbilities.Add(bioroidAbility);
                }

                continue;
            }

            switch (action.Kind, action.Parameters)
            {
                case (ActionKind.PassPriority, NoActionParameters):
                case (ActionKind.JackOut, NoActionParameters):
                case (ActionKind.ContinueRun, NoActionParameters):
                    actual.Add((action.Kind, null, 0, 0, ""));
                    break;
                case (ActionKind.RezIce, RunIceParameters rez):
                    actual.Add((action.Kind, rez.IceId, 0, 0, ""));
                    actualRunRezes.Add(rez);
                    break;
                case (ActionKind.RezCard, RezCardParameters rez):
                    actualRuntimeRezes.Add(rez);
                    break;
                case (ActionKind.BreakSubroutines, RunBreakParameters breakSubroutinesAction):
                    CardInstanceState? breakSource = state.Cards.FirstOrDefault(
                        candidate => candidate?.Id == breakSubroutinesAction.BreakerId);
                    if (breakSource is not null && behaviorCatalog.TryGet(breakSource.DefinitionId, out CardBehaviorDefinition? breakDefinition)
                        && breakDefinition.Hosting?.BreakHostSubroutine == true)
                    {
                        if (breakSubroutinesAction.SubroutineId is not { } target || breakSubroutinesAction.Quantity != 1
                            || breakSubroutinesAction.HostedCredits != 0 || breakSubroutinesAction.CostQuote is not null
                            || !actualHostedBreaks.Add((breakSource.Id, target))
                            || (breakSubroutinesAction.SubroutineOrdinals is { } hostedTargets
                                && (hostedTargets.Count != 1 || !run.Encounter!.Subroutines.Any(sub => sub.Id == target && sub.Ordinal == hostedTargets[0]))))
                        {
                            Add(violations, "run.invalid-hosted-break", path, "Hosted breaks require one distinct target and one counter payment.");
                        }
                        break;
                    }
                    if (breakSubroutinesAction.SubroutineId is not null)
                    {
                        Add(violations, "run.invalid-break-target", path, "Ordinary breakers cannot contain a hosted target.");
                    }
                    CostQuote? expectedBreakQuote = null;
                    if (breakSource is not null
                        && behaviorCatalog.TryGetIcebreaker(
                            breakSource.DefinitionId,
                            out IcebreakerBehaviorSpec? breakSourceSpec))
                    {
                        expectedBreakQuote = GameEngine.ProjectCostQuote(
                            state,
                            behaviorCatalog,
                            breakSource.Id,
                            BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                            breakSourceSpec.BreakCost);
                    }

                    if (breakSubroutinesAction.CostQuote is null
                        ? expectedBreakQuote is not null
                        : !breakSubroutinesAction.CostQuote.Equals(expectedBreakQuote))
                    {
                        Add(
                            violations,
                            "run.break-cost-quote-mismatch",
                            $"{path}.legalActions",
                            "Break action cost quote must match the authoritative breaker definition and current turn facts.");
                    }

                    actual.Add((
                        action.Kind,
                        breakSubroutinesAction.BreakerId,
                        breakSubroutinesAction.Quantity,
                        breakSubroutinesAction.HostedCredits, breakSubroutinesAction.SubroutineOrdinals is { } ordinals ? string.Join(",", ordinals) : ""));
                    break;
                case (ActionKind.PumpBreaker, RunBreakerParameters pumpBreaker):
                    actual.Add((action.Kind, pumpBreaker.BreakerId, 0, pumpBreaker.HostedCredits, ""));
                    break;
                case (ActionKind.ChooseBreachCandidate, BreachCandidateParameters breachCandidate):
                    actual.Add((action.Kind, breachCandidate.CardId, 0, 0, ""));
                    break;
                case (ActionKind.NoAccessAction, NoActionParameters):
                    actual.Add((action.Kind, null, 0, 0, ""));
                    break;
                case (ActionKind.TrashAccessedCard, RunCardPaymentParameters trash):
                    actual.Add((action.Kind, trash.CardId, 0, trash.HostedCredits, ""));
                    break;
                case (ActionKind.StealAgenda, RunCardPaymentParameters steal):
                    actual.Add((action.Kind, steal.CardId, 0, steal.HostedCredits, ""));
                    break;
            }
        }

        if (unknownTrashCost is { } unknownTarget)
        {
            actual.RemoveAll(value => value.Kind == ActionKind.TrashAccessedCard && value.Target == unknownTarget);
        }

        var expectedCounts = expected.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        var actualCounts = actual.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        foreach (((ActionKind Kind, CardInstanceId? Target, int Quantity, int HostedCredits, string Targets) spec, int count) in expectedCounts)
        {
            actualCounts.TryGetValue(spec, out int actualCount);
            if (actualCount != count)
            {
                Add(
                    violations,
                    actualCount == 0 ? "run.missing-action" : "run.action-count-mismatch",
                    $"{path}.legalActions",
                    $"Run timing decision requires exactly {count} '{spec.Kind}' action(s) for target '{spec.Target?.ToString() ?? "none"}', "
                    + $"quantity {spec.Quantity}, targets [{spec.Targets}], hosted credits {spec.HostedCredits}, but contains {actualCount}.");
            }
        }

        foreach (((ActionKind Kind, CardInstanceId? Target, int Quantity, int HostedCredits, string Targets) spec, int count) in actualCounts)
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
                    + $"quantity {spec.Quantity}, targets [{spec.Targets}], hosted credits {spec.HostedCredits}, expected {expectedCount}.");
            }
        }

        if (!expectedHostedBreaks.SetEquals(actualHostedBreaks))
        {
            Add(violations, "run.hosted-break-target-mismatch", path,
                "Hosted counter break actions must name exactly every currently unbroken host subroutine.");
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

        ValidateRunIceActionBijection(
            expectedRunRezes,
            actualRunRezes,
            path,
            violations);

        ValidateBioroidAbilityActionBijection(
            expectedBioroidAbilities,
            actualBioroidAbilities,
            path,
            violations);

        IReadOnlyList<AbilityActionParameters> expectedAbilities = [];
        try
        {
            expectedAbilities = GameEngine.MaterializeAbilityActionParameters(state, behaviorCatalog, runtimeCardCatalog);
            if (run.ForbidsStealOrTrash(run.ChosenAccessCards.Count > 0
                    ? state.Cards.FirstOrDefault(card => card?.Id == run.ChosenAccessCards[^1])?.DefinitionId
                    : null))
            {
                // Mirror the reducer's declaration-authenticated filter: an
                // access ability is suppressed only if its typed behavior can
                // trash a Corp card. Unrelated breach-access abilities remain
                // part of the exact legal-action bijection.
                expectedAbilities = expectedAbilities
                    .Where(parameters => !GameEngine.IsAbilityBlockedByRunRestriction(
                        behaviorCatalog,
                        parameters))
                    .ToArray();
            }
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException
            or OverflowException)
        {
            Add(
                violations,
                "decision.ability-materialization-failed",
                $"{path}.legalActions",
                $"Run timing ability actions could not be materialized from the authoritative state: {exception.Message}");
        }

        ValidateAbilityActionBijection(
            expectedAbilities,
            actualAbilities,
            path,
            violations,
            windowName: "Run timing");

        if (pending.Owner != RunDecisionOwner(state, run))
        {
            Add(violations, "run.decision-owner-mismatch", $"{path}.owner", "Run timing decision owner does not match the current priority/Runner decision owner.");
        }

        static IEnumerable<int> LegalHostedCreditSplits(
            int wallet,
            int outside,
            int amount)
        {
            int minimumHosted = Math.Max(0, amount - wallet);
            int maximumHosted = Math.Min(amount, outside);
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

            RezCardParameters? expectedTarget = expected.FirstOrDefault(item => item.CardId == candidate.CardId
                && item.ForfeitAgendaId == candidate.ForfeitAgendaId);
            if (expectedTarget is not null
                && (expectedTarget.CreditCost != candidate.CreditCost
                    || (expectedTarget.CostQuote is null
                        ? candidate.CostQuote is not null
                        : !expectedTarget.CostQuote.Equals(candidate.CostQuote))))
            {
                Add(
                    violations,
                    costMismatchCode,
                    $"{path}.legalActions",
                    $"Runtime rez action for '{candidate.CardId}' carries a frozen cost/quote that does not match the authoritative expected cost {expectedTarget.CreditCost}.");
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

    private static void ValidateRunIceActionBijection(
        IReadOnlyList<RunIceParameters> expected,
        IReadOnlyList<RunIceParameters> actual,
        string path,
        List<StateInvariantViolation> violations)
    {
        foreach (RunIceParameters candidate in actual)
        {
            RunIceParameters? expectedTarget = expected.FirstOrDefault(item => item.IceId == candidate.IceId);
            if (expectedTarget is not null
                && !CostQuotesEqual(expectedTarget.CostQuote, candidate.CostQuote))
            {
                Add(
                    violations,
                    "run.rez-cost-quote-mismatch",
                    $"{path}.legalActions",
                    $"Approached ICE rez action for '{candidate.IceId}' carries a cost quote that does not match the authoritative quote.");
            }
        }

        foreach (RunIceParameters candidate in expected)
        {
            if (!actual.Any(item => item.IceId == candidate.IceId
                && CostQuotesEqual(item.CostQuote, candidate.CostQuote)))
            {
                Add(
                    violations,
                    "run.missing-rez-action",
                    $"{path}.legalActions",
                    $"Run timing decision is missing the materialized approached ICE rez action for '{candidate.IceId}'.");
            }
        }
    }

    private static void ValidateBioroidAbilityActionBijection(
        IReadOnlyList<RunBioroidAbilityParameters> expected,
        List<RunBioroidAbilityParameters> actual,
        string path,
        List<StateInvariantViolation> violations)
    {
        for (int index = 0; index < actual.Count; index++)
        {
            RunBioroidAbilityParameters candidate = actual[index];
            if (actual.Take(index).Any(item => item == candidate))
            {
                Add(
                    violations,
                    "run.duplicate-bioroid-action",
                    $"{path}.legalActions",
                    $"Bioroid ability action for ICE '{candidate.IceId}', subroutine '{candidate.SubroutineId}', and ability '{candidate.AbilityKey}' is duplicated.");
            }

            if (!expected.Any(item => item == candidate))
            {
                Add(
                    violations,
                    "run.extra-bioroid-action",
                    $"{path}.legalActions",
                    $"Run timing decision contains an unmaterialized bioroid ability action for ICE '{candidate.IceId}', subroutine '{candidate.SubroutineId}', and ability '{candidate.AbilityKey}'.");
            }
        }

        foreach (RunBioroidAbilityParameters candidate in expected)
        {
            if (!actual.Any(item => item == candidate))
            {
                Add(
                    violations,
                    "run.missing-bioroid-action",
                    $"{path}.legalActions",
                    $"Run timing decision is missing the materialized bioroid ability action for ICE '{candidate.IceId}', subroutine '{candidate.SubroutineId}', and ability '{candidate.AbilityKey}'.");
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

    private static bool ContainsBehaviorDynamicAmount(
        IReadOnlyList<BehaviorInstruction> instructions)
    {
        foreach (BehaviorInstruction instruction in instructions)
        {
            if (instruction.Kind == BehaviorInstructionKind.GainCredits
                && instruction.AmountOperand is { Kind: null or BehaviorAmountKind.SourceCounter })
            {
                return true;
            }

            if (instruction.Branch is { } branch
                && (ContainsBehaviorDynamicAmount(branch.WhenTrue)
                    || ContainsBehaviorDynamicAmount(branch.WhenFalse)))
            {
                return true;
            }

            if (instruction.Choice is { } choice
                && choice.Options.Any(option => ContainsBehaviorDynamicAmount(option.Instructions)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCorpInstallableRuntimeClass(RuntimeCardClass cardClass) =>
        cardClass is RuntimeCardClass.Agenda
            or RuntimeCardClass.Asset
            or RuntimeCardClass.Upgrade
            or RuntimeCardClass.Ice;

    private static void ValidateIceSubroutineInstallFrame(
        GameState state,
        InstallFrame install,
        int index,
        string path,
        PlayerState? installOwner,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
    {
        bool inward = install.InitiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts;
        if (installOwner is null || installOwner.Side != PlayerSide.Corp)
        {
            Add(
                violations,
                "resolution.install-owner-side",
                $"{path}.owner",
                "ICE subroutine install frames must be owned by the Corp.");
        }

        IceSubroutineFrame? parent = index > 0
            ? state.ResolutionStack[index - 1] as IceSubroutineFrame
            : null;
        if (parent is null)
        {
            Add(
                violations,
                "resolution.install-parent-missing",
                path,
                "An ICE subroutine install frame requires an immediately preceding ICE subroutine parent frame.");
            return;
        }

        if (parent.Stage != IceSubroutineFrameStage.AwaitingInstall)
        {
            Add(
                violations,
                "resolution.install-parent-stage",
                $"{path}.parentFrameIdentity",
                "An ICE subroutine install frame requires an AwaitingInstall parent stage.");
        }

        if (!string.Equals(install.ParentFrameIdentity, parent.FrameIdentity, StringComparison.Ordinal))
        {
            Add(
                violations,
                "resolution.install-parent-identity-mismatch",
                $"{path}.parentFrameIdentity",
                "ICE subroutine install parent identity must match the exact serialized ICE frame.");
        }

        RunState? run = state.RunState;
        bool runMatches = run is not null
            && run.Id == parent.RunId
            && run.Step == RunTimingStep.Encounter
            && run.AttackedServer == parent.ServerId
            && run.CurrentIceId == parent.IceId
            && run.Encounter is { Status: RunEncounterStatus.Initialized } encounter
            && encounter.Id == parent.EncounterId
            && encounter.IceId == parent.IceId
            && encounter.Subroutines.Any(subroutine => subroutine.Id == parent.SubroutineId
                && subroutine.Ordinal == parent.Ordinal
                && !subroutine.Broken
                && !subroutine.Resolved);
        if (!runMatches)
        {
            Add(
                violations,
                "resolution.subroutine-run-mismatch",
                path,
                "An ICE subroutine install frame must match the live unresolved encounter subroutine.");
        }

        ZoneState? parentIceZone = state.Zones.FirstOrDefault(zone => zone?.Id == parent.IceZoneId);
        if (parentIceZone is null
            || parentIceZone.Kind != ZoneKind.ServerIce
            || parentIceZone.OwnerSide != PlayerSide.Corp
            || parentIceZone.ServerId != parent.ServerId
            || !parentIceZone.CardIds.SequenceEqual(parent.IceZoneCardIds)
            || !parentIceZone.CardIds.Contains(parent.IceId))
        {
            Add(
                violations,
                "resolution.subroutine-topology-mismatch",
                $"{path}.parentFrameIdentity",
                "An ICE subroutine install child must retain the exact live attacked ICE-zone topology captured by its parent.");
        }

        ZoneState? source = state.Zones.FirstOrDefault(zone => zone?.Id == install.SourceZoneId);
        if (source is null
            || install.SourceZoneKind is not (ZoneKind.CorpHand or ZoneKind.CorpDiscard)
            || source.Kind != install.SourceZoneKind
            || source.OwnerSide != PlayerSide.Corp
            || source.ServerId is not null
            || !source.CardIds.Contains(install.CardId))
        {
            Add(
                violations,
                "resolution.install-source-mismatch",
                $"{path}.sourceZoneId",
                "ICE subroutine installs must source a Corp card from the captured HQ or Archives zone.");
        }

        CardInstanceState? card = state.Cards.FirstOrDefault(candidate => candidate?.Id == install.CardId);
        if (card is null)
        {
            Add(violations, "resolution.unknown-install-card", $"{path}.cardId", $"Install card '{install.CardId}' does not exist.");
        }
        else if (card.ZoneId != install.SourceZoneId
            || card.OwnerSide != PlayerSide.Corp
            || card.DefinitionId != install.DefinitionId)
        {
            Add(
                violations,
                "resolution.install-card-mismatch",
                path,
                "ICE subroutine install card must remain the authenticated Corp physical card in its captured source zone.");
        }

        if (runtimeCardCatalog is not null
            && card is not null
            && runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? runtime))
        {
            if (runtime.Side != PlayerSide.Corp
                || !IsCorpInstallableRuntimeClass(runtime.CardClass)
                || install.CardClass != runtime.CardClass
                || (inward && runtime.CardClass != RuntimeCardClass.Ice)
                || (!inward
                    && install.DestinationKind == InstallDestinationKind.ServerIce
                    && runtime.CardClass != RuntimeCardClass.Ice)
                || (!inward
                    && install.DestinationKind == InstallDestinationKind.ServerRoot
                    && runtime.CardClass == RuntimeCardClass.Ice))
            {
                Add(
                    violations,
                    "resolution.install-runtime-mismatch",
                    path,
                    "ICE subroutine install class and destination must match authoritative Corp runtime metadata.");
            }
        }

        if (install.CostQuote is not null)
        {
            Add(
                violations,
                "resolution.install-cost-quote-invalid",
                $"{path}.costQuote",
                "Corp ICE subroutine installs must not carry a Runner self-cost quote.");
        }

        if (inward)
        {
            if (install.DestinationKind != InstallDestinationKind.ServerIce
                || install.ServerId != parent.ServerId
                || install.CreateNewRemote
                || install.DestinationZoneId != parent.IceZoneId
                || install.InwardAnchorIceId != parent.IceId
                || install.InstallCost != 0
                || install.CandidateReplacementCardIds.Count != 0
                || install.RequiredReplacementCardIds.Count != 0
                || install.SelectedReplacementCardIds.Count != 0)
            {
                Add(
                    violations,
                    "resolution.subroutine-inward-install-invalid",
                    path,
                    "A directly-inward Brân install must target the current attacked ICE zone, anchor Brân, and carry zero cost without replacements.");
            }
        }
        else
        {
            if (install.InwardAnchorIceId is not null
                || install.DestinationKind is not (InstallDestinationKind.ServerRoot or InstallDestinationKind.ServerIce))
            {
                Add(
                    violations,
                    "resolution.subroutine-normal-install-invalid",
                    path,
                    "A normal ICE subroutine install may target only a Corp server root or ICE zone and must not retain an inward anchor.");
            }

            if (install.CreateNewRemote)
            {
                if (install.ServerId is not null || install.DestinationZoneId is not null)
                {
                    Add(
                        violations,
                        "resolution.install-destination-remote",
                        path,
                        "A normal ICE subroutine new-remote install must omit server and physical destination identities until commit.");
                }
            }
            else
            {
                ServerState? server = install.ServerId is { } serverId
                    ? state.Servers.FirstOrDefault(candidate => candidate?.Id == serverId)
                    : null;
                ZoneState? destination = server is null
                    ? null
                    : state.Zones.FirstOrDefault(zone => zone?.Id == (install.DestinationKind == InstallDestinationKind.ServerIce
                        ? server.IceZoneId
                        : server.RootZoneId));
                if (server is null
                    || destination is null
                    || install.DestinationZoneId != destination.Id
                    || destination.OwnerSide != PlayerSide.Corp
                    || destination.Kind != (install.DestinationKind == InstallDestinationKind.ServerIce
                        ? ZoneKind.ServerIce
                        : ZoneKind.ServerRoot)
                    || destination.ServerId != server.Id)
                {
                    Add(
                        violations,
                        "resolution.install-destination-mismatch",
                        $"{path}.destinationZoneId",
                        "A normal ICE subroutine install must retain the exact physical existing-server destination.");
                }
            }
        }
    }

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

        bool hasAbilityInstallTopology = index >= 3
            && state.ResolutionStack[index - 2] is AbilityFrame
            && state.ResolutionStack[index - 3] is TurnFrame or RunTimingFrame;
        bool hasCardPlayInstallTopology = index >= 3
            && state.ResolutionStack[index - 2] is CardPlayFrame
            && state.ResolutionStack[index - 3] is TurnFrame;
        bool hasTriggerInstallTopology = index >= 3
            && state.ResolutionStack[index - 2] is EffectCheckpointFrame
            && state.ResolutionStack[index - 3] is EffectResolutionFrame;
        if (!hasAbilityInstallTopology && !hasCardPlayInstallTopology && !hasTriggerInstallTopology)
        {
            Add(
                violations,
                "resolution.install-parent-topology",
                path,
                "A behavior-effect install frame requires an exact card-play, ability, or catalog-trigger parent topology.");
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

        IReadOnlyList<BehaviorInstruction> rootInstructions = behaviorParent.AbilityKey is not null
            ? definition.PaidAbilities
                .SingleOrDefault(ability => string.Equals(
                    ability.Key,
                    behaviorParent.AbilityKey,
                    StringComparison.Ordinal))
                ?.Instructions
                ?? []
            : behaviorParent.TriggerKey is not null
                ? definition.Triggers
                    .SingleOrDefault(trigger => string.Equals(
                        trigger.Key,
                        behaviorParent.TriggerKey,
                        StringComparison.Ordinal))
                    ?.Instructions
                    ?? []
                : definition.Instructions;
        List<BehaviorInstruction> replay = rootInstructions.ToList();
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
                SourceZone: BehaviorZone sourceZone,
                DestinationZone: BehaviorZone.Rig,
            }
            || sourceZone is not (BehaviorZone.Stack or BehaviorZone.Grip))
        {
            Add(
                violations,
                "resolution.install-instruction-mismatch",
                path,
                "Behavior-effect install cursor must identify a chosen Runner Stack or Grip card installed to the Runner Rig.");
            return;
        }

        string bindingKey = StableIdentifier.TryValidate(chosenTarget.Selector)
            ? chosenTarget.Selector!
            : "chosen";
        if (chosenTarget.Selector is not null
            && !StableIdentifier.TryValidate(chosenTarget.Selector))
        {
            Add(
                violations,
                "resolution.install-binding-mismatch",
                $"{path}.cardId",
                "Behavior-effect installs must consume a stable binding selector.");
        }

        BehaviorBinding? chosenBinding = behaviorParent.Bindings.SingleOrDefault(
            binding => string.Equals(binding.Key, bindingKey, StringComparison.Ordinal));
        if (chosenBinding is null || chosenBinding.CardIds.Count != 1)
        {
            Add(
                violations,
                "resolution.install-binding-missing",
                $"{path}.cardId",
                "Behavior-effect installs require exactly one current stable binding.");
        }

        CardInstanceState? installCard = state.Cards.FirstOrDefault(card => card?.Id == install.CardId);
        ZoneKind expectedSourceKind = sourceZone == BehaviorZone.Grip
            ? ZoneKind.RunnerGrip
            : ZoneKind.RunnerStack;
        ZoneState? installSource = state.Zones.FirstOrDefault(zone => zone?.Kind == expectedSourceKind
            && zone.ServerId is null
            && zone.Id == install.SourceZoneId);
        ZoneState? runnerRig = state.Zones.FirstOrDefault(zone => zone?.Kind == ZoneKind.RunnerRig && zone.ServerId is null);
        if (installSource is null
            || install.SourceZoneKind != expectedSourceKind
            || installCard is null
            || installCard.ZoneId != installSource.Id
            || !installSource.CardIds.Contains(install.CardId)
            || installCard.OwnerSide != PlayerSide.Runner)
        {
            Add(
                violations,
                "resolution.install-source-mismatch",
                $"{path}.sourceZoneId",
                "Behavior-effect install cards must remain Runner-owned in their declared Grip or Stack source.");
        }

        if (!IsRunnerInstallableRuntimeClass(install.CardClass)
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
                "Behavior-effect installs must target the canonical Runner Rig as a Runner-installable card.");
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
                if (spec.Side != PlayerSide.Runner || !IsRunnerInstallableRuntimeClass(spec.CardClass))
                {
                    Add(
                        violations,
                        "resolution.install-runtime-class-invalid",
                        $"{path}.cardClass",
                        "Behavior-effect installs require Runner Program, Hardware, or Resource runtime metadata.");
                }

                CostQuote? expectedInstallQuote = GameEngine.ProjectCostQuote(
                    state,
                    behaviorCatalog,
                    install.CardId,
                    BehaviorCostModifierKind.RunnerInstallCreditCost,
                    spec.InstallCost ?? 0,
                    runtimeCardCatalog);
                int expectedInstallCost = expectedInstallQuote?.EffectiveCost ?? (spec.InstallCost ?? 0);
                if (install.CardClass != spec.CardClass
                    || install.InstallCost != expectedInstallCost)
                {
                    Add(
                        violations,
                        "resolution.install-runtime-mismatch",
                        path,
                        "Behavior-effect install class and cost must match authoritative runtime metadata.");
                }

                if (install.CostQuote is null
                    ? expectedInstallQuote is not null
                    : !install.CostQuote.Equals(expectedInstallQuote))
                {
                    Add(
                        violations,
                        "resolution.install-cost-quote-mismatch",
                        $"{path}.costQuote",
                        "Behavior-effect install cost quote must match the authoritative source definition and current turn facts.");
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
            { Side: PlayerSide.Runner }
                && runtimeCardCatalog is not null
                ? ActiveRunnerInstallConflictIds(state, runtimeCardCatalog, installSpecForProjection)
                : [];
            CardInstanceId[] expectedCandidates = installSpecForProjection is
            { Side: PlayerSide.Runner }
                && runtimeCardCatalog is not null
                ? RunnerInstallCandidateIds(state, runtimeCardCatalog, runnerRig, installSpecForProjection)
                : [];
            if (!install.CandidateReplacementCardIds.SequenceEqual(expectedCandidates))
            {
                Add(
                    violations,
                    "resolution.install-candidates-mismatch",
                    $"{path}.candidateReplacementCardIds",
                    "Behavior-effect install candidates must equal the current Runner install conflicts.");
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
                    "Behavior-effect install replacement definitions must match the current Runner install conflicts.");
            }
        }

        if (originTarget is null
            || originTarget.Kind is not (BehaviorTargetKind.ZoneCard or BehaviorTargetKind.ZoneCards)
            || originTarget.Zone != sourceZone
            || originSelection is null
            || !originSelection.SequenceEqual([install.CardId]))
        {
            Add(
                violations,
                "resolution.install-origin-missing",
                $"{path}.cardId",
                "Behavior-effect install binding must originate in a reachable declared source-zone search or choice continuation.");
        }
        else if (runtimeCardCatalog is not null
            && !BehaviorCardMatches(state, behaviorCatalog, runtimeCardCatalog, install.CardId, originTarget.Filter))
        {
            Add(
                violations,
                "resolution.install-origin-stale",
                $"{path}.cardId",
                "Behavior-effect install card no longer matches its originating source selector.");
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
        if (install.CostQuote is not null)
        {
            Add(
                violations,
                "resolution.install-cost-quote-invalid",
                $"{path}.costQuote",
                "Corp install frames must not carry a Runner self-cost quote.");
        }

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
            else if (!(frame.HostCardId is null && action.HostCardId is not null
                && action.SelectedReplacementCardIds.SequenceEqual(frame.SelectedReplacementCardIds)))
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
            if (frame.InitiationKind == InstallInitiationKind.BehaviorZoneInstall
                && frame.CardClass == RuntimeCardClass.Ice
                && GameEngine.TryGetZoneInstallRule(state, frame, out int zoneDiscount, out BehaviorInstallCostRule zoneRule))
            {
                expectedActionCost = zoneRule == BehaviorInstallCostRule.IgnoreAllCosts
                    ? 0
                    : Math.Max(0, expectedActionCost - zoneDiscount);
            }

            if (action.CreditCost != expectedActionCost)
            {
                Add(
                    violations,
                    "decision.install-cost-mismatch",
                    $"{path}.parameters.creditCost",
                    "Install action cost must match the authoritative dynamic cost for the current selection.");
            }

            if (action.CostQuote is null
                ? frame.CostQuote is not null
                : !action.CostQuote.Equals(frame.CostQuote))
            {
                Add(
                    violations,
                    "decision.install-cost-quote-mismatch",
                    $"{path}.parameters.costQuote",
                    "Install action cost quote must match the frozen install frame quote.");
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
        var actualAbilityParameters = new List<AbilityActionParameters>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            if (action.Kind == ActionKind.UseAbility)
            {
                if (action.Parameters is AbilityActionParameters ability)
                {
                    actualAbilityParameters.Add(ability);
                }

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
        IReadOnlyList<AbilityActionParameters> expectedAbilities = [];
        try
        {
            expectedAbilities = GameEngine.MaterializeAbilityActionParameters(state, behaviorCatalog, runtimeCardCatalog);
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException
            or OverflowException)
        {
            Add(
                violations,
                "decision.ability-materialization-failed",
                path + ".legalActions",
                $"{frame.ActiveSide} turn ability actions could not be materialized from the authoritative state: {exception.Message}");
        }
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
                    runtimeCardCatalog,
                    behaviorCatalog);
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

        ValidateAbilityActionBijection(
            expectedAbilities,
            actualAbilityParameters,
            path,
            violations,
            windowName: "Turn action");
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

    private static void ValidateAbilityActionBijection(
        IReadOnlyList<AbilityActionParameters> expected,
        List<AbilityActionParameters> actual,
        string path,
        List<StateInvariantViolation> violations,
        string windowName)
    {
        if (actual.Count != expected.Count)
        {
            Add(
                violations,
                actual.Count < expected.Count
                    ? "decision.missing-ability-action"
                    : "decision.extra-ability-action",
                $"{path}.legalActions",
                $"{windowName} decision contains {actual.Count} ability action(s), expected {expected.Count}.");
        }

        for (int index = 0; index < actual.Count; index++)
        {
            AbilityActionParameters candidate = actual[index];
            if (actual.Take(index).Any(previous => AbilityParametersMatch(previous, candidate)))
            {
                Add(
                    violations,
                    "decision.duplicate-ability-action",
                    $"{path}.legalActions",
                    "Ability actions must be semantically unique.");
            }

            AbilityActionParameters? sameIdentity = expected.FirstOrDefault(issued =>
                issued.SourceCardId == candidate.SourceCardId
                && string.Equals(issued.AbilityKey, candidate.AbilityKey, StringComparison.Ordinal));
            // One source/key may intentionally have several issued variants
            // when a structured additional cost offers multiple legal card
            // combinations. Only report a payload mismatch when the complete
            // candidate matches none of those variants; comparing every
            // variant to the first would reject all but the first pair.
            if (sameIdentity is not null
                && !expected.Any(issued => AbilityParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.ability-action-payload-mismatch",
                    $"{path}.legalActions",
                    "Ability action payload does not match the current materialized source, timing, costs, or usage snapshot.");
            }

            if (!expected.Any(issued => AbilityParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.extra-ability-action",
                    $"{path}.legalActions",
                    "Decision contains an unmaterialized ability action.");
            }
        }

        foreach (AbilityActionParameters issued in expected)
        {
            if (!actual.Any(candidate => AbilityParametersMatch(candidate, issued)))
            {
                Add(
                    violations,
                    "decision.missing-ability-action",
                    $"{path}.legalActions",
                    "Decision is missing a materialized ability action.");
            }
        }
    }

    private static void ValidateTurnTimingActions(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
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
        var actualAbilities = new List<AbilityActionParameters>();
        foreach (LegalAction? action in pending.LegalActions)
        {
            if (action is null)
            {
                continue;
            }

            if (action.Kind == ActionKind.UseAbility
                && action.Parameters is AbilityActionParameters ability)
            {
                actualAbilities.Add(ability);
            }
            else if (action.Kind == ActionKind.ScoreAgenda
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
                expectedRezes = GameEngine.MaterializeRezCardParameters(
                    state,
                    behaviorCatalog,
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

        IReadOnlyList<AbilityActionParameters> expectedAbilities = [];
        try
        {
            expectedAbilities = GameEngine.MaterializeAbilityActionParameters(state, behaviorCatalog, runtimeCardCatalog);
        }
        catch (Exception exception) when (exception is ResolutionFailureException
            or InvalidOperationException
            or KeyNotFoundException
            or ArgumentException
            or OverflowException)
        {
            Add(
                violations,
                "decision.ability-materialization-failed",
                $"{path}.legalActions",
                $"Turn timing ability actions could not be materialized from the authoritative state: {exception.Message}");
        }

        ValidateAbilityActionBijection(
            expectedAbilities,
            actualAbilities,
            path,
            violations,
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
        && actual.CreditCost == expected.CreditCost
        && actual.TargetServerId == expected.TargetServerId
        && actual.TrashCostCardId == expected.TrashCostCardId;

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

    private static bool AbilityParametersMatch(
        AbilityActionParameters actual,
        AbilityActionParameters expected) =>
        actual.SourceCardId == expected.SourceCardId
        && actual.DefinitionId == expected.DefinitionId
        && string.Equals(actual.AbilityKey, expected.AbilityKey, StringComparison.Ordinal)
        && actual.Timing == expected.Timing
        && actual.Activation == expected.Activation
        && actual.Costs is not null
        && expected.Costs is not null
        && actual.Costs.SequenceEqual(expected.Costs)
        && actual.TargetCardIds is not null
        && expected.TargetCardIds is not null
        && actual.TargetCardIds.SequenceEqual(expected.TargetCardIds)
        && AbilityAdditionalCostSelectionsMatch(
            actual.AdditionalCostSelections,
            expected.AdditionalCostSelections)
        && actual.Usage is not null
        && expected.Usage is not null
        && actual.Usage.Equals(expected.Usage)
        && string.Equals(actual.CatalogFingerprint, expected.CatalogFingerprint, StringComparison.Ordinal)
        && string.Equals(actual.DefinitionFingerprint, expected.DefinitionFingerprint, StringComparison.Ordinal)
        && actual.SourceZoneId == expected.SourceZoneId
        && actual.SourceZoneKind == expected.SourceZoneKind;

    private static bool AbilityAdditionalCostSelectionsMatch(
        IReadOnlyList<AbilityAdditionalCostSelection>? actual,
        IReadOnlyList<AbilityAdditionalCostSelection>? expected)
    {
        AbilityAdditionalCostSelection[] left = (actual ?? []).ToArray();
        AbilityAdditionalCostSelection[] right = (expected ?? []).ToArray();
        return left.Length == right.Length
            && left.Zip(right).All(pair =>
                pair.First.CostIndex == pair.Second.CostIndex
                && string.Equals(pair.First.CostKey, pair.Second.CostKey, StringComparison.Ordinal)
                && pair.First.CardIds.SequenceEqual(pair.Second.CardIds));
    }

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
        && actual.CreditCost == expected.CreditCost
        && actual.ForfeitAgendaId == expected.ForfeitAgendaId
        && (actual.CostQuote is null
            ? expected.CostQuote is null
            : actual.CostQuote.Equals(expected.CostQuote));

    private static bool CostQuotesEqual(CostQuote? left, CostQuote? right) =>
        left is null ? right is null : left.Equals(right);

    private static bool InstallParametersMatch(
        InstallCardParameters actual,
        InstallCardParameters expected) =>
        actual.CardId == expected.CardId
        && actual.HostCardId == expected.HostCardId
        && actual.DestinationKind == expected.DestinationKind
        && actual.ServerId == expected.ServerId
        && actual.CreateNewRemote == expected.CreateNewRemote
        && actual.CreditCost == expected.CreditCost
        && (actual.CostQuote is null
            ? expected.CostQuote is null
            : actual.CostQuote.Equals(expected.CostQuote))
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
        frame is SetupFrame or ChooseNumberFrame
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
        || frame is IceSubroutineFrame { Stage: IceSubroutineFrameStage.AwaitingChoice }
        || frame is BehaviorInstallChoiceFrame { Stage: BehaviorInstallChoiceStage.AwaitingChoice }
        || frame is RunTimingFrame runTiming && IsRunPromptStep(runTiming.Step);

    private static bool TryGetNumberRange(ResolutionFrame? frame, out int minimum, out int maximum)
    {
        switch (frame)
        {
            case SetupFrame setup:
                minimum = 0;
                maximum = setup.Redrawn ? 0 : 1;
                return true;
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
        int lastSelectionCursor = -1;
        string? lastSelectionKey = null;
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
                lastSelectionCursor = step.InstructionCursor;
                lastSelectionKey = step.Key;
                if (selected.Length > 0)
                {
                    lastNonEmptySelection = selected;
                }
                else
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
            if (string.Equals(binding.Key, "trigger-subject", StringComparison.Ordinal))
            {
                // This binding is seeded by the checkpoint reducer for a
                // catalog-trigger child. It is not produced by a card choice
                // continuation and is validated against the exact parent
                // trigger invocation by the surrounding frame validator.
                if (frame.TriggerKey is null)
                {
                    Add(
                        violations,
                        "resolution.behavior-trigger-subject-unexpected",
                        $"{path}.bindings",
                        "The trigger-subject binding is reserved for catalog trigger children.");
                }

                continue;
            }

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

            // EL-BEHAVIOR-INSTALL: a keyed zone install binds the card it installed.
            if (!selectionsByKey.ContainsKey(binding.Key)
                && binding.CardIds.Count == 1
                && GameEngine.DeclaresKeyedInstall(rootInstructions, binding.Key))
            {
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

        bool consumed = lastSelectionCursor >= 0
            && replay.Skip(lastSelectionCursor).Take(Math.Max(0, frame.InstructionCursor - lastSelectionCursor))
                .Any(instruction => instruction.Kind is BehaviorInstructionKind.TrashCard or BehaviorInstructionKind.AddToHand or BehaviorInstructionKind.MoveCard or BehaviorInstructionKind.InstallCard
                    && instruction.Target is { Kind: BehaviorTargetKind.ChosenCard } target
                    && (target.Selector is null || target.Selector == lastSelectionKey));
        if (lastNonEmptySelection is not null
            && !consumed
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
                    || target.IsOther
                    ? frame.SourceCardId
                    : null);
            if (instruction.Kind == BehaviorInstructionKind.Choose
                && specification is not null)
            {
                expectedCandidates = FilterImmediateInstallCandidates(
                    state,
                    behaviorCatalog,
                    runtimeCardCatalog,
                    frame,
                    specification,
                    derivedInstructions,
                    expectedCandidates);
            }
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
                .Where(option => GameEngine.IsBehaviorZoneInstallOptionAvailable(
                    state,
                    behaviorCatalog,
                    runtimeCardCatalog,
                    frame,
                    option))
                .Where(option => GameEngine.IsBehaviorChoiceOptionAffordable(
                    state,
                    behaviorCatalog,
                    runtimeCardCatalog,
                    choice.Owner,
                    option,
                    frame.SourceCardId))
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

    /// <summary>
    /// Mirrors the reducer's immediate-following InstallCard projection for
    /// a pending target choice.  The reducer freezes only cards that can
    /// survive the shared behavior-install planner; validating the raw target
    /// alone would reject that authenticated, intentionally narrower list.
    /// </summary>
    private static CardInstanceId[] FilterImmediateInstallCandidates(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        BehaviorResolutionFrame frame,
        BehaviorChoiceSpec specification,
        List<BehaviorInstruction> derivedInstructions,
        CardInstanceId[] candidates)
    {
        if (runtimeCardCatalog is null
            || specification.Target is not
            {
                Kind: BehaviorTargetKind.ZoneCard,
                Zone: BehaviorZone.Grip or BehaviorZone.Stack,
            }
            || frame.InstructionCursor < 0
            || frame.InstructionCursor + 1 >= derivedInstructions.Count
            || derivedInstructions[frame.InstructionCursor + 1] is not
            {
                Kind: BehaviorInstructionKind.InstallCard,
                Target: { Kind: BehaviorTargetKind.ChosenCard } installTarget,
                SourceZone: { } sourceZone,
                DestinationZone: BehaviorZone.Rig,
            }
            || sourceZone != specification.Target.Zone)
        {
            return candidates.ToArray();
        }

        // The reducer binds the selection under both the choice key and the
        // conventional "chosen" key.  A different stable selector denotes a
        // separate binding and must not accidentally inherit this filter.
        string bindingKey = StableIdentifier.TryValidate(installTarget.Selector)
            ? installTarget.Selector!
            : "chosen";
        if (!string.Equals(bindingKey, "chosen", StringComparison.Ordinal)
            && !string.Equals(bindingKey, specification.Key, StringComparison.Ordinal))
        {
            return candidates.ToArray();
        }

        var filtered = new List<CardInstanceId>(candidates.Length);
        foreach (CardInstanceId candidate in candidates)
        {
            BehaviorContinuationStep selectionStep = new(
                frame.InstructionCursor,
                specification.Key,
                BehaviorContinuationKind.ChoiceSelection,
                cardIds: [candidate]);
            IReadOnlyList<BehaviorBinding> bindings = frame.Bindings
                .Where(binding => !string.Equals(binding.Key, specification.Key, StringComparison.Ordinal)
                    && !string.Equals(binding.Key, "chosen", StringComparison.Ordinal))
                .Append(new BehaviorBinding(specification.Key, [candidate]))
                .Append(new BehaviorBinding("chosen", [candidate]))
                .ToArray();
            BehaviorResolutionFrame hypothetical = frame.With(
                bindings: bindings,
                continuation: frame.Continuation.Append(selectionStep).ToArray());
            var option = new BehaviorChoiceOption(
                "install",
                [derivedInstructions[frame.InstructionCursor + 1]]);
            if (IsBehaviorInstallOptionAvailable(
                state,
                behaviorCatalog,
                runtimeCardCatalog,
                hypothetical,
                option))
            {
                filtered.Add(candidate);
            }
        }

        return filtered.ToArray();
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
            ZoneState? zone = FindBehaviorZone(state, target.Zone!.Value, target.Filter?.OwnerSide);
            return zone is null
                ? []
                : zone.CardIds
                    .Take(target.TopCount ?? int.MaxValue)
                    .Where(cardId => !target.IsHostedOnSource
                        || state.Cards.FirstOrDefault(card => card.Id == cardId)?.HostCardId == excludedCardId)
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
                    && (!target.IsOther || card.Id != excludedCardId)
                    && (!target.IsCopyOfArchives || GameEngine.IsCopyOfFaceUpArchivesCard(state.Cards, state.Zones, card))
                    && (!target.IsNotAi || !GameEngine.CardSubtypes(runtimeCardCatalog, card.DefinitionId).Contains("ai", StringComparer.Ordinal))
                    && (!target.IsInAttackedServer || state.RunState is { } attacked
                        && state.Zones.FirstOrDefault(zone => zone.Id == card.ZoneId)?.ServerId == attacked.AttackedServer)
                    && (target.Kind != BehaviorTargetKind.InstalledCardNotThisTurn
                        || !IsInstalledThisTurn(state, card.Id, owner))
                    && BehaviorCardMatches(state, behaviorCatalog, runtimeCardCatalog, card.Id, target.Filter))
                .OrderBy(card => card!.Id.Value, StringComparer.Ordinal)
                .Select(card => card!.Id)
                .ToArray();
        }

        return [];
    }

    private static ZoneState? FindBehaviorZone(
        GameState state,
        BehaviorZone zone,
        PlayerSide? ownerSide = null) => zone switch
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
            BehaviorZone.RemovedFromGame => ownerSide switch
            {
                PlayerSide.Corp => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.CorpRemovedFromGame && value.ServerId is null),
                PlayerSide.Runner => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.RunnerRemovedFromGame && value.ServerId is null),
                _ => null,
            },
            BehaviorZone.HostedFaceUp => ownerSide switch
            {
                PlayerSide.Corp => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.CorpHostedFaceUp && value.ServerId is null),
                PlayerSide.Runner => state.Zones.FirstOrDefault(value => value.Kind == ZoneKind.RunnerHostedFaceUp && value.ServerId is null),
                _ => null,
            },
            BehaviorZone.ServerRoot => state.Servers
                .Where(server => server.Kind == ServerKind.Remote && server.Exists)
                .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
                .Select(server => state.Zones.FirstOrDefault(zoneState => zoneState.Id == server.RootZoneId))
                .FirstOrDefault(value => value is not null),
            BehaviorZone.ServerIce => state.Servers
                .Where(server => server.Kind == ServerKind.Remote && server.Exists)
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
            || (filter.Rezzed is { } rezzed && card.Rezzed != rezzed)
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

            if (filter.AgendaPoints is { } points && runtimeSpec.AgendaPoints != points)
            {
                return false;
            }

            return filter.CardType switch
            {
                BehaviorCardType.Any => true,
                BehaviorCardType.NonAgenda => runtimeSpec.CardClass != RuntimeCardClass.Agenda,
                BehaviorCardType.Agenda => runtimeSpec.CardClass == RuntimeCardClass.Agenda,
                BehaviorCardType.Asset => runtimeSpec.CardClass == RuntimeCardClass.Asset,
                BehaviorCardType.Event => runtimeSpec.CardClass == RuntimeCardClass.Event,
                BehaviorCardType.Hardware => runtimeSpec.CardClass == RuntimeCardClass.Hardware,
                BehaviorCardType.Ice => runtimeSpec.CardClass == RuntimeCardClass.Ice,
                BehaviorCardType.Icebreaker => runtimeSpec.CardClass == RuntimeCardClass.Program
                    && runtimeSpec.Subtypes.Contains("icebreaker", StringComparer.Ordinal),
                BehaviorCardType.Program => runtimeSpec.CardClass == RuntimeCardClass.Program,
                BehaviorCardType.ProgramOrHardware => runtimeSpec.CardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware,
                BehaviorCardType.ResourceOrHardware => runtimeSpec.CardClass is RuntimeCardClass.Resource or RuntimeCardClass.Hardware,
                BehaviorCardType.Identity => runtimeSpec.CardClass == RuntimeCardClass.Identity,
                BehaviorCardType.Operation => runtimeSpec.CardClass == RuntimeCardClass.Operation,
                BehaviorCardType.Resource => runtimeSpec.CardClass == RuntimeCardClass.Resource,
                BehaviorCardType.Upgrade => runtimeSpec.CardClass == RuntimeCardClass.Upgrade,
                BehaviorCardType.Advanceable => runtimeSpec.CardClass == RuntimeCardClass.Agenda
                    || behaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? advanceable)
                        && advanceable.CanBeAdvanced,
                _ => false,
            };
        }

        if (filter.Subtype is not null || filter.CardType == BehaviorCardType.Any)
        {
            return filter.Subtype is null;
        }

        return filter.CardType switch
        {
            BehaviorCardType.ProgramOrHardware => behaviorCatalog.TryGetCardType(card.DefinitionId, out BehaviorCardType installedClass)
                && installedClass is BehaviorCardType.Program or BehaviorCardType.Icebreaker or BehaviorCardType.Hardware,
            BehaviorCardType.ResourceOrHardware => behaviorCatalog.TryGetCardType(card.DefinitionId, out BehaviorCardType resourceOrHardware)
                && resourceOrHardware is BehaviorCardType.Resource or BehaviorCardType.Hardware,
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

    // Mirrors the reducer: only an install of a chosen card in custody is an install option.
    private static bool IsBehaviorInstallOption(BehaviorChoiceOption option) =>
        option.Instructions.Count == 1
        && option.Instructions[0] is { Kind: BehaviorInstructionKind.InstallCard, Target.Kind: BehaviorTargetKind.ChosenCard };

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
            if (frame.SetAside.Count > 0)
            {
                state = GameEngine.ProjectBehaviorInstallSelection(state, behaviorCatalog, runtimeCardCatalog,
                    frame, instruction, out frame);
            }
            if (instruction.Target is not { Kind: BehaviorTargetKind.ChosenCard } target
                || instruction.SourceZone is not (BehaviorZone.Stack or BehaviorZone.Grip)
                || instruction.DestinationZone != BehaviorZone.Rig
                || (target.Selector is not null && !StableIdentifier.TryValidate(target.Selector)))
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
            ZoneKind expectedSourceKind = instruction.SourceZone == BehaviorZone.Grip
                ? ZoneKind.RunnerGrip
                : ZoneKind.RunnerStack;
            ZoneState? source = state.Zones.FirstOrDefault(
                zone => zone?.Kind == expectedSourceKind
                    && zone.OwnerSide == PlayerSide.Runner
                    && zone.ServerId is null
                    && zone.Id == card?.ZoneId);
            ZoneState? rig = state.Zones.FirstOrDefault(
                zone => zone?.Kind == ZoneKind.RunnerRig
                    && zone.OwnerSide == PlayerSide.Runner
                    && zone.ServerId is null);
            if (card is null
                || source is null
                || rig is null
                || card.OwnerSide != PlayerSide.Runner
                || !source.CardIds.Contains(card.Id)
                || !runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? incoming)
                || incoming.Side != PlayerSide.Runner
                || incoming.CardClass is not (RuntimeCardClass.Program
                    or RuntimeCardClass.Hardware
                    or RuntimeCardClass.Resource))
            {
                return false;
            }

            // A card that installs only hosted on ICE (a Trojan such as
            // Tranquilizer) is a behavior-install option only while some ICE is
            // installed to host it, as in the engine's deferred host choice.
            if (behaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? incomingBehavior)
                && incomingBehavior.Hosting is not null
                && !state.Cards.Any(host => host is not null
                    && host.OwnerSide == PlayerSide.Corp
                    && state.Zones.Any(zone => zone?.Id == host.ZoneId && zone.Kind == ZoneKind.ServerIce)
                    && runtimeCardCatalog.TryGet(host.DefinitionId, out CardRuntimeSpec? hostSpec)
                    && hostSpec.CardClass == RuntimeCardClass.Ice))
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
                    binding.CardIds,
                    instruction.SourceZone.Value))
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
            if (incoming.CardClass == RuntimeCardClass.Program
                && !CanBehaviorInstallProgram(
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
            CostQuote? installQuote = GameEngine.ProjectCostQuote(
                state,
                behaviorCatalog,
                card.Id,
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                incoming.InstallCost ?? 0,
                runtimeCardCatalog);
            int installCost = installQuote?.EffectiveCost ?? (incoming.InstallCost ?? 0);
            return runner is not null && runner.Credits >= installCost;
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
        IReadOnlyList<CardInstanceId> cardIds,
        BehaviorZone expectedSourceZone = BehaviorZone.Stack)
    {
        if (!behaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition))
        {
            return false;
        }

        IReadOnlyList<BehaviorInstruction> rootInstructions = frame.AbilityKey is not null
            ? definition.PaidAbilities
                .SingleOrDefault(ability => string.Equals(
                    ability.Key,
                    frame.AbilityKey,
                    StringComparison.Ordinal))
                ?.Instructions
                ?? []
            : frame.TriggerKey is not null
                ? definition.Triggers
                    .SingleOrDefault(trigger => string.Equals(
                        trigger.Key,
                        frame.TriggerKey,
                        StringComparison.Ordinal))
                    ?.Instructions
                    ?? []
                : definition.Instructions;
        List<BehaviorInstruction> replay = rootInstructions.ToList();
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
            && originatingTarget.Zone == expectedSourceZone
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
        HashSet<CardInstanceId> knownCardIds,
        IReadOnlyList<CardInstanceState> knownCards,
        IReadOnlyList<ServerState> knownServers,
        Dictionary<PlayerId, PlayerSide> knownPlayers,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations,
        int? extraAllottedClicks)
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

        // EL-EXTRA-CLICKS: "+1 allotted click next turn" (Aggressive
        // Trendsetting) raises a turn's allotment by the previous own turn's
        // extra clicks, and VP-ALLOTTED-CLICKS' "-1" (Caveat Emptor) lowers it,
        // never below zero; a last turn's own predecessor is gone, so its
        // allotment is unconstrained.
        int expectedClicks = Math.Max(0, (expectedSide == PlayerSide.Corp ? 3 : 4) + (extraAllottedClicks ?? 0));
        if (extraAllottedClicks is not null && facts.ClicksAllotted != expectedClicks)
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

        var attemptedServers = new HashSet<ServerId>();
        for (int index = 0; index < facts.AttemptedRunServerIds.Count; index++)
        {
            ServerId serverId = facts.AttemptedRunServerIds[index];
            string itemPath = $"{path}.attemptedRunServerIds[{index}]";
            bool validId = StableIdentifier.TryValidate(serverId.Value);
            if (!validId)
            {
                Add(violations, "id.invalid", itemPath, "Attempted-run server ID must be non-empty and stable.");
            }
            if (validId && !attemptedServers.Add(serverId))
            {
                Add(violations, "turn.duplicate-attempted-run-server", itemPath, $"Attempted-run server '{serverId}' is duplicated.");
            }
        }

        var successfulServers = new HashSet<ServerId>();
        for (int index = 0; index < facts.SuccessfulRunServerIds.Count; index++)
        {
            ServerId serverId = facts.SuccessfulRunServerIds[index];
            string itemPath = $"{path}.successfulRunServerIds[{index}]";
            bool validId = StableIdentifier.TryValidate(serverId.Value);
            if (!validId)
            {
                Add(violations, "id.invalid", itemPath, "Successful-run server ID must be non-empty and stable.");
            }
            if (validId && !successfulServers.Add(serverId))
            {
                Add(violations, "turn.duplicate-successful-run-server", itemPath, $"Successful-run server '{serverId}' is duplicated.");
            }

            if (validId && !attemptedServers.Contains(serverId))
            {
                Add(violations, "turn.successful-run-not-attempted", itemPath, $"Successful-run server '{serverId}' was not recorded as attempted.");
            }
        }

        if (facts.AttemptedRunServerIds.Count > 0 && expectedSide != PlayerSide.Runner)
        {
            Add(violations, "turn.corp-attempted-run", $"{path}.attemptedRunServerIds", "Only Runner turn facts may record run attempts.");
        }

        if (facts.SuccessfulRunServerIds.Count > 0 && expectedSide != PlayerSide.Runner)
        {
            Add(violations, "turn.corp-successful-run", $"{path}.successfulRunServerIds", "Only Runner turn facts may record successful runs.");
        }

        if (!facts.SuccessfulRun && facts.SuccessfulRunServerIds.Count > 0)
        {
            Add(
                violations,
                "turn.successful-run-flag-mismatch",
                $"{path}.successfulRun",
                "Successful-run server IDs require SuccessfulRun to be true.");
        }

        // HQ-breach occurrences are intentionally repeat-preserving. Unlike
        // attempted/successful run sets, their order and repeated IDs are
        // meaningful first-breach evidence; only stable, real HQ server IDs
        // are valid entries.
        if (facts.HqBreachOccurrenceServerIds.Count > 0 && !facts.TurnBegan)
        {
            Add(
                violations,
                "turn.hq-breach-before-begin",
                $"{path}.hqBreachOccurrenceServerIds",
                "HQ-breach occurrences require a formally begun turn.");
        }

        for (int index = 0; index < facts.HqBreachOccurrenceServerIds.Count; index++)
        {
            ServerId serverId = facts.HqBreachOccurrenceServerIds[index];
            string itemPath = $"{path}.hqBreachOccurrenceServerIds[{index}]";
            bool validId = StableIdentifier.TryValidate(serverId.Value);
            if (!validId)
            {
                Add(violations, "id.invalid", itemPath, "HQ-breach server ID must be non-empty and stable.");
                continue;
            }

            ServerState? server = knownServers.FirstOrDefault(candidate => candidate?.Id == serverId);
            if (server is null)
            {
                Add(
                    violations,
                    "turn.unknown-hq-breach-server",
                    itemPath,
                    $"HQ-breach occurrence references unknown server '{serverId}'.");
            }
            else if (server.Kind != ServerKind.Headquarters)
            {
                Add(
                    violations,
                    "turn.hq-breach-server-kind",
                    itemPath,
                    $"HQ-breach occurrence '{serverId}' must identify a Headquarters server, not '{server.Kind}'.");
            }
        }

        if (facts.BasicDrawOccurrenceIds.Count > 0 && !facts.TurnBegan)
        {
            Add(
                violations,
                "turn.basic-draw-before-begin",
                $"{path}.basicDrawOccurrenceIds",
                "Basic-draw occurrences require a formally begun turn.");
        }

        var basicDrawOccurrences = new HashSet<OccurrenceId>();
        for (int index = 0; index < facts.BasicDrawOccurrenceIds.Count; index++)
        {
            OccurrenceId occurrenceId = facts.BasicDrawOccurrenceIds[index];
            string itemPath = $"{path}.basicDrawOccurrenceIds[{index}]";
            bool validId = StableIdentifier.TryValidate(occurrenceId.Value);
            if (!validId)
            {
                Add(violations, "id.invalid", itemPath, "Basic-draw occurrence ID must be non-empty and stable.");
            }
            else if (!basicDrawOccurrences.Add(occurrenceId))
            {
                Add(
                    violations,
                    "turn.duplicate-basic-draw-occurrence",
                    itemPath,
                    $"Basic-draw occurrence '{occurrenceId}' is duplicated.");
            }
        }

        ValidateTurnFactCardOccurrences(
            facts.AccessedCardOccurrenceIds,
            "accessedCardOccurrenceIds",
            "accessed-card",
            path,
            knownCardIds,
            null,
            null,
            violations);
        ValidateTurnFactCardOccurrences(
            facts.TrashedCardOccurrenceIds,
            "trashedCardOccurrenceIds",
            "trashed-card",
            path,
            knownCardIds,
            null,
            null,
            violations);
        ValidateTurnFactCardOccurrences(
            facts.ProgramInstallOccurrenceIds,
            "programInstallOccurrenceIds",
            "program-install",
            path,
            knownCardIds,
            knownCards,
            runtimeCardCatalog,
            violations);

        for (int index = 0; index < facts.AgendaTurnChanges.Count; index++)
        {
            AgendaTurnChange? change = facts.AgendaTurnChanges[index];
            string itemPath = $"{path}.agendaTurnChanges[{index}]";
            if (change is null)
            {
                Add(violations, "turn.null-agenda-change", itemPath, "Agenda changes must not be null.");
                continue;
            }

            if (!StableIdentifier.TryValidate(change.CardId.Value))
            {
                Add(violations, "id.invalid", $"{itemPath}.cardId", "Agenda card ID must be non-empty and stable.");
            }
            else if (!knownCardIds.Contains(change.CardId))
            {
                Add(violations, "turn.unknown-agenda-card", $"{itemPath}.cardId", $"Agenda card '{change.CardId}' does not exist in the game state.");
            }

            bool validBeneficiary = IsPlayerSide(change.BeneficiarySide);
            if (!validBeneficiary)
            {
                Add(violations, "turn.invalid-agenda-beneficiary", $"{itemPath}.beneficiarySide", $"'{change.BeneficiarySide}' is not a valid agenda beneficiary side.");
            }

            bool validKind = Enum.IsDefined(change.Kind);
            if (!validKind)
            {
                Add(violations, "turn.invalid-agenda-kind", $"{itemPath}.kind", $"'{change.Kind}' is not a supported agenda change kind.");
            }

            if (change.Points <= 0)
            {
                Add(violations, "turn.invalid-agenda-points", $"{itemPath}.points", "Agenda change points must be positive.");
            }

            if (validBeneficiary && validKind)
            {
                PlayerSide expectedBeneficiary = change.Kind == AgendaTurnChangeKind.Score
                    ? PlayerSide.Corp
                    : PlayerSide.Runner;
                if (change.BeneficiarySide != expectedBeneficiary)
                {
                    Add(
                        violations,
                        "turn.agenda-beneficiary-kind-mismatch",
                        $"{itemPath}.beneficiarySide",
                        $"An agenda '{change.Kind}' change must benefit '{expectedBeneficiary}'.");
                }

                if (expectedSide != expectedBeneficiary)
                {
                    Add(
                        violations,
                        "turn.agenda-turn-side-mismatch",
                        itemPath,
                        $"An agenda '{change.Kind}' change must be recorded in the '{expectedBeneficiary}' turn facts.");
                }
            }
        }

        for (int index = 0; index < facts.TagTurnChanges.Count; index++)
        {
            TagTurnChange? change = facts.TagTurnChanges[index];
            string itemPath = $"{path}.tagTurnChanges[{index}]";
            if (change is null)
            {
                Add(violations, "turn.null-tag-change", itemPath, "Tag changes must not be null.");
                continue;
            }

            if (!StableIdentifier.TryValidate(change.AffectedPlayerId.Value))
            {
                Add(violations, "id.invalid", $"{itemPath}.affectedPlayerId", "Tag target player ID must be non-empty and stable.");
            }
            else if (!knownPlayers.TryGetValue(change.AffectedPlayerId, out PlayerSide affectedSide))
            {
                Add(violations, "turn.unknown-tag-player", $"{itemPath}.affectedPlayerId", $"Tag target player '{change.AffectedPlayerId}' does not exist in the game state.");
            }
            else if (affectedSide != PlayerSide.Runner)
            {
                Add(violations, "turn.tag-target-not-runner", $"{itemPath}.affectedPlayerId", "Only the Runner may be tagged.");
            }

            if (change.Delta == 0)
            {
                Add(violations, "turn.zero-tag-change", $"{itemPath}.delta", "A tag change must be nonzero.");
            }
        }

        var firstXKeys = new HashSet<(CardInstanceId SourceCardId, string Key)>();
        for (int index = 0; index < facts.FirstXUseKeys.Count; index++)
        {
            FirstXUseKey? use = facts.FirstXUseKeys[index];
            string itemPath = $"{path}.firstXUseKeys[{index}]";
            if (use is null)
            {
                Add(violations, "turn.null-first-x-use", itemPath, "First-X use entries must not be null.");
                continue;
            }

            if (!StableIdentifier.TryValidate(use.SourceCardId.Value))
            {
                Add(violations, "id.invalid", $"{itemPath}.sourceCardId", "First-X source card ID must be non-empty and stable.");
            }
            else if (!knownCardIds.Contains(use.SourceCardId))
            {
                Add(violations, "turn.unknown-first-x-source", $"{itemPath}.sourceCardId", $"First-X source card '{use.SourceCardId}' does not exist in the game state.");
            }

            bool validKey = StableIdentifier.TryValidate(use.Key);
            if (!validKey)
            {
                Add(violations, "id.invalid", $"{itemPath}.key", "First-X key must be non-empty and stable.");
            }

            if (use.Count <= 0)
            {
                Add(violations, "turn.invalid-first-x-count", $"{itemPath}.count", "First-X use count must be positive.");
            }

            if (StableIdentifier.TryValidate(use.SourceCardId.Value) && validKey
                && !firstXKeys.Add((use.SourceCardId, use.Key)))
            {
                Add(violations, "turn.duplicate-first-x-key", itemPath, $"First-X key '{use.SourceCardId}/{use.Key}' is duplicated.");
            }
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

        var usedAbilities = new HashSet<AbilityUseKey>();
        for (int index = 0; index < facts.UsedAbilityKeys.Count; index++)
        {
            AbilityUseKey? use = facts.UsedAbilityKeys[index];
            if (use is null)
            {
                Add(violations, "turn.null-ability-use", $"{path}.usedAbilityKeys[{index}]", "Ability usage entries must not be null.");
                continue;
            }

            if (!StableIdentifier.TryValidate(use.SourceCardId.Value))
            {
                Add(violations, "id.invalid", $"{path}.usedAbilityKeys[{index}].sourceCardId", "Ability usage source card ID must be non-empty and stable.");
            }

            if (!StableIdentifier.TryValidate(use.AbilityKey))
            {
                Add(violations, "id.invalid", $"{path}.usedAbilityKeys[{index}].abilityKey", "Ability usage keys must be non-empty and stable.");
            }

            if (!usedAbilities.Add(use))
            {
                Add(violations, "turn.duplicate-ability-use", $"{path}.usedAbilityKeys[{index}]", $"Ability usage '{use.SourceCardId}/{use.AbilityKey}' is duplicated.");
            }
        }
    }

    private static void ValidateTurnFactCardOccurrences(
        IReadOnlyList<CardInstanceId> occurrenceIds,
        string fieldName,
        string occurrenceName,
        string path,
        HashSet<CardInstanceId> knownCardIds,
        IReadOnlyList<CardInstanceState>? knownCards,
        IRuntimeCardCatalog? runtimeCardCatalog,
        List<StateInvariantViolation> violations)
    {
        for (int index = 0; index < occurrenceIds.Count; index++)
        {
            CardInstanceId cardId = occurrenceIds[index];
            string itemPath = $"{path}.{fieldName}[{index}]";
            if (!StableIdentifier.TryValidate(cardId.Value))
            {
                Add(violations, "id.invalid", itemPath, $"{occurrenceName} card ID must be non-empty and stable.");
            }
            else if (!knownCardIds.Contains(cardId))
            {
                Add(violations, "turn.unknown-card-occurrence", itemPath, $"{occurrenceName} card '{cardId}' does not exist in the game state.");
            }
            else if (fieldName == "programInstallOccurrenceIds")
            {
                CardInstanceState? card = knownCards?.FirstOrDefault(candidate => candidate?.Id == cardId);
                if (card is null)
                {
                    continue;
                }

                if (card.OwnerSide != PlayerSide.Runner)
                {
                    Add(
                        violations,
                        "turn.program-install-owner-mismatch",
                        itemPath,
                        $"A program-install occurrence must reference a Runner-owned card, but '{cardId}' is owned by '{card.OwnerSide}'.");
                }

                if (runtimeCardCatalog is null)
                {
                    continue;
                }

                if (!runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec))
                {
                    Add(
                        violations,
                        "turn.program-install-runtime-missing",
                        itemPath,
                        $"Runtime metadata for program-install occurrence '{cardId}' ({card.DefinitionId}) is unavailable.");
                }
                else if (spec.Side != PlayerSide.Runner || spec.CardClass != RuntimeCardClass.Program)
                {
                    Add(
                        violations,
                        "turn.program-install-class-invalid",
                        itemPath,
                        $"Program-install occurrence '{cardId}' must identify a Runner Program runtime card.");
                }
            }
        }
    }

    private static bool IsOwnerDiscard(ZoneState? zone, PlayerSide ownerSide) =>
        zone is not null
        && zone.Kind == (ownerSide == PlayerSide.Corp
            ? ZoneKind.CorpDiscard
            : ZoneKind.RunnerHeap);

    private static bool IsPlayerSide(PlayerSide side) =>
        side is PlayerSide.Corp or PlayerSide.Runner;

    private static void Add(
        List<StateInvariantViolation> violations,
        string code,
        string path,
        string message) =>
        violations.Add(new StateInvariantViolation(code, path, message));

    private static bool IsIceSubroutineTrigger(IBehaviorCatalog behaviorCatalog, BehaviorResolutionFrame frame) =>
        frame.TriggerKey is { } key
        && behaviorCatalog.TryGet(frame.DefinitionId, out CardBehaviorDefinition? definition)
        && definition.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.IceSubroutine
            && string.Equals(trigger.Key, key, StringComparison.Ordinal));
}
