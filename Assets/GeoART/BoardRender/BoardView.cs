using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;
using UnityEngine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Draws the board and the session's model in the transform's local XZ plane (A-2/A-3/A-4).
    /// Six meshes, one draw call each, rebuilt only when dirty (render queues 3000..3005, in this order):
    ///   Base    - translucent sheet, outer frame (D-04), and the dashed outline of a corner-registered surface when it
    ///             is larger than the board (skipped when showBase is off), plus the dashed square or hex tile edges.
    ///             Guide lines (grid, frame, user snap points) are drawn in BoardAppearance.Guides: Black (the original
    ///             dark colours, default) or White (light, for dark surfaces). Nothing is filled in AR
    ///             from <see cref="TileGrid"/> (D-07; drawn whenever showGrid is on, so also in AR)
    ///   Shapes  - committed lines/circles/polygons on visible layers + user snap points  (BoardChanged)
    ///             A layer with bands on (Layer.Bands, docs/BANDS.md) draws its band outlines first (BandCache: the
    ///             topology is rebuilt only on shape changes, the outline only when the band settings change), then its
    ///             lines and polygons unless the structure is hidden, then circles and snap points as usual. Like every
    ///             part, it is rebuilt at most once per frame (LateUpdate), however many changes arrive.
    ///   Selection - each selected shape on a visible layer: a wide translucent halo (solid even for dotted shapes), then
    ///             the shape's own stroke again on top, so it reads above its neighbours  (BoardChanged/StateChanged)
    ///   Candidates - snap candidate dots, tinted and sized by SnapKind tier               (BoardChanged/StateChanged)
    ///   Overlay - tool preview ghosts (incl. the engine's snap hover ring), cursor         (PreviewChanged/StateChanged)
    ///             The snapped dot is a filled orange disc under the hover ring, and on every new acquisition
    ///             (EditorSession.SnapAcquired) the ring "pops" from 1.6x to 1x over <see cref="RingPopSeconds"/>; the
    ///             overlay is rebuilt each frame only while that runs.
    ///   SelectionFrame - while something is selected: a dashed cyan box around the selection's union bounds and the
    ///             pivot marker (ring and "+") at its centre, the point Move/Scale work about. Bounds come from the
    ///             engine's <see cref="GeoART.Engine.SelectionGeometry"/>, so the drawn pivot is the engine's pivot.
    ///             It stays on the originals during a transform drag (the ghosts move)    (with Selection)
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
        [SerializeField, Tooltip("Grid lines when BoardAppearance.Guides is White (Black uses dividerColor).")]
        Color whiteGridColor = new Color(0.93f, 0.93f, 0.95f, 0.85f);
        [SerializeField, Tooltip("Frame (flat scene) when Guides is White (Black uses frameColor).")]
        Color whiteFrameColor = new Color(0.95f, 0.95f, 0.97f, 1f);
        [SerializeField, Tooltip("User snap points when Guides is White (Black uses snapPointColor).")]
        Color whiteSnapPointColor = new Color(0.95f, 0.95f, 0.97f, 1f);
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
        [SerializeField, Tooltip("Halo behind selected shapes.")]
        Color selectionColor = new Color(0.1f, 0.75f, 1f, 0.55f);
        [SerializeField, Tooltip("Dashed box around the selection's bounds.")]
        Color selectionFrameColor = new Color(0.1f, 0.75f, 1f, 0.9f);
        [SerializeField, Tooltip("Pivot marker (ring and plus) at the centre of the selection's bounds.")]
        Color pivotColor = new Color(0.1f, 0.75f, 1f, 1f);

        // Heights above the board plane (metres) so layers never z-fight with each other.
        const float ShapesLift = 0.0008f;
        const float SelectionLift = 0.0010f;
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
        const float SelectionHaloMeters = 0.0018f;  // halo reaches this far beyond each side of the stroke (times StrokeScale)

        IEditorSession _session;
        EditorSession _engine;   // the concrete session, when it is one (SnapAcquired, HoverRing)
        bool _subscribed;

        /// <summary>Duration of the hover ring pop on a new snap acquisition.</summary>
        public const float RingPopSeconds = 0.12f;
        /// <summary>Starting scale of the popping hover ring.</summary>
        public const float RingPopScale = 1.6f;
        /// <summary>Seconds, monotonic; EditMode tests swap in a fake clock.</summary>
        public System.Func<float> Clock = () => Time.unscaledTime;
        float _popStart;
        bool _popping;
        Part _base, _shapes, _selection, _candidates, _overlay, _selectionFrame;
        bool _baseDirty = true, _shapesDirty = true, _selectionDirty = true, _candidatesDirty = true, _overlayDirty = true;
        int _lastGridN = -1;
        GridKind _lastGridKind = (GridKind)(-1);
        readonly List<(Vec2 A, Vec2 B)> _gridEdges = new List<(Vec2 A, Vec2 B)>(64);
        float _surfaceW = float.NaN, _surfaceH = float.NaN;
        readonly List<Vector3> _poly = new List<Vector3>(16);
        readonly BandCache _bandCache = new BandCache();

        /// <summary>The band geometry cache (counters for tests and the width-slider cost estimate).</summary>
        public BandCache Bands => _bandCache;

        public float SideMeters => sideMeters;

        static bool WhiteGuides => BoardAppearance.Guides == BoardGuideColor.White;
        Color GridColor => WhiteGuides ? whiteGridColor : dividerColor;
        Color FrameColor => WhiteGuides ? whiteFrameColor : frameColor;
        Color SnapPointColor => WhiteGuides ? whiteSnapPointColor : snapPointColor;
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
            _selection = new Part(transform, "Selection", material, 3002);
            _candidates = new Part(transform, "Candidates", material, 3003);
            _overlay = new Part(transform, "Overlay", material, 3004);
            _selectionFrame = new Part(transform, "SelectionFrame", material, 3005);
        }

        void Start()
        {
            if (_session == null && ServiceHub.TryResolve<IEditorSession>(out var session))
                Bind(session);
        }

        void OnEnable()
        {
            Subscribe();
            BoardAppearance.Changed += OnAppearanceChanged;
            MarkAllDirty();
        }

        void OnDisable()
        {
            Unsubscribe();
            BoardAppearance.Changed -= OnAppearanceChanged;
        }

        void OnAppearanceChanged() => _baseDirty = _shapesDirty = true;   // grid, frame, user snap points

        void OnDestroy()
        {
            _base?.Dispose();
            _shapes?.Dispose();
            _selection?.Dispose();
            _candidates?.Dispose();
            _overlay?.Dispose();
            _selectionFrame?.Dispose();
        }

        /// <summary>Attach to a session (real or stub). Pass null to show an empty board.</summary>
        public void Bind(IEditorSession session)
        {
            Unsubscribe();
            _session = session;
            _bandCache.Clear();
            if (isActiveAndEnabled) Subscribe();
            MarkAllDirty();
        }

        public void MarkAllDirty() =>
            _baseDirty = _shapesDirty = _selectionDirty = _candidatesDirty = _overlayDirty = true;

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
            _engine = _session as EditorSession;
            if (_engine != null) _engine.SnapAcquired += OnSnapAcquired;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || _session == null) return;
            _session.BoardChanged -= OnBoardChanged;
            _session.PreviewChanged -= OnPreviewChanged;
            _session.StateChanged -= OnStateChanged;
            if (_engine != null) _engine.SnapAcquired -= OnSnapAcquired;
            _engine = null;
            _popping = false;
            _subscribed = false;
        }

        void OnSnapAcquired(SnapCandidate _)
        {
            _popStart = Clock();
            _popping = true;
            _overlayDirty = true;
        }

        /// <summary>Hover ring scale now: 1.6 easing out to 1 over the pop, 1 when idle.</summary>
        float RingScale()
        {
            if (!_popping) return 1f;
            float t = Mathf.Clamp01((Clock() - _popStart) / RingPopSeconds);
            float ease = 1f - (1f - t) * (1f - t);
            return Mathf.Lerp(RingPopScale, 1f, ease);
        }

        void OnBoardChanged(BoardChangeKind kind)
        {
            _shapesDirty = true;
            _selectionDirty = true;    // selected shapes moved, were hidden or deleted
            _candidatesDirty = true;   // candidates depend on geometry, layer visibility and grid
            _overlayDirty = true;
            if ((kind & (BoardChangeKind.Grid | BoardChangeKind.Reset)) != 0) _baseDirty = true;
            if ((kind & (BoardChangeKind.Shapes | BoardChangeKind.Reset | BoardChangeKind.History)) != 0) _bandCache.MarkTopologyDirty();
        }

        void OnPreviewChanged() => _overlayDirty = true;

        void OnStateChanged()
        {
            _selectionDirty = true;    // the selection changed (it raises StateChanged, never BoardChanged)
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
            if (_selectionDirty) { RebuildSelection(board); _selectionDirty = false; }
            if (_candidatesDirty) { RebuildCandidates(); _candidatesDirty = false; }
            if (_popping)
            {
                _overlayDirty = true;   // animate: rebuild every frame while the pop runs, then once more at exactly 1x
                if (Clock() - _popStart >= RingPopSeconds) _popping = false;
            }
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
                Color grid = GridColor;
                foreach (var (a, b) in _gridEdges)
                {
                    float phase = 0f;
                    mb.AddDashedSegment(P(a, 0f), P(b, 0f), 0.0011f * StrokeScale, grid, Dash, Gap, ref phase);
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
            mb.AddPolyline(_poly, true, 0.002f * StrokeScale, FrameColor);

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
            _bandCache.BeginFrame();
            if (board != null)
            {
                foreach (var layer in board.Layers)
                {
                    if (layer == null || !layer.Visible) continue;
                    float opacity = Mathf.Clamp01((float)layer.Opacity);
                    if (opacity <= 0f) continue;
                    LayerBands bands = layer.Bands.Sanitized();
                    bool hideStructure = false;
                    BandOutline outline = null;
                    if (bands.On)
                    {
                        outline = AddBands(mb, layer, bands, opacity);
                        hideStructure = bands.HideStructure;
                    }
                    List<StructureCut> cuts = outline?.StructureCuts;
                    int cut = 0;
                    for (int i = 0; i < layer.Shapes.Count; i++)
                    {
                        Shape shape = layer.Shapes[i];
                        if (shape == null || (hideStructure && !(shape is CircleShape))) continue;   // circles are never banded
                        Color32 color = WithOpacity(ToColor32(shape.Style.Color), opacity);
                        // Interlace with the structure shown: an under band's structure line is cut where the over band covers it.
                        while (cuts != null && cut < cuts.Count && cuts[cut].Shape < i) cut++;
                        int end = cut;
                        while (cuts != null && end < cuts.Count && cuts[end].Shape == i) end++;
                        if (end > cut && (shape is LineShape || shape is PolygonShape)) AddCutShape(mb, shape, cuts, cut, end, color);
                        else AddShape(mb, shape, color, ShapesLift);
                        cut = end;
                    }
                    Color32 snapColor = WithOpacity(SnapPointColor, opacity);
                    foreach (var sp in layer.SnapPoints)
                        if (sp != null) mb.AddDisc(P(sp.Position, ShapesLift), 0.0035f * StrokeScale, snapColor);
                }
            }
            _bandCache.EndFrame();
            _shapes.Apply();
        }

        /// <summary>
        /// Band options for this board (shared with the layer popup: BandOutlineOptions.ForBoard): the inner-line inset
        /// floor (2 mm) and minimum half width scale like the strokes. Drawing skips the global crossing count.
        /// </summary>
        BandOutlineOptions BandOptions(in LayerBands bands)
        {
            BandOutlineOptions o = BandOutlineOptions.ForBoard(bands, sideMeters);
            o.SkipSelfIntersections = true;
            return o;
        }

        BandOutline AddBands(BoardMeshBuilder mb, Layer layer, in LayerBands bands, float opacity)
        {
            BandOutline o = _bandCache.Get(layer, BandOptions(bands));
            Color32 color = WithOpacity(ToColor32(bands.OutlineColor), opacity);
            float width = WidthOf(bands.OutlineWidth);
            AddRuns(mb, o, o.Runs, width, color);
            AddRuns(mb, o, o.InnerRuns, width, color);
            AddBridges(mb, o.Bridges, width, color);
            AddBridges(mb, o.InnerBridges, width, color);
            return o;
        }

        /// <summary>Flat local direction of a board direction at a board point (zero stays zero).</summary>
        Vector3 LocalDir(Vec2 at, Vec2 dir)
        {
            if (dir.X == 0 && dir.Y == 0) return Vector3.zero;
            Vector3 d = P(new Vec2(at.X + dir.X * 1e-3, at.Y + dir.Y * 1e-3), 0f) - P(at, 0f);
            d.y = 0f;
            return d;
        }

        static Vec2 Along(Vec2 a, Vec2 b, double t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>
        /// A line or polygon with cuts[from..to) hidden (StructureCuts of one shape, sorted by edge and T0): the visible
        /// pieces as open polylines, square caps at the shape's own ends, cut ends along the over band's edge, mitred joins
        /// at polygon corners. Dotted pieces carry the dash phase across the gaps.
        /// </summary>
        void AddCutShape(BoardMeshBuilder mb, Shape shape, List<StructureCut> cuts, int from, int to, Color32 color)
        {
            float width = WidthOf(shape.Style.Width);
            bool dashed = shape.Style.Dash == LineStyle.Dotted;
            float phase = 0f;
            if (shape is LineShape line)
            {
                double prev = 0;
                Vec2 prevDir = default;
                bool prevCap = true;
                for (int k = from; k < to; k++)
                {
                    StructureCut c = cuts[k];
                    if (c.T0 > prev) AddPiece(mb, Along(line.A, line.B, prev), Along(line.A, line.B, c.T0), null, prevCap, prevDir, false, c.Dir0, width, color, dashed, ref phase);
                    phase += (float)(Vec2.Distance(line.A, line.B) * (c.T1 - System.Math.Max(prev, c.T0)) * sideMeters);
                    prev = System.Math.Max(prev, c.T1);
                    prevDir = c.Dir1;
                    prevCap = false;
                }
                if (prev < 1) AddPiece(mb, Along(line.A, line.B, prev), line.B, null, prevCap, prevDir, true, default, width, color, dashed, ref phase);
                return;
            }
            var poly = (PolygonShape)shape;
            int n = poly.Sides;
            int m = to - from;
            for (int i = 0; i < m; i++)
            {
                StructureCut start = cuts[from + i], next = cuts[from + (i + 1) % m];
                Vec2 a0 = BandGraph.PolygonVertex(poly, start.Edge), a1 = BandGraph.PolygonVertex(poly, (start.Edge + 1) % n);
                Vec2 b0 = BandGraph.PolygonVertex(poly, next.Edge), b1 = BandGraph.PolygonVertex(poly, (next.Edge + 1) % n);
                _cutPoints.Clear();
                _cutPoints.Add(Along(a0, a1, start.T1));
                bool sameEdgeAhead = m > 1 && next.Edge == start.Edge && next.T0 >= start.T1;
                if (!sameEdgeAhead)
                {
                    int k = start.Edge;
                    do
                    {
                        k = (k + 1) % n;
                        _cutPoints.Add(BandGraph.PolygonVertex(poly, k));
                    } while (k != next.Edge);
                }
                _cutPoints.Add(Along(b0, b1, next.T0));
                AddPiece(mb, default, default, _cutPoints, false, start.Dir1, false, next.Dir0, width, color, dashed, ref phase);
            }
        }

        readonly List<Vec2> _cutPoints = new List<Vec2>(16);

        /// <summary>One visible piece: a..b, or the board points in <paramref name="points"/> when given.</summary>
        void AddPiece(BoardMeshBuilder mb, Vec2 a, Vec2 b, List<Vec2> points, bool capStart, Vec2 dirStart, bool capEnd,
            Vec2 dirEnd, float width, Color32 color, bool dashed, ref float phase)
        {
            _poly.Clear();
            if (points == null) { _poly.Add(P(a, ShapesLift)); _poly.Add(P(b, ShapesLift)); }
            else foreach (var p in points) _poly.Add(P(p, ShapesLift));
            if (_poly.Count < 2) return;
            if (dashed)
            {
                for (int i = 0; i + 1 < _poly.Count; i++)
                    mb.AddDashedSegment(_poly[i], _poly[i + 1], width, color, StrokeDash, StrokeGap, ref phase);
                return;
            }
            Vec2 first = points == null ? a : points[0], last = points == null ? b : points[points.Count - 1];
            mb.AddOpenPolyline(_poly, width, color, capStart, LocalDir(first, dirStart), capEnd, LocalDir(last, dirEnd));
        }

        void AddRuns(BoardMeshBuilder mb, BandOutline o, List<BandRun> runs, float width, Color32 color)
        {
            for (int r = 0; r < runs.Count; r++)
            {
                BandRun run = runs[r];
                _poly.Clear();
                for (int i = 0; i < run.Count; i++) _poly.Add(P(o.Points[run.Start + i], ShapesLift));
                mb.AddPolyline(_poly, run.Closed, width, color);
            }
        }

        void AddBridges(BoardMeshBuilder mb, List<Vec2> pairs, float width, Color32 color)
        {
            for (int i = 0; i + 1 < pairs.Count; i += 2)
                mb.AddSegmentNoCaps(P(pairs[i], ShapesLift), P(pairs[i + 1], ShapesLift), width, color);
        }

        /// <summary>
        /// Selected shapes on visible layers: first a solid halo in <see cref="selectionColor"/>, wider than the stroke by
        /// <see cref="SelectionHaloMeters"/> on each side, then the shape's own stroke (layer opacity applied) on top.
        /// Also rebuilds the SelectionFrame part from the same shapes' union bounds.
        /// </summary>
        void RebuildSelection(Board board)
        {
            var mb = _selection.Builder;
            mb.Clear();
            var ids = _session?.Selection?.Ids;
            bool anyBounds = false;
            GeoART.Engine.BoardRect bounds = default;
            if (board != null && ids != null && ids.Count > 0)
            {
                float haloExtra = 2f * SelectionHaloMeters * StrokeScale;
                foreach (var layer in board.Layers)
                {
                    if (layer == null || !layer.Visible) continue;
                    float opacity = Mathf.Clamp01((float)layer.Opacity);
                    foreach (var shape in layer.Shapes)
                    {
                        if (shape == null || !ids.Contains(shape.Id)) continue;
                        if (GeoART.Engine.SelectionGeometry.TryGetBounds(shape, out var b))
                        {
                            bounds = anyBounds ? GeoART.Engine.BoardRect.Union(bounds, b) : b;
                            anyBounds = true;
                        }
                        AddShapeStroke(mb, shape, selectionColor, SelectionLift, WidthOf(shape.Style.Width) + haloExtra, false);
                        if (opacity > 0f)
                            AddShape(mb, shape, WithOpacity(ToColor32(shape.Style.Color), opacity), SelectionLift);
                    }
                }
            }
            _selection.Apply();
            RebuildSelectionFrame(anyBounds, bounds);
        }

        /// <summary>
        /// The selection's dashed bounds box (padded by the halo width) and the pivot marker at the bounds centre; empty
        /// when nothing selected has bounds.
        /// </summary>
        void RebuildSelectionFrame(bool any, GeoART.Engine.BoardRect b)
        {
            var mb = _selectionFrame.Builder;
            mb.Clear();
            if (any && b.IsFinite && sideMeters > 0f)
            {
                float s = StrokeScale;
                double pad = SelectionHaloMeters * s / sideMeters;   // metres -> board units
                _poly.Clear();
                _poly.Add(P(new Vec2(b.MinX - pad, b.MinY - pad), SelectionLift));
                _poly.Add(P(new Vec2(b.MaxX + pad, b.MinY - pad), SelectionLift));
                _poly.Add(P(new Vec2(b.MaxX + pad, b.MaxY + pad), SelectionLift));
                _poly.Add(P(new Vec2(b.MinX - pad, b.MaxY + pad), SelectionLift));
                mb.AddPolyline(_poly, true, 0.0009f * s, selectionFrameColor, true, StrokeDash, StrokeGap);

                var c = new Vec2((b.MinX + b.MaxX) * 0.5, (b.MinY + b.MaxY) * 0.5);
                float width = 0.0012f * s;
                mb.AddCircle(P(c, SelectionLift), 0.0045f * s, width, pivotColor);
                double arm = 0.006f * s / sideMeters;
                mb.AddSegment(P(new Vec2(c.X - arm, c.Y), SelectionLift), P(new Vec2(c.X + arm, c.Y), SelectionLift), width, pivotColor);
                mb.AddSegment(P(new Vec2(c.X, c.Y - arm), SelectionLift), P(new Vec2(c.X, c.Y + arm), SelectionLift), width, pivotColor);
            }
            _selectionFrame.Apply();
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
                Shape hoverRing = _engine?.HoverRing;
                if (preview != null)
                    for (int i = 0; i < preview.Count; i++)
                    {
                        Shape shape = preview[i];
                        if (shape == null) continue;
                        if (ReferenceEquals(shape, hoverRing) && shape is CircleShape ring && ring.Center.IsFinite)
                        {
                            // The snapped dot as a filled orange disc, then the (popping) hover ring around it.
                            Vector3 c = P(ring.Center, OverlayLift);
                            mb.AddDisc(c, GuideDotRadius * StrokeScale, cursorColor, 14);
                            mb.AddCircle(c, (float)(ring.Radius * sideMeters) * RingScale(), WidthOf(ring.Style.Width), previewColor);
                            continue;
                        }
                        AddShape(mb, shape, previewColor, OverlayLift);
                    }

                if (_session.Cursor is Vec2 cursor && cursor.IsFinite)
                {
                    Vector3 c = P(cursor, OverlayLift);
                    mb.AddCircle(c, 0.009f * StrokeScale, 0.0012f * StrokeScale, cursorColor);
                    mb.AddDisc(c, 0.0022f * StrokeScale, cursorColor, 10);
                }
            }
            _overlay.Apply();
        }

        void AddShape(BoardMeshBuilder mb, Shape shape, Color32 color, float lift) =>
            AddShapeStroke(mb, shape, color, lift, WidthOf(shape.Style.Width), shape.Style.Dash == LineStyle.Dotted);

        /// <summary>The shape's outline with an explicit width and dash (the selection halo is wider and always solid).</summary>
        void AddShapeStroke(BoardMeshBuilder mb, Shape shape, Color32 color, float lift, float width, bool dashed)
        {
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
