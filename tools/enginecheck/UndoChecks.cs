using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Undo/redo (EditorSession.Undo.cs): one step per edit with an exact restore, non-steps, opacity merging, the full
    /// walk back and forward, ClearBoard/LoadBoard, ids, snap index, selection and active-layer rules, open gestures, the
    /// cap, snapshot isolation, event counts and the allocation-free Move path. Boards are compared with <see cref="Dump"/>.
    /// NextId is compared separately: it never decreases across Undo/Redo, so a restored board can hold a larger NextId
    /// than the stored one.
    /// </summary>
    public static class UndoChecks
    {
        static double s_T;
        static PointerEvent Ev(PointerPhase p, double x, double y, bool on = true) =>
            new PointerEvent(p, new Vec2(x, y), on, s_T += 0.016);

        static void DragOpen(EditorSession s, double ax, double ay, double bx, double by, int steps = 5)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= steps; i++)
            {
                double k = (double)i / steps;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * k, ay + (by - ay) * k));
            }
        }

        static void Drag(EditorSession s, double ax, double ay, double bx, double by, int steps = 5)
        {
            DragOpen(s, ax, ay, bx, by, steps);
            s.Pointer(Ev(PointerPhase.Up, bx, by));
        }

        static void Tap(EditorSession s, double x, double y)
        {
            s.Pointer(Ev(PointerPhase.Down, x, y));
            s.Pointer(Ev(PointerPhase.Up, x, y));
        }

        static int ShapeCount(EditorSession s)
        {
            int n = 0;
            foreach (var l in s.Board.Layers) n += l.Shapes.Count;
            return n;
        }

        static readonly StrokeStyle St = StrokeStyle.Default;
        static readonly StrokeStyle Fancy = new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted);

        static LineShape AddLine(Board b, Layer l, double ax, double ay, double bx, double by, StrokeStyle? style = null)
        {
            var line = new LineShape(b.NewId(), new Vec2(ax, ay), new Vec2(bx, by), style ?? St);
            l.Shapes.Add(line);
            return line;
        }

        static CircleShape AddCircle(Board b, Layer l, double cx, double cy, double r)
        {
            var c = new CircleShape(b.NewId(), new Vec2(cx, cy), r, St);
            l.Shapes.Add(c);
            return c;
        }

        /// <summary>
        /// Two layers (bottom "Layer 1": a fancy line and a circle; top "Layer 2", opacity 0.75, active: a line, a hexagon
        /// and a snap point). Ids: bottom 1, top 2, bottom line 3, circle 4, top line 5, polygon 6, snap point 7.
        /// </summary>
        static Board Rich()
        {
            var b = new Board();
            var bottom = new Layer(b.NewId(), "Layer 1");
            var top = new Layer(b.NewId(), "Layer 2") { Opacity = 0.75 };
            b.Layers.Add(bottom);
            b.Layers.Add(top);
            AddLine(b, bottom, 0.1, 0.1, 0.3, 0.3, Fancy);
            AddCircle(b, bottom, 0.6, 0.6, 0.1);
            AddLine(b, top, 0.2, 0.8, 0.4, 0.85);
            top.Shapes.Add(new PolygonShape(b.NewId(), new Vec2(0.3, 0.6), 6, 0.05, 0.2, St));
            top.SnapPoints.Add(new SnapPoint(b.NewId(), new Vec2(0.33, 0.44)));
            b.ActiveLayerId = top.Id;
            return b;
        }

        static Layer Bottom(EditorSession s) => s.Board.Layers[0];
        static Layer Top(EditorSession s) => s.Board.Layers[s.Board.Layers.Count - 1];

        static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        static string P(Vec2 v) => "(" + R(v.X) + "," + R(v.Y) + ")";

        /// <summary>Everything on the board, doubles exact ("R"). NextId and ActiveLayerId only when asked.</summary>
        static string Dump(Board b, bool nextId = true, bool active = true)
        {
            var sb = new StringBuilder();
            sb.Append("grid ").Append(b.GridN).Append(' ').Append(b.GridKind);
            if (active) sb.Append(" active ").Append(b.ActiveLayerId.Value);
            if (nextId) sb.Append(" next ").Append(b.NextId);
            sb.Append('\n');
            foreach (var l in b.Layers)
            {
                if (l == null) { sb.Append("null layer\n"); continue; }
                sb.Append("L ").Append(l.Id.Value).Append(" '").Append(l.Name).Append("' ").Append(l.Visible)
                    .Append(' ').Append(R(l.Opacity)).Append('\n');
                foreach (var sh in l.Shapes)
                {
                    if (sh == null) { sb.Append("  null\n"); continue; }
                    sb.Append("  ").Append(sh.Kind).Append(' ').Append(sh.Id.Value).Append(' ')
                        .Append(sh.Style.Color).Append(' ').Append(sh.Style.Width).Append(' ').Append(sh.Style.Dash).Append(' ');
                    switch (sh)
                    {
                        case LineShape ln: sb.Append(P(ln.A)).Append(P(ln.B)); break;
                        case CircleShape c: sb.Append(P(c.Center)).Append(R(c.Radius)); break;
                        case PolygonShape p:
                            sb.Append(P(p.Center)).Append(p.Sides).Append(' ').Append(R(p.Radius)).Append(' ').Append(R(p.RotationRad));
                            break;
                    }
                    sb.Append('\n');
                }
                foreach (var sp in l.SnapPoints)
                    sb.Append("  snap ").Append(sp == null ? "null" : sp.Id.Value + " " + P(sp.Position)).Append('\n');
            }
            return sb.ToString();
        }

        static string Sel(EditorSession s)
        {
            var ids = new List<int>();
            foreach (var id in s.Selection.Ids) ids.Add(id.Value);
            ids.Sort();
            return string.Join(",", ids);
        }

        static Shape FindShape(Board b, EntityId id)
        {
            foreach (var l in b.Layers)
                foreach (var sh in l.Shapes)
                    if (sh != null && sh.Id == id) return sh;
            return null;
        }

        static bool HasCandidate(EditorSession s, Vec2 p)
        {
            var list = s.SnapCandidates;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Position == p) return true;
            return false;
        }

        static bool IsSelectionTool(ToolId t) =>
            t == ToolId.SelectMove || t == ToolId.Move || t == ToolId.Scale || t == ToolId.Duplicate;

        static void Select(EditorSession s, ToolId tool, params EntityId[] ids)
        {
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(ids);
            if (tool != ToolId.SelectMove) s.SetTool(tool);
            Check.That(s.ActiveTool == tool && s.Selection.Ids.Count == ids.Length, $"precondition: {tool} with {ids.Length} selected");
        }

        sealed class Recorder
        {
            public readonly List<BoardChangeKind> Board = new List<BoardChangeKind>();
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> Order = new List<string>();
            public int State, Preview;

            public Recorder(EditorSession s)
            {
                s.BoardChanged += k => { Board.Add(k); Order.Add("B"); };
                s.StateChanged += () => { State++; Order.Add("S"); };
                s.PreviewChanged += () => { Preview++; Order.Add("P"); };
                s.Toast += m => { Toasts.Add(m); Order.Add("T"); };
            }

            public void Reset() { Board.Clear(); Toasts.Clear(); Order.Clear(); State = 0; Preview = 0; }
            public string Seq => string.Join("", Order);
            public string T => Toasts.Count == 0 ? "<none>" : string.Join(" | ", Toasts);
        }

        /// <summary>One Undo/Redo: exactly one BoardChanged(HistoryChangeKind), one StateChanged, one toast.</summary>
        static void CheckStepEvents(Recorder r, string toast, string what)
        {
            Check.That(r.Board.Count == 1 && r.Board[0] == EditorSession.HistoryChangeKind,
                $"{what}: one BoardChanged(HistoryChangeKind), got {string.Join(",", r.Board)}");
            var k = r.Board[0];
            Check.That((k & BoardChangeKind.History) != 0 && (k & BoardChangeKind.Shapes) != 0 && (k & BoardChangeKind.Layers) != 0
                && (k & BoardChangeKind.Grid) != 0 && (k & BoardChangeKind.SnapPoints) != 0 && (k & BoardChangeKind.Reset) == 0,
                $"{what}: History plus every content flag, no Reset");
            Check.That(r.State == 1, $"{what}: one StateChanged, got {r.State}");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == toast, $"{what}: toast '{toast}', got {r.T}");
        }

        // ---------- 1. empty history ----------

        public static void EmptyHistoryToastsAndChangesNothing()
        {
            var s = new EditorSession(Rich());
            Check.That(!s.CanUndo && !s.CanRedo && s.UndoCount == 0 && s.RedoCount == 0, "fresh session: no history");
            Check.That(s.NextUndoLabel == null && s.NextRedoLabel == null, "no labels");
            string before = Dump(s.Board);
            Board board = s.Board;
            var r = new Recorder(s);
            s.Undo();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == EditorSession.NothingToUndoToast && r.Toasts[0] == "Nothing to undo", r.T);
            Check.That(r.Board.Count == 0 && r.State == 0 && r.Preview == 0, "Undo with nothing: no other event");
            r.Reset();
            s.Redo();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == EditorSession.NothingToRedoToast && r.Toasts[0] == "Nothing to redo", r.T);
            Check.That(r.Board.Count == 0 && r.State == 0 && r.Preview == 0, "Redo with nothing: no other event");
            Check.That(ReferenceEquals(board, s.Board) && Dump(s.Board) == before, "board untouched");

            // An open gesture is left alone when there is nothing to undo.
            DragOpen(s, 0.5, 0.2, 0.8, 0.3);
            int preview = s.Preview.Count;
            s.Undo();
            Check.That(s.GestureActive && s.Preview.Count == preview, "nothing to undo: the gesture stays open");
            s.Pointer(Ev(PointerPhase.Up, 0.8, 0.3));
            Check.That(s.UndoCount == 1 && s.NextUndoLabel == EditorSession.UndoLabels.Line, "and still commits");
        }

        // ---------- 2. each action: one step, exact restore, exact redo ----------

        sealed class Case
        {
            public string Name, Label;
            public bool Snap;
            public Action<EditorSession> Prep, Act;
        }

        static IEnumerable<Case> Cases()
        {
            var e3 = new EntityId(3);
            var e4 = new EntityId(4);
            var e5 = new EntityId(5);
            yield return new Case { Name = "line drag", Label = "Line", Act = s => Drag(s, 0.5, 0.2, 0.8, 0.3) };
            yield return new Case
            {
                Name = "tap-to-connect", Label = "Line", Snap = true,
                Act = s => { Tap(s, 0.502, 0.497); Tap(s, 0.748, 0.253); },
            };
            yield return new Case
            {
                Name = "circle", Label = "Circle", Prep = s => s.SetTool(ToolId.Circle),
                Act = s => Drag(s, 0.75, 0.3, 0.85, 0.3),
            };
            yield return new Case
            {
                Name = "move", Label = "Move", Prep = s => Select(s, ToolId.Move, e3, e5),
                Act = s => Drag(s, 0.45, 0.05, 0.55, 0.1),
            };
            yield return new Case
            {
                Name = "scale", Label = "Scale", Prep = s => Select(s, ToolId.Scale, e4),
                Act = s => Drag(s, 0.75, 0.6, 0.7, 0.6),
            };
            yield return new Case
            {
                Name = "duplicate (two layers)", Label = "Duplicate", Prep = s => Select(s, ToolId.Duplicate, e3, e5),
                Act = s => Drag(s, 0.9, 0.05, 0.95, 0.07),
            };
            yield return new Case { Name = "add layer", Label = "Add layer", Act = s => s.AddLayer() };
            yield return new Case
            {
                Name = "delete bottom layer", Label = "Delete layer", Prep = s => Select(s, ToolId.SelectMove, e3, e5),
                Act = s => s.DeleteBottomLayer(),
            };
            yield return new Case { Name = "layer order", Label = "Layer order", Act = s => s.MoveLayer(Top(s).Id, 0) };
            yield return new Case
            {
                Name = "hide layer", Label = "Hide layer", Prep = s => Select(s, ToolId.SelectMove, e3, e5),
                Act = s => s.SetLayerVisible(Bottom(s).Id, false),
            };
            yield return new Case
            {
                Name = "show layer", Label = "Show layer", Prep = s => s.SetLayerVisible(Bottom(s).Id, false),
                Act = s => s.SetLayerVisible(Bottom(s).Id, true),
            };
            yield return new Case
            {
                Name = "layer opacity", Label = "Layer opacity",
                Act = s => { s.SetLayerOpacity(Top(s).Id, 0.3); s.SetLayerOpacity(Top(s).Id, 0.1); },
            };
            yield return new Case { Name = "grid type", Label = "Grid type", Act = s => s.SetGridKind(GridKind.Hex) };
            yield return new Case { Name = "tiles", Label = "Tiles", Act = s => s.SetGrid(3) };
            yield return new Case
            {
                Name = "clear board", Label = "Clear board",
                Prep = s => { s.SetGridKind(GridKind.Hex); s.SetGrid(4); Select(s, ToolId.SelectMove, e3, e4); },
                Act = s => s.ClearBoard(),
            };
        }

        public static void EveryActionIsOneStepWithExactRestoreAndRedo()
        {
            foreach (var c in Cases())
            {
                var s = new EditorSession(Rich());
                s.SetSnapEnabled(c.Snap);
                c.Prep?.Invoke(s);
                int count = s.UndoCount;
                string before = Dump(s.Board, nextId: false);
                int nextBefore = s.Board.NextId;
                string selBefore = Sel(s);

                c.Act(s);
                Check.That(s.UndoCount == count + 1, $"{c.Name}: one step (count {count} -> {s.UndoCount})");
                Check.That(s.NextUndoLabel == c.Label && s.CanUndo && !s.CanRedo, $"{c.Name}: label '{s.NextUndoLabel}'");
                string after = Dump(s.Board, nextId: false, active: false);
                Check.That(after != Dump(Rich(), false, false) || c.Prep != null, $"{c.Name}: the action changed the board");
                EntityId afterActive = s.Board.ActiveLayerId;
                int nextAfter = s.Board.NextId;
                string selAfter = Sel(s);
                ToolId toolAfter = s.ActiveTool;
                Check.That(nextAfter >= nextBefore, $"{c.Name}: NextId grew or stayed");

                var r = new Recorder(s);
                s.Undo();
                Check.That(Dump(s.Board, nextId: false) == before,
                    $"{c.Name}: Undo restores exactly\nwant:\n{before}got:\n{Dump(s.Board, nextId: false)}");
                Check.That(s.Board.NextId == nextAfter, $"{c.Name}: NextId never decreases ({nextAfter} -> {s.Board.NextId})");
                CheckStepEvents(r, EditorSession.UndidToast(c.Label), c.Name + " undo");
                Check.That(r.Toasts[0] == "Undid: " + c.Label, "toast text");
                Check.That(s.UndoCount == count && s.RedoCount == 1 && s.NextRedoLabel == c.Label && s.CanRedo, $"{c.Name}: moved to redo");
                Check.That(s.ActiveTool == toolAfter || (toolAfter != ToolId.SelectMove && IsSelectionTool(toolAfter)),
                    $"{c.Name}: tool {toolAfter} -> {s.ActiveTool}");
                if (IsSelectionTool(s.ActiveTool))
                    Check.That(Sel(s) == selBefore, $"{c.Name}: selection restored ({selBefore}), got {Sel(s)}");
                else Check.That(s.Selection.Ids.Count == 0, $"{c.Name}: no selection in {s.ActiveTool}");

                r.Reset();
                EntityId activeBeforeRedo = s.Board.ActiveLayerId;
                s.Redo();
                Check.That(Dump(s.Board, nextId: false, active: false) == after,
                    $"{c.Name}: Redo re-applies exactly\nwant:\n{after}got:\n{Dump(s.Board, false, false)}");
                EntityId wantActive = s.Board.FindLayer(activeBeforeRedo) != null ? activeBeforeRedo : afterActive;
                Check.That(s.Board.ActiveLayerId == wantActive, $"{c.Name}: Redo keeps the current active layer when it exists");
                Check.That(s.Board.NextId == nextAfter, $"{c.Name}: NextId after Redo");
                CheckStepEvents(r, EditorSession.RedidToast(c.Label), c.Name + " redo");
                Check.That(r.Toasts[0] == "Redid: " + c.Label, "redo toast text");
                Check.That(s.UndoCount == count + 1 && s.RedoCount == 0, $"{c.Name}: back on the undo stack");
                if (IsSelectionTool(s.ActiveTool))
                    Check.That(Sel(s) == selAfter, $"{c.Name}: Redo restores the after-selection ({selAfter}), got {Sel(s)}");
            }
        }

        // ---------- 3. non-steps ----------

        public static void NonEditsAreNeverSteps()
        {
            var s = new EditorSession(Rich());
            Layer bottom = Bottom(s), top = Top(s);
            void Same(string what) => Check.That(s.UndoCount == 0 && !s.CanUndo, $"{what}: no undo step (count {s.UndoCount})");

            s.SelectLayer(bottom.Id); s.SelectLayer(top.Id); s.SelectLayer(top.Id); s.SelectLayer(new EntityId(999));
            Same("SelectLayer");
            s.SetTool(ToolId.Circle); s.SetTool(ToolId.Line); s.SetTool(ToolId.Stamps); s.SetTool(ToolId.Move);
            Same("SetTool");
            s.SetTool(ToolId.SelectMove);
            Drag(s, 0.0, 0.0, 1.0, 1.0);
            Tap(s, 0.2, 0.2);
            Tap(s, 0.95, 0.05);
            DragOpen(s, 0.0, 0.0, 0.5, 0.5);
            s.Pointer(Ev(PointerPhase.Cancel, 0.5, 0.5));
            s.SetSelection(new[] { new EntityId(3), new EntityId(5) });
            s.ClearSelection();
            Same("Select gestures, SetSelection, ClearSelection");
            s.SetTool(ToolId.Line);
            s.SetSnapEnabled(false); s.SetSnapEnabled(true);
            s.SetOrthoLock(true); s.SetOrthoLock(false);
            s.Style = Fancy; s.ToggleDottedLine(); s.ToggleDottedLine(); s.Style = St;
            Same("snap, H/V, style");
            Tap(s, 0.502, 0.497);
            Check.That(s.PendingConnectStart.HasValue, "precondition: pending start");
            Same("first tap-to-connect tap");
            Tap(s, 0.502, 0.497);   // the same candidate: toggles off
            Same("same candidate twice");
            s.SetSnapEnabled(false);
            Drag(s, 0.5, 0.5, 0.503, 0.5);
            s.SetTool(ToolId.Circle);
            Drag(s, 0.5, 0.5, 0.505, 0.5);
            Tap(s, 0.5, 0.5);
            s.SetTool(ToolId.Line);
            Same("too-short strokes and taps");
            DragOpen(s, 0.1, 0.5, 0.9, 0.5);
            s.Pointer(Ev(PointerPhase.Cancel, 0.9, 0.5));
            DragOpen(s, 0.1, 0.5, 0.9, 0.5);
            s.SetTool(ToolId.Circle);
            DragOpen(s, 0.1, 0.5, 0.9, 0.5);
            s.Pointer(Ev(PointerPhase.Down, 0.2, 0.2));   // a lost Up
            s.Pointer(Ev(PointerPhase.Cancel, 0.2, 0.2));
            Same("cancelled strokes");
            s.SetGrid(2); s.SetGridKind(GridKind.Square); s.SetGridKind((GridKind)7);
            s.SetLayerOpacity(top.Id, 0.75); s.SetLayerOpacity(top.Id, double.NaN); s.SetLayerOpacity(new EntityId(999), 0.5);
            s.SetLayerVisible(top.Id, true); s.SetLayerVisible(new EntityId(999), false);
            s.MoveLayer(top.Id, 1); s.MoveLayer(top.Id, 99); s.MoveLayer(new EntityId(999), 0);
            Same("unchanged grid, opacity, visibility, order");

            foreach (var tool in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
            {
                Select(s, tool, new EntityId(3), new EntityId(4));
                s.Pointer(Ev(PointerPhase.Down, 0.8, 0.2));
                s.Pointer(Ev(PointerPhase.Move, 0.9, 0.3));
                s.Pointer(Ev(PointerPhase.Move, 0.8, 0.2));
                s.Pointer(Ev(PointerPhase.Up, 0.8, 0.2));
                Same(tool + " identity");
                DragOpen(s, 0.8, 0.2, 0.9, 0.3);
                s.Pointer(Ev(PointerPhase.Cancel, 0.9, 0.3));
                DragOpen(s, 0.8, 0.2, 0.9, 0.3);
                s.SetTool(ToolId.SelectMove);
                Same(tool + " cancel");
                Select(s, tool, new EntityId(3));
                Tap(s, 0.7, 0.6);   // on the circle's ring: tap-pick
                Check.That(s.Selection.Ids.Contains(new EntityId(4)), $"{tool}: the tap picked the circle");
                Tap(s, 0.95, 0.05);
                Same(tool + " tap-picks");
            }
            Check.That(Dump(s.Board, nextId: false) == Dump(Rich(), nextId: false), "the board never changed");

            // Hidden active layer: refused stroke.
            var hb = Rich();
            hb.Layers[1].Visible = false;
            var h = new EditorSession(hb);
            h.SetSnapEnabled(false);
            Drag(h, 0.1, 0.5, 0.9, 0.5);
            h.SetTool(ToolId.Circle);
            Drag(h, 0.5, 0.5, 0.7, 0.5);
            Check.That(h.UndoCount == 0, "hidden-layer refusal: no step");

            // Layer cap and the only-layer delete.
            var cap = new EditorSession();
            for (int i = 0; i < EditorSession.MaxLayers - 1; i++) cap.AddLayer();
            int capSteps = cap.UndoCount;
            Check.That(capSteps == EditorSession.MaxLayers - 1, "each AddLayer was a step");
            cap.AddLayer();
            Check.That(cap.UndoCount == capSteps, "AddLayer at the cap: no step");
            var one = new EditorSession();
            one.DeleteBottomLayer();
            Check.That(one.UndoCount == 0, "only-layer delete: no step");
        }

        // ---------- 4. opacity merging ----------

        public static void LayerOpacityMergesPerPopupSession()
        {
            var s = new EditorSession(Rich());
            Layer top = Top(s), bottom = Bottom(s);
            for (int i = 1; i <= 20; i++) s.SetLayerOpacity(top.Id, i / 21.0);
            Check.That(s.UndoCount == 1 && s.NextUndoLabel == EditorSession.UndoLabels.LayerOpacity, $"20 edits: one step, got {s.UndoCount}");
            s.Undo();
            Check.That(Top(s).Opacity == 0.75, "Undo: the original opacity exactly");

            // SelectLayer (even a no-op on the active layer) ends the merge.
            s = new EditorSession(Rich());
            top = Top(s);
            s.SetLayerOpacity(top.Id, 0.5);
            s.SetLayerOpacity(top.Id, 0.4);
            s.SelectLayer(top.Id);
            s.SetLayerOpacity(top.Id, 0.3);
            s.SetLayerOpacity(top.Id, 0.2);
            Check.That(s.UndoCount == 2, $"SelectLayer between: two steps, got {s.UndoCount}");
            s.Undo();
            Check.That(Top(s).Opacity == 0.4, "first Undo: back to the end of the first run");
            s.Undo();
            Check.That(Top(s).Opacity == 0.75, "second Undo: the original");

            // Another layer splits the run.
            s = new EditorSession(Rich());
            top = Top(s); bottom = Bottom(s);
            s.SetLayerOpacity(top.Id, 0.5);
            s.SetLayerOpacity(bottom.Id, 0.5);
            s.SetLayerOpacity(top.Id, 0.25);
            Check.That(s.UndoCount == 3, $"top, bottom, top: three steps, got {s.UndoCount}");

            // Another action splits it.
            s = new EditorSession(Rich());
            s.SetSnapEnabled(false);
            top = Top(s);
            s.SetLayerOpacity(top.Id, 0.5);
            Drag(s, 0.5, 0.2, 0.8, 0.3);
            s.SetLayerOpacity(top.Id, 0.25);
            Check.That(s.UndoCount == 3, $"opacity, line, opacity: three steps, got {s.UndoCount}");

            // A refused or unchanged edit does not start a merge; Undo ends one.
            s = new EditorSession(Rich());
            top = Top(s);
            s.SetLayerOpacity(top.Id, 0.75);
            Check.That(s.UndoCount == 0, "unchanged: nothing");
            s.SetLayerOpacity(top.Id, 0.5);
            s.Undo();
            s.SetLayerOpacity(top.Id, 0.6);
            Check.That(s.UndoCount == 1 && !s.CanRedo, "after Undo an opacity edit is a new step");
            s.SetLayerOpacity(top.Id, 0.7);
            Check.That(s.UndoCount == 1, "and merges again");
            s.Redo();   // nothing to redo: ends nothing visible, toasts
            s.Undo();
            Check.That(Top(s).Opacity == 0.75, "back to the original");
            s.Redo();
            s.SetLayerOpacity(top.Id, 0.1);
            Check.That(s.UndoCount == 2, "Redo ends the merge too");
        }

        // ---------- 5. walk back and forward through everything ----------

        public static void WalkBackEverythingAndForwardAgain()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var states = new List<string> { Dump(s.Board, nextId: false, active: false) };
            var actives = new List<EntityId> { s.Board.ActiveLayerId };
            var labels = new List<string>();
            var allIds = new HashSet<int>();
            int lastNext = s.Board.NextId;

            void Collect()
            {
                foreach (var l in s.Board.Layers)
                {
                    allIds.Add(l.Id.Value);
                    foreach (var sh in l.Shapes) allIds.Add(sh.Id.Value);
                }
            }
            void Unique(string what)
            {
                var seen = new HashSet<int>();
                foreach (var l in s.Board.Layers)
                {
                    Check.That(seen.Add(l.Id.Value), $"{what}: layer id {l.Id.Value} unique");
                    foreach (var sh in l.Shapes) Check.That(seen.Add(sh.Id.Value), $"{what}: shape id {sh.Id.Value} unique");
                }
                foreach (int id in seen) Check.That(id < s.Board.NextId, $"{what}: id {id} below NextId {s.Board.NextId}");
                Check.That(s.Board.NextId >= lastNext, $"{what}: NextId never decreases ({lastNext} -> {s.Board.NextId})");
                lastNext = s.Board.NextId;
            }
            void Step(string label, Action act)
            {
                act();
                labels.Add(label);
                Check.That(s.UndoCount == labels.Count && s.NextUndoLabel == label,
                    $"action {labels.Count} ({label}): one step, count {s.UndoCount}, label {s.NextUndoLabel}");
                states.Add(Dump(s.Board, nextId: false, active: false));
                actives.Add(s.Board.ActiveLayerId);
                Collect();
                Unique(label);
            }

            EntityId l1 = s.Board.ActiveLayerId, l2 = default;
            EntityId movedLine = default;
            Step("Line", () => Drag(s, 0.1, 0.1, 0.4, 0.15));
            Step("Circle", () => { s.SetTool(ToolId.Circle); Drag(s, 0.3, 0.7, 0.4, 0.7); s.SetTool(ToolId.Line); });
            Step("Line", () =>
            {
                s.SetSnapEnabled(true);
                Tap(s, 0.502, 0.497);
                Tap(s, 0.748, 0.253);
                s.SetSnapEnabled(false);
            });
            Step("Add layer", () => l2 = s.AddLayer());
            Step("Line", () => { Drag(s, 0.6, 0.6, 0.8, 0.9); movedLine = Top(s).Shapes[0].Id; });
            Step("Hide layer", () => s.SetLayerVisible(l1, false));
            Step("Layer order", () => s.MoveLayer(l2, 0));
            Step("Layer opacity", () => { s.SetLayerOpacity(l2, 0.6); s.SetLayerOpacity(l2, 0.45); });
            Step("Grid type", () => s.SetGridKind(GridKind.Hex));
            Step("Tiles", () => s.SetGrid(3));
            Step("Move", () => { Select(s, ToolId.Move, movedLine); Drag(s, 0.2, 0.2, 0.25, 0.15); });
            Step("Scale", () => { s.SetTool(ToolId.Scale); Drag(s, 0.95, 0.75, 0.9, 0.75); });
            Step("Duplicate", () => { s.SetTool(ToolId.Duplicate); Drag(s, 0.1, 0.9, 0.05, 0.95); });
            Step("Delete layer", () => s.DeleteBottomLayer());
            Step("Clear board", () => s.ClearBoard());
            int n = labels.Count;
            Check.That(n == 15, "15 actions");
            for (int i = 1; i <= n; i++) Check.That(states[i] != states[i - 1], $"action {i} changed the board");

            var r = new Recorder(s);
            for (int k = n; k >= 1; k--)
            {
                EntityId prevActive = s.Board.ActiveLayerId;
                r.Reset();
                s.Undo();
                Check.That(Dump(s.Board, nextId: false, active: false) == states[k - 1],
                    $"Undo {n - k + 1} ({labels[k - 1]}): state {k - 1}\nwant:\n{states[k - 1]}got:\n{Dump(s.Board, false, false)}");
                EntityId want = s.Board.FindLayer(prevActive) != null ? prevActive : actives[k - 1];
                Check.That(s.Board.ActiveLayerId == want, $"Undo {labels[k - 1]}: active layer rule");
                CheckStepEvents(r, EditorSession.UndidToast(labels[k - 1]), "undo " + labels[k - 1]);
                Unique("undo " + labels[k - 1]);
            }
            Check.That(states[0] == Dump(s.Board, nextId: false, active: false) && s.Board.ActiveLayerId == l1,
                "back at the initial board");
            Check.That(!s.CanUndo && s.RedoCount == n, "everything undone, all redoable");
            r.Reset();
            s.Undo();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == EditorSession.NothingToUndoToast && r.Board.Count == 0 && r.State == 0,
                $"16th Undo: '{EditorSession.NothingToUndoToast}' only, got {r.T}");

            for (int k = 1; k <= n; k++)
            {
                EntityId prevActive = s.Board.ActiveLayerId;
                r.Reset();
                s.Redo();
                Check.That(Dump(s.Board, nextId: false, active: false) == states[k],
                    $"Redo {k} ({labels[k - 1]}): state {k}\nwant:\n{states[k]}got:\n{Dump(s.Board, false, false)}");
                EntityId want = s.Board.FindLayer(prevActive) != null ? prevActive : actives[k];
                Check.That(s.Board.ActiveLayerId == want, $"Redo {labels[k - 1]}: active layer rule");
                CheckStepEvents(r, EditorSession.RedidToast(labels[k - 1]), "redo " + labels[k - 1]);
                Unique("redo " + labels[k - 1]);
            }
            Check.That(!s.CanRedo && s.UndoCount == n, "everything redone");

            // Draw on the cleared board: the new id was never issued before.
            s.SetTool(ToolId.Line);
            Drag(s, 0.1, 0.5, 0.9, 0.55);
            Unique("draw after the walk");
            var fresh = Top(s).Shapes[Top(s).Shapes.Count - 1].Id.Value;
            Check.That(!allIds.Contains(fresh), $"the new shape id {fresh} was never issued before");
        }

        // ---------- 6. redo cleared ----------

        public static void NewActionClearsRedo()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, 0.1, 0.1, 0.4, 0.1);
            Drag(s, 0.1, 0.3, 0.4, 0.3);
            Drag(s, 0.1, 0.5, 0.4, 0.5);
            s.Undo(); s.Undo();
            Check.That(s.RedoCount == 2 && s.UndoCount == 1, "two redoable");
            Drag(s, 0.1, 0.7, 0.4, 0.7);
            Check.That(!s.CanRedo && s.RedoCount == 0 && s.UndoCount == 2, "a new stroke clears redo");
            var r = new Recorder(s);
            s.Redo();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == EditorSession.NothingToRedoToast && r.Board.Count == 0, r.T);
            Check.That(ShapeCount(s) == 2, "two lines");
        }

        // ---------- 7. ClearBoard ----------

        public static void ClearBoardIsUndoableAndNeverReusesIds()
        {
            var s = new EditorSession(Rich());
            s.SetGridKind(GridKind.Hex);
            s.SetGrid(4);
            Select(s, ToolId.SelectMove, new EntityId(3), new EntityId(5));
            string before = Dump(s.Board, nextId: false);
            int oldNext = s.Board.NextId;
            var oldIds = new HashSet<int>();
            foreach (var l in s.Board.Layers) { oldIds.Add(l.Id.Value); foreach (var sh in l.Shapes) oldIds.Add(sh.Id.Value); }

            var r = new Recorder(s);
            s.ClearBoard();
            Check.That(r.Board.Count == 1 && r.Board[0] == BoardChangeKind.Reset, "ClearBoard still raises BoardChanged(Reset)");
            Check.That(s.Board.Layers.Count == 1 && s.Board.Layers[0].Name == "Layer 1" && ShapeCount(s) == 0, "one empty Layer 1");
            Check.That(s.Board.GridKind == GridKind.Square && s.Board.GridN == 2, "2x2 square");
            Check.That(s.Board.Layers[0].Id.Value >= oldNext && !oldIds.Contains(s.Board.Layers[0].Id.Value),
                $"the fresh layer's id {s.Board.Layers[0].Id.Value} is new (old NextId {oldNext})");
            Check.That(s.Board.NextId > oldNext && s.Board.ActiveLayerId == s.Board.Layers[0].Id, "NextId kept and advanced");
            Check.That(s.Selection.Ids.Count == 0 && s.CanUndo && s.NextUndoLabel == "Clear board", "selection empty, undoable");

            r.Reset();
            s.Undo();
            Check.That(Dump(s.Board, nextId: false) == before, "Undo restores layers, shapes, ids, grid and opacity exactly");
            Check.That(Sel(s) == "3,5", $"selection restored in Select, got {Sel(s)}");
            CheckStepEvents(r, "Undid: Clear board", "undo clear");

            // Clear, draw, Undo, Undo: back past the Clear.
            s.SetTool(ToolId.Line);
            s.SetSnapEnabled(false);
            s.ClearBoard();
            Drag(s, 0.1, 0.1, 0.5, 0.5);
            s.Undo();
            Check.That(ShapeCount(s) == 0 && s.Board.Layers.Count == 1, "the stroke on the cleared board is gone");
            s.Undo();
            Check.That(Dump(s.Board, nextId: false, active: false) == Dump(Rich().DeepCloneWith(b =>
            {
                b.GridKind = GridKind.Hex; b.GridN = 4;
            }), nextId: false, active: false), "and the Clear is undone");

            // The existing expectations still hold.
            var g = new EditorSession();
            g.AddLayer(); g.AddLayer();
            g.ClearBoard();
            g.AddLayer();
            Check.That(g.Board.Layers[0].Name == "Layer 1" && g.Board.Layers[1].Name == "Layer 2", "naming restarts on the fresh board");
        }

        static Board DeepCloneWith(this Board b, Action<Board> edit)
        {
            var c = b.DeepClone();
            edit(c);
            return c;
        }

        // ---------- 8. LoadBoard ----------

        public static void LoadBoardResetsHistoryBeforeItsEvents()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, 0.1, 0.1, 0.4, 0.1);
            Drag(s, 0.1, 0.3, 0.4, 0.3);
            s.Undo();
            Check.That(s.CanUndo && s.CanRedo, "precondition: both stacks");
            bool sawReset = false;
            s.BoardChanged += k =>
            {
                if ((k & BoardChangeKind.Reset) == 0) return;
                sawReset = true;
                Check.That(!s.CanUndo && !s.CanRedo, "BoardChanged(Reset): history already cleared");
            };
            s.LoadBoard(GoldenBoards.StarOnFullBoard());
            Check.That(sawReset && !s.CanUndo && !s.CanRedo && s.UndoCount == 0 && s.RedoCount == 0, "LoadBoard clears both");
            var r = new Recorder(s);
            s.Undo();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == EditorSession.NothingToUndoToast, r.T);
        }

        // ---------- 9. ids ----------

        public static void IdsAreNeverReissued()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, 0.1, 0.1, 0.4, 0.1);
            EntityId a = s.ActiveLayer.Shapes[0].Id;
            s.Undo();
            Drag(s, 0.1, 0.3, 0.4, 0.3);
            EntityId b = s.ActiveLayer.Shapes[0].Id;
            Check.That(b != a && b.Value >= a.Value + 1, $"draw, Undo, draw: new id {b.Value} (old {a.Value})");

            EntityId la = s.AddLayer();
            s.Undo();
            EntityId lb = s.AddLayer();
            Check.That(lb != la && lb.Value > la.Value, $"AddLayer, Undo, AddLayer: new id {lb.Value} (old {la.Value})");

            // Redo after more ids were issued keeps the larger NextId.
            s.Undo();
            int next = s.Board.NextId;
            s.Redo();
            Check.That(s.Board.NextId >= next, "Redo never lowers NextId");
        }

        // ---------- 10. snap index ----------

        public static void SnapCandidatesFollowTheRestoredBoard()
        {
            var a = new Vec2(0.13, 0.11);
            var b = new Vec2(0.41, 0.37);
            var empty = new EditorSession();
            Check.That(!HasCandidate(empty, a) && !HasCandidate(empty, b), "precondition: not grid candidates");

            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, a.X, a.Y, b.X, b.Y);
            s.SetSnapEnabled(true);
            Check.That(HasCandidate(s, a) && HasCandidate(s, b), "the line's endpoints are candidates");
            s.Undo();
            Check.That(!HasCandidate(s, a) && !HasCandidate(s, b), "Undo: gone");
            s.Redo();
            Check.That(HasCandidate(s, a) && HasCandidate(s, b), "Redo: back");

            EntityId id = s.ActiveLayer.Shapes[0].Id;
            s.SetSnapEnabled(false);
            Select(s, ToolId.Move, id);
            Drag(s, 0.8, 0.8, 0.85, 0.83);
            var moved = (LineShape)FindShape(s.Board, id);
            s.SetSnapEnabled(true);
            Check.That(HasCandidate(s, moved.A) && !HasCandidate(s, a), "moved: new endpoints");
            s.Undo();
            Check.That(HasCandidate(s, a) && HasCandidate(s, b) && !HasCandidate(s, moved.A), "Undo of the Move: old endpoints are candidates again");
        }

        // ---------- 11. selection ----------

        public static void SelectionIsRestoredOnlyInSelectionTools()
        {
            // Select + Move + Undo.
            var s = new EditorSession(Rich());
            s.SetSnapEnabled(false);
            Select(s, ToolId.Move, new EntityId(3), new EntityId(4));
            Drag(s, 0.45, 0.05, 0.55, 0.1);
            s.Undo();
            Check.That(Sel(s) == "3,4" && s.ActiveTool == ToolId.Move, $"Move undone: still selected, still Move ({Sel(s)}, {s.ActiveTool})");
            var line = (LineShape)FindShape(s.Board, new EntityId(3));
            var circle = (CircleShape)FindShape(s.Board, new EntityId(4));
            Check.That(line.A == new Vec2(0.1, 0.1) && line.B == new Vec2(0.3, 0.3) && circle.Center == new Vec2(0.6, 0.6), "old coordinates ==");

            // Duplicate + Undo.
            s = new EditorSession(Rich());
            s.SetSnapEnabled(false);
            Select(s, ToolId.Duplicate, new EntityId(3), new EntityId(5));
            Drag(s, 0.9, 0.05, 0.95, 0.07);
            Check.That(ShapeCount(s) == 6 && s.ActiveTool == ToolId.Move && Sel(s) != "3,5", "precondition: copies selected, Move");
            s.Undo();
            Check.That(ShapeCount(s) == 4 && Sel(s) == "3,5" && s.ActiveTool == ToolId.Move, $"copies gone, originals selected, Move ({Sel(s)})");

            // Line tool: the step had a selection, the restored selection stays empty.
            s = new EditorSession(Rich());
            s.SetSnapEnabled(false);
            Select(s, ToolId.Move, new EntityId(3));
            Drag(s, 0.45, 0.05, 0.55, 0.1);
            s.SetTool(ToolId.Line);
            s.Undo();
            Check.That(s.Selection.Ids.Count == 0 && s.ActiveTool == ToolId.Line, "Line tool: no selection after Undo");

            // Move with an empty stored selection falls back to Select with one StateChanged.
            s = new EditorSession(Rich());
            s.SetSnapEnabled(false);
            Drag(s, 0.5, 0.2, 0.8, 0.3);
            Select(s, ToolId.Move, new EntityId(3));
            var r = new Recorder(s);
            s.Undo();
            Check.That(s.Selection.Ids.Count == 0 && s.ActiveTool == ToolId.SelectMove && r.State == 1,
                $"empty restored selection in Move: Select, one StateChanged (tool {s.ActiveTool}, {r.State})");
            Check.That(s.ToolHint == EditorSession.SelectHintToast, "Select's hint");

            // Hide (prunes the selection) then Undo: selected again.
            s = new EditorSession(Rich());
            Select(s, ToolId.SelectMove, new EntityId(3), new EntityId(5));
            s.SetLayerVisible(Bottom(s).Id, false);
            Check.That(Sel(s) == "5", "hidden layer's shape pruned");
            s.Undo();
            Check.That(Sel(s) == "3,5" && Bottom(s).Visible, "Undo of the hide: visible and selected again");
        }

        // ---------- 12. active layer ----------

        public static void ActiveLayerIsKeptWhenItStillExists()
        {
            var s = new EditorSession(Rich());
            EntityId was = s.Board.ActiveLayerId;
            s.AddLayer();
            s.Undo();
            Check.That(s.Board.ActiveLayerId == was, "AddLayer undone: the previous layer is active");

            s = new EditorSession(Rich());
            s.SetSnapEnabled(false);
            EntityId l1 = Bottom(s).Id, l2 = Top(s).Id;
            s.SelectLayer(l1);
            Drag(s, 0.5, 0.2, 0.8, 0.3);
            Check.That(Bottom(s).Shapes.Count == 3, "precondition: drawn on L1");
            s.SelectLayer(l2);
            s.Undo();
            Check.That(Bottom(s).Shapes.Count == 2 && s.Board.ActiveLayerId == l2, "Undo keeps the user's current layer (L2)");
        }

        // ---------- 13. open gestures ----------

        public static void UndoCancelsAnOpenGesture()
        {
            foreach (var kind in new[] { "line", "circle", "select", "move", "scale", "duplicate" })
            {
                var s = new EditorSession(Rich());
                s.SetSnapEnabled(false);
                Drag(s, 0.5, 0.2, 0.8, 0.25);   // the step Undo will revert
                EntityId last = Top(s).Shapes[Top(s).Shapes.Count - 1].Id;
                s.SetSnapEnabled(true);
                var line = (LineShape)FindShape(s.Board, new EntityId(3));
                Vec2 a = line.A, b = line.B;
                switch (kind)
                {
                    case "line": DragOpen(s, 0.1, 0.5, 0.9, 0.55); break;
                    case "circle": s.SetTool(ToolId.Circle); DragOpen(s, 0.4, 0.4, 0.5, 0.45); break;
                    case "select": s.SetTool(ToolId.SelectMove); DragOpen(s, 0.0, 0.0, 0.7, 0.7); break;
                    case "move": Select(s, ToolId.Move, line.Id); DragOpen(s, 0.12, 0.1, 0.5, 0.3); break;
                    case "scale": Select(s, ToolId.Scale, line.Id); DragOpen(s, 0.35, 0.35, 0.45, 0.45); break;
                    case "duplicate": Select(s, ToolId.Duplicate, line.Id); DragOpen(s, 0.12, 0.1, 0.5, 0.3); break;
                }
                Check.That(s.GestureActive && s.Preview.Count > 0, $"{kind}: precondition: open gesture with visuals");
                if (kind == "move" || kind == "scale")
                    Check.That(!HasCandidate(s, a), $"{kind}: precondition: own endpoints excluded");
                int count = s.UndoCount;
                var r = new Recorder(s);
                s.Undo();
                Check.That(!s.GestureActive && s.Preview.Count == 0 && !s.Cursor.HasValue, $"{kind}: gesture closed, no visuals");
                Check.That(FindShape(s.Board, last) == null && s.UndoCount == count - 1, $"{kind}: the previous action is undone");
                Check.That(r.Preview >= 1 && r.Seq.IndexOf('P') < r.Seq.IndexOf('B'), $"{kind}: PreviewChanged before BoardChanged ({r.Seq})");
                Check.That(r.Board.Count == 1 && r.State == 1, $"{kind}: one BoardChanged, one StateChanged ({r.Seq})");
                Check.That(HasCandidate(s, a) && HasCandidate(s, b), $"{kind}: own candidates back (exclusion lifted)");
                string dump = Dump(s.Board);
                s.Pointer(Ev(PointerPhase.Move, 0.6, 0.6));
                s.Pointer(Ev(PointerPhase.Up, 0.6, 0.6));
                Check.That(Dump(s.Board) == dump && s.UndoCount == count - 1, $"{kind}: a stray Up commits nothing");
            }
        }

        // ---------- 14. pending tap-to-connect ----------

        public static void UndoClearsThePendingConnectStart()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, 0.1, 0.1, 0.4, 0.15);
            s.SetSnapEnabled(true);
            Tap(s, 0.502, 0.497);
            Check.That(s.PendingConnectStart.HasValue && s.Preview.Count == 1, "precondition: pending start with its ring");
            var r = new Recorder(s);
            s.Undo();
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "pending start and ring gone");
            Check.That(r.Preview == 1 && r.Seq == "PBST", $"PreviewChanged, BoardChanged, StateChanged, Toast ({r.Seq})");
            Check.That(ShapeCount(s) == 0, "and the line is undone");
            Tap(s, 0.748, 0.253);
            Check.That(ShapeCount(s) == 0 && s.PendingConnectStart.HasValue, "the next tap starts afresh");
        }

        // ---------- 15. cap ----------

        public static void HistoryKeepsTheLast200Steps()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            for (int i = 0; i < 205; i++)
            {
                double y = 0.02 + i * 0.0045;
                Drag(s, 0.1, y, 0.9, y, 2);
            }
            Check.That(ShapeCount(s) == 205 && s.UndoCount == EditorSession.MaxUndoSteps && EditorSession.MaxUndoSteps == 200,
                $"205 strokes: 200 steps kept, got {s.UndoCount}");
            for (int i = 0; i < 200; i++) s.Undo();
            Check.That(ShapeCount(s) == 5 && !s.CanUndo && s.RedoCount == 200, $"200 Undos: the state after action 5 ({ShapeCount(s)} shapes)");
            var r = new Recorder(s);
            s.Undo();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == EditorSession.HistoryLimitToast
                && r.Toasts[0] == "Nothing more to undo: only the last 200 actions are kept" && r.Board.Count == 0 && r.State == 0, r.T);
            s.Redo();
            Drag(s, 0.1, 0.99, 0.9, 0.99, 2);
            Check.That(s.UndoCount == 2 && s.RedoCount == 0, "redo + a new stroke: two steps, redo cleared");
            s.LoadBoard(GoldenBoards.Empty());
            r.Reset();
            s.Undo();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == EditorSession.NothingToUndoToast, $"LoadBoard resets the trimmed flag: {r.T}");
        }

        // ---------- 16. snapshot isolation ----------

        public static void StoredStepsAreNeverAliasedToTheLiveBoard()
        {
            var s = new EditorSession(Rich());
            s.SetSnapEnabled(false);
            var id = new EntityId(3);
            string Pos() { var l = (LineShape)FindShape(s.Board, id); return P(l.A) + P(l.B); }
            string p0 = Pos();
            Select(s, ToolId.Move, id);
            Drag(s, 0.45, 0.05, 0.55, 0.1);
            string p1 = Pos();
            Drag(s, 0.45, 0.05, 0.5, 0.2);
            string p2 = Pos();
            Check.That(p0 != p1 && p1 != p2, "precondition: two moves");
            s.Undo();
            Check.That(Pos() == p1, "Undo: after the first move");
            s.Undo();
            Check.That(Pos() == p0, "Undo twice: the original doubles");

            // A board that came back from a stack and was edited must not corrupt the steps still stored.
            s.Redo();
            s.Redo();
            Check.That(Pos() == p2, "Redo twice");
            s.Undo();
            s.Redo();
            Drag(s, 0.45, 0.05, 0.4, 0.1);
            string p3 = Pos();
            Check.That(p3 != p2, "third move");
            s.Undo();
            Check.That(Pos() == p2, "Undo the third move");
            s.Undo();
            Check.That(Pos() == p1, "then the second");
            s.Undo();
            Check.That(Pos() == p0, "then the first: the pre-first-Move doubles");

            // DeepClone shares nothing and keeps null entries.
            var b = Rich();
            b.Layers[0].Shapes.Add(null);
            b.Layers[1].SnapPoints.Add(null);
            var c = b.DeepClone();
            Check.That(Dump(c) == Dump(b), "DeepClone copies every field");
            Check.That(!ReferenceEquals(c, b) && !ReferenceEquals(c.Layers, b.Layers) && c.Layers.Count == b.Layers.Count, "new board and list");
            for (int i = 0; i < b.Layers.Count; i++)
            {
                Layer bl = b.Layers[i], cl = c.Layers[i];
                Check.That(!ReferenceEquals(bl, cl) && !ReferenceEquals(bl.Shapes, cl.Shapes) && !ReferenceEquals(bl.SnapPoints, cl.SnapPoints),
                    $"layer {i}: new layer and lists");
                Check.That(bl.Shapes.Count == cl.Shapes.Count && bl.SnapPoints.Count == cl.SnapPoints.Count, $"layer {i}: same counts");
                for (int j = 0; j < bl.Shapes.Count; j++)
                    Check.That(bl.Shapes[j] == null ? cl.Shapes[j] == null : !ReferenceEquals(bl.Shapes[j], cl.Shapes[j]),
                        $"layer {i} shape {j}: a new instance (null kept)");
                for (int j = 0; j < bl.SnapPoints.Count; j++)
                    Check.That(bl.SnapPoints[j] == null ? cl.SnapPoints[j] == null : !ReferenceEquals(bl.SnapPoints[j], cl.SnapPoints[j]),
                        $"layer {i} snap point {j}: a new instance (null kept)");
            }
            ((LineShape)c.Layers[0].Shapes[0]).A = new Vec2(0.9, 0.9);
            c.Layers[1].Opacity = 0.1;
            c.NextId = 99;
            Check.That(Dump(b) == Dump(Rich().DeepCloneWith(x => { x.Layers[0].Shapes.Add(null); x.Layers[1].SnapPoints.Add(null); })),
                "editing the clone leaves the source untouched");
        }

        // ---------- 17. event counts ----------

        public static void NormalEditsKeepTheirEventCounts()
        {
            var s = new EditorSession(Rich());
            s.SetSnapEnabled(false);
            var r = new Recorder(s);
            bool canUndoInHandler = false;
            s.BoardChanged += _ => canUndoInHandler = s.CanUndo;
            Drag(s, 0.5, 0.2, 0.8, 0.3);
            Check.That(r.Board.Count == 1 && r.Board[0] == BoardChangeKind.Shapes && r.State == 0,
                $"Line commit: one BoardChanged(Shapes), no StateChanged ({r.Seq})");
            Check.That(canUndoInHandler, "CanUndo is already true inside BoardChanged");

            r.Reset();
            s.AddLayer();
            Check.That(r.Board.Count == 1 && r.Board[0] == BoardChangeKind.Layers && r.State == 1, $"AddLayer: B(Layers) + S ({r.Seq})");

            r.Reset();
            s.SetGrid(3);
            Check.That(r.Board.Count == 1 && r.Board[0] == BoardChangeKind.Grid && r.State == 0, $"SetGrid: B(Grid) only ({r.Seq})");

            r.Reset();
            s.SetLayerOpacity(Top(s).Id, 0.5);
            s.SetLayerOpacity(Top(s).Id, 0.4);
            Check.That(r.Board.Count == 2 && r.State == 0, $"opacity: one B(Layers) per call, no StateChanged ({r.Seq})");

            Select(s, ToolId.Duplicate, new EntityId(3), new EntityId(5));
            r.Reset();
            Drag(s, 0.9, 0.05, 0.95, 0.07);
            Check.That(r.Board.Count == 1 && r.State == 1, $"Duplicate: one BoardChanged, one StateChanged ({r.Seq})");

            r.Reset();
            s.Undo();
            Check.That(canUndoInHandler == s.CanUndo, "Undo: CanUndo already updated inside BoardChanged");
        }

        // ---------- 18. allocation ----------

        sealed class AffineMetric : ISnapMetric
        {
            public double Scale = 1000, OffX = 37, OffY = -12, Tol = 24;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                screenPx = new Vec2(board.X * Scale + OffX, board.Y * Scale + OffY);
                return true;
            }
        }

        static int s_events;

        public static void MovePathAllocatesNothingWithHistory()
        {
            var moves = new PointerEvent[200];
            for (int i = 0; i < moves.Length; i++)
            {
                double t = i / 199.0;
                moves[i] = new PointerEvent(PointerPhase.Move, new Vec2(0.5 + 0.35 * Math.Cos(7 * t), 0.5 + 0.35 * Math.Sin(5 * t)), true, i);
            }
            foreach (bool withMetric in new[] { false, true })
                foreach (var tool in new[] { ToolId.Line, ToolId.Move })
                {
                    var s = new EditorSession(GoldenBoards.StarOnFullBoard());
                    if (withMetric) s.SnapMetric = new AffineMetric();
                    s.PreviewChanged += () => s_events++;
                    s.StateChanged += () => s_events++;
                    Drag(s, 0.2, 0.3, 0.7, 0.6, 20);
                    EntityId drawn = s.ActiveLayer.Shapes[s.ActiveLayer.Shapes.Count - 1].Id;
                    s.SetGrid(3);
                    s.AddLayer();
                    s.Undo();
                    Check.That(s.UndoCount == 2 && s.RedoCount == 1, "precondition: history on both stacks");
                    if (tool == ToolId.Move) Select(s, ToolId.Move, drawn);   // the star spans the board: it could not move
                    for (int round = 0; round < 2; round++)   // warm-up, then measured
                    {
                        int count = s.UndoCount;
                        s.Pointer(Ev(PointerPhase.Down, moves[0].BoardPos.X, moves[0].BoardPos.Y));
                        for (int i = 1; i < 3; i++) s.Pointer(moves[i]);
                        int before = s_events;
                        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                        for (int i = 3; i < moves.Length; i++) s.Pointer(moves[i]);
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
                        int events = s_events - before;
                        Check.That(s.UndoCount == count, $"{tool}: no step before Up");
                        s.Pointer(Ev(PointerPhase.Up, moves[moves.Length - 1].BoardPos.X, moves[moves.Length - 1].BoardPos.Y));
                        Check.That(s.UndoCount == Math.Min(count + 1, EditorSession.MaxUndoSteps), $"{tool}: one step on Up");
                        if (round == 0) continue;
                        Check.That(events > 100, $"{tool}: the moves did work ({events} events; snapped repeats draw nothing)");
                        Check.That(bytes == 0, $"{tool}, metric {withMetric}: 197 Moves allocated {bytes} bytes with history");
                    }
                }
        }
    }
}
