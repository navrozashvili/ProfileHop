using static ProfileHop.Core.Tests.Hidpp.Fakes.Frames;

namespace ProfileHop.Core.Tests.Hidpp.Fakes;

/// <summary>
/// A HID++ 2.0 device behind one device index, answering long-report requests with byte-exact replies built from
/// the verified layouts (docs/protocol-verification.md). Unknown calls get a HID++ 2.0 error.
/// </summary>
internal sealed class SimulatedDevice
{
    public byte Major { get; set; } = 4;
    public byte Minor { get; set; } = 2;

    /// <summary>Feature table, position = feature index. Index 0 must be Root (0x0000).</summary>
    public List<ushort> Features { get; } = [0x0000, 0x0001, 0x0003, 0x0005, 0x1D4B, 0x8100];

    public string Name { get; set; } = "PRO X2 SUPERSTRIKE";
    public byte DeviceType { get; set; } = 3; // mouse

    public byte OnboardMode { get; set; } = 1;
    public byte ProfileCount { get; set; } = 3;
    public ushort SectorSize { get; set; } = 255;
    public ushort CurrentProfile { get; set; } = 1;
    public byte DpiIndex { get; set; } = 1;

    /// <summary>getProfilesDescription byte 1. 2 = libratbag G303 layout; the real PRO X2 SUPERSTRIKE reports 0x08.</summary>
    public byte ProfileFormat { get; set; } = 2;

    /// <summary>When set, getCurrentProfile returns this instead of <see cref="CurrentProfile"/>.</summary>
    public ushort? ReportedProfileOverride { get; set; }

    /// <summary>When true, setCurrentProfile is acknowledged but ignored.</summary>
    public bool IgnoreSetProfile { get; set; }

    /// <summary>Sector contents for memoryRead, keyed by sector.</summary>
    public Dictionary<ushort, byte[]> Sectors { get; } = new()
    {
        [0x0000] = Directory((1, true), (2, true), (3, false)),
        [0x0001] = Profile(400, 800, 1600, 3200, 0),
        [0x0002] = Profile(800, 0, 0, 0, 0),
    };

    public List<(int Function, byte[] Params)> Calls { get; } = [];

    public byte IndexOf(ushort featureId) => (byte)Features.IndexOf(featureId);

    public static byte[] Directory(params (ushort Sector, bool Enabled)[] entries)
    {
        var data = new byte[256];
        Array.Fill(data, (byte)0xFF);
        int i = 0;
        foreach (var (sector, enabled) in entries)
        {
            data[i++] = (byte)(sector >> 8);
            data[i++] = (byte)sector;
            data[i++] = enabled ? (byte)1 : (byte)0;
            data[i++] = 0;
        }
        data[i++] = 0xFF;
        data[i++] = 0xFF;
        data[i++] = 0;
        data[i] = 0;
        return data;
    }

    /// <summary>Profile sector: [report rate, default dpi, switched dpi, dpi0..dpi4 LE16, ...].</summary>
    public static byte[] Profile(params ushort[] dpis)
    {
        var data = new byte[256];
        Array.Fill(data, (byte)0xFF);
        data[0] = 1;
        data[1] = 0;
        data[2] = 0;
        for (int i = 0; i < 5; i++)
        {
            data[3 + 2 * i] = (byte)dpis[i];
            data[4 + 2 * i] = (byte)(dpis[i] >> 8);
        }
        return data;
    }

    public IEnumerable<byte[]> Handle(byte[] req)
    {
        byte dev = req[1], feat = req[2], fnByte = req[3];
        int fn = fnByte >> 4;
        byte[] p = req[4..20];

        if (feat >= Features.Count)
            return [Error20(dev, feat, fnByte, 0x06)];

        ushort featureId = Features[feat];
        lock (Calls)
        {
            if (featureId == 0x8100)
                Calls.Add((fn, p));
        }

        byte[]? reply = (featureId, fn) switch
        {
            (0x0000, 0) => GetFeature((ushort)((p[0] << 8) | p[1])),
            (0x0000, 1) => [Major, Minor, p[2]],
            (0x0001, 0) => [(byte)(Features.Count - 1)],
            (0x0001, 1) => p[0] < Features.Count
                ? [(byte)(Features[p[0]] >> 8), (byte)Features[p[0]], 0, 0]
                : null,
            (0x0005, 0) => [(byte)Name.Length],
            (0x0005, 1) => NameChunk(p[0]),
            (0x0005, 2) => [DeviceType],
            (0x8100, 0) => [1, ProfileFormat, 1, ProfileCount, 1, 8, 16, (byte)(SectorSize >> 8), (byte)SectorSize, 0, 2],
            (0x8100, 1) => SetOnboardMode(p[0]),
            (0x8100, 2) => [OnboardMode],
            (0x8100, 3) => SetProfile((ushort)((p[0] << 8) | p[1])),
            (0x8100, 4) => GetProfile(),
            (0x8100, 5) => MemoryRead((ushort)((p[0] << 8) | p[1]), (p[2] << 8) | p[3]),
            (0x8100, 11) => [DpiIndex],
            (0x8100, 12) => SetDpi(p[0]),
            _ => null,
        };

        if (reply is null)
            return [Error20(dev, feat, fnByte, fn > 0xD ? (byte)0x07 : (byte)0x02)];
        return [Long(dev, feat, fnByte, reply)];
    }

    private byte[] GetFeature(ushort id)
    {
        int index = Features.IndexOf(id);
        return index < 0 ? [0, 0, 0] : [(byte)index, 0, 1];
    }

    private byte[] NameChunk(int offset)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(Name);
        return bytes.Skip(offset).Take(16).ToArray();
    }

    private byte[] SetOnboardMode(byte mode)
    {
        OnboardMode = mode;
        return [];
    }

    private byte[]? SetProfile(ushort profile)
    {
        if (profile == 0 || profile > ProfileCount)
            return null;
        if (!IgnoreSetProfile && OnboardMode != 2) // real mouse in host mode: accepted, no effect
            CurrentProfile = profile;
        return [];
    }

    private byte[] GetProfile()
    {
        ushort v = ReportedProfileOverride ?? (OnboardMode == 2 ? (ushort)0 : CurrentProfile); // host mode reports 0 (real PRO X2 SUPERSTRIKE)
        return [(byte)(v >> 8), (byte)v];
    }

    private byte[]? MemoryRead(ushort sector, int offset)
    {
        if (!Sectors.TryGetValue(sector, out byte[]? data) || offset + 16 > SectorSize)
            return null;
        return data[offset..(offset + 16)];
    }

    private byte[]? SetDpi(byte index)
    {
        if (index > 4)
            return null;
        DpiIndex = index;
        return [];
    }
}

/// <summary>A receiver: devices at indexes 0x01-0x06, HID++ 1.0 errors elsewhere.</summary>
internal sealed class SimulatedReceiver
{
    public Dictionary<byte, SimulatedDevice> Paired { get; } = [];

    /// <summary>Indexes that are paired but asleep: 0x8F with error 0x09 (resource error).</summary>
    public HashSet<byte> Asleep { get; } = [];

    /// <summary>Indexes that never answer.</summary>
    public HashSet<byte> Silent { get; } = [];

    public IEnumerable<byte[]> Handle(byte[] req)
    {
        byte dev = req[1];
        if (Silent.Contains(dev))
            return [];
        if (Asleep.Contains(dev))
            return [Error10(dev, req[2], req[3], 0x09)];
        if (Paired.TryGetValue(dev, out SimulatedDevice? d))
            return d.Handle(req);
        // receiver itself (0xFF) speaks HID++ 1.0: invalid sub-ID; empty slots: unknown device
        return [Error10(dev, req[2], req[3], dev == 0xFF ? (byte)0x01 : (byte)0x08)];
    }
}
