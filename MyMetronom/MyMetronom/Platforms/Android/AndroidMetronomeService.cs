using Android.Media;
using Android.Content;
using Android.Util;
using System.Runtime.CompilerServices;

namespace MyMetronom.Services;

public sealed class AndroidMetronomeService : IMetronomeService
{
    private readonly IBeepService _beep;
    private const string LogTag = "AndroidMetronome";

    private volatile int _bpm = 120;
    private volatile Subdivision _subdiv = Subdivision.Quarter;

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private AudioTrack? _track;

    private AudioManager? _audioManager;
    private AudioFocusRequestClass? _focusRequest;

    private volatile bool _stopping;

    public int Bpm => _bpm;
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public event EventHandler? Tick;

    public Subdivision Subdivision => _subdiv;

    public AndroidMetronomeService(IBeepService beep)
    {
        _beep = beep;
    }

    public void Start(int bpm)
    {
        SetBpm(bpm);
        if (IsRunning) return;

        _stopping = false;
        RequestAudioFocus();

        _cts = new CancellationTokenSource();
        _thread = new Thread(() => RunAudioThread(_cts.Token))
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        try { Log.Debug(LogTag, "Start: audio thread started"); } catch { }
    }

    public void Stop()
    {
        // Signal stop to prevent further scheduling
        _stopping = true;

        // Capture references to avoid races then clear state for UI immediacy
        var t = _thread; var cts = _cts;
        _thread = null; _cts = null;
        try { cts?.Cancel(); } catch { }
        try { Log.Debug(LogTag, "Stop: cancel signaled"); } catch { }

        // Do not block UI thread; wait in background and then abandon focus
        _ = Task.Run(() =>
        {
            try { t?.Join(2000); } catch { }
            finally
            {
                try { Log.Debug(LogTag, "Stop: audio thread joined"); } catch { }
                AbandonAudioFocus();
            }
        });
    }

    public void SetBpm(int bpm)
    {
        if (bpm < 30) bpm = 30;
        if (bpm > 300) bpm = 300;
        _bpm = bpm;
    }

    public void SetSubdivision(Subdivision subdivision)
    {
        _subdiv = subdivision;
    }

    private void RunAudioThread(CancellationToken ct)
    {
        int sampleRate = 44100;
        try
        {
            var am = (AudioManager?)Android.App.Application.Context.GetSystemService(Context.AudioService);
            var rateStr = am?.GetProperty(AudioManager.PropertyOutputSampleRate);
            if (int.TryParse(rateStr, out var nativeRate) && nativeRate > 0)
                sampleRate = nativeRate;
        }
        catch { }

        var channelMask = ChannelOut.Mono;
        int minBuf = AudioTrack.GetMinBufferSize(sampleRate, channelMask, Encoding.Pcm16bit);
        int frameSamples = Math.Max(minBuf / 2, sampleRate / 20);
        int writeSamples = AlignTo(frameSamples, 256);

        var attrs = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)
            .SetContentType(AudioContentType.Music)
            .Build();

        var format = new AudioFormat.Builder()
            .SetSampleRate(sampleRate)
            .SetEncoding(Encoding.Pcm16bit)
            .SetChannelMask(channelMask)
            .Build();

        _track = new AudioTrack(attrs, format, writeSamples * sizeof(short) * 4, AudioTrackMode.Stream, 0);
        var track = _track;

        try
        {
            var buffer = new short[writeSamples];
            track.Write(buffer, 0, buffer.Length);
            track.Play();
            try
            {
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Lollipop)
                    track.SetVolume(1.0f);
                else
#pragma warning disable CS0618
                    track.SetStereoVolume(1.0f, 1.0f);
#pragma warning restore CS0618
            }
            catch { }

            long samplesGenerated = writeSamples;
            long samplesPerTick = SamplesPerTick(sampleRate, _bpm, (int)_subdiv);
            long nextClickSample = samplesGenerated;
            int subIndex = 0;

            while (!ct.IsCancellationRequested && !_stopping)
            {
                System.Array.Clear(buffer, 0, buffer.Length);

                int bpm = _bpm;
                int div = (int)_subdiv;
                if (div <= 0) div = 1;
                samplesPerTick = SamplesPerTick(sampleRate, bpm, div);

                long blockStart = samplesGenerated;
                long blockEnd = blockStart + buffer.Length;

                while (!_stopping && nextClickSample < blockEnd)
                {
                    if (nextClickSample < blockStart)
                    {
                        long delta = blockStart - nextClickSample;
                        long steps = (delta + samplesPerTick - 1) / samplesPerTick;
                        nextClickSample += steps * samplesPerTick;
                        subIndex = (subIndex + (int)steps) % div;
                        continue;
                    }

                    if (_stopping) break;
                    bool isAccent = (subIndex == 0);
                    try { _beep.Beep(isAccent ? 90 : 60, isAccent ? 1200 : null); } catch { }
                    try { Tick?.Invoke(this, EventArgs.Empty); } catch { }

                    nextClickSample += samplesPerTick;
                    subIndex = (subIndex + 1) % div;
                }

                int written = 0;
                while (written < buffer.Length)
                {
                    if (_stopping || ct.IsCancellationRequested) break;
                    int toWrite = buffer.Length - written;
                    int n;
                    try
                    {
                        n = track.Write(buffer, written, toWrite, WriteMode.NonBlocking);
                    }
                    catch
                    {
                        try { Log.Warn(LogTag, "Write threw, exiting audio thread"); } catch { }
                        return;
                    }
                    if (n == 0)
                    {
                        Thread.SpinWait(5000);
                        continue;
                    }
                    if (n < 0) break;
                    written += n;
                }

                samplesGenerated += buffer.Length;
            }
        }
        finally
        {
            try
            {
                track?.Stop();
                track?.Flush();
                track?.Release();
            }
            catch { }
            finally
            {
                try { track?.Dispose(); } catch { }
                _track = null;
                try { Log.Debug(LogTag, "Audio thread cleaned up"); } catch { }
            }
        }
    }

    private static long SamplesPerTick(int sampleRate, int bpm, int div)
    {
        if (div <= 0) div = 1;
        double s = sampleRate * 60.0 / (bpm * div);
        long v = (long)Math.Round(s);
        return v < 1 ? 1 : v;
    }

    private static int AlignTo(int value, int alignment)
        => ((value + alignment - 1) / alignment) * alignment;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Mix(short[] dest, int destOffset, short[] src)
    {
        int n = Math.Min(src.Length, dest.Length - destOffset);
        for (int i = 0; i < n; i++)
        {
            int sum = dest[destOffset + i] + src[i];
            if (sum > short.MaxValue) sum = short.MaxValue;
            else if (sum < short.MinValue) sum = short.MinValue;
            dest[destOffset + i] = (short)sum;
        }
    }

    private void RequestAudioFocus()
    {
        try
        {
            var context = Android.App.Application.Context;
            _audioManager = (AudioManager?)context.GetSystemService(Context.AudioService);
            if (_audioManager == null) return;

            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                var attrs = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)
                    .SetContentType(AudioContentType.Music)
                    .Build();

                _focusRequest = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                    .SetAudioAttributes(attrs)
                    .Build();

                _ = _audioManager.RequestAudioFocus(_focusRequest);
            }
            else
            {
#pragma warning disable CS0618
                _audioManager.RequestAudioFocus(null, Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CS0618
            }
        }
        catch { }
    }

    private void AbandonAudioFocus()
    {
        try
        {
            if (_audioManager == null) return;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                if (_focusRequest != null)
                    _audioManager.AbandonAudioFocusRequest(_focusRequest);
            }
            else
            {
#pragma warning disable CS0618
                _audioManager.AbandonAudioFocus(null);
#pragma warning restore CS0618
            }
        }
        catch { }
        finally
        {
            _focusRequest = null;
            _audioManager = null;
        }
    }
}
