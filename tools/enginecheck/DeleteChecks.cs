using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Delete (plan 5.1): removes the selected shapes on visible layers as one "Delete" step, empties the selection (a
    /// transform tool falls back to Select), toasts the count, rebuilds snap points; Undo brings them back; nothing
    /// selected does nothing.
    /// </summary>
    public static class DeleteChecks
    {
        static LineShape Line(EditorSession s, Layer l, double x0, double y0, double x1, double y1)
        {
            var line = new LineShape(s.Board.NewId(), new Vec2(x0, y0), new Vec2(x1, y1), StrokeStyle.Default);
            l.Shapes.Add(line);
            return line;
        }

        public static void DeleteRemovesTheSelectionAsOneStep()
        {
            var s = new EditorSession();
            var a = Line(s, s.ActiveLayer, 0.1, 0.1, 0.3, 0.3);
            var b = new CircleShape(s.Board.NewId(), new Vec2(0.6, 0.6), 0.1, StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(b);
            var keep = Line(s, s.ActiveLayer, 0.7, 0.1, 0.9, 0.2);
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { a.Id, b.Id });
            s.SetTool(ToolId.Move);
            var toasts = new List<string>();
            int shapes = 0;
            s.Toast += t => toasts.Add(t);
            s.BoardChanged += k => { if ((k & BoardChangeKind.Shapes) != 0) shapes++; };
            int steps = s.UndoCount;
            s.DeleteSelection();
            Check.That(s.ActiveLayer.Shapes.Count == 1 && s.ActiveLayer.Shapes[0] == keep, "both selected shapes removed");
            Check.That(s.UndoCount == steps + 1 && s.NextUndoLabel == "Delete" && shapes == 1, "one 'Delete' step, one BoardChanged(Shapes)");
            Check.That(s.SelectionCount == 0 && s.ActiveTool == ToolId.SelectMove, "selection emptied, Move falls back to Select");
            Check.That(toasts.Count > 0 && toasts[toasts.Count - 1] == "Deleted 2 shapes", "toast");
            bool endpoint = false;
            foreach (var c in s.SnapCandidates) if (c.Position == new Vec2(0.1, 0.1)) endpoint = true;
            Check.That(!endpoint, "the deleted line's endpoints are no longer snap points");
            s.Undo();
            Check.That(s.ActiveLayer.Shapes.Count == 3, "Undo brings them back");
        }

        public static void NothingSelectedDoesNothing_HiddenLayersAreUntouched()
        {
            var s = new EditorSession();
            Line(s, s.ActiveLayer, 0.1, 0.1, 0.3, 0.3);
            s.DeleteSelection();
            Check.That(!s.CanUndo && s.ActiveLayer.Shapes.Count == 1, "nothing selected: nothing happens");

            EntityId bottom = s.ActiveLayer.Id;
            var low = s.ActiveLayer.Shapes[0];
            EntityId top = s.AddLayer();
            var high = Line(s, s.Board.FindLayer(top), 0.5, 0.5, 0.7, 0.7);
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { low.Id, high.Id });
            s.SetLayerVisible(bottom, false);   // hiding prunes the bottom shape from the selection
            s.DeleteSelection();
            Check.That(s.Board.FindLayer(bottom).Shapes.Count == 1, "the hidden layer's shape survives");
            Check.That(s.Board.FindLayer(top).Shapes.Count == 0, "the visible one is deleted");
        }
    }
}
