namespace Direct2dDemo.Shared;

public interface IDirect2dContext : IDisposable
{
    void Initialize(nint hwnd, int width, int height);

    void HwndResized(int width, int height);
}