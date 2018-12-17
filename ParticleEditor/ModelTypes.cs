using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Squared.Render;

namespace ParticleEditor {
    public partial class ParticleEditor : MultithreadedGame, INuklearHost {
        public static readonly Dictionary<string, Dictionary<string, string>> FieldTypeOverrides =
            new Dictionary<string, Dictionary<string, string>> {
                {
                    "ParticleSystemConfiguration", new Dictionary<string, string> {
                        {"GlobalColor", "ColorF" }
                    }
                }
            };
    }
}
