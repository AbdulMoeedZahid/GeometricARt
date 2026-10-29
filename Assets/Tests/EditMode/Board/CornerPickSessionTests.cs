using GeoART.BoardRender;
using GeoART.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace GeoART.Tests
{
    /// <summary>
    /// Corner picking with synthetic camera rays, no AR (CORNER_PLACEMENT_PLAN 5, tests 8-16): the full flow, the
    /// handedness link between CornerRectFit and BoardGeometry, bottom-edge orientation, aim rejections, undo/clear.
    /// </summary>
    public class CornerPickSessionTests
    {
        const float Lift = 0.002f;
        const float Tol = 1e-4f;
        static readonly Vector3 CamPos = new Vector3(0f, 0.35f, -0.30f);
        static readonly Vector3 RectCenter = new Vector3(0.05f, 0f, 0.1f);

        GameObject _boardGo, _camGo;
        Transform _board;
        Camera _cam;
        RenderTexture _rt;

        [SetUp]
        public void SetUp()
        {
            _boardGo = new GameObject("TestBoard");
            _board = _boardGo.transform;
            _camGo = new GameObject("TestCamera");
            _cam = _camGo.AddComponent<Camera>();
            _rt = new RenderTexture(390, 844, 16);
            _cam.targetTexture = _rt;
            _cam.fieldOfView = 60f;
            _cam.nearClipPlane = 0.02f;
            AimCamera(CamPos);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_boardGo);
            Object.DestroyImmediate(_camGo);
            Object.DestroyImmediate(_rt);
        }

        void AimCamera(Vector3 position)
        {
            _camGo.transform.position = position;
            _camGo.transform.LookAt(RectCenter);
        }

        /// <summary>World corners (counter-clockwise seen from above) of a w x h rectangle on y = height, yawed by deg.</summary>
        static Vector3[] RectCorners(Vector3 center, float w, float h, float deg, float height = 0f)
        {
            var rot = Quaternion.AngleAxis(deg, Vector3.up);
            var c = new Vector3(center.x, height, center.z);
            return new[]
            {
                c + rot * new Vector3(-w / 2, 0, -h / 2), c + rot * new Vector3(w / 2, 0, -h / 2),
                c + rot * new Vector3(w / 2, 0, h / 2), c + rot * new Vector3(-w / 2, 0, h / 2),
            };
        }

        static bool Feed(CornerPickSession s, Vector3 camPos, Vector3 corner, int samples = CornerPickSession.SteadyFrames)
        {
            var ray = new Ray(camPos, corner - camPos);
            for (int i = 0; i < samples; i++) s.UpdateAim(ray);
            return s.TryAddCorner();
        }

        static CornerPickSession PickA4()
        {
            var s = new CornerPickSession();
            s.SetPlane(Vector3.zero, Vector3.up);
            foreach (var c in RectCorners(RectCenter, 0.21f, 0.297f, 20f))
                Assert.IsTrue(Feed(s, CamPos, c), $"add {c}: {s.LastActionIssue}");
            return s;
        }

        Vector3 TowardTop(CornerPickSession s) =>
            CornerPickSession.TowardTop(s.PlaneNormal, _cam.transform.forward, _cam.transform.up, false);

        static void AssertNear(Vector3 expected, Vector3 actual, float tol, string label)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(tol), $"{label}: expected {expected:F5}, got {actual:F5}");
        }

        [Test]
        public void PlacementRotation_IsNotMirrored()
        {
            var s = PickA4();
            Assert.IsTrue(s.TryGetPlacement(TowardTop(s), Lift, out Pose pose, out RectFrame frame));
            Assert.That(Vector3.Dot(pose.rotation * Vector3.right, s.FromPlaneDir(frame.Right).normalized), Is.GreaterThan(0.9999f));
            Assert.That(Vector3.Dot(pose.rotation * Vector3.forward, s.FromPlaneDir(frame.Up).normalized), Is.GreaterThan(0.9999f));
            Assert.That(Vector3.Dot(pose.rotation * Vector3.up, s.PlaneNormal), Is.GreaterThan(0.9999f));
        }

        [Test]
        public void FullFlow_SyntheticRays_A4()
        {
            var s = PickA4();
            Assert.That(s.CornerCount, Is.EqualTo(4));
            Assert.IsTrue(s.HasValidFit, s.FitIssue.ToString());
            Assert.IsTrue(s.TryGetPlacement(TowardTop(s), Lift, out Pose pose, out RectFrame frame));
            Assert.That(frame.SquareSide, Is.EqualTo(0.21).Within(Tol));
            Assert.That(System.Math.Max(frame.Width, frame.Height), Is.EqualTo(0.297).Within(Tol));
            AssertNear(RectCenter + Vector3.up * Lift, pose.position, Tol, "board centre");
        }

        [Test]
        public void FittedBoard_ScreenRoundTrip_NonDefaultSide()
        {
            var s = PickA4();
            Assert.IsTrue(s.TryGetPlacement(TowardTop(s), Lift, out Pose pose, out RectFrame frame));
            float side = (float)frame.SquareSide;
            Assert.That(side, Is.Not.EqualTo(BoardGeometry.DefaultSideMeters).Within(0.01f));
            _board.SetPositionAndRotation(pose.position, pose.rotation);

            var points = new[] { new Vec2(0.5, 0.5), new Vec2(0.02, 0.02), new Vec2(0.98, 0.02), new Vec2(0.02, 0.98), new Vec2(0.98, 0.98) };
            foreach (var p in points)
            {
                Assert.IsTrue(BoardGeometry.TryBoardToScreen(_cam, _board, side, p, out var px), $"project {p}");
                Assert.IsTrue(BoardGeometry.TryScreenToBoard(_cam, _board, side, px, out var back), $"unproject {p}");
                Assert.That(back.X, Is.EqualTo(p.X).Within(1e-4), $"{p} u");
                Assert.That(back.Y, Is.EqualTo(p.Y).Within(1e-4), $"{p} v");

                // Handedness link: BoardGeometry's local mapping equals CornerRectFit's BoardPoint on the plane.
                Vector3 viaBoard = _board.TransformPoint(BoardGeometry.BoardToLocal(p, side));
                Vector3 viaFit = s.FromPlane(frame.BoardPoint(p)) + s.PlaneNormal * Lift;
                AssertNear(viaFit, viaBoard, Tol, $"board point {p}");
            }
        }

        [Test]
        public void BottomEdgeFacesCamera()
        {
            var s = PickA4();
            Assert.IsTrue(s.TryGetPlacement(TowardTop(s), Lift, out Pose pose, out RectFrame frame));
            float side = (float)frame.SquareSide;
            _board.SetPositionAndRotation(pose.position, pose.rotation);

            Vector3 nearW = _board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(0.5, 0), side));
            Vector3 farW = _board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(0.5, 1), side));
            Assert.That(Vector3.Distance(CamPos, nearW), Is.LessThan(Vector3.Distance(CamPos, farW)), "v = 0 is the near edge");

            BoardGeometry.TryBoardToScreen(_cam, _board, side, new Vec2(0.1, 0.5), out var left);
            BoardGeometry.TryBoardToScreen(_cam, _board, side, new Vec2(0.9, 0.5), out var right);
            BoardGeometry.TryBoardToScreen(_cam, _board, side, new Vec2(0.5, 0.1), out var near);
            BoardGeometry.TryBoardToScreen(_cam, _board, side, new Vec2(0.5, 0.9), out var far);
            Assert.That(right.X, Is.GreaterThan(left.X), "+u appears to the right");
            Assert.That(far.Y, Is.GreaterThan(near.Y), "+v (top) appears farther / higher");

            // Walk to +X and look back along -X: the frame turns by 90 degrees, so the near edge faces the user again.
            var camX = new Vector3(0.6f, 0.35f, 0.1f);
            AimCamera(camX);
            Assert.IsTrue(s.TryGetPlacement(TowardTop(s), Lift, out Pose pose2, out RectFrame frame2));
            Assert.That(System.Math.Abs(frame.Up.X * frame2.Up.X + frame.Up.Y * frame2.Up.Y), Is.LessThan(1e-6), "Up rotated by 90 degrees");
            Assert.That(frame2.Width, Is.EqualTo(frame.Height).Within(1e-9));
            _board.SetPositionAndRotation(pose2.position, pose2.rotation);
            Vector3 near2 = _board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(0.5, 0), side));
            Vector3 far2 = _board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(0.5, 1), side));
            Assert.That(Vector3.Distance(camX, near2), Is.LessThan(Vector3.Distance(camX, far2)));
        }

        [Test]
        public void TowardTop_LookingStraightDown_UsesCameraUp()
        {
            Vector3 t = CornerPickSession.TowardTop(Vector3.up, Vector3.down, Vector3.forward, false);
            Assert.That(Vector3.Dot(t.normalized, Vector3.forward), Is.GreaterThan(0.9999f));

            // Vertical surface (deferred, D8): gravity-up projected onto the wall.
            Vector3 g = CornerPickSession.TowardTop(Vector3.back, Vector3.forward, Vector3.up, true);
            Assert.That(Vector3.Dot(g.normalized, Vector3.up), Is.GreaterThan(0.9999f));
        }

        [Test]
        public void Aim_Rejections()
        {
            var s = new CornerPickSession();
            s.SetPlane(Vector3.zero, Vector3.up);

            s.UpdateAim(new Ray(new Vector3(0f, 0.1f, -1f), new Vector3(0f, -0.1f, 1f)));
            Assert.IsFalse(s.AimValid);
            Assert.That(s.AimIssue, Is.EqualTo(CornerIssue.AimTooShallow));

            s.UpdateAim(new Ray(new Vector3(0f, 0.3f, 0f), Vector3.up));
            Assert.That(s.AimIssue, Is.EqualTo(CornerIssue.NoSurface));

            var origin = new Vector3(0f, 1f, 0f);
            s.UpdateAim(new Ray(origin, new Vector3(0f, 0f, Mathf.Sqrt(8f)) - origin));   // 3 m along the ray
            Assert.That(s.AimIssue, Is.EqualTo(CornerIssue.TooFar));

            // Fewer than SteadyFrames valid samples.
            Assert.IsFalse(Feed(s, CamPos, RectCenter, CornerPickSession.SteadyFrames - 1));
            Assert.That(s.LastActionIssue, Is.EqualTo(CornerIssue.Unsteady));

            // Samples spread 2 cm (alternating +-1 cm).
            for (int i = 0; i < CornerPickSession.SteadyFrames; i++)
            {
                var target = RectCenter + new Vector3(i % 2 == 0 ? -0.01f : 0.01f, 0f, 0f);
                s.UpdateAim(new Ray(CamPos, target - CamPos));
            }
            Assert.IsTrue(s.AimValid);
            Assert.IsFalse(s.TryAddCorner());
            Assert.That(s.LastActionIssue, Is.EqualTo(CornerIssue.Unsteady));
            Assert.That(s.CornerCount, Is.EqualTo(0));
        }

        [Test]
        public void TooCloseToExistingCorner_IsRejected()
        {
            var s = new CornerPickSession();
            s.SetPlane(Vector3.zero, Vector3.up);
            Assert.IsTrue(Feed(s, CamPos, RectCenter));
            Assert.IsFalse(Feed(s, CamPos, RectCenter + new Vector3(0.03f, 0f, 0f)));
            Assert.That(s.LastActionIssue, Is.EqualTo(CornerIssue.TooCloseToCorner));
            Assert.That(s.LastActionIssueCorner, Is.EqualTo(0));
        }

        [Test]
        public void UndoClear_UpdateCountsAndFit()
        {
            var s = new CornerPickSession();
            s.SetPlane(Vector3.zero, Vector3.up);
            // Trapezoid: top 13 cm, bottom 21 cm -> invalid fit.
            var pts = new[]
            {
                new Vector3(-0.105f, 0f, 0f), new Vector3(0.105f, 0f, 0f),
                new Vector3(0.065f, 0f, 0.297f), new Vector3(-0.065f, 0f, 0.297f),
            };
            foreach (var p in pts) Assert.IsTrue(Feed(s, CamPos, p));
            Assert.That(s.CornerCount, Is.EqualTo(4));
            Assert.IsFalse(s.HasValidFit);
            Assert.That(s.FitIssue, Is.EqualTo(CornerIssue.NotRectangular));

            int v = s.Version;
            s.UndoCorner();
            Assert.That(s.CornerCount, Is.EqualTo(3));
            Assert.That(s.FitIssue, Is.EqualTo(CornerIssue.None));
            Assert.That(s.FitIssueCorner, Is.EqualTo(-1));
            Assert.That(s.Version, Is.GreaterThan(v));

            v = s.Version;
            s.Clear();
            Assert.That(s.CornerCount, Is.EqualTo(0));
            Assert.That(s.Version, Is.GreaterThan(v));
        }

        [Test]
        public void CornersOffPlane_AreProjected()
        {
            var s = new CornerPickSession();
            var corners = RectCorners(RectCenter, 0.21f, 0.297f, 20f);
            for (int i = 0; i < 4; i++)
            {
                // Each corner is taken while ARKit reports the plane 1 cm higher or lower (height refinement).
                float h = i % 2 == 0 ? 0.01f : -0.01f;
                s.SetPlane(new Vector3(0f, h, 0f), Vector3.up);
                Assert.IsTrue(Feed(s, CamPos, corners[i] + Vector3.up * h), $"corner {i}: {s.LastActionIssue}");
            }
            s.SetPlane(Vector3.zero, Vector3.up);
            for (int i = 0; i < 4; i++) AssertNear(corners[i], s.GetCorner(i), 1e-5f, $"corner {i} projected");
            Assert.IsTrue(s.TryGetPlacement(TowardTop(s), 0f, out _, out RectFrame frame));
            Assert.That(frame.SquareSide, Is.EqualTo(0.21).Within(1e-5));
            Assert.That(System.Math.Max(frame.Width, frame.Height), Is.EqualTo(0.297).Within(1e-5));
            Assert.That(s.Fit.MaxResidual, Is.LessThan(1e-5));
        }

        [Test]
        public void BoardView_StrokeScaleFor()
        {
            Assert.That(BoardView.StrokeScaleFor(0.21f), Is.EqualTo(0.525f).Within(1e-6f));
            Assert.That(BoardView.StrokeScaleFor(0.10f), Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(BoardView.StrokeScaleFor(1.5f), Is.EqualTo(2.0f).Within(1e-6f));
        }
    }
}
