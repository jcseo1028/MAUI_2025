namespace MyMetronom.Services;

public interface IMetronomeService
{
    int Bpm { get; }
    bool IsRunning { get; }

    event EventHandler? Tick;

    void Start(int bpm);
    void Stop();
    void SetBpm(int bpm);

    // 세분화 기댓값 설정 API
    Subdivision Subdivision { get; }
    void SetSubdivision(Subdivision subdivision);
}
