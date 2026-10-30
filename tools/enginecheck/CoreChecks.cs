using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    public static class CoreChecks
    {
        public static void NewSessionHasOneActiveLayer()
        {
            var s = new EditorSession();
            Check.That(s.Board.Layers.Count == 1, "one layer");
            Check.That(s.ActiveLayer != null && s.ActiveLayer.Id == s.Board.ActiveLayerId, "active layer resolves");
        }

        public static void LoadEmptyBoardCreatesLayer()
        {
            var s = new EditorSession();
            s.LoadBoard(new Board());
            Check.That(s.Board.Layers.Count == 1 && s.ActiveLayer != null, "LoadBoard guarantees a layer");
        }
    }
}
