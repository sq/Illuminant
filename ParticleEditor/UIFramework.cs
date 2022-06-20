using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Game.Input;
using Squared.Illuminant;
using Squared.Illuminant.Configuration;
using Squared.Illuminant.Modeling;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Util;
using Nuke = NuklearDotNet.Nuklear;

namespace Lumined {
    public partial class PropertyEditor {
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

        private static XmlDocument IlluminantXml;
        private static readonly Dictionary<string, string> SummaryCache = new Dictionary<string, string>();
        private readonly Dictionary<Type, List<CachedPropertyInfo>> CachedMembers =
            new Dictionary<Type, List<CachedPropertyInfo>>(new ReferenceComparer<Type>());
        private readonly Dictionary<string, PropertyGridCache> GridCaches = 
            new Dictionary<string, PropertyGridCache>();
        private PropertyGridCache SystemProperties = new PropertyGridCache(), 
            TransformProperties = new PropertyGridCache(),
            DocumentProperties = new PropertyGridCache();
        private List<Type> TransformTypes = GetTransformTypes().ToList();

        internal KeyboardInputProvider KeyboardInputHandler;
        internal Stack<string> NameStack = new Stack<string>();

        public readonly EditorGame Game;

        private ILookup<string, CachedPropertyInfo> Formula1Properties = CachePropertyInfo(typeof(Formula1)).ToLookup(cpi => cpi.Name);
        private ILookup<string, CachedPropertyInfo> Formula3Properties = CachePropertyInfo(typeof(Formula3)).ToLookup(cpi => cpi.Name);
        private ILookup<string, CachedPropertyInfo> Formula4Properties = CachePropertyInfo(typeof(Formula4)).ToLookup(cpi => cpi.Name);

        private readonly string[] PrefixedBezierElementNames = new[] { "#A", "#B", "#C", "#D" };
        private readonly string[] BezierElementNames = new[] { "A", "B", "C", "D" };
        private readonly Dictionary<BezierM, int> BezierSelectedRows = new Dictionary<BezierM, int>();

        internal bool TransformSortRequired = false;

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

        public PropertyEditor (EditorGame game) {
            Game = game;

            KeyboardInputHandler = new KeyboardInput();
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
                if (FieldTypeOverrides.TryGetValue(t.Name, out d)) {
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

        internal static Type GetElementType (Type type) {
            if (type.IsArray)
                return type.GetElementType();

            foreach (var iface in type.GetInterfaces()) {
                if (iface.Name != "IList`1")
                    continue;
                if (iface.Namespace != "System.Collections.Generic")
                    continue;

                var ga = iface.GetGenericArguments();
                if ((ga == null) || (ga.Length == 0))
                    continue;

                return ga[0];
            }

            return null;
        }

        private static CachedPropertyInfo GetElementInfo (Type type) {
            var elementType = GetElementType(type);
            if (elementType == null)
                return null;

            var info = GetInfoForField(type, "Item", elementType);
            return new CachedPropertyInfo {
                Name = "Value",
                Info = info,
                Field = null,
                Property = null,
                RawType = elementType,
                Type = elementType,
                AllowNull = false,
                IsWritable = true,
                Getter = (i) => ((ElementBox)i).Value,
                Setter = (i, v) => { ((ElementBox)i).Value = v; }
            };
        }

        private static string GetSummaryForMember (MemberInfo m) {
            var key = string.Format("{0}:{1}.{2}", m is FieldInfo ? "F" : "P", m.DeclaringType.FullName, m.Name);
            string result;
            if (SummaryCache.TryGetValue(key, out result))
                return result;

            if (IlluminantXml == null) {
                IlluminantXml = new XmlDocument();
                if (File.Exists("Illuminant.xml"))
                using (var s = File.OpenRead("Illuminant.xml"))
                    IlluminantXml.Load(s);
            }

            var node = IlluminantXml.SelectSingleNode(string.Format(
                "//member[starts-with(@name, '{0}')]/summary", key
            ));
            if (node != null) {
                var text = Regex.Replace(node.InnerText, "[\r\n \t]+", " ").Trim();
                result = text;
            } else
                result = null;
            SummaryCache[key] = result;
            return result;
        }

        private static int CountParentTypes (Type type) {
            Type t = type;
            int result = 0;
            while (t != null) {
                t = t.BaseType;
                result++;
            }
            return result;
        }

        private static IEnumerable<CachedPropertyInfo> CachePropertyInfo (Type type) {
            var members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public);
            return from m in members
                   where (m.MemberType == MemberTypes.Field) || (m.MemberType == MemberTypes.Property)
                   let f = m as FieldInfo
                   let p = m as PropertyInfo
                   let _mtype = (f != null) ? f.FieldType : p.PropertyType
                   let isNullable = _mtype.Name == "Nullable`1"
                   let allowNull = _mtype.IsClass || isNullable
                   let mtype = isNullable ? _mtype.GetGenericArguments()[0] : _mtype
                   where (mtype != typeof(Type))
                   let info = GetInfoForField(type, m.Name, mtype)
                   where m.Name != "IsActive2"
                   let isList = (info.Type == "List") || (info.Type == "ValueList")
                   let enumValueNames = mtype.IsEnum ? mtype.GetEnumNames() : null
                   let isWritable = ((f != null) && !f.IsInitOnly) || ((p != null) && p.CanWrite && (p.GetSetMethod()?.IsPublic ?? false))
                   let isGetItem = (p != null) && (p.GetIndexParameters().Length > 0)
                   let summary = GetSummaryForMember(m)
                   where (f == null) || !f.IsInitOnly || isList
                   where (p == null) || p.CanRead || isList
                   where !m.GetCustomAttributes<NonSerializedAttribute>().Any()
                   orderby CountParentTypes(m.DeclaringType)
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
                       IsGetItem = isGetItem,
                       IsWritable = isWritable,
                       Summary = summary
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
            string key, ModelTypeInfo? info, ref float value, ref bool changed, float? min = null, float? max = null, string tooltip = null
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
            var _max = max.GetValueOrDefault(_info.Max.GetValueOrDefault(81920));
            if (Nuklear.Property(key, ref value, _min, _max, step, inc, tooltip: tooltip)) {
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
            return (Controller.SelectedProperty != null) &&
                object.ReferenceEquals(Controller.SelectedProperty.Instance, instance) &&
                (Controller.SelectedProperty.Key == actualName);
        }

        private Controller.PositionPropertyInfo SelectProperty (object instance, string key, Vector2 value) {
            var result = new Controller.PositionPropertyInfo {
                Instance = instance,
                Key = key,
                CurrentValue = value,
                DisplayName = PickName(key)
            };
            Controller.SelectedProperty = Controller.NewSelectedProperty = result;
            return result;
        }

        private bool TickSelectableProperty (
            object instance, string actualName, ref Vector2 v2
        ) {
            var sp = Controller.SelectedProperty;

            var isSelected = IsPropertySelected(instance, actualName);
            if (Nuklear.SelectableText(actualName, isSelected)) {
                isSelected = true;
                sp = SelectProperty(instance, actualName, v2);
            }

            if (!isSelected)
                return false;

            Controller.NewSelectedProperty = sp;
            if (sp?.NewValue != null) {
                sp.CurrentValue = v2 = sp.NewValue.Value;
                sp.NewValue = null;
                return true;
            }
            return false;
        }

        private bool TickSelectableProperty (
            CachedPropertyInfo cpi, object instance, string actualName, ref IParameter value
        ) {
            if (value.IsConstant) {
                object v = value.Constant;
                var result = TickSelectableProperty(cpi, instance, actualName, ref v);
                if (result)
                    value.Constant = v;
                return result;
            } else {
                Nuklear.Label(actualName ?? cpi?.Name);
                return false;
            }
        }

        private bool TickSelectableProperty (
            CachedPropertyInfo cpi, object instance, string actualName, ref object value
        ) {
            var canSelect = false;
            string typeName = null;

            if (cpi != null)
                typeName = cpi.Type.Name;
            if (cpi?.Info != null)
                typeName = cpi.Info.Type;

            if (typeName == "Parameter`1") {
                var eleType = cpi.Type.GetGenericArguments()[0];
                typeName = eleType.Name;
            }

            if (typeName == null) {
                if (value == null)
                    typeName = "null";
                else
                    typeName = value.GetType().Name;
            }

            canSelect = CanGetV2FromType(typeName);

            if (canSelect) {
                Vector2? _v2 = null;
                if (cpi != null)
                    _v2 = TryGetPropertyPosition(cpi, instance);
                if (_v2 == null)
                    _v2 = GetV2FromValue(value, typeName);
                if (!_v2.HasValue)
                    canSelect = false;

                if (!canSelect) {
                    Nuklear.Label(actualName ?? cpi?.Name);
                    return false;
                }

                var v2 = _v2.Value;

                if (TickSelectableProperty(instance, actualName, ref v2)) {
                    var result = false;
                    if (cpi != null)
                        result = TrySetPropertyPosition(ref value, cpi, instance, v2);

                    if (result == false)
                        result = SetV2IntoValue(ref value, typeName, v2);

                    // FIXME: Apply min/max clamping

                    return result;
                }
            } else {
                Nuklear.Label(actualName ?? cpi?.Name);
            }

            return false;
        }

        private bool CanGetV2FromType (string typeName) {
            switch (typeName) {
                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Matrix":
                    return true;
                default:
                    return false;
            }
        }

        private Vector2? GetV2FromValue (object value, string typeName) {
            if (value == null)
                return null;

            switch (typeName) {
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
                default:
                    return null;
            }
        }

        private Vector2? TryGetPropertyPosition (CachedPropertyInfo cpi, object instance) {
            var valueType = cpi.Info.Type ?? cpi.Type.Name;
            var value = cpi.Getter(instance);
            return GetV2FromValue(value, valueType);
        }

        private bool SetV2IntoValue (ref object value, string typeName, Vector2 xy) {
            switch (typeName) {
                case "Vector2":
                    value = xy;
                    return true;
                case "Vector3":
                    var v3 = (Vector3)value;
                    v3.X = xy.X;
                    v3.Y = xy.Y;
                    value = v3;
                    return true;
                case "Vector4":
                    var v4 = (Vector4)value;
                    v4.X = xy.X;
                    v4.Y = xy.Y;
                    value = v4;
                    return true;
                case "Matrix":
                    var m = (Matrix)value;
                    m.M41 = xy.X;
                    m.M42 = xy.Y;
                    value = m;
                    return true;
            }

            return false;
        }

        private bool TrySetPropertyPosition (ref object value, CachedPropertyInfo cpi, object instance, Vector2 xy) {
            var valueType = cpi.Info.Type ?? cpi.Type.Name;

            var _value = cpi.Getter(instance);
            if (SetV2IntoValue(ref _value, valueType, xy)) {
                cpi.Setter(instance, _value);
                value = _value;
                return true;
            }

            return false;
        }

        private unsafe bool RenderFormula (CachedPropertyInfo cpi, object instance, string actualName, Formula1 value, bool isColor) {
            var result = false;
            var name = cpi.Name;

            using (var pGroup = Nuklear.CollapsingGroup(name, actualName, false, tooltip: cpi.Summary)) {
                if (pGroup.Visible) {
                    Nuklear.NewRow(LineHeight, 2);
                    if (Nuklear.Button("Zero")) {
                        value.SetToConstant(0);
                        result = true;
                    }
                    if (Nuklear.Button("One")) {
                        value.SetToConstant(1);
                        result = true;
                    }

                    var spp = Controller.SelectedProperty;
                    var isFormulaSelected = (spp != null) && object.ReferenceEquals(spp.Instance, value);

                    bool changed = false;
                    var p = (IParameter)value.Constant;
                    if (RenderParameter(null, value, ref changed, "Constant", cpi.Info.Type, ref p, true))
                        value.Constant = (Parameter<float>)p;

                    p = value.RandomScale;
                    if (RenderParameter(null, value, ref changed, "Scale", cpi.Info.Type, ref p, true))
                        value.RandomScale = (Parameter<float>)p;

                    var k = "Random Offset";

                    p = value.Offset;
                    if (RenderParameter(null, value, ref changed, k, cpi.Info.Type, ref p, true))
                        value.Offset = (Parameter<float>)p;

                    if (changed)
                        cpi.Setter(instance, value);
                }
            }

            return result;
        }

        private unsafe bool RenderFormula (CachedPropertyInfo cpi, object instance, string actualName, Formula3 value, bool isColor) {
            var result = false;
            var name = cpi.Name;

            using (var pGroup = Nuklear.CollapsingGroup(name, actualName, false, tooltip: cpi.Summary)) {
                if (pGroup.Visible) {
                    Nuklear.NewRow(LineHeight, 3);
                    if (Nuklear.Button("Zero")) {
                        value.SetToConstant(Vector3.Zero);
                        result = true;
                    }
                    if (Nuklear.Button("One")) {
                        value.SetToConstant(Vector3.One);
                        result = true;
                    }
                    if (Nuklear.Button("Unit Normal")) {
                        value.SetToUnitNormal();
                        result = true;
                    }

                    var spp = Controller.SelectedProperty;
                    var isFormulaSelected = (spp != null) && object.ReferenceEquals(spp.Instance, value);

                    bool changed = false;
                    var p = (IParameter)value.Constant;
                    if (RenderParameter(null, value, ref changed, "Constant", cpi.Info.Type, ref p, true))
                        value.Constant = (Parameter<Vector3>)p;

                    p = value.RandomScale;
                    if (RenderParameter(null, value, ref changed, "Scale", cpi.Info.Type, ref p, true))
                        value.RandomScale = (Parameter<Vector3>)p;

                    var k = (value.Type != FormulaType.Linear) ? "Constant Radius" : "Random Offset";

                    p = value.Offset;
                    if (RenderParameter(null, value, ref changed, k, cpi.Info.Type, ref p, true))
                        value.Offset = (Parameter<Vector3>)p;

                    Nuklear.NewRow(LineHeight, 1);
                    var t = (object)value.Type;
                    if (Nuklear.EnumCombo(ref t, tooltip: "Formula Type")) {
                        value.Type = (FormulaType)t;
                        result = true;
                    }

                    if (changed)
                        cpi.Setter(instance, value);
                }
            }

            return result;
        }

        private unsafe bool RenderFormula (CachedPropertyInfo cpi, object instance, string actualName, Formula4 value, bool isColor) {
            var result = false;
            var name = cpi.Name;

            using (var pGroup = Nuklear.CollapsingGroup(name, actualName, false, tooltip: cpi.Summary)) {
                if (pGroup.Visible) {
                    Nuklear.NewRow(LineHeight, 2);
                    if (Nuklear.Button("Zero")) {
                        value.SetToConstant(Vector4.Zero);
                        result = true;
                    }
                    if (Nuklear.Button("One")) {
                        value.SetToConstant(Vector4.One);
                        result = true;
                    }

                    var spp = Controller.SelectedProperty;
                    var isFormulaSelected = (spp != null) && object.ReferenceEquals(spp.Instance, value);

                    bool changed = false;
                    var p = (IParameter)value.Constant;
                    if (RenderParameter(null, value, ref changed, "Constant", cpi.Info.Type, ref p, true))
                        value.Constant = (Parameter<Vector4>)p;

                    p = value.RandomScale;
                    if (RenderParameter(null, value, ref changed, "Scale", cpi.Info.Type, ref p, true))
                        value.RandomScale = (Parameter<Vector4>)p;

                    var k = "Random Offset";

                    p = value.Offset;
                    if (RenderParameter(null, value, ref changed, k, cpi.Info.Type, ref p, true))
                        value.Offset = (Parameter<Vector4>)p;

                    if (changed)
                        cpi.Setter(instance, value);
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
            var type = cpi?.Type ?? value?.GetType();
            if (type == null)
                return false;
            if (!CachedMembers.TryGetValue(type, out members))
                CachedMembers[type] = members = CachePropertyInfo(type).ToList();

            var groupKey = PickName(actualName);

            if ((instance == null) || (value == null))
                return false;
            using (var pGroup = Nuklear.CollapsingGroup(cpi?.Name ?? actualName, groupKey, false, tooltip: cpi?.Summary)) {
                if (pGroup.Visible) {
                    foreach (var i in members) 
                        if (RenderProperty(cache, i, value, cpi?.Info.Type, cpi?.Name))
                            changed = true;

                    if (changed)
                        cpi?.Setter(instance, value);
                }
                return changed;
            }
        }

        private unsafe bool RenderProperty (
            PropertyGridCache cache,
            CachedPropertyInfo cpi,
            object instance,
            string parentType = null,
            string prefix = null
        ) {
            if (cpi.IsGetItem)
                return false;
            if (cpi?.Info.Hidden ?? false)
                return false;

            var actualName = cpi.Name;

            var value = cpi.Getter(instance);
            var valueType = cpi.Info.Type ?? cpi.Type.Name;

            try {
                NameStack.Push(actualName);

                return RenderPropertyInner(
                    cache, cpi, instance, parentType, prefix,
                    valueType, actualName, value
                );
            } finally {
                NameStack.Pop();
            }
        }

        private unsafe bool HandleNullable (
            CachedPropertyInfo cpi, object instance, string actualName, ref object value, ref bool changed
        ) {
            Nuklear.NewRow(LineHeight, 2);
            if (TickSelectableProperty(cpi, instance, actualName, ref value)) {
                changed = true;
                // Do we need to invoke this?
                cpi.Setter(instance, value);
            }

            if (cpi.AllowNull) {
                var isNull = value == null;
                if (isNull) {
                    if (Nuklear.Button("Create", tooltip: cpi.Summary)) {
                        var gdv = cpi?.Info.GetDefaultValue;
                        if (gdv != null)
                            value = gdv(instance);
                        else
                            value = Activator.CreateInstance(cpi.Type);
                        cpi.Setter(instance, value);
                        changed = true;
                    }
                    return true;
                } else {
                    if (Nuklear.Button("Erase", tooltip: cpi.Summary)) {
                        cpi.Setter(instance, value = null);
                        changed = true;
                        return true;
                    }
                }
            }

            return false;
        }

        private unsafe bool RenderPropertyInner (
            PropertyGridCache cache,
            CachedPropertyInfo cpi,
            object instance, string parentType, string prefix,
            string valueType, string actualName, object value
        ) {
            // HACK to avoid showing protected/internal bools with no setter
            if ((valueType == "Boolean") && cpi?.IsWritable == false)
                return false;

            var ctx = Nuklear.Context;
            bool changed = false, b;

            if (GenericNullableObjectTypes.Contains(valueType)) {
                if (HandleNullable(cpi, instance, actualName, ref value, ref changed))
                    return changed;
                return RenderGenericObjectProperty(cache, cpi, instance, value, actualName);
            }

            if (GenericObjectTypes.Contains(valueType)) {
                return RenderGenericObjectProperty(cache, cpi, instance, value, actualName);
            }

            var writable = cpi?.IsWritable ?? true;

            switch (valueType) {
                case "List":
                    return RenderListProperty(cpi, instance, ref changed, actualName, value, false);

                case "ValueList":
                    return RenderListProperty(cpi, instance, ref changed, actualName, value, true);

                case "Parameter`1": {
                    if (!writable)
                        break;

                    var p = (IParameter)value;
                    return RenderParameter(cpi, instance, ref changed, actualName, parentType, ref p, true);
                }

                case "ColorFormula":
                    return RenderFormula(cpi, instance, actualName, (Formula4)value, true);

                case "Formula1":
                    return RenderFormula(cpi, instance, actualName, (Formula1)value, true);

                case "Formula3":
                    return RenderFormula(cpi, instance, actualName, (Formula3)value, true);

                case "Formula4":
                    return RenderFormula(cpi, instance, actualName, (Formula4)value, true);

                case "ParticleSystemReference":
                    if (!writable)
                        return false;

                    return RenderSystemReferenceProperty(cpi, instance, ref changed, actualName, value);

                case "DirectoryName":
                    return RenderDirectoryNameProperty(cpi, instance, ref changed, actualName, value);

                case "NullableLazyResource`1":
                    if (!writable)
                        return false;

                    return RenderTextureProperty(cpi, instance, ref changed, actualName, value);

                case "Int32":
                case "Single":
                    if (!writable)
                        break;

                    if (!cpi.AllowNull || (value != null)) {
                        const float buttonSizePx = 26f;
                        var rl = new RowLayout {
                            {1.0f, true}
                        };
                        if (cpi.AllowNull)
                            rl.Add(buttonSizePx, false);
                        rl.Apply(Nuklear, LineHeight);

                        if (cpi.Type == typeof(float)) {
                            var v = (float)value;
                            RenderPropertyElement(cpi.Name, cpi.Info, ref v, ref changed, tooltip: cpi.Summary);
                            if (changed) {
                                cpi.Setter(instance, v);
                                return true;
                            }
                        } else {
                            var v = (int)value;
                            if (Nuklear.Property(
                                cpi.Name, ref v, 
                                (int)cpi.Info.Min.GetValueOrDefault(0), 
                                (int)cpi.Info.Max.GetValueOrDefault(81920), 
                                1, 0.5f, tooltip: cpi.Summary
                            )) {
                                cpi.Setter(instance, v);
                                return true;
                            }
                        }
                        if (cpi.AllowNull) {
                            if (ShowEraseButton()) {
                                cpi.Setter(instance, null);
                                return true;
                            }
                        }
                        return false;
                    }
                    break;

                case "Color":
                case "ColorF":
                    if (!writable)
                        break;

                    return RenderColorProperty(cpi, instance, out changed, value);

                case "Matrix":
                case "Matrix3x4":
                    var m = (Matrix)value;
                    var temp = false;
                    return RenderMatrixProperty(cpi, instance, ref changed, actualName, ref m, valueType.EndsWith("3x4"), ref temp, ref temp);
            }

            if (HandleNullable(cpi, instance, actualName, ref value, ref changed))
                return changed;

            if (value == null) {
                Nuklear.Label("null", false);
                return false;
            }

            switch (valueType) {
                case "TransformArea":
                case "ParticleColorLifeRamp":
                    return RenderGenericObjectProperty(cache, cpi, instance, value, actualName);

                case "BezierF":
                case "Bezier2":
                case "Bezier4":
                case "ColorBezier4":
                    return RenderBezierProperty(cpi, instance, actualName, value, null, valueType.StartsWith("Color"));
                
                case "String":
                    Nuklear.NewRow(LineHeight + 3, 1);
                    var text = value.ToString();
                    if (writable) {
                        if (Nuklear.Textbox(ref text, tooltip: cpi.Summary)) {
                            cpi.Setter(instance, text);
                            return true;
                        }
                    } else {
                        Nuklear.Label(text);
                    }
                    return false;

                case "Boolean":
                    b = (bool)value;
                    if (Nuklear.Checkbox(null, ref b, tooltip: cpi.Summary)) {
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
                    Nuklear.NewRow(LineHeight, 3);
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
                        return RenderEnumProperty(cpi, instance, value);
                    } else if (value.GetType().IsPrimitive) {
                        Nuklear.Label(value.ToString());
                    } else {
                        Nuklear.Label(value.GetType().Name);
                    }
                    return false;
            }
        }

        private unsafe bool RenderEnumProperty (CachedPropertyInfo cpi, object instance, object value) {
            if (!(cpi?.IsWritable ?? true)) {
                Nuklear.Label(value.ToString());
                return false;
            }

            if (Nuklear.EnumCombo(ref value, cpi.Type, cpi.EnumValueNames)) {
                cpi.Setter(instance, value);
                return true;
            }

            return false;
        }

        private unsafe bool ShowEraseButton () {
            return Nuklear.Button("×", tooltip: "Set to null");
        }

        private unsafe bool ShowNewVariableButton () {
            return Nuklear.Button("=", tooltip: "Create new variable");
        }

        private unsafe bool ShowConstantButton () {
            return Nuklear.Button("×", tooltip: "Remove variable reference");
        }

        private unsafe bool ShowReferenceButton () {
            return Nuklear.Button("=", tooltip: "Use variable");
        }

        private unsafe bool ShowBezierButton () {
            return Nuklear.Button("∑", tooltip: "Use bezier curve");
        }

        private unsafe bool ShowExpressionButton () {
            return Nuklear.Button("+", tooltip: "Use expression");
        }

        private string PickName (string suffix) {
            var result = string.Join(".", NameStack.Where(n => !string.IsNullOrWhiteSpace(n)).Reverse().Concat(new[] { suffix }).ToArray());
            return result;
        }

        private unsafe bool RenderParameter (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, string parentType, ref IParameter p, bool allowReferences
        ) {
            var valueType = p.ValueType;
            var isConstant = p.IsConstant;
            var isBezier = p.IsBezier;
            var isReference = p.IsReference;
            var isExpression = p.IsExpression;
            var isMatrix = valueType.Name.EndsWith("Matrix");
            var now = (float)Game.View.Time.Seconds;

            bool isColor = (parentType ?? "").StartsWith("Color");
            bool doBezierConversion = false, doReferenceConversion = false;

            var hasAvailableReference = Model.HasAnyConstantsOfType(valueType);
            var widths = new float[8];
            const float buttonSizePx = 26f;

            if (isBezier) {
                var b = p.Bezier;
                changed = RenderBezierProperty(cpi, null, actualName, b, now, isColor, false, true);
                if (changed && b.Count == 0)
                    p = p.ToConstant();
            } else if (isReference) {
                var layout = new RowLayout {
                    {0.4f },
                    {0.6f },
                    {buttonSizePx, false },
                    {buttonSizePx, false },
                    {buttonSizePx, false }
                };
                layout.Apply(Nuklear, LineHeight);
                var names = Model.ConstantNamesOfType(valueType).ToList();
                int selectedIndex = names.IndexOf(p.Name);
                if (selectedIndex >= 0) {
                    if (Nuklear.SelectableText(actualName, Controller.SelectedVariableName == p.Name))
                        Controller.SelectedVariableName = p.Name;
                } else {
                    Nuklear.Label(actualName, false);
                }
                if (Nuklear.ComboBox(ref selectedIndex, (i) => (i < 0) ? "" : names[i], names.Count)) {
                    changed = true;
                    if ((selectedIndex >= 0) && (selectedIndex < names.Count)) {
                        p.Name = names[selectedIndex];
                        // FIXME: This is annoying when building expressions
                        // Controller.SelectedVariableName = p.Name;
                    }
                }
                if (ShowExpressionButton()) {
                    changed = true;
                    p = p.ToExpression();
                }
                if (ShowNewVariableButton()) {
                    changed = true;
                    p.Name = Controller.AddVariable(valueType, PickName(actualName));
                    // Copy original constant
                    var def = Model.NamedVariables[p.Name];
                    def.DefaultValue.Constant = p.Constant;
                }
                if (ShowConstantButton()) {
                    changed = true;
                    p = p.ToConstant();
                }
            } else if (isExpression) {
                if (RenderGenericObjectProperty(null, cpi, p, p.Expression, actualName))
                    changed = true;
            } else {
                int eltCount;
                switch (valueType.Name) {
                    case "Single":
                        eltCount = 1;
                        break;
                    case "Vector2":
                        eltCount = 2;
                        break;
                    case "Vector3":
                        eltCount = 3;
                        break;
                    case "Vector4":
                    case "Matrix":
                        eltCount = 4;
                        break;
                    case "DynamicMatrix":
                        eltCount = 2;
                        break;
                    default:
                        throw new Exception();
                }
                var row = new RowLayout();
                for (int i = 0; i < eltCount; i++)
                    row.Add(1.0f / eltCount);
                row.Add(buttonSizePx, false);
                row.Add(buttonSizePx, false);
                if (allowReferences)
                    row.Add(buttonSizePx, false);

                if ((eltCount > 1) && !isMatrix) {
                    Nuklear.NewRow(LineHeight, 1);
                    if (TickSelectableProperty(cpi, instance, actualName, ref p))
                        changed = true;
                }

                if (!isMatrix)
                    row.Apply(Nuklear, LineHeight);

                switch (valueType.Name) {
                    case "Single":
                        var fp = (Parameter<float>)p;
                        var fc = fp.Constant;
                        if (RenderPropertyElement(actualName, cpi?.Info, ref fc, ref changed)) {
                            fp.Constant = fc;
                            p = fp;
                        }
                        break;
                    case "Vector2":
                        var v2p = (Parameter<Vector2>)p;
                        var v2c = v2p.Constant;
                        if (RenderVectorProperty(cpi, ref v2c, ref changed, false)) {
                            v2p.Constant = v2c;
                            p = v2p;
                        }
                        break;
                    case "Vector3":
                        var v3p = (Parameter<Vector3>)p;
                        var v3c = v3p.Constant;
                        if (RenderVectorProperty(cpi, ref v3c, ref changed, false)) {
                            v3p.Constant = v3c;
                            p = v3p;
                        }
                        break;
                    case "Vector4":
                        var v4p = (Parameter<Vector4>)p;
                        var v4c = v4p.Constant;
                        if (RenderVectorProperty(cpi, ref v4c, ref changed, isColor, false)) {
                            v4p.Constant = v4c;
                            p = v4p;
                        }
                        break;
                    case "DynamicMatrix":
                        var dmp = (Parameter<DynamicMatrix>)p;
                        var dmc = dmp.Constant;
                        doBezierConversion = true;
                        doReferenceConversion = allowReferences;
                        if (RenderMatrixProperty(cpi, null, ref changed, actualName, ref dmc, false, true, ref doBezierConversion, ref doReferenceConversion)) {
                            if (doReferenceConversion) {
                                dmp = dmp.ToReference();
                            } else {
                                dmp.Constant = dmc;
                            }
                            p = dmp;
                        }
                        break;
                    default:
                        throw new Exception();
                }

                if (!isMatrix) {
                    doBezierConversion = ShowBezierButton();
                    if (ShowExpressionButton()) {
                        changed = true;
                        p = p.ToExpression();
                    }
                    if (hasAvailableReference && allowReferences)
                        doReferenceConversion = ShowReferenceButton();
                    else if (allowReferences) {
                        if (ShowNewVariableButton()) {
                            changed = true;
                            p = p.ToReference();
                            p.Name = Controller.AddVariable(valueType, PickName(actualName));
                            // Copy original constant
                            var def = Model.NamedVariables[p.Name];
                            def.DefaultValue.Constant = p.Constant;
                        }
                    }
                }

                if (doBezierConversion) {
                    p = p.ToBezier();
                    changed = true;
                    // HACK to auto-open
                    RenderBezierProperty(cpi, null, actualName, p.Bezier, now, isColor, true, false);
                } else if (doReferenceConversion) {
                    p = p.ToReference();
                    changed = true;
                }
            }

            if (changed && (cpi != null))
                cpi.Setter(instance, p);
            return changed;
        }

        private void SetVectorRowLayout (int columnCount, bool eraseButton) {
            const float buttonSizePx = 26f;
            var rl = new RowLayout();
            for (int i = 0; i < columnCount; i++)
                rl.Add(1.0f / columnCount, true);
            if (eraseButton)
                rl.Add(buttonSizePx, false);
            rl.Apply(Nuklear, LineHeight);
        }

        private unsafe bool RenderVectorProperty (CachedPropertyInfo cpi, ref Vector2 v2, ref bool changed, bool layout = true) {
            if (layout)
                SetVectorRowLayout(2, cpi?.AllowNull ?? false);
            var a = RenderPropertyElement("#x", cpi?.Info, ref v2.X, ref changed);
            var b = RenderPropertyElement("#y", cpi?.Info, ref v2.Y, ref changed);
            return a || b;
        }

        private unsafe bool RenderVectorProperty (CachedPropertyInfo cpi, ref Vector3 v3, ref bool changed, bool layout = true) {
            if (layout)
                SetVectorRowLayout(3, cpi?.AllowNull ?? false);
            var a = RenderPropertyElement("#x", cpi?.Info, ref v3.X, ref changed);
            var b = RenderPropertyElement("#y", cpi?.Info, ref v3.Y, ref changed);
            var c = RenderPropertyElement("#z", cpi?.Info, ref v3.Z, ref changed);
            return a || b || c;
        }

        private unsafe bool RenderVectorProperty (CachedPropertyInfo cpi, ref Vector4 v4, ref bool changed, bool isColor, bool layout = true) {
            if (layout)
                SetVectorRowLayout(4, cpi?.AllowNull ?? false);
            var a = RenderPropertyElement(isColor ? "#r" : "#x", cpi?.Info, ref v4.X, ref changed);
            var b = RenderPropertyElement(isColor ? "#g" : "#y", cpi?.Info, ref v4.Y, ref changed);
            var c = RenderPropertyElement(isColor ? "#b" : "#z", cpi?.Info, ref v4.Z, ref changed);
            var d = RenderPropertyElement(isColor ? "#a" : "#w", cpi?.Info, ref v4.W, ref changed);
            return a || b || c || d;
        }

        private struct MatrixGenerateParameters {
            public float Angle, Scale;
            public Vector3 Translation;
        }

        private readonly Dictionary<string, MatrixGenerateParameters> MatrixGenerateParams = new Dictionary<string, MatrixGenerateParameters>();

        private unsafe bool RenderMatrixProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, ref Matrix m, bool is3x4, 
            ref bool doBezierConversion, ref bool doReferenceConversion
        ) {
            MatrixGenerateParameters p;
            var isGenerated = false;
            if (!MatrixGenerateParams.TryGetValue(actualName, out p))
                p = new MatrixGenerateParameters { Scale = 1 };
            else
                isGenerated = (p.Angle != 0) || (p.Scale != 1) || (p.Translation.Length() >= 0.5f);

            var dm = new DynamicMatrix {
                Matrix = m,
                IsGenerated = isGenerated,
                Angle = p.Angle,
                Scale = p.Scale,
                Translation = p.Translation
            };
            var result = RenderMatrixProperty(cpi, instance, ref changed, actualName, ref dm, is3x4, false, ref doBezierConversion, ref doReferenceConversion);
            p.Angle = dm.Angle;
            p.Scale = dm.Scale;
            p.Translation = dm.Translation;
            MatrixGenerateParams[actualName] = p;
            return result;
        }

        private unsafe bool RenderMatrixProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, ref DynamicMatrix dm, bool is3x4, 
            bool isDynamic, ref bool doBezierConversion, ref bool doReferenceConversion
        ) {
            var ctx = Nuklear.Context;
            using (var pGroup = Nuklear.CollapsingGroup(actualName, actualName, false, tooltip: cpi.Summary)) {
                if (pGroup.Visible) {
                    var buttonCount = 1;
                    if (isDynamic)
                        buttonCount++;
                    if (doReferenceConversion)
                        buttonCount++;
                    if (doBezierConversion)
                        buttonCount++;

                    NuklearService.Tree? grp = null;
                    if (!isDynamic) {
                        grp = Nuklear.CollapsingGroup("Generate", "GenerateMatrix", false, NextMatrixIndex++);
                        dm.IsGenerated = dm.IsGenerated || grp.Value.Visible;
                    } else {
                        Nuklear.NewRow(LineHeight, buttonCount);
                        if (Nuklear.Checkbox("Generated", ref dm.IsGenerated))
                            changed = true;
                    }

                    var isGroupOpen = (grp != null) && (grp.Value.Visible);

                    if (isGroupOpen || isDynamic) {
                        if (!isDynamic)
                            Nuklear.NewRow(LineHeight, buttonCount);

                        if (Nuklear.Button("Identity")) {
                            dm.Matrix = Matrix.Identity;
                            dm.Angle = 0;
                            dm.Scale = 1;
                            dm.Translation = Vector3.Zero;
                            dm.IsGenerated = true;
                            changed = true;
                        }
                    }

                    if (doBezierConversion)
                        doBezierConversion = ShowBezierButton();
                    if (doReferenceConversion)
                        doReferenceConversion = ShowReferenceButton();

                    if (isGroupOpen || dm.IsGenerated) {
                        Nuklear.NewRow(LineHeight, 2);
                        if (Nuklear.Property("#Angle (X)", ref dm.AngleX, -720, 720, 1f, 0.5f)) {
                            changed = true;
                            dm.IsGenerated = true;
                        }
                        if (Nuklear.Property("#Angle (Y)", ref dm.AngleY, -720, 720, 1f, 0.5f)) {
                            changed = true;
                            dm.IsGenerated = true;
                        }
                        if (Nuklear.Property("#Angle (Z)", ref dm.AngleZ, -720, 720, 1f, 0.5f)) {
                            changed = true;
                            dm.IsGenerated = true;
                        }
                        if (Nuklear.Property("#Scale", ref dm.Scale, -5, 5, 0.05f, 0.01f)) {
                            changed = true;
                            dm.IsGenerated = true;
                        }
                        if (RenderVectorProperty(null, ref dm.Translation, ref changed, true))
                            dm.IsGenerated = true;

                        dm.Regenerate();
                    } else {
                        dm.Regenerate();
                        var m = dm.Matrix;

                        Nuklear.NewRow(LineHeight, is3x4 ? 3 : 4);
                        RenderPropertyElement("#xx", cpi?.Info, ref m.M11, ref changed);
                        RenderPropertyElement("#xy", cpi?.Info, ref m.M12, ref changed);
                        RenderPropertyElement("#xz", cpi?.Info, ref m.M13, ref changed);
                        if (!is3x4)
                            RenderPropertyElement("#xw", cpi?.Info, ref m.M14, ref changed);
                        RenderPropertyElement("#yx", cpi?.Info, ref m.M21, ref changed);
                        RenderPropertyElement("#yy", cpi?.Info, ref m.M22, ref changed);
                        RenderPropertyElement("#yz", cpi?.Info, ref m.M23, ref changed);
                        if (!is3x4)
                            RenderPropertyElement("#yw", cpi?.Info, ref m.M24, ref changed);
                        RenderPropertyElement("#zx", cpi?.Info, ref m.M31, ref changed);
                        RenderPropertyElement("#zy", cpi?.Info, ref m.M32, ref changed);
                        RenderPropertyElement("#zz", cpi?.Info, ref m.M33, ref changed);
                        if (!is3x4)
                            RenderPropertyElement("#zw", cpi?.Info, ref m.M34, ref changed);

                        var isSelected = IsPropertySelected(instance, "Constant");
                        Nuklear.NewRow(LineHeight, 2);

                        var xy = new Vector2(m.M41, m.M42);
                        if (TickSelectableProperty(instance, "Constant", ref xy)) {
                            m.M41 = xy.X;
                            m.M42 = xy.Y;
                            changed = true;
                        }
                        if (Nuklear.Button("Zero")) {
                            m.M41 = m.M42 = m.M43 = 0;
                            changed = true;
                        }
                        Nuklear.NewRow(LineHeight, 3);
                        RenderPropertyElement("#wx", cpi?.Info, ref m.M41, ref changed);
                        RenderPropertyElement("#wy", cpi?.Info, ref m.M42, ref changed);
                        RenderPropertyElement("#wz", cpi?.Info, ref m.M43, ref changed);

                        dm.Matrix = m;
                    }

                    if (grp != null)
                        grp.Value.Dispose();

                    if (changed) {
                        if (instance != null) {
                            if (cpi.Type == typeof(Matrix)) {
                                dm.Regenerate();
                                cpi.Setter(instance, dm.Matrix);
                            } else
                                cpi.Setter(instance, dm);
                        }
                        return true;
                    }
                } else {
                    doBezierConversion = false;
                    doReferenceConversion = false;
                }
            }
            return false;
        }

        private unsafe bool RenderBezierProperty (
            CachedPropertyInfo cpi, object instance,
            string actualName, object value, float? currentT,
            bool isColor, bool initiallyOpen = false, bool allowErase = false
        ) {
            bool changed = false;
            var bm = value as BezierM;
            int selectedRow = 0;
            bool fullyDynamic = false;
            if (bm != null) {
                BezierSelectedRows.TryGetValue(bm, out selectedRow);
                fullyDynamic = bm.IsFullyDynamic;
            }

            int elementCount;
            if (value is BezierF)
                elementCount = 1;
            else if (value is Bezier2)
                elementCount = 2;
            else if (value is Bezier3)
                elementCount = 3;
            else
                elementCount = 4;

            var ctx = Nuklear.Context;
            using (var pGroup = Nuklear.CollapsingGroup(cpi?.Name ?? actualName, actualName, initiallyOpen, tooltip: cpi?.Summary)) {
                if (pGroup.Visible) {
                    var b = (IBezier)value;

                    if (b.Count > 1) {
                        Bounds panel;
                        if (Nuklear.CustomPanel(180, out panel)) {
                            var m = Game.ScreenSpaceBezierVisualizer;
                            using (var pb = PrimitiveBatch<VertexPositionColorTexture>.New(
                                Nuklear.PendingGroup, 9999, m, (dm, _) => {
                                    Squared.Illuminant.Uniforms.ClampedBezier4 cb;
                                    if (bm != null) {
                                        if (fullyDynamic) {
                                            float ss = 360;
                                            Vector2 va = new Vector2(bm.A.Angle, bm.A.Scale * ss), 
                                                vb = new Vector2(bm.B.Angle, bm.B.Scale * ss),
                                                vc = new Vector2(bm.C.Angle, bm.C.Scale * ss), 
                                                vd = new Vector2(bm.D.Angle, bm.D.Scale * ss);
                                            cb = new Squared.Illuminant.Uniforms.ClampedBezier4(b, ref va, ref vb, ref vc, ref vd, 0, 0);
                                        } else {
                                            cb = new Squared.Illuminant.Uniforms.ClampedBezier4(bm, selectedRow);
                                        }
                                    } else
                                        cb = new Squared.Illuminant.Uniforms.ClampedBezier4(b);
                                    Game.uBezier.Set(m, ref cb);
                                    m.Effect.Parameters["CurrentT"].SetValue(currentT.GetValueOrDefault(-99999));
                                    m.Effect.Parameters["ElementCount"].SetValue(elementCount);
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
                                pb.Add(pdc);
                            }
                        }
                    }

                    Nuklear.NewRow(LineHeight, ((bm != null) && !fullyDynamic) ? 5 : 4);

                    var cnt = b.Count;
                    if (Nuklear.Property("##", ref cnt, allowErase ? 0 : 1, 4, 1, 0.5f, tooltip: "Control Point Count")) {
                        // Copy existing row when adding new one
                        if ((b.Count < cnt) && (cnt > 1))
                            b[cnt - 1] = b[cnt - 2];
                        b.Count = cnt;
                        changed = true;
                    }

                    var val = b.MinValue;
                    if (RenderPropertyElement("#≥", null, ref val, ref changed, tooltip: "Minimum Value"))
                        b.MinValue = val;

                    val = b.MaxValue;
                    if (RenderPropertyElement("#≤", null, ref val, ref changed, tooltip: "Maximum Value"))
                        b.MaxValue = val;

                    if ((bm != null) && !fullyDynamic && Nuklear.Property("#Row", ref selectedRow, 0, 3, 1, 0.5f))
                        BezierSelectedRows[bm] = selectedRow;

                    object mode = b.Mode;
                    if (Nuklear.EnumCombo(ref mode, tooltip: "Time Mode")) {
                        b.Mode = (BezierTimeMode)mode;
                    }

                    for (int i = 0; i < cnt; i++) {
                        var elt = b[i];
                        if (elt is float) {
                            var v = (float)elt;
                            Nuklear.NewRow(LineHeight, 1);
                            if (RenderPropertyElement(PrefixedBezierElementNames[i], cpi?.Info, ref v, ref changed))
                                b[i] = v;
                        } else if (elt is Vector2) {
                            var v2 = (Vector2)elt;
                            if (RenderVectorProperty(null, ref v2, ref changed))
                                b[i] = v2;
                        } else if (elt is Vector3) {
                            var v3 = (Vector3)elt;
                            if (RenderVectorProperty(null, ref v3, ref changed))
                                b[i] = v3;
                        } else if (elt is Vector4) {
                            var v4 = (Vector4)elt;
                            if (RenderVectorProperty(null, ref v4, ref changed, isColor))
                                b[i] = v4;
                        } else if (elt is Matrix) {
                            var m = (Matrix)elt;
                            bool temp = false;
                            // FIXME: Implement reference conversion
                            if (RenderMatrixProperty(cpi, null, ref changed, BezierElementNames[i], ref m, false, ref temp, ref temp))
                                b[i] = m;
                        } else if (elt is DynamicMatrix) {
                            var dm = (DynamicMatrix)elt;
                            bool temp = false;
                            // FIXME: Implement reference conversion
                            if (RenderMatrixProperty(cpi, null, ref changed, BezierElementNames[i], ref dm, false, true, ref temp, ref temp))
                                b[i] = dm;
                        } else {
                            throw new Exception();
                        }
                    }
                }
            }

            return changed;
        }

        private unsafe bool RenderSystemReferenceProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, object _value
        ) {
            var ctx = Nuklear.Context;
            var value = (ParticleSystemReference)_value;
            var hasValue = value.TryInitialize(Game.View.ResolveReference);

            Nuklear.NewRow(LineHeight, 2);

            Nuklear.Label(actualName);

            int index = 0;
            if (hasValue)
                index = Game.View.Systems.FindIndex((psv) => psv.Instance == value.Instance) + 1;

            if (Nuklear.ComboBox(ref index, (i) =>
                (i > 0) && (i <= Game.View.Systems.Count)
                    ? string.Format("#{0} '{1}'", i, Game.View.Systems[i - 1].Model.Name)
                    : "none",
                Game.View.Systems.Count + 1
            )) {
                if (index > 0) {
                    var sys = Game.View.Systems[index - 1];
                    value.Name = sys.Model.Name;
                    value.Index = index - 1;
                    value.TryInitialize(Game.View.ResolveReference);
                } else {
                    value.Name = null;
                    value.Index = null;
                    value.Instance = null;
                }
                cpi.Setter(instance, value);
                return true;
            }

            return false;
        }

        private unsafe bool RenderDirectoryNameProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, object _value
        ) {
            var ctx = Nuklear.Context;
            var dn = (_value as DirectoryName);
            var value = dn?.Path;
            var hasValue = !String.IsNullOrWhiteSpace(value);
            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false, tooltip: cpi.Summary)) {
                if (pGroup.Visible) {
                    Nuklear.NewRow(LineHeight, 1);
                    Nuklear.Label(value ?? "none");
                    Nuklear.NewRow(LineHeight, 2);
                    if (Nuklear.Button("Select")) {
                        if (dn == null)
                            _value = dn = new DirectoryName();
                        Controller.SelectDirectory(ref dn.Path);
                        cpi.Setter(instance, dn);
                        changed = true;
                    }
                    if (Nuklear.Button("Remove", hasValue)) {
                        _value = dn = new DirectoryName();
                        cpi.Setter(instance, dn);
                        changed = true;
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
            var hasValue = (value != null) && value.IsInitialized;
            Nuklear.NewRow(LineHeight, 3);
            Nuklear.Label(cpi.Name);
            if (Nuklear.Button("Select")) {
                Controller.SelectTexture(
                    cpi, instance, value,
                    Game.View.GetResourceDirectory()
                );
                changed = false;
            }
            if (Nuklear.Button("Remove", hasValue)) {
                if (value.Instance != null)
                    Game.RenderCoordinator.DisposeResource(value.Instance);
                value = new NullableLazyResource<Texture2D>();
                cpi.Setter(instance, value);
                changed = true;
                hasValue = false;
            }

            if (hasValue) {
                var tex = value.Instance;
                var height = Math.Min(200, tex.Height);
                Bounds panel;
                if (Nuklear.CustomPanel(height, out panel)) {
                    float scaleF = Math.Min(
                        height / (float)tex.Height,
                        panel.Size.X / (float)tex.Width
                    );
                    var scaledW = tex.Width * scaleF;
                    var xOffset = Math.Max(0, (panel.Size.X - scaledW) / 2f);
                    var ss = (tex.Format == SurfaceFormat.Vector4)
                        ? SamplerState.PointClamp
                        : SamplerState.LinearClamp;
                    Nuklear.PendingRenderer.Draw(tex, panel.TopLeft + new Vector2(xOffset, 0), scale: new Vector2(scaleF), layer: 9999, samplerState: ss);

                    var nameText = Path.GetFileName(value.Name);
                    using (var buffer = BufferPool<BitmapDrawCall>.Allocate(nameText.Length * 2)) {
                        var layout = Game.Font.LayoutString(
                            nameText, buffer, panel.TopLeft,
                            lineBreakAtX: panel.Size.X, alignToPixels: true, wordWrap: true,
                            horizontalAlignment: Squared.Render.Text.HorizontalAlignment.Center,
                            reverseOrder: true
                        );
                        Nuklear.PendingRenderer.DrawMultiple(
                            layout.DrawCalls,
                            layer: 10000, material: Game.TextMaterial
                        );
                    }
                }
            }

            return changed;
        }

        private unsafe bool RenderListProperty (
            CachedPropertyInfo cpi, object instance, ref bool changed, 
            string actualName, object _list, bool itemsAreValues
        ) {
            var ctx = Nuklear.Context;
            var list = (System.Collections.IList)_list;
            var itemType = GetElementType(_list.GetType());

            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false, tooltip: cpi.Summary)) {
                if (pGroup.Visible) {
                    PropertyGridCache pgc;
                    if (!GridCaches.TryGetValue(actualName, out pgc))
                        GridCaches[actualName] = pgc = new PropertyGridCache();

                    Nuklear.NewRow(LineHeight, 3);
                    var indexChanged = Nuklear.Property("##", ref pgc.SelectedIndex, 0, list.Count - 1, 1, 0.5f);
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
                                if (RenderProperty(pgc, cpi.ElementInfo, pgc.Box, cpi.Info.Type)) {
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
            using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, cpi.Name, false, tooltip: cpi.Summary)) {
                if (pGroup.Visible) {
                    Vector4 c;
                    if (value is Vector4)
                        c = (Vector4)value;
                    else if (value is Color)
                        c = ((Color)value).ToVector4();
                    else
                        throw new Exception();
                    var oldColor = new NuklearDotNet.nk_colorf {
                        r = c.X,
                        g = c.Y,
                        b = c.Z,
                        a = c.W,
                    };
                    Nuklear.NewRow(LineHeight, 2);
                    var resetToTransparent = Nuklear.Button("Transparent");
                    var resetToWhite = Nuklear.Button("White");
                    Nuklear.NewRow(96, 1);
                    var temp = Nuke.nk_color_picker(ctx, oldColor, NuklearDotNet.nk_color_format.NK_RGBA);
                    var newColor = resetToWhite 
                        ? Vector4.One 
                        : resetToTransparent 
                            ? Vector4.Zero
                            : new Vector4(temp.r, temp.g, temp.b, temp.a);
                    if (newColor != c)
                        changed = true;
                    Nuklear.NewRow(LineHeight, 4);
                    RenderPropertyElement("#R", null, ref newColor.X, ref changed, 0, 1);
                    RenderPropertyElement("#G", null, ref newColor.Y, ref changed, 0, 1);
                    RenderPropertyElement("#B", null, ref newColor.Z, ref changed, 0, 1);
                    RenderPropertyElement("#A", null, ref newColor.W, ref changed, 0, 1);
                    if (changed) {
                        if (value is Vector4)
                            value = newColor;
                        else if (value is Color)
                            value = new Color(newColor.X, newColor.Y, newColor.Z, newColor.W);
                        cpi.Setter(instance, value);
                        return true;
                    }
                }
            }
            return false;
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
        public bool AllowNull, IsGetItem, IsWritable;
        public CachedPropertyInfo ElementInfo;
        public string[] EnumValueNames;
        public string Summary;
    }
}
