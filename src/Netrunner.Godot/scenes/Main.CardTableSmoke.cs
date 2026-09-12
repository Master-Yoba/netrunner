using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private async Task RunCardTableSmokeAsync()
	{
		try
		{
			bool screenshots = (OS.GetCmdlineUserArgs().Contains("--m4-9-screenshots", StringComparer.Ordinal) || OS.GetCmdlineUserArgs().Contains("--m4-10-screenshots", StringComparer.Ordinal));
			bool requireOfficial = OS.GetCmdlineUserArgs().Contains("--require-official-content", StringComparer.Ordinal);
			// An action prompt can request a name before the first board render.
			_gameTable.GetServerDisplayName("server:4");
			if (requireOfficial)
			{
				foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
				{
					using var back = new TableCardControl();
					back.SetBackSide(side);
					if (!back.HasCardBackTexture || back.BackSide != side)
						throw new InvalidOperationException($"Missing local {side} card back.");
					back.Free();
				}
			}
			if (requireOfficial && (!_metadataSnapshot.IsOfficial || _metadataSnapshot.CardCount != 77))
				throw new InvalidOperationException("The M4.9 provisioned gate requires 77 official card designs.");
			GetWindow().Size = new Vector2I(1920, 1080);
			GetWindow().ContentScaleSize = new Vector2I(1920, 1080);
			await TableFrames(3);
			if (screenshots) await SaveTableEvidence("setup-1920");
			if (_metadataSnapshot.IsOfficial)
			{
				_seedInput.Text = "47042";
				_humanSide.Select(0);
				_startButton.EmitSignal(BaseButton.SignalName.Pressed);
				_reducedMotion.ButtonPressed = true;
				for (int choice = 0; choice < 12; choice++)
				{
					Observation observation = await SettledTableObservationAsync();
					if (_currentSnapshot!.View.Turn.ActiveSide == PlayerSide.Runner) break;
					LegalAction action = observation.LegalActions.First(a => a.Kind == ActionKind.PassPriority
						|| a.Parameters is NumberChoiceParameters { Value: 0 });
					if (!_tableActionButtons.TryGetValue(action.Id, out Button? button))
						throw new InvalidOperationException("Opening-hand/priority action missing from default dock.");
					await ClickTableButton(button);
				}
				await TableFrames(3);
				if (_tableHistory.Entries.Count(e => e.Summary.Contains("replaces a card", StringComparison.Ordinal)) != 2)
					throw new InvalidOperationException("Nico replacement history is incomplete.");
				if (screenshots) await SaveTableEvidence("nico-table");
				AssertCentralServerPresentation(_currentSnapshot!.View);
				_matchHistory.SetExpanded(true);
				await TableFrames(3);
				if (screenshots) await SaveTableEvidence("nico-history");
				for (int priority = 0; priority < 8 && _currentSnapshot!.View.Decision?.Kind != DecisionKind.TurnAction; priority++)
				{
					Observation current = await SettledTableObservationAsync();
					LegalAction? pass = current.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority);
					if (pass is null) break;
					await ClickTableButton(_tableActionButtons[pass.Id]);
				}
				await SettledTableObservationAsync();
				if (_tableHistory.Entries.Any(entry => entry.Summary.Contains("passes priority", StringComparison.Ordinal)))
					throw new InvalidOperationException("Pass-priority chatter remains in the visible history.");
				LegalActionViewModel eventPlay = _currentSnapshot!.View.LegalActions.First(action => action.Parameters is PlayCardParameters);
				string playableId = ((PlayCardParameters)eventPlay.Parameters).CardId.Value;
				if (!_gameTable.VisibleCards.TryGetValue(playableId, out TableCardControl? playable)
					|| !playable.IsActionableSource
					|| _gameTable.VisibleCards.Values.Any(card => card.Card?.IsKnown != true && card.IsActionableSource))
					throw new InvalidOperationException("Issued playable-card glow is missing or reveals hidden cards.");
				if (screenshots) await SaveTableEvidence("actionable-cards-1920");
				RenderCardTableActions(_currentSnapshot with { IsOpponentThinking = true });
				if (_gameTable.VisibleCards.Values.Any(card => card.IsActionableSource || card.IsTargetCandidate))
					throw new InvalidOperationException("Card action cues remained active during opponent thinking.");
				RenderCardTableActions(_currentSnapshot);
				SelectTableCard(((PlayCardParameters)eventPlay.Parameters).CardId.Value);
				if (!_tableActionButtons.ContainsKey(eventPlay.Id)) throw new InvalidOperationException("Selecting a hand event did not offer its issued play action.");
				string rdId = _currentSnapshot.View.Servers.Single(server => server.Kind == ServerKind.RAndD).Id;
				LegalActionViewModel basicRun = _currentSnapshot.View.LegalActions.First(action => action.Parameters is RunServerParameters target && target.ServerId.Value == rdId);
				string laneName = _gameTable.GetServerDisplayName(((RunServerParameters)basicRun.Parameters).ServerId.Value);
				Button laneButton = FindDescendants<Button>(_gameTable).First(button => button.Text == laneName);
				await ClickTableButton(laneButton);
				if (!_tableActionButtons.TryGetValue(basicRun.Id, out Button? runButton))
					throw new InvalidOperationException("Selecting a server did not offer its issued run action.");
				await ClickTableButton(runButton);
				await _coordinator!.GetObservationAsync(PlayerSide.Runner);
				await TableFrames(3);
				if (_currentSnapshot.View.Run is null) throw new InvalidOperationException("Contextual run action did not start its run.");
				bool sawAccess = false;
				for (int step = 0; step < 120 && _currentSnapshot.View.Run is not null; step++)
				{
					await SettledTableObservationAsync();
					if (_currentSnapshot.View.Run is null) break;
					if (_currentSnapshot.IsOpponentThinking || Volatile.Read(ref _submissionInFlight) != 0) continue;
					if (_stagedAction is not null)
					{
						await ClickTableButton(FindDescendants<Button>(_tableActions).Single(button => button.Text == "Confirm"));
						continue;
					}
					LegalActionViewModel next = _currentSnapshot.View.LegalActions.First(action => action.Kind is
						ActionKind.ContinueRun or ActionKind.PassPriority or ActionKind.ChooseBreachCandidate or ActionKind.NoAccessAction);
					if (next.Kind == ActionKind.NoAccessAction)
					{
						CardViewModel? accessed = _currentSnapshot.View.Run.Access.Accessed[^1];
						if (accessed is not { IsKnown: true } || !_tableAccessPanel.Visible
							|| _tableAccessCard.Card?.Id != accessed.Id || _tableAccessTitle.Text != accessed.Title
							|| !_tableHistory.Entries.Any(entry => entry.Summary.Contains($"accesses {accessed.Title}", StringComparison.Ordinal)))
							throw new InvalidOperationException("R&D access card or named history missing from the table.");
						if (_tableActionButtons[next.Id].Text != "Continue access")
							throw new InvalidOperationException("Access continuation has an unclear label.");
						GD.Print($"M49_ACCESS_READY action_count={_currentSnapshot.ActionCount}");
						if (screenshots) await SaveTableEvidence("rd-access");
						sawAccess = true;
					}
					await ClickTableButton(_tableActionButtons[next.Id]);
				}
				await _coordinator.GetObservationAsync(PlayerSide.Runner);
				await TableFrames(3);
				if (!sawAccess || _currentSnapshot.View.Run is not null || _tableAccessPanel.Visible)
					throw new InvalidOperationException("R&D access did not finish and clear its panel.");
				GD.Print("M49_RD_ACCESS PASS seed=47042 visible_card=true named_history=true cleared=true");
				GD.Print("M49_CONTEXT PASS event_from_hand=true run_from_server=true");
				await AssertHoverStable("live-1920");
				_coordinator!.SnapshotPublished -= QueueSnapshot;
				_coordinator.Dispose();
				_coordinator = null;
				CloseFullMatchDiagnostics("m4-9-official-complete");
				GD.Print("M49_NICO PASS mouse=true replacements=2 hidden_install=true");
			}
			(CardViewModel[] sampleHand, IReadOnlyList<ServerViewModel> sampleServers, CardViewModel[] sampleRig) = CreateSlice48Board();
			CardViewModel[] hand = Enumerable.Range(0, 10).Select(i => sampleHand[i % sampleHand.Length] with { Id = $"table-hand-{i}" }).ToArray();
			CardViewModel[] rig = Enumerable.Range(0, 12).Select(i => sampleRig[i % sampleRig.Length] with { Id = $"table-rig-{i}" }).ToArray();
			ServerViewModel[] servers = Enumerable.Range(0, 6).Select(i => sampleServers[i % sampleServers.Count] with
			{
				Id = $"table-remote-{i}",
				Ice = Enumerable.Range(0, i == 0 ? 5 : 2).Select(j => j == 1 ? null :
					sampleServers[0].Ice[0]! with { Id = $"table-ice-{i}-{j}" }).ToArray(),
				Root = [sampleServers[0].Root[0]! with { Id = $"table-root-{i}" }],
			}).ToArray();
			rig[0] = rig[0] with { HostCardId = "table-ice-0-0" };
			DecisionViewModel decision = new("table-decision", 1, new PlayerId("table-player"),
				PlayerSide.Runner, DecisionKind.BehaviorChoice, "Choose a server, then choose a card.");
			LegalActionViewModel option = new(new ActionId("table-option"), ActionKind.ChooseBehavior,
				"Choose server", new BehaviorChoiceParameters("table-choice", "server"));
			MatchViewModel fixture = CreateSlice48View(hand, servers, rig, decision, [option], null,
				new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null));
			DecisionViewModel toyDecision = decision with { Kind = DecisionKind.ToyTurn };
			LegalActionViewModel toyAction = new(new ActionId("table-toy-action"), ActionKind.GainCredit, "Gain credit", new NoActionParameters());
			RenderSnapshot(CreateSlice48Snapshot(fixture with { Decision = toyDecision, LegalActions = [toyAction] }, toyDecision));
			if (!_tableActionButtons.ContainsKey(toyAction.Id)) throw new InvalidOperationException("Ungrouped issued action was omitted from the table dock.");
			var history = Enumerable.Range(1, 2000).Select(i => new MatchHistoryEntry($"stress-{i}", i, i / 10,
				i % 2 == 0 ? PlayerSide.Corp : PlayerSide.Runner, $"Player action {i}", ["Retained detail."])).ToArray();
			_matchHistory.ClearHistory();
			_matchHistory.Append(history);
			_matchHistory.Append(history);
			if (!_matchHistory.ExportText().Contains("Player action 1", StringComparison.Ordinal)
				|| !_matchHistory.ExportText().Contains("Player action 2000", StringComparison.Ordinal))
				throw new InvalidOperationException("Long-session history was truncated.");
			_matchHistory.SetExpanded(true);
			await TableFrames(2);
			_matchHistory.JumpToOldest();
			await TableFrames(2);
			int oldestPage = _matchHistory.PageStart;
			var appendedHistory = Enumerable.Range(2001, 3).Select(i => new MatchHistoryEntry(
				$"stress-{i}",
				i,
				i / 10,
				PlayerSide.Runner,
				$"Player action {i}",
				["Appended while the reader was browsing older history."])).ToArray();
			_matchHistory.Append(appendedHistory);
			await TableFrames(2);
			if (_matchHistory.PageStart != oldestPage || _matchHistory.NewEntryCount != appendedHistory.Length)
				throw new InvalidOperationException("History moved the reader or lost the unread-entry count while browsing older entries.");
			_matchHistory.JumpToLatest();
			await TableFrames(2);
			if (_matchHistory.PageStart != Math.Max(0, _matchHistory.EntryCount - _matchHistory.PageSize)
				|| _matchHistory.NewEntryCount != 0)
				throw new InvalidOperationException("History did not follow the newest page or clear unread entries.");
			foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
			{
				GetWindow().Size = size;
				GetWindow().ContentScaleSize = size;
				foreach (PlayerSide side in new[] { PlayerSide.Runner, PlayerSide.Corp })
				{
					CardViewModel[] corpHand = Enumerable.Range(0, 10).Select(i => CreateSlice48ContentCard(
						$"table-hand-{i}", "Operation", "Corp operation", "Gain credits.", side: "corp")).ToArray();
					MatchViewModel view = fixture with
					{
						Viewer = side,
						CorpHand = new ZoneViewModel(ZoneKind.CorpHand, 10, side == PlayerSide.Corp ? corpHand : []),
						RunnerHand = new ZoneViewModel(ZoneKind.RunnerGrip, 10, side == PlayerSide.Runner ? hand : []),
						Decision = decision with { OwnerSide = side },
					};
					RenderSnapshot(CreateSlice48Snapshot(view, view.Decision));
					_matchHistory.SetExpanded(false);
					await TableFrames(4);
					AssertCentralServerPresentation(view);
					AssertBoardCardAlignment(view);
					await AssertHoverStable($"fixture-{size.X}-{side}");
					Rect2 viewport = GetViewport().GetVisibleRect();
					if (!viewport.Encloses(_tableActions.GetGlobalRect())
						|| !viewport.Encloses(_gameTable.GetGlobalRect()))
						throw new InvalidOperationException($"Table/dock escaped viewport at {size} ({side}).");
					int visibleHand = _gameTable.VisibleCards.Values.Count(c => c.Card?.Id.StartsWith("table-hand-", StringComparison.Ordinal) == true
						&& _gameTable.GetGlobalRect().Encloses(c.GetGlobalRect()));
					int visibleRig = _gameTable.VisibleCards.Values.Count(c => c.Card?.Id.StartsWith("table-rig-", StringComparison.Ordinal) == true
						&& _gameTable.GetGlobalRect().Encloses(c.GetGlobalRect()));
					if (visibleHand < 3 || visibleRig < 2)
						throw new InvalidOperationException($"Playable hand/rig is clipped: hand={visibleHand}, rig={visibleRig} at {size}.");
					TableCardControl card = _gameTable.VisibleCards.Values.First(c => c.IsVisibleInTree() && c.Card is { IsKnown: true });
					ulong node = card.GetInstanceId();
					_gameTable.Render(view);
					if (!_gameTable.VisibleCards.Values.Any(c => c.GetInstanceId() == node))
						throw new InvalidOperationException("Unchanged board rebuilt its card nodes.");
					card.GrabFocus();
					Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true });
					await TableFrames(2);
					Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = false });
					if (!card.IsSelected) throw new InvalidOperationException("Keyboard card selection failed.");
					TableCardControl[] handControls = _gameTable.VisibleCards.Values
						.Where(control => control.Card?.Id.StartsWith("table-hand-", StringComparison.Ordinal) == true)
						.GroupBy(control => control.Card!.Id, StringComparer.Ordinal)
						.Select(group => group.First())
						.OrderBy(control => control.Card!.Id, StringComparer.Ordinal)
						.ToArray();
					TableCardControl[] rigControls = _gameTable.VisibleCards.Values
						.Where(control => control.Card?.Id.StartsWith("table-rig-", StringComparison.Ordinal) == true)
						.GroupBy(control => control.Card!.Id, StringComparer.Ordinal)
						.Select(group => group.First())
						.OrderBy(control => control.Card!.Id, StringComparer.Ordinal)
						.ToArray();
					if (handControls.Length < 10 || rigControls.Length < 12)
						throw new InvalidOperationException($"Dense overflow fixture lost cards: hand={handControls.Length}, rig={rigControls.Length}.");
					await AssertOverflowFocus(handControls, "local hand");
					await AssertOverflowFocus(rigControls, "runner rig");
					TableCardControl hidden = _gameTable.VisibleCards.Values.First(control => control.Card is null);
					hidden.GrabFocus();
					await TableFrames(2);
					if (_inspectedCard is not null || !_tableInspect.Disabled)
						throw new InvalidOperationException("Concealed focus retained a previous known-card inspector.");
					_inspectedCard = card.Card;
					ShowInspectedCardZoom();
					await TableFrames(1);
					if (!_zoomOverlay.Visible) throw new InvalidOperationException("Card zoom failed.");
					HideCardZoom();
					if (screenshots) await SaveTableEvidence($"dense-{side.ToString().ToLowerInvariant()}-{size.X}");
					if (size.X == 1920 && side == PlayerSide.Runner)
					{
						CardViewModel outgoing = view.Servers[0].Root.OfType<CardViewModel>().First();
						ServerViewModel[] replaced = view.Servers.Select((server, index) => index == 0 ? server with { Root = [null] } : server).ToArray();
						_gameTable.MotionEnabled = true;
						_gameTable.Render(view with { Servers = replaced, CorpDiscard = [outgoing] });
						await TableFrames(3);
						if (_gameTable.ActiveMotionCount == 0) throw new InvalidOperationException("Replacement movement was not presented.");
						_gameTable.StopMotion();
						if (_gameTable.ActiveMotionCount != 0) throw new InvalidOperationException("Skip failed to cancel visual motion.");
						RenderSnapshot(CreateSlice48Snapshot(view, view.Decision));
						await TableFrames(3);
						GD.Print("M49_MOTION PASS replacement=true skip=true");
					}
					_matchHistory.SetExpanded(true);
					_matchHistory.SetPinned(size.X >= 1800);
					await TableFrames(2);
					if (size.X >= 1800 && _gameTable.GetGlobalRect().End.X > _matchHistory.GetGlobalRect().Position.X)
						throw new InvalidOperationException("Pinned history overlaps the large table.");
					if (!viewport.Encloses(_matchHistory.GetGlobalRect()))
						throw new InvalidOperationException("Expanded history escaped the viewport.");
					if (screenshots) await SaveTableEvidence($"history-{side.ToString().ToLowerInvariant()}-{size.X}");

					CardViewModel runIce = servers[0].Ice.OfType<CardViewModel>().First();
					RunFocusViewModel activeRun = CreateSlice48Run(runIce) with
					{
						AttackedServerId = servers[0].Id,
						CurrentIce = runIce,
					};
					CardViewModel corpDiscardCard = CreateSlice48ContentCard(
						$"table-discard-{side}-{size.X}",
						"Agenda",
						"Discarded agenda",
						"A public discard fixture.",
						side: "corp");
					CardViewModel runnerHeapCard = CreateSlice48ContentCard(
						$"table-heap-{side}-{size.X}",
						"Event",
						"Discarded event",
						"A public heap fixture.",
						side: "runner");
					MatchViewModel activeRunView = view with
					{
						Run = activeRun,
						CorpDiscard = [corpDiscardCard],
						CorpDiscardCount = 3,
						RunnerHeap = [runnerHeapCard],
					};
					RenderSnapshot(CreateSlice48Snapshot(activeRunView, activeRunView.Decision));
					await TableFrames(3);
					AssertBoardCardAlignment(activeRunView);
					if (!SceneTreeContainsText(_gameTable, "▶")
						&& !SceneTreeContainsText(_gameTable, "ACTIVE RUN")
						&& !SceneTreeContainsText(_gameTable, "RUN ·"))
						throw new InvalidOperationException("Active-run table fixture did not expose its lane route.");
					if (screenshots) await SaveTableEvidence($"active-run-{side.ToString().ToLowerInvariant()}-{size.X}");

					EncounterViewModel brokenEncounter = activeRun.Encounter! with
					{
						Subroutines = activeRun.Encounter!.Subroutines.Select(sub => sub with { Broken = true }).ToArray(),
						EffectiveStrength = 99,
					};
					MatchViewModel brokenView = activeRunView with { Run = activeRun with { Encounter = brokenEncounter } };
					RenderSnapshot(CreateSlice48Snapshot(brokenView, brokenView.Decision));
					await TableFrames(3);
					if (!SceneTreeContainsText(_gameTable, "0 unbroken")
						|| !_gameTable.FindChildren("RunMarker", "Label", true, false).Cast<Label>()
							.Any(label => label.Visible && label.TooltipText.Contains("Strength 99", StringComparison.Ordinal)))
						throw new InvalidOperationException("Encounter-only change left the lane marker stale.");
					MatchViewModel discardView = activeRunView with { Run = null };
					RenderSnapshot(CreateSlice48Snapshot(discardView, discardView.Decision));
					await TableFrames(3);
					if (!SceneTreeContainsText(_gameTable, "Archives") || !SceneTreeContainsText(_gameTable, "Heap"))
						throw new InvalidOperationException("Discard/heap table fixture did not expose public pile labels.");
					if (screenshots) await SaveTableEvidence($"discard-{side.ToString().ToLowerInvariant()}-{size.X}");
					if (!ShowTablePile("zone:corp-archives") || _tablePileCards.GetChildCount() == 0)
						throw new InvalidOperationException("Public pile browser failed to open.");
					await TableFrames(2);
					TableCardControl[] archiveCards = _tablePileCards.GetChildren().OfType<TableCardControl>().ToArray();
					if (archiveCards.Length != 3 || archiveCards.Count(card => card.Card is null) != 2
						|| !SceneTreeContainsText(_gameTable, "Archives · 3"))
						throw new InvalidOperationException("Archives total or concealed placeholders were lost.");
					if (screenshots && size.X == 1920 && side == PlayerSide.Runner) await SaveTableEvidence("archives-browser-1920");
					HideTablePile();

					MatchViewModel outcomeView = discardView with
					{
						Decision = null,
						LegalActions = Array.Empty<LegalActionViewModel>(),
						Outcome = new OutcomeViewModel(GameOutcomeKind.Won, PlayerSide.Runner, GameWinReason.AgendaPoints),
					};
					RenderSnapshot(CreateSlice48Snapshot(outcomeView, null));
					await TableFrames(3);
					if (!_tablePrompt.Text.Contains("Match complete", StringComparison.Ordinal)
						|| !SceneTreeContainsText(_focusPresentation, "New match"))
						throw new InvalidOperationException("Outcome fixture did not expose a restart path.");
					if (screenshots) await SaveTableEvidence($"outcome-{side.ToString().ToLowerInvariant()}-{size.X}");
					GD.Print($"M49_TABLE size={size.X}x{size.Y} side={side} layout=true keyboard=true zoom=true stable_nodes=true history=2000");
				}
			}
			var frameTimes = new List<double>(120);
			long allocationStart = GC.GetTotalAllocatedBytes(false);
			for (int frame = 0; frame < 120; frame++)
			{
				long started = System.Diagnostics.Stopwatch.GetTimestamp();
				await TableFrames(1);
				frameTimes.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
			}
			long idleAllocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(false) - allocationStart);
			frameTimes.Sort();
			GD.Print($"M49_PERF frames=120 median_ms={frameTimes[60]:F2} p95_ms={frameTimes[114]:F2} renderer={RenderingServer.GetVideoAdapterName()}");
			GD.Print($"M410_ALLOC frames=120 allocated_bytes={idleAllocatedBytes} bytes_per_frame={idleAllocatedBytes / 120.0:F2}");
			GD.Print($"M49_UI PASS official={_metadataSnapshot.IsOfficial} dense=true history=true");
			GetTree().Quit();
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			GD.Print("M49_UI FAIL " + exception.Message);
			GetTree().Quit(1);
		}
	}

	private async Task AssertOverflowFocus(
		IReadOnlyList<TableCardControl> controls,
		string region)
	{
		TableCardControl last = controls[^1];
		last.GrabFocus();
		await TableFrames(4);
		if (!last.HasFocus())
			throw new InvalidOperationException($"{region} overflow did not retain keyboard focus on its last card.");
		if (!_gameTable.GetGlobalRect().Intersects(last.GetGlobalRect()))
			throw new InvalidOperationException($"{region} overflow focus target remained outside the table viewport.");
	}

	private async Task ClickTableButton(Button button)
	{
		Vector2 point = button.GetGlobalRect().GetCenter();
		Input.ParseInputEvent(new InputEventMouseButton
		{
			ButtonIndex = MouseButton.Left,
			Pressed = true,
			Position = point,
			GlobalPosition = point
		});
		await TableFrames(1);
		Input.ParseInputEvent(new InputEventMouseButton
		{
			ButtonIndex = MouseButton.Left,
			Pressed = false,
			Position = point,
			GlobalPosition = point
		});
		await TableFrames(3);
	}

	private async Task<Observation> SettledTableObservationAsync()
	{
		Observation observation = await _coordinator!.GetObservationAsync(PlayerSide.Runner)
			.WaitAsync(TimeSpan.FromSeconds(10));
		for (int frame = 0; frame < 180; frame++)
		{
			await TableFrames(1);
			if (_currentSnapshot?.View.Decision?.Id == observation.Decision?.Id.Value
				&& _tablePublications.Count == 0 && Volatile.Read(ref _submissionInFlight) == 0)
			{
				if (observation.LegalActions is [{ Kind: ActionKind.PassPriority }])
					throw new InvalidOperationException("A sole human Pass Priority decision did not advance automatically.");
				return observation;
			}
		}
		throw new InvalidOperationException("The displayed decision did not settle after automatic actions.");
	}

	private async Task TableFrames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	// The inspect button toggles between the disabled and enabled theme
	// styleboxes on hover; both must keep the dock and table at a stable size.
	private async Task AssertHoverStable(string tag)
	{
		TableCardControl? known = _gameTable.VisibleCards.Values
			.FirstOrDefault(c => c.IsInsideTree() && c.Card is { IsKnown: true } && c.Card!.Title.Length >= 10);
		TableCardControl? hidden = _gameTable.VisibleCards.Values
			.FirstOrDefault(c => c.IsInsideTree() && c.Card is null);
		if (known is null || hidden is null) return;

		Vector2 baseTable = _gameTable.Size;
		Rect2 sourceRect = known.GetGlobalRect();
		known.EmitSignal(Control.SignalName.MouseEntered);
		await TableFrames(6);
		Vector2 knownTable = _gameTable.Size;
		if (!CardHoverPreviewVisible || CardHoverPreviewCardId != known.CardId
			|| !GetViewport().GetVisibleRect().Encloses(CardHoverPreviewGlobalRect)
			|| sourceRect != known.GetGlobalRect())
			throw new InvalidOperationException($"Known-card hover preview invalid at {tag}: visible={CardHoverPreviewVisible} id={CardHoverPreviewCardId}/{known.CardId} rect={CardHoverPreviewGlobalRect} source={sourceRect}/{known.GetGlobalRect()}.");
		if ((OS.GetCmdlineUserArgs().Contains("--m4-9-screenshots", StringComparer.Ordinal) || OS.GetCmdlineUserArgs().Contains("--m4-10-screenshots", StringComparer.Ordinal)))
			await SaveTableEvidence($"hover-{tag}");
		known.EmitSignal(Control.SignalName.MouseExited);
		known.ReleaseFocus();
		hidden.EmitSignal(Control.SignalName.MouseEntered);
		await TableFrames(6);
		Vector2 hiddenTable = _gameTable.Size;
		if (CardHoverPreviewVisible || CardHoverPreviewCardId is not null)
			throw new InvalidOperationException($"Hidden-card hover retained a known preview at {tag}.");
		hidden.EmitSignal(Control.SignalName.MouseExited);
		known.GrabFocus();
		await TableFrames(3);
		if (!CardHoverPreviewVisible || CardHoverPreviewCardId != known.CardId)
			throw new InvalidOperationException($"Keyboard focus did not show the card preview at {tag}.");
		CardViewModel? savedCard = known.Card;
		known.SetCard(null);
		await TableFrames(2);
		if (CardHoverPreviewVisible || CardHoverPreviewCardId is not null)
			throw new InvalidOperationException($"Concealing the previewed card did not clear its face at {tag}.");
		known.SetCard(savedCard);
		known.ReleaseFocus();
		await TableFrames(6);
		if (knownTable != baseTable || hiddenTable != baseTable)
			throw new InvalidOperationException($"Hovering a card resizes the table at {tag}: base={baseTable} known={knownTable} hidden={hiddenTable}.");
	}

	private void AssertCentralServerPresentation(MatchViewModel view)
	{
		ServerViewModel? hq = view.Servers.FirstOrDefault(server => server.Kind == ServerKind.Headquarters);
		if (hq is null) return; // The dense layout fixture contains only remotes.
		string[] expected = ["Archives", "R&D", "HQ"];
		Button[] headers = FindDescendants<Button>(_gameTable)
			.Where(button => expected.Contains(button.Text, StringComparer.Ordinal))
			.OrderBy(button => button.GlobalPosition.X).ToArray();
		if (!headers.Select(button => button.Text).SequenceEqual(expected))
			throw new InvalidOperationException("Central server labels or left-to-right order are incorrect.");
		if (_gameTable.GetServerDisplayName(hq.Id) != "HQ"
			|| _gameTable.VisibleCards.Keys.Any(key => key.StartsWith($"slot:pile:{hq.Id}:", StringComparison.Ordinal)))
			throw new InvalidOperationException("HQ has a remote label or decorative hand backs.");
		if (!FindDescendants<Label>(_gameTable).Any(label => label.Text == $"ROOT · HQ · {view.CorpHand.Count} cards in hand"))
			throw new InvalidOperationException("HQ public hand count is missing.");
	}

	private void AssertBoardCardAlignment(MatchViewModel view)
	{
		IEnumerable<CardViewModel> hand = view.Viewer == PlayerSide.Runner
			? view.RunnerHand.VisibleCards : view.CorpHand.VisibleCards;
		var runnerCards = hand.Concat(view.RunnerRig.Where(card => string.IsNullOrEmpty(card.HostCardId)))
			.Concat(view.RunnerIdentity is { } identity ? [identity] : [])
			.Where(card => _gameTable.VisibleCards.ContainsKey(card.Id))
			.Select(card => _gameTable.VisibleCards[card.Id].GetGlobalRect()).ToArray();
		if (runnerCards.Length > 1 && runnerCards.Max(rect => rect.Position.Y) - runnerCards.Min(rect => rect.Position.Y) > 1)
			throw new InvalidOperationException($"Runner card tops are misaligned: {string.Join(", ", runnerCards.Select(rect => rect.Position.Y))}.");

		HBoxContainer[] roots = _gameTable.FindChildren("RootCards", "HBoxContainer", true, false)
			.Cast<HBoxContainer>().Where(row => row.IsVisibleInTree()).ToArray();
		float? rootTop = null;
		float? iceTop = null;
		foreach (HBoxContainer root in roots)
		{
			ScrollContainer iceScroll = root.GetParent().GetNode<ScrollContainer>("IceScroll");
			rootTop ??= root.GlobalPosition.Y;
			iceTop ??= iceScroll.GlobalPosition.Y;
			if (Mathf.Abs(root.GlobalPosition.Y - rootTop.Value) > 1 || Mathf.Abs(iceScroll.GlobalPosition.Y - iceTop.Value) > 1)
				throw new InvalidOperationException("Server root/ICE rows shifted between lanes.");
			Rect2[] cards = root.GetChildren().OfType<TableCardControl>().Where(card => card.Visible)
				.Select(card => card.GetGlobalRect()).ToArray();
			float center = root.GetGlobalRect().GetCenter().X;
			if (cards.Length > 0 && Mathf.Abs((cards.Min(rect => rect.Position.X) + cards.Max(rect => rect.End.X)) / 2 - center) > 1)
				throw new InvalidOperationException("Server root cards are off the lane centerline.");
			foreach (TableCardControl ice in FindDescendants<TableCardControl>(iceScroll).Where(card => card.IsIce))
				if (Mathf.Abs(ice.GetGlobalRect().GetCenter().X - center) > 1)
					throw new InvalidOperationException($"ICE is off the root centerline: {ice.GetGlobalRect().GetCenter().X} vs {center}.");
		}
	}

	private async Task SaveTableEvidence(string name)
	{
		// The headless display server never emits frame_post_draw, so sync on
		// main-loop frames instead of waiting for a render callback that would
		// block the smoke forever.
		if (DisplayServer.GetName() != "headless")
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		else
			await TableFrames(2);
		string milestone = OS.GetCmdlineUserArgs().Contains("--m4-10-screenshots", StringComparer.Ordinal) ? "m4-10" : "m4-9";
		string directory = ProjectSettings.GlobalizePath($"res://../../docs/evidence/{milestone}");
		Directory.CreateDirectory(directory);
		using Image frame = GetViewport().GetTexture().GetImage();
		if (frame.SavePng(Path.Combine(directory, name + ".png")) != Error.Ok)
			throw new IOException("Could not save native table evidence.");
	}
}
