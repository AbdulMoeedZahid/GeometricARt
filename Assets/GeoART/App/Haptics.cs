using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GeoART.App
{
    /// <summary>
    /// A light haptic "tick" for snap acquisitions and hold menus (ui-line-plan 4.4), at most one per
    /// <see cref="MinIntervalSeconds"/>, so a fast drag across a row of dots does not buzz continuously.
    ///   iOS      UISelectionFeedbackGenerator.selectionChanged via Assets/Plugins/iOS/GeoHaptics.mm (DllImport __Internal)
    ///   Android  View.performHapticFeedback(CLOCK_TICK, FLAG_IGNORE_VIEW_SETTING) on the activity's decor view, on the UI
    ///            thread; any failure turns it off for the session
    ///   Editor   no-op
    /// <see cref="Clock"/> and <see cref="Sink"/> let EditMode tests run the throttle without a device.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Shortest time between two ticks.</summary>
        public const float MinIntervalSeconds = 0.06f;

        /// <summary>Seconds, monotonic. Defaults to Time.unscaledTime.</summary>
        public static Func<float> Clock = () => Time.unscaledTime;

        /// <summary>When set, receives each tick instead of the platform call (tests).</summary>
        public static Action Sink;

        static float s_last = float.NegativeInfinity;

        /// <summary>Ticks delivered since start (or <see cref="ResetThrottle"/>).</summary>
        public static int TickCount { get; private set; }

        /// <summary>Plays one tick unless the previous one was under <see cref="MinIntervalSeconds"/> ago. True if it played.</summary>
        public static bool Tick()
        {
            float now = Clock();
            if (now - s_last < MinIntervalSeconds) return false;
            s_last = now;
            TickCount++;
            if (Sink != null) Sink();
            else Platform();
            return true;
        }

        /// <summary>Forgets the last tick time and the count (tests).</summary>
        public static void ResetThrottle()
        {
            s_last = float.NegativeInfinity;
            TickCount = 0;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void GeoHapticsSelection();

        static bool s_failed;

        static void Platform()
        {
            if (s_failed) return;
            try { GeoHapticsSelection(); }
            catch (Exception) { s_failed = true; }   // e.g. the plugin was stripped: stay silent
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        const int ClockTick = 4;              // HapticFeedbackConstants.CLOCK_TICK
        const int IgnoreViewSetting = 1;      // HapticFeedbackConstants.FLAG_IGNORE_VIEW_SETTING

        static AndroidJavaObject s_activity, s_view;
        static AndroidJavaRunnable s_run;
        static bool s_failed;

        static void Platform()
        {
            if (s_failed) return;
            try
            {
                if (s_view == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        s_activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    using (var window = s_activity.Call<AndroidJavaObject>("getWindow"))
                        s_view = window.Call<AndroidJavaObject>("getDecorView");
                    s_run = () =>
                    {
                        try { s_view.Call<bool>("performHapticFeedback", ClockTick, IgnoreViewSetting); }
                        catch (Exception) { s_failed = true; }
                    };
                }
                s_activity.Call("runOnUiThread", s_run);
            }
            catch (Exception) { s_failed = true; }
        }
#else
        static void Platform() { }
#endif
    }
}
