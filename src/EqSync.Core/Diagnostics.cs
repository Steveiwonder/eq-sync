namespace EqSync.Core;

public interface IEqSyncLogger
{
    string LogPath { get; }

    void Info(string message);

    void Error(Exception exception, string message);
}

public sealed class FileEqSyncLogger : IEqSyncLogger
{
    private readonly object _gate = new();

    public string LogPath { get; }

    public FileEqSyncLogger(string? logPath = null)
    {
        LogPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqSync",
            "logs",
            "eqsync.log");
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
        }
    }
}

public sealed class NullEqSyncLogger : IEqSyncLogger
{
    public static NullEqSyncLogger Instance { get; } = new();

    public string LogPath => string.Empty;

    public void Info(string message)
    {
    }

    public void Error(Exception exception, string message)
    {
    }
}
