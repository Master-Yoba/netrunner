using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record SoloSaveReceipt(SoloSavePreview Preview, long ActionCount);
public sealed record LoadedSoloSession(MatchCoordinator Coordinator, SoloSavePreview Preview);

/// <summary>What a saved game of one format loads with: its catalogs and its match composer.</summary>
public sealed record SoloSaveContent(IBehaviorCatalog Behavior, IRuntimeCardCatalog Runtime,
    Func<MatchRequest, FullMatchDefinition> Compose);

/// <summary>
/// Client persistence facade. Private payloads never cross the presentation API.
/// All writes share one queue; switching sessions invalidates pending automatic captures.
/// </summary>
public sealed class SoloSessionService : IAsyncDisposable
{
    public const string AutoSaveId = "00000000000000000000000000000001";
    private readonly SoloSaveStore _store;
    private readonly Func<string?, SoloSaveContent> _content;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly object _gate = new();
    private MatchCoordinator? _active;
    private long _generation;
    private bool _autoPending;
    private bool _disposed;
    private Task _autoWorker = Task.CompletedTask;
    private long _autoWrittenGeneration = -1;
    private long _autoWrittenAction = -1;
    private volatile string? _autoSaveError;

    public SoloSessionService(IBehaviorCatalog behavior, IRuntimeCardCatalog runtime,
        Func<MatchRequest, FullMatchDefinition> compose, string? directory = null)
        : this(_ => new SoloSaveContent(behavior, runtime, compose), directory)
    {
    }

    /// <summary>A service whose saves load with the content of the format each records (D-EL-8).</summary>
    public SoloSessionService(Func<string?, SoloSaveContent> content, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _store = new(format =>
        {
            SoloSaveContent loaded = content(format);
            return (loaded.Behavior, loaded.Runtime);
        }, directory);
        _content = content;
    }

    public string DirectoryPath => _store.DirectoryPath;

    public void SetActiveSession(MatchCoordinator? coordinator)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active = coordinator;
            _generation++;
            _autoPending = false;
            AutoSaveError = null;
        }
    }

    /// <summary>Wraps the bot of a loaded LLM opponent's save in the LLM opponent again (see MatchCoordinatorOptions).</summary>
    public Func<OfflineAgent, FullMatchDefinition, IGameAgent?>? RestoreLlmOpponent { get; set; }

    public Task<IReadOnlyList<SoloSaveBrowserEntry>> BrowseAsync(CancellationToken cancellationToken = default) =>
        _store.BrowseAsync(cancellationToken);

    public async Task<LoadedSoloSession> LoadAsync(string saveId, bool backup = false,
        string? traceDirectory = null, bool enableDeveloperTools = false, CancellationToken cancellationToken = default)
    {
        SoloSaveDocument document = await _store.LoadAsync(saveId, backup, cancellationToken).ConfigureAwait(false);
        MatchCoordinator coordinator = await Task.Run(() => new MatchCoordinator(_content(document.Metadata.FormatId).Compose(document.Payload.Request), document,
            new MatchCoordinatorOptions
            {
                HumanSide = document.Metadata.HumanSide,
                EnableDeveloperTools = enableDeveloperTools,
                AutoPassPriority = true,
                AgentDelay = TimeSpan.FromMilliseconds(45),
                SessionTraceDirectory = traceDirectory,
                RestoreLlmOpponent = RestoreLlmOpponent,
            }), cancellationToken).ConfigureAwait(false);
        try
        {
            await coordinator.StartAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(coordinator, document.Preview);
        }
        catch
        {
            await coordinator.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Pauses at capture. The client settles presentation before confirming and continuing.</summary>
    public async Task<SoloSaveReceipt> SaveAsync(MatchCoordinator coordinator, string name,
        CancellationToken cancellationToken = default)
    {
        long generation = GenerationFor(coordinator);
        SoloSaveDocument document = await coordinator.CaptureSaveAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequireCurrent(coordinator, generation);
            await _store.WriteAsync(document, cancellationToken).ConfigureAwait(false);
            // Use this exact committed boundary for the post-manual rolling save.
            // A rolling-save failure must not misreport a durable manual save as lost.
            try
            {
                RequireCurrent(coordinator, generation);
                await WriteAutoSaveAsync(document, generation, cancellationToken).ConfigureAwait(false);
                if (IsCurrent(coordinator, generation)) AutoSaveError = null;
            }
            catch (Exception exception) when (IsStorageFailure(exception) || exception is OperationCanceledException)
            {
                if (IsCurrent(coordinator, generation)) AutoSaveError = "Automatic save failed. Your manual save is available in Resume.";
            }
            return new(document.Preview, document.Payload.Snapshot.ActionSequence);
        }
        finally { _writes.Release(); }
    }

    public string? AutoSaveError { get => _autoSaveError; private set => _autoSaveError = value; }

    /// <summary>Coalesces repeated requests while a capture/write is pending, without pausing play.</summary>
    public Task RequestAutoSaveAsync(MatchCoordinator coordinator)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(coordinator, _active)) return Task.CompletedTask;
            _autoPending = true;
            if (_autoWorker.IsCompleted) _autoWorker = Task.Run(AutoSaveLoopAsync);
            return _autoWorker;
        }
    }

    private async Task AutoSaveLoopAsync()
    {
        while (true)
        {
            MatchCoordinator coordinator;
            long generation;
            lock (_gate)
            {
                if (_disposed || !_autoPending || _active is null)
                {
                    // Set under the request lock so a new request cannot be lost
                    // between the empty check and this task becoming completed.
                    _autoWorker = Task.CompletedTask;
                    return;
                }
                coordinator = _active;
                generation = _generation;
                _autoPending = false;
            }
            try
            {
                SoloSaveDocument document = await coordinator.CaptureAutoSaveAsync("Automatic save", AutoSaveId).ConfigureAwait(false);
                await _writes.WaitAsync().ConfigureAwait(false);
                try
                {
                    RequireCurrent(coordinator, generation);
                    await WriteAutoSaveAsync(document, generation).ConfigureAwait(false);
                    AutoSaveError = null;
                }
                finally { _writes.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (IsStorageFailure(exception) || exception is ObjectDisposedException)
            {
                if (IsCurrent(coordinator, generation)) AutoSaveError = "Automatic save failed. Use Save game to try again.";
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (_gate)
        {
            _disposed = true;
            _generation++;
            _active = null;
            pending = _autoWorker;
        }
        await pending.ConfigureAwait(false);
        await _writes.WaitAsync().ConfigureAwait(false);
        _store.Dispose();
        _writes.Release();
        // Keep the semaphore usable for callers already waiting during shutdown;
        // their generation check cancels them before they reach the disposed store.
    }

    private long GenerationFor(MatchCoordinator coordinator)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(coordinator, _active)) throw new OperationCanceledException("Session changed.");
            return _generation;
        }
    }

    private bool IsCurrent(MatchCoordinator coordinator, long generation)
    {
        lock (_gate) return !_disposed && generation == _generation && ReferenceEquals(coordinator, _active);
    }

    private void RequireCurrent(MatchCoordinator coordinator, long generation)
    {
        if (!IsCurrent(coordinator, generation)) throw new OperationCanceledException("Session changed.");
    }

    private static SoloSaveDocument AsAutoSave(SoloSaveDocument document) => SoloSaveDocument.Create(
        SoloSaveDocument.NewMetadata("Automatic save", document.Metadata.HumanSide, AutoSaveId,
            formatId: document.Metadata.FormatId), document.Payload);

    private async Task WriteAutoSaveAsync(SoloSaveDocument document, long generation, CancellationToken cancellationToken = default)
    {
        // An automatic capture can have queued behind a newer manual capture.
        if (_autoWrittenGeneration == generation && document.Payload.Snapshot.ActionSequence < _autoWrittenAction) return;
        await _store.WriteAsync(AsAutoSave(document), cancellationToken).ConfigureAwait(false);
        _autoWrittenGeneration = generation;
        _autoWrittenAction = document.Payload.Snapshot.ActionSequence;
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException;
}
