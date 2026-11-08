using ProfileHop.Core.Hid;
using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Hidpp;

/// <summary>
/// HID++ 2.0 request/response over an <see cref="IHidTransport"/>. One request in flight at a time; a background
/// pump reads every input report and either completes the pending request, raises <see cref="Notification"/>
/// (swId 0), or drops the report. Does not own the transport. With a log at Debug level every sent (TX) and received
/// (RX) report is hex-dumped.
/// </summary>
public sealed class HidppClient : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IHidTransport _transport;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _time;
    private readonly ILog? _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _requestLock = new();
    private readonly Lock _pendingLock = new();
    private readonly Task _pump;
    private Pending? _pending;
    private byte _swId; // last software ID sent; guarded by _requestLock
    private volatile bool _closed;

    public HidppClient(IHidTransport transport, TimeSpan? timeout = null, TimeProvider? timeProvider = null, ILog? log = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeout = timeout ?? DefaultTimeout;
        _time = timeProvider ?? TimeProvider.System;
        _log = log;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>
    /// Raised on the pump thread for every swId-0 frame and every HID++ 1.0 receiver notification (short report,
    /// sub-id 0x40-0x7F, e.g. <see cref="DeviceConnection"/>). Handlers must not call <see cref="Request"/> synchronously.
    /// </summary>
    public event Action<HidppMessage>? Notification;

    /// <summary>True once the transport's report stream has ended (device gone) or the client was disposed.</summary>
    public bool IsClosed => _closed;

    /// <summary>
    /// Sends a long report and waits for the matching reply (same device index, feature index and byte 3, i.e.
    /// function and this send's software ID). Times out after the configured timeout, retries once with a new
    /// software ID. Returns the 16 reply parameter bytes.
    /// </summary>
    /// <exception cref="HidppException">HID++ 2.0 error reply.</exception>
    /// <exception cref="DeviceUnreachableException">HID++ 1.0 error reply (0x8F).</exception>
    /// <exception cref="TimeoutException">No reply after the retry.</exception>
    /// <exception cref="IOException">Transport closed.</exception>
    public byte[] Request(byte deviceIndex, byte featureIndex, byte function, ReadOnlySpan<byte> parameters)
    {
        lock (_requestLock)
        {
            const int attempts = 2;
            for (int attempt = 1; ; attempt++)
            {
                ThrowIfClosed();
                // A fresh swId per send: a late reply to an earlier attempt or request carries an old swId and is
                // dropped instead of completing this one with the wrong data.
                byte[] frame = HidppMessage.EncodeLong(deviceIndex, featureIndex, function, NextSwId(), parameters);
                var pending = new Pending(deviceIndex, featureIndex, frame[3]);
                lock (_pendingLock) _pending = pending;
                try
                {
                    LogFrame("TX ", frame);
                    _transport.Write(frame);
                    return pending.Completion.Task.WaitAsync(_timeout, _time).GetAwaiter().GetResult().Params;
                }
                catch (TimeoutException) when (attempt < attempts)
                {
                    // retry
                }
                catch (TimeoutException)
                {
                    throw new TimeoutException(
                        $"No HID++ reply from device 0x{deviceIndex:X2}, feature index 0x{featureIndex:X2}, function {function} after {attempts} attempts");
                }
                finally
                {
                    lock (_pendingLock)
                    {
                        if (ReferenceEquals(_pending, pending))
                            _pending = null;
                    }
                }
            }
        }
    }

    /// <summary>Software IDs 0x1..0xF in turn; 0 is reserved for device notifications.</summary>
    private byte NextSwId()
    {
        _swId = (byte)(_swId % 0x0F + 1);
        return _swId;
    }

    private void ThrowIfClosed()
    {
        if (_closed)
            throw new IOException("HID++ transport is closed");
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (byte[] report in _transport.Reports.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                Dispatch(report);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Reader faulted: treat like a closed transport.
        }
        finally
        {
            _closed = true;
            Pending? pending;
            lock (_pendingLock) pending = _pending;
            pending?.Completion.TrySetException(new IOException("HID++ transport closed while waiting for a reply"));
        }
    }

    /// <summary>Hex-dumps a frame at Debug; formats nothing unless Debug is enabled.</summary>
    private void LogFrame(string direction, byte[] frame)
    {
        if (_log is { } log && log.IsEnabled(LogLevel.Debug))
            log.Write(LogLevel.Debug, direction + HexFormat.Frame(frame));
    }

    private void Dispatch(byte[] report)
    {
        LogFrame("RX ", report);
        if (!HidppMessage.TryDecode(report, out HidppMessage msg))
            return;

        lock (_pendingLock)
        {
            if (_pending is { } p && msg.DeviceIndex == p.DeviceIndex)
            {
                if (msg.FeatureIndex == p.FeatureIndex && msg.FunctionByte == p.FunctionByte)
                {
                    p.Completion.TrySetResult(msg);
                    return;
                }

                // Error frames: [dev, marker, orig feature index, orig byte 3, code]
                if ((msg.IsHidpp20Error || msg.IsHidpp10Error)
                    && msg.FunctionByte == p.FeatureIndex && msg.Params[0] == p.FunctionByte)
                {
                    byte code = msg.Params[1];
                    Exception ex = msg.IsHidpp20Error
                        ? new HidppException((HidppError)code, p.DeviceIndex, p.FeatureIndex, (byte)(p.FunctionByte >> 4))
                        : new DeviceUnreachableException(p.DeviceIndex, code);
                    p.Completion.TrySetException(ex);
                    return;
                }
            }
        }

        // HID++ 1.0 receiver notifications (short report, sub-id 0x40-0x7F) carry data, not a swId, in byte 3.
        // Our requests and their replies are long reports, so a short one in that range is never a reply.
        bool hidpp10Notification = !msg.IsLong && msg.FeatureIndex is >= 0x40 and <= 0x7F;
        if (msg.IsNotification || hidpp10Notification)
        {
            try
            {
                Notification?.Invoke(msg);
            }
            catch (Exception)
            {
                // A faulty handler must not kill the pump.
            }
        }
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested)
            return;
        _cts.Cancel();
        try
        {
            _pump.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        _closed = true;
        _cts.Dispose();
    }

    private sealed class Pending(byte deviceIndex, byte featureIndex, byte functionByte)
    {
        public byte DeviceIndex { get; } = deviceIndex;
        public byte FeatureIndex { get; } = featureIndex;
        public byte FunctionByte { get; } = functionByte;
        public TaskCompletionSource<HidppMessage> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
