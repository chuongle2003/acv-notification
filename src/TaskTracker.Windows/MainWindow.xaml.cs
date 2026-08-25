using System.Windows;
using TaskTracker.Windows.Views;

namespace TaskTracker.Windows;

public partial class MainWindow : Window
{
    public MainWindow(ViewModels.MainViewModel viewModel, DeadlineReviewView reviewView)
    {
        InitializeComponent();
        DataContext = viewModel;

        // DeadlineReviewView requires constructor injection (no default ctor), so
        // it is hosted via ContentControl instead of being declared in XAML.
        ReviewHost.Content = reviewView;
    }
}
