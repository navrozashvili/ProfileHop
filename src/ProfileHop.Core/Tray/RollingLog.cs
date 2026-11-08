using System.Globalization;
using System.Text;

namespace ProfileHop.Core.Tray;

/// <summary>
/// Thread-safe text log: <c>profilehop.log</c> in <paramref name="directory"/>, rolled to <c>profilehop.1.log</c>
/// (one old file kept) when the next line would exceed <paramref name="maxBytes"/>. Never throws: logging must not
/// take the app down, so I/O failures are swallowed.
/// </summary>
public sealed class RollingLog(string directory, LogLevel minLevel, long maxBytes = RollingLog.DefaultMaxBytes, Func<DateTime>? clock = null)
    : ILog, IDisposable
{
    public const long DefaultMaxBytes = 1024 * 1024;
    public const string FileName = "profilehop.log";
    public const string OldFileName = "profilehop.1.log";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _lock = new();
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.Now);
    private readonly string _path = Path.Combine(directory, FileName);
    private readonly string _oldPath = Path.Combine(directory, OldFileName);
    private FileStream? _stream;
    private bool _disposed;

    public LogLevel MinLevel { get; set; } = minLevel;

    public string Directory => directory;

    public bool IsEnabled(LogLevel level) => level >= MinLevel;

    public void Write(LogLevel level, string message)
    {
        if (!IsEnabled(level)) return;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{_clock():yyyy-MM-dd HH:mm:ss.fff} [{Tag(level)}] {message}{Environment.NewLine}");
        var bytes = Utf8.GetBytes(line);

        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                var stream = _stream ??= Open();
                if (stream.Length > 0 && stream.Length + bytes.Length > maxBytes)
                {
                    stream.Dispose();
                    _stream = null;
                    File.Move(_path, _oldPath, overwrite: true);
                    stream = _stream = Open();
                }
                stream.Write(bytes);
                stream.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _stream?.Dispose();
                _stream = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _stream?.Dispose();
            _stream = null;
        }
    }

    private FileStream Open()
    {
        System.IO.Directory.CreateDirectory(directory);
        return new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warning => "WRN",
        _ => "ERR",
    };
}
