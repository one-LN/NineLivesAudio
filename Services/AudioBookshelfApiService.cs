using CommunityToolkit.Mvvm.Messaging;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineLivesAudio.Services;

public class AudioBookshelfApiService : IAudioBookshelfApiService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly HttpClient _httpClient;
    private string? _authToken;
    private string? _serverUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsAuthenticated => !string.IsNullOrEmpty(_authToken);
    public string? ServerUrl => _serverUrl;

    public string? LastError { get; private set; }

    // Event migrated to WeakReferenceMessenger: ReconnectionAttemptedMessage

    public AudioBookshelfApiService(ISettingsService settingsService, ILocalizationService localization)
    {
        _settingsService = settingsService;
        _localization = localization;

        var handler = new HttpClientHandler
        {
            // Scoped SSL bypass: only trust self-signed certs for the configured server host
            ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            {
                if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
                    return true;

                // Only bypass if user opted in AND this is the configured server
                if (_settingsService.Settings.AllowSelfSignedCertificates
                    && !string.IsNullOrEmpty(_serverUrl)
                    && message.RequestUri != null)
                {
                    var configuredHost = new Uri(_serverUrl).Host;
                    return string.Equals(message.RequestUri.Host, configuredHost, StringComparison.OrdinalIgnoreCase);
                }

                return false; // Default: reject invalid certs
            }
        };

        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        // Use 5-minute timeout for downloads, individual operations can override with CancellationToken
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    private async Task InitializeFromSettingsAsync()
    {
        var token = await _settingsService.GetAuthTokenAsync();
        var settings = _settingsService.Settings;

        if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(settings.ServerUrl))
        {
            _authToken = token;
            _serverUrl = settings.ServerUrl;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<bool> LoginAsync(string serverUrl, string username, string password)
    {
        try
        {
            _serverUrl = serverUrl.TrimEnd('/');

            // Per-request timeout via CTS — never mutate the shared HttpClient.Timeout,
            // which would break concurrent downloads/syncs that rely on the 5-minute default.
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var loginData = new { username, password };
            var jsonPayload = JsonSerializer.Serialize(loginData, JsonOptions);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // NOTE: never log the request payload (contains the password) or the
            // response body (contains the auth token).
            var response = await _httpClient.PostAsync($"{_serverUrl}/login", content, timeoutCts.Token);

            var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                LastError = _localization.Format("LoginFailedStatus", (int)response.StatusCode);
                return false;
            }

            var loginResponse = JsonSerializer.Deserialize<LoginResponse>(responseContent, JsonOptions);

            if (loginResponse?.User?.Token == null)
            {
                LastError = _localization.GetString("LoginNoToken");
                return false;
            }

            _authToken = loginResponse.User.Token;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);

            // Save credentials
            await _settingsService.SaveAuthTokenAsync(_authToken);
            var settings = _settingsService.Settings;
            settings.ServerUrl = _serverUrl;
            settings.Username = username;
            await _settingsService.SaveSettingsAsync();

            LastError = null;
            return true;
        }
        catch (HttpRequestException ex)
        {
            LastError = _localization.Format("ConnectionFailedDetail", ex.Message);
            return false;
        }
        catch (TaskCanceledException)
        {
            LastError = _localization.GetString("ConnectionTimeout");
            return false;
        }
        catch (Exception ex)
        {
            LastError = _localization.Format("UnexpectedError", ex.Message);
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        _authToken = null;
        _serverUrl = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        await _settingsService.ClearAuthTokenAsync();
    }

    public async Task<bool> ValidateTokenAsync()
    {
        try
        {
            // If not authenticated, try to restore from saved credentials first
            if (!IsAuthenticated || string.IsNullOrEmpty(_serverUrl))
            {
                await InitializeFromSettingsAsync();
            }

            if (!IsAuthenticated || string.IsNullOrEmpty(_serverUrl))
                return false;

            var response = await _httpClient.GetAsync($"{_serverUrl}/api/me");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task<List<Library>> GetLibrariesAsync()
    {
        EnsureAuthenticated();

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/libraries");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<LibrariesResponse>(json, JsonOptions);

            return apiResponse?.Libraries?.Select(l => new Library
            {
                Id = l.Id,
                Name = l.Name,
                DisplayOrder = l.DisplayOrder,
                Icon = l.Icon ?? "audiobook",
                MediaType = l.MediaType ?? "book",
                Folders = l.Folders?.Select(f => new Folder
                {
                    Id = f.Id,
                    FullPath = f.FullPath,
                    LibraryId = l.Id
                }).ToList() ?? new List<Folder>()
            }).ToList() ?? new List<Library>();
        }
        catch (Exception ex)
        {
            return new List<Library>();
        }
    }

    public async Task<List<AudioBook>> GetLibraryItemsAsync(string libraryId, int limit = 100, int page = 0)
    {
        EnsureAuthenticated();

        try
        {
            var allItems = new List<AudioBook>();
            int currentPage = page;

            while (true)
            {
                var response = await _httpClient.GetAsync(
                    $"{_serverUrl}/api/libraries/{libraryId}/items?limit={limit}&page={currentPage}&minified=0");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<LibraryItemsResponse>(json, JsonOptions);

                if (apiResponse?.Results == null || apiResponse.Results.Count == 0)
                    break;

                allItems.AddRange(apiResponse.Results.Select(MapToAudioBook));

                // Stop if we've fetched all items
                if (allItems.Count >= apiResponse.Total)
                    break;

                currentPage++;
            }

            return allItems;
        }
        catch (Exception ex)
        {
            return new List<AudioBook>();
        }
    }

    public async Task<AudioBook?> GetAudioBookAsync(string itemId)
    {
        EnsureAuthenticated();

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/items/{itemId}?expanded=1");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var item = JsonSerializer.Deserialize<ApiLibraryItem>(json, JsonOptions);

            return item != null ? MapToAudioBook(item) : null;
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    public async Task<Stream> GetAudioFileStreamAsync(string itemId, string fileIno)
    {
        EnsureAuthenticated();


        var url = $"{_serverUrl}/api/items/{itemId}/file/{fileIno}";

        // Use the single shared HttpClient with ResponseHeadersRead for streaming
        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync();
        return new ResponseStream(stream, response);
    }

    /// <summary>
    /// Wraps a stream and its owning HttpResponseMessage so both are disposed together.
    /// </summary>
    private class ResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public ResponseStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => _inner.ReadAsync(buffer, offset, count, ct);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public async Task<byte[]?> GetCoverImageAsync(string itemId, int? width = null, int? height = null)
    {
        EnsureAuthenticated();

        try
        {
            var url = $"{_serverUrl}/api/items/{itemId}/cover";
            if (width.HasValue || height.HasValue)
            {
                var queryParams = new List<string>();
                if (width.HasValue) queryParams.Add($"width={width}");
                if (height.HasValue) queryParams.Add($"height={height}");
                url += "?" + string.Join("&", queryParams);
            }

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    public async Task<PlaybackSessionInfo?> StartPlaybackSessionAsync(string itemId)
    {
        EnsureAuthenticated();

        try
        {
            var requestData = new
            {
                deviceInfo = new
                {
                    clientName = "NineLivesAudio",
                    deviceId = Environment.MachineName
                },
                supportedMimeTypes = new[] { "audio/mpeg", "audio/mp4", "audio/ogg", "audio/flac" }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestData, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_serverUrl}/api/items/{itemId}/play", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var session = JsonSerializer.Deserialize<ApiPlaybackSession>(json, JsonOptions);

            if (session == null) return null;

            return new PlaybackSessionInfo
            {
                Id = session.Id,
                ItemId = session.LibraryItemId,
                EpisodeId = session.EpisodeId,
                CurrentTime = session.CurrentTime,
                Duration = session.Duration,
                MediaType = session.MediaType ?? "book",
                AudioTracks = session.AudioTracks?.Select(t => new AudioStreamInfo
                {
                    Index = t.Index,
                    Codec = t.Codec ?? "mp3",
                    Title = t.Title,
                    Duration = t.Duration,
                    ContentUrl = t.ContentUrl?.StartsWith("http") == true
                        ? $"{t.ContentUrl}?token={_authToken}"
                        : $"{_serverUrl}{t.ContentUrl}?token={_authToken}"
                }).ToList() ?? new List<AudioStreamInfo>(),
                Chapters = session.Chapters?.Select(c => new Chapter
                {
                    Id = c.Id,
                    Start = c.Start,
                    End = c.End,
                    Title = c.Title
                }).ToList() ?? new List<Chapter>()
            };
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    public async Task<bool> UpdateProgressAsync(string itemId, double currentTime, bool isFinished = false)
    {
        EnsureAuthenticated();

        try
        {
            var progressData = new
            {
                currentTime,
                isFinished,
                progress = currentTime // This will be calculated on server based on duration
            };

            var content = new StringContent(JsonSerializer.Serialize(progressData, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PatchAsync($"{_serverUrl}/api/me/progress/{itemId}", content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task<bool> SyncSessionProgressAsync(string sessionId, double currentTime, double duration, double timeListened = 0)
    {
        EnsureAuthenticated();

        try
        {
            var syncData = new
            {
                currentTime,
                duration,
                timeListened
            };

            var content = new StringContent(JsonSerializer.Serialize(syncData, JsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_serverUrl}/api/session/{sessionId}/sync", content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task ClosePlaybackSessionAsync(string sessionId)
    {
        EnsureAuthenticated();

        try
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            await _httpClient.PostAsync($"{_serverUrl}/api/session/{sessionId}/close", content);
        }
        catch (Exception ex)
        {
        }
    }

    public async Task<UserProgress?> GetUserProgressAsync(string itemId)
    {
        EnsureAuthenticated();

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/me/progress/{itemId}");
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var progress = JsonSerializer.Deserialize<ApiUserProgress>(json, JsonOptions);

            if (progress == null) return null;

            return new UserProgress
            {
                LibraryItemId = progress.LibraryItemId,
                CurrentTime = TimeSpan.FromSeconds(progress.CurrentTime),
                Progress = progress.Progress,
                IsFinished = progress.IsFinished,
                LastUpdate = DateTimeOffset.FromUnixTimeMilliseconds(progress.LastUpdate).DateTime
            };
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    public async Task<List<UserProgress>> GetAllUserProgressAsync()
    {
        EnsureAuthenticated();

        try
        {
            // Use /api/me which returns the user object with mediaProgress array
            // (NOT /api/me/items-in-progress which returns library items without progress values)
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/me");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var me = JsonSerializer.Deserialize<ApiMeResponse>(json, JsonOptions);

            return me?.MediaProgress?
                .Where(p => !string.IsNullOrEmpty(p.LibraryItemId))
                .Select(p => new UserProgress
                {
                    LibraryItemId = p.LibraryItemId,
                    CurrentTime = TimeSpan.FromSeconds(p.CurrentTime),
                    Progress = p.Progress,
                    IsFinished = p.IsFinished,
                    LastUpdate = p.LastUpdate > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(p.LastUpdate).DateTime
                        : DateTime.MinValue
                }).ToList() ?? new List<UserProgress>();
        }
        catch (Exception ex)
        {
            return new List<UserProgress>();
        }
    }

    public async Task<List<Bookmark>> GetBookmarksAsync(string itemId)
    {
        EnsureAuthenticated();

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/me");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var me = JsonSerializer.Deserialize<ApiMeResponse>(json, JsonOptions);

            return me?.Bookmarks?
                .Where(b => b.LibraryItemId == itemId)
                .OrderBy(b => b.Time)
                .Select(b => new Bookmark
                {
                    Id = b.Id,
                    LibraryItemId = b.LibraryItemId,
                    Title = b.Title,
                    Time = b.Time,
                    CreatedAt = b.CreatedAt > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(b.CreatedAt).DateTime
                        : DateTime.MinValue
                }).ToList() ?? new List<Bookmark>();
        }
        catch (Exception ex)
        {
            return new List<Bookmark>();
        }
    }

    public async Task<bool> CreateBookmarkAsync(string itemId, string title, double time)
    {
        EnsureAuthenticated();

        try
        {
            var bookmarkData = new { title, time };
            var content = new StringContent(
                JsonSerializer.Serialize(bookmarkData, JsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                $"{_serverUrl}/api/me/item/{itemId}/bookmark", content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task<bool> DeleteBookmarkAsync(string itemId, double time)
    {
        EnsureAuthenticated();

        try
        {
            var response = await _httpClient.DeleteAsync(
                $"{_serverUrl}/api/me/item/{itemId}/bookmark/{time}");

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task<bool> TryReconnectAsync()
    {
        // Attempt 1: Try current server URL
        if (!string.IsNullOrEmpty(_serverUrl) && !string.IsNullOrEmpty(_authToken))
        {
            int[] delaysSeconds = [5, 15, 30, 60];
            for (int attempt = 0; attempt < delaysSeconds.Length; attempt++)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{_serverUrl}/api/me");
                    if (response.IsSuccessStatusCode)
                    {
                        WeakReferenceMessenger.Default.Send(new ReconnectionAttemptedMessage(new ReconnectionEventArgs
                        {
                            Success = true,
                            ServerUrl = _serverUrl,
                            AttemptNumber = attempt + 1
                        }));
                        return true;
                    }
                }
                catch { /* continue retry */ }

                WeakReferenceMessenger.Default.Send(new ReconnectionAttemptedMessage(new ReconnectionEventArgs
                {
                    Success = false,
                    ServerUrl = _serverUrl,
                    ErrorMessage = $"Attempt {attempt + 1} failed, retrying in {delaysSeconds[attempt]}s",
                    AttemptNumber = attempt + 1
                }));

                await Task.Delay(TimeSpan.FromSeconds(delaysSeconds[attempt]));
            }
        }

        // Attempt 2: Try alternate server profiles
        var profiles = _settingsService.Settings.ServerProfiles;
        foreach (var profile in profiles.Where(p => p.Url != _serverUrl))
        {
            try
            {
                var testUrl = profile.Url.TrimEnd('/');
                var response = await _httpClient.GetAsync($"{testUrl}/api/me");
                if (response.IsSuccessStatusCode)
                {
                    _serverUrl = testUrl;
                    profile.LastConnected = DateTime.UtcNow;
                    WeakReferenceMessenger.Default.Send(new ReconnectionAttemptedMessage(new ReconnectionEventArgs
                    {
                        Success = true,
                        ServerUrl = testUrl,
                        AttemptNumber = 0
                    }));
                    return true;
                }
            }
            catch { /* try next profile */ }
        }

        WeakReferenceMessenger.Default.Send(new ReconnectionAttemptedMessage(new ReconnectionEventArgs
        {
            Success = false,
            ErrorMessage = _localization.GetString("ReconnectExhausted")
        }));
        return false;
    }

    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
    }

    private AudioBook MapToAudioBook(ApiLibraryItem item)
    {
        var metadata = item.Media?.Metadata;
        var audioFiles = item.Media?.AudioFiles ?? new List<ApiAudioFile>();
        var firstSeries = metadata?.Series?.FirstOrDefault();

        return new AudioBook
        {
            Id = item.Id,
            Title = metadata?.Title ?? _localization.GetString("UnknownTitle"),
            Author = metadata?.AuthorName ?? metadata?.Authors?.FirstOrDefault()?.Name ?? _localization.GetString("UnknownAuthor"),
            Narrator = metadata?.NarratorName ?? metadata?.Narrators?.FirstOrDefault(),
            Description = metadata?.Description,
            CoverPath = !string.IsNullOrEmpty(item.Media?.CoverPath)
                ? $"{_serverUrl}/api/items/{item.Id}/cover?token={_authToken}"
                : null,
            Duration = TimeSpan.FromSeconds(item.Media?.Duration ?? 0),
            AddedAt = item.AddedAt.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(item.AddedAt.Value).DateTime
                : null,
            SeriesName = firstSeries?.Name ?? metadata?.SeriesName,
            SeriesSequence = firstSeries?.Sequence,
            Genres = metadata?.Genres ?? new List<string>(),
            Tags = metadata?.Tags ?? new List<string>(),
            AudioFiles = audioFiles.Select((af, idx) => new AudioFile
            {
                Id = af.Ino ?? idx.ToString(),
                Ino = af.Ino ?? string.Empty,
                Index = af.Index ?? idx,
                Duration = TimeSpan.FromSeconds(af.Duration ?? 0),
                Filename = af.Metadata?.Filename ?? $"track_{idx + 1}",
                MimeType = af.MimeType,
                Size = af.Metadata?.Size ?? 0
            }).ToList(),
            Chapters = item.Media?.Chapters?.Select(c => new Chapter
            {
                Id = c.Id,
                Start = c.Start,
                End = c.End,
                Title = c.Title
            }).ToList() ?? new List<Chapter>(),
            CurrentTime = TimeSpan.FromSeconds(item.UserMediaProgress?.CurrentTime ?? 0),
            Progress = item.UserMediaProgress?.Progress ?? 0,
            IsFinished = item.UserMediaProgress?.IsFinished ?? false
        };
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    // API Response DTOs
    private class LoginResponse
    {
        public ApiUser? User { get; set; }
    }

    private class ApiUser
    {
        public string? Id { get; set; }
        public string? Username { get; set; }
        public string? Token { get; set; }
    }

    private class LibrariesResponse
    {
        public List<ApiLibrary>? Libraries { get; set; }
    }

    private class ApiLibrary
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public string? Icon { get; set; }
        public string? MediaType { get; set; }
        public List<ApiFolder>? Folders { get; set; }
    }

    private class ApiFolder
    {
        public string Id { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }

    private class LibraryItemsResponse
    {
        public List<ApiLibraryItem>? Results { get; set; }
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Page { get; set; }
    }

    private class ApiLibraryItem
    {
        public string Id { get; set; } = string.Empty;
        public long? AddedAt { get; set; }
        public ApiMedia? Media { get; set; }
        public ApiUserMediaProgress? UserMediaProgress { get; set; }
    }

    private class ApiMedia
    {
        public ApiMetadata? Metadata { get; set; }
        public string? CoverPath { get; set; }
        public double? Duration { get; set; }
        public List<ApiAudioFile>? AudioFiles { get; set; }
        public List<ApiChapter>? Chapters { get; set; }
    }

    private class ApiMetadata
    {
        public string? Title { get; set; }
        public string? AuthorName { get; set; }
        public string? NarratorName { get; set; }
        public string? Description { get; set; }
        public List<ApiAuthor>? Authors { get; set; }
        public List<string>? Narrators { get; set; }
        public string? SeriesName { get; set; }

        [JsonConverter(typeof(SeriesConverter))]
        public List<ApiSeries>? Series { get; set; }
        public List<string>? Genres { get; set; }
        public List<string>? Tags { get; set; }
    }

    private class ApiSeries
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Sequence { get; set; }
    }

    /// <summary>
    /// Handles both single object and array formats for the series field.
    /// Library items endpoint returns a single object; expanded item returns an array.
    /// </summary>
    private class SeriesConverter : JsonConverter<List<ApiSeries>?>
    {
        public override List<ApiSeries>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                return JsonSerializer.Deserialize<List<ApiSeries>>(ref reader, options);
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                var single = JsonSerializer.Deserialize<ApiSeries>(ref reader, options);
                return single != null ? new List<ApiSeries> { single } : new List<ApiSeries>();
            }

            // Skip unexpected token types
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, List<ApiSeries>? value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    private class ApiAuthor
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    private class ApiAudioFile
    {
        public string? Ino { get; set; }
        public int? Index { get; set; }
        public double? Duration { get; set; }
        public string? MimeType { get; set; }
        public ApiFileMetadata? Metadata { get; set; }
    }

    private class ApiFileMetadata
    {
        public string? Filename { get; set; }
        public long? Size { get; set; }
    }

    private class ApiUserMediaProgress
    {
        public double? CurrentTime { get; set; }
        public double? Progress { get; set; }
        public bool? IsFinished { get; set; }
    }

    private class ApiChapter
    {
        public int Id { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private class ApiPlaybackSession
    {
        public string Id { get; set; } = string.Empty;
        public string LibraryItemId { get; set; } = string.Empty;
        public string? EpisodeId { get; set; }
        public double CurrentTime { get; set; }
        public double Duration { get; set; }
        public string? MediaType { get; set; }
        public List<ApiAudioTrack>? AudioTracks { get; set; }
        public List<ApiChapter>? Chapters { get; set; }
    }

    private class ApiAudioTrack
    {
        public int Index { get; set; }
        public string? Codec { get; set; }
        public string? Title { get; set; }
        public double Duration { get; set; }
        public string ContentUrl { get; set; } = string.Empty;
    }

    private class ApiUserProgress
    {
        public string LibraryItemId { get; set; } = string.Empty;
        public double CurrentTime { get; set; }
        public double Progress { get; set; }
        public bool IsFinished { get; set; }
        public long LastUpdate { get; set; }
    }

    private class ApiMeResponse
    {
        public List<ApiMeMediaProgress>? MediaProgress { get; set; }
        public List<ApiBookmark>? Bookmarks { get; set; }
    }

    private class ApiBookmark
    {
        public string Id { get; set; } = string.Empty;
        public string LibraryItemId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public double Time { get; set; }
        public long CreatedAt { get; set; }
    }

    private class ApiMeMediaProgress
    {
        public string Id { get; set; } = string.Empty;
        public string LibraryItemId { get; set; } = string.Empty;
        public string? EpisodeId { get; set; }
        public double Duration { get; set; }
        public double Progress { get; set; }
        public double CurrentTime { get; set; }
        public bool IsFinished { get; set; }
        public bool HideFromContinueListening { get; set; }
        public long LastUpdate { get; set; }
        public long StartedAt { get; set; }
        public long? FinishedAt { get; set; }
    }
}
