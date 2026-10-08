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
- **Compatibility** covers >90% of the licensed library by game count with **17 mappers**, but the
  long mapper tail, PAL, and FDS are missing.
- **Feature/accuracy** is "good enough for most commercial games" but not cycle-accurate.
- The keystone blocker: the **CPU executes whole instructions atomically** (`Mos6502.Step()` returns a
  cycle count) instead of cycle-stepping the bus. Most hard accuracy gaps hang off this one decision.

---

## Dimension scorecard

### 1. Speed — *smallest gap*

| | Current | Mesen |
|---|---|---|
| Core frame time | ~0.5 ms Release (≈34× realtime, measured) | comparable multiples |
| Fast-forward / rewind | none | large multiples, buffered rewind |

- Realtime speed has large headroom today.
- Cycle-stepping the CPU (Tier B) will cost ~2–5× and spend that headroom; re-optimization will be
  needed to stay realtime.
- Fast-forward/rewind multiples are a save-state + re-sim feature, not raw speed.

### 2. Compatibility — *large but tractable*

| Area | Current | Mesen | Gap |
|---|---|---|---|
| Mappers | **17** (0,1,2,3,4,7,9,10,11,21–26,66,69) | ~300 | >90% of licensed games covered; missing MMC5(5), Namco 163(19), VRC7(85), most unlicensed/multicart, long tail |
| Regions | **NTSC only** (262-line timing hardcoded) | NTSC / PAL / Dendy + auto-detect | no PAL (312-line, different CPU:APU ratios, 50 Hz) |
| Formats | iNES + NES 2.0 | + FDS, UNIF, NSF/NSFe, full submappers | no Famicom Disk System, no music formats |
| Input | 2 standard pads | Zapper, Four Score (4p), Power Pad, Arkanoid, Family BASIC keyboard, … | peripherals absent |
| Battery / NVRAM | ✅ | ✅ | at parity |

### 3. Feature — accuracy + tooling

**3a. Emulation accuracy** (several gaps already noted in `CLAUDE.md`):

- **CPU:** atomic instructions → no cycle-stepped bus, no dummy reads/writes; DMA/OAM-DMA/DMC
  cycle-stealing is approximate; interrupt polling isn't at exact cycles; unstable opcodes
  (SHX/SHY/SHA/TAS/LAS) use the common "AND high-byte+1" approximation.
- **PPU:** scanline-batch + catch-up gives correct *mid-scanline* effects but not true per-dot.
  Missing: exact sprite-0 dot, the sprite-overflow hardware bug, OAM decay/corruption, open-bus
  reads, palette-access quirks, and **PPUMASK grayscale/emphasis** (not applied). Register writes land
  at *instruction* granularity (~a few px).
- **APU:** solid core, but **no expansion audio** (VRC6/FME-7 decoded but silent; VRC7 FM, Namco 163,
  MMC5, Sunsoft 5B absent), the **DPCM-DMA ↔ controller-read conflict** isn't modeled, and
  mixing/frame-counter edges are approximate.
- **Mappers:** MMC3 IRQ is a per-scanline A12 *approximation*; VRC6 models CHR mode 0 only.
- **Validation:** hand-assembled tests only; Mesen passes the full blargg/accuracy test-ROM suite,
  which has not been run here.

**3b. Tooling** (Mesen's enormous surface — none of this exists yet):
save states, rewind, movies/TAS, netplay; a full debugger (CPU/PPU/APU debuggers, breakpoints, trace
logger, event viewer, memory/nametable/CHR/sprite viewers, disassembler, Lua scripting); cheats
(Game Genie/PAR); video filters (NTSC composite, scanlines, HD packs, shaders, overscan/aspect);
audio filters/equalizer; per-game config & input remapping.

---

## Roadmap

### Tier A — high value, moderate effort → "excellent daily driver," ~98% compat

- [ ] **Save states + rewind** — deterministic serialization of every component (CPU/PPU/APU/mapper/
      RAM/OAM/VRAM). Most-expected missing feature; also the substrate for TAS/netplay.
- [ ] **PAL + Dendy** region support with auto-detect (parameterize the hardcoded NTSC timing in
      `Ppu`/`NesMachine`/`Apu`).
- [ ] **Mapper batch:** MMC5 (5), Namco 163 (19), VRC7 (85), + common unlicensed/multicart mappers.
- [ ] **Expansion audio:** synthesize the already-decoded VRC6/FME-7; add VRC7 FM, Namco 163, MMC5.
- [ ] **Peripherals:** Zapper, Four Score (4-player), Power Pad.
- [ ] **FDS** (Famicom Disk System) — large library unlock.
- [ ] Apply **PPUMASK grayscale/emphasis** bits (small, closes a visible gap).

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

**Suggested first step:** **save states** (self-contained, immediately useful, no architectural
rewrite) or the **cycle-stepped CPU** (the accuracy keystone, gates most of Tier B).
