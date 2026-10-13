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

    /// <summary>
    /// Bot plan H3 batch 7 (the guide's C01, C02): a flooded HQ lets an agenda into a remote the Runner gets into, unless HQ
    /// holds ICE the Corp can put there and rez; v33 (and v34, which carries its rules) installs that ICE first. A box ladder game, LEO Glacier against
    /// Magdalene, where v31 put the agenda behind one ICE and the Runner stole it.
    /// </summary>
    [Theory]
    [InlineData(CorpHeuristicPolicy.V31, RuntimeCardClass.Agenda)]
    [InlineData(CorpHeuristicPolicy.V34, RuntimeCardClass.Ice)]
    public async Task TheScoringServerIsProtectedBeforeTheAgendaGoesIn(string version, RuntimeCardClass installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("corp-protects-scoring-server", version, 722);
        (_, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures",
            "saved-games", "corp-protects-scoring-server"));
        RuntimeCardClass? installed = chosen.Parameters is InstallCardParameters install
            && definition.Runtime.TryGet(state.GetCard(install.CardId).DefinitionId, out CardRuntimeSpec spec) ? spec.CardClass : null;
        Assert.True(installed == installs, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    /// <summary>
    /// Bot plan H3 batch 8 (the guide's C13): with three agendas in HQ the Corp doesn't click to draw a fourth; v34 takes the
    /// credit. A box ladder game, LEO Glacier against Magdalene, where v31 drew with three agendas in HQ on turn 2.
    /// </summary>
    [Theory]
    [InlineData(CorpHeuristicPolicy.V31, true)]
    [InlineData(CorpHeuristicPolicy.V34, false)]
    public async Task AFloodedHqDrawsNoMoreCards(string version, bool draws)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, _) = await ChooseAtAsync("corp-flooded-hq-draw", version, 55);
        Assert.True((chosen.Kind == ActionKind.DrawCard) == draws, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    /// <summary>
    /// Corp v38: LEO Construction ends a run by trashing a rezzed bioroid in the attacked server, so its remote is protected
    /// by a bioroid; 68 of 85 agendas stolen from LEO's remotes were behind none. A box ladder game, LEO Glacier against
    /// MuslihaT, action 175: v34 puts N-Pot on the remote, v38 Bumi 1.0.
    /// </summary>
    [Theory]
    [InlineData(CorpHeuristicPolicy.V34, false)]
    [InlineData(CorpHeuristicPolicy.V38, true)]
    public async Task LeosRemoteIsProtectedByABioroid(string version, bool bioroid)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("corp-identity-bioroid-remote", version, 175);
        (_, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures",
            "saved-games", "corp-identity-bioroid-remote"));
        bool installsBioroid = chosen.Parameters is InstallCardParameters install
            && definition.Runtime.TryGet(state.GetCard(install.CardId).DefinitionId, out CardRuntimeSpec spec)
            && spec.Subtypes.Contains("bioroid", StringComparer.OrdinalIgnoreCase);
        Assert.True(installsBioroid == bioroid, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    /// <summary>The Corp version's choice at the recorded decision <paramref name="sequence"/>, having watched the game to it.</summary>
    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string fixture, string version, long sequence)
    {
        (ReplayArtifact replay, FullMatchDefinition definition) = SavedDecision.ReadSaved(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", fixture));
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, sequence, PlayerSide.Corp, version);
        return (choice.Chosen, choice.State);
    }
}
