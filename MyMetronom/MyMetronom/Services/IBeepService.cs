namespace MyMetronom.Services;

public interface IBeepService
{
    // 주 음과 비강세 음을 구분하여 다르게 재생할 수 있도록 파라미터 추가
    void Beep(int milliseconds = 30, int? frequencyHz = null);
}
