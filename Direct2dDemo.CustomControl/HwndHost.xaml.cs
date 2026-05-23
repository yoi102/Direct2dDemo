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
    }

    private void PanelHost_Resize(object? sender, EventArgs e)
    {
        if (Direct2dContext is null)
            return;
        Direct2dContext.HwndResized(panelHost.ClientSize.Width, panelHost.ClientSize.Height);
    }

    public IDirect2dContext Direct2dContext
    {
        get { return (IDirect2dContext)GetValue(Direct2dContextProperty); }
        set { SetValue(Direct2dContextProperty, value); }
    }

    // Using a DependencyProperty as the backing store for IDirect2dContext.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty Direct2dContextProperty =
        DependencyProperty.Register(
            nameof(Direct2dContext),
            typeof(IDirect2dContext),
            typeof(HwndHost),
            new PropertyMetadata(null, OnDirect2dContextChanged));

    private static void OnDirect2dContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HwndHost hwndHost)
            return;

        if (e.NewValue is not IDirect2dContext direct2DContext)
            throw new ArgumentNullException(nameof(e.NewValue));

        direct2DContext.Initialize(hwndHost.panelHost.Handle, hwndHost.panelHost.ClientSize.Width, hwndHost.panelHost.ClientSize.Height);
    }
}