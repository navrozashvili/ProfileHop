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
        }

        Console.WriteLine("profilehop-cli <command>");
        Console.WriteLine("  list    List Logitech HID++ collections");
        return args.Length == 0 ? 0 : 1;
    }
}
