using System.Text.Json;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class FuzzCommandTests
{
    [Fact]
    public void DeveloperFuzzWritesSummaryAndSucceeds()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"netrunner-fuzz-{Guid.NewGuid():N}");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            int exitCode = CliCommands.Run(
                ["fuzz", "--games", "3", "--seed", "4", "--workers", "2", "--output", directory, "--json"], output, error);

            Assert.True(exitCode == 0, error.ToString());
            using JsonDocument report = JsonDocument.Parse(output.ToString());
            Assert.Equal("ok", report.RootElement.GetProperty("status").GetString());
            Assert.Equal(3, report.RootElement.GetProperty("games").GetInt32());
            Assert.Equal(3, report.RootElement.GetProperty("results").GetArrayLength());
            Assert.True(File.Exists(Path.Combine(directory, "summary.json")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WorkerCountDoesNotChangeResults()
    {
        using var serialOutput = new StringWriter();
        using var parallelOutput = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CliCommands.Run(["fuzz", "--games", "3", "--seed", "8", "--json"], serialOutput, error));
        Assert.Equal(0, CliCommands.Run(["fuzz", "--games", "3", "--seed", "8", "--workers", "3", "--json"], parallelOutput, error));

        using JsonDocument serial = JsonDocument.Parse(serialOutput.ToString());
        using JsonDocument parallel = JsonDocument.Parse(parallelOutput.ToString());
        Assert.Equal(serial.RootElement.GetProperty("results").GetRawText(), parallel.RootElement.GetProperty("results").GetRawText());
    }

    [Theory]
    [InlineData("--games", "0")]
    [InlineData("--content", "somewhere")]
    [InlineData("--workers", "65")]
    [InlineData("--unknown", "1")]
    public void InvalidArgumentsExitWithTwo(string option, string value)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, CliCommands.Run(["fuzz", option, value], output, error));
    }

    [Fact]
    public void ExistingOutputDirectoryIsRejected()
    {
        string directory = Directory.CreateTempSubdirectory("netrunner-fuzz-existing-").FullName;
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            Assert.Equal(2, CliCommands.Run(["fuzz", "--games", "1", "--output", directory], output, error));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
