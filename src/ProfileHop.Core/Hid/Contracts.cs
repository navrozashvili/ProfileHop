using System.Threading.Channels;

namespace ProfileHop.Core.Hid;

/// <summary>One HID top-level collection on the Logitech vendor usage page (0xFF00).</summary>
public sealed record HidCollection(
    string Path,
    ushort VendorId,
    ushort ProductId,
    string? Product,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength);

/// <summary>All HID++ collections that belong to one physical device (receiver or wired mouse).</summary>
/// <param name="DeviceKey">Path key shared by the collections (see <see cref="HidPaths.DeviceKey"/>).</param>
public sealed record HidDevice(string DeviceKey, IReadOnlyList<HidCollection> Collections);

/// <summary>A write/read pipe to one HID++ device, covering all of its opened collections.</summary>
public interface IHidTransport : IDisposable
{
    /// <summary>Sends one complete report. Byte 0 is the report ID (0x10 short, 0x11 long).</summary>
    void Write(ReadOnlySpan<byte> report);

    /// <summary>Every input report from any opened collection. Byte 0 is the report ID.</summary>
    ChannelReader<byte[]> Reports { get; }
}

/// <summary>Finds HID++ devices and opens transports to them.</summary>
public interface IHidDeviceSource
{
    IReadOnlyList<HidDevice> Enumerate();

    IHidTransport Open(HidDevice device);
}
