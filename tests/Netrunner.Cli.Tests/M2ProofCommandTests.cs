using System.Text.Json;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class M2ProofCommandTests
{
    // Pinned from a green engine run (M2 milestone proof, slice 4; re-pinned
    // by M2 rework U5 and kept stable by U6 — the agenda mid-access window
    // (pass only) and the automatic 7.2.3 steal emit the same event stream
    // as the pre-U6 player-steal action, see M2ProofTests); the same
    // constants M2ProofTests pins for both policies.
    private const string ExpectedFinalStateHash =
        "c822e9790dcad48ccdb52f42539be678943fc109820ea2c7e20e5f8fa03805ac";
    private const string ExpectedEventStreamHash =
        "d20cdef94e97ac928ca088e14952b65d2116fecda00485fff5c3b05e1b6e9e20";

    [Fact]
    public void M2ProofCommandSucceedsAndReportsPinnedHashes()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "M2Scenarios", "representative-run.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int code = CliCommands.Run(["m2-proof", path, "--json"], output, error);

        Assert.Equal(0, code);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal("m2:representative-run", root.GetProperty("scenarioId").GetString());
        Assert.True(root.GetProperty("replayVerified").GetBoolean());
        Assert.Empty(root.GetProperty("traceProblems").EnumerateArray().Select(element => element.GetString()));
        Assert.Equal(ExpectedFinalStateHash, root.GetProperty("finalStateHash").GetString());
        Assert.Equal(ExpectedEventStreamHash, root.GetProperty("eventStreamHash").GetString());
        Assert.True(root.GetProperty("checkpoints").GetInt32() > 0);
        Assert.True(root.GetProperty("replayActions").GetInt32() > 0);
        JsonElement policies = root.GetProperty("policies");
        Assert.Equal(ExpectedFinalStateHash, policies.GetProperty("scripted-human").GetProperty("finalStateHash").GetString());
        Assert.Equal(ExpectedEventStreamHash, policies.GetProperty("scripted-human").GetProperty("eventStreamHash").GetString());
        Assert.Equal(ExpectedFinalStateHash, policies.GetProperty("contract-agent").GetProperty("finalStateHash").GetString());
        Assert.Equal(ExpectedEventStreamHash, policies.GetProperty("contract-agent").GetProperty("eventStreamHash").GetString());
    }

    [Fact]
    public void M2ProofCommandFailsForMissingScenarioFile()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "M2Scenarios", "does-not-exist.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int code = CliCommands.Run(["m2-proof", path, "--json"], output, error);

        Assert.NotEqual(0, code);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
    }
}
