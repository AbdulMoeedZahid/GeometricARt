using GeoART.Contracts;
using GeoART.Engine;
using UnityEngine;

namespace GeoART.App
{
    /// <summary>
    /// Registers the real <see cref="EditorSession"/> as the scene's IEditorSession and gives it a
    /// <see cref="ScreenSnapMetric"/> for screen-space snapping, and adds <see cref="SnapFeedback"/> (haptic tick on each
    /// snap). Runs with the module installers
    /// (-100), before StubSessionInstaller (-50), so the stub then stays out of the way.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class EngineInstaller : MonoBehaviour
    {
        public enum Fixture { Empty, StarOnFullBoard, Tiled2x2 }

        [SerializeField] Fixture fixture = Fixture.Empty;

        EditorSession _session;

        public EditorSession Session => _session;

        void Awake()
        {
            Board board;
            switch (fixture)
            {
                case Fixture.StarOnFullBoard: board = GoldenBoards.StarOnFullBoard(); break;
                case Fixture.Tiled2x2: board = GoldenBoards.Tiled2x2(); break;
                default: board = GoldenBoards.Empty(); break;
            }
            // Screen-space snap/tap tolerances. The metric resolves IBoardSpace lazily, so Awake order does not matter.
            _session = new EditorSession(board) { SnapMetric = new ScreenSnapMetric() };
            ServiceHub.Register<IEditorSession>(_session);
            // Haptic tick on each snap acquisition; added here (not in the scenes) so no scene has to be regenerated.
            if (GetComponent<SnapFeedback>() == null) gameObject.AddComponent<SnapFeedback>();
        }

        void OnDestroy()
        {
            if (_session != null) ServiceHub.Unregister<IEditorSession>(_session);
        }
    }
}
