using System.Diagnostics;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class AtomicFileProcessTests
{
    [Theory]
    [InlineData("primary-flushed", false)]
    [InlineData("primary-flushed", true)]
    [InlineData("backup-flushed", true)]
    [InlineData("published", false)]
    [InlineData("published", true)]
    public async Task ProcessTerminationDuringWritePreservesPublishedGenerations(string stage, bool overwrite)
    {
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-atomic-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "save");
        string marker = Path.Combine(directory, "ready");
        try
        {
            if (overwrite)
            {
                AtomicFile.Write(path, [1]);
                AtomicFile.Write(path, [2], backupPath: path + ".bak");
            }
            string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { } root
                    ? Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet") : "dotnet");
            var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, RedirectStandardError = true };
            foreach (string argument in new[] { "exec", "--runtimeconfig",
                Path.ChangeExtension(typeof(AtomicFileProcessTests).Assembly.Location, ".runtimeconfig.json"),
                typeof(PersistenceProbe.Program).Assembly.Location, path, marker, stage }) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start storage probe.");
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!File.Exists(marker))
                {
                    if (process.HasExited) throw new InvalidOperationException(await process.StandardError.ReadToEndAsync());
                    await Task.Delay(10, deadline.Token);
                }
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(deadline.Token);
                if (stage == "published") Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(path));
                else if (overwrite) Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(path));
                else Assert.False(File.Exists(path));
                if (overwrite) Assert.Equal(new byte[] { stage == "published" ? (byte)2 : (byte)1 }, File.ReadAllBytes(path + ".bak"));
                else Assert.False(File.Exists(path + ".bak"));
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
