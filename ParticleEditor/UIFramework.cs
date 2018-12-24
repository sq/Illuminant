using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Util;
using Nuke = NuklearDotNet.Nuklear;

namespace ParticleEditor {
    public partial class ParticleEditor : MultithreadedGame, INuklearHost {
        internal class KeyboardInput : System.Windows.Forms.IMessageFilter {
            public struct Deactivation : IDisposable {
                public KeyboardInput This;

                public void Dispose () {
                    System.Windows.Forms.Application.AddMessageFilter(This);
                }
            }

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

            public void Install () {
                System.Windows.Forms.Application.AddMessageFilter(this);
            }

            public Deactivation Deactivate () {
                System.Windows.Forms.Application.RemoveMessageFilter(this);
                return new Deactivation { This = this };
            }

            public bool PreFilterMessage (ref System.Windows.Forms.Message m) {
                switch (m.Msg) {
                    case WM_KEYDOWN:
                    case WM_KEYUP:
                        // XNA normally doesn't invoke TranslateMessage so we don't get any char events
                        TranslateMessage(ref m);
                        return false;
                    case WM_CHAR:
                        var ch = (char)m.WParam.ToInt32();
                        // We can get wm_char events for control characters like backspace and Nuklear *does not like that*
                        if (ch >= 32)
                            Buffer.Add(ch);
                        return true;
                    default:
                        return false;
                }
            }
        }

        internal class CachedPropertyInfo {
            public string Name;
            public ModelTypeInfo Info;
            public FieldInfo Field;
            public PropertyInfo Property;
            public Type RawType, Type;
            public Func<object, object> Getter;
            public Action<object, object> Setter;
            public bool AllowNull;
            public CachedPropertyInfo ElementInfo;
        }

        private struct PropertyGridCache {
            public Type CachedType;
            public List<CachedPropertyInfo> Members;

            public uint ScrollX, ScrollY;
            public int SelectedIndex;

            internal bool Prepare (Type type) {
                if (type == CachedType)
                    return false;

                CachedType = type;
                Members = CachePropertyInfo(type).ToList();
                SelectedIndex = 0;
                return true;
            }
        }

        private readonly Dictionary<Type, List<CachedPropertyInfo>> CachedMembers =
            new Dictionary<Type, List<CachedPropertyInfo>>(new ReferenceComparer<Type>());
        private readonly Dictionary<string, PropertyGridCache> GridCaches = 
            new Dictionary<string, PropertyGridCache>();
        private PropertyGridCache SystemProperties, TransformProperties;
        private List<Type> TransformTypes = GetTransformTypes().ToList();

        internal KeyboardInput KeyboardInputHandler;

        private static IEnumerable<Type> GetTransformTypes () {
            var tTransform = typeof(ParticleTransform);
            return from t in tTransform.Assembly.GetTypes()
                   where tTransform.IsAssignableFrom(t)
                   where !t.IsAbstract
                   select t;
        }

        internal static ModelTypeInfo GetInfoForField (Type type, string fieldName, Type fieldType) {
            var t = type;
            while (t != null) {
                Dictionary<string, ModelTypeInfo> d;
                if (FieldTypeOverrides.TryGetValue(type.Name, out d)) {
                    ModelTypeInfo temp;
                    if (d.TryGetValue(fieldName, out temp))
                        return temp;
                }
                t = t.BaseType;
            }

            return new ModelTypeInfo {
                Type = fieldType.Name
            };
        }

        internal class ElementBox {
            public object Value;
        }

        private static CachedPropertyInfo GetElementInfo (Type type) {
            if (typeof(System.Collections.IList).IsAssignableFrom(type)) {
                var elementType = type.GetGenericArguments()[0];
                var info = GetInfoForField(type, "Item", elementType);
                return new CachedPropertyInfo {
                    Name = "Value",
                    Info = info,
                    Field = null,
                    Property = null,
                    RawType = elementType,
                    Type = elementType,
                    AllowNull = false,
                    Getter = (i) => ((ElementBox)i).Value,
                    Setter = (i, v) => {  ((ElementBox)i).Value = v; }
                };
            } else {
                return null;
            }
        }

        private static IEnumerable<CachedPropertyInfo> CachePropertyInfo (Type type) {
            return from m in type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                   where (m.MemberType == MemberTypes.Field) || (m.MemberType == MemberTypes.Property)
                   let f = m as FieldInfo
                   let p = m as PropertyInfo
                   let _mtype = (f != null) ? f.FieldType : p.PropertyType
                   let isNullable = _mtype.Name == "Nullable`1"
                   let allowNull = _mtype.IsClass || isNullable
                   let mtype = isNullable ? _mtype.GetGenericArguments()[0] : _mtype
                   let info = GetInfoForField(type, m.Name, mtype)
                   let isList = (info.Type == "List") || (info.Type == "ValueList")
                   let isWritable = ((f != null) && !f.IsInitOnly) || ((p != null) && p.CanWrite)
                   where (f == null) || !f.IsInitOnly || isList
                   where (p == null) || (p.CanWrite && p.CanRead) || isList
                   where !m.GetCustomAttributes<NonSerializedAttribute>().Any()
                   orderby m.Name
                   select new CachedPropertyInfo {
                       Name = m.Name,
                       Info = info,
                       Field = f,
                       Property = p,
                       RawType = _mtype,
                       Type = mtype,
                       AllowNull = allowNull,
                       Getter = (f != null) ? (Func<object, object>)f.GetValue : p.GetValue,
                       Setter = isWritable
                           ? ((f != null) ? (Action<object, object>)f.SetValue : p.SetValue)
                           : (i, v) => { },
                       ElementInfo = GetElementInfo(mtype)
                   };
        }

        internal void RunWorkItem (Action workItem) {
            Scheduler.QueueWorkItemForNextStep(() => {
                using (KeyboardInputHandler.Deactivate()) {
                    RenderCoordinator.WaitForActiveDraws();
                    workItem();
                }
            });
        }

        private unsafe bool RenderPropertyGridNonScrolling (object instance, ref PropertyGridCache cache) {
            var result = false;

            foreach (var cpi in cache.Members) {
                if (RenderProperty(ref cache, cpi, instance))
                    result = true;
            }

            return result;
        }

        private unsafe bool RenderPropertyGrid (object instance, ref PropertyGridCache cache, float? heightPx) {
            if (heightPx.HasValue) {
                using (var g = Nuklear.ScrollingGroup(heightPx.Value, "Properties", ref cache.ScrollX, ref cache.ScrollY))
                if (g.Visible)
                    return RenderPropertyGridNonScrolling(instance, ref cache);
                else
                    return false;
            } else {
                return RenderPropertyGridNonScrolling(instance, ref cache);
            }
        }

        private static readonly KeyValuePair<float, float>[] PropertyIncrementSteps = new[] {
            new KeyValuePair<float, float>(0f, 0.01f),
            new KeyValuePair<float, float>(1f, 0.02f),
            new KeyValuePair<float, float>(5f, 0.1f),
            new KeyValuePair<float, float>(25f, 0.5f),
            new KeyValuePair<float, float>(100f, 1f),
            new KeyValuePair<float, float>(1000f, 5f),
        };

        private int GetScaleIndex (float value, out float inc) {
            int result = 0;
            inc = PropertyIncrementSteps[0].Value;
            for (int i = 0; i < PropertyIncrementSteps.Length; i++) {
                var kvp = PropertyIncrementSteps[i];
                result = i;
                inc = kvp.Value;
                if (Math.Abs(value) <= kvp.Key)
                    break;
            }
            return result;
        }

        private unsafe void RenderPropertyElement (
            string key, ModelTypeInfo? info, ref float value, ref bool changed, float? min = null, float? max = null
        ) {
            // FIXME
            if (Single.IsInfinity(value) || Single.IsNaN(value))
                value = 0;

            var _info = info.GetValueOrDefault(default(ModelTypeInfo));
            float lowStep = 0.05f;
            float highStep = 1f;
            float step = (value >= 5) ? highStep : lowStep;

            float inc = PropertyIncrementSteps[0].Value;
            int scaleIndex = GetScaleIndex(value, out inc);

            var _min = min.GetValueOrDefault(_info.Min.GetValueOrDefault(-4096));
            var _max = max.GetValueOrDefault(_info.Max.GetValueOrDefault(4096));
            if (Nuklear.Property(key, ref value, _min, _max, step, inc)) {
                changed = true;
                float newInc;
                var newIndex = GetScaleIndex(value, out newInc);
                // Mask off tiny decimals when transitioning between small and large
                if ((newInc > 1) && (inc < 1))
                    value = (float)(Math.Floor(Math.Abs(value)) * Math.Sign(value));
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

            var isActive = false;
            var value = cpi.Getter(instance);

            var valueType = cpi.Info.Type ?? cpi.Type.Name;

            switch (valueType) {
                case "List":
                    return RenderListProperty(cpi, instance, ref changed, actualName, value, false);

                case "ValueList":
                    return RenderListProperty(cpi, instance, ref changed, actualName, value, true);

                case "Formula":
                case "FMAParameters`1":
                    List<CachedPropertyInfo> members;
                    if (!CachedMembers.TryGetValue(cpi.Type, out members))
                        CachedMembers[cpi.Type] = members = CachePropertyInfo(cpi.Type).ToList();

                    using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                        if (pGroup.Visible) {
                            foreach (var i in members) 
                                if (RenderProperty(ref cache, i, value, cpi.Name))
                                    changed = true;

                            if (changed)
                                cpi.Setter(instance, value);
                        }
                        return changed;
                    }

                case "ParticleTexture":
                    return RenderTextureProperty(cpi, instance, ref changed, actualName, value);

                case "Int32":
                case "Single":
                    if (!cpi.AllowNull || (value != null)) {
                        Nuke.nk_layout_row_dynamic(ctx, LineHeight, 1);
                        if (cpi.Type == typeof(float)) {
                            var v = (float)value;
                            RenderPropertyElement(cpi.Name, cpi.Info, ref v, ref changed);
                            if (changed) {
                                cpi.Setter(instance, v);
                                return true;
                            }
                        } else {
                            var v = (int)value;
                            if (Nuklear.Property(
                                cpi.Name, ref v, 
                                (int)cpi.Info.Min.GetValueOrDefault(0), 
                                (int)cpi.Info.Min.GetValueOrDefault(40960), 
                                1, 1
                            )) {
                                cpi.Setter(instance, v);
                                return true;
                            }
                        }
                        return false;
                    }
                    break;

                case "ColorF":
                    return RenderColorProperty(cpi, instance, out changed, value);
                case "Matrix":
                    return RenderMatrixProperty(cpi, instance, ref changed, actualName, value, false);
                case "Matrix3x4":
                    return RenderMatrixProperty(cpi, instance, ref changed, actualName, value, true);
            }

            Nuke.nk_layout_row_dynamic(ctx, LineHeight, 2);
            Nuklear.SelectableText(cpi.Name, isActive);

            if (cpi.AllowNull) {
                var isNull = value == null;
                if (isNull) {
                    if (Nuklear.Button("Create")) {
                        value = Activator.CreateInstance(cpi.Type);
                        cpi.Setter(instance, value);
                        changed = true;
                    }
                    return changed;
                } else {
                    if (Nuklear.Button("Erase")) {
                        cpi.Setter(instance, value = null);
                        changed = true;
                        return changed;
                    }
                }
            }

            if (value == null) {
                Nuklear.SelectableText("null", isActive);
                return false;
            }

            switch (valueType) {
                case "String":
                    var text = value.ToString();
                    if (Nuklear.Textbox(ref text)) {
                        cpi.Setter(instance, text);
                        return true;
                    }
                    return false;

                case "Boolean":
                    b = (bool)value;
                    if (Checkbox(null, ref b)) {
                        cpi.Setter(instance, b);
                        return true;
                    }
                    return false;

                case "Vector2":
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 2);
                    var v2 = (Vector2)value;
                    RenderPropertyElement("#x", cpi.Info, ref v2.X, ref changed);
                    RenderPropertyElement("#y", cpi.Info, ref v2.Y, ref changed);
                    if (changed) {
                        cpi.Setter(instance, v2);
                        return true;
                    }
                    return false;

                case "Vector3":
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 3);
                    var v3 = (Vector3)value;
                    RenderPropertyElement("#x", cpi.Info, ref v3.X, ref changed);
                    RenderPropertyElement("#y", cpi.Info, ref v3.Y, ref changed);
                    RenderPropertyElement("#z", cpi.Info, ref v3.Z, ref changed);
                    if (changed) {
                        cpi.Setter(instance, v3);
                        return true;
                    }
                    return false;

                case "Vector4":
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 4);
                    var v4 = (Vector4)value;
                    RenderPropertyElement("#x", cpi.Info, ref v4.X, ref changed);
                    RenderPropertyElement("#y", cpi.Info, ref v4.Y, ref changed);
                    RenderPropertyElement("#z", cpi.Info, ref v4.Z, ref changed);
                    RenderPropertyElement("#w", cpi.Info, ref v4.W, ref changed);
                    if (changed) {
                        cpi.Setter(instance, v4);
                        return true;
                    }
                    return false;

                default:
                    if (Nuklear.SelectableText(value.GetType().Name, isActive))
                        return true;
                    return false;
            }
        }

        private struct MatrixGenerateParameters {
            public float Angle, Scale;
        }

        private readonly Dictionary<string, MatrixGenerateParameters> MatrixGenerateParams = new Dictionary<string, MatrixGenerateParameters>();

        private unsafe bool RenderMatrixProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, object value, bool is3x4
        ) {
            var ctx = Nuklear.Context;
            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                if (pGroup.Visible) {
                    var m = (Matrix)value;

                    using (var grp = Nuklear.CollapsingGroup("Generate", "GenerateMatrix", false, NextMatrixIndex++))
                    if (grp.Visible) {
                        MatrixGenerateParameters p;
                        if (!MatrixGenerateParams.TryGetValue(actualName, out p)) {
                            p = new MatrixGenerateParameters { Angle = 0, Scale = 1 };
                        }

                        Nuke.nk_layout_row_dynamic(ctx, LineHeight, 1);
                        if (Nuklear.Button("Identity")) {
                            m = Matrix.Identity;
                            p.Angle = 0;
                            p.Scale = 1;
                            changed = true;
                        }

                        bool regenerate = false;

                        Nuke.nk_layout_row_dynamic(ctx, LineHeight, 1);
                        if (Nuklear.Property("Rotate", ref p.Angle, -360, 360, 0.5f, 0.25f)) {
                            regenerate = true;
                            changed = true;
                        }
                        if (Nuklear.Property("Scale", ref p.Scale, -5, 5, 0.05f, 0.01f)) {
                            regenerate = true;
                            changed = true;
                        }

                        if (regenerate) {
                            m = Matrix.CreateRotationZ(MathHelper.ToRadians(p.Angle)) *
                                Matrix.CreateScale(p.Scale);
                        }

                        if (changed || regenerate)
                            MatrixGenerateParams[actualName] = p;
                    } else {
                        Nuke.nk_layout_row_dynamic(ctx, LineHeight, is3x4 ? 3 : 4);
                        RenderPropertyElement("#xx", cpi.Info, ref m.M11, ref changed);
                        RenderPropertyElement("#xy", cpi.Info, ref m.M12, ref changed);
                        RenderPropertyElement("#xz", cpi.Info, ref m.M13, ref changed);
                        if (!is3x4)
                            RenderPropertyElement("#xw", cpi.Info, ref m.M14, ref changed);
                        RenderPropertyElement("#yx", cpi.Info, ref m.M21, ref changed);
                        RenderPropertyElement("#yy", cpi.Info, ref m.M22, ref changed);
                        RenderPropertyElement("#yz", cpi.Info, ref m.M23, ref changed);
                        if (!is3x4)
                            RenderPropertyElement("#yw", cpi.Info, ref m.M24, ref changed);
                        RenderPropertyElement("#zx", cpi.Info, ref m.M31, ref changed);
                        RenderPropertyElement("#zy", cpi.Info, ref m.M32, ref changed);
                        RenderPropertyElement("#zz", cpi.Info, ref m.M33, ref changed);
                        if (!is3x4)
                            RenderPropertyElement("#zw", cpi.Info, ref m.M34, ref changed);
                        RenderPropertyElement("#wx", cpi.Info, ref m.M41, ref changed);
                        RenderPropertyElement("#wy", cpi.Info, ref m.M42, ref changed);
                        RenderPropertyElement("#wz", cpi.Info, ref m.M43, ref changed);
                        if (!is3x4)
                            RenderPropertyElement("#ww", cpi.Info, ref m.M44, ref changed);
                    }

                    if (changed) {
                        cpi.Setter(instance, m);
                        return true;
                    }
                }
            }
            return false;
        }

        private unsafe bool RenderTextureProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, object value
        ) {
            var ctx = Nuklear.Context;
            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                if (pGroup.Visible) {
                    var tex = (ParticleTexture)value;
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 2);
                    if (Nuklear.Button("Select")) {
                        Controller.SelectTexture(cpi, instance, tex);
                        changed = false;
                        return false;
                    }
                    if (Nuklear.Button("Erase")) {
                        tex.Texture = new NullableLazyResource<Texture2D>();
                        cpi.Setter(instance, tex);
                        changed = true;
                        return true;
                    }
                    
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 1);
                    Nuke.nk_label_wrap(ctx, tex.Texture.Name != null ? Path.GetFileName(tex.Texture.Name) : "none");
                }
            }
            return false;
        }

        private unsafe bool RenderListProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, object _list, bool itemsAreValues
        ) {
            var ctx = Nuklear.Context;
            var list = (System.Collections.IList)_list;
            var itemType = _list.GetType().GetGenericArguments()[0];

            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                if (pGroup.Visible) {
                    PropertyGridCache pgc;
                    if (!GridCaches.TryGetValue(actualName, out pgc))
                        pgc = new PropertyGridCache();

                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 3);
                    var indexChanged = Nuklear.Property("##", ref pgc.SelectedIndex, 0, list.Count - 1, 1, 1);
                    var canAdd = (list.Count < cpi.Info.MaxCount.GetValueOrDefault(999));
                    var canRemove = (list.Count > 0);
                    if (Nuklear.Button("Add", canAdd)) {
                        object newItem;
                        var gdv = cpi.Info.GetDefaultValue;
                        if (gdv != null)
                            newItem = gdv(instance);
                        else
                            newItem = Activator.CreateInstance(itemType);

                        list.Add(newItem);
                        pgc.SelectedIndex = list.Count - 1;
                        changed = true;
                    }
                    if (Nuklear.Button("Remove", canRemove)) {
                        list.RemoveAt(pgc.SelectedIndex);
                        changed = true;
                    }

                    if (pgc.SelectedIndex >= list.Count)
                        pgc.SelectedIndex--;
                    if (pgc.SelectedIndex < 0)
                        pgc.SelectedIndex = 0;

                    if (pgc.SelectedIndex < list.Count) {
                        var item = list[pgc.SelectedIndex];
                        if (item != null) {
                            pgc.Prepare(item.GetType());
                            if (itemsAreValues) {
                                var box = new ElementBox { Value = item };
                                if (RenderProperty(ref pgc, cpi.ElementInfo, box)) {
                                    list[pgc.SelectedIndex] = box.Value;
                                    changed = true;
                                }
                            } else {
                                if (RenderPropertyGridNonScrolling(item, ref pgc)) {
                                    list[pgc.SelectedIndex] = item;
                                    changed = true;
                                }
                            }
                        }
                    }

                    GridCaches[actualName] = pgc;
                }
            }

            if (changed)
                cpi.Setter(instance, list);
            return changed;
        }

        private unsafe bool RenderColorProperty (
            CachedPropertyInfo cpi, object instance, out bool changed, 
            object value
        ) {
            changed = false;
            var ctx = Nuklear.Context;
            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, cpi.Name, false)) {
                if (pGroup.Visible) {
                    var c = (Vector4)value;
                    var oldColor = new NuklearDotNet.nk_colorf {
                        r = c.X,
                        g = c.Y,
                        b = c.Z,
                        a = c.W,
                    };
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 2);
                    var resetToTransparent = Nuklear.Button("Transparent");
                    var resetToWhite = Nuklear.Button("White");
                    Nuke.nk_layout_row_dynamic(ctx, 96, 1);
                    var temp = Nuke.nk_color_picker(ctx, oldColor, NuklearDotNet.nk_color_format.NK_RGBA);
                    var newColor = resetToWhite 
                        ? Vector4.One 
                        : resetToTransparent 
                            ? Vector4.Zero
                            : new Vector4(temp.r, temp.g, temp.b, temp.a);
                    if (newColor != c)
                        changed = true;
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 4);
                    RenderPropertyElement("#R", null, ref newColor.X, ref changed, 0, 1);
                    RenderPropertyElement("#G", null, ref newColor.Y, ref changed, 0, 1);
                    RenderPropertyElement("#B", null, ref newColor.Z, ref changed, 0, 1);
                    RenderPropertyElement("#A", null, ref newColor.W, ref changed, 0, 1);
                    if (changed) {
                        cpi.Setter(instance, newColor);
                        return true;
                    }
                }
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
