using System.Diagnostics;

namespace MyMetronom.Services;

public sealed class MetronomeService : IMetronomeService
{
    private readonly IBeepService _beep;
    private CancellationTokenSource? _cts;

    public int Bpm { get; private set; } = 120;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public event EventHandler? Tick;

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

    private async Task RunAsync(CancellationToken ct)
    {
        var sw = new Stopwatch();
        sw.Start();

        var intervalMs = 60000.0 / Bpm;
        long tickIndex = 0;

        while (!ct.IsCancellationRequested)
        {
            try { _beep.Beep(25); } catch { }
            Tick?.Invoke(this, EventArgs.Empty);

            tickIndex++;
            var nextTargetMs = tickIndex * intervalMs;
            var delayMs = nextTargetMs - sw.Elapsed.TotalMilliseconds;
            if (delayMs < 0) delayMs = 0;

            try { await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct); }
            catch (TaskCanceledException) { break; }

            intervalMs = 60000.0 / Bpm;
        }

        sw.Stop();
    }
}
