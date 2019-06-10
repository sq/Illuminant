using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Illuminant.Particles;
using Newtonsoft.Json;
using System.IO;
using Newtonsoft.Json.Linq;
using Microsoft.Xna.Framework;
using System.Runtime.Serialization;
using System.Collections;
using System.Reflection;
using Squared.Illuminant.Configuration;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Illuminant.Modeling {
    public partial class EngineModel {
        private string GetSystemName (SystemModel sm, int index) {
            var name = (sm.Name ?? "").Replace(" ", "").Replace("-", "");
            name = name.Substring(0, 1).ToUpper().Substring(1);
            if (String.IsNullOrWhiteSpace(name)) {
                name = "System" + index;
            }
            return name;
        }

        private void WriteCodeHeader (TextWriter tw, string name) {
            tw.WriteLine(
@"using System;
using System.Collections.Generic;
using Squared.Illuminant;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;

namespace Squared.Illuminant.Compiled {{
    public class @{0} : IDisposable {{
        public bool IsDisposed {{ get; private set; }}

        public readonly ParticleEngine Engine;
        public readonly Dictionary<string, object> NamedVariables;
        public readonly Dictionary<string, object> UserData;
", name
            );

            int i = 0;
            foreach (var s in Systems) {
                tw.WriteLine("        public readonly ParticleSystem {0};", GetSystemName(s, i++));
            }

            tw.WriteLine(
@"
        public @{0} (ParticleEngine engine) {{
            ParticleSystem s;
            Engine = engine;", name
            );
        }

        private void WriteCodeFooter (TextWriter tw) {
            tw.WriteLine(
@"
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
"
            );

            int i = 0;
            foreach (var s in Systems) {
                tw.WriteLine("            {0}.Dispose();", GetSystemName(s, i++));
            }

            tw.WriteLine(
@"        }
    }
}
"
            );
        }

        private void WriteConfiguration (TextWriter tw) {
        }

        private void WriteSystems (TextWriter tw) {
            int i = 0;
            foreach (var s in Systems) {
                var name = GetSystemName(s, i++);

                WriteSystem(tw, s, name);
            }
        }

        private void WriteSystem (TextWriter tw, SystemModel s, string name) {
            tw.WriteLine(
@"            
            var {0}Configuration = new ParticleSystemConfiguration {{
            }};

            s = {0} = new ParticleSystem(engine, {0}Configuration);", name
            );

            WriteTransforms(tw, s);
        }

        private void WriteTransforms (TextWriter tw, SystemModel s) {
            foreach (var t in s.Transforms) {
                WriteTransform(tw, s, t);
            }
        }

        private void WriteTransform (TextWriter tw, SystemModel s, TransformModel t) {
            tw.WriteLine(
@"
            s.Transforms.Add(new {0} {{
            }});", t.Type.FullName
            );
        }

        private void WriteUserData (TextWriter tw) {
        }

        private void WriteNamedVariables (TextWriter tw) {
        }
    }
}
