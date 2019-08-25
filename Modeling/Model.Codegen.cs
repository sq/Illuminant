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
using Squared.Illuminant.Particles.Transforms;

namespace Squared.Illuminant.Modeling {
    public partial class EngineModel {
        private static HashSet<Type> IncludedSerializationTypes = new HashSet<Type> {
            typeof(ParticleCollision),
            typeof(ParticleAppearance),
            typeof(ParticleColor),
            typeof(ParticleSystemReference),
            typeof(FMA.FMAParameters),
            typeof(TransformArea),
            typeof(GeometricTransform.GTParameters),
            typeof(Noise.NoiseParameters<>),
            typeof(Noise.NoiseParametersF),
            typeof(Noise.NoiseParameters3),
            typeof(Noise.NoiseParameters4),
            typeof(Gravity.Attractor),
        };

        private string GetSystemName (SystemModel sm, int index) {
            if (String.IsNullOrWhiteSpace(sm.Name))
                return "System" + index;
            else
                return FormatName(sm.Name);
        }

        private string FormatName (string name) {
            name = (name ?? "").Replace(" ", "").Replace("-", "").Replace(".", "");
            name = name.Substring(0, 1).ToUpper() + name.Substring(1);
            return name;
        }

        private IEnumerable<string> TagsFromString (string s) {
            return (
                from _ in (s ?? "").Split(',')
                let t = (_ ?? "").Trim()
                where !string.IsNullOrEmpty(t)
                select t
            ).OrderBy(t => t, StringComparer.InvariantCultureIgnoreCase);
        }

        private IEnumerable<string> TagsForTransform (TransformModel tm) {
            return TagsFromString(tm.Tags);
        }

        private IEnumerable<string> TagsForSystem (SystemModel sm) {
            return TagsFromString(sm.Tags);
        }

        private IEnumerable<string> TagsForSystemTransforms (SystemModel sm) {
            return sm.Transforms.SelectMany(TagsForTransform);
        }

        private void WriteCodeHeader (TextWriter tw, string name) {
            var allTagNames = 
                Systems
                    .SelectMany(TagsForSystem)
                    .Concat(Systems.SelectMany(TagsForSystemTransforms))
                    .OrderBy(t => t, StringComparer.InvariantCultureIgnoreCase)
                    .Distinct();

            tw.WriteLine(
@"using System;
using System.Collections.Generic;
using Squared.Util;
using Squared.Illuminant.Particles;
using Microsoft.Xna.Framework;

using G_BS = Microsoft.Xna.Framework.Graphics.BlendState;

namespace Squared.Illuminant.Compiled {{
    public class @{0} : IParticleSystems {{
        public bool IsDisposed {{ get; private set; }}

        public readonly ParticleEngine Engine;
", name
            );

            int i = 0;
            foreach (var s in Systems) {
                tw.WriteLine("        public readonly ParticleSystem {0};", GetSystemName(s, i++));
            }

            tw.WriteLine();

            foreach (var s in Systems) {
                foreach (var t in s.Transforms) {
                    if (String.IsNullOrWhiteSpace(t.Name))
                        continue;
                    tw.WriteLine("        public readonly {0} {1};", GetTypeName(t.Type), FormatName(t.Name));
                }
            }

            tw.WriteLine(
@"
        public ParticleSystem this [int index] {
            get {"
            );

            i = 0;
            foreach (var s in Systems) {
                tw.WriteLine("                if (index == {0}) return {1};", i, GetSystemName(s, i));
                i++;
            }

            tw.WriteLine(@"
                throw new ArgumentOutOfRangeException(""index"");
            }}
        }}

        public int SystemCount {{
            get {{
                return {0};
            }}
        }}", Systems.Count
            );

        tw.WriteLine("{0}        public class _Tags {{", Environment.NewLine);

        foreach (var t in allTagNames)
            tw.WriteLine("            public bool {0} = true;", FormatName(t));

        tw.WriteLine("        }");
        tw.WriteLine("        public readonly _Tags Tags = new _Tags();");

        tw.WriteLine(
@"
        public @{0} (ParticleEngine engine, ITimeProvider timeProvider = null) {{
            ParticleSystem s;
            Engine = engine;", name
            );
        }

        private void WriteCodeFooter (TextWriter tw) {
            var namedSystems = new Dictionary<string, SystemModel>();
            int i = 0;
            foreach (var s in Systems)
                namedSystems[GetSystemName(s, i++)] = s;

            WriteUpdateMethod(tw, namedSystems);
            WriteRenderMethod(tw, namedSystems);
            WriteDisposeMethod(tw, namedSystems);
        }

        private void WriteRenderMethod (TextWriter tw, Dictionary<string, SystemModel> systems) {
            tw.WriteLine();
            tw.WriteLine("        public void Render (Render.IBatchContainer container, int layer, Render.Material material = null, Matrix? transform = null, G_BS blendState = null, ParticleRenderParameters renderParams = null, bool usePreviousData = false) {");

            var orderedSystems = (from kvp in systems orderby kvp.Value.DrawOrder select kvp);
            foreach (var kvp in orderedSystems) {
                var name = kvp.Key;
                var s = kvp.Value;
                tw.WriteLine(
                    "            var {0}BlendState = blendState ?? {1};", 
                    name, s.AdditiveBlend 
                        ? "G_BS.Additive"
                        : "G_BS.AlphaBlend"
                );
                var tags = TagsForSystem(s).ToList();
                if (tags.Count > 0)
                    tw.Write("{0}            if ({1}){0}    ", Environment.NewLine, string.Join(" && ", tags.Select(tag => "Tags." + FormatName(tag))));
                tw.WriteLine("            {0}.Render(container, layer++, material: material, transform: transform, blendState: {0}BlendState, renderParams: renderParams, usePreviousData: usePreviousData);", name);
            }

            tw.WriteLine("        }");
        }

        private void WriteUpdateMethod (TextWriter tw, Dictionary<string, SystemModel> systems) {
            tw.WriteLine();
            tw.WriteLine("        public void Update (Render.IBatchContainer container, int layer) {");

            var orderedSystems = (from kvp in systems orderby kvp.Value.UpdateOrder select kvp.Key);

            foreach (var s in systems) {
                for (int i = 0, n = s.Value.Transforms.Count; i < n; i++) {
                    var t = s.Value.Transforms[i];
                    var tags = TagsForTransform(t).ToList();
                    if (tags.Count <= 0)
                        continue;

                    tw.WriteLine("            {0}.Transforms[{1}].IsActive2 = {2};", FormatName(s.Key), i, string.Join(" && ", tags.Select(tag => "Tags." + FormatName(tag))));
                }
            }

            // TODO: Conditionally update based on tags also?
            foreach (var name in orderedSystems)
                tw.WriteLine("            {0}.Update(container, layer++);", name);

            tw.WriteLine("        }");
        }

        private void WriteDisposeMethod (TextWriter tw, Dictionary<string, SystemModel> systems) {
            tw.WriteLine(
@"
        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
"
            );

            foreach (var kvp in systems)
                tw.WriteLine("            {0}.Dispose();", kvp.Key);

            tw.WriteLine(
@"        }
    }
}
"
            );
        }

        private void WriteSystems (TextWriter tw) {
            int i = 0;
            foreach (var s in Systems) {
                var name = GetSystemName(s, i++);

                WriteSystem(tw, s, name);
            }

            i = 0;
            foreach (var s in Systems) {
                var name = GetSystemName(s, i++);

                WriteTransforms(tw, s, name);
            }


            tw.WriteLine(
@"        }
"
            );
        }

        private void WriteSystem (TextWriter tw, SystemModel s, string name) {
            tw.WriteLine(
@"            
            var {0}Configuration = new ParticleSystemConfiguration {{", name
            );

            WriteConfiguration(tw, s.Configuration);

            tw.WriteLine(
@"            }};

            {0} = new ParticleSystem(engine, {0}Configuration);", name
            );
        }

        private void WriteConfiguration (TextWriter tw, ParticleSystemConfiguration c) {
            tw.WriteLine("                TimeProvider = timeProvider,");
            WriteMembers(tw, c, typeof(ParticleSystemConfiguration));
        }

        private void WriteTransforms (TextWriter tw, SystemModel s, string systemName) {
            foreach (var t in s.Transforms) {
                WriteTransform(tw, s, systemName, t);
            }
        }

        delegate bool TryGetValue (string name, out object result);

        private bool IsList (Type t) {
            return typeof(IList).IsAssignableFrom(t);
        }

        private void WriteMembers (
            TextWriter tw, object o, Type type,
            TryGetValue getValue = null
        ) {
            foreach (var m in type.GetMembers(BindingFlags.Public | BindingFlags.Instance)) {
                var f = m as FieldInfo;
                var p = m as PropertyInfo;

                string memberName;
                Type memberType;
                object value = null;

                if (f != null) {
                    memberName = f.Name;
                    memberType = f.FieldType;
                    if (!IsList(memberType)) {
                        if (f.IsInitOnly)
                            continue;
                    }
                    if (getValue == null)
                        value = f.GetValue(o);
                } else if (p != null) {
                    memberName = p.Name;
                    memberType = p.PropertyType;
                    if (IsList(memberType) && p.CanRead) {
                    } else {
                        if (!p.CanWrite || !p.CanRead)
                            continue;
                        if (p.GetSetMethod(false) == null)
                            continue;
                    }
                    if (getValue == null)
                        value = p.GetValue(o);
                } else {
                    continue;
                }

                if (getValue != null) {
                    if (!getValue(memberName, out value))
                        continue;
                }

                if (value == null)
                    continue;

                // Bad data in properties dict
                if (!memberType.IsAssignableFrom(value.GetType()))
                    continue;

                var formatted = FormatValue(value, memberType);
                if (formatted == null)
                    continue;

                tw.WriteLine("                {0} = {1},", memberName, formatted);
            }
        }

        private void WriteTransform (TextWriter tw, SystemModel s, string systemName, TransformModel t) {
            tw.WriteLine(
@"
            {0}.Transforms.Add({2}new {1} {{", systemName, GetTypeName(t.Type), string.IsNullOrWhiteSpace(t.Name) ? "" : FormatName(t.Name) + " = "
            );

            WriteMembers(tw, t, t.Type, (string name, out object value) => {
                if (!t.Properties.ContainsKey(name)) {
                    value = null;
                    return false;
                }

                value = t.Properties[name].Value;
                return true;
            });

            tw.WriteLine("            });");
        }

        private void WriteNamedVariables (TextWriter tw) {
            foreach (var kvp in NamedVariables) {
                var name = FormatName(kvp.Key);
                WriteNamedVariable(tw, name, kvp.Value);
            }
        }

        private string GetTypeName (Type t) {
            if (t == null)
                return "object";

            var typeName = t.Namespace + "." + t.Name.Replace("`1", "").Replace("`2", "").Replace("`3", "");
            var ga = t.GetGenericArguments();
            if ((ga != null) && (ga.Length > 0)) {
                typeName += "<";
                bool first = true;
                foreach (var a in ga) {
                    if (!first)
                        typeName += ", ";

                    var aName = GetTypeName(a);
                    typeName += aName;
                    first = false;
                }
                typeName += ">";
            }

            return typeName.Replace("Squared.Illuminant.", "")
                .Replace("Microsoft.Xna.Framework.", "")
                .Replace("System.", "");
        }

        private void WriteNamedVariable (TextWriter tw, string name, NamedVariableDefinition def) {
            Type valueType = null;
            string valueText = "null";

            // throw?
            if (def?.DefaultValue == null)
                return;

            valueType = def.ValueType;
            valueText = FormatValue(def.DefaultValue, ref valueType);

            var typeName = GetTypeName(valueType);
            tw.WriteLine("        public {0} @{1} = ", typeName, FormatName(name));
            tw.WriteLine("            {0};", valueText ?? "default(" + typeName + ")");
        }

        private string FormatValue (object value, Type type = null) {
            return FormatValue(value, ref type);
        }

        private string MakeEvaluator (string name) {
            var isBezier = false;
            IParameter param;
            NamedVariableDefinition def;

            if (NamedVariables.TryGetValue(name, out def)) {
                param = def.DefaultValue;
                isBezier = param.IsBezier;
            }

            name = FormatName(name);

            string expr;
            if (isBezier)
                expr = string.Format("@{0}.Evaluate(t)", name);
            else
                expr = string.Format("@{0}", name);

            if (def?.IsExternal == true)
                return string.Format("(t) => {{ {1} result; if (!Engine.Resolve{1}(\"{0}\", t, out result)) result = {2}; return result; }}", name, GetTypeName(def.ValueType), expr);
            else
                return "(t) => " + expr;
        }

        private string FormatValue (object value, ref Type type) {
            if (value == null)
                return null;

            var iList = value as IList;
            if (iList != null) {
                var sb = new StringBuilder();
                sb.Append("{ ");
                for (int i = 0, c = iList.Count; i < c; i++) {
                    var item = iList[i];
                    var itemText = FormatValue(item);
                    if (itemText != null)
                        sb.AppendFormat("{0}, ", itemText);
                }
                sb.Append("}");
                return sb.ToString();
            }

            var iParam = value as IParameter;
            string valueText;
            if (iParam != null) {
                if (iParam.IsExpression) {
                    type = iParam.ValueType;
                    return string.Format(
                        "new Configuration.Parameter<{0}>(new Configuration.BinaryParameterExpression<{0}>(\r\n{1}, {2},\r\n{3}))",
                        GetTypeName(type), FormatValue(iParam.Expression.LeftHandSide),
                        "Configuration.Operators." + iParam.Expression.Operator.ToString(),
                        FormatValue(iParam.Expression.RightHandSide)
                    );
                } else if (iParam.IsConstant) {
                    type = iParam.ValueType;
                    value = iParam.Constant;
                    return FormatValue(value);
                } else if (iParam.IsBezier) {
                    type = iParam.Bezier.GetType();
                    value = iParam.Bezier;
                    return FormatValue(iParam.Bezier);
                } else if (iParam.IsReference) {
                    type = iParam.GetType();
                    return string.Format(
                        "new {0}({1}, {2})",
                        GetTypeName(type),
                        FormatValue(iParam.Name),
                        MakeEvaluator(iParam.Name)
                    );
                }
            } else
                type = value.GetType();

            var tempType = type;

            var bezier = value as IBezier;
            if (bezier != null) {
                return string.Format(
                    @"new {0} {{
                    A = {1},
                    B = {2},
                    C = {3},
                    D = {4},
                    Count = {5}, Mode = BezierTimeMode.{6},
                    MinValue = {7}, MaxValue = {8}
            }}", 
                    GetTypeName(type),
                    FormatValue(bezier[0]), FormatValue(bezier[1]),
                    FormatValue(bezier[2]), FormatValue(bezier[3]),
                    bezier.Count, bezier.Mode,
                    FormatValue(bezier.MinValue), FormatValue(bezier.MaxValue)
                );
            }

            type = (type ?? value.GetType());

            var lazyResource = value as ILazyResource;
            if ((lazyResource != null) && (lazyResource.Name != null)) {
                var localName = lazyResource.Name;
                var absolutePath = Path.GetDirectoryName(Path.GetFullPath(Filename));
                var absoluteName = Path.GetFullPath(lazyResource.Name);
                localName = absoluteName.Replace(absolutePath, "");
                if (localName.StartsWith("\\"))
                    localName = localName.Substring(1);
                return string.Format("{{ Name = {0} }}", FormatValue(localName));
            }

            if (value is ParticleSystemReference) {
                var reference = (ParticleSystemReference)value;
                return "@" + FormatName(reference.Name);
            }

            var formula = value as IFormula;
            if (formula != null) {
                if (formula.Type != null) {
                    return string.Format(
                        @"new {0} {{
                        Constant = {1},
                        Offset = {2},
                        RandomScale = {3},
                        Type = FormulaType.{4}
                }}", 
                        GetTypeName(type),
                        FormatValue(formula.Constant),
                        FormatValue(formula.Offset),
                        FormatValue(formula.RandomScale),
                        formula.Type
                    );
                } else {
                    return string.Format(
                        @"new {0} {{
                        Constant = {1},
                        Offset = {2},
                        RandomScale = {3}
                }}", 
                        GetTypeName(type),
                        FormatValue(formula.Constant),
                        FormatValue(formula.Offset),
                        FormatValue(formula.RandomScale)
                    );
                }
            }

            // TODO: Formulas

            switch (type.Name) {
                case "Boolean":
                    return (bool)value ? "true" : "false";
                case "Int32":
                    return value.ToString();
                case "Single":
                    return value.ToString() + "f";
                case "Vector2":
                    var v2 = (Vector2)value;
                    return "new Vector2(" + FormatValue(v2.X) + 
                        ", " + FormatValue(v2.Y) + ")";
                case "Vector3":
                    var v3 = (Vector3)value;
                    return "new Vector3(" + FormatValue(v3.X) + 
                        ", " + FormatValue(v3.Y) + 
                        ", " + FormatValue(v3.Z) + ")";
                case "Vector4":
                    var v4 = (Vector4)value;
                    return "new Vector4(" + FormatValue(v4.X) + 
                        ", " + FormatValue(v4.Y) + 
                        ", " + FormatValue(v4.Z) + 
                        ", " + FormatValue(v4.W) + ")";
                case "String":
                    var s = (string)value;
                    return string.Format(
                        "\"{0}\"", s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    );
            }

            if (IncludedSerializationTypes.Contains(type)) {
                var sb = new StringBuilder();
                using (var sw = new StringWriter(sb)) {
                    sb.AppendLine("{");
                    WriteMembers(sw, value, type);
                    sb.AppendLine("}");
                }
                return sb.ToString();
            }

            // FIXME
            // throw new NotImplementedException(value.GetType().FullName);
            return null;
        }
    }
}
