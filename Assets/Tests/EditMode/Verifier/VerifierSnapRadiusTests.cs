using System;
using System.Reflection;
using GeoART.App;
using NUnit.Framework;
using UnityEngine;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// VERIFIER (ui-line-plan 4.2 / 4.4, P3 + P3b): the App side of the snap changes. The engine side is in
    /// tools/enginecheck/VerifierSnapFeelChecks.cs. Haptics types are looked up by name, so this file compiles before P3b.
    /// </summary>
    public sealed class VerifierSnapRadiusTests
    {
        [Test]
        public void ToleranceIsThirtyTwoReferencePixels()
        {
            Assert.AreEqual(32f, ScreenSnapMetric.ToleranceReferencePx, "capture radius 32 ref px (64 pt circle on an iPhone)");
            var m = new ScreenSnapMetric();
            float expected = 32f * ScreenSnapMetric.ScaleFactor(Screen.width, Screen.height);
            Assert.AreEqual(expected, (float)m.TolerancePx, 1e-3f, "TolerancePx = 32 x the CanvasScaler factor");
            Assert.AreEqual(96f, 32f * ScreenSnapMetric.ScaleFactor(1170f, 2532f), 0.5f, "1170x2532 iPhone: 96 px = 32 pt");
        }

        static Type AppType(string name)
        {
            var t = typeof(ScreenSnapMetric).Assembly.GetType("GeoART.App." + name);
            if (t == null) Assert.Ignore($"GeoART.App.{name} not built yet (P3b)");
            return t;
        }

        [Test]
        public void HapticsTickIsANoOpInTheEditor()
        {
            var t = AppType("Haptics");
            var tick = t.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            Assert.NotNull(tick, "static Haptics.Tick() (plan 4.4)");
            for (int i = 0; i < 3; i++) tick.Invoke(null, null);   // must not throw, buzz or log errors in the editor
        }

        [Test]
        public void SnapFeedbackIsAComponent()
        {
            var t = AppType("SnapFeedback");
            Assert.IsTrue(typeof(MonoBehaviour).IsAssignableFrom(t), "SnapFeedback is a MonoBehaviour (plan 4.4)");
        }
    }
}
