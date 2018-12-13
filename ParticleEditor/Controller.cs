using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ParticleEditor {
    public class Controller {
        [StructLayout(LayoutKind.Sequential)]
        public class State {
            public uint SystemListX, SystemListY;
            public int SelectedSystemIndex;
        }

        public Model Model;
        public readonly State CurrentState = new State();

        private GCHandle StatePin;

        public Controller (Model model) {
            Model = model;
            StatePin = GCHandle.Alloc(CurrentState, GCHandleType.Pinned);
        }
    }
}
