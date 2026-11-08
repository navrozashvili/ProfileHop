using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.Storage.FileSystem;

namespace ProfileHop.Core.Hid;

/// <summary>
/// Overlapped read/write pipe over every HID++ collection of one device. One background reader thread per
/// opened collection feeds <see cref="Reports"/>; writes go to the long collection (usage 2) when present.
/// </summary>
internal sealed unsafe class HidChannel : IHidTransport
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const int ErrorIoPending = 997;
    private const uint WriteTimeoutMs = 1000;

    private readonly OpenCollection[] _collections;
    private readonly OpenCollection _writeTarget;
    private readonly Channel<byte[]> _reports = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly Lock _writeLock = new();
    private readonly HANDLE _writeEvent;
    private int _runningReaders;
    private int _disposed;

    public HidChannel(HidDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var opened = new List<OpenCollection>();
        var errors = new List<string>();
        foreach (HidCollection info in device.Collections)
        {
            SafeFileHandle handle = PInvoke.CreateFile(
                info.Path,
                GenericRead | GenericWrite,
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                null,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED,
                null);
            if (handle.IsInvalid)
            {
                errors.Add($"{info.Path}: error {Marshal.GetLastPInvokeError()}");
                handle.Dispose();
                continue;
            }

            opened.Add(new OpenCollection(info, handle));
        }

        int target = HidReports.ChooseWriteTarget(opened.Select(c => c.Info).ToList());
        _writeEvent = target < 0 ? HANDLE.Null : CreateManualResetEvent();
        if (target < 0 || _writeEvent.IsNull)
        {
            foreach (OpenCollection c in opened)
            {
                c.Handle.Dispose();
            }

            string reason = target < 0 ? "no writable HID++ collection could be opened" : "CreateEvent failed";
            throw new IOException($"Cannot open {device.DeviceKey}: {reason}. {string.Join("; ", errors)}");
        }

        _collections = [.. opened];
        _writeTarget = _collections[target];

        foreach (OpenCollection c in _collections)
        {
            if (c.Info.InputReportLength <= 0)
            {
                continue;
            }

            Interlocked.Increment(ref _runningReaders);
            c.Reader = new Thread(() => ReadLoop(c))
            {
                IsBackground = true,
                Name = $"HID reader {c.Info.Path}",
            };
            c.Reader.Start();
        }
    }

    public ChannelReader<byte[]> Reports => _reports.Reader;

    public void Write(ReadOnlySpan<byte> report)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        byte[] buffer = HidReports.PadToLength(report, _writeTarget.Info.OutputReportLength);

        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (TryWriteFile(buffer, out int error))
            {
                return;
            }

            if (PInvoke.HidD_SetOutputReport(_writeTarget.Handle, buffer))
            {
                return;
            }

            throw new IOException(
                $"Write to {_writeTarget.Info.Path} failed: WriteFile error {error}, HidD_SetOutputReport also failed.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (OpenCollection c in _collections)
        {
            PInvoke.CancelIoEx(c.Handle, null);
        }

        foreach (OpenCollection c in _collections)
        {
            // A reader may have issued a new ReadFile between our flag and the cancel; keep cancelling until it exits.
            while (c.Reader is not null && !c.Reader.Join(100))
            {
                PInvoke.CancelIoEx(c.Handle, null);
            }
        }

        lock (_writeLock)
        {
            foreach (OpenCollection c in _collections)
            {
                c.Handle.Dispose(); // CloseHandle
            }

            PInvoke.CloseHandle(_writeEvent);
        }

        _reports.Writer.TryComplete();
    }

    private bool TryWriteFile(byte[] buffer, out int error)
    {
        var handle = (HANDLE)_writeTarget.Handle.DangerousGetHandle();
        NativeOverlapped overlapped = default;
        overlapped.EventHandle = _writeEvent;
        error = 0;

        fixed (byte* p = buffer)
        {
            if (!PInvoke.WriteFile(handle, p, (uint)buffer.Length, null, &overlapped))
            {
                error = Marshal.GetLastPInvokeError();
                if (error != ErrorIoPending)
                {
                    return false;
                }
            }

            if (PInvoke.WaitForSingleObject(_writeEvent, WriteTimeoutMs) != WAIT_EVENT.WAIT_OBJECT_0)
            {
                PInvoke.CancelIoEx(handle, &overlapped);
            }

            // Always wait for completion: the overlapped struct and buffer live in this frame.
            uint written;
            if (!PInvoke.GetOverlappedResult(handle, &overlapped, &written, true))
            {
                error = Marshal.GetLastPInvokeError();
                return false;
            }

            return written == buffer.Length;
        }
    }

    private void ReadLoop(OpenCollection collection)
    {
        var handle = (HANDLE)collection.Handle.DangerousGetHandle();
        int length = collection.Info.InputReportLength;
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)length);
        var overlapped = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
        HANDLE readEvent = CreateManualResetEvent();
        try
        {
            while (!readEvent.IsNull && Volatile.Read(ref _disposed) == 0)
            {
                *overlapped = default;
                overlapped->EventHandle = readEvent;
                if (!PInvoke.ReadFile(handle, buffer, (uint)length, null, overlapped)
                    && Marshal.GetLastPInvokeError() != ErrorIoPending)
                {
                    break;
                }

                uint read;
                if (!PInvoke.GetOverlappedResult(handle, overlapped, &read, true))
                {
                    break; // cancelled by Dispose, or the device went away
                }

                if (read > 0)
                {
                    _reports.Writer.TryWrite(new ReadOnlySpan<byte>(buffer, (int)read).ToArray());
                }
            }
        }
        finally
        {
            if (!readEvent.IsNull)
            {
                PInvoke.CloseHandle(readEvent);
            }

            NativeMemory.Free(overlapped);
            NativeMemory.Free(buffer);

            if (Interlocked.Decrement(ref _runningReaders) == 0 && Volatile.Read(ref _disposed) == 0)
            {
                _reports.Writer.TryComplete(new IOException("All HID readers stopped; the device may have been removed."));
            }
        }
    }

    private static HANDLE CreateManualResetEvent() =>
        PInvoke.CreateEvent((SECURITY_ATTRIBUTES*)null, true, false, default(PCWSTR));

    private sealed class OpenCollection(HidCollection info, SafeFileHandle handle)
    {
        public HidCollection Info { get; } = info;

        public SafeFileHandle Handle { get; } = handle;

        public Thread? Reader { get; set; }
    }
}
