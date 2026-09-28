using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>How a Corp deck plans to win (AI plan A8).</summary>
public enum CorpArchetype
{
    Generic = 0,
    /// <summary>One or two remotes behind expensive, taxing ICE; score safely.</summary>
    Glacier = 1,
    /// <summary>Score early behind cheap ICE before the Runner sets up.</summary>
    Rush = 2,
    /// <summary>Score without leaving an agenda exposed for a Runner turn.</summary>
    FastAdvance = 3,
    /// <summary>Traps and agendas alike in remotes; the Runner can't tell which is which.</summary>
    ShellGame = 4,
    /// <summary>Win by damage.</summary>
    Kill = 5,
    /// <summary>Land tags, then cash them in.</summary>
    TagPunish = 6,
    /// <summary>Many cheap remotes with assets.</summary>
    Horizontal = 7,
}

/// <summary>
/// The plan-level knobs an archetype sets in the Corp policy (v12). A profile
/// isn't a separate bot: the same heuristic reads these numbers.
/// </summary>
public sealed record CorpArchetypeProfile(
    double MaxRemotes,
    double MinIceForAgenda,
    double ScoringIceTarget,
    double CentralIceTarget,
    double HoldAgendas,
    double RushBonus,
    double AssetBonus,
    double NeverAdvance,
    double TrapWeight,
    double TagWeight,
    double DamageWeight)
{
    /// <summary>What v11 does without an archetype: at most two remotes, HQ and R&amp;D to two ICE.</summary>
    public static CorpArchetypeProfile Generic { get; } = new(2, 1, 2, 2, 0, 0, 0, 0, 1, 1, 1);

    public static CorpArchetypeProfile Blend(CorpArchetypeProfile primary, CorpArchetypeProfile secondary, double secondaryWeight)
    {
        double w = secondaryWeight, p = 1 - w;
        return new(p * primary.MaxRemotes + w * secondary.MaxRemotes, p * primary.MinIceForAgenda + w * secondary.MinIceForAgenda,
            p * primary.ScoringIceTarget + w * secondary.ScoringIceTarget, p * primary.CentralIceTarget + w * secondary.CentralIceTarget,
            p * primary.HoldAgendas + w * secondary.HoldAgendas, p * primary.RushBonus + w * secondary.RushBonus,
            p * primary.AssetBonus + w * secondary.AssetBonus, p * primary.NeverAdvance + w * secondary.NeverAdvance,
            p * primary.TrapWeight + w * secondary.TrapWeight, p * primary.TagWeight + w * secondary.TagWeight,
            p * primary.DamageWeight + w * secondary.DamageWeight);
    }
}

public static class CorpArchetypes
{
    /// <summary>How much a secondary archetype counts against the primary.</summary>
    public const double SecondaryWeight = 0.3;

    public static IReadOnlyDictionary<string, CorpArchetype> Keys { get; } = new Dictionary<string, CorpArchetype>(StringComparer.Ordinal)
    {
        ["glacier"] = CorpArchetype.Glacier,
        ["rush"] = CorpArchetype.Rush,
        ["fast-advance"] = CorpArchetype.FastAdvance,
        ["shell-game"] = CorpArchetype.ShellGame,
        ["kill"] = CorpArchetype.Kill,
        ["tag-punish"] = CorpArchetype.TagPunish,
        ["horizontal"] = CorpArchetype.Horizontal,
    };

    public static CorpArchetypeProfile Profile(CorpArchetype archetype) => archetype switch
    {
        CorpArchetype.Glacier => CorpArchetypeProfile.Generic with { MaxRemotes = 2, MinIceForAgenda = 3, ScoringIceTarget = 3 },
        CorpArchetype.Rush => CorpArchetypeProfile.Generic with { MinIceForAgenda = 1, ScoringIceTarget = 1, CentralIceTarget = 1, RushBonus = 1 },
        CorpArchetype.FastAdvance => CorpArchetypeProfile.Generic with { MaxRemotes = 1.5, HoldAgendas = 1 },
        CorpArchetype.ShellGame => CorpArchetypeProfile.Generic with
        {
            MaxRemotes = 3,
            ScoringIceTarget = 1,
            CentralIceTarget = 1.5,
            NeverAdvance = 1,
            TrapWeight = 1.7,
            AssetBonus = 0.5,
        },
        CorpArchetype.Kill => CorpArchetypeProfile.Generic with { DamageWeight = 1.8 },
        CorpArchetype.TagPunish => CorpArchetypeProfile.Generic with { TagWeight = 1.8, DamageWeight = 1.4 },
        CorpArchetype.Horizontal => CorpArchetypeProfile.Generic with
        {
            MaxRemotes = 4,
            MinIceForAgenda = 0.5,
            ScoringIceTarget = 1,
            CentralIceTarget = 1.5,
            AssetBonus = 1,
        },
        _ => CorpArchetypeProfile.Generic,
    };

    /// <summary>The profile for a primary and optional secondary archetype.</summary>
    public static CorpArchetypeProfile Profile(CorpArchetype primary, CorpArchetype secondary) =>
        secondary == CorpArchetype.Generic || secondary == primary
            ? Profile(primary)
            : CorpArchetypeProfile.Blend(Profile(primary), Profile(secondary), SecondaryWeight);

    /// <summary>
    /// A deck's archetypes from its cards, for decks that don't declare them:
    /// traps, tag sources and punishments, damage, assets, fast-advance cards and
    /// the cost of its ICE each score an archetype; the best two are kept when
    /// the second scores at least half the first.
    /// </summary>
    public static (CorpArchetype Primary, CorpArchetype Secondary) Infer(IEnumerable<(CardEvaluationDescriptor Card, int Quantity)> deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        var score = Enum.GetValues<CorpArchetype>().ToDictionary(value => value, _ => 0.0);
        double iceCost = 0, ice = 0;
        foreach ((CardEvaluationDescriptor card, int quantity) in deck)
        {
            IEnumerable<BehaviorInstruction> instructions = card.Behavior is null ? []
                : HeuristicContext.Flatten(card.Behavior.Instructions.Concat(card.Behavior.Triggers.SelectMany(trigger => trigger.Instructions)));
            BehaviorInstruction[] all = [.. instructions];
            bool tags = all.Any(instruction => instruction.Kind == BehaviorInstructionKind.AddTags)
                || card.Ice?.Subroutines.Any(subroutine => subroutine is GiveTagsSubroutineSpec) == true;
            bool punish = all.Any(instruction => instruction.Kind == BehaviorInstructionKind.Branch
                && instruction.Branch?.Condition.Kind == BehaviorConditionKind.Tagged);
            bool damage = all.Any(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage);
            bool trap = card.Behavior?.CanBeAdvanced == true && card.Ice is null && card.Agenda is null && damage;
            if (card.Ice is { } spec)
            {
                ice += quantity;
                iceCost += spec.RezCost * quantity;
            }
            // Tags alone are common; a tag deck also carries cards that punish them.
            if (tags) score[CorpArchetype.TagPunish] += quantity * 0.5;
            if (punish) score[CorpArchetype.TagPunish] += quantity * 1.5;
            // An upgrade that works on approach (Manegarm Skunkworks) taxes a tall server.
            if (card.Printed.CardClass == RuntimeCardClass.Upgrade
                && card.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.ServerApproached) == true)
                score[CorpArchetype.Glacier] += quantity * 2;
            if (damage && !trap) score[CorpArchetype.Kill] += quantity * 0.6;
            if (trap) score[CorpArchetype.ShellGame] += quantity * 1.5;
            if (card.Printed.CardClass == RuntimeCardClass.Asset && !trap) score[CorpArchetype.Horizontal] += quantity * 0.8;
            if (card.Printed.CardClass == RuntimeCardClass.Operation && all.Any(instruction => instruction.Kind == BehaviorInstructionKind.AdvanceCard))
                score[CorpArchetype.FastAdvance] += quantity * 2;
        }
        double average = ice == 0 ? 0 : iceCost / ice;
        score[CorpArchetype.Glacier] += Math.Max(0, average - 3) * 8;
        score[CorpArchetype.Rush] += Math.Max(0, 3 - average) * 4;
        score.Remove(CorpArchetype.Generic);
        KeyValuePair<CorpArchetype, double>[] ranked = [.. score.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key)];
        if (ranked[0].Value <= 0) return (CorpArchetype.Generic, CorpArchetype.Generic);
        return (ranked[0].Key, ranked[1].Value >= ranked[0].Value / 2 ? ranked[1].Key : CorpArchetype.Generic);
    }
}
