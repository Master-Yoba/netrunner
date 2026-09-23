using System.Globalization;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    private static int RunSnapshot(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        try { return RunSnapshotAsync(args, output).GetAwaiter().GetResult(); }
        catch (Exception exception) when (IsCommandFailure(exception))
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { Status = "failed", exception.Message }));
            else error.WriteLine($"snapshot failed: {exception.Message}");
            return 2;
        }
    }

    private static async Task<int> RunSnapshotAsync(string[] args, TextWriter output)
    {
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("snapshot requires an input GameSnapshot or developer snapshot file.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? destination = null;
        string? until = null;
        ActionId? action = null;
        int limit = 1000;
        PlayerSide side = PlayerSide.Runner;
        bool synthetic = false, raw = false, step = false, json = false;
        for (int index = 2; index < args.Length; index++)
        {
            string flag = args[index];
            if (!seen.Add(flag)) throw new ArgumentException($"Duplicate option {flag}.");
            string Value() => ++index < args.Length ? args[index] : throw new ArgumentException($"{flag} requires a value.");
            switch (flag)
            {
                case "--output": destination = Value(); break;
                case "--until": until = Value(); break;
                case "--max-actions": limit = Number(Value()); break;
                case "--action": action = new(Value()); break;
                case "--side":
                    side = Value() switch
                    {
                        "corp" => PlayerSide.Corp,
                        "runner" => PlayerSide.Runner,
                        _ => throw new ArgumentException("--side must be corp or runner."),
                    }; break;
                case "--synthetic": synthetic = true; break;
                case "--raw": raw = true; break;
                case "--step": step = true; break;
                case "--json": json = true; break;
                default: throw new ArgumentException($"Unknown snapshot option {flag}.");
            }
        }
        if (step && until is not null || action is not null && !step || limit is < 1 or > 10000
            || (step || until is not null || raw) && destination is null)
            throw new ArgumentException("Choose --step or --until, supply --output for changes, and limit to 1–10000 actions. --action requires --step.");
        CardDataSet data;
        IBehaviorCatalog behavior;
        IRuntimeCardCatalog runtime;
        if (synthetic)
        {
            DeveloperMatchCards cards = DeveloperMatchCards.Create();
            data = cards.Data; behavior = cards.Behavior; runtime = cards.Runtime;
        }
        else
        {
            data = CardDataResource.LoadEmbedded(); behavior = GatewayBehaviorCatalog.Create(); runtime = GatewayRuntimeCardCatalog.Create(data);
        }
        DeveloperSnapshotDocument document = DeveloperSnapshotStore.Read(args[1], side, behavior, runtime);
        DeveloperAdvanceResult? advanced = null;
        if (step || until is not null)
        {
            int? turn = until?.StartsWith("turn:", StringComparison.Ordinal) == true
                ? Number(until[5..]) : null;
            DeveloperAdvanceTarget target = step ? DeveloperAdvanceTarget.ActionLimit : until switch
            {
                "human" => DeveloperAdvanceTarget.HumanDecision,
                "outcome" => DeveloperAdvanceTarget.Outcome,
                _ when turn is not null => DeveloperAdvanceTarget.Turn,
                _ => throw new ArgumentException("--until must be human, outcome or turn:N."),
            };
            await using var coordinator = new MatchCoordinator(document, new MatchCoordinatorOptions
            {
                HumanSide = document.HumanSide,
                EnableDeveloperTools = true,
                BehaviorCatalog = behavior,
                RuntimeCardCatalog = runtime,
                CardMetadataResolver = new CardCatalogMetadataResolver(data),
            });
            await coordinator.StartAsync();
            advanced = await coordinator.AdvanceDeveloperAsync(new(target, step ? 1 : limit, turn, action));
            document = await coordinator.ExportDeveloperSnapshotAsync();
        }
        if (destination is not null) DeveloperSnapshotStore.Write(destination, document, behavior, runtime, raw);
        bool failed = advanced?.Reason == DeveloperStopReason.Failure;
        var result = new
        {
            Status = failed ? "failed" : "ok",
            document.Origin,
            document.HumanSide,
            document.Snapshot.ActionSequence,
            document.Snapshot.Turn,
            document.Snapshot.Outcome,
            StateHash = document.EngineStateHash,
            PolicyHash = document.PolicyStateHash,
            AcceptedActions = advanced?.AcceptedActions ?? 0,
            StopReason = advanced?.Reason.ToString(),
            Error = advanced?.Snapshot.Feedback?.Message
        };
        output.WriteLine(json ? EngineJson.Serialize(result)
            : $"snapshot={result.Status} origin={result.Origin} action-sequence={result.ActionSequence} advanced={result.AcceptedActions} stop={result.StopReason ?? "validated"} state={result.StateHash}");
        return failed ? 1 : 0;

        static int Number(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            ? number : throw new ArgumentException("Snapshot counts and turns must be nonnegative integers.");
    }
}
