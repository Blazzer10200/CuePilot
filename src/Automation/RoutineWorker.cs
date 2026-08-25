namespace CuePilot;

internal static class RoutineWorker
{
    internal static Task Start(Func<Task> work) => Task.Run(work);
}

/// <summary>
/// Owns one background routine at a time. A replacement run cannot start until
/// the previous run has returned, including its cleanup and final status work.
/// </summary>
internal sealed class OwnedRoutineWorker : IDisposable
{
    internal static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(3);

    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private Task? task;
    private long generation;

    internal bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return task is { IsCompleted: false };
            }
        }
    }

    internal long Start(Func<long, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        lock (sync)
        {
            CleanupCompletedNoLock();
            if (task is not null)
            {
                throw new InvalidOperationException("The previous routine is still stopping.");
            }

            var source = new CancellationTokenSource();
            var currentGeneration = ++generation;
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellation = source;
            task = RoutineWorker.Start(async () =>
            {
                await startGate.Task.ConfigureAwait(false);
                try
                {
                    await work(currentGeneration, source.Token).ConfigureAwait(false);
                }
                finally
                {
                    Complete(currentGeneration, source);
                }
            });
            startGate.SetResult();
            return currentGeneration;
        }
    }

    internal void Cancel()
    {
        CancellationTokenSource? source;
        lock (sync)
        {
            source = cancellation;
        }

        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion won the race and already disposed this run.
        }
    }

    internal bool WaitForCompletion(TimeSpan? timeout = null)
    {
        Task? activeTask;
        lock (sync)
        {
            activeTask = task;
        }

        if (activeTask is null)
        {
            return true;
        }

        try
        {
            return activeTask.Wait(timeout ?? DefaultStopTimeout);
        }
        catch (AggregateException exception) when (exception.Flatten().InnerExceptions.All(
            static error => error is OperationCanceledException))
        {
            return true;
        }
        finally
        {
            if (activeTask.IsCompleted)
            {
                lock (sync)
                {
                    CleanupCompletedNoLock();
                }
            }
        }
    }

    internal bool Stop(TimeSpan? timeout = null)
    {
        Cancel();
        return WaitForCompletion(timeout);
    }

    private void Complete(long completedGeneration, CancellationTokenSource source)
    {
        var disposeSource = false;
        lock (sync)
        {
            if (generation == completedGeneration && ReferenceEquals(cancellation, source))
            {
                cancellation = null;
                task = null;
                disposeSource = true;
            }
        }

        if (disposeSource)
        {
            source.Dispose();
        }
    }

    private void CleanupCompletedNoLock()
    {
        if (task is not { IsCompleted: true })
        {
            return;
        }

        task = null;
        cancellation?.Dispose();
        cancellation = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
