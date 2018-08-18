using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;

namespace TestGame {
    public interface ISetting {
        void Update (Scene s);
        string Name { get; set; }
        string Group { get; set; }
        UTF8String GetLabelUTF8 ();
    }

    public class GroupAttribute : Attribute {
        public string Name;

        public GroupAttribute (string name) {
            Name = name;
        }
    }

    public unsafe struct UTF8String : IDisposable {
        public byte* pText;
        public int Length;

        public UTF8String (string text) {
            var encoder = Encoding.UTF8.GetEncoder();
            fixed (char* pChars = text) {
                Length = encoder.GetByteCount(pChars, text.Length, true);
                pText = (byte*)NuklearDotNet.NuklearAPI.Malloc((IntPtr)(Length + 2)).ToPointer();
                int temp;
                bool temp2;
                encoder.Convert(pChars, text.Length, pText, Length, true, out temp, out temp, out temp2);
                pText[Length] = 0;
            }
        }

        public void Dispose () {
            NuklearDotNet.NuklearAPI.StdFree((IntPtr)pText);
            pText = null;
            Length = 0;
        }
    }

    public abstract class Setting<T> : ISetting
        where T : IEquatable<T>
    {
        public event EventHandler<T> Changed;
        public string Name { get; set; }
        public string Group { get; set; }
        protected T _Value;

        private UTF8String LabelUTF8;

        public virtual T Value { 
            get { return _Value; }
            set {
                if (!_Value.Equals(value)) {
                    _Value = value;
                    if (Changed != null)
                        Changed(this, value);
                }
            }
        }

        public UTF8String GetLabelUTF8 () {
            if (LabelUTF8.Length == 0)
                LabelUTF8 = new UTF8String(GetLabelText());

            return LabelUTF8;
        }

        protected abstract string GetLabelText ();

        public abstract void Update (Scene s);

        public static implicit operator T (Setting<T> setting) {
            return setting.Value;
        }
    }

    public class Toggle : Setting<bool> {
        public Keys Key;

        public override void Update (Scene s) {
            if (s.KeyWasPressed(Key))
                Value = !Value;
        }

        protected override string GetLabelText () {
            return string.Format("{0} {1}", Name, Key);
        }

        public override string ToString () {
            return string.Format("{0,-2} {1} {2}", Key, Value ? "+" : "-", Name);
        }
    }

    public class Slider : Setting<float> {
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

            if (Value == newValue)
                return;

            Value = newValue;
        }

        protected override string GetLabelText () {
            return string.Format("{0} {1} {2}", MinusKey, Name, PlusKey);
        }

        public override string ToString () {
            string formattedValue;
            if (Speed < 1) {
                formattedValue = string.Format("{0:00.000}", Value);
            } else {
                formattedValue = string.Format("{0:00000}", Value);
            }
            return string.Format("{0,-2} {1:0} {2} {3,2}", MinusKey, formattedValue, Name, PlusKey);
        }
    }

    public class SettingCollection : List<ISetting> {
        public class Group : List<ISetting> {
            public readonly string Name;
            public bool Visible = true;
            private UTF8String NameUTF8;

            public Group (string name) {
                Name = name;
            }

            public UTF8String GetNameUTF8 () {
                if (NameUTF8.Length == 0)
                    NameUTF8 = new UTF8String(Name);

                return NameUTF8;
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
