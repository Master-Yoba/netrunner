using System.Runtime.InteropServices;
using System.Text.Json;
using Netrunner.Core;

if (args is ["doctor"] or ["doctor", "--json"])
{
    string? godotPath = Environment.GetEnvironmentVariable("GODOT4");
    bool godotAvailable = godotPath is not null && File.Exists(godotPath);
    var report = new
    {
        status = godotAvailable ? "ok" : "incomplete",
        application = BuildMetadata.Current,
        runtime = RuntimeInformation.FrameworkDescription,
        os = RuntimeInformation.OSDescription,
        architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        godot = new
        {
            path = godotPath,
            available = godotAvailable,
        },
    };

    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return godotAvailable ? 0 : 1;
}

if (args is ["self-test", "--seed", var seedText]
    && ulong.TryParse(seedText, out ulong seed))
{
    DeterministicRandom first = new(seed);
    ulong[] values = Enumerable.Range(0, 4).Select(_ => first.NextUInt64()).ToArray();
    DeterministicRandom restored = DeterministicRandom.Restore(first.State);

    Console.WriteLine($"version={BuildMetadata.Current.ApplicationVersion}");
    Console.WriteLine($"rules={BuildMetadata.Current.RulesVersion}");
    Console.WriteLine($"seed={seed}");
    Console.WriteLine($"sample={string.Join(',', values)}");
    Console.WriteLine($"restored-next={restored.NextUInt64()}");
    return 0;
}

Console.Error.WriteLine("Usage: Netrunner.Cli doctor [--json] | self-test --seed <unsigned-integer>");
return 2;
