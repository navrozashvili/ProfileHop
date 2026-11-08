using ProfileHop.Core.Hidpp;
using ProfileHop.Core.Tests.Hidpp.Fakes;
using static ProfileHop.Core.Tests.Hidpp.Fakes.Frames;

namespace ProfileHop.Core.Tests.Hidpp;

public class HidppClientTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(60);

    private static (FakeHidTransport, HidppClient) Create(Func<byte[], IEnumerable<byte[]>>? responder = null)
    {
        var t = new FakeHidTransport { Responder = responder };
        return (t, new HidppClient(t, ShortTimeout));
    }

    [Fact]
    public void Default_timeout_is_500ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), HidppClient.DefaultTimeout);
    }

    [Fact]
    public void Request_writes_long_frame_and_returns_reply_params()
    {
        var (t, c) = Create(_ => [Long(0x01, 0x09, 0x4B, 0x00, 0x02)]);
        using var _ = c;

        byte[] reply = c.Request(0x01, 0x09, 4, []);

        Assert.Equal(Long(0x01, 0x09, 0x4B), t.Written.Single());
        Assert.Equal(16, reply.Length);
        Assert.Equal(new byte[] { 0x00, 0x02 }, reply[..2]);
    }

    [Fact]
    public void Request_sends_params()
    {
        var (t, c) = Create(_ => [Long(0x01, 0x09, 0x3B)]);
        using var _ = c;

        c.Request(0x01, 0x09, 3, [0x00, 0x02]);

        Assert.Equal(Long(0x01, 0x09, 0x3B, 0x00, 0x02), t.Written.Single());
    }

    [Fact]
    public void Request_accepts_short_reply()
    {
        var (_, c) = Create(_ => [Short(0xFF, 0x00, 0x1B, 0x04, 0x02, 0x5A)]);
        using var __ = c;

        byte[] reply = c.Request(0xFF, 0x00, 1, [0, 0, 0x5A]);

        Assert.Equal(new byte[] { 0x04, 0x02, 0x5A }, reply[..3]);
    }

    [Fact]
    public void Request_ignores_unrelated_reports_until_the_match()
    {
        var (_, c) = Create(_ =>
        [
            [0x20, 0x01, 0x41, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // DJ report
            Long(0x02, 0x09, 0x4B, 0xAA), // other device index
            Long(0x01, 0x08, 0x4B, 0xAA), // other feature index
            Long(0x01, 0x09, 0x3B, 0xAA), // other function
            Long(0x01, 0x09, 0x4A, 0xAA), // other swId
            Error20(0x01, 0x08, 0x4B, 0x02), // error for another feature
            Error20(0x01, 0x09, 0x3B, 0x02), // error for another function
            Error10(0x02, 0x09, 0x4B, 0x09), // 1.0 error for another device
            Long(0x01, 0x09, 0x4B, 0x07),
        ]);
        using var _ = c;

        byte[] reply = c.Request(0x01, 0x09, 4, []);

        Assert.Equal(0x07, reply[0]);
    }

    [Theory]
    [InlineData(0x02, HidppError.InvalidArgument)]
    [InlineData(0x08, HidppError.Busy)]
    [InlineData(0x09, HidppError.Unsupported)]
    public void Hidpp20_error_reply_throws_typed_exception_without_retry(byte code, HidppError expected)
    {
        var (t, c) = Create(_ => [Error20(0x01, 0x09, 0x3B, code)]);
        using var _ = c;

        var ex = Assert.Throws<HidppException>(() => c.Request(0x01, 0x09, 3, [0x00, 0x09]));

        Assert.Equal(expected, ex.Error);
        Assert.Equal(0x01, ex.DeviceIndex);
        Assert.Equal(0x09, ex.FeatureIndex);
        Assert.Equal(3, ex.Function);
        Assert.Single(t.Written);
    }

    [Fact]
    public void Hidpp10_error_reply_throws_device_unreachable()
    {
        var (t, c) = Create(_ => [Error10(0x03, 0x00, 0x1B, 0x09)]);
        using var _ = c;

        var ex = Assert.Throws<DeviceUnreachableException>(() => c.Request(0x03, 0x00, 1, [0, 0, 0x5A]));

        Assert.Equal(0x03, ex.DeviceIndex);
        Assert.Equal(0x09, ex.Hidpp10Error);
        Assert.Single(t.Written);
    }

    [Fact]
    public void Hidpp10_error_in_a_long_report_is_also_matched()
    {
        var (_, c) = Create(_ => [Long(0x03, 0x8F, 0x00, 0x1B, 0x08)]);
        using var __ = c;

        var ex = Assert.Throws<DeviceUnreachableException>(() => c.Request(0x03, 0x00, 1, []));
        Assert.Equal(0x08, ex.Hidpp10Error);
    }

    [Fact]
    public void Timeout_retries_once_and_succeeds_on_second_attempt()
    {
        int writes = 0;
        var (t, c) = Create(_ => ++writes == 1 ? [] : [Long(0x01, 0x09, 0x4B, 0x02)]);
        using var _ = c;

        byte[] reply = c.Request(0x01, 0x09, 4, []);

        Assert.Equal(0x02, reply[0]);
        Assert.Equal(2, t.Written.Count);
        Assert.Equal(t.Written[0], t.Written[1]);
    }

    [Fact]
    public void Late_reply_to_a_timed_out_attempt_does_not_complete_the_next_request()
    {
        // Request 1: attempt 1 gets no reply in time, the retry is answered. The late reply to attempt 1 then
        // arrives while request 2 (same feature/function, e.g. the next memoryRead chunk) waits.
        var writes = new List<byte[]>();
        var t = new FakeHidTransport { EchoSwId = false };
        t.Responder = w =>
        {
            writes.Add(w);
            static byte[] Reply(byte[] req, byte data) => Long(req[1], req[2], req[3], data);
            return writes.Count switch
            {
                1 => [],
                2 => [Reply(writes[1], 0x01)],
                _ => [Reply(writes[0], 0x01), Reply(writes[2], 0x02)],
            };
        };
        using var c = new HidppClient(t, ShortTimeout);

        Assert.Equal(0x01, c.Request(0x01, 0x0E, 5, [0x00, 0x00, 0x00, 0x00])[0]);
        Assert.Equal(0x02, c.Request(0x01, 0x0E, 5, [0x00, 0x00, 0x00, 0x10])[0]);
    }

    [Fact]
    public void SwId_rotates_per_send_and_is_never_zero()
    {
        var (t, c) = Create(w => [Long(w[1], w[2], w[3])]);
        using var _ = c;

        for (int i = 0; i < 20; i++)
            c.Request(0x01, 0x09, 4, []);

        byte[] swIds = t.RawWritten.Select(w => (byte)(w[3] & 0x0F)).ToArray();
        Assert.All(swIds, s => Assert.InRange(s, (byte)1, (byte)0xF));
        for (int i = 1; i < swIds.Length; i++)
            Assert.NotEqual(swIds[i - 1], swIds[i]);
    }

    [Fact]
    public void Retry_uses_a_new_swId()
    {
        int writes = 0;
        var (t, c) = Create(w => ++writes == 1 ? [] : [Long(w[1], w[2], w[3], 0x02)]);
        using var _ = c;

        c.Request(0x01, 0x09, 4, []);

        Assert.NotEqual(t.RawWritten[0][3], t.RawWritten[1][3]);
        Assert.Equal(t.RawWritten[0][3] & 0xF0, t.RawWritten[1][3] & 0xF0);
    }

    [Fact]
    public void Two_timeouts_throw_TimeoutException()
    {
        var (t, c) = Create();
        using var _ = c;

        Assert.Throws<TimeoutException>(() => c.Request(0x01, 0x09, 4, []));
        Assert.Equal(2, t.Written.Count);
    }

    [Fact]
    public async Task Timeout_uses_injected_time_provider()
    {
        var t = new FakeHidTransport();
        var clock = new ManualTimeProvider();
        using var c = new HidppClient(t, TimeSpan.FromHours(1), clock);

        var task = Task.Run(() => c.Request(0x01, 0x09, 4, []));
        SpinWait.SpinUntil(() => t.Written.Count == 1, 2000);
        clock.Advance(TimeSpan.FromHours(1));
        SpinWait.SpinUntil(() => t.Written.Count == 2, 2000);
        clock.Advance(TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Notification_with_swid_zero_is_raised()
    {
        var (t, c) = Create();
        using var _ = c;
        var received = new TaskCompletionSource<HidppMessage>();
        c.Notification += m => received.TrySetResult(m);

        t.Push(Long(0x01, 0x09, 0x00, 0x00, 0x02));

        HidppMessage n = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0x01, n.DeviceIndex);
        Assert.Equal(0x09, n.FeatureIndex);
        Assert.Equal(0, n.Function);
        Assert.Equal(0x02, n.Params[1]);
    }

    [Fact]
    public async Task Notification_during_a_request_is_raised_and_not_taken_as_reply()
    {
        var (_, c) = Create(_ => [Long(0x01, 0x09, 0x00, 0x00, 0x03), Long(0x01, 0x09, 0x0B, 0x01)]);
        using var __ = c;
        var received = new TaskCompletionSource<HidppMessage>();
        c.Notification += m => received.TrySetResult(m);

        byte[] reply = c.Request(0x01, 0x09, 0, []);

        Assert.Equal(0x01, reply[0]);
        HidppMessage n = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0x03, n.Params[1]);
    }

    [Fact]
    public void Replies_with_our_swid_and_no_pending_request_are_not_notifications()
    {
        var (t, c) = Create();
        using var _ = c;
        int count = 0;
        c.Notification += _ => Interlocked.Increment(ref count);

        t.Push(Long(0x01, 0x09, 0x4B, 0x02));
        t.Push(Error20(0x01, 0x09, 0x40, 0x02));
        t.Push(Long(0x01, 0x09, 0x00, 0x01)); // sentinel notification
        SpinWait.SpinUntil(() => Volatile.Read(ref count) > 0, 2000);
        Thread.Sleep(20);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Closed_transport_fails_pending_request_without_waiting_for_timeout()
    {
        var t = new FakeHidTransport();
        using var c = new HidppClient(t, TimeSpan.FromSeconds(30));
        t.Responder = _ => { t.Close(); return []; };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<IOException>(() => c.Request(0x01, 0x09, 4, []));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Request_after_transport_closed_throws()
    {
        var (t, c) = Create();
        using var _ = c;
        t.Close();
        SpinWait.SpinUntil(() => c.IsClosed, 2000);

        Assert.Throws<IOException>(() => c.Request(0x01, 0x09, 4, []));
    }

    [Fact]
    public void Write_failure_propagates()
    {
        var (t, c) = Create();
        using var _ = c;
        t.WriteException = new IOException("gone");

        var ex = Assert.Throws<IOException>(() => c.Request(0x01, 0x09, 4, []));
        Assert.Equal("gone", ex.Message);
    }
}
