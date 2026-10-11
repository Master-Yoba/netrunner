using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>Detached, immutable public definitions for observation-only agents.</summary>
public sealed class GatewayEvaluationCatalog : ICardEvaluationCatalog
{
    public const string CurrentVersion = "gateway-evaluation-v1";
    private readonly ReadOnlyDictionary<CardDefinitionId, CardEvaluationDescriptor> _cards;

    public GatewayEvaluationCatalog(IRuntimeCardCatalog runtime, IBehaviorCatalog behavior)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(behavior);
        BehaviorVersion = behavior.Version;
        var cards = new Dictionary<CardDefinitionId, CardEvaluationDescriptor>();
        foreach (CardDefinitionId id in behavior.AllDefinitionIds.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            if (!runtime.TryGet(id, out CardRuntimeSpec? printed)) continue;
            behavior.TryGet(id, out CardBehaviorDefinition? definition);
            behavior.TryGetIce(id, out IceBehaviorSpec? ice);
            behavior.TryGetIcebreaker(id, out IcebreakerBehaviorSpec? breaker);
            behavior.TryGetRunEvent(id, out RunEventBehaviorSpec? runEvent);
            behavior.TryGetAsset(id, out AssetBehaviorSpec? asset);
            behavior.TryGetAgenda(id, out AgendaBehaviorSpec? agenda);
            // Deeply detach through the existing typed constructors, which
            // freeze declaration collections. No metadata text enters this API.
            printed = EngineJson.Deserialize<CardRuntimeSpec>(EngineJson.Serialize(printed));
            definition = Copy(definition);
            ice = Copy(ice);
            breaker = Copy(breaker);
            runEvent = Copy(runEvent);
            BehaviorInstructionKind[] effects = definition is null ? [] : Walk(definition.Instructions
                .Concat(definition.Triggers.SelectMany(trigger => trigger.Instructions))
                .Concat(definition.PaidAbilities.SelectMany(ability => ability.Instructions)))
                .Select(instruction => instruction.Kind).Distinct().Order().ToArray();
            cards.Add(id, new(printed, definition, ice, breaker, runEvent, Copy(asset), Copy(agenda), Array.AsReadOnly(effects),
                RequiresApproximateEvaluation: true));
        }
        _cards = new ReadOnlyDictionary<CardDefinitionId, CardEvaluationDescriptor>(cards);
        DefinitionIds = Array.AsReadOnly(cards.Keys.ToArray());
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(new
        {
            Version,
            Runtime = runtime.Fingerprint,
            Behavior = behavior.Fingerprint,
            Cards = cards.Values.ToArray(),
        })))).ToLowerInvariant();
    }

    public string Version => CurrentVersion;
    public string? BehaviorVersion { get; }
    public string Fingerprint { get; }
    public IReadOnlyCollection<CardDefinitionId> DefinitionIds { get; }
    public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) => _cards.TryGetValue(definitionId, out descriptor!);
    public string? FactionOf(CardDefinitionId definitionId) => _cards.ContainsKey(definitionId) ? Factions.Value.GetValueOrDefault(definitionId.Value) : null;

    /// <summary>The embedded cards' printed factions by code; empty where no card data is embedded.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Factions = new(() =>
        CardDataResource.TryLoadEmbedded(out _, out _)
            ? CardDataResource.LoadEmbeddedPool().Cards.GroupBy(card => card.Code, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Faction, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static T? Copy<T>(T? value) where T : class => value is null ? null : EngineJson.Deserialize<T>(EngineJson.Serialize(value));

    private static IEnumerable<BehaviorInstruction> Walk(IEnumerable<BehaviorInstruction> instructions)
    {
        foreach (BehaviorInstruction instruction in instructions)
        {
            yield return instruction;
            if (instruction.Branch is { } branch)
                foreach (BehaviorInstruction nested in Walk(branch.WhenTrue.Concat(branch.WhenFalse))) yield return nested;
            if (instruction.Choice is { } choice)
                foreach (BehaviorInstruction nested in Walk(choice.Options.SelectMany(option => option.Instructions))) yield return nested;
        }
    }
}
