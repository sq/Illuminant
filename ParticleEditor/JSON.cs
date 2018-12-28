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
        public override bool CanConvert (Type objectType) {
            switch (objectType.Name) {
                case "Parameter`1":
                case "ModelProperty":
                case "DynamicMatrix":
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
                case "Parameter`1": {
                    var obj = JObject.Load(reader);
                    var typeName = obj["ValueType"].ToString();
                    var type = ResolveTypeFromShortName(typeName);
                    if (type == null)
                        throw new Exception("Could not resolve type " + typeName);
                    var tResult = typeof(Parameter<>).MakeGenericType(type);
                    IParameter result;
                    if (obj.ContainsKey("Constant"))
                        result = (IParameter)Activator.CreateInstance(tResult, new object[] { obj["Constant"].ToObject(type, serializer) });
                    else if (obj.ContainsKey("Bezier")) {
                        var bezierTypeName = obj["BezierType"].ToString();
                        var bezierType = ResolveTypeFromShortName(bezierTypeName);
                        result = (IParameter)Activator.CreateInstance(tResult, new object[] { obj["Bezier"].ToObject(bezierType, serializer) });
                    } else
                        throw new InvalidDataException();
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
                    else
                        return new DynamicMatrix(
                            (float)obj["Angle"],
                            (float)obj["Scale"]
                        );
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
                case "Parameter`1": {
                    var p = (IParameter)value;
                    string typeName = PickTypeName(p.ValueType);
                    object obj;
                    if (p.IsBezier) {
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
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
