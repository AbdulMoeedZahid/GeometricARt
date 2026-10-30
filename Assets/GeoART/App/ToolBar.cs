using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// Tool picker built as uGUI at runtime (like PlacementControls): a compact vertical column on the RIGHT edge,
    /// vertically centred inside the safe area (the left edge belongs to the layer sidebar).
    ///   Line / Circle   select the tool; the active one is filled with the accent orange
    ///   Dotted          toggles IEditorSession.ToggleDottedLine (orange when on)
    ///   Width           cycles the stroke width Thin -> Medium -> Thick (label shows the current one)
    /// Icons are drawn procedurally into small textures once in Awake. Every button is a raycast target, so
    /// BoardInputRouter ignores touches that start on it. Visible only while the board is placed (or when the scene
    /// has no IBoardSpace). Event-driven: refreshes on session/placement StateChanged, no per-frame work except a
    /// cheap safe-area comparison.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolBar : MonoBehaviour
    {
        [SerializeField] bool show = true;

        static readonly Color Accent = new Color(1f, 0.42f, 0f, 0.95f);
        static readonly Color Idle = new Color(0.08f, 0.08f, 0.1f, 0.78f);
        const float ButtonW = 56f, ButtonH = 58f, Spacing = 8f, GroupGap = 14f, EdgeMargin = 8f;

        enum Glyph { Line, Circle, Dotted, Width }

        IEditorSession _session;
        IBoardSpace _boardSpace;
        Canvas _canvas;
        RectTransform _column;
        Rect _safeArea;
        Image _lineBg, _circleBg, _dottedBg;
        Text _widthLabel;
        Texture2D[] _textures = new Texture2D[5];
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
            scaler.referenceResolution = new Vector2(390f, 844f);
            scaler.matchWidthOrHeight = 0.5f;

            var columnGo = new GameObject("Column", typeof(RectTransform));
            columnGo.transform.SetParent(canvasGo.transform, false);
            _column = (RectTransform)columnGo.transform;
            _column.pivot = new Vector2(1f, 0.5f);
            _column.sizeDelta = new Vector2(ButtonW, 4 * ButtonH + 2 * Spacing + GroupGap);
            ApplySafeArea();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _rounded = MakeRoundedSprite(0);
            float y = 0f;
            _lineBg = CreateButton("Line", Glyph.Line, font, ref y, () => _session?.SetTool(ToolId.Line), out _);
            _circleBg = CreateButton("Circle", Glyph.Circle, font, ref y, () => _session?.SetTool(ToolId.Circle), out _);
            y += GroupGap - Spacing;
            _dottedBg = CreateButton("Dotted", Glyph.Dotted, font, ref y, () => _session?.ToggleDottedLine(), out _);
            CreateButton("Medium", Glyph.Width, font, ref y, CycleWidth, out _widthLabel);

            _canvas.enabled = false;
        }

        void Start()
        {
            ServiceHub.TryResolve(out _session);
            ServiceHub.TryResolve(out _boardSpace);
            if (_session != null) _session.StateChanged += Refresh;
            if (_boardSpace != null) _boardSpace.StateChanged += OnPlacementChanged;
            Refresh();
        }

        void OnDestroy()
        {
            if (_session != null) _session.StateChanged -= Refresh;
            if (_boardSpace != null) _boardSpace.StateChanged -= OnPlacementChanged;
            if (_rounded != null) Destroy(_rounded);
            foreach (var t in _textures)
                if (t != null) Destroy(t);
        }

        void LateUpdate()
        {
            if (Screen.safeArea != _safeArea) ApplySafeArea();
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
            SetColor(_dottedBg, _session.DottedLine ? Accent : Idle);
            string width = WidthLabel(_session.Style.Width);
            if (!ReferenceEquals(_widthLabel.text, width)) _widthLabel.text = width;
        }

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

        Image CreateButton(string label, Glyph glyph, Font font, ref float y, UnityEngine.Events.UnityAction onClick,
            out Text text)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_column, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(ButtonW, ButtonH);
            rt.anchoredPosition = new Vector2(0f, -y);
            y += ButtonH + Spacing;

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
            irt.sizeDelta = new Vector2(30f, 30f);
            irt.anchoredPosition = new Vector2(0f, -6f);
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

        /// <summary>Right edge of the safe area, vertically centred in it.</summary>
        void ApplySafeArea()
        {
            _safeArea = Screen.safeArea;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            var anchor = new Vector2(_safeArea.xMax / w, (_safeArea.yMin + _safeArea.yMax) * 0.5f / h);
            _column.anchorMin = _column.anchorMax = anchor;
            _column.anchoredPosition = new Vector2(-EdgeMargin, 0f);
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
                default:   // Width: three bars of increasing thickness
                {
                    float d1 = SegmentDistance(p, new Vector2(0.18f, 0.76f), new Vector2(0.82f, 0.76f)) - 0.02f;
                    float d2 = SegmentDistance(p, new Vector2(0.18f, 0.52f), new Vector2(0.82f, 0.52f)) - 0.045f;
                    float d3 = SegmentDistance(p, new Vector2(0.18f, 0.24f), new Vector2(0.82f, 0.24f)) - 0.075f;
                    return Mathf.Min(d1, Mathf.Min(d2, d3));
                }
            }
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
            _textures[4 + slot] = tex;
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
