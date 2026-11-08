using ProfileHop.Core;
using ProfileHop.Core.Hid;
using ProfileHop.Core.Hidpp;

namespace ProfileHop.Cli;

/// <summary>
/// <c>profiles [--raw]</c>: getProfilesDescription, getCurrentProfile, raw getCurrentDpiIndex reply, the profile
/// directory, and every listed profile sector (plus the read-only ROM profiles) decoded per the reported profile
/// format. Read-only: getters and memoryRead (function 5) only; never switches the onboard mode.
/// </summary>
internal static class ProfilesCommand
{
    public static int Run(IHidDeviceSource source, string[] args)
    {
        bool raw = args.Contains("--raw");
        using var controller = new MouseController(source);
        controller.Configure(null, forceOnboardMode: false);
        try
        {
            ConnectedMouse mouse = controller.Connect();
            CliOutput.PrintMouse(mouse);
            ProfilesDescription d = controller.GetProfilesDescription();
            PrintDescription(d);

            ProfileState s = controller.GetState();
            Console.WriteLine($"Onboard mode:          {s.OnboardMode} ({(s.OnboardMode == OnboardProfilesFeature.ModeHost ? "host" : s.OnboardMode == OnboardProfilesFeature.ModeOnboard ? "onboard" : "unknown")})");
            Console.WriteLine($"Current profile:       {s.CurrentProfile} (getCurrentProfile)");
            Console.WriteLine($"getCurrentDpiIndex:    reply {Try(() => HexFormat.Frame(controller.GetCurrentDpiIndexReply()))}");
            Console.WriteLine("                       (libratbag reads byte 0 as the 0-based DPI slot)");

            Console.WriteLine();
            byte[]? dirSector = ReadOrReport(controller, ProfileDirectory.Sector);
            if (dirSector is not null)
            {
                Console.WriteLine($"Profile directory (sector 0x0000): CRC {(ProfileSector.HasValidCrc(dirSector) ? "valid" : "INVALID (libratbag would fall back to ROM profiles)")}");
                if (raw)
                    Dump(dirSector);
            }

            IReadOnlyList<ProfileDirectoryEntry> dir;
            try
            {
                dir = controller.GetProfileDirectory();
            }
            catch (HidppException ex)
            {
                Console.Error.WriteLine($"reading the profile directory failed: {ex.Message}");
                dir = [];
            }
            Console.WriteLine($"Directory entries: {dir.Count}");

            foreach (ProfileDirectoryEntry e in dir)
            {
                Console.WriteLine();
                Console.WriteLine($"Entry {e.Position}: sector 0x{e.Sector:X4} (profile {e.Sector}), {(e.Enabled ? "enabled" : "disabled")}" +
                    (e.Sector == s.CurrentProfile ? "  <== current" : ""));
                PrintSector(controller, d, e.Sector, raw);
            }

            for (int i = 1; i <= d.ProfileCountOob; i++)
            {
                ushort sector = (ushort)(0x0100 + i);
                Console.WriteLine();
                Console.WriteLine($"ROM profile {i}: sector 0x{sector:X4} (read-only, libratbag HIDPP20_ROM_PROFILES_G402 + {i})");
                PrintSector(controller, d, sector, raw);
            }
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
            Console.Error.WriteLine($"profiles failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void PrintDescription(ProfilesDescription d)
    {
        Console.WriteLine("getProfilesDescription:");
        Console.WriteLine($"  Memory model:        0x{d.MemoryModel:X2}{(d.MemoryModel == 1 ? " (G402, the only one libratbag/Solaar accept)" : " (unknown to libratbag/Solaar)")}");
        Console.WriteLine($"  Profile format:      0x{d.ProfileFormat:X2} ({ProfileSector.FormatName(d.ProfileFormat) ?? "unknown to libratbag/Solaar"})");
        Console.WriteLine($"  Macro format:        0x{d.MacroFormat:X2}");
        Console.WriteLine($"  Profile count:       {d.ProfileCount}");
        Console.WriteLine($"  ROM profile count:   {d.ProfileCountOob}");
        Console.WriteLine($"  Button count:        {d.ButtonCount}");
        Console.WriteLine($"  Sector count:        {d.SectorCount}");
        Console.WriteLine($"  Sector size:         {d.SectorSize}");
        Console.WriteLine($"  Mechanical layout:   0x{d.MechanicalLayout:X2}");
        Console.WriteLine($"  Various info:        0x{d.VariousInfo:X2}");
    }

    private static void PrintSector(MouseController controller, ProfilesDescription d, ushort sector, bool raw)
    {
        byte[]? data = ReadOrReport(controller, sector);
        if (data is null)
            return;

        if (ProfileSector.IsErased(data))
            Console.WriteLine("  Sector is erased (all FF).");
        else if (ProfileSector.Decode(d.ProfileFormat, data) is { } p)
        {
            Console.WriteLine($"  Layout: profile format 0x{d.ProfileFormat:X2} ({ProfileSector.FormatName(d.ProfileFormat)}), " +
                "libratbag union hidpp20_internal_profile / Solaar OnboardProfile.from_bytes");
            Console.WriteLine($"  Report rate:         byte 0 = {p.ReportRateMs} ms -> {p.ReportRateHz} Hz");
            Console.WriteLine($"  Default DPI index:   {p.DefaultDpiIndex}");
            Console.WriteLine($"  Switched DPI index:  {p.SwitchedDpiIndex}");
            Console.WriteLine($"  DPI list (LE16 x5):  {string.Join(", ", p.Dpi.Select((v, i) => $"[{i}] {(v == 0 ? "off" : v.ToString())}"))}");
            Console.WriteLine($"  CRC:                 stored 0x{p.StoredCrc:X4}, computed 0x{p.ComputedCrc:X4} -> {(p.CrcValid ? "valid" : "INVALID")}");
        }
        else
        {
            Console.WriteLine($"  Profile format 0x{d.ProfileFormat:X2} is not one libratbag/Solaar decode (1-5); raw only.");
            Console.WriteLine($"  CRC: {(ProfileSector.HasValidCrc(data) ? "valid" : "INVALID")}");
            if (!raw)
                Dump(data);
        }

        if (raw)
            Dump(data);
    }

    private static byte[]? ReadOrReport(MouseController controller, ushort sector)
    {
        try
        {
            return controller.ReadSector(sector);
        }
        catch (HidppException ex)
        {
            Console.WriteLine($"  memoryRead of sector 0x{sector:X4} rejected: {ex.Message}");
            return null;
        }
    }

    private static void Dump(byte[] data)
    {
        Console.WriteLine($"  {data.Length} bytes via memoryRead:");
        foreach (string line in HexFormat.Dump(data))
            Console.WriteLine("    " + line);
    }

    private static string Try(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (HidppException ex)
        {
            return "rejected: " + ex.Message;
        }
    }
}
