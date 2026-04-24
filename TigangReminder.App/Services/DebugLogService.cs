using System.Text;
using Windows.Storage;

namespace TigangReminder_App.Services;

public sealed class DebugLogService
{
    private readonly string _logDirectoryPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "logs");
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DebugLogService()
    {
        _logFilePath = Path.Combine(_logDirectoryPath, "training-debug.log");
    }

    public string LogFilePath => _logFilePath;

    public async Task LogAsync(string category, string message)
    {
        Directory.CreateDirectory(_logDirectoryPath);

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{category}] {message}{Environment.NewLine}";
        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_logFilePath, line, Encoding.UTF8);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Log(string category, string message)
    {
        Directory.CreateDirectory(_logDirectoryPath);
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{category}] {message}{Environment.NewLine}";

        _gate.Wait();
        try
        {
            File.AppendAllText(_logFilePath, line, Encoding.UTF8);
        }
        finally
        {
            _gate.Release();
        }
    }
}
