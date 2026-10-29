using System;
using System.Collections.Generic;

namespace GeoART.Contracts
{
    /// <summary>
    /// Tiny service locator (MVP_PLAN 2.3). Installers register in Awake, consumers resolve in Start or later.
    /// Exactly one provider per service: a duplicate registration throws so wiring mistakes fail loudly.
    /// GeoART.App clears it on SubsystemRegistration, so Play Mode with domain reload disabled starts clean.
    /// </summary>
    public static class ServiceHub
    {
        static readonly Dictionary<Type, object> s_Services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (s_Services.ContainsKey(typeof(T)))
                throw new InvalidOperationException(
                    $"ServiceHub: {typeof(T).Name} is already registered. A scene must contain exactly one provider for it.");
            s_Services[typeof(T)] = service;
        }

        /// <summary>Removes the registration only if <paramref name="service"/> is the current provider.</summary>
        public static void Unregister<T>(T service) where T : class
        {
            if (s_Services.TryGetValue(typeof(T), out var current) && ReferenceEquals(current, service))
                s_Services.Remove(typeof(T));
        }

        public static T Resolve<T>() where T : class
        {
            if (TryResolve<T>(out var service)) return service;
            throw new InvalidOperationException(
                $"ServiceHub: no provider registered for {typeof(T).Name}. Check that the scene contains its module prefab.");
        }

        public static bool TryResolve<T>(out T service) where T : class
        {
            if (s_Services.TryGetValue(typeof(T), out var o))
            {
                service = (T)o;
                return true;
            }
            service = null;
            return false;
        }

        public static bool IsRegistered<T>() where T : class => s_Services.ContainsKey(typeof(T));

        public static void Clear() => s_Services.Clear();
    }
}
