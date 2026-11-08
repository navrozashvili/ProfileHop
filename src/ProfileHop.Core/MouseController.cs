using ProfileHop.Core.Hid;
using ProfileHop.Core.Hidpp;
using ProfileHop.Core.Tray;

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
/// 0x8100 was taken instead (automatic choice only).</param>
/// <param name="Key">Stable device key (<see cref="DeviceKey"/>).</param>
public sealed record ConnectedMouse(
    HidDevice Device,
    ushort ProductId,
    string? Product,
    byte DeviceIndex,
    ProtocolVersion Version,
    string? Name,
    byte? DeviceType,
    bool TypeFallback,
    string Key);

/// <summary>
/// The mouse is in host mode (G HUB took over) and forceOnboardMode is off: setCurrentProfile would be accepted but
/// ignored, so nothing was sent. <see cref="State"/> is the state read just now (current profile 0 in host mode).
/// </summary>
public sealed class HostModeException(ProfileState state)
    : Exception("Mouse is in host mode (G HUB); close G HUB or enable forceOnboardMode")
{
    public ProfileState State { get; } = state;
}

public class MouseNotFoundException : Exception
{
    public MouseNotFoundException(IReadOnlyList<DeviceProbe> probes)
        : this($"No HID++ 2.0 device with OnboardProfiles (0x8100) found ({probes.Count} device indexes tried)", probes, null)
    {
    }

    protected MouseNotFoundException(string message, IReadOnlyList<DeviceProbe> probes, Exception? inner)
        : base(message, inner)
    {
        Probes = probes;
    }

    public IReadOnlyList<DeviceProbe> Probes { get; }
}

/// <summary>
/// The device chosen in config (<see cref="MouseController.PreferredDevice"/>) is not reachable: its receiver is absent,
/// the receiver says the device is not connected (asleep, off), or it answered and then dropped off. Nothing was sent to
/// any other device. The next call retries; <see cref="IProfileSwitcher.DeviceArrived"/> fires when its receiver reports
/// it back online.
/// </summary>
public sealed class ChosenDeviceNotFoundException(string deviceKey, IReadOnlyList<DeviceProbe> probes, Exception? inner = null)
    : MouseNotFoundException($"{Core.DeviceKey.NameOf(deviceKey)} not found (asleep or off?)", probes, inner)
{
    /// <summary>The configured key, e.g. <c>C54D/PRO X2 SUPERSTRIKE</c>.</summary>
    public string DeviceKey { get; } = deviceKey;

    /// <summary>The name part of the key, e.g. <c>PRO X2 SUPERSTRIKE</c>.</summary>
    public string DeviceName { get; } = Core.DeviceKey.NameOf(deviceKey);
}

/// <summary>
/// Finds the mouse (all HID++ devices, indexes 0x01..0x06 then 0xFF), keeps the connection until a failure or
/// <see cref="MarkStale"/>, and implements the tray's <see cref="IProfileSwitcher"/>. Device choice: the
/// <see cref="PreferredDevice"/> if set — only that device, never another one: only receivers with the key's PID are
/// opened and its last index is tried first; if it is not there <see cref="ChosenDeviceNotFoundException"/> is thrown
/// and its receiver stays open (read thread only) until a <see cref="DeviceConnection"/> notification says the device
/// is back, which raises <see cref="DeviceArrived"/>. Automatic (no preference): first device type mouse, else first
/// device with 0x8100. Thread-safe; calls block.
/// </summary>
public sealed class MouseController : IProfileSwitcher
{
    private static readonly byte[] ProbeOrder = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0xFF];

    private const byte OnboardModeOnboard = 1;
    private const byte OnboardModeHost = 2;

    /// <summary>HID++ 1.0 error 8: nothing paired at that index (hidpp10_constants.py ErrorCode.UNKNOWN_DEVICE).</summary>
    private const byte UnknownDevice = 0x08;

    private readonly IHidDeviceSource _source;
    private readonly TimeSpan? _timeout;
    private readonly TimeProvider? _time;
    private readonly ILog? _log;
    private readonly Lock _lock = new();
    private Connection? _connection;
    private IReadOnlyList<DeviceProbe> _lastProbe = [];
    private string? _preferredDevice;
    private byte? _preferredIndex; // device index the preferred device was last connected at (cheap first probe)
    private volatile Opened? _waiting; // the preferred device's receiver, kept open while the device is unreachable
    private volatile int _waitIndex = -1; // index to wait for on _waiting; -1 = any
    private bool _forceOnboardMode;
    private byte _ping = 0x5A;
    private bool _disposed;
    private volatile bool _cancelled;

    public MouseController(IHidDeviceSource source, TimeSpan? requestTimeout = null, TimeProvider? timeProvider = null,
        ILog? log = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _timeout = requestTimeout;
        _time = timeProvider;
        _log = log;
    }

    public event Action<int>? ProfileChangedByDevice;

    /// <inheritdoc/>
    public event Action? DeviceArrived;

    /// <summary>Every device index tried by the most recent scan.</summary>
    public IReadOnlyList<DeviceProbe> LastProbe
    {
        get { lock (_lock) return _lastProbe; }
    }

    /// <summary>Preferred device key (<see cref="DeviceKey"/>); null = automatic.</summary>
    public string? PreferredDevice
    {
        get { lock (_lock) return _preferredDevice; }
    }

    /// <summary>When true, a mouse found in host mode (2) is switched to onboard mode (1) on connect.</summary>
    public bool ForceOnboardMode
    {
        get { lock (_lock) return _forceOnboardMode; }
    }

    public void Configure(string? preferredDevice, bool forceOnboardMode)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string? preference = string.IsNullOrWhiteSpace(preferredDevice) ? null : preferredDevice.Trim();
            if (!string.Equals(preference, _preferredDevice, StringComparison.OrdinalIgnoreCase))
                _preferredIndex = null;
            _preferredDevice = preference;
            _forceOnboardMode = forceOnboardMode;
            CloseConnection();
        }
    }

    public IReadOnlyList<KnownDevice> ListDevices(bool rescan)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (rescan)
            {
                CloseConnection();
                try
                {
                    EnsureConnected(fullScan: true);
                }
                catch (MouseNotFoundException)
                {
                    // the list below is empty or shows what answered
                }
            }

            string? active = _connection?.Info.Key;
            var list = new List<KnownDevice>();
            foreach (DeviceProbe p in _lastProbe)
            {
                if (p.Version is not { Major: >= 2 } || !p.HasOnboardProfiles)
                    continue;
                string key = DeviceKey.Format(p.ProductId, p.Name, p.DeviceIndex);
                if (list.Exists(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(new KnownDevice(key, DeviceKey.DisplayName(p.ProductId, p.Name, p.DeviceIndex), p.DeviceType,
                    active is not null && string.Equals(key, active, StringComparison.Ordinal)));
            }
            return list;
        }
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

    /// <summary>
    /// Whole onboard sector (sector size bytes) via memoryRead (function 5, read-only), in 16-byte chunks; like
    /// libratbag the last chunk is read from <c>sectorSize - 16</c>. For the CLI's <c>status --raw</c>.
    /// </summary>
    public byte[] ReadSector(ushort sector) => Execute(c =>
    {
        const int chunk = 16;
        int size = Math.Max((int)c.Description.SectorSize, chunk);
        var data = new byte[size];
        for (int offset = 0; offset < size; offset += chunk)
        {
            int at = Math.Min(offset, size - chunk);
            c.Onboard.MemoryRead(sector, (ushort)at).CopyTo(data, at);
        }
        return data;
    });

    /// <summary>Every profile directory entry (enabled or not), via memoryRead of sector 0x0000. For <c>profiles</c>.</summary>
    public IReadOnlyList<ProfileDirectoryEntry> GetProfileDirectory() =>
        Execute(c => ProfileDirectory.Read(c.Onboard, c.Description));

    /// <summary>getCurrentDpiIndex reply parameter bytes, unparsed. For <c>profiles</c>.</summary>
    public byte[] GetCurrentDpiIndexReply() => Execute(c => c.Onboard.GetCurrentDpiIndexReply());

    public ProfileState SetProfile(int profile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(profile, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(profile, 0xFFFF);
        return Execute(c =>
        {
            // In host mode the mouse acknowledges setCurrentProfile but ignores it (and reports profile 0).
            if (ApplyOnboardMode(c) == OnboardModeHost)
                throw new HostModeException(ReadState(c));
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
            enabled = EnabledDpiSlots(c);
        }
        catch (HidppException)
        {
            // profile sector unreadable: fall back to all five slots
        }

        byte next = NextSlot(current, enabled);
        if (next != current) // a single enabled slot: nothing to switch to
            c.Onboard.SetCurrentDpiIndex(next);
        return (int)next;
    });

    public void MarkStale()
    {
        lock (_lock) CloseConnection();
    }

    /// <inheritdoc/>
    public void Cancel() => _cancelled = true; // no lock: the point is to reach a scan that holds it

    private void ThrowIfCancelled()
    {
        if (_cancelled)
            throw new OperationCanceledException("ProfileHop is exiting.");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            CloseConnection();
        }
    }

    /// <summary>
    /// Enabled DPI slots (DPI != 0) of the current profile, decoded per the profile format from getProfilesDescription
    /// (<see cref="ProfileSector.DpiSlots"/>: libratbag layout for 1..5, inferred layout for 0x08). Reads only the
    /// 16-byte memoryRead chunks covering the DPI list. Null when the format's layout is unknown.
    /// </summary>
    private static bool[]? EnabledDpiSlots(Connection c)
    {
        byte format = c.Description.ProfileFormat;
        int need = ProfileSector.DpiHeadLength(format);
        int profile = c.Onboard.GetCurrentProfile();
        if (need == 0 || profile is < 1 or > 0xFFFF)
            return null;

        const int chunk = 16;
        var head = new byte[(need + chunk - 1) / chunk * chunk];
        for (int offset = 0; offset < head.Length; offset += chunk)
            c.Onboard.MemoryRead((ushort)profile, (ushort)offset).CopyTo(head, offset);
        return ProfileSector.DpiSlots(format, head)?.Select(d => d != 0).ToArray();
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

        c.ProfileNames ??= ReadProfileNames(c, enabled);
        return new ProfileState(mode, current, enabled, dpi) { ProfileNames = c.ProfileNames };
    }

    /// <summary>
    /// Names stored on the mouse (<see cref="ProfileSector.Name"/>), read once per connection with memoryRead only: the
    /// 48-byte name field of each enabled profile's sector, plus profiles 1..profile count when the directory lists
    /// fewer than two (the real mouse does; see ProfileCycle). A profile whose sector can't be read has no name.
    /// </summary>
    private static Dictionary<int, string> ReadProfileNames(Connection c, IReadOnlyList<int> enabled)
    {
        var names = new Dictionary<int, string>();
        ProfilesDescription d;
        try
        {
            d = c.Description;
        }
        catch (HidppException)
        {
            return names;
        }
        const int chunk = 16;
        bool knownLayout = ProfileSector.CanDecode(d.ProfileFormat) || ProfileSector.IsInferredFormat08(d.ProfileFormat);
        if (!knownLayout || d.SectorSize < ProfileSector.NameOffset + ProfileSector.NameLength)
            return names;

        var profiles = new SortedSet<int>(enabled);
        if (enabled.Count < 2)
            for (int p = 1; p <= d.ProfileCount; p++)
                profiles.Add(p);

        var field = new byte[ProfileSector.NameLength];
        foreach (int profile in profiles)
        {
            try
            {
                for (int offset = 0; offset < field.Length; offset += chunk)
                    c.Onboard.MemoryRead((ushort)profile, (ushort)(ProfileSector.NameOffset + offset)).AsSpan(0, chunk).CopyTo(field.AsSpan(offset));
            }
            catch (HidppException)
            {
                continue;
            }
            if (ProfileSector.Name(d.ProfileFormat, field) is { } name)
                names[profile] = name;
        }
        return names;
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
            catch (DeviceUnreachableException ex) when (_preferredDevice is { } preference && ReferenceEquals(_connection, c))
            {
                // The receiver says the chosen device dropped off (asleep, off): keep its receiver open and wait for it.
                _connection = null;
                _log?.Write(LogLevel.Info, $"Chosen device '{preference}' stopped answering ({ex.Message}); waiting for it to reconnect.");
                StartWaiting(c.Opened, c.Info.DeviceIndex);
                throw new ChosenDeviceNotFoundException(preference, [], ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                CloseConnection();
                throw;
            }
        }
    }

    /// <summary>
    /// The link to the mouse is gone or unusable (no reply, transport closed, receiver says the device is not
    /// reachable). Device rejections (HID++ 2.0 error, unconfirmed reply, host mode) keep the connection.
    /// </summary>
    public static bool IsTransportFailure(Exception ex) =>
        ex is TimeoutException or IOException or DeviceUnreachableException or ObjectDisposedException;

    private Connection EnsureConnected(bool fullScan)
    {
        ThrowIfCancelled();
        if (_connection is { Client.IsClosed: false } live)
            return live;
        CloseConnection();
        Connection c = Scan(fullScan);
        try
        {
            ApplyOnboardMode(c);
        }
        catch
        {
            c.Dispose();
            throw;
        }
        _connection = c;
        return c;
    }

    /// <summary>Reads the onboard mode; with forceOnboardMode, switches host mode back to onboard. Returns the resulting mode.</summary>
    private byte ApplyOnboardMode(Connection c)
    {
        byte mode = c.Onboard.GetOnboardMode();
        if (mode != OnboardModeHost)
            return mode;
        if (!_forceOnboardMode)
        {
            _log?.Write(LogLevel.Info, "Mouse is in host mode (G HUB); forceOnboardMode is off, leaving it.");
            return mode;
        }
        c.Onboard.SetOnboardMode(OnboardModeOnboard);
        _log?.Write(LogLevel.Info, "Mouse was in host mode; switched it to onboard mode (forceOnboardMode).");
        return OnboardModeOnboard;
    }

    private void CloseConnection()
    {
        Connection? c = _connection;
        _connection = null;
        c?.Dispose();
        Opened? waiting = _waiting;
        _waiting = null;
        waiting?.Dispose();
    }

    /// <summary>Keeps the chosen device's receiver open; its read thread reports the device coming back (no polling).</summary>
    private void StartWaiting(Opened receiver, byte? index)
    {
        _waitIndex = index ?? -1;
        _waiting = receiver;
        receiver.Client.Notification += msg => OnReceiverNotification(receiver, msg);
    }

    /// <summary>Pump thread: the receiver reports a device link change. Only acts while waiting for the chosen device.</summary>
    private void OnReceiverNotification(Opened receiver, HidppMessage msg)
    {
        if (!ReferenceEquals(_waiting, receiver) || !DeviceConnection.TryDecode(msg, out bool linkEstablished) || !linkEstablished)
            return;
        int index = _waitIndex;
        if (index >= 0 && msg.DeviceIndex != index)
            return;
        _log?.Write(LogLevel.Info, $"Receiver reports device 0x{msg.DeviceIndex:X2} connected; reconnecting to the chosen device.");
        DeviceArrived?.Invoke();
    }

    private Connection Scan(bool fullScan)
    {
        var probes = new List<DeviceProbe>();
        var open = new List<Opened>();
        string? preference = _preferredDevice;
        // The key's PID is the receiver's (or the wired mouse's): a chosen device can only be behind that PID, so other
        // devices are not even opened (a rescan for the device list still probes them, read-only).
        ushort? chosenPid = preference is null ? null : DeviceKey.ProductIdOf(preference);
        Candidate? preferred = null;
        Candidate? mouse = null;
        Candidate? firstCandidate = null;
        Opened? waitOn = null; // the chosen device's receiver, to wait on if the device is not there
        byte? waitIndex = null;

        try
        {
            foreach (HidDevice device in _source.Enumerate())
            {
                ThrowIfCancelled();
                ushort pid = device.Collections.Count > 0 ? device.Collections[0].ProductId : (ushort)0;
                bool chosenReceiver = chosenPid == pid;
                if (!fullScan && chosenPid is not null && !chosenReceiver)
                    continue;
                if (HidReports.Unusable(device.Collections) is { } unusable)
                {
                    probes.Add(new DeviceProbe(device, pid, 0, null, false, null, null, unusable));
                    continue;
                }

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

                var client = new HidppClient(transport, _timeout, _time, _log);
                var opened = new Opened(device, transport, client);
                open.Add(opened);
                var found = new List<Candidate>();
                byte? lastIndex = chosenReceiver ? _preferredIndex : null;
                try
                {
                    foreach (byte index in lastIndex is byte first ? [first, .. ProbeOrder.Where(i => i != first)] : ProbeOrder)
                    {
                        ThrowIfCancelled();
                        (DeviceProbe probe, Candidate? candidate, byte? hidpp10Error) = ProbeIndex(opened, pid, index);
                        probes.Add(probe);
                        if (index == lastIndex && hidpp10Error is { } error && error != UnknownDevice)
                        {
                            // The receiver says the chosen device is paired here but not connected (asleep, off):
                            // don't probe the other indexes, wait for this one.
                            waitIndex = index;
                            if (!fullScan)
                                break;
                        }
                        if (candidate is null)
                            continue;
                        found.Add(candidate);
                        if (!fullScan && (preference is null ? candidate.IsMouse : candidate.Matches(preference)))
                            break;
                    }
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
                {
                    probes.Add(new DeviceProbe(device, pid, 0, null, false, null, null, "transport failed: " + ex.Message));
                    found.Clear();
                    chosenReceiver = false; // nothing to wait on
                }

                if (chosenReceiver)
                    waitOn ??= opened;

                firstCandidate ??= found.FirstOrDefault();
                mouse ??= found.FirstOrDefault(c => c.IsMouse);
                if (preference is not null)
                    preferred ??= found.FirstOrDefault(c => c.Matches(preference));
                if (!fullScan && (preference is null ? mouse : preferred) is not null)
                    break;
            }
        }
        catch
        {
            foreach (Opened o in open)
                o.Dispose();
            throw;
        }
        finally
        {
            _lastProbe = probes;
            if (_log?.IsEnabled(LogLevel.Debug) == true)
            {
                foreach (DeviceProbe p in probes)
                    _log.Write(LogLevel.Debug, $"Probe {p.ProductId:X4} index 0x{p.DeviceIndex:X2}: " +
                        (p.Error ?? $"HID++ {p.Version}, 0x8100 {(p.HasOnboardProfiles ? "yes" : "no")}, type {p.DeviceType}, '{p.Name}'"));
            }
        }

        // A chosen device is the only one ever used: no fallback to another device.
        Candidate? chosen = preference is not null ? preferred : mouse ?? firstCandidate;
        Opened? keep = chosen?.Opened ?? (preference is not null ? waitOn : null);
        foreach (Opened o in open)
        {
            if (!ReferenceEquals(o, keep))
                o.Dispose();
        }

        if (chosen is null && preference is not null)
        {
            _log?.Write(LogLevel.Warning, $"Chosen device '{preference}' not found ({probes.Count} device indexes tried)" +
                (waitOn is null ? "; its receiver is not plugged in." : "; waiting for its receiver to report it."));
            if (waitOn is not null)
                StartWaiting(waitOn, waitIndex ?? _preferredIndex);
            throw new ChosenDeviceNotFoundException(preference, probes);
        }

        if (chosen is null)
        {
            _log?.Write(LogLevel.Info, $"No device with OnboardProfiles found ({probes.Count} device indexes tried).");
            throw new MouseNotFoundException(probes);
        }

        if (preference is not null)
            _preferredIndex = chosen.Probe.DeviceIndex;

        var info = new ConnectedMouse(
            chosen.Opened.Device,
            chosen.Probe.ProductId,
            chosen.Opened.Device.Collections.Count > 0 ? chosen.Opened.Device.Collections[0].Product : null,
            chosen.Probe.DeviceIndex,
            chosen.Probe.Version!.Value,
            chosen.Probe.Name,
            chosen.Probe.DeviceType,
            TypeFallback: preferred is null && !chosen.IsMouse,
            chosen.Key);

        _log?.Write(LogLevel.Info, $"Connected to {info.Key} at index 0x{info.DeviceIndex:X2}, HID++ {info.Version}" +
            (preferred is not null ? " (chosen device)." : info.TypeFallback ? " (no mouse type found, first 0x8100 device)." : " (automatic)."));
        return new Connection(this, chosen.Opened, chosen.Device, info);
    }

    /// <summary>Probes one index; the third value is the HID++ 1.0 error code when the receiver answered 0x8F.</summary>
    private (DeviceProbe, Candidate?, byte?) ProbeIndex(Opened opened, ushort pid, byte index)
    {
        var device = new HidppDevice(opened.Client, index);
        ProtocolVersion version;
        try
        {
            version = device.Root.GetProtocolVersion(NextPing());
        }
        catch (DeviceUnreachableException ex)
        {
            return (Skipped($"HID++ 1.0 error 0x{ex.Hidpp10Error:X2}"), null, ex.Hidpp10Error);
        }
        catch (TimeoutException)
        {
            return (Skipped("no reply"), null, null);
        }
        catch (Exception ex) when (ex is HidppException or HidppProtocolException)
        {
            return (Skipped(ex.Message), null, null);
        }

        if (version.Major < 2)
            return (new DeviceProbe(opened.Device, pid, index, version, false, null, null, "HID++ < 2.0"), null, null);

        bool hasOnboard;
        try
        {
            hasOnboard = device.GetFeatureIndex(FeatureIds.OnboardProfiles) is not null;
        }
        catch (Exception ex) when (ex is HidppException or HidppProtocolException or TimeoutException or DeviceUnreachableException)
        {
            return (new DeviceProbe(opened.Device, pid, index, version, false, null, null, "getFeature failed: " + ex.Message), null, null);
        }

        byte? type = null;
        string? name = null;
        if (TryDeviceName(device, out byte t, out string n))
        {
            type = t;
            name = n;
        }

        var probe = new DeviceProbe(opened.Device, pid, index, version, hasOnboard, type, name, null);
        return (probe, hasOnboard ? new Candidate(opened, device, probe) : null, null);

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
            catch (Exception ex)
            {
                if (IsTransportFailure(ex))
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
        public string Key => DeviceKey.Format(Probe.ProductId, Probe.Name, Probe.DeviceIndex);
        public bool Matches(string preference) => DeviceKey.Matches(Key, preference);
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
        public Opened Opened => _opened;
        public HidppDevice Device { get; }
        public ConnectedMouse Info { get; }
        public OnboardProfilesFeature Onboard { get; }
        public byte OnboardIndex { get; }
        public ProfilesDescription Description => _description ??= Onboard.GetProfilesDescription();

        /// <summary>Profile names read on the first state read of this connection (null until then).</summary>
        public IReadOnlyDictionary<int, string>? ProfileNames { get; set; }

        public void Dispose() => _opened.Dispose();
    }
}
