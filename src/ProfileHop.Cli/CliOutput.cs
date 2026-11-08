using ProfileHop.Core;
using ProfileHop.Core.Hidpp;

namespace ProfileHop.Cli;

internal static class CliOutput
{
    public static string DeviceType(byte? type) => type is { } t ? DeviceNameFeature.TypeName(t) : "-";

    public static void PrintMouse(ConnectedMouse m)
    {
        Console.WriteLine(
            $"Device: PID 0x{m.ProductId:X4} ({m.Product ?? "?"}), index 0x{m.DeviceIndex:X2}, HID++ {m.Version}, " +
            $"type {DeviceType(m.DeviceType)}, name \"{m.Name ?? "-"}\"");
        if (m.TypeFallback)
            Console.WriteLine("Warning: no device reported type mouse; using the first device with ONBOARD_PROFILES (0x8100).");
    }

    public static void PrintProbes(IReadOnlyList<DeviceProbe> probes)
    {
        Console.WriteLine("Probed device indexes:");
        Console.WriteLine("  PID     Index  HID++  0x8100  Type        Name / result");
        foreach (DeviceProbe p in probes)
        {
            string version = p.Version?.ToString() ?? "-";
            string onboard = p.Version is null ? "-" : p.HasOnboardProfiles ? "yes" : "no";
            string detail = p.Error ?? $"\"{p.Name ?? "-"}\"";
            Console.WriteLine($"  0x{p.ProductId:X4}  0x{p.DeviceIndex:X2}   {version,-5}  {onboard,-6}  {DeviceType(p.DeviceType),-10}  {detail}");
        }
    }
}
