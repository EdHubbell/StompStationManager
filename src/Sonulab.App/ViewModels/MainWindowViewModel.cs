using CommunityToolkit.Mvvm.ComponentModel;
using Sonulab.Core.Connection;
using Sonulab.Core.Transport;

namespace Sonulab.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private ConnectionViewModel _connection;
    [ObservableProperty] private PresetListViewModel? _presets;
    [ObservableProperty] private ParameterEditorViewModel? _editor;

    public MainWindowViewModel()
    {
        var options = new SerialLinkOptions { OpenSettleMs = 1500, ProbeAttempts = 3 };
        var connector = new SonuConnector(() => new SystemSerialPort(), options);
        var session = new DeviceSession(connector, new CompatibilityChecker(FirmwareCatalog.Default));

        var ports = System.IO.Ports.SerialPort.GetPortNames();
        var portList = (ports.Length > 0 ? ports : new[] { "COM6" }) as IReadOnlyList<string>;

        _connection = new ConnectionViewModel(session, portList);
        _connection.Connected += (_, _) =>
        {
            Presets = new PresetListViewModel(
                _connection.Repository!,
                _connection.Reorder!,
                _connection.WritesAllowed);
            _ = Presets.RefreshCommand.ExecuteAsync(null);
            Editor = new ParameterEditorViewModel(_connection.Client!);
        };
    }
}
