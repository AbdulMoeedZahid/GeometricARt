using System.Collections.Generic;
using GeoART.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EntityId = GeoART.Contracts.EntityId;

namespace GeoART.App
{
    /// <summary>
    /// Middle-left layer sidebar: a slim pill with "+" (add a layer at the top), one dash per layer (top layer at the
    /// top) and "-" (delete the bottom layer). "+" and "-" sit on round chips (neutral and red) behind thin separators so
    /// they never read as layer dashes. On a dash:
    ///   tap          select the layer (immediately on release)
    ///   double tap   toggle its visibility (second tap within <see cref="DoubleTapSeconds"/> of the first release)
    ///   hold         select + open the layer popup next to the dash (<see cref="HoldSeconds"/> without moving):
    ///                opacity, and the Bands section (Off / Double / Interlace, width in mm with an amber/red warning,
    ///                structure line, outline colour; inner lines and Flip weave arrive with interlace). The toolbar's
    ///                Bands button opens the same popup for the active layer (<see cref="OpenSettings"/>).
    ///   slide        moving more than <see cref="Slop"/> picks the dash up; it follows the finger, the others open a
    ///                gap, and release commits MoveLayer.
    /// uGUI built at runtime (like PlacementControls); every interactive part is a raycast target, so board input
    /// ignores touches that start here. Rebuilt on BoardChanged / StateChanged only; Update just runs the hold timer,
    /// row animation and label fade.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LayerSidebar : MonoBehaviour, ILayerPointerTarget
    {
        // gestures (seconds; distances in 390x844 reference units)
        const float HoldSeconds = 0.45f;
        const float DoubleTapSeconds = 0.3f;
        const float Slop = 8f;
        const float LabelSeconds = 1.3f, LabelFade = 0.3f;
        const float SmoothRate = 20f;
        const float StalePressSeconds = 1.5f;   // a press with no events this long may be replaced (lost PointerUp)

        // layout (reference units)
        const float PillWidth = 50f, EdgeInset = 6f, ButtonHeight = 42f, Pad = 4f;
        const float RowMax = 34f, RowMin = 22f, MaxHeightFraction = 0.46f;
        const float PopupWidth = 236f, PopupHeight = 318f, Gap = 8f;
        const float BandRowY = -92f;   // top of the Bands section inside the popup

        const int BlockerTag = -1, WidthSliderTag = -2;
        const double DefaultBoardSide = 0.40;   // metres, when no ICornerPlacement is registered

        // Warning texts of the Bands section (docs/BANDS.md).
        public const string BandsTooWideNote = "Too wide: the bands overlap. Make them narrower.";
        public const string BandsWideNote = "Bands this wide make the shapes small.";
        public const string CirclesNotBandedNote = "Circles are not banded.";
        public const string InnerTooNarrowNote = "Inner lines need a wider band.";
        public static string ConflictsNote(int n) => n == 1 ? "1 crossing can't alternate." : n + " crossings can't alternate.";
        static readonly Color NoteGrey = new Color(1f, 1f, 1f, 0.6f);
        static readonly Color NoteAmber = new Color(1f, 0.75f, 0.2f, 1f);
        static readonly Color NoteRed = new Color(1f, 0.35f, 0.3f, 1f);
        static readonly Color ChipIdle = new Color(1f, 1f, 1f, 0.14f);

        static readonly Color PillColor = new Color(0.07f, 0.07f, 0.09f, 0.62f);
        static readonly Color HoleColor = new Color(0.1f, 0.1f, 0.12f, 1f);
        static readonly Color DashColor = new Color(0.93f, 0.93f, 0.96f, 1f);
        static readonly Color Accent = new Color(1f, 0.42f, 0f, 1f);
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);
        static readonly Color PlusChipColor = new Color(1f, 1f, 1f, 0.2f);
        static readonly Color MinusChipColor = new Color(0.84f, 0.19f, 0.19f, 0.85f);   // destructive: Palette.Red
        static readonly Color SeparatorColor = new Color(1f, 1f, 1f, 0.22f);
        const float ChipSize = 32f;

        sealed class Row
        {
            public bool InUse;
            public EntityId Id;
            public int Display;               // 0 = top of the sidebar = Board.Layers[Count - 1]
            public float Y, TargetY;          // centre, relative to the top of the rows area
            public RectTransform Rect, Dash, Hole;
            public Image Hit, DashImage;
        }

        IEditorSession _session;
        Canvas _canvas;
        RectTransform _area, _pill, _rowsArea, _popup, _label;
        Image _plusGlyphH, _plusGlyphV, _minusGlyph, _plusChip, _minusChip;
        GameObject _blocker;
        CanvasGroup _labelGroup;
        Text _labelText, _popupTitle, _popupPercent;
        Slider _slider;

        // Bands section of the popup.
        readonly Image[] _modeChips = new Image[3];   // Off, Double, Interlace
        Image _structureChip, _innerChip, _flipChip, _outlineSwatch;
        Slider _bandSlider;
        Text _bandWidthText, _bandNote;
        bool _bandDragging, _safetyDirty = true;
        double _safeMax = double.PositiveInfinity;
        int _safeCrossings;
        double _safeAtWidth = double.NaN;   // the width SelfIntersections was counted at
        EntityId _safetyLayer;
        LayerBands _safeBands;              // the settings the safety numbers were computed for
        int _safeConflicts;                 // BandGraph.WeaveConflicts (topology only)
        bool _safeInnerShown;
        readonly List<Row> _rows = new List<Row>();          // pool; index = relay tag
        Rect _safeArea;
        int _screenW, _screenH;   // ApplySafeArea depends on the screen size too, not just the safe area
        float _scale = 1f, _rowH = RowMax, _pillH;
        int _count;
        bool _animating;
        EntityId _lastActive;

        // current press on a dash
        Row _press;
        int _pressPointer;
        Vector2 _pressPos;
        float _pressTime, _pressActivity, _dragDy;
        bool _holdFired, _dragging, _secondTap;
        int _dragTarget;

        // double-tap memory
        EntityId _lastTapId;
        float _lastTapUp = -10f;

        EntityId _popupLayer;
        int _lastPercent = -1;
        float _labelUntil;

        void Awake()
        {
            _canvas = LayerUiKit.CreateCanvas(transform, "LayerSidebar", 110, true);
            _area = LayerUiKit.NewRect("SafeArea", _canvas.transform);

            _pill = LayerUiKit.NewImage("Pill", _area, PillColor, true, PillWidth * 0.5f).rectTransform;
            LayerUiKit.Place(_pill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(EdgeInset, 0f),
                new Vector2(PillWidth, 200f));

            var plus = CreateButton("Add layer (+)", OnPlus);
            LayerUiKit.Place(plus, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Pad),
                new Vector2(PillWidth - 2 * Pad, ButtonHeight));
            _plusChip = Chip(plus, PlusChipColor);
            _plusGlyphH = Glyph(plus, 16f, 3f, 0f);
            _plusGlyphV = Glyph(plus, 16f, 3f, 90f);
            Separator(plus, 0f);

            var minus = CreateButton("Delete bottom layer (-)", OnMinus);
            LayerUiKit.Place(minus, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, Pad),
                new Vector2(PillWidth - 2 * Pad, ButtonHeight));
            _minusChip = Chip(minus, MinusChipColor);
            _minusGlyph = Glyph(minus, 14f, 3f, 0f);
            Separator(minus, 1f);

            _rowsArea = LayerUiKit.NewRect("Rows", _pill);
            _rowsArea.anchorMin = new Vector2(0f, 0f);
            _rowsArea.anchorMax = new Vector2(1f, 1f);
            _rowsArea.pivot = new Vector2(0.5f, 1f);
            _rowsArea.offsetMin = new Vector2(0f, Pad + ButtonHeight);
            _rowsArea.offsetMax = new Vector2(0f, -(Pad + ButtonHeight));

            BuildLabel();
            BuildPopup();
            ApplySafeArea();
        }

        void Start()
        {
            ServiceHub.TryResolve(out _session);
            if (_session != null)
            {
                _session.BoardChanged += OnBoardChanged;
                _session.StateChanged += Refresh;
                _lastActive = _session.Board.ActiveLayerId;
            }
            Refresh();
            SnapRows();
        }

        void OnDestroy()
        {
            if (_session == null) return;
            _session.BoardChanged -= OnBoardChanged;
            _session.StateChanged -= Refresh;
        }

        void OnDisable() => CancelPress();

        void OnApplicationPause(bool paused)
        {
            if (paused) CancelPress();
        }

        void OnBoardChanged(BoardChangeKind kind)
        {
            if ((kind & (BoardChangeKind.Shapes | BoardChangeKind.Reset | BoardChangeKind.History)) != 0) _safetyDirty = true;
            if ((kind & (BoardChangeKind.Layers | BoardChangeKind.Reset)) != 0) Refresh();
            else if ((kind & BoardChangeKind.Shapes) != 0 && _popup.gameObject.activeSelf) Refresh();   // warning follows edits
        }

        void Update()
        {
            if (Screen.safeArea != _safeArea || Screen.width != _screenW || Screen.height != _screenH)
            {
                ApplySafeArea();
                Layout();
            }
            float now = Time.unscaledTime;

            if (_press != null && !_dragging && !_holdFired)
            {
                float t = (now - _pressTime) / HoldSeconds;
                if (t >= 1f) FireHold();
                else _press.Hit.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.08f, 0.2f, t));   // hold "charging"
            }

            if (_animating) Animate(Time.unscaledDeltaTime);

            if (_labelUntil > 0f)
            {
                float left = _labelUntil - now;
                if (left <= 0f) { _labelUntil = 0f; _label.gameObject.SetActive(false); }
                else _labelGroup.alpha = Mathf.Clamp01(left / LabelFade);
            }
        }

        // ---------- model -> view ----------

        void Refresh()
        {
            if (_session == null) return;
            Board board = _session.Board;
            var layers = board.Layers;
            int n = layers.Count;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (r.InUse && board.FindLayer(r.Id) == null)
                {
                    if (r == _press) CancelPress();
                    r.InUse = false;
                    r.Rect.gameObject.SetActive(false);
                }
            }

            for (int i = 0; i < n; i++)
            {
                Layer layer = layers[i];
                Row row = FindRow(layer.Id);
                if (row == null)
                {
                    row = TakeRow();
                    row.Id = layer.Id;
                    row.Display = n - 1 - i;
                    row.Y = row.TargetY = SlotY(row.Display);   // new rows appear in place, no slide-in
                    row.Rect.anchoredPosition = new Vector2(0f, row.Y);
                }
                row.Display = n - 1 - i;
                ApplyStyle(row, layer, layer.Id == board.ActiveLayerId);
            }

            _count = n;
            Layout();

            bool full = n >= GeoART.Engine.EditorSession.MaxLayers;
            SetGlyphAlpha(_plusGlyphH, full ? 0.35f : 0.95f);
            SetGlyphAlpha(_plusGlyphV, full ? 0.35f : 0.95f);
            SetGlyphAlpha(_minusGlyph, n <= 1 ? 0.35f : 0.95f);
            SetGlyphAlpha(_plusChip, full ? PlusChipColor.a * 0.4f : PlusChipColor.a);
            SetGlyphAlpha(_minusChip, n <= 1 ? MinusChipColor.a * 0.35f : MinusChipColor.a);

            if (_popup.gameObject.activeSelf)
            {
                Layer pl = board.FindLayer(_popupLayer);
                if (pl == null) ClosePopup();
                else SyncPopup(pl);
            }

            if (board.ActiveLayerId != _lastActive)
            {
                _lastActive = board.ActiveLayerId;
                Layer active = board.FindLayer(_lastActive);
                Row ar = FindRow(_lastActive);
                if (active != null && ar != null && !_popup.gameObject.activeSelf)
                    ShowLabel(active.Visible ? active.Name : active.Name + " (hidden)", ar.Display);
            }
        }

        void ApplyStyle(Row row, Layer layer, bool selected)
        {
            bool dragged = row == _press && _dragging;
            float w = selected ? 30f : 18f, h = selected ? 6f : 4f;
            Color c = selected ? Accent : DashColor;
            if (layer.Visible) c.a = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01((float)layer.Opacity));
            else { h += 2f; c.a = selected ? 0.85f : 0.55f; }
            row.Dash.sizeDelta = new Vector2(w, h);
            row.DashImage.color = c;
            row.Hole.gameObject.SetActive(!layer.Visible);
            row.Hole.sizeDelta = new Vector2(w - 3f, h - 3f);
            row.Dash.localScale = dragged ? new Vector3(1.25f, 1.25f, 1f) : Vector3.one;
            if (row != _press) row.Hit.color = Clear;
        }

        void Layout()
        {
            int n = Mathf.Max(1, _count);
            float screenH = Screen.height / _scale, areaH = _safeArea.height / _scale;
            float maxPill = Mathf.Min(screenH * MaxHeightFraction, areaH - 16f);
            _rowH = Mathf.Clamp((maxPill - 2f * (ButtonHeight + Pad)) / n, RowMin, RowMax);
            _pillH = n * _rowH + 2f * (ButtonHeight + Pad);
            _pill.sizeDelta = new Vector2(PillWidth, _pillH);
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].InUse) _rows[i].Rect.sizeDelta = new Vector2(PillWidth - 4f, _rowH);
            ComputeTargets();
            if (_popup.gameObject.activeSelf) PlacePopup();
        }

        /// <summary>Row target positions; while dragging, rows between the origin and target slot shift by one.</summary>
        void ComputeTargets()
        {
            int s = _dragging ? _press.Display : -1, t = _dragTarget;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (!r.InUse) continue;
                if (_dragging && r == _press)
                {
                    r.TargetY = Mathf.Clamp(SlotY(s) + _dragDy, SlotY(_count - 1), SlotY(0));
                    r.Y = r.TargetY;
                    r.Rect.anchoredPosition = new Vector2(0f, r.Y);
                    continue;
                }
                int slot = r.Display;
                if (_dragging)
                {
                    if (s < t && slot > s && slot <= t) slot--;
                    else if (t < s && slot >= t && slot < s) slot++;
                }
                r.TargetY = SlotY(slot);
            }
            _animating = true;
        }

        void Animate(float dt)
        {
            float k = 1f - Mathf.Exp(-SmoothRate * dt);
            bool moving = false;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (!r.InUse || Mathf.Approximately(r.Y, r.TargetY)) continue;
                r.Y = Mathf.Abs(r.TargetY - r.Y) < 0.2f ? r.TargetY : Mathf.Lerp(r.Y, r.TargetY, k);
                r.Rect.anchoredPosition = new Vector2(0f, r.Y);
                moving |= r.Y != r.TargetY;
            }
            _animating = moving;
        }

        void SnapRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (!r.InUse) continue;
                r.Y = r.TargetY;
                r.Rect.anchoredPosition = new Vector2(0f, r.Y);
            }
            _animating = false;
        }

        float SlotY(int display) => -(display + 0.5f) * _rowH;

        /// <summary>Centre of a display slot in SafeArea coordinates (origin at its vertical middle).</summary>
        float SlotCenterInArea(int display) => _pillH * 0.5f - Pad - ButtonHeight + SlotY(display);

        // ---------- gestures ----------

        void ILayerPointerTarget.OnRelayDown(int tag, PointerEventData e)
        {
            if (tag == WidthSliderTag) { _bandDragging = true; return; }
            if (tag == BlockerTag) return;   // closes on release: the blocker must cover the whole touch (BoardInputRouter)
            if (_press != null)
            {
                // Normally a second finger: ignore it. But if the old press's PointerUp was lost (system gesture,
                // touch cancel), a new press with the same pointer id, or one after a quiet spell, replaces it.
                bool stale = e.pointerId == _pressPointer || Time.unscaledTime - _pressActivity > StalePressSeconds;
                if (!stale) return;
                CancelPress();
            }
            if (_session == null || tag < 0 || tag >= _rows.Count || !_rows[tag].InUse) return;
            Row row = _rows[tag];
            _press = row;
            _pressPointer = e.pointerId;
            _pressPos = e.position;
            _pressTime = _pressActivity = Time.unscaledTime;
            _holdFired = _dragging = false;
            _dragDy = 0f;
            _secondTap = _lastTapId == row.Id && _pressTime - _lastTapUp <= DoubleTapSeconds;
            row.Hit.color = new Color(1f, 1f, 1f, 0.08f);
        }

        void ILayerPointerTarget.OnRelayDrag(int tag, PointerEventData e)
        {
            if (_press == null || e.pointerId != _pressPointer || _holdFired) return;
            _pressActivity = Time.unscaledTime;
            Vector2 d = (e.position - _pressPos) / _scale;
            if (!_dragging)
            {
                if (d.sqrMagnitude < Slop * Slop) return;
                _lastTapId = EntityId.None;
                if (_count < 2) { CancelPress(); return; }
                _dragging = true;
                _press.Rect.SetAsLastSibling();
                _press.Hit.color = new Color(1f, 1f, 1f, 0.16f);
                _press.Dash.localScale = new Vector3(1.25f, 1.25f, 1f);
                _label.gameObject.SetActive(false);
                _labelUntil = 0f;
            }
            _dragDy = d.y;
            _dragTarget = Mathf.Clamp(Mathf.RoundToInt(_press.Display - _dragDy / _rowH), 0, _count - 1);
            ComputeTargets();
        }

        void ILayerPointerTarget.OnRelayUp(int tag, PointerEventData e)
        {
            if (tag == WidthSliderTag)
            {
                _bandDragging = false;   // the crossing count is taken once, on release (it is the costly part)
                _safetyDirty = true;
                Layer pl = _session?.Board.FindLayer(_popupLayer);
                if (pl != null) SyncPopup(pl);
                return;
            }
            if (tag == BlockerTag) { ClosePopup(); return; }
            if (_press == null || e.pointerId != _pressPointer) return;
            Row row = _press;
            bool dragging = _dragging, held = _holdFired, second = _secondTap;
            int target = _dragTarget;
            EndPress();

            if (dragging)
            {
                _lastTapId = EntityId.None;
                ComputeTargets();                                    // drop back into the gap
                _session.MoveLayer(row.Id, _count - 1 - target);     // Refresh re-slots everyone, animated
                return;
            }
            if (held) { _lastTapId = EntityId.None; return; }

            if (second)
            {
                _lastTapId = EntityId.None;
                Layer layer = _session.Board.FindLayer(row.Id);
                if (layer == null) return;
                bool show = !layer.Visible;
                _session.SetLayerVisible(row.Id, show);
                ShowLabel(layer.Name + (show ? " shown" : " hidden"), row.Display);
                return;
            }
            _session.SelectLayer(row.Id);
            _lastTapId = row.Id;
            _lastTapUp = Time.unscaledTime;
        }

        void FireHold()
        {
            _holdFired = true;
            _lastTapId = EntityId.None;
            Row row = _press;
            row.Hit.color = new Color(1f, 1f, 1f, 0.16f);
            _session.SelectLayer(row.Id);
            Layer layer = _session.Board.FindLayer(row.Id);
            if (layer != null) OpenPopup(row, layer);
        }

        void EndPress()
        {
            Row row = _press;
            _press = null;
            _dragging = _holdFired = _secondTap = false;
            _dragDy = 0f;
            if (row == null) return;
            row.Hit.color = Clear;
            row.Dash.localScale = Vector3.one;
        }

        void CancelPress()
        {
            if (_press == null) return;
            EndPress();
            if (_rows.Count > 0) ComputeTargets();
        }

        void OnPlus()
        {
            if (_session == null) return;
            _session.AddLayer();   // engine selects it (label shows) or toasts at the cap
        }

        void OnMinus()
        {
            if (_session == null) return;
            var layers = _session.Board.Layers;
            string name = layers.Count > 1 ? layers[0].Name : null;
            _session.DeleteBottomLayer();   // engine toasts when only one layer is left
            if (name != null && _count > 0) ShowLabel("Deleted " + name, _count - 1);
        }

        // ---------- opacity popup ----------

        /// <summary>
        /// Opens the layer popup (opacity and bands) for <paramref name="id"/>, as a hold on its dash does: selects the
        /// layer (ending any slider merge) and places the popup next to its dash. Used by the toolbar's Bands button.
        /// </summary>
        public void OpenSettings(EntityId id)
        {
            if (_session == null) return;
            Layer layer = _session.Board.FindLayer(id);
            if (layer == null) return;
            CancelPress();
            _session.SelectLayer(id);
            OpenPopup(FindRow(id), layer);
        }

        void OpenPopup(Row row, Layer layer)
        {
            _popupLayer = layer.Id;
            _safetyDirty = true;
            _bandDragging = false;
            _label.gameObject.SetActive(false);
            _labelUntil = 0f;
            SyncPopup(layer);
            _blocker.SetActive(true);
            _popup.gameObject.SetActive(true);
            PlacePopup();
        }

        void SyncPopup(Layer layer)
        {
            _popupTitle.text = layer.Visible ? layer.Name : layer.Name + " (hidden)";
            float v = Mathf.Clamp01((float)layer.Opacity);
            if (!Mathf.Approximately(_slider.value, v)) _slider.SetValueWithoutNotify(v);
            SetPercent(v);
            SyncBands(layer);
        }

        // ---------- bands section ----------

        void SyncBands(Layer layer)
        {
            LayerBands b = layer.Bands.Sanitized();
            int mode = !b.On ? 0 : b.Mode == BandMode.Interlace ? 2 : 1;
            for (int i = 0; i < _modeChips.Length; i++) SetChip(_modeChips[i], i == mode);
            SetChip(_structureChip, !b.HideStructure);
            SetChip(_innerChip, b.InnerLines);
            SetChip(_flipChip, b.FlipWeave);
            SetEnabled(_flipChip, b.On && b.Mode == BandMode.Interlace);   // dimmed (not hidden) outside Interlace
            Color outline = new Color32(b.OutlineColor.R, b.OutlineColor.G, b.OutlineColor.B, 255);
            if (_outlineSwatch.color != outline) _outlineSwatch.color = outline;
            if (!Mathf.Approximately(_bandSlider.value, (float)b.Width)) _bandSlider.SetValueWithoutNotify((float)b.Width);
            _bandWidthText.text = (b.Width * BoardSideMeters() * 1000.0).ToString("0.0") + " mm";

            LayerBands others = _safeBands;
            others.Width = b.Width;
            if (_safetyDirty || _safetyLayer != layer.Id || others != b || (!_bandDragging && _safeAtWidth != b.Width))
                ComputeSafety(layer, b);
            string note = "";
            Color color = NoteGrey;
            bool crossings = _safeCrossings > 0 && _safeAtWidth == b.Width;   // stale while dragging: recounted on release
            bool interlace = b.Mode == BandMode.Interlace;
            // Priority: red, amber width, amber conflicts, then the grey notes.
            if (b.On && (b.Width >= _safeMax || crossings)) { note = BandsTooWideNote; color = NoteRed; }
            else if (b.On && b.Width > 0.75 * _safeMax) { note = BandsWideNote; color = NoteAmber; }
            else if (b.On && interlace && _safeConflicts > 0) { note = ConflictsNote(_safeConflicts); color = NoteAmber; }
            else if (b.On && b.InnerLines && !_safeInnerShown && _safeAtWidth == b.Width) note = InnerTooNarrowNote;
            else if (b.On && HasCircles(layer)) note = CirclesNotBandedNote;
            if (_bandNote.text != note) _bandNote.text = note;
            if (_bandNote.color != color) _bandNote.color = color;
        }

        /// <summary>
        /// MaxSafeWidth (topology only) and the crossing count at the current width for the popup's warning. Done on open,
        /// on width release and after shape edits, never per slider tick.
        /// </summary>
        void ComputeSafety(Layer layer, in LayerBands b)
        {
            _safetyDirty = false;
            _safetyLayer = layer.Id;
            _safeAtWidth = b.Width;
            _safeBands = b;
            var graph = GeoART.Engine.BandGraph.Build(layer.Shapes);
            var options = GeoART.Engine.BandOutlineOptions.ForBoard(b, BoardSideMeters());   // the same formula BoardView draws with
            var outline = GeoART.Engine.BandOutline.Build(graph, options);
            _safeMax = outline.MaxSafeWidth;
            _safeCrossings = outline.SelfIntersections;
            _safeConflicts = graph.WeaveConflicts;
            _safeInnerShown = outline.InnerShown;
        }

        static bool HasCircles(Layer layer)
        {
            foreach (var sh in layer.Shapes)
                if (sh is CircleShape) return true;
            return false;
        }

        double BoardSideMeters() =>
            ServiceHub.TryResolve(out ICornerPlacement placement) && placement.BoardSideMeters > 0
                ? placement.BoardSideMeters
                : DefaultBoardSide;

        static void SetChip(Image chip, bool on)
        {
            if (chip == null) return;
            Color c = on ? Accent : ChipIdle;
            if (chip.color != c) chip.color = c;
        }

        void EditBands(System.Func<LayerBands, LayerBands> change)
        {
            if (_session == null || _popupLayer.IsNone) return;
            Layer layer = _session.Board.FindLayer(_popupLayer);
            if (layer == null) return;
            _session.SetLayerBands(_popupLayer, change(layer.Bands.Sanitized()));   // BoardChanged(Layers) -> Refresh -> SyncPopup
        }

        void OnBandMode(int mode) => EditBands(b =>
        {
            if (mode == 0) b.On = false;
            else
            {
                b.On = true;
                b.Mode = mode == 2 ? BandMode.Interlace : BandMode.Double;
            }
            return b;
        });

        void OnBandWidth(float v) => EditBands(b => { b.Width = v; return b; });
        void OnStructureToggle() => EditBands(b => { b.HideStructure = !b.HideStructure; return b; });
        void OnFlipToggle() => EditBands(b => { b.FlipWeave = !b.FlipWeave; return b; });

        /// <summary>
        /// Inner lines on/off. Turning them on raises the band to at least 10 mm on this board in the same step, so the
        /// inner lines show and stay 2 mm clear of a Medium structure line (readable at arm's length in AR).
        /// </summary>
        void OnInnerToggle() => EditBands(b =>
        {
            b.InnerLines = !b.InnerLines;
            if (b.InnerLines)
            {
                double min = GeoART.Engine.BandOutlineOptions.InnerLinesMinWidthMeters / BoardSideMeters();
                if (b.Width < min) b.Width = System.Math.Min(min, LayerBands.MaxWidth);
            }
            return b;
        });
        void OnOutlineColor() => EditBands(b => { b.OutlineColor = _session.Style.Color; return b; });

        void PlacePopup()
        {
            Row row = FindRow(_popupLayer);
            float y = row != null ? SlotCenterInArea(row.Display) : 0f;
            float limit = Mathf.Max(0f, _safeArea.height / _scale * 0.5f - PopupHeight * 0.5f - 4f);
            y = Mathf.Clamp(y, -limit, limit);
            _popup.anchoredPosition = new Vector2(EdgeInset + PillWidth + Gap, y);
        }

        void ClosePopup()
        {
            _popupLayer = EntityId.None;
            _popup.gameObject.SetActive(false);
            _blocker.SetActive(false);
        }

        void OnSlider(float v)
        {
            if (_session == null || _popupLayer.IsNone) return;
            _session.SetLayerOpacity(_popupLayer, v);
            SetPercent(v);
        }

        void SetPercent(float v)
        {
            int pct = Mathf.RoundToInt(v * 100f);
            if (_lastPercent == pct) return;
            _lastPercent = pct;
            _popupPercent.text = pct + "%";
        }

        // ---------- name label ----------

        void ShowLabel(string text, int display)
        {
            _labelText.text = text;
            _label.anchoredPosition = new Vector2(EdgeInset + PillWidth + Gap, SlotCenterInArea(display));
            _label.gameObject.SetActive(true);
            _labelGroup.alpha = 1f;
            _labelUntil = Time.unscaledTime + LabelSeconds;
        }

        // ---------- building ----------

        Row FindRow(EntityId id)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].InUse && _rows[i].Id == id) return _rows[i];
            return null;
        }

        Row TakeRow()
        {
            for (int i = 0; i < _rows.Count; i++)
                if (!_rows[i].InUse)
                {
                    _rows[i].InUse = true;
                    _rows[i].Rect.gameObject.SetActive(true);
                    return _rows[i];
                }

            var row = new Row { InUse = true };
            row.Hit = LayerUiKit.NewImage("Layer row " + _rows.Count, _rowsArea, Clear, true, 10f);
            row.Rect = row.Hit.rectTransform;
            LayerUiKit.Place(row.Rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(PillWidth - 4f, _rowH));
            var dash = LayerUiKit.NewImage("Dash", row.Rect, DashColor, false, 4f);
            row.DashImage = dash;
            row.Dash = dash.rectTransform;
            LayerUiKit.Place(row.Dash, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 4f));
            row.Hole = LayerUiKit.NewImage("Hole", row.Dash, HoleColor, false, 3f).rectTransform;
            LayerUiKit.Place(row.Hole, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15f, 3f));
            row.Hole.gameObject.SetActive(false);
            var relay = row.Rect.gameObject.AddComponent<LayerPointerRelay>();
            relay.Target = this;
            relay.Tag = _rows.Count;
            _rows.Add(row);
            return row;
        }

        RectTransform CreateButton(string name, UnityEngine.Events.UnityAction onClick)
        {
            Image bg = LayerUiKit.NewImage(name, _pill, Color.white, true, 12f);
            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock cb = button.colors;
            cb.normalColor = cb.highlightedColor = cb.selectedColor = Clear;
            cb.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            cb.disabledColor = Clear;
            cb.fadeDuration = 0.06f;
            button.colors = cb;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);
            return bg.rectTransform;
        }

        static Image Glyph(RectTransform parent, float length, float thickness, float angle)
        {
            Image bar = LayerUiKit.NewImage("Glyph", parent, new Color(1f, 1f, 1f, 0.95f), false, thickness * 0.5f);
            LayerUiKit.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(length, thickness));
            bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            return bar;
        }

        /// <summary>Round badge behind a button glyph (created before the glyph so it draws underneath).</summary>
        static Image Chip(RectTransform parent, Color color)
        {
            Image chip = LayerUiKit.NewImage("Chip", parent, color, false, ChipSize * 0.5f);
            LayerUiKit.Place(chip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(ChipSize, ChipSize));
            return chip;
        }

        /// <summary>Hairline between a button and the dash rows: edgeY 0 = the button's bottom edge, 1 = its top edge.</summary>
        static void Separator(RectTransform parent, float edgeY)
        {
            Image line = LayerUiKit.NewImage("Separator", parent, SeparatorColor, false, 0.5f);
            LayerUiKit.Place(line.rectTransform, new Vector2(0.5f, edgeY), new Vector2(0.5f, edgeY), Vector2.zero,
                new Vector2(PillWidth - 18f, 1f));
        }

        static void SetGlyphAlpha(Image glyph, float a)
        {
            Color c = glyph.color;
            if (Mathf.Approximately(c.a, a)) return;
            c.a = a;
            glyph.color = c;
        }

        void BuildLabel()
        {
            Image bg = LayerUiKit.NewImage("LayerName", _area, new Color(0.07f, 0.07f, 0.09f, 0.72f), false, 11f);
            _label = bg.rectTransform;
            _labelGroup = bg.gameObject.AddComponent<CanvasGroup>();
            _labelGroup.blocksRaycasts = false;
            _labelGroup.interactable = false;
            var fitter = bg.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 4, 4);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            LayerUiKit.Place(_label, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(100f, 24f));
            _labelText = LayerUiKit.NewText("Text", _label, 14, TextAnchor.MiddleLeft, Color.white);
            _label.gameObject.SetActive(false);
        }

        void BuildPopup()
        {
            // Full-screen catcher under the popup: a touch anywhere else closes it (and never reaches the board).
            // It lives in the safe-area rect (so it sorts between the sidebar and the popup) but overhangs it to cover
            // the whole screen.
            Image blocker = LayerUiKit.NewImage("PopupBlocker", _area, Clear, true, 1f);
            blocker.sprite = null;
            blocker.type = Image.Type.Simple;
            RectTransform brt = blocker.rectTransform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(-4000f, -4000f);
            brt.offsetMax = new Vector2(4000f, 4000f);
            var relay = blocker.gameObject.AddComponent<LayerPointerRelay>();
            relay.Target = this;
            relay.Tag = BlockerTag;
            _blocker = blocker.gameObject;
            _blocker.SetActive(false);

            Image panel = LayerUiKit.NewImage("OpacityPopup", _area, new Color(0.07f, 0.07f, 0.09f, 0.88f), true, 14f);
            _popup = panel.rectTransform;
            LayerUiKit.Place(_popup, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(PopupWidth, PopupHeight));

            _popupTitle = LayerUiKit.NewText("Title", _popup, 14, TextAnchor.MiddleLeft, Color.white);
            _popupTitle.fontStyle = FontStyle.Bold;
            _popupTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            LayerUiKit.Place(_popupTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -8f),
                new Vector2(PopupWidth - 56f, 26f));

            var caption = LayerUiKit.NewText("Caption", _popup, 11, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));
            caption.text = "Opacity";
            LayerUiKit.Place(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -32f),
                new Vector2(80f, 16f));

            Image close = LayerUiKit.NewImage("Close", _popup, Color.white, true, 12f);
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close;
            ColorBlock cb = closeButton.colors;
            cb.normalColor = cb.highlightedColor = cb.selectedColor = Clear;
            cb.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            closeButton.colors = cb;
            closeButton.navigation = new Navigation { mode = Navigation.Mode.None };
            closeButton.onClick.AddListener(ClosePopup);
            LayerUiKit.Place(close.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -4f),
                new Vector2(38f, 38f));
            Glyph(close.rectTransform, 15f, 2.5f, 45f);
            Glyph(close.rectTransform, 15f, 2.5f, -45f);

            _slider = BuildSlider(_popup, "OpacitySlider", new Vector2(6f, -46f), PopupWidth - 70f, 0f, 1f);
            _slider.SetValueWithoutNotify(1f);
            _slider.onValueChanged.AddListener(OnSlider);

            _popupPercent = LayerUiKit.NewText("Percent", _popup, 15, TextAnchor.MiddleRight, Color.white);
            LayerUiKit.Place(_popupPercent.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -46f),
                new Vector2(46f, 36f));

            BuildBandsSection();
            _popup.gameObject.SetActive(false);
        }

        void BuildBandsSection()
        {
            const float w = PopupWidth - 24f;
            float y = BandRowY;
            Caption("BandsCaption", "Bands", y);
            string[] modes = { "Off", "Double", "Interlace" };
            for (int i = 0; i < modes.Length; i++)
            {
                int mode = i;
                _modeChips[i] = ChipButton("Bands " + modes[i], modes[i], new Vector2(12f + i * w / 3f, y - 18f),
                    new Vector2(w / 3f - 4f, 30f), () => OnBandMode(mode));
            }

            y -= 58f;
            Caption("WidthCaption", "Width", y);
            _bandWidthText = LayerUiKit.NewText("WidthValue", _popup, 13, TextAnchor.MiddleRight, Color.white);
            LayerUiKit.Place(_bandWidthText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, y),
                new Vector2(90f, 16f));
            _bandSlider = BuildSlider(_popup, "BandWidthSlider", new Vector2(6f, y - 16f), PopupWidth - 12f,
                (float)LayerBands.MinWidth, (float)LayerBands.MaxWidth);
            _bandSlider.SetValueWithoutNotify((float)LayerBands.DefaultWidth);
            _bandSlider.onValueChanged.AddListener(OnBandWidth);
            var relay = _bandSlider.gameObject.AddComponent<LayerPointerRelay>();   // release: count the crossings once
            relay.Target = this;
            relay.Tag = WidthSliderTag;

            y -= 58f;
            _structureChip = ChipButton("Structure line", "Structure line", new Vector2(12f, y), new Vector2(w * 0.6f, 30f),
                OnStructureToggle);
            Image outline = ChipButton("Outline colour", "Outline", new Vector2(12f + w * 0.62f, y), new Vector2(w * 0.38f, 30f),
                OnOutlineColor);
            outline.color = ChipIdle;
            _outlineSwatch = LayerUiKit.NewImage("Swatch", outline.rectTransform, Color.black, false, 8f);
            LayerUiKit.Place(_outlineSwatch.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f),
                new Vector2(16f, 16f));

            y -= 36f;
            _innerChip = ChipButton("Inner lines", "Inner lines", new Vector2(12f, y), new Vector2(w * 0.49f, 30f), OnInnerToggle);
            _flipChip = ChipButton("Flip weave", "Flip weave", new Vector2(12f + w * 0.51f, y), new Vector2(w * 0.49f, 30f), OnFlipToggle);

            y -= 38f;
            _bandNote = LayerUiKit.NewText("BandNote", _popup, 11, TextAnchor.UpperLeft, NoteGrey);
            _bandNote.horizontalOverflow = HorizontalWrapMode.Wrap;
            LayerUiKit.Place(_bandNote.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, y),
                new Vector2(w, 32f));
        }

        void Caption(string name, string text, float y)
        {
            var caption = LayerUiKit.NewText(name, _popup, 11, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));
            caption.text = text;
            LayerUiKit.Place(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, y),
                new Vector2(120f, 16f));
        }

        /// <summary>A rounded chip button (orange when on, see <see cref="SetChip"/>) with a 12 pt label.</summary>
        Image ChipButton(string name, string label, Vector2 topLeft, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            Image bg = LayerUiKit.NewImage(name, _popup, ChipIdle, true, 9f);
            LayerUiKit.Place(bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), topLeft, size);
            var button = bg.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(onClick);
            Text t = LayerUiKit.NewText("Label", bg.rectTransform, 12, TextAnchor.MiddleCenter, Color.white);
            t.text = label;
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            return bg;
        }

        static void SetEnabled(Image chip, bool on)
        {
            chip.GetComponent<Button>().interactable = on;
            var group = chip.GetComponent<CanvasGroup>();
            if (group == null) group = chip.gameObject.AddComponent<CanvasGroup>();   // Unity null: no ??
            group.alpha = on ? 1f : 0.35f;
        }

        static Slider BuildSlider(RectTransform parent, string name, Vector2 topLeft, float width, float min, float max)
        {
            // The slider's own Image is a transparent, finger-sized hit area around the thin track.
            Image hit = LayerUiKit.NewImage(name, parent, Clear, true, 1f);
            RectTransform rt = hit.rectTransform;
            LayerUiKit.Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), topLeft, new Vector2(width, 36f));

            Image track = LayerUiKit.NewImage("Track", rt, new Color(1f, 1f, 1f, 0.25f), false, 3f);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(-24f, 6f);

            RectTransform fillArea = LayerUiKit.NewRect("Fill Area", rt);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(-24f, 6f);
            Image fill = LayerUiKit.NewImage("Fill", fillArea, Accent, false, 3f);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.sizeDelta = Vector2.zero;

            RectTransform handleArea = LayerUiKit.NewRect("Handle Slide Area", rt);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.sizeDelta = new Vector2(-24f, 0f);
            Image handle = LayerUiKit.NewImage("Handle", handleArea, Color.white, false, 12f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(24f, 24f);

            var slider = hit.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            return slider;
        }

        /// <summary>Area = Screen.safeArea; the pill hugs its left edge, vertically centred.</summary>
        void ApplySafeArea()
        {
            _safeArea = Screen.safeArea;
            _screenW = Screen.width;
            _screenH = Screen.height;
            _scale = LayerUiKit.ScaleFactor();
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            _area.anchorMin = new Vector2(_safeArea.xMin / w, _safeArea.yMin / h);
            _area.anchorMax = new Vector2(_safeArea.xMax / w, _safeArea.yMax / h);
            _area.offsetMin = _area.offsetMax = Vector2.zero;
        }
    }
}
