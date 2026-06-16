using Sonulab.Core;
using Sonulab.Core.Services;
using Xunit;

public class DeviceRepositoryTests
{
    static async Task<(DeviceRepository repo, FakePresetDevice dev)> Repo()
    {
        var d = new FakePresetDevice();
        d.SeedSlot(0, "Alpha", new[] { @"root\app\amp\amp:{""value"":""AmpA""}" });
        d.SeedSlot(1, "Beta", new[] { @"root\app\amp\amp:{""value"":""AmpB""}" });
        await d.OpenAsync();
        return (new DeviceRepository(new SonuClient(d)), d);
    }

    [Fact] public async Task ListPresets_returns_30_slots_with_names_and_emptiness()
    {
        var (repo, _) = await Repo();
        var slots = await repo.ListPresetsAsync();
        Assert.Equal(30, slots.Count);
        Assert.Equal(0, slots[0].Index);
        Assert.Equal("Alpha", slots[0].Name);
        Assert.False(slots[0].IsEmpty);
        Assert.True(slots[2].IsEmpty);
    }

    [Fact] public async Task RenameAsync_changes_only_the_name()
    {
        var (repo, _) = await Repo();
        await repo.RenameAsync(0, "Renamed");
        var slots = await repo.ListPresetsAsync();
        Assert.Equal("Renamed", slots[0].Name);
    }

    [Fact] public async Task DeleteAsync_empties_the_slot()
    {
        var (repo, _) = await Repo();
        await repo.DeleteAsync(1);
        var slots = await repo.ListPresetsAsync();
        Assert.True(slots[1].IsEmpty);
    }

    [Fact] public async Task Select_then_SaveCurrentAs_named_slot_copies_content()
    {
        var (repo, dev) = await Repo();
        await repo.RenameAsync(5, "Clone");          // create slot 5 named "Clone"
        await repo.SelectPresetAsync("Alpha");
        await repo.SaveCurrentAsAsync("Clone");
        var a = await repo.ReadPresetAsync(0);
        var c = await repo.ReadPresetAsync(5);
        Assert.Equal(a.ToBytes(), c.ToBytes());
    }

    [Fact] public async Task ReadPresetAsync_returns_slot_content_document()
    {
        var (repo, _) = await Repo();
        var doc = await repo.ReadPresetAsync(0);
        Assert.Equal("\"AmpA\"", doc.GetValueJson(@"root\app\amp\amp"));
    }
}
