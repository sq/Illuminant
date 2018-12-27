using System;

namespace TestGame {
    static class Program {
        [STAThread]
        static void Main (string[] args) {
            using (TestGame game = new TestGame())
                game.Run();
        }
    }
}