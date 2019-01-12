using System;

namespace Lumined {
#if WINDOWS || XBOX
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
#endif
}

