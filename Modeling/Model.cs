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
    public class EngineModel {
        public string Filename { get; private set; }
        public readonly NamedVariableCollection NamedVariables = new NamedVariableCollection();
        public readonly List<SystemModel> Systems = new List<SystemModel>();
        public readonly Dictionary<string, object> UserData = new Dictionary<string, object>();

        public EngineModel () {
            Filename = null;
        }

        public T GetUserData<T> (string key)
            where T : class {
            object result;
            if (!UserData.TryGetValue(key, out result))
                return default(T);

            var jud = result as JObject;
            if (jud != null) {
                var ud = jud.ToObject<T>();
                UserData[key] = ud;
                return ud;
            }

            return result as T;
        }

        public void Normalize (bool forSave) {
            foreach (var s in Systems)
                s.Normalize(forSave);
        }

        private static JsonSerializer MakeSerializer () {
            return new JsonSerializer {
                Converters = {
                    new IlluminantJsonConverter()
                },
                ContractResolver = new WritablePropertiesOnlyResolver(),
                Formatting = Formatting.Indented
            };
        }

        public static EngineModel Load (Stream s) {
            using (var reader = new StreamReader(s, Encoding.UTF8, false)) {
                var serializer = MakeSerializer();
                using (var jreader = new JsonTextReader(reader)) {
                    var result = serializer.Deserialize<EngineModel>(jreader);
                    if (result != null)
                        result.Sort();
                    return result;
                }
            }
        }

        public static EngineModel Load (string fileName) {
            using (var stream = File.OpenRead(fileName)) {
                var result = Load(stream);
                if (result != null) {
                    result.Filename = Path.GetFullPath(fileName);
                    result.Sort();
                }
                return result;
            }
        }

        internal void Sort () {
            foreach (var s in Systems)
                s.Sort();
        }

        public void Save (string fileName) {
            Normalize(true);

            var tempPath = Path.GetTempFileName();
            using (var writer = new StreamWriter(tempPath, false, Encoding.UTF8)) {
                var serializer = MakeSerializer();
                serializer.Serialize(writer, this);
            }
            File.Copy(tempPath, fileName, true);
            Filename = Path.GetFullPath(fileName);
        }

        public IEnumerable<string> ConstantNamesOfType (Type valueType) {
            return (from kvp in NamedVariables where kvp.Value.ValueType == valueType select kvp.Key);
        }

        public bool HasAnyConstantsOfType (Type valueType) {
            return NamedVariables.Values.Any(c => c.ValueType == valueType);
        }
    }

    public class SystemModel {
        internal class TransformSorter : IComparer<TransformModel> {
            public int Compare (TransformModel x, TransformModel y) {
                return (x.UpdateOrder - y.UpdateOrder);
            }
        }

        internal static readonly TransformSorter Comparer = new TransformSorter();

        public string Name;
        public int UpdateOrder, DrawOrder;
        public BlendState BlendState;
        public ParticleSystemConfiguration Configuration;
        public readonly List<TransformModel> Transforms = new List<TransformModel>();

        public void Sort () {
            Transforms.Sort(Comparer);
        }

        public void Normalize (bool forSave) {
            foreach (var t in Transforms)
                t.Normalize(forSave);

            if (forSave)
                Sort();
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
        public int UpdateOrder;
        public readonly Dictionary<string, ModelProperty> Properties = new Dictionary<string, ModelProperty>();

        public void Normalize (bool forSave) {
            var keys = Properties.Keys.ToList();
            foreach (var key in keys) {
                var m = Type.GetMember(key).FirstOrDefault();
                if (m == null) {
                    if (forSave)
                        Properties.Remove(key);
                    continue;
                }

                var p = Properties[key];
                if (p != null)
                    p.Normalize();
                else if (forSave)
                    Properties.Remove(key);
            }
        }

        public TransformModel Clone () {
            var result = new TransformModel {
                Name = Name,
                Type = Type
            };
            foreach (var kvp in Properties) {
                if (kvp.Value != null)
                    result.Properties.Add(kvp.Key, kvp.Value.Clone());
            }
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
            /*
            var s = Value as string;
            if (s == null)
                return;
            if (Type == typeof(string))
                return;
            
            Value = JsonConvert.DeserializeObject(s, Type, new IlluminantJsonConverter());
            */
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

    public class NamedVariableCollection : Dictionary<string, IParameter> {
        public NamedVariableCollection ()
            : base (StringComparer.OrdinalIgnoreCase) {
        }

        public bool Set<T> (string name, ref T value)
            where T: struct {
            IParameter p;
            if (!TryGetValue(name, out p))
                return false;

            if (!(p is Parameter<T>))
                return false;

            var pv = (Parameter<T>)p;
            pv.Constant = value;
            this[name] = pv;
            return true;
        }

        public bool Set<T> (string name, T value)
            where T: struct {
            return Set(name, ref value);
        }
    }
}
