using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// The run drawn on the board (roadmap U8): a neon connection from the Runner's
/// edge of the attacked server straight through the ICE it has passed to where
/// the run is now. Passed rezzed ICE derezzes (a chromatic split, scanlines,
/// displaced slices and a burnt aperture the line runs through); the ICE being
/// encountered is lit at the contact point; the rest of the way to the root is
/// a dim dashed line. Presentation only.
/// </summary>
public partial class GameTableControl
{
	private static readonly Color SplitMagenta = new("ff3b7a");
	private static readonly Color SplitCyan = new("3bf0ff");
	private Control? _runConnection;
	private double _glitchClock;
	private int _glitchSeed;

	/// <summary>True while the board is drawing a run's connection.</summary>
	public bool RunConnectionVisible { get; private set; }

	private void EnsureRunConnection()
	{
		if (_runConnection is not null) return;
		_runConnection = new Control { Name = "RunConnection", MouseFilter = MouseFilterEnum.Ignore, ZIndex = 24 };
		AddChild(_runConnection);
		_runConnection.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_runConnection.Draw += DrawRunConnection;
	}

	private void UpdateRunConnection(double delta)
	{
		EnsureRunConnection();
		bool active = _pendingView?.Run is { EndReason: null } run && _serverLanes.ContainsKey(run.AttackedServerId);
		if (!active && !RunConnectionVisible) return;
		RunConnectionVisible = active;
		if (!Hand.ReducedMotion && MotionEnabled)
		{
			_glitchClock += delta;
			if (_glitchClock > 0.45)
			{
				_glitchClock = 0;
				_glitchSeed++;
			}
		}
		_runConnection!.QueueRedraw();
	}

	private void DrawRunConnection()
	{
		if (_runConnection is not { } canvas || _pendingView?.Run is not { EndReason: null } run
			|| !_serverLanes.TryGetValue(run.AttackedServerId, out ServerLaneParts? lane) || !lane.Panel.IsVisibleInTree())
			return;
		ServerViewModel? server = _pendingView.Servers.FirstOrDefault(candidate => candidate.Id == run.AttackedServerId);
		if (server is null) return;
		Transform2D toLocal = canvas.GetGlobalTransform().AffineInverse();
		Rect2 Local(Rect2 global) => new(toLocal * global.Position, toLocal.BasisXform(global.Size));

		Rect2 laneRect = Local(lane.Panel.GetGlobalRect());
		Rect2 rootRect = Local(lane.RootCards.GetGlobalRect());
		float x = rootRect.GetCenter().X;
		bool runnerBelow = !CorpSeatLayout;
		float start = runnerBelow ? laneRect.End.Y + 6 : laneRect.Position.Y - 6;
		float rootEdge = runnerBelow ? rootRect.End.Y : rootRect.Position.Y;

		int count = server.OrderedIce.Count;
		// Core counts ICE from the innermost (0); OrderedIce runs outermost first.
		int current = run.CurrentIceIndex >= 0 && run.CurrentIceIndex < count ? count - 1 - run.CurrentIceIndex : count;
		bool currentPassed = run.Step is RunTimingStep.MovementPaidWindow or RunTimingStep.JackOutDecision
			or RunTimingStep.MoveInward or RunTimingStep.AfterMovePaidWindow;
		bool encountering = run.Encounter is { Status: RunEncounterStatus.Initialized };

		float front = rootEdge;
		Rect2? contact = null;
		for (int display = 0; display < count; display++)
		{
			if (!_slotAliases.TryGetValue($"server:{server.Id}:ice:{display}", out TableCardControl? ice) || !ice.IsVisibleInTree()) continue;
			Rect2 rect = Local(ice.GetGlobalRect());
			bool passed = display < current || (display == current && currentPassed);
			if (passed)
			{
				if (server.OrderedIce[display] is { IsKnown: true, Rezzed: true }) DrawDerezzed(canvas, rect, x, display);
				else DrawPassedTag(canvas, rect);
			}
			else if (display == current)
			{
				contact = rect;
				front = runnerBelow ? rect.End.Y : rect.Position.Y;
			}
		}

		Color neon = _runnerFaction.Accent;
		// The lit connection from the Runner's edge to the front.
		DrawNeonLine(canvas, new Vector2(x, start), new Vector2(x, front), neon);
		// Where the connection meets the ICE, or the root it reached.
		if (contact is { } touched)
		{
			Color corp = _corpFaction.Accent;
			float glow = encountering ? 0.55f : 0.3f;
			canvas.DrawRect(touched.Grow(4), corp with { A = glow * 0.35f }, false, 6);
			canvas.DrawRect(touched.Grow(1), corp with { A = glow + 0.3f }, false, 2);
			canvas.DrawCircle(new Vector2(x, front), 11, neon with { A = 0.25f });
			canvas.DrawCircle(new Vector2(x, front), 6, neon);
			// Dim dashes over the rest of the way to the root.
			DrawDashes(canvas, new Vector2(x, runnerBelow ? touched.Position.Y : touched.End.Y), new Vector2(x, rootEdge), neon with { A = 0.45f });
		}
		else
		{
			canvas.DrawRect(rootRect.Grow(6), neon with { A = 0.14f }, false, 10);
			canvas.DrawRect(rootRect.Grow(2), neon with { A = 0.8f }, false, 2);
		}
		// The jack-in point at the Runner's edge.
		canvas.DrawCircle(new Vector2(x, start), 12, neon with { A = 0.25f });
		canvas.DrawArc(new Vector2(x, start), 9, 0, Mathf.Tau, 28, neon, 3, true);
		canvas.DrawCircle(new Vector2(x, start), 6, _runnerFaction.Ground);
	}

	private static void DrawNeonLine(Control canvas, Vector2 from, Vector2 to, Color neon)
	{
		canvas.DrawLine(from, to, neon with { A = 0.12f }, 14);
		canvas.DrawLine(from, to, neon with { A = 0.3f }, 7);
		canvas.DrawLine(from, to, neon, 3);
	}

	private static void DrawDashes(Control canvas, Vector2 from, Vector2 to, Color color)
	{
		float length = from.DistanceTo(to);
		if (length < 1) return;
		Vector2 step = (to - from) / length;
		for (float travelled = 0; travelled < length; travelled += 12)
			canvas.DrawLine(from + step * travelled, from + step * Mathf.Min(length, travelled + 6), color, 2);
	}

	/// <summary>Passed ICE that was never rezzed stays whole, marked passed.</summary>
	private void DrawPassedTag(Control canvas, Rect2 rect)
	{
		if (TableFonts.Mono is not { } mono) return;
		canvas.DrawRect(rect, new Color(0, 0, 0, 0.25f));
		canvas.DrawString(mono, rect.Position + new Vector2(rect.Size.X - 58, 14), "PASSED", HorizontalAlignment.Left, -1, 10, _runnerFaction.Text);
	}

	/// <summary>Rezzed ICE the run went through: it derezzes around the connection instead of tearing.</summary>
	private void DrawDerezzed(Control canvas, Rect2 rect, float lineX, int salt)
	{
		Color corp = _corpFaction.Accent;
		Color neon = _runnerFaction.Accent;
		canvas.DrawRect(rect, new Color(0.02f, 0.03f, 0.04f, 0.5f));
		for (float y = rect.Position.Y + 1; y < rect.End.Y; y += 3)
			canvas.DrawLine(new Vector2(rect.Position.X, y), new Vector2(rect.End.X, y), corp with { A = 0.1f }, 1);
		// Chromatic split of the frame.
		canvas.DrawRect(rect with { Position = rect.Position + new Vector2(-3, 0) }, SplitMagenta with { A = 0.55f }, false, 1);
		canvas.DrawRect(rect with { Position = rect.Position + new Vector2(3, 0) }, SplitCyan with { A = 0.55f }, false, 1);
		// Slices knocked out of line; they shift now and then unless motion is reduced.
		var random = new Random(unchecked(salt * 7919 + _glitchSeed * 104729));
		for (int slice = 0; slice < 3; slice++)
		{
			float y = rect.Position.Y + (float)random.NextDouble() * (rect.Size.Y - 8);
			float width = rect.Size.X * (0.25f + (float)random.NextDouble() * 0.3f);
			float offset = ((float)random.NextDouble() - 0.5f) * 36;
			float left = slice % 2 == 0 ? rect.Position.X + offset : rect.End.X - width + offset;
			float height = 3 + (float)random.NextDouble() * 5;
			canvas.DrawRect(new Rect2(left, y, width, height), corp with { A = 0.22f });
			canvas.DrawLine(new Vector2(left, y), new Vector2(left, y + height), corp with { A = 0.8f }, 2);
		}
		// The aperture the connection burnt through, and a few pixel shards around it.
		Rect2 aperture = new(lineX - 14, rect.Position.Y - 5, 28, rect.Size.Y + 10);
		canvas.DrawRect(aperture, _runnerFaction.Ground);
		canvas.DrawLine(aperture.Position, aperture.Position + new Vector2(0, aperture.Size.Y), neon, 1);
		canvas.DrawLine(aperture.Position + new Vector2(aperture.Size.X, 0), aperture.End, neon, 1);
		canvas.DrawRect(aperture.Grow(3), neon with { A = 0.18f }, false, 4);
		for (int shard = 0; shard < 6; shard++)
		{
			float dx = ((float)random.NextDouble() - 0.5f) * 70;
			float dy = (float)random.NextDouble() * rect.Size.Y;
			float size = 2 + (float)random.NextDouble() * 4;
			Color color = shard % 3 == 0 ? SplitCyan : shard % 3 == 1 ? neon : corp;
			canvas.DrawRect(new Rect2(lineX + dx - size / 2, rect.Position.Y + dy, size, size), color with { A = 0.85f });
		}
		if (TableFonts.Mono is { } mono)
			canvas.DrawString(mono, new Vector2(rect.End.X - 62, rect.Position.Y + 14), "SEVERED", HorizontalAlignment.Left, -1, 10, _runnerFaction.Text);
	}
}
