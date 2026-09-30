using System.Globalization;
using System.Text;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

public static partial class CliCommands
{
    /// <summary>
    /// ai-selfplay --content &lt;corp~runner&gt; --seed N [--corp entrant] [--runner entrant] --output &lt;new dir&gt;.
    /// Plays one bot-versus-bot game and writes its replay and a readable log of
    /// every real choice on both sides, with a board summary at each turn's
    /// start: the raw material for reviewing the bot's play (Wave 4.5).
    /// </summary>
    private static int RunAiSelfPlay(string[] args, TextWriter output, TextWriter error)
    {
        string? content = null, destination = null;
        string corpEntrant = "standard", runnerEntrant = "standard";
        ulong seed = 1;
        try
        {
            for (int index = 1; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--content" when index + 1 < args.Length: content = args[++index]; break;
                    case "--seed" when index + 1 < args.Length: seed = ulong.Parse(args[++index], CultureInfo.InvariantCulture); break;
                    case "--corp" when index + 1 < args.Length: corpEntrant = args[++index]; break;
                    case "--runner" when index + 1 < args.Length: runnerEntrant = args[++index]; break;
                    case "--output" when index + 1 < args.Length: destination = args[++index]; break;
                    default: throw new ArgumentException($"Unsupported ai-selfplay argument '{args[index]}'.");
                }
            }
            if (content is null || destination is null) throw new ArgumentException("--content and --output are required.");
            if (Directory.Exists(destination)) throw new ArgumentException("--output must name a new directory.");
            if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
                throw new ArgumentException("ai-selfplay needs previously provisioned official content embedded at build time.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            error.WriteLine($"ai-selfplay failed: {exception.Message}");
            return 2;
        }

        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(content, seed);
        FullMatchResult result = FullMatchRunner.RunAsync(definition, RatingLadder.DefaultActionCap,
            RatingLadder.CreateAgent(corpEntrant, PlayerSide.Corp, definition),
            RatingLadder.CreateAgent(runnerEntrant, PlayerSide.Runner, definition)).GetAwaiter().GetResult();
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "replay.json"), EngineJson.Serialize(result.Replay));
        string log = SelfPlayLog(definition, result, corpEntrant, runnerEntrant);
        File.WriteAllText(Path.Combine(destination, "game.txt"), log);
        output.WriteLine($"ai-selfplay content={content} seed={seed} winner={result.Outcome.Winner?.ToString() ?? "none"} "
            + $"reason={result.Outcome.Reason?.ToString() ?? "none"} actions={result.ActionCount} output={destination}");
        return 0;
    }

    private static string SelfPlayLog(FullMatchDefinition definition, FullMatchResult result, string corpEntrant, string runnerEntrant)
    {
        var log = new StringBuilder();
        log.AppendLine(CultureInfo.InvariantCulture, $"# {definition.Request.CorpDeck.PresetId} ({corpEntrant}) vs {definition.Request.RunnerDeck.PresetId} ({runnerEntrant})");
        GameState state = result.Replay.InitialSnapshot.ToState();
        var metadata = new CardCatalogMetadataResolver(CardDataResource.LoadEmbedded());
        (int, PlayerSide)? turn = null;
        foreach (ReplayAction action in result.Replay.Actions)
        {
            PendingDecision pending = state.PendingDecision!;
            PlayerSide side = state.GetPlayer(pending.Owner).Side;
            if (turn != (state.Turn.TurnNumber, state.Turn.ActiveSide))
            {
                turn = (state.Turn.TurnNumber, state.Turn.ActiveSide);
                log.AppendLine().AppendLine(BoardSummary(state, definition, metadata));
            }
            if (pending.LegalActions.Count > 1)
            {
                Observation observation = ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime);
                Func<LegalAction?, string?> describe = HumanGameReview.Describer(ObservationViewMapper.Build(observation, metadata, definition.Behavior));
                LegalAction? chosen = pending.LegalActions.FirstOrDefault(value => value.Id == action.Submission.ActionId);
                log.AppendLine(CultureInfo.InvariantCulture,
                    $"#{action.Sequence} {side} {pending.Kind}: {describe(chosen) ?? chosen?.Description} ({pending.LegalActions.Count} options)");
            }
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime, ApplyMode.Simulation).State;
        }
        log.AppendLine().AppendLine(CultureInfo.InvariantCulture,
            $"# Result: {result.Outcome.Winner?.ToString() ?? "none"} by {result.Outcome.Reason?.ToString() ?? "none"} after {result.ActionCount} actions");
        return log.ToString();
    }

    /// <summary>Turn, credits, points and every server's ICE and root, with card names (the review sees both sides).</summary>
    private static string BoardSummary(GameState state, FullMatchDefinition definition, CardCatalogMetadataResolver metadata)
    {
        string Name(CardInstanceId id)
        {
            CardInstanceState card = state.GetCard(id);
            string title = metadata.TryResolve(card.DefinitionId.Value, out CardPresentationMetadata found) ? found.Title : card.DefinitionId.Value;
            return card.FaceUp ? title : $"{title}*";
        }
        PlayerState corp = state.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState runner = state.Players.Single(player => player.Side == PlayerSide.Runner);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"## Turn {state.Turn.TurnNumber} {state.Turn.ActiveSide} · Corp {corp.Credits}c {corp.AgendaPoints}pt HQ {state.GetZone(ZoneKind.CorpHand).CardIds.Count}"
            + $" · Runner {runner.Credits}c {runner.AgendaPoints}pt grip {state.GetZone(ZoneKind.RunnerGrip).CardIds.Count} tags {state.RulesState?.Players.FirstOrDefault(player => player.PlayerId == runner.Id)?.Tags ?? 0}");
        foreach (ServerState server in state.Servers)
        {
            ZoneState[] zones = [.. state.Zones.Where(zone => zone.ServerId == server.Id)];
            string ice = string.Join(", ", zones.Where(zone => zone.Kind == ZoneKind.ServerIce).SelectMany(zone => zone.CardIds).Select(Name));
            string root = string.Join(", ", zones.Where(zone => zone.Kind == ZoneKind.ServerRoot).SelectMany(zone => zone.CardIds)
                .Select(id => Name(id) + (state.GetCard(id).AdvancementCounters is > 0 and var counters ? $" [{counters}]" : "")));
            text.AppendLine().Append(CultureInfo.InvariantCulture, $"  {server.Kind} {server.Id}: ICE [{ice}] root [{root}]");
        }
        IEnumerable<string> rig = state.GetZone(ZoneKind.RunnerRig).CardIds.Select(Name);
        text.AppendLine().Append(CultureInfo.InvariantCulture, $"  Rig: {string.Join(", ", rig)}");
        return text.ToString();
    }
}
