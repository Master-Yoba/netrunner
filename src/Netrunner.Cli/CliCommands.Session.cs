using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunSessionRecover(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("session-recover requires a session directory.");
            long? throughAction = null;
            string? replayPath = null;
            string? snapshotPath = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 2; index < args.Length; index++)
            {
                string option = args[index];
                if (!seen.Add(option)) throw new ArgumentException($"Duplicate option '{option}'.");
                if (option == "--json") continue;
                if (option is not ("--at-action" or "--replay-out" or "--snapshot-out"))
                    throw new ArgumentException($"Unsupported session-recover option '{option}'.");
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{option} requires a value.");
                string value = args[index];
                if (option == "--at-action")
                {
                    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long sequence))
                        throw new ArgumentException("--at-action requires a nonnegative action sequence.");
                    throughAction = sequence;
                }
                else if (option == "--replay-out") replayPath = value;
                else snapshotPath = value;
            }

            string sessionDirectory = Path.GetFullPath(args[1]);
            foreach (string destination in new[] { replayPath, snapshotPath }.OfType<string>())
            {
                string relative = Path.GetRelativePath(sessionDirectory, Path.GetFullPath(destination));
                if (!Path.IsPathRooted(relative) && relative != ".."
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new ArgumentException("Write exports outside the source session directory to preserve its evidence.");
            }
            if (replayPath is not null && snapshotPath is not null
                && string.Equals(Path.GetFullPath(replayPath), Path.GetFullPath(snapshotPath), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Replay and snapshot outputs must use different files.");
            if (!Directory.Exists(sessionDirectory))
                throw new DirectoryNotFoundException($"Session directory does not exist: {sessionDirectory}");
            SessionTraceManifest manifest = SessionTrace.ReadManifest(sessionDirectory);
            IBehaviorCatalog behavior;
            IRuntimeCardCatalog runtime;
            if (manifest.BehaviorRegistryVersion == DeveloperMatchCards.BehaviorVersion)
            {
                DeveloperMatchCards cards = DeveloperMatchCards.Create();
                behavior = cards.Behavior;
                runtime = cards.Runtime;
            }
            else if (manifest.BehaviorRegistryVersion == GatewayBehaviorCatalog.Version)
            {
                behavior = GatewayBehaviorCatalog.Create();
                runtime = GatewayRuntimeCardCatalog.CreateEmbedded();
            }
            else throw new InvalidOperationException($"Unsupported session behavior catalog '{manifest.BehaviorRegistryVersion}'.");

            SessionRecoveryResult recovered = SessionTrace.Recover(args[1], behavior, runtime, throughAction);
            string? replayOutput = replayPath is null ? null : WriteReplayAtomically(recovered.Replay, replayPath);
            string? snapshotOutput = snapshotPath is null ? null : WriteSessionSnapshot(recovered.State, snapshotPath, behavior, runtime);
            string stateHash = DeterministicHash.ForState(recovered.State);
            var result = new
            {
                Status = "ok",
                ActionSequence = recovered.State.ActionSequence,
                StateHash = stateHash,
                recovered.HasInterruptedAction,
                recovered.IgnoredIncompleteTail,
                ReplayPath = replayOutput,
                SnapshotPath = snapshotOutput,
            };
            if (json) output.WriteLine(EngineJson.Serialize(result));
            else
            {
                output.WriteLine("session-recover=ok");
                output.WriteLine($"action-sequence={recovered.State.ActionSequence}");
                output.WriteLine($"state-hash={stateHash}");
                if (recovered.HasInterruptedAction) output.WriteLine("interrupted-action=true (restored the last committed state)");
                if (recovered.IgnoredIncompleteTail) output.WriteLine("incomplete-final-record=ignored");
                if (replayOutput is not null) output.WriteLine($"replay={replayOutput}");
                if (snapshotOutput is not null) output.WriteLine($"snapshot={snapshotOutput}");
            }
            return 0;
        }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { Status = "failed", exception.Message }));
            else error.WriteLine($"session-recover failed: {exception.Message}");
            return 1;
        }
    }

    private static string WriteSessionSnapshot(GameState state, string path, IBehaviorCatalog behavior, IRuntimeCardCatalog runtime)
    {
        string destination = Path.GetFullPath(path);
        string temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(EngineJson.SerializeUtf8(GameSnapshot.From(state, behavior, runtime)));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
