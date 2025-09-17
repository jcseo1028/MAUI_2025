using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace MyMetronom.Services;

public sealed class MetronomeService : IMetronomeService
{
    private readonly IBeepService _beep;
    private readonly ILogger<MetronomeService> _logger;
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
    private volatile bool _winStopping;
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

    public MetronomeService(IBeepService beep, ILogger<MetronomeService> logger)
    {
        _beep = beep;
        _logger = logger;
    }

    public void Start(int bpm)
    {
        SetBpm(bpm);
#if WINDOWS
        if (_timerId != 0) return;
        _winStopping = false;
        _logger.LogDebug("Start: Windows timer starting (BPM={Bpm}, Subdiv={Subdiv})", Bpm, (int)Subdivision);
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
        _logger.LogDebug("Start: thread started (BPM={Bpm}, Subdiv={Subdiv})", Bpm, (int)Subdivision);
#endif
    }

    public void Stop()
    {
#if WINDOWS
        _logger.LogDebug("Stop: Windows timer stopping");
        StopWindows();
        _logger.LogDebug("Stop: Windows timer stopped");
#else
        var t = _thread; var c = _cts;
        _thread = null; _cts = null;
        try { c?.Cancel(); } catch { }
        _logger.LogDebug("Stop: cancel signaled (thread join async)");
        _ = Task.Run(() =>
        {
            try { t?.Join(1000); } catch { }
            _logger.LogDebug("Stop: thread joined");
        });
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
        try { timeBeginPeriod(1); } catch { }
        _winSw = Stopwatch.StartNew();
        _winStartTicks = _winSw.ElapsedTicks;
        _winTickIndex = 0;
        _timerCallback = OnWinTimer;
        ScheduleNextWindows();
        _logger.LogDebug("Windows: timer scheduled first tick");
    }

    private void StopWindows()
    {
        // Signal stop so callback/scheduler stop creating new timers
        _winStopping = true;

        // Swap timer id under lock, then kill outside to avoid deadlock with TIME_KILL_SYNCHRONOUS
        uint idToKill = 0;
        lock (_winLock)
        {
            idToKill = _timerId;
            _timerId = 0;
        }
        if (idToKill != 0)
        {
            try { timeKillEvent(idToKill); } catch { }
        }

        try { timeEndPeriod(1); } catch { }
        _winSw?.Stop();
        _winSw = null;
        _timerCallback = null;
    }

    private void ScheduleNextWindows()
    {
        if (_winSw is null || _winStopping) return;
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
            if (_winStopping) return;
            if (_timerId != 0)
            {
                try { timeKillEvent(_timerId); } catch { }
                _timerId = 0;
            }
            _timerId = timeSetEvent((uint)delayMs, 1, _timerCallback, IntPtr.Zero, TIME_ONESHOT | TIME_KILL_SYNCHRONOUS);
        }
    }

    private void OnWinTimer(uint id, uint msg, IntPtr user, IntPtr dw1, IntPtr dw2)
    {
        if (_winStopping) return;

        int div = (int)Subdivision;
        bool isAccent = (_winTickIndex % div) == 0;
        try
        {
            _beep.Beep(isAccent ? 100 : 60, isAccent ? 1200 : null);
        }
        catch { }

        try { Tick?.Invoke(this, EventArgs.Empty); } catch { }

        _winTickIndex++;
        ScheduleNextWindows();
    }
#endif

    private void RunLoop(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Thread: run loop started");

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
        _logger.LogDebug("Thread: run loop ended");
    }
}
