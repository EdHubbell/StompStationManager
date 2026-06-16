using Sonulab.Core;
using Sonulab.Core.Services;
using Xunit;

public class ReorderServiceTests
{
    static (DeviceRepository repo, FakePresetDevice dev) Seed()
    {
        var dev = new FakePresetDevice();
        // 4 presets in slots 0..3, content tagged so we can tell them apart
        dev.SeedSlot(0, "A", new[] { @"root\app\amp\amp:{""value"":""mA""}" });
        dev.SeedSlot(1, "B", new[] { @"root\app\amp\amp:{""value"":""mB""}" });
        dev.SeedSlot(2, "C", new[] { @"root\app\amp\amp:{""value"":""mC""}" });
        dev.SeedSlot(3, "D", new[] { @"root\app\amp\amp:{""value"":""mD""}" });
        return (new DeviceRepository(new SonuClient(dev)), dev);
    }

    static async Task<string[]> Names(DeviceRepository repo) =>
        (await repo.ListPresetsAsync()).Select(s => s.Name).ToArray();

    [Fact] public async Task Move_down_reorders_names_in_order()
    {
        var (repo, dev) = Seed(); await dev.OpenAsync();
        await new ReorderService(repo).MoveAsync(from: 1, to: 3);
        var names = await Names(repo);
        Assert.Equal("A", names[0]);
        Assert.Equal("C", names[1]);
        Assert.Equal("D", names[2]);
        Assert.Equal("B", names[3]);     // B moved from slot 1 to slot 3
    }

    [Fact] public async Task Move_carries_content_with_the_preset()
    {
        var (repo, dev) = Seed(); await dev.OpenAsync();
        await new ReorderService(repo).MoveAsync(from: 1, to: 3);
        var slot3 = await repo.ReadPresetAsync(3);
        Assert.Equal("\"mB\"", slot3.GetValueJson(@"root\app\amp\amp"));   // B's content followed B
        var slot1 = await repo.ReadPresetAsync(1);
        Assert.Equal("\"mC\"", slot1.GetValueJson(@"root\app\amp\amp"));   // C shifted up into slot 1
    }

    [Fact] public async Task Move_up_reorders_correctly()
    {
        var (repo, dev) = Seed(); await dev.OpenAsync();
        await new ReorderService(repo).MoveAsync(from: 3, to: 0);
        Assert.Equal(new[] { "D", "A", "B", "C" }, (await Names(repo))[..4]);
    }

    [Fact] public async Task Same_index_move_is_noop()
    {
        var (repo, dev) = Seed(); await dev.OpenAsync();
        await new ReorderService(repo).MoveAsync(2, 2);
        Assert.Equal(new[] { "A", "B", "C", "D" }, (await Names(repo))[..4]);
    }

    [Fact] public async Task Reports_progress()
    {
        var (repo, dev) = Seed(); await dev.OpenAsync();
        var seen = new List<ReorderProgress>();
        await new ReorderService(repo).MoveAsync(1, 3, new Progress<ReorderProgress>(p => { lock (seen) seen.Add(p); }));
        Assert.NotEmpty(seen);
        Assert.All(seen, p => Assert.True(p.Done <= p.Total));
    }

    // A device that fails the Nth save lets us prove rollback restores the original arrangement.
    sealed class FailAfterNSaves : FakePresetDevice
    {
        private readonly int _n; private int _saves;
        public FailAfterNSaves(int n) => _n = n;
        public override Task<string> SendAsync(string command, System.Threading.CancellationToken ct = default)
        {
            if (command.Contains("\"save\":\"save\"") && ++_saves == _n + 1)
                throw new System.IO.IOException("simulated write failure");
            return base.SendAsync(command, ct);
        }
    }

    [Fact] public async Task Rollback_restores_original_on_failure()
    {
        var dev = new FailAfterNSaves(1);   // allow the first slot's save, fail the next
        dev.SeedSlot(0, "A", new[] { @"root\app\amp\amp:{""value"":""mA""}" });
        dev.SeedSlot(1, "B", new[] { @"root\app\amp\amp:{""value"":""mB""}" });
        dev.SeedSlot(2, "C", new[] { @"root\app\amp\amp:{""value"":""mC""}" });
        dev.SeedSlot(3, "D", new[] { @"root\app\amp\amp:{""value"":""mD""}" });
        await dev.OpenAsync();
        var repo = new DeviceRepository(new SonuClient(dev));

        await Assert.ThrowsAnyAsync<System.Exception>(() => new ReorderService(repo).MoveAsync(1, 3));

        // After rollback the original A,B,C,D order and content are intact.
        var slots = await repo.ListPresetsAsync();
        Assert.Equal(new[] { "A", "B", "C", "D" }, slots.Take(4).Select(s => s.Name).ToArray());
        Assert.Equal("\"mB\"", (await repo.ReadPresetAsync(1)).GetValueJson(@"root\app\amp\amp"));
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(0, 30)]
    [InlineData(31, 1)]
    public async Task Out_of_range_indices_throw(int from, int to)
    {
        var (repo, dev) = Seed(); await dev.OpenAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new ReorderService(repo).MoveAsync(from, to));
    }

    [Fact] public async Task Moving_an_empty_slot_throws()
    {
        var (repo, dev) = Seed(); await dev.OpenAsync();   // slots 4..29 are empty
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ReorderService(repo).MoveAsync(10, 0));
    }
}
