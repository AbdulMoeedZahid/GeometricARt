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
    /// top) and "-" (delete the bottom layer). On a dash:
    ///   tap          select the layer (immediately on release)
    ///   double tap   toggle its visibility (second tap within <see cref="DoubleTapSeconds"/> of the first release)
    ///   hold         select + open the opacity popup next to the dash (<see cref="HoldSeconds"/> without moving)
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
        const float PopupWidth = 204f, PopupHeight = 96f, Gap = 8f;

        const int BlockerTag = -1;

        static readonly Color PillColor = new Color(0.07f, 0.07f, 0.09f, 0.62f);
        static readonly Color HoleColor = new Color(0.1f, 0.1f, 0.12f, 1f);
        static readonly Color DashColor = new Color(0.93f, 0.93f, 0.96f, 1f);
        static readonly Color Accent = new Color(1f, 0.42f, 0f, 1f);
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

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
        Image _plusGlyphH, _plusGlyphV, _minusGlyph;
        GameObject _blocker;
        CanvasGroup _labelGroup;
        Text _labelText, _popupTitle, _popupPercent;
        Slider _slider;
        readonly List<Row> _rows = new List<Row>();          // pool; index = relay tag
        Rect _safeArea;
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
            _plusGlyphH = Glyph(plus, 18f, 3f, 0f);
            _plusGlyphV = Glyph(plus, 18f, 3f, 90f);

            var minus = CreateButton("Delete bottom layer (-)", OnMinus);
            LayerUiKit.Place(minus, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, Pad),
                new Vector2(PillWidth - 2 * Pad, ButtonHeight));
            _minusGlyph = Glyph(minus, 18f, 3f, 0f);

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
            if ((kind & (BoardChangeKind.Layers | BoardChangeKind.Reset)) != 0) Refresh();
        }

        void Update()
        {
            if (Screen.safeArea != _safeArea) { ApplySafeArea(); Layout(); }
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
            if (tag == BlockerTag) { ClosePopup(); return; }
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

        void OpenPopup(Row row, Layer layer)
        {
            _popupLayer = layer.Id;
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
        }

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

            _slider = BuildSlider(_popup);
            _slider.onValueChanged.AddListener(OnSlider);

            _popupPercent = LayerUiKit.NewText("Percent", _popup, 15, TextAnchor.MiddleRight, Color.white);
            LayerUiKit.Place(_popupPercent.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-12f, 10f),
                new Vector2(46f, 36f));

            _popup.gameObject.SetActive(false);
        }

        static Slider BuildSlider(RectTransform parent)
        {
            // The slider's own Image is a transparent, finger-sized hit area around the thin track.
            Image hit = LayerUiKit.NewImage("OpacitySlider", parent, Clear, true, 1f);
            RectTransform rt = hit.rectTransform;
            LayerUiKit.Place(rt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(6f, 10f),
                new Vector2(PopupWidth - 70f, 36f));

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
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.SetValueWithoutNotify(1f);
            return slider;
        }

        /// <summary>Area = Screen.safeArea; the pill hugs its left edge, vertically centred.</summary>
        void ApplySafeArea()
        {
            _safeArea = Screen.safeArea;
            _scale = LayerUiKit.ScaleFactor();
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            _area.anchorMin = new Vector2(_safeArea.xMin / w, _safeArea.yMin / h);
            _area.anchorMax = new Vector2(_safeArea.xMax / w, _safeArea.yMax / h);
            _area.offsetMin = _area.offsetMax = Vector2.zero;
        }
    }
}
