using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// The run HUD (roadmap U8): one panel beside the attacked server that holds the
/// run's header, the encountered ICE and its subroutines, the Runner's matching
/// breakers with their boosts and breaks inline, Break all, and the run's own
/// continue and jack-out controls. The access panel docks under its header when
/// the run succeeds. Every button submits an issued action from the current
/// decision; Break all re-plans after every snapshot and stops as soon as no
/// step is issued. Scripted smokes keep the older panels unless they ask for the
/// HUD (<c>--run-hud-smoke</c>), so their checks hold.
/// </summary>
public partial class Main
{
	private const float RunHudWidth = 480;
	private PanelContainer? _runHud;
	private VBoxContainer? _runHudBody;
	private Label? _runHudTitle;
	private Label? _runHudStep;
	private Label? _runHudCredits;
	private HBoxContainer? _runHudFooter;
	private CheckButton? _runHudSetting;
	private string? _runHudServerId;
	private MatchViewModel? _runHudView;
	private string? _breakAllBreaker;
	private string? _breakAllEncounter;
	private long _breakAllSubmittedSequence = -1;
	private readonly Dictionary<ActionId, Button> _runHudButtons = new();

	/// <summary>True when the HUD owns this snapshot's run controls.</summary>
	private bool RunHudActive { get; set; }

	private bool RunHudEnabled
	{
		get
		{
			IReadOnlyList<string> arguments = OS.GetCmdlineUserArgs();
			if (arguments.Contains("--run-hud-smoke", StringComparer.Ordinal) || arguments.Contains("--run-hud", StringComparer.Ordinal)) return true;
			bool scripted = arguments.Any(arg => arg.Contains("smoke", StringComparison.Ordinal)
				|| arg.Contains("capture", StringComparison.Ordinal) || arg.Contains("gate", StringComparison.Ordinal));
			return !scripted && !_interactionLab && _runHudSetting?.ButtonPressed != false;
		}
	}

	private void BuildRunHud()
	{
		_runHudSetting = _settingsMenu?.GetNodeOrNull<CheckButton>("%RunHud");
		_runHud = new PanelContainer { Name = "RunHud", Visible = false, ZIndex = 110, MouseFilter = Control.MouseFilterEnum.Stop };
		_runHud.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.039f, 0.059f, 0.078f, 0.97f),
			BorderColor = FactionPalette.NeutralRunner.Accent,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ShadowColor = FactionPalette.NeutralRunner.Accent with { A = 0.2f },
			ShadowSize = 14,
			ContentMarginLeft = 18,
			ContentMarginRight = 18,
			ContentMarginTop = 14,
			ContentMarginBottom = 14,
		});
		VBoxContainer content = new() { Name = "Content", MouseFilter = Control.MouseFilterEnum.Ignore };
		content.AddThemeConstantOverride("separation", 12);
		_runHud.AddChild(content);

		HBoxContainer header = new() { Name = "Header", MouseFilter = Control.MouseFilterEnum.Ignore };
		header.AddThemeConstantOverride("separation", 12);
		_runHudTitle = new Label { Name = "Title", ThemeTypeVariation = "DisplayLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
		_runHudTitle.AddThemeFontSizeOverride("font_size", 26);
		_runHudStep = new Label
		{
			Name = "Step",
			ThemeTypeVariation = "MonoLabel",
			VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_runHudStep.AddThemeFontSizeOverride("font_size", 12);
		_runHudCredits = new Label
		{
			Name = "Credits",
			ThemeTypeVariation = "MonoLabel",
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_runHudCredits.AddThemeFontSizeOverride("font_size", 12);
		header.AddChild(_runHudTitle);
		header.AddChild(_runHudStep);
		header.AddChild(_runHudCredits);
		content.AddChild(header);

		_runHudBody = new VBoxContainer { Name = "Body", MouseFilter = Control.MouseFilterEnum.Ignore };
		_runHudBody.AddThemeConstantOverride("separation", 8);
		content.AddChild(_runHudBody);

		content.AddChild(new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore });
		_runHudFooter = new HBoxContainer { Name = "Footer", Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
		_runHudFooter.AddThemeConstantOverride("separation", 10);
		content.AddChild(_runHudFooter);
		_tableViewport.AddChild(_runHud);
	}

	/// <summary>True when the HUD owns this snapshot's run controls.</summary>
	private bool RunHudOwns(MatchSnapshot snapshot) => RunHudEnabled && !snapshot.IsTerminal
		&& snapshot.View.Viewer == PlayerSide.Runner && snapshot.View.Run is { EndReason: null } && _accessAllRunId is null;

	/// <summary>The issued actions the HUD shows: breaker boosts and breaks, encounter abilities, and continue or jack out.</summary>
	private static bool RunHudTakes(LegalActionViewModel action, MatchViewModel view) => action.Kind switch
	{
		ActionKind.PumpBreaker or ActionKind.BreakSubroutines => BoardIcebreakerPresenter.Source(action) is not null,
		ActionKind.PassPriority or ActionKind.JackOut or ActionKind.ContinueRun or ActionKind.UseBioroidAbility => true,
		ActionKind.UseAbility => view.Run?.Encounter is { Status: RunEncounterStatus.Initialized },
		_ => false,
	};

	/// <summary>Shows or hides the HUD for this snapshot, and rebuilds it when the snapshot changed.</summary>
	private void RefreshRunHud(MatchSnapshot snapshot)
	{
		if (_runHud is null) BuildRunHud();
		MatchViewModel view = snapshot.View;
		bool show = RunHudOwns(snapshot);
		RunHudActive = show;
		if (!show)
		{
			_runHud!.Visible = false;
			StopBreakAll();
			_runHudView = null;
			return;
		}

		RunFocusViewModel run = view.Run!;
		_runHudServerId = run.AttackedServerId;
		// Rebuild for every new observation; the lab can republish under the same sequence.
		if (ReferenceEquals(_runHudView, view) && _runHud!.Visible)
		{
			UpdateRunHudButtons(snapshot);
			return;
		}
		_runHudView = view;
		ApplyRunHudFaction();
		_runHudTitle!.Text = $"RUN · {_gameTable.GetServerDisplayName(run.AttackedServerId).ToUpperInvariant()}";
		_runHudStep!.Text = RunHudStepText(view, run);
		_runHudCredits!.Text = run.RunCredits > 0
			? $"{view.Runner.Credits} ¢ + {run.RunCredits} run ¢ · {view.Runner.Clicks} clicks"
			: $"{view.Runner.Credits} ¢ · {view.Runner.Clicks} clicks";
		DetachChildren(_runHudBody!);
		DetachChildren(_runHudFooter!);
		_runHudButtons.Clear();

		bool mine = view.Decision is { Kind: DecisionKind.RunTiming } decision && decision.OwnerSide == view.Viewer;
		bool ours = mine && CanUseBoardActions(snapshot);
		IReadOnlyList<LegalActionViewModel> issued = ours ? view.LegalActions.Where(action => RunHudTakes(action, view)).ToArray() : [];
		if (run.Encounter is { Status: RunEncounterStatus.Initialized, Ice: { IsKnown: true } } encounter)
			BuildEncounterSection(snapshot, encounter, issued);
		else if (AccessActionsPresenter.Card(view) is { IsKnown: true })
			_runHudBody!.AddChild(MakeWrappedLabel("Accessing. Choose what to do with the card below.", 13, Muted));
		else
			_runHudBody!.AddChild(MakeWrappedLabel(RunHudWaitingText(view, run, mine,
				view.LegalActions.Any(action => !RunHudTakes(action, view))), 13, Muted));

		// The run's own controls: everything issued that isn't a breaker's, in the footer.
		HashSet<ActionId> placed = [.. _runHudButtons.Keys];
		foreach (LegalActionViewModel action in issued.Where(action => !placed.Contains(action.Id)
			&& action.Kind is ActionKind.PassPriority or ActionKind.JackOut or ActionKind.ContinueRun or ActionKind.UseBioroidAbility))
		{
			bool unbroken = run.Encounter is { Status: RunEncounterStatus.Initialized } open && open.RemainingSubroutines > 0;
			string label = action.Kind == ActionKind.PassPriority && unbroken ? "Let it fire"
				: action.Kind == ActionKind.PassPriority ? "Continue"
				: TableActionLabel(action);
			Button button = RunHudButton(label, action.Kind == ActionKind.JackOut ? RunHudTone.Quiet
				: action.Kind == ActionKind.PassPriority && unbroken ? RunHudTone.Danger : RunHudTone.Normal);
			button.Pressed += () => SubmitRunHudAction(action);
			_runHudFooter!.AddChild(button);
			_runHudButtons[action.Id] = button;
		}
		_runHudFooter!.Visible = _runHudFooter.GetChildCount() > 0;
		_runHud!.Visible = true;
		ContinueBreakAll(snapshot);
	}

	/// <summary>Removes children at once, so a rebuild in the same frame keeps its node names.</summary>
	private static void DetachChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			parent.RemoveChild(child);
			child.QueueFree();
		}
	}

	private static string RunHudStepText(MatchViewModel view, RunFocusViewModel run)
	{
		ServerViewModel? server = view.Servers.FirstOrDefault(candidate => candidate.Id == run.AttackedServerId);
		int count = server?.OrderedIce.Count ?? 0;
		// Core counts ICE from the innermost (0); the run moves from the outermost inward.
		string position = run.CurrentIceIndex >= 0 && count > 0 ? $"ICE {count - run.CurrentIceIndex} OF {count}" : "SERVER";
		string step = run.Encounter is { Status: RunEncounterStatus.Initialized } ? "ENCOUNTER"
			: run.Access.State != RunAccessState.NotStarted ? "ACCESS"
			: run.Step switch
			{
				RunTimingStep.Approach or RunTimingStep.ApproachPaidWindow => "APPROACH",
				RunTimingStep.MovementPaidWindow or RunTimingStep.JackOutDecision or RunTimingStep.MoveInward
					or RunTimingStep.AfterMovePaidWindow => "PASSED",
				RunTimingStep.ApproachServer => "APPROACH",
				_ => "RUN",
			};
		return $"{position} · {step}";
	}

	private static string RunHudWaitingText(MatchViewModel view, RunFocusViewModel run, bool mine, bool boardChoice)
	{
		if (!mine) return "Waiting for the Corp…";
		if (boardChoice && run.Access.State != RunAccessState.NotStarted) return "Choose what to access on the board.";
		if (run.CurrentIce is { IsKnown: true } ice) return $"Next: {ice.Title}.";
		if (run.CurrentIceIndex >= 0) return "Next: face-down ICE. The Corp may rez it.";
		return "Next: the server. Continue to breach it.";
	}

	private void BuildEncounterSection(MatchSnapshot snapshot, EncounterViewModel encounter, IReadOnlyList<LegalActionViewModel> issued)
	{
		CardViewModel ice = encounter.Ice!;
		VBoxContainer body = _runHudBody!;
		Label heading = new() { Text = ice.Title.ToUpperInvariant(), ThemeTypeVariation = "DisplayLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
		heading.AddThemeFontSizeOverride("font_size", 20);
		body.AddChild(heading);
		string subtype = ice.Subtypes.Count == 0 ? "ICE" : string.Join(" · ", ice.Subtypes).ToUpperInvariant();
		Label meta = new()
		{
			Text = $"{subtype} · STRENGTH {encounter.EffectiveStrength ?? ice.Strength}",
			ThemeTypeVariation = "MonoLabel",
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		body.AddChild(meta);

		string[] printed = ice.RulesText.Split("[subroutine]", StringSplitOptions.None).Skip(1).Select(text => text.Trim()).ToArray();
		foreach (SubroutineViewModel subroutine in encounter.Subroutines.OrderBy(value => value.Ordinal))
		{
			string text = subroutine.Ordinal < printed.Length ? FriendlyText(printed[subroutine.Ordinal]) : $"Subroutine {subroutine.Ordinal + 1}";
			(string mark, Color color) = subroutine.Broken ? ("BROKEN", new Color(0.55f, 0.6f, 0.62f))
				: subroutine.Resolved ? ("FIRED", new Color(0.7f, 0.72f, 0.75f))
				: ("UNBROKEN", new Color(1f, 0.62f, 0.7f));
			body.AddChild(SubroutineRow(subroutine.Ordinal + 1, text, mark, color));
		}

		Label breakersHeading = new() { Text = "YOUR BREAKERS", ThemeTypeVariation = "MonoLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
		body.AddChild(breakersHeading);
		IEnumerable<EncounterBreakerViewModel> breakers = encounter.Breakers.OrderByDescending(breaker => breaker.CanInterfaceEncounter);
		HashSet<string> shown = new(StringComparer.Ordinal);
		foreach (EncounterBreakerViewModel breaker in breakers)
		{
			shown.Add(breaker.CardId);
			body.AddChild(BreakerRow(snapshot, encounter, breaker.CardId, breaker.Title, breaker, issued));
		}
		// Hosted counter abilities and other issued break sources without strength metadata.
		foreach (string source in issued.Select(BoardIcebreakerPresenter.Source).OfType<string>().Distinct(StringComparer.Ordinal).Where(source => !shown.Contains(source)))
			body.AddChild(BreakerRow(snapshot, encounter, source, ActionCardName(source), null, issued));
		foreach (LegalActionViewModel ability in issued.Where(action => action.Kind == ActionKind.UseAbility))
		{
			Button button = RunHudButton(TableActionLabel(ability), RunHudTone.Normal);
			button.Pressed += () => SubmitRunHudAction(ability);
			body.AddChild(button);
			_runHudButtons[ability.Id] = button;
		}
	}

	private static PanelContainer SubroutineRow(int number, string text, string mark, Color color)
	{
		PanelContainer row = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
		row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = color with { A = 0.07f },
			BorderColor = color with { A = 0.7f },
			BorderWidthLeft = 2,
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
		});
		HBoxContainer line = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
		line.AddThemeConstantOverride("separation", 10);
		Label ordinal = new() { Text = $"↳ {number}", ThemeTypeVariation = "MonoLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
		ordinal.AddThemeColorOverride("font_color", color);
		GameSymbolLabel body = new()
		{
			SymbolText = text,
			FitContent = true,
			ScrollActive = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		body.AddThemeFontSizeOverride("normal_font_size", 14);
		Label status = new() { Text = mark, ThemeTypeVariation = "MonoLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
		status.AddThemeColorOverride("font_color", color);
		line.AddChild(ordinal);
		line.AddChild(body);
		line.AddChild(status);
		row.AddChild(line);
		return row;
	}

	private PanelContainer BreakerRow(MatchSnapshot snapshot, EncounterViewModel encounter, string id, string title,
		EncounterBreakerViewModel? breaker, IReadOnlyList<LegalActionViewModel> issued)
	{
		bool matching = breaker?.CanInterfaceEncounter ?? true;
		Color accent = _gameTable.RunnerFaction.Accent;
		PanelContainer row = new() { Name = $"Breaker-{id}", MouseFilter = Control.MouseFilterEnum.Ignore };
		row.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = matching ? accent with { A = 0.08f } : new Color(0.05f, 0.07f, 0.09f),
			BorderColor = matching ? accent : new Color(0.17f, 0.2f, 0.24f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft = 12,
			ContentMarginRight = 12,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
		});
		VBoxContainer column = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 6);
		HBoxContainer top = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
		top.AddThemeConstantOverride("separation", 10);
		Label name = new() { Text = title, ThemeTypeVariation = "DisplayLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
		name.AddThemeFontSizeOverride("font_size", 18);
		if (!matching) name.Modulate = new Color(1, 1, 1, 0.6f);
		string strength = breaker is null ? "HOSTED"
			: encounter.EffectiveStrength is { } needed && matching && breaker.EffectiveStrength < needed ? $"STR {breaker.EffectiveStrength} → {needed}"
			: matching ? $"STR {breaker.EffectiveStrength} · STRONG ENOUGH" : $"STR {breaker.EffectiveStrength}";
		Label detail = new()
		{
			Text = matching ? strength : $"{strength} · can't break {IceKind(encounter)}",
			ThemeTypeVariation = "MonoLabel",
			VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		top.AddChild(name);
		top.AddChild(detail);
		column.AddChild(top);
		row.AddChild(column);
		if (!matching) return row;

		HBoxContainer controls = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
		controls.AddThemeConstantOverride("separation", 8);
		LegalActionViewModel[] own = BoardIcebreakerPresenter.EncounterActions(snapshot.View, issued)
			.Where(action => BoardIcebreakerPresenter.Source(action) == id).ToArray();
		bool atStrength = breaker is null || encounter.EffectiveStrength is not { } required || breaker.EffectiveStrength >= required;
		foreach (LegalActionViewModel pump in own.Where(action => action.Kind == ActionKind.PumpBreaker && !atStrength).Take(1))
		{
			string text = breaker is { PumpStrength: { } amount } ? $"+{amount} strength · {breaker.PumpCost} ¢" : "Boost";
			Button button = RunHudButton(text, RunHudTone.Normal);
			button.Pressed += () => SubmitRunHudAction(pump);
			controls.AddChild(button);
			_runHudButtons[pump.Id] = button;
		}
		// One button per subroutine this breaker can break alone, cheapest payment first.
		foreach (var single in own.Where(action => action.Kind == ActionKind.BreakSubroutines)
			.Select(action => (Action: action, Targets: BoardIcebreakerPresenter.BrokenOrdinals(encounter, action)))
			.Where(pair => pair.Targets.Count == 1)
			.GroupBy(pair => pair.Targets[0]).Select(group => group.First()).OrderBy(pair => pair.Targets[0]))
		{
			string cost = single.Action.CostQuote is { } quote ? $" · {quote.EffectiveCost} ¢" : string.Empty;
			Button button = RunHudButton($"Break sub {single.Targets[0] + 1}{cost}", RunHudTone.Normal);
			button.Pressed += () => SubmitRunHudAction(single.Action);
			controls.AddChild(button);
			_runHudButtons[single.Action.Id] = button;
		}
		controls.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
		if (BreakAllPresenter.Plan(snapshot.View with { LegalActions = issued }, id) is { } plan
			&& encounter.RemainingSubroutines > 0)
		{
			string estimate = plan.EstimatedCredits is { } credits ? $" · {credits} ¢" : string.Empty;
			Button breakAll = RunHudButton($"BREAK ALL{estimate}", RunHudTone.Primary);
			breakAll.Name = "BreakAll";
			breakAll.Disabled = !plan.Affordable;
			breakAll.TooltipText = plan.Affordable
				? "Boost and break every remaining subroutine with this breaker, one issued action at a time."
				: "Not enough credits to break every subroutine with this breaker.";
			breakAll.Pressed += () => StartBreakAll(id, encounter.Id);
			controls.AddChild(breakAll);
		}
		if (controls.GetChildCount() > 1) column.AddChild(controls);
		return row;
	}

	private static string IceKind(EncounterViewModel encounter)
	{
		IReadOnlyList<string> subtypes = encounter.Ice?.Subtypes ?? [];
		foreach (string kind in new[] { "Barrier", "Code Gate", "Sentry" })
			if (subtypes.Any(subtype => subtype.Equals(kind, StringComparison.OrdinalIgnoreCase))) return kind.ToLowerInvariant() + "s";
		return "this ICE";
	}

	private enum RunHudTone { Normal, Primary, Danger, Quiet }

	private Button RunHudButton(string text, RunHudTone tone)
	{
		Button button = CreateTableActionButton(text);
		button.CustomMinimumSize = new Vector2(0, tone == RunHudTone.Primary ? 46 : 40);
		if (tone == RunHudTone.Primary)
		{
			Color accent = _gameTable.RunnerFaction.Accent;
			button.ThemeTypeVariation = "PrimaryAction";
			button.AddThemeStyleboxOverride("normal", new StyleBoxFlat
			{
				BgColor = accent,
				CornerRadiusTopLeft = 3,
				CornerRadiusTopRight = 3,
				CornerRadiusBottomLeft = 3,
				CornerRadiusBottomRight = 3,
				ContentMarginLeft = 16,
				ContentMarginRight = 16
			});
			button.AddThemeStyleboxOverride("hover", new StyleBoxFlat
			{
				BgColor = accent.Lightened(0.15f),
				CornerRadiusTopLeft = 3,
				CornerRadiusTopRight = 3,
				CornerRadiusBottomLeft = 3,
				CornerRadiusBottomRight = 3,
				ContentMarginLeft = 16,
				ContentMarginRight = 16
			});
			button.AddThemeColorOverride("font_color", new Color(0.02f, 0.06f, 0.1f));
			button.AddThemeColorOverride("font_hover_color", new Color(0.02f, 0.06f, 0.1f));
		}
		else if (tone == RunHudTone.Danger)
		{
			button.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.77f));
		}
		else if (tone == RunHudTone.Quiet)
		{
			button.AddThemeColorOverride("font_color", Muted);
		}
		return button;
	}

	private void ApplyRunHudFaction()
	{
		if (_runHud?.GetThemeStylebox("panel") is not StyleBoxFlat style) return;
		Color accent = _gameTable.RunnerFaction.Accent;
		style.BorderColor = accent;
		style.ShadowColor = accent with { A = 0.2f };
		_runHudStep?.AddThemeColorOverride("font_color", _gameTable.RunnerFaction.Text);
	}

	private void SubmitRunHudAction(LegalActionViewModel action)
	{
		if (_currentSnapshot is not { } snapshot || !RunHudActive || !CanUseBoardActions(snapshot)
			|| snapshot.View.Decision is not { } decision
			|| !snapshot.View.LegalActions.Any(candidate => candidate.Id == action.Id)) return;
		StopBreakAll();
		foreach (Button button in _runHudButtons.Values) button.Disabled = true;
		RecordFullMatchSelection("run-hud", action.Kind.ToString());
		SubmitAction(decision, action);
	}

	private void UpdateRunHudButtons(MatchSnapshot snapshot)
	{
		bool enabled = CanUseBoardActions(snapshot);
		foreach ((ActionId id, Button button) in _runHudButtons)
			button.Disabled = !enabled || !snapshot.View.LegalActions.Any(action => action.Id == id);
		ContinueBreakAll(snapshot);
	}

	private void StartBreakAll(string breakerId, string encounterId)
	{
		_breakAllBreaker = breakerId;
		_breakAllEncounter = encounterId;
		_breakAllSubmittedSequence = -1;
		RecordFullMatchSelection("run-hud", "break-all");
		if (_currentSnapshot is { } snapshot) ContinueBreakAll(snapshot);
	}

	private void StopBreakAll()
	{
		_breakAllBreaker = null;
		_breakAllEncounter = null;
		_breakAllSubmittedSequence = -1;
	}

	/// <summary>Submits the next Break all step once per snapshot, while the same encounter still needs one.</summary>
	private void ContinueBreakAll(MatchSnapshot snapshot)
	{
		if (_breakAllBreaker is not { } breaker) return;
		if (snapshot.View.Run?.Encounter is not { Status: RunEncounterStatus.Initialized } encounter || encounter.Id != _breakAllEncounter
			|| encounter.RemainingSubroutines == 0)
		{
			StopBreakAll();
			return;
		}
		if (_breakAllSubmittedSequence == snapshot.Sequence || !CanUseBoardActions(snapshot) || snapshot.View.Decision is not { } decision) return;
		if (BreakAllPresenter.Plan(snapshot.View, breaker) is not { Affordable: true } plan)
		{
			StopBreakAll();
			return;
		}
		_breakAllSubmittedSequence = snapshot.Sequence;
		foreach (Button button in _runHudButtons.Values) button.Disabled = true;
		SubmitAction(decision, plan.Next);
	}

	/// <summary>Keeps the HUD beside the attacked server, and the access panel under its header.</summary>
	private void PlaceRunHud()
	{
		if (_runHud is not { Visible: true } hud) return;
		Rect2 bounds = _tableViewport.GetGlobalRect();
		Transform2D toLocal = _tableViewport.GetGlobalTransform().AffineInverse();
		hud.Size = new Vector2(RunHudWidth, 0);
		hud.ResetSize();
		hud.Size = new Vector2(RunHudWidth, hud.Size.Y);
		Rect2? lane = _runHudServerId is { } server ? _gameTable.GetServerLaneRect(server) : null;
		Vector2 size = hud.Size;
		float scale = _tableViewport.GetGlobalTransform().Scale.X;
		// The table's right edge, clear of centred choice panels; beside the lane if the lane is there.
		float x = _tableViewport.Size.X - size.X - 24;
		float y = 12;
		if (lane is { } rect)
		{
			Vector2 local = toLocal * rect.Position;
			float laneRight = local.X + rect.Size.X / scale;
			y = local.Y;
			if (laneRight > x) x = local.X - size.X - 16;
		}
		hud.Position = new Vector2(Math.Clamp(x, 12, Math.Max(12, _tableViewport.Size.X - size.X - 12)),
			Math.Clamp(y, 12, Math.Max(12, _tableViewport.Size.Y - size.Y - 12)));
		if (_tableAccessPanel is { Visible: true } access)
		{
			access.ResetSize();
			Rect2 hudRect = hud.GetGlobalRect();
			access.GlobalPosition = new Vector2(Math.Max(bounds.Position.X, hudRect.End.X - access.GetGlobalRect().Size.X), hudRect.End.Y + 10);
			if (access.GetGlobalRect().End.Y > bounds.End.Y)
				access.GlobalPosition = access.GlobalPosition with { Y = Math.Max(bounds.Position.Y, bounds.End.Y - access.GetGlobalRect().Size.Y) };
		}
	}
}
