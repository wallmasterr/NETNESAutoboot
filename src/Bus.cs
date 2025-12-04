public class Bus : IBus{
    public CPU cpu;
    public PPU ppu;
    public APU apu;
    public Cartridge cartridge;

    public byte[] ram; //2KB RAM

    public Input input = new Input();

    public Bus(Cartridge cartridge) {
        this.cartridge = cartridge;
        cpu = new CPU(this);
        ppu = new PPU(this);
        apu = new APU(this);

        ram = new byte[2048];

        Console.WriteLine("Bus init");
    }

    public byte Read(ushort address) {
        if (address == 0x4016) {
            return input.Read4016(); //NES controller input
        }

        if (address == 0x4015) {
            return apu.ReadStatus();
        }

        if (address >= 0x2000 && address <= 0x3FFF) {
            ushort reg = (ushort)(0x2000 + (address & 0x0007));
            byte result = ppu.ReadPPURegister(reg);
            return result;
        } else if (address >= 0x0000 && address < 0x2000) {
            return ram[address & 0x07FF];
        } else if (address >= 0x6000 && address <= 0xFFFF) {
            return cartridge.CPURead(address);
        }

        return 0;
    }

    public void Write(ushort address, byte value) {
        if (address == 0x4016) {
            input.Write4016(value);
            return;
        }

        if (address == 0x4014) {
            ppu.WriteOAMDMA(value);
            return;
        }

        // APU registers
        if (address >= 0x4000 && address <= 0x4013) {
            switch (address) {
                case 0x4000: apu.SetPulseCtrl(apu.pulse1, value); break;
                case 0x4001: apu.SetPulseSweep(apu.pulse1, value); break;
                case 0x4002: apu.SetPulseTimer(apu.pulse1, value); break;
                case 0x4003: apu.SetPulseLengthCounter(apu.pulse1, value); break;
                case 0x4004: apu.SetPulseCtrl(apu.pulse2, value); break;
                case 0x4005: apu.SetPulseSweep(apu.pulse2, value); break;
                case 0x4006: apu.SetPulseTimer(apu.pulse2, value); break;
                case 0x4007: apu.SetPulseLengthCounter(apu.pulse2, value); break;
                case 0x4008: apu.SetTriCounter(value); break;
                case 0x4009: break; // Unused
                case 0x400A: apu.SetTriTimerLow(value); break;
                case 0x400B: apu.SetTriLength(value); break;
                case 0x400C: apu.SetNoiseCtrl(value); break;
                case 0x400D: break; // Unused
                case 0x400E: apu.SetNoisePeriod(value); break;
                case 0x400F: apu.SetNoiseLength(value); break;
                case 0x4010: apu.SetDmcCtrl(value); break;
                case 0x4011: apu.SetDmcDa(value); break;
                case 0x4012: apu.SetDmcAddr(value); break;
                case 0x4013: apu.SetDmcLength(value); break;
            }
            return;
        }

        if (address == 0x4015) {
            apu.SetStatus(value);
            return;
        }

        if (address == 0x4017) {
            apu.SetFrameCounterCtrl(value);
            return;
        }

        if (address >= 0x2000 && address <= 0x3FFF) {
            ushort reg = (ushort)(0x2000 + (address & 0x0007));
            ppu.WritePPURegister(reg, value);
        } else if (address >= 0x0000 && address < 0x2000) {
            ram[address & 0x07FF] = value;
        } else if (address >= 0x6000 && address <= 0xFFFF) {
            cartridge.CPUWrite(address, value);
        }
    }
}