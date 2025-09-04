using Android.Media;
using Android.Content;
using System.Runtime.CompilerServices;

namespace MyMetronom.Services;

public sealed class AndroidMetronomeService : IMetronomeService
{
    private readonly IBeepService _beep;

    private volatile int _bpm = 120;
    private volatile Subdivision _subdiv = Subdivision.Quarter;

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private AudioTrack? _track;

    private AudioManager? _audioManager;
    private AudioFocusRequestClass? _focusRequest;

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

        RequestAudioFocus();

        _cts = new CancellationTokenSource();
        _thread = new Thread(() => RunAudioThread(_cts.Token))
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _thread?.Join(500); } catch { }
        _thread = null;
        _cts = null;
        try
        {
            _track?.Stop();
            _track?.Flush();
            _track?.Release();
        }
        catch { }
        finally
        {
            _track?.Dispose();
            _track = null;
        }

        AbandonAudioFocus();
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
        // Resolve native sample rate
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
        int frameSamples = Math.Max(minBuf / 2, sampleRate / 20); // ~50ms
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

        // Pre-warm with silence and start
        var buffer = new short[writeSamples];
        _track.Write(buffer, 0, buffer.Length);
        _track.Play();
        try
        {
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Lollipop)
                _track.SetVolume(1.0f);
            else
#pragma warning disable CS0618
                _track.SetStereoVolume(1.0f, 1.0f);
#pragma warning restore CS0618
        }
        catch { }

        long samplesGenerated = writeSamples; // account for warmup
        long samplesPerTick = SamplesPerTick(sampleRate, _bpm, (int)_subdiv);
        long nextClickSample = samplesGenerated; // first tick right after warmup
        int subIndex = 0;

        while (!ct.IsCancellationRequested)
        {
            System.Array.Clear(buffer, 0, buffer.Length);

            // Snapshot timing
            int bpm = _bpm;
            int div = (int)_subdiv;
            if (div <= 0) div = 1;
            samplesPerTick = SamplesPerTick(sampleRate, bpm, div);

            long blockStart = samplesGenerated;
            long blockEnd = blockStart + buffer.Length;

            while (nextClickSample < blockEnd)
            {
                if (nextClickSample < blockStart)
                {
                    long delta = blockStart - nextClickSample;
                    long steps = (delta + samplesPerTick - 1) / samplesPerTick;
                    nextClickSample += steps * samplesPerTick;
                    subIndex = (subIndex + (int)steps) % div;
                    continue;
                }

                // Produce audible tick via SoundPool
                bool isAccent = (subIndex == 0);
                try { _beep.Beep(isAccent ? 90 : 60, isAccent ? 1200 : null); } catch { }

                try { Tick?.Invoke(this, EventArgs.Empty); } catch { }

                nextClickSample += samplesPerTick;
                subIndex = (subIndex + 1) % div;
            }

            int written = 0;
            while (written < buffer.Length && !ct.IsCancellationRequested)
            {
                int n = _track!.Write(buffer, written, buffer.Length - written);
                if (n <= 0) break;
                written += n;
            }

            samplesGenerated += buffer.Length;
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
