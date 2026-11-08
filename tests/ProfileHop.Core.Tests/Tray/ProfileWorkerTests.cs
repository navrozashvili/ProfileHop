using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class ProfileWorkerTests
{
    internal sealed class FakeSwitcher : IProfileSwitcher
    {
        public int Current { get; set; } = 1;
        public List<int> Enabled { get; set; } = [1, 2, 3];
        public int Mode { get; set; } = 1;
        public int Dpi { get; set; }
        public Exception? FailWith { get; set; }
        public Exception? FailSetWith { get; set; }
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }
        public Func<int, int>? ConfirmedProfile { get; set; }

        public event Action<int>? ProfileChangedByDevice;

        public bool HasSubscribers => ProfileChangedByDevice is not null;

        public void RaiseDeviceChange(int profile) => ProfileChangedByDevice?.Invoke(profile);

        private ProfileState State => new(Mode, Current, Enabled, Dpi);

        public ProfileState GetState()
        {
            lock (Calls) Calls.Add("get");
            ThrowIfFailing();
            return State;
        }

        public ProfileState SetProfile(int profile)
        {
            lock (Calls) Calls.Add($"set {profile}");
            ThrowIfFailing();
            if (FailSetWith is not null) throw FailSetWith;
            Current = ConfirmedProfile?.Invoke(profile) ?? profile;
            return State;
        }

        public int NextDpiSlot()
        {
            lock (Calls) Calls.Add("dpi");
            ThrowIfFailing();
            return ++Dpi;
        }

        public void MarkStale()
        {
            lock (Calls) Calls.Add("stale");
        }

        public List<KnownDevice> Devices { get; set; } = [];
        public Exception? ListFailsWith { get; set; }

        public void Configure(string? preferredDevice, bool forceOnboardMode)
        {
            lock (Calls) Calls.Add($"configure {preferredDevice ?? "auto"} {(forceOnboardMode ? "force" : "noforce")}");
        }

        public IReadOnlyList<KnownDevice> ListDevices(bool rescan)
        {
            lock (Calls) Calls.Add(rescan ? "rescan" : "list");
            if (ListFailsWith is not null) throw ListFailsWith;
            return Devices;
        }

        public void Dispose() => Disposed = true;

        private void ThrowIfFailing()
        {
            if (FailWith is not null) throw FailWith;
        }
    }

    private static (ProfileWorker worker, FakeSwitcher mouse, List<WorkerStatus> statuses) Create()
    {
        var mouse = new FakeSwitcher();
        var worker = new ProfileWorker(mouse);
        var statuses = new List<WorkerStatus>();
        worker.StatusChanged += s => { lock (statuses) statuses.Add(s); };
        return (worker, mouse, statuses);
    }

    [Fact]
    public void Starts_in_starting_state_and_does_nothing_without_requests()
    {
        var (worker, mouse, statuses) = Create();
        Assert.Equal(WorkerStatusKind.Starting, worker.Status.Kind);
        Assert.False(worker.ProcessPending());
        Assert.Empty(mouse.Calls);
        Assert.Empty(statuses);
    }

    [Fact]
    public void Refresh_reads_state()
    {
        var (worker, mouse, statuses) = Create();
        mouse.Current = 2;

        worker.RequestRefresh();
        Assert.True(worker.ProcessPending());

        Assert.Equal(["get"], mouse.Calls);
        var s = Assert.Single(statuses);
        Assert.True(s.IsReachable);
        Assert.Equal(2, s.State!.CurrentProfile);
        Assert.Same(s, worker.Status);
    }

    [Fact]
    public void Rapid_absolute_requests_keep_only_the_latest()
    {
        var (worker, mouse, statuses) = Create();

        worker.RequestProfile(2);
        worker.RequestProfile(3);
        worker.RequestProfile(1);
        worker.ProcessPending();

        Assert.Equal(["set 1"], mouse.Calls);
        Assert.Equal(1, Assert.Single(statuses).State!.CurrentProfile);
    }

    [Fact]
    public void Absolute_request_supersedes_pending_refresh()
    {
        var (worker, mouse, _) = Create();
        worker.RequestRefresh();
        worker.RequestProfile(2);
        worker.ProcessPending();
        Assert.Equal(["set 2"], mouse.Calls);
    }

    [Fact]
    public void Next_becomes_absolute_target_from_known_state()
    {
        var (worker, mouse, _) = Create();
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();

        worker.RequestNextProfile();
        worker.ProcessPending();

        Assert.Equal(["set 2"], mouse.Calls);
    }

    [Fact]
    public void Rapid_next_presses_advance_from_the_pending_target()
    {
        var (worker, mouse, _) = Create();
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();

        worker.RequestNextProfile(); // 1 -> 2
        worker.RequestNextProfile(); // 2 -> 3
        worker.RequestNextProfile(); // 3 -> 1 (wrap)
        worker.RequestNextProfile(); // 1 -> 2
        worker.ProcessPending();

        Assert.Equal(["set 2"], mouse.Calls);
    }

    [Fact]
    public void Next_after_absolute_advances_from_it()
    {
        var (worker, mouse, _) = Create();
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();

        worker.RequestProfile(3);
        worker.RequestNextProfile();
        worker.ProcessPending();

        Assert.Equal(["set 1"], mouse.Calls);
    }

    [Fact]
    public void Next_with_unknown_state_reads_state_first()
    {
        var (worker, mouse, statuses) = Create();
        mouse.Current = 2;

        worker.RequestNextProfile();
        worker.ProcessPending();

        Assert.Equal(["get", "set 3"], mouse.Calls);
        Assert.Equal(3, Assert.Single(statuses).State!.CurrentProfile);
    }

    [Fact]
    public void Several_next_presses_with_unknown_state_all_count()
    {
        var (worker, mouse, _) = Create();
        worker.RequestNextProfile();
        worker.RequestNextProfile();
        worker.ProcessPending();
        Assert.Equal(["get", "set 3"], mouse.Calls);
    }

    [Fact]
    public void Absolute_request_cancels_unresolved_next()
    {
        var (worker, mouse, _) = Create();
        worker.RequestNextProfile();
        worker.RequestProfile(1);
        worker.ProcessPending();
        Assert.Equal(["set 1"], mouse.Calls);
    }

    [Fact]
    public void Cycle_override_is_used_for_next()
    {
        var (worker, mouse, _) = Create();
        worker.CycleOverride = [1, 3];
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();

        worker.RequestNextProfile();
        worker.ProcessPending();

        Assert.Equal(["set 3"], mouse.Calls);
    }

    [Fact]
    public void Next_with_one_enabled_profile_cycles_configured_profiles_and_logs_once()
    {
        var log = new ListLog();
        var mouse = new FakeSwitcher { Enabled = [1] }; // real mouse: "enabled profiles: 1"
        var worker = new ProfileWorker(mouse, log) { ConfiguredProfiles = [1, 2] };
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();

        worker.RequestNextProfile();
        worker.ProcessPending();
        worker.RequestNextProfile();
        worker.ProcessPending();

        Assert.Equal(["set 2", "set 1"], mouse.Calls);
        Assert.Single(log.Lines, l => l.Message.Contains("configured profiles", StringComparison.Ordinal));
    }

    [Fact]
    public void Status_reports_confirmed_profile_not_requested_one()
    {
        var (worker, mouse, statuses) = Create();
        mouse.ConfirmedProfile = _ => 1; // device refused and stayed on 1

        worker.RequestProfile(2);
        worker.ProcessPending();

        Assert.Equal(1, Assert.Single(statuses).State!.CurrentProfile);
    }

    [Fact]
    public void Failure_becomes_unreachable_and_next_press_retries()
    {
        var (worker, mouse, statuses) = Create();
        mouse.FailWith = new IOException("device gone");

        worker.RequestProfile(2);
        worker.ProcessPending();

        var failed = Assert.Single(statuses);
        Assert.Equal(WorkerStatusKind.Unreachable, failed.Kind);
        Assert.Contains("device gone", failed.Error);

        mouse.FailWith = null;
        worker.RequestProfile(2);
        worker.ProcessPending();

        Assert.Equal(["set 2", "set 2"], mouse.Calls);
        Assert.True(worker.Status.IsReachable);
    }

    [Fact]
    public void Host_mode_switch_shows_host_mode_status_not_unreachable()
    {
        var (worker, mouse, statuses) = Create();
        var host = new ProfileState(2, 0, [1], 4);
        mouse.FailWith = new HostModeException(host);

        worker.RequestProfile(2);
        worker.ProcessPending();

        var s = Assert.Single(statuses);
        Assert.Equal(WorkerStatusKind.Reachable, s.Kind);
        Assert.Same(host, s.State);
        Assert.Equal("ProfileHop: mouse in host mode (close G HUB)", TrayText.Tooltip(s, []));
    }

    [Fact]
    public void Device_rejection_keeps_reachable_status_with_notice()
    {
        var log = new ListLog();
        var mouse = new FakeSwitcher();
        var worker = new ProfileWorker(mouse, log);
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.FailWith = new Core.Hidpp.HidppException(Core.Hidpp.HidppError.InvalidArgument, 1, 0x0E, 3);

        worker.RequestProfile(2);
        worker.ProcessPending();

        Assert.Equal(WorkerStatusKind.Reachable, worker.Status.Kind);
        Assert.Equal(1, worker.Status.State!.CurrentProfile);
        Assert.Equal("Profile 2 rejected by mouse (InvalidArgument)", worker.Status.Notice);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("rejected"));
    }

    [Fact]
    public void Device_rejection_with_unknown_state_reads_state()
    {
        var (worker, mouse, _) = Create();
        mouse.FailSetWith = new Core.Hidpp.HidppProtocolException("not confirmed");

        worker.RequestProfile(2);
        worker.ProcessPending();

        Assert.Equal(["set 2", "get"], mouse.Calls);
        Assert.Equal(WorkerStatusKind.Reachable, worker.Status.Kind);
        Assert.Equal(1, worker.Status.State!.CurrentProfile);
        Assert.Equal("Profile 2 not confirmed by mouse", worker.Status.Notice);
    }

    [Fact]
    public void Next_after_failure_reads_state_again()
    {
        var (worker, mouse, _) = Create();
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.FailWith = new TimeoutException();
        worker.RequestProfile(2);
        worker.ProcessPending();
        mouse.FailWith = null;
        mouse.Calls.Clear();
        mouse.Current = 3;

        worker.RequestNextProfile();
        worker.ProcessPending();

        Assert.Equal(["get", "set 1"], mouse.Calls);
    }

    [Fact]
    public void Device_change_burst_marks_stale_and_reconnects_once_when_reachable()
    {
        // Reconnecting re-subscribes the profile-change notifications (mouse-button switches); without it the tray
        // would stop following the mouse after any HID arrival or resume.
        var (worker, mouse, statuses) = Create();
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();
        statuses.Clear();

        worker.NotifyDeviceChange();
        worker.NotifyDeviceChange();
        worker.NotifyDeviceChange();
        worker.ProcessPending();

        Assert.Equal(["stale", "get"], mouse.Calls);
        Assert.True(Assert.Single(statuses).IsReachable);
        Assert.False(worker.ProcessPending());
    }

    [Fact]
    public void Device_change_while_unreachable_retries_to_clear_grey_icon()
    {
        var (worker, mouse, _) = Create();
        mouse.FailWith = new IOException("unplugged");
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.FailWith = null;
        mouse.Calls.Clear();

        worker.NotifyDeviceChange();
        worker.ProcessPending();

        Assert.Equal(["stale", "get"], mouse.Calls);
        Assert.True(worker.Status.IsReachable);
    }

    [Fact]
    public void Stale_is_applied_before_a_pending_switch()
    {
        var (worker, mouse, _) = Create();
        worker.RequestProfile(2);
        worker.NotifyDeviceChange();
        worker.ProcessPending();
        Assert.Equal(["stale", "set 2"], mouse.Calls);
    }

    [Fact]
    public void Profile_change_from_device_updates_status_without_switching()
    {
        var (worker, mouse, statuses) = Create();
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();

        mouse.RaiseDeviceChange(3);
        worker.ProcessPending();

        Assert.Empty(mouse.Calls);
        Assert.Equal(3, statuses[^1].State!.CurrentProfile);
        Assert.Equal([1, 2, 3], statuses[^1].State!.EnabledProfiles);
    }

    [Fact]
    public void Profile_change_from_device_with_unknown_state_reads_state()
    {
        var (worker, mouse, _) = Create();
        mouse.Current = 2;
        mouse.RaiseDeviceChange(2);
        worker.ProcessPending();
        Assert.Equal(["get"], mouse.Calls);
        Assert.Equal(2, worker.Status.State!.CurrentProfile);
    }

    [Fact]
    public void Dpi_presses_each_step_after_the_profile_switch()
    {
        var (worker, mouse, _) = Create();
        var slots = new List<int>();
        worker.DpiSlotChanged += slots.Add;

        worker.RequestNextDpiSlot();
        worker.RequestProfile(2);
        worker.RequestNextDpiSlot();
        worker.ProcessPending();

        Assert.Equal(["set 2", "dpi", "dpi"], mouse.Calls);
        Assert.Equal([1, 2], slots);
        Assert.Equal(2, worker.Status.State!.DpiIndex);
    }

    [Fact]
    public void Dpi_failure_is_unreachable()
    {
        var (worker, mouse, _) = Create();
        mouse.FailWith = new IOException("x");
        worker.RequestNextDpiSlot();
        worker.ProcessPending();
        Assert.Equal(WorkerStatusKind.Unreachable, worker.Status.Kind);
    }

    [Fact]
    public void Failures_are_logged()
    {
        var mouse = new FakeSwitcher { FailWith = new IOException("cable") };
        var log = new ListLog();
        var worker = new ProfileWorker(mouse, log);

        worker.RequestProfile(2);
        worker.ProcessPending();

        Assert.Contains(log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("cable", StringComparison.Ordinal));
    }

    [Fact]
    public void Background_thread_processes_requests_and_dispose_stops_it()
    {
        var mouse = new FakeSwitcher();
        var worker = new ProfileWorker(mouse);
        using var done = new ManualResetEventSlim();
        worker.StatusChanged += s => { if (s.State?.CurrentProfile == 2) done.Set(); };

        worker.Start();
        worker.RequestProfile(2);

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        worker.Dispose();
        Assert.False(mouse.HasSubscribers);
        Assert.False(mouse.Disposed); // the switcher is owned by the caller
        worker.RequestProfile(1); // after dispose: ignored, no throw
    }

    // ---- device choice ----

    [Fact]
    public void Reconfigure_configures_switcher_then_refreshes_state()
    {
        var (worker, mouse, statuses) = Create();

        worker.Reconfigure("C54D/PRO X2 SUPERSTRIKE", forceOnboardMode: true);
        worker.ProcessPending();

        Assert.Equal(["configure C54D/PRO X2 SUPERSTRIKE force", "get"], mouse.Calls);
        Assert.True(Assert.Single(statuses).IsReachable);
    }

    [Fact]
    public void Reconfigure_runs_before_a_pending_switch()
    {
        var (worker, mouse, _) = Create();

        worker.RequestProfile(2);
        worker.Reconfigure(null, forceOnboardMode: false);
        worker.ProcessPending();

        Assert.Equal(["configure auto noforce", "set 2"], mouse.Calls);
    }

    [Fact]
    public void RequestDevices_lists_and_publishes_without_touching_state()
    {
        var (worker, mouse, statuses) = Create();
        mouse.Devices = [new KnownDevice("C54D/PRO X2 SUPERSTRIKE", "PRO X2 SUPERSTRIKE (receiver C54D)", 3, true)];
        var published = new List<IReadOnlyList<KnownDevice>>();
        worker.DevicesChanged += d => published.Add(d);
        Assert.Null(worker.Devices);

        worker.RequestDevices(rescan: false);
        worker.ProcessPending();

        Assert.Equal(["list"], mouse.Calls);
        Assert.Equal(mouse.Devices, Assert.Single(published));
        Assert.Equal(mouse.Devices, worker.Devices);
        Assert.Empty(statuses);
    }

    [Fact]
    public void RequestDevices_rescan_also_refreshes_state()
    {
        var (worker, mouse, statuses) = Create();

        worker.RequestDevices(rescan: false);
        worker.RequestDevices(rescan: true);
        worker.ProcessPending();

        Assert.Equal(["rescan", "get"], mouse.Calls);
        Assert.Single(statuses);
    }

    [Fact]
    public void RequestDevices_failure_is_logged_and_keeps_status()
    {
        var mouse = new FakeSwitcher { ListFailsWith = new IOException("boom") };
        var log = new ListLog();
        var worker = new ProfileWorker(mouse, log);
        var published = 0;
        worker.DevicesChanged += _ => published++;

        worker.RequestDevices(rescan: false);
        worker.ProcessPending();

        Assert.Equal(WorkerStatusKind.Starting, worker.Status.Kind);
        Assert.Equal(0, published);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("boom"));
    }

    internal sealed class ListLog : ILog
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public LogLevel MinLevel { get; set; } = LogLevel.Debug;

        /// <summary>Calls to IsEnabled; Write records every line, even below MinLevel, so tests can catch skipped checks.</summary>
        public bool IsEnabled(LogLevel level) => level >= MinLevel;

        public void Write(LogLevel level, string message)
        {
            lock (Lines) Lines.Add((level, message));
        }
    }
}
