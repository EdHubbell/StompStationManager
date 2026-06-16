using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace Sonulab.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavView.SelectionChanged += OnNavSelectionChanged;
        // Select Presets by default
        NavView.SelectedItem = NavPresets;
    }

    private void OnNavSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        PresetsPage.IsVisible = ReferenceEquals(e.SelectedItem, NavPresets);
        AmpsPage.IsVisible    = ReferenceEquals(e.SelectedItem, NavAmps);
        IRsPage.IsVisible     = ReferenceEquals(e.SelectedItem, NavIRs);
    }
}
