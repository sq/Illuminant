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
using Squared.Illuminant.Modeling;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Util;
using Nuke = NuklearDotNet.Nuklear;

namespace ParticleEditor {
    public partial class PropertyEditor {
        internal class KeyboardInput : System.Windows.Forms.IMessageFilter {
            public struct Deactivation : IDisposable {
                public KeyboardInput This;

                public void Dispose () {
                    This.DeactivateCount--;
                    if (This.DeactivateCount == 0)
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

            private int DeactivateCount = 0;

            public KeyboardInput (ParticleEditor game) {
                Game = game;
            }

            public void Install () {
                System.Windows.Forms.Application.AddMessageFilter(this);
            }

            public Deactivation Deactivate () {
                this.DeactivateCount++;
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

        private class PropertyGridCache {
            public Type CachedType;
            public List<CachedPropertyInfo> Members;

            public uint ScrollX, ScrollY;
            public int SelectedIndex;

            public ElementBox Box;

            internal bool Prepare (object instance, Type type = null) {
                if (type == null)
                    type = instance.GetType();
                if (type == CachedType)
                    return false;

                CachedType = type;
                Members = CachePropertyInfo(type).ToList();
                SelectedIndex = 0;
                Box = new ElementBox();
                return true;
            }
        }

        private readonly Dictionary<Type, List<CachedPropertyInfo>> CachedMembers =
            new Dictionary<Type, List<CachedPropertyInfo>>(new ReferenceComparer<Type>());
        private readonly Dictionary<string, PropertyGridCache> GridCaches = 
            new Dictionary<string, PropertyGridCache>();
        private PropertyGridCache SystemProperties = new PropertyGridCache(), 
            TransformProperties = new PropertyGridCache();
        private List<Type> TransformTypes = GetTransformTypes().ToList();

        internal KeyboardInput KeyboardInputHandler;

        public readonly ParticleEditor Game;

        public NuklearService Nuklear {
            get {
                return Game.Nuklear;
            }
        }

        public EngineModel Model {
            get {
                return Game.Model;
            }
        }

        public Controller Controller {
            get {
                return Game.Controller;
            }
        }

        public GraphicsDeviceManager Graphics {
            get {
                return Game.Graphics;
            }
        }

        public int FrameBufferWidth {
            get {
                return Game.Graphics.PreferredBackBufferWidth;
            }
        }

        public int FrameBufferHeight {
            get {
                return Game.Graphics.PreferredBackBufferHeight;
            }
        }

        public PropertyEditor (ParticleEditor game) {
            Game = game;

            KeyboardInputHandler = new KeyboardInput(game);
            KeyboardInputHandler.Install();
        }

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
                    Setter = (i, v) => { ((ElementBox)i).Value = v; }
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
                   let enumValueNames = mtype.IsEnum ? mtype.GetEnumNames() : null
                   let isWritable = ((f != null) && !f.IsInitOnly) || ((p != null) && p.CanWrite)
                   let isGetItem = (p != null) && (p.GetIndexParameters().Length > 0)
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
                       ElementInfo = GetElementInfo(mtype),
                       EnumValueNames = enumValueNames,
                       IsGetItem = isGetItem
                   };
        }

        internal void RunWorkItem (Action workItem) {
            Game.Scheduler.QueueWorkItemForNextStep(() => {
                using (KeyboardInputHandler.Deactivate()) {
                    Game.RenderCoordinator.WaitForActiveDraws();
                    workItem();
                }
            });
        }

        private unsafe bool RenderPropertyGridNonScrolling (object instance, PropertyGridCache cache) {
            var result = false;

            foreach (var cpi in cache.Members) {
                if (RenderProperty(cache, cpi, instance))
                    result = true;
            }

            return result;
        }

        private unsafe bool RenderPropertyGrid (object instance, PropertyGridCache cache, float? heightPx) {
            if (heightPx.HasValue) {
                using (var g = Nuklear.ScrollingGroup(heightPx.Value, "Properties", ref cache.ScrollX, ref cache.ScrollY))
                if (g.Visible)
                    return RenderPropertyGridNonScrolling(instance, cache);
                else
                    return false;
            } else {
                return RenderPropertyGridNonScrolling(instance, cache);
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

        private unsafe bool RenderPropertyElement (
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
                return true;
            }

            return false;
        }

        private bool IsPropertySelected (
            object instance, string actualName
        ) {
            return (Controller.SelectedPositionProperty != null) &&
                object.ReferenceEquals(Controller.SelectedPositionProperty.Instance, instance) &&
                (Controller.SelectedPositionProperty.Key == actualName);
        }

        private bool SelectProperty (
            CachedPropertyInfo cpi, object instance, string actualName
        ) {
            switch (cpi.Info.Type) {
                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Matrix":
                    break;
                default:
                    return false;
            }

            float scale = cpi.Info.DragScale.GetValueOrDefault(1.0f);
            Controller.SelectedPositionProperty = new Controller.PositionPropertyInfo {
                Key = actualName,
                Instance = instance,
                Set = (xy) =>
                    TrySetPropertyPosition(cpi, instance, xy * scale),
                Get = () =>
                    TryGetPropertyPosition(cpi, instance) / scale
            };
            return true;
        }

        private void SelectProperty (object instance, string key, Action<Vector2> set, Func<Vector2?> get) {
            Controller.SelectedPositionProperty = new Controller.PositionPropertyInfo {
                Instance = instance,
                Key = key,
                Set = set,
                Get = get
            };
        }

        private Vector2? TryGetPropertyPosition (CachedPropertyInfo cpi, object instance) {
            var valueType = cpi.Info.Type ?? cpi.Type.Name;
            var value = cpi.Getter(instance);
            switch (valueType) {
                case "Vector2":
                    return (Vector2)value;
                case "Vector3":
                    var v3 = (Vector3)value;
                    return new Vector2(v3.X, v3.Y);
                case "Vector4":
                    var v4 = (Vector4)value;
                    return new Vector2(v4.X, v4.Y);
                case "Matrix":
                    var m = (Matrix)value;
                    return new Vector2(m.M41, m.M42);
            }

            return null;
        }

        private bool TrySetPropertyPosition (CachedPropertyInfo cpi, object instance, Vector2 xy) {
            var valueType = cpi.Info.Type ?? cpi.Type.Name;

            var value = cpi.Getter(instance);
            switch (valueType) {
                case "Vector2":
                    cpi.Setter(instance, xy);
                    return true;
                case "Vector3":
                    var v3 = (Vector3)value;
                    v3.X = xy.X;
                    v3.Y = xy.Y;
                    cpi.Setter(instance, v3);
                    return true;
                case "Vector4":
                    var v4 = (Vector4)value;
                    v4.X = xy.X;
                    v4.Y = xy.Y;
                    cpi.Setter(instance, v4);
                    return true;
                case "Matrix":
                    var m = (Matrix)value;
                    m.M41 = xy.X;
                    m.M42 = xy.Y;
                    cpi.Setter(instance, m);
                    return true;
            }

            return false;
        }

        private unsafe bool RenderFormula (string name, string actualName, Formula value, bool isColor) {
            var result = false;

            using (var pGroup = Nuklear.CollapsingGroup(name, actualName, false)) {
                if (pGroup.Visible) {
                    Nuke.nk_layout_row_dynamic(Nuklear.Context, LineHeight, 3);
                    if (Nuklear.Button("Zero")) {
                        value.SetToConstant(Vector4.Zero);
                        result = true;
                    }
                    if (Nuklear.Button("One")) {
                        value.SetToConstant(Vector4.One);
                        result = true;
                    }
                    if (Nuklear.Button("Unit Normal")) {
                        value.SetToUnitNormal();
                        result = true;
                    }

                    var spp = Controller.SelectedPositionProperty;
                    var isFormulaSelected = (spp != null) && object.ReferenceEquals(spp.Instance, value);

                    Nuke.nk_layout_row_dynamic(Nuklear.Context, LineHeight, 1);
                    if (Nuklear.SelectableText("Constant", isFormulaSelected && (spp.Key == "Constant")))
                        SelectProperty(
                            value, "Constant",
                            (v) => { value.Constant.X = v.X; value.Constant.Y = v.Y; },
                            () => new Vector2(value.Constant.X, value.Constant.Y)
                        );

                    RenderVectorProperty(null, ref value.Constant, ref result, isColor);

                    Nuke.nk_layout_row_dynamic(Nuklear.Context, LineHeight, 1);
                    if (Nuklear.SelectableText("Scale", isFormulaSelected && (spp.Key == "Scale")))
                        SelectProperty(
                            value, "Scale",
                            (v) => { value.RandomScale.X = v.X; value.RandomScale.Y = v.Y; },
                            () => new Vector2(value.RandomScale.X, value.RandomScale.Y)
                        );

                    RenderVectorProperty(null, ref value.RandomScale, ref result, isColor);

                    var k = value.Circular ? "Constant Radius" : "Random Offset";
                    Nuke.nk_layout_row_dynamic(Nuklear.Context, LineHeight, 1);
                    if (Nuklear.SelectableText(k, isFormulaSelected && (spp.Key == k))) {
                        float scale = value.Circular ? 10f : 200f;
                        var off = (value.Circular ? 0 : 0.5f);
                        SelectProperty(
                            value, k,
                            (v) => {
                                value.Offset.X = (v.X / scale) - off;
                                value.Offset.Y = (v.Y / scale) - off;
                                if (!value.Circular) {
                                    value.Offset.X = Arithmetic.Clamp(value.Offset.X, -1, 0);
                                    value.Offset.Y = Arithmetic.Clamp(value.Offset.Y, -1, 0);
                                }
                            },
                            () => {
                                var res = new Vector2(value.Offset.X + off, value.Offset.Y + off);
                                return res * scale;
                            }
                        );
                    }

                    RenderVectorProperty(null, ref value.Offset, ref result, isColor);

                    Nuke.nk_layout_row_dynamic(Nuklear.Context, LineHeight, 1);
                    if (Checkbox("Circular", ref value.Circular))
                        result = true;
                }
            }

            return result;
        }

        private unsafe bool RenderGenericObjectProperty (
            PropertyGridCache cache, CachedPropertyInfo cpi,
            object instance, object value, string actualName
        ) {
            bool changed = false;
            List<CachedPropertyInfo> members;
            if (!CachedMembers.TryGetValue(cpi.Type, out members))
                CachedMembers[cpi.Type] = members = CachePropertyInfo(cpi.Type).ToList();

            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                if (pGroup.Visible) {
                    foreach (var i in members) 
                        if (RenderProperty(cache, i, value, cpi.Name))
                            changed = true;

                    if (changed)
                        cpi.Setter(instance, value);
                }
                return changed;
            }
        }

        private unsafe bool RenderProperty (
            PropertyGridCache cache,
            CachedPropertyInfo cpi,
            object instance,
            string prefix = null
        ) {
            if (cpi.IsGetItem)
                return false;

            bool changed = false, b;
            var ctx = Nuklear.Context;
            var actualName = cpi.Name;
            if (!string.IsNullOrEmpty(prefix))
                actualName = prefix + actualName;

            var isActive = IsPropertySelected(instance, actualName);
            var value = cpi.Getter(instance);

            var valueType = cpi.Info.Type ?? cpi.Type.Name;

            switch (valueType) {
                case "List":
                    return RenderListProperty(cpi, instance, ref changed, actualName, value, false);

                case "ValueList":
                    return RenderListProperty(cpi, instance, ref changed, actualName, value, true);

                case "ColorFormula":
                case "Formula":
                    return RenderFormula(cpi.Name, actualName, (Formula)value, valueType.StartsWith("Color"));

                case "NullableLazyResource`1":
                    return RenderTextureProperty(cpi, instance, ref changed, actualName, value);
                
                case "ParticleAppearance":
                case "ParticleColor":
                case "FMAParameters`1":
                    return RenderGenericObjectProperty(cache, cpi, instance, value, actualName);

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
            if (Nuklear.SelectableText(cpi.Name, isActive))
                SelectProperty(cpi, instance, actualName);

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
                Nuklear.Label("null", isActive);
                return false;
            }

            switch (valueType) {
                case "TransformArea":
                case "ParticleColorLifeRamp":
                    return RenderGenericObjectProperty(cache, cpi, instance, value, actualName);

                case "Bezier2":
                case "Bezier4":
                case "ColorBezier4":
                    return RenderBezierProperty(cpi, instance, actualName, value, valueType.StartsWith("Color"));

                case "String":
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight + 3, 1);
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
                    var v2 = (Vector2)value;
                    RenderVectorProperty(cpi, ref v2, ref changed);
                    if (changed)
                        cpi.Setter(instance, v2);
                    return changed;

                case "Normal":
                case "Vector3":
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 3);
                    var v3 = (Vector3)value;
                    if (valueType == "Normal") {
                        if (Nuklear.Button("X")) {
                            v3 = Vector3.UnitX;
                            changed = true;
                        }
                        if (Nuklear.Button("Y")) {
                            v3 = Vector3.UnitY;
                            changed = true;
                        }
                        if (Nuklear.Button("Z")) {
                            v3 = Vector3.UnitZ;
                            changed = true;
                        }
                    }
                    RenderPropertyElement("#x", cpi.Info, ref v3.X, ref changed);
                    RenderPropertyElement("#y", cpi.Info, ref v3.Y, ref changed);
                    RenderPropertyElement("#z", cpi.Info, ref v3.Z, ref changed);
                    if (changed) {
                        cpi.Setter(instance, v3);
                        return true;
                    }
                    return false;
                
                case "Vector4":
                    var v4 = (Vector4)value;
                    RenderVectorProperty(cpi, ref v4, ref changed, false);
                    if (changed)
                        cpi.Setter(instance, v4);
                    return changed;
                
                default:
                    if (cpi.Type.IsEnum) {
                        var names = cpi.EnumValueNames;
                        var name = Enum.GetName(cpi.Type, value);
                        var selectedIndex = Array.IndexOf(names, name);
                        if (Nuklear.ComboBox(ref selectedIndex, (i) => names[i], names.Length)) {
                            var newName = names[selectedIndex];
                            var newValue = Enum.Parse(cpi.Type, newName, true);
                            cpi.Setter(instance, newValue);
                            return true;
                        }
                    } else {
                        Nuklear.Label(value.GetType().Name);
                    }
                    return false;
            }
        }

        private unsafe bool RenderVectorProperty (CachedPropertyInfo cpi, ref Vector2 v2, ref bool changed) {
            Nuke.nk_layout_row_dynamic(Nuklear.Context, LineHeight, 2);
            var a = RenderPropertyElement("#x", cpi?.Info, ref v2.X, ref changed);
            var b = RenderPropertyElement("#y", cpi?.Info, ref v2.Y, ref changed);
            return a || b;
        }

        private unsafe bool RenderVectorProperty (CachedPropertyInfo cpi, ref Vector4 v4, ref bool changed, bool isColor) {
            Nuke.nk_layout_row_dynamic(Nuklear.Context, LineHeight, 4);
            var a = RenderPropertyElement(isColor ? "#r" : "#x", cpi?.Info, ref v4.X, ref changed);
            var b = RenderPropertyElement(isColor ? "#g" : "#y", cpi?.Info, ref v4.Y, ref changed);
            var c = RenderPropertyElement(isColor ? "#b" : "#z", cpi?.Info, ref v4.Z, ref changed);
            var d = RenderPropertyElement(isColor ? "#a" : "#w", cpi?.Info, ref v4.W, ref changed);
            return a || b || c || d;
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

                        var isSelected = IsPropertySelected(instance, "Constant");
                        Nuke.nk_layout_row_dynamic(ctx, LineHeight, 2);
                        if (Nuklear.SelectableText("Constant", isSelected))
                            SelectProperty(cpi, instance, "Constant");
                        if (Nuklear.Button("Zero")) {
                            m.M41 = m.M42 = m.M43 = 0;
                            changed = true;
                        }
                        Nuke.nk_layout_row_dynamic(ctx, LineHeight, 3);
                        RenderPropertyElement("#wx", cpi.Info, ref m.M41, ref changed);
                        RenderPropertyElement("#wy", cpi.Info, ref m.M42, ref changed);
                        RenderPropertyElement("#wz", cpi.Info, ref m.M43, ref changed);
                    }

                    if (changed) {
                        cpi.Setter(instance, m);
                        return true;
                    }
                }
            }
            return false;
        }

        private unsafe bool RenderBezierProperty (
            CachedPropertyInfo cpi, object instance,
            string actualName, object value, bool isColor
        ) {
            bool changed = false;

            var ctx = Nuklear.Context;
            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                if (pGroup.Visible) {
                    var b = (IBezier)value;

                    if (b.Count > 1) {
                        Bounds panel;
                        if (Nuklear.CustomPanel(180, out panel)) {
                            var m = Game.ScreenSpaceBezierVisualizer;
                            using (var pb = PrimitiveBatch<VertexPositionColorTexture>.New(
                                Nuklear.PendingGroup, 9999, m, (dm, _) => {
                                    var cb = new Squared.Illuminant.Uniforms.ClampedBezier4(b);
                                    Game.Materials.TrySetBoundUniform(m, "Bezier", ref cb);
                                }
                            )) {
                                var tl = new VertexPositionColorTexture(new Vector3(panel.TopLeft, 0), Color.White, Vector2.Zero);
                                var tr = new VertexPositionColorTexture(new Vector3(panel.TopRight, 0), Color.White, new Vector2(1, 0));
                                var bl = new VertexPositionColorTexture(new Vector3(panel.BottomLeft, 0), Color.White, new Vector2(0, 1));
                                var br = new VertexPositionColorTexture(new Vector3(panel.BottomRight, 0), Color.White, Vector2.One);
                                var verts = new[] { tl, tr, bl, tr, br, bl };
                                var pdc = new PrimitiveDrawCall<VertexPositionColorTexture>(
                                    PrimitiveType.TriangleList, verts, 0, 2
                                );
                                pb.Add(ref pdc);
                            }
                        }
                    }

                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 3);

                    var cnt = b.Count;
                    if (Nuklear.Property("#Count", ref cnt, 1, 4, 1, 1)) {
                        // Copy existing row when adding new one
                        if ((b.Count < cnt) && (cnt > 1))
                            b[cnt - 1] = b[cnt - 2];
                        b.Count = cnt;
                        changed = true;
                    }

                    var val = b.MinValue;
                    if (RenderPropertyElement("#Min", null, ref val, ref changed))
                        b.MinValue = val;

                    val = b.MaxValue;
                    if (RenderPropertyElement("#Max", null, ref val, ref changed))
                        b.MaxValue = val;

                    for (int i = 0; i < cnt; i++) {
                        var elt = b[i];
                        if (elt is Vector2) {
                            var v2 = (Vector2)elt;
                            if (RenderVectorProperty(null, ref v2, ref changed))
                                b[i] = v2;
                        } else if (elt is Vector4) {
                            var v4 = (Vector4)elt;
                            if (RenderVectorProperty(null, ref v4, ref changed, isColor))
                                b[i] = v4;
                        }
                    }
                }
            }

            return changed;
        }

        private unsafe bool RenderTextureProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, object _value
        ) {
            var ctx = Nuklear.Context;
            var value = (NullableLazyResource<Texture2D>)_value;
            var hasValue = (value != null) && (value.Name != null);
            Nuke.nk_layout_row_dynamic(ctx, LineHeight, 2);
            if (Nuklear.Button("Select Image")) {
                Controller.SelectTexture(cpi, instance, value);
                changed = false;
            }
            if (Nuklear.Button("Erase", hasValue)) {
                value = new NullableLazyResource<Texture2D>();
                cpi.Setter(instance, value);
                changed = true;
            }
                    
            Nuke.nk_layout_row_dynamic(ctx, LineHeight, 1);
            Nuke.nk_label_wrap(
                ctx, string.Format(
                    "{0}: {1}",
                    cpi.Name, hasValue ? Path.GetFileName(value.Name) : "none"
                )
            );
            return changed;
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
                        GridCaches[actualName] = pgc = new PropertyGridCache();

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
                            if (pgc.Prepare(item) && itemsAreValues) {
                                cpi.ElementInfo.Getter = (i) => {
                                    if (pgc.SelectedIndex < list.Count)
                                        return list[pgc.SelectedIndex];
                                    else
                                        return null;
                                };
                                cpi.ElementInfo.Setter = (i, v) => {
                                    pgc.Box.Value = v;
                                    list[pgc.SelectedIndex] = v;
                                };
                            }
                            if (itemsAreValues) {
                                pgc.Box.Value = item;
                                if (RenderProperty(pgc, cpi.ElementInfo, pgc.Box)) {
                                    list[pgc.SelectedIndex] = pgc.Box.Value;
                                    changed = true;
                                }
                            } else {
                                if (RenderPropertyGridNonScrolling(item, pgc)) {
                                    list[pgc.SelectedIndex] = item;
                                    changed = true;
                                }
                            }
                        }
                    }
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

    internal class CachedPropertyInfo {
        public string Name;
        public PropertyEditor.ModelTypeInfo Info;
        public FieldInfo Field;
        public PropertyInfo Property;
        public Type RawType, Type;
        public Func<object, object> Getter;
        public Action<object, object> Setter;
        public bool AllowNull, IsGetItem;
        public CachedPropertyInfo ElementInfo;
        public string[] EnumValueNames;
    }
}
