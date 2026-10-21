using Godot;

namespace Netrunner.GodotClient;

/// <summary>Shared presentation sizing for the passive hover card scene.</summary>
[Tool]
public partial class CardPreviewLayout : PanelContainer
{
	private TableCardControl? _face;
	private CardAppearance? _observedAppearance;
	private float _portraitScale = 1.6f;
	private float _iceScale = 4f / 3f;

	[Export]
	public Vector2 PreferredSize { get; set; } = new(640, 400);

	[Export(PropertyHint.Range, "0,40,1")]
	public float ViewportMargin { get; set; } = 8;

	[Export(PropertyHint.Range, "0,40,1")]
	public float SourceGap { get; set; } = 12;

	[Export(PropertyHint.Range, "1,3,0.05")]
	public float PortraitScale
	{
		get => _portraitScale;
		set { _portraitScale = Mathf.Clamp(value, 1, 3); RefreshCardSize(); }
	}

	[Export(PropertyHint.Range, "1,3,0.05")]
	public float IceScale
	{
		get => _iceScale;
		set { _iceScale = Mathf.Clamp(value, 1, 3); RefreshCardSize(); }
	}

	public override void _Ready()
	{
		_face = GetNode<TableCardControl>("%Card");
		_face.AppearanceReplaced += HandleAppearanceReplaced;
		ObserveAppearance();
		RefreshCardSize();
	}

	public override void _EnterTree()
	{
		if (_face is not null)
		{
			_face.AppearanceReplaced += HandleAppearanceReplaced;
			ObserveAppearance();
			RefreshCardSize();
		}
	}

	public override void _ExitTree()
	{
		if (_face is not null) _face.AppearanceReplaced -= HandleAppearanceReplaced;
		if (_observedAppearance is not null) _observedAppearance.Changed -= RefreshCardSize;
		_observedAppearance = null;
	}

	public void RefreshCardSize()
	{
		if (_face is null || !IsInstanceValid(_face)) return;
		ObserveAppearance();
		_face.SetDisplaySize(_face.IsIce ? _face.Appearance.IceSize * IceScale : _face.Appearance.PortraitSize * PortraitScale);
	}

	private void HandleAppearanceReplaced(TableCardControl _) => RefreshCardSize();

	private void ObserveAppearance()
	{
		if (!IsInsideTree() || _face is null || ReferenceEquals(_observedAppearance, _face.Appearance)) return;
		if (_observedAppearance is not null) _observedAppearance.Changed -= RefreshCardSize;
		_observedAppearance = _face.Appearance;
		_observedAppearance.Changed += RefreshCardSize;
	}
}
