using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal sealed record LeaderboardEntry(string Uuid, string Name, double Value);

internal sealed record LeaderboardBoardsResult(
    Dictionary<string, IReadOnlyList<LeaderboardEntry>> Boards,
    bool FromCache,
    DateTimeOffset? CachedAtUtc);

internal sealed class LeaderboardClient
{
    private const string ApiBaseUrl = "https://stats.ahuai.top";
    private static readonly TimeSpan LeaderboardReadTimeout = TimeSpan.FromSeconds(8);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _boardsCachePath;
    private BoardsCacheFile? _boardsCache;

    public LeaderboardClient(string dataDirectory)
    {
        _boardsCachePath = Path.Combine(dataDirectory, "leaderboard_cache.json");
        AtomicFile.TryDeserialize(_boardsCachePath, out _boardsCache);
    }

    public async Task<string> GetOrCreateUuidAsync(string fingerprint)
    {
        await _lock.WaitAsync();
        try
        {
            using HttpResponseMessage response = await Http.PostAsJsonAsync(
                $"{ApiBaseUrl}/api/device", new { fingerprint });
            response.EnsureSuccessStatusCode();
            DeviceResponse device = await response.Content.ReadFromJsonAsync<DeviceResponse>()
                ?? throw new JsonException("设备接口返回为空。");
            return string.IsNullOrWhiteSpace(device.Uuid)
                ? throw new JsonException("设备接口未返回 UUID。")
                : device.Uuid;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<LeaderboardBoardsResult> GetBoardsAsync(
        DateTime date,
        bool includeLuck = true,
        bool includeCollections = true)
    {
        string[] metrics = GetMetrics(includeLuck, includeCollections);
        using CancellationTokenSource timeout = new(LeaderboardReadTimeout);
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                $"{ApiBaseUrl}/api/leaderboard?date={date:yyyy-MM-dd}", timeout.Token);
            response.EnsureSuccessStatusCode();
            ApiLeaderboardResponse api = await response.Content.ReadFromJsonAsync<ApiLeaderboardResponse>(
                cancellationToken: timeout.Token) ?? throw new JsonException("排行榜接口返回为空。");
            Dictionary<string, IReadOnlyList<LeaderboardEntry>> boards = metrics.ToDictionary(
                metric => metric,
                metric => api.Boards.TryGetValue(metric, out List<ApiBoardEntry>? entries)
                    ? (IReadOnlyList<LeaderboardEntry>)entries
                        .Select(entry => new LeaderboardEntry(entry.Uuid, entry.Name, entry.Value))
                        .ToList()
                    : []);
            SaveBoardsCache(date, boards);
            return new LeaderboardBoardsResult(boards, false, _boardsCache?.CachedAtUtc);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            AppLog.Info($"排行榜读取超过 {LeaderboardReadTimeout.TotalSeconds:0} 秒，改用缓存");
            return GetCachedResult(date, metrics);
        }
        catch (Exception ex)
        {
            AppLog.Info($"排行榜读取失败，改用缓存：{ex.Message}");
            return GetCachedResult(date, metrics);
        }
    }

    public async Task<bool> SubmitAllAsync(
        string uuid,
        string displayName,
        DateTime date,
        IReadOnlyDictionary<string, double> values)
    {
        await _lock.WaitAsync();
        try
        {
            using HttpResponseMessage response = await Http.PostAsJsonAsync(
                $"{ApiBaseUrl}/api/statistics",
                new { uuid, name = displayName, date = date.ToString("yyyy-MM-dd"), values });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLog.Info($"排行榜用户数据上传失败（UUID={uuid}）：{ex.Message}");
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    private LeaderboardBoardsResult GetCachedResult(DateTime date, IReadOnlyList<string> metrics)
    {
        Dictionary<string, IReadOnlyList<LeaderboardEntry>> boards = GetCachedBoards(
            date, metrics, out DateTimeOffset? cachedAtUtc);
        return new LeaderboardBoardsResult(boards, true, cachedAtUtc);
    }

    private void SaveBoardsCache(
        DateTime date,
        Dictionary<string, IReadOnlyList<LeaderboardEntry>> boards)
    {
        var cache = new BoardsCacheFile
        {
            Date = date.ToString("yyyy-MM-dd"),
            CachedAtUtc = DateTimeOffset.UtcNow,
            Boards = boards.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Select(entry => new BoardEntryDto
                {
                    Uuid = entry.Uuid,
                    Name = entry.Name,
                    Value = entry.Value,
                }).ToList()),
        };
        _boardsCache = cache;
        try
        {
            AtomicFile.WriteAllText(_boardsCachePath, JsonSerializer.Serialize(cache));
        }
        catch
        {
        }
    }

    private Dictionary<string, IReadOnlyList<LeaderboardEntry>> GetCachedBoards(
        DateTime date,
        IReadOnlyList<string> metrics,
        out DateTimeOffset? cachedAtUtc)
    {
        cachedAtUtc = null;
        if (_boardsCache is null ||
            !DateTime.TryParseExact(
                _boardsCache.Date,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime cachedDate) ||
            cachedDate.Date != date.Date)
        {
            return metrics.ToDictionary(metric => metric, _ => (IReadOnlyList<LeaderboardEntry>)[]);
        }

        cachedAtUtc = _boardsCache.CachedAtUtc;
        return metrics.ToDictionary(
            metric => metric,
            metric => _boardsCache.Boards.TryGetValue(metric, out List<BoardEntryDto>? entries)
                ? (IReadOnlyList<LeaderboardEntry>)entries
                    .Select(entry => new LeaderboardEntry(entry.Uuid, entry.Name, entry.Value))
                    .ToList()
                : []);
    }

    private static string[] GetMetrics(bool includeLuck, bool includeCollections)
    {
        List<string> metrics =
        [
            "active", "active7", "mouse_total", "mouse_total7", "mouse_left", "mouse_left7",
            "mouse_right", "mouse_right7", "keyboard", "keyboard7", "active30", "mouse_total30",
            "mouse_left30", "mouse_right30", "keyboard30", "active_total", "mouse_total_total",
            "mouse_left_total", "mouse_right_total", "keyboard_total",
        ];
        if (includeLuck) metrics.Add("luck");
        if (includeCollections) metrics.Add("collections");
        return [.. metrics];
    }

    private sealed class DeviceResponse
    {
        public string Uuid { get; set; } = "";
    }

    private sealed class ApiLeaderboardResponse
    {
        public Dictionary<string, List<ApiBoardEntry>> Boards { get; set; } = [];
    }

    private sealed class ApiBoardEntry
    {
        public string Uuid { get; set; } = "";
        public string Name { get; set; } = "";
        public double Value { get; set; }
    }

    private sealed class BoardsCacheFile
    {
        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("cached_at_utc")]
        public DateTimeOffset CachedAtUtc { get; set; }

        [JsonPropertyName("boards")]
        public Dictionary<string, List<BoardEntryDto>> Boards { get; set; } = [];
    }

    private sealed class BoardEntryDto
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("value")]
        public double Value { get; set; }
    }
}
