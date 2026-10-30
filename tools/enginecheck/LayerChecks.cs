using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>Layer commands of EditorSession (EditorSession.Layers.cs).</summary>
    public static class LayerChecks
    {
        sealed class Recorder
        {
            public readonly List<BoardChangeKind> Board = new List<BoardChangeKind>();
            public readonly List<string> Toasts = new List<string>();
            public int State;

            public Recorder(EditorSession s)
            {
                s.BoardChanged += k => Board.Add(k);
                s.StateChanged += () => State++;
                s.Toast += m => Toasts.Add(m);
            }

            public bool SawLayers => Board.Exists(k => (k & BoardChangeKind.Layers) != 0);
            public void Reset() { Board.Clear(); Toasts.Clear(); State = 0; }
        }

        static string Names(Board b)
        {
            var parts = new List<string>();
            foreach (var l in b.Layers) parts.Add(l.Name);
            return string.Join(",", parts);
        }

        public static void AddLayerGoesOnTopAndBecomesActive()
        {
            var s = new EditorSession();
            var r = new Recorder(s);
            EntityId first = s.Board.Layers[0].Id;
            EntityId id = s.AddLayer();
            Check.That(!id.IsNone, "id returned");
            Check.That(s.Board.Layers.Count == 2, "two layers");
            Check.That(s.Board.Layers[1].Id == id, "new layer is last (top)");
            Check.That(s.Board.Layers[0].Id == first, "old layer stays at the bottom");
            Check.That(s.Board.ActiveLayerId == id, "new layer active");
            Check.That(s.Board.Layers[1].Name == "Layer 2", "named Layer 2, got " + s.Board.Layers[1].Name);
            Check.That(s.Board.Layers[1].Visible && s.Board.Layers[1].Opacity == 1.0, "visible, opaque");
            Check.That(r.SawLayers && r.State > 0, "BoardChanged(Layers) + StateChanged");
        }

        public static void AddLayerNamesAreUniqueAndIncreasing()
        {
            var s = new EditorSession();
            s.AddLayer(); s.AddLayer();                       // Layer 1,2,3
            Check.That(Names(s.Board) == "Layer 1,Layer 2,Layer 3", Names(s.Board));
            s.DeleteBottomLayer();                            // Layer 2,3
            s.AddLayer();
            Check.That(Names(s.Board) == "Layer 2,Layer 3,Layer 4", Names(s.Board));
            s.MoveLayer(s.Board.Layers[2].Id, 0);             // Layer 4,2,3
            s.DeleteBottomLayer();                            // 2,3 (4 gone, number not reused)
            s.AddLayer();
            Check.That(Names(s.Board) == "Layer 2,Layer 3,Layer 5", Names(s.Board));

            var g = new EditorSession(GoldenBoards.StarOnFullBoard());
            int before = g.Board.Layers.Count;
            g.AddLayer();
            Check.That(g.Board.Layers[before].Name == "Layer " + (before + 1), "custom-named board: " + Names(g.Board));
        }

        public static void AddLayerNameCounterResetsOnLoadBoard()
        {
            var s = new EditorSession();
            s.AddLayer(); s.AddLayer();
            s.ClearBoard();
            s.AddLayer();
            Check.That(Names(s.Board) == "Layer 1,Layer 2", "fresh board restarts naming: " + Names(s.Board));
        }

        public static void AddLayerCapsAtTwelveWithToast()
        {
            var s = new EditorSession();
            for (int i = 0; i < 20; i++) s.AddLayer();
            Check.That(s.Board.Layers.Count == EditorSession.MaxLayers && EditorSession.MaxLayers == 12, "capped at 12");
            EntityId active = s.Board.ActiveLayerId;
            var r = new Recorder(s);
            EntityId id = s.AddLayer();
            Check.That(id.IsNone, "None at the cap");
            Check.That(s.Board.Layers.Count == 12, "still 12");
            Check.That(s.Board.ActiveLayerId == active, "active unchanged");
            Check.That(r.Toasts.Count == 1 && r.Board.Count == 0, "toast, no BoardChanged");
        }

        public static void SelectLayerSetsActive()
        {
            var s = new EditorSession();
            EntityId bottom = s.Board.Layers[0].Id;
            s.AddLayer();
            var r = new Recorder(s);
            s.SelectLayer(bottom);
            Check.That(s.Board.ActiveLayerId == bottom, "bottom active");
            Check.That(s.ActiveLayer.Id == bottom, "ActiveLayer resolves");
            Check.That(r.State > 0 && r.SawLayers, "events raised");

            r.Reset();
            s.SelectLayer(new EntityId(9999));
            Check.That(s.Board.ActiveLayerId == bottom && r.State == 0 && r.Board.Count == 0, "unknown id ignored");
            s.SelectLayer(bottom);
            Check.That(r.State == 0 && r.Board.Count == 0, "re-selecting active is a no-op");
        }

        public static void MoveLayerReordersAndClamps()
        {
            var s = new EditorSession();
            s.AddLayer(); s.AddLayer();                       // 1,2,3
            var r = new Recorder(s);
            EntityId l3 = s.Board.Layers[2].Id;
            s.MoveLayer(l3, 0);
            Check.That(Names(s.Board) == "Layer 3,Layer 1,Layer 2", Names(s.Board));
            Check.That(r.SawLayers, "BoardChanged(Layers)");
            s.MoveLayer(l3, 99);
            Check.That(Names(s.Board) == "Layer 1,Layer 2,Layer 3", "clamped high: " + Names(s.Board));
            s.MoveLayer(l3, -5);
            Check.That(Names(s.Board) == "Layer 3,Layer 1,Layer 2", "clamped low: " + Names(s.Board));
            s.MoveLayer(s.Board.Layers[1].Id, 2);
            Check.That(Names(s.Board) == "Layer 3,Layer 2,Layer 1", "middle to top: " + Names(s.Board));

            r.Reset();
            s.MoveLayer(l3, 0);
            Check.That(r.Board.Count == 0, "unchanged index raises nothing");
            s.MoveLayer(new EntityId(9999), 1);
            Check.That(r.Board.Count == 0 && Names(s.Board) == "Layer 3,Layer 2,Layer 1", "unknown id ignored");
        }

        public static void MoveLayerKeepsActiveAndContents()
        {
            var s = new EditorSession();
            EntityId bottom = s.Board.Layers[0].Id;
            s.Board.Layers[0].Shapes.Add(new LineShape(s.Board.NewId(), new Vec2(0, 0), new Vec2(1, 1), StrokeStyle.Default));
            s.AddLayer();
            s.SelectLayer(bottom);
            s.MoveLayer(bottom, 1);
            Check.That(s.Board.Layers[1].Id == bottom && s.Board.Layers[1].Shapes.Count == 1, "shapes travel with the layer");
            Check.That(s.Board.ActiveLayerId == bottom, "active layer unchanged by a move");
        }

        public static void DeleteBottomLayerRemovesLayerAndContents()
        {
            var s = new EditorSession();
            Layer bottom = s.Board.Layers[0];
            var line = new LineShape(s.Board.NewId(), new Vec2(0, 0), new Vec2(1, 1), StrokeStyle.Default);
            bottom.Shapes.Add(line);
            bottom.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.5, 0.5)));
            s.Selection.Ids.Add(line.Id);
            EntityId top = s.AddLayer();
            var r = new Recorder(s);
            s.DeleteBottomLayer();
            Check.That(s.Board.Layers.Count == 1 && s.Board.Layers[0].Id == top, "only the top layer remains");
            Check.That(s.Board.FindLayer(bottom.Id) == null, "bottom gone");
            Check.That(!s.Selection.Ids.Contains(line.Id), "deleted shapes leave the selection");
            Check.That(s.Board.ActiveLayerId == top, "active (top) unchanged");
            Check.That(r.SawLayers && r.State > 0, "events raised");
        }

        public static void DeleteActiveBottomLayerActivatesNewBottom()
        {
            var s = new EditorSession();
            EntityId l1 = s.Board.Layers[0].Id;
            EntityId l2 = s.AddLayer();
            s.AddLayer();
            s.SelectLayer(l1);
            s.DeleteBottomLayer();
            Check.That(s.Board.ActiveLayerId == l2, "new bottom becomes active");
            Check.That(s.ActiveLayer != null, "ActiveLayer resolves");
        }

        public static void DeleteOnlyLayerIsRefusedWithToast()
        {
            var s = new EditorSession();
            EntityId only = s.Board.Layers[0].Id;
            var r = new Recorder(s);
            s.DeleteBottomLayer();
            Check.That(s.Board.Layers.Count == 1 && s.Board.Layers[0].Id == only, "layer kept");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "Can't delete the only layer", "toast text");
            Check.That(r.Board.Count == 0 && r.State == 0, "no change events");
        }

        public static void DeleteDownToOneThenRefuse()
        {
            var s = new EditorSession();
            for (int i = 0; i < 5; i++) s.AddLayer();
            var r = new Recorder(s);
            for (int i = 0; i < 10; i++) s.DeleteBottomLayer();
            Check.That(s.Board.Layers.Count == 1, "one layer left");
            Check.That(r.Toasts.Count == 5, "5 refusals toasted, got " + r.Toasts.Count);
            Check.That(s.Board.ActiveLayerId == s.Board.Layers[0].Id, "survivor active");
        }

        public static void SetLayerVisibleToggles()
        {
            var s = new EditorSession();
            EntityId id = s.Board.Layers[0].Id;
            var r = new Recorder(s);
            s.SetLayerVisible(id, false);
            Check.That(!s.Board.Layers[0].Visible && r.SawLayers, "hidden + BoardChanged(Layers)");
            r.Reset();
            s.SetLayerVisible(id, false);
            Check.That(r.Board.Count == 0, "same value raises nothing");
            s.SetLayerVisible(id, true);
            Check.That(s.Board.Layers[0].Visible && r.SawLayers, "shown again");
            s.SetLayerVisible(new EntityId(9999), false);
            Check.That(s.Board.Layers[0].Visible, "unknown id ignored");
        }

        public static void SetLayerOpacityClampsAndIgnoresNaN()
        {
            var s = new EditorSession();
            EntityId id = s.Board.Layers[0].Id;
            var r = new Recorder(s);
            s.SetLayerOpacity(id, 0.25);
            Check.Near(s.Board.Layers[0].Opacity, 0.25, "set");
            Check.That(r.SawLayers, "BoardChanged(Layers)");
            s.SetLayerOpacity(id, 3);
            Check.Near(s.Board.Layers[0].Opacity, 1, "clamped high");
            s.SetLayerOpacity(id, -1);
            Check.Near(s.Board.Layers[0].Opacity, 0, "clamped low");
            s.SetLayerOpacity(id, double.NaN);
            Check.Near(s.Board.Layers[0].Opacity, 0, "NaN ignored");
            s.SetLayerOpacity(id, double.PositiveInfinity);
            Check.Near(s.Board.Layers[0].Opacity, 1, "+inf clamps to 1");
            r.Reset();
            s.SetLayerOpacity(id, 1);
            Check.That(r.Board.Count == 0, "same value raises nothing");
            s.SetLayerOpacity(new EntityId(9999), 0.5);
            Check.Near(s.Board.Layers[0].Opacity, 1, "unknown id ignored");
        }

        public static void LayerOpsWorkOnLoadedGoldenBoard()
        {
            var s = new EditorSession();
            s.LoadBoard(GoldenBoards.StarOnFullBoard());
            int n = s.Board.Layers.Count;
            EntityId top = s.Board.Layers[n - 1].Id;
            s.MoveLayer(top, 0);
            Check.That(s.Board.Layers[0].Id == top, "moved to bottom");
            s.DeleteBottomLayer();
            Check.That(s.Board.Layers.Count == n - 1 && s.Board.FindLayer(top) == null, "deleted");
            Check.That(s.ActiveLayer != null, "active resolves after delete");
        }
    }
}
