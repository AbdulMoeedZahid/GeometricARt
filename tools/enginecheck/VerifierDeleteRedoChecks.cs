using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER (P7 Delete, P8 Redo): DeleteSelection removes exactly the selected shapes on visible layers (lines,
    /// circles, polygons; several layers), is one "Delete" step whose Undo restores every shape in its place and the
    /// selection, empties the selection (transform tools fall back to Select), toasts the count, cancels an open drag
    /// first; an empty selection does nothing. Redo re-applies with the right label and is cleared by any new edit.
    /// </summary>
    public static class VerifierDeleteRedoChecks
    {
        static Vec2 V(double x, double y) => new Vec2(x, y);
        static double s_T;
        static PointerEvent Ev(PointerPhase p, Vec2 v) => new PointerEvent(p, v, true, s_T += 0.016);

        static string Dump(Board b)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in b.Layers)
            {
                sb.Append(l.Id.Value).Append(':');
                foreach (var s in l.Shapes) sb.Append(s.Id.Value).Append(s.Kind).Append(',');
                sb.Append('|');
            }
            return sb.ToString();
        }

        static EditorSession Mixed(out List<Shape> sel, out Shape keep, out Layer hidden, out Shape hiddenShape)
        {
            var b = GoldenBoards.Empty();
            var a = b.Layers[0];
            var c = new Layer(b.NewId(), "B");
            hidden = new Layer(b.NewId(), "H");
            b.Layers.Add(c);
            b.Layers.Add(hidden);
            var l1 = new LineShape(b.NewId(), V(0.1, 0.1), V(0.3, 0.2), StrokeStyle.Default);
            keep = new LineShape(b.NewId(), V(0.6, 0.6), V(0.9, 0.7), StrokeStyle.Default);
            var circle = new CircleShape(b.NewId(), V(0.5, 0.5), 0.1, StrokeStyle.Default);
            var poly = new PolygonShape(b.NewId(), V(0.3, 0.7), 6, 0.08, 0, StrokeStyle.Default);
            hiddenShape = new LineShape(b.NewId(), V(0.2, 0.9), V(0.4, 0.9), StrokeStyle.Default);
            a.Shapes.Add(l1); a.Shapes.Add(keep); a.Shapes.Add(circle);
            c.Shapes.Add(poly);
            hidden.Shapes.Add(hiddenShape);
            var s = new EditorSession(b);
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { l1.Id, circle.Id, poly.Id, hiddenShape.Id });
            sel = new List<Shape> { l1, circle, poly };
            return s;
        }

        public static void V_DeleteMixedMultiLayerIsOneStep()
        {
            var s = Mixed(out var sel, out var keep, out var hidden, out var hs);
            Check.That(s.SelectionCount == 4, "precondition: 4 selected (one on a layer about to be hidden)");
            s.SetLayerVisible(hidden.Id, false);              // prunes its member from the selection
            Check.That(s.SelectionCount == 3, "precondition: the hidden member left the selection");
            string before = Dump(s.Board);
            int steps = s.UndoCount;
            var toasts = new List<string>();
            int boards = 0, states = 0;
            s.Toast += m => toasts.Add(m);
            s.BoardChanged += k => boards++;
            s.StateChanged += () => states++;
            s.DeleteSelection();
            Check.That(s.UndoCount == steps + 1 && s.NextUndoLabel == "Delete", $"one \"Delete\" step, got {s.UndoCount - steps} {s.NextUndoLabel}");
            foreach (var sh in sel)
                foreach (var l in s.Board.Layers) Check.That(!l.Shapes.Contains(sh), $"{sh.Kind} removed");
            Check.That(s.Board.Layers[0].Shapes.Contains(keep), "an unselected shape stays");
            Check.That(s.Board.FindLayer(hidden.Id).Shapes.Contains(hs), "the hidden layer's shape is untouched");
            Check.That(s.SelectionCount == 0, "selection emptied");
            Check.That(boards == 1 && toasts.Count == 1 && toasts[0] == "Deleted 3 shapes", $"one BoardChanged, toast 'Deleted 3 shapes' ({boards}, {string.Join(";", toasts)})");
            s.Undo();
            Check.That(Dump(s.Board) == before, "Undo restores every shape in its layer and place");
            Check.That(s.SelectionCount == 3, $"Undo restores the selection in Select ({s.SelectionCount})");
            s.Redo();
            Check.That(s.SelectionCount == 0 && s.Board.Layers[0].Shapes.Count == 1, "Redo deletes again");
        }

        public static void V_DeleteFromMoveFallsBackToSelectAndCancelsTheDrag()
        {
            var s = Mixed(out var sel, out _, out _, out _);
            s.SetTool(ToolId.Move);
            Check.That(s.ActiveTool == ToolId.Move, "precondition: Move");
            s.Pointer(Ev(PointerPhase.Down, V(0.3, 0.2)));
            s.Pointer(Ev(PointerPhase.Move, V(0.35, 0.25)));
            s.Pointer(Ev(PointerPhase.Move, V(0.45, 0.35)));   // an open Move drag
            s.DeleteSelection();
            Check.That(s.ActiveTool == ToolId.SelectMove, $"Move falls back to Select, got {s.ActiveTool}");
            Check.That(!s.GestureActive && s.Preview.Count == 0, "the drag was cancelled, no ghosts left");
            s.Undo();
            var l1 = (LineShape)s.Board.Layers[0].Shapes[0];
            Check.That(l1.A == V(0.1, 0.1) && l1.B == V(0.3, 0.2), "Undo restores the line where it was before the cancelled drag");
            s.Pointer(Ev(PointerPhase.Up, V(0.45, 0.35)));
            Check.That(s.UndoCount == 0 || s.NextRedoLabel == "Delete", "a late Up after the cancel commits nothing");
        }

        public static void V_DeleteWithNothingSelectedDoesNothing()
        {
            var s = new EditorSession(GoldenBoards.StarOnFullBoard());
            int boards = 0, toasts = 0;
            s.BoardChanged += k => boards++;
            s.Toast += m => toasts++;
            s.DeleteSelection();
            s.SetTool(ToolId.SelectMove);   // (its hint toast is not Delete's)
            toasts = 0;
            s.DeleteSelection();
            Check.That(s.UndoCount == 0 && boards == 0 && toasts == 0, "no selection: no step, no event, no toast");
        }

        public static void V_RedoLabelsAndClearedByANewEdit()
        {
            var s = new EditorSession(GoldenBoards.StarOnFullBoard());
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { s.ActiveLayer.Shapes[0].Id });
            s.DeleteSelection();
            LayerBands b = LayerBands.Default; b.On = true;
            s.SetLayerBands(s.ActiveLayer.Id, b);
            var toasts = new List<string>();
            s.Toast += m => toasts.Add(m);
            s.Undo();
            s.Undo();
            Check.That(s.CanRedo && s.RedoCount == 2 && s.NextRedoLabel == "Delete", $"two to redo, next 'Delete' ({s.NextRedoLabel})");
            s.Redo();
            Check.That(toasts[toasts.Count - 1] == "Redid: Delete" && s.NextRedoLabel == "Bands", "Redid: Delete; then Bands");
            s.SetGrid(3);                                    // any new edit
            Check.That(!s.CanRedo && s.RedoCount == 0, "a new edit clears Redo");
            toasts.Clear();
            s.Redo();
            Check.That(toasts.Count == 1 && toasts[0] == "Nothing to redo", "Redo with nothing: toast only");
        }
    }
}
