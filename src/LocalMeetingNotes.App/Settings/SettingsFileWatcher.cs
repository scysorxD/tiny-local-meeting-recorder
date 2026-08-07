using System.IO;

namespace LocalMeetingNotes.App.Settings;

public sealed class SettingsFileWatcher : IDisposable
{
    private readonly JsonSettingsStore _store;
    private readonly string _settingsFilePath;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    public SettingsFileWatcher(
        JsonSettingsStore store,
        string settingsFilePath,
        TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _store = store;
        _settingsFilePath = settingsFilePath;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path must include a directory.");
        var fileName = Path.GetFileName(_settingsFilePath);

        Directory.CreateDirectory(directory);

        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += OnWatcherEvent;
            _watcher.Created += OnWatcherEvent;
            _watcher.Renamed += OnWatcherEvent;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            if (_watcher is not null)
            {
                _watcher.Changed -= OnWatcherEvent;
                _watcher.Created -= OnWatcherEvent;
                _watcher.Renamed -= OnWatcherEvent;
                _watcher.Dispose();
                _watcher = null;
            }

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) => ScheduleReload();

    private void ScheduleReload()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = DebouncedReloadAsync(token);
        }
    }

    private async Task DebouncedReloadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounce, token);
            await _store.TryReloadAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }
}
