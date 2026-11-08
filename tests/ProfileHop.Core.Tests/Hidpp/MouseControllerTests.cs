using ProfileHop.Core.Hidpp;
using ProfileHop.Core.Tests.Hidpp.Fakes;
using static ProfileHop.Core.Tests.Hidpp.Fakes.Frames;

namespace ProfileHop.Core.Tests.Hidpp;

public sealed class MouseControllerTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(40);

    private readonly FakeHidDeviceSource _source = new();
    private readonly SimulatedReceiver _receiver = new();
    private readonly SimulatedDevice _mouse = new();
    private MouseController? _controller;

    private MouseController Controller => _controller ??= new MouseController(_source, Timeout);

    public void Dispose() => _controller?.Dispose();

    private Core.Hid.HidDevice AddReceiverWithMouseAt(byte index, ushort pid = 0xC54D)
    {
        _receiver.Paired[index] = _mouse;
        return _source.Add("receiver-" + pid.ToString("X4"), pid, _receiver.Handle);
    }

    private static SimulatedDevice Keyboard() => new() { Name = "G915 KEYBOARD", DeviceType = 0 };

    // ---- connect / probe ----

    [Fact]
    public void Connect_finds_mouse_at_index_1_on_receiver()
    {
        AddReceiverWithMouseAt(0x01);

        ConnectedMouse m = Controller.Connect();

        Assert.Equal(0x01, m.DeviceIndex);
        Assert.Equal(0xC54D, m.ProductId);
        Assert.Equal(new ProtocolVersion(4, 2), m.Version);
        Assert.Equal("PRO X2 SUPERSTRIKE", m.Name);
        Assert.Equal((byte)3, m.DeviceType);
        Assert.False(m.TypeFallback);
    }

    [Fact]
    public void Connect_probes_indexes_1_to_6_in_order_with_getProtocolVersion()
    {
        AddReceiverWithMouseAt(0x03);
        Controller.Connect();

        var probes = _source.Opened.Single().Transport.Written
            .Where(w => w[2] == 0x00 && w[3] == 0x1B).Select(w => w[1]).ToList();

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, probes.Take(3));
        Assert.Equal(0x03, Controller.Connect().DeviceIndex);
    }

    [Fact]
    public void Probe_frame_is_root_getProtocolVersion_with_ping()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Connect();

        byte[] first = _source.Opened.Single().Transport.Written[0];

        Assert.Equal(new byte[] { 0x11, 0x01, 0x00, 0x1B, 0x00, 0x00 }, first[..6]);
    }

    [Fact]
    public void Connect_finds_wired_mouse_at_0xFF_after_silent_indexes()
    {
        var wired = new SimulatedDevice();
        _source.Add("wired", 0xC0A1, req => req[1] == 0xFF ? wired.Handle(req) : []);

        ConnectedMouse m = Controller.Connect();

        Assert.Equal(0xFF, m.DeviceIndex);
    }

    [Fact]
    public void Asleep_and_unknown_indexes_are_skipped()
    {
        _receiver.Asleep.Add(0x01);
        AddReceiverWithMouseAt(0x02);

        Assert.Equal(0x02, Controller.Connect().DeviceIndex);
    }

    [Fact]
    public void Hidpp1_device_is_skipped()
    {
        _receiver.Paired[0x01] = new SimulatedDevice { Major = 1, Minor = 0 };
        AddReceiverWithMouseAt(0x02);

        Assert.Equal(0x02, Controller.Connect().DeviceIndex);
    }

    [Fact]
    public void Device_without_0x8100_is_skipped()
    {
        var plain = new SimulatedDevice();
        plain.Features.Remove(0x8100);
        _receiver.Paired[0x01] = plain;
        AddReceiverWithMouseAt(0x02);

        Assert.Equal(0x02, Controller.Connect().DeviceIndex);
    }

    [Fact]
    public void Keyboard_with_0x8100_on_first_receiver_loses_to_mouse_on_second()
    {
        var kbReceiver = new SimulatedReceiver();
        kbReceiver.Paired[0x01] = Keyboard();
        var first = _source.Add("receiver-C53A", 0xC53A, kbReceiver.Handle);
        AddReceiverWithMouseAt(0x01, 0xC54D);

        ConnectedMouse m = Controller.Connect();

        Assert.Equal(0xC54D, m.ProductId);
        Assert.Equal("PRO X2 SUPERSTRIKE", m.Name);
        Assert.False(m.TypeFallback);
        Assert.True(_source.LastTransportFor(first).IsDisposed);
        Assert.False(_source.Opened.Last().Transport.IsDisposed);
    }

    [Fact]
    public void Keyboard_and_mouse_on_same_receiver_picks_mouse()
    {
        _receiver.Paired[0x01] = Keyboard();
        AddReceiverWithMouseAt(0x02);

        Assert.Equal(0x02, Controller.Connect().DeviceIndex);
    }

    [Fact]
    public void Falls_back_to_first_0x8100_device_when_none_is_a_mouse()
    {
        var kbReceiver = new SimulatedReceiver();
        kbReceiver.Paired[0x01] = Keyboard();
        _source.Add("receiver-C53A", 0xC53A, kbReceiver.Handle);
        var nameless = new SimulatedDevice();
        nameless.Features.Remove(0x0005);
        _receiver.Paired[0x01] = nameless;
        _source.Add("receiver-C54D", 0xC54D, _receiver.Handle);

        ConnectedMouse m = Controller.Connect();

        Assert.Equal(0xC53A, m.ProductId);
        Assert.True(m.TypeFallback);
        Assert.Equal((byte)0, m.DeviceType);
    }

    [Fact]
    public void Probe_results_list_every_device_and_index_tried()
    {
        var kbReceiver = new SimulatedReceiver();
        kbReceiver.Paired[0x01] = Keyboard();
        _source.Add("receiver-C53A", 0xC53A, kbReceiver.Handle);
        AddReceiverWithMouseAt(0x01);

        Controller.Connect(fullScan: true);
        IReadOnlyList<DeviceProbe> probes = Controller.LastProbe;

        Assert.Equal(14, probes.Count);
        DeviceProbe kb = probes.Single(p => p.ProductId == 0xC53A && p.DeviceIndex == 0x01);
        Assert.Equal("G915 KEYBOARD", kb.Name);
        Assert.Equal((byte)0, kb.DeviceType);
        Assert.True(kb.HasOnboardProfiles);
        DeviceProbe rx = probes.Single(p => p.ProductId == 0xC54D && p.DeviceIndex == 0xFF);
        Assert.Null(rx.Version);
        Assert.Contains("0x01", rx.Error);
        Assert.Equal(0x01, Controller.Connect().DeviceIndex);
    }

    [Fact]
    public void Nothing_found_throws_and_disposes_all_transports()
    {
        _source.Add("receiver-C53A", 0xC53A, new SimulatedReceiver().Handle);

        Assert.Throws<MouseNotFoundException>(() => Controller.Connect());
        Assert.All(_source.Opened, o => Assert.True(o.Transport.IsDisposed));
    }

    [Fact]
    public void Connection_is_cached()
    {
        AddReceiverWithMouseAt(0x01);

        Controller.GetState();
        Controller.GetState();

        Assert.Equal(1, _source.EnumerateCount);
        Assert.Single(_source.Opened);
    }

    [Fact]
    public void MarkStale_disposes_and_next_call_reconnects()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.GetState();
        FakeHidTransport first = _source.Opened.Single().Transport;

        Controller.MarkStale();

        Assert.True(first.IsDisposed);
        Controller.GetState();
        Assert.Equal(2, _source.EnumerateCount);
    }

    // ---- device preference ----

    private SimulatedDevice AddSecondReceiverMouse(string name = "G502 X PLUS", ushort pid = 0xC547)
    {
        var other = new SimulatedDevice { Name = name };
        var rx = new SimulatedReceiver();
        rx.Paired[0x01] = other;
        _source.Add("receiver-" + pid.ToString("X4"), pid, rx.Handle);
        return other;
    }

    [Fact]
    public void Device_key_is_pid_hex_slash_name()
    {
        AddReceiverWithMouseAt(0x01);

        Assert.Equal("C54D/PRO X2 SUPERSTRIKE", Controller.Connect().Key);
        Assert.Equal("C54D/PRO X2 SUPERSTRIKE", DeviceKey.Format(0xC54D, "PRO X2 SUPERSTRIKE", 0x01));
        Assert.Equal("C54D/#02", DeviceKey.Format(0xC54D, null, 0x02));
    }

    [Theory]
    [InlineData("C54D/PRO X2 SUPERSTRIKE", 0xC54D, "PRO X2 SUPERSTRIKE")]
    [InlineData(" c53a/G502 X PLUS ", 0xC53A, "G502 X PLUS")]
    [InlineData("C54D/#02", 0xC54D, "#02")]
    [InlineData("SOMETHING", null, "SOMETHING")]
    [InlineData("XYZW/NAME", null, "XYZW/NAME")]
    public void Device_key_parts(string key, int? pid, string name)
    {
        Assert.Equal(pid is null ? null : (ushort?)pid, DeviceKey.ProductIdOf(key.Trim()));
        Assert.Equal(name, DeviceKey.NameOf(key));
    }

    [Fact]
    public void Preference_picks_matching_device_over_first_mouse()
    {
        AddReceiverWithMouseAt(0x01);
        AddSecondReceiverMouse();
        Controller.Configure("C547/G502 X PLUS", forceOnboardMode: false);

        ConnectedMouse m = Controller.Connect();

        Assert.Equal(0xC547, m.ProductId);
        Assert.Equal("G502 X PLUS", m.Name);
    }

    [Fact]
    public void Preference_matches_case_insensitively()
    {
        AddReceiverWithMouseAt(0x01);
        AddSecondReceiverMouse();
        Controller.Configure("c547/g502 x plus", forceOnboardMode: false);

        Assert.Equal(0xC547, Controller.Connect().ProductId);
    }

    [Fact]
    public void Preference_can_pick_a_non_mouse_device_with_0x8100()
    {
        _receiver.Paired[0x01] = Keyboard();
        AddReceiverWithMouseAt(0x02);
        Controller.Configure("C54D/G915 KEYBOARD", forceOnboardMode: false);

        ConnectedMouse m = Controller.Connect();

        Assert.Equal(0x01, m.DeviceIndex);
        Assert.False(m.TypeFallback);
    }

    [Fact]
    public void Preference_stops_scan_once_found()
    {
        AddReceiverWithMouseAt(0x01);
        AddSecondReceiverMouse();
        Controller.Configure("C54D/PRO X2 SUPERSTRIKE", forceOnboardMode: false);

        Controller.Connect();

        Assert.Single(_source.Opened);
    }

    [Fact]
    public void Missing_preference_throws_instead_of_falling_back_to_another_device()
    {
        AddReceiverWithMouseAt(0x01);
        var log = new Tray.ProfileWorkerTests.ListLog();
        _controller = new MouseController(_source, Timeout, log: log);
        Controller.Configure("C54D/SOMETHING ELSE", forceOnboardMode: false);

        var ex = Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.Connect());

        Assert.Equal("C54D/SOMETHING ELSE", ex.DeviceKey);
        Assert.Equal("SOMETHING ELSE", ex.DeviceName);
        Assert.NotEmpty(ex.Probes);
        Assert.IsAssignableFrom<MouseNotFoundException>(ex); // the CLI's probe listing still applies
        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        Assert.Empty(_mouse.Calls); // only the probe's reads, never an OnboardProfiles call
        Assert.Contains(log.Lines, l => l.Level == Core.Tray.LogLevel.Warning && l.Message.Contains("C54D/SOMETHING ELSE"));
    }

    // ---- chosen device asleep / missing (never fall back to another device) ----

    private const string Superstrike = "C54D/PRO X2 SUPERSTRIKE";

    /// <summary>The user's PC: a G502 X PLUS with 0x8100 on receiver C53A (enumerated first), the SUPERSTRIKE on C54D.</summary>
    private Core.Hid.HidDevice AddOtherMouseReceiverFirst()
    {
        var rx = new SimulatedReceiver();
        rx.Paired[0x01] = new SimulatedDevice { Name = "G502 X PLUS" };
        return _source.Add("receiver-C53A", 0xC53A, rx.Handle);
    }

    private void AssertNeverOpened(Core.Hid.HidDevice device) =>
        Assert.DoesNotContain(_source.Opened, o => o.Device == device);

    [Fact]
    public void Chosen_device_asleep_throws_and_never_touches_the_other_mouse()
    {
        var g502 = AddOtherMouseReceiverFirst();
        _receiver.Asleep.Add(0x01);
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: true);

        var ex = Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.SetProfile(2));
        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.NextDpiSlot());

        Assert.Equal("PRO X2 SUPERSTRIKE", ex.DeviceName);
        Assert.Contains(ex.Probes, p => p.ProductId == 0xC54D && p.DeviceIndex == 0x01 && p.Error!.Contains("0x09"));
        AssertNeverOpened(g502);
    }

    [Fact]
    public void Chosen_receiver_unplugged_throws_and_never_touches_the_other_mouse()
    {
        var g502 = AddOtherMouseReceiverFirst();
        Controller.Configure(Superstrike, forceOnboardMode: true);

        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        AssertNeverOpened(g502);
    }

    [Fact]
    public void Chosen_device_is_connected_by_the_next_call_once_it_wakes()
    {
        AddOtherMouseReceiverFirst();
        _receiver.Asleep.Add(0x01);
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: true);
        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());

        _receiver.Asleep.Remove(0x01);

        Assert.Equal(1, Controller.GetState().CurrentProfile);
        Assert.Equal(Superstrike, Controller.Connect().Key);
    }

    [Fact]
    public void Device_change_reconnect_obeys_the_chosen_device()
    {
        var g502 = AddOtherMouseReceiverFirst();
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: true);
        Controller.GetState();

        Controller.MarkStale(); // WM_DEVICECHANGE / resume
        _receiver.Asleep.Add(0x01);

        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        AssertNeverOpened(g502);
    }

    [Fact]
    public void Chosen_device_asleep_at_its_last_index_probes_only_that_index()
    {
        var rx = AddReceiverWithMouseAt(0x02);
        Controller.Configure(Superstrike, forceOnboardMode: false);
        Controller.GetState();
        Controller.MarkStale();
        _receiver.Asleep.Add(0x02);

        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());

        var written = _source.LastTransportFor(rx).Written;
        Assert.Single(written);
        Assert.Equal(0x02, written[0][1]);
    }

    [Fact]
    public void Chosen_device_moved_to_another_index_is_still_found()
    {
        AddReceiverWithMouseAt(0x02);
        Controller.Configure(Superstrike, forceOnboardMode: false);
        Controller.GetState();
        Controller.MarkStale();
        _receiver.Paired.Remove(0x02); // empty slot now: HID++ 1.0 error 0x08 (unknown device)
        _receiver.Paired[0x04] = _mouse;

        Assert.Equal(0x04, Controller.Connect().DeviceIndex);
    }

    [Fact]
    public void Chosen_device_going_to_sleep_while_connected_throws_chosen_device_not_found()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: false);
        Controller.GetState();
        _receiver.Asleep.Add(0x01);

        var ex = Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());

        Assert.IsType<DeviceUnreachableException>(ex.InnerException);
    }

    private static byte[] Connected(byte index, bool link) => Short(index, 0x41, 0x04, link ? (byte)0x00 : (byte)0x40, 0x8A, 0x40);

    [Fact]
    public async Task Connection_notification_while_waiting_for_the_chosen_device_raises_DeviceArrived()
    {
        AddOtherMouseReceiverFirst();
        _receiver.Asleep.Add(0x01);
        var rx = AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: true);
        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        var arrived = new TaskCompletionSource();
        Controller.DeviceArrived += () => arrived.TrySetResult();
        FakeHidTransport waiting = _source.LastTransportFor(rx);
        Assert.False(waiting.IsDisposed); // kept open for the receiver's notifications (read thread only)

        waiting.Push(Connected(0x01, link: false)); // still asleep: nothing
        await Task.Delay(100);
        Assert.False(arrived.Task.IsCompleted);

        waiting.Push(Connected(0x01, link: true));
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Chosen_device_that_fell_asleep_while_connected_is_awaited_at_its_index()
    {
        var rx = AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: false);
        Controller.GetState();
        _receiver.Asleep.Add(0x01);
        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        int count = 0;
        Controller.DeviceArrived += () => Interlocked.Increment(ref count);
        FakeHidTransport waiting = _source.LastTransportFor(rx);
        Assert.False(waiting.IsDisposed);

        waiting.Push(Connected(0x03, link: true)); // another device on the same receiver
        await Task.Delay(100);
        Assert.Equal(0, count);

        waiting.Push(Connected(0x01, link: true));
        SpinWait.SpinUntil(() => Volatile.Read(ref count) > 0, 2000);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Connection_notifications_on_a_live_connection_add_no_work()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: false);
        Controller.GetState();
        int count = 0;
        Controller.DeviceArrived += () => Interlocked.Increment(ref count);
        FakeHidTransport t = _source.Opened.Single().Transport;
        int written = t.Written.Count;

        t.Push(Connected(0x01, link: false));
        t.Push(Connected(0x01, link: true));
        await Task.Delay(100);

        Assert.Equal(0, count);
        Assert.Equal(written, t.Written.Count);
    }

    [Fact]
    public void Waiting_receiver_is_closed_by_MarkStale_Configure_and_Dispose()
    {
        _receiver.Asleep.Add(0x01);
        var rx = AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: false);

        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        Controller.MarkStale();
        Assert.True(_source.LastTransportFor(rx).IsDisposed);

        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        Controller.Configure(null, forceOnboardMode: false);
        Assert.True(_source.LastTransportFor(rx).IsDisposed);

        Controller.Configure(Superstrike, forceOnboardMode: false);
        Assert.Throws<ChosenDeviceNotFoundException>(() => Controller.GetState());
        Controller.Dispose();
        Assert.True(_source.LastTransportFor(rx).IsDisposed);
    }

    [Fact]
    public void Automatic_mode_still_takes_the_other_mouse_when_the_superstrike_sleeps()
    {
        AddOtherMouseReceiverFirst();
        _receiver.Asleep.Add(0x01);
        AddReceiverWithMouseAt(0x01);

        Assert.Equal("C53A/G502 X PLUS", Controller.Connect().Key);
    }

    [Fact]
    public void Configure_with_the_same_preference_still_reconnects()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: true);
        Controller.GetState();
        FakeHidTransport first = _source.Opened.Single().Transport;

        Controller.Configure(Superstrike, forceOnboardMode: true);

        Assert.True(first.IsDisposed);
        Controller.GetState();
        Assert.Equal(2, _source.Opened.Count);
    }

    [Fact]
    public void Rescan_connects_the_chosen_device_once_it_is_present()
    {
        AddOtherMouseReceiverFirst();
        _receiver.Asleep.Add(0x01);
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(Superstrike, forceOnboardMode: true);

        IReadOnlyList<KnownDevice> asleep = Controller.ListDevices(rescan: true);
        Assert.DoesNotContain(asleep, d => d.IsActive); // the G502 is listed but not used

        _receiver.Asleep.Remove(0x01);
        IReadOnlyList<KnownDevice> awake = Controller.ListDevices(rescan: true);

        Assert.Equal(Superstrike, Assert.Single(awake, d => d.IsActive).Key);
    }

    [Fact]
    public void Configure_drops_connection_and_next_call_uses_new_preference()
    {
        AddReceiverWithMouseAt(0x01);
        AddSecondReceiverMouse();
        Assert.Equal(0xC54D, Controller.Connect().ProductId);
        FakeHidTransport first = _source.Opened.Single().Transport;

        Controller.Configure("C547/G502 X PLUS", forceOnboardMode: false);

        Assert.True(first.IsDisposed);
        Assert.Equal(0xC547, Controller.Connect().ProductId);
        Assert.Equal("C547/G502 X PLUS", Controller.PreferredDevice);
    }

    [Fact]
    public void Connect_logs_the_chosen_device()
    {
        AddReceiverWithMouseAt(0x01);
        var log = new Tray.ProfileWorkerTests.ListLog();
        _controller = new MouseController(_source, Timeout, log: log);

        Controller.Connect();

        Assert.Contains(log.Lines, l => l.Level == Core.Tray.LogLevel.Info && l.Message.Contains("C54D/PRO X2 SUPERSTRIKE"));
    }

    // ---- device list ----

    [Fact]
    public void ListDevices_without_rescan_does_not_probe()
    {
        AddReceiverWithMouseAt(0x01);

        Assert.Empty(Controller.ListDevices(rescan: false));
        Assert.Equal(0, _source.EnumerateCount);
    }

    [Fact]
    public void ListDevices_returns_qualifying_devices_from_last_scan()
    {
        _receiver.Paired[0x01] = Keyboard();
        var hidpp1 = new SimulatedDevice { Major = 1, Minor = 0, Name = "OLD" };
        _receiver.Paired[0x02] = hidpp1;
        AddReceiverWithMouseAt(0x03);
        Controller.Connect();

        IReadOnlyList<KnownDevice> devices = Controller.ListDevices(rescan: false);

        Assert.Equal(2, devices.Count);
        Assert.Equal(new KnownDevice("C54D/G915 KEYBOARD", "G915 KEYBOARD (receiver C54D)", 0, false), devices[0]);
        Assert.Equal(new KnownDevice("C54D/PRO X2 SUPERSTRIKE", "PRO X2 SUPERSTRIKE (receiver C54D)", 3, true), devices[1]);
    }

    [Fact]
    public void ListDevices_rescan_probes_every_receiver_and_keeps_preference()
    {
        AddReceiverWithMouseAt(0x01);
        AddSecondReceiverMouse();
        Controller.Connect(); // stops at the first mouse
        Assert.Single(Controller.ListDevices(rescan: false));

        IReadOnlyList<KnownDevice> devices = Controller.ListDevices(rescan: true);

        Assert.Equal(["C54D/PRO X2 SUPERSTRIKE", "C547/G502 X PLUS"], devices.Select(d => d.Key));
        Assert.True(devices[0].IsActive);
        Assert.Equal(0xC54D, Controller.Connect().ProductId);
    }

    [Fact]
    public void ListDevices_rescan_with_nothing_found_returns_empty_list()
    {
        _source.Add("receiver-C53A", 0xC53A, new SimulatedReceiver().Handle);

        Assert.Empty(Controller.ListDevices(rescan: true));
    }

    [Fact]
    public void Wired_device_display_name_says_usb()
    {
        var wired = new SimulatedDevice();
        _source.Add("wired", 0xC0A1, req => req[1] == 0xFF ? wired.Handle(req) : []);
        Controller.Connect();

        Assert.Equal("PRO X2 SUPERSTRIKE (USB C0A1)", Controller.ListDevices(rescan: false).Single().DisplayName);
    }

    // ---- onboard mode ----

    [Fact]
    public void Force_onboard_mode_switches_host_mode_mouse_to_onboard_on_connect()
    {
        _mouse.OnboardMode = 2;
        AddReceiverWithMouseAt(0x01);
        var log = new Tray.ProfileWorkerTests.ListLog();
        _controller = new MouseController(_source, Timeout, log: log);
        Controller.Configure(null, forceOnboardMode: true);

        Controller.Connect();

        Assert.Equal(1, _mouse.OnboardMode);
        var set = Assert.Single(_mouse.Calls, c => c.Function == 1);
        Assert.Equal(1, set.Params[0]);
        Assert.Equal(1, Controller.GetState().OnboardMode);
        Assert.Contains(log.Lines, l => l.Level == Core.Tray.LogLevel.Info && l.Message.Contains("onboard mode"));
    }

    [Fact]
    public void Force_onboard_mode_leaves_onboard_mouse_alone()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(null, forceOnboardMode: true);

        Controller.Connect();

        Assert.DoesNotContain(_mouse.Calls, c => c.Function == 1);
    }

    [Fact]
    public void Without_force_host_mode_is_left_alone()
    {
        _mouse.OnboardMode = 2;
        AddReceiverWithMouseAt(0x01);

        Controller.Connect();

        Assert.DoesNotContain(_mouse.Calls, c => c.Function == 1);
        Assert.Equal(2, Controller.GetState().OnboardMode);
    }

    // ---- state / switching ----

    [Fact]
    public void GetState_reads_mode_current_enabled_and_dpi()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.CurrentProfile = 2;
        _mouse.DpiIndex = 3;

        ProfileState s = Controller.GetState();

        Assert.Equal(1, s.OnboardMode);
        Assert.Equal(2, s.CurrentProfile);
        Assert.Equal([1, 2], s.EnabledProfiles);
        Assert.Equal(3, s.DpiIndex);
    }

    [Fact]
    public void GetState_with_unreadable_directory_returns_empty_enabled_list()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.Sectors.Remove(0x0000);

        ProfileState s = Controller.GetState();

        Assert.Empty(s.EnabledProfiles);
        Assert.Equal(1, s.CurrentProfile);
    }

    [Fact]
    public void SetProfile_sends_setCurrentProfile_and_confirms()
    {
        AddReceiverWithMouseAt(0x01);

        ProfileState s = Controller.SetProfile(2);

        Assert.Equal(2, s.CurrentProfile);
        byte ob = _mouse.IndexOf(0x8100);
        var written = _source.Opened.Single().Transport.Written;
        int set = written.ToList().FindIndex(w => w.SequenceEqual(Long(0x01, ob, Fn(3), 0x00, 0x02)));
        int get = written.ToList().FindIndex(w => w.SequenceEqual(Long(0x01, ob, Fn(4))));
        Assert.True(set >= 0 && get > set);
    }

    [Fact]
    public void SetProfile_mismatch_throws_and_keeps_connection()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.IgnoreSetProfile = true;

        Assert.Throws<HidppProtocolException>(() => Controller.SetProfile(2));
        Assert.False(_source.Opened.Single().Transport.IsDisposed);
        Controller.GetState();
        Assert.Equal(1, _source.EnumerateCount);
    }

    [Fact]
    public void SetProfile_device_error_keeps_connection_and_rethrows()
    {
        AddReceiverWithMouseAt(0x01);

        var ex = Assert.Throws<HidppException>(() => Controller.SetProfile(9));
        Assert.Equal(HidppError.InvalidArgument, ex.Error);
        Assert.False(_source.Opened.Single().Transport.IsDisposed);
    }

    [Fact]
    public void Device_unreachable_error_closes_connection()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.GetState();
        _receiver.Asleep.Add(0x01);

        Assert.Throws<DeviceUnreachableException>(() => Controller.GetState());
        Assert.True(_source.Opened.Single().Transport.IsDisposed);
    }

    [Fact]
    public void Simulated_host_mode_reports_profile_0_like_the_real_mouse()
    {
        _mouse.OnboardMode = 2;
        AddReceiverWithMouseAt(0x01);

        ProfileState s = Controller.GetState();

        Assert.Equal(2, s.OnboardMode);
        Assert.Equal(0, s.CurrentProfile);
    }

    [Fact]
    public void SetProfile_in_host_mode_throws_HostModeException_without_sending_set()
    {
        _mouse.OnboardMode = 2;
        AddReceiverWithMouseAt(0x01);

        var ex = Assert.Throws<HostModeException>(() => Controller.SetProfile(2));

        Assert.DoesNotContain(_mouse.Calls, c => c.Function == 3);
        Assert.Equal(2, ex.State.OnboardMode);
        Assert.Equal(0, ex.State.CurrentProfile);
        Assert.False(_source.Opened.Single().Transport.IsDisposed);
    }

    [Fact]
    public void SetProfile_with_force_switches_back_to_onboard_when_G_HUB_took_over_after_connect()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Configure(null, forceOnboardMode: true);
        Controller.Connect();
        _mouse.OnboardMode = 2; // G HUB started later

        ProfileState s = Controller.SetProfile(2);

        Assert.Equal(1, _mouse.OnboardMode);
        Assert.Equal(2, s.CurrentProfile);
    }

    [Fact]
    public void SetProfile_rejects_invalid_number_without_touching_device()
    {
        AddReceiverWithMouseAt(0x01);

        Assert.Throws<ArgumentOutOfRangeException>(() => Controller.SetProfile(0));
        Assert.Empty(_source.Opened);
    }

    [Fact]
    public void Transport_failure_marks_stale_and_rethrows()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.GetState();
        FakeHidTransport t = _source.Opened.Single().Transport;
        t.WriteException = new IOException("unplugged");

        Assert.Throws<IOException>(() => Controller.GetState());
        Assert.True(t.IsDisposed);
    }

    [Fact]
    public void NextDpiSlot_moves_to_next_enabled_slot()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.DpiIndex = 1;

        int next = Controller.NextDpiSlot();

        Assert.Equal(2, next);
        Assert.Equal(2, _mouse.DpiIndex);
        byte ob = _mouse.IndexOf(0x8100);
        Assert.Contains(_source.Opened.Single().Transport.Written, w => w.SequenceEqual(Long(0x01, ob, 0xCB, 2)));
        Assert.Contains(_source.Opened.Single().Transport.Written, w => w.SequenceEqual(Long(0x01, ob, Fn(5), 0x00, 0x01, 0x00, 0x00)));
    }

    [Fact]
    public void NextDpiSlot_wraps_past_disabled_slots()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.DpiIndex = 3; // profile 1 slots: 400, 800, 1600, 3200, 0

        Assert.Equal(0, Controller.NextDpiSlot());
    }

    [Fact]
    public void NextDpiSlot_with_single_slot_stays()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.CurrentProfile = 2; // profile 2: only slot 0
        _mouse.DpiIndex = 0;

        Assert.Equal(0, Controller.NextDpiSlot());
    }

    [Fact]
    public void NextDpiSlot_wraps_at_5_when_profile_unreadable()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.Sectors.Remove(0x0001);
        _mouse.DpiIndex = 4;

        Assert.Equal(0, Controller.NextDpiSlot());
    }

    // Real PRO X2 SUPERSTRIKE sectors (profile format 0x08, layout inferred from device dumps).
    private void UseRealFormat08Profiles(ushort profile, byte dpiIndex)
    {
        _mouse.ProfileFormat = 0x08;
        _mouse.ProfileCount = 5;
        foreach (ushort s in new ushort[] { 1, 2, 3, 4, 5 })
            _mouse.Sectors[s] = Format08Fixture.Sector(s);
        _mouse.CurrentProfile = profile;
        _mouse.DpiIndex = dpiIndex;
    }

    private bool SentSetDpiIndex() => _mouse.Calls.Any(c => c.Function == 12);

    [Fact]
    public void NextDpiSlot_format08_single_enabled_entry_sends_no_set()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(1, 0); // P1: only entry 0 (400) enabled

        Assert.Equal(0, Controller.NextDpiSlot());
        Assert.False(SentSetDpiIndex());
        Assert.Equal(0, _mouse.DpiIndex);
    }

    [Fact]
    public void NextDpiSlot_format08_profile4_wraps_from_3_to_0_past_disabled_entry4()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(4, 3); // P4: entries 0-3 = 1600, entry 4 X = 0

        Assert.Equal(0, Controller.NextDpiSlot());
        Assert.Equal(0, _mouse.DpiIndex);
    }

    [Fact]
    public void NextDpiSlot_format08_profile5_wraps_from_4_to_0()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(5, 4);

        Assert.Equal(0, Controller.NextDpiSlot());
        Assert.Equal(0, _mouse.DpiIndex);
    }

    [Fact]
    public void NextDpiSlot_format08_profile4_steps_from_1_to_2()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(4, 1);

        Assert.Equal(2, Controller.NextDpiSlot());
        Assert.Equal(2, _mouse.DpiIndex);
    }

    [Fact]
    public void NextDpiSlot_format08_reads_the_first_two_chunks_of_the_profile_sector_only()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(4, 3);

        Controller.NextDpiSlot();

        var reads = _mouse.Calls.Where(c => c.Function == 5).Select(c => (c.Params[0] << 8 | c.Params[1], c.Params[2] << 8 | c.Params[3])).ToList();
        Assert.Equal([(4, 0), (4, 16)], reads);
        Assert.DoesNotContain(_mouse.Calls, c => c.Function is 6 or 7 or 8);
    }

    // ---- profile names ----

    private int ProfileSectorReads() =>
        _mouse.Calls.Count(c => c.Function == 5 && (c.Params[0] << 8 | c.Params[1]) is >= 1 and <= 0xFF);

    [Fact]
    public void GetState_reads_format08_names_of_enabled_profiles()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(1, 0);
        _mouse.Sectors[0x0000] = SimulatedDevice.Directory((1, true), (2, true), (3, true), (4, true), (5, true));

        ProfileState s = Controller.GetState();

        Assert.Equal(new Dictionary<int, string> { [1] = "400", [2] = "600", [3] = "800", [4] = "1600", [5] = "3200" }, s.ProfileNames);
        Assert.DoesNotContain(_mouse.Calls, c => c.Function is 6 or 7 or 8);
    }

    [Fact]
    public void Profile_names_are_cached_per_connection()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(1, 0);
        _mouse.Sectors[0x0000] = SimulatedDevice.Directory((1, true), (5, true));

        Controller.GetState();
        int reads = ProfileSectorReads();
        ProfileState switched = Controller.SetProfile(5);
        Controller.NextDpiSlot(); // reads the DPI chunks of sector 5 only
        Controller.GetState();

        Assert.Equal("3200", switched.ProfileNames[5]);
        Assert.Equal(reads + 2, ProfileSectorReads());

        Controller.MarkStale();
        Controller.GetState();
        Assert.True(ProfileSectorReads() > reads + 2); // new connection reads them again
    }

    [Fact]
    public void Unreadable_profile_sector_just_has_no_name()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(1, 0);
        _mouse.Sectors[0x0000] = SimulatedDevice.Directory((1, true), (4, true));
        _mouse.Sectors.Remove(0x0004);

        ProfileState s = Controller.GetState();

        Assert.Equal(new Dictionary<int, string> { [1] = "400" }, s.ProfileNames);
        Assert.Equal([1, 4], s.EnabledProfiles);
    }

    [Fact]
    public void With_fewer_than_two_enabled_profiles_names_of_all_described_profiles_are_read()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(1, 0); // ProfileCount 5
        _mouse.Sectors[0x0000] = SimulatedDevice.Directory((1, true));

        ProfileState s = Controller.GetState();

        Assert.Equal(["400", "600", "800", "1600", "3200"], s.ProfileNames.OrderBy(kv => kv.Key).Select(kv => kv.Value));
    }

    // ---- per-profile DPI and device name ----

    [Fact]
    public void GetState_fills_profile_dpis_and_device_name()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(2, 1);
        _mouse.Sectors[0x0000] = SimulatedDevice.Directory((1, true), (2, true), (3, true), (4, true), (5, true));

        ProfileState s = Controller.GetState();

        Assert.Equal("PRO X2 SUPERSTRIKE", s.DeviceName);
        Assert.Equal([1, 2, 3, 4, 5], s.ProfileDpis.Keys.OrderBy(k => k));
        foreach (ushort p in new ushort[] { 1, 2, 3, 4, 5 })
            Assert.Equal(ProfileSector.DpiSlots(0x08, Format08Fixture.Sector(p)), s.ProfileDpis[p]);
        ushort slot = s.ProfileDpis[2][1];
        Assert.Equal(slot == 0 ? null : (int?)slot, s.CurrentDpi);
        Assert.DoesNotContain(_mouse.Calls, c => c.Function is 6 or 7 or 8);
    }

    [Fact]
    public void Profile_dpis_are_read_once_per_connection()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(1, 0);
        _mouse.Sectors[0x0000] = SimulatedDevice.Directory((1, true), (2, true));

        Controller.GetState();
        int reads = ProfileSectorReads();
        ProfileState second = Controller.GetState();

        Assert.Equal(reads, ProfileSectorReads());
        Assert.NotEmpty(second.ProfileDpis);
    }

    [Fact]
    public void Unreadable_dpi_sector_gives_no_profile_dpis_instead_of_throwing()
    {
        AddReceiverWithMouseAt(0x01);
        UseRealFormat08Profiles(1, 0);
        _mouse.Sectors[0x0000] = SimulatedDevice.Directory((1, true), (4, true));
        _mouse.Sectors.Remove(0x0004);

        ProfileState s = Controller.GetState();

        Assert.Equal([1], s.ProfileDpis.Keys);
        Assert.Null(new ProfileState(1, 4, [1, 4], 0) { ProfileDpis = s.ProfileDpis }.CurrentDpi);
    }

    [Fact]
    public void Unknown_profile_format_reads_no_profile_dpis()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.ProfileFormat = 0x09;

        Assert.Empty(Controller.GetState().ProfileDpis);
    }

    [Fact]
    public void Unknown_profile_format_reads_no_names()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.ProfileFormat = 0x09;

        ProfileState s = Controller.GetState();

        Assert.Empty(s.ProfileNames);
        Assert.Equal(0, ProfileSectorReads());
    }

    [Fact]
    public void NextDpiSlot_unknown_format_cycles_all_five_slots()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.ProfileFormat = 0x09;
        _mouse.DpiIndex = 4; // profile 1 would have slot 4 disabled under the libratbag layout

        Assert.Equal(0, Controller.NextDpiSlot());
        _mouse.DpiIndex = 3;
        Assert.Equal(4, Controller.NextDpiSlot());
    }

    // ---- notifications ----

    [Fact]
    public async Task Profile_notification_requeries_and_raises_event()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Connect();
        var raised = new TaskCompletionSource<int>();
        Controller.ProfileChangedByDevice += p => raised.TrySetResult(p);

        _mouse.CurrentProfile = 2;
        _source.Opened.Single().Transport.Push(Long(0x01, _mouse.IndexOf(0x8100), 0x00, 0x00, 0x02));

        Assert.Equal(2, await raised.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Other_notifications_are_ignored()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Connect();
        int count = 0;
        Controller.ProfileChangedByDevice += _ => Interlocked.Increment(ref count);
        var t = _source.Opened.Single().Transport;
        byte ob = _mouse.IndexOf(0x8100);

        t.Push(Long(0x01, ob, 0x10, 0x02)); // DPI change (function 1)
        t.Push(Long(0x01, 0x04, 0x00, 0x00, 0x02)); // other feature
        t.Push(Long(0x02, ob, 0x00, 0x00, 0x02)); // other device index
        await Task.Delay(150);

        Assert.Equal(0, count);
    }

    [Fact]
    public void Dispose_closes_transport_and_blocks_further_use()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Connect();

        Controller.Dispose();

        Assert.True(_source.Opened.Single().Transport.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => Controller.GetState());
    }

    [Fact]
    public void Cancel_during_a_scan_stops_before_the_next_probe_and_closes_what_was_opened()
    {
        // Exit during a long rescan: Cancel (from the exiting thread) must end the scan at the next probe instead of
        // walking every index of every receiver while Dispose waits for the lock.
        _receiver.Paired[0x06] = _mouse; // the scan would try indexes 1..5 first
        _source.Add("receiver-C54D", 0xC54D, req =>
        {
            Controller.Cancel();
            return _receiver.Handle(req);
        });
        AddSecondReceiverMouse();

        Assert.Throws<OperationCanceledException>(() => Controller.ListDevices(rescan: true));

        var (_, transport) = Assert.Single(_source.Opened); // the second receiver was never opened
        Assert.Single(transport.Written);                   // index 1 only
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public void Device_without_a_long_collection_is_not_opened_and_the_probe_says_why()
    {
        var shortOnly = new Core.Hid.HidDevice("short-only",
            [new Core.Hid.HidCollection("short-only&col01", 0x046D, 0xC53F, "USB Receiver", 0xFF00, 0x0001, 7, 7)]);
        var source = new ShortOnlyFirstSource(shortOnly, _source);
        AddReceiverWithMouseAt(0x01);
        _controller = new MouseController(source, Timeout);

        ConnectedMouse m = Controller.Connect(fullScan: true);

        Assert.Equal(0xC54D, m.ProductId);
        Assert.False(source.OpenedShortOnly);
        DeviceProbe skipped = Controller.LastProbe[0];
        Assert.Same(shortOnly, skipped.Device);
        Assert.Equal(0xC53F, skipped.ProductId);
        Assert.Equal("no long HID++ collection", skipped.Error);
        Assert.DoesNotContain(Controller.ListDevices(rescan: false), d => d.Key.StartsWith("C53F", StringComparison.Ordinal));
    }

    /// <summary>Enumerates a short-only device first, then the fake source's devices.</summary>
    private sealed class ShortOnlyFirstSource(Core.Hid.HidDevice shortOnly, FakeHidDeviceSource rest) : Core.Hid.IHidDeviceSource
    {
        public bool OpenedShortOnly { get; private set; }

        public IReadOnlyList<Core.Hid.HidDevice> Enumerate() => [shortOnly, .. rest.Enumerate()];

        public Core.Hid.IHidTransport Open(Core.Hid.HidDevice device)
        {
            if (device == shortOnly)
            {
                OpenedShortOnly = true;
                return new FakeHidTransport(); // answers nothing: every probe would time out
            }
            return rest.Open(device);
        }
    }

    [Fact]
    public void Calls_after_cancel_throw_without_device_io()
    {
        AddReceiverWithMouseAt(0x01);
        Controller.Cancel();

        Assert.Throws<OperationCanceledException>(() => Controller.GetState());
        Assert.Empty(_source.Opened);
        Controller.Dispose(); // still fine
    }

    [Fact]
    public void Controller_passes_its_log_to_the_HID_client()
    {
        AddReceiverWithMouseAt(0x01);
        var log = new Tray.ProfileWorkerTests.ListLog();
        _controller = new MouseController(_source, Timeout, log: log);

        Controller.Connect();

        Assert.Contains(log.Lines, l => l.Level == Core.Tray.LogLevel.Debug && l.Message.StartsWith("TX 11 01 00 1", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSector_reads_the_whole_directory_sector_with_memoryRead_only()
    {
        AddReceiverWithMouseAt(0x01); // sector size 255, like the real mouse

        byte[] raw = Controller.ReadSector(0x0000);

        Assert.Equal(255, raw.Length);
        Assert.Equal(_mouse.Sectors[0x0000][..255], raw);
        Assert.All(_mouse.Calls, c => Assert.True(c.Function is 0 or 2 or 5, $"function {c.Function}"));
        Assert.Contains(_mouse.Calls, c => c.Function == 5 && c.Params[2] == 0 && c.Params[3] == 0xEF); // last chunk: 255 - 16
    }

    [Fact]
    public void GetProfileDirectory_returns_every_entry_including_disabled()
    {
        AddReceiverWithMouseAt(0x01);

        IReadOnlyList<ProfileDirectoryEntry> dir = Controller.GetProfileDirectory();

        Assert.Equal([new(1, 1, true), new(2, 2, true), new(3, 3, false)], dir);
        Assert.All(_mouse.Calls, c => Assert.True(c.Function is 0 or 2 or 5, $"function {c.Function}"));
    }

    [Fact]
    public void GetCurrentDpiIndexReply_returns_all_reply_parameter_bytes()
    {
        AddReceiverWithMouseAt(0x01);
        _mouse.DpiIndex = 4;

        byte[] reply = Controller.GetCurrentDpiIndexReply();

        Assert.Equal(16, reply.Length);
        Assert.Equal(4, reply[0]);
        Assert.All(_mouse.Calls, c => Assert.True(c.Function is 0 or 2 or 11, $"function {c.Function}"));
    }

    [Fact]
    public void Real_feature_table_from_probe_fixture_finds_onboard_profiles_at_0x0E()
    {
        _mouse.Features.Clear();
        _mouse.Features.AddRange(ProbeFixture.FeatureIds());
        AddReceiverWithMouseAt(0x01);

        Assert.Equal(36, Controller.GetFeatureTable().Count);
        Assert.Equal(0x0E, _mouse.IndexOf(0x8100));
        Assert.Equal(1, Controller.GetState().CurrentProfile);
    }

    [Fact]
    public void Feature_table_and_description_are_available_for_the_cli()
    {
        AddReceiverWithMouseAt(0x01);

        Assert.Equal(_mouse.Features, Controller.GetFeatureTable().Select(f => f.Id));
        Assert.Equal(3, Controller.GetProfilesDescription().ProfileCount);
    }
}
