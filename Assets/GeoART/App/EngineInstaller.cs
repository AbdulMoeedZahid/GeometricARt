using GeoART.Contracts;
using GeoART.Engine;
using UnityEngine;

namespace GeoART.App
{
    /// <summary>
    /// Registers the real <see cref="EditorSession"/> as the scene's IEditorSession. Runs with the module installers
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
            _session = new EditorSession(board);
            ServiceHub.Register<IEditorSession>(_session);
        }

        void OnDestroy()
        {
            if (_session != null) ServiceHub.Unregister<IEditorSession>(_session);
        }
    }
}
