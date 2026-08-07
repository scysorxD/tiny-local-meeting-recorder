using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly RecordingCoordinator _recordingCoordinator;
    private readonly IAudioCaptureService _captureService;
    private readonly ISessionRepository _sessionRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IModelCatalog _modelCatalog;
    private readonly ITranscriptionQueue _transcriptionQueue;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private DateTimeOffset? _recordingStartedAt;
    private bool _disposed;

    public MainViewModel(
        RecordingCoordinator recordingCoordinator,
        IAudioCaptureService captureService,
        ISessionRepository sessionRepository,
        ISettingsStore settingsStore,
        IModelCatalog modelCatalog,
        ITranscriptionQueue transcriptionQueue)
    {
        _recordingCoordinator = recordingCoordinator;
        _captureService = captureService;
        _sessionRepository = sessionRepository;
        _settingsStore = settingsStore;
        _modelCatalog = modelCatalog;
        _transcriptionQueue = transcriptionQueue;

        StartCommand = new RelayCommand(() => StartRequested?.Invoke(this, EventArgs.Empty), () => !IsRecording);
        StopCommand = new AsyncRelayCommand(StopRecordingAsync, () => IsRecording);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        RetryCommand = new AsyncRelayCommand<SessionRowViewModel>(RetryAsync, row => row?.SessionId is not null);
        OpenItemCommand = new RelayCommand<SessionRowViewModel>(OpenItem);
        CopyNoteCommand = new RelayCommand<SessionRowViewModel>(CopyNote, row => row is { Status: "Completed" });
        OpenFolderCommand = new RelayCommand<SessionRowViewModel>(OpenFolder);
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateElapsed();

        _captureService.MetersUpdated += OnMetersUpdated;
        _transcriptionQueue.ProgressChanged += OnQueueProgressChanged;
        _settingsStore.SettingsReloaded += OnSettingsReloaded;
    }

    public event EventHandler? StartRequested;

    public event EventHandler? SettingsRequested;

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = [];

    public IRelayCommand StartCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IAsyncRelayCommand<SessionRowViewModel> RetryCommand { get; }

    public IRelayCommand<SessionRowViewModel> OpenItemCommand { get; }

    public IRelayCommand<SessionRowViewModel> CopyNoteCommand { get; }

    public IRelayCommand<SessionRowViewModel> OpenFolderCommand { get; }

    public string GetMeetingsFolder() => _settingsStore.Current.MeetingsFolder;

    public bool CloseToTray => _settingsStore.Current.CloseToTray;

    public GlobalAppStatus ComputeGlobalStatus()
    {
        var hasQueueWork = Sessions.Any(row =>
            row.Status is nameof(SessionStatus.Queued)
                or nameof(SessionStatus.TranscribingMic)
                or nameof(SessionStatus.TranscribingSystem)
                or nameof(SessionStatus.Merging)
                or nameof(SessionStatus.WritingNote));

        if (IsRecording && hasQueueWork)
        {
            return GlobalAppStatus.RecordingAndTranscribing;
        }

        if (IsRecording)
        {
            return GlobalAppStatus.Recording;
        }

        if (hasQueueWork)
        {
            return GlobalAppStatus.Transcribing;
        }

        if (Sessions.Any(row => row.Status is nameof(SessionStatus.Failed) or nameof(SessionStatus.Interrupted)))
        {
            return GlobalAppStatus.Error;
        }

        return GlobalAppStatus.Ready;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool isRecording;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private string elapsedText = "00:00:00";

    [ObservableProperty]
    private double microphoneLevel;

    [ObservableProperty]
    private double systemLevel;

    [ObservableProperty]
    private bool hasNoModel;

    public async Task InitializeAsync()
    {
        RefreshModelWarning();
        await RefreshSessionsAsync();
    }

    public async Task StartRecordingAsync(MeetingMetadata metadata)
    {
        var session = await _recordingCoordinator.StartAsync(metadata);
        _recordingStartedAt = session.StartedAt ?? DateTimeOffset.UtcNow;
        IsRecording = true;
        StatusText = "Recording";
        _timer.Start();
        await RefreshSessionsAsync();
    }

    public async Task StopRecordingAsync()
    {
        StatusText = "Stopping recording…";
        var session = await _recordingCoordinator.StopAsync();

        _timer.Stop();
        IsRecording = false;
        ElapsedText = "00:00:00";
        MicrophoneLevel = 0;
        SystemLevel = 0;

        if (session.Status == SessionStatus.Queued)
        {
            _transcriptionQueue.Enqueue(session.SessionId);
            StatusText = "Recording saved; transcription queued.";
        }
        else if (session.Status == SessionStatus.WaitingForModel)
        {
            StatusText = "Recording saved; waiting for a Whisper model.";
        }
        else
        {
            StatusText = session.Error ?? $"Recording {session.Status}.";
        }

        RefreshModelWarning();
        await RefreshSessionsAsync();
    }

    public async Task RefreshSessionsAsync()
    {
        var root = _settingsStore.Current.MeetingsFolder;
        var rows = new List<SessionRowViewModel>();

        try
        {
            var processing = await _sessionRepository.LoadProcessingAsync(root);
            rows.AddRange(processing
                .OrderByDescending(session => session.StartedAt)
                .Select(SessionRowViewModel.FromSession));

            if (Directory.Exists(root))
            {
                rows.AddRange(Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Select(SessionRowViewModel.FromNote));
            }
        }
        catch (Exception exception)
        {
            StatusText = $"Could not scan meetings: {exception.Message}";
        }

        Sessions.Clear();
        foreach (var row in rows)
        {
            Sessions.Add(row);
        }
    }

    private async Task RetryAsync(SessionRowViewModel? row)
    {
        if (row?.SessionId is not { } sessionId)
        {
            return;
        }

        try
        {
            await _transcriptionQueue.RetryAsync(sessionId);
            StatusText = "Retry queued.";
            await RefreshSessionsAsync();
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            System.Windows.MessageBox.Show(exception.Message, "Retry failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private static void OpenItem(SessionRowViewModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Path) || !File.Exists(row.Path))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = row.Path,
            UseShellExecute = true,
        });
    }

    private static void CopyNote(SessionRowViewModel? row)
    {
        if (row is null || !File.Exists(row.Path))
        {
            return;
        }

        System.Windows.Clipboard.SetText(File.ReadAllText(row.Path));
    }

    private void OpenFolder(SessionRowViewModel? row)
    {
        var target = row?.Path;
        if (string.IsNullOrWhiteSpace(target))
        {
            target = GetMeetingsFolder();
        }

        var folder = File.Exists(target) ? Path.GetDirectoryName(target) : target;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _captureService.MetersUpdated -= OnMetersUpdated;
        _transcriptionQueue.ProgressChanged -= OnQueueProgressChanged;
        _settingsStore.SettingsReloaded -= OnSettingsReloaded;
    }

    private void RefreshModelWarning()
    {
        HasNoModel = !_modelCatalog.Discover(_settingsStore.Current.ModelsFolder).Any(model => model.IsValid);
    }

    private void UpdateElapsed()
    {
        if (_recordingStartedAt is { } startedAt)
        {
            ElapsedText = (DateTimeOffset.UtcNow - startedAt).ToString(@"hh\:mm\:ss");
        }
    }

    private void OnMetersUpdated(object? sender, AudioMeterEventArgs args)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (args.Track == AudioTrack.Microphone)
            {
                MicrophoneLevel = args.Peak;
            }
            else
            {
                SystemLevel = args.Peak;
            }
        });
    }

    private void OnQueueProgressChanged(object? sender, SessionProgressEventArgs args)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            StatusText = args.Percentage is { } percentage
                ? $"{args.Status}: {percentage}%"
                : args.Status.ToString();
            await RefreshSessionsAsync();
        });
    }

    private void OnSettingsReloaded(object? sender, AppSettings settings)
    {
        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            RefreshModelWarning();
            _ = RefreshSessionsAsync();
        });
    }
}
