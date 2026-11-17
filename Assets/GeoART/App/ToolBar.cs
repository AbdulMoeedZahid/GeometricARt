using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// Tool picker built as uGUI at runtime (like PlacementControls): a compact vertical column on the RIGHT edge,
    /// vertically centred inside the safe area (the left edge belongs to the layer sidebar).
    ///   Line / Circle / Select   pick the tool (radio buttons); the active one is filled with the accent orange.
    ///                   Select (marquee-corner icon) sets ToolId.SelectMove: drag a box or tap a stroke to select. It
    ///                   stays orange in Move, Scale and Duplicate too (the selection tools)
    ///   Dotted          toggles IEditorSession.ToggleDottedLine (orange when on)
    ///   Width           cycles the stroke width Thin -> Medium -> Thick (label shows the current one)
    ///   Grid            switches the tile grid Square <-> Hex (icon and label show the current kind)
    ///   Tiles           cycles tiles per side 1 -> 2 -> 3 -> 4 -> 1 (label "2x2" etc.)
    ///   Snap            toggles IEditorSession.SetSnapEnabled (orange when on; on by default)
    ///   HV              toggles IEditorSession.SetOrthoLock: Line tool draws horizontal/vertical lines (orange when on)
    /// Nine buttons in four groups (3, 2, 2, 2), 550 reference px tall: fits a 390x844 portrait screen with room for
    /// the placement row and the coach text. Shorter screens (landscape: Android 16 ignores the portrait lock on large
    /// screens) shrink the column to stay between the two, see <see cref="ApplySafeArea"/>.
    /// Icons are drawn procedurally into small textures once in Awake. Every button is a raycast target, and the column
    /// (like the action bar) has a transparent raycast-target panel behind its buttons (<see cref="AddTouchBlocker"/>), so
    /// BoardInputRouter ignores touches that start anywhere on it, gaps between buttons included. Visible only while the board is placed (or when the scene
    /// has no IBoardSpace). Event-driven: refreshes on session/placement StateChanged, no per-frame work except a
    /// cheap safe-area comparison.
    /// The contextual action bar (Move | Scale | Duplicate | Deselect, shown while something is selected in a selection
    /// tool) is in ToolBar.ActionBar.cs.
    /// Undo (top-left of the safe area, dimmed when there is nothing to undo, hidden unless the board is Placed) is in
    /// ToolBar.Undo.cs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class ToolBar : MonoBehaviour
    {
        [SerializeField] bool show = true;

        static readonly Color Accent = new Color(1f, 0.42f, 0f, 0.95f);
        static readonly Color Idle = new Color(0.08f, 0.08f, 0.1f, 0.78f);
        // ButtonH went from 58 to 56 when the column grew from 6 to 8 buttons (Snap, HV), and from 56 to 52 at 9 (Select).
        const float ButtonW = 56f, ButtonH = 52f, Spacing = 8f, GroupGap = 14f, EdgeMargin = 8f;
        // Screen-height fractions the column stays clear of: PlacementControls' row ends 11% above the safe-area bottom
        // (its Re-place button shows while the bar does), DevHud's coach text starts 20% below the top of the screen.
        const float PlacementRowTop = 0.11f, CoachTextBottom = 0.80f, MinFit = 0.4f;
        const int ButtonCount = 9, GroupCount = 4;

        /// <summary>Column height in reference px: 9 buttons, one spacing between buttons of a group, a gap between groups.</summary>
        public const float ColumnHeight =
            ButtonCount * ButtonH + (ButtonCount - GroupCount) * Spacing + (GroupCount - 1) * GroupGap;

        enum Glyph { Line, Circle, Dotted, Width, Square, Hex, Tiles, Snap, HV, Select, Move, Scale, Duplicate, Deselect, Undo }
        const int GlyphCount = 15;   // = number of Glyph values: the rounded sprite's texture sits at index GlyphCount

        IEditorSession _session;
        IBoardSpace _boardSpace;
        Canvas _canvas;
        RectTransform _column;
        Rect _safeArea;
        int _screenW, _screenH;   // ApplySafeArea depends on the screen size too, not just the safe area
        Image _lineBg, _circleBg, _selectBg, _dottedBg, _snapBg, _hvBg;
        Text _widthLabel, _gridLabel, _tilesLabel;
        RawImage _gridIcon;
        Texture _squareGlyph, _hexGlyph;
        readonly Texture2D[] _textures = new Texture2D[GlyphCount + 1];   // glyphs, then the rounded sprite
        Sprite _rounded;

        void Awake()
        {
            var canvasGo = new GameObject("ToolBar", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 105;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ScreenSnapMetric.ReferenceResolution;   // 390x844, shared with snap tolerance
            scaler.matchWidthOrHeight = ScreenSnapMetric.MatchWidthOrHeight;

            var columnGo = new GameObject("Column", typeof(RectTransform));
            columnGo.transform.SetParent(canvasGo.transform, false);
            _column = (RectTransform)columnGo.transform;
            _column.pivot = new Vector2(1f, 0.5f);
            _column.sizeDelta = new Vector2(ButtonW, ColumnHeight);
            AddTouchBlocker(columnGo);
            ApplySafeArea();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _rounded = MakeRoundedSprite(0);
            float y = 0f;
            _lineBg = CreateButton("Line", Glyph.Line, font, ref y, () => _session?.SetTool(ToolId.Line), out _);
            _circleBg = CreateButton("Circle", Glyph.Circle, font, ref y, () => _session?.SetTool(ToolId.Circle), out _);
            _selectBg = CreateButton("Select", Glyph.Select, font, ref y, () => _session?.SetTool(ToolId.SelectMove), out _);
            y += GroupGap - Spacing;
            _dottedBg = CreateButton("Dotted", Glyph.Dotted, font, ref y, () => _session?.ToggleDottedLine(), out _);
            CreateButton("Medium", Glyph.Width, font, ref y, CycleWidth, out _widthLabel);
            y += GroupGap - Spacing;
            _hexGlyph = MakeGlyph(Glyph.Hex);
            var gridBg = CreateButton("Square", Glyph.Square, font, ref y, ToggleGridKind, out _gridLabel);
            _gridIcon = gridBg.GetComponentInChildren<RawImage>();
            _squareGlyph = _gridIcon.texture;
            CreateButton("2x2", Glyph.Tiles, font, ref y, CycleGridN, out _tilesLabel);
            y += GroupGap - Spacing;
            _snapBg = CreateButton("Snap", Glyph.Snap, font, ref y, ToggleSnap, out _);
            _hvBg = CreateButton("HV", Glyph.HV, font, ref y, ToggleOrthoLock, out var hvLabel);
            hvLabel.text = "H/V";
            BuildActionBar(font);
            BuildUndoButton(font);

            _canvas.enabled = false;
        }

        void Start()
        {
            ServiceHub.TryResolve(out _session);
            ServiceHub.TryResolve(out _boardSpace);
            if (_session != null)
            {
                _session.StateChanged += Refresh;
                _session.BoardChanged += OnBoardChanged;
            }
            if (_boardSpace != null) _boardSpace.StateChanged += OnPlacementChanged;
            Refresh();
        }

        void OnDestroy()
        {
            if (_session != null)
            {
                _session.StateChanged -= Refresh;
                _session.BoardChanged -= OnBoardChanged;
            }
            if (_boardSpace != null) _boardSpace.StateChanged -= OnPlacementChanged;
            if (_rounded != null) Destroy(_rounded);
            foreach (var t in _textures)
                if (t != null) Destroy(t);
        }

        void LateUpdate()
        {
            if (Screen.safeArea != _safeArea || Screen.width != _screenW || Screen.height != _screenH) ApplySafeArea();
        }

        void OnPlacementChanged(PlacementState _) => Refresh();

        void Refresh()
        {
            bool visible = show && _session != null && (_boardSpace == null || _boardSpace.IsPlaced);
            if (_canvas.enabled != visible) _canvas.enabled = visible;
            if (_session == null) return;

            ToolId tool = _session.ActiveTool;
            SetColor(_lineBg, tool == ToolId.Line ? Accent : Idle);
            SetColor(_circleBg, tool == ToolId.Circle ? Accent : Idle);
            SetColor(_selectBg, IsSelectionTool(tool) ? Accent : Idle);
            SetColor(_dottedBg, _session.DottedLine ? Accent : Idle);
            SetColor(_snapBg, _session.SnapEnabled ? Accent : Idle);
            SetColor(_hvBg, _session.OrthoLock ? Accent : Idle);
            string width = WidthLabel(_session.Style.Width);
            if (!ReferenceEquals(_widthLabel.text, width)) _widthLabel.text = width;
            RefreshGrid();
            RefreshActionBar(visible);
            RefreshUndo(visible);
        }

        /// <summary>Select and the tools that act on its selection (Move, Scale, Duplicate).</summary>
        static bool IsSelectionTool(ToolId t) =>
            t == ToolId.SelectMove || t == ToolId.Move || t == ToolId.Scale || t == ToolId.Duplicate;

        void OnBoardChanged(BoardChangeKind kind)
        {
            if ((kind & (BoardChangeKind.Grid | BoardChangeKind.Reset)) != 0) RefreshGrid();
            RefreshUndo(_canvas.enabled);   // CanUndo changes only together with BoardChanged (no StateChanged)
        }

        void RefreshGrid()
        {
            Board board = _session?.Board;
            if (board == null) return;
            bool hex = board.GridKind == GridKind.Hex;
            string kind = hex ? "Hex" : "Square";
            if (!ReferenceEquals(_gridLabel.text, kind)) _gridLabel.text = kind;
            Texture icon = hex ? _hexGlyph : _squareGlyph;
            if (_gridIcon.texture != icon) _gridIcon.texture = icon;
            string tiles = TilesLabel(TileGrid.ClampN(board.GridN));
            if (!ReferenceEquals(_tilesLabel.text, tiles)) _tilesLabel.text = tiles;
        }

        void ToggleGridKind()
        {
            Board board = _session?.Board;
            if (board == null) return;
            _session.SetGridKind(board.GridKind == GridKind.Hex ? GridKind.Square : GridKind.Hex);
        }

        void CycleGridN()
        {
            Board board = _session?.Board;
            if (board == null) return;
            int n = TileGrid.ClampN(board.GridN);
            _session.SetGrid(n >= Board.MaxGridN ? Board.MinGridN : n + 1);
        }

        void ToggleSnap()
        {
            if (_session == null) return;
            _session.SetSnapEnabled(!_session.SnapEnabled);   // raises StateChanged -> Refresh
        }

        void ToggleOrthoLock()
        {
            if (_session == null) return;
            _session.SetOrthoLock(!_session.OrthoLock);
        }

        static readonly string[] s_TilesLabels = { "1x1", "2x2", "3x3", "4x4" };

        static string TilesLabel(int n) => s_TilesLabels[n - 1];

        void CycleWidth()
        {
            if (_session == null) return;
            StrokeStyle s = _session.Style;
            LineWidth next = s.Width == LineWidth.Thin ? LineWidth.Medium
                           : s.Width == LineWidth.Medium ? LineWidth.Thick : LineWidth.Thin;
            _session.Style = s.WithWidth(next);
        }

        static string WidthLabel(LineWidth w) =>
            w == LineWidth.Thin ? "Thin" : w == LineWidth.Thick ? "Thick" : "Medium";

        static void SetColor(Image image, Color c)
        {
            if (image != null && image.color != c) image.color = c;
        }

        // ---------- building ----------

        /// <summary>
        /// A fully transparent Image over <paramref name="go"/>'s whole rect, a raycast target: uGUI raycasts (and so
        /// BoardInputRouter's IsOverUI) hit the footprint between buttons too, so a touch in a gap never reaches the board.
        /// </summary>
        static void AddTouchBlocker(GameObject go)
        {
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = true;
        }

        Image CreateButton(string label, Glyph glyph, Font font, ref float y, UnityEngine.Events.UnityAction onClick,
            out Text text)
        {
            var bg = MakeButton(label, glyph, font, _column, onClick, out text);
            var rt = (RectTransform)bg.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(ButtonW, ButtonH);
            rt.anchoredPosition = new Vector2(0f, -y);
            y += ButtonH + Spacing;
            return bg;
        }

        /// <summary>A rounded button (raycast target) with a 28 px glyph and an 11 pt label; the caller places it.</summary>
        Image MakeButton(string label, Glyph glyph, Font font, RectTransform parent, UnityEngine.Events.UnityAction onClick,
            out Text text)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            var bg = go.GetComponent<Image>();
            bg.sprite = _rounded;
            bg.type = Image.Type.Sliced;
            bg.color = Idle;
            bg.raycastTarget = true;
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            var irt = (RectTransform)iconGo.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.sizeDelta = new Vector2(28f, 28f);
            irt.anchoredPosition = new Vector2(0f, -3f);
            var icon = iconGo.GetComponent<RawImage>();
            icon.texture = MakeGlyph(glyph);
            icon.raycastTarget = false;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 0f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(0f, 18f);
            trt.anchoredPosition = new Vector2(0f, 3f);
            text = textGo.GetComponent<Text>();
            text.text = label;
            text.font = font;
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return bg;
        }

        /// <summary>
        /// Right edge of the safe area, vertically centred in it, but kept inside the band between the placement row and
        /// the coach text: moved when centring would overlap one of them, and scaled down (never below
        /// <see cref="MinFit"/>) when the band is shorter than the column, e.g. a landscape window.
        /// </summary>
        void ApplySafeArea()
        {
            _safeArea = Screen.safeArea;
            _screenW = Screen.width;
            _screenH = Screen.height;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            float scale = ScreenSnapMetric.ScaleFactor(w, h);   // reference px -> screen px, as the CanvasScaler computes it
            float margin = EdgeMargin * scale;
            float bottom = _safeArea.yMin + PlacementRowTop * h + margin;
            float top = Mathf.Min(_safeArea.yMax, CoachTextBottom * h) - margin;
            float fit = Mathf.Clamp((top - bottom) / (ColumnHeight * scale), MinFit, 1f);
            float half = ColumnHeight * scale * fit * 0.5f;
            float centre = Mathf.Clamp((_safeArea.yMin + _safeArea.yMax) * 0.5f, bottom + half, Mathf.Max(bottom + half, top - half));
            _column.anchorMin = _column.anchorMax = new Vector2(_safeArea.xMax / w, centre / h);
            _column.anchoredPosition = new Vector2(-EdgeMargin, 0f);
            _column.localScale = new Vector3(fit, fit, 1f);
            PlaceActionBar(w, h, scale, fit);
            PlaceUndoButton(w, h, scale, fit);
        }

        // ---------- procedural icons (white on transparent, anti-aliased with a signed distance) ----------

        const int GlyphPx = 64;

        Texture2D MakeGlyph(Glyph g)
        {
            var tex = NewTexture(GlyphPx);
            var px = new Color32[GlyphPx * GlyphPx];
            for (int j = 0; j < GlyphPx; j++)
            for (int i = 0; i < GlyphPx; i++)
            {
                var p = new Vector2((i + 0.5f) / GlyphPx, (j + 0.5f) / GlyphPx);
                float d = GlyphDistance(g, p);   // in icon units (1 = icon side)
                float a = Mathf.Clamp01(0.5f - d * GlyphPx);
                px[j * GlyphPx + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _textures[(int)g] = tex;
            return tex;
        }

        static float GlyphDistance(Glyph g, Vector2 p)
        {
            Vector2 a = new Vector2(0.18f, 0.18f), b = new Vector2(0.82f, 0.82f);
            switch (g)
            {
                case Glyph.Line:
                    return Mathf.Min(SegmentDistance(p, a, b) - 0.045f,
                        Mathf.Min(Vector2.Distance(p, a), Vector2.Distance(p, b)) - 0.085f);
                case Glyph.Circle:
                    return Mathf.Min(Mathf.Abs(Vector2.Distance(p, new Vector2(0.5f, 0.5f)) - 0.36f) - 0.045f,
                        Vector2.Distance(p, new Vector2(0.5f, 0.5f)) - 0.07f);
                case Glyph.Dotted:
                {
                    // dashes along the diagonal: 4 dashes with gaps
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    float cell = t * 4f;
                    float k = Mathf.Floor(Mathf.Min(cell, 3.999f));
                    Vector2 s0 = a + ab * ((k + 0.08f) / 4f), s1 = a + ab * ((k + 0.62f) / 4f);
                    return SegmentDistance(p, s0, s1) - 0.05f;
                }
                case Glyph.Square:   // 2x2 squares
                {
                    float d = Mathf.Abs(BoxDistance(p, new Vector2(0.5f, 0.5f), 0.32f));
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.5f, 0.18f), new Vector2(0.5f, 0.82f)));
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.18f, 0.5f), new Vector2(0.82f, 0.5f)));
                    return d - 0.035f;
                }
                case Glyph.Hex:   // one pointy-top hexagon outline
                {
                    float d = float.MaxValue;
                    for (int k = 0; k < 6; k++)
                    {
                        float a0 = Mathf.PI / 6f + k * Mathf.PI / 3f, a1 = a0 + Mathf.PI / 3f;
                        var v0 = new Vector2(0.5f + 0.36f * Mathf.Cos(a0), 0.5f + 0.36f * Mathf.Sin(a0));
                        var v1 = new Vector2(0.5f + 0.36f * Mathf.Cos(a1), 0.5f + 0.36f * Mathf.Sin(a1));
                        d = Mathf.Min(d, SegmentDistance(p, v0, v1));
                    }
                    return d - 0.045f;
                }
                case Glyph.Snap:   // a line ending exactly on a ringed dot: "lands on the point"
                {
                    var dot = new Vector2(0.68f, 0.68f);
                    float d = SegmentDistance(p, new Vector2(0.16f, 0.16f), dot) - 0.04f;
                    d = Mathf.Min(d, Mathf.Abs(Vector2.Distance(p, dot) - 0.17f) - 0.035f);
                    return Mathf.Min(d, Vector2.Distance(p, dot) - 0.07f);
                }
                case Glyph.HV:   // a plus with end ticks: horizontal and vertical only
                {
                    float d = Mathf.Min(SegmentDistance(p, new Vector2(0.16f, 0.5f), new Vector2(0.84f, 0.5f)),
                        SegmentDistance(p, new Vector2(0.5f, 0.16f), new Vector2(0.5f, 0.84f))) - 0.04f;
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.16f, 0.4f), new Vector2(0.16f, 0.6f)) - 0.035f);
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.84f, 0.4f), new Vector2(0.84f, 0.6f)) - 0.035f);
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.4f, 0.16f), new Vector2(0.6f, 0.16f)) - 0.035f);
                    return Mathf.Min(d, SegmentDistance(p, new Vector2(0.4f, 0.84f), new Vector2(0.6f, 0.84f)) - 0.035f);
                }
                case Glyph.Select:   // four corner brackets of a marquee
                {
                    float d = float.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        var c = new Vector2((k & 1) == 0 ? 0.2f : 0.8f, (k & 2) == 0 ? 0.2f : 0.8f);
                        float sx = c.x < 0.5f ? 1f : -1f, sy = c.y < 0.5f ? 1f : -1f;
                        d = Mathf.Min(d, SegmentDistance(p, c, c + new Vector2(0.22f * sx, 0f)));
                        d = Mathf.Min(d, SegmentDistance(p, c, c + new Vector2(0f, 0.22f * sy)));
                    }
                    return d - 0.045f;
                }
                case Glyph.Move:   // four-way arrow
                {
                    var o = new Vector2(0.5f, 0.5f);
                    float d = Mathf.Min(SegmentDistance(p, new Vector2(0.18f, 0.5f), new Vector2(0.82f, 0.5f)),
                        SegmentDistance(p, new Vector2(0.5f, 0.18f), new Vector2(0.5f, 0.82f))) - 0.035f;
                    for (int k = 0; k < 4; k++)
                    {
                        var u = k == 0 ? Vector2.right : k == 1 ? Vector2.left : k == 2 ? Vector2.up : Vector2.down;
                        var n = new Vector2(-u.y, u.x);
                        Vector2 t = o + 0.32f * u;
                        d = Mathf.Min(d, SegmentDistance(p, t, t - 0.12f * u + 0.10f * n) - 0.035f);
                        d = Mathf.Min(d, SegmentDistance(p, t, t - 0.12f * u - 0.10f * n) - 0.035f);
                    }
                    return d;
                }
                case Glyph.Scale:   // small box, big corner, diagonal arrow out of the box
                {
                    float d = Mathf.Abs(BoxDistance(p, new Vector2(0.34f, 0.34f), 0.14f)) - 0.035f;
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.18f, 0.82f), new Vector2(0.82f, 0.82f)) - 0.035f);
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.82f, 0.82f), new Vector2(0.82f, 0.18f)) - 0.035f);
                    var tip = new Vector2(0.74f, 0.74f);
                    d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.52f, 0.52f), tip) - 0.035f);
                    d = Mathf.Min(d, SegmentDistance(p, tip, new Vector2(0.6f, 0.74f)) - 0.035f);
                    return Mathf.Min(d, SegmentDistance(p, tip, new Vector2(0.74f, 0.6f)) - 0.035f);
                }
                case Glyph.Duplicate:   // two overlapping squares
                    return Mathf.Min(Mathf.Abs(BoxDistance(p, new Vector2(0.42f, 0.42f), 0.2f)),
                        Mathf.Abs(BoxDistance(p, new Vector2(0.6f, 0.6f), 0.2f))) - 0.035f;
                case Glyph.Undo:   // U-turn arrow pointing back (left): top bar, right half-ring, bottom bar, arrowhead
                {
                    var c = new Vector2(0.58f, 0.435f);
                    const float r = 0.19f;
                    var top = new Vector2(0.58f, 0.625f);
                    var bottom = new Vector2(0.58f, 0.245f);
                    var tip = new Vector2(0.24f, 0.625f);
                    float d = p.x >= c.x ? Mathf.Abs(Vector2.Distance(p, c) - r)
                                         : Mathf.Min(Vector2.Distance(p, top), Vector2.Distance(p, bottom));
                    d = Mathf.Min(d, SegmentDistance(p, tip, top));
                    d = Mathf.Min(d, SegmentDistance(p, bottom, new Vector2(0.38f, 0.245f)));
                    d = Mathf.Min(d, SegmentDistance(p, tip, new Vector2(0.37f, 0.755f)));
                    d = Mathf.Min(d, SegmentDistance(p, tip, new Vector2(0.37f, 0.495f)));
                    return d - 0.045f;
                }
                case Glyph.Deselect:   // an X
                    return Mathf.Min(SegmentDistance(p, new Vector2(0.26f, 0.26f), new Vector2(0.74f, 0.74f)),
                        SegmentDistance(p, new Vector2(0.26f, 0.74f), new Vector2(0.74f, 0.26f))) - 0.045f;
                case Glyph.Tiles:   // 3x3 dots
                {
                    float d = float.MaxValue;
                    for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                        d = Mathf.Min(d, Vector2.Distance(p, new Vector2(0.22f + 0.28f * i, 0.22f + 0.28f * j)));
                    return d - 0.075f;
                }
                default:   // Width: three bars of increasing thickness
                {
                    float d1 = SegmentDistance(p, new Vector2(0.18f, 0.76f), new Vector2(0.82f, 0.76f)) - 0.02f;
                    float d2 = SegmentDistance(p, new Vector2(0.18f, 0.52f), new Vector2(0.82f, 0.52f)) - 0.045f;
                    float d3 = SegmentDistance(p, new Vector2(0.18f, 0.24f), new Vector2(0.82f, 0.24f)) - 0.075f;
                    return Mathf.Min(d1, Mathf.Min(d2, d3));
                }
            }
        }

        /// <summary>Signed distance to an axis-aligned square of half-side <paramref name="half"/>.</summary>
        static float BoxDistance(Vector2 p, Vector2 center, float half)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - center.x) - half, Mathf.Abs(p.y - center.y) - half);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>White rounded rectangle for a sliced Image (corner radius 10 px of a 32 px texture).</summary>
        Sprite MakeRoundedSprite(int slot)
        {
            const int size = 32;
            const float radius = 10f;
            var tex = NewTexture(size);
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = Mathf.Max(Mathf.Abs(i + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                float y = Mathf.Max(Mathf.Abs(j + 0.5f - size * 0.5f) - (size * 0.5f - radius), 0f);
                float d = Mathf.Sqrt(x * x + y * y) - radius;
                float a = Mathf.Clamp01(0.5f - d);
                px[j * size + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _textures[GlyphCount + slot] = tex;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }

        static Texture2D NewTexture(int size) =>
            new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
    }
}
