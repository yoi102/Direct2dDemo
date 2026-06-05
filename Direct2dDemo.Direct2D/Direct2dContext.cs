using Direct2dDemo.Shared;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Vanara.PInvoke;
using static Vanara.PInvoke.D2d1;
using static Vanara.PInvoke.DXGI;
using D2D1_COLOR_F = Vanara.PInvoke.DXGI.D3DCOLORVALUE;

namespace Direct2dDemo.Direct2D;

public class Direct2dContext : IDrawingContext, ICanvasContext
{
    private Stopwatch stopwatch = new Stopwatch();

    public event EventHandler<double>? Rendered;

    public int Width => direct2DWrapper.Width;
    public int Height => direct2DWrapper.Height;
    public List<IDrawingElement> DrawingElements { get; } = new List<IDrawingElement>();

    private readonly Direct2DWrapper direct2DWrapper = new Direct2DWrapper();

    private static readonly D2D1_COLOR_F background = new D2D1_COLOR_F(1f, 1f, 1f, 1.0f);

    public void Render()
    {
        if (direct2DWrapper.Context is null)
            return;
        stopwatch.Restart();

        direct2DWrapper.BeginDraw();
        Clear();
        Draw();
        direct2DWrapper.EndDraw();
        direct2DWrapper.Present();

        stopwatch.Stop();
        Rendered?.Invoke(this, stopwatch.ElapsedMilliseconds);

        Direct2DWrapper.SafeRelease(ref _commandList);
        Direct2DWrapper.SafeRelease(ref staticBitmap);
    }

    private void Clear()
    {
        if (direct2DWrapper.Context is null)
            return;
        direct2DWrapper.Context.Clear(background);
        Direct2DWrapper.SafeRelease(ref _commandList);
    }

    private void Draw()
    {
        if (direct2DWrapper.Context is null)
            return;

        foreach (var element in DrawingElements)
        {
            element.Draw(direct2DWrapper);
        }
    }

    public void HwndResized(int width, int height)
    {
        direct2DWrapper.TargetResized(width, height);
        Render();
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        direct2DWrapper.SetTarget(hwnd, width, height);
        Render();
    }

    public void ClearData()
    {
        direct2DWrapper.Context?.SetTransform(D2D_MATRIX_3X2_F.Identity());
        DrawingElements.Clear();
        direct2DWrapper.ClearCache();
    }

    public void Dispose()
    {
        direct2DWrapper.Dispose();
    }

    private ID2D1Bitmap? staticBitmap;

    private float _offsetX;
    private float _offsetY;

    public void Move(int deltaX, int deltaY)
    {
        //比重新绘制快一些。
        stopwatch.Restart();

        _offsetX += deltaX;
        _offsetY += deltaY;

        //if (staticBitmap is null && direct2DWrapper.Context is not null)
        //{
        //    staticBitmap = this.direct2DWrapper.CreateBitmap();
        //    direct2DWrapper.Context.GetTarget(out var oldImage);
        //    direct2DWrapper.Context.SetTarget(staticBitmap);
        //    direct2DWrapper.Context.BeginDraw();
        //    direct2DWrapper.Context.Clear(background);
        //    Draw();
        //    direct2DWrapper.Context.EndDraw();
        //    direct2DWrapper.Present();
        //    direct2DWrapper.Context.SetTarget(oldImage);
        //}
        //if (staticBitmap is not null && direct2DWrapper.Context is not null)
        //{
        //    direct2DWrapper.BeginDraw();
        //    direct2DWrapper.Context.Clear(background);
        //    direct2DWrapper.Context.DrawBitmap(
        //     staticBitmap,
        //     new D2D_RECT_F(_offsetX, _offsetY, _offsetX + Width, _offsetY + Height),
        //     1.0f,
        //     D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_NEAREST_NEIGHBOR,
        //     new D2D_RECT_F(0, 0, Width, Height));

        //    direct2DWrapper.EndDraw();
        //    direct2DWrapper.Present();
        //}
        RenderCommandList();

        //暂时不知道怎么增量更新
        _offsetX = 0;
        _offsetY = 0;

        stopwatch.Stop();
        Rendered?.Invoke(this, stopwatch.ElapsedMilliseconds);
    }

    private ID2D1CommandList? _commandList;

    [MemberNotNullWhen(true, nameof(_commandList))]
    private bool BuildCommandList()
    {
        if (direct2DWrapper.Context is null)
            return false;

        Direct2DWrapper.SafeRelease(ref _commandList);
        _commandList = direct2DWrapper.Context.CreateCommandList();

        direct2DWrapper.Context.GetTarget(out var oldTarget);

        direct2DWrapper.Context.SetTarget(_commandList);

        direct2DWrapper.Context.BeginDraw();

        direct2DWrapper.Context.SetTransform(D2D_MATRIX_3X2_F.Identity());

        foreach (var element in DrawingElements)
        {
            element.Draw(direct2DWrapper);
        }

        direct2DWrapper.Context.EndDraw();

        _commandList.Close();

        direct2DWrapper.Context.SetTarget(oldTarget);
        return true;
    }

    private void RenderCommandList()
    {
        if (direct2DWrapper.Context is null)
            return;

        if (_commandList is null)
        {
            if (!BuildCommandList())
                return;
        }

        direct2DWrapper.BeginDraw();

        direct2DWrapper.Context.SetTransform(D2D_MATRIX_3X2_F.Identity());
        direct2DWrapper.Context.Clear(background);

        direct2DWrapper.Context.SetTransform(
            new D2D_MATRIX_3X2_F(
                _scale, 0,
                0, _scale,
            _offsetX, _offsetY));
        direct2DWrapper.Context.DrawImage(
            _commandList,
            IntPtr.Zero,
            null,
            D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_LINEAR,
            D2D1_COMPOSITE_MODE.D2D1_COMPOSITE_MODE_SOURCE_OVER);

        //direct2DWrapper.Context.SetTransform(D2D_MATRIX_3X2_F.Identity());

        direct2DWrapper.EndDraw();
        direct2DWrapper.Present();
    }

    private float _scale = 1.0f;
    private const float MinScale = 0.05f;
    private const float MaxScale = 100.0f;

    public void Zoom(float zoomFactor, int centerX, int centerY)
    {
        if (zoomFactor <= 0)
            return;
        Direct2DWrapper.SafeRelease(ref staticBitmap);
        stopwatch.Restart();

        var oldScale = _scale;

        var newScale = Clamp(oldScale * zoomFactor, MinScale, MaxScale);

        // 实际缩放倍率，考虑到了 MinScale / MaxScale 限制
        var actualFactor = newScale / oldScale;

        // 关键：保持鼠标所在位置的模型点不动
        _offsetX = centerX - (centerX - _offsetX) * actualFactor;
        _offsetY = centerY - (centerY - _offsetY) * actualFactor;

        _scale = newScale;

        RenderCommandList();

        stopwatch.Stop();
        Rendered?.Invoke(this, stopwatch.ElapsedMilliseconds);
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }
}