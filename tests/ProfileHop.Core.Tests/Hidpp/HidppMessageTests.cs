using ProfileHop.Core.Hidpp;

namespace ProfileHop.Core.Tests.Hidpp;

public class HidppMessageTests
{
    [Fact]
    public void SoftwareId_is_0x0B()
    {
        Assert.Equal(0x0B, HidppMessage.SoftwareId);
    }

    [Fact]
    public void EncodeLong_builds_20_byte_frame_with_function_and_swid_in_byte_3()
    {
        byte[] frame = HidppMessage.EncodeLong(0x01, 0x09, 3, HidppMessage.SoftwareId, [0x00, 0x02]);

        Assert.Equal(
            new byte[] { 0x11, 0x01, 0x09, 0x3B, 0x00, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            frame);
    }

    [Fact]
    public void EncodeLong_with_no_params_zero_pads()
    {
        byte[] frame = HidppMessage.EncodeLong(0xFF, 0x00, 1, 0x0B, []);

        Assert.Equal(20, frame.Length);
        Assert.Equal(new byte[] { 0x11, 0xFF, 0x00, 0x1B }, frame[..4]);
        Assert.All(frame[4..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void EncodeLong_accepts_16_params()
    {
        byte[] p = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();

        byte[] frame = HidppMessage.EncodeLong(0x01, 0x02, 0xC, 0x0B, p);

        Assert.Equal(0xCB, frame[3]);
        Assert.Equal(p, frame[4..]);
    }

    [Fact]
    public void EncodeLong_rejects_more_than_16_params()
    {
        Assert.Throws<ArgumentException>(() => HidppMessage.EncodeLong(1, 2, 0, 0x0B, new byte[17]));
    }

    [Theory]
    [InlineData(16, 0x0B)]
    [InlineData(0, 16)]
    public void EncodeLong_rejects_out_of_range_nibbles(int function, int swId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => HidppMessage.EncodeLong(1, 2, (byte)function, (byte)swId, []));
    }

    [Fact]
    public void TryDecode_long_report()
    {
        byte[] frame = [0x11, 0x01, 0x09, 0x4B, 0x00, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x7F];

        Assert.True(HidppMessage.TryDecode(frame, out HidppMessage m));

        Assert.True(m.IsLong);
        Assert.Equal(0x01, m.DeviceIndex);
        Assert.Equal(0x09, m.FeatureIndex);
        Assert.Equal(4, m.Function);
        Assert.Equal(0x0B, m.SwId);
        Assert.Equal(0x4B, m.FunctionByte);
        Assert.Equal(16, m.Params.Length);
        Assert.Equal(0x02, m.Params[1]);
        Assert.Equal(0x7F, m.Params[15]);
    }

    [Fact]
    public void TryDecode_short_report_pads_params_to_16_bytes()
    {
        byte[] frame = [0x10, 0xFF, 0x00, 0x1B, 0x04, 0x02, 0x5A];

        Assert.True(HidppMessage.TryDecode(frame, out HidppMessage m));

        Assert.False(m.IsLong);
        Assert.Equal(0xFF, m.DeviceIndex);
        Assert.Equal(0x00, m.FeatureIndex);
        Assert.Equal(1, m.Function);
        Assert.Equal(0x0B, m.SwId);
        Assert.Equal(16, m.Params.Length);
        Assert.Equal(new byte[] { 0x04, 0x02, 0x5A, 0 }, m.Params[..4]);
    }

    [Fact]
    public void TryDecode_accepts_frames_longer_than_the_report_length()
    {
        byte[] frame = new byte[32];
        frame[0] = 0x10;
        frame[1] = 0x01;

        Assert.True(HidppMessage.TryDecode(frame, out HidppMessage m));
        Assert.Equal(0x01, m.DeviceIndex);
    }

    [Theory]
    [InlineData(new byte[] { 0x20, 0x01, 0x02, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })] // DJ report
    [InlineData(new byte[] { 0x10, 0x01, 0x02, 0x03, 0x00, 0x00 })] // short too short
    [InlineData(new byte[] { 0x11, 0x01, 0x02, 0x03, 0x00, 0x00, 0x00 })] // long too short
    [InlineData(new byte[] { })]
    public void TryDecode_rejects_unknown_or_truncated_reports(byte[] frame)
    {
        Assert.False(HidppMessage.TryDecode(frame, out _));
    }

    [Fact]
    public void IsNotification_when_swid_is_zero()
    {
        HidppMessage.TryDecode([0x11, 0x01, 0x09, 0x00, 0x00, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], out var n);
        HidppMessage.TryDecode([0x11, 0x01, 0x09, 0x0B, 0x00, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], out var r);

        Assert.True(n.IsNotification);
        Assert.False(r.IsNotification);
    }

    [Fact]
    public void Error_frames_are_not_notifications_even_with_low_nibble_zero()
    {
        // HID++ 2.0 error for feature index 9, function byte 0x30 (swId 0 is never sent by us, but be safe).
        HidppMessage.TryDecode([0x11, 0x01, 0xFF, 0x09, 0x30, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], out var e20);
        HidppMessage.TryDecode([0x10, 0x01, 0x8F, 0x00, 0x1B, 0x09, 0x00], out var e10);

        Assert.True(e20.IsHidpp20Error);
        Assert.False(e20.IsNotification);
        Assert.True(e10.IsHidpp10Error);
        Assert.False(e10.IsNotification);
    }

    // ---- receiver device connection notification (HID++ 1.0 sub-id 0x41, docs/protocol-verification.md) ----

    private static HidppMessage Decode(params byte[] frame)
    {
        Assert.True(HidppMessage.TryDecode(frame, out HidppMessage m));
        return m;
    }

    [Theory]
    [InlineData(0x04, 0x00, true)]  // protocol 0x04, link bit clear = established
    [InlineData(0x04, 0x40, false)] // flags bit 0x40 = link not established
    [InlineData(0x0C, 0x20, true)]  // the encrypted bit alone does not matter
    [InlineData(0x10, 0x60, false)] // Bolt: still the 0x40 bit
    [InlineData(0x02, 0x40, true)]  // old 27 MHz protocol: always established
    public void Connection_notification_decodes_link_state(byte protocol, byte flags, bool established)
    {
        HidppMessage m = Decode(0x10, 0x01, 0x41, protocol, flags, 0x8A, 0x40);

        Assert.True(DeviceConnection.TryDecode(m, out bool link));
        Assert.Equal(established, link);
    }

    [Fact]
    public void Connection_notification_needs_short_report_sub_id_0x41_and_a_protocol()
    {
        Assert.False(DeviceConnection.TryDecode(Decode(0x10, 0x01, 0x41, 0x00, 0x00, 0x8A, 0x40), out _)); // protocol 0: unknown
        Assert.False(DeviceConnection.TryDecode(Decode(0x10, 0x01, 0x40, 0x02, 0x00, 0x00, 0x00), out _)); // 0x40 = unpaired
        var longFrame = new byte[20];
        longFrame[0] = 0x11;
        longFrame[1] = 0x01;
        longFrame[2] = 0x41;
        longFrame[3] = 0x04;
        Assert.False(DeviceConnection.TryDecode(Decode(longFrame), out _)); // HID++ 2.0 frame at feature index 0x41
    }
}
