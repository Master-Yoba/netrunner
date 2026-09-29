using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// Draws the two halves of the table behind every board control (roadmap U5,
/// "Corp chrome against Runner neon"). The Corp half is brushed steel tinted to
/// its faction, with a double rule and ruler ticks along the edge that faces the
/// Runner and a faint wordmark; the Runner half is near-black with a faint dot
/// grid and a neon edge line. Presentation only: it never takes input.
/// </summary>
[Tool]
public partial class TableBackdrop : Control
{
	private FactionColors _corp = FactionPalette.NeutralCorp;
	private FactionColors _runner = FactionPalette.NeutralRunner;
	private float _split = -1;
	private bool _corpOnTop = true;

	/// <summary>Y of the boundary between the halves, in this control's coordinates; negative means the middle.</summary>
	public float Split => _split;

	public bool CorpOnTop => _corpOnTop;

	public FactionColors Corp => _corp;

	public FactionColors Runner => _runner;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Resized += QueueRedraw;
	}

	public void SetFactions(FactionColors corp, FactionColors runner)
	{
		if (corp == _corp && runner == _runner) return;
		_corp = corp;
		_runner = runner;
		QueueRedraw();
	}

	/// <summary>Places the boundary between the halves; redraws only when it moved.</summary>
	public void SetSplit(float split, bool corpOnTop)
	{
		if (Mathf.IsEqualApprox(split, _split) && corpOnTop == _corpOnTop) return;
		_split = split;
		_corpOnTop = corpOnTop;
		QueueRedraw();
	}

	public override void _Draw()
	{
		float width = Size.X;
		float height = Size.Y;
		if (width <= 0 || height <= 0) return;
		float split = _split < 0 ? height / 2f : Mathf.Clamp(_split, 0, height);
		Rect2 corpRect = _corpOnTop ? new Rect2(0, 0, width, split) : new Rect2(0, split, width, height - split);
		Rect2 runnerRect = _corpOnTop ? new Rect2(0, split, width, height - split) : new Rect2(0, 0, width, split);
		DrawCorpHalf(corpRect);
		DrawRunnerHalf(runnerRect);
	}

	private void DrawCorpHalf(Rect2 area)
	{
		DrawRect(area, _corp.Ground);
		// Brushed steel: fine vertical pinstripes.
		Color stripe = new(1, 1, 1, 0.02f);
		for (float x = area.Position.X; x < area.End.X; x += 7)
		{
			DrawRect(new Rect2(x, area.Position.Y, 2, area.Size.Y), stripe);
		}

		// The edge facing the Runner: a precise double rule with ruler ticks.
		float edge = _corpOnTop ? area.End.Y : area.Position.Y;
		float inward = _corpOnTop ? -1 : 1;
		Color accent = _corp.Accent;
		DrawLine(new Vector2(area.Position.X, edge + inward * 1), new Vector2(area.End.X, edge + inward * 1), accent, 1);
		DrawLine(new Vector2(area.Position.X, edge + inward * 4), new Vector2(area.End.X, edge + inward * 4), accent with { A = 0.35f }, 1);
		for (float x = area.Position.X; x < area.End.X; x += 8)
		{
			bool major = Mathf.PosMod(x - area.Position.X, 40) < 0.5f;
			float tick = major ? 7 : 3;
			float start = edge + inward * 5;
			DrawLine(new Vector2(x, start), new Vector2(x, start + inward * tick), accent with { A = major ? 0.35f : 0.2f }, 1);
		}

		// A faint wordmark and a classification label just inside that edge.
		Font? display = TableFonts.DisplayWide;
		Font? mono = TableFonts.Mono;
		float baseline = _corpOnTop ? edge - 18 : edge + 30;
		if (display is not null)
		{
			DrawString(display, new Vector2(area.Position.X + 24, baseline), $"{_corp.Name} · SECURE NETWORK",
				HorizontalAlignment.Left, -1, 14, accent with { A = 0.22f });
		}

		if (mono is not null)
		{
			const string label = "CLASSIFIED · CORP INFRASTRUCTURE";
			float labelWidth = mono.GetStringSize(label, HorizontalAlignment.Left, -1, 10).X;
			DrawString(mono, new Vector2(area.End.X - labelWidth - 24, baseline), label,
				HorizontalAlignment.Left, -1, 10, accent with { A = 0.38f });
		}
	}

	private void DrawRunnerHalf(Rect2 area)
	{
		DrawRect(area, _runner.Ground);
		// A faint circuit grid of dots.
		Color dot = _runner.Accent with { A = 0.12f };
		for (float y = area.Position.Y + 11; y < area.End.Y; y += 22)
		{
			for (float x = area.Position.X + 11; x < area.End.X; x += 22)
			{
				DrawRect(new Rect2(x - 1, y - 1, 2, 2), dot);
			}
		}

		// The neon edge facing the Corp, with a soft glow.
		float edge = _corpOnTop ? area.Position.Y : area.End.Y;
		Color accent = _runner.Accent;
		DrawLine(new Vector2(area.Position.X, edge), new Vector2(area.End.X, edge), accent with { A = 0.08f }, 12);
		DrawLine(new Vector2(area.Position.X, edge), new Vector2(area.End.X, edge), accent with { A = 0.22f }, 4);
		DrawLine(new Vector2(area.Position.X, edge), new Vector2(area.End.X, edge), accent, 1);
	}
}
