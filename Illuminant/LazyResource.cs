using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Illuminant {
    public class LazyResource<T> where T : class {
        public bool IsNullable { get; protected set; }

        public string Name;
        [NonSerialized]
        public T Instance;

        public LazyResource (string name, T existingInstance = null) {
            Name = name;
            Instance = existingInstance;
        }

        public LazyResource (T existingInstance) {
            Instance = existingInstance;
        }

        public void EnsureInitialized (Func<string, T> resourceLoader) {
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

        public static implicit operator LazyResource<T> (T instance) {
            return new LazyResource<T>(instance);
        }

        public static implicit operator T (LazyResource<T> rsrc) {
            if (rsrc.Instance == null) {
                if (rsrc.IsNullable)
                    return null;
                else
                    throw new ResourceNotLoadedException();
            }
            return rsrc.Instance;
        }
    }

    public class NullableLazyResource<T> : LazyResource<T> where T : class {
        public NullableLazyResource (string name, T existingInstance = null)
            : base (name, existingInstance) {
            IsNullable = true;
        }

        public NullableLazyResource (T existingInstance)
            : base (existingInstance) {
            IsNullable = true;
        }

        public NullableLazyResource ()
            : base (null) {
            IsNullable = true;
        }
    }

    public class ResourceNotLoadedException : Exception {
        public ResourceNotLoadedException (string message = null) 
            : base(message ?? "Lazy resource not loaded") {
        }
    }
}
