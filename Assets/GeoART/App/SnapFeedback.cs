using GeoART.Contracts;
using GeoART.Engine;
using UnityEngine;

namespace GeoART.App
{
    /// <summary>
    /// Plays a haptic tick (<see cref="Haptics.Tick"/>, throttled to one per 60 ms) whenever the engine acquires a new snap
    /// candidate (<see cref="EditorSession.SnapAcquired"/>: a snapped Down, or a different dot during a drag). The visual
    /// side (the hover ring pop and the filled dot) is in BoardView. Added at runtime by <see cref="EngineInstaller"/>, so
    /// the scenes need no change. Event-driven: no per-frame work.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnapFeedback : MonoBehaviour
    {
        EditorSession _session;

        void Start()
        {
            if (ServiceHub.TryResolve(out IEditorSession session)) _session = session as EditorSession;
            if (_session != null) _session.SnapAcquired += OnSnapAcquired;
        }

        void OnDestroy()
        {
            if (_session != null) _session.SnapAcquired -= OnSnapAcquired;
            _session = null;
        }

        static void OnSnapAcquired(SnapCandidate _) => Haptics.Tick();
    }
}
