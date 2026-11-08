using ProfileHop.Core.Tray;

namespace ProfileHop.Core.Tests.Tray;

public class RollingLogTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 13, 5, 9, 42);

    [Fact]
    public void Writes_timestamped_lines_and_creates_folder()
    {
        using var dir = new TempDir();
        var logDir = Path.Combine(dir.Path, "logs");
        using (var log = new RollingLog(logDir, LogLevel.Debug, clock: () => Now))
        {
            log.Write(LogLevel.Info, "hello");
            log.Write(LogLevel.Debug, "frame 11 01 0F");
        }

        var lines = File.ReadAllLines(Path.Combine(logDir, "profilehop.log"));
        Assert.Equal(["2026-11-08 13:05:09.042 [INF] hello", "2026-11-08 13:05:09.042 [DBG] frame 11 01 0F"], lines);
    }

    [Fact]
    public void Filters_below_min_level_and_level_can_change()
    {
        using var dir = new TempDir();
        using (var log = new RollingLog(dir.Path, LogLevel.Info, clock: () => Now))
        {
            Assert.False(log.IsEnabled(LogLevel.Debug));
            Assert.True(log.IsEnabled(LogLevel.Error));
            log.Write(LogLevel.Debug, "hidden");
            log.Write(LogLevel.Warning, "shown");
            log.MinLevel = LogLevel.Debug;
            log.Write(LogLevel.Debug, "now shown");
        }

        var text = File.ReadAllText(dir.File("profilehop.log"));
        Assert.DoesNotContain("hidden", text);
        Assert.Contains("[WRN] shown", text);
        Assert.Contains("[DBG] now shown", text);
    }

    [Fact]
    public void Appends_to_existing_log()
    {
        using var dir = new TempDir();
        using (var log = new RollingLog(dir.Path, LogLevel.Info, clock: () => Now)) log.Write(LogLevel.Info, "one");
        using (var log = new RollingLog(dir.Path, LogLevel.Info, clock: () => Now)) log.Write(LogLevel.Error, "two");

        Assert.Equal(2, File.ReadAllLines(dir.File("profilehop.log")).Length);
    }

    [Fact]
    public void Rolls_at_max_size_keeping_one_old_file()
    {
        using var dir = new TempDir();
        using (var log = new RollingLog(dir.Path, LogLevel.Info, maxBytes: 200, clock: () => Now))
        {
            for (var i = 0; i < 30; i++) log.Write(LogLevel.Info, $"line {i:D2} padding padding");
        }

        var current = new FileInfo(dir.File("profilehop.log"));
        var old = new FileInfo(dir.File("profilehop.1.log"));
        Assert.True(current.Length <= 200, $"current is {current.Length} bytes");
        Assert.True(old.Exists);
        Assert.True(old.Length <= 200);
        Assert.Equal(2, Directory.GetFiles(dir.Path).Length);
        Assert.Contains("line 29", File.ReadAllText(current.FullName));
    }

    [Fact]
    public void Is_thread_safe()
    {
        using var dir = new TempDir();
        using (var log = new RollingLog(dir.Path, LogLevel.Info, clock: () => Now))
        {
            Parallel.For(0, 200, i => log.Write(LogLevel.Info, $"msg {i}"));
        }

        Assert.Equal(200, File.ReadAllLines(dir.File("profilehop.log")).Length);
    }

    [Fact]
    public void Write_after_dispose_is_ignored()
    {
        using var dir = new TempDir();
        var log = new RollingLog(dir.Path, LogLevel.Info, clock: () => Now);
        log.Dispose();
        log.Write(LogLevel.Error, "late"); // must not throw
    }

    [Fact]
    public void Unusable_folder_does_not_throw()
    {
        using var dir = new TempDir();
        var blocker = dir.File("blocker");
        File.WriteAllText(blocker, "file, not folder");

        using var log = new RollingLog(Path.Combine(blocker, "logs"), LogLevel.Info, clock: () => Now);
        log.Write(LogLevel.Error, "nowhere to go");
    }
}
