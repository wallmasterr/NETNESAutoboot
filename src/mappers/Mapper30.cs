public class Mapper30 : IMapper { //UNROM 512
    private Cartridge cartridge;
    private byte prgBank;
    private bool mirrorVertical;

    public Mapper30(Cartridge cart) {
        cartridge = cart;
        prgBank = 0;
        mirrorVertical = false;
    }

    public void Reset() {
        prgBank = 0;
        mirrorVertical = false;
        cartridge.SetMirroring(Mirroring.Horizontal);
    }

    public byte CPURead(ushort addr) {
        if (addr >= 0x8000 && addr <= 0xBFFF) {
            // Swappable bank (16KB)
            int bankCount = cartridge.prgROM.Length / 0x4000;
            int selectedBank = prgBank % Math.Max(1, bankCount);
            int index = (selectedBank * 0x4000) + (addr - 0x8000);
            return index < cartridge.prgROM.Length ? cartridge.prgROM[index] : (byte)0xFF;
        } else if (addr >= 0xC000 && addr <= 0xFFFF) {
            // Fixed to last bank (16KB)
            int fixedBankStart = cartridge.prgROM.Length - 0x4000;
            int index = fixedBankStart + (addr - 0xC000);
            return index < cartridge.prgROM.Length ? cartridge.prgROM[index] : (byte)0xFF;
        }
        return 0;
    }

    public void CPUWrite(ushort addr, byte val) {
        if (addr >= 0x8000) {
            // Lower 5 bits select PRG bank (0-31, supports up to 512KB)
            prgBank = (byte)(val & 0x1F);
            
            // Bit 6 controls mirroring (0 = horizontal, 1 = vertical)
            bool newMirrorVertical = (val & 0x40) != 0;
            if (newMirrorVertical != mirrorVertical) {
                mirrorVertical = newMirrorVertical;
                cartridge.SetMirroring(mirrorVertical ? Mirroring.Vertical : Mirroring.Horizontal);
            }
        }
    }

    public byte PPURead(ushort addr) {
        if (addr < 0x2000) {
            if (cartridge.chrBanks == 0) {
                // CHR RAM
                return cartridge.chrRAM[addr];
            }
            // CHR ROM (up to 256KB, 32 banks of 8KB)
            return cartridge.chrROM[addr % cartridge.chrROM.Length];
        }
        return 0;
    }

    public void PPUWrite(ushort addr, byte val) {
        if (cartridge.chrBanks == 0 && addr < 0x2000) {
            // CHR RAM
            cartridge.chrRAM[addr] = val;
        }
    }
}

