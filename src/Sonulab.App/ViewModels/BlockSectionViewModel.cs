using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sonulab.App.ViewModels;

public sealed partial class BlockSectionViewModel : ObservableObject
{
    public string Header { get; }
    [ObservableProperty] private bool _isExpanded = true;
    public ObservableCollection<ParameterFieldViewModel> Fields { get; } = new();
    public ObservableCollection<SubGroupViewModel> SubGroups { get; } = new();
    public BlockSectionViewModel(string header) => Header = header;
}
