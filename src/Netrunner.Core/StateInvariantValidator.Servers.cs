namespace Netrunner.Core;

public static partial class StateInvariantValidator
{
    private static void ValidateServerLifecycle(GameState state, ServerState server, string path, List<StateInvariantViolation> violations)
    {
        if (!Enum.IsDefined(server.Lifecycle)
            || server.Lifecycle != ServerLifecycle.Active
                && (server.Kind != ServerKind.Remote
                    || state.Zones.Any(zone => zone.ServerId == server.Id && zone.CardIds.Count > 0)))
        {
            Add(violations, "servers.invalid-lifecycle", path, "Only empty remote servers may await a checkpoint or retain a destroyed-server tombstone.");
        }

        if (!server.Exists && state.ResolutionStack.OfType<InstallFrame>().Any(frame => frame.ServerId == server.Id))
        {
            Add(violations, "servers.destroyed-install", path, "An install destination cannot be destroyed before its install finishes.");
        }

        if (server.Kind == ServerKind.Remote && server.Lifecycle == ServerLifecycle.Active
            && state.Zones.Any(zone => zone.Kind == ZoneKind.CorpIdentity)
            && state.Zones.Where(zone => zone.ServerId == server.Id).All(zone => zone.CardIds.Count == 0)
            && !state.ResolutionStack.OfType<InstallFrame>().Any(frame => frame.ServerId == server.Id))
        {
            Add(violations, "servers.empty-active-match-remote", path, "A setup-created match cannot retain an active empty remote outside installation.");
        }
    }
}
