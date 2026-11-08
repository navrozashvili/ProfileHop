namespace ProfileHop.Core.Hidpp;

/// <summary>HID++ 2.0 error codes (libratbag hidpp-generic.h, Solaar hidpp20_constants.ErrorCode).</summary>
public enum HidppError : byte
{
    NoError = 0,
    Unknown = 1,
    InvalidArgument = 2,
    OutOfRange = 3,
    HwError = 4,
    LogitechInternal = 5,
    InvalidFeatureIndex = 6,
    InvalidFunctionId = 7,
    Busy = 8,
    Unsupported = 9,
}

/// <summary>The device answered a request with a HID++ 2.0 error frame (byte 2 = 0xFF).</summary>
public sealed class HidppException : Exception
{
    public HidppException(HidppError error, byte deviceIndex, byte featureIndex, byte function)
        : base($"HID++ error {Describe(error)} from device 0x{deviceIndex:X2}, feature index 0x{featureIndex:X2}, function {function}")
    {
        Error = error;
        DeviceIndex = deviceIndex;
        FeatureIndex = featureIndex;
        Function = function;
    }

    public HidppError Error { get; }
    public byte DeviceIndex { get; }
    public byte FeatureIndex { get; }
    public byte Function { get; }

    private static string Describe(HidppError error) =>
        Enum.IsDefined(error) ? $"{error} (0x{(byte)error:X2})" : $"0x{(byte)error:X2}";
}

/// <summary>
/// HID++ 1.0 error frame (byte 2 = 0x8F), typically from the receiver: nothing reachable at that device index
/// (asleep, off, not paired) or the target only speaks HID++ 1.0.
/// </summary>
public sealed class DeviceUnreachableException : Exception
{
    public DeviceUnreachableException(byte deviceIndex, byte hidpp10Error)
        : base($"Device 0x{deviceIndex:X2} not reachable (HID++ 1.0 error 0x{hidpp10Error:X2})")
    {
        DeviceIndex = deviceIndex;
        Hidpp10Error = hidpp10Error;
    }

    public byte DeviceIndex { get; }

    /// <summary>1 invalid sub-ID (HID++ 1.0 device), 4 connect failed, 8 unknown device, 9 resource error, ...</summary>
    public byte Hidpp10Error { get; }
}

/// <summary>A reply arrived but did not make sense (bad ping echo, confirmation mismatch, malformed data).</summary>
public sealed class HidppProtocolException(string message) : Exception(message);
