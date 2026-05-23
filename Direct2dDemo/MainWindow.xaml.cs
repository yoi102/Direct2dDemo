using System.Windows;

namespace Direct2dDemo;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel vm;

    public MainWindow()
    {
        InitializeComponent();
        this.DataContext = vm = new MainWindowViewModel();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        vm.Dispose();
    }
}