using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyMetronom.Services;

public sealed class MetronomeService : IMetronomeService
{
    private readonly IBeepService _beep;
    private CancellationTokenSource? _cts;
    private Thread? _thread;

#if WINDOWS
    // WinMM multimedia timer
    private const uint TIME_ONESHOT = 0x0000;
    private const uint TIME_PERIODIC = 0x0001;
    private const uint TIME_KILL_SYNCHRONOUS = 0x0100;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeEndPeriod(uint uPeriod);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeSetEvent(uint uDelay, uint uResolution, TimeProc? lpTimeProc, IntPtr dwUser, uint fuEvent);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeKillEvent(uint uTimerId);

    private delegate void TimeProc(uint uTimerID, uint uMsg, IntPtr dwUser, IntPtr dw1, IntPtr dw2);

    private uint _timerId;
    private Stopwatch? _winSw;
    private long _winStartTicks;
    private long _winTickIndex;
    private TimeProc? _timerCallback;
    private readonly object _winLock = new();
#endif

    public int Bpm { get; private set; } = 120;
    public bool IsRunning => _cts is { IsCancellationRequested: false } ||
#if WINDOWS
                             _timerId != 0;
#else
                             false;
#endif

    public event EventHandler? Tick;

    public Subdivision Subdivision { get; private set; } = Subdivision.Quarter;

    public MetronomeService(IBeepService beep)
    {
        _beep = beep;
    }

    public void Start(int bpm)
    {
        SetBpm(bpm);
#if WINDOWS
        if (_timerId != 0) return;
        StartWindows();
#else
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => RunLoop(_cts.Token))
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
#endif
    }

    public void Stop()
    {
#if WINDOWS
        StopWindows();
#else
        _cts?.Cancel();
        try { _thread?.Join(500); } catch { }
        _thread = null;
        _cts = null;
#endif
    }

    public void SetBpm(int bpm)
    {
        if (bpm < 30) bpm = 30;
        if (bpm > 300) bpm = 300;
        Bpm = bpm;
    }

    public void SetSubdivision(Subdivision subdivision)
        => Subdivision = subdivision;

#if WINDOWS
    private void StartWindows()
    {
        // Request 1ms system timer resolution
        timeBeginPeriod(1);
        _winSw = Stopwatch.StartNew();
        _winStartTicks = _winSw.ElapsedTicks;
        _winTickIndex = 0;
        _timerCallback = OnWinTimer;
        ScheduleNextWindows();
    }

    private void StopWindows()
    {
        lock (_winLock)
        {
            if (_timerId != 0)
            {
                try { timeKillEvent(_timerId); } catch { }
                _timerId = 0;
            }
        }
        try { timeEndPeriod(1); } catch { }
        _winSw?.Stop();
        _winSw = null;
    }

    private void ScheduleNextWindows()
    {
        if (_winSw is null) return;
        double freq = Stopwatch.Frequency;
        int div = (int)Subdivision;
        double intervalSec = 60.0 / (Bpm * div);
        long intervalTicks = (long)Math.Round(intervalSec * freq);

        long target = _winStartTicks + (_winTickIndex + 1) * intervalTicks;
        long now = _winSw.ElapsedTicks;
        long remainingTicks = target - now;
        int delayMs = (int)Math.Max(1, Math.Round(remainingTicks * 1000.0 / freq));

        lock (_winLock)
        {
            if (_timerId != 0)
            {
                // Ensure only one outstanding timer
                try { timeKillEvent(_timerId); } catch { }
                _timerId = 0;
            }
            _timerId = timeSetEvent((uint)delayMs, 1, _timerCallback, IntPtr.Zero, TIME_ONESHOT | TIME_KILL_SYNCHRONOUS);
        }
    }

    private void OnWinTimer(uint id, uint msg, IntPtr user, IntPtr dw1, IntPtr dw2)
    {
        // Perform the tick
        int div = (int)Subdivision;
        bool isAccent = (_winTickIndex % div) == 0;
        try
        {
            _beep.Beep(isAccent ? 100 : 60, isAccent ? 1200 : null);
        }
        catch { }

        try { Tick?.Invoke(this, EventArgs.Empty); } catch { }

        // Advance and schedule next
        _winTickIndex++;
        ScheduleNextWindows();
    }
#endif

    private void RunLoop(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        long tickIndex = 0;
        long startTicks = sw.ElapsedTicks;

        while (!ct.IsCancellationRequested)
        {
            int div = (int)Subdivision;
            double ticksPerSecond = Stopwatch.Frequency;
            double intervalSeconds = 60.0 / (Bpm * div);
            long intervalTicks = (long)Math.Round(intervalSeconds * ticksPerSecond);

            long target = startTicks + (tickIndex + 1) * intervalTicks;

            while (!ct.IsCancellationRequested)
            {
                long now = sw.ElapsedTicks;
                long remainingTicks = target - now;
                if (remainingTicks <= 0) break;

                double remainingMs = (remainingTicks * 1000.0) / ticksPerSecond;
                if (remainingMs > 8)
                {
                    int sleepMs = (int)Math.Max(1, Math.Floor(remainingMs - 2));
                    try { Thread.Sleep(sleepMs); } catch { }
                }
                else
                {
                    Thread.SpinWait(200);
                }
            }

            if (ct.IsCancellationRequested) break;

            int subIndex = (int)(tickIndex % div);
            bool isAccent = subIndex == 0;

            try
            {
                if (OperatingSystem.IsWindows())
                    _beep.Beep(isAccent ? 80 : 40, isAccent ? 1400 : 880);
                else
                    _beep.Beep(isAccent ? 100 : 60, isAccent ? 1200 : null);
            }
            catch { }

            Tick?.Invoke(this, EventArgs.Empty);
            tickIndex++;
        }

        sw.Stop();
    }
}
