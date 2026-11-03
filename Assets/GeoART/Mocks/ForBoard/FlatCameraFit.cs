using GeoART.BoardRender;
using GeoART.Contracts;
using UnityEngine;

namespace GeoART.Mocks
{
    /// <summary>
    /// Editor sandbox helper: moves the flat-scene camera so the WHOLE board is visible at any aspect ratio (portrait or
    /// landscape), clear of the right-edge ToolBar and the left-edge layer sidebar. The camera keeps its viewing
    /// direction (the tilt set in the scene, ~50 degrees in Sandbox_A_Flat); only its distance to the board centre
    /// changes, found by bisection so all four board corners land inside the screen minus the margins.
    /// Refits on Start and whenever the screen size or safe area changes (one cheap comparison per frame), or when
    /// <see cref="Refit"/> is called (e.g. after a test tilts the camera). Not used on device: AR moves the camera.
    /// Added to the camera by SceneBuilder (Sandbox_A_Flat).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class FlatCameraFit : MonoBehaviour
    {
        [SerializeField, Tooltip("Defaults to the scene's FlatBoardSpace.")] FlatBoardSpace boardSpace;
        [SerializeField] bool fitOnScreenChange = true;

        [Header("Margins in UI reference px (390x844, match 0.5, like ToolBar)")]
        [SerializeField] float leftPx = 72f;     // layer sidebar
        [SerializeField] float rightPx = 76f;    // ToolBar column (56) + edge margin + gap
        [SerializeField] float topPx = 24f;
        [SerializeField] float bottomPx = 24f;

        static readonly Vector2 ReferenceResolution = new Vector2(390f, 844f);
        const float MinDistance = 0.05f, MaxDistance = 20f;
        const int Iterations = 40;

        Camera _cam;
        int _lastW, _lastH;
        Rect _lastSafe;
        readonly Vector3[] _corners = new Vector3[4];

        void Awake() => _cam = GetComponent<Camera>();

        void Start() => Refit();

        void LateUpdate()
        {
            if (!fitOnScreenChange) return;
            if (Screen.width != _lastW || Screen.height != _lastH || Screen.safeArea != _lastSafe) Refit();
        }

        /// <summary>Re-aims the camera at the board centre along its current viewing direction and fits the distance.</summary>
        public void Refit()
        {
            _lastW = Screen.width;
            _lastH = Screen.height;
            _lastSafe = Screen.safeArea;
            if (_cam == null) _cam = GetComponent<Camera>();
            if (boardSpace == null) boardSpace = FindAnyObjectByType<FlatBoardSpace>();
            if (_cam == null || boardSpace == null) return;

            Transform board = boardSpace.BoardTransform;
            float side = (float)boardSpace.BoardSideMeters;
            Vector3 center = board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(0.5, 0.5), side));
            _corners[0] = board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(0, 0), side));
            _corners[1] = board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(1, 0), side));
            _corners[2] = board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(1, 1), side));
            _corners[3] = board.TransformPoint(BoardGeometry.BoardToLocal(new Vec2(0, 1), side));

            Vector3 dir = center - transform.position;
            if (dir.sqrMagnitude < 1e-10f) dir = transform.forward;
            dir.Normalize();
            Quaternion rot = Quaternion.LookRotation(dir, transform.up);
            transform.rotation = rot;

            Rect inner = InnerViewport();
            float lo = MinDistance, hi = MaxDistance;
            if (!Fits(center, dir, hi, inner)) { Place(center, dir, hi); return; }   // margins impossible: farthest
            for (int i = 0; i < Iterations; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Fits(center, dir, mid, inner)) hi = mid; else lo = mid;
            }
            Place(center, dir, hi);
        }

        /// <summary>Safe area minus the UI margins, in viewport units (0..1).</summary>
        Rect InnerViewport()
        {
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            float scale = Mathf.Sqrt(w / ReferenceResolution.x * (h / ReferenceResolution.y));   // CanvasScaler, match 0.5
            Rect safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) safe = new Rect(0, 0, w, h);
            float xMin = (safe.xMin + leftPx * scale) / w, xMax = (safe.xMax - rightPx * scale) / w;
            float yMin = (safe.yMin + bottomPx * scale) / h, yMax = (safe.yMax - topPx * scale) / h;
            if (xMax <= xMin) { xMin = safe.xMin / w; xMax = safe.xMax / w; }
            if (yMax <= yMin) { yMin = safe.yMin / h; yMax = safe.yMax / h; }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        bool Fits(Vector3 center, Vector3 dir, float distance, Rect inner)
        {
            Place(center, dir, distance);
            for (int i = 0; i < _corners.Length; i++)
            {
                Vector3 v = _cam.WorldToViewportPoint(_corners[i]);
                if (v.z <= _cam.nearClipPlane || !inner.Contains(new Vector2(v.x, v.y))) return false;
            }
            return true;
        }

        void Place(Vector3 center, Vector3 dir, float distance) => transform.position = center - dir * distance;
    }
}
