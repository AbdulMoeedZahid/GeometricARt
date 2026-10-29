using GeoART.Contracts;
using UnityEngine;

namespace GeoART.Mocks
{
    /// <summary>
    /// Registers a <see cref="StubEditorSession"/> only when no real IEditorSession was registered.
    /// Runs after the module installers (-100), so B's EngineHost always wins. Remove from Main.unity once
    /// EngineHost is composed there.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class StubSessionInstaller : MonoBehaviour
    {
        public enum Fixture { Empty, StarOnFullBoard, Tiled2x2 }

        [SerializeField] Fixture fixture = Fixture.Empty;

        StubEditorSession _session;

        void Awake()
        {
            if (ServiceHub.IsRegistered<IEditorSession>()) return;

            Board board;
            switch (fixture)
            {
                case Fixture.StarOnFullBoard: board = GoldenBoards.StarOnFullBoard(); break;
                case Fixture.Tiled2x2: board = GoldenBoards.Tiled2x2(); break;
                default: board = GoldenBoards.Empty(); break;
            }
            _session = new StubEditorSession(board);
            ServiceHub.Register<IEditorSession>(_session);
            Debug.Log($"[GeoART] Using StubEditorSession ({fixture}); no real IEditorSession in this scene.", this);
        }

        void OnDestroy()
        {
            if (_session != null) ServiceHub.Unregister<IEditorSession>(_session);
        }
    }
}
