using System.Diagnostics;

namespace MyMetronom.Services;

public sealed class MetronomeService : IMetronomeService
{
    private readonly IBeepService _beep;
    private CancellationTokenSource? _cts;

    public int Bpm { get; private set; } = 120;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public event EventHandler? Tick;

    public Subdivision Subdivision { get; private set; } = Subdivision.Quarter;

    public MetronomeService(IBeepService beep)
    {
        _beep = beep;
    }

    public void Start(int bpm)
    {
        SetBpm(bpm);
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void SetBpm(int bpm)
    {
        if (bpm < 30) bpm = 30;
        if (bpm > 300) bpm = 300;
        Bpm = bpm;
    }

    public void SetSubdivision(Subdivision subdivision)
        => Subdivision = subdivision;

    private async Task RunAsync(CancellationToken ct)
    {
        var sw = new Stopwatch();
        sw.Start();

        long globalIndex = 0;

        while (!ct.IsCancellationRequested)
        {
            var div = (int)Subdivision;
            var subIndex = (int)(globalIndex % div);
            var isAccent = subIndex == 0;

            try
            {
                // 강조(시작음)과 일반음 차별: 길이와(Windows는 주파수도) 다르게
                if (OperatingSystem.IsWindows())
                    _beep.Beep(isAccent ? 60 : 25, isAccent ? 1400 : 800);
                else
                    _beep.Beep(isAccent ? 60 : 25);
            }
            catch { }

            Tick?.Invoke(this, EventArgs.Empty);
            globalIndex++;

            var intervalMs = 60000.0 / (Bpm * div);
            var nextTargetMs = globalIndex * intervalMs;
            var delayMs = nextTargetMs - sw.Elapsed.TotalMilliseconds;
            if (delayMs < 0) delayMs = 0;

            try { await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct); }
            catch (TaskCanceledException) { break; }
        }

        sw.Stop();
    }
}
