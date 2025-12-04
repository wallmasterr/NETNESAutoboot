using NAudio.Wave;

public class NES {
    Cartridge cartridge;
    Bus bus;
    private WaveOutEvent? waveOut;
    private BufferedWaveProvider? waveProvider;
    private bool audioInitialized = false;

    public NES() {
        cartridge = new Cartridge(Helper.romPath);
        bus = new Bus(cartridge);

        bus.cpu.Reset();
        
        // Initialize audio
        InitAudio();
        
        Console.WriteLine("NES");
    }

    private void InitAudio() {
        try {
            // Create wave format: 48000 Hz, 16-bit, mono
            WaveFormat waveFormat = new WaveFormat(48000, 16, 1);
            
            // Create buffered wave provider
            waveProvider = new BufferedWaveProvider(waveFormat) {
                BufferLength = 48000 * 2 * 2, // 2 seconds of audio
                DiscardOnBufferOverflow = true
            };

            // Create and start wave out
            waveOut = new WaveOutEvent();
            waveOut.Init(waveProvider);
            waveOut.Play();
            
            audioInitialized = true;
            Console.WriteLine("Audio initialized");
        } catch (Exception ex) {
            Console.WriteLine($"Audio initialization error: {ex.Message}");
            audioInitialized = false;
        }
    }

    public void Run() {
        int cycles = 0;

        bus.input.UpdateController();

        while (cycles < 29828) {
            int used = bus.cpu.ExecuteInstruction();
            cycles += used;
            
            // APU runs at CPU speed (once per CPU cycle)
            for (int i = 0; i < used; i++) {
                bus.apu.Execute();
            }
            
            // PPU runs at 3x CPU speed
            bus.ppu.Step(used * 3);
        }

        // Update audio output
        UpdateAudio();

        bus.ppu.DrawFrame(Helper.scale);
    }

    private void UpdateAudio() {
        if (!audioInitialized || waveProvider == null) {
            return;
        }

        short[] audioBuffer = bus.apu.GetAudioBuffer();
        uint bufferIndex = bus.apu.GetBufferIndex();

        if (bufferIndex > 0) {
            // Convert short[] to byte[] for NAudio
            byte[] audioBytes = new byte[bufferIndex * 2];
            Buffer.BlockCopy(audioBuffer, 0, audioBytes, 0, (int)(bufferIndex * 2));
            
            // Add to wave provider
            waveProvider.AddSamples(audioBytes, 0, audioBytes.Length);

            bus.apu.ResetBuffer();
        }
    }

    public void Cleanup() {
        if (audioInitialized) {
            waveOut?.Stop();
            waveOut?.Dispose();
            waveOut = null;
            waveProvider = null;
            audioInitialized = false;
        }
    }
}