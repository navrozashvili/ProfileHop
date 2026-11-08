namespace ProfileHop.Core.Tray;

/// <summary>
/// Runs every <see cref="IProfileSwitcher"/> call on one background thread so the UI thread never blocks on the mouse.
/// Requests are coalesced: rapid presses collapse into one absolute target ("profile 2"), relative "next" presses are
/// counted from the pending target (or the last known profile), and only the result is sent to the mouse.
/// Any exception from the switcher turns into <see cref="WorkerStatusKind.Unreachable"/>; the next request retries
/// (the switcher reconnects lazily).
/// </summary>
public sealed class ProfileWorker : IDisposable
{
    private readonly IProfileSwitcher _switcher;
    private readonly ILog? _log;
    private readonly object _lock = new(); // object, not Lock: used with Monitor.Wait/Pulse
    private Thread? _thread;
    private bool _disposed;

    // Pending work, guarded by _lock.
    private int? _target;         // absolute profile requested
    private int _nextCount;       // "next profile" presses on top of _target (or the current profile)
    private bool _refresh;
    private bool _stale;
    private int? _deviceProfile;
    private int _dpiSteps;

    // Worker-thread state.
    private ProfileState? _lastState;
    private volatile WorkerStatus _status = WorkerStatus.Starting;
    private volatile IReadOnlyList<int>? _cycleOverride;

    public ProfileWorker(IProfileSwitcher switcher, ILog? log = null)
    {
        _switcher = switcher;
        _log = log;
        _switcher.ProfileChangedByDevice += OnProfileChangedByDevice;
    }

    /// <summary>Raised on the worker thread after every processed batch (switch, refresh, failure).</summary>
    public event Action<WorkerStatus>? StatusChanged;

    /// <summary>Raised on the worker thread with the new DPI slot index.</summary>
    public event Action<int>? DpiSlotChanged;

    /// <summary>Latest status; safe to read from any thread.</summary>
    public WorkerStatus Status => _status;

    /// <summary>Explicit cycle order from config (null/empty = the mouse's enabled profiles). Settable on config reload.</summary>
    public IReadOnlyList<int>? CycleOverride
    {
        get => _cycleOverride;
        set => _cycleOverride = value;
    }

    public void Start()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "ProfileHop worker" };
            _thread.Start();
        }
    }

    public void RequestProfile(int profile) => Post(() =>
    {
        _target = profile;
        _nextCount = 0;
    });

    public void RequestNextProfile() => Post(() => _nextCount++);

    public void RequestNextDpiSlot() => Post(() => _dpiSteps++);

    public void RequestRefresh() => Post(() => _refresh = true);

    /// <summary>A HID device arrived or left (WM_DEVICECHANGE) or the PC resumed: drop the connection.</summary>
    public void NotifyDeviceChange() => Post(() => _stale = true);

    /// <summary>Runs one coalesced batch on the calling thread. Returns false when nothing was pending.</summary>
    public bool ProcessPending()
    {
        int? target, deviceProfile;
        int nextCount, dpiSteps;
        bool refresh, stale;
        lock (_lock)
        {
            if (!HasPendingLocked()) return false;
            (target, nextCount, refresh, stale, deviceProfile, dpiSteps) = (_target, _nextCount, _refresh, _stale, _deviceProfile, _dpiSteps);
            (_target, _nextCount, _refresh, _stale, _deviceProfile, _dpiSteps) = (null, 0, false, false, null, 0);
        }

        WorkerStatus status;
        try
        {
            if (stale)
            {
                _log?.Write(LogLevel.Debug, "Device change: marking connection stale.");
                _switcher.MarkStale();
                if (_status.Kind == WorkerStatusKind.Unreachable) refresh = true;
            }

            var state = Apply(target, nextCount, refresh, deviceProfile);

            for (var i = 0; i < dpiSteps; i++)
            {
                var slot = _switcher.NextDpiSlot();
                _log?.Write(LogLevel.Info, $"DPI slot {slot}.");
                DpiSlotChanged?.Invoke(slot);
                state = (state ?? _lastState ?? _switcher.GetState()) with { DpiIndex = slot };
            }

            if (state is null) return true; // only a stale mark: nothing new to show
            _lastState = state;
            status = WorkerStatus.Reachable(state);
        }
        catch (Exception ex)
        {
            _lastState = null;
            _log?.Write(LogLevel.Warning, $"Mouse not reachable: {ex.GetType().Name}: {ex.Message}");
            status = WorkerStatus.Unreachable(ex.Message);
        }

        _status = status;
        StatusChanged?.Invoke(status);
        return true;
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            thread = _thread;
            Monitor.PulseAll(_lock);
        }
        _switcher.ProfileChangedByDevice -= OnProfileChangedByDevice;
        // A switcher call can take a while (wireless wake-up); don't hang exit on it.
        thread?.Join(TimeSpan.FromSeconds(3));
    }

    private ProfileState? Apply(int? target, int nextCount, bool refresh, int? deviceProfile)
    {
        if (target is not null || nextCount > 0)
        {
            var overrideList = _cycleOverride;
            var needsState = target is null || (nextCount > 0 && overrideList is not { Count: > 0 });
            var known = needsState ? _lastState ?? _switcher.GetState() : _lastState;
            var profile = target ?? known!.CurrentProfile;
            for (var i = 0; i < nextCount; i++)
                profile = ProfileCycle.Next(profile, known?.EnabledProfiles ?? [], overrideList);

            _log?.Write(LogLevel.Info, $"Switching to profile {profile}.");
            var state = _switcher.SetProfile(profile);
            if (state.CurrentProfile != profile)
                _log?.Write(LogLevel.Warning, $"Asked for profile {profile}, mouse reports {state.CurrentProfile}.");
            return state;
        }

        if (refresh) return _switcher.GetState();

        if (deviceProfile is int p)
        {
            _log?.Write(LogLevel.Info, $"Mouse switched to profile {p} by itself.");
            return _lastState is null ? _switcher.GetState() : _lastState with { CurrentProfile = p };
        }

        return null;
    }

    private void OnProfileChangedByDevice(int profile) => Post(() => _deviceProfile = profile);

    private void Post(Action update)
    {
        lock (_lock)
        {
            if (_disposed) return;
            update();
            Monitor.Pulse(_lock);
        }
    }

    private bool HasPendingLocked() =>
        _target is not null || _nextCount > 0 || _refresh || _stale || _deviceProfile is not null || _dpiSteps > 0;

    private void Run()
    {
        while (true)
        {
            lock (_lock)
            {
                while (!_disposed && !HasPendingLocked()) Monitor.Wait(_lock);
                if (_disposed) return;
            }
            try
            {
                ProcessPending();
            }
            catch (Exception ex)
            {
                // Only a StatusChanged/DpiSlotChanged handler can get here; keep the worker alive.
                _log?.Write(LogLevel.Error, $"Worker handler failed: {ex}");
            }
        }
    }
}
