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
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateElapsed();

        _captureService.MetersUpdated += OnMetersUpdated;
        _transcriptionQueue.ProgressChanged += OnQueueProgressChanged;
        _settingsStore.SettingsReloaded += OnSettingsReloaded;
    }

    public event EventHandler? StartRequested;

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = [];

    public IRelayCommand StartCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }

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
