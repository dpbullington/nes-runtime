using NesRuntime.Emulation;
using NesRuntime.Sdk.Game;
using NesRuntime.Sdk.Input;

// MAUI's implicit usings pull in Microsoft.Maui.Controls.Button; disambiguate to the SDK's button enum.
using Button = NesRuntime.Sdk.Input.Button;

namespace NesRuntime.Maui.Games;

/// <summary>
/// Runs a real iNES/NES 2.0 cartridge on top of the engine. It drives a <see cref="NesMachine"/>
/// (6502 CPU + 2C02 PPU + mapper) one frame per engine frame, feeds it the engine's controllers, and
/// presents the emulated PPU output directly through the framebuffer hook.
/// </summary>
/// <remarks>
/// Set <see cref="RomImage"/> before launch to run a cartridge you own; otherwise a small built-in
/// test ROM (a boxed-tile pattern) runs so the pipeline is visible without any external file. This
/// is a functional NROM/frame-accurate core, not full Mesen parity — no audio yet, and mid-frame
/// effects (sprite-0 splits) are not reproduced.
/// </remarks>
public sealed class EmulatorGame : NesGame
{
    /// <summary>The ROM to run. Assign a real iNES/NES 2.0 image before launch, or leave null for the
    /// built-in test ROM.</summary>
    public static byte[]? RomImage;

    /// <summary>The ROM file's name without extension (e.g. "Zelda"), used to key the battery-save
    /// folder. Assign alongside <see cref="RomImage"/>.</summary>
    public static string? RomName;

    /// <summary>Human-readable outcome of the last battery restore/save, or save-state/rewind action —
    /// surfaced by the host so a silent path/permission failure is visible.</summary>
    public static string BatteryStatus = "";

    /// <summary>Set by the host while the Rewind control is held; the game then plays recent frames backward.</summary>
    public static bool RewindHeld;

    /// <summary>Number of manual save-state slots the host exposes.</summary>
    public const int SlotCount = 8;

    // The one running emulator instance, so the host can flush its battery on stop/quit.
    private static EmulatorGame? _current;

    // How often (in frames, ~60/s) to check the battery RAM for changes and flush it to disk.
    private const int AutosaveIntervalFrames = 60;

    // Rewind history: a per-frame ring of machine states (+ the input applied that frame). At ~22 KB
    // per state this is roughly 30 MB for 20 s, which is fine on desktop.
    private const int RewindCapacityFrames = 1200;
    private readonly RewindBuffer _rewind = new(RewindCapacityFrames);
    private NesButton _in0, _in1;

    private NesMachine _machine = null!;
    private string? _batteryPath;
    private byte[] _lastBattery = [];
    private int _framesSinceSave;

    public override GameInfo Info => new() { Title = "NES Emulator", Author = "NesRuntime", MaxPlayers = 2 };

    protected override void Load()
    {
        byte[] image = RomImage ?? BuiltInTestRom();
        _machine = new NesMachine(INesRom.Parse(image));
        _current = this;
        _batteryPath = null;
        _lastBattery = [];
        _rewind.Clear();
        RewindHeld = false;

        // Only battery-backed cartridges persist their PRG-RAM; restore any prior save now.
        if (!_machine.HasBattery)
        {
            BatteryStatus = "cartridge is not battery-backed (iNES header bit clear) — no save";
            return;
        }
        if (string.IsNullOrEmpty(RomName))
        {
            BatteryStatus = "no ROM name set — cannot locate save folder";
            return;
        }

        _batteryPath = ResolveSavePath("battery.bin");
        TryLoadBattery();
    }

    protected override void Update()
    {
        _in0 = ReadPad(0);
        _in1 = ReadPad(1);
        _machine.SetButtons(0, _in0);
        _machine.SetButtons(1, _in1);

        // Autosave on change so an app force-quit loses at most ~1s of battery RAM.
        if (_batteryPath is not null && ++_framesSinceSave >= AutosaveIntervalFrames)
        {
            _framesSinceSave = 0;
            SaveBattery();
        }
    }

    /// <summary>Persists the running cartridge's battery RAM if it changed. Called by the host on stop.</summary>
    public static void FlushCurrent() => _current?.SaveBattery();

    protected override void Unload() => SaveBattery();

    protected override void Render() { }

    protected override bool RenderFramebuffer(Span<byte> framebuffer)
    {
        if (RewindHeld && _rewind.TryPopBack(out byte[] state, out NesButton in0, out NesButton in1))
        {
            // Play backward: restore an older state, replay its input, and re-render that frame.
            _machine.LoadState(state);
            _machine.SetButtons(0, in0);
            _machine.SetButtons(1, in1);
            _machine.StepFrame(framebuffer);
        }
        else
        {
            // Forward: snapshot the pre-step state (+ this frame's input) so it can be rewound later.
            _rewind.Push(_machine.SaveState(), _in0, _in1);
            _machine.StepFrame(framebuffer); // steps the whole frame: video + APU audio
        }
        ReportEmphasis((NesRuntime.Core.Ppu.ColorEmphasis)_machine.Emphasis); // PPUMASK R/G/B tint for the host
        return true;
    }

    protected override bool RenderAudio(Span<float> buffer)
    {
        _machine.CopyAudio(buffer); // this frame's APU samples (already generated by StepFrame)
        return true;
    }

    private NesButton ReadPad(int player)
    {
        GamePad pad = Input.GetPad(player);
        NesButton b = NesButton.None;
        if (pad.IsDown(Button.A)) b |= NesButton.A;
        if (pad.IsDown(Button.B)) b |= NesButton.B;
        if (pad.IsDown(Button.Select)) b |= NesButton.Select;
        if (pad.IsDown(Button.Start)) b |= NesButton.Start;
        if (pad.IsDown(Button.Up)) b |= NesButton.Up;
        if (pad.IsDown(Button.Down)) b |= NesButton.Down;
        if (pad.IsDown(Button.Left)) b |= NesButton.Left;
        if (pad.IsDown(Button.Right)) b |= NesButton.Right;
        return b;
    }

    // --- Battery-backed SRAM persistence ---

    private void TryLoadBattery()
    {
        if (_batteryPath is null)
        {
            BatteryStatus = "no writable profile directory found for saves";
            return;
        }

        try
        {
            if (File.Exists(_batteryPath))
            {
                byte[] data = File.ReadAllBytes(_batteryPath);
                _machine.LoadBattery(data);
                BatteryStatus = $"restored {data.Length} B — {_batteryPath}";
            }
            else
            {
                BatteryStatus = $"no save yet — {_batteryPath}";
            }

            _lastBattery = _machine.BatteryRam.ToArray(); // baseline so autosave only writes real changes
        }
        catch (Exception ex)
        {
            // A missing/locked/corrupt save must never crash the game — just start from blank SRAM.
            BatteryStatus = $"load error: {ex.Message} — {_batteryPath}";
        }
    }

    private void SaveBattery()
    {
        if (_batteryPath is null || !_machine.HasBattery)
            return;

        ReadOnlySpan<byte> ram = _machine.BatteryRam;
        if (ram.SequenceEqual(_lastBattery))
            return; // unchanged since the last flush — skip the write

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_batteryPath)!);
            byte[] snapshot = ram.ToArray();
            File.WriteAllBytes(_batteryPath, snapshot);
            _lastBattery = snapshot;
            BatteryStatus = $"saved {snapshot.Length} B — {_batteryPath}";
        }
        catch (Exception ex)
        {
            // Out of space / permission denied must not crash the game; retry on the next autosave.
            BatteryStatus = $"save error: {ex.Message} — {_batteryPath}";
        }
    }

    // ~/.nes-runtime/save/<rom-name>/<fileName> (battery.bin or state0.bin). Tries the user profile home
    // first (honoring "~"); if that isn't writable — e.g. the App Sandbox denies the real home — it
    // falls back to the app's guaranteed-writable data container, keeping the same folder shape. Returns
    // the first base whose directory it can actually create (the create doubles as a writability probe).
    private static string? ResolveSavePath(string fileName)
    {
        if (string.IsNullOrEmpty(RomName))
            return null;

        string safe = string.Concat(RomName!.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "rom";

        foreach (string? baseDir in SaveBaseCandidates())
        {
            if (string.IsNullOrEmpty(baseDir))
                continue;
            try
            {
                string dir = Path.Combine(baseDir, ".nes-runtime", "save", safe);
                Directory.CreateDirectory(dir); // also probes that this base is writable
                return Path.Combine(dir, fileName);
            }
            catch
            {
                // Not writable (sandbox/permissions) — try the next candidate.
            }
        }
        return null;
    }

    // --- Save states (manual, multi-slot) ---

    /// <summary>Metadata for one save-state slot, for the host's slot picker.</summary>
    public readonly record struct SlotInfo(int Index, bool Exists, DateTime Time);

    private static string SlotFile(int slot) => $"state{slot}.bin";

    /// <summary>Lists all slots for the current ROM with their on-disk timestamps (read-only; safe any time).</summary>
    public static SlotInfo[] GetSlots()
    {
        var slots = new SlotInfo[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            string? path = ResolveSavePath(SlotFile(i));
            bool exists = path is not null && File.Exists(path);
            slots[i] = new SlotInfo(i, exists, exists ? File.GetLastWriteTime(path!) : default);
        }
        return slots;
    }

    // These run on the UI thread between frames (the whole loop is single-threaded), so touching the
    // machine directly is safe — no need to defer to Update.
    public static void SaveToSlot(int slot) => _current?.SaveStateToSlot(slot);
    public static void LoadFromSlot(int slot) => _current?.LoadStateFromSlot(slot);

    /// <summary>Deletes a slot file (pure file I/O — no running game required).</summary>
    public static void DeleteSlot(int slot)
    {
        string? path = ResolveSavePath(SlotFile(slot));
        try
        {
            if (path is not null && File.Exists(path)) { File.Delete(path); BatteryStatus = $"💾 deleted slot {slot + 1}"; }
            else BatteryStatus = $"💾 slot {slot + 1} is already empty";
        }
        catch (Exception ex) { BatteryStatus = $"💾 delete error: {ex.Message}"; }
    }

    private void SaveStateToSlot(int slot)
    {
        string? path = ResolveSavePath(SlotFile(slot));
        if (path is null) { BatteryStatus = "💾 no writable location for save state"; return; }
        try
        {
            File.WriteAllBytes(path, _machine.SaveState());
            BatteryStatus = $"💾 saved slot {slot + 1}";
        }
        catch (Exception ex) { BatteryStatus = $"💾 save-state error: {ex.Message}"; }
    }

    private void LoadStateFromSlot(int slot)
    {
        string? path = ResolveSavePath(SlotFile(slot));
        if (path is null || !File.Exists(path)) { BatteryStatus = $"💾 slot {slot + 1} is empty"; return; }
        try
        {
            _machine.LoadState(File.ReadAllBytes(path));
            _rewind.Clear(); // old rewind history no longer matches the restored timeline
            BatteryStatus = $"💾 loaded slot {slot + 1}";
        }
        catch (Exception ex) { BatteryStatus = $"💾 load-state error: {ex.Message}"; }
    }

    private static IEnumerable<string?> SaveBaseCandidates()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetEnvironmentVariable("HOME");
        yield return FileSystem.AppDataDirectory;                                 // sandbox-safe container
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Personal); // Documents
    }

    // A minimal NROM ROM: reset code writes a palette, fills the top tile rows with a boxed tile, and
    // enables background rendering. Proves the emulator runs without needing an external cartridge.
    private static byte[] BuiltInTestRom()
    {
        var prg = new byte[0x4000];
        byte[] code =
        [
            0x78, 0xD8,                             // SEI ; CLD
            0xA9, 0x3F, 0x8D, 0x06, 0x20,           // PPUADDR = $3F00
            0xA9, 0x00, 0x8D, 0x06, 0x20,
            0xA9, 0x0F, 0x8D, 0x07, 0x20,           // palette 0: backdrop / red / orange / green
            0xA9, 0x16, 0x8D, 0x07, 0x20,
            0xA9, 0x27, 0x8D, 0x07, 0x20,
            0xA9, 0x2A, 0x8D, 0x07, 0x20,
            0xA9, 0x20, 0x8D, 0x06, 0x20,           // PPUADDR = $2000
            0xA9, 0x00, 0x8D, 0x06, 0x20,
            0xA2, 0x00,                             // LDX #0
            0xA9, 0x01, 0x8D, 0x07, 0x20,           // L: tile 1 -> nametable
            0xE8, 0xD0, 0xF8,                       //    INX ; BNE L  (256 tiles)
            0xA9, 0x80, 0x8D, 0x00, 0x20,           // PPUCTRL = NMI on
            0xA9, 0x0A, 0x8D, 0x01, 0x20,           // PPUMASK = show bg
            0x4C, 0x3E, 0x80,                       // JMP $803E (idle)
            0x40,                                   // NMI: RTI
        ];
        Array.Copy(code, prg, code.Length);
        prg[0x3FFA] = 0x41; prg[0x3FFB] = 0x80;     // NMI vector
        prg[0x3FFC] = 0x00; prg[0x3FFD] = 0x80;     // RESET vector
        prg[0x3FFE] = 0x41; prg[0x3FFF] = 0x80;     // IRQ vector

        var chr = new byte[0x2000];
        byte[] plane0 = [0xFF, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0xFF];
        byte[] plane1 = [0x00, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x7E, 0x00];
        for (int i = 0; i < 8; i++) { chr[16 + i] = plane0[i]; chr[24 + i] = plane1[i]; }

        var img = new byte[16 + prg.Length + chr.Length];
        img[0] = (byte)'N'; img[1] = (byte)'E'; img[2] = (byte)'S'; img[3] = 0x1A;
        img[4] = 1; img[5] = 1;
        Array.Copy(prg, 0, img, 16, prg.Length);
        Array.Copy(chr, 0, img, 16 + prg.Length, chr.Length);
        return img;
    }

    /// <summary>
    /// A fixed-capacity ring of recent machine save-states (+ the controller input applied that
    /// frame). Pushing past capacity overwrites the oldest frame; popping the back yields the most
    /// recent frame, which is how rewind walks the timeline backward.
    /// </summary>
    private sealed class RewindBuffer(int capacity)
    {
        private readonly byte[]?[] _state = new byte[capacity][];
        private readonly NesButton[] _in0 = new NesButton[capacity];
        private readonly NesButton[] _in1 = new NesButton[capacity];
        private int _head, _count;

        public bool TryPopBack(out byte[] state, out NesButton in0, out NesButton in1)
        {
            if (_count == 0) { state = []; in0 = in1 = NesButton.None; return false; }
            _count--;
            int i = (_head + _count) % _state.Length;
            state = _state[i]!;
            in0 = _in0[i];
            in1 = _in1[i];
            return true;
        }

        public void Push(byte[] state, NesButton in0, NesButton in1)
        {
            int i = (_head + _count) % _state.Length;
            if (_count < _state.Length) _count++;
            else _head = (_head + 1) % _state.Length; // full: drop the oldest
            _state[i] = state;
            _in0[i] = in0;
            _in1[i] = in1;
        }

        public void Clear() { _head = 0; _count = 0; }
    }
}
