using MyMetronom.Services;

namespace MyMetronom;

public sealed class BeepService : IBeepService
{
    public void Beep(int milliseconds = 30, int? frequencyHz = null)
    {
        try
        {
            // 강조음은 더 높은 톤으로 구분
            if (frequencyHz.HasValue)
                Console.Beep(Math.Clamp(frequencyHz.Value, 37, 32767), Math.Max(1, milliseconds));
            else
                Console.Beep(800, Math.Max(1, milliseconds));
        }
        catch { }
    }
}
