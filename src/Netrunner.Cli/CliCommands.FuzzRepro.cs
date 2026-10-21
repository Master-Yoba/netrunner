using System.Text.Json;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// fuzz-repro &lt;failure-directory&gt; [--json]. Loads a fuzz failure's prior
    /// state and failing submission, re-applies that single action, and reports
    /// whether the defect still occurs. Exit 1 while it reproduces, 0 once it is
    /// fixed, 2 for unreadable evidence. The prior state is already the minimal
    /// reproduction: one action from a known position.
    /// </summary>
    private static int RunFuzzRepro(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        string status;
        string? message;
        try
        {
            if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("fuzz-repro requires a failure directory written by `fuzz --output`.");
            string directory = Path.GetFullPath(args[1]);
            using JsonDocument failure = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "failure.json")));
            JsonElement root = failure.RootElement;
            string recordedStatus = root.GetProperty("status").GetString() ?? "";
            string contentId = root.GetProperty("fuzzCase").GetProperty("contentId").GetString()
                ?? throw new ArgumentException("failure.json has no content ID.");
            if (root.GetProperty("failingSubmission").ValueKind == JsonValueKind.Null)
                throw new ArgumentException($"A {recordedStatus} failure has no single failing action to re-apply; replay replay-prefix.json instead.");
            ActionSubmission submission = EngineJson.Deserialize<ActionSubmission>(root.GetProperty("failingSubmission").GetRawText());
            ulong seed = root.GetProperty("fuzzCase").GetProperty("gameSeed").GetUInt64();
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(contentId, seed);
            GameSnapshot snapshot = EngineJson.Deserialize<GameSnapshot>(
                File.ReadAllText(Path.Combine(directory, "state-before.json")), definition.Behavior, definition.Runtime);
            GameState state = GameState.FromSnapshot(snapshot, definition.Behavior, definition.Runtime);

            if (recordedStatus == nameof(FuzzGameStatus.SimulationMismatch))
            {
                message = GameEngine.VerifySimulationEquivalence(state, submission, definition.Behavior, definition.Runtime);
                status = message is null ? "fixed" : "reproduces";
            }
            else
            {
                try
                {
                    ApplyResult applied = GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime);
                    status = applied.Accepted ? "fixed" : "reproduces";
                    message = applied.Accepted ? null : $"{applied.Rejection?.Code}: {applied.Rejection?.Message}";
                }
                catch (Exception exception) when (IsCommandFailure(exception))
                {
                    status = "reproduces";
                    message = $"{exception.GetType().Name}: {exception.Message}";
                }
            }
        }
        catch (Exception exception) when (IsCommandFailure(exception) || exception is JsonException or KeyNotFoundException)
        {
            if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status = "invalid", message = exception.Message }));
            else error.WriteLine($"fuzz-repro failed: {exception.Message}");
            return 2;
        }

        if (json) output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, status, message }));
        else output.WriteLine(message is null ? $"fuzz-repro={status}" : $"fuzz-repro={status} {message}");
        return status == "fixed" ? 0 : 1;
    }
}
