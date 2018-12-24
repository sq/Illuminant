using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Illuminant {
    [Serializable]
    public class LazyResource<T> : ISerializable 
        where T : GraphicsResource {

        public string Name;
        [NonSerialized]
        public T Instance;

        protected LazyResource () {
        }

        public virtual bool IsNullable {
            get {
                return false;
            }
        }

        public LazyResource (string name, T existingInstance = null) {
            Name = name;
            Instance = existingInstance;
        }

        public LazyResource (T existingInstance) {
            Instance = existingInstance;
        }

        public void EnsureInitialized (Func<string, T> resourceLoader) {
            if ((Instance != null) && Instance.IsDisposed)
                Instance = null;

            if (Instance != null)
                return;

            if (Name == null) {
                if (IsNullable)
                    return;
                else
                    throw new ResourceNotLoadedException("No name for resource");
            }

            if (resourceLoader != null)
                Instance = resourceLoader(Name);
            else
                throw new ResourceNotLoadedException("No resource loader for type " + typeof(T).Name);
        }

        internal LazyResource (SerializationInfo info, StreamingContext context) {
            Name = (string)info.GetValue("Name", typeof(string));
        }

        void ISerializable.GetObjectData (SerializationInfo info, StreamingContext context) {
            info.AddValue("Name", Name);
        }

        public static implicit operator LazyResource<T> (T instance) {
            return new LazyResource<T>(instance);
        }

        public static implicit operator T (LazyResource<T> rsrc) {
            if (rsrc == null)
                return null;

            if ((rsrc.Instance != null) && rsrc.Instance.IsDisposed)
                rsrc.Instance = null;

            if (rsrc.Instance == null) {
                if (rsrc.IsNullable)
                    return null;
                else
                    throw new ResourceNotLoadedException();
            }

            return rsrc.Instance;
        }
    }

    [Serializable]
    public sealed class NullableLazyResource<T> : LazyResource<T> where T : GraphicsResource {
        public NullableLazyResource (string name, T existingInstance = null)
            : base (name, existingInstance) {
        }

        public NullableLazyResource (T existingInstance)
            : base (existingInstance) {
        }

        public NullableLazyResource ()
            : base (null) {
        }

        public override bool IsNullable {
            get {
                return true;
            }
        }

        internal NullableLazyResource (SerializationInfo info, StreamingContext context) {
            Name = (string)info.GetValue("Name", typeof(string));
        }

        public static implicit operator NullableLazyResource<T> (T instance) {
            return new NullableLazyResource<T>(instance);
        }
    }

    public class ResourceNotLoadedException : Exception {
        public ResourceNotLoadedException (string message = null) 
            : base(message ?? "Lazy resource not loaded") {
        }
    }
}
