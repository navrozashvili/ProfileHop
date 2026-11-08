using ProfileHop.Core.Hidpp;
using ProfileHop.Core.Tests.Hidpp.Fakes;
using static ProfileHop.Core.Tests.Hidpp.Fakes.Frames;

namespace ProfileHop.Core.Tests.Hidpp;

public sealed class FeatureTests : IDisposable
{
    private const byte Dev = 0x01;
    private readonly SimulatedDevice _sim = new();
    private readonly FakeHidTransport _transport = new();
    private readonly HidppClient _client;
    private readonly HidppDevice _device;

    public FeatureTests()
    {
        _transport.Responder = _sim.Handle;
        _client = new HidppClient(_transport, TimeSpan.FromMilliseconds(200));
        _device = new HidppDevice(_client, Dev);
    }

    public void Dispose() => _client.Dispose();

    private byte OnboardIndex => _sim.IndexOf(0x8100);

    // ---- Root ----

    [Fact]
    public void GetFeature_sends_feature_id_big_endian_on_index_0_function_0()
    {
        FeatureInfo? info = _device.Root.GetFeature(0x8100);

        Assert.Equal(Long(Dev, 0x00, 0x0B, 0x81, 0x00), _transport.Written.Single());
        Assert.NotNull(info);
        Assert.Equal(OnboardIndex, info.Value.Index);
        Assert.Equal(1, info.Value.Version);
    }

    [Fact]
    public void GetFeature_returns_null_when_index_is_0()
    {
        Assert.Null(_device.Root.GetFeature(0x2201));
    }

    [Fact]
    public void GetProtocolVersion_sends_ping_and_parses_major_minor()
    {
        ProtocolVersion v = _device.Root.GetProtocolVersion(0x5A);

        Assert.Equal(Long(Dev, 0x00, 0x1B, 0x00, 0x00, 0x5A), _transport.Written.Single());
        Assert.Equal(4, v.Major);
        Assert.Equal(2, v.Minor);
    }

    [Fact]
    public void GetProtocolVersion_rejects_wrong_ping_echo()
    {
        _transport.Responder = _ => [Long(Dev, 0x00, 0x1B, 4, 2, 0x11)];

        Assert.Throws<HidppProtocolException>(() => _device.Root.GetProtocolVersion(0x5A));
    }

    [Fact]
    public void Feature_index_lookup_is_cached_including_absent_features()
    {
        Assert.Equal(OnboardIndex, _device.GetFeatureIndex(0x8100));
        Assert.Equal(OnboardIndex, _device.GetFeatureIndex(0x8100));
        Assert.Null(_device.GetFeatureIndex(0x2201));
        Assert.Null(_device.GetFeatureIndex(0x2201));

        Assert.Equal(2, _transport.Written.Count);
    }

    [Fact]
    public void Root_feature_index_is_always_0_without_a_request()
    {
        Assert.Equal((byte)0, _device.GetFeatureIndex(0x0000));
        Assert.Empty(_transport.Written);
    }

    [Fact]
    public void RequireFeatureIndex_throws_when_absent()
    {
        Assert.Throws<NotSupportedException>(() => _device.RequireFeatureIndex(0x2201));
    }

    // ---- FeatureSet ----

    [Fact]
    public void FeatureSet_GetCount_excludes_root()
    {
        var fs = new FeatureSetFeature(_device);

        Assert.Equal(_sim.Features.Count - 1, fs.GetCount());
        Assert.Equal(Long(Dev, _sim.IndexOf(0x0001), Fn(0)), _transport.Written[^1]);
    }

    [Fact]
    public void FeatureSet_GetFeatureId_parses_id_type_version()
    {
        _transport.Responder = req => req[2] == 0
            ? [Long(Dev, 0, req[3], 0x01, 0, 0)]
            : [Long(Dev, 0x01, req[3], 0x81, 0x00, 0x40, 0x07)];
        var fs = new FeatureSetFeature(_device);

        FeatureEntry e = fs.GetFeatureId(9);

        Assert.Equal(Long(Dev, 0x01, Fn(1), 9), _transport.Written[^1]);
        Assert.Equal(new FeatureEntry(9, 0x8100, 0x40, 0x07), e);
    }

    [Fact]
    public void FeatureSet_GetAll_returns_indexes_0_through_count()
    {
        var fs = new FeatureSetFeature(_device);

        IReadOnlyList<FeatureEntry> all = fs.GetAll();

        Assert.Equal(_sim.Features, all.Select(e => e.Id));
        Assert.Equal(Enumerable.Range(0, _sim.Features.Count).Select(i => (byte)i), all.Select(e => e.Index));
    }

    // ---- DeviceName ----

    [Fact]
    public void DeviceName_reads_name_in_chunks()
    {
        _sim.Name = "PRO X2 SUPERSTRIKE LIGHTSPEED"; // 29 chars, two chunks
        var dn = new DeviceNameFeature(_device);

        string name = dn.GetName();

        byte idx = _sim.IndexOf(0x0005);
        Assert.Equal("PRO X2 SUPERSTRIKE LIGHTSPEED", name);
        Assert.Contains(_transport.Written, w => w.SequenceEqual(Long(Dev, idx, Fn(0))));
        Assert.Contains(_transport.Written, w => w.SequenceEqual(Long(Dev, idx, Fn(1), 0)));
        Assert.Contains(_transport.Written, w => w.SequenceEqual(Long(Dev, idx, Fn(1), 16)));
    }

    [Fact]
    public void DeviceName_GetDeviceType_is_function_2()
    {
        var dn = new DeviceNameFeature(_device);

        Assert.Equal(3, dn.GetDeviceType());
        Assert.Equal(Long(Dev, _sim.IndexOf(0x0005), Fn(2)), _transport.Written[^1]);
    }

    [Fact]
    public void DeviceName_stops_on_empty_chunk()
    {
        _transport.Responder = req => req[2] == 0
            ? [Long(Dev, 0, req[3], 3, 0, 0)]
            : (req[3] >> 4) == 0 ? [Long(Dev, 3, req[3], 40)] : [Long(Dev, 3, req[3])];
        var dn = new DeviceNameFeature(_device);

        Assert.Equal("", dn.GetName());
        Assert.True(_transport.Written.Count < 20);
    }

    // ---- OnboardProfiles ----

    [Fact]
    public void GetProfilesDescription_parses_layout()
    {
        var ob = new OnboardProfilesFeature(_device);

        ProfilesDescription d = ob.GetProfilesDescription();

        Assert.Equal(Long(Dev, OnboardIndex, Fn(0)), _transport.Written[^1]);
        Assert.Equal(new ProfilesDescription(1, 2, 1, 3, 1, 8, 16, 255, 0, 2), d);
    }

    [Fact]
    public void GetOnboardMode_is_function_2()
    {
        var ob = new OnboardProfilesFeature(_device);

        Assert.Equal(1, ob.GetOnboardMode());
        Assert.Equal(Long(Dev, OnboardIndex, Fn(2)), _transport.Written[^1]);
    }

    [Fact]
    public void SetOnboardMode_is_function_1_with_mode_byte()
    {
        var ob = new OnboardProfilesFeature(_device);

        ob.SetOnboardMode(1);

        Assert.Equal(Long(Dev, OnboardIndex, Fn(1), 1), _transport.Written[^1]);
    }

    [Fact]
    public void SetCurrentProfile_is_function_3_with_big_endian_profile()
    {
        var ob = new OnboardProfilesFeature(_device);

        ob.SetCurrentProfile(2);

        Assert.Equal(Long(Dev, OnboardIndex, Fn(3), 0x00, 0x02), _transport.Written[^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x10000)]
    public void SetCurrentProfile_rejects_out_of_range(int profile)
    {
        var ob = new OnboardProfilesFeature(_device);
        Assert.Throws<ArgumentOutOfRangeException>(() => ob.SetCurrentProfile(profile));
    }

    [Fact]
    public void GetCurrentProfile_is_function_4_and_parses_big_endian()
    {
        _sim.CurrentProfile = 0x0102;
        var ob = new OnboardProfilesFeature(_device);

        Assert.Equal(0x0102, ob.GetCurrentProfile());
        Assert.Equal(Long(Dev, OnboardIndex, Fn(4)), _transport.Written[^1]);
    }

    [Fact]
    public void MemoryRead_is_function_5_with_sector_and_offset_big_endian()
    {
        var ob = new OnboardProfilesFeature(_device);

        byte[] data = ob.MemoryRead(0x0001, 0x0010);

        Assert.Equal(Long(Dev, OnboardIndex, Fn(5), 0x00, 0x01, 0x00, 0x10), _transport.Written[^1]);
        Assert.Equal(_sim.Sectors[0x0001][16..32], data);
    }

    [Fact]
    public void GetCurrentDpiIndex_is_function_11()
    {
        var ob = new OnboardProfilesFeature(_device);

        Assert.Equal(1, ob.GetCurrentDpiIndex());
        Assert.Equal(Long(Dev, OnboardIndex, 0xBB), _transport.Written[^1]);
    }

    [Fact]
    public void SetCurrentDpiIndex_is_function_12()
    {
        var ob = new OnboardProfilesFeature(_device);

        ob.SetCurrentDpiIndex(3);

        Assert.Equal(Long(Dev, OnboardIndex, 0xCB, 3), _transport.Written[^1]);
    }

    [Fact]
    public void OnboardProfiles_feature_index_is_looked_up_once()
    {
        var ob = new OnboardProfilesFeature(_device);

        ob.GetOnboardMode();
        ob.GetOnboardMode();

        Assert.Single(_transport.Written, w => w[2] == 0x00);
    }

    [Fact]
    public void OnboardProfiles_throws_NotSupported_when_feature_missing()
    {
        _sim.Features.Remove(0x8100);
        var ob = new OnboardProfilesFeature(_device);

        Assert.Throws<NotSupportedException>(() => ob.GetOnboardMode());
    }

    [Fact]
    public void OnboardProfiles_has_no_memory_write_api()
    {
        string[] names = typeof(OnboardProfilesFeature).GetMethods().Select(m => m.Name).ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Write", StringComparison.OrdinalIgnoreCase));
    }
}
