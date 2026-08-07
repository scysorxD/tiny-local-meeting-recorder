using FluentAssertions;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Tests.Settings;

public class SettingsValidatorTests
{
    private readonly AppSettings _defaults = SettingsDefaults.Create(@"C:\App");

    [Fact]
    public void Validate_accepts_default_settings()
    {
        var result = SettingsValidator.Validate(_defaults, _defaults);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Normalized.MeetingsFolder.Should().Be(_defaults.MeetingsFolder);
        result.Normalized.ModelsFolder.Should().Be(_defaults.ModelsFolder);
        result.Normalized.SelectedModel.Should().Be(SettingsDefaults.DefaultSelectedModel);
        result.Normalized.Language.Should().Be(SettingsDefaults.DefaultLanguage);
        result.Normalized.TranscriptionThreads.Should().Be(SettingsDefaults.DefaultTranscriptionThreads);
        result.Normalized.Microphone.Mode.Should().Be(SettingsDefaults.MicrophoneModeDefaultCommunications);
        result.Normalized.SystemOutput.Mode.Should().Be(SettingsDefaults.SystemOutputModeDefault);
        result.Normalized.DeleteAudioAfterSuccess.Should().BeTrue();
        result.Normalized.CloseToTray.Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_empty_meetings_folder()
    {
        var settings = CloneDefaults();
        settings.MeetingsFolder = "   ";

        var result = SettingsValidator.Validate(settings, _defaults);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("meetingsFolder"));
        result.Normalized.MeetingsFolder.Should().Be(_defaults.MeetingsFolder);
    }

    [Fact]
    public void Validate_rejects_selected_model_with_path_separators()
    {
        var settings = CloneDefaults();
        settings.SelectedModel = @"models\ggml-base.bin";

        var result = SettingsValidator.Validate(settings, _defaults);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("selectedModel"));
        result.Normalized.SelectedModel.Should().Be(SettingsDefaults.DefaultSelectedModel);
    }

    [Fact]
    public void Validate_rejects_unsupported_language()
    {
        var settings = CloneDefaults();
        settings.Language = "fr";

        var result = SettingsValidator.Validate(settings, _defaults);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("language"));
        result.Normalized.Language.Should().Be(SettingsDefaults.DefaultLanguage);
    }

    [Fact]
    public void Validate_accepts_supported_languages()
    {
        foreach (var language in new[] { "auto", "en", "es", "EN" })
        {
            var settings = CloneDefaults();
            settings.Language = language;

            var result = SettingsValidator.Validate(settings, _defaults);

            result.IsValid.Should().BeTrue();
            result.Normalized.Language.Should().Be(language.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? SettingsDefaults.DefaultLanguage
                : language.ToLowerInvariant());
        }
    }

    [Fact]
    public void Validate_rejects_transcription_threads_out_of_range()
    {
        var settings = CloneDefaults();
        settings.TranscriptionThreads = 0;

        var result = SettingsValidator.Validate(settings, _defaults);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("transcriptionThreads"));
        result.Normalized.TranscriptionThreads.Should().Be(SettingsDefaults.DefaultTranscriptionThreads);
    }

    [Fact]
    public void Validate_rejects_device_mode_without_device_id()
    {
        var settings = CloneDefaults();
        settings.Microphone = new DeviceSelection
        {
            Mode = SettingsDefaults.MicrophoneModeDevice,
        };

        var result = SettingsValidator.Validate(settings, _defaults);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("microphone.deviceId"));
        result.Normalized.Microphone.Mode.Should().Be(SettingsDefaults.MicrophoneModeDefaultCommunications);
    }

    [Fact]
    public void Validate_accepts_explicit_device_selection()
    {
        var settings = CloneDefaults();
        settings.Microphone = new DeviceSelection
        {
            Mode = SettingsDefaults.MicrophoneModeDevice,
            DeviceId = "{0.0.1.00000000}.{guid}",
        };
        settings.SystemOutput = new DeviceSelection
        {
            Mode = SettingsDefaults.SystemOutputModeDevice,
            DeviceId = "{0.0.0.00000000}.{guid}",
        };

        var result = SettingsValidator.Validate(settings, _defaults);

        result.IsValid.Should().BeTrue();
        result.Normalized.Microphone.DeviceId.Should().Be(settings.Microphone.DeviceId);
        result.Normalized.SystemOutput.DeviceId.Should().Be(settings.SystemOutput.DeviceId);
    }

    [Fact]
    public void Validate_rejects_invalid_system_output_mode()
    {
        var settings = CloneDefaults();
        settings.SystemOutput = new DeviceSelection { Mode = "invalid" };

        var result = SettingsValidator.Validate(settings, _defaults);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("systemOutput.mode"));
        result.Normalized.SystemOutput.Mode.Should().Be(SettingsDefaults.SystemOutputModeDefault);
    }

    private AppSettings CloneDefaults()
    {
        var clone = SettingsValidator.Validate(_defaults, _defaults).Normalized;
        return new AppSettings
        {
            MeetingsFolder = clone.MeetingsFolder,
            ModelsFolder = clone.ModelsFolder,
            SelectedModel = clone.SelectedModel,
            Language = clone.Language,
            TranscriptionThreads = clone.TranscriptionThreads,
            Microphone = new DeviceSelection
            {
                Mode = clone.Microphone.Mode,
                DeviceId = clone.Microphone.DeviceId,
            },
            SystemOutput = new DeviceSelection
            {
                Mode = clone.SystemOutput.Mode,
                DeviceId = clone.SystemOutput.DeviceId,
            },
            DeleteAudioAfterSuccess = clone.DeleteAudioAfterSuccess,
            StartMinimized = clone.StartMinimized,
            CloseToTray = clone.CloseToTray,
        };
    }
}
