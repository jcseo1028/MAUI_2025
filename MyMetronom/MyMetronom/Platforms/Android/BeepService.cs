using Android.Media;
using Android.Util;
using MyMetronom.Services;
using System.Threading;

namespace MyMetronom;

public sealed class BeepService : IBeepService
{
    private const string Tag = "BeepService";
    private static readonly object _initLock = new();
    private static bool _initialized;
    private static bool _normalReady;
    private static bool _accentReady;
    private static SoundPool? _pool;
    private static int _normalId;
    private static int _accentId;

    private static readonly ManualResetEventSlim _normalLoaded = new(false);
    private static readonly ManualResetEventSlim _accentLoaded = new(false);

    public void Beep(int milliseconds = 30, int? frequencyHz = null)
    {
        EnsureInitialized();
        if (_pool is null)
        {
            Log.Warn(Tag, "SoundPool not initialized");
            return;
        }

        var wantAccent = frequencyHz.HasValue;
        var soundId = wantAccent ? _accentId : _normalId;
        var ready = wantAccent ? _accentReady : _normalReady;

        if (!ready)
        {
            // Wait up to 1s for the sample to finish loading on first call
            var ev = wantAccent ? _accentLoaded : _normalLoaded;
            ev.Wait(TimeSpan.FromMilliseconds(1000));
            ready = wantAccent ? _accentReady : _normalReady;
        }

        if (soundId == 0 || !ready)
        {
            Log.Warn(Tag, $"Sound not ready. accent={wantAccent}, id={soundId}, ready={ready}");
            return;
        }

        var streamId = _pool.Play(soundId, 1f, 1f, 1, 0, 1f);
        Log.Debug(Tag, $"Play result streamId={streamId}, accent={wantAccent}");
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;

            try
            {
                var folder = FileSystem.AppDataDirectory;
                var normalPath = Path.Combine(folder, "met_normal.wav");
                var accentPath = Path.Combine(folder, "met_accent.wav");

                if (!File.Exists(normalPath))
                    GenerateSineWaveWav(normalPath, 880.0, 0.1); // 100ms
                if (!File.Exists(accentPath))
                    GenerateSineWaveWav(accentPath, 1320.0, 0.12); // 120ms

                var attrs = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.Media)
                    .SetContentType(AudioContentType.Music)
                    .Build();

                _pool = new SoundPool.Builder()
                    .SetAudioAttributes(attrs)
                    .SetMaxStreams(4)
                    .Build();

                _pool.SetOnLoadCompleteListener(new LoadListener());

                _normalLoaded.Reset();
                _accentLoaded.Reset();

                _normalId = _pool.Load(normalPath, 1);
                _accentId = _pool.Load(accentPath, 1);

                Log.Debug(Tag, $"Loading sounds normalId={_normalId}, accentId={_accentId}");
                _initialized = true;
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Initialization failed: {ex}");
                _initialized = false;
            }
        }
    }

    private sealed class LoadListener : Java.Lang.Object, SoundPool.IOnLoadCompleteListener
    {
        public void OnLoadComplete(SoundPool? soundPool, int sampleId, int status)
        {
            if (status != 0)
            {
                Log.Warn(Tag, $"OnLoadComplete status={status} for sampleId={sampleId}");
                return;
            }

            if (sampleId == _normalId)
            {
                _normalReady = true;
                _normalLoaded.Set();
                Log.Debug(Tag, "Normal sound ready");
            }
            else if (sampleId == _accentId)
            {
                _accentReady = true;
                _accentLoaded.Set();
                Log.Debug(Tag, "Accent sound ready");
            }
        }
    }

    // Generate a simple PCM16 mono WAV file with a sine wave
    private static void GenerateSineWaveWav(string path, double frequencyHz, double durationSeconds)
    {
        const int sampleRate = 44100;
        int samples = (int)(sampleRate * durationSeconds);
        short[] pcm = new short[samples];
        double amp = 0.9 * short.MaxValue; // louder
        for (int i = 0; i < samples; i++)
        {
            pcm[i] = (short)(amp * Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate));
        }

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        int byteRate = sampleRate * 2; // mono, 16-bit
        int subchunk2Size = samples * 2;
        int chunkSize = 36 + subchunk2Size;

        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(chunkSize);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt subchunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // PCM
        bw.Write((short)1); // AudioFormat=1 (PCM)
        bw.Write((short)1); // NumChannels=1 (mono)
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)2); // BlockAlign
        bw.Write((short)16); // BitsPerSample

        // data subchunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(subchunk2Size);

        // PCM data
        for (int i = 0; i < pcm.Length; i++)
            bw.Write(pcm[i]);

        bw.Flush();
    }
}
