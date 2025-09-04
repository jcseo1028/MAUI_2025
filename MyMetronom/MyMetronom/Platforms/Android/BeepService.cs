using Android.Media;
using MyMetronom.Services;

namespace MyMetronom;

public sealed class BeepService : IBeepService
{
    public void Beep(int milliseconds = 30)
    {
        using var tg = new ToneGenerator(Android.Media.Stream.Music, 100);
        tg.StartTone(Tone.Dtmf0, milliseconds);
    }
}
