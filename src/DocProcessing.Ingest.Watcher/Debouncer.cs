using System.Collections.Concurrent;

namespace DocProcessing.Ingest.Watcher;

// Collapses N rapid Trigger(key) calls into a single onFire(key) invocation
// once `delay` has elapsed with no further triggers for that key.
// Per-key state lives in a ConcurrentDictionary so different keys fire independently.
public sealed class Debouncer<TKey> : IDisposable where TKey : notnull
{
    private readonly TimeSpan _delay;
    private readonly Func<TKey, CancellationToken, Task> _onFire;
    private readonly ConcurrentDictionary<TKey, CancellationTokenSource> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TimeProvider _time;

    public Debouncer(
        TimeSpan delay,
        Func<TKey, CancellationToken, Task> onFire,
        TimeProvider? time = null)
    {
        _delay = delay;
        _onFire = onFire;
        _time = time ?? TimeProvider.System;
    }

    public void Trigger(TKey key)
    {
        if (_shutdown.IsCancellationRequested) return;

        var newCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);

        _pending.AddOrUpdate(
            key,
            addValueFactory: _ => newCts,
            updateValueFactory: (_, previous) =>
            {
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
                return newCts;
            });

        _ = FireLater(key, newCts);
    }

    private async Task FireLater(TKey key, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_delay, _time, cts.Token);
            await _onFire(key, _shutdown.Token);
        }
        catch (OperationCanceledException) { /* superseded or shutting down */ }
        finally
        {
            if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, cts))
            {
                _pending.TryRemove(KeyValuePair.Create(key, cts));
            }
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (_shutdown.IsCancellationRequested) return;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
