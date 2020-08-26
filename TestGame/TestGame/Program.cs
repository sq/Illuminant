using System;
using System.Linq;

namespace TestGame {
    static class Program {
        [STAThread]
        static void Main (string[] args) {
            if (args.Contains("/?") || args.Contains("-?") || args.Contains("--help")) {
                Console.WriteLine("Pass --scene:<name> to select an initial scene, from the following list:");
                foreach (var t in TestGame.SceneTypes)
                    Console.WriteLine(t.Name);
                return;
            }

            using (TestGame game = new TestGame())
                game.Run();
        }
    }
}