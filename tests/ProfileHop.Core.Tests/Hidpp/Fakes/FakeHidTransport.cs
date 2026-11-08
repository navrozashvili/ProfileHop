using System.Threading.Channels;
using ProfileHop.Core.Hid;

namespace ProfileHop.Core.Tests.Hidpp.Fakes;

/// <summary>
/// Scripted transport: every written report is recorded and handed to <see cref="Responder"/>, whose returned
/// reports are queued as input. <see cref="Push"/> injects unsolicited input.
/// <para>
/// The client rotates its software ID per send; fixtures are written with swId 0xB (see <see cref="Frames.Fn"/>).
/// <see cref="Written"/> therefore shows every frame with swId 0xB, and with <see cref="EchoSwId"/> a reply whose
/// swId nibble is 0xB gets the request's swId, like a real device echoing it. <see cref="RawWritten"/> has the frames
/// as sent.
/// </para>
/// </summary>
internal sealed class FakeHidTransport : IHidTransport
{
    private readonly Channel<byte[]> _input = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte[]> _written = [];

    public Func<byte[], IEnumerable<byte[]>>? Responder { get; set; }

    /// <summary>Replace swId 0xB in replies with the request's swId (default on).</summary>
    public bool EchoSwId { get; set; } = true;

    public Exception? WriteException { get; set; }

    public bool IsDisposed { get; private set; }

    public ChannelReader<byte[]> Reports => _input.Reader;

    /// <summary>Written frames with the swId normalised to 0xB (compare against <see cref="Frames"/> fixtures).</summary>
    public IReadOnlyList<byte[]> Written
    {
        get
        {
            lock (_written) return _written.Select(Normalise).ToArray();
        }
    }

    /// <summary>Written frames exactly as sent.</summary>
    public IReadOnlyList<byte[]> RawWritten
    {
        get { lock (_written) return _written.ToArray(); }
    }

    public void Write(ReadOnlySpan<byte> report)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (WriteException is not null)
            throw WriteException;

        byte[] copy = report.ToArray();
        lock (_written) _written.Add(copy);
        if (Responder is null)
            return;
        foreach (byte[] reply in Responder(copy))
            _input.Writer.TryWrite(EchoSwId ? Echo(reply, (byte)(copy[3] & 0x0F)) : reply);
    }

    public void Push(byte[] report) => _input.Writer.TryWrite(report);

    /// <summary>Simulates the device disappearing: the input channel completes.</summary>
    public void Close() => _input.Writer.TryComplete();

    public void Dispose()
    {
        IsDisposed = true;
        _input.Writer.TryComplete();
    }

    private static byte[] Normalise(byte[] frame)
    {
        if (frame.Length < 4 || (frame[3] & 0x0F) == 0)
            return frame;
        byte[] copy = (byte[])frame.Clone();
        copy[3] = (byte)((copy[3] & 0xF0) | 0x0B);
        return copy;
    }

    private static byte[] Echo(byte[] reply, byte swId)
    {
        if (reply.Length < 5 || reply[0] is not (0x10 or 0x11))
            return reply;
        // Error frames carry the original byte 3 at byte 4.
        int at = reply[2] is 0xFF or 0x8F ? 4 : 3;
        if ((reply[at] & 0x0F) != 0x0B)
            return reply;
        byte[] copy = (byte[])reply.Clone();
        copy[at] = (byte)((copy[at] & 0xF0) | swId);
        return copy;
    }
}
