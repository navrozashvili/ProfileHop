using ProfileHop.Core.Hid;

namespace ProfileHop.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        switch (args.Length == 0 ? null : args[0])
        {
            case "list":
                return ListCommand.Run(new WindowsHidDeviceSource());
            case "probe":
                return ProbeCommand.Run(new WindowsHidDeviceSource(), args);
            case "status":
                return StatusCommand.Run(new WindowsHidDeviceSource(), args);
            case "set-profile":
                return SetProfileCommand.Run(new WindowsHidDeviceSource(), args);
        }

        Console.WriteLine("profilehop-cli <command>");
        Console.WriteLine("  list               List Logitech HID++ collections");
        Console.WriteLine("  probe              Find the mouse and dump its HID++ feature table");
        Console.WriteLine("  status             Show onboard mode, profiles, current profile, DPI slot");
        Console.WriteLine("  set-profile <n>    Activate onboard profile n");
        return args.Length == 0 ? 0 : 1;
    }
}
