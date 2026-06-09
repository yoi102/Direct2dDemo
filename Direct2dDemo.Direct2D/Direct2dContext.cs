using Direct2dDemo.Shared;
using Direct2dDemo.Shared.Elements;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Vanara.PInvoke;
using static Vanara.PInvoke.D2d1;
using static Vanara.PInvoke.DXGI;
using D2D1_COLOR_F = Vanara.PInvoke.DXGI.D3DCOLORVALUE;

namespace Direct2dDemo.Direct2D;

public class Direct2dContext : IDrawingContext, ICanvasContext
{
    private readonly Stopwatch stopwatch = new();

    public event EventHandler<double>? Rendered;

    public int Width => direct2DWrapper.Width;
    public int Height => direct2DWrapper.Height;
    public List<IDrawingElement> DrawingElements { get; } = new List<IDrawingElement>();

    private readonly Direct2DWrapper direct2DWrapper = new Direct2DWrapper();

    private static readonly D2D1_COLOR_F background = new D2D1_COLOR_F(1f, 1f, 1f, 1.0f);

    private ID2D1CommandList? _commandList;
    private bool _commandListDirty = true;

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
        //强制更新
        _commandListDirty = true;
        Direct2DWrapper.SafeRelease(ref _commandList);
        RenderCurrentView();
    }

    private void RenderCurrentView()
    {
        if (direct2DWrapper.Context is null)
            return;

        stopwatch.Restart();

        RenderCommandList();

        stopwatch.Stop();
        Rendered?.Invoke(this, stopwatch.ElapsedMilliseconds);
    }

    private void Clear()
    {
        if (direct2DWrapper.Context is null)
            return;

        direct2DWrapper.Context.SetTransform(D2D_MATRIX_3X2_F.Identity());
        direct2DWrapper.Context.Clear(background);
    }

    public void HwndResized(int width, int height)
    {
        direct2DWrapper.TargetResized(width, height);
        InvalidateCommandList();
        RenderCurrentView();
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        direct2DWrapper.SetTarget(hwnd, width, height);
        InvalidateCommandList();
        RenderCurrentView();
    }

    public void ClearData()
    {
        direct2DWrapper.Context?.SetTransform(D2D_MATRIX_3X2_F.Identity());

        DrawingElements.Clear();
        direct2DWrapper.ClearCache();

        _offsetX = 0;
        _offsetY = 0;
        _panStartX = 0;
        _panStartY = 0;
        _scale = 1.0f;

        InvalidateCommandList();
    }

    public void Dispose()
    {
        Direct2DWrapper.SafeRelease(ref _commandList);
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

        InvalidateCommandList();
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

        InvalidateCommandList();
        RenderCurrentView();
    }

    private void InvalidateCommandList()
    {
        _commandListDirty = true;
        Direct2DWrapper.SafeRelease(ref _commandList);
    }

    [MemberNotNullWhen(true, nameof(_commandList))]
    private bool BuildCommandList()
    {
        var context = direct2DWrapper.Context;
        if (context is null)
            return false;

        Direct2DWrapper.SafeRelease(ref _commandList);
        _commandList = context.CreateCommandList();

        ID2D1Image? oldTarget = null;

        try
        {
            context.GetTarget(out oldTarget);
            context.SetTarget(_commandList);

            context.BeginDraw();
            context.SetTransform(D2D_MATRIX_3X2_F.Identity());

            foreach (var element in DrawingElements)
            {
                // command list 里记录的是已经转换后的屏幕坐标。
                element.Draw(direct2DWrapper, _offsetX, _offsetY, _scale);
            }

            context.EndDraw().ThrowIfFailed();
            _commandList.Close();

            _commandListDirty = false;
            return true;
        }
        catch
        {
            Direct2DWrapper.SafeRelease(ref _commandList);
            _commandListDirty = true;
            throw;
        }
        finally
        {
            context.SetTarget(oldTarget);
            Direct2DWrapper.SafeRelease(ref oldTarget);
        }
    }

    private void RenderCommandList()
    {
        var context = direct2DWrapper.Context;
        if (context is null)
            return;

        if (_commandListDirty || _commandList is null)
        {
            if (!BuildCommandList())
                return;
        }

        direct2DWrapper.BeginDraw();

        Clear();

        // 注意：这里必须保持 Identity。
        // offset / scale 已经被 DrawExtension 烘焙进 command list 了。
        context.SetTransform(D2D_MATRIX_3X2_F.Identity());
        context.DrawImage(
            _commandList,
            IntPtr.Zero,
            null,
            D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR,
            D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

        context.SetTransform(D2D_MATRIX_3X2_F.Identity());

        direct2DWrapper.EndDraw();
        direct2DWrapper.Present();
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
