using System.Globalization;
using ProfileHop.Core.Hid;

namespace ProfileHop.Cli;

/// <summary><c>list</c>: prints every Logitech HID++ device and its collections. Query only, sends nothing.</summary>
internal static class ListCommand
{
    public static int Run(IHidDeviceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlyList<HidDevice> devices = source.Enumerate();
        if (devices.Count == 0)
        {
            Console.WriteLine("No Logitech HID++ collections (VID 046D, usage page FF00) found.");
            return 1;
        }

        foreach (HidDevice device in devices)
        {
            HidCollection first = device.Collections[0];
            Console.WriteLine(device.DeviceKey);
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  PID 0x{first.ProductId:X4}  \"{first.Product ?? "(no product string)"}\""));
            foreach (HidCollection c in device.Collections)
            {
                string usage = HidReports.Classify(c.UsagePage, c.Usage) switch
                {
                    HidReportKind.Short => "short",
                    HidReportKind.Long => "long",
                    _ => string.Create(CultureInfo.InvariantCulture, $"0x{c.UsagePage:X4}/0x{c.Usage:X4}"),
                };
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    {usage,-5}  in {c.InputReportLength,2}  out {c.OutputReportLength,2}  {c.Path}"));
            }
        }

        return 0;
    }
}
