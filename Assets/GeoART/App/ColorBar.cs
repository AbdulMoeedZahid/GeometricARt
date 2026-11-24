using System;
using GeoART.Contracts;
using UnityEngine;

namespace GeoART.App
{
    /// <summary>
    /// Colour logic behind the ToolBar's Colour button and its palette (ToolBar.Colour.cs). It has no bar of its own any
    /// more: the slim pill that sat at the bottom-left was hidden under the Select action bar exactly when a colour was
    /// needed to restyle the selection, so the palette moved into the right-hand tool column. What stays here:
    ///   shape colour      <see cref="SelectColor"/> / <see cref="SelectShapeColor"/>: the colour for NEW
    ///                     lines/circles/polygons (IEditorSession.Style colour, like the Width button sets its width); with
    ///                     a selection it also restyles the selected shapes (one "Style" step); without one, drawn shapes
    ///                     keep their colour
    ///   custom colour     the last colour picked on the colour wheel (ColorBar.Wheel.cs), kept in PlayerPrefs
    ///   board guides      <see cref="ToggleBoardGuides"/>: board guide lines Black &lt;-&gt; White (BoardAppearance.Guides:
    ///                     dashed grid and outline; nothing is filled, the real surface stays visible)
    ///   no fill           <see cref="SelectNoFill"/>, the Fill tool's eraser (only reachable while ToolBar.FillEnabled)
    ///   the colour wheel  its own canvas (sorting order 115), see ColorBar.Wheel.cs
    /// The wheel closes while the board is not placed (or the component is switched off with <c>show</c>). Scenes and
    /// tests add this component next to the ToolBar; scenes saved before it existed get one at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class ColorBar : MonoBehaviour
    {
        [SerializeField] bool show = true;

        static readonly Color Accent = new Color(1f, 0.42f, 0f, 0.95f);
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        /// <summary>Raised when the custom colour (the wheel's last pick) changes.</summary>
        public event Action CustomChanged;

        IEditorSession _session;
        IBoardSpace _boardSpace;
        Rect _safeArea;
        int _screenW, _screenH;

        void Awake()
        {
            LoadCustom();
            BuildWheel();
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
            DestroyWheelTextures();
            if (_session != null) _session.StateChanged -= Refresh;
            if (_boardSpace != null) _boardSpace.StateChanged -= OnPlacementChanged;
        }

        void LateUpdate()
        {
            if (Screen.safeArea == _safeArea && Screen.width == _screenW && Screen.height == _screenH) return;
            _safeArea = Screen.safeArea;
            _screenW = Screen.width;
            _screenH = Screen.height;
            if (WheelOpen) PlaceWheel();   // placed for the old layout
        }

        void OnPlacementChanged(PlacementState _) => Refresh();

        void Refresh()
        {
            bool visible = show && _session != null && (_boardSpace == null || _boardSpace.IsPlaced);
            if (!visible) CloseWheel();
        }

        // ---------- actions ----------

        /// <summary>Shape colour for new shapes (keeps width and dash); with a selection it restyles the selection too.</summary>
        public void SelectShapeColor(int index)
        {
            if (index < 0 || index >= BoardAppearance.ShapeColors.Length) return;
            SelectColor(BoardAppearance.ShapeColors[index]);
        }

        /// <summary>Makes <paramref name="color"/> the current colour; with a selection, one "Style" step restyles it.</summary>
        public void SelectColor(RgbaColor color) => ApplyColor(_session, color);

        /// <summary>
        /// The colour change itself, also used by the ToolBar when the scene has no ColorBar: Style colour (raises
        /// StateChanged), the Fill tool's "no fill" ends, and a non-empty selection is restyled (one "Style" step).
        /// </summary>
        public static void ApplyColor(IEditorSession session, RgbaColor color)
        {
            if (session == null) return;
            if (session.ActiveTool == ToolId.Fill) session.SetFillErase(false);   // a colour ends "no fill"
            session.Style = session.Style.WithColor(color);
            if (session.ActiveTool != ToolId.Fill && session.Selection.Ids.Count > 0)
                session.RestyleSelection(StyleParts.Color, session.Style);
        }

        /// <summary>The custom colour when there is one; otherwise (nothing picked on the wheel yet) opens the wheel.</summary>
        public void SelectCustom()
        {
            if (_hasCustom) SelectColor(_custom);
            else OpenWheel();
        }

        /// <summary>The Fill tool's "no fill": taps clear fills until a colour is picked or the tool changes.</summary>
        public void SelectNoFill() => ApplyNoFill(_session);

        public static void ApplyNoFill(IEditorSession session)
        {
            if (session == null || session.ActiveTool != ToolId.Fill) return;
            session.SetFillErase(true);
        }

        /// <summary>Board guide lines Black &lt;-&gt; White.</summary>
        public static void ToggleBoardGuides() =>
            BoardAppearance.SetGuides(BoardAppearance.Guides == BoardGuideColor.Black ? BoardGuideColor.White : BoardGuideColor.Black);

        internal static Color ToColor(RgbaColor c) => new Color32(c.R, c.G, c.B, c.A);

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
