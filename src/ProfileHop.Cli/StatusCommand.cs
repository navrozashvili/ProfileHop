using ProfileHop.Core;
using ProfileHop.Core.Hid;
using ProfileHop.Core.Hidpp;

namespace ProfileHop.Cli;

/// <summary>
/// <c>status [--force-onboard] [--raw]</c>: onboard mode, profile count, enabled profiles, current profile, DPI slot.
/// <c>--raw</c> also hex-dumps sector 0x0000 (memoryRead, read-only). Read-only unless <c>--force-onboard</c> switches
/// host mode back to onboard.
/// </summary>
internal static class StatusCommand
{
    public static int Run(IHidDeviceSource source, string[] args)
    {
        bool force = args.Contains("--force-onboard");
        using var controller = new MouseController(source);
        controller.Configure(null, forceOnboardMode: force);
        try
        {
            ConnectedMouse mouse = controller.Connect();
            CliOutput.PrintMouse(mouse);
            ProfilesDescription d = controller.GetProfilesDescription();
            ProfileState s = controller.GetState();
            Print(d, s);
            if (!args.Contains("--raw"))
                return 0;

            Console.WriteLine();
            byte[] raw;
            try
            {
                raw = controller.ReadSector(ProfileDirectory.Sector);
            }
            catch (HidppException ex)
            {
                Console.Error.WriteLine($"memoryRead of sector 0x0000 rejected: {ex.Message}");
                return 1;
            }
            Console.WriteLine($"Sector 0x0000 (profile directory), {raw.Length} bytes via memoryRead:");
            foreach (string line in HexFormat.Dump(raw))
                Console.WriteLine("  " + line);
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
        Console.WriteLine($"Current profile:  {s.CurrentProfile}{(s.CurrentProfile == 0 ? " (none: host mode, close G HUB or use --force-onboard)" : "")}");
        Console.WriteLine($"DPI slot:         {(s.DpiIndex is { } dpi ? dpi.ToString() : "not read")}");
    }
}
