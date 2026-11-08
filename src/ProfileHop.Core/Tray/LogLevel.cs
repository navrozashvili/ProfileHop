namespace ProfileHop.Core.Tray;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public interface ILog
{
    bool IsEnabled(LogLevel level);

    void Write(LogLevel level, string message);
}
