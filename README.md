# NesRuntime

Write Nintendo Entertainment System games in **C#** and run them against a modern GUI
(MAUI or any other host) — without ever touching 6502 assembly or hardware registers.

NesRuntime is a **runtime**, not an emulator. You don't load a pre-built `.nes` ROM and
interpret its opcodes; you write your game logic in C# against a friendly SDK, and the runtime
maps it onto a hardware-faithful model of the NES. Everything you author is still bounded by the
**real limits of the console** — 64 sprites, 8 per scanline, four background + four sprite
palettes of three colors each drawn from the fixed 64-color system palette, a 256×240 tile grid,
and exactly five audio channels — so what you build looks and sounds authentically like an NES
game.

> Status: **API skeleton with working render + audio slices.** The full surface is present and
> documented. Two vertical slices are implemented end-to-end: (1) the PPU compositor (background
> tiles, palettes, 64 sprites with flipping/priority and the 8-per-scanline limit) with the real
> 64-color master palette, and (2) a software audio synthesizer (pulse/triangle/noise + one-shot
> sound effects) pumped through the frame loop to the host and played on Mac Catalyst. So a game
> renders real frames **and makes sound** on the MAUI host. The register-accurate APU/PPU,
> cartridge/mapper, and CPU-bus paths remain stubs.

## Solution layout

```
NesRuntime.slnx
 src/
   NesRuntime.Core/        Hardware-faithful model — the "what the NES can do" layer
   NesRuntime.Rendering/   Backend abstraction — decouples the runtime from any host
   NesRuntime.Sdk/         Game-author-facing API — the "how you write a game" layer
   NesRuntime.Emulation/   Self-contained NES execution core — runs real iNES/NES 2.0 ROMs
 hosts/
   NesRuntime.Maui/        .NET MAUI presentation host (SkiaSharp canvas + on-screen controls)
```

Dependency direction: `Sdk → Rendering → Core`, and a host depends on all three. Game code depends
only on **`NesRuntime.Sdk`**; a presentation host implements the **`NesRuntime.Rendering`**
backends and drives the loop. `NesRuntime.Maui` is the first such host.

### `NesRuntime.Core` — the hardware model

An authentic model of the console's subsystems. Game authors never reference this directly; the
SDK is built on it and it defines the constraints the SDK enforces.

| Area | Key types |
|------|-----------|
| Console / timing | `NesConsole`, `Region`, `ConsoleTiming`, `NesHardware` (limit constants) |
| CPU (2A03 core) | `Cpu2A03`, `InterruptSource` — interrupt lines & cycle accounting, **not** an opcode interpreter |
| Memory | `MemoryBus`, `IMemoryRegion`, `AddressRange`, `InternalRam` |
| PPU (2C02) | `Ppu2C02`, `PatternTable`/`Tile`, `Nametable`/`AttributeTable`, `ObjectAttributeMemory`, `PaletteMemory`, `MasterPalette`, `NesColor`, `MirroringMode`, `ScrollState`, `PpuControl`/`PpuMask`/`PpuStatus` |
| APU | `Apu2A03`, `PulseChannel`, `TriangleChannel`, `NoiseChannel`, `DmcChannel`, `FrameCounter`, `Envelope`/`SweepUnit`/`LengthCounter` |
| Input | `ControllerPort`, `StandardController`, `Zapper`, `FourScore`, `ExpansionPort`, `NesButtons` |
| Cartridge | `Cartridge`, `INesHeader`, `IMapper`, `MapperRegistry`, `PrgRom`, `ChrMemory`, `BatteryBackedRam` |
| DMA | `OamDmaUnit` |

### `NesRuntime.Rendering` — the host seam

Interfaces a presentation host implements so the runtime can drive it: `IVideoBackend`,
`IAudioBackend`, `IInputBackend`, `IFrameClock`, plus the `FrameBuffer` (256×240 palette indices)
and `RuntimeHost`, which binds a `NesConsole` to the four backends and runs the frame loop.

### `NesRuntime.Sdk` — what you write games against

| Area | Key types |
|------|-----------|
| Game | `NesGame` (base class: `Load`/`Update`/`Render`/`Unload`), `GameInfo`, `GameRunner` |
| Graphics | `IGraphicsDevice`, `ITileSet`/`TileHandle`, `IPaletteSet`/`SystemColors`, `IBackgroundLayer`, `ISpriteLayer`/`SpriteDraw`, `Camera` |
| Audio | `IAudioDevice`, `IToneVoice`/`INoiseVoice`/`ISampleVoice`, `Note`, `IMusicPlayer`/`MusicTrack`, `SoundEffect`/`SampleClip` |
| Input | `IInputManager`, `GamePad` (with `WasPressed`/`IsDown`/`WasReleased`), `Button`, `Pointer` |
| Storage | `ISaveManager` — battery-backed saves |
| Content | `IContentPipeline` (PNG→tiles, WAV→samples) and `INativeContent` (CHR, NSF, iNES ingest) |

Content authoring is **dual-path**: high-level imports (a PNG becomes tiles + a quantized NES
palette; a WAV becomes a DMC sample) *and* native NES formats (raw CHR pattern tables, NSF songs,
complete iNES cartridges), both producing the same runtime objects.

## What a game looks like

```csharp
using NesRuntime.Sdk.Game;
using NesRuntime.Sdk.Graphics;
using NesRuntime.Sdk.Input;

public sealed class HelloNes : NesGame
{
    private TileHandle _hero;
    private int _x, _y;

    public override GameInfo Info => new() { Title = "Hello NES", Author = "you" };

    protected override void Load()
    {
        Graphics.Palettes.SetSprite(0, SystemColors.White, SystemColors.Red, SystemColors.Black);
        _hero = Graphics.Tiles["hero"];   // added via the content pipeline
    }

    protected override void Update()
    {
        var pad = Input.GetPad(0);
        if (pad.IsDown(Button.Right)) _x++;
        if (pad.IsDown(Button.Left))  _x--;
    }

    protected override void Render()
    {
        Graphics.Sprites.Draw(new SpriteDraw(_x, _y, _hero, PaletteIndex: 0));
    }
}
```

A host launches it with `GameRunner.Create<HelloNes>(video, audio, input, clock)`, which returns a
`RuntimeHost` to `Run()`.

## The MAUI host

`hosts/NesRuntime.Maui` is a .NET MAUI app that implements the four `NesRuntime.Rendering`
backends and drives a game:

| Backend | Implementation |
|---------|----------------|
| `IVideoBackend` | `MauiVideoBackend` → `NesGameView`, an `SKCanvasView` that blits the 256×240 framebuffer with nearest-neighbor scaling |
| `IAudioBackend` | `MauiAudioBackend` → plays the synthesized samples via `AVAudioEngine` on Mac Catalyst (a ring buffer feeds an `AVAudioSourceNode`); no-op sink on platforms not yet wired |
| `IInputBackend` | `MauiInputBackend` — merges on-screen buttons, **physical game controllers** (Apple GameController on Mac Catalyst, **Windows.Gaming.Input** on Windows; controller *N* → player *N*), and the **keyboard** on both platforms (arrows = D-pad, Z/X = B/A, Enter = Start, Right-Shift = Select). The host shows a live controller-connection status line. |
| `IFrameClock` | `DispatcherFrameClock`, ticking the loop on the MAUI UI dispatcher at the NTSC rate |

`MainPage` wires these to `HelloNesGame` via `GameRunner` and runs the loop. **Pressing Start
renders and plays sound**: the sample game draws a background border and bitmap-font text, plays an
intro jingle plus a blip when the hero bumps a wall, and moves a hero sprite with the on-screen
D-pad. Here is a frame captured straight from the runtime's framebuffer (3× nearest-neighbor):

![Rendered frame: blue border, black backdrop, "Hello, NES!" text, hero sprite](docs/sample-frame.png)

The sample game ships an 8×8 bitmap font covering every printable ASCII character (0x20–0x7E):

![Font sheet: all 95 printable ASCII glyphs rendered by the runtime](docs/font-sheet.png)

The host runs **`BreakoutGame`** by default — a Breakout clone built entirely on the SDK: the brick
wall is background tiles (destroying one is a single nametable write, so it isn't bound by the
sprite budget), while the paddle, ball, and score/lives HUD are sprites. Move Left/Right, launch
with A. Swap the type argument in `MainPage` (`GameRunner.Create<…>`) to run `HelloNesGame` instead.

![Breakout clone: color-banded brick wall, paddle, ball, score and lives HUD](docs/breakout.png)

## Running real ROMs — the emulator

`NesRuntime.Emulation` is a self-contained NES execution core that runs an actual iNES/NES 2.0
cartridge: a full-official-opcode **6502 CPU** (`Mos6502`), a **scanline-accurate 2C02 PPU** (`Ppu`),
a **2A03 APU** (`Apu`) — pulse ×2, triangle, noise, DMC, frame sequencer, non-linear mixer — mappers
**NROM (0), MMC1 (1), UxROM (2), CNROM (3), MMC3 (4), AxROM (7)** including MMC3's scanline-counter
IRQ (`Mapper`), the iNES/NES 2.0 loader (`INesRom`), and two controllers wired together by
`NesMachine`. `EmulatorGame` (a `NesGame` subclass) drives it one frame per engine frame, feeds it
the engine's controllers, presents the emulated PPU output through the framebuffer hook
(`NesGame.RenderFramebuffer`), and plays the APU's samples through the audio hook
(`NesGame.RenderAudio` → `MauiAudioBackend`). In the host, click **"Load ROM…"** to pick an `.nes` file and run it
(or set `EmulatorGame.RomImage` in code before launch); with neither, a built-in test ROM runs.
Below is that test ROM booted and rendered by the core (its reset code writes a palette, fills the
top tile rows, and enables background rendering):

![Emulator running a test ROM: boxed-tile pattern the ROM's 6502 code drew](docs/emulator.png)

Scope: a **scanline-accurate** core with **audio**, covering the six mappers above (**~80% of the
library**) — CPU execution interleaves with per-scanline rendering, so scrolling, mid-frame
**sprite-0-hit splits** and **MMC3 scanline-IRQ splits** (status bars), and per-line scroll changes
render correctly, while the **full APU** (pulse/triangle/noise/DMC) plays music, effects, and drum
samples. Verified by CPU + mapper + scanline/sprite-0/IRQ + APU/DMC unit tests and hand-assembled
test ROMs — **not** full [Mesen](https://github.com/nesdev-org/MesenCE/)-level parity. Not yet
implemented: **dot**-accurate PPU/DMC timing (mid-scanline raster tricks; the DMC's DMA CPU-stall
cycles), unofficial CPU opcodes, and rarer mappers. Unsupported mappers report an error rather than
mis-rendering.

It targets the desktop platforms: **Mac Catalyst** (built on macOS) and **Windows** (built on
Windows). Each OS builds only the target it can produce, so the solution builds cleanly in Rider or
`dotnet` on either without extra SDKs. **Mac Catalyst builds clean and runs on macOS today.** To
add mobile back, re-add `net10.0-android` / `net10.0-ios` to the host's `<TargetFrameworks>` once
the Android SDK and Xcode's iOS simulator runtimes are installed.

## Build

```sh
# SDK class libraries (no workload needed):
dotnet build src/NesRuntime.Sdk/NesRuntime.Sdk.csproj

# The MAUI host needs the MAUI workload (install once; root-owned SDK path → sudo):
sudo dotnet workload install maui

# The whole solution builds cleanly on macOS (host resolves to Mac Catalyst) or Windows:
dotnet build NesRuntime.slnx
```

Requires the **.NET 10 SDK** plus the **MAUI workload** for the host; the three libraries build
without the workload. On macOS the host builds Mac Catalyst; on Windows it builds the Windows
target — so `dotnet build NesRuntime.slnx` (and Rider's solution build) succeeds on either.

## Design decisions

- **Strict-authentic fidelity.** The API models and enforces real NES hardware limits; a game
  cannot exceed what the console could produce.
- **Namespace / product:** `NesRuntime`.
- **Target framework:** libraries `net10.0`; MAUI host `net10.0-maccatalyst` (macOS) / `net10.0-windows` (Windows).
- **Regions:** NTSC (default), PAL, and Dendy are all modeled via `ConsoleTiming`.

## Roadmap

The PPU render and audio slices are in. Next: make the APU register-accurate (replace the stand-in
synthesizer), implement scrolling/mid-frame split rendering, add native content import (PNG→tiles,
CHR/NSF/iNES loaders), wire audio output on Windows, and stand up a test project around the
compositor and synth.
