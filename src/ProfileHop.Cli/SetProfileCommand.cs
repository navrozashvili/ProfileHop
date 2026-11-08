using System.Globalization;
using ProfileHop.Core;
using ProfileHop.Core.Hid;
using ProfileHop.Core.Hidpp;

namespace ProfileHop.Cli;

/// <summary>
/// <c>set-profile &lt;n&gt;</c>: setCurrentProfile(n), confirmed with getCurrentProfile. Accepts args with or
/// without the leading command name.
/// </summary>
internal static class SetProfileCommand
{
    public static int Run(IHidDeviceSource source, string[] args)
    {
        string[] rest = args.Length > 0 && args[0] == "set-profile" ? args[1..] : args;
        if (rest.Length != 1
            || !int.TryParse(rest[0], NumberStyles.None, CultureInfo.InvariantCulture, out int profile)
            || profile < 1 || profile > 0xFFFF)
        {
            Console.Error.WriteLine("usage: set-profile <n>   (n = 1-based onboard profile number)");
            return 2;
        }

        using var controller = new MouseController(source);
        try
        {
            ConnectedMouse mouse = controller.Connect();
            CliOutput.PrintMouse(mouse);
            ProfileState s = controller.SetProfile(profile);
            Console.WriteLine($"Switched to profile {s.CurrentProfile} (confirmed by getCurrentProfile).");
            StatusCommand.Print(controller.GetProfilesDescription(), s);
            return 0;
        }
        catch (MouseNotFoundException ex)
        {
            CliOutput.PrintProbes(ex.Probes);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (HidppException ex)
        {
            Console.Error.WriteLine($"set-profile {profile} rejected by the mouse: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"set-profile failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
