using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Codex reviews of Corp v19, 1 October, in saved ladder games of the current bots:
/// - it kept advancing an Urtica Cipher whose damage already exceeded the Runner's grip;
/// - Sprint shuffled the agendas that would have won (at 6 points) back into R&D;
/// - its reserve for agenda ICE declined Ansel 1.0 on R&D and Diviner on HQ (with an agenda in hand), and
///   the Runner stole from both.
/// Reviews of Corp v20's losses to Codex: agendas behind ICE the Runner's rig broke, and ICE on an Archives of spent
/// operations.
/// </summary>
public sealed class CorpCodexV20Tests
{
    [Theory]
    [InlineData(CorpHeuristicPolicy.V19, true)]
    [InlineData(CorpHeuristicPolicy.V20, false)]
    public async Task ALethalTrapIsNotAdvancedFurther(string version, bool advances)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-urtica", version, 461);
        bool urtica = chosen.Parameters is AdvanceCardParameters advance && Title(state, advance.CardId) == "Urtica Cipher";
        Assert.True(advances == urtica, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V19, true)]
    [InlineData(CorpHeuristicPolicy.V20, false)]
    public async Task SprintKeepsTheAgendaThatWouldWin(string version, bool shufflesAnAgenda)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-sprint-ansel", version, 785);
        CardInstanceId[] cards = chosen.Parameters is BehaviorChoiceParameters choice ? [.. choice.CardIds] : [];
        bool agenda = cards.Any(id => Title(state, id) is "Luminal Transubstantiation" or "Offworld Office");
        Assert.True(shufflesAnAgenda == agenda, $"{version} chose {string.Join(", ", cards.Select(id => Title(state, id)))}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V19, "codex-corp-sprint-ansel", 246, false)]
    [InlineData(CorpHeuristicPolicy.V20, "codex-corp-sprint-ansel", 246, true)]
    [InlineData(CorpHeuristicPolicy.V19, "codex-corp-diviner", 121, false)]
    [InlineData(CorpHeuristicPolicy.V20, "codex-corp-diviner", 121, true)]
    public async Task CentralIceIsRezzedWhenItCanStopTheRun(string version, string fixture, long sequence, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, _) = await ChooseAtAsync(fixture, version, sequence);
        Assert.True(rezzes == (chosen.Kind == ActionKind.RezCard), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V20, true)]
    [InlineData(CorpHeuristicPolicy.V21, false)]
    public async Task AnAgendaWaitsWhileTheRunnerSRigBreaksItsServer(string version, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October): Orbital Superiority behind a lone Palisade against an installed Cleaver,
        // stolen that turn.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-breakable-agenda", version, 223);
        bool agenda = chosen.Parameters is InstallCardParameters install && Title(state, install.CardId) == "Orbital Superiority";
        Assert.True(installs == agenda, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V20, true)]
    [InlineData(CorpHeuristicPolicy.V21, false)]
    public async Task ArchivesOfSpentOperationsGetsNoIce(string version, bool walls)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October): Palisade on an Archives of two Predictive Planograms and Retribution.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-archives-ice", version, 270);
        bool archives = chosen.Parameters is InstallCardParameters { ServerId: { } server }
            && state.Servers.Single(value => value.Id == server).Kind == ServerKind.Archives;
        Assert.True(walls == archives, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V21, "codex-corp-full-hq-agenda", 488, true)]
    [InlineData(CorpHeuristicPolicy.V22, "codex-corp-full-hq-agenda", 488, false)]
    public async Task AFullHqDoesNotPushAnAgendaIntoABreakableServer(string version, string fixture, long sequence, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 3): Send a Message behind a lone Palisade against Cleaver and 6
        // credits; v21's rule let a full HQ through. (With two agendas in HQ one still goes out, as v21 allowed.)
        (LegalAction chosen, GameState state) = await ChooseAtAsync(fixture, version, sequence);
        bool agenda = chosen.Parameters is InstallCardParameters install && state.GetCard(install.CardId).DefinitionId.Value is var code
            && CardDataResource.LoadEmbedded().Cards.Single(card => card.Code == code).AgendaPoints is not null;
        Assert.True(installs == agenda, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V21, false)]
    [InlineData(CorpHeuristicPolicy.V22, true)]
    public async Task AnAssetThatPaysBackAtOnceIsRezzed(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 3): Regolith Mining License held back to keep the ICE budget.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-regolith", version, 51);
        bool regolith = chosen.Parameters is RezCardParameters rez && Title(state, rez.CardId) == "Regolith Mining License";
        Assert.True(rezzes == regolith, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V21, true)]
    [InlineData(CorpHeuristicPolicy.V22, false)]
    public async Task ABrokeCorpBuildsMoneyBeforePurging(string version, bool purges)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 3): three clicks of purging at 0 credits while Botulus grew back.
        (LegalAction chosen, _) = await ChooseAtAsync("codex-corp-broke-purge", version, 331);
        Assert.True(purges == (chosen.Kind == ActionKind.PurgeVirusCounters), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V22, true)]
    [InlineData(CorpHeuristicPolicy.V23, false)]
    public async Task ATrapTheRunnerHasAccessedIsNotAdvanced(string version, bool advances)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 4): an Urtica Cipher the Runner accessed at #62, advanced at #219 with an
        // agenda ready to score behind ICE.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-known-trap", version, 219);
        bool urtica = chosen.Parameters is AdvanceCardParameters advance && Title(state, advance.CardId) == "Urtica Cipher";
        Assert.True(advances == urtica, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V22, true)]
    [InlineData(CorpHeuristicPolicy.V23, false)]
    public async Task AnAssetWaitsForAClickToUseIt(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 4): Regolith Mining License rezzed with no click left; trashed at once.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-regolith-no-click", version, 440);
        bool regolith = chosen.Parameters is RezCardParameters rez && Title(state, rez.CardId) == "Regolith Mining License";
        Assert.True(rezzes == regolith, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V22, true)]
    [InlineData(CorpHeuristicPolicy.V23, false)]
    public async Task TwoAgendasStayInASaferHq(string version, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 4): with Offworld Office and Above the Law in HQ, Offworld Office went
        // behind a lone Whitespace against Buzzsaw and was stolen.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-two-agendas", version, 682);
        bool agenda = chosen.Parameters is InstallCardParameters install && Title(state, install.CardId) is "Offworld Office" or "Above the Law";
        Assert.True(installs == agenda, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V23, "Botulus")]
    [InlineData(CorpHeuristicPolicy.V24, "Carmen")]
    public async Task RetributionTrashesTheBreakerThatMatters(string version, string expected)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 4): Retribution trashed Botulus over the Carmen breaking every sentry on
        // the board; the target score had the wrong sign for a Runner card.
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-two-agendas", version, 348);
        string target = chosen.Parameters is BehaviorChoiceParameters choice ? string.Join(", ", choice.CardIds.Select(id => Title(state, id))) : "";
        Assert.Equal(expected, target);
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V23, false)]
    [InlineData(CorpHeuristicPolicy.V24, true)]
    public async Task BallistaEndsTheRunOnAnAgendaServer(string version, bool ends)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 4): Ballista trashed Cleaver instead of ending the run on Offworld Office's
        // server, with the inner Ballista unaffordable; the Runner stole the agenda.
        (LegalAction chosen, _) = await ChooseAtAsync("codex-corp-ballista-etr", version, 487);
        bool endRun = chosen.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.EndRun };
        Assert.True(ends == endRun, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    private static string Title(GameState state, CardInstanceId id) =>
        CardDataResource.LoadEmbedded().Cards.Single(card => card.Code == state.GetCard(id).DefinitionId.Value).Title;

    /// <summary>The Corp version's choice at the recorded decision <paramref name="sequence"/>, having watched the game to it.</summary>
    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string fixture, string version, long sequence)
    {
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", fixture)));
        GameState state = saved.Replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", version);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            if (recorded.Sequence == sequence)
            {
                PendingDecision pending = state.PendingDecision!;
                ActionId id = await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime));
                return (pending.LegalActions.Single(action => action.Id == id), state);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The game has no action {sequence}.");
    }
}
