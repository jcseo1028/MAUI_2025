namespace MyMetronom.Services;

public interface IRunStatsService
{
    Task InitializeAsync();
    Task<TimeSpan> GetTotalForDateAsync(DateOnly date);
    Task AddAsync(DateTimeOffset startUtc, DateTimeOffset endUtc);
    Task<IReadOnlyDictionary<DateOnly, TimeSpan>> GetTotalsForRangeAsync(DateOnly start, DateOnly end);
}
