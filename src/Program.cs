public class Program {
    public static void Main(string[] args) {
        Console.WriteLine("NET-NES");

        Helper.Flags(args);

        // Auto-boot game.nes from res folder if no ROM was specified
        if (Helper.mode == 1 && string.IsNullOrEmpty(Helper.romPath)) {
            string gamePath = Path.Combine(AppContext.BaseDirectory, "res", "game.nes");
            if (File.Exists(gamePath)) {
                Helper.romPath = gamePath;
                Helper.insertingRom = true;
                Console.WriteLine($"Auto-loading: {gamePath}");
            }
        }

        if (Helper.mode == 1) {
           GUI gui = new GUI();

           gui.Run();
        } else if (Helper.mode == 2) {
            TestRunner testRunner = new TestRunner();
            
            testRunner.Run(Helper.jsonPath);
        }
    }
}