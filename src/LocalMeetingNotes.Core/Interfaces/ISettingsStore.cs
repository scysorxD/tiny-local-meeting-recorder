using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Interfaces;

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? SettingsReloaded;

    Task<AppSettings> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
