using Netrunner.Core;

if (args is ["self-test", "--seed", var seedText]
    && ulong.TryParse(seedText, out ulong seed))
{
    DeterministicRandom first = new(seed);
    ulong[] values = Enumerable.Range(0, 4).Select(_ => first.NextUInt64()).ToArray();
    DeterministicRandom restored = DeterministicRandom.Restore(first.State);

    Console.WriteLine($"rules={BuildMetadata.RulesVersion}");
    Console.WriteLine($"seed={seed}");
    Console.WriteLine($"sample={string.Join(',', values)}");
    Console.WriteLine($"restored-next={restored.NextUInt64()}");
    return 0;
}

Console.Error.WriteLine("Usage: Netrunner.Cli self-test --seed <unsigned-integer>");
return 2;

