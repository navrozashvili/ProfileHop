using System.Threading.Channels;
using ProfileHop.Core.Hid;

namespace ProfileHop.Core.Tests.Hidpp.Fakes;

/// <summary>
/// Scripted transport: every written report is recorded and handed to <see cref="Responder"/>, whose returned
/// reports are queued as input. <see cref="Push"/> injects unsolicited input.
/// </summary>
internal sealed class FakeHidTransport : IHidTransport
{
    private readonly Channel<byte[]> _input = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte[]> _written = [];

    public Func<byte[], IEnumerable<byte[]>>? Responder { get; set; }

    public Exception? WriteException { get; set; }

    public bool IsDisposed { get; private set; }

    public ChannelReader<byte[]> Reports => _input.Reader;

    public IReadOnlyList<byte[]> Written
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
            _input.Writer.TryWrite(reply);
    }

    public void Push(byte[] report) => _input.Writer.TryWrite(report);

    /// <summary>Simulates the device disappearing: the input channel completes.</summary>
    public void Close() => _input.Writer.TryComplete();

    public void Dispose()
    {
        IsDisposed = true;
        _input.Writer.TryComplete();
    }
}
