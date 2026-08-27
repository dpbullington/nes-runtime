# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

NesRuntime lets people **write NES games in C#** and run them against a modern GUI. It is a
**runtime, not an emulator**: there is no 6502 opcode interpreter and no pre-built `.nes` ROM is
executed. Game logic is authored in C# against the SDK, and the runtime maps it onto a
hardware-faithful model of the NES.

Current state: **API skeleton with a working render slice.** The full surface exists and is
XML-documented. One vertical slice is implemented end-to-end (see "Implemented render path" below);
the rest of the bodies still throw `NotImplementedException`. The libraries compile with 0 warnings
and the Mac Catalyst host renders real frames.

## Build & test

```sh
# The three SDK libraries — no workload needed, expected 0 warnings / 0 errors:
dotnet build src/NesRuntime.Sdk/NesRuntime.Sdk.csproj

# The whole solution — builds clean on macOS (host → Mac Catalyst) or Windows (host → Windows):
dotnet build NesRuntime.slnx

# Run the host for the ROM emulator — use RELEASE (see perf note below):
dotnet build -t:Run -c Release -f net10.0-maccatalyst hosts/NesRuntime.Maui/NesRuntime.Maui.csproj
```

- Requires the **.NET 10 SDK** (10.x installed). The solution file is the newer **`.slnx`** format
  (`NesRuntime.slnx`), not `.sln`.
- The **MAUI workload is installed** in this environment. The host targets **desktop only** — Mac
  Catalyst on macOS, Windows on Windows (each conditioned by OS). So `dotnet build NesRuntime.slnx`
  and Rider's solution build succeed on either without the Android SDK or Xcode iOS simulators.
  Mac Catalyst is the verified-runnable target (builds 0/0). **Android/iOS targets were removed**
  because they need those platform SDKs; re-add `net10.0-android`/`net10.0-ios` to the host
  `<TargetFrameworks>` once the SDKs are installed.
- **Run the emulator host in Release, not Debug.** The runtime loop runs on the UI thread
  (`DispatcherFrameClock`), so a `StepFrame` that overruns the ~16.7 ms frame budget makes the
  dispatcher-timer callbacks pile up and video/audio/input lag together. The `NesRuntime.Emulation`
  core (per-pixel PPU + per-cycle APU) is a tight loop that the JIT only optimizes in Release: a
  full-render frame is ~0.5 ms in Release (≈34× realtime) but ~2.4 ms in Debug (JIT skips
  optimization/inlining), and Debug leaves little headroom once host present/audio/input overhead is
  added. If it runs slow, check the run configuration is **Release** first.
- Watch for **name collisions with MAUI's implicit usings** in host code: `Microsoft.Maui.Controls`
  is globally imported, so its `Button`/`Region`/etc. clash with `NesRuntime.Sdk.Input.Button` and
  `NesRuntime.Core.Region`. Disambiguate with a `using X = ...;` alias or full qualification (see
  `Games/HelloNesGame.cs` and `MainPage.xaml.cs`).
- SkiaSharp is **3.x**: pixel-scaled blits use `canvas.DrawImage(SKImage, dest, SKSamplingOptions)`
  (there is no `DrawBitmap` sampling-options overload) — see `Controls/NesGameView.cs`.
- There is **no test project yet**. When adding one, create it under `tests/` and add it with
  `dotnet sln add`.
- Keep the library build clean: it is expected to stay at **0 warnings**. `Directory.Build.props`
  suppresses only `CS1591` (missing XML doc) and `CS0067` (declared-but-unraised events) because
  both are inherent to a documented skeleton — do not suppress correctness warnings there.

### Target-framework layout (important)

`Directory.Build.props` deliberately does **not** set a `TargetFramework`. The three libraries pin
`net10.0` in their own csproj; the MAUI host multi-targets platform monikers via
`<TargetFrameworks>`, conditioned by OS — `net10.0-maccatalyst` on macOS, `net10.0-windows…` on
Windows (so each machine builds only the target it can produce). A shared singular
`<TargetFramework>` would collapse the host's multi-target build and break it — do not move the TFM
back into the shared props.

## Architecture

Three layers, strict one-way dependency `Sdk → Rendering → Core`:

- **`src/NesRuntime.Core`** — the hardware model ("what the NES can do"). Faithful subsystems:
  `NesConsole` (composition root), CPU interrupt/timing (`Cpu2A03` — interrupt lines only, no
  opcodes), `MemoryBus`/`IMemoryRegion`, the PPU (`Ppu2C02` and its pattern tables, nametables,
  OAM, palettes, scroll), the APU (`Apu2A03` + five channel types), input devices, and
  cartridge/mappers (`Cartridge`, `IMapper`, `MapperRegistry`). Game code never references this.
- **`src/NesRuntime.Rendering`** — the host seam. A presentation host implements `IVideoBackend`,
  `IAudioBackend`, `IInputBackend`, `IFrameClock`; `RuntimeHost` binds a `NesConsole` to them and
  runs the frame loop. `FrameBuffer` carries 256×240 palette indices (color is resolved host-side
  via `MasterPalette`).
- **`src/NesRuntime.Sdk`** — the author-facing API. `NesGame` is the base class
  (`Load`/`Update`/`Render`/`Unload`, called once per frame); it exposes `IGraphicsDevice`,
  `IAudioDevice`, `IInputManager`, `ISaveManager`. `GameRunner.Create<TGame>(...)` boots a game
  onto the runtime and returns a `RuntimeHost`.
- **`hosts/NesRuntime.Maui`** — the presentation host. Implements the four rendering backends
  (`MauiVideoBackend`→`NesGameView` SkiaSharp canvas, `MauiAudioBackend`, `MauiInputBackend` which
  merges on-screen controls + physical game controllers (Apple GameController `#if MACCATALYST`,
  `Windows.Gaming.Input` `#elif WINDOWS`) + keyboard on both platforms (GCKeyboard on Mac,
  `InputKeyboardSource.GetKeyStateForCurrentThread` on Windows), `DispatcherFrameClock` on the UI
  dispatcher) and wires them to a game via
  `GameRunner` in `MainPage`. The whole runtime loop runs on the UI thread by design, so
  presentation/input need no cross-thread marshaling. Pressing Start runs and renders the sample
  game; the try/catch around startup remains as a safety net for paths that are still stubbed.
  The host currently launches `EmulatorGame` (swap the `GameRunner.Create<…>` type arg for
  `BreakoutGame` / `HelloNesGame`). **Controller gotcha:** older/generic USB pads expose the
  deprecated `GCGamepad` profile, not `GCExtendedGamepad`, so `MauiInputBackend` reads both
  (`ReadLegacyGamepad`). Never call `GCController.MicroGamepad` on such a pad — the binding
  mis-casts (`GCGamepad`→`GCMicroGamepad`) and aborts the app; GameController reads are wrapped in
  try/catch so a bad controller can't crash the game. **Entitlements gotcha:** the csproj wires
  `Platforms/MacCatalyst/Entitlements.plist` via `<CodesignEntitlements>` (it is *not* auto-applied)
  so the sandbox grants the "Load ROM…" picker file access and the USB controller access
  (`device.usb`). Use `files.user-selected.read-**write**` — MAUI's picker requests a read-write
  sandbox extension, so read-only fails with "couldn't issue sandbox extension …read-write". Without
  wiring the plist at all, the picker fails with "Failed to create an FPSandboxingURLWrapper".
- **`src/NesRuntime.Emulation`** — a self-contained NES execution core that runs real ROMs, distinct
  from the C# authoring SDK: `Mos6502` (full official **and** unofficial/illegal 6502 opcode set —
  LAX/SAX/DCP/ISC/SLO/RLA/SRE/RRA/ANC/ALR/ARR/SBX/undocumented NOPs + unstable stores — plus a
  level-triggered `IrqLine`),
  `Ppu` (**2C02 with mid-scanline accuracy at scanline-batch speed** — `NesMachine.StepFrame` runs the
  dot clock (`Ppu.Run`) three dots per CPU cycle, but pixels are rendered *lazily*: each visible line
  is drawn as a fast span (directly from the loopy v/t/x scroll regs), and only up to the current dot.
  `Ppu.CatchUp` renders the line up to now, and the machine forces it right before any write that
  changes the picture — a PPU register/palette write (inside `WriteRegister`) or a mapper CHR/mirroring
  write (in the bus, before `_mapper.CpuWrite`) — and before every `$2002` read. So a mid-scanline
  scroll/mask/palette/CHR change affects only the pixels after it, and the sprite-0 hit becomes visible
  at the dot the CPU polls it — **mid-frame raster splits + status-bar splits work**. A line with no
  mid-line writes is drawn in one span at dot 256 (old-scanline cost); only lines doing raster tricks
  pay finer catch-up. Background tile fetches are cached across each tile's 8 px and sprite patterns
  pre-fetched once per line. NMI is an edge off vblank via `Ppu.ConsumeNmi`),
  `Apu` (2A03 audio — pulse ×2/triangle/noise/DMC + frame sequencer + non-linear mixer; clocked per
  CPU cycle, emits a float sample stream via `NesMachine.CopyAudio`; the DMC reads sample bytes via a
  memory-reader callback and reports its **DMA CPU-stall cycles** (~4/fetch) via `Apu.TakeDmcStall()`,
  which the machine adds to the master cycle count so the CPU falls behind as on hardware),
  `Mapper` (NROM 0, MMC1 1, UxROM 2, CNROM 3, MMC3 4, AxROM 7, MMC2 9, MMC4 10, Color Dreams 11,
  Konami VRC2/VRC4 21·22·23·25 and VRC6 24·26, GxROM 66, Sunsoft FME-7 69; unknown mappers throw
  `NotSupportedException`). Two IRQ hooks on `IMapper` (both default no-op, both drive
  `IMapper.IrqPending` → `Mos6502.IrqLine`): MMC3's scanline IRQ via `OnScanline()` (clocked per
  rendered scanline), and the CPU-cycle down-counter IRQs of FME-7 (16-bit) and Konami VRC4/VRC6
  (8-bit + 341-dot prescaler, shared `VrcIrq` helper) via `OnCpuCycles(int)` (clocked in the CPU step
  loop). `IrqLine` is refreshed after each clock and after every mapper write (so an `$E000` MMC3
  acknowledge, an FME-7 `$D`, or a VRC control/ack write clears it). MMC2/MMC4 flip their CHR banks
  via a **fetch latch** updated as a side effect of `ChrRead` (tile `$FD`/`$FE`), needing no PPU
  change. FME-7's 5B and VRC6's expansion audio are accepted but not synthesized; VRC2/VRC4 register
  select bits are decoded by OR-ing every candidate address line (robust to iNES-1.0 submapper
  ambiguity).
  `INesRom` (iNES/NES 2.0 loader), and `NesMachine` (bus + controllers + per-frame step). The PPU
  fetches all CHR through `IMapper.ChrRead` and reads `IMapper.Mirroring` live, and the bus routes
  PRG through the mapper — so adding a (non-IRQ) mapper is just a new `IMapper` class, no PPU changes.
  It emits 256×240 system-palette indices. The host's `EmulatorGame : NesGame` wraps it and presents
  via the **`NesGame.RenderFramebuffer` hook** (raw video) and the **`NesGame.RenderAudio` hook**
  (raw float samples → `MauiAudioBackend`) — escape hatches (`SoftwareRuntimeCore` prefers them over
  the tile/sprite compositor + SDK synth) that let a game emit whole frames + audio directly. Verified
  by CPU + mapper + APU unit tests, a hand-assembled **mid-scanline raster-split ROM** (proves the
  catch-up renderer: a row splits bg→backdrop mid-line, which a per-scanline renderer can't produce),
  and other hand-assembled ROMs (scratchpad harness).
  It is mid-scanline-accurate with the full APU, full illegal-opcode set, DMC DMA stalls, and the
  common mappers (0/1/2/3/4/7/9/10/11/21-26/66/69 — well past 90% of the licensed library). The
  remaining gap versus Mesen: because the CPU executes whole instructions atomically (not
  cycle-stepped), a register write takes effect at **instruction granularity** — within a few CPU
  cycles / a handful of dots of its true store cycle — so a raster split lands on the right scanline
  and within a few pixels horizontally, not at the exact dot. Also: the unstable stores (SHX/SHY/SHA/TAS/LAS) use the common
  "AND high-byte+1" behavior rather than being bus-conflict/RDY accurate; VRC6/FME-7 expansion audio
  is silent; VRC6 CHR banking mode 0 (eight 1 KiB banks) is the only mode modeled; and the PPUMASK
  grayscale/color-emphasis bits ($2001 bits 0, 5-7) are not applied to the output palette index.

## Conventions that matter here

- **Strict hardware authenticity is a hard requirement, not a nicety.** The SDK deliberately
  enforces real NES limits (64 sprites total, 8 per scanline, 4+4 palettes of 3 colors from the
  fixed 64-color system palette, 256×240 tile grid, 5 audio channels). `NesHardware` holds these
  constants — reference them rather than hard-coding numbers, and preserve the constraints when
  implementing stubs. Colors are `NesColor` **indices into the system palette**, never arbitrary
  RGB.
- **Dual-path content is intentional.** Every asset kind supports both a high-level import
  (PNG→tiles+palette, WAV→DMC sample) and a native NES-format loader (CHR, NSF, iNES) — see
  `IContentPipeline` and `INativeContent`. Keep both paths when extending content.
- **The Core layer stays game-agnostic.** No game-facing convenience belongs in `NesRuntime.Core`;
  that sugar lives in the SDK. Core models hardware; the SDK makes it pleasant.
- **File/namespace layout:** namespaces follow folders (`NesRuntime.Core.Ppu`,
  `NesRuntime.Sdk.Graphics`, …). One primary public type per file, with tightly-coupled small
  types (an enum + its owner) allowed to share a file.
- `Directory.Build.props` centralizes shared settings (net10.0, nullable, implicit usings, doc
  generation) for every project — add cross-cutting settings there, not per-csproj.

## Implemented render path (the vertical slice)

A game renders real frames through this chain — everything here has real logic, not stubs:

- **`Ppu2C02.RenderTo(Span<byte>)`** (Core) — the software compositor. Fills the backdrop, draws
  the background (nametable tile indices → pattern-table pixels → attribute-table palette), then
  sprites (OAM order = priority, transparency, H/V flip, 8×8 and 8×16, behind-background priority,
  sprite-0 hit, and the authentic 8-sprites-per-scanline drop with the overflow flag). Outputs one
  system-palette index per pixel. Supporting Core types (`MasterPalette` with the real 64-color
  table, `PaletteMemory`, `PatternTable`/`Tile`, `Nametable`/`AttributeTable`,
  `ObjectAttributeMemory`, `ScrollState`) are implemented.
- **`FrameBuffer`** (Rendering) — holds the index buffer; `ResolveTo` converts to 0xRRGGBB via
  `MasterPalette`.
- **SDK software services** — `SoftwareGraphicsDevice` (+ tile set / palette set / background /
  sprite layer) writes the game's draw calls into a `Ppu2C02`; `SoftwareInputManager`/`GamePad`
  give edge-detected input; `SilentAudioDevice` and `MemorySaveManager` are working no-op/in-memory
  services.
- **Audio** — `SynthAudioDevice` (SDK) is a working stand-in synthesizer: pulse (square, with
  duty), triangle, an LFSR noise voice, and a `SoundEffect` player (one tone step per frame). It
  implements `IAudioDevice`, so game code (`Audio.Pulse1.PlayNote(...)`, `Audio.Play(effect)`)
  drives it. `Note.Frequency`/`FromMidi` and `SoundEffect.FromSteps` are implemented. It is *not*
  the register-accurate APU — that's still stubbed.
- **Loop** — `GameRunner.Create<TGame>` builds a `Ppu2C02` + services (including `SynthAudioDevice`),
  attaches the game, runs `Load`, and returns a `RuntimeHost` wrapping a `SoftwareRuntimeCore`
  (implements `IRuntimeCore`). Each `StepFrame`: poll input → game Update/Render → PPU composite →
  **synthesize this frame's audio and push it to `IAudioBackend.QueueSamples`** → present.
  `MauiAudioBackend` plays those samples via `AVAudioEngine` on Mac Catalyst (ring buffer →
  `AVAudioSourceNode`; `#if MACCATALYST`, no-op elsewhere). **Note the layering seam: `RuntimeHost`
  (Rendering) is driven by the `IRuntimeCore` abstraction, not a `NesConsole`** — the SDK owns frame
  production. `NesConsole` remains a stub for a future register-accurate path.

Verify a render-path change without launching MAUI: drive `GameRunner.Create<T>` with fake
backends, `Fire()` a manual `IFrameClock`, and inspect the returned `FrameBuffer` (that is how the
slice was verified — resolving to RGB and writing a BMP).

## Not yet present (planned)

No test project exists yet. Audio works via the stand-in `SynthAudioDevice`, but the
**register-accurate APU** (`Apu2A03` and its channel types) is still stubbed, as are cartridge/mapper
loading, the CPU memory bus, and the register-accurate PPU path (`Ppu2C02.Read`/`Write`). Next
steps: make the APU real; scrolling/mid-frame splits; native content import (PNG→tiles,
CHR/NSF/iNES); Windows audio output; and a test project around the compositor and synth. The sample
game lives at `hosts/NesRuntime.Maui/Games/HelloNesGame.cs`.
