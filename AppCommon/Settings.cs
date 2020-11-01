using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;

namespace TestGame {
    public interface ISetting {
        void Initialize (FieldInfo f);
        void Update (Scene s);
        string Name { get; set; }
        string Group { get; set; }
        string GetFormattedValue ();
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class ItemsAttribute : Attribute {
        public object Value;
        public string Label;

        public ItemsAttribute (object value, string label = null) {
            Value = value;
            Label = label;
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class GroupAttribute : Attribute {
        public string Name;

        public GroupAttribute (string name) {
            Name = name;
        }
    }

    public abstract class Setting<T> : ISetting
        where T : IEquatable<T>
    {
        public event EventHandler<T> Changed;
        public string Name { get; set; }
        public string Group { get; set; }
        protected T _Value;

        public virtual T Value { 
            get { return _Value; }
            set {
                var eqc = EqualityComparer<T>.Default;
                if (!eqc.Equals(_Value, value)) {
                    _Value = value;
                    if (Changed != null)
                        Changed(this, value);
                }
            }
        }

        protected abstract string GetLabelText ();

        public virtual void Initialize (FieldInfo f) {
        }

        public abstract string GetFormattedValue ();

        public abstract void Update (Scene s);

        public static implicit operator T (Setting<T> setting) {
            return setting.Value;
        }

        public static string KeyToString (Keys key) {
            switch (key) {
                case Keys.None:
                    return "";
                case Keys.OemMinus:
                    return "-";
                case Keys.OemPlus:
                    return "+";
                case Keys.OemSemicolon:
                    return ";";
                case Keys.OemQuotes:
                    return "\"";
                case Keys.OemComma:
                    return ",";
                case Keys.OemPeriod:
                    return ".";
                case Keys.D0:
                case Keys.D1:
                case Keys.D2:
                case Keys.D3:
                case Keys.D4:
                case Keys.D5:
                case Keys.D6:
                case Keys.D7:
                case Keys.D8:
                case Keys.D9:
                    return key.ToString().Substring(1);
                default:
                    return key.ToString();
            }
        }
    }

    public class Toggle : Setting<bool> {
        public Keys Key;

        public override void Update (Scene s) {
            if (s.KeyWasPressed(Key))
                Value = !Value;
        }

        protected override string GetLabelText () {
            return string.Format("{1} {0}", Name, KeyToString(Key)).Trim();
        }

        public override string GetFormattedValue () {
            return Value.ToString();
        }

        public override string ToString () {
            return string.Format("{0,-2} {1} {2}", Key, Value ? "+" : "-", Name);
        }
    }

    public class Slider : Setting<float> {
        public bool AsProperty = true;
        public bool Integral = false;
        public Keys MinusKey, PlusKey;
        public float? Min, Max;
        public float Speed = 1;

        public override void Update (Scene s) {
            float delta = 0;

            if (s.KeyWasPressed(MinusKey))
                delta = -Speed;
            else if (s.KeyWasPressed(PlusKey))
                delta = Speed;
            else
                return;

            var newValue = Value + delta;
            if (Min.HasValue)
                newValue = Math.Max(newValue, Min.Value);
            if (Max.HasValue)
                newValue = Math.Min(newValue, Max.Value);

            if (Integral)
                newValue = (float)Math.Floor(newValue);

            if (Value == newValue)
                return;

            Value = newValue;
        }

        protected override string GetLabelText () {
            if ((MinusKey != Keys.None) || (PlusKey != Keys.None))
                return string.Format("{0} {1} / {2}", Name, KeyToString(MinusKey), KeyToString(PlusKey)).Trim();
            else
                return Name;
        }

        public override string GetFormattedValue () {
            if (Speed < 1) {
                return string.Format("{0:0.000}", Value);
            } else {
                return string.Format("{0:0}", Value);
            }
        }

        public override string ToString () {
            var formattedValue = GetFormattedValue();
            return string.Format("{0,-2} {1:0} {2} {3,2}", MinusKey, formattedValue, Name, PlusKey);
        }
    }

    public interface IDropdown : ISetting {
        object GetItem (int index);
        object SelectedItem { get; set; }
        int SelectedIndex { get; set; }
        int Count { get; }
    }

    public class Dropdown<T> : Setting<T>, IEnumerable<Dropdown<T>.Item>, IDropdown
        where T : IEquatable<T>
    {
        public Keys Key;

        public readonly List<Item> Items = new List<Item>();

        public class Item {
            public T Value;
            public string Label;

            public override string ToString () {
                return Label;
            }
        }

        public int Count {
            get {
                return Items.Count;
            }
        }

        public T SelectedItem {
            get {
                return Value;
            }
            set {
                Value = value;
            }
        }

        public int SelectedIndex {
            get {
                var eqc = EqualityComparer<T>.Default;
                return Items.FindIndex(i => eqc.Equals(Value, i.Value));
            }
            set {
                Value = Items[value].Value;
            }
        }

        object IDropdown.SelectedItem {
            get => Value;
            set {
                var i = value as Item;
                if (i == null)
                    Value = default(T);
                else
                    Value = i.Value;
            }
        }

        public void Clear () {
            Items.Clear();
        }

        public void Add (T value, string label = null) {
            Items.Add(new Item {
                Value = value,
                Label = label ?? value.ToString()
            });
        }

        public void AddRange (T[] values, string[] labels = null) {
            var oldCount = 0;

            for (int i = 0; i < values.Length; i++)
                Add(values[i], labels != null ? labels[i] : null);

            if ((Items.Count > 0) && oldCount == 0 && (Comparer<T>.Default.Compare(Value, default(T)) == 0))
                Value = Items[0].Value;
        }

        public unsafe override void Initialize (FieldInfo f) {
            var cas = f.GetCustomAttributes<ItemsAttribute>();
            foreach (var ca in cas)
                Add((T)ca.Value, ca.Label);

            if (Items.Count > 0)
                Value = Items[0].Value;
        }

        public override void Update (Scene s) {
            if (s.KeyWasPressed(Key)) {
                var index = SelectedIndex;
                index = (index + 1) % Count;
                SelectedIndex = index;
            }
        }

        protected override string GetLabelText () {
            return string.Format("{1} {0}", Name, KeyToString(Key)).Trim();
        }

        public override string GetFormattedValue () {
            return Value.ToString();
        }

        public override string ToString () {
            return Value.ToString();
        }

        public IEnumerator<Item> GetEnumerator () {
            return Items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return Items.GetEnumerator();
        }

        object IDropdown.GetItem (int index) {
            return Items[index];
        }
    }

    public class SettingCollection : List<ISetting> {
        public class Group : List<ISetting> {
            public readonly string Name;
            public bool Visible = true;

            public Group (string name) {
                Name = name;
            }
        }

        public Dictionary<string, Group> Groups = new Dictionary<string, Group>();

        public SettingCollection (object obj) {
            var tSetting = typeof(ISetting);
            foreach (var f in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (!tSetting.IsAssignableFrom(f.FieldType))
                    continue;

                var setting = (ISetting)Activator.CreateInstance(f.FieldType);
                setting.Name = f.Name;

                var ca = f.GetCustomAttribute<GroupAttribute>();
                if (ca != null)
                    setting.Group = ca.Name;

                setting.Initialize(f);

                if (setting.Group != null) {
                    Group group;
                    if (!Groups.TryGetValue(setting.Group, out group)) {
                        Groups[setting.Group] = group = new Group(setting.Group);
                    }

                    group.Add(setting);
                } else {
                    Add(setting);
                }

                f.SetValue(obj, setting);
            }
        }

        public void Update (Scene scene) {
            foreach (var g in Groups.Values)
                foreach (var s in g)
                    s.Update(scene);

            foreach (var s in this)
                s.Update(scene);
        }
    }
}
