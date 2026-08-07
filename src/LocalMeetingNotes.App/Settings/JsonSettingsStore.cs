using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.App.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _settingsFilePath;
    private readonly Func<AppSettings> _defaultSettingsFactory;
    private readonly object _gate = new();
    private bool _hasLoadedOnce;

    public JsonSettingsStore(string settingsFilePath, Func<AppSettings> defaultSettingsFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentNullException.ThrowIfNull(defaultSettingsFactory);
        _settingsFilePath = settingsFilePath;
        _defaultSettingsFactory = defaultSettingsFactory;
        Current = defaultSettingsFactory();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? SettingsReloaded;

    public static string GetDefaultSettingsFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LocalMeetingNotes", "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return await LoadMissingSettingsAsync(ct);
        }

        return await TryReadSettingsAsync(isReload: false, ct);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var defaults = _defaultSettingsFactory();
        var validation = SettingsValidator.Validate(settings, defaults);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Settings validation failed: {string.Join("; ", validation.Errors)}");
        }

        var json = JsonSerializer.Serialize(validation.Normalized, JsonOptions);
        await AtomicFile.WriteUtf8Async(_settingsFilePath, json, ct, overwrite: true);

        lock (_gate)
        {
            Current = CloneSettings(validation.Normalized);
            _hasLoadedOnce = true;
        }
    }

    internal async Task<bool> TryReloadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return false;
        }

        var previous = CloneSettings(Current);
        var loaded = await TryReadSettingsAsync(isReload: true, ct);
        if (SettingsEqual(previous, loaded))
        {
            return false;
        }

        SettingsReloaded?.Invoke(this, loaded);
        return true;
    }

    private async Task<AppSettings> LoadMissingSettingsAsync(CancellationToken ct)
    {
        var defaults = _defaultSettingsFactory();
        Current = CloneSettings(defaults);
        _hasLoadedOnce = true;
        await SaveAsync(defaults, ct);
        return Current;
    }

    private async Task<AppSettings> TryReadSettingsAsync(bool isReload, CancellationToken ct)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(_settingsFilePath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HandleReadFailure(isReload, ex);
        }

        AppSettings? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return await HandleCorruptJsonAsync(isReload, ct);
        }

        if (deserialized is null)
        {
            return await HandleCorruptJsonAsync(isReload, ct);
        }

        var defaults = _defaultSettingsFactory();
        var validation = SettingsValidator.Validate(deserialized, defaults);
        var normalized = validation.Normalized;

        lock (_gate)
        {
            Current = CloneSettings(normalized);
            _hasLoadedOnce = true;
        }

        return Current;
    }

    private AppSettings HandleReadFailure(bool isReload, Exception ex)
    {
        if (isReload && _hasLoadedOnce)
        {
            return Current;
        }

        throw new IOException($"Unable to read settings from '{_settingsFilePath}'.", ex);
    }

    private async Task<AppSettings> HandleCorruptJsonAsync(bool isReload, CancellationToken ct)
    {
        if (isReload && _hasLoadedOnce)
        {
            return Current;
        }

        await BackupCorruptFileAsync(ct);

        var defaults = _defaultSettingsFactory();
        lock (_gate)
        {
            Current = CloneSettings(defaults);
            _hasLoadedOnce = true;
        }

        await SaveAsync(defaults, ct);
        return Current;
    }

    private async Task BackupCorruptFileAsync(CancellationToken ct)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_settingsFilePath)
            ?? throw new InvalidOperationException("Settings file path must include a directory.");
        Directory.CreateDirectory(directory);

        var backupPath = Path.Combine(
            directory,
            $"settings.corrupt.{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");

        ct.ThrowIfCancellationRequested();
        File.Copy(_settingsFilePath, backupPath, overwrite: false);
        await Task.CompletedTask;
    }

    private static bool SettingsEqual(AppSettings left, AppSettings right) =>
        left.MeetingsFolder == right.MeetingsFolder
        && left.ModelsFolder == right.ModelsFolder
        && left.SelectedModel == right.SelectedModel
        && left.Language == right.Language
        && left.TranscriptionThreads == right.TranscriptionThreads
        && left.Microphone.Mode == right.Microphone.Mode
        && left.Microphone.DeviceId == right.Microphone.DeviceId
        && left.SystemOutput.Mode == right.SystemOutput.Mode
        && left.SystemOutput.DeviceId == right.SystemOutput.DeviceId
        && left.DeleteAudioAfterSuccess == right.DeleteAudioAfterSuccess
        && left.StartMinimized == right.StartMinimized
        && left.CloseToTray == right.CloseToTray;

    private static AppSettings CloneSettings(AppSettings source) =>
        new()
        {
            MeetingsFolder = source.MeetingsFolder,
            ModelsFolder = source.ModelsFolder,
            SelectedModel = source.SelectedModel,
            Language = source.Language,
            TranscriptionThreads = source.TranscriptionThreads,
            Microphone = new DeviceSelection
            {
                Mode = source.Microphone.Mode,
                DeviceId = source.Microphone.DeviceId,
            },
            SystemOutput = new DeviceSelection
            {
                Mode = source.SystemOutput.Mode,
                DeviceId = source.SystemOutput.DeviceId,
            },
            DeleteAudioAfterSuccess = source.DeleteAudioAfterSuccess,
            StartMinimized = source.StartMinimized,
            CloseToTray = source.CloseToTray,
        };
}
