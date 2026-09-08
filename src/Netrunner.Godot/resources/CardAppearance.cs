using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// Shared, editor-authored visual values for a <see cref="TableCardControl"/>.
///
/// This resource deliberately contains presentation values only.  A card
/// control may share one instance with any number of cards; per-card state is
/// kept by the control and never written back to this resource.
/// </summary>
[Tool]
[GlobalClass]
public partial class CardAppearance : Resource
{
	private Vector2 _portraitSize = new(150, 210);
	private Vector2 _iceSize = new(240, 108);
	private float _cornerRadius = 7.0f;
	private float _frameWidth = 1.0f;
	private float _emphasisFrameWidth = 2.0f;

	private int _titleFontSize = 10;
	private int _typeFontSize = 8;
	private int _statsFontSize = 8;
	private int _compactStatsFontSize = 9;
	private int _rulesFontSize = 7;
	private int _statusFontSize = 7;
	private int _counterFontSize = 7;
	private Color _titleColor = Colors.White;
	private Color _typeColor = new(0.80f, 0.88f, 0.92f);
	private Color _statsColor = new(0.91f, 0.94f, 0.98f);
	private Color _rulesColor = new(0.66f, 0.73f, 0.79f);
	private Color _statusColor = new(0.74f, 0.81f, 0.86f);
	private Color _counterColor = new(1.0f, 0.83f, 0.39f);

	private Color _surfaceColor = new(0.055f, 0.075f, 0.11f);
	private Color _hiddenSurfaceColor = new(0.075f, 0.10f, 0.15f);
	private Color _frameColor = new(0.39f, 0.47f, 0.55f);
	private Color _focusColor = new(0.46f, 0.95f, 0.82f);
	private Color _selectedColor = new(0.34f, 0.70f, 1.0f);
	private Color _targetColor = new(0.39f, 0.92f, 0.82f);
	private Color _actionableColor = new(0.96f, 0.99f, 1.0f);
	private Color _hiddenColor = new(0.31f, 0.39f, 0.48f);
	private Color _counterBackground = new(0.20f, 0.14f, 0.08f, 0.94f);
	private Color _counterBorderColor = new(0.80f, 0.57f, 0.20f, 0.75f);
	private Color _statusBackground = new(0.08f, 0.12f, 0.17f, 0.88f);
	private Color _hiddenStatusBackground = new(0.07f, 0.09f, 0.13f, 0.92f);

	private Color _anarchAccent = new(0.82f, 0.30f, 0.42f);
	private Color _criminalAccent = new(0.30f, 0.55f, 0.92f);
	private Color _shaperAccent = new(0.35f, 0.78f, 0.63f);
	private Color _haasAccent = new(0.74f, 0.42f, 0.84f);
	private Color _jintekiAccent = new(0.93f, 0.38f, 0.42f);
	private Color _nbnAccent = new(0.95f, 0.66f, 0.25f);
	private Color _weylandAccent = new(0.38f, 0.68f, 0.47f);
	private Color _iceAccent = new(0.31f, 0.67f, 0.80f);
	private Color _defaultAccent = new(0.61f, 0.68f, 0.75f);

	private float _headerDarken = 0.42f;
	private float _headerLighten = 0.12f;
	private float _artworkOpacity = 0.87f;
	private float _motifOpacityWithArtwork = 0.26f;
	private float _motifOpacityWithoutArtwork = 0.72f;
	private float _motifFillOpacity = 0.16f;
	private float _backInsetOpacity = 0.22f;
	private float _backCenterOpacity = 0.75f;
	private float _backRingOpacity = 0.72f;
	private float _targetBadgeOpacity = 0.18f;
	private float _selectedBadgeOpacity = 0.20f;
	private float _statusBadgeOpacity = 0.16f;
	private float _motifLineWidth = 1.3f;
	private float _backLineWidth = 1.3f;
	private float _backCrossWidth = 2.0f;

	private float _glowOuterExtent = 16.0f;
	private float _glowInnerExtent = 12.0f;
	private float _glowOpacity = 0.65f;
	private float _glowFalloff = 2.0f;
	private float _frameShadowOpacity = 0.33f;

	[ExportGroup("Card sizing")]
	[Export]
	public Vector2 PortraitSize
	{
		get => _portraitSize;
		set => SetVector2(ref _portraitSize, ClampSize(value));
	}

	[Export]
	public Vector2 IceSize
	{
		get => _iceSize;
		set => SetVector2(ref _iceSize, ClampSize(value));
	}

	[Export(PropertyHint.Range, "0,32,0.5")]
	public float CornerRadius
	{
		get => _cornerRadius;
		set => SetFloat(ref _cornerRadius, Mathf.Clamp(value, 0.0f, 32.0f));
	}

	[Export(PropertyHint.Range, "1,8,1")]
	public float FrameWidth
	{
		get => _frameWidth;
		set => SetFloat(ref _frameWidth, Mathf.Clamp(value, 1.0f, 8.0f));
	}

	[Export(PropertyHint.Range, "1,8,1")]
	public float EmphasisFrameWidth
	{
		get => _emphasisFrameWidth;
		set => SetFloat(ref _emphasisFrameWidth, Mathf.Clamp(value, 1.0f, 8.0f));
	}

	[ExportGroup("Label typography")]
	[Export(PropertyHint.Range, "6,32,1")]
	public int TitleFontSize
	{
		get => _titleFontSize;
		set => SetInt(ref _titleFontSize, Mathf.Clamp(value, 6, 32));
	}

	[Export(PropertyHint.Range, "6,32,1")]
	public int TypeFontSize
	{
		get => _typeFontSize;
		set => SetInt(ref _typeFontSize, Mathf.Clamp(value, 6, 32));
	}

	[Export(PropertyHint.Range, "6,32,1")]
	public int StatsFontSize
	{
		get => _statsFontSize;
		set => SetInt(ref _statsFontSize, Mathf.Clamp(value, 6, 32));
	}

	[Export(PropertyHint.Range, "6,32,1")]
	public int CompactStatsFontSize
	{
		get => _compactStatsFontSize;
		set => SetInt(ref _compactStatsFontSize, Mathf.Clamp(value, 6, 32));
	}

	[Export(PropertyHint.Range, "6,32,1")]
	public int RulesFontSize
	{
		get => _rulesFontSize;
		set => SetInt(ref _rulesFontSize, Mathf.Clamp(value, 6, 32));
	}

	[Export(PropertyHint.Range, "6,32,1")]
	public int StatusFontSize
	{
		get => _statusFontSize;
		set => SetInt(ref _statusFontSize, Mathf.Clamp(value, 6, 32));
	}

	[Export(PropertyHint.Range, "6,32,1")]
	public int CounterFontSize
	{
		get => _counterFontSize;
		set => SetInt(ref _counterFontSize, Mathf.Clamp(value, 6, 32));
	}

	[Export]
	public Color TitleColor
	{
		get => _titleColor;
		set => SetColor(ref _titleColor, value);
	}

	[Export]
	public Color TypeColor
	{
		get => _typeColor;
		set => SetColor(ref _typeColor, value);
	}

	[Export]
	public Color StatsColor
	{
		get => _statsColor;
		set => SetColor(ref _statsColor, value);
	}

	[Export]
	public Color RulesColor
	{
		get => _rulesColor;
		set => SetColor(ref _rulesColor, value);
	}

	[Export]
	public Color StatusColor
	{
		get => _statusColor;
		set => SetColor(ref _statusColor, value);
	}

	[Export]
	public Color CounterColor
	{
		get => _counterColor;
		set => SetColor(ref _counterColor, value);
	}

	[ExportGroup("Card colors")]
	[Export]
	public Color SurfaceColor
	{
		get => _surfaceColor;
		set => SetColor(ref _surfaceColor, value);
	}

	[Export]
	public Color HiddenSurfaceColor
	{
		get => _hiddenSurfaceColor;
		set => SetColor(ref _hiddenSurfaceColor, value);
	}

	[Export]
	public Color FrameColor
	{
		get => _frameColor;
		set => SetColor(ref _frameColor, value);
	}

	[Export]
	public Color FocusColor
	{
		get => _focusColor;
		set => SetColor(ref _focusColor, value);
	}

	[Export]
	public Color SelectedColor
	{
		get => _selectedColor;
		set => SetColor(ref _selectedColor, value);
	}

	[Export]
	public Color TargetColor
	{
		get => _targetColor;
		set => SetColor(ref _targetColor, value);
	}

	[Export]
	public Color ActionableColor
	{
		get => _actionableColor;
		set => SetColor(ref _actionableColor, value);
	}

	[Export]
	public Color HiddenColor
	{
		get => _hiddenColor;
		set => SetColor(ref _hiddenColor, value);
	}

	[Export]
	public Color StatusBackground
	{
		get => _statusBackground;
		set => SetColor(ref _statusBackground, value);
	}

	[Export]
	public Color HiddenStatusBackground
	{
		get => _hiddenStatusBackground;
		set => SetColor(ref _hiddenStatusBackground, value);
	}

	[Export]
	public Color CounterBackground
	{
		get => _counterBackground;
		set => SetColor(ref _counterBackground, value);
	}

	[Export]
	public Color CounterBorderColor
	{
		get => _counterBorderColor;
		set => SetColor(ref _counterBorderColor, value);
	}

	[ExportGroup("Faction accents")]
	[Export] public Color AnarchAccent { get => _anarchAccent; set => SetColor(ref _anarchAccent, value); }
	[Export] public Color CriminalAccent { get => _criminalAccent; set => SetColor(ref _criminalAccent, value); }
	[Export] public Color ShaperAccent { get => _shaperAccent; set => SetColor(ref _shaperAccent, value); }
	[Export] public Color HaasAccent { get => _haasAccent; set => SetColor(ref _haasAccent, value); }
	[Export] public Color JintekiAccent { get => _jintekiAccent; set => SetColor(ref _jintekiAccent, value); }
	[Export] public Color NbnAccent { get => _nbnAccent; set => SetColor(ref _nbnAccent, value); }
	[Export] public Color WeylandAccent { get => _weylandAccent; set => SetColor(ref _weylandAccent, value); }
	[Export] public Color IceAccent { get => _iceAccent; set => SetColor(ref _iceAccent, value); }
	[Export] public Color DefaultAccent { get => _defaultAccent; set => SetColor(ref _defaultAccent, value); }

	[ExportGroup("Drawing")]
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float HeaderDarken
	{
		get => _headerDarken;
		set => SetFloat(ref _headerDarken, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float HeaderLighten
	{
		get => _headerLighten;
		set => SetFloat(ref _headerLighten, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float ArtworkOpacity
	{
		get => _artworkOpacity;
		set => SetFloat(ref _artworkOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float MotifOpacityWithArtwork
	{
		get => _motifOpacityWithArtwork;
		set => SetFloat(ref _motifOpacityWithArtwork, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float MotifOpacityWithoutArtwork
	{
		get => _motifOpacityWithoutArtwork;
		set => SetFloat(ref _motifOpacityWithoutArtwork, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float MotifFillOpacity
	{
		get => _motifFillOpacity;
		set => SetFloat(ref _motifFillOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float BackInsetOpacity
	{
		get => _backInsetOpacity;
		set => SetFloat(ref _backInsetOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float BackCenterOpacity
	{
		get => _backCenterOpacity;
		set => SetFloat(ref _backCenterOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float BackRingOpacity
	{
		get => _backRingOpacity;
		set => SetFloat(ref _backRingOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float TargetBadgeOpacity
	{
		get => _targetBadgeOpacity;
		set => SetFloat(ref _targetBadgeOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float SelectedBadgeOpacity
	{
		get => _selectedBadgeOpacity;
		set => SetFloat(ref _selectedBadgeOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float StatusBadgeOpacity
	{
		get => _statusBadgeOpacity;
		set => SetFloat(ref _statusBadgeOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0.5,8,0.1")]
	public float MotifLineWidth
	{
		get => _motifLineWidth;
		set => SetFloat(ref _motifLineWidth, Mathf.Clamp(value, 0.5f, 8.0f));
	}

	[Export(PropertyHint.Range, "0.5,8,0.1")]
	public float BackLineWidth
	{
		get => _backLineWidth;
		set => SetFloat(ref _backLineWidth, Mathf.Clamp(value, 0.5f, 8.0f));
	}

	[Export(PropertyHint.Range, "0.5,8,0.1")]
	public float BackCrossWidth
	{
		get => _backCrossWidth;
		set => SetFloat(ref _backCrossWidth, Mathf.Clamp(value, 0.5f, 8.0f));
	}

	[ExportGroup("Action glow")]
	[Export(PropertyHint.Range, "0,48,1")]
	public float GlowOuterExtent
	{
		get => _glowOuterExtent;
		set => SetFloat(ref _glowOuterExtent, Mathf.Clamp(value, 0.0f, 48.0f));
	}

	[Export(PropertyHint.Range, "0,48,1")]
	public float GlowInnerExtent
	{
		get => _glowInnerExtent;
		set => SetFloat(ref _glowInnerExtent, Mathf.Clamp(value, 0.0f, 48.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float GlowOpacity
	{
		get => _glowOpacity;
		set => SetFloat(ref _glowOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[Export(PropertyHint.Range, "0.1,8,0.1")]
	public float GlowFalloff
	{
		get => _glowFalloff;
		set => SetFloat(ref _glowFalloff, Mathf.Clamp(value, 0.1f, 8.0f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float FrameShadowOpacity
	{
		get => _frameShadowOpacity;
		set => SetFloat(ref _frameShadowOpacity, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	/// <summary>Compatibility alias for tools that call the inner glow width.</summary>
	public float GlowWidth
	{
		get => GlowInnerExtent;
		set => GlowInnerExtent = value;
	}

	private static Vector2 ClampSize(Vector2 value) => new(
		Mathf.Clamp(value.X, 32.0f, 960.0f),
		Mathf.Clamp(value.Y, 32.0f, 960.0f));

	private void SetVector2(ref Vector2 field, Vector2 value)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		EmitChanged();
	}

	private void SetFloat(ref float field, float value)
	{
		if (Mathf.IsEqualApprox(field, value))
		{
			return;
		}

		field = value;
		EmitChanged();
	}

	private void SetInt(ref int field, int value)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		EmitChanged();
	}

	private void SetColor(ref Color field, Color value)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		EmitChanged();
	}
}
