# Mesen Parity Roadmap

An honest gap analysis of the `NesRuntime.Emulation` core against **[Mesen](https://www.mesen.ca/)**
(the reference-grade, cycle-accurate NES emulator), with a prioritized, checkbox-tracked roadmap.

> **Framing.** NesRuntime is primarily a **C# game-authoring runtime**; the ROM emulator
> (`src/NesRuntime.Emulation`) is a secondary, self-contained core. Literal Mesen parity — cycle
> accuracy + ~300 mappers + a full debugger + netplay/TAS + video/audio filters — is a multi-year
> effort and effectively a second product. The productive goal is a **high-accuracy daily-driver**
> (Tier A + the CPU rewrite in Tier B). Tier C tooling is à-la-carte.

## Bottom line

- **Speed** is *not* the problem today (~0.5 ms/frame Release ≈ 34× realtime). It becomes a concern
  only after cycle accuracy is added.
- **Compatibility** covers >90% of the licensed library by game count with **20 mapper numbers**
  (16 classes), but the long mapper tail, PAL/Dendy, and FDS are missing.
- **Feature set** is now a featureful daily-driver — save states, rewind, color emphasis, most
  expansion audio — but still **not cycle-accurate**.
- The keystone blocker: the **CPU executes whole instructions atomically** (`Mos6502.Step()` returns a
  cycle count) instead of cycle-stepping the bus. Most hard accuracy gaps hang off this one decision.

### Where we are (tier read)

- **Tier A (high-accuracy daily driver): ~60% done.** ✅ save states + rewind, grayscale/emphasis,
  VRC6/FME-7 expansion audio, the VRC7/Namco 163/MMC5 mapper batch. Remaining: PAL/Dendy, FDS,
  peripherals, the deferred expansion audio (VRC7 FM, N163 wavetable, MMC5 sound, 5B noise/envelope),
  and finishing MMC5's PPU features.
- **Tier B (accuracy parity): keystone not started (~0%).** Partial timing fidelity exists (dot-exact
  sprite-0, mid-scanline rendering, DMC stalls), but the cycle-stepped CPU + test-ROM validation — the
  actual "accuracy" bar — hasn't begun.
- **Tier C (tooling): ~0%.** Debugger, Lua, cheats, video/audio filters, TAS/netplay.

**One-liner:** moved from "solid NTSC core" to "featureful daily-driver for most of the NTSC licensed
library"; the two things that most define Mesen parity — **cycle-level accuracy** and the **tooling
suite** — remain almost entirely ahead and are a multi-year effort.

---

## Dimension scorecard

### 1. Speed — *smallest gap*

| | Current | Mesen |
|---|---|---|
| Core frame time | ~0.5 ms Release (≈34× realtime, measured) | comparable multiples |
| Rewind | ✅ ~20 s ring (per-frame save states) | ✅ buffered rewind |
| Fast-forward | ✅ hold-to-FF (`RuntimeHost.SpeedMultiplier`, 4×) | ✅ large multiples |

- Realtime speed has large headroom today.
- Cycle-stepping the CPU (Tier B) will cost ~2–5× and spend that headroom; re-optimization will be
  needed to stay realtime.
- ✅ Fast-forward (run N frames/tick, present the last) and rewind both exist; higher FF multiples are
  just a bigger `SpeedMultiplier` and are limited only by the per-frame budget (huge headroom in Release).

### 2. Compatibility — *large but tractable*

| Area | Current | Mesen | Gap |
|---|---|---|---|
| Mappers | **20 numbers / 16 classes** (0,1,2,3,4,5,7,9,10,11,19,21–26,66,69,85) | ~300 | >90% of licensed games covered; MMC5(5) partial; missing the unlicensed/multicart long tail |
| Regions | **NTSC only** (262-line timing hardcoded) | NTSC / PAL / Dendy + auto-detect | no PAL (312-line, different CPU:APU ratios, 50 Hz) |
| Formats | iNES + NES 2.0 | + FDS, UNIF, NSF/NSFe, full submappers | no Famicom Disk System, no music formats |
| Input | 2 standard pads | Zapper, Four Score (4p), Power Pad, Arkanoid, Family BASIC keyboard, … | peripherals absent |
| Battery / NVRAM | ✅ | ✅ | at parity |
| Save states / rewind | ✅ (8 slots + ~20 s rewind) | ✅ | at parity |

### 3. Feature — accuracy + tooling

**3a. Emulation accuracy** (several gaps also noted in `CLAUDE.md`):

- **CPU:** atomic instructions → no cycle-stepped bus, no dummy reads/writes; DMA/OAM-DMA/DMC
  cycle-stealing is approximate; interrupt polling isn't at exact cycles; unstable opcodes
  (SHX/SHY/SHA/TAS/LAS) use the common "AND high-byte+1" approximation. *(Unchanged — the keystone.)*
- **PPU:** scanline-batch + catch-up gives correct *mid-scanline* effects but not true per-dot.
  ✅ dot-exact sprite-0 hit and **PPUMASK grayscale/emphasis** are now done. Still missing: the
  sprite-overflow hardware bug, OAM decay/corruption, open-bus reads, palette-access quirks. Register
  writes land at *instruction* granularity (~a few px).
- **APU:** solid core. ✅ DMC DMA stalls modeled; ✅ VRC6 + FME-7/Sunsoft 5B expansion audio
  synthesized. Still missing: VRC7 FM, Namco 163 wavetable, and MMC5 expansion audio (plus 5B
  noise/envelope); the **DPCM-DMA ↔ controller-read conflict**; mixing/frame-counter edges approximate.
- **Mappers:** MMC3 IRQ is a per-scanline A12 *approximation*; MMC5 is partial (ExRAM/fill/split
  nametable modes + audio deferred); VRC6 models CHR mode 0 only.
- **Validation:** hand-assembled + register/banking unit tests only; Mesen passes the full
  blargg/accuracy test-ROM suite, which has not been run here.

**3b. Tooling** (Mesen's enormous surface):
✅ save states + rewind now exist. Still missing: movies/TAS, netplay; a full debugger (CPU/PPU/APU
debuggers, breakpoints, trace logger, event viewer, memory/nametable/CHR/sprite viewers, disassembler,
Lua scripting); cheats (Game Genie/PAR); video filters (NTSC composite, scanlines, HD packs, shaders,
overscan/aspect); audio filters/equalizer; per-game config & input remapping.

---

## Roadmap

### Tier A — high value, moderate effort → "excellent daily driver," ~98% compat

- [x] **Save states + rewind** — ✅ done. `StateSerializer` (one bidirectional pass) + `SerializeState`
      on CPU/PPU/APU(+channels)/every mapper/`VrcIrq`; `NesMachine.SaveState()/LoadState()` with a
      magic/version/mapper-guarded header. Host: **8 timestamped save-state slots** (picker +
      Save/Load/Delete) at `~/.nes-runtime/save/<rom>/state<N>.bin`, plus a per-frame rewind ring
      (`RewindBuffer`, ~20 s) with a hold-to-rewind control. Verified by idempotent-round-trip +
      deterministic-replay tests.
- [ ] **PAL + Dendy** region support with auto-detect (parameterize the hardcoded NTSC timing in
      `Ppu`/`NesMachine`/`Apu`).
- [~] **Mapper batch:** ✅ VRC7 (85) and Namco 163 (19) banking + IRQ; MMC5 (5) partial (PRG/CHR
      banking incl. the 8×16 sprite/bg CHR split, multiplier, scanline IRQ). Remaining: MMC5
      ExRAM-nametable/fill/split + audio, VRC7 FM audio, N163 wavetable audio, and unlicensed/multicart
      mappers. Verified by register/banking/IRQ unit tests (rendering unverified without ROMs).
- [~] **Expansion audio:** ✅ VRC6 (2 pulse + sawtooth) and FME-7 Sunsoft 5B (3 YM2149 square channels,
      4-bit log volume) synthesized and mixed into the APU via `IMapper.AudioSample()`. Remaining: 5B
      noise + hardware envelope, and the other expansion chips — VRC7 FM, Namco 163, MMC5.
- [ ] **Peripherals:** Zapper, Four Score (4-player), Power Pad.
- [ ] **FDS** (Famicom Disk System) — large library unlock.
- [x] Apply **PPUMASK grayscale/emphasis** bits — ✅ done. Grayscale is a per-pixel index mask in
      `Ppu.Palette`; color emphasis is captured per frame (`NesMachine.Emphasis`) and tints the frame
      host-side via `FrameBuffer.Emphasis` → `MasterPalette.ToRgb`. Verified by harness tests.

### Tier B — accuracy parity (the hard core, several months)

- [ ] **Rewrite the CPU as cycle-stepped over a unified bus** that ticks PPU/APU per cycle. Unlocks:
      true dot-accurate PPU, exact DMA/DMC/OAM timing, DPCM↔controller conflicts, sprite-overflow bug,
      open bus, OAM decay.
- [ ] **Validate against the accuracy test-ROM suite** (blargg ppu/apu/cpu, sprite_hit, vbl_nmi,
      mmc3_irq, etc.) as the objective scorecard.
- [ ] **PAL PPU/APU** exactness (beyond the Tier-A timing parameterization).

### Tier C — tooling (open-ended, many months; à-la-carte)

- [ ] Debugger suite: breakpoints, trace logger, memory/PPU/nametable/CHR/sprite viewers, disassembler.
- [ ] Lua scripting API.
- [ ] Cheats (Game Genie / Pro Action Rocky).
- [ ] Video: NTSC composite filter, scanlines, HD packs, shaders, overscan/aspect config, recording.
- [ ] Audio: filters, equalizer, per-channel volume, recording.
- [ ] Movies / TAS recording, netplay.

---

## Recommendation

Target **Tier A + the cycle-stepped CPU from Tier B**, validated against the test-ROM suite — that
yields a genuinely high-accuracy, ~98%-compatible emulator with save states and most peripherals,
which is what "parity" means for *playing games*. Treat Tier C as optional; it is where Mesen's decade
of surface area lives and where the effort diverges most from this project's authoring-runtime purpose.

**Done so far:** save states + rewind, grayscale/emphasis, VRC6/FME-7 audio, and the
VRC7/Namco 163/MMC5 mapper batch — Tier A is ~60% complete.

**Suggested next steps:** finish Tier A breadth with **PAL/Dendy** (broad compatibility, self-contained)
or **FDS** (big library unlock); or start the accuracy story with the **cycle-stepped CPU + test-ROM
harness** (the Tier-B keystone — the first thing that makes a measurable dent in accuracy parity).
