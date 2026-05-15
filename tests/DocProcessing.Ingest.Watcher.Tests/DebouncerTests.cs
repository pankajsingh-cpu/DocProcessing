using DocProcessing.Ingest.Watcher;

namespace DocProcessing.Ingest.Watcher.Tests;

public class DebouncerTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(400);

    [Fact]
    public async Task Trigger_Fires_Once_After_Delay()
    {
        var fires = new List<string>();
        var done = new TaskCompletionSource();

        using var debouncer = new Debouncer<string>(Delay, (k, _) =>
        {
            fires.Add(k);
            done.TrySetResult();
            return Task.CompletedTask;
        });

        debouncer.Trigger("a");

        var completed = await Task.WhenAny(done.Task, Task.Delay(Delay + Slack));
        completed.Should().Be(done.Task);
        fires.Should().Equal("a");
    }

    [Fact]
    public async Task Trigger_Collapses_Rapid_Bursts_Into_Single_Fire()
    {
        var fires = new List<string>();
        var done = new TaskCompletionSource();

        using var debouncer = new Debouncer<string>(Delay, (k, _) =>
        {
            lock (fires) fires.Add(k);
            done.TrySetResult();
            return Task.CompletedTask;
        });

        for (var i = 0; i < 20; i++)
        {
            debouncer.Trigger("a");
            await Task.Delay(5); // shorter than Delay, so the timer keeps resetting
        }

        var completed = await Task.WhenAny(done.Task, Task.Delay(Delay + Slack));
        completed.Should().Be(done.Task);

        // Give any stragglers a chance to fire — they shouldn't.
        await Task.Delay(Delay);
        lock (fires) fires.Should().Equal("a");
    }

    [Fact]
    public async Task Trigger_Fires_Independently_Per_Key()
    {
        var fires = new HashSet<string>();
        var gate = new SemaphoreSlim(0, 3);

        using var debouncer = new Debouncer<string>(Delay, (k, _) =>
        {
            lock (fires) fires.Add(k);
            gate.Release();
            return Task.CompletedTask;
        });

        debouncer.Trigger("a");
        debouncer.Trigger("b");
        debouncer.Trigger("c");

        var allFired =
            await gate.WaitAsync(Delay + Slack) &&
            await gate.WaitAsync(Delay + Slack) &&
            await gate.WaitAsync(Delay + Slack);

        allFired.Should().BeTrue();
        lock (fires) fires.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public async Task Dispose_Cancels_Pending_Triggers()
    {
        var fires = 0;
        var debouncer = new Debouncer<string>(Delay, (_, _) =>
        {
            Interlocked.Increment(ref fires);
            return Task.CompletedTask;
        });

        debouncer.Trigger("a");
        debouncer.Dispose();

        await Task.Delay(Delay + Slack);
        fires.Should().Be(0);
    }
}
