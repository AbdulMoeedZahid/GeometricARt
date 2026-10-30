using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GeoART.App
{
    /// <summary>
    /// Routes one-finger touches (Mouse in the Editor) on the placed board to <see cref="IEditorSession.Pointer"/>:
    /// Down, Move every frame while held (the camera moves even when the finger does not), Up on release.
    ///   - A press that starts on uGUI, while the board is not placed, in the frame the board got placed, or off the
    ///     board is ignored until it is released.
    ///   - A drag that leaves the board is clamped to the board edge (bisection along the screen path from the last
    ///     on-board position); if even that cannot be projected (tracking lost), the last valid point is kept.
    ///   - Cancel on a second finger, on any placement change away from Placed (re-place, tracking lost), on pause or
    ///     focus loss, and when disabled. After a cancel the press is ignored until every finger is up.
    ///   - Board points are smoothed with a time-based <see cref="OneEuroFilter2D"/> (AR tracking makes the projected
    ///     finger point jitter); the Down point is raw, Move/Up are filtered.
    /// No per-frame allocations.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardInputRouter : MonoBehaviour
    {
        [Header("Smoothing (One Euro, board units: 1 = board side)")]
        [SerializeField, Tooltip("Cutoff at rest (Hz). Lower = steadier, more lag when the finger stops.")]
        float minCutoffHz = 1.2f;
        [SerializeField, Tooltip("Cutoff gain per board-side/s of speed. Higher = less lag while dragging.")]
        float beta = 5f;
        [SerializeField, Tooltip("Cutoff of the speed estimate (Hz).")]
        float derivativeCutoffHz = 1f;

        const int EdgeBisectSteps = 12;

        enum Source { None, Touch, Mouse }

        static readonly List<RaycastResult> s_UiHits = new List<RaycastResult>();

        IBoardSpace _boardSpace;
        IEditorSession _session;
        OneEuroFilter2D _filter;
        Source _source;            // device that owns the current press (None = no press)
        bool _gestureOpen;         // Down was sent and neither Up nor Cancel yet
        bool _wasPlaced;
        Vector2 _lastOnBoardScreen;
        Vec2 _lastSent;

        public bool GestureOpen => _gestureOpen;

        void Awake() => _filter = new OneEuroFilter2D(minCutoffHz, beta, derivativeCutoffHz);

        void Start()
        {
            ServiceHub.TryResolve(out _boardSpace);
            ServiceHub.TryResolve(out _session);
            if (_boardSpace != null) _boardSpace.StateChanged += OnPlacementChanged;
            _wasPlaced = BoardPlaced;
        }

        void OnDestroy()
        {
            if (_boardSpace != null) _boardSpace.StateChanged -= OnPlacementChanged;
        }

        void OnDisable() => CancelGesture();
        void OnApplicationPause(bool paused) { if (paused) CancelGesture(); }
        void OnApplicationFocus(bool focused) { if (!focused) CancelGesture(); }

        void OnPlacementChanged(PlacementState state)
        {
            if (state != PlacementState.Placed) CancelGesture();
        }

        bool BoardPlaced => _boardSpace != null && _boardSpace.IsPlaced;

        void Update()
        {
            bool placedNow = BoardPlaced;
            if (_session != null) Poll(placedNow);
            _wasPlaced = placedNow;
        }

        void Poll(bool placedNow)
        {
            var touchscreen = Touchscreen.current;
            var mouse = Mouse.current;

            if (_source == Source.None)
            {
                if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
                    Begin(Source.Touch, touchscreen.primaryTouch.position.ReadValue(), placedNow, FingerCount(touchscreen));
                else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                    Begin(Source.Mouse, mouse.position.ReadValue(), placedNow, 1);
                return;
            }

            bool held;
            Vector2 position;
            if (_source == Source.Touch)
            {
                int fingers = touchscreen != null ? FingerCount(touchscreen) : 0;
                if (fingers > 1) CancelGesture();
                held = fingers > 0;
                position = touchscreen != null ? touchscreen.primaryTouch.position.ReadValue() : default;
            }
            else
            {
                held = mouse != null && mouse.leftButton.isPressed;
                position = mouse != null ? mouse.position.ReadValue() : default;
            }

            if (!_gestureOpen)
            {
                if (!held) _source = Source.None;
                return;
            }
            Track(position);
            if (held) return;

            _gestureOpen = false;
            _source = Source.None;
            _session.Pointer(new PointerEvent(PointerPhase.Up, _lastSent, true, Time.unscaledTimeAsDouble));
        }

        void Begin(Source source, Vector2 screen, bool placedNow, int fingers)
        {
            _source = source;   // owns the press until release, even when ignored
            if (!placedNow || !_wasPlaced || fingers > 1 || IsOverUI(screen)) return;
            if (!_boardSpace.TryScreenToBoard(new Vec2(screen.x, screen.y), out Vec2 board)) return;

            double t = Time.unscaledTimeAsDouble;
            _filter.MinCutoff = minCutoffHz;
            _filter.Beta = beta;
            _filter.DerivativeCutoff = derivativeCutoffHz;
            _filter.Reset();
            _lastSent = _filter.Filter(board, t);
            _lastOnBoardScreen = screen;
            _gestureOpen = true;
            _session.Pointer(new PointerEvent(PointerPhase.Down, _lastSent, true, t));
        }

        void Track(Vector2 screen)
        {
            if (!TryProjectClamped(screen, out Vec2 board)) return;   // keep the last valid point
            double t = Time.unscaledTimeAsDouble;
            Vec2 f = _filter.Filter(board, t);
            f = new Vec2(Clamp01(f.X), Clamp01(f.Y));
            if (f == _lastSent) return;
            _lastSent = f;
            _session.Pointer(new PointerEvent(PointerPhase.Move, f, true, t));
        }

        /// <summary>On the board: the projection. Off it: the board-edge point on the screen path from the last on-board
        /// position (re-projected this frame, so camera motion is respected). False if neither projects.</summary>
        bool TryProjectClamped(Vector2 screen, out Vec2 board)
        {
            if (_boardSpace.TryScreenToBoard(new Vec2(screen.x, screen.y), out board))
            {
                _lastOnBoardScreen = screen;
                return true;
            }
            Vector2 inside = _lastOnBoardScreen, outside = screen;
            if (!_boardSpace.TryScreenToBoard(new Vec2(inside.x, inside.y), out board)) return false;
            for (int i = 0; i < EdgeBisectSteps; i++)
            {
                Vector2 mid = (inside + outside) * 0.5f;
                if (_boardSpace.TryScreenToBoard(new Vec2(mid.x, mid.y), out Vec2 b)) { inside = mid; board = b; }
                else outside = mid;
            }
            return true;
        }

        /// <summary>Aborts the open gesture; the press stays owned (ignored) until released.</summary>
        void CancelGesture()
        {
            if (!_gestureOpen) return;
            _gestureOpen = false;
            _session?.Pointer(new PointerEvent(PointerPhase.Cancel, _lastSent, false, Time.unscaledTimeAsDouble));
        }

        static int FingerCount(Touchscreen touchscreen)
        {
            var touches = touchscreen.touches;
            int n = 0;
            for (int i = 0; i < touches.Count; i++)
                if (touches[i].press.isPressed) n++;
            return n;
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        static bool IsOverUI(Vector2 position)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            s_UiHits.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = position }, s_UiHits);
            return s_UiHits.Count > 0;
        }
    }
}
