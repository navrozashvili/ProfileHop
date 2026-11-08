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

        public event Action? DeviceArrived;

        public void RaiseDeviceArrived() => DeviceArrived?.Invoke();

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
            if (BlockListUntilCancelled)
            {
                ListEntered.Set();
                Cancelled.Wait();
                throw new OperationCanceledException();
            }
            if (ListFailsWith is not null) throw ListFailsWith;
            return Devices;
        }

        /// <summary>ListDevices blocks (like a long rescan) until <see cref="Cancel"/>.</summary>
        public bool BlockListUntilCancelled { get; set; }
        public ManualResetEventSlim ListEntered { get; } = new();
        public ManualResetEventSlim Cancelled { get; } = new();

        public void Cancel()
        {
            lock (Calls) Calls.Add("cancel");
            Cancelled.Set();
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

    private static (ProfileWorker worker, FakeSwitcher mouse) Known(int current)
    {
        var (worker, mouse, _) = Create();
        mouse.Current = current;
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();
        return (worker, mouse);
    }

    [Fact]
    public void Previous_goes_back_from_known_state_and_wraps()
    {
        var (worker, mouse) = Known(1);

        worker.RequestPreviousProfile();
        worker.ProcessPending();

        Assert.Equal(["set 3"], mouse.Calls);
    }

    [Fact]
    public void Rapid_previous_presses_accumulate()
    {
        var (worker, mouse) = Known(3);

        worker.RequestPreviousProfile(); // 3 -> 2
        worker.RequestPreviousProfile(); // 2 -> 1
        worker.RequestPreviousProfile(); // 1 -> 3 (wrap)
        worker.RequestPreviousProfile(); // 3 -> 2
        worker.ProcessPending();

        Assert.Equal(["set 2"], mouse.Calls);
    }

    [Fact]
    public void Next_next_previous_nets_one_step_forward()
    {
        var (worker, mouse) = Known(1);

        worker.RequestNextProfile();
        worker.RequestNextProfile();
        worker.RequestPreviousProfile();
        worker.ProcessPending();

        Assert.Equal(["set 2"], mouse.Calls);
    }

    [Fact]
    public void Previous_cancels_next_when_net_steps_are_zero()
    {
        var (worker, mouse) = Known(2);

        worker.RequestNextProfile();
        worker.RequestPreviousProfile();

        Assert.False(worker.ProcessPending());
        Assert.Empty(mouse.Calls);
    }

    [Fact]
    public void Previous_after_absolute_goes_back_from_it()
    {
        var (worker, mouse) = Known(2);

        worker.RequestProfile(1);
        worker.RequestPreviousProfile();
        worker.ProcessPending();

        Assert.Equal(["set 3"], mouse.Calls);
    }

    [Fact]
    public void Absolute_request_cancels_unresolved_previous()
    {
        var (worker, mouse, _) = Create();
        worker.RequestPreviousProfile();
        worker.RequestProfile(2);
        worker.ProcessPending();
        Assert.Equal(["set 2"], mouse.Calls);
    }

    [Fact]
    public void Absolute_target_with_net_zero_steps_still_switches_to_it()
    {
        var (worker, mouse) = Known(1);

        worker.RequestProfile(3);
        worker.RequestNextProfile();
        worker.RequestPreviousProfile();
        worker.ProcessPending();

        Assert.Equal(["set 3"], mouse.Calls);
    }

    [Fact]
    public void Previous_with_unknown_state_reads_state_first()
    {
        var (worker, mouse, _) = Create();
        mouse.Current = 2;

        worker.RequestPreviousProfile();
        worker.ProcessPending();

        Assert.Equal(["get", "set 1"], mouse.Calls);
    }

    [Fact]
    public void Previous_uses_cycle_override()
    {
        var (worker, mouse) = Known(1);
        worker.CycleOverride = [1, 3];

        worker.RequestPreviousProfile();
        worker.ProcessPending();

        Assert.Equal(["set 3"], mouse.Calls);
    }

    [Fact]
    public void Previous_with_one_enabled_profile_cycles_configured_profiles()
    {
        var mouse = new FakeSwitcher { Enabled = [1] };
        var worker = new ProfileWorker(mouse) { ConfiguredProfiles = [1, 2, 4] };
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();

        worker.RequestPreviousProfile();
        worker.ProcessPending();

        Assert.Equal(["set 4"], mouse.Calls);
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

    [Fact]
    public void Dispose_cancels_an_in_flight_switcher_call_so_exit_is_prompt()
    {
        var mouse = new FakeSwitcher { BlockListUntilCancelled = true };
        var worker = new ProfileWorker(mouse);
        worker.Start();
        worker.RequestDevices(rescan: true);
        Assert.True(mouse.ListEntered.Wait(TimeSpan.FromSeconds(5)));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        worker.Dispose();

        Assert.True(mouse.Cancelled.IsSet);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Dispose took {sw.Elapsed}");
    }

    [Fact]
    public void Chosen_device_not_found_is_unreachable_with_its_name_and_next_press_retries()
    {
        var (worker, mouse, statuses) = Create();
        mouse.FailWith = new ChosenDeviceNotFoundException("C54D/PRO X2 SUPERSTRIKE", []);

        worker.RequestNextProfile();
        worker.ProcessPending();

        var failed = Assert.Single(statuses);
        Assert.Equal(WorkerStatusKind.Unreachable, failed.Kind);
        Assert.Equal("PRO X2 SUPERSTRIKE", failed.MissingDevice);

        mouse.FailWith = null;
        worker.RequestNextProfile();
        worker.ProcessPending();
        Assert.True(worker.Status.IsReachable);
        Assert.Null(worker.Status.MissingDevice);
    }

    [Fact]
    public void Chosen_device_arriving_marks_stale_and_refreshes_once()
    {
        var (worker, mouse, _) = Create();
        mouse.FailWith = new ChosenDeviceNotFoundException("C54D/PRO X2 SUPERSTRIKE", []);
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.FailWith = null;
        mouse.Calls.Clear();

        mouse.RaiseDeviceArrived();
        mouse.RaiseDeviceArrived();
        worker.ProcessPending();

        Assert.Equal(["stale", "get"], mouse.Calls);
        Assert.True(worker.Status.IsReachable);
        Assert.False(worker.ProcessPending());
    }

    [Fact]
    public void Reconfigure_with_the_same_device_still_reconnects()
    {
        var (worker, mouse, _) = Create();
        worker.Reconfigure("C54D/PRO X2 SUPERSTRIKE", forceOnboardMode: true);
        worker.ProcessPending();
        mouse.Calls.Clear();

        worker.Reconfigure("C54D/PRO X2 SUPERSTRIKE", forceOnboardMode: true);
        worker.ProcessPending();

        Assert.Equal(["configure C54D/PRO X2 SUPERSTRIKE force", "get"], mouse.Calls);
    }

    [Fact]
    public void Dispose_unsubscribes_from_device_arrival()
    {
        var (worker, mouse, _) = Create();
        worker.Dispose();

        mouse.RaiseDeviceArrived();

        Assert.False(worker.ProcessPending());
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
        worker.DevicesChanged += d => published.Add(d.Devices);
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
        var published = new List<DeviceListResult>();
        worker.DevicesChanged += published.Add;

        long id = worker.RequestDevices(rescan: false);
        worker.ProcessPending();

        Assert.Equal(WorkerStatusKind.Starting, worker.Status.Kind);
        // Still answered (with the error), so the tray's "Rescanning…" never gets stuck.
        var r = Assert.Single(published);
        Assert.Equal(id, r.RequestId);
        Assert.Equal("boom", r.Error);
        Assert.Empty(r.Devices);
        Assert.Null(worker.Devices);
        Assert.Contains(log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("boom"));
    }

    [Fact]
    public void RequestDevices_ids_increase_and_each_result_names_the_latest_request_it_answers()
    {
        var (worker, _, _) = Create();
        var published = new List<DeviceListResult>();
        worker.DevicesChanged += published.Add;

        long first = worker.RequestDevices(rescan: false);
        worker.ProcessPending();
        long second = worker.RequestDevices(rescan: true);
        long third = worker.RequestDevices(rescan: false); // coalesced with the rescan
        worker.ProcessPending();

        Assert.True(first > 0 && second > first && third > second);
        Assert.Equal([first, third], published.Select(p => p.RequestId));
    }

    [Fact]
    public void Rescan_result_is_recognised_even_after_unrelated_status_changes()
    {
        // The tray's "Rescan found N devices" balloon: a hotkey batch finishing first must not swallow it.
        var (worker, _, statuses) = Create();
        var published = new List<DeviceListResult>();
        worker.DevicesChanged += published.Add;

        long menuOpened = worker.RequestDevices(rescan: false);
        worker.ProcessPending();
        long rescan = worker.RequestDevices(rescan: true);
        worker.RequestProfile(2);
        worker.ProcessPending();

        Assert.Single(statuses);
        Assert.False(published[0].Answers(rescan)); // the earlier menu listing is not the rescan's answer
        Assert.True(published[1].Answers(rescan));
        Assert.False(published[1].Answers(0));      // nothing pending
        Assert.True(menuOpened < rescan);
    }

    // ---- user vs automatic switches ----

    [Fact]
    public void UserRequestStatusCarriesUserSwitch()
    {
        var (worker, mouse, statuses) = Create();

        worker.RequestProfile(2, new("a", true));
        worker.ProcessPending();

        Assert.Equal(["set 2"], mouse.Calls);
        var s = Assert.Single(statuses);
        Assert.Equal(new UserSwitch("a", true), s.User);
        Assert.False(s.Automatic);
    }

    [Fact]
    public void AutoRequestStatusIsAutomatic()
    {
        var (worker, mouse, statuses) = Create();

        worker.RequestAutoProfile(3);
        worker.ProcessPending();

        Assert.Equal(["set 3"], mouse.Calls);
        var s = Assert.Single(statuses);
        Assert.True(s.Automatic);
        Assert.Null(s.User);
    }

    [Fact]
    public void AutoTargetEqualToCurrentSendsNothing()
    {
        var (worker, mouse, statuses) = Create();
        mouse.Current = 2;
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();
        statuses.Clear();

        worker.RequestAutoProfile(2);
        worker.ProcessPending();

        Assert.Empty(mouse.Calls);
        Assert.Empty(statuses);
        Assert.False(worker.ProcessPending());
    }

    [Fact]
    public void AutoTargetEqualToPendingIsStillApplied()
    {
        // Coalesced A→B→A: the net target is the current profile, so nothing is sent.
        var (worker, mouse) = Known(1);
        worker.RequestAutoProfile(3);
        worker.RequestAutoProfile(1);
        worker.ProcessPending();
        Assert.Empty(mouse.Calls);

        // Processed one by one: the skip compares against the confirmed state (now 3), not the one from before.
        worker.RequestAutoProfile(3);
        worker.ProcessPending();
        worker.RequestAutoProfile(1);
        worker.ProcessPending();
        Assert.Equal(["set 3", "set 1"], mouse.Calls);
    }

    [Fact]
    public void AutoAfterUserKeepsUserSwitchAndRunsNext()
    {
        var (worker, mouse, statuses) = Create();
        mouse.Enabled = [1, 2, 3, 4];

        worker.RequestProfile(2, new("a", true));
        worker.RequestAutoProfile(4);

        Assert.True(worker.ProcessPending());
        Assert.Equal(["set 2"], mouse.Calls);
        var user = Assert.Single(statuses);
        Assert.Equal("a", user.User?.LearnFor);
        Assert.False(user.Automatic);

        Assert.True(worker.ProcessPending());
        Assert.Equal(["set 2", "set 4"], mouse.Calls);
        Assert.Equal(2, statuses.Count);
        Assert.Null(statuses[1].User);
        Assert.True(statuses[1].Automatic);
        Assert.False(worker.ProcessPending());
    }

    [Fact]
    public void PendingAutoTargetAfterUserIsSkippedWhenAlreadyThere()
    {
        var (worker, mouse) = Known(1);

        worker.RequestProfile(2, new("a", true));
        worker.RequestAutoProfile(2);
        worker.ProcessPending();
        worker.ProcessPending();

        Assert.Equal(["set 2"], mouse.Calls);
        Assert.False(worker.ProcessPending());
    }

    [Fact]
    public void UserStepsAfterAutoKeepTheAutoTargetPending()
    {
        var (worker, mouse, statuses) = Create();
        mouse.Current = 1;
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.Calls.Clear();
        statuses.Clear();

        worker.RequestNextProfile(new("a", true));
        worker.RequestAutoProfile(3);
        worker.RequestNextProfile(new("a", true));
        worker.ProcessPending();
        worker.ProcessPending();

        // Both steps count from the current profile (1 → 3); the automatic 3 then matches and is skipped.
        Assert.Equal(["set 3"], mouse.Calls);
        Assert.Equal("a", Assert.Single(statuses).User?.LearnFor);
    }

    [Fact]
    public void UserAbsoluteAfterAutoSupersedesIt()
    {
        var (worker, mouse, statuses) = Create();

        worker.RequestAutoProfile(4);
        worker.RequestProfile(2, new("a", true));
        worker.ProcessPending();

        Assert.Equal(["set 2"], mouse.Calls);
        Assert.Equal("a", Assert.Single(statuses).User?.LearnFor);
        Assert.False(worker.ProcessPending());
    }

    [Fact]
    public void AutoAfterCancelledUserStepsIsAutomaticWithoutUser()
    {
        var (worker, mouse, statuses) = Create();

        worker.RequestNextProfile(new("a", true));
        worker.RequestPreviousProfile(new("a", true));
        worker.RequestAutoProfile(3);
        worker.ProcessPending();

        Assert.Equal(["set 3"], mouse.Calls);
        var s = Assert.Single(statuses);
        Assert.Null(s.User);
        Assert.True(s.Automatic);
    }

    [Fact]
    public void UserAfterAutoKeepsUserSwitch()
    {
        var (worker, mouse, statuses) = Create();
        mouse.Enabled = [1, 2, 3, 4, 5];

        worker.RequestAutoProfile(4);
        worker.RequestNextProfile(new("b", true));
        worker.ProcessPending();

        Assert.Equal(["get", "set 5"], mouse.Calls);
        var s = Assert.Single(statuses);
        Assert.Equal("b", s.User?.LearnFor);
        Assert.False(s.Automatic);
    }

    [Fact]
    public void DeviceChangeHasNoUser()
    {
        var (worker, mouse, statuses) = Create();

        mouse.RaiseDeviceChange(3);
        worker.ProcessPending();

        var s = Assert.Single(statuses);
        Assert.Null(s.User);
        Assert.False(s.Automatic);
    }

    [Fact]
    public void RefreshHasNoUser()
    {
        var (worker, _, statuses) = Create();

        worker.RequestRefresh();
        worker.ProcessPending();

        Assert.Null(Assert.Single(statuses).User);
    }

    [Fact]
    public void UserSwitchThatCancelsOutIsNotLearnedByALaterBatch()
    {
        var (worker, mouse, statuses) = Create();
        mouse.Current = 2;
        worker.RequestRefresh();
        worker.ProcessPending();
        statuses.Clear();

        worker.RequestNextProfile(new("a", true));
        worker.RequestPreviousProfile(new("a", true));
        Assert.False(worker.ProcessPending());
        worker.RequestRefresh();
        worker.ProcessPending();

        Assert.Null(Assert.Single(statuses).User);
    }

    [Fact]
    public void FailedUserSwitchCarriesNoUser()
    {
        var (worker, mouse, statuses) = Create();
        mouse.FailWith = new IOException("gone");
        worker.RequestProfile(2, new("a", true));
        worker.ProcessPending();

        mouse.FailWith = null;
        mouse.FailSetWith = new Core.Hidpp.HidppProtocolException("not confirmed");
        worker.RequestProfile(2, new("a", true));
        worker.ProcessPending();

        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, s => Assert.Null(s.User));
    }

    [Fact]
    public void AutoTargetEqualToCurrentIsSentWhenDeviceChangeIsPending()
    {
        var (worker, mouse) = Known(2);

        mouse.RaiseDeviceChange(3);
        worker.RequestAutoProfile(2);
        worker.ProcessPending();

        Assert.Equal(["set 2"], mouse.Calls);
        Assert.Equal(2, worker.Status.State!.CurrentProfile);
        Assert.True(worker.Status.Automatic);
    }

    [Fact]
    public void AutoTargetEqualToCurrentIsSentWhenRefreshOrStaleIsPending()
    {
        var (worker, mouse) = Known(2);

        worker.RequestRefresh();
        worker.RequestAutoProfile(2);
        worker.ProcessPending();
        worker.NotifyDeviceChange();
        worker.RequestAutoProfile(2);
        worker.ProcessPending();

        Assert.Equal(["set 2", "stale", "set 2"], mouse.Calls);
    }

    [Fact]
    public void FailedAutoSwitchIsAutomaticWithoutUser()
    {
        var (worker, mouse, statuses) = Create();

        mouse.FailWith = new IOException("gone");
        worker.RequestAutoProfile(2);
        worker.ProcessPending();

        mouse.FailWith = null;
        worker.RequestRefresh();
        worker.ProcessPending();
        mouse.FailSetWith = new Core.Hidpp.HidppProtocolException("not confirmed");
        worker.RequestAutoProfile(3);
        worker.ProcessPending();

        mouse.FailSetWith = null;
        mouse.FailWith = new HostModeException(new ProfileState(2, 0, [1], 4));
        worker.RequestAutoProfile(2);
        worker.ProcessPending();

        Assert.Equal(WorkerStatusKind.Unreachable, statuses[0].Kind);
        Assert.NotNull(statuses[2].Notice);
        Assert.Equal(2, statuses[3].State!.OnboardMode);
        Assert.All([statuses[0], statuses[2], statuses[3]], s =>
        {
            Assert.True(s.Automatic);
            Assert.Null(s.User);
        });
        Assert.False(statuses[1].Automatic); // the refresh in between
    }

    [Fact]
    public void FailedUserSwitchIsNotAutomatic()
    {
        var (worker, mouse, statuses) = Create();
        mouse.FailWith = new IOException("gone");

        worker.RequestProfile(2, new("a", true));
        worker.ProcessPending();

        Assert.False(Assert.Single(statuses).Automatic);
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
