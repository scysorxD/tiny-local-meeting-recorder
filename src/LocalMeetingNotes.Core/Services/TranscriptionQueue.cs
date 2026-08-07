using System.Threading.Channels;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Services;

public sealed class TranscriptionQueue : ITranscriptionQueue, IAsyncDisposable
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly Func<Guid, CancellationToken, Task> _processAsync;
    private readonly Func<Guid, CancellationToken, Task>? _retryAsync;
    private readonly TranscriptionPipeline? _pipeline;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _consumer;
    private TaskCompletionSource _idle = CompletedSource();
    private int _pending;
    private bool _disposed;

    public TranscriptionQueue(
        Func<Guid, CancellationToken, Task> processAsync,
        Func<Guid, CancellationToken, Task>? retryAsync = null)
    {
        _processAsync = processAsync ?? throw new ArgumentNullException(nameof(processAsync));
        _retryAsync = retryAsync;
        _consumer = ConsumeAsync();
    }

    public TranscriptionQueue(TranscriptionPipeline pipeline)
        : this(
            pipeline is null
                ? throw new ArgumentNullException(nameof(pipeline))
                : pipeline.ProcessAsync,
            async (sessionId, ct) =>
            {
                if (!await pipeline.PrepareRetryAsync(sessionId, ct))
                {
                    throw new InvalidOperationException("A Whisper model must be available before retrying transcription.");
                }
            })
    {
        _pipeline = pipeline;
        _pipeline.ProgressChanged += HandlePipelineProgressChanged;
    }

    public event EventHandler<SessionProgressEventArgs>? ProgressChanged;

    public void Enqueue(Guid sessionId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Interlocked.Increment(ref _pending) == 1)
        {
            Interlocked.Exchange(ref _idle, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        if (!_channel.Writer.TryWrite(sessionId))
        {
            CompleteOne();
            throw new InvalidOperationException("The transcription queue is no longer accepting work.");
        }
    }

    public async Task RetryAsync(Guid sessionId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        if (_retryAsync is not null)
        {
            await _retryAsync(sessionId, ct);
        }

        Enqueue(sessionId);
    }

    public Task WhenIdleAsync() => Volatile.Read(ref _idle).Task;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        await _cancellationTokenSource.CancelAsync();
        await _consumer;
        if (_pipeline is not null)
        {
            _pipeline.ProgressChanged -= HandlePipelineProgressChanged;
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var sessionId in _channel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
            {
                try
                {
                    await _processAsync(sessionId, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Failures are isolated so the next queued session can proceed.
                }
                finally
                {
                    CompleteOne();
                }
            }
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
        }
    }

    private void CompleteOne()
    {
        if (Interlocked.Decrement(ref _pending) == 0)
        {
            Volatile.Read(ref _idle).TrySetResult();
        }
    }

    private void HandlePipelineProgressChanged(object? sender, SessionProgressEventArgs eventArgs) =>
        ProgressChanged?.Invoke(this, eventArgs);

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
