namespace MyMetronom.Services;

public interface IBeepService
{
    // �� ���� �񰭼� ���� �����Ͽ� �ٸ��� ����� �� �ֵ��� �Ķ���� �߰�
    void Beep(int milliseconds = 30, int? frequencyHz = null);
}
