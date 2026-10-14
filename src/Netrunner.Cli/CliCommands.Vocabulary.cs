using System.Collections;
using System.Reflection;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// card-vocabulary [--json]. E6 Core-growth tracking: for each registered card, the engine vocabulary its behaviour uses:
    /// every Netrunner.Core enum value and every concrete Netrunner.Core spec type reachable from its evaluation descriptor,
    /// and its extension handler (bespoke engine code), if any. <c>scripts/core-growth.py</c> compares it with the vocabulary
    /// a batch added to Core. Exit 0 on success, 2 for invalid arguments.
    /// </summary>
    private static int RunCardVocabulary(string[] args, TextWriter output, TextWriter error)
    {
        bool json = args.Contains("--json", StringComparer.Ordinal);
        if (args.Skip(1).Any(arg => arg != "--json"))
        {
            error.WriteLine("card-vocabulary failed: the only option is --json.");
            return 2;
        }
        // The widest embedded pool: Startup (Gateway, Elevation and Vantage Point)
        // in an official build, so the E6 report sees every registered card.
        CardPool pool = CardPools.Startup.IsEmbedded ? CardPools.Startup
            : CardPools.GatewayElevation.IsEmbedded ? CardPools.GatewayElevation : CardPools.Gateway;
        BehaviorCatalog behavior = pool.CreateBehavior();
        IRuntimeCardCatalog runtime = CardDataResource.EmbeddedMode == CardDataResource.OfficialMode
            ? pool.CreateEmbeddedRuntime() : null!;
        if (runtime is null)
        {
            error.WriteLine("card-vocabulary failed: it needs previously provisioned official content embedded at build time.");
            return 2;
        }
        var catalog = new GatewayEvaluationCatalog(runtime, behavior);
        var cards = catalog.DefinitionIds.OrderBy(id => id.Value, StringComparer.Ordinal).Select(id =>
        {
            catalog.TryGet(id, out CardEvaluationDescriptor descriptor);
            var found = new SortedSet<string>(StringComparer.Ordinal);
            Walk(descriptor, found, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            return new { code = id.Value, extensionHandler = descriptor.Behavior?.ExtensionHandlerId, vocabulary = found.ToArray() };
        }).ToArray();
        if (json)
        {
            output.WriteLine(EngineJson.Serialize(new { schemaVersion = 1, cards }));
            return 0;
        }
        foreach (var card in cards)
            output.WriteLine($"{card.code} {(card.extensionHandler is null ? "" : "handler=" + card.extensionHandler + " ")}{string.Join(' ', card.vocabulary)}");
        return 0;
    }

    private static readonly Assembly CoreAssembly = typeof(GameState).Assembly;

    /// <summary>Collects Core enum values ("Type.Value") and concrete Core spec types ("type:Name") reachable from a value.</summary>
    private static void Walk(object? value, SortedSet<string> found, HashSet<object> seen, int depth)
    {
        if (value is null || depth > 24) return;
        Type type = value.GetType();
        if (type.IsEnum)
        {
            if (type.Assembly == CoreAssembly) found.Add($"{type.Name}.{value}");
            return;
        }
        if (type.IsPrimitive || value is string or decimal) return;
        if (!type.IsValueType && !seen.Add(value)) return;
        if (value is IEnumerable sequence)
        {
            foreach (object? item in sequence) Walk(item, found, seen, depth + 1);
            return;
        }
        if (type.Assembly != CoreAssembly) return;
        if (!type.IsValueType) found.Add($"type:{type.Name}");
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            object? child;
            try { child = property.GetValue(value); }
            catch (TargetInvocationException) { continue; }
            Walk(child, found, seen, depth + 1);
        }
    }
}
