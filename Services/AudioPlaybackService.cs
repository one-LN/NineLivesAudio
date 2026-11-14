using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services.Playback;
using NAudio.Wave;

namespace NineLivesAudio.Services;

public class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly IAudioBookshelfApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger;
    private readonly IPlaybackSourceResolver _sourceResolver;
    private readonly INotificationService _notifications;
    private readonly ISyncService _syncService;
    private readonly Data.ILocalDatabase _database;
    private readonly ITrackManager _trackManager;
    private readonly ILocalizationService _localization;

    // NAudio for local file playback
    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioFileReader;

    // Windows MediaPlayer for streaming
    private Windows.Media.Playback.MediaPlayer? _mediaPlayer;

    // SMTC: background MediaPlayer used solely for SMTC registration when using NAudio
    private Windows.Media.Playback.MediaPlayer? _smtcPlayer;

    // Shared HttpClient for temp downloads (avoid socket exhaustion)
    private HttpClient? _tempDownloadClient;

    private System.Timers.Timer? _positionTimer;
    private AudioBook? _currentAudioBook;
    private PlaybackSessionInfo? _currentSession;
    private PlaybackState _state = PlaybackState.Stopped;
    private float _playbackSpeed = 1.0f;
    private float _volume = 0.8f;
    private bool _isStreaming; // true = MediaPlayer, false = NAudio local
    private bool _isLocalFile; // true if playing from downloaded file
    private CancellationTokenSource? _loadCts;
    private bool _allowNearZeroProgressSync;
    private const double ProgressSyncGuardSeconds = 5.0;
    private StartupProgressStatus _startupProgressStatus = StartupProgressStatus.Unknown;

    // Session sync timer (12-second interval for progress sync to server)
    private PeriodicTimer? _sessionSyncTimer;
    private CancellationTokenSource? _sessionSyncCts;
    private DateTime _sessionStartTime;
    private double _accumulatedListenTime;
    private DateTime _lastSyncTickTime;

    // Multi-track state
    private List<string> _trackPaths = new();     // local file paths for multi-track
    private List<AudioStreamInfo> _streamTracks = new(); // streaming tracks
    private List<double> _trackDurations = new(); // cumulative durations for offset calc
    private int _currentTrackIndex;
    private bool _autoAdvancing; // guards against re-entrancy during track advance

    // Chapter state
    private List<Chapter> _chapters = new();
    private int _currentChapterIndex = -1;

    // Skip intro / outro (global settings, see AppSettings.SkipIntroSeconds/SkipOutroSeconds);
    // the per-chapter bookkeeping lives next to the methods themselves.
    private bool _outroSkipInProgress;

    private enum StartupProgressStatus
    {
        Unknown,
        Resolved,
        TimedOut,
        Failed
    }

    // Events migrated to WeakReferenceMessenger:
    // PlaybackStateChangedMessage, PositionChangedMessage, TrackChangedMessage, ChapterChangedMessage

    public PlaybackState State => _state;
    public float PlaybackSpeed => _playbackSpeed;
    public float Volume => _volume;
    public AudioBook? CurrentAudioBook => _currentAudioBook;
    public int CurrentTrackIndex => _currentTrackIndex;
    public int TotalTracks => Math.Max(_trackPaths.Count, _streamTracks.Count);
    public List<Chapter> Chapters => _chapters;
    public Chapter? CurrentChapter => _currentChapterIndex >= 0 && _currentChapterIndex < _chapters.Count
        ? _chapters[_currentChapterIndex] : null;
    public int CurrentChapterIndex => _currentChapterIndex;

    public TimeSpan Position
    {
        get
        {
            var trackPosition = TimeSpan.Zero;
            if (_isStreaming && _mediaPlayer != null)
                trackPosition = _mediaPlayer.PlaybackSession.Position;
            else
                trackPosition = _audioFileReader?.CurrentTime ?? TimeSpan.Zero;

            // Add cumulative duration of previous tracks
            if (_currentTrackIndex > 0 && _currentTrackIndex <= _trackDurations.Count)
                return trackPosition + TimeSpan.FromSeconds(_trackDurations[_currentTrackIndex - 1]);
            return trackPosition;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            // Total duration across all tracks
            if (_trackDurations.Count > 0)
                return TimeSpan.FromSeconds(_trackDurations.Last());
            if (_isStreaming && _mediaPlayer != null)
                return _mediaPlayer.PlaybackSession.NaturalDuration;
            return _audioFileReader?.TotalTime ?? TimeSpan.Zero;
        }
    }

    public bool IsPlayingLocalFile => _isLocalFile;

    public AudioPlaybackService(
        IAudioBookshelfApiService apiService,
        ISettingsService settingsService,
        ILoggingService loggingService,
        IPlaybackSourceResolver sourceResolver,
        INotificationService notifications,
        ISyncService syncService,
        Data.ILocalDatabase database,
        ITrackManager trackManager,
        ILocalizationService localization)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _logger = loggingService;
        _sourceResolver = sourceResolver;
        _notifications = notifications;
        _syncService = syncService;
        _database = database;
        _trackManager = trackManager;
        _localization = localization;

        _positionTimer = new System.Timers.Timer(500);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
    }

    private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        var position = Position;
        WeakReferenceMessenger.Default.Send(new PositionChangedMessage(position));
        UpdateCurrentChapter();
        DetectPlaybackStall(position.TotalSeconds);
        _ = ApplyChapterSkipsAsync();
    }

    public async Task<bool> LoadAudioBookAsync(AudioBook audioBook)
    {
        _uiDispatcher ??= Windows.System.DispatcherQueue.GetForCurrentThread();
        // Cancel any previous load and dispose old CTS
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            SetState(PlaybackState.Loading);
            _logger.Log($"[Playback] Loading: {audioBook.Title}");

            await StopAsync();
            _currentAudioBook = audioBook;
            _isLocalFile = false;
            _currentTrackIndex = 0;
            _accumulatedListenTime = 0;
            _trackPaths.Clear();
            _streamTracks.Clear();
            _trackDurations.Clear();
            _autoAdvancing = false;
            _chapters = audioBook.Chapters.OrderBy(c => c.Start).ToList();
            _currentChapterIndex = -1;
            ResetChapterSkipState(-1);
            _outroSkipInProgress = false;
            _startupProgressStatus = StartupProgressStatus.Unknown;

            var progressResolved = await ResolveInitialProgressAsync(audioBook, ct);
            _allowNearZeroProgressSync = progressResolved || audioBook.CurrentTime.TotalSeconds >= ProgressSyncGuardSeconds;

            // === Use PlaybackSourceResolver for decision ===
            var source = _sourceResolver.ResolveSource(audioBook);

            // If the resolver recovered download state from disk (DB was stale),
            // persist it immediately so future loads don't need to re-scan
            if (source.WasRecoveredFromDisk)
            {
                _logger.Log($"[Playback] Persisting recovered download state for '{audioBook.Title}'");
                try { await _database.UpdateAudioBookAsync(audioBook); }
                catch (Exception ex) { _logger.LogDebug($"[Playback] Failed to persist recovered state: {ex.Message}"); }
            }

            // Start a server session — source-type-aware to avoid blocking local playback
            if (_apiService.IsAuthenticated)
            {
                if (source.Type == PlaybackSourceType.LocalFile)
                {
                    // Fire-and-forget with 2s timeout — don't block local file playback
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            var sessionTask = _apiService.StartPlaybackSessionAsync(audioBook.Id);
                            var completed = await Task.WhenAny(sessionTask, Task.Delay(2000, cts.Token));
                            if (completed == sessionTask && sessionTask.Result != null)
                            {
                                _currentSession = sessionTask.Result;
                                _logger.Log($"[Playback] Session started (background): {_currentSession.Id}");
                                if (_chapters.Count == 0 && _currentSession.Chapters.Count > 0)
                                    _chapters = _currentSession.Chapters.OrderBy(c => c.Start).ToList();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug($"[Playback] Background session start failed (non-fatal): {ex.Message}");
                        }
                    });
                }
                else
                {
                    // Blocking session start for streams — required for stream URLs
                    try
                    {
                        _currentSession = await _apiService.StartPlaybackSessionAsync(audioBook.Id);
                        if (_currentSession != null)
                        {
                            _logger.Log($"[Playback] Session started: {_currentSession.Id}");
                            if (_chapters.Count == 0 && _currentSession.Chapters.Count > 0)
                                _chapters = _currentSession.Chapters.OrderBy(c => c.Start).ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"[Playback] Session start failed (non-fatal): {ex.Message}");
                    }
                }
            }

            // Tell SyncService this item is active (session owns progress)
            _syncService.SetActivePlaybackItem(audioBook.Id);

            // Seed PlaybackProgress so Nine Lives shows this book immediately
            try
            {
                await _database.SavePlaybackProgressAsync(
                    audioBook.Id,
                    audioBook.CurrentTime,
                    isFinished: false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[Playback] Initial progress seed failed (non-fatal): {ex.Message}");
            }

            switch (source.Type)
            {
                case PlaybackSourceType.LocalFile:
                    _logger.Log($"[Playback] Pipeline: LOCAL file {source.LocalFilePath}");
                    _isLocalFile = true;
                    BuildLocalTrackList(audioBook, source.LocalFilePath!);
                    await NormalizeResumePositionAsync(audioBook);
                    NormalizeChapterUnits();
                    AlignChaptersToTracks();
                    DetermineStartingTrack(audioBook.CurrentTime);
                    LogLoadSummary(audioBook);
                    var localLoaded = await LoadLocalFileAsync(_trackPaths[_currentTrackIndex], audioBook, ct);
                    if (localLoaded) SetupSmtc(audioBook);
                    return localLoaded;

                case PlaybackSourceType.Stream:
                    _logger.Log($"[Playback] Pipeline: STREAM for {audioBook.Id}");

                    if (_currentSession != null && _currentSession.AudioTracks.Count > 0)
                    {
                        _streamTracks = _currentSession.AudioTracks.OrderBy(t => t.Index).ToList();
                        BuildStreamTrackDurations();
                        await NormalizeResumePositionAsync(audioBook);
                        NormalizeChapterUnits();
                        AlignChaptersToTracks();
                        DetermineStartingTrack(audioBook.CurrentTime);
                        LogLoadSummary(audioBook);

                        var track = _streamTracks[_currentTrackIndex];
                        _logger.Log($"[Playback] Stream URL obtained, {_streamTracks.Count} tracks");

                        if (await LoadStreamAsync(track.ContentUrl, audioBook, ct))
                        {
                            SetupSmtc(audioBook);
                            return true;
                        }

                        _logger.LogWarning("[Playback] Stream failed, falling back to temp download");
                    }

                    _logger.Log("[Playback] Pipeline: FALLBACK temp download");
                    if (_currentSession == null || _currentSession.AudioTracks.Count == 0)
                    {
                        _logger.LogWarning("[Playback] No playback session or tracks available");
                        var noTracks = _localization.GetString("NoAudioTracks");
                        SetState(PlaybackState.Stopped, noTracks);
                        _notifications.ShowError(noTracks, _localization.GetString("PlaybackError"));
                        return false;
                    }

                    var fallbackTrack = _currentSession.AudioTracks.First();
                    var fallbackLoaded = await LoadTempDownloadAsync(fallbackTrack.ContentUrl, audioBook, ct);
                    if (fallbackLoaded) SetupSmtc(audioBook);
                    return fallbackLoaded;

                case PlaybackSourceType.Unavailable:
                default:
                    _logger.LogWarning($"[Playback] Source unavailable: {source.ErrorMessage}");
                    SetState(PlaybackState.Stopped, source.ErrorMessage);
                    _notifications.ShowWarning(source.ErrorMessage ?? _localization.GetString("CannotPlayBook"), _localization.GetString("Playback"));
                    return false;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Log("[Playback] Load cancelled");
            SetState(PlaybackState.Stopped);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Playback] Load failed", ex);
            SetState(PlaybackState.Stopped, _localization.Format("FailedLoadDetail", ex.Message));
            _notifications.ShowError(ex.Message, _localization.GetString("PlaybackError"));
            return false;
        }
    }

    private Task<bool> LoadLocalFileAsync(string path, AudioBook audioBook, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _isStreaming = false;

        // Unwire old events before disposing to prevent leak
        UnwireNAudioEvents();

        // Dispose previous NAudio objects
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _audioFileReader?.Dispose();

        _audioFileReader = new AudioFileReader(path);
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_audioFileReader);
        _outputDevice.Volume = _volume;
        WireNAudioEvents();

        RescaleTimelineIfNeeded(_currentTrackIndex, _audioFileReader.TotalTime);
        ReconcileTrackDuration(_currentTrackIndex, _audioFileReader.TotalTime);

        // Seek within the current track (not overall position)
        var withinTrackOffset = GetWithinTrackOffset(audioBook.CurrentTime);
        if (withinTrackOffset.TotalSeconds > 0)
            _audioFileReader.CurrentTime = withinTrackOffset;

        SetState(PlaybackState.Paused);
        _logger.Log($"[Playback] Track {_currentTrackIndex + 1}/{TotalTracks} loaded. Duration: {Duration}");
        return Task.FromResult(true);
    }

    private Task<bool> LoadLocalTrackAsync(string path, CancellationToken ct, bool autoPlay)
    {
        ct.ThrowIfCancellationRequested();
        _isStreaming = false;

        // Unwire old events before disposing to prevent leak
        UnwireNAudioEvents();

        _outputDevice?.Stop();
        _outputDevice?.Dispose();
        _audioFileReader?.Dispose();

        _audioFileReader = new AudioFileReader(path);
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_audioFileReader);
        _outputDevice.Volume = _volume;
        WireNAudioEvents();

        ReconcileTrackDuration(_currentTrackIndex, _audioFileReader.TotalTime);

        if (autoPlay)
        {
            SetState(PlaybackState.Playing);
            _outputDevice.Play();
            _positionTimer?.Start();
        }
        else
        {
            SetState(PlaybackState.Paused);
            _positionTimer?.Stop();
        }

        _logger.Log($"[Playback] Track {_currentTrackIndex + 1}/{TotalTracks} loaded (auto-advance)");
        return Task.FromResult(true);
    }

    private async Task<bool> LoadStreamAsync(string streamUrl, AudioBook audioBook, CancellationToken ct, bool seekToPosition = true)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            _isStreaming = true;

            // Unwire old events before disposing to prevent leak. Pausing first matters:
            // disposing a MediaPlayer that is still playing can block the media thread and
            // take the whole window down with it.
            UnwireMediaPlayerEvents();
            try { _mediaPlayer?.Pause(); } catch { /* already gone */ }
            _mediaPlayer?.Dispose();
            _mediaPlayer = null;

            _mediaPlayer = new Windows.Media.Playback.MediaPlayer();
            _mediaPlayer.Volume = _volume;
            _mediaPlayer.PlaybackSession.PlaybackRate = _playbackSpeed;
            _mediaPlayer.AutoPlay = false;

            _mediaPlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(streamUrl));

            // Wait for media to open (with timeout)
            var tcs = new TaskCompletionSource<bool>();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            void onOpened(Windows.Media.Playback.MediaPlayer sender, object args)
                => tcs.TrySetResult(true);
            void onFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
                => tcs.TrySetResult(false);

            _mediaPlayer.MediaOpened += onOpened;
            _mediaPlayer.MediaFailed += onFailed;

            using (timeoutCts.Token.Register(() => tcs.TrySetResult(false)))
            {
                var opened = await tcs.Task;
                _mediaPlayer.MediaOpened -= onOpened;
                _mediaPlayer.MediaFailed -= onFailed;

                if (!opened)
                {
                    _logger.LogWarning("[Playback] MediaPlayer failed to open stream");
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                    _isStreaming = false;
                    return false;
                }
            }

            WireMediaPlayerEvents();

            // Same self-healing as local playback: the stream knows its own length.
            if (_trackDurations.Count > 1)
            {
                RescaleTimelineIfNeeded(_currentTrackIndex, _mediaPlayer.PlaybackSession.NaturalDuration);
                ReconcileTrackDuration(_currentTrackIndex, _mediaPlayer.PlaybackSession.NaturalDuration);
            }

            if (seekToPosition)
            {
                var withinTrack = GetWithinTrackOffset(audioBook.CurrentTime);
                if (withinTrack.TotalSeconds > 0)
                    _mediaPlayer.PlaybackSession.Position = withinTrack;
            }

            SetState(PlaybackState.Paused);
            _logger.Log($"[Playback] Stream loaded. Duration: {Duration}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Playback] Stream exception: {ex.Message}");
            _mediaPlayer?.Dispose();
            _mediaPlayer = null;
            _isStreaming = false;
            return false;
        }
    }

    private async Task<bool> LoadTempDownloadAsync(string url, AudioBook audioBook, CancellationToken ct)
    {
        // Inside the app's own data folder, not %TEMP%: a downloaded stream file can be
        // hundreds of megabytes and must not be left behind on the system drive.
        var tempDir = Path.Combine(NineLivesAudio.Helpers.AppPaths.DataRoot, "stream-cache");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"abs_stream_{audioBook.Id}.tmp");

        _logger.Log($"[Playback] Temp download to: {tempPath}");

        if (_tempDownloadClient == null)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None) return true;
                    if (_settingsService.Settings.AllowSelfSignedCertificates
                        && !string.IsNullOrEmpty(_apiService.ServerUrl)
                        && msg.RequestUri != null
                        && Uri.TryCreate(_apiService.ServerUrl, UriKind.Absolute, out var configuredUri))
                    {
                        return string.Equals(msg.RequestUri.Host, configuredUri.Host, StringComparison.OrdinalIgnoreCase);
                    }
                    return false;
                }
            };
            _tempDownloadClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        }

        using var response = await _tempDownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        using var networkStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long downloaded = 0;
        int bytesRead;

        while ((bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
            downloaded += bytesRead;

            if (downloaded % (1024 * 1024) < 81920 && totalBytes.HasValue && totalBytes.Value > 0)
            {
                var pct = (int)(downloaded * 100 / totalBytes.Value);
                _logger.LogDebug($"[Playback] Temp download: {pct}%");
            }
        }

        fileStream.Close();
        _logger.Log($"[Playback] Temp download complete: {downloaded / 1024}KB");

        return await LoadLocalFileAsync(tempPath, audioBook, ct);
    }

    public async Task PlayAsync()
    {
        if (_isStreaming && _mediaPlayer != null)
        {
            _mediaPlayer.Play();
            _positionTimer?.Start();
            SetState(PlaybackState.Playing);
        }
        else if (_outputDevice != null && (_state == PlaybackState.Paused || _state == PlaybackState.Stopped))
        {
            _outputDevice.Play();
            _positionTimer?.Start();
            SetState(PlaybackState.Playing);
        }

        // Start session sync timer (12s interval)
        _sessionStartTime = DateTime.UtcNow;
        _lastSyncTickTime = DateTime.UtcNow;
        StartSessionSyncTimer();
        UpdateSmtcPlaybackState(true);

        // Chapter intro skipping happens from UpdateCurrentChapter, which knows which
        // chapter the position belongs to.
    }

    public Task PauseAsync()
    {
        StopSessionSyncTimer();

        if (_isStreaming && _mediaPlayer != null)
        {
            _mediaPlayer.Pause();
            _positionTimer?.Stop();
            SetState(PlaybackState.Paused);
        }
        else if (_outputDevice != null && _state == PlaybackState.Playing)
        {
            _outputDevice.Pause();
            _positionTimer?.Stop();
            SetState(PlaybackState.Paused);
        }

        UpdateSmtcPlaybackState(false);
        // Sync progress immediately on pause
        _ = SyncProgressNowAsync();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        StopSessionSyncTimer();
        _positionTimer?.Stop();
        ResetChapterSkipState(-1);

        // Flush final progress before stopping
        if (_currentAudioBook != null)
        {
            var finalPosition = Position.TotalSeconds;
            if (!ShouldBlockNearZeroSync(finalPosition, isFinished: false))
            {
                await _syncService.FlushPlaybackProgressAsync(
                    _currentAudioBook.Id,
                    finalPosition,
                    Duration.TotalSeconds,
                    isFinished: false);
            }
            else
            {
                _logger.LogDebug($"[Playback] Suppressed final near-zero flush at {finalPosition:F1}s");
            }
        }

        // Unwire all events before disposing to prevent leaks
        UnwireMediaPlayerEvents();
        UnwireNAudioEvents();
        UnwireSmtcEvents();

        // Properly dispose streaming MediaPlayer
        if (_isStreaming && _mediaPlayer != null)
        {
            try
            {
                _mediaPlayer.Pause();
                _mediaPlayer.Source = null;
            }
            catch { /* best effort */ }
            finally
            {
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
                _isStreaming = false;
            }
        }

        // Dispose NAudio resources
        if (_outputDevice != null)
        {
            try { _outputDevice.Stop(); } catch { /* best-effort: ignore */ }
            _outputDevice.Dispose();
            _outputDevice = null;
        }

        _audioFileReader?.Dispose();
        _audioFileReader = null;

        // Dispose SMTC player (important for memory)
        if (_smtcPlayer != null)
        {
            try
            {
                _smtcPlayer.Pause();
                _smtcPlayer.Source = null;
            }
            catch { /* best-effort: ignore */ }
            finally
            {
                _smtcPlayer.Dispose();
                _smtcPlayer = null;
            }
        }

        if (_currentSession != null)
            _ = CloseSessionAsync();

        SetState(PlaybackState.Stopped);
    }

    public async Task SeekAsync(TimeSpan position)
    {
        // For multi-track, check if we need to switch tracks
        if (_trackDurations.Count > 1)
        {
            int targetTrack = 0;
            for (int i = 0; i < _trackDurations.Count; i++)
            {
                if (position.TotalSeconds < _trackDurations[i])
                {
                    targetTrack = i;
                    break;
                }
                if (i == _trackDurations.Count - 1)
                    targetTrack = i;
            }

            if (targetTrack != _currentTrackIndex)
            {
                _currentTrackIndex = targetTrack;
                WeakReferenceMessenger.Default.Send(new TrackChangedMessage(_currentTrackIndex));
                var wasPlaying = _state == PlaybackState.Playing;

                var trackLoaded = false;

                if (_isStreaming && _streamTracks.Count > targetTrack && _currentAudioBook != null)
                {
                    // seekToPosition: false — we set the exact offset right below, so the
                    // track must not first jump to the saved book progress.
                    trackLoaded = await LoadStreamAsync(_streamTracks[targetTrack].ContentUrl, _currentAudioBook,
                        CancellationToken.None, seekToPosition: false);
                }
                else if (!_isStreaming && _trackPaths.Count > targetTrack)
                {
                    // Never auto-play before the offset is applied: that produced an
                    // audible blip from second 0 of the new file.
                    trackLoaded = await LoadLocalTrackAsync(_trackPaths[targetTrack], CancellationToken.None, autoPlay: false);
                }

                if (!trackLoaded)
                {
                    // Opening the target file failed (network, expired session, bad URL).
                    // Bailing out silently left the player sitting on the old position with
                    // no explanation, which reads exactly like "the jump button does nothing".
                    _logger.LogWarning($"[Playback] Cross-file seek aborted: could not open track {targetTrack + 1}");
                    _notifications.ShowWarning(_localization.GetString("JumpFailed"), _localization.GetString("Playback"));
                    return;
                }

                var withinTrack = GetWithinTrackOffset(position);
                if (_isStreaming && _mediaPlayer != null)
                    _mediaPlayer.PlaybackSession.Position = withinTrack;
                else if (_audioFileReader != null)
                    _audioFileReader.CurrentTime = withinTrack;

                if (wasPlaying) await PlayAsync();
                WeakReferenceMessenger.Default.Send(new PositionChangedMessage(position));
                return;
            }
        }

        // Same track — simple seek
        if (_isStreaming && _mediaPlayer != null)
        {
            // For streaming, position is within current track
            var withinTrack = _trackDurations.Count > 1 ? GetWithinTrackOffset(position) : position;
            _mediaPlayer.PlaybackSession.Position = withinTrack;
        }
        else if (_audioFileReader != null)
        {
            var withinTrack = _trackDurations.Count > 1 ? GetWithinTrackOffset(position) : position;
            _audioFileReader.CurrentTime = withinTrack;
        }

        WeakReferenceMessenger.Default.Send(new PositionChangedMessage(position));
    }

    public Task SetPlaybackSpeedAsync(float speed)
    {
        _playbackSpeed = Math.Clamp(speed, 0.5f, 3.0f);
        if (_isStreaming && _mediaPlayer != null)
            _mediaPlayer.PlaybackSession.PlaybackRate = _playbackSpeed;
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        if (_isStreaming && _mediaPlayer != null)
            _mediaPlayer.Volume = _volume;
        else if (_outputDevice != null)
            _outputDevice.Volume = _volume;
        return Task.CompletedTask;
    }

    private void WireNAudioEvents()
    {
        if (_outputDevice == null) return;
        _outputDevice.PlaybackStopped += OnNAudioPlaybackStopped;
    }

    private void UnwireNAudioEvents()
    {
        if (_outputDevice != null)
            _outputDevice.PlaybackStopped -= OnNAudioPlaybackStopped;
    }

    private void OnNAudioPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            StopSessionSyncTimer();
            _logger.LogError("[Playback] NAudio error", e.Exception);
            SetState(PlaybackState.Stopped, e.Exception.Message);
            return;
        }

        // Auto-advance to next track if available
        if (!_autoAdvancing && _currentTrackIndex + 1 < _trackPaths.Count)
        {
            _autoAdvancing = true;
            _currentTrackIndex++;
            WeakReferenceMessenger.Default.Send(new TrackChangedMessage(_currentTrackIndex));
            _logger.Log($"[Playback] Auto-advancing to track {_currentTrackIndex + 1}/{TotalTracks}");
            _ = Task.Run(async () =>
            {
                try
                {
                    await LoadLocalTrackAsync(_trackPaths[_currentTrackIndex], CancellationToken.None, autoPlay: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Playback] Track advance failed", ex);
                    SetState(PlaybackState.Stopped, ex.Message);
                }
                finally { _autoAdvancing = false; }
            });
            return;
        }

        // No more tracks — end of book
        StopSessionSyncTimer();
        _logger.Log("[Playback] End of book (all tracks)");
        SetState(PlaybackState.Stopped);
        if (_currentAudioBook != null)
        {
            _ = _syncService.FlushPlaybackProgressAsync(
                _currentAudioBook.Id,
                Position.TotalSeconds,
                Duration.TotalSeconds,
                isFinished: true);
        }
        if (_currentSession != null)
            _ = CloseSessionAsync();
    }

    private void WireMediaPlayerEvents()
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.MediaEnded += OnMediaPlayerEnded;
        _mediaPlayer.MediaFailed += OnMediaPlayerFailed;
        _mediaPlayer.BufferingStarted += OnMediaPlayerBufferingStarted;
        _mediaPlayer.BufferingEnded += OnMediaPlayerBufferingEnded;
    }

    private void UnwireMediaPlayerEvents()
    {
        if (_mediaPlayer == null) return;
        _mediaPlayer.MediaEnded -= OnMediaPlayerEnded;
        _mediaPlayer.MediaFailed -= OnMediaPlayerFailed;
        _mediaPlayer.BufferingStarted -= OnMediaPlayerBufferingStarted;
        _mediaPlayer.BufferingEnded -= OnMediaPlayerBufferingEnded;
    }

    private void OnMediaPlayerEnded(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        // Auto-advance to next stream track if available
        if (!_autoAdvancing && _currentTrackIndex + 1 < _streamTracks.Count)
        {
            _autoAdvancing = true;
            _currentTrackIndex++;
            WeakReferenceMessenger.Default.Send(new TrackChangedMessage(_currentTrackIndex));
            _logger.Log($"[Playback] Stream auto-advancing to track {_currentTrackIndex + 1}/{TotalTracks}");
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_currentAudioBook != null)
                        await LoadStreamAsync(_streamTracks[_currentTrackIndex].ContentUrl, _currentAudioBook, CancellationToken.None, seekToPosition: false);
                    await PlayAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Playback] Stream track advance failed", ex);
                    SetState(PlaybackState.Stopped, ex.Message);
                }
                finally { _autoAdvancing = false; }
            });
            return;
        }

        // No more tracks
        StopSessionSyncTimer();
        _logger.Log("[Playback] Stream ended (all tracks)");
        SetState(PlaybackState.Stopped);
        if (_currentAudioBook != null)
        {
            _ = _syncService.FlushPlaybackProgressAsync(
                _currentAudioBook.Id,
                Position.TotalSeconds,
                Duration.TotalSeconds,
                isFinished: true);
        }
        if (_currentSession != null)
            _ = CloseSessionAsync();
    }

    private void OnMediaPlayerFailed(Windows.Media.Playback.MediaPlayer sender, Windows.Media.Playback.MediaPlayerFailedEventArgs args)
    {
        _logger.LogError($"[Playback] MediaPlayer error: {args.ErrorMessage}");
        SetState(PlaybackState.Stopped, args.ErrorMessage);
    }

    private void OnMediaPlayerBufferingStarted(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        if (_state == PlaybackState.Playing)
            SetState(PlaybackState.Buffering);
    }

    private void OnMediaPlayerBufferingEnded(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        if (_state == PlaybackState.Buffering)
            SetState(PlaybackState.Playing);
    }

    private void SetState(PlaybackState newState, string? errorMessage = null)
    {
        var oldState = _state;
        _state = newState;
        _logger.LogDebug($"[Playback] {oldState} -> {newState}{(errorMessage != null ? $" ({errorMessage})" : "")}");
        WeakReferenceMessenger.Default.Send(new PlaybackStateChangedMessage(new PlaybackStateChangedEventArgs
        {
            State = newState,
            OldState = oldState,
            ErrorMessage = errorMessage
        }));
    }

    private void StartSessionSyncTimer()
    {
        StopSessionSyncTimer();
        _sessionSyncCts = new CancellationTokenSource();
        _sessionSyncTimer = new PeriodicTimer(TimeSpan.FromSeconds(12));
        _ = RunSessionSyncLoopAsync(_sessionSyncCts.Token);
    }

    private void StopSessionSyncTimer()
    {
        _sessionSyncCts?.Cancel();
        _sessionSyncTimer?.Dispose();
        _sessionSyncTimer = null;
        _sessionSyncCts?.Dispose();
        _sessionSyncCts = null;
    }

    private async Task RunSessionSyncLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_sessionSyncTimer != null && await _sessionSyncTimer.WaitForNextTickAsync(ct))
            {
                await SyncProgressNowAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Session sync loop error: {ex.Message}");
        }
    }

    private async Task SyncProgressNowAsync()
    {
        if (_currentAudioBook == null) return;

        var currentTime = Position.TotalSeconds;
        var duration = Duration.TotalSeconds;

        if (ShouldBlockNearZeroSync(currentTime, isFinished: false))
        {
            _logger.LogDebug($"[Playback] Skipping near-zero sync ({currentTime:F1}s) until startup position is verified");
            return;
        }

        // Save to local DB first (crash safety)
        try
        {
            await _database.SavePlaybackProgressAsync(
                _currentAudioBook.Id,
                TimeSpan.FromSeconds(currentTime),
                isFinished: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Local progress save failed: {ex.Message}");
        }

        // Sync to server via session if available
        if (_currentSession != null && _apiService.IsAuthenticated)
        {
            try
            {
                var now = DateTime.UtcNow;
                var tickElapsed = (now - _lastSyncTickTime).TotalSeconds;
                _lastSyncTickTime = now;
                _accumulatedListenTime += tickElapsed * _playbackSpeed;
                await _apiService.SyncSessionProgressAsync(
                    _currentSession.Id, currentTime, duration, _accumulatedListenTime);
                _logger.LogDebug($"[Playback] Session sync: {currentTime:F1}s, listened: {_accumulatedListenTime:F0}s");
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[Playback] Session sync failed: {ex.Message}");
            }
        }
        else
        {
            // Fallback: use SyncService throttled progress reporting
            await _syncService.ReportPlaybackPositionAsync(
                _currentAudioBook.Id, currentTime, duration);
        }
    }

    /// <summary>
    /// Logs when the position stops moving while the player claims to be playing — that is
    /// exactly what a wedged media session looks like, and without this line it is invisible.
    /// </summary>
    private void DetectPlaybackStall(double positionSeconds)
    {
        if (_state != PlaybackState.Playing)
        {
            _lastTickPosition = positionSeconds;
            _lastProgressAt = DateTime.UtcNow;
            return;
        }

        if (Math.Abs(positionSeconds - _lastTickPosition) > 0.05)
        {
            _lastTickPosition = positionSeconds;
            _lastProgressAt = DateTime.UtcNow;
            return;
        }

        var stalled = DateTime.UtcNow - _lastProgressAt;
        if (stalled.TotalSeconds < 8) return;
        if ((DateTime.UtcNow - _lastStallLogAt).TotalSeconds < 60) return;

        _lastStallLogAt = DateTime.UtcNow;
        _logger.LogWarning($"[Playback] Position has not moved for {stalled.TotalSeconds:F0}s " +
                           $"(state {_state}, position {positionSeconds:F1}s, track {_currentTrackIndex + 1}/{TotalTracks})");
    }

    // Chapter helpers
    private void UpdateCurrentChapter()
    {
        if (_chapters.Count == 0) return;

        var posSeconds = Position.TotalSeconds;

        // Fast path: ~80% of 500ms ticks land in the same chapter — skip search entirely.
        // Half a second of tolerance: right at a boundary the reported position can sit on
        // the next chapter's start while the audio still belongs to the current one (track
        // switching flips between 61512.9 and 61513.0), which made the chapter label flicker
        // back and forth between two chapters.
        const double BoundaryToleranceSeconds = 0.5;
        if (_currentChapterIndex >= 0 && _currentChapterIndex < _chapters.Count)
        {
            var current = _chapters[_currentChapterIndex];
            if (posSeconds >= current.Start - BoundaryToleranceSeconds &&
                posSeconds < current.End + BoundaryToleranceSeconds)
                return; // Still in the same chapter, no change
        }

        // Slow path: binary search for the chapter containing this position
        int newIndex = BinarySearchChapter(posSeconds);

        // Chapters do not always cover the whole timeline (intro/outro outside any
        // chapter, or a gap between files). Keep showing the chapter that is still
        // playing instead of dropping the whole chapter context mid-book.
        if (newIndex < 0)
            newIndex = FindPreviousChapterIndex(posSeconds);

        if (newIndex != _currentChapterIndex)
        {
            _currentChapterIndex = newIndex;
            var chapter = CurrentChapter;
            _logger.LogDebug($"[Playback] Chapter -> #{newIndex + 1}/{_chapters.Count} at {posSeconds:F0}s " +
                             $"(bounds {chapter?.Start:F0}s–{chapter?.End:F0}s) '{chapter?.Title}'");
            WeakReferenceMessenger.Default.Send(new ChapterChangedMessage(CurrentChapter));

            // Every chapter gets its intro trimmed, not just the first one of the book.
            // ApplyChapterSkipsAsync (position timer) does the work: it also covers jumps
            // that never raise a change event, e.g. a seek straight into a new chapter.
            ResetChapterSkipState(newIndex);
        }
    }

    private int BinarySearchChapter(double posSeconds)
        => _trackManager.FindChapterForPosition(_chapters, posSeconds);

    /// <summary>Index of the last chapter whose start is at or before <paramref name="posSeconds"/>.</summary>
    private int FindPreviousChapterIndex(double posSeconds)
    {
        var index = -1;
        for (var i = 0; i < _chapters.Count; i++)
        {
            if (_chapters[i].Start > posSeconds) break;
            index = i;
        }
        return index;
    }

    public async Task SeekToChapterAsync(int chapterIndex)
    {
        if (chapterIndex < 0 || chapterIndex >= _chapters.Count) return;
        var chapter = _chapters[chapterIndex];
        _logger.Log($"[Playback] Chapter jump #{chapterIndex + 1}/{_chapters.Count} '{chapter.Title}' " +
                    $"-> {TimeSpan.FromSeconds(chapter.Start)} (raw start {chapter.Start:0.###}s, " +
                    $"book duration {Duration})");
        await SeekAsync(TimeSpan.FromSeconds(chapter.Start));
        _currentChapterIndex = chapterIndex;
        ResetChapterSkipState(chapterIndex);
        WeakReferenceMessenger.Default.Send(new ChapterChangedMessage(chapter));

        // Jumping into a chapter counts as entering it: trim its intro immediately
        // instead of waiting for the next timer tick (that would leak 0.5 s of it).
        await ApplyChapterIntroSkipAsync(chapter, chapterIndex);
    }

    // ── Skip intro / skip outro ────────────────────────────────────────────────
    // Both settings are per chapter: a multi-file audiobook repeats its opening and
    // closing on every file, so "35 / 15" has to apply at chapter 700 just like at 7.
    //
    // IMPORTANT: this runs on the position timer (a pool thread, twice a second) and must
    // never reload the media player. Disposing a MediaPlayer that is still playing from a
    // pool thread froze the whole window ("未响应") at the 14-second mark of a chapter, so
    // skipping only ever moves the playback position *inside the track that is loaded*:
    //   · skip intro → jump forward, same track;
    //   · skip outro → jump to the end of the file and let the existing MediaEnded
    //     auto-advance (the path that has always worked) switch to the next chapter.

    private Windows.System.DispatcherQueue? _uiDispatcher;
    private int _skipCheckQueued;
    private double _lastTickPosition = -1;
    private DateTime _lastProgressAt = DateTime.UtcNow;
    private DateTime _lastStallLogAt = DateTime.MinValue;

    private int _skipChapterIndex = -1;
    private int _introSkipAttempts;
    private bool _introSkipDone;
    private bool _outroSkipDone;
    private bool _introSkipBusy;

    private void ResetChapterSkipState(int chapterIndex)
    {
        _skipChapterIndex = chapterIndex;
        _introSkipAttempts = 0;
        _introSkipDone = false;
        _outroSkipDone = false;
    }

    /// <summary>Absolute position where the currently loaded track ends.</summary>
    private double CurrentTrackEndSeconds =>
        _currentTrackIndex >= 0 && _currentTrackIndex < _trackDurations.Count
            ? _trackDurations[_currentTrackIndex]
            : Duration.TotalSeconds;

    /// <summary>Absolute position where the currently loaded track starts.</summary>
    private double CurrentTrackStartSeconds =>
        _currentTrackIndex > 0 && _currentTrackIndex <= _trackDurations.Count
            ? _trackDurations[_currentTrackIndex - 1]
            : 0;

    /// <summary>
    /// Seeks inside the track that is already loaded — never triggers a cross-file load.
    /// Returns false when the media session is not ready (the caller retries later).
    /// </summary>
    private bool SeekWithinCurrentTrack(double absoluteSeconds)
    {
        var trackEnd = CurrentTrackEndSeconds;
        var target = Math.Min(absoluteSeconds, trackEnd - 0.35);
        target = Math.Max(target, CurrentTrackStartSeconds);
        var within = GetWithinTrackOffset(TimeSpan.FromSeconds(target));

        try
        {
            if (_isStreaming && _mediaPlayer != null)
            {
                _mediaPlayer.PlaybackSession.Position = within;
            }
            else if (_audioFileReader != null)
            {
                _audioFileReader.CurrentTime = within;
            }
            else
            {
                return false;
            }

            WeakReferenceMessenger.Default.Send(new PositionChangedMessage(TimeSpan.FromSeconds(target)));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Playback] In-track seek failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Entry point from the position timer (twice a second): trims the intro of the chapter
    /// that is playing, and leaves it as soon as fewer than N seconds are left.
    /// </summary>
    private async Task ApplyChapterSkipsAsync()
    {
        try
        {
            // The timer fires on a pool thread. Anything that touches the player or sends a
            // message from there can freeze the window, so hop to the UI thread once.
            var dispatcher = _uiDispatcher;
            if (dispatcher != null && !dispatcher.HasThreadAccess)
            {
                if (Interlocked.Exchange(ref _skipCheckQueued, 1) == 0)
                {
                    var queued = dispatcher.TryEnqueue(() =>
                    {
                        Interlocked.Exchange(ref _skipCheckQueued, 0);
                        _ = ApplyChapterSkipsAsync();
                    });
                    if (!queued) Interlocked.Exchange(ref _skipCheckQueued, 0);
                }
                return;
            }

            if (_state != PlaybackState.Playing && _state != PlaybackState.Buffering) return;
            if (_chapters.Count == 0 || _currentChapterIndex < 0) return;

            var chapter = CurrentChapter;
            if (chapter == null) return;

            if (_skipChapterIndex != _currentChapterIndex)
                ResetChapterSkipState(_currentChapterIndex);

            await ApplyChapterIntroSkipAsync(chapter, _currentChapterIndex);
            await TrySkipOutroAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("[Playback] Chapter skip check failed", ex);
        }
    }

    /// <summary>
    /// Trims the first <see cref="AppSettings.SkipIntroSeconds"/> seconds of one chapter.
    /// Only while the position is still inside that window, at most a few times per chapter,
    /// and only when the target lies in the loaded track (otherwise the next tick, after the
    /// normal track change, takes care of it).
    /// </summary>
    private async Task ApplyChapterIntroSkipAsync(Chapter chapter, int chapterIndex)
    {
        var skip = Math.Max(0, _settingsService.Settings.SkipIntroSeconds);
        if (skip <= 0 || _introSkipBusy || _introSkipDone || chapterIndex < 0) return;

        // Never eat a chapter that is barely long enough to hold the skip window.
        if (chapter.Duration.TotalSeconds <= skip * 1.5) return;

        var introEnd = chapter.Start + skip;
        var position = Position.TotalSeconds;
        if (position < chapter.Start - 1) return;   // not in this chapter yet
        if (position >= introEnd - 1) { _introSkipDone = true; return; }  // already past it

        // The target must be inside the loaded file, otherwise skip from the timer would
        // have to open another file (that is what froze the app).
        if (introEnd > CurrentTrackEndSeconds - 0.35)
        {
            _introSkipDone = true;
            _logger.LogDebug($"[Playback] Skip intro deferred: target {introEnd:F0}s is in the next file");
            return;
        }

        if (_introSkipAttempts >= 10)
        {
            if (_introSkipAttempts == 10)
            {
                _introSkipAttempts++;
                _logger.LogWarning($"[Playback] Skip intro gave up on chapter #{chapterIndex + 1} " +
                                   $"(position {position:F0}s)");
            }
            return;
        }

        _introSkipAttempts++;
        _introSkipBusy = true;
        try
        {
            _logger.Log($"[Playback] Skip intro: {position:F0}s -> {introEnd:F0}s " +
                        $"(chapter #{chapterIndex + 1}, skip {skip}s)");
            if (SeekWithinCurrentTrack(introEnd))
                _introSkipDone = true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Playback] Skip intro failed", ex);
        }
        finally
        {
            _introSkipBusy = false;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Leaves the current chapter once fewer than <see cref="AppSettings.SkipOutroSeconds"/>
    /// seconds remain: jumps to the end of the loaded file and lets the normal track change
    /// go on to the next chapter (finishing the book from the last chapter).
    /// </summary>
    private async Task TrySkipOutroAsync()
    {
        if (_outroSkipDone || _outroSkipInProgress) return;

        var skip = Math.Max(0, _settingsService.Settings.SkipOutroSeconds);
        if (skip <= 0) return; // feature off

        var chapter = CurrentChapter;
        if (chapter == null || _currentChapterIndex < 0) return;

        var chapterLength = chapter.Duration.TotalSeconds;
        if (chapterLength <= 0 || chapterLength <= skip * 1.5) return; // don't gut a short chapter

        var position = Position.TotalSeconds;
        var remaining = chapter.End - position;
        if (remaining > skip || remaining < -2) return;

        _outroSkipInProgress = true;
        try
        {
            var trackEnd = CurrentTrackEndSeconds;

            if (_currentChapterIndex + 1 >= _chapters.Count && chapter.End >= trackEnd - 1)
            {
                _outroSkipDone = true;
                _logger.Log("[Playback] Skip outro: last chapter reached — finishing book");
                await FinishCurrentBookAsync();
                return;
            }

            // Chapter ends mid-file: jump to its end, the intro skip takes over from there.
            // Chapter ends with the file: jump just before the end so MediaEnded advances.
            var target = chapter.End >= trackEnd - 0.5 ? trackEnd - 0.35 : chapter.End;

            if (target <= position + 0.25)
            {
                _outroSkipDone = true;   // already there — the normal track change will run
                return;
            }

            _logger.Log($"[Playback] Skip outro: {position:F0}s -> {target:F0}s " +
                        $"({remaining:F0}s left of chapter #{_currentChapterIndex + 1})");
            if (SeekWithinCurrentTrack(target))
                _outroSkipDone = true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Playback] Skip outro failed", ex);
        }
        finally
        {
            _outroSkipInProgress = false;
        }

        await Task.CompletedTask;
    }

    /// <summary>Stops playback and reports the book as finished (end of last chapter).</summary>
    private async Task FinishCurrentBookAsync()
    {
        _positionTimer?.Stop();
        StopSessionSyncTimer();

        try
        {
            if (_isStreaming && _mediaPlayer != null)
                _mediaPlayer.Pause();
            else
                _outputDevice?.Pause();
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Finish pause failed: {ex.Message}");
        }

        SetState(PlaybackState.Stopped);
        UpdateSmtcPlaybackState(false);

        if (_currentAudioBook != null)
        {
            var total = Duration.TotalSeconds;
            await _syncService.FlushPlaybackProgressAsync(
                _currentAudioBook.Id, total, total, isFinished: true);
        }

        if (_currentSession != null)
            await CloseSessionAsync();
    }

    /// <summary>
    /// A resume position can arrive in the wrong unit or be plain corrupt (a previous
    /// build saved milliseconds, a third-party client wrote garbage). Trusting it makes
    /// the book open at an impossible time, which also hides the current chapter.
    /// Runs as soon as the real book duration is known.
    /// </summary>
    private async Task NormalizeResumePositionAsync(AudioBook audioBook)
    {
        var position = audioBook.CurrentTime.TotalSeconds;
        if (position <= 0) return;

        var duration = Duration.TotalSeconds;
        if (duration <= 0)
        {
            _logger.LogDebug($"[Playback] Resume position {position:F0}s kept (book duration unknown)");
            return;
        }

        // A little overshoot is metadata rounding, not a unit problem.
        if (position <= duration * 1.02) return;

        TimeSpan corrected;
        if (position / 1000.0 <= duration * 1.02)
        {
            corrected = TimeSpan.FromSeconds(position / 1000.0);
            _logger.LogWarning($"[Playback] Resume position looked like MILLISECONDS " +
                               $"({position:F0}s vs book {duration:F0}s) -> using {corrected}");
        }
        else
        {
            corrected = TimeSpan.Zero;
            _logger.LogWarning($"[Playback] Resume position {position:F0}s exceeds book duration " +
                               $"{duration:F0}s -> restarting from the beginning");
        }

        audioBook.CurrentTime = corrected;
        _notifications.ShowWarning(_localization.GetString("PositionCorrected"), _localization.GetString("Playback"));
        try
        {
            await _database.SavePlaybackProgressAsync(audioBook.Id, corrected, isFinished: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Persisting corrected position failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Chapters are supposed to be whole-book seconds. Libraries touched by older
    /// tooling can store them in milliseconds, which makes every jump land 1000x too
    /// far. Detect it against the book duration and rescale once.
    /// </summary>
    private void NormalizeChapterUnits()
    {
        if (_chapters.Count == 0) return;

        var duration = Duration.TotalSeconds;
        if (duration <= 0) return;

        var lastEnd = _chapters.Max(c => c.End > 0 ? c.End : c.Start);
        if (lastEnd <= duration * 1.02) return;
        if (lastEnd / 1000.0 > duration * 1.02) return; // genuinely inconsistent, leave it

        foreach (var chapter in _chapters)
        {
            chapter.Start /= 1000.0;
            chapter.End /= 1000.0;
        }

        _logger.LogWarning($"[Playback] Chapter times looked like milliseconds " +
                           $"(last end {lastEnd:F0} vs book {duration:F0}s) — rescaled");
    }

    private void LogLoadSummary(AudioBook audioBook)
    {
        var firstChapter = _chapters.Count > 0 ? _chapters[0].Start.ToString("0") : "-";
        var lastChapter = _chapters.Count > 0 ? _chapters[^1].Start.ToString("0") : "-";
        _logger.Log($"[Playback] Loaded '{audioBook.Title}': book {Duration}, " +
                    $"resume {audioBook.CurrentTime}, start track {_currentTrackIndex + 1}/{Math.Max(1, _trackDurations.Count)}, " +
                    $"chapters {_chapters.Count} (first {firstChapter}s, last {lastChapter}s)");
    }

    /// <summary>
    /// The saved track table (and the progress stitched to it) can be off by exactly
    /// 1000x when older builds or another client wrote milliseconds. One decoded file
    /// is enough to tell: if the expected length of this track is 1000x the real one,
    /// rescale the whole timeline so resume, chapters and durations agree again.
    /// </summary>
    private void RescaleTimelineIfNeeded(int trackIndex, TimeSpan actualDuration)
    {
        var actualSeconds = actualDuration.TotalSeconds;
        if (actualSeconds <= 0) return;
        if (trackIndex < 0 || trackIndex >= _trackDurations.Count) return;

        var previous = trackIndex == 0 ? 0 : _trackDurations[trackIndex - 1];
        var expected = _trackDurations[trackIndex] - previous;
        if (expected <= 0) return;

        var ratio = expected / actualSeconds;
        if (ratio < 100 || ratio > 10000) return; // not a unit mismatch

        for (var i = 0; i < _trackDurations.Count; i++)
            _trackDurations[i] /= 1000.0;

        foreach (var chapter in _chapters)
        {
            chapter.Start /= 1000.0;
            chapter.End /= 1000.0;
        }

        if (_currentAudioBook != null)
        {
            var fixedResume = TimeSpan.FromSeconds(_currentAudioBook.CurrentTime.TotalSeconds / 1000.0);
            _logger.LogWarning($"[Playback] Timeline looked 1000x too long (expected {expected:F0}s for a " +
                               $"{actualSeconds:F0}s track) — rescaled. Resume {_currentAudioBook.CurrentTime} -> {fixedResume}");
            _currentAudioBook.CurrentTime = fixedResume;
            _ = PersistCorrectedProgressAsync();
        }

        _logger.Log($"[Playback] Rescaled book duration: {Duration}");
    }

    /// <summary>Writes a self-healed resume position back so the book is not re-broken on next open.</summary>
    private async Task PersistCorrectedProgressAsync()
    {
        try
        {
            if (_currentAudioBook == null) return;
            await _database.SavePlaybackProgressAsync(_currentAudioBook.Id, _currentAudioBook.CurrentTime, isFinished: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Persisting corrected progress failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Chapter times and the per-file track table are two independent estimates of the same
    /// timeline. On a multi-file book (typically one file per chapter) integer rounding drifts
    /// a second or two per file, so by chapter 300 the two disagree by minutes — a jump then
    /// looks like it lands in the wrong chapter. When the counts line up 1:1 the files are the
    /// ground truth (they are what the player actually seeks in), so use them.
    /// </summary>
    private void AlignChaptersToTracks()
    {
        if (_chapters.Count == 0 || _trackDurations.Count == 0) return;
        if (_chapters.Count != _trackDurations.Count) return;

        var changed = 0;
        for (var i = 0; i < _chapters.Count; i++)
        {
            var start = i == 0 ? 0 : _trackDurations[i - 1];
            var end = _trackDurations[i];

            if (Math.Abs(_chapters[i].Start - start) > 0.5 || Math.Abs(_chapters[i].End - end) > 0.5)
                changed++;

            _chapters[i].Start = start;
            _chapters[i].End = end;
        }

        if (changed > 0)
            _logger.Log($"[Playback] Chapters aligned to the file table: {_chapters.Count} rows, " +
                        $"{changed} differed by more than 0.5s");
        else
            _logger.LogDebug("[Playback] Chapters already match the file table");
    }

    /// <summary>
    /// The cumulative track table is built from DB metadata, which can be missing or
    /// stale (many library endpoints omit per-file duration). The decoder knows the
    /// truth for the file we just opened, so shift the remaining entries by the delta —
    /// that keeps both the total duration and every chapter offset honest.
    /// </summary>
    private void ReconcileTrackDuration(int trackIndex, TimeSpan actualDuration)
    {
        if (actualDuration.TotalSeconds <= 0) return;
        if (trackIndex < 0 || trackIndex >= _trackDurations.Count) return;

        var previous = trackIndex == 0 ? 0 : _trackDurations[trackIndex - 1];
        var expected = previous + actualDuration.TotalSeconds;
        var delta = expected - _trackDurations[trackIndex];

        if (Math.Abs(delta) < 1.0) return; // already right within a second

        for (var i = trackIndex; i < _trackDurations.Count; i++)
            _trackDurations[i] += delta;

        // Chapter times were derived from this table, so they have to follow it.
        if (_chapters.Count == _trackDurations.Count)
            AlignChaptersToTracks();

        _logger.Log($"[Playback] Track {trackIndex + 1} duration corrected by {delta:+0.0;-0.0}s " +
                    $"(book total now {TimeSpan.FromSeconds(_trackDurations.Last())})");
    }

    // Multi-track helpers — delegate to ITrackManager
    private void BuildLocalTrackList(AudioBook audioBook, string primaryPath)
    {
        _trackPaths.Clear();
        _trackDurations.Clear();

        var result = _trackManager.BuildLocalTrackList(audioBook, primaryPath);
        _trackPaths.AddRange(result.Paths);
        _trackDurations.AddRange(result.CumulativeDurations);

        // Metadata from the server often omits per-file duration. A zero/duplicate step
        // makes every offset (starting track, chapter seek, total duration) wrong, so
        // probe the real durations from the files themselves before anything else runs.
        if (_trackDurations.Count > 1 && HasBrokenDurations(_trackDurations))
            RebuildDurationsFromFiles();

        _logger.Log($"[Playback] Built track list: {_trackPaths.Count} local files, total {Duration}");
    }

    private static bool HasBrokenDurations(IReadOnlyList<double> cumulative)
    {
        var previous = 0.0;
        foreach (var value in cumulative)
        {
            if (value <= previous) return true; // not strictly increasing
            previous = value;
        }
        return false;
    }

    /// <summary>Reads real track lengths with the decoder and rebuilds the cumulative table.</summary>
    private void RebuildDurationsFromFiles()
    {
        var rebuilt = new List<double>(_trackPaths.Count);
        double cumulative = 0;

        foreach (var path in _trackPaths)
        {
            double seconds = 0;
            try
            {
                using var reader = new AudioFileReader(path);
                seconds = reader.TotalTime.TotalSeconds;
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[Playback] Duration probe failed for {Path.GetFileName(path)}: {ex.Message}");
            }

            cumulative += Math.Max(0, seconds);
            rebuilt.Add(cumulative);
        }

        _trackDurations.Clear();
        _trackDurations.AddRange(rebuilt);
        _logger.Log($"[Playback] Rebuilt {rebuilt.Count} track durations from disk (total {Duration})");
    }

    private void BuildStreamTrackDurations()
    {
        _trackDurations.Clear();
        var durations = _trackManager.BuildStreamTrackDurations(_streamTracks);
        _trackDurations.AddRange(durations);
    }

    private void DetermineStartingTrack(TimeSpan currentTime)
    {
        _currentTrackIndex = _trackManager.DetermineStartingTrack(_trackDurations, currentTime);
    }

    private TimeSpan GetWithinTrackOffset(TimeSpan overallPosition)
    {
        return _trackManager.GetWithinTrackOffset(_currentTrackIndex, _trackDurations, overallPosition);
    }

    private async Task CloseSessionAsync()
    {
        if (_currentSession == null) return;
        try
        {
            await _apiService.ClosePlaybackSessionAsync(_currentSession.Id);
            _logger.Log($"[Playback] Session closed");
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Session close failed: {ex.Message}");
        }
        _currentSession = null;
    }

    // === SMTC (System Media Transport Controls) ===

    private void SetupSmtc(AudioBook book)
    {
        try
        {
            if (_isStreaming && _mediaPlayer != null)
            {
                // Streaming MediaPlayer has built-in SMTC — configure display + controls
                ConfigureSmtcForMediaPlayer(_mediaPlayer, book);
                _logger.Log("[SMTC] Configured on streaming MediaPlayer");
                return;
            }

            // NAudio path: create a background MediaPlayer solely for SMTC
            UnwireSmtcEvents();
            _smtcPlayer?.Dispose();
            _smtcPlayer = new Windows.Media.Playback.MediaPlayer
            {
                AutoPlay = false,
                Volume = 0 // silent — only used for SMTC registration
            };

            ConfigureSmtcForMediaPlayer(_smtcPlayer, book);
            WireSmtcEvents();

            _logger.Log("[SMTC] Configured via background MediaPlayer for NAudio");
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[SMTC] Setup failed (non-fatal): {ex.Message}");
        }
    }

    private void ConfigureSmtcForMediaPlayer(Windows.Media.Playback.MediaPlayer player, AudioBook book)
    {
        player.CommandManager.IsEnabled = true;
        player.SystemMediaTransportControls.IsEnabled = true;
        player.SystemMediaTransportControls.IsPlayEnabled = true;
        player.SystemMediaTransportControls.IsPauseEnabled = true;
        player.SystemMediaTransportControls.IsNextEnabled = true;
        player.SystemMediaTransportControls.IsPreviousEnabled = true;

        var updater = player.SystemMediaTransportControls.DisplayUpdater;
        updater.Type = Windows.Media.MediaPlaybackType.Music;
        updater.MusicProperties.Title = book.Title ?? "Unknown";
        updater.MusicProperties.Artist = book.Author ?? "Unknown";
        updater.Update();
    }

    private void UpdateSmtcPlaybackState(bool isPlaying)
    {
        try
        {
            var smtc = _smtcPlayer?.SystemMediaTransportControls
                       ?? (_isStreaming ? _mediaPlayer?.SystemMediaTransportControls : null);
            if (smtc != null)
            {
                smtc.PlaybackStatus = isPlaying
                    ? Windows.Media.MediaPlaybackStatus.Playing
                    : Windows.Media.MediaPlaybackStatus.Paused;
            }
        }
        catch { /* non-fatal */ }
    }

    private void WireSmtcEvents()
    {
        if (_smtcPlayer == null) return;
        _smtcPlayer.CommandManager.PlayReceived += OnSmtcPlayReceived;
        _smtcPlayer.CommandManager.PauseReceived += OnSmtcPauseReceived;
        _smtcPlayer.CommandManager.NextReceived += OnSmtcNextReceived;
        _smtcPlayer.CommandManager.PreviousReceived += OnSmtcPreviousReceived;
    }

    private void UnwireSmtcEvents()
    {
        if (_smtcPlayer == null) return;
        _smtcPlayer.CommandManager.PlayReceived -= OnSmtcPlayReceived;
        _smtcPlayer.CommandManager.PauseReceived -= OnSmtcPauseReceived;
        _smtcPlayer.CommandManager.NextReceived -= OnSmtcNextReceived;
        _smtcPlayer.CommandManager.PreviousReceived -= OnSmtcPreviousReceived;
    }

    private void OnSmtcPlayReceived(Windows.Media.Playback.MediaPlaybackCommandManager sender,
        Windows.Media.Playback.MediaPlaybackCommandManagerPlayReceivedEventArgs args) => _ = PlayAsync();

    private void OnSmtcPauseReceived(Windows.Media.Playback.MediaPlaybackCommandManager sender,
        Windows.Media.Playback.MediaPlaybackCommandManagerPauseReceivedEventArgs args) => _ = PauseAsync();

    private void OnSmtcNextReceived(Windows.Media.Playback.MediaPlaybackCommandManager sender,
        Windows.Media.Playback.MediaPlaybackCommandManagerNextReceivedEventArgs args)
    {
        var pos = Position + TimeSpan.FromSeconds(30);
        if (pos > Duration) pos = Duration;
        _ = SeekAsync(pos);
    }

    private void OnSmtcPreviousReceived(Windows.Media.Playback.MediaPlaybackCommandManager sender,
        Windows.Media.Playback.MediaPlaybackCommandManagerPreviousReceivedEventArgs args)
    {
        var pos = Position - TimeSpan.FromSeconds(10);
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        _ = SeekAsync(pos);
    }

    private async Task<bool> ResolveInitialProgressAsync(AudioBook audioBook, CancellationToken ct)
    {
        var originalPosition = audioBook.CurrentTime;
        var bestPosition = originalPosition;
        DateTime? bestTimestamp = null;
        bool authoritativeResolved = false;

        try
        {
            var local = await _database.GetPlaybackProgressWithTimestampAsync(audioBook.Id);
            if (local.HasValue)
            {
                bestPosition = local.Value.position;
                bestTimestamp = local.Value.updatedAt;
                authoritativeResolved = true;
                _startupProgressStatus = StartupProgressStatus.Resolved;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug($"[Playback] Local startup progress lookup failed: {ex.Message}");
        }

        if (_apiService.IsAuthenticated)
        {
            try
            {
                var progressTask = _apiService.GetUserProgressAsync(audioBook.Id);
                var completed = await Task.WhenAny(progressTask, Task.Delay(TimeSpan.FromSeconds(4), ct));
                if (completed == progressTask)
                {
                    var server = await progressTask;
                    if (server != null)
                    {
                        // Strictly newer wins — on ties, local is preserved
                        if (!bestTimestamp.HasValue || server.LastUpdate > bestTimestamp.Value)
                        {
                            _logger.Log($"[Playback] POSITION CHANGE (startup): server is newer ({server.LastUpdate:O} > {bestTimestamp:O}), using server position {server.CurrentTime}");
                            bestPosition = server.CurrentTime;
                            bestTimestamp = server.LastUpdate;
                        }
                        else
                        {
                            _logger.LogDebug($"[Playback] Startup: keeping local position {bestPosition} (local {bestTimestamp:O} >= server {server.LastUpdate:O})");
                        }

                        authoritativeResolved = true;
                        _startupProgressStatus = StartupProgressStatus.Resolved;
                    }
                }
                else
                {
                    _startupProgressStatus = StartupProgressStatus.TimedOut;
                    _logger.LogWarning("[Playback] Startup progress fetch timed out (4s); blocking near-zero uploads until playback advances");
                }
            }
            catch (Exception ex)
            {
                _startupProgressStatus = StartupProgressStatus.Failed;
                _logger.LogDebug($"[Playback] Startup progress fetch failed: {ex.Message}");
            }
        }

        if (bestPosition != originalPosition)
        {
            _logger.Log($"[Playback] Startup progress applied: {bestPosition} (was {originalPosition})");
            audioBook.CurrentTime = bestPosition;
        }

        return authoritativeResolved;
    }

    private bool ShouldBlockNearZeroSync(double currentTime, bool isFinished)
    {
        if (isFinished) return false;
        if (_startupProgressStatus == StartupProgressStatus.Resolved) return false;

        if (!_allowNearZeroProgressSync)
        {
            if (currentTime >= ProgressSyncGuardSeconds)
            {
                _allowNearZeroProgressSync = true;
                return false;
            }

            return true;
        }

        return false;
    }

    public void Dispose()
    {
        StopSessionSyncTimer();

        // Unwire all events before disposing to prevent leaks
        UnwireNAudioEvents();
        UnwireMediaPlayerEvents();
        UnwireSmtcEvents();

        // Stop and dispose timer
        if (_positionTimer != null)
        {
            _positionTimer.Elapsed -= OnPositionTimerElapsed;
            _positionTimer.Stop();
            _positionTimer.Dispose();
            _positionTimer = null;
        }

        // Dispose NAudio
        if (_outputDevice != null)
        {
            try { _outputDevice.Stop(); } catch { /* best-effort: ignore */ }
            _outputDevice.Dispose();
            _outputDevice = null;
        }
        _audioFileReader?.Dispose();
        _audioFileReader = null;

        // Dispose MediaPlayers
        if (_mediaPlayer != null)
        {
            try
            {
                _mediaPlayer.Pause();
                _mediaPlayer.Source = null;
            }
            catch { /* best-effort: ignore */ }
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        if (_smtcPlayer != null)
        {
            try
            {
                _smtcPlayer.Pause();
                _smtcPlayer.Source = null;
            }
            catch { /* best-effort: ignore */ }
            _smtcPlayer.Dispose();
            _smtcPlayer = null;
        }

        // Dispose cancellation token source
        _loadCts?.Dispose();
        _loadCts = null;
        _tempDownloadClient?.Dispose();
    }
}
