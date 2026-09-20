using System.Text.Json;
using Godot;
using Netrunner.Cards;

namespace Netrunner.GodotClient;

/// <summary>Session-local, lazy texture sharing. Only call with a viewer-known definition.</summary>
internal sealed class LocalCardArtworkResolver
{
	private static LocalCardArtworkResolver? _shared;
	internal static LocalCardArtworkResolver Shared => _shared ??= new(CardArtworkCache.ResolveDirectory());
	private readonly string _directory;
	private readonly Dictionary<string, CardArtworkEntry> _entries = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Artwork?> _textures = new(StringComparer.Ordinal);
	internal int LoadCount { get; private set; }

	internal LocalCardArtworkResolver(string directory)
	{
		_directory = directory;
		try
		{
			if (!File.Exists(CardArtworkCache.SafePath(directory, CardArtworkCache.ManifestName))) return;
			foreach (CardArtworkEntry entry in CardArtworkCache.ReadManifest(directory).Cards)
				_entries.Add(entry.DefinitionId, entry);
		}
		catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
		{
			GD.PushWarning($"Card artwork cache unavailable: {error.Message}");
		}
	}

	internal Artwork? TryLoad(string definitionId)
	{
		if (_textures.TryGetValue(definitionId, out Artwork? cached)) return cached;
		Artwork? result = null;
		try
		{
			if (_entries.TryGetValue(definitionId, out CardArtworkEntry? entry))
			{
				LoadCount++;
				byte[] bytes = CardArtworkCache.ReadImage(_directory, entry);
				using var image = new Image();
				Error status = Path.GetExtension(entry.Path) switch
				{
					".png" => image.LoadPngFromBuffer(bytes),
					".jpg" => image.LoadJpgFromBuffer(bytes),
					".webp" => image.LoadWebpFromBuffer(bytes),
					_ => Error.FileUnrecognized,
				};
				if (status == Error.Ok && !image.IsEmpty() && image.GetWidth() <= 4096 && image.GetHeight() <= 4096)
					result = new Artwork(ImageTexture.CreateFromImage(image), true);
			}
		}
		catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
		{
			GD.PushWarning($"Card artwork {definitionId} unavailable: {error.Message}");
		}
		result ??= TryLoadLegacy(definitionId);
		_textures[definitionId] = result;
		return result;
	}

	private static Artwork? TryLoadLegacy(string definitionId)
	{
		string code = new(definitionId.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
		if (code.Length == 0) return null;
		string path = $"user://card-art/{code}.png";
		if (!Godot.FileAccess.FileExists(path)) return null;
		using Image? image = Image.LoadFromFile(path);
		return image is null || image.IsEmpty() ? null : new Artwork(ImageTexture.CreateFromImage(image), false);
	}

	internal sealed record Artwork(ImageTexture Texture, bool FullFace);
}
