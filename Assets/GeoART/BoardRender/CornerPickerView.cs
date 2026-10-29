using System.Collections.Generic;
using GeoART.Contracts;
using UnityEngine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Visuals for corner placement (CORNER_PLACEMENT_PLAN 4.5): the aim marker under the screen-centre crosshair, the
    /// picked corners, a rubber band to the aim point and, with 4 corners, the fitted rectangle preview (green outline,
    /// translucent board square, arrow on the edge that becomes the top) or a red outline if the fit is invalid.
    /// The owner calls <see cref="Show"/> every frame while picking and <see cref="Hide"/> otherwise. The mesh is tiny
    /// and rebuilt every visible frame with reused buffers, so there is no GC.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CornerPickerView : MonoBehaviour
    {
        [SerializeField, Tooltip("Material using the GeoART/BoardUnlit shader with vertex colours on.")]
        Material material;
        [SerializeField, Tooltip("Used to size the aim marker by distance. Defaults to Camera.main.")]
        Camera viewCamera;
        [SerializeField] Color aimColor = Color.white;
        [SerializeField] Color cornerColor = new Color(0.1f, 0.85f, 1f, 1f);
        [SerializeField] Color badColor = new Color(1f, 0.15f, 0.15f, 1f);
        [SerializeField] Color fitColor = new Color(0.2f, 1f, 0.4f, 1f);
        [SerializeField] Color boardFillColor = new Color(1f, 1f, 1f, 0.12f);

        const float OutlineWidth = 0.002f, BandWidth = 0.0015f, MarkerWidth = 0.0015f;
        const float CornerRadius = 0.004f, Dash = 0.008f, Gap = 0.006f;
        const float GreyAlpha = 0.4f;

        readonly BoardMeshBuilder _mb = new BoardMeshBuilder();
        readonly List<Vector3> _poly = new List<Vector3>(8);
        readonly Vec2[] _pts = new Vec2[CornerPickSession.RequiredCorners];
        readonly int[] _order = new int[CornerPickSession.RequiredCorners];
        GameObject _meshGo;
        Mesh _mesh;
        Material _instance;

        public Camera ViewCamera { get => viewCamera; set => viewCamera = value; }

        void Awake()
        {
            _meshGo = new GameObject("CornerPickerMesh");
            _meshGo.transform.SetParent(transform, false);
            _meshGo.layer = gameObject.layer;
            _mesh = new Mesh { name = "CornerPicker" };
            _mesh.MarkDynamic();
            _meshGo.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _meshGo.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (material != null)
            {
                _instance = new Material(material) { renderQueue = 3004 };
                mr.sharedMaterial = _instance;
            }
            _meshGo.SetActive(false);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_instance != null) Destroy(_instance);
        }

        public void Hide()
        {
            if (_meshGo != null && _meshGo.activeSelf) _meshGo.SetActive(false);
        }

        /// <summary>
        /// Draws the current picking state. <paramref name="frame"/> is used only when <paramref name="previewValid"/>.
        /// <paramref name="highlightCorner"/> (pick order) is drawn red, e.g. the corner named by the current issue.
        /// </summary>
        public void Show(CornerPickSession s, bool previewValid, in RectFrame frame, float lift, int highlightCorner = -1)
        {
            if (_meshGo == null || s == null || !s.HasPlane) { Hide(); return; }

            // Local XZ of this transform = the plane's 2D coordinates (see CornerPickSession).
            transform.SetPositionAndRotation(s.PlanePoint + s.PlaneNormal * lift,
                                             Quaternion.LookRotation(s.PlaneAxisY, s.PlaneNormal));
            _mb.Clear();
            int count = s.CornerCount;

            if (count == CornerPickSession.RequiredCorners)
            {
                if (previewValid) AddPreview(frame);
                else AddInvalidOutline(s);
            }

            // Rubber band: picked corners in pick order, then dashed to the aim point (and back to corner 0 at 3 corners).
            for (int i = 0; i + 1 < count; i++)
                _mb.AddSegment(L(s, s.GetCorner(i)), L(s, s.GetCorner(i + 1)), BandWidth, cornerColor);
            if (count > 0 && count < CornerPickSession.RequiredCorners && s.AimValid)
            {
                Vector3 last = L(s, s.GetCorner(count - 1)), aim = L(s, s.AimPoint);
                float phase = 0f;
                _mb.AddDashedSegment(last, aim, BandWidth, cornerColor, Dash, Gap, ref phase);
                if (count == 3)
                {
                    phase = 0f;
                    _mb.AddDashedSegment(aim, L(s, s.GetCorner(0)), BandWidth, cornerColor, Dash, Gap, ref phase);
                }
            }

            for (int i = 0; i < count; i++)
                _mb.AddDisc(L(s, s.GetCorner(i)), CornerRadius, i == highlightCorner ? badColor : cornerColor, 16);

            if (count < CornerPickSession.RequiredCorners) AddAimMarker(s);

            _mb.Apply(_mesh);
            if (!_meshGo.activeSelf) _meshGo.SetActive(true);
        }

        void AddAimMarker(CornerPickSession s)
        {
            bool valid = s.AimValid;
            if (!valid && !s.HasLastAim) return;
            Vector3 world = valid ? s.AimPoint : s.LastAimPoint;
            Color color = aimColor;
            if (!valid) color.a *= GreyAlpha;

            Camera cam = viewCamera != null ? viewCamera : Camera.main;
            float distance = cam != null ? Vector3.Distance(cam.transform.position, world) : 0.4f;
            float r = Mathf.Clamp(0.03f * distance, 0.008f, 0.03f);

            Vector3 c = L(s, world);
            _mb.AddCircle(c, r, 32, MarkerWidth, color);
            // Cross with a gap in the centre so the exact point stays visible.
            float a = 0.35f * r, b = 1.4f * r;
            _mb.AddSegment(c + new Vector3(a, 0f, 0f), c + new Vector3(b, 0f, 0f), MarkerWidth, color);
            _mb.AddSegment(c - new Vector3(a, 0f, 0f), c - new Vector3(b, 0f, 0f), MarkerWidth, color);
            _mb.AddSegment(c + new Vector3(0f, 0f, a), c + new Vector3(0f, 0f, b), MarkerWidth, color);
            _mb.AddSegment(c - new Vector3(0f, 0f, a), c - new Vector3(0f, 0f, b), MarkerWidth, color);
        }

        void AddPreview(in RectFrame frame)
        {
            // Registered surface outline.
            _poly.Clear();
            _poly.Add(L(frame.SurfacePoint(0, 0))); _poly.Add(L(frame.SurfacePoint(1, 0)));
            _poly.Add(L(frame.SurfacePoint(1, 1))); _poly.Add(L(frame.SurfacePoint(0, 1)));
            _mb.AddPolyline(_poly, true, OutlineWidth, fitColor);

            // Board square (largest centred square, D1): translucent fill and frame.
            Vector3 b00 = L(frame.BoardPoint(new Vec2(0, 0))), b10 = L(frame.BoardPoint(new Vec2(1, 0)));
            Vector3 b11 = L(frame.BoardPoint(new Vec2(1, 1))), b01 = L(frame.BoardPoint(new Vec2(0, 1)));
            _mb.AddQuad(b00, b01, b11, b10, boardFillColor);
            _poly.Clear();
            _poly.Add(b00); _poly.Add(b10); _poly.Add(b11); _poly.Add(b01);
            _mb.AddPolyline(_poly, true, OutlineWidth, aimColor);

            // Arrow on the edge that becomes the board top, same shape as ReticleView.
            Vector3 tip = L(frame.BoardPoint(new Vec2(0.5, 0.99)));
            _mb.AddSegment(L(frame.BoardPoint(new Vec2(0.44, 0.94))), tip, OutlineWidth, fitColor);
            _mb.AddSegment(L(frame.BoardPoint(new Vec2(0.56, 0.94))), tip, OutlineWidth, fitColor);
        }

        void AddInvalidOutline(CornerPickSession s)
        {
            for (int i = 0; i < _pts.Length; i++) _pts[i] = s.ToPlane(s.GetCorner(i));
            CornerRectFit.OrderAroundCentroid(_pts, _order);
            _poly.Clear();
            for (int i = 0; i < _order.Length; i++) _poly.Add(L(_pts[_order[i]]));
            _mb.AddPolyline(_poly, true, OutlineWidth, badColor);
        }

        static Vector3 L(Vec2 p) => new Vector3((float)p.X, 0f, (float)p.Y);
        static Vector3 L(CornerPickSession s, Vector3 world) => L(s.ToPlane(world));
    }
}
