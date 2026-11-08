namespace ProfileHop.Core.Hid;

/// <summary>Finds Logitech HID++ devices through SetupAPI/HID.dll and opens overlapped transports to them.</summary>
public sealed class WindowsHidDeviceSource : IHidDeviceSource
{
    public IReadOnlyList<HidDevice> Enumerate() =>
        HidDeviceFinder.FindDevices()
            .Where(d => d.Collections.Any(c => c.UsagePage == HidReports.VendorUsagePage))
            .ToList();

    public IHidTransport Open(HidDevice device) => new HidChannel(device);
}
