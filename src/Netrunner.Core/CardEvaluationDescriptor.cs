namespace Netrunner.Core;

/// <summary>Public definition data only; contains neither instances nor hidden decks.</summary>
public interface ICardEvaluationCatalog
{
    string Version { get; }
    string Fingerprint { get; }
    IReadOnlyCollection<CardDefinitionId> DefinitionIds { get; }
    bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor);

    /// <summary>A7: the behavior-registry version of the pool these cards play in, where the catalog knows it.</summary>
    string? BehaviorVersion => null;

    /// <summary>
    /// A card's printed faction (bot plan H3 batch 10), public like its cost: a Runner reads the Corp's identity and knows
    /// which of the pool's cards its deck is built around. Null where the catalog has no card data for it. It comes from the
    /// card data, whose checksum benchmark manifests pin already, so it stays out of <see cref="Fingerprint"/>.
    /// </summary>
    string? FactionOf(CardDefinitionId definitionId) => null;
}

/// <summary>
/// Reuses immutable compiled declarations rather than translating printed text
/// or implementing a second rules interpreter. Declarations describe potential
/// effects; current eligibility and exact dynamic costs come from legal actions.
/// </summary>
public sealed record CardEvaluationDescriptor(
    CardRuntimeSpec Printed,
    CardBehaviorDefinition? Behavior,
    IceBehaviorSpec? Ice,
    IcebreakerBehaviorSpec? Icebreaker,
    RunEventBehaviorSpec? RunEvent,
    AssetBehaviorSpec? Asset,
    AgendaBehaviorSpec? Agenda,
    IReadOnlyList<BehaviorInstructionKind> EffectKinds,
    bool RequiresApproximateEvaluation);
