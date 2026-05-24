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
    }

    private void PanelHost_Paint(object? sender, PaintEventArgs e)
    {
        if (DrawingContext is null)
            return;
        DrawingContext.Render();
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

        drawingContext.Initialize(hwndHost.panelHost.Handle, hwndHost.panelHost.ClientSize.Width, hwndHost.panelHost.ClientSize.Height);
    }
}