using UnityEngine;
using UnityEngine.EventSystems;

namespace GeoART.App
{
    /// <summary>Receiver for <see cref="LayerPointerRelay"/> events; <c>tag</c> identifies the sending element.</summary>
    interface ILayerPointerTarget
    {
        void OnRelayDown(int tag, PointerEventData e);
        void OnRelayDrag(int tag, PointerEventData e);
        void OnRelayUp(int tag, PointerEventData e);
    }

    /// <summary>
    /// Forwards EventSystem pointer events from a raycast-target Image to its owner (the layer sidebar). Turns off the
    /// EventSystem drag threshold so the owner sees every move and applies its own (reference-unit) thresholds.
    /// </summary>
    [DisallowMultipleComponent]
    sealed class LayerPointerRelay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public ILayerPointerTarget Target;
        public int Tag;

        public void OnPointerDown(PointerEventData e) => Target?.OnRelayDown(Tag, e);
        public void OnPointerUp(PointerEventData e) => Target?.OnRelayUp(Tag, e);
        public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e) => Target?.OnRelayDrag(Tag, e);
        public void OnEndDrag(PointerEventData e) { }
    }
}
