using System.Text;

namespace LocalMeetingNotes.Core.Files;

public static class AtomicFile
{
    public static async Task WriteUtf8Async(string destinationPath, string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("Destination path must include a directory.", nameof(destinationPath));

        Directory.CreateDirectory(directory);

        var tempPath = destinationPath + ".tmp";

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        await using (var stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.SequentialScan))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, destinationPath, overwrite: false);

        var readBack = await File.ReadAllTextAsync(destinationPath, Encoding.UTF8, cancellationToken);
        if (!string.Equals(readBack, content, StringComparison.Ordinal))
        {
            throw new IOException($"Verification failed after writing '{destinationPath}'.");
        }
    }
}
