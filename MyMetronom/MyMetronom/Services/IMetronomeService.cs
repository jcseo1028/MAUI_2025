namespace MyMetronom.Services;

public interface IMetronomeService
{
    int Bpm { get; }
    bool IsRunning { get; }

    event EventHandler? Tick;

    void Start(int bpm);
    void Stop();
    void SetBpm(int bpm);

    // ����ȭ ��� ���� API
    Subdivision Subdivision { get; }
    void SetSubdivision(Subdivision subdivision);
}
