using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCompanionMonitor;

internal sealed record PowerCarry(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    TimeSpan Powered,
    TimeSpan Awake);

internal sealed class PowerSessionStore
{
    private const int CurrentVersion = 2;
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly DateTimeOffset _currentStartUtc;
    private readonly List<SessionDto> _sessions = [];
    private PowerCarry? _carry;

    public PowerSessionStore(string dataDirectory, PowerCarry? migrationCarry = null)
    {
        _filePath = Path.Combine(dataDirectory, "power_sessions.json");
        _currentStartUtc = DateTimeOffset.UtcNow;
        if (!Load())
        {
            _carry = migrationCarry;
        }
        RecordHeartbeat();
    }

    public IReadOnlyList<Interval> GetIntervals()
    {
        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return _sessions
                .Select(session => new Interval(session.StartUtc, session.EndUtc))
                .Append(new Interval(_currentStartUtc, DateTimeOffset.MaxValue))
                .Where(interval => interval.End > interval.Start && interval.Start <= now)
                .ToList();
        }
    }

    public PowerCarry? GetCarry()
    {
        lock (_lock)
        {
            return _carry;
        }
    }

    public void RecordHeartbeat()
    {
        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SessionDto? current = _sessions.FirstOrDefault(session =>
                Math.Abs((session.StartUtc - _currentStartUtc).TotalSeconds) < 5);
            if (current is null)
            {
                current = new SessionDto { StartUtc = _currentStartUtc };
                _sessions.Add(current);
            }
            current.EndUtc = now;
            DateTimeOffset cutoff = now.AddDays(-90);
            _sessions.RemoveAll(session => session.EndUtc < cutoff);
            if (_carry?.EndUtc < cutoff)
            {
                _carry = null;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(new FileData
                {
                    Version = CurrentVersion,
                    Carry = _carry is null ? null : CarryDto.FromRecord(_carry),
                    Sessions = _sessions.OrderBy(session => session.StartUtc).ToList(),
                }));
            }
            catch (Exception ex)
            {
                AppLog.Info($"电源会话心跳保存失败：{ex.Message}");
            }
        }
    }

    private bool Load()
    {
        try
        {
            if (AtomicFile.TryDeserialize(_filePath, out FileData? data) &&
                data?.Version == CurrentVersion &&
                data.Sessions is not null)
            {
                _sessions.AddRange(data.Sessions.Where(session => session.EndUtc > session.StartUtc));
                _carry = data.Carry?.ToRecord();
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLog.Info($"电源会话记录加载失败：{ex.Message}");
        }
        return false;
    }

    private sealed class FileData
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("carry")]
        public CarryDto? Carry { get; set; }

        [JsonPropertyName("sessions")]
        public List<SessionDto> Sessions { get; set; } = [];
    }

    private sealed class CarryDto
    {
        [JsonPropertyName("start_utc")]
        public DateTimeOffset StartUtc { get; set; }

        [JsonPropertyName("end_utc")]
        public DateTimeOffset EndUtc { get; set; }

        [JsonPropertyName("powered_seconds")]
        public long PoweredSeconds { get; set; }

        [JsonPropertyName("awake_seconds")]
        public long AwakeSeconds { get; set; }

        public static CarryDto FromRecord(PowerCarry carry) => new()
        {
            StartUtc = carry.StartUtc,
            EndUtc = carry.EndUtc,
            PoweredSeconds = Math.Max(0, (long)carry.Powered.TotalSeconds),
            AwakeSeconds = Math.Max(0, (long)carry.Awake.TotalSeconds),
        };

        public PowerCarry? ToRecord()
        {
            if (EndUtc <= StartUtc)
            {
                return null;
            }
            long powered = Math.Max(0, PoweredSeconds);
            long awake = Math.Clamp(AwakeSeconds, 0, powered);
            return new PowerCarry(
                StartUtc,
                EndUtc,
                TimeSpan.FromSeconds(powered),
                TimeSpan.FromSeconds(awake));
        }
    }

    private sealed class SessionDto
    {
        [JsonPropertyName("start_utc")]
        public DateTimeOffset StartUtc { get; set; }

        [JsonPropertyName("end_utc")]
        public DateTimeOffset EndUtc { get; set; }
    }
}
