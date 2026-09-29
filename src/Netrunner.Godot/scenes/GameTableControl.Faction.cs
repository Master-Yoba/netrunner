using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Faction chrome (roadmap U5): table halves, identity plates and server brackets follow each identity's faction.</summary>
public partial class GameTableControl
{
	private const float LaneBracketLength = 14;
	private FactionColors _corpFaction = FactionPalette.NeutralCorp;
	private FactionColors _runnerFaction = FactionPalette.NeutralRunner;

	public FactionColors CorpFaction => _corpFaction;

	public FactionColors RunnerFaction => _runnerFaction;

	private TableBackdrop? Backdrop => GetNodeOrNull<TableBackdrop>("Background");

	private void ApplyFactionChrome(MatchViewModel view)
	{
		FactionColors corp = FactionPalette.For(view.CorpIdentity?.Faction, PlayerSide.Corp);
		FactionColors runner = FactionPalette.For(view.RunnerIdentity?.Faction, PlayerSide.Runner);
		bool changed = corp != _corpFaction || runner != _runnerFaction;
		_corpFaction = corp;
		_runnerFaction = runner;
		Backdrop?.SetFactions(corp, runner);
		ApplyPlate("Surface/Margins/Layout/ServerField/Content/Summary/Content/Plate", view.CorpIdentity?.Faction, PlayerSide.Corp, corp);
		ApplyPlate("Surface/Margins/Layout/RunnerField/Content/Summary/Content/Plate", view.RunnerIdentity?.Faction, PlayerSide.Runner, runner);
		if (!changed) return;
		foreach (ServerLaneParts lane in _serverLanes.Values)
		{
			lane.Panel.QueueRedraw();
		}
	}

	private void ApplyPlate(string path, string? faction, PlayerSide side, FactionColors colors)
	{
		if (GetNodeOrNull<Control>(path) is not { } plate) return;
		plate.GetNodeOrNull<FactionEmblem>("Emblem")?.SetFaction(faction, side);
		if (plate.GetNodeOrNull<Label>("Side") is { } label)
		{
			label.Text = colors.Name;
			label.AddThemeColorOverride("font_color", side == PlayerSide.Corp ? new Color(0.91f, 0.93f, 0.95f) : colors.Text);
			label.TooltipText = side == PlayerSide.Corp ? "Corp identity faction" : "Runner identity faction";
		}
	}

	/// <summary>Central servers carry the Null Signal server icon in their header.</summary>
	private static void ApplyLaneIcon(Button header, ServerKind kind)
	{
		string? path = kind switch
		{
			ServerKind.Headquarters => "res://icons/server-hq.svg",
			ServerKind.RAndD => "res://icons/server-rd.svg",
			ServerKind.Archives => "res://icons/server-archives.svg",
			_ => null,
		};
		header.Icon = path is not null && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		header.AddThemeConstantOverride("icon_max_width", 18);
		header.AddThemeColorOverride("icon_normal_color", new Color(0.79f, 0.83f, 0.8f));
	}

	/// <summary>Draws the Corp-coloured corner brackets over a server lane's steel slab.</summary>
	private void DrawLaneBrackets(PanelContainer panel)
	{
		Vector2 size = panel.Size;
		if (size.X <= 0 || size.Y <= 0) return;
		Color accent = _corpFaction.Accent;
		const float width = 2;
		float length = Mathf.Min(LaneBracketLength, Mathf.Min(size.X, size.Y) / 3);
		panel.DrawLine(new Vector2(0, 1), new Vector2(length, 1), accent, width);
		panel.DrawLine(new Vector2(1, 0), new Vector2(1, length), accent, width);
		panel.DrawLine(new Vector2(size.X - length, size.Y - 1), new Vector2(size.X, size.Y - 1), accent, width);
		panel.DrawLine(new Vector2(size.X - 1, size.Y - length), new Vector2(size.X - 1, size.Y), accent, width);
	}

	/// <summary>Keeps the backdrop's boundary midway between the Corp's server field and the Runner's area.</summary>
	private void UpdateBackdropSplit()
	{
		if (Backdrop is not { } backdrop) return;
		if (GetNodeOrNull<Control>("Surface/Margins/Layout/ServerField") is not { } corpField
			|| GetNodeOrNull<Control>("Surface/Margins/Layout/RunnerField") is not { } runnerField)
			return;
		Transform2D toLocal = backdrop.GetGlobalTransform().AffineInverse();
		Rect2 corp = corpField.GetGlobalRect();
		Rect2 runner = runnerField.GetGlobalRect();
		bool corpOnTop = corp.Position.Y <= runner.Position.Y;
		float boundary = corpOnTop
			? (corp.End.Y + runner.Position.Y) / 2
			: (runner.End.Y + corp.Position.Y) / 2;
		float local = (toLocal * new Vector2(0, boundary)).Y;
		backdrop.SetSplit(Mathf.Round(local), corpOnTop);
	}
}
