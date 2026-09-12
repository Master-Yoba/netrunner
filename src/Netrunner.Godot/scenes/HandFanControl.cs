using Godot;

namespace Netrunner.GodotClient;

/// <summary>Presentation-only hand fan with stable hit targets during hover motion.</summary>
[Tool]
public partial class HandFanControl : Control
{
	[Export(PropertyHint.Range, "20,180,1")] public float CardStep { get; set; } = 112;
	[Export(PropertyHint.Range, "0,15,0.5")] public float FanAngle { get; set; } = 6;
	[Export(PropertyHint.Range, "0,50,1")] public float ArcHeight { get; set; } = 14;
	[Export(PropertyHint.Range, "0,80,1")] public float BottomPeek { get; set; } = 24;
	[Export(PropertyHint.Range, "0,100,1")] public float HoverLift { get; set; } = 44;
	[Export(PropertyHint.Range, "1,1.4,0.01")] public float HoverScale { get; set; } = 1.08f;
	[Export(PropertyHint.Range, "0,0.3,0.01")] public float MotionSeconds { get; set; } = 0.055f;
	public bool ReducedMotion { get; set; }
	public bool DragActive { get; set; }
	private readonly List<HandEntry> _entries = new();
	private TableCardControl? _hovered;
	public string? HoveredCardId => _hovered?.CardId;
	public bool ContainsCard(TableCardControl card) => _entries.Any(entry => ReferenceEquals(entry.Card, card));

	public void SetCards(IReadOnlyList<TableCardControl> cards)
	{
		foreach (HandEntry old in _entries)
			if (!cards.Contains(old.Card) && IsInstanceValid(old.Card)) old.Card.PointerHitTest = null;
		_entries.Clear();
		if (_hovered is not null && !cards.Contains(_hovered)) _hovered = null;
		foreach (TableCardControl card in cards)
		{
			_entries.Add(new(card));
			card.PointerHitTest = global => ReferenceEquals(CardAt(global), card);
		}
		GetNode<Label>("Count").Text = cards.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
		GetNode<Label>("Empty").Visible = cards.Count == 0;
		LayoutCards(0);
	}

	public void Hover(TableCardControl card, bool active)
	{
		if (!ContainsCard(card)) return;
		if (active && !DragActive) _hovered = card;
		else if (ReferenceEquals(_hovered, card)) _hovered = null;
	}

	public override void _Process(double delta) => LayoutCards(delta);

	public override void _EnterTree()
	{
		foreach (HandEntry entry in _entries)
		{
			TableCardControl card = entry.Card;
			card.PointerHitTest = global => ReferenceEquals(CardAt(global), card);
		}
	}

	public override void _ExitTree()
	{
		foreach (HandEntry entry in _entries)
			if (IsInstanceValid(entry.Card)) entry.Card.PointerHitTest = null;
	}

	private void LayoutCards(double delta)
	{
		if (DragActive) _hovered = null;
		float width = _entries.Count == 0 ? 150 : _entries.Max(entry => entry.Card.Size.X);
		float available = Mathf.Max(width, Size.X - 112);
		float step = _entries.Count < 2 ? 0 : Mathf.Min(CardStep, (available - width) / (_entries.Count - 1));
		float start = Size.X - 88 - width - step * Math.Max(0, _entries.Count - 1);
		float weight = ReducedMotion || Engine.IsEditorHint() || MotionSeconds <= 0 || delta <= 0
			? 1 : 1 - Mathf.Exp(-(float)delta / MotionSeconds);
		for (int i = 0; i < _entries.Count; i++)
		{
			HandEntry entry = _entries[i];
			TableCardControl card = entry.Card;
			float t = _entries.Count < 2 ? 0 : 2f * i / (_entries.Count - 1) - 1;
			float angle = Mathf.DegToRad(FanAngle * t);
			Vector2 home = new(start + i * step, Size.Y - card.Size.Y + BottomPeek + ArcHeight * t * t);
			Vector2 pivot = card.Size * new Vector2(0.5f, 1);
			entry.Home = new Transform2D(angle, home + pivot - pivot.Rotated(angle));
			bool hovered = ReferenceEquals(_hovered, card) && card.Card?.IsKnown == true;
			card.PivotOffset = pivot;
			card.Position = card.Position.Lerp(home - new Vector2(0, hovered ? HoverLift : 0), weight);
			card.Rotation = Mathf.LerpAngle(card.Rotation, hovered ? 0 : angle, weight);
			card.Scale = card.Scale.Lerp(Vector2.One * (hovered ? HoverScale : 1), weight);
			card.ZIndex = hovered ? 30 : i;
		}
	}

	private TableCardControl? CardAt(Vector2 global)
	{
		Vector2 point = GetGlobalTransform().AffineInverse() * global;
		for (int i = _entries.Count - 1; i >= 0; i--)
		{
			HandEntry entry = _entries[i];
			if (new Rect2(Vector2.Zero, entry.Card.Size).HasPoint(entry.Home.AffineInverse() * point)) return entry.Card;
		}
		// Resting footprints win over the fan; the lifted card remains reachable above it.
		return _hovered is { } raised && new Rect2(Vector2.Zero, raised.Size)
			.HasPoint(raised.GetGlobalTransform().AffineInverse() * global) ? raised : null;
	}

	private sealed class HandEntry(TableCardControl card)
	{
		public TableCardControl Card { get; } = card;
		public Transform2D Home { get; set; } = Transform2D.Identity;
	}
}
