using Direct2dDemo.Shared;
using Direct2dDemo.Shared.Elements;
using System.Diagnostics;
using System.Numerics;
using Vortice.Mathematics;

namespace Direct2dDemo.Direct2D;

public class Direct2dContext : IDrawingContext, ICanvasContext
{
    private readonly Stopwatch stopwatch = new();

    public event EventHandler<double>? Rendered;

    public int Width => direct2DWrapper.Width;
    public int Height => direct2DWrapper.Height;
    public List<IDrawingElement> DrawingElements { get; } = new List<IDrawingElement>();

    private readonly Direct2DWrapper direct2DWrapper = new Direct2DWrapper();

    private static readonly Color4 background = new Color4(1f, 1f, 1f, 1.0f);

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
        if (direct2DWrapper.Context is null)
            return;

        stopwatch.Restart();

        InternalRender();

        stopwatch.Stop();
        Rendered?.Invoke(this, stopwatch.ElapsedMilliseconds);
    }

    private void InternalRender()
    {
        if (direct2DWrapper.Context is null)
            return;
        if (direct2DWrapper.Direct2DResourceCache is null)
            return;

        var context = direct2DWrapper.Context;

        context.BeginDraw();

        this.Clear();
        foreach (var element in DrawingElements)
        {
            // command list 里记录的是已经转换后的屏幕坐标。
            element.Draw(direct2DWrapper.Direct2DResourceCache, direct2DWrapper.Context, _offsetX, _offsetY, _scale);
        }

        context.EndDraw();

        direct2DWrapper.Present();
    }

    private void Clear()
    {
        var context = direct2DWrapper.Context;
        if (context is null)
            return;

        context.Transform = Matrix3x2.Identity;
        context.Clear(background);
    }

    public void HwndResized(int width, int height)
    {
        direct2DWrapper.TargetResized(width, height);
        RenderCurrentView();
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        direct2DWrapper.SetTarget(hwnd, width, height);
        RenderCurrentView();
    }

    public void ClearData()
    {
        if (direct2DWrapper.Context is not null)
            direct2DWrapper.Context.Transform = Matrix3x2.Identity;

        DrawingElements.Clear();
        direct2DWrapper.Direct2DResourceCache?.ClearCache();

        _offsetX = 0;
        _offsetY = 0;
        _panStartX = 0;
        _panStartY = 0;
        _panStartOffsetX = 0;
        _panStartOffsetY = 0;
        _scale = 1.0f;
    }

    public void Dispose()
    {
        direct2DWrapper.Dispose();
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
        // 为了让鼠标所在的 world 点保持在 centerX / centerY，不跳动：
        // world = (center - oldOffset) / oldScale
        // newOffset = center - world * newScale
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