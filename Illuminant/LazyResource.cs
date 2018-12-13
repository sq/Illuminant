using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Illuminant {
    public class LazyResource<T> where T : class {
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

            if (Name == null)
                throw new InvalidOperationException("No name for resource");

            if (resourceLoader != null)
                Instance = resourceLoader(Name);
            else
                throw new InvalidOperationException("No resource loader for type " + typeof(T).Name);
        }

        public static implicit operator LazyResource<T> (T instance) {
            return new LazyResource<T>(instance);
        }

        public static implicit operator T (LazyResource<T> rsrc) {
            if (rsrc.Instance == null)
                throw new InvalidOperationException("Resource not loaded");
            return rsrc.Instance;
        }
    }
}
