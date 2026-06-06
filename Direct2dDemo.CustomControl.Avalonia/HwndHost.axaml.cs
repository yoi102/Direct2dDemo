using Avalonia;
using Avalonia.Controls;
using Direct2dDemo.Shared;

namespace Direct2dDemo.CustomControl.Avalonia;

public partial class HwndHost : UserControl
{
    public HwndHost()
    {
        InitializeComponent();
    }

    public IDrawingContext? DrawingContext
    {
        get => GetValue(DrawingContextProperty);
        set => SetValue(DrawingContextProperty, value);
    }

    public static readonly StyledProperty<IDrawingContext?> DrawingContextProperty =
        AvaloniaProperty.Register<HwndHost, IDrawingContext?>(
            nameof(DrawingContext));
}