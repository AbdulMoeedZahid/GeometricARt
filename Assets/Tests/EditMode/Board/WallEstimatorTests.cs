using GeoART.BoardRender;
using GeoART.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace GeoART.Tests
{
    /// <summary>
    /// Wall detection from feature points (vertical surfaces): walls are found from noisy points at any yaw and viewing
    /// angle, while floors, table tops, clutter and sparse points are rejected, so tables never get a fake wall.
    /// Also the distance-scaled steadiness that applies to walls only.
    /// </summary>
    public class WallEstimatorTests
    {
        const float Noise = 0.003f;   // ARKit/ARCore feature points are typically a few mm off the surface

        static readonly Vector3 WallCenter = new Vector3(0f, 1.4f, 1.0f);

        /// <summary>Feature points on a 0.8 x 0.8 m vertical patch through <paramref name="center"/> with the given normal.</summary>
        static void AddWall(WallEstimator w, Vector3 center, Vector3 normal, int seed, float noise = Noise, float time = 0f, ulong firstId = 0)
        {
            var rng = new System.Random(seed);
            Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;
            ulong id = firstId;
            for (int i = 0; i < 400; i++)
            {
                float a = (float)(rng.NextDouble() - 0.5) * 0.8f;
                float b = (float)(rng.NextDouble() - 0.5) * 0.8f;
                float n = (float)(rng.NextDouble() * 2 - 1) * noise;
                w.Observe(id++, center + along * a + Vector3.up * b + normal * n, time);
            }
        }

        static void AddHorizontal(WallEstimator w, Vector3 center, float size, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < 400; i++)
            {
                float a = (float)(rng.NextDouble() - 0.5) * size;
                float b = (float)(rng.NextDouble() - 0.5) * size;
                float n = (float)(rng.NextDouble() * 2 - 1) * Noise;
                w.Observe((ulong)i, center + new Vector3(a, n, b), 0f);
            }
        }

        static Ray RayAt(Vector3 from, Vector3 to) => new Ray(from, (to - from).normalized);

        /// <summary>Same as Quaternion.AngleAxis(deg, Vector3.up) * v, in plain math (also runs outside the Editor).</summary>
        static Vector3 Yaw(Vector3 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
        }

        [Test]
        public void Wall_HeadOn_IsFound()
        {
            var w = new WallEstimator();
            AddWall(w, WallCenter, Vector3.back, 1);
            var ray = RayAt(new Vector3(0f, 1.4f, 0f), WallCenter);
            Assert.IsTrue(w.TryFit(ray, out Vector3 point, out Vector3 normal));
            Assert.That(Vector3.Dot(normal, Vector3.back), Is.GreaterThan(0.999f), "normal faces the user");
            Assert.That(normal.y, Is.EqualTo(0f), "exact wall normal");
            Assert.That(Mathf.Abs(Vector3.Dot(point - WallCenter, Vector3.back)), Is.LessThan(0.005f), "on the wall");
            Assert.IsTrue(w.Filter(point, normal, ray, 0f, out Vector3 hit, out _));
            Assert.That(Vector3.Distance(hit, WallCenter), Is.LessThan(0.01f));
        }

        [Test]
        public void Wall_AnyYawAndObliqueView_IsFound()
        {
            foreach (float yaw in new[] { 0f, 30f, 90f, 160f, -75f })
            {
                Vector3 normal = Yaw(Vector3.back, yaw);
                var w = new WallEstimator();
                AddWall(w, WallCenter, normal, 2);
                // From 1.2 m away, 40 degrees to the side and from below.
                Vector3 side = Yaw(normal, 40f);
                var ray = RayAt(WallCenter + side * 1.2f + Vector3.down * 0.3f, WallCenter);
                Assert.IsTrue(w.TryFit(ray, out _, out Vector3 n), $"yaw {yaw}");
                Assert.That(Vector3.Dot(n, normal), Is.GreaterThan(0.995f), $"yaw {yaw}");
            }
        }

        [Test]
        public void Floor_SeenAtGrazingAngle_IsRejected()
        {
            var w = new WallEstimator();
            AddHorizontal(w, new Vector3(0f, 0f, 1.5f), 2.0f, 3);
            Assert.IsFalse(w.TryFit(RayAt(new Vector3(0f, 1.4f, 0f), new Vector3(0f, 0f, 1.5f)), out _, out _));
        }

        [Test]
        public void TableTop_SeenFromAbove_IsRejected()
        {
            var w = new WallEstimator();
            AddHorizontal(w, new Vector3(0f, 0.75f, 0.3f), 0.8f, 4);
            foreach (var cam in new[] { new Vector3(0f, 1.15f, 0.3f), new Vector3(0f, 1.1f, -0.1f), new Vector3(0.3f, 1.0f, 0f) })
                Assert.IsFalse(w.TryFit(RayAt(cam, new Vector3(0f, 0.75f, 0.3f)), out _, out _), $"from {cam}");
        }

        [Test]
        public void Clutter_AndSparsePoints_AreRejected()
        {
            var w = new WallEstimator();
            var rng = new System.Random(5);
            for (int i = 0; i < 400; i++)
                w.Observe((ulong)i, WallCenter + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f,
                                                             (float)rng.NextDouble() - 0.5f) * 0.6f, 0f);
            var ray = RayAt(new Vector3(0f, 1.4f, 0f), WallCenter);
            Assert.IsFalse(w.TryFit(ray, out _, out _), "3D clutter");

            var sparse = new WallEstimator();
            for (int i = 0; i < WallEstimator.MinFitPoints - 1; i++)
                sparse.Observe((ulong)i, WallCenter + new Vector3((i % 4) * 0.05f - 0.075f, (i / 4) * 0.05f - 0.05f, 0f), 0f);
            Assert.IsFalse(sparse.TryFit(ray, out _, out _), "too few points");
        }

        [Test]
        public void NarrowVerticalStrip_IsRejected()
        {
            // A door frame or table leg: tall but not wide.
            var w = new WallEstimator();
            var rng = new System.Random(6);
            for (int i = 0; i < 200; i++)
                w.Observe((ulong)i, WallCenter + new Vector3((float)(rng.NextDouble() - 0.5) * 0.03f, (float)(rng.NextDouble() - 0.5) * 0.8f, 0f), 0f);
            Assert.IsFalse(w.TryFit(RayAt(new Vector3(0f, 1.4f, 0f), WallCenter), out _, out _));
        }

        [Test]
        public void Wall_WithSomeOutliers_IsFound()
        {
            var w = new WallEstimator();
            AddWall(w, WallCenter, Vector3.back, 7);
            var rng = new System.Random(8);
            for (int i = 0; i < 40; i++)   // e.g. a picture frame edge or a light switch in front of the wall
                w.Observe(10000UL + (ulong)i, WallCenter + new Vector3((float)(rng.NextDouble() - 0.5) * 0.3f,
                                                                      (float)(rng.NextDouble() - 0.5) * 0.3f, -0.08f), 0f);
            Assert.IsTrue(w.TryFit(RayAt(new Vector3(0f, 1.4f, 0f), WallCenter), out Vector3 point, out Vector3 n));
            Assert.That(Vector3.Dot(n, Vector3.back), Is.GreaterThan(0.999f));
            Assert.That(Mathf.Abs(Vector3.Dot(point - WallCenter, Vector3.back)), Is.LessThan(0.01f));
        }

        [Test]
        public void Prune_ForgetsOldPoints()
        {
            var w = new WallEstimator();
            AddWall(w, WallCenter, Vector3.back, 9, time: 0f);
            Assert.That(w.StoredPointCount, Is.EqualTo(400));
            w.Prune(WallEstimator.PointLifetime + 0.1f);
            Assert.That(w.StoredPointCount, Is.EqualTo(0));
            Assert.IsFalse(w.TryFit(RayAt(new Vector3(0f, 1.4f, 0f), WallCenter), out _, out _));
        }

        [Test]
        public void Filter_SmoothsJitter_RestartsOnNewSurface_AndHolds()
        {
            var w = new WallEstimator();
            var ray = RayAt(new Vector3(0f, 1.4f, 0f), WallCenter);
            Assert.IsTrue(w.Filter(WallCenter, Vector3.back, ray, 0f, out Vector3 h0, out _));
            // A 1 cm jump of the raw estimate moves the filtered hit by only a quarter of it.
            Assert.IsTrue(w.Filter(WallCenter + Vector3.back * 0.01f, Vector3.back, ray, 0.03f, out Vector3 h1, out _));
            Assert.That(Vector3.Distance(h0, h1), Is.InRange(0.001f, 0.004f));
            // A different wall 30 cm closer restarts the filter immediately.
            Assert.IsTrue(w.Filter(WallCenter + Vector3.back * 0.3f, Vector3.back, ray, 0.06f, out Vector3 h2, out _));
            Assert.That(h2.z, Is.EqualTo(0.7f).Within(1e-4f));
            // Short gaps are bridged, long ones are not.
            Assert.IsTrue(w.TryHold(ray, 0.2f, 0.3f, out _, out _));
            Assert.IsFalse(w.TryHold(ray, 0.5f, 0.3f, out _, out _));
        }

        [Test]
        public void SnapWallNormal_AcceptsWallsOnly_AndFacesViewer()
        {
            Assert.IsTrue(WallEstimator.TrySnapWallNormal(new Vector3(0.1f, 0.2f, 1f), WallCenter, Vector3.zero, out Vector3 n));
            Assert.That(n.y, Is.EqualTo(0f));
            Assert.That(Vector3.Dot(n, Vector3.zero - WallCenter), Is.GreaterThan(0f), "faces the viewer");
            Assert.IsFalse(WallEstimator.TrySnapWallNormal(Vector3.up, WallCenter, Vector3.zero, out _), "table");
            Assert.IsFalse(WallEstimator.TrySnapWallNormal(new Vector3(0f, 0.6f, -0.8f), WallCenter, Vector3.zero, out _), "45-ish degrees");
        }

        // ---------- plain walls: wide cone and remembered walls ----------

        /// <summary>Feature points only on a small textured patch (a socket, a picture corner) of the wall z = 1.</summary>
        static void AddPatch(WallEstimator w, Vector3 center, float size, int count, int seed, ulong firstId = 0)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
                w.Observe(firstId + (ulong)i, center + new Vector3((float)(rng.NextDouble() - 0.5) * size, (float)(rng.NextDouble() - 0.5) * size,
                                                                    (float)(rng.NextDouble() * 2 - 1) * Noise), 0f);
        }

        [Test]
        public void SparseWall_IsFoundByTheWideCone()
        {
            // 20 points spread over 0.9 m of wall: too few in the narrow cone, enough in the wide one.
            var w = new WallEstimator();
            AddPatch(w, WallCenter, 0.9f, 20, 11);
            var ray = RayAt(new Vector3(0f, 1.4f, 0f), WallCenter);
            Assert.IsTrue(w.TryFit(ray, out _, out Vector3 n), "wide cone");
            Assert.That(Vector3.Dot(n, Vector3.back), Is.GreaterThan(0.99f));
        }

        [Test]
        public void PlainWall_AimStaysOnTheRememberedWall()
        {
            var w = new WallEstimator();
            var cam = new Vector3(0f, 1.4f, 0f);
            var socket = new Vector3(-0.6f, 0.4f, 1f);   // the only texture: a socket low on a plain wall
            AddPatch(w, socket, 0.15f, 40, 12);

            Assert.IsTrue(w.TryEstimate(RayAt(cam, socket), 0f, 0.3f, out _, out _), "found at the socket");
            Assert.That(w.RememberedWallCount, Is.EqualTo(1));

            // Plain areas: no points anywhere near, yet the crosshair stays on the wall (after the hold has expired).
            foreach (var target in new[] { WallCenter, new Vector3(0.6f, 1.6f, 1f), new Vector3(-0.2f, 1.2f, 1f) })
            {
                Assert.IsTrue(w.TryEstimate(RayAt(cam, target), 1f, 0.3f, out Vector3 hit, out Vector3 n), $"plain spot {target}");
                Assert.That(Vector3.Distance(hit, target), Is.LessThan(0.01f), $"hit on the wall at {target}");
                Assert.That(Vector3.Dot(n, Vector3.back), Is.GreaterThan(0.999f));
            }

            // Too far from where the wall was seen, looking down at the floor, or after Clear: no wall.
            Assert.IsFalse(w.TryEstimate(RayAt(cam, new Vector3(2.4f, 1.4f, 1f)), 2f, 0.3f, out _, out _), "beyond reach");
            Assert.IsFalse(w.TryEstimate(RayAt(cam, new Vector3(0f, 0f, 0.6f)), 3f, 0.3f, out _, out _), "floor in front of the wall");
            w.Clear();
            Assert.IsFalse(w.TryEstimate(RayAt(cam, WallCenter), 4f, 0.3f, out _, out _), "cleared");
        }

        [Test]
        public void RememberedWall_GrowsAndIsNotDuplicated()
        {
            var w = new WallEstimator();
            var cam = new Vector3(0f, 1.4f, 0f);
            var left = new Vector3(-0.6f, 1.4f, 1f);
            var right = new Vector3(0.6f, 1.4f, 1.005f);   // same wall, a few mm off
            AddPatch(w, left, 0.15f, 40, 13);
            AddPatch(w, right, 0.15f, 40, 14, 1000);
            Assert.IsTrue(w.TryEstimate(RayAt(cam, left), 0f, 0.3f, out _, out _));
            Assert.IsTrue(w.TryEstimate(RayAt(cam, right), 0.1f, 0.3f, out _, out _));
            Assert.That(w.RememberedWallCount, Is.EqualTo(1), "one wall, not two");

            // A second, perpendicular wall is remembered separately.
            var side = new WallEstimator();
            AddWall(side, new Vector3(1.2f, 1.4f, 0.5f), Vector3.left, 16);
            Assert.IsTrue(side.TryEstimate(RayAt(cam, new Vector3(1.2f, 1.4f, 0.5f)), 0f, 0.3f, out _, out Vector3 n2));
            Assert.That(Vector3.Dot(n2, Vector3.left), Is.GreaterThan(0.99f));
        }

        // ---------- steadiness: distance-scaled on walls only ----------

        static bool AddCornerWithSpread(CornerPickSession s, Vector3 camPos, Vector3 target, Vector3 jitterAxis, float spread)
        {
            for (int i = 0; i < CornerPickSession.SteadyFrames; i++)
            {
                float k = (i % 2 == 0 ? 0.5f : -0.5f) * spread;   // samples alternate +-spread/2: each is spread/2 off the mean
                s.UpdateAim(new Ray(camPos, target + jitterAxis * k - camPos));
            }
            return s.TryAddCorner();
        }

        [Test]
        public void Steadiness_WallAtDistance_ToleratesHandTremor()
        {
            var s = new CornerPickSession();
            s.SetPlane(WallCenter, Vector3.back);
            var cam = new Vector3(0f, 1.4f, -0.5f);   // 1.5 m from the wall: limit 24 mm
            Assert.That(s.SteadySpreadLimit, Is.GreaterThanOrEqualTo(CornerPickSession.MaxSteadySpread));
            Assert.IsTrue(AddCornerWithSpread(s, cam, WallCenter, Vector3.right, 0.020f), s.LastActionIssue.ToString());
            Assert.That(s.SteadySpreadLimit, Is.EqualTo(1.5f * CornerPickSession.WallSteadySpreadPerMeter).Within(1e-3f));

            Assert.IsFalse(AddCornerWithSpread(s, cam, WallCenter + Vector3.right * 0.3f, Vector3.right, 0.060f), "real motion still rejected");
            Assert.That(s.LastActionIssue, Is.EqualTo(CornerIssue.Unsteady));
        }

        [Test]
        public void Steadiness_Table_IsUnchanged()
        {
            var s = new CornerPickSession();
            s.SetPlane(Vector3.zero, Vector3.up);
            var cam = new Vector3(0f, 1.2f, -0.6f);   // far from the table: still the fixed 8 mm limit
            Assert.IsFalse(AddCornerWithSpread(s, cam, Vector3.zero, Vector3.right, 0.020f), "same tremor as the wall test");
            Assert.That(s.LastActionIssue, Is.EqualTo(CornerIssue.Unsteady));
            Assert.That(s.SteadySpreadLimit, Is.EqualTo(CornerPickSession.MaxSteadySpread));
            Assert.IsTrue(AddCornerWithSpread(s, cam, Vector3.zero, Vector3.right, 0.006f));
        }
    }
}
