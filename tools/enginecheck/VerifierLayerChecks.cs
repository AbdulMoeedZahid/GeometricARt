using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER: layer rules written from the spec (index 0 = bottom; AddLayer on top, "Layer N", active, cap 12;
    /// MoveLayer clamps; DeleteBottomLayer keeps the last layer; visibility/opacity raise BoardChanged so BoardView redraws).
    /// </summary>
    public static class VerifierLayerChecks
    {
        sealed class Log
        {
            public readonly List<BoardChangeKind> Board = new List<BoardChangeKind>();
            public int State, Preview;
            public readonly List<string> Toasts = new List<string>();

            public Log(EditorSession s)
            {
                s.BoardChanged += k => Board.Add(k);
                s.StateChanged += () => State++;
                s.PreviewChanged += () => Preview++;
                s.Toast += m => Toasts.Add(m);
            }

            public void Reset() { Board.Clear(); State = 0; Preview = 0; Toasts.Clear(); }
            public bool AnyBoard => Board.Count > 0;
        }

        static EditorSession WithLayers(int count)
        {
            var s = new EditorSession();
            for (int i = 1; i < count; i++) s.AddLayer();
            Check.That(s.Board.Layers.Count == count, $"setup: expected {count} layers, got {s.Board.Layers.Count}");
            return s;
        }

        static string Order(EditorSession s)
        {
            var parts = new List<string>();
            foreach (var l in s.Board.Layers) parts.Add(l.Id.Value.ToString());
            return string.Join(",", parts);
        }

        public static void V_AddLayerGoesOnTopAndBecomesActive()
        {
            var s = new EditorSession();
            var log = new Log(s);
            EntityId bottom = s.Board.Layers[0].Id;
            EntityId id = s.AddLayer();
            Check.That(!id.IsNone, "AddLayer returns a real id");
            Check.That(s.Board.Layers.Count == 2, "two layers");
            Check.That(s.Board.Layers[1].Id == id, "new layer is at the top (last index)");
            Check.That(s.Board.Layers[0].Id == bottom, "old layer stays at the bottom");
            Check.That(s.Board.ActiveLayerId == id && s.ActiveLayer.Id == id, "new layer is active");
            Check.That(log.AnyBoard, "AddLayer raises BoardChanged");
            Check.That(log.Board.TrueForAll(k => k != BoardChangeKind.None), "BoardChanged kind is not None");
            Check.That(s.Board.Layers[1].Visible && s.Board.Layers[1].Opacity == 1.0, "new layer visible at 100%");
            Check.That(s.Board.Layers[1].Shapes.Count == 0, "new layer empty");
        }

        public static void V_AddLayerNamesLayerN()
        {
            var s = new EditorSession();
            s.AddLayer();
            s.AddLayer();
            Check.That(s.Board.Layers[1].Name == "Layer 2", $"second layer named 'Layer 2', got '{s.Board.Layers[1].Name}'");
            Check.That(s.Board.Layers[2].Name == "Layer 3", $"third layer named 'Layer 3', got '{s.Board.Layers[2].Name}'");
        }

        public static void V_AddLayerIdsUnique()
        {
            var s = WithLayers(6);
            var seen = new HashSet<int>();
            foreach (var l in s.Board.Layers) Check.That(seen.Add(l.Id.Value) && !l.Id.IsNone, "layer ids unique and non-zero");
        }

        public static void V_AddLayerCapTwelve()
        {
            var s = WithLayers(12);
            var log = new Log(s);
            EntityId active = s.Board.ActiveLayerId;
            EntityId id = s.AddLayer();
            Check.That(id.IsNone, "13th AddLayer returns None");
            Check.That(s.Board.Layers.Count == 12, "count stays 12");
            Check.That(s.Board.ActiveLayerId == active, "active unchanged at cap");
            Check.That(!log.AnyBoard, "no BoardChanged at cap");
            Check.That(log.Toasts.Count >= 1, "toast explains the cap");
        }

        public static void V_SelectLayer()
        {
            var s = WithLayers(3);
            var log = new Log(s);
            EntityId bottom = s.Board.Layers[0].Id;
            s.SelectLayer(bottom);
            Check.That(s.Board.ActiveLayerId == bottom, "select bottom");
            Check.That(log.State + log.Board.Count > 0, "selection raises an event (UI refresh)");
            log.Reset();
            s.SelectLayer(new EntityId(9999));
            Check.That(s.Board.ActiveLayerId == bottom, "unknown id ignored");
            s.SelectLayer(EntityId.None);
            Check.That(s.Board.ActiveLayerId == bottom, "None ignored");
            Check.That(s.ActiveLayer != null, "active resolves");
        }

        public static void V_MoveLayerReorders()
        {
            var s = WithLayers(4);
            var ids = new EntityId[4];
            for (int i = 0; i < 4; i++) ids[i] = s.Board.Layers[i].Id;
            var log = new Log(s);
            s.MoveLayer(ids[0], 2);   // bottom -> index 2
            Check.That(s.Board.Layers[2].Id == ids[0], $"moved to index 2: {Order(s)}");
            Check.That(s.Board.Layers[0].Id == ids[1] && s.Board.Layers[1].Id == ids[2] && s.Board.Layers[3].Id == ids[3],
                $"others keep relative order: {Order(s)}");
            Check.That(log.AnyBoard, "MoveLayer raises BoardChanged");
            s.MoveLayer(ids[3], 0);   // top -> bottom
            Check.That(s.Board.Layers[0].Id == ids[3], $"top to bottom: {Order(s)}");
            Check.That(s.Board.Layers.Count == 4, "count unchanged");
        }

        public static void V_MoveLayerClamps()
        {
            var s = WithLayers(3);
            EntityId a = s.Board.Layers[0].Id, c = s.Board.Layers[2].Id;
            s.MoveLayer(a, 99);
            Check.That(s.Board.Layers[2].Id == a, $"clamped to top: {Order(s)}");
            s.MoveLayer(a, -5);
            Check.That(s.Board.Layers[0].Id == a, $"clamped to bottom: {Order(s)}");
            s.MoveLayer(c, int.MaxValue);
            Check.That(s.Board.Layers[2].Id == c, "int.MaxValue clamps");
            s.MoveLayer(c, int.MinValue);
            Check.That(s.Board.Layers[0].Id == c, "int.MinValue clamps");
            Check.That(s.Board.Layers.Count == 3, "count unchanged");
        }

        public static void V_MoveLayerNoOps()
        {
            var s = WithLayers(3);
            string before = Order(s);
            var log = new Log(s);
            s.MoveLayer(new EntityId(4242), 0);
            Check.That(Order(s) == before, "unknown id: no change");
            s.MoveLayer(s.Board.Layers[1].Id, 1);
            Check.That(Order(s) == before, "same index: no change");
            Check.That(!log.AnyBoard, "no-op moves raise no BoardChanged");
        }

        public static void V_MoveLayerKeepsActive()
        {
            var s = WithLayers(3);
            EntityId active = s.Board.ActiveLayerId;
            s.MoveLayer(active, 0);
            Check.That(s.Board.ActiveLayerId == active, "moving keeps the active layer");
            s.MoveLayer(s.Board.Layers[2].Id, 0);
            Check.That(s.Board.ActiveLayerId == active, "moving another keeps the active layer");
        }

        public static void V_DeleteBottomRemovesIndexZero()
        {
            var s = WithLayers(3);
            EntityId b0 = s.Board.Layers[0].Id, b1 = s.Board.Layers[1].Id, top = s.Board.Layers[2].Id;
            var log = new Log(s);
            s.DeleteBottomLayer();
            Check.That(s.Board.Layers.Count == 2, "one layer removed");
            Check.That(s.Board.FindLayer(b0) == null, "bottom (index 0) removed");
            Check.That(s.Board.Layers[0].Id == b1 && s.Board.Layers[1].Id == top, "rest unchanged");
            Check.That(s.Board.ActiveLayerId == top, "active (top) kept when it was not deleted");
            Check.That(log.AnyBoard, "delete raises BoardChanged");
        }

        public static void V_DeleteBottomActiveMovesToNewBottom()
        {
            var s = WithLayers(3);
            EntityId b1 = s.Board.Layers[1].Id;
            s.SelectLayer(s.Board.Layers[0].Id);
            s.DeleteBottomLayer();
            Check.That(s.Board.ActiveLayerId == b1, "new bottom becomes active");
            Check.That(s.ActiveLayer != null, "active resolves");
        }

        public static void V_DeleteBottomNeverLast()
        {
            var s = new EditorSession();
            s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), new Vec2(0.1, 0.1), new Vec2(0.9, 0.9), StrokeStyle.Default));
            var log = new Log(s);
            EntityId only = s.Board.Layers[0].Id;
            s.DeleteBottomLayer();
            Check.That(s.Board.Layers.Count == 1 && s.Board.Layers[0].Id == only, "last layer kept");
            Check.That(s.Board.Layers[0].Shapes.Count == 1, "its shapes kept");
            Check.That(log.Toasts.Count >= 1, "toast when refusing");
            Check.That(!log.AnyBoard, "no BoardChanged when refusing");
        }

        public static void V_DeleteBottomRepeatedly()
        {
            var s = WithLayers(5);
            for (int i = 0; i < 10; i++) s.DeleteBottomLayer();
            Check.That(s.Board.Layers.Count == 1, "down to one");
            Check.That(s.ActiveLayer != null && s.ActiveLayer.Id == s.Board.Layers[0].Id, "remaining layer active");
            Check.That(!s.AddLayer().IsNone, "can add again");
        }

        public static void V_VisibilityRaisesBoardChanged()
        {
            var s = WithLayers(2);
            EntityId id = s.Board.Layers[0].Id;
            var log = new Log(s);
            s.SetLayerVisible(id, false);
            Check.That(!s.Board.Layers[0].Visible, "hidden");
            Check.That(log.AnyBoard, "hide raises BoardChanged (BoardView redraws)");
            log.Reset();
            s.SetLayerVisible(id, false);
            Check.That(!log.AnyBoard, "same value: no event");
            s.SetLayerVisible(id, true);
            Check.That(s.Board.Layers[0].Visible && log.AnyBoard, "shown again");
            s.SetLayerVisible(new EntityId(777), false);
            Check.That(s.Board.Layers[0].Visible && s.Board.Layers[1].Visible, "unknown id no-op");
        }

        public static void V_HidingDoesNotChangeOrderOrActive()
        {
            var s = WithLayers(3);
            string before = Order(s);
            EntityId active = s.Board.ActiveLayerId;
            s.SetLayerVisible(active, false);
            Check.That(Order(s) == before && s.Board.ActiveLayerId == active, "hide keeps order and active");
        }

        public static void V_OpacityClampsAndIgnoresNaN()
        {
            var s = WithLayers(2);
            EntityId id = s.Board.Layers[1].Id;
            var log = new Log(s);
            s.SetLayerOpacity(id, 0.4);
            Check.Near(s.Board.Layers[1].Opacity, 0.4, "set 0.4");
            Check.That(log.AnyBoard, "opacity raises BoardChanged (BoardView only rebuilds shapes on BoardChanged)");
            s.SetLayerOpacity(id, 2.5);
            Check.Near(s.Board.Layers[1].Opacity, 1.0, "clamp high");
            s.SetLayerOpacity(id, -1);
            Check.Near(s.Board.Layers[1].Opacity, 0.0, "clamp low");
            s.SetLayerOpacity(id, double.NaN);
            Check.Near(s.Board.Layers[1].Opacity, 0.0, "NaN ignored");
            s.SetLayerOpacity(id, double.PositiveInfinity);
            Check.That(s.Board.Layers[1].Opacity >= 0 && s.Board.Layers[1].Opacity <= 1, "+inf stays in range");
            s.SetLayerOpacity(new EntityId(555), 0.5);
            Check.Near(s.Board.Layers[0].Opacity, 1.0, "unknown id no-op");
        }

        public static void V_LayerOpsAfterLoadBoard()
        {
            var s = WithLayers(4);
            s.LoadBoard(GoldenBoards.StarOnFullBoard());
            int n = s.Board.Layers.Count;
            EntityId id = s.AddLayer();
            Check.That(!id.IsNone && s.Board.Layers[n].Id == id, "AddLayer works on a loaded board");
            foreach (var l in s.Board.Layers)
                Check.That(l.Id == id || l.Id.Value != id.Value, "id unique on loaded board");
            int countIds = 0;
            foreach (var l in s.Board.Layers) if (l.Id == id) countIds++;
            Check.That(countIds == 1, "new id does not collide with fixture ids");
        }

        public static void V_EventsSeeConsistentState()
        {
            // Listeners (UI) read the board inside the event; state must already be final.
            var s = WithLayers(2);
            int seenCount = -1;
            EntityId seenActive = EntityId.None;
            s.BoardChanged += _ => { seenCount = s.Board.Layers.Count; seenActive = s.Board.ActiveLayerId; };
            EntityId id = s.AddLayer();
            Check.That(seenCount == 3 && seenActive == id, "AddLayer: state final when BoardChanged fires");
            s.SelectLayer(s.Board.Layers[0].Id);
            s.DeleteBottomLayer();
            Check.That(seenCount == 2 && s.Board.FindLayer(seenActive) != null, "Delete: active valid when BoardChanged fires");
        }
    }
}
