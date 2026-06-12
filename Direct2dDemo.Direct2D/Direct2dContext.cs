using Direct2dDemo.Shared;
using Direct2dDemo.Shared.Elements;
using System.Diagnostics;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dDemo.Direct2D;

public class Direct2dContext : IDrawingContext, ICanvasContext
{
    private readonly Stopwatch _stopwatch = new();
    private readonly Direct2DWrapper _direct2DWrapper = new();
    private readonly MultiDeviceRenderer _multiDeviceRenderer;

    private static readonly Color4 BackgroundColor = new(1f, 1f, 1f, 1.0f);

    private const float MinScale = 0.05f;
    private const float MaxScale = 100.0f;

    private float _panStartX;
    private float _panStartY;
    private float _offsetX;
    private float _offsetY;
    private float _panStartOffsetX;
    private float _panStartOffsetY;
    private float _scale = 1.0f;

    public Direct2dContext()
    {
        _multiDeviceRenderer = new MultiDeviceRenderer(_direct2DWrapper);
    }

    public event EventHandler<double>? Rendered;

    public int Width => _direct2DWrapper.Width;
    public int Height => _direct2DWrapper.Height;
    public List<IDrawingElement> DrawingElements { get; } = new();

    /// <summary>
    /// 开启后：元素数量超过 MultiThreadThreshold 时，使用多个独立 Device 离屏并行绘制。
    /// </summary>
    public bool EnableMultiThread
    {
        get => _multiDeviceRenderer.Enabled;
        set => _multiDeviceRenderer.Enabled = value;
    }

    /// <summary>
    /// 多 Device 数量。建议 2～4；对象特别多时再提高。
    /// </summary>
    public int MultiThreadDeviceCount
    {
        get => _multiDeviceRenderer.DeviceCount;
        set => _multiDeviceRenderer.DeviceCount = value;
    }

    /// <summary>
    /// 元素太少时，多 Device 的离屏绘制 + 合成成本会超过收益。
    /// </summary>
    public int MultiThreadThreshold
    {
        get => _multiDeviceRenderer.Threshold;
        set => _multiDeviceRenderer.Threshold = value;
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        _multiDeviceRenderer.Reset();
        _direct2DWrapper.SetTarget(hwnd, width, height);
        RenderCurrentView();
    }

    public void HwndResized(int width, int height)
    {
        // Resize 后，离屏共享 Texture 尺寸已经不匹配，必须重建。
        _multiDeviceRenderer.Reset();
        _direct2DWrapper.TargetResized(width, height);
        RenderCurrentView();
    }

    public void Render() => RenderCurrentView();

    public void ClearData()
    {
        if (_direct2DWrapper.Context is not null)
            _direct2DWrapper.Context.Transform = Matrix3x2.Identity;

        DrawingElements.Clear();
        _direct2DWrapper.Direct2DResourceCache?.ClearCache();
        _multiDeviceRenderer.ClearResourceCaches();

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
        _multiDeviceRenderer.Dispose();
        _direct2DWrapper.Dispose();
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
        _offsetX = _panStartOffsetX + (x - _panStartX);
        _offsetY = _panStartOffsetY + (y - _panStartY);
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

        var worldX = (centerX - _offsetX) / oldScale;
        var worldY = (centerY - _offsetY) / oldScale;

        _scale = newScale;
        _offsetX = centerX - worldX * newScale;
        _offsetY = centerY - worldY * newScale;

        RenderCurrentView();
    }

    private void RenderCurrentView()
    {
        if (_direct2DWrapper.Context is null)
            return;

        _stopwatch.Restart();
        InternalRender();
        _stopwatch.Stop();

        Rendered?.Invoke(this, _stopwatch.ElapsedMilliseconds);
    }

    private void InternalRender()
    {
        var mainContext = _direct2DWrapper.Context;
        var mainCache = _direct2DWrapper.Direct2DResourceCache;

        if (mainContext is null || mainCache is null)
            return;

        // 避免 UI 线程修改 DrawingElements 时，Parallel.For 正在遍历同一个 List。
        var elements = DrawingElements.ToArray();

        MultiDeviceFrameLease? multiDeviceLease = null;
        var mainDrawEnded = false;

        mainContext.BeginDraw();

        try
        {
            Clear(mainContext);

            if (!_multiDeviceRenderer.TryDraw(elements, mainContext, _offsetX, _offsetY, _scale, out multiDeviceLease))
            {
                DrawSingleThread(elements, mainCache, mainContext);
            }

            // DrawImage 命令通常到 EndDraw 才真正提交。
            // 因此 multiDeviceLease 必须在 EndDraw 之后再 Dispose，不能提前释放 KeyedMutex。
            mainContext.EndDraw();
            mainDrawEnded = true;
        }
        finally
        {
            if (!mainDrawEnded)
            {
                try { mainContext.EndDraw(); } catch { }
            }

            multiDeviceLease?.Dispose();
        }

        _direct2DWrapper.Present();
    }

    private void DrawSingleThread(
        IReadOnlyList<IDrawingElement> elements,
        Direct2DResourceCache cache,
        ID2D1DeviceContext context)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            elements[i].Draw(cache, context, _offsetX, _offsetY, _scale);
        }
    }

    private static void Clear(ID2D1DeviceContext context)
    {
        context.Transform = Matrix3x2.Identity;
        context.Clear(BackgroundColor);
    }

    private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    private static bool AlmostSame(float a, float b) => Math.Abs(a - b) <= 0.000001f;
}
