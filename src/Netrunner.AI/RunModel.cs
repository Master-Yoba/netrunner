using System.Numerics;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>How bad a fired subroutine is, for choosing what to let through.</summary>
public enum SubroutineSeverity
{
    Minor,
    Moderate,
    Serious,
}

/// <summary>
/// What a subroutine does when it fires, in a small typed vocabulary read from
/// the behavior catalog rather than card text (A4a).
/// </summary>
/// <remarks><paramref name="TrashClass"/> limits what a trash subroutine can hit; <paramref name="EndsRunWithoutTarget"/>
/// means it ends the run instead when the Runner has nothing it can trash.</remarks>
public sealed record SubroutineEffect(bool EndsRun, int? EndsRunIfCreditsAtMost, int Damage, int Tags, int CreditsLost,
    int PayOrTagCredits, int PayOrTagTags, bool TrashesCard, bool InstallsIce, bool Restricts, int CorpGains,
    RuntimeCardClass? TrashClass = null, bool EndsRunWithoutTarget = false)
{
    public SubroutineSeverity Severity => EndsRun || EndsRunIfCreditsAtMost is not null || TrashesCard || Restricts ? SubroutineSeverity.Serious
        : Damage > 0 || Tags > 0 || InstallsIce || CreditsLost >= 3 || PayOrTagTags > 0 ? SubroutineSeverity.Moderate
        : SubroutineSeverity.Minor;

    public static SubroutineEffect Of(IceSubroutineSpec specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var none = new SubroutineEffect(false, null, 0, 0, 0, 0, 0, false, false, false, 0);
        return specification switch
        {
            EndRunSubroutineSpec => none with { EndsRun = true },
            EndRunIfCreditsAtMostSubroutineSpec conditional when conditional.Target == PlayerSide.Runner
                => none with { EndsRunIfCreditsAtMost = conditional.CreditLimit },
            LoseCreditsSubroutineSpec loss when loss.Target == PlayerSide.Runner => none with { CreditsLost = loss.Amount },
            DamageSubroutineSpec damage => none with { Damage = damage.Amount },
            GiveTagsSubroutineSpec tags => none with { Tags = tags.Amount },
            PayCreditsOrTagSubroutineSpec pay => none with { PayOrTagCredits = pay.CreditAmount, PayOrTagTags = pay.TagAmount },
            TrashInstalledRunnerCardSubroutineSpec trash => none with
            {
                TrashesCard = true,
                TrashClass = trash.RequiredCardClass,
                EndsRunWithoutTarget = trash.EndRunAlternative,
            },
            InstallCardSubroutineSpec => none with { InstallsIce = true },
            RunRestrictionSubroutineSpec => none with { Restricts = true },
            GainCreditsSubroutineSpec gain when gain.Target == PlayerSide.Corp => none with { CorpGains = gain.Amount },
            _ => none,
        };
    }
}

/// <summary>How the Runner gets past one piece of ICE on the planned path.</summary>
public sealed record IceCrossing(string IceId, bool Known, string Method, int Credits, int Clicks, int Damage, int Tags,
    int CreditsLost, SubroutineSeverity WorstFired);

/// <summary>
/// The cheapest complete path into a server from the visible board (A4a): the
/// credits and clicks spent breaking and paying tolls, what is let through, and
/// ICE the Runner can't evaluate because it's unrezzed or unknown.
/// </summary>
public sealed record RunForecast(bool CanGetIn, string? BlockedBy, int Credits, int Clicks, int CreditsLost, int Damage, int Tags,
    int UnknownIce, bool SeriousFired, IReadOnlyList<IceCrossing> Crossings);

/// <summary>
/// Plans a run as one path through every piece of ICE, outermost to innermost,
/// with the clicks and credits available (A4a, after chiriboga's run calculator;
/// a clean-room design). For each known piece of ICE it chooses which
/// subroutines to break (with a matching breaker, a hosted Trojan's counters or
/// a bioroid's lost clicks) and which to let fire, taking the cheapest choice
/// that lets the run continue; a path that has to let an end-the-run fire is
/// blocked. Unrezzed ICE is counted, not priced: the caller weighs that risk.
/// </summary>
public static class RunModel
{
    /// <summary>Credit-equivalent penalties for letting a subroutine fire.</summary>
    private const int DamagePenalty = 2;
    private const int TagPenalty = 2;
    private const int ClickPenalty = 2;
    private const int InstallIcePenalty = 3;
    private const int RestrictionPenalty = 2;

    /// <summary>Forecasts a run on <paramref name="serverId"/> from an observation (validation and tests).</summary>
    public static RunForecast Forecast(Observation observation, ICardEvaluationCatalog catalog, string serverId, int? clicksAfterStart = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(catalog);
        AgentFeatures features = ObservationReader.Read(observation, AgentFeatures.VersionWithHiddenAdvancements);
        var context = new HeuristicContext(features, catalog,
            RunnerPolicyProfiles.Create("standard", 0, RunnerHeuristicPolicy.LatestHeuristic), generation: 6);
        AgentServer server = features.Board.Servers.Single(value => value.Id == serverId);
        return Plan(context, server, server.Ice.Length - 1, features.Self.Credits, clicksAfterStart ?? Math.Max(0, features.Self.Clicks - 1));
    }

    /// <param name="grip">The Runner's grip size; defaults to the viewer's hand, which is the grip when the Runner plans.</param>
    /// <param name="corpRezBudget">
    /// From the Corp's side (public information about the Runner, its own ICE known):
    /// the credits it can spend rezzing, so unrezzed ICE counts as rezzed while the
    /// budget lasts, outermost first. Null when the Runner plans.
    /// </param>
    internal static RunForecast Plan(HeuristicContext context, AgentServer server, int lastIndex, int credits, int clicks, int upfrontCost = 0,
        int? grip = null, int? corpRezBudget = null)
    {
        clicks = Math.Max(0, clicks);
        int gripSize = grip ?? context.Features.Hand.Length;
        int rezBudget = corpRezBudget ?? 0;
        int spent = 0, clicksSpent = 0, lost = 0, damage = 0, tags = 0, unknown = 0;
        bool serious = false;
        var crossings = new List<IceCrossing>();
        int balance = credits - upfrontCost;
        for (int index = Math.Min(lastIndex, server.Ice.Length - 1); index >= 0; index--)
        {
            AgentCard? card = server.Ice[index];
            IceBehaviorSpec? ice = context.Definition(card)?.Ice;
            bool faced = card is not null && ice is not null && (card.Rezzed == true || context.Features.Board.Run?.Encounter?.Ice?.Id == card.Id);
            if (!faced && corpRezBudget is not null && card is not null && ice is not null && rezBudget >= ice.RezCost)
            {
                rezBudget -= ice.RezCost;
                faced = true;
            }
            if (!faced)
            {
                unknown++;
                continue;
            }
            IceCrossing? crossing = Cross(context, server, card!, ice!, balance - spent - lost, clicks - clicksSpent, gripSize - damage);
            if (crossing is null)
                return new(false, card!.Id, spent, clicksSpent, lost, damage, tags, unknown, serious, crossings);
            crossings.Add(crossing);
            spent += crossing.Credits;
            clicksSpent += crossing.Clicks;
            lost += crossing.CreditsLost;
            damage += crossing.Damage;
            tags += crossing.Tags;
            serious |= crossing.WorstFired == SubroutineSeverity.Serious;
        }
        (bool tollBlocked, int tollCredits, int tollClicks) = RunnerHeuristicPolicy.ApproachToll(context, server,
            balance - spent - lost, clicks - clicksSpent);
        if (tollBlocked) return new(false, "approach-toll", spent, clicksSpent, lost, damage, tags, unknown, serious, crossings);
        return new(true, null, spent + tollCredits, clicksSpent + tollClicks, lost, damage, tags, unknown, serious, crossings);
    }

    /// <summary>The cheapest way past one known piece of ICE within the budget, or null if every way ends the run.</summary>
    private static IceCrossing? Cross(HeuristicContext context, AgentServer server, AgentCard card, IceBehaviorSpec ice,
        int credits, int clicks, int grip)
    {
        IReadOnlyList<IceSubroutineSpec> subroutines = ice.Subroutines;
        if (subroutines.Count > 8) return null; // beyond the bounded search: treat as unknown-hard
        SubroutineEffect[] effects = subroutines.Select(SubroutineEffect.Of).ToArray();
        int all = (1 << effects.Length) - 1;
        int strength = IceStrength(context, server, card, ice);
        int hosted = context.Features.Board.RunnerRig.Where(rig => rig.HostCardId == card.Id
            && context.Definition(rig)?.Behavior?.Hosting?.BreakHostSubroutine == true)
            .Sum(rig => rig.Counters.Where(counter => counter.Purpose == CardCounterPurpose.Virus).Sum(counter => counter.Count));
        bool bioroid = ice.EncounteredBioroidAbilities is { Count: > 0 };
        var breakers = context.Features.Board.RunnerRig
            .Select(rig => (Card: rig, Breaker: context.Definition(rig)?.Icebreaker))
            .Where(pair => pair.Breaker is { } breaker && (breaker.InterfaceType == IceType.Any || breaker.InterfaceType == ice.IceType))
            .ToArray();
        IceCrossing? best = null;
        int bestScore = int.MaxValue;
        for (int broken = all; ; broken = (broken - 1) & all)
        {
            int fired = all & ~broken;
            int count = BitOperations.PopCount((uint)broken);
            // Pay for the broken subroutines: hosted counters first, then one breaker or bioroid clicks.
            int free = Math.Min(hosted, count);
            int rest = count - free;
            var options = new List<(string Method, int Credits, int Clicks)>();
            if (rest == 0) options.Add((free > 0 ? "hosted" : "none", 0, 0));
            foreach ((AgentCard breakerCard, IcebreakerBehaviorSpec? spec) in breakers)
            {
                if (rest == 0) break;
                IcebreakerBehaviorSpec breaker = spec!;
                int current = BreakerStrength(context, breakerCard, breaker);
                int increment = RunnerValues.PumpStrength(context, breaker);
                if (strength > current && increment <= 0) continue;
                int pumps = strength <= current ? 0 : (strength - current + increment - 1) / increment;
                int limit = breaker.BreakLimit == 0 ? rest : breaker.BreakLimit;
                bool encountering = context.Features.Board.Run?.Encounter?.Ice?.Id == card.Id;
                options.Add((breakerCard.Id, pumps * breaker.PumpCost
                    + (rest + limit - 1) / limit * RunnerValues.BreakCost(context, breakerCard, breaker, useIssuedQuote: encountering), 0));
            }
            if (rest > 0 && bioroid) options.Add(("bioroid", 0, rest));
            foreach ((string method, int creditCost, int clickCost) in options)
            {
                if (creditCost > credits || clickCost > clicks) continue;
                int balance = credits - creditCost;
                bool ends = false;
                int damage = 0, tags = 0, lost = 0, penalty = 0;
                SubroutineSeverity worst = SubroutineSeverity.Minor;
                for (int ordinal = 0; ordinal < effects.Length; ordinal++)
                {
                    if ((fired & (1 << ordinal)) == 0) continue;
                    SubroutineEffect effect = effects[ordinal];
                    if (effect.Severity > worst) worst = effect.Severity;
                    int drained = Math.Min(balance, effect.CreditsLost);
                    balance -= drained;
                    lost += drained;
                    ends |= effect.EndsRun || effect.EndsRunIfCreditsAtMost is { } limit && balance <= limit;
                    damage += effect.Damage;
                    if (effect.PayOrTagCredits > 0 && balance - effect.PayOrTagCredits >= 2)
                    {
                        balance -= effect.PayOrTagCredits;
                        lost += effect.PayOrTagCredits;
                    }
                    else tags += effect.PayOrTagTags;
                    tags += effect.Tags;
                    if (effect.TrashesCard)
                    {
                        AgentCard[] targets = context.Features.Board.RunnerRig.Where(rig => effect.TrashClass is not { } required
                            || context.Definition(rig)?.Printed.CardClass == required).ToArray();
                        ends |= targets.Length == 0 && effect.EndsRunWithoutTarget;
                        penalty += Math.Min(6, targets.Select(rig => RunnerValues.Card(context, rig)).DefaultIfEmpty(0).Max() / 100);
                    }
                    if (effect.InstallsIce) penalty += InstallIcePenalty;
                    if (effect.Restricts) penalty += RestrictionPenalty;
                }
                // Damage that empties the grip is treated as lethal: the Runner flatlines
                // only past the grip, but an empty grip loses to the next point of damage
                // (keeping this caution tested better on the ladder than taking the hit).
                if (ends || damage > 0 && damage >= grip) continue;
                int score = creditCost + clickCost * ClickPenalty + lost + damage * DamagePenalty + tags * TagPenalty + penalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new(card.Id, true, method, creditCost, clickCost, damage, tags, lost, fired == 0 ? SubroutineSeverity.Minor : worst);
                }
            }
            if (broken == 0) break;
        }
        return best;
    }

    /// <summary>The ICE's strength as the Runner will meet it: the live quote while encountering, else printed plus known modifiers.</summary>
    internal static int IceStrength(HeuristicContext context, AgentServer server, AgentCard card, IceBehaviorSpec ice)
    {
        if (context.Features.Board.Run?.Encounter is { } active && active.Ice?.Id == card.Id)
            return active.StrengthQuote?.EffectiveValue ?? ice.Strength;
        int strength = ice.Strength;
        strength += ice.StrengthModifiers.Where(modifier => modifier.Condition == IceStrengthModifierCondition.ProtectingRemoteServer)
            .Sum(modifier => server.Kind == ServerKind.Remote ? modifier.Delta : 0);
        strength += (ice.ValueModifiers ?? []).Where(modifier => modifier.Source == BehaviorValueSourceKind.CounterThreshold
            && (modifier.CounterName == "advancement" ? card.AdvancementCounters ?? 0
                : card.Counters.Where(counter => counter.CounterName == modifier.CounterName).Sum(counter => counter.Count))
            >= (modifier.Threshold ?? int.MaxValue)).Sum(modifier => modifier.Delta);
        return strength;
    }

    /// <summary>A breaker's strength: the live quote while encountering, else base plus per-installed-card modifiers.</summary>
    internal static int BreakerStrength(HeuristicContext context, AgentCard card, IcebreakerBehaviorSpec breaker)
    {
        int? quoted = context.Features.Board.Run?.Encounter?.BreakerStrengths.FirstOrDefault(value => value.CardId == card.Id)?.StrengthQuote.EffectiveValue;
        if (quoted is { } value) return value;
        return breaker.BaseStrength + (breaker.ValueModifiers ?? []).Where(modifier => modifier.Source == BehaviorValueSourceKind.PerInstalledCard)
            .Sum(modifier => modifier.Delta * context.Features.Board.RunnerRig.Count(known => context.Definition(known)?.Icebreaker is not null));
    }
}
