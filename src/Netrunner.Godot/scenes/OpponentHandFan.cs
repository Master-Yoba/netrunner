using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

/// <summary>
/// The opponent's hand as a face-down fan peeking from the top edge of the
/// table (roadmap U5): about a quarter of each card shows. It knows only the
/// public hand size, never which cards are in it. Cards fly to and from its
/// slots when the opponent draws, discards or plays.
/// </summary>
[Tool]
public partial class OpponentHandFan : Control
{
	[Export(PropertyHint.Range, "20,120,1")] public float CardWidth { get; set; } = 64;
	[Export(PropertyHint.Range, "20,180,1")] public float CardHeight { get; set; } = 90;
	[Export(PropertyHint.Range, "0.1,1,0.01")] public float VisibleFraction { get; set; } = 0.3f;
	[Export(PropertyHint.Range, "10,80,1")] public float CardStep { get; set; } = 40;
	[Export(PropertyHint.Range, "0,15,0.5")] public float FanAngle { get; set; } = 4;
	[Export(PropertyHint.Range, "0,0.5,0.01")] public float MotionSeconds { get; set; } = 0.09f;
	public bool ReducedMotion { get; set; }

	private readonly List<float> _offsets = new();
	private int _count;
	private PlayerSide _side = PlayerSide.Corp;
	private Texture2D? _back;
	private Color _accent = FactionPalette.NeutralCorp.Accent;
	private int _hidden;

	/// <summary>The public number of cards in the opponent's hand.</summary>
	public int Count => _count;

	public PlayerSide Side => _side;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ClipContents = false;
	}

	public void SetSide(PlayerSide side, Color accent)
	{
		if (side != _side || _back is null)
		{
			_side = side;
			_back = TableCardControl.LoadBackTexture(side);
		}
		_accent = accent;
		QueueRedraw();
	}

	/// <summary>Sets the hand size. New cards slide in from the side; the fan re-centres smoothly.</summary>
	public void SetCount(int count)
	{
		count = Math.Max(0, count);
		if (count == _count) return;
		_count = count;
		while (_offsets.Count < count) _offsets.Add(_offsets.Count == 0 ? 0 : _offsets[^1]);
		while (_offsets.Count > count) _offsets.RemoveAt(_offsets.Count - 1);
		QueueRedraw();
	}

	/// <summary>
	/// Hides the last <paramref name="cards"/> slots while a card is in flight
	/// toward them, so a drawn card appears once it lands.
	/// </summary>
	public void HoldArrivals(int cards)
	{
		_hidden = Math.Clamp(cards, 0, _count);
		QueueRedraw();
	}

	public void ReleaseArrival()
	{
		if (_hidden > 0) _hidden--;
		QueueRedraw();
	}

	/// <summary>The full rectangle of slot <paramref name="index"/> (mostly above the top edge), in global coordinates.</summary>
	public Rect2 SlotGlobalRect(int index)
	{
		int count = Math.Max(1, _count);
		index = Math.Clamp(index, 0, count - 1);
		Vector2 local = SlotHome(index, count);
		return new Rect2(GetGlobalTransform() * local, new Vector2(CardWidth, CardHeight));
	}

	/// <summary>Where a card leaving the hand starts: the slot's visible sliver pulled down to full view.</summary>
	public Rect2 LaunchGlobalRect(int index)
	{
		Rect2 slot = SlotGlobalRect(index);
		return slot with { Position = slot.Position + new Vector2(0, CardHeight * (1 - VisibleFraction)) };
	}

	private float Width(int count) => CardWidth + CardStep * Math.Max(0, count - 1);

	private Vector2 SlotHome(int index, int count)
	{
		float start = (Size.X - Width(count)) / 2;
		return new Vector2(start + index * CardStep, CardHeight * VisibleFraction - CardHeight);
	}

	public override void _Process(double delta)
	{
		float weight = ReducedMotion || MotionSeconds <= 0 ? 1 : 1 - Mathf.Exp(-(float)delta / MotionSeconds);
		bool moving = false;
		for (int i = 0; i < _offsets.Count; i++)
		{
			float target = SlotHome(i, _count).X;
			float next = Mathf.Lerp(_offsets[i], target, weight);
			if (Mathf.Abs(next - target) < 0.3f) next = target;
			moving |= !Mathf.IsEqualApprox(next, _offsets[i]);
			_offsets[i] = next;
		}
		if (moving) QueueRedraw();
	}

	public override void _Draw()
	{
		int shown = _count - _hidden;
		Vector2 size = new(CardWidth, CardHeight);
		for (int i = 0; i < shown; i++)
		{
			float x = i < _offsets.Count ? _offsets[i] : SlotHome(i, _count).X;
			float t = _count < 2 ? 0 : 2f * i / (_count - 1) - 1;
			float angle = Mathf.DegToRad(-FanAngle * t);
			Vector2 home = new(x, SlotHome(i, _count).Y);
			// Pivot high above the card so the fan opens downward from the table edge.
			Vector2 pivot = home + new Vector2(CardWidth / 2, -CardHeight * 0.6f);
			DrawSetTransform(pivot, angle, Vector2.One);
			Rect2 rect = new(home - pivot, size);
			DrawRect(new Rect2(rect.Position + new Vector2(0, 3), rect.Size), new Color(0, 0, 0, 0.45f));
			if (_back is not null) DrawTextureRect(_back, rect, false);
			else DrawRect(rect, new Color(0.07f, 0.09f, 0.11f));
			DrawRect(rect, _accent with { A = 0.7f }, false, 1);
		}
		DrawSetTransform(Vector2.Zero, 0, Vector2.One);
	}
}
