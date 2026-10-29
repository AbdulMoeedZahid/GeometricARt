using GeoART.Contracts;
using UnityEngine;

namespace GeoART.App
{
    /// <summary>App-wide startup: clean service registry, frame-rate target, screen kept awake while in AR.</summary>
    public static class AppBootstrap
    {
        /// <summary>
        /// Runs before any scene loads (also with domain reload disabled), so no stale service or subscription
        /// survives from a previous Play Mode session (MVP_PLAN 2.3).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ServiceHub.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ConfigurePlatform()
        {
            // iOS players default to 30 fps; ARKit delivers camera frames at 60 Hz.
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
