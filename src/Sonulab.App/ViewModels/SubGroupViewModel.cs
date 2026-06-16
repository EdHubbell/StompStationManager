using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sonulab.App.ViewModels;

public sealed partial class SubGroupViewModel : ObservableObject
{
    public string Header { get; }
    public ObservableCollection<ParameterFieldViewModel> Fields { get; } = new();
    public SubGroupViewModel(string header) => Header = header;
}
