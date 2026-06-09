using Direct2dDemo.Shared;
using Direct2dDemo.Shared.Elements;
using System.Diagnostics;
using System.Drawing;

namespace Direct2dDemo.GDI;

public class GdiContext : IDrawingContext, IDrawingGdiContext, ICanvasContext
{
    private readonly Stopwatch stopwatch = new();

    public event EventHandler<double>? Rendered;

    private readonly GdiWrapper gdiWrapper = new GdiWrapper();

    public int Width => gdiWrapper.Width;
    public int Height => gdiWrapper.Height;

    public List<IDrawingElement> DrawingElements { get; } = new List<IDrawingElement>();

    private static readonly Color background = Color.White;

    private float _panStartX;
    private float _panStartY;
    private float _offsetX;
    private float _offsetY;
    private float _panStartOffsetX;
    private float _panStartOffsetY;

    private float _scale = 1.0f;

    private const float MinScale = 0.05f;
    private const float MaxScale = 100.0f;

    public void Render()
    {
        RenderCurrentView();
    }

    private void RenderCurrentView()
    {
        if (!gdiWrapper.IsTargetReady)
            return;

        stopwatch.Restart();

        gdiWrapper.BeginDraw();

        try
        {
            Clear();
            Draw();
        }
        finally
        {
            gdiWrapper.EndDraw();
        }

        stopwatch.Stop();
        Rendered?.Invoke(this, stopwatch.ElapsedMilliseconds);
    }

    private void Clear()
    {
        gdiWrapper.Clear(background);
    }

    private void Draw()
    {
        foreach (var element in DrawingElements)
        {
            // 和 Direct2D 一样：这里把 offset / scale 传给 DrawExtension，
            // 不依赖 HDC 的 SetWorldTransform。
            element.Draw(gdiWrapper, _offsetX, _offsetY, _scale);
        }
    }

    public void HwndResized(int width, int height)
    {
        gdiWrapper.TargetResized(width, height);
        RenderCurrentView();
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        gdiWrapper.SetTarget(hwnd, width, height);
        RenderCurrentView();
    }

    public void ClearData()
    {
        DrawingElements.Clear();

        _offsetX = 0;
        _offsetY = 0;
        _panStartX = 0;
        _panStartY = 0;
        _panStartOffsetX = 0;
        _panStartOffsetY = 0;
        _scale = 1.0f;
    }

    public void BitBlt()
    {
        gdiWrapper.BitBlt();
    }
    public void BitBlt(nint hdc)
    {
        gdiWrapper.BitBlt(hdc);
    }

    public void Dispose()
    {
        gdiWrapper.Dispose();
    }

    public void BeginPan(int x, int y)
    {
        _panStartX = x;
        _panStartY = y;
        _panStartOffsetX = _offsetX;
        _panStartOffsetY = _offsetY;
    }

    public void Pan(int x, int y)
    {
        var deltaX = x - _panStartX;
        var deltaY = y - _panStartY;

        _offsetX = _panStartOffsetX + deltaX;
        _offsetY = _panStartOffsetY + deltaY;

        RenderCurrentView();
    }

    public void EndPan(int x, int y)
    {
        _panStartX = 0;
        _panStartY = 0;
    }

    public void Zoom(float zoomFactor, int centerX, int centerY)
    {
        if (zoomFactor <= 0)
            return;

        var oldScale = _scale;
        var newScale = Clamp(oldScale * zoomFactor, MinScale, MaxScale);

        if (AlmostSame(oldScale, newScale))
            return;

        // screen = world * scale + offset
        // 保持鼠标所在 world 点缩放前后仍落在 centerX / centerY。
        var worldX = (centerX - _offsetX) / oldScale;
        var worldY = (centerY - _offsetY) / oldScale;

        _scale = newScale;
        _offsetX = centerX - worldX * newScale;
        _offsetY = centerY - worldY * newScale;

        RenderCurrentView();
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }

    private static bool AlmostSame(float a, float b)
    {
        return Math.Abs(a - b) <= 0.000001f;
    }
}
