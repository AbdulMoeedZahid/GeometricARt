using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// Colour picker built as uGUI at runtime (like ToolBar): one slim pill at the bottom-left of the safe area, just
    /// above the placement buttons. It is narrow enough (about 290 of 390 reference px) to end left of the right-edge
    /// ToolBar column, whose bottom reaches down to about 19% of the screen height.
    ///   5 round swatches  shape colour for NEW lines/circles/polygons: Black, White, Red, Green, Blue
    ///                     (sets IEditorSession.Style colour, like the Width button sets its width; the selected
    ///                     swatch gets the accent orange ring). With a selection the colour also restyles the selected
    ///                     shapes (one "Style" step); without one, drawn shapes keep their colour
    ///   Custom swatch     a rainbow ring around the last colour picked on the colour wheel (ColorBar.Wheel.cs): a tap
    ///                     selects that colour, or opens the wheel when there is none or it is already the current one
    ///   Board button      board guide lines Black &lt;-&gt; White (BoardAppearance.Guides: dashed grid and outline;
    ///                     nothing is filled, the real surface stays visible)
    /// Every element, including the pill background, is a raycast target, so BoardInputRouter ignores touches on it.
    /// Visible only while the board is placed (or when the scene has no IBoardSpace), like ToolBar.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class ColorBar : MonoBehaviour
    {
        [SerializeField] bool show = true;

        static readonly Color Accent = new Color(1f, 0.42f, 0f, 0.95f);
        static readonly Color PillColor = new Color(0.08f, 0.08f, 0.1f, 0.78f);
        static readonly Color SwatchOutline = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        // Reference px (390x844, match 0.5).
        // Slots went from 40 to 36 px when the custom swatch was added, so the pill still ends left of the tool column.
        public const float PillHeight = 48f, Swatch = 28f, Ring = 34f, SwatchSlot = 36f, BoardButtonW = 72f;
        public const float EdgeMargin = 8f;
        const float Pad = 6f, Gap = 8f;
        /// <summary>Pill bottom above the safe area's bottom, as a fraction of the screen height (placement row ends at 11%).</summary>
        public const float BottomFraction = 0.12f;

        /// <summary>Palette swatches, the custom swatch, the Board button.</summary>
        public static float PillWidth => Pad + (BoardAppearance.ShapeColors.Length + 1) * SwatchSlot + Gap + BoardButtonW + Pad;

        IEditorSession _session;
        IBoardSpace _boardSpace;
        Canvas _canvas;
        RectTransform _pill;
        Rect _safeArea;
        int _screenW, _screenH;
        readonly Image[] _rings = new Image[BoardAppearance.ShapeColors.Length];
        Image _boardIcon, _boardIconBorder;
        Text _boardLabel;

        public RectTransform Pill => _pill;

        void Awake()
        {
            _canvas = LayerUiKit.CreateCanvas(transform, "ColorBar", 104, true);
            _pill = LayerUiKit.NewImage("Pill", _canvas.transform, PillColor, true, PillHeight * 0.5f).rectTransform;
            ApplySafeArea();

            float x = Pad;
            RgbaColor[] colors = BoardAppearance.ShapeColors;
            for (int i = 0; i < colors.Length && i < _rings.Length; i++)
            {
                _rings[i] = CreateSwatch(i, colors[i], x);
                x += SwatchSlot;
            }
            CreateCustomSwatch(x);
            x += SwatchSlot;
            x += Gap;
            CreateBoardButton(x);
            BuildWheel();
            _canvas.enabled = false;
        }

        void Start()
        {
            ServiceHub.TryResolve(out _session);
            ServiceHub.TryResolve(out _boardSpace);
            if (_session != null) _session.StateChanged += Refresh;
            if (_boardSpace != null) _boardSpace.StateChanged += OnPlacementChanged;
            BoardAppearance.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            DestroyWheelTextures();
            if (_session != null) _session.StateChanged -= Refresh;
            if (_boardSpace != null) _boardSpace.StateChanged -= OnPlacementChanged;
            BoardAppearance.Changed -= Refresh;
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
            if (!visible) CloseWheel();

            RgbaColor current = _session != null ? _session.Style.Color : Palette.Black;
            RgbaColor[] colors = BoardAppearance.ShapeColors;
            for (int i = 0; i < colors.Length && i < _rings.Length; i++)
                SetColor(_rings[i], colors[i].Equals(current) ? Accent : Clear);
            RefreshCustom(current);

            bool white = BoardAppearance.Guides == BoardGuideColor.White;
            SetColor(_boardIcon, white ? ToColor(Palette.White) : ToColor(Palette.Black));
            SetColor(_boardIconBorder, white ? new Color(0.55f, 0.55f, 0.6f, 1f) : new Color(0.9f, 0.9f, 0.92f, 1f));
            string label = white ? "White" : "Black";
            if (_boardLabel != null && _boardLabel.text != label) _boardLabel.text = label;
        }

        // ---------- actions ----------

        /// <summary>Shape colour for new shapes (keeps width and dash); with a selection it restyles the selection too.</summary>
        public void SelectShapeColor(int index)
        {
            if (index < 0 || index >= BoardAppearance.ShapeColors.Length) return;
            SelectColor(BoardAppearance.ShapeColors[index]);
        }

        /// <summary>Makes <paramref name="color"/> the current colour; with a selection, one "Style" step restyles it.</summary>
        public void SelectColor(RgbaColor color)
        {
            if (_session == null) return;
            _session.Style = _session.Style.WithColor(color);   // raises StateChanged -> Refresh
            if (_session.Selection.Ids.Count > 0) _session.RestyleSelection(StyleParts.Color, _session.Style);
        }

        /// <summary>Board guide lines Black &lt;-&gt; White.</summary>
        public void ToggleBoardGuides() =>
            BoardAppearance.SetGuides(BoardAppearance.Guides == BoardGuideColor.Black ? BoardGuideColor.White : BoardGuideColor.Black);

        // ---------- building ----------

        Image CreateSwatch(int index, RgbaColor color, float x)
        {
            // Slot (the hit area and the selection ring), an outline so black reads on the dark pill, then the colour.
            Image ring = LayerUiKit.NewImage(BoardAppearance.ShapeColorNames[index], _pill, Clear, true, Ring * 0.5f);
            LayerUiKit.Place(ring.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x + SwatchSlot * 0.5f, 0f), new Vector2(Ring, Ring));
            var button = ring.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SelectShapeColor(index));

            Image outline = LayerUiKit.NewImage("Outline", ring.transform, SwatchOutline, false, (Swatch + 3f) * 0.5f);
            LayerUiKit.Place(outline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(Swatch + 3f, Swatch + 3f));
            Image fill = LayerUiKit.NewImage("Color", ring.transform, ToColor(color), false, Swatch * 0.5f);
            LayerUiKit.Place(fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(Swatch, Swatch));
            return ring;
        }

        void CreateBoardButton(float x)
        {
            const float h = 40f;
            Image bg = LayerUiKit.NewImage("Board", _pill, new Color(1f, 1f, 1f, 0.12f), true, 10f);
            LayerUiKit.Place(bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f),
                new Vector2(BoardButtonW, h));
            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(ToggleBoardGuides);

            _boardIconBorder = LayerUiKit.NewImage("IconBorder", bg.transform, Color.white, false, 3f);
            LayerUiKit.Place(_boardIconBorder.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(8f, 0f), new Vector2(20f, 20f));
            _boardIcon = LayerUiKit.NewImage("Icon", _boardIconBorder.transform, Color.white, false, 2f);
            LayerUiKit.Place(_boardIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(16f, 16f));

            var caption = LayerUiKit.NewText("Caption", bg.transform, 9, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.65f));
            LayerUiKit.Place(caption.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(32f, 0f),
                new Vector2(36f, 14f));
            caption.text = "Board";
            _boardLabel = LayerUiKit.NewText("Label", bg.transform, 13, TextAnchor.MiddleLeft, Color.white);
            LayerUiKit.Place(_boardLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(32f, 1f),
                new Vector2(36f, 16f));
            _boardLabel.text = "Black";
        }

        /// <summary>Left edge of the safe area (plus EdgeMargin); bottom at BottomFraction of the screen above the safe bottom.</summary>
        void ApplySafeArea()
        {
            _safeArea = Screen.safeArea;
            _screenW = Screen.width;
            _screenH = Screen.height;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            var anchor = new Vector2(_safeArea.xMin / w, _safeArea.yMin / h + BottomFraction);
            LayerUiKit.Place(_pill, anchor, new Vector2(0f, 0f), new Vector2(EdgeMargin, 0f), new Vector2(PillWidth, PillHeight));
        }

        static void SetColor(Graphic g, Color c)
        {
            if (g != null && g.color != c) g.color = c;
        }

        static Color ToColor(RgbaColor c) => new Color32(c.R, c.G, c.B, c.A);

        // ---------- installer for scenes built before the colour picker existed ----------

        /// <summary>
        /// SceneBuilder adds ColorBar next to ToolBar. Scenes saved before that (Main.unity, sandboxes) get one at
        /// runtime on the ToolBar's GameObject, so no scene has to be rebuilt. Scenes without a ToolBar are left alone.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallInLoadedScene() => EnsureNextToToolBar();

        public static ColorBar EnsureNextToToolBar()
        {
            var existing = FindAnyObjectByType<ColorBar>(FindObjectsInactive.Include);
            if (existing != null) return existing;
            var toolBar = FindAnyObjectByType<ToolBar>(FindObjectsInactive.Include);
            return toolBar != null ? toolBar.gameObject.AddComponent<ColorBar>() : null;
        }
    }
}
