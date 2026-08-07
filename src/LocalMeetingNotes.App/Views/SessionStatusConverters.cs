using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace LocalMeetingNotes.App.Views;

public sealed class SessionStatusBrushConverter : IValueConverter
{
    private static readonly Color Success = Color.FromRgb(0x4A, 0xDE, 0x80);
    private static readonly Color Danger = Color.FromRgb(0xFF, 0x5F, 0x57);
    private static readonly Color Accent = Color.FromRgb(0x6E, 0x8B, 0xFF);
    private static readonly Color Muted = Color.FromRgb(0x8A, 0x90, 0xA0);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = Resolve(value as string);
        var brush = string.Equals(parameter as string, "bg", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B))
            : new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Color Resolve(string? status) => status switch
    {
        "Completed" => Success,
        "Failed" or "Interrupted" => Danger,
        "Recording" or "Stopping" => Danger,
        "TranscribingMic" or "TranscribingSystem" or "Merging" or "WritingNote" => Accent,
        _ => Muted,
    };
}

public sealed class SessionStatusLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "TranscribingMic" => "Transcribing mic",
            "TranscribingSystem" => "Transcribing system",
            "WaitingForModel" => "Waiting for model",
            "WritingNote" => "Writing note",
            null => string.Empty,
            var status => status,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
