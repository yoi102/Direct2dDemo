using Direct2dDemo.Shared;
using System.Drawing;

namespace Direct2dDemo.GDI;

public class GdiContext : IDrawingContext
{
    public event EventHandler? Initialized;

    private readonly GdiWrapper gdiWrapper = new GdiWrapper();

    public int Width => gdiWrapper.Width;
    public int Height => gdiWrapper.Height;

    public List<IDrawingElement> DrawingElements { get; } = new List<IDrawingElement>();

    private static readonly Color background = Color.White;

    public void Render()
    {
        if (!gdiWrapper.IsTargetReady)
            return;

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

        gdiWrapper.Present();
    }

    private void Clear()
    {
        gdiWrapper.Clear(background);
    }

    private void Draw()
    {
        foreach (var element in DrawingElements)
        {
            element.Draw(gdiWrapper);
        }
    }

    public void HwndResized(int width, int height)
    {
        gdiWrapper.TargetResized(width, height);
        Render();
    }

    public void Initialize(nint hwnd, int width, int height)
    {
        gdiWrapper.SetTarget(hwnd, width, height);
        Render();
        Initialized?.Invoke(this, EventArgs.Empty);
    }

    public void ClearData()
    {
        DrawingElements.Clear();
    }

    public void Present()
    {
        gdiWrapper.Present();
    }

    public void Dispose()
    {
        gdiWrapper.Dispose();
    }
}