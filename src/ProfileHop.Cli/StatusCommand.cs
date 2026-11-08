using ProfileHop.Core;
using ProfileHop.Core.Hid;
using ProfileHop.Core.Hidpp;

namespace ProfileHop.Cli;

/// <summary><c>status</c>: onboard mode, profile count, enabled profiles, current profile, DPI slot. Read-only.</summary>
internal static class StatusCommand
{
    public static int Run(IHidDeviceSource source, string[] args)
    {
        using var controller = new MouseController(source);
        try
        {
            ConnectedMouse mouse = controller.Connect();
            CliOutput.PrintMouse(mouse);
            ProfilesDescription d = controller.GetProfilesDescription();
            ProfileState s = controller.GetState();
            Print(d, s);
            return 0;
        }
        catch (MouseNotFoundException ex)
        {
            CliOutput.PrintProbes(ex.Probes);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"status failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    internal static void Print(ProfilesDescription d, ProfileState s)
    {
        string mode = s.OnboardMode switch
        {
            OnboardProfilesFeature.ModeOnboard => "onboard",
            OnboardProfilesFeature.ModeHost => "host (software-controlled)",
            _ => "unknown",
        };
        Console.WriteLine($"Onboard mode:     {s.OnboardMode} ({mode})");
        Console.WriteLine($"Profile count:    {d.ProfileCount} (+{d.ProfileCountOob} read-only), sector size {d.SectorSize}");
        Console.WriteLine($"Enabled profiles: {(s.EnabledProfiles.Count == 0 ? "none found in directory" : string.Join(", ", s.EnabledProfiles))}");
        Console.WriteLine($"Current profile:  {s.CurrentProfile}");
        Console.WriteLine($"DPI slot:         {(s.DpiIndex is { } dpi ? dpi.ToString() : "not read")}");
    }
}
