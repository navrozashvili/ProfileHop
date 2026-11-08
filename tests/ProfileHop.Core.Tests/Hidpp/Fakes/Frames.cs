namespace ProfileHop.Core.Tests.Hidpp.Fakes;

/// <summary>Byte-exact frame builders written straight from docs/protocol-verification.md (not via HidppMessage).</summary>
internal static class Frames
{
    public static byte[] Long(byte dev, byte feat, byte fnByte, params byte[] p)
    {
        var f = new byte[20];
        f[0] = 0x11;
        f[1] = dev;
        f[2] = feat;
        f[3] = fnByte;
        p.CopyTo(f, 4);
        return f;
    }

    public static byte[] Short(byte dev, byte feat, byte fnByte, params byte[] p)
    {
        var f = new byte[7];
        f[0] = 0x10;
        f[1] = dev;
        f[2] = feat;
        f[3] = fnByte;
        p.CopyTo(f, 4);
        return f;
    }

    /// <summary>HID++ 2.0 error: [0x11, dev, 0xFF, orig feature index, orig byte 3, code].</summary>
    public static byte[] Error20(byte dev, byte feat, byte fnByte, byte code) => Long(dev, 0xFF, feat, fnByte, code);

    /// <summary>HID++ 1.0 error: [0x10, dev, 0x8F, orig feature index, orig byte 3, code, 0].</summary>
    public static byte[] Error10(byte dev, byte feat, byte fnByte, byte code) => Short(dev, 0x8F, feat, fnByte, code);

    /// <summary>Byte 3 for one of our requests: function in the high nibble, swId 0x0B.</summary>
    public static byte Fn(int function) => (byte)((function << 4) | 0x0B);
}
