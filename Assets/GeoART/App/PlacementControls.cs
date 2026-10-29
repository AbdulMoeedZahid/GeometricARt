// TEMPORARY (A) until C's UI binds ICornerPlacement; remove from Main.unity then.
using GeoART.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// TEMPORARY placement buttons for A's device testing (CORNER_PLACEMENT_PLAN 4.8, D9). Built at runtime as uGUI, not
    /// IMGUI, so ARPlacementTapHandler's EventSystem hit-test (and D's input router) ignore taps on them: tapping
    /// "Undo corner" never also adds a corner. C replaces this by binding its real UI to ICornerPlacement / IBoardSpace.
    ///
    ///   Undo corner / Restart   picking, 1+ corners
    ///   Quick place 40 cm       Corners mode, PlaneFound, no corner yet
    ///   Mode: Corners / Quick   not placed, no corner yet
    ///   Place board             4 corners with a valid fit (CanConfirm)
    ///   Re-place board          placed
    /// Event-driven (StateChanged / CornersChanged); no per-frame work.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlacementControls : MonoBehaviour
    {
        [SerializeField] bool show = true;

        const string ModeCornersLabel = "Mode: Corners", ModeQuickLabel = "Mode: Quick";

        IBoardSpace _boardSpace;
        ICornerPlacement _corners;
        Canvas _canvas;
        RectTransform _row;
        Rect _safeArea;
        GameObject _undo, _restart, _quick, _mode, _place, _replace;
        Text _modeLabel;

        void Awake()
        {
            var canvasGo = new GameObject("PlacementControls (temporary)", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390f, 844f);
            scaler.matchWidthOrHeight = 0.5f;

            var rowGo = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(canvasGo.transform, false);
            _row = (RectTransform)rowGo.transform;
            var layout = rowGo.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            ApplySafeArea();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _undo = CreateButton("Undo corner", font, OnUndo, out _);
            _restart = CreateButton("Restart", font, OnRestart, out _);
            _quick = CreateButton("Quick place 40 cm", font, OnQuickPlace, out _);
            _mode = CreateButton(ModeCornersLabel, font, OnToggleMode, out _modeLabel);
            _place = CreateButton("Place board", font, OnPlace, out _);
            _replace = CreateButton("Re-place board", font, OnReplace, out _);

            _canvas.enabled = show;
            SetAllHidden();
        }

        void Start()
        {
            ServiceHub.TryResolve(out _boardSpace);
            ServiceHub.TryResolve(out _corners);
            if (_boardSpace != null) _boardSpace.StateChanged += OnStateChanged;
            if (_corners != null) _corners.CornersChanged += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (_boardSpace != null) _boardSpace.StateChanged -= OnStateChanged;
            if (_corners != null) _corners.CornersChanged -= Refresh;
        }

        void OnStateChanged(PlacementState _) => Refresh();

        void Refresh()
        {
            if (Screen.safeArea != _safeArea) ApplySafeArea();
            if (_boardSpace == null) { SetAllHidden(); return; }

            PlacementState state = _boardSpace.State;
            bool placed = _boardSpace.IsPlaced;
            bool hasCorners = _corners != null;
            int count = hasCorners ? _corners.CornerCount : 0;
            bool cornersMode = hasCorners && _corners.Mode == PlacementMode.Corners;

            SetActive(_undo, !placed && count > 0);
            SetActive(_restart, !placed && count > 0);
            SetActive(_quick, cornersMode && state == PlacementState.PlaneFound && count == 0);
            SetActive(_mode, hasCorners && !placed && count == 0 && state != PlacementState.Unsupported);
            SetActive(_place, hasCorners && _corners.CanConfirm);
            SetActive(_replace, placed);

            string modeText = cornersMode ? ModeCornersLabel : ModeQuickLabel;
            if (_modeLabel != null && !ReferenceEquals(_modeLabel.text, modeText)) _modeLabel.text = modeText;
        }

        // ---------- actions ----------

        void OnUndo() => _corners?.UndoCorner();
        void OnRestart() => _corners?.ClearCorners();
        void OnQuickPlace() => _boardSpace?.PlaceAtReticle();
        void OnPlace() => _corners?.ConfirmCorners();
        void OnReplace() => _boardSpace?.Reset();

        void OnToggleMode()
        {
            if (_corners == null) return;
            _corners.SetMode(_corners.Mode == PlacementMode.Corners ? PlacementMode.QuickPlace : PlacementMode.Corners);
            Refresh();
        }

        // ---------- building ----------

        GameObject CreateButton(string label, Font font, UnityEngine.Events.UnityAction onClick, out Text text)
        {
            GameObject go = DefaultControls.CreateButton(new DefaultControls.Resources());
            go.name = label;
            go.transform.SetParent(_row, false);
            go.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.78f);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            text = go.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                text.font = font;
                text.fontSize = 15;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 9;
                text.resizeTextMaxSize = 15;
                text.color = Color.white;
            }
            return go;
        }

        /// <summary>Row at 4-11% of the screen height, inside the safe area (home indicator, rounded corners).</summary>
        void ApplySafeArea()
        {
            _safeArea = Screen.safeArea;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            float left = _safeArea.xMin / w + 0.03f, right = _safeArea.xMax / w - 0.03f;
            float bottom = _safeArea.yMin / h;
            _row.anchorMin = new Vector2(left, bottom + 0.04f);
            _row.anchorMax = new Vector2(right, bottom + 0.11f);
            _row.offsetMin = _row.offsetMax = Vector2.zero;
        }

        void SetAllHidden()
        {
            SetActive(_undo, false);
            SetActive(_restart, false);
            SetActive(_quick, false);
            SetActive(_mode, false);
            SetActive(_place, false);
            SetActive(_replace, false);
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }
    }
}
