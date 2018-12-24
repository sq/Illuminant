using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Illuminant.Particles;
using Newtonsoft.Json;
using System.IO;

namespace ParticleEditor {
    public class Model {
        public string Filename { get; private set; }
        public readonly List<ParticleSystemModel> Systems = new List<ParticleSystemModel>();

        public Model () {
            Filename = null;
        }

        public void Normalize (bool forSave) {
            foreach (var s in Systems)
                s.Normalize(forSave);
        }

        public static Model Load (string fileName) {
            using (var reader = new System.IO.StreamReader(fileName, Encoding.UTF8, false)) {
                var serializer = new JsonSerializer {
                    Converters = {
                        new XnaJsonConverter()
                    },
                    Formatting = Formatting.Indented
                };
                using (var jreader = new JsonTextReader(reader)) {
                    var result = serializer.Deserialize<Model>(jreader);
                    if (result != null)
                        result.Filename = Path.GetFullPath(fileName);
                    return result;
                }
            }
        }

        public void Save (string fileName) {
            Normalize(true);

            var tempPath = Path.GetTempFileName();
            using (var writer = new StreamWriter(tempPath, false, Encoding.UTF8)) {
                var serializer = new JsonSerializer {
                    Converters = {
                        new XnaJsonConverter()
                    },
                    Formatting = Formatting.Indented
                };
                serializer.Serialize(writer, this);
            }
            File.Copy(tempPath, fileName, true);
            Filename = Path.GetFullPath(fileName);
        }
    }

    public class ParticleSystemModel {
        public string Name;
        public ParticleSystemConfiguration Configuration;
        public readonly List<ParticleTransformModel> Transforms = new List<ParticleTransformModel>();

        public void Normalize (bool forSave) {
            foreach (var t in Transforms)
                t.Normalize(forSave);
        }
    }

    public class ParticleTransformModel {
        public string Name;
        public Type Type;
        public readonly Dictionary<string, ModelProperty> Properties = new Dictionary<string, ModelProperty>();

        public void Normalize (bool forSave) {
            var keys = Properties.Keys.ToList();
            foreach (var key in keys) {
                var m = Type.GetMember(key).FirstOrDefault();
                if (m == null) {
                    if (forSave)
                        Properties.Remove(key);
                } else
                    Properties[key].Normalize();
            }
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
