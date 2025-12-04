public class APU {
    private const int SAMPLING_FREQUENCY = 48000;
    private const int AUDIO_BUFF_SIZE = 1024;
    private const int STATS_WIN_SIZE = 20;
    private const int NOMINAL_QUEUE_SIZE = 6000;
    private const int TND_LUT_SIZE = 203;
    private const int PULSE_LUT_SIZE = 31;

    private const int TIMER_HIGH = 0x7;
    private const int PULSE_SHIFT = 0x7;
    private const int PULSE_PERIOD = 0b01110000;

    private Bus bus;
    private CPU cpu;

    // Lookup tables
    private static float[]? pulse_LUT;
    private static float[]? tnd_LUT;
    private static bool lutInitialized = false;

    // Length counter lookup
    private static readonly byte[] lengthCounterLookup = {
        10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14,
        12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30
    };

    // Duty cycles
    private static readonly byte[][] duty = {
        new byte[] {0, 1, 0, 0, 0, 0, 0, 0}, // 12.5%
        new byte[] {0, 1, 1, 0, 0, 0, 0, 0}, // 25%
        new byte[] {0, 1, 1, 1, 1, 0, 0, 0}, // 50%
        new byte[] {1, 0, 0, 1, 1, 1, 1, 1}  // 25% negated
    };

    // Triangle sequence
    private static readonly byte[] triSequence = {
        15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
    };

    // Noise period lookups
    private static readonly ushort[] noisePeriodLookupNTSC = {
        4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068
    };

    private static readonly ushort[] noisePeriodLookupPAL = {
        4, 8, 14, 30, 60, 88, 118, 148, 188, 236, 354, 472, 708, 944, 1890, 3778
    };

    // DMC rate lookups
    private static readonly ushort[] dmcRateIndexNTSC = {
        428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54
    };

    private static readonly ushort[] dmcRateIndexPAL = {
        398, 354, 316, 298, 276, 236, 210, 198, 176, 148, 132, 118, 98, 78, 66, 50
    };

    // Audio buffer
    private short[] buff = new short[AUDIO_BUFF_SIZE];
    private uint[] statWindow = new uint[STATS_WIN_SIZE];

    // Channels
    public PulseChannel pulse1;
    public PulseChannel pulse2;
    public TriangleChannel triangle;
    public NoiseChannel noise;
    public DMCChannel dmc;

    // Sampler
    private Sampler sampler = null!;
    private Biquad filter = null!;
    private Biquad aaFilter = null!;

    // Frame sequencer
    public byte frameMode;
    public byte status;
    public bool irqInhibit;
    public bool frameInterrupt;
    public bool audioStart;
    public bool resetSequencer;
    public uint cycles;
    public uint sequencer;
    public float volume = 1.0f;


    public APU(Bus bus) {
        this.bus = bus;
        this.cpu = bus.cpu;

        if (!lutInitialized) {
            ComputeMixerLUT();
            lutInitialized = true;
        }

        pulse1 = new PulseChannel(1);
        pulse2 = new PulseChannel(2);
        triangle = new TriangleChannel();
        noise = new NoiseChannel();
        dmc = new DMCChannel(bus);

        InitSampler();
        Reset();
    }

    private static void ComputeMixerLUT() {
        pulse_LUT = new float[PULSE_LUT_SIZE];
        tnd_LUT = new float[TND_LUT_SIZE];

        pulse_LUT[0] = 0;
        for (int i = 1; i < PULSE_LUT_SIZE; i++) {
            pulse_LUT[i] = 95.52f / (8128.0f / i + 100);
        }

        tnd_LUT[0] = 0;
        for (int i = 1; i < TND_LUT_SIZE; i++) {
            tnd_LUT[i] = 163.67f / (24329.0f / i + 100);
        }
    }

    public void Reset() {
        SetStatus(0);
        triangle.sequencer.step = 0;
        dmc.counter &= 1;
        frameInterrupt = false;
        cpu.RequestIRQ(false);
    }

    private void InitSampler() {
        // Assume NTSC for now - can be adjusted based on TV system
        float cyclesPerFrame = 29780.5f;
        float rate = 60.0f;

        filter = new Biquad();
        filter.Init(Biquad.HPF, 0, 20, SAMPLING_FREQUENCY, 1);

        aaFilter = new Biquad();
        aaFilter.Init(Biquad.LPF, 0, 20000, cyclesPerFrame * rate, 1);

        sampler = new Sampler {
            maxPeriod = (uint)(cyclesPerFrame * rate / SAMPLING_FREQUENCY),
            minPeriod = 0,
            period = 0,
            index = 0,
            maxIndex = AUDIO_BUFF_SIZE,
            samples = 0,
            counter = 0,
            factorIndex = 0,
            maxFactor = 100,
            targetFactor = 48,
            equilibriumFactor = 48
        };
        sampler.minPeriod = sampler.maxPeriod - 1;
        sampler.period = sampler.minPeriod;
    }

    public void Execute() {
        // Handle sequencer reset
        if (resetSequencer) {
            resetSequencer = false;
            if (frameMode == 1) {
                QuarterFrame();
                HalfFrame();
            }
            sequencer = 0;
            goto postSequencer;
        }

        // Frame sequencer (NTSC timing - can add PAL later)
        switch (sequencer) {
            case 0:
                sequencer++;
                break;
            case 7457:
                QuarterFrame();
                sequencer++;
                break;
            case 14913:
                QuarterFrame();
                HalfFrame();
                sequencer++;
                break;
            case 22371:
                QuarterFrame();
                sequencer++;
                break;
            case 29828:
                sequencer++;
                break;
            case 29829:
                if (frameMode == 1) {
                    sequencer++;
                    break;
                }
                QuarterFrame();
                HalfFrame();
                if (!irqInhibit) {
                    frameInterrupt = true;
                    cpu.RequestIRQ(true);
                }
                sequencer = 0;
                break;
            case 37281:
                QuarterFrame();
                HalfFrame();
                sequencer = 0;
                break;
            default:
                sequencer++;
                break;
        }

    postSequencer:

        // Channel sequencer (every other cycle)
        if ((cycles & 1) != 0) {
            ClockDivider(pulse1.t);
            ClockDivider(pulse2.t);

            // Noise timer
            if (ClockDivider(noise.timer)) {
                byte feedback = (byte)((noise.shift & 1) ^ (((noise.mode ? 0x40 : 0x02) & noise.shift) > 0 ? 1 : 0));
                noise.shift >>= 1;
                noise.shift |= (ushort)(feedback != 0 ? (1 << 14) : 0);
            }
        }

        // DMC
        ClockDMC();

        // Triangle timer
        ClockTriangle();

        // Sample
        Sample();

        cycles++;
    }

    private void QuarterFrame() {
        // Envelope
        ClockDividerInverse(pulse1.envelope);
        ClockDividerInverse(pulse2.envelope);
        ClockDividerInverse(noise.envelope);

        // Triangle linear counter
        if (triangle.linearReloadFlag) {
            triangle.linearCounter = triangle.linearReload;
        } else if (triangle.linearCounter > 0) {
            triangle.linearCounter--;
        }
        triangle.linearReloadFlag = triangle.halt ? triangle.linearReloadFlag : false;
    }

    private void HalfFrame() {
        // Length and sweep
        LengthSweepPulse(pulse1);
        LengthSweepPulse(pulse2);

        // Triangle length counter
        if (!triangle.halt && triangle.lengthCounter > 0) {
            triangle.lengthCounter--;
        }

        // Noise length counter
        if (noise.l > 0 && !noise.envelope.loop) {
            noise.l--;
        }
    }

    private void Sample() {
        float sample = (float)aaFilter.Process(GetSample());
        sampler.counter++;

        if (sampler.counter >= sampler.period) {
            buff[sampler.index++] = (short)(32000 * (float)filter.Process(sample) * volume);
            if (sampler.index >= sampler.maxIndex) {
                sampler.index = 0;
            }
            sampler.samples++;
            sampler.counter = 0;

            if (sampler.factorIndex <= sampler.targetFactor) {
                sampler.period = sampler.maxPeriod;
            } else {
                sampler.period = sampler.minPeriod;
            }
            sampler.factorIndex++;
            if (sampler.factorIndex > sampler.maxFactor) {
                sampler.factorIndex = 0;
            }
        }
    }

    private float GetSample() {
        byte pulseOut = 0;
        byte tndOut = 0;

        if (pulse1.enabled && pulse1.l > 0 && !pulse1.mute) {
            byte vol = pulse1.constVolume ? (byte)pulse1.envelope.period : pulse1.envelope.step;
            pulseOut += (byte)(vol * duty[pulse1.duty][pulse1.t.step]);
        }

        if (pulse2.enabled && pulse2.l > 0 && !pulse2.mute) {
            byte vol = pulse2.constVolume ? (byte)pulse2.envelope.period : pulse2.envelope.step;
            pulseOut += (byte)(vol * duty[pulse2.duty][pulse2.t.step]);
        }

        if (triangle.enabled && triangle.sequencer.period > 1) {
            tndOut += (byte)(triSequence[triangle.sequencer.step] * 3);
        }

        if (noise.enabled && (noise.shift & 1) == 0 && noise.l > 0) {
            byte vol = noise.constVolume ? (byte)noise.envelope.period : noise.envelope.step;
            tndOut += (byte)(2 * vol);
        }

        tndOut += dmc.counter;

        if (pulse_LUT == null || tnd_LUT == null) return 0.0f;
        float amp = pulse_LUT[pulseOut] + tnd_LUT[tndOut];
        return amp > 1.0f ? 1.0f : amp;
    }

    public void SetStatus(byte value) {
        pulse1.enabled = (value & 0x01) != 0;
        pulse2.enabled = (value & 0x02) != 0;
        triangle.enabled = (value & 0x04) != 0;
        noise.enabled = (value & 0x08) != 0;
        dmc.enabled = (value & 0x10) != 0;

        if (dmc.enabled && dmc.bytesRemaining == 0) {
            dmc.bytesRemaining = dmc.sampleLength;
            dmc.currentAddr = dmc.sampleAddr;
        } else if (!dmc.enabled) {
            dmc.bytesRemaining = 0;
        }
        dmc.interrupt = false;
        cpu.RequestIRQ(false);

        // Reset length counters if disabled
        if (!pulse1.enabled) pulse1.l = 0;
        if (!pulse2.enabled) pulse2.l = 0;
        if (!triangle.enabled) triangle.lengthCounter = 0;
        if (!noise.enabled) noise.l = 0;

        status = value;
    }

    public byte ReadStatus() {
        byte result = (byte)((pulse1.l > 0 ? 0x01 : 0) |
                             (pulse2.l > 0 ? 0x02 : 0) |
                             (triangle.lengthCounter > 0 ? 0x04 : 0) |
                             (noise.l > 0 ? 0x08 : 0) |
                             (frameInterrupt ? 0x40 : 0) |
                             (dmc.interrupt ? 0x80 : 0) |
                             (dmc.bytesRemaining > 0 ? 0x10 : 0));

        frameInterrupt = false;
        cpu.RequestIRQ(false);
        return result;
    }

    public void SetFrameCounterCtrl(byte value) {
        irqInhibit = (value & 0x40) != 0;
        frameMode = (byte)((value & 0x80) != 0 ? 1 : 0);

        if (irqInhibit) {
            frameInterrupt = false;
            cpu.RequestIRQ(false);
        }
        resetSequencer = true;
    }

    // Pulse channel methods
    public void SetPulseCtrl(PulseChannel pulse, byte value) {
        pulse.constVolume = (value & 0x10) != 0;
        pulse.envelope.loop = (value & 0x20) != 0;
        pulse.envelope.period = (byte)(value & 0x0F);
        pulse.envelope.counter = pulse.envelope.period;
        pulse.envelope.step = 15;
        pulse.duty = (byte)(value >> 6);
    }

    public void SetPulseTimer(PulseChannel pulse, byte value) {
        pulse.t.period = (ushort)((pulse.t.period & 0xFF00) | value);
        UpdateTargetPeriod(pulse);
    }

    public void SetPulseSweep(PulseChannel pulse, byte value) {
        pulse.enableSweep = (value & 0x80) != 0;
        pulse.sweep.period = (ushort)(((value & PULSE_PERIOD) >> 4) + 1);
        pulse.sweep.counter = pulse.sweep.period;
        pulse.shift = (byte)(value & PULSE_SHIFT);
        pulse.neg = (value & 0x08) != 0;
        pulse.sweepReload = true;
        UpdateTargetPeriod(pulse);
    }

    public void SetPulseLengthCounter(PulseChannel pulse, byte value) {
        pulse.t.period = (ushort)((pulse.t.period & 0x00FF) | ((value & 0x07) << 8));
        if (pulse.enabled) {
            pulse.l = lengthCounterLookup[value >> 3];
        }
        UpdateTargetPeriod(pulse);
        pulse.envelope.step = 15;
    }

    // Triangle channel methods
    public void SetTriCounter(byte value) {
        triangle.linearReload = (byte)(value & 0x7F);
        triangle.halt = (value & 0x80) != 0;
    }

    public void SetTriTimerLow(byte value) {
        triangle.sequencer.period = (ushort)((triangle.sequencer.period & 0xFF00) | value);
    }

    public void SetTriLength(byte value) {
        triangle.sequencer.period = (ushort)((triangle.sequencer.period & 0x00FF) | ((value & 0x07) << 8));
        triangle.linearReloadFlag = true;
        if (triangle.enabled) {
            triangle.lengthCounter = lengthCounterLookup[value >> 3];
        }
    }

    // Noise channel methods
    public void SetNoiseCtrl(byte value) {
        noise.constVolume = (value & 0x10) != 0;
        noise.envelope.loop = (value & 0x20) != 0;
        noise.envelope.period = (byte)(value & 0x0F);
    }

    public void SetNoisePeriod(byte value) {
        // Assume NTSC for now
        noise.timer.period = noisePeriodLookupNTSC[value & 0x0F];
        noise.mode = (value & 0x80) != 0;
    }

    public void SetNoiseLength(byte value) {
        if (noise.enabled) {
            noise.l = lengthCounterLookup[value >> 3];
        }
        noise.envelope.step = 15;
    }

    // DMC channel methods
    public void SetDmcCtrl(byte value) {
        dmc.loop = (value & 0x40) != 0;
        dmc.irqEnable = (value & 0x80) != 0;
        if (!dmc.irqEnable) {
            dmc.interrupt = false;
            cpu.RequestIRQ(false);
        }
        // Assume NTSC for now
        dmc.rate = (ushort)(dmcRateIndexNTSC[value & 0x0F] - 1);
    }

    public void SetDmcDa(byte value) {
        dmc.counter = (byte)(value & 0x7F);
    }

    public void SetDmcAddr(byte value) {
        dmc.sampleAddr = (ushort)(0xC000 + value * 64);
    }

    public void SetDmcLength(byte value) {
        dmc.sampleLength = (ushort)(value * 16 + 1);
    }

    // Helper methods
    private bool ClockDivider(Divider divider) {
        if (divider.counter > 0) {
            divider.counter--;
            return false;
        }

        divider.counter = divider.period;
        divider.step++;
        if (divider.limit > 0 && divider.step > divider.limit) {
            divider.step = divider.from;
        }
        return true;
    }

    private bool ClockTriangle() {
        Divider divider = triangle.sequencer;
        if (divider.counter > 0) {
            divider.counter--;
            return false;
        }

        divider.counter = divider.period;
        if (triangle.lengthCounter > 0 && triangle.linearCounter > 0) {
            divider.step++;
        }
        if (divider.limit > 0 && divider.step > divider.limit) {
            divider.step = divider.from;
        }
        return true;
    }

    private bool ClockDividerInverse(Divider divider) {
        if (divider.counter > 0) {
            divider.counter--;
            return false;
        }
        divider.counter = divider.period;
        if (divider.limit > 0 && divider.step == 0 && divider.loop) {
            divider.step = divider.limit;
        } else if (divider.step > 0) {
            divider.step--;
        }
        return true;
    }

    private void UpdateTargetPeriod(PulseChannel pulse) {
        int change = pulse.t.period >> pulse.shift;
        change = pulse.neg ? (pulse.id == 1 ? -change - 1 : -change) : change;
        change = pulse.t.period + change;
        pulse.targetPeriod = (ushort)(change < 0 ? 0 : change);
        pulse.mute = pulse.t.period < 8 || pulse.targetPeriod > 0x7FF;
    }

    private void LengthSweepPulse(PulseChannel pulse) {
        if (pulse.sweepReload) {
            pulse.sweepReload = false;
            pulse.sweep.counter = 0;
        }

        if (ClockDivider(pulse.sweep)) {
            if (pulse.enableSweep && pulse.shift > 0 && !pulse.mute) {
                pulse.t.period = pulse.targetPeriod;
                UpdateTargetPeriod(pulse);
            }
        }

        if (pulse.l > 0 && !pulse.envelope.loop) {
            pulse.l--;
        }
    }

    private void ClockDMC() {
        if (dmc.enabled && dmc.empty) {
            if (dmc.bytesRemaining > 0) {
                // TODO: Implement DMA halt (3 cycles)
                dmc.sample = bus.Read(dmc.currentAddr);
                dmc.empty = false;
                dmc.bytesRemaining--;
                if (dmc.currentAddr == 0xFFFF) {
                    dmc.currentAddr = 0x8000;
                } else {
                    dmc.currentAddr++;
                }
                dmc.irqSet = false;
                cpu.RequestIRQ(false);
            }
            if (dmc.bytesRemaining == 0) {
                if (dmc.loop) {
                    dmc.currentAddr = dmc.sampleAddr;
                    dmc.bytesRemaining = dmc.sampleLength;
                } else if (dmc.irqEnable && !dmc.irqSet) {
                    dmc.interrupt = true;
                    dmc.irqSet = true;
                    cpu.RequestIRQ(true);
                }
            }
        }

        if (dmc.rateIndex > 0) {
            dmc.rateIndex--;
            return;
        }
        dmc.rateIndex = dmc.rate;

        if (dmc.bitsRemaining > 0) {
            if (!dmc.silence) {
                if ((dmc.bits & 1) != 0) {
                    dmc.counter += 2;
                    if (dmc.counter > 127) dmc.counter = 127;
                } else if (dmc.counter > 1) {
                    dmc.counter -= 2;
                }
                dmc.bits >>= 1;
            }
            dmc.bitsRemaining--;
        }
        if (dmc.bitsRemaining == 0) {
            if (dmc.empty) {
                dmc.silence = true;
            } else {
                dmc.bits = dmc.sample;
                dmc.empty = true;
                dmc.silence = false;
            }
            dmc.bitsRemaining = 8;
        }
    }

    public short[] GetAudioBuffer() {
        return buff;
    }

    public uint GetBufferIndex() {
        return sampler.index;
    }

    public void ResetBuffer() {
        Array.Clear(buff, 0, buff.Length);
        sampler.index = 0;
    }
}

// Data structures
public class Divider {
    public ushort period;
    public ushort counter;
    public byte step;
    public byte limit;
    public byte from;
    public bool loop;
}

public class PulseChannel {
    public Divider t;
    public byte l; // length counter
    public byte id;
    public bool neg;
    public byte shift;
    public Divider sweep;
    public bool enableSweep;
    public byte duty;
    public bool constVolume;
    public Divider envelope;
    public bool envelopeLoop;
    public bool enabled;
    public bool mute;
    public ushort targetPeriod;
    public bool sweepReload;

    public PulseChannel(byte id) {
        this.id = id;
        t = new Divider { step = 0, from = 0, limit = 7, loop = true };
        sweep = new Divider { limit = 0 };
        envelope = new Divider();
        enabled = false;
        sweepReload = false;
    }
}

public class TriangleChannel {
    public Divider sequencer;
    public byte lengthCounter;
    public byte linearReload;
    public byte linearCounter;
    public bool linearReloadFlag;
    public bool halt;
    public bool enabled;

    public TriangleChannel() {
        sequencer = new Divider { step = 0, limit = 31, from = 0 };
        enabled = false;
        halt = true;
    }
}

public class NoiseChannel {
    public Divider timer;
    public bool mode;
    public byte l;
    public ushort shift;
    public bool constVolume;
    public Divider envelope;
    public bool envelopeLoop;
    public bool enabled;

    public NoiseChannel() {
        timer = new Divider { limit = 0 };
        shift = 1;
        enabled = false;
        envelope = new Divider();
    }
}

public class DMCChannel {
    public bool enabled;
    public bool irqEnable;
    public bool loop;
    public byte counter;
    public ushort sampleLength;
    public ushort sampleAddr;
    public bool interrupt;
    public bool irqSet;
    public ushort rate;
    public ushort rateIndex;
    public byte bitsRemaining;
    public bool silence;
    public byte bits;
    public byte sample;
    public bool empty;
    public ushort bytesRemaining;
    public ushort currentAddr;

    private Bus bus;

    public DMCChannel(Bus bus) {
        this.bus = bus;
        empty = true;
        silence = true;
    }
}

public class Sampler {
    public ushort factorIndex;
    public ushort targetFactor;
    public ushort equilibriumFactor;
    public ushort maxFactor;
    public uint samples;
    public uint maxPeriod;
    public uint minPeriod;
    public uint period;
    public uint counter;
    public uint index;
    public uint maxIndex;
}

