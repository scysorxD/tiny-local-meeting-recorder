using System.IO;
using LocalMeetingNotes.Core.Interfaces;

namespace LocalMeetingNotes.App.Logging;

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class FileAppLogger : IAppLogger
{
    private readonly Func<string> _meetingsRootProvider;
    private readonly object _gate = new();

    public FileAppLogger(Func<string> meetingsRootProvider)
    {
        _meetingsRootProvider = meetingsRootProvider;
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var root = _meetingsRootProvider();
            var logDir = Path.Combine(root, ".logs");
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"{DateTimeOffset.Now:o} [{level}] {message}";
            if (exception is not null)
            {
                line += $" | {exception.GetType().Name}: {exception.Message}";
            }

            lock (_gate)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never break the app on logging failures.
        }
    }
}
