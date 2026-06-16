using System.Text;
using Sonulab.Core.Transport;
using Xunit;

public class SerialSonuLinkTests
{
    static SerialLinkOptions Fast => new() { PollMs = 2, IdleGapMs = 15, MaxWaitMs = 500 };

    [Fact] public async Task SendAsync_appends_nul_and_returns_response()
    {
        var port = new FakeSerialPort { Responder = c => c == @"read root\sys\_name" ? "root\\sys\\_name:{\"value\":\"AMP Station\"}\r\n" : "" };
        var link = new SerialSonuLink(port, "COM6", 115200, Fast);
        await link.OpenAsync();
        var resp = await link.SendAsync(@"read root\sys\_name");
        Assert.Equal(@"read root\sys\_name", port.LastCommand);   // proves NUL framing assembled the command
        Assert.Contains("\"value\":\"AMP Station\"", resp);
    }

    [Fact] public async Task OpenAsync_opens_underlying_port_with_baud()
    {
        var port = new FakeSerialPort();
        var link = new SerialSonuLink(port, "COM6", 115200, Fast);
        await link.OpenAsync();
        Assert.True(link.IsOpen);
        Assert.Equal("COM6", port.OpenedPort);
        Assert.Equal(115200, port.OpenedBaud);
    }

    [Fact] public async Task SendAsync_returns_empty_when_no_response()
    {
        var port = new FakeSerialPort { Responder = _ => "" };  // device sends nothing (e.g. a write)
        var link = new SerialSonuLink(port, "COM6", 115200, Fast);
        await link.OpenAsync();
        Assert.Equal("", await link.SendAsync(@"write root\app\amp\on_off:{""value"":""OFF""}"));
    }

    [Fact] public async Task SendAsync_throws_if_not_open()
    {
        var link = new SerialSonuLink(new FakeSerialPort(), "COM6", 115200, Fast);
        await Assert.ThrowsAsync<InvalidOperationException>(() => link.SendAsync("read x"));
    }
}
