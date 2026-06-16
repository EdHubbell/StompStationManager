using Sonulab.Core.Connection;
using Sonulab.Core.Transport;
using Xunit;

public class SonuConnectorTests
{
    static SerialLinkOptions Fast => new() { PollMs = 2, IdleGapMs = 15, MaxWaitMs = 300 };

    // A fake that only answers the name query when opened at the "correct" baud.
    static FakeSerialPort MakePort(int answersAtBaud)
    {
        var p = new FakeSerialPort();
        p.Responder = cmd =>
            (cmd == @"read root\sys\_name" && p.OpenedBaud == answersAtBaud)
                ? "root\\sys\\_name:{\"value\":\"AMP Station\"}\r\n" : "";
        return p;
    }

    [Fact] public async Task Connects_on_matching_baud()
    {
        var connector = new SonuConnector(() => MakePort(115200), Fast);
        var link = await connector.ConnectAsync(new[] { "COM6" }, new[] { 921600, 115200 });
        Assert.NotNull(link);
        Assert.True(link!.IsOpen);
    }

    [Fact] public async Task Returns_null_when_nothing_answers()
    {
        var connector = new SonuConnector(() => MakePort(115200), Fast);
        var link = await connector.ConnectAsync(new[] { "COM4", "COM5" }, new[] { 9600 });
        Assert.Null(link);
    }
}
