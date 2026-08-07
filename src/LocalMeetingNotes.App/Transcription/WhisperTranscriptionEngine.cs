using System.IO;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LocalMeetingNotes.App.Transcription;

public sealed class WhisperTranscriptionEngine : ITranscriptionEngine, IDisposable
{
    private readonly object _factoryLock = new();
    private Lazy<WhisperFactory>? _factory;
    private string? _factoryModelPath;
    private bool _disposed;

    public async Task<TrackTranscript> TranscribeAsync(
        string wavPath,
        TranscriptionOptions options,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateRequest(wavPath, options);

        try
        {
            using var processor = GetFactory(options.ModelPath)
                .CreateBuilder()
                .WithThreads(options.ThreadCount)
                .WithLanguage(options.Language)
                .WithProgressHandler(percentage => progress?.Report(new TranscriptionProgress(percentage)))
                .Build();

            await using var stream = File.OpenRead(wavPath);
            var segments = new List<TranscriptSegment>();

            await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
            {
                var text = segment.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    segments.Add(new TranscriptSegment(segment.Start, segment.End, text));
                }
            }

            return new TrackTranscript(segments);
        }
        catch (WhisperModelLoadException exception)
        {
            throw new TranscriptionEngineException(
                ErrorCategory.ModelInvalid,
                "Whisper could not load the selected model.",
                exception);
        }
        catch (Exception exception) when (IsNativeRuntimeLoadFailure(exception))
        {
            throw new TranscriptionEngineException(
                ErrorCategory.NativeRuntimeLoadFailure,
                "Whisper's native CPU runtime could not be loaded.",
                exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_factoryLock)
        {
            if (!_disposed)
            {
                if (_factory is { IsValueCreated: true })
                {
                    _factory.Value.Dispose();
                }

                _disposed = true;
            }
        }
    }

    private WhisperFactory GetFactory(string modelPath)
    {
        var fullPath = Path.GetFullPath(modelPath);

        lock (_factoryLock)
        {
            ThrowIfDisposed();
            if (!string.Equals(_factoryModelPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (_factory is { IsValueCreated: true })
                {
                    _factory.Value.Dispose();
                }

                _factoryModelPath = fullPath;
                _factory = new Lazy<WhisperFactory>(() =>
                {
                    RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
                    return WhisperFactory.FromPath(fullPath);
                }, LazyThreadSafetyMode.ExecutionAndPublication);
            }

            return _factory!.Value;
        }
    }

    private static void ValidateRequest(string wavPath, TranscriptionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModelPath);

        if (!File.Exists(wavPath))
        {
            throw new FileNotFoundException("The WAV file does not exist.", wavPath);
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new FileNotFoundException("The Whisper model does not exist.", options.ModelPath);
        }

        if (options.ThreadCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Thread count must be positive.");
        }

        if (options.Language is not ("auto" or "en" or "es"))
        {
            throw new ArgumentException("Language must be auto, en, or es.", nameof(options));
        }
    }

    private static bool IsNativeRuntimeLoadFailure(Exception exception) =>
        exception is DllNotFoundException
            or BadImageFormatException
            or EntryPointNotFoundException
            or TypeInitializationException
            or FileLoadException;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
