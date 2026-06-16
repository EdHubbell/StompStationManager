namespace Sonulab.Core.Transport;

public sealed class SerialLinkOptions
{
    public int PollMs { get; init; } = 10;
    public int IdleGapMs { get; init; } = 120;
    public int MaxWaitMs { get; init; } = 1500;
    public int OpenSettleMs { get; init; } = 0;      // delay after Open before first command (ESP32 DTR/RTS reset)
    public int ProbeAttempts { get; init; } = 1;     // identity-probe tries per (port,baud)
    public int ProbeRetryDelayMs { get; init; } = 300;
}
