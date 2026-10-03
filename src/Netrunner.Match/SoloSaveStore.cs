using System.Text.Json;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record SoloSaveBrowserEntry(string FileName, SoloSavePreview? Preview, bool IsCompatible,
    string? Error, SoloSavePreview? Backup);

/// <summary>App-owned durable saves. Files are authoritative; browser failures are isolated per file.</summary>
public sealed class SoloSaveStore : IDisposable
{
    public const long MaximumFileBytes = 64 * 1024 * 1024;
    private readonly Func<string?, (IBehaviorCatalog Behavior, IRuntimeCardCatalog Runtime)> _catalogs;
    private readonly SemaphoreSlim _writes = new(1, 1);

    public SoloSaveStore(IBehaviorCatalog behavior, IRuntimeCardCatalog runtime, string? directory = null)
        : this(_ => (behavior, runtime), directory)
    {
    }

    /// <summary>
    /// A store whose saves validate against the catalogs of their own format
    /// (D-EL-8); <paramref name="catalogs"/> throws for a format this build
    /// can't play, which marks the save incompatible.
    /// </summary>
    public SoloSaveStore(Func<string?, (IBehaviorCatalog Behavior, IRuntimeCardCatalog Runtime)> catalogs, string? directory = null)
    {
        _catalogs = catalogs;
        DirectoryPath = Path.GetFullPath(directory ?? DefaultDirectory);
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetrunnerSolo", "saves");
    public string DirectoryPath { get; }

    public async Task WriteAsync(SoloSaveDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                Validate(document);
                byte[] bytes = EngineJson.SerializeUtf8(document);
                if (bytes.LongLength > MaximumFileBytes) throw new IOException("The save exceeds the supported file size.");
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(DirectoryPath);
                string path = PathFor(document.Metadata.SaveId);
                string? backup = null;
                if (File.Exists(path))
                {
                    // Never replace a verified backup with a corrupt primary.
                    try { Validate(Read(path)); backup = path + ".bak"; }
                    catch (Exception exception) when (IsReadFailure(exception)) { }
                }
                AtomicFile.Write(path, bytes, temporary => Read(temporary).ValidateIntegrity(), backup);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    public Task<SoloSaveDocument> LoadAsync(string saveId, bool backup = false, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            SoloSaveDocument document = Read(PathFor(saveId) + (backup ? ".bak" : ""));
            Validate(document);
            return document;
        }, cancellationToken);

    public Task<IReadOnlyList<SoloSaveBrowserEntry>> BrowseAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SoloSaveBrowserEntry>>(() =>
        {
            if (!Directory.Exists(DirectoryPath)) return [];
            var entries = new List<SoloSaveBrowserEntry>();
            foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*.solo.json").Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SoloSavePreview? preview = null;
                SoloSavePreview? backup = null;
                string? error = null;
                try
                {
                    SoloSaveDocument document = Read(path);
                    document.ValidateIntegrity();
                    preview = document.Preview;
                    Validate(document);
                }
                catch (Exception exception) when (IsReadFailure(exception))
                {
                    error = exception is UnauthorizedAccessException
                        ? "This save cannot be read with the current file permissions."
                        : "This save is corrupt or incompatible with the installed game content and policies.";
                }
                if (error is not null && File.Exists(path + ".bak"))
                {
                    try
                    {
                        SoloSaveDocument previous = Read(path + ".bak");
                        Validate(previous);
                        backup = previous.Preview;
                    }
                    catch (Exception exception) when (IsReadFailure(exception)) { }
                }
                entries.Add(new(Path.GetFileName(path), preview, error is null, error, backup));
            }
            return entries.OrderByDescending(entry => entry.Preview?.UpdatedUtc ?? entry.Backup?.UpdatedUtc).ToArray();
        }, cancellationToken);

    public static SoloSaveDocument Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > MaximumFileBytes) throw new IOException("Invalid or oversized solo save file.");
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new IOException("The save changed while it was being read.");
        return EngineJson.Deserialize<SoloSaveDocument>(bytes);
    }

    public void Dispose() => _writes.Dispose();

    private void Validate(SoloSaveDocument document)
    {
        (IBehaviorCatalog behavior, IRuntimeCardCatalog runtime) = _catalogs(document.Metadata?.FormatId);
        document.Validate(behavior, runtime);
    }

    private string PathFor(string saveId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveId);
        if (saveId.Length != 32 || !Guid.TryParseExact(saveId, "N", out _)) throw new ArgumentException("Invalid save ID.", nameof(saveId));
        return Path.Combine(DirectoryPath, saveId + ".solo.json");
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException;
}
