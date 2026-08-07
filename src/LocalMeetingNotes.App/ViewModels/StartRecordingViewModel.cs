using CommunityToolkit.Mvvm.ComponentModel;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.App.ViewModels;

public sealed partial class StartRecordingViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = $"Meeting {DateTime.Now:yyyy-MM-dd HH-mm}";

    [ObservableProperty]
    private string participants = string.Empty;

    [ObservableProperty]
    private string context = string.Empty;

    [ObservableProperty]
    private string references = string.Empty;

    public MeetingMetadata CreateMetadata()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new InvalidOperationException("A meeting title is required.");
        }

        return new MeetingMetadata(
            Title.Trim(),
            SplitLines(Participants),
            string.IsNullOrWhiteSpace(Context) ? null : Context.Trim(),
            SplitLines(References));
    }

    private static IReadOnlyList<string>? SplitLines(string value)
    {
        var values = value
            .Split([',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return values.Length == 0 ? null : values;
    }
}
