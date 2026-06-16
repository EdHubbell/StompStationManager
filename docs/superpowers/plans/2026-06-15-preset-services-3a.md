# Preset Services (Plan 3a of 4) — Repository, Write-to-Slot, Duplicate, Backup

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the preset service layer — list/select/save/rename/delete, read a slot's full content, the proven **write-preset-to-slot** primitive (name → replay params → save → verify), duplicate, and backup/restore — all TDD'd against a behaviorally-faithful in-memory device, then guard-tested on hardware.

**Architecture:** A `FakePresetDevice : ISonuLink` faithfully models the real preset semantics (per-slot name+content, live app state, `save` targets the slot whose name matches, preset content is NOT `dwrite`-able) so the services get real TDD coverage offline. `DeviceRepository` and `BackupService` sit on `SonuClient` (Plan 1) and reuse its verbs. The multi-slot **reorder** algorithm is deliberately deferred to Plan 3b, which builds on the `WritePresetToSlotAsync` primitive defined here.

**Tech Stack:** .NET 10, C#, xUnit. Builds on Plan 1 (`SonuClient`, `PresetDocument`, `NodeRecord`) and Plan 2 (`DeviceSession`).

**Protocol reference (PROTOCOL.md, all hardware-confirmed):**
- Write a preset to slot N: (1) `dwrite root\presets:{"index":N,"chunk":-1,"value":<hex name, 128B>}` names the slot; (2) `write root\app\<path>:{"value":..}` for each line of the `.pst` loads live state; (3) `write root\app\preset:{"value":"<name>","save":"save"}` saves live state into the slot whose name matches. Verified byte-identical.
- Preset **content** is NOT writable via `dwrite` (only the name chunk `-1` is). Content comes from save-from-live.
- Delete = name chunk `-1` set to 128 zero bytes. Read content = `dread root\presets:{"index":N,"chunk":1..64}`.
- `save` targets the slot whose **name** equals the value → names must be unique when saving.

## Public API defined by this plan

```csharp
namespace Sonulab.Core.Model;
public sealed record PresetSlot(int Index, string Name) { public bool IsEmpty => string.IsNullOrEmpty(Name); }

namespace Sonulab.Core.Services;
public sealed class DeviceRepository {
    public DeviceRepository(SonuClient client);
    public Task<IReadOnlyList<PresetSlot>> ListPresetsAsync(CancellationToken ct = default);     // always 30
    public Task SelectPresetAsync(string name, CancellationToken ct = default);
    public Task SaveCurrentAsAsync(string name, CancellationToken ct = default);
    public Task RenameAsync(int index, string name, CancellationToken ct = default);
    public Task DeleteAsync(int index, CancellationToken ct = default);
    public Task<PresetDocument> ReadPresetAsync(int index, CancellationToken ct = default);
    public Task WritePresetToSlotAsync(int index, string name, PresetDocument doc, bool verify = true, CancellationToken ct = default);
    public Task DuplicateAsync(int sourceIndex, int destIndex, string newName, CancellationToken ct = default);
    public const int SlotCount = 30;
    public const int PresetChunks = 64;   // 8192 / 128
}

namespace Sonulab.Core.Services;
public sealed class BackupService {
    public BackupService(DeviceRepository repo);
    public Task<int> SnapshotAllAsync(string folder, CancellationToken ct = default);   // returns # presets written
    public Task RestoreSlotAsync(int index, string pstPath, CancellationToken ct = default);
}

namespace Sonulab.Core.Connection;
public static class FirmwareCatalog {
    public static IReadOnlyList<TestedFirmware> Load(string json);            // parse compatibility.json text
    public static IReadOnlyList<TestedFirmware> Default { get; }              // embedded default (2.5.1)
}
```

## File structure

```
src/Sonulab.Core/
  Model/PresetSlot.cs                  (create)
  Services/DeviceRepository.cs         (create)
  Services/BackupService.cs            (create)
  Connection/FirmwareCatalog.cs        (create)
  Connection/compatibility.json        (create — embedded resource)
  Sonulab.Core.csproj                  (modify: embed compatibility.json)
tests/Sonulab.Core.Tests/
  FakePresetDevice.cs                  (create — faithful test device)
  FakePresetDeviceTests.cs
  DeviceRepositoryTests.cs
  WritePresetToSlotTests.cs
  DuplicateTests.cs
  BackupServiceTests.cs
  FirmwareCatalogTests.cs
docs/
  HARDWARE-VALIDATION-plan3a.md        (create — guarded-write checklist, Task 9)
```

---

### Task 1: FakePresetDevice — faithful in-memory device

**Files:** Create `tests/Sonulab.Core.Tests/FakePresetDevice.cs`, `tests/Sonulab.Core.Tests/FakePresetDeviceTests.cs`.

Models real semantics: 30 slots (name + ordered content lines), a "live" app-state line list, and the command set the services use. Preset **content** `dwrite` (chunk ≥ 1) is intentionally a NO-OP (matches firmware); content is set only via save-from-live.

- [ ] **Step 1: Write the failing tests**

`tests/Sonulab.Core.Tests/FakePresetDeviceTests.cs`:
```csharp
using System.Text;
using Sonulab.Core;
using Xunit;

public class FakePresetDeviceTests
{
    static FakePresetDevice Dev()
    {
        var d = new FakePresetDevice();
        d.SeedSlot(0, "Alpha", new[] { @"root\app\amp\amp:{""value"":""AmpA""}", @"root\app\amp\vol:{""value"":50.0}" });
        d.SeedSlot(1, "Beta",  new[] { @"root\app\amp\amp:{""value"":""AmpB""}", @"root\app\amp\vol:{""value"":60.0}" });
        return d;
    }

    [Fact] public async Task Read_presets_returns_30_names()
    {
        var d = Dev(); await d.OpenAsync();
        var c = new SonuClient(d);
        var names = await c.ReadListAsync(@"root\presets");
        Assert.Equal(30, names.Count);
        Assert.Equal("Alpha", names[0]);
        Assert.Equal("", names[2]);
    }

    [Fact] public async Task Select_then_save_to_other_named_slot_copies_content()
    {
        var d = Dev(); await d.OpenAsync(); var c = new SonuClient(d);
        // name slot 5 "Gamma", select Alpha (loads its live state), save as Gamma
        var nameBytes = new byte[128]; Encoding.ASCII.GetBytes("Gamma").CopyTo(nameBytes, 0);
        await c.DWriteChunkAsync(@"root\presets", 5, -1, nameBytes);
        await c.WriteAsync(@"root\app\preset", "\"Alpha\"");          // select (no save)
        await c.SaveAsync(@"root\app\preset", "Gamma");               // save live -> slot named Gamma (5)
        var blob5 = await c.DReadBlobAsync(@"root\presets", 5, 64);
        var blob0 = await c.DReadBlobAsync(@"root\presets", 0, 64);
        Assert.Equal(blob0, blob5);                                   // slot 5 now holds Alpha's content
    }

    [Fact] public async Task Content_dwrite_is_ignored_but_name_dwrite_works()
    {
        var d = Dev(); await d.OpenAsync(); var c = new SonuClient(d);
        await c.DWriteChunkAsync(@"root\presets", 0, 1, new byte[128]); // content write -> NO-OP
        var blob = await c.DReadBlobAsync(@"root\presets", 0, 64);
        Assert.Contains("AmpA", Encoding.ASCII.GetString(blob));        // content unchanged
        var zero = new byte[128];
        await c.DWriteChunkAsync(@"root\presets", 0, -1, zero);         // name -> empty = delete
        Assert.Equal("", (await c.ReadListAsync(@"root\presets"))[0]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FakePresetDeviceTests`
Expected: FAIL — `FakePresetDevice` does not exist.

- [ ] **Step 3: Implement FakePresetDevice**

`tests/Sonulab.Core.Tests/FakePresetDevice.cs`:
```csharp
using System.Text;
using System.Text.RegularExpressions;
using Sonulab.Core.Model;
using Sonulab.Core.Transport;

/// <summary>Faithful in-memory StompStation preset model for service tests.</summary>
public sealed class FakePresetDevice : ISonuLink
{
    private sealed class Slot { public string Name = ""; public List<string> Lines = new(); }
    private readonly Slot[] _slots = Enumerable.Range(0, 30).Select(_ => new Slot()).ToArray();
    private readonly List<string> _live = new();
    private readonly Dictionary<string, string> _scalars = new();

    public bool IsOpen { get; private set; }
    public Task OpenAsync(CancellationToken ct = default) { IsOpen = true; return Task.CompletedTask; }
    public void Close() => IsOpen = false;

    public void SeedSlot(int index, string name, IEnumerable<string> lines)
    { _slots[index].Name = name; _slots[index].Lines = lines.ToList(); }
    public void SeedScalar(string path, string jsonValue) => _scalars[path] = jsonValue;

    static readonly Regex DReadRx = new(@"^dread (\S+):\{""index"":(-?\d+),""chunk"":(-?\d+)\}$");
    static readonly Regex DWriteRx = new(@"^dwrite (\S+):\{""index"":(-?\d+),""chunk"":(-?\d+),""value"":""([0-9a-fA-F]*)""\}$");
    static readonly Regex SaveRx = new(@"^write root\\app\\preset:\{""value"":""([^""]*)"",""save"":""save""\}$");
    static readonly Regex SelectRx = new(@"^write root\\app\\preset:\{""value"":""([^""]*)""\}$");
    static readonly Regex WriteRx = new(@"^write (root\\app\\\S+):(\{.*\})$");
    static readonly Regex ReadRx = new(@"^read (\S+)$");

    static byte[] FromHex(string h) { var b = new byte[h.Length / 2]; for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16); return b; }
    static byte[] SlotBytes(Slot s) { var doc = PresetDocumentFrom(s.Lines); return doc; }
    static byte[] PresetDocumentFrom(List<string> lines)
    {
        var text = string.Join("\r\n", lines);
        var bytes = new byte[8192];
        Encoding.ASCII.GetBytes(text).CopyTo(bytes, 0);
        return bytes;
    }

    public Task<string> SendAsync(string command, CancellationToken ct = default)
    {
        if (!IsOpen) throw new InvalidOperationException("not open");
        Match m;

        if ((m = DWriteRx.Match(command)).Success)
        {
            int idx = int.Parse(m.Groups[2].Value), chunk = int.Parse(m.Groups[3].Value);
            if (m.Groups[1].Value == @"root\presets" && chunk == -1)
            {
                var raw = FromHex(m.Groups[4].Value);
                var name = Encoding.ASCII.GetString(raw).TrimEnd('\0');
                _slots[idx].Name = name;
                if (name.Length == 0) _slots[idx].Lines = new();   // empty name = delete
            }
            // content chunks (>=1) to presets: ignored (matches firmware)
            return Task.FromResult("");
        }
        if ((m = DReadRx.Match(command)).Success)
        {
            int idx = int.Parse(m.Groups[2].Value), chunk = int.Parse(m.Groups[3].Value);
            var bytes = PresetDocumentFrom(_slots[idx].Lines);
            string hex = "";
            if (chunk >= 1) { var seg = bytes.Skip((chunk - 1) * 128).Take(128).ToArray(); hex = Convert.ToHexStringLower(seg); }
            return Task.FromResult($"{m.Groups[1].Value}:{{\"index\":{idx},\"chunk\":{chunk},\"value\":\"{hex}\"}}\r\n");
        }
        if ((m = SaveRx.Match(command)).Success)
        {
            var name = m.Groups[1].Value;
            var slot = _slots.FirstOrDefault(s => s.Name == name);
            if (slot != null) slot.Lines = new List<string>(_live);
            return Task.FromResult("");
        }
        if ((m = SelectRx.Match(command)).Success)
        {
            var slot = _slots.FirstOrDefault(s => s.Name == m.Groups[1].Value);
            _live.Clear(); if (slot != null) _live.AddRange(slot.Lines);
            return Task.FromResult("");
        }
        if ((m = WriteRx.Match(command)).Success)
        {
            var path = m.Groups[1].Value; var line = $"{path}:{m.Groups[2].Value}";
            int i = _live.FindIndex(l => l.StartsWith(path + ":", StringComparison.Ordinal));
            if (i >= 0) _live[i] = line; else _live.Add(line);
            return Task.FromResult("");
        }
        if ((m = ReadRx.Match(command)).Success)
        {
            var path = m.Groups[1].Value;
            if (path == @"root\presets")
            {
                var arr = string.Join(",", _slots.Select(s => "\"" + s.Name + "\""));
                return Task.FromResult($"root\\presets:{{\"value\":[{arr}]}}\r\n");
            }
            if (path == @"root\app\preset")
            {
                // current live name = name of the slot matching live content, else ""
                var match = _slots.FirstOrDefault(s => s.Lines.SequenceEqual(_live));
                return Task.FromResult($"root\\app\\preset:{{\"value\":\"{match?.Name ?? ""}\"}}\r\n");
            }
            if (_scalars.TryGetValue(path, out var v)) return Task.FromResult($"{path}:{{\"value\":{v}}}\r\n");
            return Task.FromResult("");
        }
        return Task.FromResult("");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FakePresetDeviceTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/Sonulab.Core.Tests/FakePresetDevice.cs tests/Sonulab.Core.Tests/FakePresetDeviceTests.cs
git commit -m "test(core): faithful FakePresetDevice (save-by-name, content-dwrite-ignored)"
```

---

### Task 2: PresetSlot + DeviceRepository.ListPresetsAsync

**Files:** Create `src/Sonulab.Core/Model/PresetSlot.cs`, `src/Sonulab.Core/Services/DeviceRepository.cs`; Test `tests/Sonulab.Core.Tests/DeviceRepositoryTests.cs`.

- [ ] **Step 1: Write the failing test**

`tests/Sonulab.Core.Tests/DeviceRepositoryTests.cs`:
```csharp
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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DeviceRepositoryTests`
Expected: FAIL — `PresetSlot`/`DeviceRepository` do not exist.

- [ ] **Step 3: Implement PresetSlot and DeviceRepository (list only)**

`src/Sonulab.Core/Model/PresetSlot.cs`:
```csharp
namespace Sonulab.Core.Model;

public sealed record PresetSlot(int Index, string Name)
{
    public bool IsEmpty => string.IsNullOrEmpty(Name);
}
```

`src/Sonulab.Core/Services/DeviceRepository.cs`:
```csharp
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
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DeviceRepositoryTests`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): PresetSlot + DeviceRepository.ListPresetsAsync"
```

---

### Task 3: DeviceRepository select / save / rename / delete

**Files:** Modify `src/Sonulab.Core/Services/DeviceRepository.cs`; add tests to `tests/Sonulab.Core.Tests/DeviceRepositoryTests.cs`.

- [ ] **Step 1: Write the failing tests** (add to the existing `DeviceRepositoryTests` class)
```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter DeviceRepositoryTests`
Expected: FAIL — `RenameAsync`/`DeleteAsync`/`SelectPresetAsync`/`SaveCurrentAsAsync`/`ReadPresetAsync` not defined.

- [ ] **Step 3: Implement the methods** (add to `DeviceRepository`)
```csharp
    public Task SelectPresetAsync(string name, CancellationToken ct = default) =>
        _client.WriteAsync(PresetNode, "\"" + name + "\"", ct);

    public Task SaveCurrentAsAsync(string name, CancellationToken ct = default) =>
        _client.SaveAsync(PresetNode, name, ct);

    public Task RenameAsync(int index, string name, CancellationToken ct = default) =>
        _client.DWriteChunkAsync(PresetsList, index, -1, NamePad(name), ct);

    public Task DeleteAsync(int index, CancellationToken ct = default) =>
        _client.DWriteChunkAsync(PresetsList, index, -1, new byte[128], ct);

    private static byte[] NamePad(string name)
    {
        var buf = new byte[128];
        var b = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(b, buf, Math.Min(b.Length, 128));
        return buf;
    }
```
(`ReadPresetAsync` is added in Task 4 — the `Select_then_SaveCurrentAs` test depends on it, so implement Task 4 Step 3 before running this task's tests, as noted there.)

- [ ] **Step 4: Run tests to verify they pass** (after Task 4 Step 3 is in place)

Run: `dotnet test --filter DeviceRepositoryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): DeviceRepository select/save/rename/delete"
```

---

### Task 4: DeviceRepository.ReadPresetAsync

**Files:** Modify `src/Sonulab.Core/Services/DeviceRepository.cs`; Test in `tests/Sonulab.Core.Tests/DeviceRepositoryTests.cs`.

- [ ] **Step 1: Write the failing test** (add to `DeviceRepositoryTests`)
```csharp
    [Fact] public async Task ReadPresetAsync_returns_slot_content_document()
    {
        var (repo, _) = await Repo();
        var doc = await repo.ReadPresetAsync(0);
        Assert.Equal("\"AmpA\"", doc.GetValueJson(@"root\app\amp\amp"));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DeviceRepositoryTests`
Expected: FAIL — `ReadPresetAsync` not defined.

- [ ] **Step 3: Implement ReadPresetAsync** (add to `DeviceRepository`)
```csharp
    public async Task<PresetDocument> ReadPresetAsync(int index, CancellationToken ct = default)
    {
        var bytes = await _client.DReadBlobAsync(PresetsList, index, PresetChunks, ct);
        return PresetDocument.Parse(bytes);
    }
```
Add `using Sonulab.Core.Model;` is already present (PresetSlot). `PresetDocument` is in `Sonulab.Core.Model`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DeviceRepositoryTests`
Expected: PASS — all DeviceRepository tests (including the Task 3 ones) green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): DeviceRepository.ReadPresetAsync (dread -> PresetDocument)"
```

---

### Task 5: DeviceRepository.WritePresetToSlotAsync (the core primitive)

**Files:** Modify `src/Sonulab.Core/Services/DeviceRepository.cs`; Test `tests/Sonulab.Core.Tests/WritePresetToSlotTests.cs`.

This is the proven name → replay → save → verify primitive that Plan 3b's reorder builds on.

- [ ] **Step 1: Write the failing test**

`tests/Sonulab.Core.Tests/WritePresetToSlotTests.cs`:
```csharp
using Sonulab.Core;
using Sonulab.Core.Model;
using Sonulab.Core.Services;
using Xunit;

public class WritePresetToSlotTests
{
    [Fact] public async Task Writes_document_to_empty_slot_and_verifies()
    {
        var dev = new FakePresetDevice();
        dev.SeedSlot(0, "Source", new[] { @"root\app\amp\amp:{""value"":""AmpA""}", @"root\app\amp\vol:{""value"":50.0}" });
        await dev.OpenAsync();
        var repo = new DeviceRepository(new SonuClient(dev));

        var doc = await repo.ReadPresetAsync(0);                  // read Source's content
        await repo.WritePresetToSlotAsync(7, "Copy", doc);        // write it to empty slot 7 named "Copy"

        var slots = await repo.ListPresetsAsync();
        Assert.Equal("Copy", slots[7].Name);
        Assert.Equal(doc.ToBytes(), (await repo.ReadPresetAsync(7)).ToBytes());
    }

    [Fact] public async Task Verify_failure_throws()
    {
        // A device that drops the save (content never lands) must trip the read-back verify.
        var dev = new DropSaveDevice();
        dev.SeedSlot(0, "Source", new[] { @"root\app\amp\amp:{""value"":""AmpA""}" });
        await dev.OpenAsync();
        var repo = new DeviceRepository(new SonuClient(dev));
        var doc = await repo.ReadPresetAsync(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.WritePresetToSlotAsync(7, "Copy", doc));
    }

    // FakePresetDevice variant whose SaveRx handler does nothing (simulates a failed write).
    sealed class DropSaveDevice : FakePresetDevice
    {
        public override Task<string> SendAsync(string command, System.Threading.CancellationToken ct = default)
            => command.Contains("\"save\":\"save\"") ? Task.FromResult("") : base.SendAsync(command, ct);
    }
}
```
> For `DropSaveDevice` to compile, `FakePresetDevice.SendAsync` must be `public virtual`. Update its signature in `FakePresetDevice.cs` (Task 1) from `public Task<string> SendAsync(` to `public virtual Task<string> SendAsync(`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter WritePresetToSlotTests`
Expected: FAIL — `WritePresetToSlotAsync` not defined (and the `virtual` tweak needed).

- [ ] **Step 3: Implement WritePresetToSlotAsync** (add to `DeviceRepository`) and make `FakePresetDevice.SendAsync` virtual.
```csharp
    public async Task WritePresetToSlotAsync(int index, string name, PresetDocument doc, bool verify = true, CancellationToken ct = default)
    {
        // 1) name the target slot so save-by-name lands here
        await RenameAsync(index, name, ct);
        // 2) replay the document's app params into live state
        foreach (var line in doc.Lines)
        {
            if (!NodeRecord.TryParse(line, out var rec)) continue;
            if (!rec.Path.StartsWith(@"root\app", StringComparison.Ordinal)) continue;
            if (!rec.Json.TryGetProperty("value", out var v)) continue;
            await _client.WriteAsync(rec.Path, v.GetRawText(), ct);
        }
        // 3) save live state into the slot named `name`
        await SaveCurrentAsAsync(name, ct);
        // 4) verify by reading the slot back
        if (verify)
        {
            var back = await ReadPresetAsync(index, ct);
            if (!back.ToBytes().AsSpan().SequenceEqual(doc.ToBytes()))
                throw new InvalidOperationException($"Write-back verify failed for slot {index} ('{name}').");
        }
    }
```
Add `using Sonulab.Core.Model;` (already present). `NodeRecord` is in `Sonulab.Core.Model`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter WritePresetToSlotTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): DeviceRepository.WritePresetToSlotAsync (name+replay+save+verify)"
```

---

### Task 6: DeviceRepository.DuplicateAsync

**Files:** Modify `src/Sonulab.Core/Services/DeviceRepository.cs`; Test `tests/Sonulab.Core.Tests/DuplicateTests.cs`.

- [ ] **Step 1: Write the failing test**

`tests/Sonulab.Core.Tests/DuplicateTests.cs`:
```csharp
using Sonulab.Core;
using Sonulab.Core.Services;
using Xunit;

public class DuplicateTests
{
    [Fact] public async Task Duplicate_copies_source_content_to_dest_with_new_name()
    {
        var dev = new FakePresetDevice();
        dev.SeedSlot(2, "Original", new[] { @"root\app\amp\amp:{""value"":""AmpX""}", @"root\app\amp\vol:{""value"":42.0}" });
        await dev.OpenAsync();
        var repo = new DeviceRepository(new SonuClient(dev));

        await repo.DuplicateAsync(sourceIndex: 2, destIndex: 11, newName: "Original copy");

        var slots = await repo.ListPresetsAsync();
        Assert.Equal("Original copy", slots[11].Name);
        var src = await repo.ReadPresetAsync(2);
        var dst = await repo.ReadPresetAsync(11);
        Assert.Equal(src.ToBytes(), dst.ToBytes());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter DuplicateTests`
Expected: FAIL — `DuplicateAsync` not defined.

- [ ] **Step 3: Implement DuplicateAsync** (add to `DeviceRepository`)
```csharp
    public async Task DuplicateAsync(int sourceIndex, int destIndex, string newName, CancellationToken ct = default)
    {
        var doc = await ReadPresetAsync(sourceIndex, ct);
        await WritePresetToSlotAsync(destIndex, newName, doc, verify: true, ct);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter DuplicateTests`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): DeviceRepository.DuplicateAsync"
```

---

### Task 7: BackupService — snapshot all / restore slot

**Files:** Create `src/Sonulab.Core/Services/BackupService.cs`; Test `tests/Sonulab.Core.Tests/BackupServiceTests.cs`.

- [ ] **Step 1: Write the failing tests**

`tests/Sonulab.Core.Tests/BackupServiceTests.cs`:
```csharp
using Sonulab.Core;
using Sonulab.Core.Services;
using Xunit;

public class BackupServiceTests
{
    [Fact] public async Task SnapshotAll_writes_one_pst_per_nonempty_slot()
    {
        var dev = new FakePresetDevice();
        dev.SeedSlot(0, "Alpha", new[] { @"root\app\amp\amp:{""value"":""AmpA""}" });
        dev.SeedSlot(3, "Bravo", new[] { @"root\app\amp\amp:{""value"":""AmpB""}" });
        await dev.OpenAsync();
        var repo = new DeviceRepository(new SonuClient(dev));
        var backup = new BackupService(repo);

        var dir = Path.Combine(Path.GetTempPath(), "sonulab-bk-" + Guid.NewGuid().ToString("N"));
        int n = await backup.SnapshotAllAsync(dir);

        Assert.Equal(2, n);
        Assert.Equal(2, Directory.GetFiles(dir, "*.pst").Length);
        Assert.True(File.Exists(Path.Combine(dir, "00 - Alpha.pst")));
        Assert.Equal(8192, new FileInfo(Path.Combine(dir, "00 - Alpha.pst")).Length);
        Directory.Delete(dir, true);
    }

    [Fact] public async Task RestoreSlot_writes_pst_back_to_device()
    {
        var dev = new FakePresetDevice();
        dev.SeedSlot(0, "Alpha", new[] { @"root\app\amp\amp:{""value"":""AmpA""}" });
        await dev.OpenAsync();
        var repo = new DeviceRepository(new SonuClient(dev));
        var backup = new BackupService(repo);

        var dir = Path.Combine(Path.GetTempPath(), "sonulab-rs-" + Guid.NewGuid().ToString("N"));
        await backup.SnapshotAllAsync(dir);
        var pst = Path.Combine(dir, "00 - Alpha.pst");

        await repo.DeleteAsync(0);
        Assert.True((await repo.ListPresetsAsync())[0].IsEmpty);

        await backup.RestoreSlotAsync(0, pst);
        var restored = await repo.ReadPresetAsync(0);
        Assert.Equal("\"AmpA\"", restored.GetValueJson(@"root\app\amp\amp"));
        Directory.Delete(dir, true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter BackupServiceTests`
Expected: FAIL — `BackupService` does not exist.

- [ ] **Step 3: Implement BackupService**

`src/Sonulab.Core/Services/BackupService.cs`:
```csharp
using Sonulab.Core.Model;

namespace Sonulab.Core.Services;

public sealed class BackupService
{
    private readonly DeviceRepository _repo;
    public BackupService(DeviceRepository repo) => _repo = repo;

    public async Task<int> SnapshotAllAsync(string folder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        int count = 0;
        foreach (var slot in await _repo.ListPresetsAsync(ct))
        {
            if (slot.IsEmpty) continue;
            var doc = await _repo.ReadPresetAsync(slot.Index, ct);
            var file = Path.Combine(folder, $"{slot.Index:D2} - {Sanitize(slot.Name)}.pst");
            await File.WriteAllBytesAsync(file, doc.ToBytes(), ct);
            count++;
        }
        return count;
    }

    public async Task RestoreSlotAsync(int index, string pstPath, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(pstPath, ct);
        var doc = PresetDocument.Parse(bytes);
        // recover the display name from the file name "NN - Name.pst", fall back to the slot's name
        var stem = Path.GetFileNameWithoutExtension(pstPath);
        int dash = stem.IndexOf(" - ", StringComparison.Ordinal);
        var name = dash >= 0 ? stem[(dash + 3)..] : stem;
        await _repo.WritePresetToSlotAsync(index, name, doc, verify: true, ct);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter BackupServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): BackupService snapshot-all / restore-slot (.pst)"
```

---

### Task 8: FirmwareCatalog — load tested-firmware list from JSON

**Files:** Create `src/Sonulab.Core/Connection/FirmwareCatalog.cs`, `src/Sonulab.Core/Connection/compatibility.json`; Modify `src/Sonulab.Core/Sonulab.Core.csproj`; Test `tests/Sonulab.Core.Tests/FirmwareCatalogTests.cs`.

Removes the hard-coded tested-firmware list (currently passed in the Plan 2 harness) into a data file.

- [ ] **Step 1: Write the failing tests**

`tests/Sonulab.Core.Tests/FirmwareCatalogTests.cs`:
```csharp
using Sonulab.Core.Connection;
using Xunit;

public class FirmwareCatalogTests
{
    [Fact] public void Load_parses_entries()
    {
        var json = "[{\"license\":\"stompstation1\",\"arch\":\"ESP32S3\",\"version\":\"2.5.1\"}]";
        var list = FirmwareCatalog.Load(json);
        var fw = Assert.Single(list);
        Assert.Equal("stompstation1", fw.License);
        Assert.Equal("ESP32S3", fw.Arch);
        Assert.Equal("2.5.1", fw.Version);
    }

    [Fact] public void Default_includes_the_known_tested_firmware()
    {
        Assert.Contains(FirmwareCatalog.Default,
            f => f.License == "stompstation1" && f.Arch == "ESP32S3" && f.Version == "2.5.1");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FirmwareCatalogTests`
Expected: FAIL — `FirmwareCatalog` does not exist.

- [ ] **Step 3: Create the resource and embed it**

`src/Sonulab.Core/Connection/compatibility.json`:
```json
[
  { "license": "stompstation1", "arch": "ESP32S3", "version": "2.5.1" }
]
```

In `src/Sonulab.Core/Sonulab.Core.csproj`, add inside a `<ItemGroup>`:
```xml
  <ItemGroup>
    <EmbeddedResource Include="Connection/compatibility.json" />
  </ItemGroup>
```

- [ ] **Step 4: Implement FirmwareCatalog**

`src/Sonulab.Core/Connection/FirmwareCatalog.cs`:
```csharp
using System.Reflection;
using System.Text.Json;

namespace Sonulab.Core.Connection;

public static class FirmwareCatalog
{
    private sealed record Entry(string license, string arch, string version);

    public static IReadOnlyList<TestedFirmware> Load(string json)
    {
        var entries = JsonSerializer.Deserialize<List<Entry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        return entries.Select(e => new TestedFirmware(e.license, e.arch, e.version)).ToList();
    }

    private static readonly Lazy<IReadOnlyList<TestedFirmware>> _default = new(() =>
    {
        var asm = typeof(FirmwareCatalog).Assembly;
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("compatibility.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return Load(r.ReadToEnd());
    });

    public static IReadOnlyList<TestedFirmware> Default => _default.Value;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FirmwareCatalogTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS — all Plan 1/2/3a tests green.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(core): FirmwareCatalog (embedded compatibility.json + loader)"
```

---

### Task 9: Hardware validation (guarded writes on throwaway slots)

**Files:** Create `docs/HARDWARE-VALIDATION-plan3a.md`; extend `tools/HwCheck/Program.cs` with a `--write-test` mode. This is the integration gate for the write primitives. **Operator runs it with VoidX closed; it writes ONLY to empty throwaway slots and restores them.**

- [ ] **Step 1: Add a guarded write-test mode to the harness**

In `tools/HwCheck/Program.cs`, after the read-only section, add a block that runs only when `args` contains `--write-test`:
```csharp
if (Array.IndexOf(args, "--write-test") >= 0)
{
    Console.WriteLine("\n--- GUARDED WRITE TEST (empty slots only) ---");
    var dev2 = new DeviceSession(new SonuConnector(() => new SystemSerialPort(),
        new SerialLinkOptions { OpenSettleMs = 1500, ProbeAttempts = 3 }),
        new CompatibilityChecker(FirmwareCatalog.Default));
    var st = await dev2.ConnectAsync(new[] { "COM6" }, new[] { 115200 });
    if (!st.Connected || !st.Compatibility!.WritesAllowed) { Console.WriteLine("writes not allowed; abort"); return 3; }
    var repo = new Sonulab.Core.Services.DeviceRepository(dev2.Client!);

    var slots = await repo.ListPresetsAsync();
    int empty = slots.First(s => s.IsEmpty).Index;
    int source = slots.First(s => !s.IsEmpty).Index;
    Console.WriteLine($"duplicating slot {source} ('{slots[source].Name}') -> empty slot {empty} as 'HW Test'...");
    await repo.DuplicateAsync(source, empty, "HW Test");
    var check = await repo.ListPresetsAsync();
    Console.WriteLine(check[empty].Name == "HW Test" ? $"  OK: slot {empty} now 'HW Test'" : "  FAIL");
    var srcDoc = await repo.ReadPresetAsync(source);
    var dupDoc = await repo.ReadPresetAsync(empty);
    Console.WriteLine(srcDoc.ToBytes().AsSpan().SequenceEqual(dupDoc.ToBytes()) ? "  OK: content matches source" : "  FAIL: content differs");
    await repo.DeleteAsync(empty);
    Console.WriteLine($"  cleaned up slot {empty} (deleted). DONE.");
    dev2.Disconnect();
}
```
Add `using Sonulab.Core.Connection;` if not present.

- [ ] **Step 2: Build (do not auto-run the write test)**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 3: Write the checklist doc**

Create `docs/HARDWARE-VALIDATION-plan3a.md`:
```markdown
# Plan 3a — Manual Hardware Validation (guarded writes)

VoidX-Control CLOSED. The harness writes ONLY to an empty slot and deletes it afterward.

Run: `dotnet run --project tools/HwCheck -- --write-test`

Expect:
1. Connect + Compatibility=Tested, writesAllowed=true.
2. "duplicating slot S -> empty slot E as 'HW Test'": slot E becomes 'HW Test'.
3. "content matches source": the duplicated blob equals the source blob (read-back).
4. "cleaned up slot E (deleted)": slot E returns to empty.

Record pass/fail. If content mismatch or the slot isn't created, capture output and STOP before Plan 3b.
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test/docs: Plan 3a guarded hardware write-test harness + checklist"
```

- [ ] **Step 5: (Operator) run `dotnet run --project tools/HwCheck -- --write-test`** with VoidX closed; confirm duplicate + content-match + cleanup, and record results in the doc.

---

## Self-review notes
- **Spec coverage (design spec §5–6):** list → Task 2; select/save/rename/delete → Task 3; read one preset → Task 4; write-to-slot primitive → Task 5; duplicate → Task 6; backup/restore → Task 7; firmware catalog (replaces hard-coded list) → Task 8. **Reorder is intentionally deferred to Plan 3b** (built on Task 5's primitive). The faithful fake (Task 1) enables real TDD of save-by-name + content-not-dwrite-able.
- **Placeholder scan:** none — all code is complete. The one cross-task test dependency (Task 3's `Select_then_SaveCurrentAs` needs Task 4's `ReadPresetAsync`) is called out in Task 3 Step 3.
- **Type consistency:** signatures match the "Public API" block. `WritePresetToSlotAsync(index,name,doc,verify,ct)` is reused by `DuplicateAsync` (Task 6) and `BackupService.RestoreSlotAsync` (Task 7). `FakePresetDevice.SendAsync` is `public virtual` (set in Task 1) so Task 5's `DropSaveDevice` can override it. Repository constants `SlotCount`/`PresetChunks` used in list + read.
- **Safety:** `WritePresetToSlotAsync` verifies by read-back and throws on mismatch; the hardware test only touches empty slots and restores them.
```
