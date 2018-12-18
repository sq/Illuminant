using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Illuminant.Particles;

namespace ParticleEditor {
    public class Model {
        public string Filename { get; private set; }
        public readonly List<ParticleSystemModel> Systems = new List<ParticleSystemModel>();

        public Model () {
            Filename = "untitled";
        }

        public void Normalize () {
            foreach (var s in Systems)
                s.Normalize();
        }
    }

    public class ParticleSystemModel {
        public string Name;
        public ParticleSystemConfiguration Configuration;
        public readonly List<ParticleTransformModel> Transforms = new List<ParticleTransformModel>();

        public void Normalize () {
            foreach (var t in Transforms)
                t.Normalize();
        }
    }

    public class ParticleTransformModel {
        public string Name;
        public Type Type;
        public readonly Dictionary<string, ModelProperty> Properties = new Dictionary<string, ModelProperty>();

        public void Normalize () {
            foreach (var kvp in Properties)
                kvp.Value.Normalize();
        }
    }

    public class ModelProperty {
        public Type Type;
        public object Value;

        public static ModelProperty New (object value) {
            if (value == null)
                return null;

            return new ModelProperty {
                Type = value.GetType(),
                Value = value
            };
        }

        public void Normalize () {
            var s = Value as string;
            if (s == null)
                return;
            if (Type == typeof(string))
                return;

            Value = Newtonsoft.Json.JsonConvert.DeserializeObject(s, Type, new XnaJsonConverter());
        }
    }
}
