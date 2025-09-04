using MyMetronom.Services;

namespace MyMetronom;

public sealed class BeepService : IBeepService
{
    public void Beep(int milliseconds = 30)
    {
        // Fall back to Console.Beep if SystemSounds is unavailable without extra package
        try
        {
            Console.Beep(800, Math.Max(1, milliseconds));
        }
        catch
        {
            // ignore if not supported (non-interactive sessions)
        }
    }
}
