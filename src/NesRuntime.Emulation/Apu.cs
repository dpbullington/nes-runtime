namespace NesRuntime.Emulation;

/// <summary>
/// The 2A03 APU: two pulse channels, triangle, and noise, driven by the frame sequencer and mixed
/// with the hardware's non-linear formulas into a float sample stream. It is clocked at the CPU rate
/// and emits samples at the host rate. The DMC (delta-modulation / sample) channel is not yet
/// implemented — a known omission (drum samples / voice clips will be missing).
/// </summary>
public sealed class Apu
{
    private const double CpuHz = 1789773.0; // NTSC

    private static readonly int[] LengthTable =
    [
        10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14,
        12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30,
    ];
    private static readonly int[] NoisePeriods =
        [4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068];
    private static readonly byte[][] DutyTable =
    [
        [0, 1, 0, 0, 0, 0, 0, 0],
        [0, 1, 1, 0, 0, 0, 0, 0],
        [0, 1, 1, 1, 1, 0, 0, 0],
        [1, 0, 0, 1, 1, 1, 1, 1],
    ];
    private static readonly int[] TriangleSeq =
    [
        15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    ];

    private readonly Pulse _pulse1 = new(onesComplement: true);
    private readonly Pulse _pulse2 = new(onesComplement: false);
    private readonly Triangle _triangle = new();
    private readonly Noise _noise = new();
    private readonly Dmc _dmc;

    private int _cycle;
    private int _frameCycle;
    private bool _mode5, _irqInhibit, _frameIrq;

    private readonly double _cyclesPerSample;
    private double _sampleAccum;
    private readonly float[] _buffer;
    private int _count;
    private float _hpPrevIn, _hpPrevOut;

    /// <summary>Creates the APU. <paramref name="readMemory"/> reads CPU-space bytes for the DMC's sample DMA.</summary>
    public Apu(int sampleRate, Func<ushort, byte> readMemory)
    {
        _cyclesPerSample = CpuHz / sampleRate;
        _buffer = new float[sampleRate]; // up to 1 second
        _dmc = new Dmc(readMemory);
    }

    /// <summary>The frame-counter IRQ or the DMC IRQ is asserted.</summary>
    public bool IrqPending => _frameIrq || _dmc.IrqPending;

    /// <summary>
    /// Returns and clears the CPU cycles stolen by DMC sample DMA since the last call. The machine
    /// adds these to its master cycle count so the CPU falls behind exactly as it does on hardware.
    /// </summary>
    public int TakeDmcStall() { int s = _dmc.Stall; _dmc.Stall = 0; return s; }

    /// <summary>Resets the per-frame sample buffer. Call at the start of each frame.</summary>
    public void BeginFrame() => _count = 0;

    /// <summary>Samples generated so far this frame.</summary>
    public int SampleCount => _count;

    /// <summary>Copies this frame's samples into <paramref name="dest"/>, padding with the last value.</summary>
    public void CopyAudio(Span<float> dest)
    {
        int n = Math.Min(_count, dest.Length);
        for (int i = 0; i < n; i++) dest[i] = _buffer[i];
        float last = n > 0 ? _buffer[n - 1] : 0f;
        for (int i = n; i < dest.Length; i++) dest[i] = last;
    }

    /// <summary>Advances the APU by the given number of CPU cycles.</summary>
    public void Clock(int cpuCycles)
    {
        for (int i = 0; i < cpuCycles; i++)
            ClockCycle();
    }

    private void ClockCycle()
    {
        _triangle.ClockTimer();                       // triangle: every CPU cycle
        _dmc.ClockTimer();                            // DMC: every CPU cycle
        if ((_cycle & 1) == 0)                         // pulses + noise: every APU cycle (2 CPU)
        {
            _pulse1.ClockTimer();
            _pulse2.ClockTimer();
            _noise.ClockTimer();
        }
        _cycle++;

        ClockFrameSequencer();

        _sampleAccum += 1.0;
        if (_sampleAccum >= _cyclesPerSample)
        {
            _sampleAccum -= _cyclesPerSample;
            Emit();
        }
    }

    private void ClockFrameSequencer()
    {
        _frameCycle++;
        if (_mode5)
        {
            switch (_frameCycle)
            {
                case 7457: QuarterFrame(); break;
                case 14913: QuarterFrame(); HalfFrame(); break;
                case 22371: QuarterFrame(); break;
                case 37281: QuarterFrame(); HalfFrame(); break;
                case 37282: _frameCycle = 0; break;
            }
        }
        else
        {
            switch (_frameCycle)
            {
                case 7457: QuarterFrame(); break;
                case 14913: QuarterFrame(); HalfFrame(); break;
                case 22371: QuarterFrame(); break;
                case 29829: QuarterFrame(); HalfFrame(); if (!_irqInhibit) _frameIrq = true; break;
                case 29830: _frameCycle = 0; break;
            }
        }
    }

    private void QuarterFrame()
    {
        _pulse1.ClockEnvelope();
        _pulse2.ClockEnvelope();
        _noise.ClockEnvelope();
        _triangle.ClockLinear();
    }

    private void HalfFrame()
    {
        _pulse1.ClockLength(); _pulse2.ClockLength(); _triangle.ClockLength(); _noise.ClockLength();
        _pulse1.ClockSweep(); _pulse2.ClockSweep();
    }

    private void Emit()
    {
        float mix = Mix();
        float outp = mix - _hpPrevIn + 0.995f * _hpPrevOut; // one-pole high-pass removes the DC offset
        _hpPrevIn = mix;
        _hpPrevOut = outp;
        if (_count < _buffer.Length)
            _buffer[_count++] = outp;
    }

    private float Mix()
    {
        int p1 = _pulse1.Output(), p2 = _pulse2.Output();
        int t = _triangle.Output(), n = _noise.Output(), d = _dmc.Output();
        double pulse = (p1 + p2) == 0 ? 0 : 95.88 / (8128.0 / (p1 + p2) + 100);
        double tnd = (t == 0 && n == 0 && d == 0) ? 0
            : 159.79 / (1.0 / (t / 8227.0 + n / 12241.0 + d / 22638.0) + 100);
        return (float)(pulse + tnd);
    }

    public void WriteRegister(ushort address, byte value)
    {
        switch (address)
        {
            case 0x4000: _pulse1.Write0(value); break;
            case 0x4001: _pulse1.Write1(value); break;
            case 0x4002: _pulse1.Write2(value); break;
            case 0x4003: _pulse1.Write3(value); break;
            case 0x4004: _pulse2.Write0(value); break;
            case 0x4005: _pulse2.Write1(value); break;
            case 0x4006: _pulse2.Write2(value); break;
            case 0x4007: _pulse2.Write3(value); break;
            case 0x4008: _triangle.Write0(value); break;
            case 0x400A: _triangle.Write2(value); break;
            case 0x400B: _triangle.Write3(value); break;
            case 0x400C: _noise.Write0(value); break;
            case 0x400E: _noise.Write2(value); break;
            case 0x400F: _noise.Write3(value); break;
            case 0x4010: _dmc.Write0(value); break;
            case 0x4011: _dmc.Write1(value); break;
            case 0x4012: _dmc.Write2(value); break;
            case 0x4013: _dmc.Write3(value); break;
            case 0x4015:
                _pulse1.SetEnabled((value & 1) != 0);
                _pulse2.SetEnabled((value & 2) != 0);
                _triangle.SetEnabled((value & 4) != 0);
                _noise.SetEnabled((value & 8) != 0);
                _dmc.SetEnabled((value & 0x10) != 0);
                _dmc.IrqPending = false; // writing $4015 clears the DMC interrupt flag
                break;
            case 0x4017:
                _mode5 = (value & 0x80) != 0;
                _irqInhibit = (value & 0x40) != 0;
                if (_irqInhibit) _frameIrq = false;
                _frameCycle = 0;
                if (_mode5) { QuarterFrame(); HalfFrame(); }
                break;
        }
    }

    public byte ReadStatus()
    {
        byte s = 0;
        if (_pulse1.LengthNonZero) s |= 0x01;
        if (_pulse2.LengthNonZero) s |= 0x02;
        if (_triangle.LengthNonZero) s |= 0x04;
        if (_noise.LengthNonZero) s |= 0x08;
        if (_dmc.Active) s |= 0x10;
        if (_frameIrq) s |= 0x40;
        if (_dmc.IrqPending) s |= 0x80;
        _frameIrq = false; // reading $4015 clears the frame IRQ (not the DMC IRQ)
        return s;
    }

    // --- Channels ---

    private sealed class Pulse(bool onesComplement)
    {
        private int _duty, _dutyPos, _timer, _timerReload, _length, _volume;
        private bool _halt, _constVol, _enabled;
        private bool _sweepEnabled, _sweepNegate, _sweepReload;
        private int _sweepPeriod, _sweepShift, _sweepDivider;
        private bool _envStart;
        private int _envDivider, _envDecay;

        public bool LengthNonZero => _length > 0;
        public void SetEnabled(bool on) { _enabled = on; if (!on) _length = 0; }

        public void Write0(byte v) { _duty = v >> 6; _halt = (v & 0x20) != 0; _constVol = (v & 0x10) != 0; _volume = v & 0x0F; }
        public void Write1(byte v) { _sweepEnabled = (v & 0x80) != 0; _sweepPeriod = (v >> 4) & 7; _sweepNegate = (v & 8) != 0; _sweepShift = v & 7; _sweepReload = true; }
        public void Write2(byte v) { _timerReload = (_timerReload & 0x700) | v; }
        public void Write3(byte v) { _timerReload = (_timerReload & 0xFF) | ((v & 7) << 8); if (_enabled) _length = LengthTable[v >> 3]; _dutyPos = 0; _envStart = true; }

        public void ClockTimer()
        {
            if (_timer == 0) { _timer = _timerReload; _dutyPos = (_dutyPos + 1) & 7; }
            else _timer--;
        }

        public void ClockEnvelope()
        {
            if (_envStart) { _envStart = false; _envDecay = 15; _envDivider = _volume; }
            else if (_envDivider == 0) { _envDivider = _volume; if (_envDecay > 0) _envDecay--; else if (_halt) _envDecay = 15; }
            else _envDivider--;
        }

        public void ClockLength() { if (!_halt && _length > 0) _length--; }

        public void ClockSweep()
        {
            if (_sweepDivider == 0 && _sweepEnabled && _sweepShift > 0 && !Muted())
            {
                int target = Target();
                if (target <= 0x7FF) _timerReload = target;
            }
            if (_sweepDivider == 0 || _sweepReload) { _sweepDivider = _sweepPeriod; _sweepReload = false; }
            else _sweepDivider--;
        }

        private int Target()
        {
            int change = _timerReload >> _sweepShift;
            if (_sweepNegate) { change = -change; if (onesComplement) change--; }
            return _timerReload + change;
        }

        private bool Muted() => _timerReload < 8 || Target() > 0x7FF;

        public int Output()
        {
            if (!_enabled || _length == 0 || Muted() || DutyTable[_duty][_dutyPos] == 0) return 0;
            return _constVol ? _volume : _envDecay;
        }
    }

    private sealed class Triangle
    {
        private int _timer, _timerReload, _seqPos, _length, _linearReload, _linear;
        private bool _control, _reloadFlag, _enabled;

        public bool LengthNonZero => _length > 0;
        public void SetEnabled(bool on) { _enabled = on; if (!on) _length = 0; }

        public void Write0(byte v) { _control = (v & 0x80) != 0; _linearReload = v & 0x7F; }
        public void Write2(byte v) { _timerReload = (_timerReload & 0x700) | v; }
        public void Write3(byte v) { _timerReload = (_timerReload & 0xFF) | ((v & 7) << 8); if (_enabled) _length = LengthTable[v >> 3]; _reloadFlag = true; }

        public void ClockTimer()
        {
            if (_timer == 0)
            {
                _timer = _timerReload;
                if (_length > 0 && _linear > 0) _seqPos = (_seqPos + 1) & 31;
            }
            else _timer--;
        }

        public void ClockLinear()
        {
            if (_reloadFlag) _linear = _linearReload;
            else if (_linear > 0) _linear--;
            if (!_control) _reloadFlag = false;
        }

        public void ClockLength() { if (!_control && _length > 0) _length--; }

        public int Output() => _timerReload < 2 ? 0 : TriangleSeq[_seqPos]; // mute ultrasonic to avoid pops
    }

    private sealed class Noise
    {
        private int _shift = 1, _timer, _timerReload, _length, _volume;
        private bool _mode, _halt, _constVol, _enabled, _envStart;
        private int _envDivider, _envDecay;

        public bool LengthNonZero => _length > 0;
        public void SetEnabled(bool on) { _enabled = on; if (!on) _length = 0; }

        public void Write0(byte v) { _halt = (v & 0x20) != 0; _constVol = (v & 0x10) != 0; _volume = v & 0x0F; }
        public void Write2(byte v) { _mode = (v & 0x80) != 0; _timerReload = NoisePeriods[v & 0x0F]; }
        public void Write3(byte v) { if (_enabled) _length = LengthTable[v >> 3]; _envStart = true; }

        public void ClockTimer()
        {
            if (_timer == 0)
            {
                _timer = _timerReload;
                int fb = (_shift & 1) ^ ((_shift >> (_mode ? 6 : 1)) & 1);
                _shift = (_shift >> 1) | (fb << 14);
            }
            else _timer--;
        }

        public void ClockEnvelope()
        {
            if (_envStart) { _envStart = false; _envDecay = 15; _envDivider = _volume; }
            else if (_envDivider == 0) { _envDivider = _volume; if (_envDecay > 0) _envDecay--; else if (_halt) _envDecay = 15; }
            else _envDivider--;
        }

        public void ClockLength() { if (!_halt && _length > 0) _length--; }

        public int Output()
        {
            if (!_enabled || _length == 0 || (_shift & 1) != 0) return 0;
            return _constVol ? _volume : _envDecay;
        }
    }

    /// <summary>
    /// Delta Modulation Channel: plays 1-bit delta-encoded PCM fetched from PRG memory via DMA. The
    /// output level moves ±2 per bit. Can loop and raise an IRQ when a non-looping sample finishes.
    /// (The DMA's CPU-stall cycles are not modeled — a scanline-accuracy omission, not an audio one.)
    /// </summary>
    private sealed class Dmc(Func<ushort, byte> read)
    {
        private static readonly int[] RateTable =
            [428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54];

        private bool _irqEnabled, _loop;
        private int _rate = 428, _timer = 428, _output;
        private ushort _sampleAddr = 0xC000, _curAddr;
        private int _sampleLen = 1, _bytesRemaining;
        private int _shift, _bitsRemaining = 8;
        private bool _bufferEmpty = true, _silence = true;
        private byte _sampleBuffer;

        public bool IrqPending;
        public int Stall; // CPU cycles the pending sample DMA stole (consumed by the machine)
        public bool Active => _bytesRemaining > 0;
        public int Output() => _output; // 0-127

        public void Write0(byte v)
        {
            _irqEnabled = (v & 0x80) != 0;
            _loop = (v & 0x40) != 0;
            _rate = RateTable[v & 0x0F];
            if (!_irqEnabled) IrqPending = false;
        }
        public void Write1(byte v) => _output = v & 0x7F;
        public void Write2(byte v) => _sampleAddr = (ushort)(0xC000 + v * 64);
        public void Write3(byte v) => _sampleLen = v * 16 + 1;

        public void SetEnabled(bool on)
        {
            if (!on) _bytesRemaining = 0;
            else if (_bytesRemaining == 0) { _curAddr = _sampleAddr; _bytesRemaining = _sampleLen; }
        }

        public void ClockTimer()
        {
            // Refill the sample buffer from memory when empty (the DMA, minus its stall cycles).
            if (_bufferEmpty && _bytesRemaining > 0)
            {
                _sampleBuffer = read(_curAddr);
                Stall += 4; // the sample DMA halts the CPU ~4 cycles while it reads the byte
                _bufferEmpty = false;
                _curAddr = _curAddr == 0xFFFF ? (ushort)0x8000 : (ushort)(_curAddr + 1);
                if (--_bytesRemaining == 0)
                {
                    if (_loop) { _curAddr = _sampleAddr; _bytesRemaining = _sampleLen; }
                    else if (_irqEnabled) IrqPending = true;
                }
            }

            if (--_timer > 0) return;
            _timer = _rate;

            if (!_silence)
            {
                if ((_shift & 1) != 0) { if (_output <= 125) _output += 2; }
                else { if (_output >= 2) _output -= 2; }
            }
            _shift >>= 1;
            if (--_bitsRemaining == 0)
            {
                _bitsRemaining = 8;
                if (_bufferEmpty) { _silence = true; }
                else { _silence = false; _shift = _sampleBuffer; _bufferEmpty = true; }
            }
        }
    }
}
