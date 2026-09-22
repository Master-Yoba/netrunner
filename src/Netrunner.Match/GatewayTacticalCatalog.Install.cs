using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private static TacticalFixture InstallLine(TacticalCaseMetadata metadata, TacticalBoard board, GameState initial,
        string sourceRole, Func<InstallCardParameters, bool> initialChoice, params string[] preserveRoles)
    {
        CardInstanceId source = board.Card(sourceRole);
        ZoneId oldZone = initial.GetCard(source).ZoneId;
        bool Expected(LegalAction action) => action.Parameters is InstallCardParameters install && install.CardId == source && initialChoice(install);
        if (!initial.PendingDecision!.LegalActions.Any(Expected)) throw new InvalidOperationException($"{metadata.Id}: missing intended install.");
        return new(metadata, board, initial,
            trace => !trace.OwnSteps.Any() ? trace.State.PendingDecision!.LegalActions.First(Expected)
                : MinimalInstallChoice(trace.State, source),
            trace => trace.State.GetCard(source).ZoneId != oldZone && trace.State.PendingDecision?.Kind is not
                (DecisionKind.InstallChoice or DecisionKind.MemoryCleanup or DecisionKind.BehaviorChoice or DecisionKind.TriggerChoice or DecisionKind.TriggerOrdering),
            trace => trace.OwnSteps.FirstOrDefault() is { } first && Expected(first.Action)
                && trace.State.GetZone(trace.State.GetCard(source).ZoneId).Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce or ZoneKind.RunnerRig
                && preserveRoles.All(role => trace.State.GetCard(board.Card(role)).ZoneId == initial.GetCard(board.Card(role)).ZoneId),
            Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(action => !Expected(action)));
    }

    private static LegalAction MinimalInstallChoice(GameState state, CardInstanceId source)
    {
        LegalAction[] actions = state.PendingDecision!.LegalActions.ToArray();
        LegalAction? finalize = actions.FirstOrDefault(action => action.Parameters is InstallCardParameters install
            && install.CardId == source && install.FinalizeSelection);
        if (finalize is not null) return finalize;
        return actions.FirstOrDefault(action => action.Parameters is InstallCardParameters install && install.CardId == source
                && install.SelectedReplacementCardIds.All(install.RequiredReplacementCardIds.Contains))
            ?? TacticalBoard.Passive(state);
    }

    private TacticalFixture BuildCorpDevelopment(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        board.CorpCredits = 6;
        switch (metadata.Id)
        {
            case "corp-development-01":
                board.Add("agenda-one", "30067", ZoneKind.CorpHand).Add("agenda-two", "30069", ZoneKind.CorpHand)
                    .Add("agenda-three", "30070", ZoneKind.CorpHand).Add("protection", "30072", ZoneKind.CorpHand)
                    .Add("rd-protection", "30074", ZoneKind.ServerIce, "rd", rezzed: true);
                return IceInstall("hq");
            case "corp-development-02":
                board.Add("hq-protection", "30072", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("protection", "30074", ZoneKind.CorpHand).Add("conduit", "30024", ZoneKind.RunnerRig);
                return IceInstall("rd");
            case "corp-development-03":
                board.Add("hq-protection", "30072", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("rd-protection", "30074", ZoneKind.ServerIce, "rd", rezzed: true)
                    .Add("protection", "30072", ZoneKind.CorpHand).Add("agenda", "30067", ZoneKind.CorpHand);
                return InstallLine(metadata, board, board.Action(PlayerSide.Corp), "protection",
                    install => install.DestinationKind == InstallDestinationKind.ServerIce && install.CreateNewRemote);
            case "corp-development-04":
                board.CorpCredits = 3;
                board.Add("hq-protection", "30073", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("hq-inner", "30074", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("protection", "30072", ZoneKind.CorpHand).Add("agenda", "30067", ZoneKind.CorpHand);
                TacticalFixture fixture = IceInstall("rd");
                return fixture with { Success = trace => fixture.Success(trace) && trace.Player(PlayerSide.Corp).Credits >= 3 };
            case "corp-development-05":
                board.Add("pharos", "30063", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("botulus", "30004", ZoneKind.RunnerRig, host: "pharos").Counter("botulus", "virus", 4)
                    .Add("second-pharos", "30063", ZoneKind.ServerIce, "rd", rezzed: true)
                    .Add("second-botulus", "30004", ZoneKind.RunnerRig, host: "second-pharos").Counter("second-botulus", "virus", 4);
                TacticalFixture purge = TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Corp), action => action.Kind == ActionKind.PurgeVirusCounters);
                return purge with { Success = trace => purge.Success(trace) && trace.State.RulesState!.Counters.All(counter => counter.Purpose != CardCounterPurpose.Virus) };
            default: throw new NotSupportedException(metadata.Id);
        }

        TacticalFixture IceInstall(string server)
        {
            ServerId destination = board.Server(server);
            return InstallLine(metadata, board, board.Action(PlayerSide.Corp), "protection",
                install => install.DestinationKind == InstallDestinationKind.ServerIce && install.ServerId == destination);
        }
    }

    private TacticalFixture BuildCorpReplacement(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        board.CorpCredits = 8;
        if (metadata.Id == "corp-replacement-01")
        {
            board.Add("nico", "30037", ZoneKind.ServerRoot, rezzed: true)
                .Counter("nico", "credit", 6, CardCounterPurpose.HostedCredit)
                .Add("incoming", "30067", ZoneKind.CorpHand);
            return InstallLine(metadata, board, board.Action(PlayerSide.Corp), "incoming", install => install.CreateNewRemote, "nico");
        }
        // Spin Doctor's rez draw has already been spent and Archives is empty.
        // This avoids inventing an installed zero-counter Nico/Regolith, both
        // of which Core trashes when their hosted pool empties.
        board.Add("spent-asset", "30053", ZoneKind.ServerRoot, rezzed: true)
            .Add("keep-upgrade", "30042", ZoneKind.ServerRoot, rezzed: true)
            .Add("incoming", metadata.Id == "corp-replacement-03" ? "30058" : "30067", ZoneKind.CorpHand);
        ServerId destination = board.Server("remote");
        GameState state = board.Action(PlayerSide.Corp);
        Func<InstallCardParameters, bool> target = install => install.ServerId == destination;
        if (metadata.Id is "corp-replacement-04" or "corp-replacement-05")
        {
            LegalAction begin = state.PendingDecision!.LegalActions.First(action => action.Parameters is InstallCardParameters install
                && install.CardId == board.Card("incoming") && install.ServerId == destination);
            state = board.Apply(state, begin).State;
            if (metadata.Id == "corp-replacement-04")
            {
                state = board.Apply(state, state.PendingDecision!.LegalActions.First(action => action.Parameters is InstallCardParameters install
                    && install.SelectedReplacementCardIds.SequenceEqual([board.Card("spent-asset")]))).State;
                target = install => install.FinalizeSelection;
            }
            else target = install => !install.FinalizeSelection && install.SelectedReplacementCardIds.SequenceEqual([board.Card("spent-asset")]);
        }
        TacticalFixture fixture = InstallLine(metadata, board, state, "incoming", target, "keep-upgrade");
        return metadata.Id == "corp-replacement-03"
            ? fixture with { Success = trace => fixture.Success(trace) && trace.State.GetCard(board.Card("spent-asset")).ZoneId == state.GetCard(board.Card("spent-asset")).ZoneId }
            : fixture with { Success = trace => fixture.Success(trace) && trace.State.GetZone(trace.State.GetCard(board.Card("spent-asset")).ZoneId).Kind == ZoneKind.CorpDiscard };
    }
}
