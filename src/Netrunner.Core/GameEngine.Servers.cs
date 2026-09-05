namespace Netrunner.Core;

public static partial class GameEngine
{
    // Remote creation and its initial install are one atomic reduction. Only
    // an actual card transfer can leave an established server empty; legacy
    // board fixtures may deliberately contain unoccupied placeholder remotes.
    private static void RefreshRemoteLifecycle(WorkingState working, ServerId? serverId)
    {
        int index = working.Servers.FindIndex(server => server.Id == serverId && server.Kind == ServerKind.Remote);
        if (index < 0)
        {
            return;
        }

        ServerState server = working.Servers[index];
        if (!server.Exists)
        {
            return;
        }

        bool empty = working.Zones.Where(zone => zone.ServerId == server.Id).All(zone => zone.CardIds.Count == 0);
        working.Servers[index] = new ServerState(server.Id, server.Kind, server.IceZoneId, server.RootZoneId,
            empty ? ServerLifecycle.EmptyPendingCheckpoint : ServerLifecycle.Active);
    }

    // CR 10.3.1h / 8.5.9. Tombstones retain source-zone provenance while
    // effects and run windows unwind, but are not legal install/run targets.
    private static void ProcessRemoteCheckpoint(WorkingState working)
    {
        for (int i = 0; i < working.Servers.Count; i++)
        {
            ServerState server = working.Servers[i];
            if (server.Lifecycle != ServerLifecycle.EmptyPendingCheckpoint
                || working.ResolutionStack.OfType<InstallFrame>().Any(frame => frame.ServerId == server.Id))
            {
                continue;
            }

            working.Servers[i] = new ServerState(server.Id, server.Kind, server.IceZoneId, server.RootZoneId,
                ServerLifecycle.Destroyed);
        }

        if (working.RunState is not null
            || working.ResolutionStack.Any(frame => frame is not TurnFrame and not ToyTurnFrame))
        {
            return;
        }

        foreach (ServerState server in working.Servers.Where(server => !server.Exists).ToArray())
        {
            working.Zones.RemoveAll(zone => zone.ServerId == server.Id);
            working.Servers.Remove(server);
        }
    }

    private static bool AttackedServerDestroyed(WorkingState working, RunState run) =>
        working.Servers.Any(server => server.Id == run.AttackedServer && !server.Exists);

    private static void EndRunAtDestroyedServer(WorkingState working, RunState run) =>
        SetRunTiming(working, run.WithTiming(RunTimingStep.RunEnd, run.CurrentIceIndex, run.CurrentIceId,
            run.Successful, run.Unsuccessful, RunEndReason.EndedByRule, null, run.Encounter, run.AccessState));
}
