using SQLite;

namespace MyMetronom.Services;

public sealed class RunStatsService : IRunStatsService
{
    private readonly string _dbPath;
    private SQLiteAsyncConnection? _conn;

    public RunStatsService()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "runstats.db3");
    }

    public async Task InitializeAsync()
    {
        _conn ??= new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        await _conn.CreateTableAsync<RunEntry>();
    }

    public async Task<TimeSpan> GetTotalForDateAsync(DateOnly date)
    {
        await InitializeAsync();
        var dayStart = date.ToDateTime(new TimeOnly(0,0), DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var sql = "SELECT SUM(DurationMs) FROM RunEntry WHERE StartUtc >= ? AND StartUtc < ?";
        var sum = await _conn!.ExecuteScalarAsync<long?>(sql, dayStart.Ticks, dayEnd.Ticks);
        return TimeSpan.FromMilliseconds((sum ?? 0));
    }

    public async Task<IReadOnlyDictionary<DateOnly, TimeSpan>> GetTotalsForRangeAsync(DateOnly start, DateOnly end)
    {
        await InitializeAsync();
        if (end < start) return new Dictionary<DateOnly, TimeSpan>();

        var startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var sql = "SELECT StartUtc, DurationMs FROM RunEntry WHERE StartUtc >= ? AND StartUtc < ?";
        var rows = await _conn!.QueryAsync<RunEntry>(sql, startUtc.Ticks, endUtc.Ticks);

        var map = new Dictionary<DateOnly, long>();
        foreach (var r in rows)
        {
            var day = DateOnly.FromDateTime(new DateTime(r.StartUtc, DateTimeKind.Utc));
            map.TryGetValue(day, out var cur);
            map[day] = cur + r.DurationMs;
        }

        var result = new Dictionary<DateOnly, TimeSpan>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            map.TryGetValue(d, out var ms);
            result[d] = TimeSpan.FromMilliseconds(ms);
        }
        return result;
    }

    public async Task AddAsync(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        await InitializeAsync();
        if (endUtc <= startUtc) return;

        // Split by day boundaries
        var s = startUtc.UtcDateTime;
        var e = endUtc.UtcDateTime;
        var cur = s;
        while (cur < e)
        {
            var nextBoundary = DateTime.SpecifyKind(new DateTime(cur.Year, cur.Month, cur.Day, 0, 0, 0).AddDays(1), DateTimeKind.Utc);
            var segmentEnd = e < nextBoundary ? e : nextBoundary;
            var dur = segmentEnd - cur;

            var entry = new RunEntry
            {
                StartUtc = cur.Ticks,
                DurationMs = (long)dur.TotalMilliseconds
            };
            await _conn!.InsertAsync(entry);

            cur = segmentEnd;
        }
    }

    [Table("RunEntry")]
    private sealed class RunEntry
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public long StartUtc { get; set; }
        public long DurationMs { get; set; }
    }
}
