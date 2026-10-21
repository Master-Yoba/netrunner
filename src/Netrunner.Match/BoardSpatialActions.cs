using Netrunner.Core;

namespace Netrunner.Match;

public enum BoardTargetKind { Card, ServerRoot, ServerIce, Server, NewRemote, Subroutine }

/// <summary>Role separates effect targets, ordered positions, and each additional card cost.</summary>
public sealed record BoardTarget(BoardTargetKind Kind, string Id, string Role = "Target")
{
    public BehaviorAdditionalCostKind? PaymentKind { get; init; }
}
public sealed record BoardSpatialAction(
    LegalActionViewModel Action, string? SourceId, string Family,
    IReadOnlyList<BoardTarget> Targets, bool Automatic);

/// <summary>Spatial vocabulary derived exclusively from issued typed actions and viewer-safe handles.</summary>
public static class BoardSpatialActions
{
    public static IReadOnlyList<BoardSpatialAction> Project(MatchViewModel view)
    {
        if (view.Decision is not { } decision || decision.OwnerSide != view.Viewer
            || !Enum.IsDefined(decision.Kind) || !Enum.IsDefined(decision.OwnerSide)
            || view.Outcome.Kind != GameOutcomeKind.Ongoing) return [];
        HashSet<string> cards = Cards(view).Select(card => card.Id)
            .Concat(view.CardPositions.Select(position => position.CardId))
            .Concat(view.Servers.SelectMany(server => server.IcePositions).Select(position => position.CardId))
            .ToHashSet(StringComparer.Ordinal);
        var result = new List<BoardSpatialAction>();
        foreach (LegalActionViewModel action in RunPaymentPreference.Apply(view.LegalActions))
        {
            string? source = null;
            string family = action.Kind.ToString();
            bool automatic = false;
            var targets = new List<BoardTarget>();
            void Card(string id, string role = "Target", BehaviorAdditionalCostKind? paymentKind = null) =>
                targets.Add(new(BoardTargetKind.Card, id, role) { PaymentKind = paymentKind });
            switch (action.Kind, action.Parameters)
            {
                case (ActionKind.RezCard, RezCardParameters p): source = p.CardId.Value; break;
                case (ActionKind.RezIce, RunIceParameters p): source = p.IceId.Value; break;
                case (ActionKind.AdvanceCard, AdvanceCardParameters p): source = p.CardId.Value; break;
                case (ActionKind.ScoreAgenda, ScoreAgendaParameters p): source = p.CardId.Value; break;
                case (ActionKind.TrashTaggedResource, TrashTaggedResourceParameters p): source = p.CardId.Value; break;
                case (ActionKind.UseAbility, AbilityActionParameters p):
                    source = p.SourceCardId.Value;
                    family += ":" + p.AbilityKey;
                    for (int i = 0; i < p.TargetCardIds.Count; i++) Card(p.TargetCardIds[i].Value, $"Target {i + 1}");
                    foreach (AbilityAdditionalCostSelection cost in p.AdditionalCostSelectionsOrEmpty)
                        foreach (CardInstanceId id in cost.CardIds)
                            Card(id.Value, $"Pay card cost {cost.CostIndex + 1}",
                                cost.CostIndex >= 0 && cost.CostIndex < p.Costs.Count ? p.Costs[cost.CostIndex].AdditionalKind : null);
                    break;
                case (ActionKind.UseBioroidAbility, RunBioroidAbilityParameters p):
                    source = p.IceId.Value;
                    family += ":" + p.AbilityKey;
                    targets.Add(new(BoardTargetKind.Subroutine, p.SubroutineOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture), "Subroutine"));
                    // VP-BIOROID-MULTI-BREAK: "break up to 2" names a second subroutine (Ansel 2.0).
                    if (p.SecondSubroutineOrdinal is { } second)
                        targets.Add(new(BoardTargetKind.Subroutine, second.ToString(System.Globalization.CultureInfo.InvariantCulture), "Second subroutine"));
                    break;
                case (ActionKind.ChooseCard or ActionKind.TrashProgram, CardChoiceParameters p) when decision.Kind != DecisionKind.TurnDiscard:
                    automatic = true; Card(p.CardId.Value); break;
                case (ActionKind.ChooseBehavior, BehaviorChoiceParameters p) when p.CardIds.Count > 0 || view.CardSelection?.Candidates.Count > 0:
                    automatic = true;
                    family += ":" + p.ChoiceKey + ":" + string.Join("/", p.OptionKeys);
                    bool trashPayment = action.BehaviorChoice?.Costs.Any(cost => cost.AdditionalKind == BehaviorAdditionalCostKind.TrashCards) == true;
                    for (int i = 0; i < p.CardIds.Count; i++)
                        Card(p.CardIds[i].Value, view.CardSelection?.Mode == BehaviorChoiceMode.OrderSelection ? $"Position {i + 1}"
                            : trashPayment ? "Pay card cost" : "Target", trashPayment ? BehaviorAdditionalCostKind.TrashCards : null);
                    break;
                case (ActionKind.ResolveIceSubroutine, IceSubroutineChoiceParameters { CardId: { } id } p):
                    automatic = true; family += ":" + p.Choice; Card(id.Value, p.Choice == IceSubroutineChoiceKind.InstallCard ? "Install card" : "Trash card");
                    if (p.Choice == IceSubroutineChoiceKind.InstallCard)
                        targets.Add(p.CreateNewRemote ? new(BoardTargetKind.NewRemote, "new", "Destination")
                            : new(p.DestinationKind == InstallDestinationKind.ServerIce ? BoardTargetKind.ServerIce : BoardTargetKind.ServerRoot, p.ServerId!.Value.Value, "Destination"));
                    break;
                case (ActionKind.ChooseBreachCandidate, BreachCandidateParameters p):
                    automatic = true;
                    if (p.CardId is { } candidate) Card(candidate.Value, "Access card");
                    else if (view.Servers.FirstOrDefault(server => server.Kind == (p.Source == BreachHiddenCandidateSource.CorpHand ? ServerKind.Headquarters : ServerKind.RAndD)) is { } server)
                        targets.Add(new(BoardTargetKind.Server, server.Id, "Access source"));
                    break;
                default: continue;
            }
            if (action.Kind == ActionKind.ChooseBreachCandidate && targets.Count == 0) continue;
            // Do not offer a partly exposed target set. The fallback remains available.
            if (source is not null && !cards.Contains(source)
                || targets.Any(target => target.Kind == BoardTargetKind.Card && !cards.Contains(target.Id))) continue;
            result.Add(new(action, source, family, targets.AsReadOnly(), automatic));
        }
        return result.AsReadOnly();
    }

    public static IReadOnlyList<CardViewModel> Cards(MatchViewModel view) =>
        view.CorpHand.VisibleCards.Concat(view.RunnerHand.VisibleCards)
            .Concat(view.CorpDiscard).Concat(view.RunnerHeap).Concat(view.RunnerRig)
            .Concat(view.CorpScore).Concat(view.RunnerScore).Concat(view.ChoiceCards)
            .Concat(new[] { view.CorpIdentity, view.RunnerIdentity }.OfType<CardViewModel>())
            .Concat(view.Servers.SelectMany(server => server.Ice.Concat(server.Root)).OfType<CardViewModel>())
            .Concat(view.Run is { } run ? new[] { run.CurrentIce, run.InitiatingSource, run.Encounter?.Ice }
                .Concat(run.Access.Candidates).Concat(run.Access.Chosen).Concat(run.Access.Accessed).OfType<CardViewModel>() : [])
            .GroupBy(card => card.Id, StringComparer.Ordinal).Select(group => group.Last()).ToArray();
}

/// <summary>Pure local selection. Only CompleteActions may be submitted; candidate clicks never pay costs.</summary>
public sealed class BoardSpatialSelection
{
    private readonly IReadOnlyList<BoardSpatialAction> _actions;
    private readonly List<BoardTarget> _selected = [];
    public BoardSpatialSelection(IReadOnlyList<BoardSpatialAction> actions) => _actions = actions;
    public IReadOnlyList<BoardTarget> Selected => _selected.AsReadOnly();
    public IReadOnlyList<BoardSpatialAction> Remaining => _actions.Where(action => _selected.All(action.Targets.Contains)).ToArray();
    public IReadOnlyList<BoardSpatialAction> CompleteActions => Remaining.Where(action => action.Targets.Count == _selected.Count).ToArray();
    public IReadOnlyList<BoardTarget> Candidates => Remaining.SelectMany(action =>
    {
        BoardTarget[] remaining = action.Targets.Except(_selected).ToArray();
        string? role = remaining.FirstOrDefault()?.Role;
        return remaining.Where(target => target.Role == role);
    }).Distinct().ToArray();
    public bool Toggle(BoardTarget target)
    {
        int index = _selected.IndexOf(target);
        if (index >= 0) { _selected.RemoveRange(index, _selected.Count - index); return true; }
        if (!Candidates.Contains(target)) return false;
        _selected.Add(target);
        return true;
    }
}
