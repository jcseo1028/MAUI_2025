using Android.Media;
using MyMetronom.Services;

namespace MyMetronom;

public sealed class BeepService : IBeepService
{
    public void Beep(int milliseconds = 30, int? frequencyHz = null)
    {
        // ToneGenerator는 사전 정의된 톤만 선택 가능, 주파수 직접 지정은 불가.
        // 강조음/일반음을 톤 종류로 구분.
        using var tg = new ToneGenerator(Android.Media.Stream.Music, 100);
        var tone = frequencyHz.HasValue ? Tone.PropBeep : Tone.Dtmf0; // 강조: PropBeep, 일반: Dtmf0
        tg.StartTone(tone, milliseconds);
    }
}
