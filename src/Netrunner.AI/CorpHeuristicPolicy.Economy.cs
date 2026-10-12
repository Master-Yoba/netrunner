using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class CorpHeuristicPolicy
{
    private static AgentUtility Play(HeuristicContext context, PlayCardParameters play)
    {
        AgentCard? card = context.Card(play.CardId);
        CardBehaviorDefinition? behavior = context.Definition(card)?.Behavior;
        if (behavior is null) return Value(-50, "play/unmodelled", approximate: true, fallback: true);
        int effect = Effects(context, behavior.Instructions, card, behavior.Controller);
        if (context.Configuration.PolicyVersion == V36) effect += FastAdvance(context, behavior, play.CreditCost, play.CardId.Value);
        // v30: a play only possible before the turn's first action finishes (Petty Cash) is now or never this turn.
        if (effect - play.CreditCost * context.CreditValue > 0 && NowOrNever(behavior.Conditions)) effect += NowOrNeverBonus;
        return new(effect >= 10000 ? 100 : 0, effect - play.CreditCost * context.CreditValue,
            "play/effect-minus-current-cost", Approximate: effect < 10000);
    }

    /// <summary>
    /// v36 (bot plan H3 batch 12; MATCHUP_NEBULA.md NE07 to NE09): an operation that gains clicks is worth the best agenda
    /// it lets the Corp score this turn that it can't score without it: one from HQ, installed and advanced in full, or an
    /// installed one advanced the rest of the way, each advancement a click and a credit. Front-side Nebula with 7 credits
    /// plays Nanomanagement and installs and advances a three-advancement agenda; v34 never played it. The other
    /// click-gaining operations in HQ count too, cheapest first, as far as the credits go (the review of #141: NE09's three
    /// Nanomanagements score Next Big Thing from 17 credits, and the first of them starts that line).
    /// </summary>
    internal static int FastAdvance(HeuristicContext context, CardBehaviorDefinition behavior, int creditCost, string? cardId = null)
    {
        int gained = Gained(behavior);
        if (gained <= 1) return 0;
        int clicks = context.Self.Clicks, credits = context.Self.Credits;
        // Each line plays this operation, then the others in HQ one more at a time.
        var lines = new List<(int Clicks, int Credits)> { (clicks - 1 + gained, credits - creditCost) };
        foreach ((int otherGain, int otherCost) in context.Features.Hand.Where(card => card.Id != cardId)
            .Select(card => context.Definition(card)).OfType<CardEvaluationDescriptor>()
            .Where(definition => definition.Printed.CardClass == RuntimeCardClass.Operation && definition.Behavior is not null && Gained(definition.Behavior) > 1)
            .Select(definition => (Gained(definition.Behavior!), definition.Printed.PlayCost ?? 0)).OrderBy(other => other.Item2))
        {
            (int lastClicks, int lastCredits) = lines[^1];
            if (otherCost > lastCredits || lastClicks < 1) break;
            lines.Add((lastClicks - 1 + otherGain, lastCredits - otherCost));
        }
        int best = 0;
        foreach (AgentCard card in context.Features.Hand)
            if (context.Definition(card) is { Agenda: not null, Printed: { } agenda })
                best = Math.Max(best, Enabled(1 + agenda.AdvancementRequirement, agenda.AdvancementRequirement, agenda.AgendaPoints));
        foreach (AgentCard card in context.Features.Board.Servers.Where(server => server.Kind == ServerKind.Remote).SelectMany(server => server.Root).OfType<AgentCard>())
            if (context.Definition(card) is { Agenda: not null, Printed: { } agenda })
            {
                int left = Math.Max(0, agenda.AdvancementRequirement - (card.AdvancementCounters ?? 0));
                best = Math.Max(best, Enabled(left, left, agenda.AgendaPoints));
            }
        return best;

        int Enabled(int clicksNeeded, int creditsNeeded, int points) =>
            (clicksNeeded > clicks || creditsNeeded > credits)
            && lines.Any(line => clicksNeeded <= line.Clicks && creditsNeeded <= line.Credits) ? 200 + 300 * points : 0;

        static int Gained(CardBehaviorDefinition definition) => HeuristicContext.Flatten(definition.Instructions)
            .Where(instruction => instruction.Kind == BehaviorInstructionKind.GainClicks).Sum(instruction => instruction.Amount);
    }

    private static AgentUtility Ability(HeuristicContext context, AbilityActionParameters ability)
    {
        AgentCard? card = context.Card(ability.SourceCardId);
        BehaviorPaidAbility? declaration = context.Definition(card)?.Behavior?.PaidAbilities.SingleOrDefault(value => value.Key == ability.AbilityKey);
        if (declaration is null) return Value(-50, "ability/unmodelled", approximate: true, fallback: true);
        // v28 (bot loop 1): an ability that plays its own card plays nothing when the Corp can't pay the card's cost (Petty Cash
        // from Archives with 2 credits spent the click for nothing).
        if (declaration.Instructions.Any(instruction => instruction.Kind == BehaviorInstructionKind.PlayCard
                && instruction.Target?.Kind == BehaviorTargetKind.Self)
            && (context.Definition(card)?.Printed.PlayCost ?? 0) > context.Self.Credits
                - ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount))
            return Value(-100, "ability/play-it-cannot-pay-for", approximate: true);
        int benefit = Effects(context, declaration.Instructions, card, context.Features.Side);
        // v30: Petty Cash from Archives refunds its click: a free gain, and only before the turn's first action finishes.
        if (NowOrNever(declaration.Conditions) && context.Definition(card)?.Behavior?.Instructions is { } played
            && HeuristicContext.Flatten(played).Any(instruction => instruction.Kind == BehaviorInstructionKind.GainClicks))
            benefit += NowOrNeverBonus;
        int costs = ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * context.CreditValue;
        costs += ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.TrashSelf
            || cost.Kind == BehaviorCostKind.RemoveSelfFromGame).Sum(_ => context.CardValue(card, installed: true));
        return Value(benefit - costs, "ability/payoff-minus-cost", approximate: true);
    }

    /// <summary>
    /// v15 (playtest, 30 September): shuffling up to <paramref name="count"/> cards from Archives into
    /// R&amp;D (Spin Doctor) is worth the best cards there to get back (v14 spent it with Archives empty):
    /// - an agenda, out of the Runner's reach in Archives and back where the Corp can draw it
    ///   (an agenda trashed from a server about to be broken into, then recovered);
    /// - an upgrade the Runner trashed;
    /// - an economy card while the Corp is short of money;
    /// - a face-down card, kept hidden from an Archives run;
    /// - anything, when R&amp;D is running low.
    /// It is spent once, so it normally waits for a use worth more than keeping it. Two moments
    /// don't wait (after Chiriboga's Corp AI): the Runner past the last ICE of Archives, when its
    /// agendas would be stolen, and past the last ICE of the card's own server, when it is used
    /// now or trashed.
    /// </summary>
    private static int ArchivesShuffleValue(HeuristicContext context, int count, AgentCard? source)
    {
        AgentBoard board = context.Features.Board;
        AgentServer? reached = board.Run is { EndReason: null, Successful: false, CurrentIceIndex: < 0 } run
            ? board.Servers.FirstOrDefault(server => server.Id == run.AttackedServer) : null;
        bool archivesReached = reached?.Kind == ServerKind.Archives;
        bool ownServerReached = source is not null && reached?.Root.Any(card => card?.Id == source.Id) == true;
        bool deckLow = board.HiddenZoneCounts.CorpDeck < 10;
        bool poor = context.Self.Credits < 5;
        int Worth(AgentCard card, bool faceDown)
        {
            CardEvaluationDescriptor? definition = context.Definition(card);
            int worth = definition switch
            {
                // About to be stolen: worth the points the Runner would score.
                { Agenda: { } agenda } when archivesReached => 250 + agenda.AgendaPoints * 300,
                { Agenda: not null } => 250,
                { Printed.CardClass: RuntimeCardClass.Upgrade } => 160,
                _ when IsEconomy(definition) => poor ? 160 : 40,
                _ => faceDown ? 40 : 20,
            };
            return deckLow ? Math.Max(worth, 100) : worth;
        }
        int keepForLater = archivesReached || ownServerReached ? 0 : 100;
        return board.CorpDiscard.Select(card => Worth(card, faceDown: false))
            .Concat(context.Features.PrivateCards.FaceDownArchives.Select(card => Worth(card, faceDown: true)))
            .OrderDescending().Take(count).Sum() - keepForLater;
    }

    /// <summary>
    /// v29: whether the run in progress can't get past the ICE still ahead, the one encountered included, with the Runner's
    /// credits and clicks and the Corp rezzing what it can pay for.
    /// </summary>
    private static bool RunStopsAnyway(HeuristicContext context)
    {
        if (context.Features.Board.Run is not { EndReason: null, Successful: false } run || run.CurrentIceIndex < 0) return false;
        AgentServer? server = context.Features.Board.Servers.FirstOrDefault(value => value.Id == run.AttackedServer);
        if (server is null) return false;
        int last = run.Step == RunTimingStep.JackOutDecision ? run.CurrentIceIndex - 1 : run.CurrentIceIndex;
        if (last < 0) return false;
        return !RunModel.Plan(context, server, last, context.Opponent.Credits + run.RunCredits, context.Opponent.Clicks,
            grip: context.Features.Board.HiddenZoneCounts.RunnerGrip, corpRezBudget: context.Self.Credits).CanGetIn;
    }

    /// <summary>v30: what taking a now-or-never play first is worth: the turn's other plays stay available after it.</summary>
    private const int NowOrNeverBonus = 300;

    /// <summary>v30: whether a play or ability is legal only before the turn's first action finishes.</summary>
    private static bool NowOrNever(IReadOnlyList<BehaviorCondition>? conditions) =>
        conditions?.Any(condition => condition.Kind == BehaviorConditionKind.NoActionFinishedThisTurn) == true;

    /// <summary>
    /// v30: IP Enforcement's install of an agenda worth X from the Runner's score area: the Runner loses those points and the
    /// Corp holds the agenda again, with a counter while the Runner stays tagged. When scoring it this turn wins, the play wins.
    /// </summary>
    private static int ReclaimValue(HeuristicContext context, BehaviorCardFilter? filter, int tagsRemoved)
    {
        AgentCard? agenda = context.Features.Board.RunnerScore.Where(card => context.Definition(card)?.Agenda is not null
                && (filter?.AgendaPoints is not { } points || context.Definition(card)!.Printed.AgendaPoints == points))
            .OrderByDescending(card => context.Definition(card)!.Printed.AgendaPoints).FirstOrDefault();
        if (agenda is null) return 0;
        CardEvaluationDescriptor definition = context.Definition(agenda)!;
        int points = definition.Printed.AgendaPoints;
        int counters = context.Opponent.Tags > tagsRemoved ? 1 : 0;
        int remaining = Math.Max(0, definition.Printed.AdvancementRequirement - counters);
        bool scoresNow = remaining <= context.Self.Clicks - 1 && remaining <= context.Self.Credits;
        if (scoresNow && context.Self.AgendaPoints + points >= 7) return 20000;
        return points * 350 + (scoresNow ? points * 300 : points * 120);
    }

    /// <summary>A card that makes the Corp money: when played, when its turn begins, or by its abilities.</summary>
    private static bool IsEconomy(CardEvaluationDescriptor? definition) =>
        definition?.Behavior is { } behavior
        && behavior.Instructions.Concat(behavior.Triggers.SelectMany(trigger => trigger.Instructions))
            .Concat(behavior.PaidAbilities.SelectMany(ability => ability.Instructions))
            .Any(instruction => instruction.Kind is BehaviorInstructionKind.GainCredits or BehaviorInstructionKind.LoadHostedCredits
                or BehaviorInstructionKind.PlaceHostedCredits);

    private static int Effects(HeuristicContext context, IEnumerable<BehaviorInstruction> instructions, AgentCard? source, PlayerSide controller)
    {
        int total = 0;
        BehaviorTarget? chosenTarget = null;
        int chosenCount = 1;
        int tagsRemoved = 0;
        foreach (BehaviorInstruction instruction in instructions)
        {
            if (instruction.Kind == BehaviorInstructionKind.Choose && instruction.Choice?.Target is { } target)
            {
                chosenTarget = target;
                chosenCount = instruction.Choice.Cardinality.Count ?? int.MaxValue;
            }
            bool self = controller == context.Features.Side;
            // An explicit recipient (Elevation) names whose credits change, whoever controls the effect.
            if (instruction.Recipient is BehaviorPlayerSelector.Corp or BehaviorPlayerSelector.Runner)
                self = (instruction.Recipient == BehaviorPlayerSelector.Corp) == (context.Features.Side == PlayerSide.Corp);
            int sign = self ? 1 : -1;
            int amount = HeuristicContext.EffectAmount(context, instruction, source);
            switch (instruction.Kind)
            {
                case BehaviorInstructionKind.GainCredits: total += sign * amount * (self ? context.CreditValue : 100); break;
                case BehaviorInstructionKind.LoseCredits: total -= sign * amount * context.CreditValue; break;
                // Elevation (EL.3): counters, faces, agendas and turn structure.
                case BehaviorInstructionKind.AddCounter: total += amount * 70; break;
                case BehaviorInstructionKind.RemoveCounter: total -= amount * 50; break;
                case BehaviorInstructionKind.RemoveTags: total -= amount * 150; tagsRemoved += amount; break;
                case BehaviorInstructionKind.FlipSelf: total += 50; break;
                case BehaviorInstructionKind.ForfeitAgenda: total -= 600; break;
                case BehaviorInstructionKind.GainAllottedClicksNextTurn: total += sign * amount * 90; break;
                case BehaviorInstructionKind.ProhibitScoringTargetThisTurn: total -= 20; break;
                case BehaviorInstructionKind.Sabotage: total -= amount * 80; break;
                case BehaviorInstructionKind.PlaceOnTopOfDeck: total += 60; break;
                // Elevation (EL.5): a nested play is the played operation's own value; a faceup agenda
                // (BANGUN) punishes its access.
                case BehaviorInstructionKind.PlayCard: total += 250; break;
                case BehaviorInstructionKind.TurnFaceUp: total += 40; break;
                // Elevation (EL.7): runs and ICE. Moving the Runner to Archives or swapping in fresh ICE
                // stops a run's threat; a cheaper rez is credits; a purge strips the Runner's viruses.
                case BehaviorInstructionKind.MoveRunnerToServer: total += 200; break;
                case BehaviorInstructionKind.SwapApproachedIce: total += 150; break;
                case BehaviorInstructionKind.RezPayingLess: total += amount * context.CreditValue; break;
                case BehaviorInstructionKind.PurgeVirusCounters: total += 60; break;
                case BehaviorInstructionKind.AddBreachAccess: total -= amount * 150; break;
                case BehaviorInstructionKind.RefillRecurringCredits: total += 2 * context.CreditValue; break;
                case BehaviorInstructionKind.InstallCard when instruction.SourceZone == BehaviorZone.ScoreArea:
                    total += ReclaimValue(context, instruction.Target?.Filter, tagsRemoved);
                    break;
                case BehaviorInstructionKind.InstallCard when instruction.IsZoneInstall: total += 250; break;
                case BehaviorInstructionKind.GainClicks: total += sign * amount * 100; break;
                case BehaviorInstructionKind.Draw:
                    if (controller == PlayerSide.Corp && amount >= context.Features.Board.HiddenZoneCounts.CorpDeck) return -20000;
                    total += sign * amount * (self ? context.DrawValue : context.Features.Board.HiddenZoneCounts.RunnerGrip < 3 ? 140 : 75);
                    break;
                case BehaviorInstructionKind.TakeHostedCredits:
                case BehaviorInstructionKind.TakeUpToHostedCredits:
                    total += sign * Math.Min(amount, HeuristicContext.Hosted(source)) * context.CreditValue; break;
                case BehaviorInstructionKind.TakeAllHostedCredits:
                    total += sign * HeuristicContext.Hosted(source) * context.CreditValue; break;
                case BehaviorInstructionKind.PlaceHostedCredits:
                case BehaviorInstructionKind.LoadHostedCredits: total += sign * amount * 40; break;
                case BehaviorInstructionKind.DealDamage:
                    amount = instruction.DamageAmountOperand switch
                    {
                        { Kind: BehaviorDamageAmountKind.ScoredAgendaPrintedPointsThisTurn } => context.Features.Board.TurnFacts?.Corp.ThisTurn?.AgendaTurnChanges
                            .Where(change => change.Kind == AgendaTurnChangeKind.Score).Sum(change => change.Points) ?? 0,
                        { Kind: BehaviorDamageAmountKind.SourceAdvancementCounters } damageOperand => damageOperand.BaseAmount + (source?.AdvancementCounters ?? 0),
                        _ => amount,
                    };
                    total += amount > context.Features.Board.HiddenZoneCounts.RunnerGrip ? 10000
                        : (int)(amount * 100 * context.CorpProfile.DamageWeight);
                    break;
                case BehaviorInstructionKind.AddTags:
                    // v7: clearing a tag costs the Runner a click and 2 credits, and
                    // while tagged its resources can be trashed (Wave 4 C1 audit).
                    // v12: a tag deck's punishments make each tag worth more.
                    // v13 (playtest, 29 September): a tag the Corp can't punish this turn is cleared by the
                    // Runner next turn, so it's worth only the click and two credits that costs.
                    if (context.Features.Side == PlayerSide.Corp && !CanPunishTagThisTurn(context))
                    {
                        total += amount * 150;
                        break;
                    }
                    total += (int)(amount * (250 + context.Features.Board.RunnerRig
                        .Where(card => context.Definition(card)?.Printed.CardClass == RuntimeCardClass.Resource)
                        .Select(card => context.CardValue(card, installed: true)).DefaultIfEmpty(0).Max() / 2)
                        * context.CorpProfile.TagWeight);
                    break;
                // v29 (bot loop 2): trashing its own card costs the Corp the cheapest it can choose (LEO's bioroid), where the
                // target filter's missing card type had matched the Runner's cards and made it a gain.
                case BehaviorInstructionKind.TrashCard when self
                    && instruction.Target is { Kind: BehaviorTargetKind.ChosenCard } && chosenTarget?.Filter?.OwnerSide == PlayerSide.Corp:
                    // The candidates are the target selector's: LEO's bioroid must be in the attacked server. Any other
                    // selector is priced at its dearest candidate.
                    IEnumerable<int> own = context.Features.Board.Servers
                        .Where(server => !chosenTarget.IsInAttackedServer || server.Id == context.Features.Board.Run?.AttackedServer)
                        .SelectMany(server => server.Ice.Concat(server.Root)).OfType<AgentCard>()
                        .Where(card => MatchesTarget(context, card, chosenTarget) && (chosenTarget.Filter.Rezzed is not { } rezzed || (card.Rezzed == true) == rezzed)
                            && (chosenTarget.Filter.Subtype is not { } subtype || context.Definition(card)?.Printed.Subtypes
                                .Contains(subtype, StringComparer.OrdinalIgnoreCase) == true))
                        .Select(card => context.CardValue(card, installed: true)).DefaultIfEmpty(0);
                    total -= chosenTarget.Selector is null || chosenTarget.IsInAttackedServer ? own.Min() : own.Max();
                    break;
                case BehaviorInstructionKind.TrashCard when self
                    && instruction.Target is { Kind: BehaviorTargetKind.ChosenCard }:
                    // v7: a trash effect is worth its best legal Runner target (Retribution).
                    total += context.Features.Board.RunnerRig.Where(card => MatchesTarget(context, card, chosenTarget))
                        .Select(card => context.CardValue(card, installed: true)).DefaultIfEmpty(0).Max();
                    break;
                case BehaviorInstructionKind.Choose when instruction.Choice is { Options.Count: 0 }:
                    break; // A card-target choice; the following instructions value the best target.
                case BehaviorInstructionKind.Choose when instruction.Choice is { Options.Count: > 0 } runnerChoice
                    && ChooserSide(runnerChoice.Chooser, controller) != context.Features.Side:
                    // v7: the opponent picks the option least good for us, and the
                    // credits it pays are ours to value (Public Trail: a tag or 8 credits).
                    total += runnerChoice.Options.Min(option => Effects(context, option.Instructions, source, controller)
                        + (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * 100);
                    break;
                case BehaviorInstructionKind.Choose:
                    // v30: only the options the Runner's tags allow (IP Enforcement's X, PR #97's review).
                    if (instruction.Choice is { Options.Count: > 0 } choice
                        && choice.Options.Where(option => option.Condition is not { Kind: BehaviorConditionKind.RunnerTagsAtLeast } tagged
                            || context.Opponent.Tags >= (tagged.Value ?? 1)).ToArray() is { Length: > 0 } offered)
                        total += offered.Max(option => Effects(context, option.Instructions, source, controller)
                            - (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * context.CreditValue);
                    else total += 100;
                    break;
                case BehaviorInstructionKind.Branch:
                    if (instruction.Branch is { } branch)
                    {
                        bool condition = branch.Condition.Kind switch
                        {
                            BehaviorConditionKind.Tagged => context.Features.Board.Players.Runner.Tags > 0,
                            BehaviorConditionKind.NotTagged => context.Features.Board.Players.Runner.Tags == 0,
                            BehaviorConditionKind.SuccessfulRunThisTurn => context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun == true,
                            _ => true,
                        };
                        total += Effects(context, condition ? branch.WhenTrue : branch.WhenFalse, source, controller);
                    }
                    break;
                // v29 (bot loop 2): ending a run the ICE ahead stops anyway gains little; LEO trashed the Brân it had just
                // rezzed against a Runner who couldn't break it.
                case BehaviorInstructionKind.EndRun when RunStopsAnyway(context):
                    total += 50;
                    break;
                case BehaviorInstructionKind.EndRun:
                    AgentServer? attacked = context.Features.Board.Servers.FirstOrDefault(server => server.Id == context.Features.Board.Run?.AttackedServer);
                    total += context.ServerValue(attacked);
                    if (attacked?.Root.Any(card => context.Definition(card)?.Agenda is { } agenda
                        && context.Opponent.AgendaPoints + agenda.AgendaPoints >= 7) == true) total += 5000;
                    break;
                // v28 (bot loop 1): the targets the choice can take, cards installed this turn included unless it says not
                // (PT Untaian's counter on an Urtica Cipher installed that turn was worth nothing and declined).
                case BehaviorInstructionKind.AdvanceCard when chosenTarget is { } advanced:
                    total += AdvanceTargets(context, advanced.Kind != BehaviorTargetKind.InstalledCardNotThisTurn)
                        .Where(target => advanced.Filter?.Rezzed is not { } rezzed || (target.Rezzed == true) == rezzed)
                        .Select(target => AdvanceTargetValue(context, target, amount, context.Self.Clicks - 1)).DefaultIfEmpty(0).Max();
                    break;
                case BehaviorInstructionKind.AdvanceCard:
                    // Playing the card spends a click before the counters land.
                    total += AdvanceTargets(context).Select(target => AdvanceTargetValue(context, target, amount, context.Self.Clicks - 1))
                        .DefaultIfEmpty(0).Max();
                    break;
                case BehaviorInstructionKind.RezIgnoringCosts: total += 300; break;
                case BehaviorInstructionKind.Search: total += 180; break;
                case BehaviorInstructionKind.ShuffleIntoDeck when self
                    && instruction.SourceZone == BehaviorZone.Archives:
                    total += ArchivesShuffleValue(context, chosenCount, source);
                    break;
                case BehaviorInstructionKind.TrashSelf: total -= context.CardValue(source, installed: true); break;
            }
        }
        return total;
    }

    /// <summary>
    /// Whether the Corp could still punish a tag this turn after spending a click on landing it: a click
    /// and two credits left to trash a Runner resource, or a punishing card in hand it can afford.
    /// </summary>
    private static bool CanPunishTagThisTurn(HeuristicContext context)
    {
        if (context.Features.Board.Turn.ActiveSide != PlayerSide.Corp || context.Self.Clicks < 2) return false;
        bool resource = context.Features.Board.RunnerRig.Any(card => context.Definition(card)?.Printed.CardClass == RuntimeCardClass.Resource)
            && context.Self.Credits >= 2 + 1;
        bool punisher = context.Features.Hand.Any(card => context.Definition(card)?.Behavior is { } behavior
            && HeuristicContext.Flatten(behavior.Instructions.Concat(behavior.Triggers.SelectMany(trigger => trigger.Instructions)))
                .Any(instruction => instruction.Kind == BehaviorInstructionKind.Branch && instruction.Branch?.Condition.Kind == BehaviorConditionKind.Tagged)
            && (context.Definition(card)!.Printed.PlayCost ?? 0) <= context.Self.Credits);
        return resource || punisher;
    }

    /// <summary>Which side makes a behavior choice.</summary>
    private static PlayerSide ChooserSide(BehaviorChooser chooser, PlayerSide controller) => chooser switch
    {
        BehaviorChooser.Corp => PlayerSide.Corp,
        BehaviorChooser.Runner => PlayerSide.Runner,
        BehaviorChooser.Opponent => controller == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp,
        _ => controller,
    };

    /// <summary>Whether an installed Runner card fits a behavior target's card filter.</summary>
    private static bool MatchesTarget(HeuristicContext context, AgentCard card, BehaviorTarget? target)
    {
        if (target?.Filter is not { } filter) return true;
        RuntimeCardClass? cardClass = context.Definition(card)?.Printed.CardClass;
        return filter.CardType switch
        {
            BehaviorCardType.Any => true,
            BehaviorCardType.Hardware => cardClass == RuntimeCardClass.Hardware,
            BehaviorCardType.Resource => cardClass == RuntimeCardClass.Resource,
            BehaviorCardType.Icebreaker => context.Definition(card)?.Icebreaker is not null,
            BehaviorCardType.ProgramOrHardware => cardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware,
            BehaviorCardType.ResourceOrHardware => cardClass is RuntimeCardClass.Resource or RuntimeCardClass.Hardware,
            _ => cardClass == RuntimeCardClass.Program,
        };
    }

    /// <summary>Installed Corp cards an effect can advance: installed before this turn.</summary>
    private static IEnumerable<AgentCard> AdvanceTargets(HeuristicContext context, bool installedThisTurn = false)
    {
        ImmutableArray<string> thisTurn = installedThisTurn ? [] : context.Features.Board.TurnFacts?.Corp.ThisTurn?.InstalledCardIds ?? [];
        return context.Features.Board.Servers.SelectMany(server => server.Root.Concat(server.Ice)).OfType<AgentCard>()
            .Select(card => context.Card(new CardInstanceId(card.Id)) ?? card)
            .Where(card => !thisTurn.Contains(card.Id));
    }

    /// <summary>
    /// What <paramref name="amount"/> counters on <paramref name="card"/> are worth: most when they
    /// complete an agenda or let the remaining clicks finish it, some as progress, nothing on
    /// cards that don't use counters.
    /// </summary>
    private static int AdvanceTargetValue(HeuristicContext context, AgentCard? card, int amount, int clicksAfter)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition is null || card is null) return 0;
        if (definition.Agenda is not null)
        {
            int remaining = definition.Printed.AdvancementRequirement - (card.AdvancementCounters ?? 0) - amount;
            int points = definition.Printed.AgendaPoints;
            if (remaining <= 0) return 700 + points * 60;
            if (remaining <= Math.Min(clicksAfter, context.Self.Credits)) return 450 + points * 50;
            return amount * 110;
        }
        bool scalesWithCounters = definition.Behavior?.Triggers.SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
            .Any(instruction => instruction.DamageAmountOperand?.Kind == BehaviorDamageAmountKind.SourceAdvancementCounters) == true;
        return scalesWithCounters ? amount * 90 : 0;
    }
}
