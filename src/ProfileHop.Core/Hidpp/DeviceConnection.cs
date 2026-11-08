namespace ProfileHop.Core.Hidpp;

/// <summary>
/// The receiver's HID++ 1.0 "device connection" notification (Solaar <c>Notification.DJ_PAIRING = 0x41</c>), sent when
/// a paired device's wireless link comes up or goes down: short report
/// <c>[0x10, device index, 0x41, protocol, flags, wpid low, wpid high]</c>. Link established = flags bit 0x40 clear
/// (protocol 0x02, old 27 MHz: always established); protocol 0 is unknown and ignored. See docs/protocol-verification.md.
/// </summary>
public static class DeviceConnection
{
    public const byte SubId = 0x41;
    private const byte LinkNotEstablished = 0x40;
    private const byte Protocol27MHz = 0x02;

    /// <summary>True when <paramref name="msg"/> is a connection notification; <paramref name="linkEstablished"/> = device online.</summary>
    public static bool TryDecode(in HidppMessage msg, out bool linkEstablished)
    {
        linkEstablished = false;
        byte protocol = msg.FunctionByte; // byte 3
        if (msg.IsLong || msg.FeatureIndex != SubId || protocol == 0)
            return false;
        byte flags = (byte)(msg.Params[0] & 0xF0); // byte 4
        linkEstablished = protocol == Protocol27MHz || (flags & LinkNotEstablished) == 0;
        return true;
    }
}
