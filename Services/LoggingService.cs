using System.IO.Compression;
using System.Threading.Channels;

namespace NineLivesAudio.Services;

public interface ILoggingService
{
    void Log(string message, LogLevel level = LogLevel.Info);
    void LogError(string message, Exception? ex = null);
    void LogWarning(string message);
    void LogDebug(string message);
    Task FlushAsync();

    /// <summary>Export all log files as a zip archive. Returns the zip file path.</summary>
    Task<string> ExportLogsAsync();

    /// <summary>Unique ID for this app run — included in every log line.</summary>
    string SessionId { get; }
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public class LoggingService : ILoggingService, IDisposable
{
    private readonly string _logPath;
    private readonly Channel<string> _channel;
    private readonly Task _consumer;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _flushSignal = new(0, 1);
    private volatile TaskCompletionSource<bool>? _flushAck;
    private bool _disposed;

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..8]; // short 8-char id

    public LoggingService()
    {
        // Bounded channel: 500 entries is sufficient for normal operation, reduces memory footprint
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        string logDir;
        try
        {
            var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            logDir = Path.Combine(localFolder, "NineLivesAudio", "Logs");
            Directory.CreateDirectory(logDir);
        }
        catch
        {
            logDir = Path.GetTempPath();
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
        _logPath = Path.Combine(logDir, $"app_{timestamp}.log");

        // Retention: delete log files older than 14 days to bound disk usage.
        try
        {
            var cutoff = DateTime.Now.AddDays(-14);
            foreach (var old in Directory.GetFiles(logDir, "app_*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(old) < cutoff)
                        File.Delete(old);
                }
                catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }

        // Start the single background consumer
        _consumer = Task.Run(ConsumeLogsAsync);

        Log($"=== Session {SessionId} started ===");
        Log($"Log file: {_logPath}");
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpper().PadRight(5);
        var logLine = $"[{timestamp}] [{SessionId}] [{levelStr}] {message}";

        // Write to debug output (IDE only, not to file)
        System.Diagnostics.Debug.WriteLine(logLine);

        // Enqueue — single write path, no duplicates
        _channel.Writer.TryWrite(logLine);
    }

    public void LogError(string message, Exception? ex = null)
    {
        if (ex != null)
        {
            Log($"{message}: {ex.Message}", LogLevel.Error);
            Log($"  Stack: {ex.StackTrace}", LogLevel.Error);
            if (ex.InnerException != null)
                Log($"  Inner: {ex.InnerException.Message}", LogLevel.Error);
        }
        else
        {
            Log(message, LogLevel.Error);
        }
    }

    public void LogWarning(string message) => Log(message, LogLevel.Warning);
    public void LogDebug(string message) => Log(message, LogLevel.Debug);

    public async Task<string> ExportLogsAsync()
    {
        await FlushAsync();

        var logDir = Path.GetDirectoryName(_logPath)!;
        var exportDir = Path.Combine(logDir, "Exports");
        Directory.CreateDirectory(exportDir);

        var zipPath = Path.Combine(exportDir, $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var logFile in Directory.GetFiles(logDir, "*.log"))
            {
                zip.CreateEntryFromFile(logFile, Path.GetFileName(logFile), CompressionLevel.Optimal);
            }
        }

        Log($"Logs exported to: {zipPath}");
        return zipPath;
    }

    /// <summary>
    /// Flush all pending log lines to disk. Called by crash hooks and dispose.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_disposed) return;

        // Ask the consumer to drain, then wait for its acknowledgement instead of
        // guessing with a fixed delay. Bounded so a wedged consumer cannot hang us.
        var ack = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _flushAck = ack;
        try { _flushSignal.Release(); }
        catch (ObjectDisposedException) { return; }     // shutting down
        catch (SemaphoreFullException) { /* a flush is already pending — consumer will wake anyway */ }

        await Task.WhenAny(ack.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
    }

    private void AcknowledgeFlush()
    {
        var ack = _flushAck;
        if (ack == null) return;
        _flushAck = null;
        ack.TrySetResult(true);
    }

    private async Task ConsumeLogsAsync()
    {
        var reader = _channel.Reader;
        var buffer = new List<string>(64);
        var flushInterval = TimeSpan.FromSeconds(2);

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                buffer.Clear();

                // Wait for at least one message, or timeout for periodic flush
                try
                {
                    if (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                    {
                        // Drain all available messages
                        while (reader.TryRead(out var line))
                        {
                            buffer.Add(line);
                            if (buffer.Count >= 100) break; // batch size cap
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Write batch to file
                if (buffer.Count > 0)
                {
                    try
                    {
                        await File.AppendAllLinesAsync(_logPath, buffer).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Log write failed: {ex.Message}");
                    }
                }

                // Tell a waiting FlushAsync that pending lines are on disk.
                AcknowledgeFlush();

                // Small delay to batch more efficiently (unless flush was requested)
                try
                {
                    await _flushSignal.WaitAsync(flushInterval, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (TimeoutException) { /* normal timeout, continue loop */ }
            }
        }
        finally
        {
            // Final drain on shutdown
            buffer.Clear();
            while (reader.TryRead(out var line))
                buffer.Add(line);

            if (buffer.Count > 0)
            {
                try { File.AppendAllLines(_logPath, buffer); }
                catch { /* best effort */ }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Log($"=== Session {SessionId} ending ===");

        // Release any waiter before tearing down the signal.
        AcknowledgeFlush();

        // Signal shutdown and wait for consumer to finish
        _channel.Writer.TryComplete();
        _cts.Cancel();

        try { _consumer.Wait(TimeSpan.FromSeconds(3)); }
        catch { /* timeout ok */ }

        _cts.Dispose();
        _flushSignal.Dispose();
    }
}
