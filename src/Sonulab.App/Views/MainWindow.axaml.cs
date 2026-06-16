using Avalonia.Controls;

namespace Sonulab.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavList.SelectionChanged += OnNavSelectionChanged;
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        PresetsPage.IsVisible = NavList.SelectedIndex == 0;
        AmpsPage.IsVisible    = NavList.SelectedIndex == 1;
        IRsPage.IsVisible     = NavList.SelectedIndex == 2;
    }
}
