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

namespace Squared.Illuminant.Modeling {
    public class EngineModel {
        public string Filename { get; private set; }
        public readonly List<SystemModel> Systems = new List<SystemModel>();

        public EngineModel () {
            Filename = null;
        }

        public void Normalize (bool forSave) {
            foreach (var s in Systems)
                s.Normalize(forSave);
        }

        public static EngineModel Load (string fileName) {
            using (var reader = new System.IO.StreamReader(fileName, Encoding.UTF8, false)) {
                var serializer = new JsonSerializer {
                    Converters = {
                        new IlluminantJsonConverter()
                    },
                    Formatting = Formatting.Indented
                };
                using (var jreader = new JsonTextReader(reader)) {
                    var result = serializer.Deserialize<EngineModel>(jreader);
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
                        new IlluminantJsonConverter()
                    },
                    Formatting = Formatting.Indented
                };
                serializer.Serialize(writer, this);
            }
            File.Copy(tempPath, fileName, true);
            Filename = Path.GetFullPath(fileName);
        }
    }

    public class SystemModel {
        public string Name;
        public ParticleSystemConfiguration Configuration;
        public readonly List<TransformModel> Transforms = new List<TransformModel>();

        public void Normalize (bool forSave) {
            foreach (var t in Transforms)
                t.Normalize(forSave);
        }

        public SystemModel Clone () {
            var result = new SystemModel {
                Name = Name,
                Configuration = Configuration.Clone()
            };
            foreach (var tm in Transforms)
                result.Transforms.Add(tm.Clone());
            return result;
        }
    }

    public class TransformModel {
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

        public TransformModel Clone () {
            var result = new TransformModel {
                Name = Name,
                Type = Type
            };
            foreach (var kvp in Properties)
                result.Properties.Add(kvp.Key, kvp.Value.Clone());
            return result;
        }
    }

    public class ModelProperty {
        public Type Type;
        public object Value;

        public static ModelProperty New (object value) {
            if (value == null)
                return null;

            return new ModelProperty(value);
        }

        public void Normalize () {
            var s = Value as string;
            if (s == null)
                return;
            if (Type == typeof(string))
                return;

            Value = JsonConvert.DeserializeObject(s, Type, new IlluminantJsonConverter());
        }

        public ModelProperty (Type type, object value) {
            Type = type;
            Value = value;
        }

        internal ModelProperty (object value) {
            Type = value.GetType();
            Value = value;
        }

        private object CloneValue (object value) {
            if (value == null)
                return null;

            var type = value.GetType();
            var cloneable = value as ICloneable;
            var list = value as IList;
            var cloneMethod = type.GetMethod("Clone");
            var mwc = type.GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
            if (cloneable != null)
                value = cloneable.Clone();
            else if (type.IsPrimitive)
                ;
            else if (cloneMethod != null)
                value = cloneMethod.Invoke(value, null);
            else if (list != null) {
                var newList = (IList)Activator.CreateInstance(type);
                foreach (var item in list)
                    newList.Add(CloneValue(item));
                value = newList;
            } else if (type.IsValueType && (mwc != null)) {
                value = mwc.Invoke(value, null);
            } else
                throw new NotImplementedException("Cannot clone value of type " + type.FullName);

            return value;
        }

        public ModelProperty Clone () {
            var value = CloneValue(Value);
            return new ModelProperty(Type, value); 
        }
    }

    public class IlluminantJsonConverter : JsonConverter {
        public override bool CanConvert (Type objectType) {
            switch (objectType.Name) {
                case "ModelProperty":
                case "Matrix":
                    return true;
                default:
                    return false;
            }
        }

        private static Type ResolveTypeFromShortName (string name) {
            return Type.GetType(name, false) ?? typeof(ParticleSystem).Assembly.GetType(name, false);
        }

        public override object ReadJson (JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            switch (objectType.Name) {
                case "ModelProperty":
                    var obj = JObject.Load(reader);
                    var typeName = obj["Type"].ToString();
                    var type = ResolveTypeFromShortName(typeName);
                    if (type == null)
                        throw new Exception("Could not resolve type " + typeName); 
                    var result = new ModelProperty(
                        type, obj["Value"].ToObject(type, serializer)
                    );
                    return result;
                case "Matrix":
                    var arr = serializer.Deserialize<float[]>(reader);
                    return new Matrix(
                        arr[0], arr[1], arr[2], arr[3],
                        arr[4], arr[5], arr[6], arr[7],
                        arr[8], arr[9], arr[10], arr[11],
                        arr[12], arr[13], arr[14], arr[15]
                    );
                default:
                    throw new NotImplementedException();
            }
        }

        public override void WriteJson (JsonWriter writer, object value, JsonSerializer serializer) {
            if (value == null)
                return;

            var type = value.GetType();
            switch (type.Name) {
                case "ModelProperty":
                    var mp = (ModelProperty)value;
                    string typeName;
                    if (ResolveTypeFromShortName(mp.Type.FullName) == mp.Type)
                        typeName = mp.Type.FullName;
                    else
                        typeName = mp.Type.AssemblyQualifiedName;
                    serializer.Serialize(writer, new {
                        Type = typeName,
                        Value = mp.Value
                    });
                    return;
                case "Matrix":
                    var m = (Matrix)value;
                    var values = new float[] {
                        m.M11, m.M12, m.M13, m.M14,
                        m.M21, m.M22, m.M23, m.M24,
                        m.M31, m.M32, m.M33, m.M34,
                        m.M41, m.M42, m.M43, m.M44,
                    };
                    serializer.Serialize(writer, values);
                    return;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
