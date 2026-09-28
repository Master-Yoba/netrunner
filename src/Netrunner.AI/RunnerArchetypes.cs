using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>How a Runner deck plans to win (AI plan A8, the Runner side).</summary>
public enum RunnerArchetype
{
    Generic = 0,
    /// <summary>Build the full rig and economy first, then win with inevitability.</summary>
    BigRig = 1,
    /// <summary>Run early and often; every run pays.</summary>
    Aggro = 2,
    /// <summary>Multi-access pressure on R&amp;D and HQ.</summary>
    CentralPressure = 3,
    /// <summary>Trash the Corp's economy and board.</summary>
    EconDenial = 4,
    /// <summary>Virus programs that tax the Corp until it purges.</summary>
    Virus = 5,
}

/// <summary>The plan-level knobs a Runner archetype sets in the Runner policy (v11).</summary>
public sealed record RunnerArchetypeProfile(
    double Aggression,
    double SetupWeight,
    double RdFocus,
    double HqFocus,
    double TrashWeight,
    double VirusWeight)
{
    public static RunnerArchetypeProfile Generic { get; } = new(0, 0, 1, 1, 1, 0);

    public static RunnerArchetypeProfile Blend(RunnerArchetypeProfile primary, RunnerArchetypeProfile secondary, double secondaryWeight)
    {
        double w = secondaryWeight, p = 1 - w;
        return new(p * primary.Aggression + w * secondary.Aggression, p * primary.SetupWeight + w * secondary.SetupWeight,
            p * primary.RdFocus + w * secondary.RdFocus, p * primary.HqFocus + w * secondary.HqFocus,
            p * primary.TrashWeight + w * secondary.TrashWeight, p * primary.VirusWeight + w * secondary.VirusWeight);
    }
}

public static class RunnerArchetypes
{
    public static IReadOnlyDictionary<string, RunnerArchetype> Keys { get; } = new Dictionary<string, RunnerArchetype>(StringComparer.Ordinal)
    {
        ["big-rig"] = RunnerArchetype.BigRig,
        ["aggro"] = RunnerArchetype.Aggro,
        ["central-pressure"] = RunnerArchetype.CentralPressure,
        ["econ-denial"] = RunnerArchetype.EconDenial,
        ["virus"] = RunnerArchetype.Virus,
    };

    public static RunnerArchetypeProfile Profile(RunnerArchetype archetype) => archetype switch
    {
        // Aggression: a flat bonus (+) or penalty (-) per affordable run, in hundreds; setup: extra value for rig pieces while the rig is incomplete.
        RunnerArchetype.BigRig => RunnerArchetypeProfile.Generic with { Aggression = -0.8, SetupWeight = 1 },
        RunnerArchetype.Aggro => RunnerArchetypeProfile.Generic with { Aggression = 1, HqFocus = 1.15 },
        RunnerArchetype.CentralPressure => RunnerArchetypeProfile.Generic with { RdFocus = 1.4, HqFocus = 1.2 },
        RunnerArchetype.EconDenial => RunnerArchetypeProfile.Generic with { TrashWeight = 1.5, Aggression = 0.4 },
        RunnerArchetype.Virus => RunnerArchetypeProfile.Generic with { VirusWeight = 1, SetupWeight = 0.5 },
        _ => RunnerArchetypeProfile.Generic,
    };

    public static RunnerArchetypeProfile Profile(RunnerArchetype primary, RunnerArchetype secondary) =>
        secondary == RunnerArchetype.Generic || secondary == primary
            ? Profile(primary)
            : RunnerArchetypeProfile.Blend(Profile(primary), Profile(secondary), CorpArchetypes.SecondaryWeight);

    /// <summary>
    /// A Runner deck's archetypes from its cards: viruses, run events and
    /// run-success payoffs, multi-access and central run events, trash payoffs,
    /// and how much of the deck is rig and economy.
    /// </summary>
    public static (RunnerArchetype Primary, RunnerArchetype Secondary) Infer(IEnumerable<(CardEvaluationDescriptor Card, int Quantity)> deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        var score = Enum.GetValues<RunnerArchetype>().ToDictionary(value => value, _ => 0.0);
        foreach ((CardEvaluationDescriptor card, int quantity) in deck)
        {
            BehaviorInstruction[] all = card.Behavior is null ? []
                : [.. HeuristicContext.Flatten(card.Behavior.Instructions.Concat(card.Behavior.Triggers.SelectMany(trigger => trigger.Instructions))
                    .Concat(card.Behavior.PaidAbilities.SelectMany(ability => ability.Instructions)))];
            bool virus = card.Printed.Subtypes.Any(subtype => string.Equals(subtype, "virus", StringComparison.OrdinalIgnoreCase));
            bool successPayoff = card.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.RunSuccessful) == true;
            bool central = all.Any(instruction => instruction.RunOperand is { ServerKind: ServerKind.RAndD or ServerKind.Headquarters });
            bool multiAccess = all.Any(instruction => instruction.RunOperand is { BreachAccessDeltaPerCounter: > 0 })
                || card.Behavior?.FirstHqBreachAccessModifiers is { Count: > 0 };
            if (virus) score[RunnerArchetype.Virus] += quantity * 1.5;
            if (successPayoff) score[RunnerArchetype.Aggro] += quantity;
            if (card.RunEvent is not null) score[RunnerArchetype.Aggro] += quantity * 0.7;
            if (central) score[RunnerArchetype.CentralPressure] += quantity;
            if (multiAccess) score[RunnerArchetype.CentralPressure] += quantity;
            if (card.Printed.CardClass is RuntimeCardClass.Hardware or RuntimeCardClass.Resource && !successPayoff)
                score[RunnerArchetype.BigRig] += quantity * 0.6;
            if (card.Icebreaker is not null) score[RunnerArchetype.BigRig] += quantity * 0.4;
        }
        score.Remove(RunnerArchetype.Generic);
        KeyValuePair<RunnerArchetype, double>[] ranked = [.. score.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key)];
        if (ranked[0].Value <= 0) return (RunnerArchetype.Generic, RunnerArchetype.Generic);
        return (ranked[0].Key, ranked[1].Value >= ranked[0].Value / 2 ? ranked[1].Key : RunnerArchetype.Generic);
    }
}
