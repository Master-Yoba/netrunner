using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class FuzzReproCommandTests
{
    [Fact]
    public void ReportsWhetherTheFailingActionStillFails()
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(RulesFuzzRunner.DeveloperContentId, 77);
        GameState state = GameEngine.CreateMatch(definition.Request.Seed, definition.Request.CorpDeck, definition.Request.RunnerDeck,
            definition.Request.CardDataVersion, definition.Behavior, definition.Runtime).State;
        PendingDecision pending = state.PendingDecision!;

        string rejected = WriteFailure(state, definition,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, new ActionId("action:not-issued")));
        string accepted = WriteFailure(state, definition,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pending.LegalActions[0].Id));
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            Assert.Equal(1, CliCommands.Run(["fuzz-repro", rejected], output, error));
            Assert.Contains("fuzz-repro=reproduces", output.ToString());
            Assert.Equal(0, CliCommands.Run(["fuzz-repro", accepted], output, error));
            Assert.Contains("fuzz-repro=fixed", output.ToString());
            Assert.Equal(2, CliCommands.Run(["fuzz-repro", Path.Combine(rejected, "missing")], output, error));
        }
        finally
        {
            Directory.Delete(rejected, recursive: true);
            Directory.Delete(accepted, recursive: true);
        }
    }

    private static string WriteFailure(GameState state, FullMatchDefinition definition, ActionSubmission submission)
    {
        string directory = Directory.CreateTempSubdirectory("netrunner-fuzz-repro-").FullName;
        File.WriteAllText(Path.Combine(directory, "state-before.json"),
            EngineJson.Serialize(state.CreateSnapshot(definition.Behavior, definition.Runtime)));
        File.WriteAllText(Path.Combine(directory, "failure.json"), EngineJson.Serialize(new
        {
            schemaVersion = 1,
            kind = "rules-fuzz-failure",
            fuzzCase = new FuzzGameCase("developer-00001", RulesFuzzRunner.DeveloperContentId, 77,
                FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal),
            status = nameof(FuzzGameStatus.Rejected),
            failingSubmission = submission,
        }));
        return directory;
    }
}
