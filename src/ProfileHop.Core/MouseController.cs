using ProfileHop.Core.Hid;
using ProfileHop.Core.Hidpp;

namespace ProfileHop.Core;

/// <summary>One device index tried during connect.</summary>
/// <param name="Version">HID++ version if the index answered getProtocolVersion, else null.</param>
/// <param name="Error">Why the index was skipped (HID++ 1.0 error, timeout, ...), or null.</param>
public sealed record DeviceProbe(
    HidDevice Device,
    ushort ProductId,
    byte DeviceIndex,
    ProtocolVersion? Version,
    bool HasOnboardProfiles,
    byte? DeviceType,
    string? Name,
    string? Error);

/// <summary>The device the controller talks to.</summary>
/// <param name="TypeFallback">True when no candidate reported device type mouse (3) and the first device with
/// 0x8100 was taken instead.</param>
public sealed record ConnectedMouse(
    HidDevice Device,
    ushort ProductId,
    string? Product,
    byte DeviceIndex,
    ProtocolVersion Version,
    string? Name,
    byte? DeviceType,
    bool TypeFallback);

public sealed class MouseNotFoundException(IReadOnlyList<DeviceProbe> probes)
    : Exception($"No HID++ 2.0 device with OnboardProfiles (0x8100) found ({probes.Count} device indexes tried)")
{
    public IReadOnlyList<DeviceProbe> Probes { get; } = probes;
}

/// <summary>
/// Finds the mouse (all HID++ devices, indexes 0x01..0x06 then 0xFF; prefers device type mouse), keeps the
/// connection until a failure or <see cref="MarkStale"/>, and implements the tray's <see cref="IProfileSwitcher"/>.
/// Thread-safe; calls block.
/// </summary>
public sealed class MouseController : IProfileSwitcher
{
    private static readonly byte[] ProbeOrder = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0xFF];

    private readonly IHidDeviceSource _source;
    private readonly TimeSpan? _timeout;
    private readonly TimeProvider? _time;
    private readonly Lock _lock = new();
    private Connection? _connection;
    private IReadOnlyList<DeviceProbe> _lastProbe = [];
    private byte _ping = 0x5A;
    private bool _disposed;

    public MouseController(IHidDeviceSource source, TimeSpan? requestTimeout = null, TimeProvider? timeProvider = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _timeout = requestTimeout;
        _time = timeProvider;
    }

    public event Action<int>? ProfileChangedByDevice;

    /// <summary>Every device index tried by the most recent scan.</summary>
    public IReadOnlyList<DeviceProbe> LastProbe
    {
        get { lock (_lock) return _lastProbe; }
    }

    /// <summary>
    /// Connects if needed and returns the chosen device. With <paramref name="fullScan"/> the existing connection
    /// is dropped and every device index is probed (for the CLI's probe listing) instead of stopping at the first mouse.
    /// </summary>
    public ConnectedMouse Connect(bool fullScan = false)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (fullScan)
                CloseConnection();
            return EnsureConnected(fullScan).Info;
        }
    }

    public IReadOnlyList<FeatureEntry> GetFeatureTable() =>
        Execute(c => new FeatureSetFeature(c.Device).GetAll());

    public ProfilesDescription GetProfilesDescription() => Execute(c => c.Description);

    public ProfileState GetState() => Execute(ReadState);

    public ProfileState SetProfile(int profile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(profile, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(profile, 0xFFFF);
        return Execute(c =>
        {
            c.Onboard.SetCurrentProfile(profile);
            int confirmed = c.Onboard.GetCurrentProfile();
            if (confirmed != profile)
                throw new HidppProtocolException($"setCurrentProfile({profile}) not confirmed: mouse reports profile {confirmed}");
            return ReadState(c);
        });
    }

    public int NextDpiSlot() => Execute(c =>
    {
        byte current = c.Onboard.GetCurrentDpiIndex();
        bool[]? enabled = null;
        try
        {
            int profile = c.Onboard.GetCurrentProfile();
            if (profile is >= 1 and <= 0xFFFF)
                enabled = EnabledDpiSlots(c.Onboard.MemoryRead((ushort)profile, 0));
        }
        catch (HidppException)
        {
            // profile sector unreadable: fall back to all five slots
        }

        byte next = NextSlot(current, enabled);
        c.Onboard.SetCurrentDpiIndex(next);
        return (int)next;
    });

    public void MarkStale()
    {
        lock (_lock) CloseConnection();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            CloseConnection();
        }
    }

    /// <summary>Profile sector bytes 3..12: five LE16 DPI values; 0 = disabled slot (libratbag).</summary>
    internal static bool[] EnabledDpiSlots(byte[] profileHead)
    {
        var enabled = new bool[OnboardProfilesFeature.DpiSlotCount];
        for (int i = 0; i < enabled.Length; i++)
            enabled[i] = (profileHead[3 + 2 * i] | (profileHead[4 + 2 * i] << 8)) != 0;
        return enabled;
    }

    internal static byte NextSlot(byte current, bool[]? enabled)
    {
        const int n = OnboardProfilesFeature.DpiSlotCount;
        if (enabled is null || !enabled.Any(e => e))
            return (byte)((current + 1) % n);
        for (int k = 1; k <= n; k++)
        {
            int i = (current + k) % n;
            if (enabled[i])
                return (byte)i;
        }
        return current;
    }

    private static ProfileState ReadState(Connection c)
    {
        byte mode = c.Onboard.GetOnboardMode();
        int current = c.Onboard.GetCurrentProfile();

        IReadOnlyList<int> enabled;
        try
        {
            enabled = ProfileDirectory.EnabledProfiles(ProfileDirectory.Read(c.Onboard, c.Description));
        }
        catch (HidppException)
        {
            enabled = []; // directory unreadable (libratbag notes the G305 rejects reads of an unwritten sector)
        }

        int? dpi;
        try
        {
            dpi = c.Onboard.GetCurrentDpiIndex();
        }
        catch (HidppException)
        {
            dpi = null;
        }

        return new ProfileState(mode, current, enabled, dpi);
    }

    private T Execute<T>(Func<Connection, T> operation)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Connection c = EnsureConnected(fullScan: false);
            try
            {
                return operation(c);
            }
            catch
            {
                CloseConnection();
                throw;
            }
        }
    }

    private Connection EnsureConnected(bool fullScan)
    {
        if (_connection is { Client.IsClosed: false } live)
            return live;
        CloseConnection();
        _connection = Scan(fullScan);
        return _connection;
    }

    private void CloseConnection()
    {
        Connection? c = _connection;
        _connection = null;
        c?.Dispose();
    }

    private Connection Scan(bool fullScan)
    {
        var probes = new List<DeviceProbe>();
        var open = new List<Opened>();
        Candidate? mouse = null;
        Candidate? firstCandidate = null;

        try
        {
            foreach (HidDevice device in _source.Enumerate())
            {
                ushort pid = device.Collections.Count > 0 ? device.Collections[0].ProductId : (ushort)0;
                IHidTransport transport;
                try
                {
                    transport = _source.Open(device);
                }
                catch (Exception ex)
                {
                    probes.Add(new DeviceProbe(device, pid, 0, null, false, null, null, "open failed: " + ex.Message));
                    continue;
                }

                var client = new HidppClient(transport, _timeout, _time);
                var opened = new Opened(device, transport, client);
                open.Add(opened);
                var found = new List<Candidate>();
                try
                {
                    foreach (byte index in ProbeOrder)
                    {
                        (DeviceProbe probe, Candidate? candidate) = ProbeIndex(opened, pid, index);
                        probes.Add(probe);
                        if (candidate is null)
                            continue;
                        found.Add(candidate);
                        if (candidate.IsMouse && !fullScan)
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    probes.Add(new DeviceProbe(device, pid, 0, null, false, null, null, "transport failed: " + ex.Message));
                    found.Clear();
                }

                firstCandidate ??= found.FirstOrDefault();
                mouse ??= found.FirstOrDefault(c => c.IsMouse);
                if (mouse is not null && !fullScan)
                    break;
            }
        }
        finally
        {
            _lastProbe = probes;
        }

        Candidate? chosen = mouse ?? firstCandidate;
        foreach (Opened o in open)
        {
            if (chosen is null || !ReferenceEquals(o, chosen.Opened))
                o.Dispose();
        }

        if (chosen is null)
            throw new MouseNotFoundException(probes);

        var info = new ConnectedMouse(
            chosen.Opened.Device,
            chosen.Probe.ProductId,
            chosen.Opened.Device.Collections.Count > 0 ? chosen.Opened.Device.Collections[0].Product : null,
            chosen.Probe.DeviceIndex,
            chosen.Probe.Version!.Value,
            chosen.Probe.Name,
            chosen.Probe.DeviceType,
            TypeFallback: !chosen.IsMouse);
        return new Connection(this, chosen.Opened, chosen.Device, info);
    }

    private (DeviceProbe, Candidate?) ProbeIndex(Opened opened, ushort pid, byte index)
    {
        var device = new HidppDevice(opened.Client, index);
        ProtocolVersion version;
        try
        {
            version = device.Root.GetProtocolVersion(NextPing());
        }
        catch (DeviceUnreachableException ex)
        {
            return (Skipped($"HID++ 1.0 error 0x{ex.Hidpp10Error:X2}"), null);
        }
        catch (TimeoutException)
        {
            return (Skipped("no reply"), null);
        }
        catch (Exception ex) when (ex is HidppException or HidppProtocolException)
        {
            return (Skipped(ex.Message), null);
        }

        if (version.Major < 2)
            return (new DeviceProbe(opened.Device, pid, index, version, false, null, null, "HID++ < 2.0"), null);

        bool hasOnboard;
        try
        {
            hasOnboard = device.GetFeatureIndex(FeatureIds.OnboardProfiles) is not null;
        }
        catch (Exception ex) when (ex is HidppException or HidppProtocolException or TimeoutException or DeviceUnreachableException)
        {
            return (new DeviceProbe(opened.Device, pid, index, version, false, null, null, "getFeature failed: " + ex.Message), null);
        }

        byte? type = null;
        string? name = null;
        if (TryDeviceName(device, out byte t, out string n))
        {
            type = t;
            name = n;
        }

        var probe = new DeviceProbe(opened.Device, pid, index, version, hasOnboard, type, name, null);
        return (probe, hasOnboard ? new Candidate(opened, device, probe) : null);

        DeviceProbe Skipped(string why) => new(opened.Device, pid, index, null, false, null, null, why);
    }

    private static bool TryDeviceName(HidppDevice device, out byte type, out string name)
    {
        type = 0;
        name = "";
        try
        {
            if (device.GetFeatureIndex(FeatureIds.DeviceName) is null)
                return false;
            var dn = new DeviceNameFeature(device);
            type = dn.GetDeviceType();
            name = dn.GetName();
            return true;
        }
        catch (Exception ex) when (ex is HidppException or HidppProtocolException or TimeoutException or DeviceUnreachableException)
        {
            return false;
        }
    }

    private byte NextPing()
    {
        _ping = (byte)(_ping == 0xFF ? 1 : _ping + 1);
        return _ping;
    }

    private void OnNotification(Connection connection, HidppMessage msg)
    {
        // OnboardProfiles function 0 notification = active profile changed (Solaar notifications.py).
        if (msg.DeviceIndex != connection.Info.DeviceIndex || msg.FeatureIndex != connection.OnboardIndex || msg.Function != 0)
            return;
        ThreadPool.QueueUserWorkItem(_ => RequeryProfile(connection));
    }

    private void RequeryProfile(Connection connection)
    {
        int profile;
        lock (_lock)
        {
            if (_disposed || !ReferenceEquals(_connection, connection))
                return;
            try
            {
                profile = connection.Onboard.GetCurrentProfile();
            }
            catch (Exception)
            {
                CloseConnection();
                return;
            }
        }
        ProfileChangedByDevice?.Invoke(profile);
    }

    private sealed class Opened(HidDevice device, IHidTransport transport, HidppClient client) : IDisposable
    {
        public HidDevice Device { get; } = device;
        public IHidTransport Transport { get; } = transport;
        public HidppClient Client { get; } = client;

        public void Dispose()
        {
            Client.Dispose();
            Transport.Dispose();
        }
    }

    private sealed class Candidate(Opened opened, HidppDevice device, DeviceProbe probe)
    {
        public Opened Opened { get; } = opened;
        public HidppDevice Device { get; } = device;
        public DeviceProbe Probe { get; } = probe;
        public bool IsMouse => Probe.DeviceType == DeviceNameFeature.TypeMouse;
    }

    private sealed class Connection : IDisposable
    {
        private readonly Opened _opened;
        private ProfilesDescription? _description;

        public Connection(MouseController owner, Opened opened, HidppDevice device, ConnectedMouse info)
        {
            _opened = opened;
            Device = device;
            Info = info;
            Onboard = new OnboardProfilesFeature(device);
            OnboardIndex = Onboard.Index; // cached by the probe's getFeature
            opened.Client.Notification += msg => owner.OnNotification(this, msg);
        }

        public HidppClient Client => _opened.Client;
        public HidppDevice Device { get; }
        public ConnectedMouse Info { get; }
        public OnboardProfilesFeature Onboard { get; }
        public byte OnboardIndex { get; }
        public ProfilesDescription Description => _description ??= Onboard.GetProfilesDescription();

        public void Dispose() => _opened.Dispose();
    }
}
