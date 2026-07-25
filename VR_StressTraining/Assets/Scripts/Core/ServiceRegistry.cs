using System;
using System.Collections.Generic;

namespace StressTraining.Core
{
    /// <summary>
    /// Explicit dependency installation point. AppBootstrapper installs concrete
    /// services once at boot; consumers resolve them once (not per frame).
    /// Reset() exists for tests and for full application restarts.
    /// </summary>
    public static class ServiceRegistry
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        public static void Install<T>(T instance) where T : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            Services[typeof(T)] = instance;
        }

        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out var s)) return (T)s;
            throw new InvalidOperationException(
                $"[ServiceRegistry] Service {typeof(T).Name} is not installed. " +
                "Check AppBootstrapper installation order.");
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out var s))
            {
                service = (T)s;
                return true;
            }
            service = null;
            return false;
        }

        public static bool Has<T>() where T : class => Services.ContainsKey(typeof(T));

        public static void Reset() => Services.Clear();
    }
}
