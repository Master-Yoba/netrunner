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
