using ProfileHop.Core.Hid;

namespace ProfileHop.Core.Tests.Hid;

public class HidPathsTests
{
    private const string Col01 = @"\\?\hid#vid_046d&pid_c547&mi_02&col01#7&2a3b4c5d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string Col02 = @"\\?\hid#vid_046d&pid_c547&mi_02&col02#7&2a3b4c5d&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string OtherInterface = @"\\?\hid#vid_046d&pid_c547&mi_01&col01#7&1f2e3d4c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string OtherInstance = @"\\?\hid#vid_046d&pid_c547&mi_02&col01#8&99aabbcc&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string NoCol = @"\\?\hid#vid_046d&pid_c547&mi_00#7&11223344&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    // Real paths of the PRO X2 SUPERSTRIKE receiver (user's machine): the collection index is also the last
    // part of the instance ID (&0000 / &0001).
    private const string RealCol01 = @"\\?\hid#vid_046d&pid_c54d&mi_02&col01#a&6df8f16&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string RealCol02 = @"\\?\hid#vid_046d&pid_c54d&mi_02&col02#a&6df8f16&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void DeviceKey_DropsCollectionAndKeepsInstance()
    {
        Assert.Equal(@"\\?\hid#vid_046d&pid_c547&mi_02#7&2a3b4c5d&0", HidPaths.DeviceKey(Col01));
    }

    [Fact]
    public void DeviceKey_SameForCollectionsOfOneDevice()
    {
        Assert.Equal(HidPaths.DeviceKey(Col01), HidPaths.DeviceKey(Col02));
    }

    [Fact]
    public void DeviceKey_SameForRealReceiverCollections()
    {
        Assert.Equal(@"\\?\hid#vid_046d&pid_c54d&mi_02#a&6df8f16&0", HidPaths.DeviceKey(RealCol01));
        Assert.Equal(HidPaths.DeviceKey(RealCol01), HidPaths.DeviceKey(RealCol02));
    }

    [Fact]
    public void DeviceKey_IsCaseInsensitive()
    {
        Assert.Equal(HidPaths.DeviceKey(Col01), HidPaths.DeviceKey(Col02.ToUpperInvariant()));
    }

    [Fact]
    public void DeviceKey_DiffersForOtherInterface()
    {
        Assert.NotEqual(HidPaths.DeviceKey(Col01), HidPaths.DeviceKey(OtherInterface));
    }

    [Fact]
    public void DeviceKey_DiffersForSecondReceiverOfSameModel()
    {
        Assert.NotEqual(HidPaths.DeviceKey(Col01), HidPaths.DeviceKey(OtherInstance));
    }

    [Fact]
    public void DeviceKey_PathWithoutCol_IsWholePath()
    {
        Assert.Equal(NoCol, HidPaths.DeviceKey(NoCol));
    }

    [Fact]
    public void Group_PutsCollectionsOfOneDeviceTogether()
    {
        HidCollection[] cols = [Make(Col01, 1), Make(OtherInterface, 1), Make(Col02.ToUpperInvariant(), 2), Make(NoCol, 1)];

        IReadOnlyList<HidDevice> devices = HidPaths.Group(cols);

        Assert.Equal(3, devices.Count);
        Assert.Equal(@"\\?\hid#vid_046d&pid_c547&mi_02#7&2a3b4c5d&0", devices[0].DeviceKey);
        Assert.Equal([Col01, Col02.ToUpperInvariant()], devices[0].Collections.Select(c => c.Path));
        Assert.Equal([OtherInterface], devices[1].Collections.Select(c => c.Path));
        Assert.Equal([NoCol], devices[2].Collections.Select(c => c.Path));
    }

    [Fact]
    public void Group_KeepsTwoReceiversOfSameModelApart()
    {
        HidCollection[] cols = [Make(Col01, 1), Make(OtherInstance, 1), Make(Col02, 2)];

        IReadOnlyList<HidDevice> devices = HidPaths.Group(cols);

        Assert.Equal(2, devices.Count);
        Assert.Equal([Col01, Col02], devices[0].Collections.Select(c => c.Path));
        Assert.Equal([OtherInstance], devices[1].Collections.Select(c => c.Path));
    }

    [Fact]
    public void Group_RealReceiverCollectionsFormOneDevice()
    {
        IReadOnlyList<HidDevice> devices = HidPaths.Group([Make(RealCol01, 1), Make(RealCol02, 2)]);

        Assert.Equal([RealCol01, RealCol02], Assert.Single(devices).Collections.Select(c => c.Path));
    }

    internal static HidCollection Make(string path, ushort usage, int input = 20, int output = 20) =>
        new(path, 0x046D, 0xC547, "USB Receiver", 0xFF00, usage, input, output);
}
