using Avalonia.Controls;
using Avalonia.Interactivity;
using Direct2dDemo.ViewModels;

namespace Direct2dDemo.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        this.Loaded += async (sender, e) =>
        {
            if (this.DataContext is MainWindowViewModel mainWindowView)
            {
                await mainWindowView.InitAsync();
            }
        };
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
    }
}