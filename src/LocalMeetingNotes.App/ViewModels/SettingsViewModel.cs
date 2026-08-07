using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly IAudioDeviceService _deviceService;
    private readonly IModelCatalog _modelCatalog;
    private readonly string _settingsFilePath;

    public SettingsViewModel(
        ISettingsStore settingsStore,
        IAudioDeviceService deviceService,
        IModelCatalog modelCatalog,
        string settingsFilePath)
    {
        _settingsStore = settingsStore;
        _deviceService = deviceService;
        _modelCatalog = modelCatalog;
        _settingsFilePath = settingsFilePath;

        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        RefreshModelsCommand = new RelayCommand(RefreshModels);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        OpenSettingsFileCommand = new RelayCommand(OpenSettingsFile);
        BrowseMeetingsFolderCommand = new RelayCommand(BrowseMeetingsFolder);
        BrowseModelsFolderCommand = new RelayCommand(BrowseModelsFolder);

        LoadFrom(_settingsStore.Current);
        RefreshDevices();
        RefreshModels();
    }

    public ObservableCollection<AudioDeviceInfo> Microphones { get; } = [];
    public ObservableCollection<AudioDeviceInfo> RenderDevices { get; } = [];
    public ObservableCollection<WhisperModelInfo> Models { get; } = [];

    public IRelayCommand RefreshDevicesCommand { get; }
    public IRelayCommand RefreshModelsCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand OpenSettingsFileCommand { get; }
    public IRelayCommand BrowseMeetingsFolderCommand { get; }
    public IRelayCommand BrowseModelsFolderCommand { get; }

    [ObservableProperty] private string meetingsFolder = string.Empty;
    [ObservableProperty] private string modelsFolder = string.Empty;
    [ObservableProperty] private WhisperModelInfo? selectedModel;
    [ObservableProperty] private string language = "auto";
    [ObservableProperty] private int transcriptionThreads = 4;
    [ObservableProperty] private bool deleteAudioAfterSuccess = true;
    [ObservableProperty] private bool startMinimized;
    [ObservableProperty] private bool closeToTray = true;
    [ObservableProperty] private AudioDeviceInfo? selectedMicrophone;
    [ObservableProperty] private AudioDeviceInfo? selectedRenderDevice;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string modelStatus = string.Empty;

    public IReadOnlyList<string> Languages { get; } = ["auto", "en", "es"];

    public void LoadFrom(AppSettings settings)
    {
        MeetingsFolder = settings.MeetingsFolder;
        ModelsFolder = settings.ModelsFolder;
        Language = string.IsNullOrWhiteSpace(settings.Language) ? "auto" : settings.Language;
        TranscriptionThreads = settings.TranscriptionThreads <= 0 ? 4 : settings.TranscriptionThreads;
        DeleteAudioAfterSuccess = settings.DeleteAudioAfterSuccess;
        StartMinimized = settings.StartMinimized;
        CloseToTray = settings.CloseToTray;
        RefreshModels();
        RefreshDevices();
        SelectedMicrophone = ResolveSelectedMic(settings);
        SelectedRenderDevice = ResolveSelectedRender(settings);
    }

    private void RefreshDevices()
    {
        Microphones.Clear();
        foreach (var mic in _deviceService.GetMicrophones())
        {
            Microphones.Add(mic);
        }

        RenderDevices.Clear();
        foreach (var device in _deviceService.GetRenderDevices())
        {
            RenderDevices.Add(device);
        }
    }

    private void RefreshModels()
    {
        Models.Clear();
        foreach (var model in _modelCatalog.Discover(ModelsFolder))
        {
            Models.Add(model);
        }

        SelectedModel = Models.FirstOrDefault(model =>
            string.Equals(model.FileName, _settingsStore.Current.SelectedModel, StringComparison.OrdinalIgnoreCase))
            ?? Models.FirstOrDefault(model => model.IsValid);

        ModelStatus = Models.Any(model => model.IsValid)
            ? $"✓ {Models.Count(model => model.IsValid)} local model(s) found"
            : "⚠ No .bin models found. Recordings will be preserved until a model is added.";
    }

    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            MeetingsFolder = MeetingsFolder.Trim(),
            ModelsFolder = ModelsFolder.Trim(),
            SelectedModel = SelectedModel?.FileName ?? string.Empty,
            Language = Language,
            TranscriptionThreads = TranscriptionThreads,
            DeleteAudioAfterSuccess = DeleteAudioAfterSuccess,
            StartMinimized = StartMinimized,
            CloseToTray = CloseToTray,
            Microphone = new DeviceSelection
            {
                Mode = SelectedMicrophone is null || SelectedMicrophone.IsDefault
                    ? "defaultCommunications"
                    : "device",
                DeviceId = SelectedMicrophone is null || SelectedMicrophone.IsDefault
                    ? null
                    : SelectedMicrophone.Id,
            },
            SystemOutput = new DeviceSelection
            {
                Mode = SelectedRenderDevice is null || SelectedRenderDevice.IsDefault
                    ? "default"
                    : "device",
                DeviceId = SelectedRenderDevice is null || SelectedRenderDevice.IsDefault
                    ? null
                    : SelectedRenderDevice.Id,
            },
        };

        await _settingsStore.SaveAsync(settings);
        StatusMessage = "Settings saved.";
        RefreshModels();
    }

    private void OpenSettingsFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        if (!File.Exists(_settingsFilePath))
        {
            File.WriteAllText(_settingsFilePath, "{}");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _settingsFilePath,
            UseShellExecute = true,
        });
    }

    private void BrowseMeetingsFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(MeetingsFolder) ? MeetingsFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            MeetingsFolder = dialog.SelectedPath;
        }
    }

    private void BrowseModelsFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(ModelsFolder) ? ModelsFolder : AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ModelsFolder = dialog.SelectedPath;
            RefreshModels();
        }
    }

    private AudioDeviceInfo? ResolveSelectedMic(AppSettings settings)
    {
        if (settings.Microphone.Mode == "device" && !string.IsNullOrWhiteSpace(settings.Microphone.DeviceId))
        {
            return Microphones.FirstOrDefault(device => device.Id == settings.Microphone.DeviceId)
                ?? _deviceService.GetDefaultCommunicationsMicrophone();
        }

        return Microphones.FirstOrDefault(device => device.IsDefault)
            ?? _deviceService.GetDefaultCommunicationsMicrophone();
    }

    private AudioDeviceInfo? ResolveSelectedRender(AppSettings settings)
    {
        if (settings.SystemOutput.Mode == "device" && !string.IsNullOrWhiteSpace(settings.SystemOutput.DeviceId))
        {
            return RenderDevices.FirstOrDefault(device => device.Id == settings.SystemOutput.DeviceId)
                ?? _deviceService.GetDefaultRenderDevice();
        }

        return RenderDevices.FirstOrDefault(device => device.IsDefault)
            ?? _deviceService.GetDefaultRenderDevice();
    }
}
