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

    /// <summary>
    /// Settings → Diagnostics. Off (default) writes Info and above; on also writes the
    /// Debug lines (API calls, state transitions, sync detail) that support asks for.
    /// </summary>
    void SetDiagnosticsMode(bool enabled);
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
    private string _logPath;
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
            logDir = NineLivesAudio.Helpers.AppPaths.LogsDirectory;
            Directory.CreateDirectory(logDir);
        }
        catch
        {
            logDir = Path.GetTempPath();
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
        _logPath = Path.Combine(logDir, $"app_{timestamp}.log");

        // Retention: one week of logs. A week is enough to look back at "it broke yesterday"
        // and keeps the folder small on a laptop that never cleans up after itself.
        try
        {
            var cutoff = DateTime.Now.AddDays(-LogRetentionDays);
            var pruned = 0;
            long prunedBytes = 0;
            foreach (var old in Directory.GetFiles(logDir, "app_*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(old) >= cutoff) continue;

                    var size = new FileInfo(old).Length;
                    File.Delete(old);
                    pruned++;
                    prunedBytes += size;
                }
                catch { /* best effort */ }
            }

            if (pruned > 0)
                LogStatic(LogLevel.Info, $"[Logging] pruned {pruned} old log file(s), {prunedBytes / 1024 / 1024} MB freed (retention {LogRetentionDays} days)");
        }
        catch { /* best effort */ }

        // Start the single background consumer
        _consumer = Task.Run(ConsumeLogsAsync);

        Log($"=== Session {SessionId} started ===");
        Log($"Log file: {_logPath}");
        Log($"Build: {DescribeBuild()}");
    }

    /// <summary>
    /// "1.0.1.0 (compiled 2026-11-14 06:41)" — the compiled date comes from the PE header, so
    /// a log can always be matched to the build it was produced by (the packaged exe would
    /// otherwise only show the time it was copied into Program Files).
    /// </summary>
    /// <summary>Log files older than this many days are deleted at startup.</summary>
    private const int LogRetentionDays = 7;

    public static string DescribeBuild()
    {
        var version = typeof(LoggingService).Assembly.GetName().Version?.ToString() ?? "unknown";

        // CI stamps the commit into InformationalVersion ("1.0.8+<sha>"), which is the only
        // trustworthy identity: the PE timestamp is a deterministic-build hash, so it can
        // read "compiled 2096-02-19" and tells you nothing about which build you have.
        try
        {
            var informational = typeof(LoggingService).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;

            if (!string.IsNullOrEmpty(informational))
            {
                var plus = informational.IndexOf('+');
                if (plus >= 0 && informational.Length > plus + 1)
                {
                    var sha = informational[(plus + 1)..].Trim();
                    if (sha.Length > 7) sha = sha[..7];
                    return $"{version} (build {sha})";
                }
            }
        }
        catch { /* fall through to the file timestamp */ }

        try
        {
            var path = typeof(LoggingService).Assembly.Location;
            if (string.IsNullOrEmpty(path))
                path = Environment.ProcessPath ?? string.Empty;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var built = File.GetLastWriteTime(path);
                if (built.Year is > 2020 and < 2100)
                    return $"{version} (local build {built:yyyy-MM-dd HH:mm})";
            }
        }
        catch { /* version-only fallback */ }
        return version;
    }

    /// <summary>
    /// Writes one line straight to the current log file. Used before the DI container exists
    /// (folder resolution, legacy data import) where <see cref="Log"/> cannot be called yet.
    /// </summary>
    public static void LogStatic(LogLevel level, string message)
    {
        try
        {
            var dir = NineLivesAudio.Helpers.AppPaths.LogsDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"app_{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [startup] [{level.ToString().ToUpper().PadRight(5)}] {message}";
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch { /* logging must never throw */ }
    }

    /// <summary>Hard cap for a single day's file — diagnostics mode is verbose by design.</summary>
    private const long MaxLogFileBytes = 25L * 1024 * 1024;

    /// <summary>How many times today's file has been rolled over (app_DATE.01.log, …).</summary>
    private int _rollIndex;

    private volatile bool _diagnosticsMode;

    public void SetDiagnosticsMode(bool enabled)
    {
        if (_diagnosticsMode == enabled) return;
        _diagnosticsMode = enabled;
        Log($"[Logging] Diagnostics mode {(enabled ? "enabled — debug lines are written" : "disabled — debug lines are skipped")}");
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        // Debug is opt-in: without diagnostics mode the file stays small and readable.
        if (level == LogLevel.Debug && !_diagnosticsMode) return;

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

    private static long FileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch { return 0; }
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
                        // Stop at the per-file cap instead of filling the disk: one final line
                        // says why the log ends here.
                        if (FileLength(_logPath) > MaxLogFileBytes)
                        {
                            // Roll over instead of discarding: the lines written right after
                            // something went wrong are the ones worth keeping.
                            _rollIndex++;
                            var directory = Path.GetDirectoryName(_logPath) ?? ".";
                            var stem = Path.GetFileNameWithoutExtension(_logPath);
                            var next = Path.Combine(directory, $"{stem}.{_rollIndex:D2}.log");

                            buffer.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{SessionId}] [WARN ] " +
                                       $"[Logging] {Path.GetFileName(_logPath)} reached {MaxLogFileBytes / 1024 / 1024} MB " +
                                       $"— continuing in {Path.GetFileName(next)}");
                            _logPath = next;
                        }

                        if (buffer.Count > 0)
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
