using ProfileHop.Core.Hid;

namespace ProfileHop.Core.Tests.Hidpp.Fakes;

internal sealed class FakeHidDeviceSource : IHidDeviceSource
{
    private readonly List<(HidDevice Device, Func<byte[], IEnumerable<byte[]>> Responder)> _devices = [];

    public int EnumerateCount { get; private set; }

    public List<(HidDevice Device, FakeHidTransport Transport)> Opened { get; } = [];

    public static HidDevice MakeDevice(string key, ushort pid) => new(key,
    [
        new HidCollection(key + "&col01", 0x046D, pid, "USB Receiver", 0xFF00, 0x0001, 7, 7),
        new HidCollection(key + "&col02", 0x046D, pid, "USB Receiver", 0xFF00, 0x0002, 20, 20),
    ]);

    public HidDevice Add(string key, ushort pid, Func<byte[], IEnumerable<byte[]>> responder)
    {
        HidDevice d = MakeDevice(key, pid);
        _devices.Add((d, responder));
        return d;
    }

    public IReadOnlyList<HidDevice> Enumerate()
    {
        EnumerateCount++;
        return _devices.Select(d => d.Device).ToList();
    }

    public IHidTransport Open(HidDevice device)
    {
        var entry = _devices.Single(d => d.Device == device);
        var t = new FakeHidTransport { Responder = entry.Responder };
        Opened.Add((device, t));
        return t;
    }

    public FakeHidTransport LastTransportFor(HidDevice device) => Opened.Last(o => o.Device == device).Transport;
}
