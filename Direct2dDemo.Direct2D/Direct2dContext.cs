using Direct2dDemo.Shared;
using Vanara.PInvoke;
using D2D1_COLOR_F = Vanara.PInvoke.DXGI.D3DCOLORVALUE;

namespace Direct2dDemo.Direct2D;

public class Direct2dContext : IDrawingContext
{
    public event EventHandler? Initialized;
    public int Width => direct2DWrapper.Width;
    public int Height => direct2DWrapper.Height;
    public List<IDrawingElement> DrawingElements { get; } = new List<IDrawingElement>();

    private readonly Direct2DWrapper direct2DWrapper = new Direct2DWrapper();

    private static readonly D2D1_COLOR_F background = new D2D1_COLOR_F(1f, 1f, 1f, 1.0f);

    public void Render()
    {
        if (direct2DWrapper.Context is null)
            return;
        direct2DWrapper.BeginDraw();

        Clear();

        Draw();

        direct2DWrapper.EndDraw();
        direct2DWrapper.Present();
    }

    private void Clear()
    {
        if (direct2DWrapper.Context is null)
            return;
        direct2DWrapper.Context.Clear(background);
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
        Initialized?.Invoke(this, EventArgs.Empty);
    }

    public void ClearData()
    {
        DrawingElements.Clear();
        direct2DWrapper.ClearCache();
    }

    public void Dispose()
    {
        direct2DWrapper.Dispose();
    }
}