namespace Netrunner.Core;

/// <summary>The cost rule a behavior zone install pays by.</summary>
internal sealed record BehaviorZoneInstallRule(BehaviorInstallCostRule CostRule, int Discount)
{
    public int Apply(int cost) => CostRule == BehaviorInstallCostRule.IgnoreAllCosts ? 0 : Math.Max(0, cost - Discount);
}

/// <summary>
/// EL-BEHAVIOR-INSTALL: installs a behavior chooses from a zone, card and
/// destination in one decision, through the ordinary install pipeline.
/// Covers HQ, Archives and R&amp;D for the Corp (and the Runner's score area),
/// and the grip, heap and stack for the Runner.
/// </summary>
public static partial class GameEngine
{
    private static readonly BehaviorZone[] ZoneInstallSources =
    [
        BehaviorZone.Headquarters,
        BehaviorZone.Archives,
        BehaviorZone.Deck,
        BehaviorZone.ScoreArea,
        BehaviorZone.Grip,
        BehaviorZone.Heap,
        BehaviorZone.Stack,
        BehaviorZone.HostedFaceUp,
    ];

    private static void PushBehaviorZoneInstall(
        WorkingState working,
        BehaviorResolutionFrame frame,
        BehaviorInstruction instruction)
    {
        PlayerState owner = GetPlayer(working, frame.Controller);
        var rule = new BehaviorZoneInstallRule(instruction.CostRule ?? BehaviorInstallCostRule.Normal, instruction.Amount);
        List<BehaviorInstallChoice> choices = MaterializeBehaviorZoneInstallChoices(
            working, owner.Side, instruction, rule, frame.SourceCardId);
        if (choices.Count == 0)
        {
            // Nothing legal to install: the instruction does nothing.
            AdvanceBehaviorCursor(working, frame);
            return;
        }

        working.ResolutionStack.Add(new BehaviorInstallChoiceFrame(
            1,
            ResolutionBehaviorIds.BehaviorInstallChoice,
            owner.Id,
            working.Allocator.NextBehaviorFrameId(),
            BehaviorFrameIdentity.Compute(frame),
            rule.CostRule,
            rule.Discount,
            instruction.Optional,
            BehaviorInstallChoiceStage.AwaitingChoice,
            choices));
    }

    private static ZoneKind? ZoneInstallSourceKind(PlayerSide side, BehaviorZone zone) => (side, zone) switch
    {
        (PlayerSide.Corp, BehaviorZone.Headquarters) => ZoneKind.CorpHand,
        (PlayerSide.Corp, BehaviorZone.Archives) => ZoneKind.CorpDiscard,
        (PlayerSide.Corp, BehaviorZone.Deck) => ZoneKind.CorpDeck,
        (PlayerSide.Corp, BehaviorZone.ScoreArea) => ZoneKind.RunnerScore,
        (PlayerSide.Runner, BehaviorZone.Grip) => ZoneKind.RunnerGrip,
        (PlayerSide.Runner, BehaviorZone.Heap) => ZoneKind.RunnerHeap,
        (PlayerSide.Runner, BehaviorZone.Stack) => ZoneKind.RunnerStack,
        (PlayerSide.Runner, BehaviorZone.HostedFaceUp) => ZoneKind.RunnerHostedFaceUp,
        _ => null,
    };

    private static List<BehaviorInstallChoice> MaterializeBehaviorZoneInstallChoices(
        WorkingState working,
        PlayerSide side,
        BehaviorInstruction instruction,
        BehaviorZoneInstallRule rule,
        CardInstanceId sourceCardId)
    {
        var choices = new List<BehaviorInstallChoice>();
        if (working.RuntimeCardCatalog is null
            || instruction.SourceZone is not { } zone
            || ZoneInstallSourceKind(side, zone) is not { } sourceKind)
        {
            return choices;
        }

        ZoneState? source = working.Zones.FirstOrDefault(candidate => candidate.Kind == sourceKind && candidate.ServerId is null);
        if (source is null)
        {
            return choices;
        }

        IReadOnlyList<CardInstanceId>? discarded = instruction.Target!.IsHandSizeDiscards
            ? working.Turn.History?.ThisTurn(working.Turn.ActiveSide)?.HandSizeDiscardIds ?? []
            : null;
        foreach (CardInstanceId cardId in source.CardIds.Take(instruction.Target!.TopCount ?? int.MaxValue)
            .Where(cardId => discarded is null || discarded.Contains(cardId))
            .Where(cardId => !instruction.Target!.IsHostedOnSource || GetCard(working, cardId).HostCardId == sourceCardId))
        {
            CardInstanceState card = GetCard(working, cardId);
            if (card.Id == sourceCardId
                || card.OwnerSide != side
                || !working.RuntimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                || !IsBasicInstallClass(side, spec.CardClass)
                || !BehaviorCardMatches(working, card.Id, instruction.Target!.Filter))
            {
                continue;
            }

            IEnumerable<(InstallDestinationKind DestinationKind, ServerId? ServerId, bool CreateNewRemote)> destinations =
                side == PlayerSide.Runner
                    ? [(InstallDestinationKind.RunnerRig, null, false)]
                    : InstallDestinations(working, side, spec);
            foreach ((InstallDestinationKind destinationKind, ServerId? serverId, bool createNewRemote) in destinations)
            {
                if (!ZoneInstallDestinationAllowed(working, instruction.DestinationZone, destinationKind, serverId, createNewRemote))
                {
                    continue;
                }

                InstallPlan? plan = BuildInstallPlan(
                    working,
                    side,
                    card.Id,
                    destinationKind,
                    serverId,
                    createNewRemote,
                    InstallInitiationKind.BehaviorZoneInstall,
                    zoneRule: rule);
                if (plan is not null)
                {
                    choices.Add(new BehaviorInstallChoice(card.Id, destinationKind, serverId, createNewRemote, plan.InstallCost));
                }
            }
        }

        return choices;
    }

    private static bool ZoneInstallDestinationAllowed(
        WorkingState working,
        BehaviorZone? narrowed,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote) => narrowed switch
        {
            null => true,
            BehaviorZone.Rig => destinationKind == InstallDestinationKind.RunnerRig,
            BehaviorZone.ServerIce => destinationKind == InstallDestinationKind.ServerIce,
            // "In the root of a remote server": a new remote or an existing remote's root.
            BehaviorZone.ServerRoot => destinationKind == InstallDestinationKind.ServerRoot
                && (createNewRemote
                    || working.Servers.Any(server => server.Id == serverId && server.Kind == ServerKind.Remote)),
            _ => false,
        };

    private static void StepBehaviorInstallChoice(WorkingState working, BehaviorInstallChoiceFrame frame)
    {
        if (frame.Stage != BehaviorInstallChoiceStage.AwaitingChoice)
        {
            throw new ResolutionFailureException("A behavior install choice cannot resume while its install is pending.");
        }

        var actions = frame.Choices
            .Select(choice => new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.InstallCard,
                $"Install {choice.CardId.Value} ({choice.CreditCost} credits)",
                new BehaviorInstallChoiceParameters(frame.FrameIdentity, choice)))
            .ToList();
        if (frame.Optional)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.InstallCard,
                "Install nothing",
                new BehaviorInstallChoiceParameters(frame.FrameIdentity, null)));
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            frame.Owner,
            DecisionKind.BehaviorInstallChoice,
            "Choose a card to install",
            actions);
    }

    private static void ConsumeBehaviorInstallChoice(WorkingState working, BehaviorInstallChoiceParameters parameters)
    {
        if (working.ResolutionStack.Count < 2
            || working.ResolutionStack[^1] is not BehaviorInstallChoiceFrame frame
            || frame.Stage != BehaviorInstallChoiceStage.AwaitingChoice
            || !string.Equals(frame.FrameIdentity, parameters.FrameIdentity, StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("The behavior install action does not match the pending install choice.");
        }

        if (parameters.Choice is null)
        {
            if (!frame.Optional)
            {
                throw new ResolutionFailureException("This behavior install is not optional.");
            }

            FinishBehaviorZoneInstallChoice(working, frame);
            return;
        }

        BehaviorInstallChoice choice = frame.Choices.SingleOrDefault(candidate => candidate == parameters.Choice)
            ?? throw new ResolutionFailureException("The behavior install choice was not offered.");
        PlayerSide side = GetPlayer(working, frame.Owner).Side;
        var rule = new BehaviorZoneInstallRule(frame.CostRule, frame.Discount);
        InstallPlan plan = BuildInstallPlan(
                working,
                side,
                choice.CardId,
                choice.DestinationKind,
                choice.ServerId,
                choice.CreateNewRemote,
                InstallInitiationKind.BehaviorZoneInstall,
                zoneRule: rule)
            ?? throw new ResolutionFailureException("The chosen behavior install is no longer legal.");
        if (plan.InstallCost != choice.CreditCost)
        {
            throw new ResolutionFailureException("The chosen behavior install cost is stale.");
        }

        working.ResolutionStack[^1] = frame.WithStage(BehaviorInstallChoiceStage.AwaitingInstall);
        working.ResolutionStack.Add(new InstallFrame(
            frame.Owner,
            plan.Card.Id,
            plan.Card.DefinitionId,
            plan.Spec.CardClass,
            plan.Source.Id,
            plan.Source.Kind,
            plan.DestinationKind,
            plan.ServerId,
            plan.CreateNewRemote,
            plan.InstallCost,
            plan.CandidateReplacementCardIds,
            plan.RequiredReplacementCardIds,
            [],
            plan.DestinationZoneId,
            plan.CandidateReplacementDefinitionIds,
            InstallInitiationKind.BehaviorZoneInstall,
            frame.FrameIdentity,
            plan.InstallCostQuote));
    }

    /// <summary>Called by the install pipeline when a zone install commits.</summary>
    private static void CompleteBehaviorZoneInstall(
        WorkingState working,
        InstallFrame install,
        PlayerId installer,
        ServerId? destinationServer)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not BehaviorInstallChoiceFrame choice
            || !string.Equals(install.ParentFrameIdentity, choice.FrameIdentity, StringComparison.Ordinal)
            || choice.Stage != BehaviorInstallChoiceStage.AwaitingInstall)
        {
            throw new ResolutionFailureException("The completed behavior zone install has no matching choice frame.");
        }

        FinishBehaviorZoneInstallChoice(working, choice, install.CardId);
        QueueCardInstalledOccurrence(working, installer, install.CardId, destinationServer);
    }

    private static void FinishBehaviorZoneInstallChoice(
        WorkingState working,
        BehaviorInstallChoiceFrame choice,
        CardInstanceId? installed = null)
    {
        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not BehaviorResolutionFrame parent
            || !string.Equals(choice.ParentFrameIdentity, BehaviorFrameIdentity.Compute(parent), StringComparison.Ordinal))
        {
            throw new ResolutionFailureException("A behavior zone install has no matching behavior parent.");
        }

        // A keyed install binds the installed card for later instructions
        // ("place 1 advancement counter on that agenda", IP Enforcement).
        if (installed is { } cardId
            && GetBehaviorInstructions(working, parent) is { } instructions
            && parent.InstructionCursor < instructions.Count
            && instructions[parent.InstructionCursor] is { Kind: BehaviorInstructionKind.InstallCard, Argument: { } key }
            && StableIdentifier.TryValidate(key))
        {
            parent = parent.With(bindings: SetBehaviorBinding(parent.Bindings, key, [cardId]));
        }

        AdvanceBehaviorCursor(working, parent);
    }

    private static BehaviorZoneInstallRule ZoneInstallRuleFor(WorkingState working, InstallFrame install)
    {
        BehaviorInstallChoiceFrame choice = working.ResolutionStack
            .OfType<BehaviorInstallChoiceFrame>()
            .LastOrDefault(candidate => string.Equals(candidate.FrameIdentity, install.ParentFrameIdentity, StringComparison.Ordinal))
            ?? throw new ResolutionFailureException("A behavior zone install has no declaring choice frame.");
        return new BehaviorZoneInstallRule(choice.CostRule, choice.Discount);
    }

    /// <summary>Validator seam: the cost rule of a zone install frame in a state.</summary>
    internal static bool TryGetZoneInstallRule(GameState state, InstallFrame install, out int discount, out BehaviorInstallCostRule costRule)
    {
        BehaviorInstallChoiceFrame? choice = state.ResolutionStack
            .OfType<BehaviorInstallChoiceFrame>()
            .LastOrDefault(candidate => string.Equals(candidate.FrameIdentity, install.ParentFrameIdentity, StringComparison.Ordinal));
        discount = choice?.Discount ?? 0;
        costRule = choice?.CostRule ?? BehaviorInstallCostRule.Normal;
        return choice is not null;
    }

    /// <summary>Validator seam: a behavior frame's identity as a child records it.</summary>
    internal static string BehaviorFrameIdentityOf(BehaviorResolutionFrame frame) => BehaviorFrameIdentity.Compute(frame);
}
