using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.HumanInterfaceDevice;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace ProfileHop.Core.Hid;

/// <summary>
/// Lists Logitech HID++ collections (VID 0x046D, usage page 0xFF00, usage 1 or 2) and groups them per device.
/// Opens each path with access 0 (query only); never reads or writes reports.
/// </summary>
internal static unsafe class HidDeviceFinder
{
    public const ushort LogitechVendorId = 0x046D;

    public static IReadOnlyList<HidDevice> FindDevices() => HidPaths.Group(EnumerateCollections());

    public static List<HidCollection> EnumerateCollections()
    {
        PInvoke.HidD_GetHidGuid(out Guid hidGuid);
        using SetupDiDestroyDeviceInfoListSafeHandle set = PInvoke.SetupDiGetClassDevs(
            hidGuid,
            null,
            HWND.Null,
            SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_DEVICEINTERFACE);
        if (set.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetupDiGetClassDevs failed.");
        }

        var result = new List<HidCollection>();
        var interfaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DATA) };
        for (uint i = 0; PInvoke.SetupDiEnumDeviceInterfaces(set, null, in hidGuid, i, ref interfaceData); i++)
        {
            string? path = GetDevicePath(set, interfaceData);
            if (path is not null && TryQuery(path) is { } collection)
            {
                result.Add(collection);
            }
        }

        return result;
    }

    private static string? GetDevicePath(SafeHandle set, in SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
        PInvoke.SetupDiGetDeviceInterfaceDetail(set, in interfaceData, default, out uint required, ref devInfo);
        if (required < sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W))
        {
            return null;
        }

        byte[] buffer = new byte[required];
        fixed (byte* p = buffer)
        {
            var detail = (SP_DEVICE_INTERFACE_DETAIL_DATA_W*)p;

            // Fixed header size (8 on x64), not the full buffer size: SetupAPI checks this exact value.
            detail->cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
            if (!PInvoke.SetupDiGetDeviceInterfaceDetail(set, in interfaceData, buffer, out _, ref devInfo))
            {
                return null;
            }

            return new string((char*)&detail->DevicePath);
        }
    }

    private static HidCollection? TryQuery(string path)
    {
        using SafeFileHandle handle = PInvoke.CreateFile(
            path,
            0,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            0,
            null);
        if (handle.IsInvalid)
        {
            return null;
        }

        var raw = (HANDLE)handle.DangerousGetHandle();
        var attributes = new HIDD_ATTRIBUTES { Size = (uint)sizeof(HIDD_ATTRIBUTES) };
        if (!PInvoke.HidD_GetAttributes(raw, &attributes) || attributes.VendorID != LogitechVendorId)
        {
            return null;
        }

        if (!PInvoke.HidD_GetPreparsedData(handle, out HidD_FreePreparsedDataSafeHandle preparsed))
        {
            return null;
        }

        HIDP_CAPS caps;
        using (preparsed)
        {
            if (PInvoke.HidP_GetCaps(new PHIDP_PREPARSED_DATA(preparsed.DangerousGetHandle()), out caps)
                != NTSTATUS.HIDP_STATUS_SUCCESS)
            {
                return null;
            }
        }

        if (HidReports.Classify(caps.UsagePage, caps.Usage) == HidReportKind.Ignore)
        {
            return null;
        }

        return new HidCollection(
            path,
            attributes.VendorID,
            attributes.ProductID,
            GetProduct(handle),
            caps.UsagePage,
            caps.Usage,
            caps.InputReportByteLength,
            caps.OutputReportByteLength);
    }

    private static string? GetProduct(SafeHandle handle)
    {
        // USB string descriptors are at most 126 UTF-16 chars; 256 chars leaves room for the terminator.
        Span<byte> buffer = stackalloc byte[512];
        buffer.Clear();
        if (!PInvoke.HidD_GetProductString(handle, buffer))
        {
            return null;
        }

        ReadOnlySpan<char> chars = MemoryMarshal.Cast<byte, char>(buffer);
        int end = chars.IndexOf('\0');
        string product = (end < 0 ? chars : chars[..end]).ToString().Trim();
        return product.Length == 0 ? null : product;
    }
}
