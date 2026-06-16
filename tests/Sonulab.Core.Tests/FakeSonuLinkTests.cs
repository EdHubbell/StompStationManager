using Sonulab.Core.Transport;
using Xunit;

public class FakeSonuLinkTests
{
    [Fact] public async Task Read_returns_seeded_scalar()
    {
        var link = new FakeSonuLink();
        link.SeedScalar(@"root\sys\_name", "\"AMP Station\"");
        await link.OpenAsync();
        var resp = await link.SendAsync(@"read root\sys\_name");
        Assert.Contains("\"value\":\"AMP Station\"", resp);
    }

    [Fact] public async Task Write_then_read_round_trips()
    {
        var link = new FakeSonuLink();
        link.SeedScalar(@"root\app\amp\on_off", "\"ON\"");
        await link.OpenAsync();
        await link.SendAsync(@"write root\app\amp\on_off:{""value"":""OFF""}");
        var resp = await link.SendAsync(@"read root\app\amp\on_off");
        Assert.Contains("\"value\":\"OFF\"", resp);
    }

    [Fact] public async Task DWrite_then_DRead_round_trips_a_chunk()
    {
        var link = new FakeSonuLink();
        await link.OpenAsync();
        await link.SendAsync(@"dwrite root\presets:{""index"":2,""chunk"":1,""value"":""41424344""}");
        var resp = await link.SendAsync(@"dread root\presets:{""index"":2,""chunk"":1}");
        Assert.Contains("\"value\":\"41424344\"", resp);
    }

    [Fact] public async Task ReadList_returns_seeded_names()
    {
        var link = new FakeSonuLink();
        link.SeedList(@"root\presets", new[] { "A", "", "B" });
        await link.OpenAsync();
        var resp = await link.SendAsync(@"read root\presets");
        Assert.Contains("[\"A\",\"\",\"B\"]", resp);
    }
}
