using System.Collections.Generic;
using GeoART.Contracts;
using UnityEngine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Draws the board and the session's model in the transform's local XZ plane (A-2/A-3/A-4).
    /// Three meshes, one draw call each, rebuilt only when dirty:
    ///   Base    - translucent sheet, outer frame (D-04), dashed cell dividers for GridN (D-07), and the dashed
    ///             outline of a corner-registered surface when it is larger than the board (empty when showBase is off)
    ///   Shapes  - committed lines/circles/polygons on visible layers + user snap points  (BoardChanged)
    ///   Overlay - snap candidates, tool preview ghosts, cursor                            (PreviewChanged/StateChanged)
    /// Binds to the IEditorSession in ServiceHub at Start, or explicitly via <see cref="Bind"/>.
    /// The side can change at runtime (<see cref="SetSideMeters"/>, corner placement); stroke widths scale with it,
    /// clamped by <see cref="StrokeScaleFor"/>, so the design keeps the same look on screen at any board size.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardView : MonoBehaviour
    {
        [SerializeField, Tooltip("Default board side in metres (D-04). Corner placement overrides it at runtime.")]
        float sideMeters = BoardGeometry.DefaultSideMeters;

        [SerializeField, Tooltip("Material using the GeoART/BoardUnlit shader with vertex colours on.")]
        Material material;

        [SerializeField, Tooltip("Draw the board itself (sheet, frame, grid dividers, registered-surface outline). " +
                                 "Off in AR: the real surface is the board, only the design is drawn on it.")]
        bool showBase = true;
        [SerializeField] bool showSnapCandidates = true;
        [SerializeField] Color sheetColor = new Color(1f, 1f, 1f, 0.32f);
        [SerializeField] Color frameColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        [SerializeField] Color dividerColor = new Color(0.2f, 0.2f, 0.25f, 0.85f);
        [SerializeField] Color snapPointColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        [SerializeField] Color candidateColor = new Color(0.25f, 0.25f, 0.3f, 0.5f);
        [SerializeField] Color previewColor = new Color(1f, 0.55f, 0.1f, 0.9f);
        [SerializeField] Color cursorColor = new Color(1f, 0.42f, 0f, 1f);
        [SerializeField] Color surfaceOutlineColor = new Color(0.1f, 0.8f, 1f, 0.9f);

        // Heights above the board plane (metres) so layers never z-fight with each other.
        const float ShapesLift = 0.0008f;
        const float OverlayLift = 0.0016f;
        const int CircleSegments = 96;

        IEditorSession _session;
        bool _subscribed;
        Part _base, _shapes, _overlay;
        bool _baseDirty = true, _shapesDirty = true, _overlayDirty = true;
        int _lastGridN = -1;
        float _surfaceW = float.NaN, _surfaceH = float.NaN;
        readonly List<Vector3> _poly = new List<Vector3>(16);

        public float SideMeters => sideMeters;
        public IEditorSession Session => _session;

        void Awake()
        {
            if (material == null)
            {
                var shader = Shader.Find("GeoART/BoardUnlit");
                if (shader != null) material = new Material(shader);
                else Debug.LogError("[BoardView] No material assigned and GeoART/BoardUnlit shader not found.", this);
            }
            _base = new Part(transform, "Base", material, 3000);
            _shapes = new Part(transform, "Shapes", material, 3001);
            _overlay = new Part(transform, "Overlay", material, 3002);
        }

        void Start()
        {
            if (_session == null && ServiceHub.TryResolve<IEditorSession>(out var session))
                Bind(session);
        }

        void OnEnable()
        {
            Subscribe();
            MarkAllDirty();
        }

        void OnDisable() => Unsubscribe();

        void OnDestroy()
        {
            _base?.Dispose();
            _shapes?.Dispose();
            _overlay?.Dispose();
        }

        /// <summary>Attach to a session (real or stub). Pass null to show an empty board.</summary>
        public void Bind(IEditorSession session)
        {
            Unsubscribe();
            _session = session;
            if (isActiveAndEnabled) Subscribe();
            MarkAllDirty();
        }

        public void MarkAllDirty() => _baseDirty = _shapesDirty = _overlayDirty = true;

        /// <summary>Runtime board side (quick place 0.40 m, or the corner-fitted square). Clamped to [0.05, 3] m.</summary>
        public void SetSideMeters(float side)
        {
            sideMeters = Mathf.Clamp(side, 0.05f, 3f);
            MarkAllDirty();
        }

        /// <summary>
        /// Size of the registered surface the board is centred in. NaN or &lt;= 0 hides the outline. The outline is drawn
        /// only when the surface differs from the board square by more than 2 mm.
        /// </summary>
        public void SetSurfaceSize(float widthMeters, float heightMeters)
        {
            bool valid = widthMeters > 0f && heightMeters > 0f;   // false for NaN
            _surfaceW = valid ? widthMeters : float.NaN;
            _surfaceH = valid ? heightMeters : float.NaN;
            _baseDirty = true;
        }

        /// <summary>Stroke scale for a board side: proportional to the 0.40 m default, clamped to [0.4, 2].</summary>
        public static float StrokeScaleFor(float side) => Mathf.Clamp(side / BoardGeometry.DefaultSideMeters, 0.4f, 2f);

        void Subscribe()
        {
            if (_subscribed || _session == null) return;
            _session.BoardChanged += OnBoardChanged;
            _session.PreviewChanged += OnPreviewChanged;
            _session.StateChanged += OnStateChanged;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || _session == null) return;
            _session.BoardChanged -= OnBoardChanged;
            _session.PreviewChanged -= OnPreviewChanged;
            _session.StateChanged -= OnStateChanged;
            _subscribed = false;
        }

        void OnBoardChanged(BoardChangeKind kind)
        {
            _shapesDirty = true;
            _overlayDirty = true;   // candidates depend on geometry and grid
            if ((kind & (BoardChangeKind.Grid | BoardChangeKind.Reset)) != 0) _baseDirty = true;
        }

        void OnPreviewChanged() => _overlayDirty = true;
        void OnStateChanged() => _overlayDirty = true;

        void LateUpdate()
        {
            Board board = _session?.Board;
            int gridN = Mathf.Clamp(board?.GridN ?? 2, Board.MinGridN, Board.MaxGridN);
            if (gridN != _lastGridN) _baseDirty = true;

            if (_baseDirty) { RebuildBase(gridN); _lastGridN = gridN; _baseDirty = false; }
            if (_shapesDirty) { RebuildShapes(board); _shapesDirty = false; }
            if (_overlayDirty) { RebuildOverlay(); _overlayDirty = false; }
        }

        // ---------- widths and patterns (scale with the board so a resized board keeps its look) ----------

        float StrokeScale => StrokeScaleFor(sideMeters);

        float WidthOf(LineWidth w)
        {
            switch (w)
            {
                case LineWidth.Thin: return 0.0018f * StrokeScale;
                case LineWidth.Thick: return 0.0055f * StrokeScale;
                default: return 0.0032f * StrokeScale;
            }
        }

        float Dash => 0.008f * StrokeScale;
        float Gap => 0.006f * StrokeScale;

        Vector3 P(Vec2 uv, float lift) => BoardGeometry.BoardToLocal(uv, sideMeters, lift);

        static Color32 ToColor32(RgbaColor c) =>
            c.A == 0 ? ToColor32(Palette.Black)   // default(StrokeStyle) guard: never draw invisible ink
                     : new Color32(c.R, c.G, c.B, c.A);

        // ---------- rebuilds ----------

        void RebuildBase(int gridN)
        {
            var mb = _base.Builder;
            mb.Clear();
            if (!showBase)
            {
                _base.Apply();
                return;
            }
            float h = sideMeters * 0.5f;
            Vector3 bl = new Vector3(-h, 0f, -h), br = new Vector3(h, 0f, -h);
            Vector3 tr = new Vector3(h, 0f, h), tl = new Vector3(-h, 0f, h);

            mb.AddQuad(bl, tl, tr, br, sheetColor);

            _poly.Clear();
            _poly.Add(bl); _poly.Add(br); _poly.Add(tr); _poly.Add(tl);
            mb.AddPolyline(_poly, true, 0.003f * StrokeScale, frameColor);

            for (int i = 1; i < gridN; i++)
            {
                double t = (double)i / gridN;
                float phase = 0f;
                mb.AddDashedSegment(P(new Vec2(t, 0), 0f), P(new Vec2(t, 1), 0f), 0.0015f * StrokeScale, dividerColor, Dash, Gap, ref phase);
                phase = 0f;
                mb.AddDashedSegment(P(new Vec2(0, t), 0f), P(new Vec2(1, t), 0f), 0.0015f * StrokeScale, dividerColor, Dash, Gap, ref phase);
            }

            // Registered surface (D1: the board is centred in it, so the outline is symmetric).
            if (!float.IsNaN(_surfaceW) && (Mathf.Abs(_surfaceW - sideMeters) > 0.002f || Mathf.Abs(_surfaceH - sideMeters) > 0.002f))
            {
                float sw = _surfaceW * 0.5f, sh = _surfaceH * 0.5f;
                _poly.Clear();
                _poly.Add(new Vector3(-sw, 0f, -sh)); _poly.Add(new Vector3(sw, 0f, -sh));
                _poly.Add(new Vector3(sw, 0f, sh)); _poly.Add(new Vector3(-sw, 0f, sh));
                mb.AddPolyline(_poly, true, 0.0012f * StrokeScale, surfaceOutlineColor, true, Dash, Gap);
            }
            _base.Apply();
        }

        void RebuildShapes(Board board)
        {
            var mb = _shapes.Builder;
            mb.Clear();
            if (board != null)
            {
                foreach (var layer in board.Layers)
                {
                    if (layer == null || !layer.Visible) continue;
                    foreach (var shape in layer.Shapes)
                        if (shape != null) AddShape(mb, shape, ToColor32(shape.Style.Color), ShapesLift);
                    foreach (var sp in layer.SnapPoints)
                        if (sp != null) mb.AddDisc(P(sp.Position, ShapesLift), 0.0035f * StrokeScale, snapPointColor);
                }
            }
            _shapes.Apply();
        }

        void RebuildOverlay()
        {
            var mb = _overlay.Builder;
            mb.Clear();
            if (_session != null)
            {
                if (showSnapCandidates && _session.SnapEnabled && _session.SnapCandidates != null)
                    foreach (var c in _session.SnapCandidates)
                        mb.AddDisc(P(c.Position, OverlayLift), 0.0025f * StrokeScale, candidateColor, 10);

                if (_session.Preview != null)
                    foreach (var ghost in _session.Preview)
                        if (ghost != null) AddShape(mb, ghost, previewColor, OverlayLift);

                if (_session.Cursor is Vec2 cursor && cursor.IsFinite)
                {
                    Vector3 c = P(cursor, OverlayLift);
                    mb.AddCircle(c, 0.009f * StrokeScale, 24, 0.0018f * StrokeScale, cursorColor);
                    mb.AddDisc(c, 0.0022f * StrokeScale, cursorColor, 10);
                }
            }
            _overlay.Apply();
        }

        void AddShape(BoardMeshBuilder mb, Shape shape, Color32 color, float lift)
        {
            float width = WidthOf(shape.Style.Width);
            bool dashed = shape.Style.Dash == LineStyle.Dotted;
            switch (shape)
            {
                case LineShape line:
                    if (dashed)
                    {
                        float phase = 0f;
                        mb.AddDashedSegment(P(line.A, lift), P(line.B, lift), width, color, Dash, Gap, ref phase);
                    }
                    else mb.AddSegment(P(line.A, lift), P(line.B, lift), width, color);
                    break;

                case CircleShape circle:
                    if (circle.Radius > 0 && circle.Center.IsFinite)
                        mb.AddCircle(P(circle.Center, lift), (float)(circle.Radius * sideMeters), CircleSegments,
                            width, color, dashed, Dash, Gap);
                    break;

                case PolygonShape poly:
                    if (poly.Sides < 3 || poly.Radius <= 0) break;
                    _poly.Clear();
                    for (int k = 0; k < poly.Sides; k++)
                    {
                        double a = poly.RotationRad + 2.0 * System.Math.PI * k / poly.Sides;
                        var v = new Vec2(poly.Center.X + poly.Radius * System.Math.Cos(a),
                                         poly.Center.Y + poly.Radius * System.Math.Sin(a));
                        _poly.Add(P(v, lift));
                    }
                    mb.AddPolyline(_poly, true, width, color, dashed, Dash, Gap);
                    break;
            }
        }

        /// <summary>A child object with its own mesh and material instance (render queue fixes draw order).</summary>
        sealed class Part
        {
            public readonly BoardMeshBuilder Builder = new BoardMeshBuilder();
            readonly Mesh _mesh;
            readonly Material _material;
            readonly GameObject _go;

            public Part(Transform parent, string name, Material source, int renderQueue)
            {
                _go = new GameObject(name);
                _go.transform.SetParent(parent, false);
                _go.layer = parent.gameObject.layer;
                _mesh = new Mesh { name = "Board" + name };
                _mesh.MarkDynamic();
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var mr = _go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (source != null)
                {
                    _material = new Material(source) { renderQueue = renderQueue };
                    mr.sharedMaterial = _material;
                }
            }

            public void Apply() => Builder.Apply(_mesh);

            public void Dispose()
            {
                if (_mesh != null) Object.Destroy(_mesh);
                if (_material != null) Object.Destroy(_material);
            }
        }
    }
}
