using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// Editor-authored geometry shared by the board and its dynamic zones.
///
/// The values describe layout intent rather than card data. Card dimensions
/// are read from <see cref="CardAppearance"/> and the compact scales below
/// preserve the same proportion when an author changes that resource.
/// </summary>
[Tool]
[GlobalClass]
public partial class BoardLayout : Resource
{
	private float _laneWidth = 285.0f;
	private float _minimumBoardWidth = 760.0f;
	private float _regionSpacing = 9.0f;
	private float _handOverlap = 36.0f;
	private float _cardSeparation = 8.0f;
	private float _rootCardOverlap = 27.0f;
	private Vector4 _outerMargins = new(14.0f, 4.0f, 14.0f, 4.0f);
	private float _compactPortraitScale = 0.70f;
	private float _iceScale = 87.0f / 108.0f;
	private float _hostedPortraitScale = 0.60f;
	private Vector2 _hostedCardOffset = new(33.0f, 27.0f);
	private Vector2 _hostedCardStep = new(20.0f, 11.0f);
	private float _serverFieldMinimumHeight = 150.0f;
	private float _runnerFieldMinimumHeight = 200.0f;

	[ExportGroup("Board regions")]
	[Export(PropertyHint.Range, "180,640,1")]
	public float LaneWidth
	{
		get => _laneWidth;
		set => SetFloat(ref _laneWidth, Mathf.Clamp(value, 180.0f, 640.0f));
	}

	[Export(PropertyHint.Range, "640,3840,10")]
	public float MinimumBoardWidth
	{
		get => _minimumBoardWidth;
		set => SetFloat(ref _minimumBoardWidth, Mathf.Clamp(value, 640.0f, 3840.0f));
	}

	[Export(PropertyHint.Range, "0,48,1")]
	public float RegionSpacing
	{
		get => _regionSpacing;
		set => SetFloat(ref _regionSpacing, Mathf.Clamp(value, 0.0f, 48.0f));
	}

	[Export(PropertyHint.Range, "0,140,1")]
	public float HandOverlap
	{
		get => _handOverlap;
		set => SetFloat(ref _handOverlap, Mathf.Clamp(value, 0.0f, 140.0f));
	}

	[Export(PropertyHint.Range, "0,48,1")]
	public float CardSeparation
	{
		get => _cardSeparation;
		set => SetFloat(ref _cardSeparation, Mathf.Clamp(value, 0.0f, 48.0f));
	}

	[Export(PropertyHint.Range, "0,140,1")]
	public float RootCardOverlap
	{
		get => _rootCardOverlap;
		set => SetFloat(ref _rootCardOverlap, Mathf.Clamp(value, 0.0f, 140.0f));
	}

	/// <summary>Left, top, right and bottom margins in pixels.</summary>
	[Export]
	public Vector4 Margins
	{
		get => _outerMargins;
		set => SetVector4(ref _outerMargins, new(
			Mathf.Clamp(value.X, 0.0f, 96.0f),
			Mathf.Clamp(value.Y, 0.0f, 96.0f),
			Mathf.Clamp(value.Z, 0.0f, 96.0f),
			Mathf.Clamp(value.W, 0.0f, 96.0f)));
	}

	/// <summary>Descriptive alias retained for callers that name board edges.</summary>
	public Vector4 OuterMargins
	{
		get => Margins;
		set => Margins = value;
	}

	[Export(PropertyHint.Range, "80,360,1")]
	public float ServerFieldMinimumHeight
	{
		get => _serverFieldMinimumHeight;
		set => SetFloat(ref _serverFieldMinimumHeight, Mathf.Clamp(value, 80.0f, 360.0f));
	}

	[Export(PropertyHint.Range, "120,360,1")]
	public float RunnerFieldMinimumHeight
	{
		get => _runnerFieldMinimumHeight;
		set => SetFloat(ref _runnerFieldMinimumHeight, Mathf.Clamp(value, 120.0f, 360.0f));
	}

	[ExportGroup("Card footprint intent")]
	/// <summary>
	/// Compact portrait cards use this scale of CardAppearance.PortraitSize.
	/// Keeping this as a scale makes Inspector card-size edits propagate to
	/// lanes, identities and piles without a second absolute card size.
	/// </summary>
	[Export(PropertyHint.Range, "0.25,1,0.01")]
	public float CompactPortraitScale
	{
		get => _compactPortraitScale;
		set => SetFloat(ref _compactPortraitScale, Mathf.Clamp(value, 0.25f, 1.0f));
	}

	/// <summary>ICE keeps the shared card width and compresses its height for a server lane.</summary>
	[Export(PropertyHint.Range, "0.25,1,0.01")]
	public float IceHeightScale
	{
		get => _iceScale;
		set => SetFloat(ref _iceScale, Mathf.Clamp(value, 0.25f, 1.0f));
	}

	/// <summary>Cards hosted on another card are intentionally smaller.</summary>
	[Export(PropertyHint.Range, "0.25,1,0.01")]
	public float HostedPortraitScale
	{
		get => _hostedPortraitScale;
		set => SetFloat(ref _hostedPortraitScale, Mathf.Clamp(value, 0.25f, 1.0f));
	}

	[Export]
	public Vector2 HostedCardOffset
	{
		get => _hostedCardOffset;
		set => SetVector2(ref _hostedCardOffset, ClampVector(value));
	}

	[Export]
	public Vector2 HostedCardStep
	{
		get => _hostedCardStep;
		set => SetVector2(ref _hostedCardStep, ClampVector(value));
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

	private void SetVector2(ref Vector2 field, Vector2 value)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		EmitChanged();
	}

	private void SetVector4(ref Vector4 field, Vector4 value)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		EmitChanged();
	}

	private static Vector2 ClampVector(Vector2 value) => new(
		Mathf.Clamp(value.X, -512.0f, 512.0f),
		Mathf.Clamp(value.Y, -512.0f, 512.0f));
}
