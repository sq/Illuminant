using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Squared.Illuminant.Configuration;
using Squared.Illuminant.Particles;

namespace Squared.Illuminant.Modeling {
    // JSON.net is a horrible library
    class WritablePropertiesOnlyResolver : DefaultContractResolver {
        protected override IList<JsonProperty> CreateProperties (Type type, MemberSerialization memberSerialization) {
            var result = base.CreateProperties(type, memberSerialization);
            if (type.GetCustomAttributes(typeof(CompilerGeneratedAttribute), true).Any())
                return result;

            var filtered = 
                (from p in result
                    where p.Writable || 
                        typeof(IList).IsAssignableFrom(p.PropertyType) ||
                        typeof(IDictionary).IsAssignableFrom(p.PropertyType)
                    select p).ToList();
            return filtered;
        }
    }

    public class IlluminantJsonConverter : JsonConverter {
        private static readonly string FnaSuffix;

        static IlluminantJsonConverter () {
            var fnaName = typeof(Vector4).AssemblyQualifiedName;
            FnaSuffix = fnaName.Substring(fnaName.IndexOf(',')).Trim();
        }

        public override bool CanConvert (Type objectType) {
            switch (objectType.Name) {
                case "NamedVariableCollection":
                case "IParameter":
                case "Parameter`1":
                case "ModelProperty":
                case "DynamicMatrix":
                case "Matrix":
                case "Color":
                    return true;
                default:
                    return false;
            }
        }

        private static Type ResolveTypeFromShortName (string name) {
#if FNA
            name = name.Replace(
                ", Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553",
                FnaSuffix
            );
#endif
            return Type.GetType(name, false) ?? typeof(ParticleSystem).Assembly.GetType(name, false) ?? typeof(Vector4).Assembly.GetType(name, false);
        }

        public override object ReadJson (JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            JToken token;

            switch (objectType.Name) {
                case "NamedVariableCollection": {
                    var result = (NamedVariableCollection)existingValue ?? new NamedVariableCollection();
                    token = JToken.Load(reader);
                    if (token.Type != JTokenType.Object)
                        throw new InvalidDataException();
                    var obj = (JObject)token;
                    foreach (var prop in obj.Properties()) {
                        var key = prop.Name.ToString();
                        var value = prop.Value;
                        var asParameter = value.ToObject<IParameter>(serializer);
                        var asDefinition = value.ToObject<NamedVariableDefinition>(serializer);
                        if (asDefinition != null) {
                            result.Add(key, asDefinition);
                        } else if (asParameter != null) {
                            result.Add(key, new NamedVariableDefinition { DefaultValue = asParameter });
                        }
                    };
                    return result;
                }
                case "IParameter":
                case "Parameter`1": {
                    Type expectedValueType = objectType.IsGenericType ? objectType.GetGenericArguments()[0] : null;
                    Type tResult = objectType.IsGenericType ? typeof(Parameter<>).MakeGenericType(expectedValueType) : null;

                    token = JToken.Load(reader);
                    var obj = token as JObject;
                    IParameter result;
                    if (obj != null) {
                        var typeName = obj["ValueType"].ToString();
                        var type = ResolveTypeFromShortName(typeName);
                        if (type == null)
                            throw new Exception("Could not resolve type " + typeName);

                        if (!objectType.IsGenericType) {
                            expectedValueType = type;
                            tResult = typeof(Parameter<>).MakeGenericType(type);
                        }

                        if (obj.ContainsKey("Name"))
                            result = (IParameter)Activator.CreateInstance(tResult, new object[] { obj["Name"].ToObject(typeof(string), serializer) });
                        else if (obj.ContainsKey("Constant"))
                            result = (IParameter)Activator.CreateInstance(tResult, new object[] { obj["Constant"].ToObject(type, serializer) });
                        else if (obj.ContainsKey("Bezier")) {
                            var bezierTypeName = obj["BezierType"].ToString();
                            var bezierType = ResolveTypeFromShortName(bezierTypeName);
                            result = (IParameter)Activator.CreateInstance(tResult, new object[] { obj["Bezier"].ToObject(bezierType, serializer) });
                        } else
                            throw new InvalidDataException();
                    } else if (expectedValueType != null) {
                        var rawValue = token.ToObject(expectedValueType, serializer);
                        result = (IParameter)Activator.CreateInstance(tResult, new object[] { rawValue });
                    } else {
                        throw new InvalidDataException();
                    }
                    return result;
                }
                case "ModelProperty": {
                    var obj = JObject.Load(reader);
                    var typeName = obj["Type"].ToString();
                    var type = ResolveTypeFromShortName(typeName);
                    if (type == null)
                        throw new Exception("Could not resolve type " + typeName); 
                    var result = new ModelProperty(
                        type, obj["Value"].ToObject(type, serializer)
                    );
                    return result;
                }
                case "DynamicMatrix": {
                    var obj = JObject.Load(reader);
                    if (obj.ContainsKey("Matrix"))
                        return new DynamicMatrix((Matrix)obj["Matrix"].ToObject(typeof(Matrix), serializer));
                    else {
                        var a = obj["Angle"];
                        var s = obj["Scale"];
                        var t = obj["Translation"];
                        var _a = (a != null) ? (float)a : 0;
                        var _s = (s != null) ? (float)s : 1;
                        var _t = (t != null) ? t.ToObject<Vector3>(serializer) : Vector3.Zero;
                        return new DynamicMatrix(_a, _s, _t);
                    }
                }
                case "Matrix":
                    var arr = serializer.Deserialize<float[]>(reader);
                    if (arr.Length == 0)
                        return Matrix.Identity;
                    else
                        return new Matrix(
                            arr[0], arr[1], arr[2], arr[3],
                            arr[4], arr[5], arr[6], arr[7],
                            arr[8], arr[9], arr[10], arr[11],
                            arr[12], arr[13], arr[14], arr[15]
                        );
                case "Color":
                    token = JToken.Load(reader);
                    var text = token.ToString();
                    var rgba = text.Split(',').Select(s => int.Parse(s)).ToList();
                    return new Color(rgba[0], rgba[1], rgba[2], rgba[3]);
                default:
                    throw new NotImplementedException();
            }
        }

        private string PickTypeName (Type type) {
            if (ResolveTypeFromShortName(type.FullName) == type)
                return type.FullName;
            else
                return type.AssemblyQualifiedName;
        }

        public override void WriteJson (JsonWriter writer, object value, JsonSerializer serializer) {
            if (value == null)
                return;

            var type = value.GetType();
            switch (type.Name) {
                case "NamedVariableCollection": {
                    var temp = new Dictionary<string, NamedVariableDefinition>((NamedVariableCollection)value);
                    serializer.Serialize(writer, temp);
                    return;
                }
                case "IParameter":
                case "Parameter`1": {
                    var p = (IParameter)value;
                    string typeName = PickTypeName(p.ValueType);
                    object obj;
                    if (p.Name != null) {
                        obj = new {
                            ValueType = typeName,
                            Name = p.Name
                        };
                    } else if (p.IsBezier) {
                        obj = new {
                            ValueType = typeName,
                            Bezier = p.Bezier,
                            BezierType = PickTypeName(p.Bezier.GetType())
                        };
                    } else {
                        obj = new {
                            ValueType = typeName,
                            Constant = p.Constant
                        };
                    }
                    serializer.Serialize(writer, obj);
                    return;
                }
                case "ModelProperty": {
                    var mp = (ModelProperty)value;
                    string typeName = PickTypeName(mp.Type);
                    serializer.Serialize(writer, new {
                        Type = typeName,
                        Value = mp.Value
                    });
                    return;
                }
                case "DynamicMatrix": {
                    var dm = (DynamicMatrix)value;
                    if (dm.IsGenerated) {
                        serializer.Serialize(writer, new {
                            Angle = dm.Angle,
                            Scale = dm.Scale
                        });
                    } else {
                        serializer.Serialize(writer, new {
                            Matrix = dm.Matrix
                        });
                    }
                    return;
                }
                case "Matrix": {
                    var m = (Matrix)value;
                    float[] values;
                    if (m == Matrix.Identity)
                        values = new float[0];
                    else
                        values = new float[] {
                            m.M11, m.M12, m.M13, m.M14,
                            m.M21, m.M22, m.M23, m.M24,
                            m.M31, m.M32, m.M33, m.M34,
                            m.M41, m.M42, m.M43, m.M44,
                        };
                    serializer.Serialize(writer, values);
                    return;
                }
                case "Color": {
                    var c = (Color)value;
                    serializer.Serialize(writer, string.Format("{0},{1},{2},{3}", c.R, c.G, c.B, c.A));
                    return;
                }
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
