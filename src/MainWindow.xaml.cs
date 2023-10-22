using System.Windows;
using FilesScanner.Services;

namespace FilesScanner;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
        DataContext = ApplicationServiceLocator.GetService<MainWindowViewModel>();
    }
}