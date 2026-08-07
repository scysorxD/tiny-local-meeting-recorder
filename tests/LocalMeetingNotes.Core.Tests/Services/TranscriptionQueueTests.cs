using FluentAssertions;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Tests.Services;

public sealed class TranscriptionQueueTests
{
    [Fact]
    public async Task Enqueue_processes_sessions_with_a_single_consumer()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concurrentProcessors = 0;
        var highestConcurrency = 0;

        await using var queue = new TranscriptionQueue(async (_, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref concurrentProcessors);
            highestConcurrency = Math.Max(highestConcurrency, current);

            try
            {
                firstStarted.TrySetResult();
                await allowFirstToFinish.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref concurrentProcessors);
            }
        });

        queue.Enqueue(Guid.NewGuid());
        await firstStarted.Task;
        queue.Enqueue(Guid.NewGuid());

        await Task.Delay(100);
        highestConcurrency.Should().Be(1);

        allowFirstToFinish.SetResult();
        await queue.WhenIdleAsync();
        highestConcurrency.Should().Be(1);
    }

    [Fact]
    public async Task Enqueue_when_a_session_fails_continues_with_the_next_session()
    {
        var processed = new List<Guid>();
        var failedSession = Guid.NewGuid();
        var succeedingSession = Guid.NewGuid();

        await using var queue = new TranscriptionQueue((sessionId, _) =>
        {
            processed.Add(sessionId);
            return sessionId == failedSession
                ? Task.FromException(new InvalidOperationException("Whisper failed."))
                : Task.CompletedTask;
        });

        queue.Enqueue(failedSession);
        queue.Enqueue(succeedingSession);

        await queue.WhenIdleAsync();

        processed.Should().Equal(failedSession, succeedingSession);
    }

    [Fact]
    public async Task DisposeAsync_cancels_an_active_session()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var queue = new TranscriptionQueue(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }
        });

        queue.Enqueue(Guid.NewGuid());
        await started.Task;

        await queue.DisposeAsync();

        await cancelled.Task;
    }
}
