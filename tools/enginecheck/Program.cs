using System;
using System.Linq;
using System.Reflection;

namespace GeoART.EngineCheck
{
    public static class Check
    {
        public static void That(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static void Near(double actual, double expected, string message, double eps = 1e-9)
        {
            if (Math.Abs(actual - expected) > eps) throw new Exception($"{message}: expected {expected}, got {actual}");
        }
    }

    static class Program
    {
        static int Main()
        {
            var checks = typeof(Program).Assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Checks"))
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();
            int failed = 0;
            foreach (var m in checks)
            {
                try
                {
                    m.Invoke(null, null);
                    Console.WriteLine($"PASS {m.DeclaringType.Name}.{m.Name}");
                }
                catch (TargetInvocationException e)
                {
                    failed++;
                    Console.WriteLine($"FAIL {m.DeclaringType.Name}.{m.Name}: {e.InnerException?.Message}");
                }
            }
            Console.WriteLine($"{checks.Count - failed}/{checks.Count} passed");
            return failed == 0 ? 0 : 1;
        }
    }
}
