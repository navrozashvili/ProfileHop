using ProfileHop.Core.Hidpp;

namespace ProfileHop.Core.Tray;

/// <summary>
/// Runs every <see cref="IProfileSwitcher"/> call on one background thread so the UI thread never blocks on the mouse.
/// Requests are coalesced: rapid presses collapse into one absolute target ("profile 2"), relative "next"/"previous"
/// presses are summed into net steps counted from the pending target (or the last known profile), and only the result
/// is sent to the mouse.
/// Failures: <see cref="HostModeException"/> shows the host-mode state; a device rejection (HID++ error, unconfirmed
/// switch) keeps the last known state and sets <see cref="WorkerStatus.Notice"/>; anything else (timeout, transport
/// closed, mouse asleep or not found) turns into <see cref="WorkerStatusKind.Unreachable"/>. The next request retries
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
    private int _steps;           // net "next" (+1) / "previous" (−1) presses on top of _target (or the current profile)
    private bool _refresh;
    private bool _stale;
    private int? _deviceProfile;
    private int _dpiSteps;
    private (string? Device, bool ForceOnboard)? _configure;
    private bool _listDevices;
    private bool _rescan;
    private long _lastDevicesRequest; // ids handed out by RequestDevices
    private long _devicesRequest;     // latest id pending
    private volatile IReadOnlyList<KnownDevice>? _devices;

    // Worker-thread state.
    private ProfileState? _lastState;
    private int? _attemptedProfile;
    private bool _loggedConfiguredFallback;
    private volatile WorkerStatus _status = WorkerStatus.Starting;
    private volatile IReadOnlyList<int>? _cycleOverride;
    private volatile IReadOnlyList<int>? _configuredProfiles;

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

    /// <summary>
    /// Raised on the worker thread after <see cref="RequestDevices"/> with the device list (or the error), tagged with
    /// the id of the request it answers.
    /// </summary>
    public event Action<DeviceListResult>? DevicesChanged;

    /// <summary>Latest status; safe to read from any thread.</summary>
    public WorkerStatus Status => _status;

    /// <summary>Device list from the last <see cref="RequestDevices"/>, or null before the first; safe from any thread.</summary>
    public IReadOnlyList<KnownDevice>? Devices => _devices;

    /// <summary>Explicit cycle order from config (null/empty = the mouse's enabled profiles). Settable on config reload.</summary>
    public IReadOnlyList<int>? CycleOverride
    {
        get => _cycleOverride;
        set => _cycleOverride = value;
    }

    /// <summary>Profile numbers named in config (<c>profiles</c>). Settable on config reload.</summary>
    public IReadOnlyList<int>? ConfiguredProfiles
    {
        get => _configuredProfiles;
        set => _configuredProfiles = value;
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
        _steps = 0;
    });

    public void RequestNextProfile() => Post(() => _steps++);

    public void RequestPreviousProfile() => Post(() => _steps--);

    public void RequestNextDpiSlot() => Post(() => _dpiSteps++);

    public void RequestRefresh() => Post(() => _refresh = true);

    /// <summary>
    /// A HID device arrived or left (WM_DEVICECHANGE) or the PC resumed: drop the connection and reconnect with one
    /// refresh. Calls that arrive before the worker gets to them collapse into one reconnect.
    /// </summary>
    public void NotifyDeviceChange() => Post(() => _stale = true);

    /// <summary>Applies a new device preference / forceOnboardMode (reconnects) and refreshes the state.</summary>
    public void Reconfigure(string? device, bool forceOnboardMode) => Post(() => _configure = (device, forceOnboardMode));

    /// <summary>
    /// Fetches the device list into <see cref="Devices"/> and raises <see cref="DevicesChanged"/>. Without
    /// <paramref name="rescan"/> this only reads the last scan's result; with it every receiver is probed and the
    /// state is refreshed. Returns this request's id (increasing; 0 after dispose): <see cref="DeviceListResult.Answers"/>
    /// tells whether a <see cref="DevicesChanged"/> result is the answer to it.
    /// </summary>
    public long RequestDevices(bool rescan)
    {
        long id = 0;
        Post(() =>
        {
            id = ++_lastDevicesRequest;
            _devicesRequest = id;
            _listDevices = true;
            _rescan |= rescan;
        });
        return id;
    }

    /// <summary>Runs one coalesced batch on the calling thread. Returns false when nothing was pending.</summary>
    public bool ProcessPending()
    {
        int? target, deviceProfile;
        int steps, dpiSteps;
        bool refresh, stale, listDevices, rescan;
        long devicesRequest;
        (string? Device, bool ForceOnboard)? configure;
        lock (_lock)
        {
            if (!HasPendingLocked()) return false;
            (target, steps, refresh, stale, deviceProfile, dpiSteps) = (_target, _steps, _refresh, _stale, _deviceProfile, _dpiSteps);
            (_target, _steps, _refresh, _stale, _deviceProfile, _dpiSteps) = (null, 0, false, false, null, 0);
            (configure, listDevices, rescan) = (_configure, _listDevices, _rescan);
            (_configure, _listDevices, _rescan) = (null, false, false);
            devicesRequest = _devicesRequest;
        }

        if (configure is { } cfg)
        {
            _log?.Write(LogLevel.Info, $"Device: {cfg.Device ?? "automatic"}, forceOnboardMode {(cfg.ForceOnboard ? "on" : "off")}.");
            _switcher.Configure(cfg.Device, cfg.ForceOnboard);
            refresh = true;
        }

        if (listDevices)
        {
            ListDevices(rescan, devicesRequest);
            if (rescan) refresh = true;
        }

        if (!HasWork(target, steps, refresh, stale, deviceProfile, dpiSteps)) return true;

        WorkerStatus status;
        try
        {
            if (stale)
            {
                _log?.Write(LogLevel.Debug, "Device change: marking connection stale.");
                _switcher.MarkStale();
                // Reconnect right away (once per batch, however many notifications arrived): the new connection
                // re-subscribes the mouse's profile-change notifications, so mouse-button switches keep showing.
                refresh = true;
            }

            var state = Apply(target, steps, refresh, deviceProfile);

            for (var i = 0; i < dpiSteps; i++)
            {
                var slot = _switcher.NextDpiSlot();
                _log?.Write(LogLevel.Info, $"DPI slot {slot}.");
                DpiSlotChanged?.Invoke(slot);
                state = (state ?? _lastState ?? _switcher.GetState()) with { DpiIndex = slot };
            }

            if (state is null) return true;
            _lastState = state;
            status = WorkerStatus.Reachable(state);
        }
        catch (HostModeException ex)
        {
            _log?.Write(LogLevel.Warning, $"Not switching: {ex.Message}");
            _lastState = ex.State;
            status = WorkerStatus.Reachable(ex.State);
        }
        catch (Exception ex) when (ex is HidppException or HidppProtocolException)
        {
            status = Rejected(ex);
        }
        catch (Exception ex)
        {
            status = Unreachable(ex);
        }

        _status = status;
        StatusChanged?.Invoke(status);
        return true;
    }

    /// <summary>Stops the worker thread and cancels the switcher (<see cref="IProfileSwitcher.Cancel"/>); the switcher itself stays the caller's to dispose.</summary>
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
        // A switcher call can take a while (a rescan probes every receiver index): cancel it so it stops at the next
        // probe, and still don't hang exit on it.
        _switcher.Cancel();
        thread?.Join(TimeSpan.FromSeconds(3));
    }

    private WorkerStatus Unreachable(Exception ex)
    {
        _lastState = null;
        _log?.Write(LogLevel.Warning, $"Mouse not reachable: {ex.GetType().Name}: {ex.Message}");
        return WorkerStatus.Unreachable(ex.Message);
    }

    /// <summary>The mouse answered but refused (HID++ error / unconfirmed): keep the last known state, add a notice.</summary>
    private WorkerStatus Rejected(Exception ex)
    {
        string notice = (_attemptedProfile, ex) switch
        {
            (int p, HidppException h) => $"Profile {p} rejected by mouse ({h.Error})",
            (int p, _) => $"Profile {p} not confirmed by mouse",
            (null, HidppException h) => $"Mouse rejected the request ({h.Error})",
            _ => "Mouse did not confirm the request",
        };
        _log?.Write(LogLevel.Warning, $"{notice}: {ex.Message}");
        var state = _lastState;
        if (state is null)
        {
            try
            {
                state = _switcher.GetState();
            }
            catch (Exception ex2)
            {
                return Unreachable(ex2);
            }
        }
        _lastState = state;
        return WorkerStatus.Reachable(state) with { Notice = notice };
    }

    private ProfileState? Apply(int? target, int steps, bool refresh, int? deviceProfile)
    {
        _attemptedProfile = null;
        if (target is not null || steps != 0)
        {
            var overrideList = _cycleOverride;
            var needsState = target is null || (steps != 0 && overrideList is not { Count: > 0 });
            var known = needsState ? _lastState ?? _switcher.GetState() : _lastState;
            var profile = target ?? known!.CurrentProfile;
            if (steps != 0)
            {
                var (candidates, fallback) = ProfileCycle.Candidates(known?.EnabledProfiles ?? [], overrideList, _configuredProfiles);
                if (fallback && !_loggedConfiguredFallback)
                {
                    _loggedConfiguredFallback = true;
                    _log?.Write(LogLevel.Info, $"Mouse reports fewer than two enabled profiles; \"next\"/\"previous\" cycle the configured profiles ({string.Join(", ", candidates)}).");
                }
                for (var i = 0; i < Math.Abs(steps); i++)
                    profile = steps > 0 ? ProfileCycle.Next(profile, candidates, null) : ProfileCycle.Previous(profile, candidates, null);
            }

            _log?.Write(LogLevel.Info, $"Switching to profile {profile}.");
            _attemptedProfile = profile;
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
        HasWork(_target, _steps, _refresh, _stale, _deviceProfile, _dpiSteps) || _configure is not null || _listDevices;

    private static bool HasWork(int? target, int steps, bool refresh, bool stale, int? deviceProfile, int dpiSteps) =>
        target is not null || steps != 0 || refresh || stale || deviceProfile is not null || dpiSteps > 0;

    private void ListDevices(bool rescan, long requestId)
    {
        IReadOnlyList<KnownDevice> devices;
        try
        {
            if (rescan) _log?.Write(LogLevel.Info, "Rescanning devices.");
            devices = _switcher.ListDevices(rescan);
        }
        catch (Exception ex)
        {
            _log?.Write(LogLevel.Warning, $"Listing devices failed: {ex.GetType().Name}: {ex.Message}");
            DevicesChanged?.Invoke(new DeviceListResult(_devices ?? [], requestId, ex.Message));
            return;
        }
        if (rescan) _log?.Write(LogLevel.Info, $"Devices: {(devices.Count == 0 ? "none" : string.Join(", ", devices.Select(d => d.Key)))}.");
        _devices = devices;
        DevicesChanged?.Invoke(new DeviceListResult(devices, requestId, null));
    }

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
