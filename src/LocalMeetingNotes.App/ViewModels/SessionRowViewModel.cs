using CommunityToolkit.Mvvm.ComponentModel;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.App.ViewModels;

public sealed class SessionRowViewModel : ObservableObject
{
    private int? progressPercentage;

    private SessionRowViewModel(
        Guid? sessionId,
        string title,
        string status,
        string detail,
        string path)
    {
        SessionId = sessionId;
        Title = title;
        Status = status;
        Detail = detail;
        Path = path;
    }

    public Guid? SessionId { get; }

    public string Title { get; }

    public string Status { get; }

    public string Detail { get; }

    public string Path { get; }

    /// <summary>
    /// Transcription progress for this session, or null when nothing is running.
    /// </summary>
    public int? ProgressPercentage
    {
        get => progressPercentage;
        set
        {
            if (SetProperty(ref progressPercentage, value))
            {
                OnPropertyChanged(nameof(HasProgress));
                OnPropertyChanged(nameof(ProgressFraction));
            }
        }
    }

    public bool HasProgress => progressPercentage is not null;

    public double ProgressFraction => (progressPercentage ?? 0) / 100d;

    public static SessionRowViewModel FromSession(MeetingSession session) =>
        new(
            session.SessionId,
            session.Metadata.Title,
            session.Status.ToString(),
            session.Error ?? FormatDate(session.StartedAt),
            session.MeetingsRoot);

    public static SessionRowViewModel FromNote(string path) =>
        new(
            null,
            System.IO.Path.GetFileNameWithoutExtension(path),
            "Completed",
            System.IO.File.GetLastWriteTime(path).ToString("g"),
            path);

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("g") ?? "Processing session";
}
