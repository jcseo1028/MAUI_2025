using Android.Media;
using MyMetronom.Services;

namespace MyMetronom;

public sealed class BeepService : IBeepService
{
    public void Beep(int milliseconds = 30, int? frequencyHz = null)
    {
        // ToneGenerator�� ���� ���ǵ� �游 ���� ����, ���ļ� ���� ������ �Ұ�.
        // ������/�Ϲ����� �� ������ ����.
        using var tg = new ToneGenerator(Android.Media.Stream.Music, 100);
        var tone = frequencyHz.HasValue ? Tone.PropBeep : Tone.Dtmf0; // ����: PropBeep, �Ϲ�: Dtmf0
        tg.StartTone(tone, milliseconds);
    }
}
