namespace LocalMeetingNotes.Core.Settings;

public static class SettingsValidator
{
    private static readonly HashSet<string> AllowedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto",
        "en",
        "es",
    };

    public static SettingsValidationResult Validate(AppSettings settings, AppSettings defaults)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(defaults);

        var errors = new List<string>();
        var normalized = Clone(settings);

        normalized.MeetingsFolder = NormalizeRequiredPath(
            normalized.MeetingsFolder,
            defaults.MeetingsFolder,
            "meetingsFolder",
            errors);

        normalized.ModelsFolder = NormalizeRequiredPath(
            normalized.ModelsFolder,
            defaults.ModelsFolder,
            "modelsFolder",
            errors);

        normalized.SelectedModel = NormalizeSelectedModel(
            normalized.SelectedModel,
            defaults.SelectedModel,
            errors);

        normalized.Language = NormalizeLanguage(
            normalized.Language,
            defaults.Language,
            errors);

        normalized.TranscriptionThreads = NormalizeThreads(
            normalized.TranscriptionThreads,
            defaults.TranscriptionThreads,
            errors);

        normalized.Microphone = NormalizeMicrophoneSelection(
            normalized.Microphone,
            defaults.Microphone,
            errors);

        normalized.SystemOutput = NormalizeSystemOutputSelection(
            normalized.SystemOutput,
            defaults.SystemOutput,
            errors);

        return new SettingsValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Normalized = normalized,
        };
    }

    private static AppSettings Clone(AppSettings source) =>
        new()
        {
            MeetingsFolder = source.MeetingsFolder,
            ModelsFolder = source.ModelsFolder,
            SelectedModel = source.SelectedModel,
            Language = source.Language,
            TranscriptionThreads = source.TranscriptionThreads,
            Microphone = new DeviceSelection
            {
                Mode = source.Microphone?.Mode ?? string.Empty,
                DeviceId = source.Microphone?.DeviceId,
            },
            SystemOutput = new DeviceSelection
            {
                Mode = source.SystemOutput?.Mode ?? string.Empty,
                DeviceId = source.SystemOutput?.DeviceId,
            },
            DeleteAudioAfterSuccess = source.DeleteAudioAfterSuccess,
            StartMinimized = source.StartMinimized,
            CloseToTray = source.CloseToTray,
        };

    private static string NormalizeRequiredPath(
        string value,
        string fallback,
        string fieldName,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldName} must not be empty.");
            return fallback;
        }

        return value.Trim();
    }

    private static string NormalizeSelectedModel(string value, string fallback, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("selectedModel must not be empty.");
            return fallback;
        }

        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.Contains('\\')
            || trimmed.Contains('/')
            || trimmed.Contains("..", StringComparison.Ordinal))
        {
            errors.Add("selectedModel must be a file name without path separators.");
            return fallback;
        }

        return trimmed;
    }

    private static string NormalizeLanguage(string value, string fallback, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("language must not be empty.");
            return fallback;
        }

        var trimmed = value.Trim();
        if (!AllowedLanguages.Contains(trimmed))
        {
            errors.Add($"language '{trimmed}' is not supported.");
            return fallback;
        }

        return trimmed.ToLowerInvariant() == "auto"
            ? SettingsDefaults.DefaultLanguage
            : trimmed.ToLowerInvariant();
    }

    private static int NormalizeThreads(int value, int fallback, List<string> errors)
    {
        if (value is < 1 or > 32)
        {
            errors.Add("transcriptionThreads must be between 1 and 32.");
            return fallback;
        }

        return value;
    }

    private static DeviceSelection NormalizeMicrophoneSelection(
        DeviceSelection? selection,
        DeviceSelection fallback,
        List<string> errors)
    {
        selection ??= new DeviceSelection();

        return selection.Mode switch
        {
            SettingsDefaults.MicrophoneModeDefaultCommunications => new DeviceSelection
            {
                Mode = SettingsDefaults.MicrophoneModeDefaultCommunications,
            },
            SettingsDefaults.MicrophoneModeDevice when string.IsNullOrWhiteSpace(selection.DeviceId) =>
                ReportDeviceIdRequired("microphone", fallback, errors),
            SettingsDefaults.MicrophoneModeDevice => new DeviceSelection
            {
                Mode = SettingsDefaults.MicrophoneModeDevice,
                DeviceId = selection.DeviceId!.Trim(),
            },
            _ => ReportInvalidMode("microphone", fallback, errors),
        };
    }

    private static DeviceSelection NormalizeSystemOutputSelection(
        DeviceSelection? selection,
        DeviceSelection fallback,
        List<string> errors)
    {
        selection ??= new DeviceSelection();

        return selection.Mode switch
        {
            SettingsDefaults.SystemOutputModeDefault => new DeviceSelection
            {
                Mode = SettingsDefaults.SystemOutputModeDefault,
            },
            SettingsDefaults.SystemOutputModeDevice when string.IsNullOrWhiteSpace(selection.DeviceId) =>
                ReportDeviceIdRequired("systemOutput", fallback, errors),
            SettingsDefaults.SystemOutputModeDevice => new DeviceSelection
            {
                Mode = SettingsDefaults.SystemOutputModeDevice,
                DeviceId = selection.DeviceId!.Trim(),
            },
            _ => ReportInvalidMode("systemOutput", fallback, errors),
        };
    }

    private static DeviceSelection ReportInvalidMode(
        string fieldName,
        DeviceSelection fallback,
        List<string> errors)
    {
        errors.Add($"{fieldName}.mode is invalid.");
        return CloneDeviceSelection(fallback);
    }

    private static DeviceSelection ReportDeviceIdRequired(
        string fieldName,
        DeviceSelection fallback,
        List<string> errors)
    {
        errors.Add($"{fieldName}.deviceId is required when mode is 'device'.");
        return CloneDeviceSelection(fallback);
    }

    private static DeviceSelection CloneDeviceSelection(DeviceSelection source) =>
        new()
        {
            Mode = source.Mode,
            DeviceId = source.DeviceId,
        };
}
