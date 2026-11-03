using System.Collections.Generic;
using GeoART.Contracts;
using UnityEngine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Draws the board and the session's model in the transform's local XZ plane (A-2/A-3/A-4).
    /// Four meshes, one draw call each, rebuilt only when dirty:
    ///   Base    - translucent sheet, outer frame (D-04), and the dashed outline of a corner-registered surface when it
    ///             is larger than the board (skipped when showBase is off), plus the dashed square or hex tile edges
    ///             from <see cref="TileGrid"/> (D-07; drawn whenever showGrid is on, so also in AR)
    ///   Shapes  - committed lines/circles/polygons on visible layers + user snap points  (BoardChanged)
    ///   Candidates - snap candidate dots, tinted and sized by SnapKind tier               (BoardChanged/StateChanged)
    ///   Overlay - tool preview ghosts (incl. the engine's snap hover ring), cursor         (PreviewChanged/StateChanged)
    /// Candidates have their own mesh so a drag (PreviewChanged every frame) never rebuilds hundreds of dots.
    /// Binds to the IEditorSession in ServiceHub at Start, or explicitly via <see cref="Bind"/>.
    /// The side can change at runtime (<see cref="SetSideMeters"/>, corner placement); stroke widths scale with it,
    /// clamped by <see cref="StrokeScaleFor"/>, so the design keeps the same look on screen at any board size.
    /// Strokes are anti-aliased by the shader (see <see cref="BoardMeshBuilder"/>); circles get a segment count from their
    /// radius (<see cref="BoardMeshBuilder.CircleSegmentsFor"/>) so large circles stay round.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardView : MonoBehaviour
    {
        [SerializeField, Tooltip("Default board side in metres (D-04). Corner placement overrides it at runtime.")]
        float sideMeters = BoardGeometry.DefaultSideMeters;

        [SerializeField, Tooltip("Material using the GeoART/BoardUnlit shader with vertex colours on.")]
        Material material;

        [SerializeField, Tooltip("Draw the board itself (sheet, frame, registered-surface outline). " +
                                 "Off in AR: the real surface is the board, only the design is drawn on it.")]
        bool showBase = true;
        [SerializeField, Tooltip("Draw the square/hex tile grid (Board.GridKind, Board.GridN), independent of showBase, " +
                                 "so the tiles are visible on the real surface in AR too.")]
        bool showGrid = true;
        [SerializeField] bool showSnapCandidates = true;
        [SerializeField] Color sheetColor = new Color(1f, 1f, 1f, 0.32f);
        [SerializeField] Color frameColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        [SerializeField] Color dividerColor = new Color(0.2f, 0.2f, 0.25f, 0.85f);
        [SerializeField] Color snapPointColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        // Snap candidate dots: a dark fill inside a light outline, so every dot reads on the light sheet, on dark strokes
        // and on a dark real surface in AR. Grid dots are smaller and translucent; shape dots are the largest and opaque.
        [SerializeField, Tooltip("Fill of grid dots (tile corners; edge midpoints and centres use it at lower alpha).")]
        Color gridDotFill = new Color(0.1f, 0.1f, 0.13f, 0.85f);
        [SerializeField, Tooltip("Outline of grid dots.")]
        Color gridDotOutline = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField, Tooltip("Fill of construction dots (centres, circle divisions, user points).")]
        Color guideDotFill = new Color(0.1f, 0.27f, 0.68f, 1f);
        [SerializeField, Tooltip("Fill of shape dots (intersections, endpoints): the strongest.")]
        Color shapeDotFill = new Color(0.02f, 0.18f, 0.62f, 1f);
        [SerializeField, Tooltip("Outline of construction and shape dots.")]
        Color dotOutline = new Color(1f, 1f, 1f, 1f);
        [SerializeField] Color previewColor = new Color(1f, 0.55f, 0.1f, 0.9f);
        [SerializeField] Color cursorColor = new Color(1f, 0.42f, 0f, 1f);
        [SerializeField] Color surfaceOutlineColor = new Color(0.1f, 0.8f, 1f, 0.9f);

        // Heights above the board plane (metres) so layers never z-fight with each other.
        const float ShapesLift = 0.0008f;
        const float CandidatesLift = 0.0012f;
        const float OverlayLift = 0.0016f;

        // Candidate dot fill radii and outline widths in metres at the 0.40 m board (times StrokeScale).
        // Before snapping: one size, 2.5 mm, no outline.
        const float MinorGridDotRadius = 0.0019f;   // CellEdgeMid, CellCenter
        const float GridDotRadius = 0.0025f;        // CellCorner (grid intersections)
        const float GuideDotRadius = 0.0030f;       // Center, CircleDivision, UserPoint
        const float ShapeDotRadius = 0.0036f;       // Intersection, Endpoint
        const float GridOutlineWidth = 0.0009f;     // light ring around grid dots
        const float DotOutlineWidth = 0.0011f;      // light ring around construction and shape dots
        const float MinorGridAlpha = 0.6f;          // edge midpoints / centres: grid colours at this alpha

        IEditorSession _session;
        bool _subscribed;
        Part _base, _shapes, _candidates, _overlay;
        bool _baseDirty = true, _shapesDirty = true, _candidatesDirty = true, _overlayDirty = true;
        int _lastGridN = -1;
        GridKind _lastGridKind = (GridKind)(-1);
        readonly List<(Vec2 A, Vec2 B)> _gridEdges = new List<(Vec2 A, Vec2 B)>(64);
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
            _candidates = new Part(transform, "Candidates", material, 3002);
            _overlay = new Part(transform, "Overlay", material, 3003);
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
            _candidates?.Dispose();
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

        public void MarkAllDirty() => _baseDirty = _shapesDirty = _candidatesDirty = _overlayDirty = true;

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
            _candidatesDirty = true;   // candidates depend on geometry, layer visibility and grid
            _overlayDirty = true;
            if ((kind & (BoardChangeKind.Grid | BoardChangeKind.Reset)) != 0) _baseDirty = true;
        }

        void OnPreviewChanged() => _overlayDirty = true;

        void OnStateChanged()
        {
            _candidatesDirty = true;   // Snap toggled on/off
            _overlayDirty = true;
        }

        void LateUpdate()
        {
            Board board = _session?.Board;
            int gridN = TileGrid.ClampN(board?.GridN ?? 2);
            GridKind gridKind = board?.GridKind ?? GridKind.Square;
            if (gridN != _lastGridN || gridKind != _lastGridKind) _baseDirty = true;

            if (_baseDirty)
            {
                RebuildBase(gridKind, gridN);
                _lastGridN = gridN;
                _lastGridKind = gridKind;
                _baseDirty = false;
            }
            if (_shapesDirty) { RebuildShapes(board); _shapesDirty = false; }
            if (_candidatesDirty) { RebuildCandidates(); _candidatesDirty = false; }
            if (_overlayDirty) { RebuildOverlay(); _overlayDirty = false; }
        }

        // ---------- widths and patterns (scale with the board so a resized board keeps its look) ----------

        float StrokeScale => StrokeScaleFor(sideMeters);

        /// <summary>Stroke width in metres at the 0.40 m board: Thin 1.0 mm, Medium 1.7 mm, Thick 2.8 mm (times StrokeScale).
        /// The shader keeps any stroke at least ~1.5 px wide on screen (fading it instead), so thin lines never break up.</summary>
        float WidthOf(LineWidth w)
        {
            switch (w)
            {
                case LineWidth.Thin: return 0.0010f * StrokeScale;
                case LineWidth.Thick: return 0.0028f * StrokeScale;
                default: return 0.0017f * StrokeScale;
            }
        }

        // Base pattern (grid dividers, surface outline) and the Dotted line style.
        float Dash => 0.008f * StrokeScale;
        float Gap => 0.006f * StrokeScale;
        float StrokeDash => 0.005f * StrokeScale;
        float StrokeGap => 0.0035f * StrokeScale;

        Vector3 P(Vec2 uv, float lift) => BoardGeometry.BoardToLocal(uv, sideMeters, lift);

        static Color32 ToColor32(RgbaColor c) =>
            c.A == 0 ? ToColor32(Palette.Black)   // default(StrokeStyle) guard: never draw invisible ink
                     : new Color32(c.R, c.G, c.B, c.A);

        static Color32 WithOpacity(Color32 c, float opacity)
        {
            c.a = (byte)Mathf.RoundToInt(c.a * opacity);
            return c;
        }

        // ---------- rebuilds ----------

        void RebuildBase(GridKind gridKind, int gridN)
        {
            var mb = _base.Builder;
            mb.Clear();
            if (showBase) AddSheetFrameAndSurface(mb);
            if (showGrid)
            {
                // Each edge once, so shared hex edges do not double up. With the base on, the frame already draws the
                // board boundary; in AR (base off) the square grid's outer edges outline the board instead.
                TileGrid.UniqueEdges(gridKind, gridN, _gridEdges, skipBoardBoundary: showBase);
                foreach (var (a, b) in _gridEdges)
                {
                    float phase = 0f;
                    mb.AddDashedSegment(P(a, 0f), P(b, 0f), 0.0011f * StrokeScale, dividerColor, Dash, Gap, ref phase);
                }
            }
            _base.Apply();
        }

        void AddSheetFrameAndSurface(BoardMeshBuilder mb)
        {
            float h = sideMeters * 0.5f;
            Vector3 bl = new Vector3(-h, 0f, -h), br = new Vector3(h, 0f, -h);
            Vector3 tr = new Vector3(h, 0f, h), tl = new Vector3(-h, 0f, h);

            mb.AddQuad(bl, tl, tr, br, sheetColor);

            _poly.Clear();
            _poly.Add(bl); _poly.Add(br); _poly.Add(tr); _poly.Add(tl);
            mb.AddPolyline(_poly, true, 0.002f * StrokeScale, frameColor);

            // Registered surface (D1: the board is centred in it, so the outline is symmetric).
            if (!float.IsNaN(_surfaceW) && (Mathf.Abs(_surfaceW - sideMeters) > 0.002f || Mathf.Abs(_surfaceH - sideMeters) > 0.002f))
            {
                float sw = _surfaceW * 0.5f, sh = _surfaceH * 0.5f;
                _poly.Clear();
                _poly.Add(new Vector3(-sw, 0f, -sh)); _poly.Add(new Vector3(sw, 0f, -sh));
                _poly.Add(new Vector3(sw, 0f, sh)); _poly.Add(new Vector3(-sw, 0f, sh));
                mb.AddPolyline(_poly, true, 0.0012f * StrokeScale, surfaceOutlineColor, true, Dash, Gap);
            }
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
                    float opacity = Mathf.Clamp01((float)layer.Opacity);
                    if (opacity <= 0f) continue;
                    foreach (var shape in layer.Shapes)
                        if (shape != null) AddShape(mb, shape, WithOpacity(ToColor32(shape.Style.Color), opacity), ShapesLift);
                    Color32 snapColor = WithOpacity(snapPointColor, opacity);
                    foreach (var sp in layer.SnapPoints)
                        if (sp != null) mb.AddDisc(P(sp.Position, ShapesLift), 0.0035f * StrokeScale, snapColor);
                }
            }
            _shapes.Apply();
        }

        /// <summary>Candidate tiers: 0 minor grid, 1 grid intersections, 2 construction points, 3 shape points.</summary>
        static int TierOf(SnapKind kind)
        {
            switch (kind)
            {
                case SnapKind.Intersection:
                case SnapKind.Endpoint: return 3;
                case SnapKind.Center:
                case SnapKind.CircleDivision:
                case SnapKind.UserPoint: return 2;
                case SnapKind.CellCorner: return 1;
                default: return 0;   // CellEdgeMid, CellCenter, None
            }
        }

        /// <summary>
        /// Dots for every snap candidate, low tiers first so strong dots sit on top. Index loops over the
        /// IReadOnlyList (no boxed enumerator). Runs on BoardChanged/StateChanged only, not per pointer Move.
        /// </summary>
        void RebuildCandidates()
        {
            var mb = _candidates.Builder;
            mb.Clear();
            IReadOnlyList<SnapCandidate> list = _session?.SnapCandidates;
            if (showSnapCandidates && list != null && _session.SnapEnabled)
            {
                float s = StrokeScale;
                for (int tier = 0; tier <= 3; tier++)
                    for (int i = 0; i < list.Count; i++)
                    {
                        SnapCandidate c = list[i];
                        if (TierOf(c.Kind) != tier || !c.Position.IsFinite) continue;
                        Vector3 p = P(c.Position, CandidatesLift);
                        switch (tier)
                        {
                            case 0:
                                AddDot(mb, p, MinorGridDotRadius * s, GridOutlineWidth * s,
                                    Faded(gridDotFill, MinorGridAlpha), Faded(gridDotOutline, MinorGridAlpha), 10);
                                break;
                            case 1:
                                AddDot(mb, p, GridDotRadius * s, GridOutlineWidth * s, gridDotFill, gridDotOutline, 12);
                                break;
                            case 2:
                                AddDot(mb, p, GuideDotRadius * s, DotOutlineWidth * s, guideDotFill, dotOutline, 14);
                                break;
                            default:
                                AddDot(mb, p, ShapeDotRadius * s, DotOutlineWidth * s, shapeDotFill, dotOutline, 16);
                                break;
                        }
                    }
            }
            _candidates.Apply();
        }

        /// <summary>A dot: light outline disc, then the dark fill on top.</summary>
        static void AddDot(BoardMeshBuilder mb, Vector3 p, float radius, float outline, Color fill, Color ring, int segments)
        {
            mb.AddDisc(p, radius + outline, ring, segments);
            mb.AddDisc(p, radius, fill, segments);
        }

        static Color Faded(Color c, float alpha) => new Color(c.r, c.g, c.b, c.a * alpha);

        void RebuildOverlay()
        {
            var mb = _overlay.Builder;
            mb.Clear();
            if (_session != null)
            {
                IReadOnlyList<Shape> preview = _session.Preview;   // index loop: no enumerator allocation per frame
                if (preview != null)
                    for (int i = 0; i < preview.Count; i++)
                        if (preview[i] != null) AddShape(mb, preview[i], previewColor, OverlayLift);

                if (_session.Cursor is Vec2 cursor && cursor.IsFinite)
                {
                    Vector3 c = P(cursor, OverlayLift);
                    mb.AddCircle(c, 0.009f * StrokeScale, 0.0012f * StrokeScale, cursorColor);
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
                        mb.AddDashedSegment(P(line.A, lift), P(line.B, lift), width, color, StrokeDash, StrokeGap, ref phase);
                    }
                    else mb.AddSegment(P(line.A, lift), P(line.B, lift), width, color);
                    break;

                case CircleShape circle:
                    if (circle.Radius > 0 && circle.Center.IsFinite)
                        mb.AddCircle(P(circle.Center, lift), (float)(circle.Radius * sideMeters), width, color,
                            dashed, StrokeDash, StrokeGap);
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
                    mb.AddPolyline(_poly, true, width, color, dashed, StrokeDash, StrokeGap);
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
