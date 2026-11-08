namespace ProfileHop.Core.Hidpp;

/// <summary>
/// One decoded HID++ frame. Layout (docs/protocol-verification.md): [0] report ID, [1] device index,
/// [2] feature index, [3] (function &lt;&lt; 4) | swId, [4..] params. Short reports (0x10) carry 3 param bytes,
/// long reports (0x11) carry 16; <see cref="Params"/> is always 16 bytes, zero-padded for short reports.
/// </summary>
public readonly struct HidppMessage
{
    public const byte ShortReportId = 0x10;
    public const byte LongReportId = 0x11;
    public const int ShortLength = 7;
    public const int LongLength = 20;
    public const int MaxParams = 16;

    /// <summary>Our fixed, non-zero software ID (same value Solaar uses). swId 0 marks notifications.</summary>
    public const byte SoftwareId = 0x0B;

    /// <summary>Byte 2 of a HID++ 2.0 error reply.</summary>
    public const byte Hidpp20ErrorMarker = 0xFF;

    /// <summary>Byte 2 of a HID++ 1.0 error reply (e.g. receiver: device not reachable).</summary>
    public const byte Hidpp10ErrorMarker = 0x8F;

    private HidppMessage(bool isLong, byte deviceIndex, byte featureIndex, byte functionByte, byte[] parameters)
    {
        IsLong = isLong;
        DeviceIndex = deviceIndex;
        FeatureIndex = featureIndex;
        FunctionByte = functionByte;
        Params = parameters;
    }

    public bool IsLong { get; }
    public byte DeviceIndex { get; }
    public byte FeatureIndex { get; }

    /// <summary>Raw byte 3.</summary>
    public byte FunctionByte { get; }

    public byte Function => (byte)(FunctionByte >> 4);
    public byte SwId => (byte)(FunctionByte & 0x0F);

    /// <summary>16 parameter bytes (short reports zero-padded).</summary>
    public byte[] Params { get; }

    public bool IsHidpp20Error => FeatureIndex == Hidpp20ErrorMarker;
    public bool IsHidpp10Error => FeatureIndex == Hidpp10ErrorMarker;

    /// <summary>Unsolicited event: swId 0 and not an error frame.</summary>
    public bool IsNotification => SwId == 0 && !IsHidpp20Error && !IsHidpp10Error;

    /// <summary>Builds a 20-byte long report.</summary>
    public static byte[] EncodeLong(byte deviceIndex, byte featureIndex, byte function, byte swId, ReadOnlySpan<byte> parameters)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(function, (byte)0x0F);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(swId, (byte)0x0F);
        if (parameters.Length > MaxParams)
            throw new ArgumentException($"At most {MaxParams} parameter bytes fit in a long report.", nameof(parameters));

        var frame = new byte[LongLength];
        frame[0] = LongReportId;
        frame[1] = deviceIndex;
        frame[2] = featureIndex;
        frame[3] = (byte)((function << 4) | swId);
        parameters.CopyTo(frame.AsSpan(4));
        return frame;
    }

    /// <summary>Decodes a short or long HID++ report; false for other report IDs or truncated frames.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> report, out HidppMessage message)
    {
        message = default;
        if (report.Length < 4)
            return false;

        bool isLong;
        int paramCount;
        switch (report[0])
        {
            case ShortReportId when report.Length >= ShortLength:
                isLong = false;
                paramCount = ShortLength - 4;
                break;
            case LongReportId when report.Length >= LongLength:
                isLong = true;
                paramCount = LongLength - 4;
                break;
            default:
                return false;
        }

        var parameters = new byte[MaxParams];
        report.Slice(4, paramCount).CopyTo(parameters);
        message = new HidppMessage(isLong, report[1], report[2], report[3], parameters);
        return true;
    }

    public override string ToString() =>
        $"{(IsLong ? "L" : "S")} dev={DeviceIndex:X2} feat={FeatureIndex:X2} fn={FunctionByte:X2} {Convert.ToHexString(Params)}";
}
