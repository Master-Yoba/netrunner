using System.Text.RegularExpressions;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Player-facing labels for issued actions. Names come only from the current viewer's presentation.</summary>
public static class ActionLabelPresenter
{
    public static string Label(LegalActionViewModel action, DecisionKind? decision,
        Func<string, string> cardName, Func<string, string> serverName,
        IReadOnlyList<LegalActionViewModel>? peers = null)
    {
        string Card(CardInstanceId card) => cardName(card.Value);
        string Cards(IEnumerable<CardInstanceId> cards) => string.Join(", ", cards.Select(Card));
        string Source() => action.Source is { } source ? cardName(source.CardId)
            + (source.EffectLabel is { } effect ? $": {effect}" : source.RuleKey is { } key ? $": {Words(key)}" : "") : "triggered ability";
        string Destination(InstallDestinationKind kind, ServerId? server, bool newRemote) =>
            kind == InstallDestinationKind.RunnerRig ? "Rig"
            : (newRemote ? "New remote" : server is { } target ? serverName(target.Value) : "Server")
                + (kind == InstallDestinationKind.ServerIce ? " · ICE" : " · root");

        switch (action.Parameters)
        {
            case PlayCardParameters play:
                return $"Play {Card(play.CardId)}"
                    + (play.TargetServerId is { } target ? $" → Run {serverName(target.Value)}" : "")
                    + $" · {Count(play.CreditCost, "credit")}"
                    + (play.TrashCostCardId is { } trashed ? $" · Trash {Card(trashed)}" : "");
            case InstallCardParameters install:
                string label = $"{(install.FinalizeSelection ? "Install" : "Choose install")} {Card(install.CardId)} → "
                    + Destination(install.DestinationKind, install.ServerId, install.CreateNewRemote)
                    + $" · {Count(install.CreditCost, "credit")}";
                if (install.HostCardId is { } host) label += $" · Host: {Card(host)}";
                if (install.SelectedReplacementCardIds.Count > 0)
                    label += $" · {(install.FinalizeSelection ? "Trash" : "Select to trash")}: {Cards(install.SelectedReplacementCardIds)}";
                else if (install.FinalizeSelection && install.CandidateReplacementCardIds.Count > 0)
                    label += " · Keep installed cards";
                return label;
            case BehaviorInstallChoiceParameters { Choice: null }:
                return "Install nothing";
            case BehaviorInstallChoiceParameters { Choice: { } chosen }:
                return $"Install {Card(chosen.CardId)} → "
                    + Destination(chosen.DestinationKind, chosen.ServerId, chosen.CreateNewRemote)
                    + $" · {Count(chosen.CreditCost, "credit")}";
            case CardChoiceParameters choice:
                string verb = decision == DecisionKind.TurnDiscard ? "Discard"
                    : action.Kind == ActionKind.TrashProgram || decision == DecisionKind.MemoryCleanup ? "Trash to free memory:"
                    : action.Kind == ActionKind.StealAgenda ? "Steal" : action.Kind == ActionKind.AdvanceAgenda ? "Advance"
                    : action.Kind == ActionKind.ScoreAgenda ? "Score" : "Choose";
                return $"{verb} {Card(choice.CardId)}";
            case NumberChoiceParameters number:
                return decision == DecisionKind.Setup ? number.Value == 0 ? "Keep hand" : "Mulligan" : $"Choose {number.Value}";
            case RunServerParameters run:
                return $"Run {serverName(run.ServerId.Value)} · 1 click";
            case RezCardParameters rez:
                return $"Rez {Card(rez.CardId)} · {Count(rez.CreditCost, "credit")}";
            case AdvanceCardParameters advance:
                return $"Advance {Card(advance.CardId)} · 1 click + {Count(advance.CreditCost, "credit")}";
            case ScoreAgendaParameters score:
                return $"Score {Card(score.CardId)} · {Count(score.AgendaPoints, "agenda point")}";
            case RemoveTagParameters tag:
                return $"Remove 1 tag · {Count(tag.ClickCost, "click")} + {Count(tag.CreditCost, "credit")}";
            case TrashTaggedResourceParameters trash:
                return $"Trash {Card(trash.CardId)} · {Count(trash.ClickCost, "click")} + {Count(trash.CreditCost, "credit")}";
            case PurgeVirusCountersParameters purge:
                return $"Purge all virus counters · {Count(purge.ClickCost, "click")}";
            case BreachCandidateParameters breach:
                return breach.CardId is { } accessed ? $"Access {Card(accessed)}"
                    : breach.Source == BreachHiddenCandidateSource.CorpHand ? "Access a random card from HQ" : "Access the top card of R&D";
            case AbilityActionParameters ability:
                string effect = action.EffectLabel ?? Words(ability.AbilityKey);
                string costs = string.Join(" + ", ability.Costs.Select((cost, index) =>
                    Cost(cost, Card(ability.SourceCardId), ability.AdditionalCostSelectionsOrEmpty
                        .FirstOrDefault(selection => selection.CostIndex == index)?.CardIds, cardName)));
                return $"{Card(ability.SourceCardId)}: {effect}"
                    + (ability.TargetCardIds.Count > 0 ? $" · Target: {Cards(ability.TargetCardIds)}" : "")
                    + (costs.Length > 0 ? $" · Cost: {costs}" : "")
                    + (ability.Usage.Limit == BehaviorAbilityUsageLimit.OncePerTurn ? " · Once per turn" : "");
            case BehaviorChoiceParameters behavior:
                string selection = behavior.CardIds.Count > 0 ? Cards(behavior.CardIds) : "no cards";
                if (!behavior.FinalizeSelection) return $"Add to selection: {selection}";
                if (behavior.OptionKeys.Count == 0)
                {
                    bool staged = peers?.Any(peer => peer.Parameters is BehaviorChoiceParameters candidate
                        && candidate.ChoiceKey == behavior.ChoiceKey && !candidate.FinalizeSelection) == true;
                    if (behavior.CardIds.Count == 0)
                        return !staged && action.BehaviorChoice?.Mode == BehaviorChoiceMode.Optional
                            ? "Skip optional effect" : "Continue without selecting cards";
                    return staged ? $"Finish selection: {selection}" : $"Choose {selection}";
                }
                BehaviorChoiceOptionViewModel? metadata = action.BehaviorChoice;
                string option = !string.IsNullOrWhiteSpace(metadata?.EffectLabel) ? metadata.EffectLabel
                    : string.Join(" → ", behavior.OptionKeys.Select(Words));
                if (metadata?.Costs.Count > 0)
                    option += " · Cost: " + string.Join(" + ", metadata.Costs.Select(cost =>
                        cost.AdditionalKind == BehaviorAdditionalCostKind.TrashCards
                            ? $"Trash {selection}" : cost.Summary));
                else if (behavior.CardIds.Count > 0) option += $" · Cards: {selection}";
                return option;
            case TriggerChoiceParameters:
                return $"{(action.Kind == ActionKind.DeclineTrigger ? "Skip" : "Use")} {Source()}";
            case TriggerOrderParameters:
                return $"Resolve next: {Source()}";
            case PreventionChoiceParameters prevention:
                return action.Kind == ActionKind.DeclinePrevention ? $"Decline prevention · {Source()}"
                    : $"Prevent {prevention.Amount} · {Source()}";
            case ReplacementChoiceParameters:
                return $"{(action.Kind == ActionKind.DeclineReplacement ? "Decline" : "Use")} replacement · {Source()}";
            case RunIceParameters ice when action.CostQuote is { } quote:
                return $"Rez {Card(ice.IceId)} · {Count(quote.EffectiveCost, "credit")}";
            case RunBreakParameters breaker when !action.Description.Contains(breaker.BreakerId.Value, StringComparison.Ordinal):
                return $"{action.Description} · With {Card(breaker.BreakerId)}";
            case RunBreakerParameters pump:
                return action.Description.Replace($"Pump {pump.BreakerId.Value}", $"Boost {Card(pump.BreakerId)} strength", StringComparison.Ordinal);
            case RunBioroidAbilityParameters bioroid:
                return $"Break subroutine {bioroid.SubroutineOrdinal + 1} on {Card(bioroid.IceId)} · "
                    + Cost(bioroid.Cost, Card(bioroid.IceId), null, cardName);
            case IceSubroutineChoiceParameters ice:
                return ice.Choice switch
                {
                    IceSubroutineChoiceKind.TrashInstalledRunnerCard => $"Trash {Card(ice.CardId!.Value)}",
                    IceSubroutineChoiceKind.InstallCard => $"Install {Card(ice.CardId!.Value)} → "
                        + Destination(ice.DestinationKind!.Value, ice.ServerId, ice.CreateNewRemote)
                        + $" · {Count(ice.CreditCost, "credit")}",
                    IceSubroutineChoiceKind.PayCredits => $"Pay {Count(ice.CreditCost, "credit")} "
                        + $"({ice.HostedCredits} hosted + {ice.CreditCost - ice.HostedCredits} personal)",
                    IceSubroutineChoiceKind.TakeTag => "Take 1 tag",
                    IceSubroutineChoiceKind.EndRun => "End the run",
                    IceSubroutineChoiceKind.JackOut => "Jack out",
                    IceSubroutineChoiceKind.ContinueRun => "Continue the run",
                    _ => "Decline this effect",
                };
        }
        // Existing breaker/access descriptions retain exact payment splits and
        // subroutine quantities. Do not replace them with a generic action name.
        return action.Kind switch
        {
            ActionKind.GainCredit => "Gain 1 credit · 1 click",
            ActionKind.DrawCard => "Draw a card · 1 click",
            ActionKind.PassPriority => "Continue",
            ActionKind.NoAccessAction => "Continue access",
            ActionKind.EndTurn => "End turn",
            _ => action.Description,
        };
    }

    /// <summary>Names a source ability and its full cost before physical targets are selected.</summary>
    public static string AbilityChoiceLabel(LegalActionViewModel action, Func<string, string> cardName)
    {
        if (action.Parameters is not AbilityActionParameters ability) return action.Description;
        string costs = string.Join(" + ", ability.Costs.Select(cost => Cost(cost, cardName(ability.SourceCardId.Value), null, cardName)));
        return (action.EffectLabel ?? Words(ability.AbilityKey))
            + (costs.Length > 0 ? " · Cost: " + costs : "")
            + (ability.Usage.Limit == BehaviorAbilityUsageLimit.OncePerTurn ? " · Once per turn" : "");
    }

    private static string Cost(BehaviorCost cost, string source, IReadOnlyList<CardInstanceId>? selected,
        Func<string, string> cardName) => cost.Kind switch
        {
            BehaviorCostKind.Credits => Count(cost.Amount, "credit"),
            BehaviorCostKind.Clicks => Count(cost.Amount, "click"),
            BehaviorCostKind.LoseClick => $"Lose {Count(cost.Amount, "click")}",
            BehaviorCostKind.Counter => $"Spend {Count(cost.Amount, cost.CounterName + " counter")}",
            BehaviorCostKind.HostedCredits => Count(cost.Amount, "hosted credit"),
            BehaviorCostKind.TrashSelf => $"Trash {source}",
            BehaviorCostKind.RemoveSelfFromGame => $"Remove {source} from game",
            BehaviorCostKind.Tags => $"Remove {Count(cost.Amount, "tag")}",
            BehaviorCostKind.Additional when cost.AdditionalKind == BehaviorAdditionalCostKind.TrashCards =>
                selected is { Count: > 0 } ? "Trash " + string.Join(", ", selected.Select(card => cardName(card.Value)))
                    : $"Trash {Count(cost.Amount, "card")}",
            _ => Words(cost.AdditionalKey ?? "additional cost"),
        };

    internal static string Words(string key)
    {
        string words = key.Replace('-', ' ').Replace('_', ' ');
        words = Regex.Replace(words, @"\brnd\b", "R&D", RegexOptions.IgnoreCase);
        words = Regex.Replace(words, @"\bhq\b", "HQ", RegexOptions.IgnoreCase);
        return words.Length == 0 ? "Choose effect" : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Count(int amount, string noun) => $"{amount} {noun}{(amount == 1 ? "" : "s")}";
}
