using Direct2dDemo.Shared;
using System.Windows;

namespace Direct2dDemo.CustomControl;

/// <summary>
/// Interaction logic for HwndHost.xaml
/// </summary>
public partial class HwndHost : System.Windows.Controls.UserControl
{
    public HwndHost()
    {
        InitializeComponent();
        panelHost.Resize += PanelHost_Resize;
        panelHost.Paint += PanelHost_Paint;
        panelHost.MouseMove += PanelHost_MouseMove;
        panelHost.MouseDown += PanelHost_MouseDown;
        panelHost.MouseWheel += PanelHost_MouseWheel;
        panelHost.MouseUp += PanelHost_MouseUp;
        this.Loaded += HwndHost_Loaded;
    }

    private void PanelHost_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            if (DrawingContext is ICanvasContext canvasContext)
            {
                canvasContext.EndPan(e.X, e.Y);
            }
        }
    }

    private void HwndHost_Loaded(object sender, RoutedEventArgs e)
    {
        DrawingContext.Initialize(panelHost.Handle, panelHost.Width, panelHost.ClientSize.Height);
    }

    private void PanelHost_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (DrawingContext is ICanvasContext canvasContext)
        {
            canvasContext.Zoom(e.Delta > 0 ? 1.1f : 0.9f, e.X, e.Y);
        }
    }


    private void PanelHost_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            if (DrawingContext is ICanvasContext canvasContext)
            {
                canvasContext.BeginPan(e.X, e.Y);
            }
        }
    }

    private void PanelHost_MouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            if (DrawingContext is ICanvasContext canvasContext)
            {
                canvasContext.Pan(e.X, e.Y);
            }
        }
    }

    private void PanelHost_Paint(object? sender, PaintEventArgs e)
    {
        if (DrawingContext is not IDrawingGdiContext drawingGdiContext)
            return;

        var hdc = e.Graphics.GetHdc();

        drawingGdiContext.BitBlt(hdc);

        e.Graphics.ReleaseHdc(hdc);
    }

    private void PanelHost_Resize(object? sender, EventArgs e)
    {
        if (DrawingContext is null)
            return;
        DrawingContext.HwndResized(panelHost.ClientSize.Width, panelHost.ClientSize.Height);
    }

    public IDrawingContext DrawingContext
    {
        get { return (IDrawingContext)GetValue(DrawingContextProperty); }
        set { SetValue(DrawingContextProperty, value); }
    }

    // Using a DependencyProperty as the backing store for IDrawingContext.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty DrawingContextProperty =
        DependencyProperty.Register(
            nameof(DrawingContext),
            typeof(IDrawingContext),
            typeof(HwndHost),
            new PropertyMetadata(null, OnDirect2dContextChanged));

    private static void OnDirect2dContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HwndHost hwndHost)
            return;

        if (e.NewValue is not IDrawingContext drawingContext)
            throw new ArgumentNullException(nameof(e.NewValue));

        //drawingContext.Initialize(hwndHost.panelHost.Handle, hwndHost.panelHost.ClientSize.Width, hwndHost.panelHost.ClientSize.Height);
    }
}