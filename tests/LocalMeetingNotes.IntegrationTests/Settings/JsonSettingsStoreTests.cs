using FluentAssertions;
using LocalMeetingNotes.App.Settings;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.IntegrationTests.Settings;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _localAppDataRoot;
    private readonly string _settingsFilePath;
    private readonly string _appBaseDirectory;

    public JsonSettingsStoreTests()
    {
        _localAppDataRoot = Path.Combine(Path.GetTempPath(), "LocalMeetingNotes.Tests", Guid.NewGuid().ToString("N"));
        _appBaseDirectory = Path.Combine(_localAppDataRoot, "app");
        Directory.CreateDirectory(_appBaseDirectory);

        var configDirectory = Path.Combine(_localAppDataRoot, "LocalMeetingNotes");
        Directory.CreateDirectory(configDirectory);
        _settingsFilePath = Path.Combine(configDirectory, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_localAppDataRoot))
        {
            Directory.Delete(_localAppDataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_when_missing_creates_defaults_file()
    {
        var store = CreateStore();

        var loaded = await store.LoadAsync();

        var expected = SettingsDefaults.Create(_appBaseDirectory);
        loaded.MeetingsFolder.Should().Be(expected.MeetingsFolder);
        loaded.ModelsFolder.Should().Be(expected.ModelsFolder);
        loaded.SelectedModel.Should().Be(SettingsDefaults.DefaultSelectedModel);
        loaded.Language.Should().Be(SettingsDefaults.DefaultLanguage);
        loaded.TranscriptionThreads.Should().Be(SettingsDefaults.DefaultTranscriptionThreads);
        loaded.DeleteAudioAfterSuccess.Should().BeTrue();
        loaded.CloseToTray.Should().BeTrue();
        loaded.Microphone.Mode.Should().Be(SettingsDefaults.MicrophoneModeDefaultCommunications);
        loaded.SystemOutput.Mode.Should().Be(SettingsDefaults.SystemOutputModeDefault);

        File.Exists(_settingsFilePath).Should().BeTrue();
        store.Current.Should().BeEquivalentTo(loaded);
    }

    [Fact]
    public async Task SaveAsync_then_LoadAsync_roundtrips_settings()
    {
        var store = CreateStore();
        await store.LoadAsync();

        var custom = SettingsDefaults.Create(_appBaseDirectory);
        custom.MeetingsFolder = Path.Combine(_localAppDataRoot, "Meetings");
        custom.SelectedModel = "ggml-small.bin";
        custom.Language = "en";
        custom.TranscriptionThreads = 8;
        custom.DeleteAudioAfterSuccess = false;
        custom.Microphone = new DeviceSelection
        {
            Mode = SettingsDefaults.MicrophoneModeDevice,
            DeviceId = "mic-device-id",
        };

        await store.SaveAsync(custom);

        var reloadedStore = CreateStore();
        var loaded = await reloadedStore.LoadAsync();

        loaded.MeetingsFolder.Should().Be(custom.MeetingsFolder);
        loaded.SelectedModel.Should().Be("ggml-small.bin");
        loaded.Language.Should().Be("en");
        loaded.TranscriptionThreads.Should().Be(8);
        loaded.DeleteAudioAfterSuccess.Should().BeFalse();
        loaded.Microphone.DeviceId.Should().Be("mic-device-id");
        File.Exists(_settingsFilePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_when_corrupt_backs_up_and_returns_defaults()
    {
        await File.WriteAllTextAsync(_settingsFilePath, "{ not valid json");

        var store = CreateStore();
        var loaded = await store.LoadAsync();

        loaded.SelectedModel.Should().Be(SettingsDefaults.DefaultSelectedModel);
        Directory.GetFiles(Path.GetDirectoryName(_settingsFilePath)!, "settings.corrupt.*.json")
            .Should().NotBeEmpty();

        var json = await File.ReadAllTextAsync(_settingsFilePath);
        json.Should().Contain("ggml-base.bin");
    }

    [Fact]
    public async Task TryReloadAsync_with_invalid_json_keeps_last_known_good()
    {
        var store = CreateStore();
        await store.LoadAsync();

        var custom = SettingsDefaults.Create(_appBaseDirectory);
        custom.Language = "es";
        await store.SaveAsync(custom);

        await File.WriteAllTextAsync(_settingsFilePath, "{ broken");

        var changed = await store.TryReloadAsync();

        changed.Should().BeFalse();
        store.Current.Language.Should().Be("es");
    }

    [Fact]
    public async Task TryReloadAsync_with_valid_change_updates_current()
    {
        var store = CreateStore();
        await store.LoadAsync();

        var custom = SettingsDefaults.Create(_appBaseDirectory);
        custom.TranscriptionThreads = 6;
        var json = System.Text.Json.JsonSerializer.Serialize(
            custom,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });
        await File.WriteAllTextAsync(_settingsFilePath, json);

        var changed = await store.TryReloadAsync();

        changed.Should().BeTrue();
        store.Current.TranscriptionThreads.Should().Be(6);
    }

    [Fact]
    public async Task SettingsFileWatcher_debounced_reload_updates_current()
    {
        var store = CreateStore();
        await store.LoadAsync();

        AppSettings? reloaded = null;
        store.SettingsReloaded += (_, settings) => reloaded = settings;

        using var watcher = new SettingsFileWatcher(store, _settingsFilePath, TimeSpan.FromMilliseconds(100));
        watcher.Start();

        var custom = SettingsDefaults.Create(_appBaseDirectory);
        custom.CloseToTray = false;
        var json = System.Text.Json.JsonSerializer.Serialize(
            custom,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });

        await File.WriteAllTextAsync(_settingsFilePath, json);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (reloaded is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        reloaded.Should().NotBeNull();
        reloaded!.CloseToTray.Should().BeFalse();
        store.Current.CloseToTray.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_rejects_invalid_settings()
    {
        var store = CreateStore();
        await store.LoadAsync();

        var invalid = SettingsDefaults.Create(_appBaseDirectory);
        invalid.TranscriptionThreads = 0;

        var act = async () => await store.SaveAsync(invalid);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private JsonSettingsStore CreateStore() =>
        new(_settingsFilePath, () => SettingsDefaults.Create(_appBaseDirectory));
}
