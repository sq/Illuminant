﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Util;
using Nuke = NuklearDotNet.Nuklear;

namespace ParticleEditor {
    public partial class ParticleEditor : MultithreadedGame, INuklearHost {
        private class KeyboardInput : System.Windows.Forms.IMessageFilter {
            [DllImport("user32.dll")]
            static extern bool TranslateMessage(ref System.Windows.Forms.Message lpMsg);

            const int WM_KEYDOWN = 0x100;
            const int WM_KEYUP = 0x101;
            const int WM_CHAR = 0x102;

            public readonly ParticleEditor Game;
            public readonly List<char> Buffer = new List<char>();

            public KeyboardInput (ParticleEditor game) {
                Game = game;
            }

            public bool PreFilterMessage (ref System.Windows.Forms.Message m) {
                switch (m.Msg) {
                    case WM_KEYDOWN:
                    case WM_KEYUP:
                        // XNA normally doesn't invoke TranslateMessage so we don't get any char events
                        TranslateMessage(ref m);
                        return false;
                    case WM_CHAR:
                        Buffer.Add((char)m.WParam.ToInt32());
                        return true;
                    default:
                        return false;
                }
            }
        }

        private class CachedPropertyInfo {
            public string Name, TypeName;
            public FieldInfo Field;
            public PropertyInfo Property;
            public Type Type;
            public Func<object, object> Getter;
            public Action<object, object> Setter;
        }

        private struct PropertyGridCache {
            public Type CachedType;
            public List<CachedPropertyInfo> Members;

            public uint ScrollX, ScrollY;
            public string SelectedPropertyName;

            internal bool Prepare (Type type) {
                if (type == CachedType)
                    return false;

                CachedType = type;
                Members = CachePropertyInfo(type).ToList();
                SelectedPropertyName = null;
                return true;
            }
        }

        private readonly Dictionary<Type, List<CachedPropertyInfo>> CachedMembers =
            new Dictionary<Type, List<CachedPropertyInfo>>(new ReferenceComparer<Type>());
        private PropertyGridCache SystemProperties, TransformProperties;
        private List<Type> TransformTypes = GetTransformTypes().ToList();

        private KeyboardInput KeyboardInputHandler;

        private static IEnumerable<Type> GetTransformTypes () {
            var tTransform = typeof(ParticleTransform);
            return from t in tTransform.Assembly.GetTypes()
                   where tTransform.IsAssignableFrom(t)
                   where !t.IsAbstract
                   select t;
        }

        private static string GetTypeNameForField (Type type, string fieldName, Type fieldType) {
            Dictionary<string, string> d;
            if (!FieldTypeOverrides.TryGetValue(type.Name, out d))
                return fieldType.Name;
            string result;
            if (!d.TryGetValue(fieldName, out result))
                return fieldType.Name;
            return result;
        }

        private static IEnumerable<CachedPropertyInfo> CachePropertyInfo (Type type) {
            return from m in type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                   where (m.MemberType == MemberTypes.Field) || (m.MemberType == MemberTypes.Property)
                   let f = m as FieldInfo
                   let p = m as PropertyInfo
                   let mtype = (f != null) ? f.FieldType : p.PropertyType
                   where (f == null) || !f.IsInitOnly
                   where (p == null) || p.CanWrite
                   where !m.GetCustomAttributes<NonSerializedAttribute>().Any()
                   orderby m.Name
                   select new CachedPropertyInfo {
                       Name = m.Name,
                       TypeName = GetTypeNameForField(type, m.Name, mtype),
                       Field = f,
                       Property = p,
                       Type = mtype,
                       Getter = (f != null) ? (Func<object, object>)f.GetValue : p.GetValue,
                       Setter = (f != null) ? (Action<object, object>)f.SetValue : p.SetValue
                   };
        }

        private unsafe void RenderPropertyGrid (object instance, ref PropertyGridCache cache, float heightPx) {
            using (var g = Nuklear.ScrollingGroup(heightPx, "Properties", ref cache.ScrollX, ref cache.ScrollY))
            if (g.Visible) {
                foreach (var cpi in cache.Members)
                    RenderProperty(ref cache, cpi, instance);
            }
        }

        private unsafe void RenderPropertyElement (
            string key, ref float value, ref bool changed, float? min = null, float? max = null
        ) {
            float lowStep = 0.05f;
            float highStep = 1f;
            float lowInc = 0.02f;
            float highInc = 1f;
            float lowThreshold = 10f;
            var isLow = Math.Abs(value) < lowThreshold;
            float step = isLow ? lowStep : highStep;
            float inc = isLow ? lowInc : highInc;
            if (Nuklear.Property(key, ref value, min.GetValueOrDefault(-4096), max.GetValueOrDefault(4096), step, inc)) {
                changed = true;
                // Mask off tiny decimals when transitioning between small and large
                if (isLow != Math.Abs(value) < lowThreshold) {
                    if (isLow)
                        value = (float)(Math.Floor(Math.Abs(value)) * Math.Sign(value));
                }
            }
        }

        private unsafe bool RenderProperty (
            ref PropertyGridCache cache,
            CachedPropertyInfo cpi,
            object instance,
            string prefix = null
        ) {
            bool changed = false, b;
            var ctx = Nuklear.Context;
            var actualName = cpi.Name;
            if (!string.IsNullOrEmpty(prefix))
                actualName = prefix + actualName;

            var isActive = cache.SelectedPropertyName == actualName;
            var value = cpi.Getter(instance);

            var valueType = cpi.TypeName;

            switch (valueType) {
                case "Formula":
                case "FMAParameters`1":
                    List<CachedPropertyInfo> members;
                    if (!CachedMembers.TryGetValue(cpi.Type, out members))
                        CachedMembers[cpi.Type] = members = CachePropertyInfo(cpi.Type).ToList();

                    using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                        // FIXME: Slow
                        if (pGroup.Visible) {
                            foreach (var i in members) 
                                if (RenderProperty(ref cache, i, value, cpi.Name))
                                    changed = true;

                            if (changed)
                                cpi.Setter(instance, value);
                        }
                        return changed;
                    }

                case "Int32":
                case "Single":
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 1);
                    if (cpi.Type == typeof(float)) {
                        var v = (float)value;
                        RenderPropertyElement(cpi.Name, ref v, ref changed);
                        if (changed) {
                            cpi.Setter(instance, v);
                            return true;
                        }
                    } else {
                        var v = (int)value;
                        if (Nuklear.Property(cpi.Name, ref v, 0, 40960, 1, 1)) {
                            cpi.Setter(instance, v);
                            return true;
                        }
                    }

                    return false;

                case "Matrix":
                    return RenderMatrixProperty(cpi, instance, ref changed, actualName, value);
            }

            Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
            Nuklear.SelectableText(cpi.Name, isActive);

            if (value == null) {
                Nuklear.SelectableText("null", isActive);
                return false;
            }

            switch (valueType) {
                case "String":
                    if (Nuklear.SelectableText(value.ToString(), isActive))
                        cache.SelectedPropertyName = actualName;
                    return false;

                case "Boolean":
                    b = (bool)value;
                    if (Checkbox(null, ref b)) {
                        cpi.Setter(instance, b);
                        return true;
                    }
                    return false;

                case "Vector2":
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
                    var v2 = (Vector2)value;
                    RenderPropertyElement("#x", ref v2.X, ref changed);
                    RenderPropertyElement("#y", ref v2.Y, ref changed);
                    if (changed) {
                        cpi.Setter(instance, v2);
                        return true;
                    }
                    return false;

                case "Vector3":
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 3);
                    var v3 = (Vector3)value;
                    RenderPropertyElement("#x", ref v3.X, ref changed);
                    RenderPropertyElement("#y", ref v3.Y, ref changed);
                    RenderPropertyElement("#z", ref v3.Z, ref changed);
                    if (changed) {
                        cpi.Setter(instance, v3);
                        return true;
                    }
                    return false;

                case "Vector4":
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 4);
                    var v4 = (Vector4)value;
                    RenderPropertyElement("#x", ref v4.X, ref changed);
                    RenderPropertyElement("#y", ref v4.Y, ref changed);
                    RenderPropertyElement("#z", ref v4.Z, ref changed);
                    RenderPropertyElement("#w", ref v4.W, ref changed);
                    if (changed) {
                        cpi.Setter(instance, v4);
                        return true;
                    }
                    return false;

                case "ColorF":
                    return RenderColorProperty(cpi, instance, out changed, value);

                default:
                    if (Nuklear.SelectableText(value.GetType().Name, isActive))
                        cache.SelectedPropertyName = actualName;
                    return false;
            }
        }

        private unsafe bool RenderMatrixProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, object value
        ) {
            var ctx = Nuklear.Context;
            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                if (pGroup.Visible) {
                    var m = (Matrix)value;

                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
                    if (Nuklear.Button("Identity")) {
                        m = Matrix.Identity;
                        changed = true;
                    }                    
                    if (Nuklear.Button("Mutate")) {
                    }
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 4);
                    RenderPropertyElement("#xx", ref m.M11, ref changed);
                    RenderPropertyElement("#xy", ref m.M12, ref changed);
                    RenderPropertyElement("#xz", ref m.M13, ref changed);
                    RenderPropertyElement("#xw", ref m.M14, ref changed);
                    RenderPropertyElement("#yx", ref m.M21, ref changed);
                    RenderPropertyElement("#yy", ref m.M22, ref changed);
                    RenderPropertyElement("#yz", ref m.M23, ref changed);
                    RenderPropertyElement("#yw", ref m.M24, ref changed);
                    RenderPropertyElement("#zx", ref m.M31, ref changed);
                    RenderPropertyElement("#zy", ref m.M32, ref changed);
                    RenderPropertyElement("#zz", ref m.M33, ref changed);
                    RenderPropertyElement("#zw", ref m.M34, ref changed);
                    RenderPropertyElement("#wx", ref m.M41, ref changed);
                    RenderPropertyElement("#wy", ref m.M42, ref changed);
                    RenderPropertyElement("#wz", ref m.M43, ref changed);
                    RenderPropertyElement("#ww", ref m.M44, ref changed);
                    if (changed) {
                        cpi.Setter(instance, m);
                        return true;
                    }
                }
            }
            return false;
        }

        private unsafe bool RenderColorProperty (
            CachedPropertyInfo cpi, object instance, out bool changed, 
            object value
        ) {
            var ctx = Nuklear.Context;
            var c = (Vector4)value;
            var oldColor = new NuklearDotNet.nk_colorf {
                r = c.X,
                g = c.Y,
                b = c.Z,
                a = c.W,
            };
            var resetToWhite = Nuklear.Button("White");
            Nuke.nk_layout_row_dynamic(ctx, 96, 1);
            var temp = Nuke.nk_color_picker(ctx, oldColor, NuklearDotNet.nk_color_format.NK_RGBA);
            var newColor = resetToWhite ? Vector4.One : new Vector4(temp.r, temp.g, temp.b, temp.a);
            changed = newColor != c;
            Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 4);
            RenderPropertyElement("#R", ref newColor.X, ref changed, 0, 1);
            RenderPropertyElement("#G", ref newColor.Y, ref changed, 0, 1);
            RenderPropertyElement("#B", ref newColor.Z, ref changed, 0, 1);
            RenderPropertyElement("#A", ref newColor.W, ref changed, 0, 1);
            if (changed) {
                cpi.Setter(instance, newColor);
                return true;
            }
            return false;
        }

        // Returns true if value changed
        private unsafe bool Checkbox (string text, ref bool value) {
            bool newValue;
            using (var temp = new NString(text))
                newValue = Nuke.nk_check_text(Nuklear.Context, temp.pText, temp.Length, value ? 0 : 1) == 0;

            var result = newValue != value;
            value = newValue;
            return result;
        }
    }
}
