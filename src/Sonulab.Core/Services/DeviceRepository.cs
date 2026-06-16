using Sonulab.Core.Model;

namespace Sonulab.Core.Services;

public sealed class DeviceRepository
{
    public const int SlotCount = 30;
    public const int PresetChunks = 64;          // 8192 / 128
    private const string PresetsList = @"root\presets";
    private const string PresetNode = @"root\app\preset";

    private readonly SonuClient _client;
    public DeviceRepository(SonuClient client) => _client = client;

    public async Task<IReadOnlyList<PresetSlot>> ListPresetsAsync(CancellationToken ct = default)
    {
        var names = await _client.ReadListAsync(PresetsList, ct);
        var slots = new List<PresetSlot>(SlotCount);
        for (int i = 0; i < SlotCount; i++)
            slots.Add(new PresetSlot(i, i < names.Count ? names[i] : ""));
        return slots;
    }

    public Task SelectPresetAsync(string name, CancellationToken ct = default) =>
        _client.WriteAsync(PresetNode, "\"" + name + "\"", ct);

    public Task SaveCurrentAsAsync(string name, CancellationToken ct = default) =>
        _client.SaveAsync(PresetNode, name, ct);

    public Task RenameAsync(int index, string name, CancellationToken ct = default) =>
        _client.DWriteChunkAsync(PresetsList, index, -1, NamePad(name), ct);

    public Task DeleteAsync(int index, CancellationToken ct = default) =>
        _client.DWriteChunkAsync(PresetsList, index, -1, new byte[128], ct);

    public async Task<PresetDocument> ReadPresetAsync(int index, CancellationToken ct = default)
    {
        var bytes = await _client.DReadBlobAsync(PresetsList, index, PresetChunks, ct);
        return PresetDocument.Parse(bytes);
    }

    private static byte[] NamePad(string name)
    {
        var buf = new byte[128];
        var b = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(b, buf, Math.Min(b.Length, 128));
        return buf;
    }
}
