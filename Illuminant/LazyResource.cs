﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util.Text;

namespace Squared.Illuminant {
    public interface ILazyResource {
        string Name { get; }
    }

    [Serializable]
    public class LazyResource<T> : ISerializable, ICloneable, ILazyResource 
        where T : GraphicsResource {

        public string Name { get; set; }
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

        public void EnsureInitialized (Func<AbstractString, T> resourceLoader) {
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

        public bool IsInitialized {
            get {
                return (Instance != null) && !Instance.IsDisposed;
            }
        }

        internal LazyResource (SerializationInfo info, StreamingContext context) {
            Name = (string)info.GetValue("Name", typeof(string));
        }

        void ISerializable.GetObjectData (SerializationInfo info, StreamingContext context) {
            info.AddValue("Name", Name);
        }

        public virtual object Clone () {
            var result = new LazyResource<T>(this.Name, this.Instance);
            return result;
        }

        public void Set (T instance, string name = null) {
            Instance = instance;
            Name = name;
        }

        public static explicit operator LazyResource<T> (T instance) {
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

        public override object Clone () {
            var result = new NullableLazyResource<T>(this.Name, this.Instance);
            return result;
        }

        public static explicit operator NullableLazyResource<T> (T instance) {
            return new NullableLazyResource<T>(instance);
        }
    }

    public class ResourceNotLoadedException : Exception {
        public ResourceNotLoadedException (string message = null) 
            : base(message ?? "Lazy resource not loaded") {
        }
    }
}
