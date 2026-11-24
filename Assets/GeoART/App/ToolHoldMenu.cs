using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GeoART.App
{
    /// <summary>
    /// Hold-to-pick menu for a toolbar button: added NEXT TO the button's uGUI <see cref="Button"/> (which still handles
    /// taps, so view tests keep clicking through IPointerClickHandler). The owner (ToolBar) builds the visuals and hands
    /// them over in <see cref="Configure"/>; this component only runs the gesture:
    ///   tap          nothing here: the Button's onClick runs as before
    ///   hold         <see cref="HoldSeconds"/> without moving more than <see cref="Slop"/> reference px: the button
    ///                "charges" (a white overlay fades in), then the hold fires: onHold runs, the chip row opens next to
    ///                the button and <see cref="Opened"/> is raised. Moving past the slop first cancels the hold.
    ///   slide        with the finger still down, the chip under it is highlighted; release on a chip picks it and closes
    ///                the row. Release anywhere else leaves the row open: tap a chip to pick, or tap outside (a
    ///                full-screen transparent blocker under the chips) to close. The blocker closes on the release of
    ///                that touch, so the whole touch stays on UI and never reaches the board.
    /// After a hold the Button's click on release must be ignored: the owner's onClick checks
    /// <see cref="SuppressClick"/> (true from the hold until the next press). The press uses its own thresholds, so the
    /// EventSystem drag threshold is turned off (as in LayerPointerRelay) and every move is seen.
    /// A haptic tick (<see cref="Haptics.Tick"/>) plays when the row opens.
    /// Time comes from <see cref="Clock"/> (Time.unscaledTime; EditMode tests swap in a fake clock). Update only runs the
    /// hold timer while a press is down.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolHoldMenu : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, ILayerPointerTarget
    {
        /// <summary>Hold time before the menu opens (same as the layer sidebar's hold).</summary>
        public const float HoldSeconds = 0.45f;
        /// <summary>Movement (reference px) that cancels a hold before it fires.</summary>
        public const float Slop = 8f;
        /// <summary>Gap (reference px) between the chip row and the button.</summary>
        public const float RowGap = 10f;

        static readonly Color Hover = new Color(1f, 0.62f, 0.3f, 0.95f);

        /// <summary>Seconds, monotonic. Defaults to Time.unscaledTime.</summary>
        public Func<float> Clock = () => Time.unscaledTime;

        /// <summary>Raised when a hold fires and the row opens (haptic tick hook).</summary>
        public event Action Opened;

        RectTransform _button, _row;
        Image _charge;
        Image[] _chips;
        GameObject _blocker;
        Func<int> _getCurrent;
        Action<int> _onPick;
        Action _onHold;
        Color _accent, _idle;

        bool _configured, _pressing, _holdFired, _cancelled, _suppressClick;
        int _pointer, _hover = -1;
        Vector2 _pressPos;
        float _pressTime;

        /// <summary>True while the chip row is shown.</summary>
        public bool IsOpen => _row != null && _row.gameObject.activeSelf;

        /// <summary>
        /// True from the moment a hold fires until the next press on the button: the Button's onClick must then do nothing
        /// (the release of a hold is not a tap).
        /// </summary>
        public bool SuppressClick => _suppressClick;

        /// <summary>
        /// Wires the visuals. <paramref name="charge"/> is a non-raycast overlay on the button; <paramref name="row"/>
        /// holds the <paramref name="chips"/> (Buttons; a click on chip i picks option i) and starts hidden;
        /// <paramref name="blocker"/> is a full-screen raycast Image under the row with a <see cref="LayerPointerRelay"/>.
        /// <paramref name="getCurrent"/> returns the selected option (filled with <paramref name="accent"/>),
        /// <paramref name="onPick"/> applies a pick and <paramref name="onHold"/> runs when a hold fires.
        /// </summary>
        public void Configure(Image charge, RectTransform row, Image[] chips, GameObject blocker, Func<int> getCurrent,
            Action<int> onPick, Action onHold, Color accent, Color idle)
        {
            _button = (RectTransform)transform;
            _charge = charge;
            _row = row;
            _chips = chips;
            _blocker = blocker;
            _getCurrent = getCurrent;
            _onPick = onPick;
            _onHold = onHold;
            _accent = accent;
            _idle = idle;
            for (int i = 0; i < chips.Length; i++)
            {
                int index = i;
                chips[i].GetComponent<Button>().onClick.AddListener(() => Pick(index));
            }
            var relay = blocker.GetComponent<LayerPointerRelay>();
            if (relay == null) relay = blocker.AddComponent<LayerPointerRelay>();
            relay.Target = this;
            _configured = true;
            SetCharge(0f);
            Close();
        }

        /// <summary>
        /// Action-only hold (no chip row): the same charge, slop and click suppression, then <paramref name="onHold"/>
        /// runs (e.g. the Bands button opens the layer popup). <see cref="Opened"/> is still raised.
        /// </summary>
        public void Configure(Image charge, Action onHold)
        {
            _button = (RectTransform)transform;
            _charge = charge;
            _chips = Array.Empty<Image>();
            _onHold = onHold;
            _configured = true;
            SetCharge(0f);
            Close();
        }

        void Update()
        {
            if (!_pressing || _holdFired || _cancelled) return;
            float t = (Clock() - _pressTime) / HoldSeconds;
            if (t >= 1f) FireHold();
            else SetCharge(Mathf.Lerp(0.06f, 0.22f, t));   // hold "charging"
        }

        void OnDisable() => Close();

        void OnApplicationPause(bool paused)
        {
            if (paused) Close();
        }

        // ---------- the press on the button ----------

        public void OnPointerDown(PointerEventData e)
        {
            if (!_configured || IsOpen) return;
            // A new press always replaces the old one, so a lost PointerUp (system gesture, touch cancel) never wedges it.
            _pressing = true;
            _pointer = e.pointerId;
            _pressPos = e.position;
            _pressTime = Clock();
            _holdFired = _cancelled = _suppressClick = false;
            SetCharge(0.06f);
        }

        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;
        public void OnBeginDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (!_pressing || e.pointerId != _pointer) return;
            if (_holdFired)
            {
                SetHover(ChipAt(e.position));
                return;
            }
            if (_cancelled) return;
            float scale = ScreenSnapMetric.ScaleFactor(Screen.width, Screen.height);
            if (((e.position - _pressPos) / scale).sqrMagnitude >= Slop * Slop)
            {
                _cancelled = true;   // a moving finger is not a hold; the release may still be a tap
                SetCharge(0f);
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_pressing || e.pointerId != _pointer) return;
            bool fired = _holdFired;
            _pressing = false;
            SetCharge(0f);
            if (!fired) return;
            int chip = ChipAt(e.position);
            if (chip >= 0) Pick(chip);
            else SetHover(-1);   // released off the chips: the row stays open for a tap
        }

        void FireHold()
        {
            _holdFired = true;
            _suppressClick = true;
            SetCharge(0f);
            _onHold?.Invoke();
            if (_row != null) Open();
            Haptics.Tick();   // the menu opened under the finger
            Opened?.Invoke();
        }

        // ---------- the row ----------

        /// <summary>Shows the row left of the button, vertically centred on it, at the button's on-screen scale.</summary>
        void Open()
        {
            var parent = (RectTransform)_row.parent;
            var c = new Vector3[4];
            _button.GetWorldCorners(c);
            Vector2 screenLeftMid = new Vector2(c[0].x, (c[0].y + c[1].y) * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenLeftMid, null, out Vector2 local);
            // The column may be shrunk to fit: match the button's scale relative to the row's parent.
            float parentScale = Mathf.Max(1e-6f, parent.lossyScale.y);
            float fit = _button.rect.height > 0f ? (c[1].y - c[0].y) / (_button.rect.height * parentScale) : 1f;
            _row.anchorMin = _row.anchorMax = new Vector2(0.5f, 0.5f);
            _row.pivot = new Vector2(1f, 0.5f);
            _row.anchoredPosition = local - parent.rect.center - new Vector2(RowGap * fit, 0f);   // offset from the centre anchor
            _row.localScale = new Vector3(fit, fit, 1f);
            _hover = -1;
            RefreshChips();
            _blocker.SetActive(true);
            _row.gameObject.SetActive(true);
        }

        /// <summary>Hides the row and blocker and drops any press in progress.</summary>
        public void Close()
        {
            _pressing = _holdFired = _cancelled = false;
            _hover = -1;
            SetCharge(0f);
            if (_row != null && _row.gameObject.activeSelf) _row.gameObject.SetActive(false);
            if (_blocker != null && _blocker.activeSelf) _blocker.SetActive(false);
        }

        void Pick(int index)
        {
            Close();
            _onPick?.Invoke(index);
        }

        int ChipAt(Vector2 screen)
        {
            if (!IsOpen) return -1;
            for (int i = 0; i < _chips.Length; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(_chips[i].rectTransform, screen, null)) return i;
            return -1;
        }

        void SetHover(int index)
        {
            if (_hover == index) return;
            _hover = index;
            RefreshChips();
        }

        void RefreshChips()
        {
            int current = _getCurrent != null ? _getCurrent() : -1;
            for (int i = 0; i < _chips.Length; i++)
            {
                Color c = i == _hover ? Hover : i == current ? _accent : _idle;
                if (_chips[i].color != c) _chips[i].color = c;
                float s = i == _hover ? 1.1f : 1f;
                _chips[i].rectTransform.localScale = new Vector3(s, s, 1f);
            }
        }

        void SetCharge(float alpha)
        {
            if (_charge == null) return;
            var c = new Color(1f, 1f, 1f, alpha);
            if (_charge.color != c) _charge.color = c;
        }

        // ---------- blocker (LayerPointerRelay) ----------

        // Close on the blocker's RELEASE, not its press: the blocker must stay up for the whole touch, because
        // BoardInputRouter decides at touch-begin (after the EventSystem ran) whether the touch is over UI. Closing on
        // the press let the first board touch both close the row and draw a line.
        void ILayerPointerTarget.OnRelayDown(int tag, PointerEventData e) { }
        void ILayerPointerTarget.OnRelayDrag(int tag, PointerEventData e) { }
        void ILayerPointerTarget.OnRelayUp(int tag, PointerEventData e) => Close();
    }
}
