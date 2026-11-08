namespace ProfileHop.Core.Tests.Hidpp.Fakes;

/// <summary>Minimal manually advanced clock: timers fire only on <see cref="Advance"/>.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_timers) return _now;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_timers) return _now.UtcTicks;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_timers)
        {
            _now += by;
            due = _timers.Where(t => t.DueAt is { } d && d <= _now).ToList();
            foreach (var t in due)
                t.DueAt = null;
        }
        foreach (var t in due)
            t.Fire();
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._timers)
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Dispose()
        {
            lock (owner._timers)
            {
                DueAt = null;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
