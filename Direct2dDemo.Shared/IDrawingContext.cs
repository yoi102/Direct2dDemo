using Direct2dDemo.Shared.Elements;

namespace Direct2dDemo.Shared;

public interface IDrawingContext : IDisposable
{
    int Width { get; }
    int Height { get; }
    List<IDrawingElement> DrawingElements { get; }

    void Initialize(nint hwnd, int width, int height);

    void HwndResized(int width, int height);

    void Render();

    void ClearData();
}

public interface IDrawingGdiContext
{
    void BitBlt(nint hdc);
    void BitBlt();
}

public interface ICanvasContext
{
    void BeginPan(int x, int y);
    void Pan(int x, int y);
    void EndPan(int x, int y);

    void Zoom(float zoomFactor, int centerX, int centerY);
}