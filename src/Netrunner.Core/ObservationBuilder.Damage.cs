namespace Netrunner.Core;

public static partial class ObservationBuilder
{
    private static void AddDamagePresentation(GameState state, PlayerSide viewer,
        IReadOnlySet<CardInstanceId> accessVisible, IBehaviorCatalog catalog, Dictionary<string, object?> publicState)
    {
        DamageEffect? pending = state.ResolutionStack.OfType<EffectCheckpointFrame>().LastOrDefault() is
        { CurrentEffect: DamageEffect effect, Stage: EffectCheckpointStage.InterruptChoice or EffectCheckpointStage.Resolve } ? effect : null;
        CardInstanceId? damageSource = DamagePresentationProjection.Source(state.ResolutionStack);
        if (pending is not null)
        {
            bool sourceVisible = damageSource is { } sourceId
                && state.Cards.FirstOrDefault(value => value.Id == sourceId) is { } source
                && IsVisibleTo(source, state.GetZone(source.ZoneId), viewer, accessVisible);
            publicState["pendingDamage"] = new Dictionary<string, object?>
            {
                ["kind"] = pending.Kind,
                ["amount"] = pending.Amount,
                ["targetId"] = pending.Target.Value,
                ["sourceCardId"] = sourceVisible ? damageSource?.Value : null,
                ["flatlineRisk"] = pending.Amount > ZoneCount(state, ZoneKind.RunnerGrip),
            };
        }

        var quotes = new List<object>();
        foreach (CardInstanceState source in state.Cards)
        {
            ZoneState zone = state.GetZone(source.ZoneId);
            if (!IsVisibleTo(source, zone, viewer, accessVisible)
                || zone.Kind is ZoneKind.CorpDiscard or ZoneKind.RunnerHeap or ZoneKind.CorpDeck or ZoneKind.RunnerStack
                || !catalog.TryGet(source.DefinitionId, out CardBehaviorDefinition? definition)) continue;
            foreach (BehaviorInstruction instruction in definition.Instructions.Concat(definition.Triggers.SelectMany(value => value.Instructions)))
            {
                if (instruction.DamageAmountOperand is not { } operand) continue;
                int quantity = operand.Kind switch
                {
                    BehaviorDamageAmountKind.SourceAdvancementCounters => source.AdvancementCounters,
                    BehaviorDamageAmountKind.ScoredAgendaPrintedPointsThisTurn =>
                        state.Turn.History?.ThisTurn(state.Turn.ActiveSide)?.AgendaTurnChanges
                            .Where(change => change.Kind == AgendaTurnChangeKind.Score && change.BeneficiarySide == PlayerSide.Corp)
                            .Sum(change => change.Points) ?? 0,
                    _ => 0,
                };
                string reason = operand.Kind == BehaviorDamageAmountKind.SourceAdvancementCounters
                    ? "hosted-advancement-counters" : "printed-agenda-points-scored-this-turn";
                quotes.Add(new Dictionary<string, object?>
                {
                    ["key"] = source.Id.Value,
                    ["valueKind"] = BehaviorValueKind.Damage,
                    ["baseValue"] = operand.BaseAmount,
                    ["effectiveValue"] = checked(operand.BaseAmount + quantity),
                    ["terms"] = new object[] { new Dictionary<string, object?>
                    {
                        ["source"] = BehaviorValueSourceKind.Variable,
                        ["operation"] = BehaviorValueOperation.Add,
                        ["delta"] = quantity,
                        ["reasonKey"] = reason,
                        ["sourceDefinitionId"] = source.DefinitionId.Value,
                        ["sourceCardId"] = source.Id.Value,
                        ["quantity"] = quantity,
                        ["unitDelta"] = 1,
                        ["variableKey"] = reason,
                    } },
                });
            }
        }
        if (quotes.Count > 0) publicState["conditionalValues"] = quotes.ToArray();
    }
}
