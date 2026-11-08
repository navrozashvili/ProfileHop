namespace ProfileHop.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("profilehop-cli <command>");
        Console.WriteLine("  list    List Logitech HID++ collections");
        return args.Length == 0 ? 0 : 1;
    }
}
