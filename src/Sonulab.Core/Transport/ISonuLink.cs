namespace Sonulab.Core.Transport;

public interface ISonuLink
{
    bool IsOpen { get; }
    Task OpenAsync(CancellationToken ct = default);
    void Close();
    Task<string> SendAsync(string command, CancellationToken ct = default); // command WITHOUT trailing NUL
}
