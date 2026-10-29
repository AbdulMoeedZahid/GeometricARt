using System.Collections.Generic;
using GeoART.Contracts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace GeoART.ARBoard
{
    /// <summary>
    /// Temporary placement input for A's scenes. While the board is not placed, a tap that does not start on UI:
    ///   QuickPlace mode (or no <see cref="ICornerPlacement"/>) -> <see cref="IBoardSpace.PlaceAtReticle"/>
    ///   Corners mode -> <see cref="ICornerPlacement.ConfirmCorners"/> once the fit is valid, else TryAddCorner().
    /// The tap position itself is ignored: the screen-centre crosshair defines the point, so the finger never covers
    /// the corner being picked. Taps on uGUI (the temporary PlacementControls buttons) are ignored. It does nothing once
    /// placed, so later taps can never create a second board (P2-02). D's input router may take over placement; then
    /// disable this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARPlacementTapHandler : MonoBehaviour
    {
        static readonly List<RaycastResult> s_UiHits = new List<RaycastResult>();
        IBoardSpace _boardSpace;
        ICornerPlacement _corners;

        void Start()
        {
            ServiceHub.TryResolve(out _boardSpace);
            ServiceHub.TryResolve(out _corners);   // optional
        }

        void Update()
        {
            if (_boardSpace == null || _boardSpace.IsPlaced) return;
            PlacementState state = _boardSpace.State;
            if (state != PlacementState.PlaneFound && state != PlacementState.PickingCorners) return;
            if (!TryGetPressThisFrame(out Vector2 position) || IsOverUI(position)) return;

            if (_corners == null || _corners.Mode == PlacementMode.QuickPlace)
            {
                if (state == PlacementState.PlaneFound) _boardSpace.PlaceAtReticle();
            }
            else if (_corners.CanConfirm) _corners.ConfirmCorners();
            else if (_corners.CornerCount < _corners.RequiredCorners) _corners.TryAddCorner();
        }

        static bool TryGetPressThisFrame(out Vector2 position)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                position = touchscreen.primaryTouch.position.ReadValue();
                return true;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return true;
            }
            position = default;
            return false;
        }

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
