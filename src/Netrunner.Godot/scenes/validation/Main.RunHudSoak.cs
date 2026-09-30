using System.Globalization;
using System.Text.Json;
using Godot;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Run HUD soak (playtests, 29 September): plays whole Gateway games through the real client
/// with the HUD on, the local seat played by the Standard bot pressing the HUD's own buttons,
/// and checks the HUD on every frame of every run:
/// - it stays visible for the whole run (except while Archives' Access all runs);
/// - at one position of the run its height changes at most once and never goes back;
/// - at one position of the run the separator above its footer doesn't move back and forth;
/// - when it is the player's run decision, every action the HUD owns has a button, enabled
///   once the table settles;
/// - it stays inside the table and clear of the history panel.
/// Violations are written to soak.json with the game, seat, snapshot and action count.
/// <c>--run-hud-soak [--soak-game I] [--soak-seed S] [--soak-max-actions N] [--soak-output DIR]</c>;
/// <c>scripts/run-hud-soak.sh</c> runs several games, one launch each.
/// </summary>
public partial class Main
{
	private const int SoakSettleFrames = 180;
	private const int SoakMissingFrames = 30;

	private static readonly (string Corp, string Runner)[] SoakPairings =
	[
		("gateway-discretion-advised", "gateway-stolen-goods"),
		("gateway-advanced-yomi", "gateway-party-hard"),
		("gateway-hyper-velocity", "gateway-planning-ahead"),
		("gateway-quick-and-dirty", "gateway-stolen-goods"),
	];

	private sealed record SoakViolation(int Game, string Seat, long Sequence, int Actions, string Kind, string Detail);

	private sealed class HudAudit
	{
		public string? Key;
		public readonly List<float> Heights = [];
		public readonly List<float> Footers = [];
		public bool SeenThisRun;
		public string? RunId;
		public int RunsSeen;
	}

	private static readonly JsonSerializerOptions SoakJson = new() { WriteIndented = true };

	private async Task RunRunHudSoakAsync(IReadOnlyList<string> arguments)
	{
		var violations = new List<SoakViolation>();
		// One game per launch: a finished match needs a scene reload before the next one.
		int first = ReadSoakInt(arguments, "--soak-game", 0);
		int games = 1;
		ulong seed = (ulong)ReadSoakInt(arguments, "--soak-seed", 1);
		int maxActions = ReadSoakInt(arguments, "--soak-max-actions", 700);
		string output = ReadSoakOption(arguments, "--soak-output")
			?? ProjectSettings.GlobalizePath($"res://../../artifacts/run-hud-soak/{DateTime.UtcNow:yyyyMMddTHHmmss}");
		int hudPresses = 0, framesAudited = 0, runsSeen = 0;
		try
		{
			if (!HasAuthenticatedOfficialContent())
				throw new InvalidOperationException("The run HUD soak needs the embedded official content snapshot.");
			Directory.CreateDirectory(output);
			_speed.Value = _speed.MaxValue;
			_fastOpponent.ButtonPressed = true;
			for (int game = first; game < first + games; game++)
			{
				PlayerSide side = game % 2 == 0 ? PlayerSide.Runner : PlayerSide.Corp;
				(string corp, string runner) = SoakPairings[game / 2 % SoakPairings.Length];
				ulong gameSeed = seed * 1000 + (ulong)game;
				GD.Print($"RUN_HUD_SOAK game={game} side={side} {corp}~{runner} seed={gameSeed}");
				_humanSide.Select(side == PlayerSide.Runner ? 0 : 1);
				SelectPresetById(_corpPreset, _corpPresetIds, corp);
				SelectPresetById(_runnerPreset, _runnerPresetIds, runner);
				_seedInput.Text = gameSeed.ToString(CultureInfo.InvariantCulture);
				MatchCoordinator? previous = _coordinator;
				BeginMatch();
				// The new match's coordinator arrives asynchronously; don't read the finished one.
				for (int wait = 0; wait < 600 && (ReferenceEquals(_coordinator, previous) || _coordinator?.CurrentSnapshot is null); wait++)
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				MatchCoordinator coordinator = _coordinator is { } started && !ReferenceEquals(started, previous)
					? started : throw new InvalidOperationException("The next match did not start.");
				var fallback = new FullGameContractAgent(new FullGameContractPolicy(side), _runtimeCardCatalog);
				var random = new Random(unchecked((int)gameSeed));
				var audit = new HudAudit();
				int actions = 0;
				// One outstanding request at a time: each one queues a command on the coordinator's
				// worker, and asking every frame starved the game itself.
				Task<Observation>? ready = null;
				while (actions < maxActions)
				{
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
					framesAudited++;
					AuditHudFrame(audit, violations, game, side, actions);
					if (coordinator.CurrentSnapshot is { IsTerminal: true }) break;
					ready ??= coordinator.GetReadyObservationAsync(side);
					if (!ready.IsCompleted) continue;
					Observation observation = await ready;
					ready = null;
					if (coordinator.CurrentSnapshot is { IsTerminal: true }) break;
					// Let the client render this decision before acting on it, as a player would see it.
					long sequence = coordinator.CurrentSnapshot!.Sequence;
					// At the Corp seat the opponent's moves play back first, so the table can lag well behind;
					// wait until it shows this decision (it may skip snapshots on the way, so not by sequence).
					DecisionViewModel? issuedDecision = coordinator.CurrentSnapshot.View.Decision;
					for (int wait = 0; wait < 3600 && !SameDecision(_currentSnapshot?.View.Decision, issuedDecision); wait++)
					{
						await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
						AuditHudFrame(audit, violations, game, side, actions);
					}
					if (!SameDecision(_currentSnapshot?.View.Decision, issuedDecision))
						GD.Print($"RUN_HUD_SOAK slow-render game={game} actions={actions} shown={_currentSnapshot?.Sequence} issued={sequence}");
					// Choose only among what the current decision offers.
					IReadOnlyList<LegalActionViewModel> offered = coordinator.CurrentSnapshot!.View.LegalActions;
					// Playtest, 29 September: a run decision once showed an empty HUD with no way on.
					if (RunHudActive && coordinator.CurrentSnapshot!.View.Decision is { Kind: DecisionKind.RunTiming } runDecision
						&& runDecision.OwnerSide == side)
					{
						bool wayOn = false;
						for (int wait = 0; wait < SoakSettleFrames && !wayOn; wait++)
						{
							wayOn = _runHudButtons.Values.Any(button => IsInstanceValid(button) && button.IsVisibleInTree() && !button.Disabled)
								|| _tableActionButtons.Values.Any(button => IsInstanceValid(button) && button.IsVisibleInTree() && !button.Disabled)
								|| _accessActionButtons.Values.Any(button => IsInstanceValid(button) && button.IsVisibleInTree() && !button.Disabled)
								|| SpatialVisible || _boardChoiceVisible;
							if (!wayOn)
							{
								await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
								AuditHudFrame(audit, violations, game, side, actions);
							}
						}
						if (!wayOn)
							violations.Add(new(game, side.ToString(), sequence, actions, "no-way-on",
								$"the player's run decision ({runDecision.Prompt}) showed no enabled control"));
					}
					bool trace = OS.GetCmdlineUserArgs().Contains("--soak-trace");
					if (trace) GD.Print($"RUN_HUD_SOAK trace choose seq={sequence} shown={_currentSnapshot?.Sequence} decision={coordinator.CurrentSnapshot.View.Decision?.Kind}");
					ActionId? picked = SoakChoice(random);
					ActionId chosen = picked is { } id && (id == BreakAllChoice || offered.Any(action => action.Id == id))
						? id : await fallback.ChooseActionAsync(observation);
					if (chosen == BreakAllChoice)
					{
						if (await PressBreakAllAsync(audit, violations, game, side, actions)) { hudPresses++; actions++; continue; }
						chosen = await fallback.ChooseActionAsync(observation);
					}
					if (trace) GD.Print($"RUN_HUD_SOAK trace chose {chosen} picked={picked}");
					if (await PressHudButtonAsync(chosen, audit, violations, game, side, actions))
					{
						hudPresses++;
						actions++;
						continue;
					}
					if (trace) GD.Print($"RUN_HUD_SOAK trace submitting {chosen}");
					MatchSubmissionResult result;
					try
					{
						result = await coordinator.SubmitHumanActionAsync(chosen);
					}
					catch (InvalidOperationException stale) when (stale.Message.Contains("awaiting a human decision", StringComparison.Ordinal))
					{
						GD.Print($"RUN_HUD_SOAK stale-choice game={game} actions={actions}: {stale.Message}");
						continue;
					}
					if (trace) GD.Print($"RUN_HUD_SOAK trace submitted accepted={result.Accepted}");
					// The decision can move on while the driver waits for the table (an auto-pass, say);
					// a stale choice is simply made again from the next decision.
					if (!result.Accepted)
					{
						GD.Print($"RUN_HUD_SOAK stale-choice game={game} actions={actions}: {result.Feedback}");
						continue;
					}
					actions++;
				}
				runsSeen += audit.RunsSeen;
				GD.Print($"RUN_HUD_SOAK game={game} actions={actions} terminal={coordinator.CurrentSnapshot?.IsTerminal} violations={violations.Count}");
			}
			await File.WriteAllTextAsync(Path.Combine(output, "soak.json"), JsonSerializer.Serialize(new
			{
				games,
				seed,
				maxActions,
				runsSeen,
				hudPresses,
				framesAudited,
				violations = violations.Select(violation => new
				{
					violation.Game,
					violation.Seat,
					violation.Sequence,
					violation.Actions,
					violation.Kind,
					violation.Detail,
				}),
			}, SoakJson));
			foreach (SoakViolation violation in violations.Take(20))
				GD.PrintErr($"RUN_HUD_SOAK VIOLATION game={violation.Game} seat={violation.Seat} seq={violation.Sequence} actions={violation.Actions} {violation.Kind}: {violation.Detail}");
			// A Runner game must have pressed the HUD; at the Corp seat auto-pass may leave nothing to press.
			bool runnerGame = first % 2 == 0;
			if (violations.Count > 0 || runsSeen == 0 || runnerGame && hudPresses == 0)
			{
				GD.PrintErr($"RUN_HUD_SOAK FAIL games={games} runs={runsSeen} hud_presses={hudPresses} violations={violations.Count} output={output}");
				QuitCleanly(1);
				return;
			}
			GD.Print($"RUN_HUD_SOAK PASS games={games} runs={runsSeen} hud_presses={hudPresses} frames={framesAudited} output={output}");
			QuitCleanly();
		}
		catch (Exception error)
		{
			GD.PrintErr($"RUN_HUD_SOAK FAIL {error}");
			QuitCleanly(1);
		}
	}

	private static readonly ActionId BreakAllChoice = new("soak:break-all");

	/// <summary>
	/// A seeded, run-happy choice among the rendered legal actions, so runs cover many situations:
	/// runs often, breaks or boosts at random (or Break all), accesses at random, jacks out rarely.
	/// Null leaves the decision to the contract agent.
	/// </summary>
	private ActionId? SoakChoice(Random random)
	{
		if (_currentSnapshot is not { View: { Decision: { } decision } view } || decision.OwnerSide != view.Viewer) return null;
		LegalActionViewModel[] legal = [.. view.LegalActions];
		LegalActionViewModel? Pick(Func<LegalActionViewModel, bool> filter)
		{
			LegalActionViewModel[] matching = [.. legal.Where(filter)];
			return matching.Length == 0 ? null : matching[random.Next(matching.Length)];
		}
		if (view.Run is null)
		{
			double pick = random.NextDouble();
			// Runs often; installs often too, so encounters have breakers to boost and break with.
			return pick < 0.45 ? Pick(action => action.Kind == ActionKind.StartRun)?.Id
				: pick < 0.8 ? Pick(action => action.Kind == ActionKind.InstallCard)?.Id
				: null;
		}
		if (_runHud?.FindChild("BreakAll", true, false) is Button { Disabled: false } && random.NextDouble() < 0.4) return BreakAllChoice;
		// A player can only press what the HUD shows (it offers the preferred payment of each boost
		// or break, as the icebreaker panel did), so run choices come from its buttons.
		if (RunHudActive)
		{
			HashSet<ActionId> shown = [.. _runHudButtons.Where(pair => IsInstanceValid(pair.Value) && pair.Value.IsVisibleInTree()).Select(pair => pair.Key)];
			legal = [.. legal.Where(action => shown.Contains(action.Id) || !RunHudTakes(action, view))];
			if (legal.Length == 0) return null;
		}
		double roll = random.NextDouble();
		LegalActionViewModel? chosen = roll < 0.08 ? Pick(action => action.Kind == ActionKind.JackOut)
			: roll < 0.6 ? Pick(action => action.Kind is ActionKind.PumpBreaker or ActionKind.BreakSubroutines or ActionKind.ChooseBreachCandidate
				or ActionKind.StealAgenda or ActionKind.TrashAccessedCard)
			: null;
		chosen ??= Pick(action => action.Kind is ActionKind.PassPriority or ActionKind.ContinueRun or ActionKind.NoAccessAction
			or ActionKind.ChooseBreachCandidate or ActionKind.StealAgenda);
		chosen ??= Pick(_ => true);
		return chosen?.Id;
	}

	/// <summary>Presses Break all once and waits until its steps have run.</summary>
	private async Task<bool> PressBreakAllAsync(HudAudit audit, List<SoakViolation> violations, int game, PlayerSide side, int actions)
	{
		if (_runHud?.FindChild("BreakAll", true, false) is not Button { Disabled: false } button) return false;
		string? encounter = _currentSnapshot?.View.Run?.Encounter?.Id;
		button.EmitSignal(BaseButton.SignalName.Pressed);
		for (int frame = 0; frame < 1200; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			AuditHudFrame(audit, violations, game, side, actions);
			if (_breakAllBreaker is null && Volatile.Read(ref _submissionInFlight) == 0) return true;
		}
		violations.Add(new(game, side.ToString(), _currentSnapshot?.Sequence ?? -1, actions, "break-all-stuck", $"Break all never finished encounter {encounter}"));
		StopBreakAll();
		return false;
	}

	/// <summary>
	/// When the HUD owns the chosen action, presses its button like a player: the button must
	/// exist, and be enabled once the table settles. Returns false to submit another way.
	/// </summary>
	private async Task<bool> PressHudButtonAsync(ActionId chosen, HudAudit audit, List<SoakViolation> violations, int game,
		PlayerSide side, int actions)
	{
		if (_currentSnapshot is not { } snapshot || !RunHudActive || snapshot.View.Decision?.Kind != DecisionKind.RunTiming
			|| snapshot.View.LegalActions.FirstOrDefault(action => action.Id == chosen) is not { } action
			|| !RunHudTakes(action, snapshot.View))
			return false;
		// Break-all steps and Access all are the HUD's own automation; a player presses it once.
		Button? button = null;
		for (int frame = 0; frame < SoakSettleFrames; frame++)
		{
			if (_runHudButtons.TryGetValue(chosen, out Button? found) && IsInstanceValid(found) && found.IsVisibleInTree())
			{
				button = found;
				if (!found.Disabled) break;
			}
			else if (frame == SoakMissingFrames)
			{
				// The HUD shows one payment for each boost or break; the same action with another
				// payment is pressed through that button, as a player would.
				string? source = BoardIcebreakerPresenter.Source(action);
				ActionId? equivalent = snapshot.View.LegalActions.FirstOrDefault(candidate => candidate.Kind == action.Kind
					&& BoardIcebreakerPresenter.Source(candidate) == source && source is not null
					&& _runHudButtons.TryGetValue(candidate.Id, out Button? shown) && IsInstanceValid(shown) && shown.IsVisibleInTree())?.Id;
				if (equivalent is { } other)
				{
					chosen = other;
					frame = -1;
					continue;
				}
				violations.Add(new(game, side.ToString(), snapshot.Sequence, actions, "missing-button",
					$"{action.Kind} ({TableActionLabel(action)}) has no HUD button"));
				return false;
			}
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			AuditHudFrame(audit, violations, game, side, actions);
		}
		if (button is null) return false;
		if (button.Disabled)
		{
			violations.Add(new(game, side.ToString(), snapshot.Sequence, actions, "disabled-button",
				$"{action.Kind} ({button.Text}) stayed disabled for {SoakSettleFrames} frames"));
			return false;
		}
		long before = _coordinator!.CurrentSnapshot!.Sequence;
		button.EmitSignal(BaseButton.SignalName.Pressed);
		for (int frame = 0; frame < 600; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			AuditHudFrame(audit, violations, game, side, actions);
			if (_coordinator.CurrentSnapshot!.Sequence != before && Volatile.Read(ref _submissionInFlight) == 0) return true;
		}
		violations.Add(new(game, side.ToString(), snapshot.Sequence, actions, "press-ignored", $"Pressing {button.Text} submitted nothing"));
		return false;
	}

	/// <summary>One frame of the HUD rules while a run is on.</summary>
	private void AuditHudFrame(HudAudit audit, List<SoakViolation> violations, int game, PlayerSide side, int actions)
	{
		MatchSnapshot? current = _currentSnapshot;
		if (current is null || current.View.Run is not { EndReason: null } run || !RunHudOwns(current))
		{
			audit.Key = null;
			audit.Heights.Clear();
			audit.Footers.Clear();
			if (current?.View.Run is null) audit.RunId = null;
			return;
		}
		MatchSnapshot snapshot = current;
		if (audit.RunId != run.Id)
		{
			audit.RunId = run.Id;
			audit.SeenThisRun = false;
			audit.RunsSeen++;
		}
		void Violation(string kind, string detail)
		{
			// One report per kind per snapshot is enough to find it.
			if (violations.Any(existing => existing.Game == game && existing.Sequence == snapshot.Sequence && existing.Kind == kind)) return;
			violations.Add(new(game, side.ToString(), snapshot.Sequence, actions, kind, detail));
		}
		if (_runHud is not { Visible: true } hud)
		{
			if (audit.SeenThisRun) Violation("hidden-mid-run", $"run {run.Id} at {run.Step}");
			return;
		}
		audit.SeenThisRun = true;
		string key = $"{run.Id}:{run.CurrentIceIndex}:{run.Encounter?.Id}:{run.Access.State}:{run.Access.Accessed.Count}";
		if (key != audit.Key)
		{
			audit.Key = key;
			audit.Heights.Clear();
			audit.Footers.Clear();
		}
		float height = hud.Size.Y;
		if (audit.Heights.Count == 0 || !Mathf.IsEqualApprox(audit.Heights[^1], height))
		{
			if (audit.Heights.Any(earlier => Mathf.IsEqualApprox(earlier, height)))
				Violation("height-oscillation", $"{string.Join(" → ", audit.Heights.Select(value => value.ToString("0", CultureInfo.InvariantCulture)))} → {height:0} at {run.Step}");
			else if (audit.Heights.Count >= 2)
				Violation("height-jumps", $"{string.Join(" → ", audit.Heights.Select(value => value.ToString("0", CultureInfo.InvariantCulture)))} → {height:0} at {run.Step}");
			audit.Heights.Add(height);
		}
		// Measured from the HUD's bottom, so a legitimate growth of the whole HUD doesn't count.
		float footer = hud.Size.Y - _runHudSeparator!.Position.Y;
		if (audit.Footers.Count == 0 || !Mathf.IsEqualApprox(audit.Footers[^1], footer))
		{
			if (audit.Footers.Any(earlier => Mathf.IsEqualApprox(earlier, footer)))
				Violation("separator-oscillation", $"{string.Join(" → ", audit.Footers.Select(value => value.ToString("0", CultureInfo.InvariantCulture)))} → {footer:0} at {run.Step}");
			audit.Footers.Add(footer);
		}
		if (hud.Size.X > RunHudWidth + 0.5f) Violation("widened", $"content widened the HUD to {hud.Size.X:0} at {run.Step}");
		Rect2 rect = hud.GetGlobalRect();
		if (!_tableViewport.GetGlobalRect().Grow(1).Encloses(rect)) Violation("outside-table", $"{rect} outside {_tableViewport.GetGlobalRect()}");
		if (_matchHistory.IsVisibleInTree() && rect.Intersects(_matchHistory.GetGlobalRect())) Violation("covers-history", $"{rect} over {_matchHistory.GetGlobalRect()}");
	}

	private static int ReadSoakInt(IReadOnlyList<string> arguments, string name, int fallback) =>
		ReadSoakOption(arguments, name) is { } text ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) : fallback;

	private static string? ReadSoakOption(IReadOnlyList<string> arguments, string name)
	{
		for (int index = 0; index < arguments.Count - 1; index++)
			if (arguments[index] == name) return arguments[index + 1];
		return null;
	}
}
