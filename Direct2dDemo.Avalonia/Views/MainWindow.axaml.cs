using Avalonia.Controls;
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
        this.Closed += (sender, e) =>
        {
            if (this.DataContext is MainWindowViewModel mainWindowView)
            {
                mainWindowView.Dispose();
            }
        };
    }
}