namespace ProfileHop.Core.Hidpp;

/// <summary>HID++ 2.0 feature IDs used by ProfileHop (docs/protocol-verification.md).</summary>
public static class FeatureIds
{
    public const ushort Root = 0x0000;
    public const ushort FeatureSet = 0x0001;
    public const ushort DeviceName = 0x0005;
    public const ushort OnboardProfiles = 0x8100;
}

/// <summary>One HID++ 2.0 device (a device index on a transport) with a cached feature-index table.</summary>
public sealed class HidppDevice
{
    private readonly Dictionary<ushort, byte?> _indexes = new() { [FeatureIds.Root] = 0 };
    private readonly Lock _lock = new();

    public HidppDevice(HidppClient client, byte deviceIndex)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        DeviceIndex = deviceIndex;
        Root = new RootFeature(this);
    }

    public HidppClient Client { get; }
    public byte DeviceIndex { get; }
    public RootFeature Root { get; }

    public byte[] Call(byte featureIndex, byte function, ReadOnlySpan<byte> parameters) =>
        Client.Request(DeviceIndex, featureIndex, function, parameters);

    /// <summary>Feature index via Root getFeature, cached (absent features too). Null if not supported.</summary>
    public byte? GetFeatureIndex(ushort featureId)
    {
        lock (_lock)
        {
            if (_indexes.TryGetValue(featureId, out byte? cached))
                return cached;
        }

        byte? index = Root.GetFeature(featureId)?.Index;
        lock (_lock) _indexes[featureId] = index;
        return index;
    }

    public byte RequireFeatureIndex(ushort featureId) =>
        GetFeatureIndex(featureId)
        ?? throw new NotSupportedException($"Device 0x{DeviceIndex:X2} does not have feature 0x{featureId:X4}");
}

public readonly record struct FeatureInfo(byte Index, byte Type, byte Version);

public readonly record struct ProtocolVersion(byte Major, byte Minor)
{
    public override string ToString() => $"{Major}.{Minor}";
}

/// <summary>Root (0x0000), always at feature index 0.</summary>
public sealed class RootFeature(HidppDevice device)
{
    private const byte FnGetFeature = 0;
    private const byte FnGetProtocolVersion = 1;

    /// <summary>getFeature: [index, type, version]; null when the reply index is 0 (not supported).</summary>
    public FeatureInfo? GetFeature(ushort featureId)
    {
        byte[] r = device.Call(0, FnGetFeature, [(byte)(featureId >> 8), (byte)featureId]);
        return r[0] == 0 ? null : new FeatureInfo(r[0], r[1], r[2]);
    }

    /// <summary>getProtocolVersion: params 00 00 ping, reply [major, minor, ping echo].</summary>
    public ProtocolVersion GetProtocolVersion(byte ping)
    {
        byte[] r = device.Call(0, FnGetProtocolVersion, [0, 0, ping]);
        if (r[2] != ping)
            throw new HidppProtocolException(
                $"Ping echo mismatch from device 0x{device.DeviceIndex:X2}: sent 0x{ping:X2}, got 0x{r[2]:X2}");
        return new ProtocolVersion(r[0], r[1]);
    }
}

public readonly record struct FeatureEntry(byte Index, ushort Id, byte Type, byte Version);

/// <summary>FeatureSet (0x0001).</summary>
public sealed class FeatureSetFeature(HidppDevice device)
{
    private const byte FnGetCount = 0;
    private const byte FnGetFeatureId = 1;

    private byte Index => device.RequireFeatureIndex(FeatureIds.FeatureSet);

    /// <summary>Number of features excluding Root; valid indexes are 0..count.</summary>
    public int GetCount() => device.Call(Index, FnGetCount, [])[0];

    public FeatureEntry GetFeatureId(byte index)
    {
        byte[] r = device.Call(Index, FnGetFeatureId, [index]);
        return new FeatureEntry(index, (ushort)((r[0] << 8) | r[1]), r[2], r[3]);
    }

    public IReadOnlyList<FeatureEntry> GetAll()
    {
        int count = GetCount();
        var list = new List<FeatureEntry>(count + 1);
        for (int i = 0; i <= count; i++)
            list.Add(GetFeatureId((byte)i));
        return list;
    }
}

/// <summary>DeviceName (0x0005): name and device type (3 = mouse).</summary>
public sealed class DeviceNameFeature(HidppDevice device)
{
    private const byte FnGetNameLength = 0;
    private const byte FnGetName = 1;
    private const byte FnGetDeviceType = 2;
    private const int MaxChunks = 16;

    public const byte TypeKeyboard = 0;
    public const byte TypeMouse = 3;

    private byte Index => device.RequireFeatureIndex(FeatureIds.DeviceName);

    public string GetName()
    {
        byte index = Index;
        int length = device.Call(index, FnGetNameLength, [])[0];
        var name = new List<byte>(length);
        for (int chunk = 0; chunk < MaxChunks && name.Count < length; chunk++)
        {
            byte[] r = device.Call(index, FnGetName, [(byte)name.Count]);
            int take = Math.Min(r.Length, length - name.Count);
            int nul = Array.IndexOf(r, (byte)0, 0, take);
            if (nul == 0)
                break;
            if (nul > 0)
                take = nul;
            name.AddRange(r.AsSpan(0, take));
        }
        return System.Text.Encoding.UTF8.GetString(name.ToArray());
    }

    public byte GetDeviceType() => device.Call(Index, FnGetDeviceType, [])[0];

    public static string TypeName(byte type) => type switch
    {
        0 => "keyboard",
        1 => "remote control",
        2 => "numpad",
        3 => "mouse",
        4 => "touchpad",
        5 => "trackball",
        6 => "presenter",
        7 => "receiver",
        _ => $"unknown ({type})",
    };
}

/// <summary>getProfilesDescription reply.</summary>
public sealed record ProfilesDescription(
    byte MemoryModel,
    byte ProfileFormat,
    byte MacroFormat,
    byte ProfileCount,
    byte ProfileCountOob,
    byte ButtonCount,
    byte SectorCount,
    ushort SectorSize,
    byte MechanicalLayout,
    byte VariousInfo);

/// <summary>
/// OnboardProfiles (0x8100). Read functions, setOnboardMode, setCurrentProfile and setCurrentDpiIndex only.
/// Memory write functions 6, 7, 8 are deliberately absent (CLAUDE.md: never write mouse memory).
/// </summary>
public sealed class OnboardProfilesFeature(HidppDevice device)
{
    private const byte FnGetProfilesDescription = 0;
    private const byte FnSetOnboardMode = 1;
    private const byte FnGetOnboardMode = 2;
    private const byte FnSetCurrentProfile = 3;
    private const byte FnGetCurrentProfile = 4;
    private const byte FnMemoryRead = 5;
    private const byte FnGetCurrentDpiIndex = 11;
    private const byte FnSetCurrentDpiIndex = 12;

    public const byte ModeOnboard = 1;
    public const byte ModeHost = 2;

    /// <summary>libratbag HIDPP20_DPI_COUNT: five DPI slots per profile.</summary>
    public const int DpiSlotCount = 5;

    public byte Index => device.RequireFeatureIndex(FeatureIds.OnboardProfiles);

    public ProfilesDescription GetProfilesDescription()
    {
        byte[] r = device.Call(Index, FnGetProfilesDescription, []);
        return new ProfilesDescription(r[0], r[1], r[2], r[3], r[4], r[5], r[6], (ushort)((r[7] << 8) | r[8]), r[9], r[10]);
    }

    public byte GetOnboardMode() => device.Call(Index, FnGetOnboardMode, [])[0];

    public void SetOnboardMode(byte mode) => device.Call(Index, FnSetOnboardMode, [mode]);

    /// <summary>1-based profile number (= profile sector), big-endian.</summary>
    public int GetCurrentProfile()
    {
        byte[] r = device.Call(Index, FnGetCurrentProfile, []);
        return (r[0] << 8) | r[1];
    }

    public void SetCurrentProfile(int profile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(profile, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(profile, 0xFFFF);
        device.Call(Index, FnSetCurrentProfile, [(byte)(profile >> 8), (byte)profile]);
    }

    /// <summary>16 bytes of onboard memory (read-only).</summary>
    public byte[] MemoryRead(ushort sector, ushort offset)
    {
        byte[] r = device.Call(Index, FnMemoryRead, [(byte)(sector >> 8), (byte)sector, (byte)(offset >> 8), (byte)offset]);
        return r[..16];
    }

    public byte GetCurrentDpiIndex() => GetCurrentDpiIndexReply()[0];

    /// <summary>All reply parameter bytes of getCurrentDpiIndex (diagnostics).</summary>
    public byte[] GetCurrentDpiIndexReply() => device.Call(Index, FnGetCurrentDpiIndex, []);

    public void SetCurrentDpiIndex(byte index) => device.Call(Index, FnSetCurrentDpiIndex, [index]);
}
