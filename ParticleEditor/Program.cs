using System;

namespace Lumined {
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            using (EditorGame game = new EditorGame())
            {
                game.Run();
            }
        }
    }
}

