using Godot;
using Netrunner.Cards;
using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private string? _artworkSmokeDirectory;

	private void PrepareCardArtworkSmoke()
	{
		_artworkSmokeDirectory = Path.Combine(Path.GetTempPath(), "netrunner-art-smoke-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_artworkSmokeDirectory);
		using Image image = Image.CreateEmpty(150, 210, false, Image.Format.Rgba8);
		image.Fill(new Color("dce7f4"));
		image.FillRect(new Rect2I(0, 0, 150, 24), new Color("185a8c"));
		image.FillRect(new Rect2I(8, 30, 134, 100), new Color("df785b"));
		for (int y = 145; y < 195; y += 12) image.FillRect(new Rect2I(12, y, 115, 4), new Color("253b54"));
		byte[] bytes = image.SavePngToBuffer();
		string hash = CardArtworkCache.Hash(bytes);
		string path = $"images/{hash}.png";
		CardArtworkCache.WriteAtomically(CardArtworkCache.SafePath(_artworkSmokeDirectory, path), bytes);
		var entries = CardArtworkCache.GatewayCodes.Select(code => new CardArtworkEntry(code, code, path,
			$"https://example.invalid/synthetic/{code}.png", "Generated geometric fixture",
			DateTimeOffset.UnixEpoch, bytes.Length, hash)).ToArray();
		// A checksum mismatch and a missing file exercise fallback without decoding invalid data.
		string corruptHash = new('b', 64);
		entries[^2] = entries[^2] with { Path = $"images/{corruptHash}.png", Sha256 = corruptHash };
		CardArtworkCache.WriteAtomically(CardArtworkCache.SafePath(_artworkSmokeDirectory, entries[^2].Path), bytes);
		string missingHash = new('c', 64);
		entries[^1] = entries[^1] with { Path = $"images/{missingHash}.png", Sha256 = missingHash };
		CardArtworkCache.WriteAtomically(CardArtworkCache.SafePath(_artworkSmokeDirectory, "manifest.json"),
			CardArtworkCache.Serialize(new CardArtworkManifest(1, "system_gateway", "en", entries)));
		System.Environment.SetEnvironmentVariable(CardArtworkCache.DirectoryVariable, _artworkSmokeDirectory);
	}

	private async Task RunCardArtworkSmokeAsync(bool liveCache)
	{
		try
		{
			if (liveCache) CardArtworkCache.Validate(CardArtworkCache.ResolveDirectory());
			if (_startPanel.GetParent() is CanvasItem setup) setup.Hide();
			_status.Text = "Artwork cache verification";
			_phase.Text = "ARTWORK";
			var row = new HBoxContainer { Position = new Vector2(180, 240) };
			row.AddThemeConstantOverride("separation", 40);
			AddChild(row);
			TableCardControl portrait = TableCardControl.Create();
			TableCardControl ice = TableCardControl.Create();
			TableCardControl concealed = TableCardControl.Create();
			TableCardControl fallback = TableCardControl.Create();
			foreach (TableCardControl control in new[] { portrait, ice, concealed, fallback }) row.AddChild(control);
			var card = new CardViewModel("art-smoke-card", "30030", "Artwork test card", "Event", true,
				false, 2, 5, null, null, null, [], "Current rules remain available in inspection.");
			foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
			{
				GetWindow().Size = size;
				GetWindow().ContentScaleSize = size;
				portrait.SetDisplaySize(new Vector2(300, 420));
				portrait.SetCard(card);
				portrait.SetActionableSource(true);
				portrait.SetSelected(true);
				ice.SetIce(true);
				ice.SetDisplaySize(new Vector2(420, 300));
				ice.SetCard(card with { Id = "art-smoke-ice", DefinitionId = "30038", Type = "ICE", Rezzed = true });
				ice.SetTargetCandidate(true);
				int loads = LocalCardArtworkResolver.Shared.LoadCount;
				concealed.SetBackSide(Netrunner.Core.PlayerSide.Corp);
				concealed.SetCard(card with { IsKnown = false, DefinitionId = "30001", Title = "Concealed identity" });
				RequireInteraction(!concealed.HasFullCardArtwork && LocalCardArtworkResolver.Shared.LoadCount == loads,
					"Concealed cards consulted the artwork cache.");
				portrait.SetCard(card);
				portrait.SetActionableSource(true);
				portrait.SetSelected(true);
				RequireInteraction(LocalCardArtworkResolver.Shared.LoadCount == loads, "Repeated snapshots decoded artwork again.");
				RequireInteraction(portrait.HasFullCardArtwork && ice.HasFullCardArtwork, "Full card artwork was not loaded.");
				fallback.SetCard(card with { Id = "missing-art", DefinitionId = "missing-synthetic-art", Title = "Text fallback" });
				RequireInteraction(!fallback.HasFullCardArtwork, "Unknown code unexpectedly loaded artwork.");
				await TableFrames(3);
				RequireInteraction(!FindDescendants<Label>(portrait).Any(label => label.IsVisibleInTree() && label.Text == card.Title),
					"Printed artwork is covered by a duplicate title.");
				RequireInteraction(FindDescendants<Label>(portrait).Any(label => label.IsVisibleInTree() && label.Text.Contains("ADV", StringComparison.Ordinal)),
					"Artwork hid advancement counters.");
				if (OS.GetCmdlineUserArgs().Contains("--card-art-screenshots")) await SaveArtworkEvidence($"faces-{size.X}", liveCache);
				_inspectedCard = ice.Card;
				ShowInspectedCardZoom();
				await TableFrames(3);
				RequireInteraction(_zoomArtwork.Visible && _zoomArtwork.Texture is not null && _zoomArtwork.Rotation == 0
					&& IsFullyInsideViewport(_zoomOverlay), "Upright inspection artwork is unavailable or outside the viewport.");
				if (OS.GetCmdlineUserArgs().Contains("--card-art-screenshots")) await SaveArtworkEvidence($"inspection-{size.X}", liveCache);
				HideCardZoom();
				RequireInteraction(_zoomArtwork.Texture is null, "Dismissed inspection retained artwork.");
				portrait.SetCard(card with { IsKnown = false });
				RequireInteraction(!portrait.HasFullCardArtwork, "Known-to-hidden transition retained artwork.");
				portrait.SetCard(null);
				RequireInteraction(!portrait.HasFullCardArtwork, "Recycled card retained artwork.");
			}
			foreach (string code in CardArtworkCache.GatewayCodes)
			{
				portrait.SetCard(card with { DefinitionId = code });
				RequireInteraction(portrait.HasFullCardArtwork == (liveCache || code is not ("30076" or "30077")),
					$"Unexpected artwork/fallback for {code}.");
			}
			row.QueueFree();
			await CheckArtworkBoardPreviewAsync(card);
			await CheckInteractionLabArtworkAsync(liveCache);
			await CheckSingleCardHoverAsync(liveCache);
			await CheckModalLayersAndEscapeAsync(liveCache);
			await CheckClickedCardHoverAsync(liveCache);
			GD.Print($"CARD_ART_SMOKE PASS live_cache={liveCache} cards=77 resolutions=2 portrait=true ice=true inspection=true hover=true single_hover=true hover_details=true hover_dismissal=true game_symbols=true modal_layers=true escape_selection=true overlays=true hidden_safe=true reuse=true fallback=true lab_scenarios=19 lab_draw=true");
			// Let the queued smoke input drain and release its C# wrappers before quitting:
			// quitting with them alive intermittently aborts Godot's shutdown (rc_owner).
			await TableFrames(5);
			GC.Collect();
			GC.WaitForPendingFinalizers();
			await TableFrames(2);
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PrintErr($"CARD_ART_SMOKE FAIL {error}");
			GetTree().Quit(1);
		}
		finally
		{
			if (_artworkSmokeDirectory is not null) Directory.Delete(_artworkSmokeDirectory, recursive: true);
		}
	}

	private async Task CheckArtworkBoardPreviewAsync(CardViewModel card)
	{
		(CardViewModel[] hand, IReadOnlyList<ServerViewModel> servers, CardViewModel[] rig) = CreateSlice48Board();
		CardViewModel previewIce = card with { Id = "art-preview-ice", DefinitionId = "30038", Type = "ICE", Rezzed = true };
		ServerViewModel[] boardServers = servers.Select((server, index) => index == 0
			? server with { Ice = new CardViewModel?[] { previewIce } } : server).ToArray();
		var decision = new DecisionViewModel("art-preview", 1, new Netrunner.Core.PlayerId("art-player"),
			Netrunner.Core.PlayerSide.Runner, Netrunner.Core.DecisionKind.TurnAction, "Inspect artwork");
		MatchViewModel view = CreateSlice48View(hand, boardServers, rig, decision, [], null,
			new OutcomeViewModel(Netrunner.Core.GameOutcomeKind.Ongoing, null, null));
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			RenderSnapshot(CreateSlice48Snapshot(view, decision));
			await TableFrames(3);
			TableCardControl source = _gameTable.VisibleCards[previewIce.Id];
			source.SetContainerHover(true);
			await TableFrames(2);
			RequireInteraction(CardHoverPreviewVisible && _cardHoverPreviewFace is { HasFullCardArtwork: true, IsIce: false },
				"Board ICE hover did not show upright artwork.");
			source.SetCard(previewIce with { IsKnown = false });
			RequireInteraction(!CardHoverPreviewVisible && !source.HasFullCardArtwork,
				"Concealing a board card retained its artwork preview.");
			_inspectedCard = previewIce;
			ShowInspectedCardZoom();
			RenderSnapshot(CreateSlice48Snapshot(view, decision));
			RequireInteraction(_zoomOverlay.Visible && _zoomArtwork.Texture is not null,
				"An unchanged visible card lost its inspection during a snapshot update.");
			MatchViewModel concealedView = view with
			{
				Servers = boardServers.Select((server, index) => index == 0
					? server with { Ice = new CardViewModel?[] { previewIce with { IsKnown = false } } } : server).ToArray(),
			};
			RenderSnapshot(CreateSlice48Snapshot(concealedView, decision));
			RequireInteraction(!_zoomOverlay.Visible && _zoomArtwork.Texture is null,
				"A new snapshot retained stale inspected artwork.");
		}
	}

	private async Task CheckInteractionLabArtworkAsync(bool liveCache)
	{
		OpenInteractionLab();
		_reducedMotion.ButtonPressed = true;
		foreach (Vector2I size in new[] { new Vector2I(1920, 1080), new Vector2I(2560, 1440) })
		{
			GetWindow().Size = size;
			GetWindow().ContentScaleSize = size;
			foreach (int scenario in Enumerable.Range(0, InteractionLabSamples.Names.Length))
			{
				_labScenario = scenario;
				ResetInteractionLab();
				await TableFrames(2);
				RequireInteraction(_gameTable.VisibleCards.Values.Where(card => card.Card?.IsKnown == true)
					.All(card => card.HasFullCardArtwork), $"Lab scenario {scenario} did not resolve known card artwork.");
			}
			foreach (int scenario in new[] { 15, 16 })
			{
				_labScenario = scenario;
				ResetInteractionLab();
				MatchViewModel view = _currentSnapshot!.View;
				SubmitInteractionLab(view.Decision!, view.LegalActions.Single(action => action.Kind == Netrunner.Core.ActionKind.DrawCard));
				await TableFrames(3);
				RequireInteraction(_gameTable.VisibleCards[$"lab-drawn-{_labRevision}"].HasFullCardArtwork,
					"A newly drawn lab card did not resolve artwork.");
				if (OS.GetCmdlineUserArgs().Contains("--card-art-screenshots") && size.X == 1920)
					await SaveArtworkEvidence($"lab-{(scenario == 15 ? "runner" : "corp")}-{size.X}", liveCache);
			}
		}
		_labScenario = 0;
		ResetInteractionLab();
		MatchViewModel known = _currentSnapshot!.View;
		CardViewModel hidden = known.RunnerHand.VisibleCards[0] with { IsKnown = false, DefinitionId = null };
		int loads = LocalCardArtworkResolver.Shared.LoadCount;
		PublishLabView(known with { RunnerHand = known.RunnerHand with { VisibleCards = [hidden] } });
		RequireInteraction(_currentSnapshot!.View.RunnerHand.VisibleCards[0].DefinitionId is null
			&& !_gameTable.VisibleCards[hidden.Id].HasFullCardArtwork && LocalCardArtworkResolver.Shared.LoadCount == loads,
			"The lab assigned or loaded artwork for a concealed card.");
	}

	private async Task SaveArtworkEvidence(string name, bool liveCache)
	{
		if (DisplayServer.GetName().Equals("headless", StringComparison.OrdinalIgnoreCase)) return;
		await TableFrames(2);
		RenderingServer.ForceDraw();
		string directory = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../..",
			liveCache ? "artifacts/card-artwork" : "docs/evidence/card-artwork"));
		Directory.CreateDirectory(directory);
		using Image screenshot = GetViewport().GetTexture().GetImage();
		screenshot.SavePng(Path.Combine(directory, name + ".png"));
	}
}
