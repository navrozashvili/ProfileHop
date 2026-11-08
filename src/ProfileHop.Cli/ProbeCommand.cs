using ProfileHop.Core;
using ProfileHop.Core.Hid;
using ProfileHop.Core.Hidpp;

namespace ProfileHop.Cli;

/// <summary><c>probe</c>: every responding device/index, the chosen device, and its full feature table. Read-only.</summary>
internal static class ProbeCommand
{
    public static int Run(IHidDeviceSource source, string[] args)
    {
        using var controller = new MouseController(source);
        ConnectedMouse mouse;
        try
        {
            mouse = controller.Connect(fullScan: true);
        }
        catch (MouseNotFoundException ex)
        {
            CliOutput.PrintProbes(ex.Probes);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"probe failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        CliOutput.PrintProbes(controller.LastProbe);
        Console.WriteLine();
        Console.Write("Chosen ");
        CliOutput.PrintMouse(mouse);

        IReadOnlyList<FeatureEntry> features;
        try
        {
            features = controller.GetFeatureTable();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"reading feature table failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Features ({features.Count}):");
        Console.WriteLine("  Index  ID      Type  Ver  Name");
        foreach (FeatureEntry f in features)
        {
            string flag = f.Id == FeatureIds.OnboardProfiles ? "   <== onboard profiles" : "";
            Console.WriteLine($"  0x{f.Index:X2}   0x{f.Id:X4}  0x{f.Type:X2}  {f.Version,3}  {FeatureNames.Get(f.Id) ?? "?"}{flag}");
        }

        bool hasOnboard = features.Any(f => f.Id == FeatureIds.OnboardProfiles);
        Console.WriteLine(hasOnboard ? "ONBOARD_PROFILES (0x8100): present" : "ONBOARD_PROFILES (0x8100): MISSING");
        return hasOnboard ? 0 : 1;
    }
}
