using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Godot;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Scripted UI capture for design review. An official match runs through the
/// normal card-table presentation while the contract agent plays the local
/// seat. Whenever a published (viewer-filtered) event batch contains one of
/// the configured event kinds, a burst of viewport frames is saved together
/// with a contact sheet. Frames can contain official card art and must stay
/// local; they are review evidence, not repository fixtures.
/// </summary>
public partial class Main
{
	private const int CaptureSheetColumns = 4;
	private const int CaptureSheetGap = 4;
	private static readonly JsonSerializerOptions CaptureJson = new() { WriteIndented = true };

	/// <summary>Raised when card-table playback renders a snapshot, which lags publication by design.</summary>
	private Action<MatchSnapshot>? _snapshotRendered;

	private sealed record CaptureOptions(
		string Output,
		ulong Seed,
		PlayerSide Side,
		string Preset,
		IReadOnlySet<string> EventKinds,
		int Bursts,
		int Frames,
		double Interval,
		int MaxActions,
		double Pace);

	private sealed record CaptureBurst(int Index, string EventKind, int ActionCount, int? Turn, string ContactSheet, string[] Frames);

	private static bool TryReadCaptureOptions(IReadOnlyList<string> arguments, out CaptureOptions? options, out string? error)
	{
		options = null;
		error = null;
		string? output = Value("--capture-output");
		if (output is null)
		{
			return false;
		}

		try
		{
			string side = Value("--capture-side") ?? "runner";
			string preset = Value("--capture-preset") ?? "basic";
			if (side is not ("corp" or "runner")) throw new ArgumentException("--capture-side requires corp or runner.");
			if (preset is not ("basic" or "expanded")) throw new ArgumentException("--capture-preset requires basic or expanded.");
			string events = Value("--capture-events") ?? string.Join(',',
				EngineEventKinds.RunInitiated, EngineEventKinds.AgendaScored, EngineEventKinds.AgendaStolen);
			options = new CaptureOptions(
				Path.GetFullPath(output),
				ulong.Parse(Value("--capture-seed") ?? "42", NumberStyles.None, CultureInfo.InvariantCulture),
				side == "corp" ? PlayerSide.Corp : PlayerSide.Runner,
				preset,
				events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal),
				Bounded("--capture-bursts", 4, 1, 100),
				Bounded("--capture-frames", 12, 1, 120),
				Bounded("--capture-interval-ms", 150, 16, 5000) / 1000.0,
				Bounded("--capture-max-actions", 800, 1, 10_000),
				Bounded("--capture-pace-ms", 500, 0, 10_000) / 1000.0);
			if (Directory.Exists(options.Output) || File.Exists(options.Output))
				throw new ArgumentException("--capture-output must name a new directory.");
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
		{
			error = exception.Message;
			return true;
		}

		string? Value(string name)
		{
			int index = arguments.ToList().IndexOf(name);
			return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
		}

		int Bounded(string name, int fallback, int minimum, int maximum)
		{
			string? text = Value(name);
			if (text is null) return fallback;
			int value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
			if (value < minimum || value > maximum) throw new ArgumentException($"{name} requires {minimum} to {maximum}.");
			return value;
		}
	}

	private async Task RunCaptureAsync(CaptureOptions options)
	{
		try
		{
			if (!HasAuthenticatedOfficialContent())
				throw new InvalidOperationException("UI capture needs the embedded official content snapshot.");
			Directory.CreateDirectory(options.Output);
			SelectCapturePresets(options.Preset);
			_seedInput.Text = options.Seed.ToString(CultureInfo.InvariantCulture);
			_humanSide.Select(options.Side == PlayerSide.Runner ? 0 : 1);
			BeginMatch();
			MatchCoordinator coordinator = _coordinator ?? throw new InvalidOperationException("The match did not start.");

			// Trigger on what the player sees: playback renders a published
			// snapshot only after earlier beats have been shown.
			var triggers = new ConcurrentQueue<string>();
			_snapshotRendered = snapshot =>
			{
				foreach (EngineEvent published in snapshot.Events)
				{
					if (options.EventKinds.Contains(published.Kind)) triggers.Enqueue(published.Kind);
				}
			};

			var agent = new FullGameContractAgent(new FullGameContractPolicy(options.Side), _runtimeCardCatalog);
			var bursts = new List<CaptureBurst>();
			int actions = 0;
			Task<Observation>? ready = null;
			Task<MatchSubmissionResult>? submitting = null;
			ulong actAfter = 0;
			// Every frame services triggers first. Submissions run in the
			// background because the coordinator completes them only after the
			// opponent has responded, which is exactly what a burst must see.
			// Motion contract (U2): a card is never on screen twice, as a flight
			// stand-in and the real card at once. Sample it on every frame.
			var duplicates = new SortedSet<string>(StringComparer.Ordinal);
			int motionFrames = 0;
			while (bursts.Count < options.Bursts && actions < options.MaxActions)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (_gameTable.ActiveMotionCount > 0) motionFrames++;
				foreach (string duplicate in _gameTable.DuplicateMotionKeys()) duplicates.Add($"{duplicate}@{actions}");
				if (triggers.TryDequeue(out string? kind))
				{
					bursts.Add(await CaptureBurstAsync(options, bursts.Count + 1, kind, actions));
					continue;
				}

				if (submitting is not null)
				{
					if (!submitting.IsCompleted) continue;
					if (!(await submitting).Accepted)
						throw new InvalidOperationException($"Capture action was rejected at action {actions}.");
					submitting = null;
					actions++;
				}

				if (coordinator.CurrentSnapshot is { IsTerminal: true }) break;
				ready ??= coordinator.GetReadyObservationAsync(options.Side);
				if (!ready.IsCompleted) continue;
				if (actAfter == 0) actAfter = Time.GetTicksMsec() + (ulong)(options.Pace * 1000);
				if (Time.GetTicksMsec() < actAfter) continue;
				Observation observation = await ready;
				ready = null;
				actAfter = 0;
				if (coordinator.CurrentSnapshot is { IsTerminal: true }) break;
				ActionId action = await agent.ChooseActionAsync(observation);
				submitting = coordinator.SubmitHumanActionAsync(action);
			}

			string summary = Path.Combine(options.Output, "capture.json");
			await File.WriteAllTextAsync(summary, JsonSerializer.Serialize(new
			{
				schemaVersion = 1,
				kind = "ui-capture",
				seed = options.Seed,
				side = options.Side.ToString(),
				preset = options.Preset,
				eventKinds = options.EventKinds.Order(StringComparer.Ordinal),
				localActions = actions,
				motionFrames,
				duplicates,
				snapMismatches = _snapMismatches,
				beats = _beats.Played.GroupBy(beat => beat.Kind).OrderBy(group => group.Key)
					.ToDictionary(group => group.Key.ToString(), group => group.Count()),
				beatCaptions = _beats.Played.Select(beat => beat.Caption).Distinct().Take(80),
				bursts,
			}, CaptureJson));
			if (duplicates.Count > 0)
				throw new InvalidOperationException($"{duplicates.Count} card(s) were on screen twice during motion: {string.Join(", ", duplicates.Take(8))}.");
			GD.Print($"UI_CAPTURE PASS bursts={bursts.Count} local_actions={actions} motion_frames={motionFrames} duplicates=0 "
				+ $"beats={_beats.Played.Count} beat_kinds={_beats.Played.Select(beat => beat.Kind).Distinct().Count()} snap_mismatches={_snapMismatches} output={options.Output}");
			GetTree().Quit(0);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"UI_CAPTURE FAIL {exception.Message}");
			GetTree().Quit(1);
		}
	}

	private void SelectCapturePresets(string preset)
	{
		foreach (var (button, ids, side) in new[]
		{
			(_corpPreset, _corpPresetIds, "corp"),
			(_runnerPreset, _runnerPresetIds, "runner"),
		})
		{
			string requested = $"system-gateway-{preset}-{side}";
			int[] matches = ids.Where(pair => pair.Value == requested).Select(pair => pair.Key).ToArray();
			if (matches.Length != 1 || button.IsItemDisabled(matches[0]))
				throw new InvalidOperationException($"Preset {requested} is unavailable.");
			button.Select(matches[0]);
		}
	}

	private async Task<CaptureBurst> CaptureBurstAsync(CaptureOptions options, int index, string kind, int actions)
	{
		string prefix = $"{index:D2}-{kind}";
		var frames = new List<Image>(options.Frames);
		var paths = new List<string>(options.Frames);
		for (int frame = 0; frame < options.Frames; frame++)
		{
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			Image image = GetViewport().GetTexture().GetImage();
			string path = Path.Combine(options.Output, $"{prefix}-{frame:D2}.png");
			Error saved = image.SavePng(path);
			if (saved != Error.Ok) throw new IOException($"Could not save {path}: {saved}");
			frames.Add(image);
			paths.Add(Path.GetFileName(path));
			await ToSignal(GetTree().CreateTimer(options.Interval), SceneTreeTimer.SignalName.Timeout);
		}

		string sheet = $"{prefix}-sheet.png";
		Error sheetSaved = ContactSheet(frames).SavePng(Path.Combine(options.Output, sheet));
		if (sheetSaved != Error.Ok) throw new IOException($"Could not save {sheet}: {sheetSaved}");
		return new CaptureBurst(index, kind, actions, _coordinator?.CurrentSnapshot?.View.Turn.TurnNumber, sheet, paths.ToArray());
	}

	private static Image ContactSheet(List<Image> frames)
	{
		int tileWidth = frames[0].GetWidth() / CaptureSheetColumns;
		int tileHeight = frames[0].GetHeight() / CaptureSheetColumns;
		int rows = (frames.Count + CaptureSheetColumns - 1) / CaptureSheetColumns;
		Image sheet = Image.CreateEmpty(
			CaptureSheetColumns * tileWidth + (CaptureSheetColumns - 1) * CaptureSheetGap,
			rows * tileHeight + (rows - 1) * CaptureSheetGap,
			false,
			Image.Format.Rgb8);
		sheet.Fill(Colors.White);
		for (int index = 0; index < frames.Count; index++)
		{
			var tile = (Image)frames[index].Duplicate();
			tile.Convert(Image.Format.Rgb8);
			tile.Resize(tileWidth, tileHeight, Image.Interpolation.Bilinear);
			sheet.BlitRect(tile, new Rect2I(0, 0, tileWidth, tileHeight), new Vector2I(
				index % CaptureSheetColumns * (tileWidth + CaptureSheetGap),
				index / CaptureSheetColumns * (tileHeight + CaptureSheetGap)));
		}

		return sheet;
	}
}
