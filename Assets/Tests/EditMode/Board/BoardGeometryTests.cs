using GeoART.BoardRender;
using GeoART.Contracts;
using GeoART.Mocks;
using NUnit.Framework;
using UnityEngine;

namespace GeoART.Tests
{
    /// <summary>
    /// A's projection checks behind P2-04 (no mirrored/rotated mapping, corners as well as centre) and the
    /// near-parallel / behind-camera / off-board rejections required by P2 4.3.
    /// </summary>
    public class BoardGeometryTests
    {
        const float Side = BoardGeometry.DefaultSideMeters;
        const double Tol = 1e-4;

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
            _rt = new RenderTexture(390, 844, 16);   // iPhone-like portrait pixel size, independent of the Editor window
            _cam.targetTexture = _rt;
            _cam.fieldOfView = 60f;
            _cam.nearClipPlane = 0.02f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_boardGo);
            Object.DestroyImmediate(_camGo);
            Object.DestroyImmediate(_rt);
        }

        void PlaceBoardFacingCamera(Vector3 boardPos, Vector3 camPos)
        {
            _camGo.transform.position = camPos;
            _camGo.transform.LookAt(boardPos);
            _board.SetPositionAndRotation(boardPos, BoardGeometry.FacingRotation(Vector3.up, _cam.transform.forward));
        }

        static void AssertUv(Vec2 expected, Vec2 actual, string label = "")
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(Tol), $"{label} u");
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(Tol), $"{label} v");
        }

        [Test]
        public void BoardToLocal_MapsCornersAndCentre()
        {
            Assert.That(BoardGeometry.BoardToLocal(new Vec2(0.5, 0.5), Side), Is.EqualTo(Vector3.zero));
            var bl = BoardGeometry.BoardToLocal(new Vec2(0, 0), Side);
            var tr = BoardGeometry.BoardToLocal(new Vec2(1, 1), Side);
            Assert.That(bl.x, Is.EqualTo(-Side / 2).Within(1e-6f));
            Assert.That(bl.z, Is.EqualTo(-Side / 2).Within(1e-6f));
            Assert.That(tr.x, Is.EqualTo(Side / 2).Within(1e-6f));
            Assert.That(tr.z, Is.EqualTo(Side / 2).Within(1e-6f));
            AssertUv(new Vec2(0.25, 0.8), BoardGeometry.LocalToBoard(BoardGeometry.BoardToLocal(new Vec2(0.25, 0.8), Side), Side));
        }

        [Test]
        public void RayStraightDown_HitsCentre()
        {
            Assert.IsTrue(BoardGeometry.TryRayToBoard(new Ray(new Vector3(0, 0.5f, 0), Vector3.down), _board, Side, out var uv));
            AssertUv(new Vec2(0.5, 0.5), uv);
        }

        [Test]
        public void NearParallelRay_IsRejected()
        {
            var ray = new Ray(new Vector3(-1f, 0.001f, 0f), new Vector3(1f, -0.001f, 0f));
            Assert.IsFalse(BoardGeometry.TryRayToBoardPlane(ray, _board, Side, out _));
        }

        [Test]
        public void RayPointingAway_IsRejected()
        {
            Assert.IsFalse(BoardGeometry.TryRayToBoardPlane(new Ray(new Vector3(0, 0.5f, 0), Vector3.up), _board, Side, out _));
        }

        [Test]
        public void HitOutsideBoard_IsRejectedButPlaneHitIsReported()
        {
            var ray = new Ray(new Vector3(0.5f, 0.5f, 0f), Vector3.down);
            Assert.IsFalse(BoardGeometry.TryRayToBoard(ray, _board, Side, out _));
            Assert.IsTrue(BoardGeometry.TryRayToBoardPlane(ray, _board, Side, out var uv));
            Assert.That(uv.X, Is.GreaterThan(1.0));
        }

        [Test]
        public void ScreenRoundTrip_CentreAndCorners_OnRotatedOffsetBoard()
        {
            PlaceBoardFacingCamera(new Vector3(0.3f, -0.2f, 0.9f), new Vector3(0.15f, 0.25f, 0.45f));
            var points = new[] { new Vec2(0.5, 0.5), new Vec2(0.02, 0.02), new Vec2(0.98, 0.02), new Vec2(0.02, 0.98), new Vec2(0.98, 0.98) };
            foreach (var p in points)
            {
                Assert.IsTrue(BoardGeometry.TryBoardToScreen(_cam, _board, Side, p, out var px), $"project {p}");
                Assert.IsTrue(BoardGeometry.TryScreenToBoard(_cam, _board, Side, px, out var back), $"unproject {p}");
                AssertUv(p, back, p.ToString());
            }
        }

        [Test]
        public void Orientation_IsNotMirrored_RightIsRightAndTopIsFar()
        {
            PlaceBoardFacingCamera(Vector3.zero, new Vector3(0f, 0.36f, -0.3f));
            BoardGeometry.TryBoardToScreen(_cam, _board, Side, new Vec2(0.1, 0.5), out var left);
            BoardGeometry.TryBoardToScreen(_cam, _board, Side, new Vec2(0.9, 0.5), out var right);
            BoardGeometry.TryBoardToScreen(_cam, _board, Side, new Vec2(0.5, 0.1), out var near);
            BoardGeometry.TryBoardToScreen(_cam, _board, Side, new Vec2(0.5, 0.9), out var far);
            Assert.That(right.X, Is.GreaterThan(left.X), "+u must appear to the right on screen");
            Assert.That(far.Y, Is.GreaterThan(near.Y), "+v (board top) must be farther from the user, higher on screen");
        }

        [Test]
        public void FacingRotation_PointsBoardTopAwayFromCamera()
        {
            var rot = BoardGeometry.FacingRotation(Vector3.up, new Vector3(0f, -0.7f, 0.7f));
            Assert.That(Vector3.Dot(rot * Vector3.forward, Vector3.forward), Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(rot * Vector3.up, Vector3.up), Is.GreaterThan(0.999f));
            // Looking straight down still yields a valid rotation.
            var down = BoardGeometry.FacingRotation(Vector3.up, Vector3.down);
            Assert.That(Vector3.Dot(down * Vector3.up, Vector3.up), Is.GreaterThan(0.999f));
        }

        [Test]
        public void BehindCamera_IsNotProjected()
        {
            PlaceBoardFacingCamera(Vector3.zero, new Vector3(0f, 0.36f, -0.3f));
            _camGo.transform.rotation = Quaternion.LookRotation(Vector3.back);   // facing away from the board
            Assert.IsFalse(BoardGeometry.TryBoardToScreen(_cam, _board, Side, new Vec2(0.5, 0.5), out _));
        }

        [Test]
        public void MeshBuilder_DashedSegmentProducesGaps()
        {
            var mb = new BoardMeshBuilder();
            float phase = 0f;
            mb.AddDashedSegment(Vector3.zero, new Vector3(0.1f, 0, 0), 0.002f, Color.black, 0.008f, 0.006f, ref phase);
            int dashes = mb.VertexCount / 4;
            Assert.That(dashes, Is.EqualTo(Mathf.CeilToInt(0.1f / 0.014f)).Within(1));
            mb.Clear();
            mb.AddSegment(Vector3.zero, Vector3.zero, 0.002f, Color.black);   // zero length draws nothing
            Assert.That(mb.VertexCount, Is.EqualTo(0));
        }

        [Test]
        public void MeshBuilder_DashedSegmentTerminatesForAnyLength()
        {
            // Regression: the old float-accumulating loop stalled at t = 0.022 under IL2CPP (strict float32), freezing the
            // app on the first corner (CornerPickerView's dashed rubber band). Every length up to 2.5 m must finish with
            // the expected dash count, also with a carried phase and a degenerate pattern.
            var mb = new BoardMeshBuilder();
            for (int mm = 1; mm <= 2500; mm++)
            {
                float len = mm * 0.001f, phase = 0f;
                mb.Clear();
                mb.AddDashedSegment(Vector3.zero, new Vector3(len, 0, 0), 0.0015f, Color.black, 0.008f, 0.006f, ref phase);
                Assert.That(mb.VertexCount / 4, Is.EqualTo(Mathf.CeilToInt(len / 0.014f)).Within(1), $"len {len}");
                Assert.That(phase, Is.EqualTo(Mathf.Repeat(len, 0.014f)).Within(1e-5f));
            }

            float carried = 0.011f;   // starts inside a gap
            mb.Clear();
            mb.AddDashedSegment(Vector3.zero, new Vector3(0.05f, 0, 0), 0.0015f, Color.black, 0.008f, 0.006f, ref carried);
            Assert.That(mb.VertexCount / 4, Is.InRange(3, 5));

            float zero = 0f;
            mb.Clear();
            mb.AddDashedSegment(Vector3.zero, new Vector3(1000f, 0, 0), 0.0015f, Color.black, 0f, 0f, ref zero);
            Assert.That(mb.VertexCount / 4, Is.LessThanOrEqualTo(BoardMeshBuilder.MaxDashesPerSegment + 1));
        }

        [Test]
        public void StubSession_ServesFixtureAndGridChanges()
        {
            var stub = new StubEditorSession(GoldenBoards.StarOnFullBoard());
            Assert.That(stub.Board.Layers.Count, Is.EqualTo(3));
            Assert.That(stub.Board.Layers[2].Name, Is.EqualTo(GoldenBoards.StarLayerName));
            BoardChangeKind seen = BoardChangeKind.None;
            stub.BoardChanged += k => seen = k;
            stub.SetGrid(3);
            Assert.That(stub.Board.GridN, Is.EqualTo(3));
            Assert.That(seen, Is.EqualTo(BoardChangeKind.Grid));
        }
    }
}
