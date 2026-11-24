using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// VERIFIER: Undo / Redo (p3-undo-briefs B1, B2), written from the spec only. Board snapshots compared with an exact
    /// serialiser (every field, doubles as "R"): one step per action type with exact restore and exact Redo, a seeded
    /// 60-action script undone and redone completely, the empty start and "Nothing to undo", ClearBoard (undoable, fresh
    /// ids), LoadBoard (history reset), ids never reissued, snap candidates after Undo, the selection and active-layer
    /// rules, Undo during every kind of open gesture, the pending tap-to-connect start, the 200-step cap and its toast,
    /// non-steps, opacity coalescing, event counts (one BoardChanged(History), one StateChanged, one toast; edits keep
    /// their old counts; CanUndo already updated inside BoardChanged), snapshot isolation, and allocation-free Moves.
    /// Undo and Redo keep NextId (ids are never reissued), so "restores the board exactly" compares everything except
    /// NextId, which is asserted separately.
    /// </summary>
    public static class VerifierUndoChecks
    {
        // Spec values (B1.12), restated so the checks do not read them from the code under test.
        const int Cap = 200;
        const string NothingToUndo = "Nothing to undo";
        const string NothingToRedo = "Nothing to redo";
        const string HistoryLimit = "Nothing more to undo: only the last 200 actions are kept";
        const string LLine = "Line", LCircle = "Circle", LMove = "Move", LScale = "Scale", LDup = "Duplicate",
            LAddLayer = "Add layer", LDeleteLayer = "Delete layer", LLayerOrder = "Layer order", LHide = "Hide layer",
            LShow = "Show layer", LOpacity = "Layer opacity", LGridType = "Grid type", LTiles = "Tiles", LClear = "Clear board";
        const string SelectHint = "Select: drag a box around whole shapes, or tap one";

        static readonly BoardChangeKind HistoryKind = BoardChangeKind.Shapes | BoardChangeKind.Layers
            | BoardChangeKind.SnapPoints | BoardChangeKind.Grid | (BoardChangeKind)32
            | (BoardChangeKind)64;   // Fills (P6): Undo/Redo also restore fills

        // ================= helpers =================

        static double s_T;
        static PointerEvent Ev(PointerPhase p, Vec2 v, bool on = true) => new PointerEvent(p, v, on, s_T += 0.016);
        static Vec2 V(double x, double y) => new Vec2(x, y);
        static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        static string P(Vec2 v) => "(" + R(v.X) + ", " + R(v.Y) + ")";

        static void Down(EditorSession s, Vec2 p) => s.Pointer(Ev(PointerPhase.Down, p));
        static void MoveTo(EditorSession s, Vec2 p) => s.Pointer(Ev(PointerPhase.Move, p));
        static void Up(EditorSession s, Vec2 p) => s.Pointer(Ev(PointerPhase.Up, p));
        static void Cancel(EditorSession s) => s.Pointer(Ev(PointerPhase.Cancel, V(0, 0), false));

        static void DragNoUp(EditorSession s, Vec2 a, Vec2 b, int steps = 6)
        {
            Down(s, a);
            for (int i = 1; i < steps; i++)
            {
                double k = (double)i / steps;
                MoveTo(s, V(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k));
            }
            MoveTo(s, b);
        }

        static void Drag(EditorSession s, Vec2 a, Vec2 b, int steps = 6)
        {
            DragNoUp(s, a, b, steps);
            Up(s, b);
        }

        static void Tap(EditorSession s, Vec2 p)
        {
            Down(s, p);
            Up(s, p);
        }

        static LineShape AddLine(Board b, Layer l, double ax, double ay, double bx, double by, StrokeStyle? st = null)
        {
            var sh = new LineShape(b.NewId(), V(ax, ay), V(bx, by), st ?? StrokeStyle.Default);
            l.Shapes.Add(sh);
            return sh;
        }

        static CircleShape AddCircle(Board b, Layer l, double cx, double cy, double r, StrokeStyle? st = null)
        {
            var sh = new CircleShape(b.NewId(), V(cx, cy), r, st ?? StrokeStyle.Default);
            l.Shapes.Add(sh);
            return sh;
        }

        static PolygonShape AddPoly(Board b, Layer l, double cx, double cy, int sides, double r, double rot, StrokeStyle? st = null)
        {
            var sh = new PolygonShape(b.NewId(), V(cx, cy), sides, r, rot, st ?? StrokeStyle.Default);
            l.Shapes.Add(sh);
            return sh;
        }

        static readonly StrokeStyle RedThickDotted = new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted);
        static readonly StrokeStyle BlueThin = new StrokeStyle(Palette.Blue, LineWidth.Thin, LineStyle.Solid);

        /// <summary>Select exactly <paramref name="shapes"/> (Select + SetSelection) and enter tool t.</summary>
        static void Enter(EditorSession s, ToolId t, params Shape[] shapes)
        {
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(IdsOf(shapes));
            Check.That(s.SelectionCount == shapes.Length, $"precondition: {shapes.Length} selected, got {s.SelectionCount}");
            s.SetTool(t);
            Check.That(s.ActiveTool == t, $"precondition: tool {t} accepted with a selection, got {s.ActiveTool}");
        }

        static List<EntityId> IdsOf(params Shape[] shapes)
        {
            var l = new List<EntityId>();
            foreach (var sh in shapes) l.Add(sh.Id);
            return l;
        }

        static string IdStr(IEnumerable<EntityId> ids)
        {
            var l = new List<int>();
            foreach (var id in ids) l.Add(id.Value);
            l.Sort();
            return "{" + string.Join(",", l) + "}";
        }

        static void Selected(EditorSession s, string what, IEnumerable<EntityId> want)
        {
            var w = new HashSet<EntityId>(want);
            Check.That(s.Selection.Ids.SetEquals(w), $"{what}: selection {IdStr(s.Selection.Ids)}, expected {IdStr(w)}");
        }

        static Shape FindShape(Board b, EntityId id)
        {
            foreach (var l in b.Layers)
                if (l != null)
                    foreach (var sh in l.Shapes)
                        if (sh != null && sh.Id == id) return sh;
            return null;
        }

        static int ShapeCount(Board b)
        {
            int n = 0;
            foreach (var l in b.Layers) if (l != null) n += l.Shapes.Count;
            return n;
        }

        static bool HasCand(EditorSession s, Vec2 p)
        {
            var list = s.SnapCandidates;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Position == p) return true;
            return false;
        }

        static bool HasPendingRing(EditorSession s)
        {
            foreach (var sh in s.Preview)
                if (sh is CircleShape c && c.Radius == EditorSession.HoverRingRadius) return true;
            return false;
        }

        // ---------- exact board serialiser ----------

        /// <summary>
        /// Everything on the board, doubles as "R": grid, active layer, NextId, and per layer id, name, visibility, opacity,
        /// every shape (type, kind, id, style, every geometry field) and every snap point, in order; null entries kept.
        /// </summary>
        static string Dump(Board b, bool nextId = true, bool active = true)
        {
            var sb = new StringBuilder();
            sb.Append("grid ").Append(b.GridN).Append(' ').Append(b.GridKind);
            if (active) sb.Append(" active ").Append(b.ActiveLayerId.Value);
            if (nextId) sb.Append(" next ").Append(b.NextId);
            sb.Append(" layers ").Append(b.Layers.Count).Append('\n');
            foreach (var l in b.Layers)
            {
                if (l == null) { sb.Append("L null\n"); continue; }
                sb.Append("L ").Append(l.Id.Value).Append(" '").Append(l.Name).Append("' ").Append(l.Visible ? "visible" : "hidden")
                  .Append(" op ").Append(R(l.Opacity)).Append(" shapes ").Append(l.Shapes.Count)
                  .Append(" snaps ").Append(l.SnapPoints.Count).Append('\n');
                foreach (var sh in l.Shapes)
                {
                    sb.Append("  ");
                    AppendShape(sb, sh);
                    sb.Append('\n');
                }
                foreach (var p in l.SnapPoints)
                    sb.Append("  P ").Append(p == null ? "null" : p.Id.Value + " " + P(p.Position)).Append('\n');
            }
            return sb.ToString();
        }

        static void AppendShape(StringBuilder sb, Shape sh)
        {
            if (sh == null) { sb.Append("null"); return; }
            StrokeStyle st = sh.Style;
            sb.Append(sh.GetType().Name).Append(' ').Append(sh.Kind).Append(" #").Append(sh.Id.Value)
              .Append(" rgba ").Append(st.Color.R).Append(',').Append(st.Color.G).Append(',').Append(st.Color.B).Append(',')
              .Append(st.Color.A).Append(' ').Append(st.Width).Append(' ').Append(st.Dash);
            switch (sh)
            {
                case LineShape l:
                    sb.Append(" A ").Append(P(l.A)).Append(" B ").Append(P(l.B));
                    break;
                case CircleShape c:
                    sb.Append(" C ").Append(P(c.Center)).Append(" r ").Append(R(c.Radius));
                    break;
                case PolygonShape g:
                    sb.Append(" C ").Append(P(g.Center)).Append(" n ").Append(g.Sides).Append(" r ").Append(R(g.Radius))
                      .Append(" rot ").Append(R(g.RotationRad));
                    break;
            }
        }

        /// <summary>Dump without NextId and without the active layer (both checked separately where they matter).</summary>
        static string DumpNA(Board b) => Dump(b, false, false);

        static void SameDump(string actual, string expected, string what)
        {
            if (actual == expected) return;
            string[] a = actual.Split('\n'), e = expected.Split('\n');
            int i = 0;
            while (i < a.Length && i < e.Length && a[i] == e[i]) i++;
            throw new Exception($"{what}: boards differ at line {i}: expected '{(i < e.Length ? e[i] : "<end>")}', " +
                                $"got '{(i < a.Length ? a[i] : "<end>")}'");
        }

        static List<int> AllIds(Board b)
        {
            var ids = new List<int>();
            foreach (var l in b.Layers)
            {
                if (l == null) continue;
                ids.Add(l.Id.Value);
                foreach (var sh in l.Shapes) if (sh != null) ids.Add(sh.Id.Value);
                foreach (var p in l.SnapPoints) if (p != null) ids.Add(p.Id.Value);
            }
            return ids;
        }

        static HashSet<int> LayerIds(Board b)
        {
            var ids = new HashSet<int>();
            foreach (var l in b.Layers) if (l != null) ids.Add(l.Id.Value);
            return ids;
        }

        /// <summary>NextId never decreases; every id is unique, positive and below NextId.</summary>
        static void CheckIds(EditorSession s, ref int lastNext, string what)
        {
            Board b = s.Board;
            Check.That(b.NextId >= lastNext, $"{what}: NextId decreased {lastNext} -> {b.NextId}");
            lastNext = b.NextId;
            var seen = new HashSet<int>();
            foreach (int id in AllIds(b))
            {
                Check.That(id > 0 && id < b.NextId, $"{what}: id {id} outside (0, NextId {b.NextId})");
                Check.That(seen.Add(id), $"{what}: id {id} appears twice on the board");
            }
        }

        /// <summary>Selected ids are on visible layers; Line/Circle never hold a selection; a transform tool never has an empty one.</summary>
        static void CheckSelection(EditorSession s, string what)
        {
            foreach (var id in s.Selection.Ids)
            {
                bool ok = false;
                foreach (var l in s.Board.Layers)
                    if (l != null && l.Visible)
                        foreach (var sh in l.Shapes)
                            if (sh != null && sh.Id == id) ok = true;
                Check.That(ok, $"{what}: selected id {id} is not a shape on a visible layer");
            }
            ToolId t = s.ActiveTool;
            if (t == ToolId.Line || t == ToolId.Circle)
                Check.That(s.SelectionCount == 0, $"{what}: {t} with a selection of {s.SelectionCount}");
            if (t == ToolId.Move || t == ToolId.Scale || t == ToolId.Duplicate)
                Check.That(s.SelectionCount > 0, $"{what}: {t} with an empty selection (should fall back to Select)");
        }

        /// <summary>Counts every session event; Log keeps the order (B, S, P, T). AtBoard: history state seen inside BoardChanged.</summary>
        sealed class Rec
        {
            public int States, Previews;
            public readonly List<BoardChangeKind> Boards = new List<BoardChangeKind>();
            public readonly List<string> Toasts = new List<string>();
            public readonly List<string> Log = new List<string>();
            public readonly List<(bool canUndo, bool canRedo, int undo, int redo)> AtBoard =
                new List<(bool canUndo, bool canRedo, int undo, int redo)>();

            public Rec(EditorSession s)
            {
                s.StateChanged += () => { States++; Log.Add("S"); };
                s.PreviewChanged += () => { Previews++; Log.Add("P"); };
                s.BoardChanged += k =>
                {
                    Boards.Add(k);
                    AtBoard.Add((s.CanUndo, s.CanRedo, s.UndoCount, s.RedoCount));
                    Log.Add("B");
                };
                s.Toast += t => { Toasts.Add(t); Log.Add("T"); };
            }

            public void Reset()
            {
                States = Previews = 0;
                Boards.Clear();
                Toasts.Clear();
                Log.Clear();
                AtBoard.Clear();
            }

            public string T => "[" + string.Join(" | ", Toasts) + "]";
            public string B => "[" + string.Join(" | ", Boards) + "]";

            /// <summary>The order of B, S and T (PreviewChanged left out).</summary>
            public string Order
            {
                get
                {
                    var l = new List<string>();
                    foreach (var x in Log) if (x != "P") l.Add(x);
                    return string.Join(",", l);
                }
            }
        }

        /// <summary>A real Undo/Redo: exactly one BoardChanged(HistoryChangeKind), one StateChanged, one toast, in that order.</summary>
        static void OneHistoryEvent(Rec r, string toast, string what)
        {
            Check.That(r.Boards.Count == 1, $"{what}: exactly one BoardChanged, got {r.B}");
            Check.That(r.Boards[0] == EditorSession.HistoryChangeKind, $"{what}: BoardChanged({r.Boards[0]}), expected HistoryChangeKind");
            Check.That(r.States == 1, $"{what}: exactly one StateChanged, got {r.States}");
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == toast, $"{what}: toast '{toast}', got {r.T}");
            Check.That(r.Order == "B,S,T", $"{what}: order BoardChanged, StateChanged, Toast; got {r.Order}");
        }

        /// <summary>Undo/Redo with nothing to step: exactly that toast and no other event; the board instance is untouched.</summary>
        static void OnlyToast(EditorSession s, Rec r, Action call, string toast, string what)
        {
            Board b = s.Board;
            string d = Dump(b);
            r.Reset();
            call();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == toast, $"{what}: toast '{toast}', got {r.T}");
            Check.That(r.Boards.Count == 0 && r.States == 0 && r.Previews == 0,
                $"{what}: no other event, got B {r.B} S {r.States} P {r.Previews}");
            Check.That(ReferenceEquals(s.Board, b), $"{what}: the board instance is unchanged");
            SameDump(Dump(s.Board), d, what);
        }

        // ---------- fixture ----------

        /// <summary>
        /// Three layers (Bottom: red dotted line, blue circle, pentagon, a user snap point, opacity 0.75; Hidden: a line;
        /// Top: a line and a circle, active), GridN 3, Square. History empty.
        /// </summary>
        sealed class Fx
        {
            public EditorSession S;
            public Layer Bottom, Hidden, Top;
            public LineShape L1, LH, LT;
            public CircleShape C1, CT;
            public PolygonShape G;
            public SnapPoint SP;
        }

        static Fx Fixture(bool snap = false)
        {
            var f = new Fx();
            var b = new Board { GridN = 3 };
            f.Bottom = new Layer(b.NewId(), "Bottom") { Opacity = 0.75 };
            f.Hidden = new Layer(b.NewId(), "Hidden") { Visible = false };
            f.Top = new Layer(b.NewId(), "Top");
            b.Layers.Add(f.Bottom);
            b.Layers.Add(f.Hidden);
            b.Layers.Add(f.Top);
            b.ActiveLayerId = f.Top.Id;
            f.L1 = AddLine(b, f.Bottom, 0.125, 0.125, 0.25, 0.1875, RedThickDotted);
            f.C1 = AddCircle(b, f.Bottom, 0.375, 0.25, 0.0625, BlueThin);
            f.G = AddPoly(b, f.Bottom, 0.625, 0.25, 5, 0.0625, 0.25, BlueThin);
            f.SP = new SnapPoint(b.NewId(), V(0.8125, 0.0625));
            f.Bottom.SnapPoints.Add(f.SP);
            f.LH = AddLine(b, f.Hidden, 0.5, 0.5, 0.75, 0.5);
            f.LT = AddLine(b, f.Top, 0.125, 0.875, 0.375, 0.875);
            f.CT = AddCircle(b, f.Top, 0.75, 0.75, 0.09375);
            f.S = new EditorSession(b);
            if (!snap) f.S.SetSnapEnabled(false);
            Check.That(!f.S.CanUndo && !f.S.CanRedo && f.S.UndoCount == 0 && f.S.RedoCount == 0, "fixture: empty history");
            return f;
        }

        // ================= contracts, strings =================

        public static void V_ConstantsStringsAndChangeKind()
        {
            Check.That(EditorSession.MaxUndoSteps == Cap, $"MaxUndoSteps {EditorSession.MaxUndoSteps}");
            Check.That(EditorSession.NothingToUndoToast == NothingToUndo, EditorSession.NothingToUndoToast);
            Check.That(EditorSession.NothingToRedoToast == NothingToRedo, EditorSession.NothingToRedoToast);
            Check.That(EditorSession.HistoryLimitToast == HistoryLimit, EditorSession.HistoryLimitToast);
            Check.That(EditorSession.UndidToast("Line") == "Undid: Line", EditorSession.UndidToast("Line"));
            Check.That(EditorSession.RedidToast("Clear board") == "Redid: Clear board", EditorSession.RedidToast("Clear board"));

            Check.That(EditorSession.UndoLabels.Line == LLine && EditorSession.UndoLabels.Circle == LCircle
                       && EditorSession.UndoLabels.Move == LMove && EditorSession.UndoLabels.Scale == LScale
                       && EditorSession.UndoLabels.Duplicate == LDup, "stroke and transform labels");
            Check.That(EditorSession.UndoLabels.AddLayer == LAddLayer && EditorSession.UndoLabels.DeleteLayer == LDeleteLayer
                       && EditorSession.UndoLabels.LayerOrder == LLayerOrder && EditorSession.UndoLabels.HideLayer == LHide
                       && EditorSession.UndoLabels.ShowLayer == LShow && EditorSession.UndoLabels.LayerOpacity == LOpacity,
                "layer labels");
            Check.That(EditorSession.UndoLabels.GridType == LGridType && EditorSession.UndoLabels.Tiles == LTiles
                       && EditorSession.UndoLabels.ClearBoard == LClear, "grid and clear labels");

            Check.That((int)BoardChangeKind.None == 0 && (int)BoardChangeKind.Shapes == 1 && (int)BoardChangeKind.Layers == 2
                       && (int)BoardChangeKind.SnapPoints == 4 && (int)BoardChangeKind.Grid == 8 && (int)BoardChangeKind.Reset == 16,
                "existing BoardChangeKind values unchanged");
            Check.That((int)BoardChangeKind.History == 32, $"History appended as 32, got {(int)BoardChangeKind.History}");
            Check.That(EditorSession.HistoryChangeKind == HistoryKind,
                $"HistoryChangeKind = Shapes|Layers|SnapPoints|Grid|History|Fills, got {EditorSession.HistoryChangeKind}");
            Check.That((EditorSession.HistoryChangeKind & BoardChangeKind.Reset) == 0, "HistoryChangeKind does not carry Reset");
        }

        public static void V_FreshSessionHasEmptyHistoryAndUndoRedoOnlyToast()
        {
            var s = new EditorSession();
            Check.That(!s.CanUndo && !s.CanRedo, "fresh: CanUndo/CanRedo false");
            Check.That(s.UndoCount == 0 && s.RedoCount == 0, $"fresh: counts {s.UndoCount}/{s.RedoCount}");
            Check.That(s.NextUndoLabel == null && s.NextRedoLabel == null, "fresh: no labels");
            var r = new Rec(s);
            OnlyToast(s, r, s.Undo, NothingToUndo, "Undo on a fresh session");
            OnlyToast(s, r, s.Redo, NothingToRedo, "Redo on a fresh session");
            IEditorSession i = s;
            Check.That(!i.CanUndo && !i.CanRedo, "IEditorSession view: false");

            // Through the interface: a commit makes CanUndo true and IEditorSession.Undo reverts it.
            s.SetSnapEnabled(false);
            Drag(s, V(0.2, 0.2), V(0.6, 0.3));
            Check.That(i.CanUndo && !i.CanRedo, "IEditorSession.CanUndo after a commit");
            i.Undo();
            Check.That(ShapeCount(s.Board) == 0 && !i.CanUndo && i.CanRedo, "IEditorSession.Undo reverts, CanRedo true");
            i.Redo();
            Check.That(ShapeCount(s.Board) == 1 && i.CanUndo && !i.CanRedo, "IEditorSession.Redo re-applies");
        }

        // ================= one step per action, exact restore, exact redo =================

        sealed class Row
        {
            public string Name, Label;
            public Action<Fx> Setup, Act;
            public BoardChangeKind[] Kinds;   // BoardChanged kinds the action itself raises (unchanged by undo)
            public int States;                // StateChanged count of the action itself
            public Action<Fx, EditorSession> AfterUndo, AfterRedo;
        }

        static readonly BoardChangeKind KShapes = BoardChangeKind.Shapes, KLayers = BoardChangeKind.Layers,
            KGrid = BoardChangeKind.Grid, KReset = BoardChangeKind.Reset,
            KDelete = BoardChangeKind.Layers | BoardChangeKind.Shapes | BoardChangeKind.SnapPoints;

        static List<Row> Rows()
        {
            return new List<Row>
            {
                new Row { Name = "line drag", Label = LLine, Kinds = new[] { KShapes }, States = 0,
                    Act = f => Drag(f.S, V(0.0625, 0.5), V(0.4375, 0.625)) },
                new Row { Name = "tap-to-connect", Label = LLine, Kinds = new[] { KShapes }, States = 0,
                    Setup = f =>
                    {
                        f.S.SetSnapEnabled(true);
                        string d = Dump(f.S.Board);
                        Tap(f.S, f.LT.A);
                        Check.That(f.S.PendingConnectStart.HasValue, "tap-to-connect: the first tap sets the pending start");
                        Check.That(f.S.UndoCount == 0 && !f.S.CanUndo, "tap-to-connect: the first tap alone is no step");
                        SameDump(Dump(f.S.Board), d, "tap-to-connect: the first tap changes nothing");
                    },
                    Act = f => Tap(f.S, f.CT.Center) },
                new Row { Name = "circle", Label = LCircle, Kinds = new[] { KShapes }, States = 0,
                    Setup = f => f.S.SetTool(ToolId.Circle),
                    Act = f => Drag(f.S, V(0.5, 0.375), V(0.5625, 0.375)),
                    AfterUndo = (f, s) => Check.That(s.ActiveTool == ToolId.Circle && s.SelectionCount == 0, "circle: tool kept") },
                new Row { Name = "move", Label = LMove, Kinds = new[] { KShapes }, States = 1,   // the exclusion at the first drag frame
                    Setup = f => Enter(f.S, ToolId.Move, f.LT, f.CT),
                    Act = f => Drag(f.S, V(0.5, 0.5), V(0.5625, 0.4375)),
                    AfterUndo = (f, s) =>
                    {
                        Selected(s, "move undone: the moved shapes stay selected", IdsOf(f.LT, f.CT));
                        Check.That(s.ActiveTool == ToolId.Move, $"move undone: tool Move, got {s.ActiveTool}");
                    },
                    AfterRedo = (f, s) =>
                    {
                        Selected(s, "move redone", IdsOf(f.LT, f.CT));
                        Check.That(s.ActiveTool == ToolId.Move, "move redone: tool Move");
                    } },
                new Row { Name = "scale", Label = LScale, Kinds = new[] { KShapes }, States = 1,
                    Setup = f => Enter(f.S, ToolId.Scale, f.CT),
                    Act = f => Drag(f.S, V(0.8125, 0.75), V(0.84375, 0.75)),
                    AfterUndo = (f, s) =>
                    {
                        Selected(s, "scale undone", IdsOf(f.CT));
                        Check.That(s.ActiveTool == ToolId.Scale, $"scale undone: tool Scale, got {s.ActiveTool}");
                        Check.That(((CircleShape)FindShape(s.Board, f.CT.Id)).Radius == 0.09375, "scale undone: radius exact");
                    } },
                new Row { Name = "duplicate", Label = LDup, Kinds = new[] { KShapes }, States = 1,
                    Setup = f => Enter(f.S, ToolId.Duplicate, f.L1, f.LT),
                    Act = f => Drag(f.S, V(0.5, 0.5), V(0.5625, 0.5625)),
                    AfterUndo = (f, s) =>
                    {
                        Selected(s, "duplicate undone: the originals are selected again", IdsOf(f.L1, f.LT));
                        Check.That(s.ActiveTool == ToolId.Move, $"duplicate undone: the tool stays Move, got {s.ActiveTool}");
                    } },
                new Row { Name = "add layer", Label = LAddLayer, Kinds = new[] { KLayers }, States = 1,
                    Act = f => f.S.AddLayer() },
                new Row { Name = "delete layer", Label = LDeleteLayer, Kinds = new[] { KDelete }, States = 1,
                    Act = f => f.S.DeleteBottomLayer() },
                new Row { Name = "layer order", Label = LLayerOrder, Kinds = new[] { KLayers }, States = 0,
                    Act = f => f.S.MoveLayer(f.Top.Id, 0) },
                new Row { Name = "hide layer", Label = LHide, Kinds = new[] { KLayers }, States = 0,
                    Act = f => f.S.SetLayerVisible(f.Bottom.Id, false) },
                new Row { Name = "show layer", Label = LShow, Kinds = new[] { KLayers }, States = 0,
                    Act = f => f.S.SetLayerVisible(f.Hidden.Id, true) },
                new Row { Name = "layer opacity", Label = LOpacity, Kinds = new[] { KLayers, KLayers, KLayers }, States = 0,
                    Act = f =>
                    {
                        f.S.SetLayerOpacity(f.Top.Id, 0.5);
                        f.S.SetLayerOpacity(f.Top.Id, 0.25);
                        f.S.SetLayerOpacity(f.Top.Id, 0.3125);
                    } },
                new Row { Name = "grid type", Label = LGridType, Kinds = new[] { KGrid }, States = 0,
                    Act = f => f.S.SetGridKind(GridKind.Hex) },
                new Row { Name = "tiles", Label = LTiles, Kinds = new[] { KGrid }, States = 0,
                    Act = f => f.S.SetGrid(1) },
                new Row { Name = "clear board", Label = LClear, Kinds = new[] { KReset }, States = 1,
                    Act = f => f.S.ClearBoard() },
            };
        }

        public static void V_EachActionIsOneStepUndoneExactlyAndRedoneExactly()
        {
            foreach (var row in Rows())
            {
                string w = row.Name;
                var f = Fixture();
                EditorSession s = f.S;
                row.Setup?.Invoke(f);
                string before = Dump(s.Board);
                Board beforeBoard = s.Board;
                int beforeNext = s.Board.NextId;
                EntityId beforeActive = s.Board.ActiveLayerId;
                var beforeIds = new HashSet<int>(AllIds(s.Board));
                var r = new Rec(s);

                row.Act(f);

                // The action itself: one step, its old event counts, CanUndo already true inside BoardChanged.
                Check.That(s.UndoCount == 1 && s.CanUndo && s.RedoCount == 0 && !s.CanRedo,
                    $"{w}: one step, got undo {s.UndoCount} redo {s.RedoCount}");
                Check.That(s.NextUndoLabel == row.Label && s.NextRedoLabel == null, $"{w}: label '{s.NextUndoLabel}'");
                Check.That(r.Boards.Count == row.Kinds.Length, $"{w}: {row.Kinds.Length} BoardChanged as before, got {r.B}");
                for (int i = 0; i < row.Kinds.Length; i++)
                    Check.That(r.Boards[i] == row.Kinds[i], $"{w}: BoardChanged #{i} {r.Boards[i]}, expected {row.Kinds[i]}");
                Check.That(r.States == row.States, $"{w}: {row.States} StateChanged as before, got {r.States}");
                foreach (var a in r.AtBoard)
                    Check.That(a.canUndo && a.undo == 1 && !a.canRedo, $"{w}: CanUndo already true inside BoardChanged");
                Check.That(r.Toasts.TrueForAll(t => !t.StartsWith("Undid") && !t.StartsWith("Redid")), $"{w}: no history toast, {r.T}");
                Check.That(s.Board.NextId >= beforeNext, $"{w}: NextId decreased");
                foreach (int id in AllIds(s.Board))
                    Check.That(beforeIds.Contains(id) || id >= beforeNext, $"{w}: new id {id} is below the old NextId {beforeNext}");

                string after = Dump(s.Board);
                Board afterBoard = s.Board;
                int afterNext = s.Board.NextId;
                EntityId afterActive = s.Board.ActiveLayerId;
                var afterLayers = LayerIds(s.Board);
                var afterSel = new HashSet<EntityId>(s.Selection.Ids);
                ToolId afterTool = s.ActiveTool;

                // Undo.
                r.Reset();
                s.Undo();
                OneHistoryEvent(r, "Undid: " + row.Label, w + " undo");
                Check.That(r.Previews == 0, $"{w} undo: no PreviewChanged without a gesture or pending ring, got {r.Previews}");
                var at = r.AtBoard[0];
                Check.That(!at.canUndo && at.canRedo && at.undo == 0 && at.redo == 1, $"{w} undo: history already stepped inside BoardChanged");
                Check.That(!ReferenceEquals(s.Board, afterBoard), $"{w} undo: a different board is live");
                SameDump(Dump(s.Board, nextId: false), Strip(before, true, false), w + " undo");
                Check.That(s.Board.ActiveLayerId == beforeActive, $"{w} undo: active layer {s.Board.ActiveLayerId}, expected {beforeActive}");
                Check.That(s.Board.NextId == afterNext, $"{w} undo: NextId kept at {afterNext} (never reissued), got {s.Board.NextId}");
                Check.That(s.UndoCount == 0 && s.RedoCount == 1 && !s.CanUndo && s.CanRedo, $"{w} undo: counts {s.UndoCount}/{s.RedoCount}");
                Check.That(s.NextUndoLabel == null && s.NextRedoLabel == row.Label, $"{w} undo: next redo label '{s.NextRedoLabel}'");
                CheckSelection(s, w + " undo");
                row.AfterUndo?.Invoke(f, s);

                // Redo.
                EntityId liveActive = s.Board.ActiveLayerId;
                r.Reset();
                s.Redo();
                OneHistoryEvent(r, "Redid: " + row.Label, w + " redo");
                at = r.AtBoard[0];
                Check.That(at.canUndo && !at.canRedo && at.undo == 1 && at.redo == 0, $"{w} redo: history already stepped inside BoardChanged");
                SameDump(Dump(s.Board, nextId: true, active: false), Strip(after, false, true), w + " redo");
                EntityId wantActive = afterLayers.Contains(liveActive.Value) ? liveActive : afterActive;
                Check.That(s.Board.ActiveLayerId == wantActive,
                    $"{w} redo: active {s.Board.ActiveLayerId}, expected {wantActive} (current layer kept if it exists)");
                Check.That(s.UndoCount == 1 && s.RedoCount == 0 && s.NextUndoLabel == row.Label, $"{w} redo: counts");
                if (row.AfterRedo != null) row.AfterRedo(f, s);
                else if (afterTool == s.ActiveTool && (afterTool == ToolId.Move || afterTool == ToolId.Scale))
                    Selected(s, w + " redo: selection", afterSel);
                CheckSelection(s, w + " redo");

                if (row.Label == LAddLayer)
                    Check.That(s.Board.ActiveLayerId == f.Top.Id, "add layer redo: the user's current layer (Top) stays active");
                if (row.Label == LDup)
                {
                    Selected(s, "duplicate redo: the copies are selected", afterSel);
                    Check.That(s.ActiveTool == ToolId.Move, "duplicate redo: tool Move");
                }
            }
        }

        /// <summary>A stored Dump with its "next N" and/or "active N" fields removed (same text as Dump with those flags off).</summary>
        static string Strip(string dump, bool next, bool active)
        {
            int nl = dump.IndexOf('\n');
            string head = dump.Substring(0, nl), rest = dump.Substring(nl);
            var parts = new List<string>(head.Split(' '));
            for (int i = 0; i + 1 < parts.Count; i++)
                if ((next && parts[i] == "next") || (active && parts[i] == "active"))
                {
                    parts.RemoveAt(i);
                    parts.RemoveAt(i);
                    i--;
                }
            return string.Join(" ", parts) + rest;
        }

        // ================= walk back to the empty board, then forward again =================

        public static void V_ScriptedWalkBackToTheEmptyBoardThenNothingToUndoThenRedoAll()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var r = new Rec(s);
            string initial = Dump(s.Board);
            EntityId layer1 = s.Board.Layers[0].Id;
            EntityId layer2 = EntityId.None, layer3 = EntityId.None;
            var lines = new List<EntityId>();

            LineShape LastLine() => (LineShape)s.ActiveLayer.Shapes[s.ActiveLayer.Shapes.Count - 1];

            var script = new List<(string label, Action act)>
            {
                (LLine, () => { Drag(s, V(0.125, 0.125), V(0.625, 0.25)); lines.Add(LastLine().Id); }),
                (LLine, () => { Drag(s, V(0.25, 0.75), V(0.75, 0.625)); lines.Add(LastLine().Id); }),
                (LCircle, () => { s.SetTool(ToolId.Circle); Drag(s, V(0.5, 0.5), V(0.625, 0.5)); s.SetTool(ToolId.Line); }),
                (LLine, () =>
                {
                    s.SetSnapEnabled(true);
                    Tap(s, V(0.125, 0.125));
                    Tap(s, V(0.25, 0.75));
                    s.SetSnapEnabled(false);
                }),
                (LAddLayer, () => layer2 = s.AddLayer()),
                (LAddLayer, () => layer3 = s.AddLayer()),
                (LLine, () => { Drag(s, V(0.375, 0.125), V(0.375, 0.875)); lines.Add(LastLine().Id); }),
                (LHide, () => s.SetLayerVisible(layer1, false)),
                (LShow, () => s.SetLayerVisible(layer1, true)),
                (LLayerOrder, () => s.MoveLayer(layer1, 1)),
                (LOpacity, () => { s.SetLayerOpacity(layer3, 0.5); s.SetLayerOpacity(layer3, 0.375); }),
                (LGridType, () => s.SetGridKind(GridKind.Hex)),
                (LTiles, () => s.SetGrid(3)),
                (LMove, () =>
                {
                    Enter(s, ToolId.Move, FindShape(s.Board, lines[0]), FindShape(s.Board, lines[2]));
                    Drag(s, V(0.5, 0.5), V(0.5625, 0.5));
                }),
                (LScale, () => { s.SetTool(ToolId.Scale); Drag(s, V(0.75, 0.5), V(0.8125, 0.5)); }),
                (LDup, () => { s.SetTool(ToolId.Duplicate); Drag(s, V(0.5, 0.5), V(0.5, 0.4375)); }),
                (LDeleteLayer, () => s.DeleteBottomLayer()),
                (LClear, () => s.ClearBoard()),
            };

            var dumps = new List<string> { initial };
            int lastNext = s.Board.NextId;
            for (int i = 0; i < script.Count; i++)
            {
                script[i].act();
                Check.That(s.UndoCount == i + 1 && s.NextUndoLabel == script[i].label,
                    $"script step {i} ({script[i].label}): undo count {s.UndoCount}, label '{s.NextUndoLabel}'");
                CheckIds(s, ref lastNext, $"script step {i}");
                dumps.Add(Dump(s.Board));
            }
            Check.That(s.Board.GridN == 2 && s.Board.GridKind == GridKind.Square && ShapeCount(s.Board) == 0, "cleared at the end");

            for (int i = script.Count - 1; i >= 0; i--)
            {
                int next = s.Board.NextId;
                r.Reset();
                s.Undo();
                string w = $"undo of step {i} ({script[i].label})";
                OneHistoryEvent(r, "Undid: " + script[i].label, w);
                SameDump(Dump(s.Board, nextId: false), Strip(dumps[i], true, false), w);
                Check.That(s.Board.NextId == next, $"{w}: NextId kept");
                Check.That(s.UndoCount == i && s.RedoCount == script.Count - i, $"{w}: counts {s.UndoCount}/{s.RedoCount}");
                CheckIds(s, ref lastNext, w);
                CheckSelection(s, w);
            }
            SameDump(Dump(s.Board, nextId: false), Strip(initial, true, false), "all undone: the empty starting board");
            Check.That(s.Board.Layers.Count == 1 && s.Board.Layers[0].Id == layer1 && s.Board.Layers[0].Name == "Layer 1"
                       && ShapeCount(s.Board) == 0 && s.Board.GridN == 2 && s.Board.GridKind == GridKind.Square,
                "all undone: one empty layer, 2x2 square grid");
            Check.That(!s.CanUndo && s.CanRedo, "all undone: CanUndo false");
            OnlyToast(s, r, s.Undo, NothingToUndo, "one Undo more");

            for (int i = 0; i < script.Count; i++)
            {
                EntityId live = s.Board.ActiveLayerId;
                int next = s.Board.NextId;
                r.Reset();
                s.Redo();
                string w = $"redo of step {i} ({script[i].label})";
                OneHistoryEvent(r, "Redid: " + script[i].label, w);
                SameDump(DumpNA(s.Board), Strip(dumps[i + 1], true, true), w);
                Check.That(s.Board.NextId == next, $"{w}: NextId kept");
                if (s.Board.FindLayer(live) != null)
                    Check.That(s.Board.ActiveLayerId == live, $"{w}: the current layer stays active");
                CheckIds(s, ref lastNext, w);
                CheckSelection(s, w);
            }
            OnlyToast(s, r, s.Redo, NothingToRedo, "one Redo more");
        }

        // ================= seeded script of 60 actions =================

        /// <summary>
        /// The non-step preparation (tool, selection, layer choice, snap toggle, a first tap) for action kind k, done here;
        /// returns the step call and its label, or null if the kind does not apply to the current board.
        /// </summary>
        static Action Prepare(EditorSession s, Random rng, int kind, out string label)
        {
            label = null;
            Board b = s.Board;
            if (kind != 3) s.SetSnapEnabled(false);
            switch (kind)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                {
                    if (!EnsureVisibleActive(s)) return null;
                    s.SetTool(kind == 2 ? ToolId.Circle : ToolId.Line);
                    if (kind == 2)
                    {
                        Vec2 c = V(0.2 + 0.6 * rng.NextDouble(), 0.2 + 0.6 * rng.NextDouble());
                        double rad = 0.02 + 0.13 * rng.NextDouble();
                        label = LCircle;
                        return () => Drag(s, c, V(c.X + rad, c.Y));
                    }
                    if (kind == 3)
                    {
                        s.SetSnapEnabled(true);
                        var cands = new List<Vec2>();
                        foreach (var cand in s.SnapCandidates)
                            if (cand.Position.X > 0.02 && cand.Position.X < 0.98 && cand.Position.Y > 0.02 && cand.Position.Y < 0.98)
                                cands.Add(cand.Position);
                        if (cands.Count < 2) return null;
                        Vec2 p = cands[rng.Next(cands.Count)], q = p;
                        for (int t = 0; t < 30 && Vec2.Distance(p, q) < 0.05; t++) q = cands[rng.Next(cands.Count)];
                        if (Vec2.Distance(p, q) < 0.05) return null;
                        int u = s.UndoCount;
                        Tap(s, p);   // pending start only
                        Check.That(s.UndoCount == u, "script: a first tap is no step");
                        label = LLine;
                        return () => Tap(s, q);
                    }
                    Vec2 a, e;
                    do
                    {
                        a = V(0.05 + 0.9 * rng.NextDouble(), 0.05 + 0.9 * rng.NextDouble());
                        e = V(0.05 + 0.9 * rng.NextDouble(), 0.05 + 0.9 * rng.NextDouble());
                    } while (Vec2.Distance(a, e) < 0.05);
                    label = LLine;
                    return () => Drag(s, a, e);
                }
                case 4:
                    if (b.Layers.Count >= EditorSession.MaxLayers) return null;
                    label = LAddLayer;
                    return () => s.AddLayer();
                case 5:
                    if (b.Layers.Count <= 1) return null;
                    label = LDeleteLayer;
                    return s.DeleteBottomLayer;
                case 6:
                {
                    if (b.Layers.Count <= 1) return null;
                    int from = rng.Next(b.Layers.Count), to = from;
                    while (to == from) to = rng.Next(b.Layers.Count);
                    EntityId id = b.Layers[from].Id;
                    label = LLayerOrder;
                    return () => s.MoveLayer(id, to);
                }
                case 7:
                {
                    Layer l = b.Layers[rng.Next(b.Layers.Count)];
                    bool show = !l.Visible;
                    EntityId id = l.Id;
                    label = show ? LShow : LHide;
                    return () => s.SetLayerVisible(id, show);
                }
                case 8:
                {
                    Layer l = b.Layers[rng.Next(b.Layers.Count)];
                    EntityId id = l.Id;
                    s.SelectLayer(id);   // the hold that opens the popup
                    var values = new List<double>();
                    int n = 2 + rng.Next(4);
                    double v = l.Opacity;
                    for (int i = 0; i < n; i++)
                    {
                        double next;
                        do next = rng.Next(65) / 64.0; while (next == v);
                        values.Add(next);
                        v = next;
                    }
                    label = LOpacity;
                    return () => { foreach (double x in values) s.SetLayerOpacity(id, x); };
                }
                case 9:
                {
                    GridKind k = b.GridKind == GridKind.Hex ? GridKind.Square : GridKind.Hex;
                    label = LGridType;
                    return () => s.SetGridKind(k);
                }
                case 10:
                {
                    int n = b.GridN;
                    while (n == b.GridN) n = 1 + rng.Next(4);
                    label = LTiles;
                    return () => s.SetGrid(n);
                }
                default:   // 11 Move, 12 Scale, 13 Duplicate
                {
                    var visible = new List<EntityId>();
                    foreach (var l in b.Layers)
                        if (l.Visible)
                            foreach (var sh in l.Shapes) visible.Add(sh.Id);
                    if (visible.Count == 0) return null;
                    var pick = new List<EntityId>();
                    int m = 1 + rng.Next(Math.Min(3, visible.Count));
                    while (pick.Count < m)
                    {
                        EntityId id = visible[rng.Next(visible.Count)];
                        if (!pick.Contains(id)) pick.Add(id);
                    }
                    ToolId tool = kind == 11 ? ToolId.Move : kind == 12 ? ToolId.Scale : ToolId.Duplicate;
                    s.SetTool(ToolId.SelectMove);
                    s.SetSelection(pick);
                    s.SetTool(tool);
                    if (s.ActiveTool != tool || !s.TryGetSelectionBounds(out BoardRect bb)) return null;
                    label = kind == 11 ? LMove : kind == 12 ? LScale : LDup;
                    if (tool == ToolId.Scale)
                    {
                        Vec2 pv = V(0.5 * (bb.MinX + bb.MaxX), 0.5 * (bb.MinY + bb.MaxY));
                        Vec2 to = V(0.5 - pv.X, 0.5 - pv.Y);
                        double len = to.Length;
                        Vec2 d = len < 1e-6 ? V(1, 0) : V(to.X / len, to.Y / len);
                        double k = 0.6 + rng.NextDouble();
                        if (Math.Abs(k - 1) < 0.1) k = 1.4;
                        return () => Drag(s, V(pv.X + 0.1 * d.X, pv.Y + 0.1 * d.Y), V(pv.X + 0.1 * k * d.X, pv.Y + 0.1 * k * d.Y));
                    }
                    Vec2 f0 = V(0.3 + 0.4 * rng.NextDouble(), 0.3 + 0.4 * rng.NextDouble());
                    Vec2 dt = V(0.3 * rng.NextDouble() - 0.15, 0.3 * rng.NextDouble() - 0.15);
                    if (dt.Length < 0.03) dt = V(0.05, -0.04);
                    return () => Drag(s, f0, V(f0.X + dt.X, f0.Y + dt.Y));
                }
            }
        }

        /// <summary>Makes a visible layer active (SelectLayer, a non-step) if the active one is hidden. False if none is visible.</summary>
        static bool EnsureVisibleActive(EditorSession s)
        {
            Layer a = s.ActiveLayer;
            if (a != null && a.Visible) return true;
            foreach (var l in s.Board.Layers)
                if (l.Visible)
                {
                    s.SelectLayer(l.Id);
                    return true;
                }
            return false;
        }

        public static void V_SeededScriptOf60ActionsUndoneAndRedoneExactly()
        {
            const int N = 60;
            var rng = new Random(1234);
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var r = new Rec(s);
            var states = new List<string> { DumpNA(s.Board) };          // states[k]: after k steps
            var layerSets = new List<HashSet<int>> { LayerIds(s.Board) };
            var captureActive = new List<EntityId>();                    // active layer when step k+1 was captured
            var labels = new List<string>();
            int lastNext = s.Board.NextId, guard = 0, refused = 0;

            while (labels.Count < N)
            {
                Check.That(++guard < 2000, $"script: stuck after {labels.Count} steps");
                int kind = labels.Count == 20 || labels.Count == 45 ? -1 : rng.Next(14);
                string label;
                Action act;
                if (kind < 0)
                {
                    label = LClear;
                    act = s.ClearBoard;
                }
                else act = Prepare(s, rng, kind, out label);
                CheckIds(s, ref lastNext, $"script prep {labels.Count}");
                Check.That(s.UndoCount == labels.Count, $"script prep {labels.Count}: preparation added a step");
                if (act == null) continue;

                string full = Dump(s.Board);
                Board live = s.Board;
                EntityId active = s.Board.ActiveLayerId;
                int next = s.Board.NextId;
                var idsBefore = new HashSet<int>(AllIds(s.Board));
                act();
                string w = $"script action {labels.Count} ({label}, kind {kind})";
                CheckIds(s, ref lastNext, w);
                CheckSelection(s, w);
                Check.That(s.UndoCount + s.RedoCount <= Cap, $"{w}: more than {Cap} steps stored");
                if (s.UndoCount == labels.Count)
                {
                    // Refused or identity (a clamped transform, a toggled-off tap pair): nothing at all may change.
                    Check.That(ReferenceEquals(s.Board, live), $"{w}: no step, yet the board was swapped");
                    SameDump(Dump(s.Board), full, w + " (no step: the model must be unchanged)");
                    refused++;
                    continue;
                }
                Check.That(s.UndoCount == labels.Count + 1, $"{w}: {s.UndoCount - labels.Count} steps for one action");
                Check.That(s.RedoCount == 0, $"{w}: a new step clears Redo");
                Check.That(s.NextUndoLabel == label, $"{w}: label '{s.NextUndoLabel}'");
                foreach (int id in AllIds(s.Board))
                    Check.That(idsBefore.Contains(id) || id >= next, $"{w}: new id {id} reissued (NextId before was {next})");
                labels.Add(label);
                captureActive.Add(active);
                states.Add(DumpNA(s.Board));
                layerSets.Add(LayerIds(s.Board));
            }
            Check.That(refused < 40, $"script: {refused} refused actions (generator too weak)");

            var activeAtUndo = new EntityId[N];
            for (int k = N - 1; k >= 0; k--)
            {
                string w = $"undo {k} ({labels[k]})";
                EntityId live = s.Board.ActiveLayerId;
                int next = s.Board.NextId;
                r.Reset();
                s.Undo();
                OneHistoryEvent(r, "Undid: " + labels[k], w);
                SameDump(DumpNA(s.Board), states[k], w);
                EntityId want = layerSets[k].Contains(live.Value) ? live : captureActive[k];
                Check.That(s.Board.ActiveLayerId == want, $"{w}: active {s.Board.ActiveLayerId}, expected {want}");
                Check.That(s.Board.NextId == next, $"{w}: NextId {next} -> {s.Board.NextId}");
                Check.That(s.UndoCount == k && s.RedoCount == N - k, $"{w}: counts {s.UndoCount}/{s.RedoCount}");
                CheckIds(s, ref lastNext, w);
                CheckSelection(s, w);
                activeAtUndo[k] = live;
            }
            Check.That(ShapeCount(s.Board) == 0 && s.Board.Layers.Count == 1, "all 60 undone: the empty starting board");
            OnlyToast(s, r, s.Undo, NothingToUndo, "61st Undo");

            for (int k = 0; k < N; k++)
            {
                string w = $"redo {k} ({labels[k]})";
                EntityId live = s.Board.ActiveLayerId;
                int next = s.Board.NextId;
                r.Reset();
                s.Redo();
                OneHistoryEvent(r, "Redid: " + labels[k], w);
                SameDump(DumpNA(s.Board), states[k + 1], w);
                EntityId want = layerSets[k + 1].Contains(live.Value) ? live : activeAtUndo[k];
                Check.That(s.Board.ActiveLayerId == want, $"{w}: active {s.Board.ActiveLayerId}, expected {want}");
                Check.That(s.Board.NextId == next, $"{w}: NextId {next} -> {s.Board.NextId}");
                Check.That(s.UndoCount == k + 1 && s.RedoCount == N - k - 1, $"{w}: counts");
                CheckIds(s, ref lastNext, w);
                CheckSelection(s, w);
            }
            OnlyToast(s, r, s.Redo, NothingToRedo, "61st Redo");
        }

        // ================= Redo =================

        public static void V_NewActionClearsRedoAndNonStepsKeepIt()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var r = new Rec(s);
            Drag(s, V(0.1, 0.1), V(0.5, 0.1));
            Drag(s, V(0.1, 0.3), V(0.5, 0.3));
            Drag(s, V(0.1, 0.5), V(0.5, 0.5));
            s.Undo();
            s.Undo();
            Check.That(s.UndoCount == 1 && s.RedoCount == 2 && s.CanRedo, "two undone");

            // Non-steps keep both stacks.
            s.SetTool(ToolId.Circle);
            s.SetSnapEnabled(true);
            s.SetSnapEnabled(false);
            s.SelectLayer(s.Board.Layers[0].Id);
            s.Style = s.Style.WithWidth(LineWidth.Thick);
            Cancel(s);
            Check.That(s.UndoCount == 1 && s.RedoCount == 2, "non-steps keep the redo stack");

            Drag(s, V(0.6, 0.6), V(0.7, 0.6));   // a new action (Circle)
            Check.That(!s.CanRedo && s.RedoCount == 0 && s.UndoCount == 2, "a new action clears Redo");
            OnlyToast(s, r, s.Redo, NothingToRedo, "Redo after a new action");
        }

        public static void V_RedoCancelsAnOpenGestureToo()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, V(0.1, 0.1), V(0.5, 0.1));
            EntityId first = s.ActiveLayer.Shapes[0].Id;
            s.Undo();
            var r = new Rec(s);
            DragNoUp(s, V(0.2, 0.6), V(0.7, 0.6));
            Check.That(s.GestureActive && s.Preview.Count > 0, "precondition: a line ghost");
            r.Reset();
            s.Redo();
            Check.That(!s.GestureActive && s.Preview.Count == 0 && !s.Cursor.HasValue, "Redo closed the gesture");
            Check.That(r.Previews >= 1, "PreviewChanged for the removed ghost");
            Check.That(r.States == 1 && r.Boards.Count == 1, $"one StateChanged, one BoardChanged; got {r.States}/{r.B}");
            Check.That(ShapeCount(s.Board) == 1 && s.ActiveLayer.Shapes[0].Id == first, "the line is back with its id");
            Up(s, V(0.7, 0.6));
            Check.That(ShapeCount(s.Board) == 1 && s.UndoCount == 1, "a stray Up commits nothing");
        }

        // ================= ClearBoard, LoadBoard =================

        public static void V_ClearBoardIsUndoableWithFreshIds()
        {
            var f = Fixture();
            EditorSession s = f.S;
            string before = Dump(s.Board);
            int oldNext = s.Board.NextId;
            var oldIds = new HashSet<int>(AllIds(s.Board));
            Enter(s, ToolId.Move, f.LT, f.CT);
            var r = new Rec(s);
            bool checkedInside = false;
            s.BoardChanged += k =>
            {
                if (k != BoardChangeKind.Reset) return;
                checkedInside = true;
                Check.That(s.CanUndo && s.UndoCount == 1 && s.NextUndoLabel == LClear, "Clear: the step is stored before BoardChanged(Reset)");
            };

            s.ClearBoard();
            Check.That(checkedInside, "Clear raised BoardChanged(Reset)");
            Check.That(r.Boards.Count == 1 && r.Boards[0] == BoardChangeKind.Reset && r.States == 1 && r.Previews == 1,
                $"Clear keeps the LoadBoard events: Reset, State, Preview; got {r.B} S {r.States} P {r.Previews}");
            Check.That(r.Toasts.Count == 0, $"Clear: no toast, got {r.T}");
            Board cleared = s.Board;
            Check.That(cleared.Layers.Count == 1 && ShapeCount(cleared) == 0 && cleared.Layers[0].Name == "Layer 1",
                "a fresh board with one empty Layer 1");
            Check.That(cleared.GridN == 2 && cleared.GridKind == GridKind.Square, "fresh board is 2x2 square");
            Check.That(cleared.Layers[0].Id.Value >= oldNext && !oldIds.Contains(cleared.Layers[0].Id.Value),
                $"the fresh layer gets a new id ({cleared.Layers[0].Id}), not an old one (old NextId {oldNext})");
            Check.That(cleared.NextId >= oldNext && cleared.NextId > cleared.Layers[0].Id.Value, $"NextId {cleared.NextId} kept above {oldNext}");
            Check.That(s.SelectionCount == 0 && s.ActiveTool == ToolId.SelectMove, "Clear empties the selection; Move falls back to Select");
            Check.That(s.UndoCount == 1 && s.NextUndoLabel == LClear, "one step: Clear board");

            int next = s.Board.NextId;
            r.Reset();
            s.Undo();
            OneHistoryEvent(r, "Undid: " + LClear, "undo Clear");
            SameDump(Dump(s.Board, nextId: false), Strip(before, true, false), "undo Clear: everything back");
            Check.That(s.Board.NextId == next, "undo Clear: NextId kept");
            Selected(s, "undo Clear in Select: the selection comes back", IdsOf(f.LT, f.CT));
            Check.That(s.ActiveTool == ToolId.SelectMove, "tool stays Select");

            // AddLayer naming after Clear restarts (existing LayerChecks expectation) and Clear again issues fresh ids.
            s.Redo();
            Check.That(ReferenceEquals(s.Board, cleared), "Redo brings back the cleared board itself");
            s.AddLayer();
            Check.That(s.Board.Layers[1].Name == "Layer 2", $"naming after Clear: {s.Board.Layers[1].Name}");
            s.Undo();
            s.Undo();
            s.ClearBoard();
            Check.That(!oldIds.Contains(s.Board.Layers[0].Id.Value) && s.Board.Layers[0].Id != cleared.Layers[0].Id,
                "a second Clear issues yet another fresh layer id");
        }

        public static void V_ClearThenDrawThenUndoTwiceGoesBackPastTheClear()
        {
            var f = Fixture();
            EditorSession s = f.S;
            string before = Dump(s.Board);
            s.ClearBoard();
            string cleared = Dump(s.Board);
            s.SetTool(ToolId.Line);
            Drag(s, V(0.25, 0.25), V(0.75, 0.75));
            string drawn = Dump(s.Board);
            Check.That(s.UndoCount == 2 && s.NextUndoLabel == LLine, "Clear + Line: two steps");
            int next = s.Board.NextId;

            s.Undo();
            SameDump(Dump(s.Board, nextId: false), Strip(cleared, true, false), "first Undo: the cleared board");
            s.Undo();
            SameDump(Dump(s.Board, nextId: false), Strip(before, true, false), "second Undo: back past the Clear");
            Check.That(s.Board.NextId == next, "NextId kept across both");
            Check.That(!s.CanUndo, "nothing older");
            s.Redo();
            s.Redo();
            SameDump(Dump(s.Board), drawn, "Redo twice: the line on the cleared board");

            // Clearing an already-empty board is still a step.
            var e = new EditorSession();
            e.ClearBoard();
            Check.That(e.UndoCount == 1 && e.NextUndoLabel == LClear, "Clear of an empty board records a step");
        }

        public static void V_ClearMidMoveDragUndoRestoresThePreDragGeometry()
        {
            var f = Fixture(snap: true);
            EditorSession s = f.S;
            string before = Dump(s.Board);
            Enter(s, ToolId.Move, f.LT, f.CT);
            DragNoUp(s, V(0.5, 0.5), V(0.6, 0.4));
            Check.That(s.GestureActive && s.Preview.Count >= 2, "precondition: ghosts");
            s.ClearBoard();
            Check.That(!s.GestureActive, "Clear cancelled the drag");
            Up(s, V(0.6, 0.4));
            Check.That(ShapeCount(s.Board) == 0, "stray Up after Clear commits nothing");
            s.Undo();
            SameDump(Dump(s.Board, nextId: false), Strip(before, true, false), "undo: the pre-drag board, ghost never committed");
            Check.That(HasCand(s, f.LT.A) && HasCand(s, f.CT.Center), "the dragged shapes' own candidates are back (exclusion lifted)");
        }

        public static void V_LoadBoardResetsHistoryBeforeItsEvents()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, V(0.1, 0.1), V(0.5, 0.1));
            Drag(s, V(0.1, 0.3), V(0.5, 0.3));
            s.Undo();
            Check.That(s.CanUndo && s.CanRedo, "precondition: both stacks");
            var nb = new Board { GridN = 4 };
            var l = new Layer(nb.NewId(), "Mine");
            nb.Layers.Add(l);
            nb.ActiveLayerId = l.Id;
            AddLine(nb, l, 0.2, 0.8, 0.8, 0.8);
            string loaded = Dump(nb);

            var r = new Rec(s);
            bool inside = false;
            s.BoardChanged += k =>
            {
                if (k != BoardChangeKind.Reset) return;
                inside = true;
                Check.That(!s.CanUndo && !s.CanRedo && s.UndoCount == 0 && s.RedoCount == 0,
                    "LoadBoard: history already empty inside BoardChanged(Reset)");
            };
            s.LoadBoard(nb);
            Check.That(inside, "LoadBoard raised BoardChanged(Reset)");
            Check.That(r.Boards.Count == 1 && r.States == 1 && r.Previews == 1 && r.Toasts.Count == 0,
                $"LoadBoard events unchanged: Reset, State, Preview; got {r.B} S {r.States} P {r.Previews} {r.T}");
            Check.That(ReferenceEquals(s.Board, nb), "the loaded board is live");
            Check.That(!s.CanUndo && !s.CanRedo && s.NextUndoLabel == null && s.NextRedoLabel == null, "history reset");
            OnlyToast(s, r, s.Undo, NothingToUndo, "Undo after LoadBoard");
            OnlyToast(s, r, s.Redo, NothingToRedo, "Redo after LoadBoard");

            Drag(s, V(0.2, 0.2), V(0.6, 0.6));
            Check.That(s.UndoCount == 1, "a new edit after Load is a step");
            s.Undo();
            SameDump(Dump(s.Board, nextId: false), Strip(loaded, true, false), "Undo goes back to the loaded board, not before it");
            Check.That(!s.CanUndo, "and no further");

            s.LoadBoard(null);
            Check.That(!s.CanUndo && !s.CanRedo && s.Board.Layers.Count == 1, "LoadBoard(null): empty board, history reset");
        }

        // ================= ids =================

        public static void V_IdsAreNeverReissuedAfterUndo()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, V(0.1, 0.1), V(0.5, 0.1));
            EntityId a = s.ActiveLayer.Shapes[0].Id;
            s.Undo();
            Check.That(ShapeCount(s.Board) == 0, "A undone");
            Drag(s, V(0.1, 0.3), V(0.5, 0.3));
            EntityId b = s.ActiveLayer.Shapes[0].Id;
            Check.That(b != a && b.Value >= a.Value + 1, $"B gets a new id: A {a}, B {b}");

            EntityId l1 = s.AddLayer();
            s.Undo();
            EntityId l2 = s.AddLayer();
            Check.That(l2 != l1 && l2.Value > l1.Value, $"AddLayer after Undo: {l1} then {l2}");

            // Duplicate, Undo, Duplicate: the second copies never reuse the first copies' ids.
            Shape sb = FindShape(s.Board, b);
            Enter(s, ToolId.Duplicate, sb);
            Drag(s, V(0.5, 0.5), V(0.5, 0.6));
            var firstCopies = new HashSet<EntityId>(s.Selection.Ids);
            s.Undo();
            s.SetTool(ToolId.Duplicate);
            Drag(s, V(0.5, 0.5), V(0.5, 0.7));
            foreach (var id in s.Selection.Ids)
                Check.That(!firstCopies.Contains(id) && id != b, $"second copy id {id} is new");

            // Redo restores the old id (a restoration, not an issue); the next new shape is above everything.
            s.SetTool(ToolId.Line);
            Drag(s, V(0.6, 0.9), V(0.9, 0.9));
            EntityId c = s.ActiveLayer.Shapes[s.ActiveLayer.Shapes.Count - 1].Id;
            s.Undo();
            s.Redo();
            Check.That(FindShape(s.Board, c) != null, "Redo brings C back with its id");
            Drag(s, V(0.6, 0.95), V(0.9, 0.95));
            EntityId d = s.ActiveLayer.Shapes[s.ActiveLayer.Shapes.Count - 1].Id;
            int max = 0;
            foreach (int id in AllIds(s.Board)) if (id != d.Value) max = Math.Max(max, id);
            Check.That(d.Value > max && d.Value > c.Value, $"D {d} above every other id ({max})");
        }

        // ================= snap candidates =================

        public static void V_SnapCandidatesFollowTheRestoredBoard()
        {
            var s = new EditorSession();   // snapping on, GridN 2
            Vec2 a = V(0.13, 0.17), b = V(0.41, 0.29);
            Check.That(!HasCand(s, a) && !HasCand(s, b), "precondition: no candidates there");
            Drag(s, a, b);
            Check.That(HasCand(s, a) && HasCand(s, b), "the new line's ends are candidates");
            s.Undo();
            Check.That(!HasCand(s, a) && !HasCand(s, b), "Undo: they are gone");
            s.Redo();
            Check.That(HasCand(s, a) && HasCand(s, b), "Redo: back");

            // Undo of a Move: the old end positions are candidates again, the moved ones are not.
            var line = (LineShape)s.ActiveLayer.Shapes[0];
            Enter(s, ToolId.Move, line);
            Drag(s, V(0.6, 0.6), V(0.7, 0.65));
            var moved = (LineShape)FindShape(s.Board, line.Id);
            Vec2 ma = moved.A, mb = moved.B;
            Check.That(ma != a && HasCand(s, ma) && !HasCand(s, a), "after the Move: new ends are candidates");
            s.Undo();
            Check.That(HasCand(s, a) && HasCand(s, b), "Undo of the Move: the old ends are candidates again (==)");
            Check.That(!HasCand(s, ma) && !HasCand(s, mb), "and the moved ends are not");

            // Grid: Tiles 3 then Undo brings the 2x2 corner back.
            Check.That(HasCand(s, V(0.5, 0.5)), "precondition: 2x2 centre corner");
            s.SetGrid(3);
            Check.That(!HasCand(s, V(0.5, 0.5)) || s.Board.GridN == 3, "3x3");
            s.Undo();
            Check.That(s.Board.GridN == 2 && HasCand(s, V(0.5, 0.5)), "Undo of Tiles: 2x2 candidates");

            // Undo of a hide: the layer's candidates return.
            s.SetTool(ToolId.Line);
            s.SetLayerVisible(s.Board.Layers[0].Id, false);
            Check.That(!HasCand(s, a), "hidden: no candidates");
            s.Undo();
            Check.That(HasCand(s, a), "Undo of Hide: candidates back");
        }

        // ================= selection and tool =================

        public static void V_SelectionAndToolAfterUndoingEachTransform()
        {
            // Move: the moved shapes come back at the exact old coordinates, still selected, tool Move.
            var f = Fixture();
            EditorSession s = f.S;
            Vec2 a0 = f.LT.A, b0 = f.LT.B, c0 = f.CT.Center;
            Enter(s, ToolId.Move, f.LT, f.CT);
            Drag(s, V(0.5, 0.5), V(0.53, 0.47));
            var r = new Rec(s);
            s.Undo();
            var lt = (LineShape)FindShape(s.Board, f.LT.Id);
            var ct = (CircleShape)FindShape(s.Board, f.CT.Id);
            Check.That(lt.A == a0 && lt.B == b0 && ct.Center == c0, $"Move undone exactly: {P(lt.A)} {P(ct.Center)}");
            Selected(s, "Move undone", IdsOf(f.LT, f.CT));
            Check.That(s.ActiveTool == ToolId.Move && r.States == 1, "tool Move, one StateChanged");

            // Scale: radius back, tool Scale.
            s.SetTool(ToolId.Scale);
            Drag(s, V(0.8125, 0.75), V(0.875, 0.75));
            s.Undo();
            Check.That(((CircleShape)FindShape(s.Board, f.CT.Id)).Radius == 0.09375, "Scale undone exactly");
            Check.That(s.ActiveTool == ToolId.Scale, "tool Scale");
            Selected(s, "Scale undone", IdsOf(f.LT, f.CT));

            // Duplicate across two layers: copies gone, originals selected, tool stays Move.
            var g = Fixture();
            Enter(g.S, ToolId.Duplicate, g.L1, g.C1, g.LT);
            Drag(g.S, V(0.5, 0.5), V(0.5625, 0.5625));
            Check.That(ShapeCount(g.S.Board) == 9 && g.S.ActiveTool == ToolId.Move, "precondition: 3 copies, Move");
            r = new Rec(g.S);
            g.S.Undo();
            Check.That(ShapeCount(g.S.Board) == 6, "copies gone");
            Selected(g.S, "Duplicate undone: the originals", IdsOf(g.L1, g.C1, g.LT));
            Check.That(g.S.ActiveTool == ToolId.Move && r.States == 1, $"tool stays Move, one StateChanged; got {g.S.ActiveTool}");
        }

        public static void V_SelectionRulesAfterUndo()
        {
            // Line tool: an Undo whose step had a selection leaves the selection empty.
            var f = Fixture();
            Enter(f.S, ToolId.Move, f.LT);
            Drag(f.S, V(0.5, 0.5), V(0.5, 0.4375));
            f.S.SetTool(ToolId.Line);
            f.S.Undo();
            Check.That(f.S.SelectionCount == 0 && f.S.ActiveTool == ToolId.Line, "Line tool: no selection after Undo");
            Check.That(((LineShape)FindShape(f.S.Board, f.LT.Id)).A == V(0.125, 0.875), "the Move was undone");
            f.S.Redo();
            f.S.SetTool(ToolId.Circle);
            f.S.Undo();
            Check.That(f.S.SelectionCount == 0 && f.S.ActiveTool == ToolId.Circle, "Circle tool: no selection after Undo");

            // Move tool, stored selection empty: falls back to Select with exactly one StateChanged.
            var g = Fixture();
            Drag(g.S, V(0.0625, 0.5), V(0.4375, 0.625));   // Line step, empty selection
            Enter(g.S, ToolId.Move, g.LT);
            var r = new Rec(g.S);
            g.S.Undo();
            Check.That(g.S.ActiveTool == ToolId.SelectMove && g.S.ToolHint == SelectHint, $"empty restored selection: Select, got {g.S.ActiveTool}");
            Check.That(g.S.SelectionCount == 0, "selection is the stored one (empty)");
            OneHistoryEvent(r, "Undid: " + LLine, "undo into Select");

            // Select tool: the stored selection replaces the current one.
            var h = Fixture();
            h.S.SetTool(ToolId.SelectMove);
            h.S.SetSelection(IdsOf(h.LT));
            h.S.SetGrid(2);                         // step with selection {LT}
            h.S.SetSelection(IdsOf(h.CT, h.L1));    // not a step
            h.S.Undo();
            Selected(h.S, "Select: the stored selection is restored", IdsOf(h.LT));

            // Hiding the layer of selected shapes prunes them; Undo brings the layer and the selection back.
            var k = Fixture();
            k.S.SetTool(ToolId.SelectMove);
            k.S.SetSelection(IdsOf(k.L1, k.C1, k.LT));
            k.S.SetLayerVisible(k.Bottom.Id, false);
            Selected(k.S, "hidden: pruned", IdsOf(k.LT));
            r = new Rec(k.S);
            k.S.Undo();
            Check.That(k.S.Board.FindLayer(k.Bottom.Id).Visible, "the layer is shown again");
            Selected(k.S, "Undo of Hide: the selection is back", IdsOf(k.L1, k.C1, k.LT));
            Check.That(r.States == 1, $"one StateChanged, got {r.States}");

            // Deleting the bottom layer with selected shapes on it: Undo restores layer, shapes and selection.
            var m = Fixture();
            Enter(m.S, ToolId.Move, m.L1, m.G, m.CT);
            m.S.DeleteBottomLayer();
            Selected(m.S, "deleted: pruned", IdsOf(m.CT));
            m.S.Undo();
            Selected(m.S, "Undo of Delete layer: the selection is back", IdsOf(m.L1, m.G, m.CT));
            Check.That(m.S.ActiveTool == ToolId.Move, "tool Move");
        }

        // ================= active layer rule =================

        public static void V_ActiveLayerRuleAfterUndo()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            EntityId l1 = s.Board.Layers[0].Id;
            EntityId l2 = s.AddLayer();
            s.Undo();
            Check.That(s.Board.ActiveLayerId == l1, "AddLayer undone: the previous layer is active");

            s.Redo();
            Check.That(s.Board.ActiveLayerId == l1, "AddLayer redone: the current layer (still there) stays active");
            s.SelectLayer(l1);
            Drag(s, V(0.1, 0.1), V(0.5, 0.1));     // on L1
            s.SelectLayer(l2);
            s.Undo();
            Check.That(s.Board.ActiveLayerId == l2, "draw on L1, select L2, Undo: L2 stays active");
            Check.That(ShapeCount(s.Board) == 0, "the line is gone");

            // Deleting the active bottom layer moves the active layer up; Undo keeps the user's current (still existing) layer.
            s.SelectLayer(l1);
            s.DeleteBottomLayer();
            Check.That(s.Board.ActiveLayerId == l2, "precondition: delete moved the active layer to L2");
            s.Undo();
            Check.That(s.Board.FindLayer(l1) != null && s.Board.ActiveLayerId == l2, "Undo of Delete: L1 back, L2 stays active");

            // Undo of AddLayer while another existing layer is selected: that layer stays active.
            EntityId l3 = s.AddLayer();
            s.SelectLayer(l1);
            s.Undo();
            Check.That(s.Board.FindLayer(l3) == null && s.Board.ActiveLayerId == l1, "L3 removed, L1 stays active");

            // Undo of Clear: the fresh layer does not exist in the old board, so the snapshot's active layer applies.
            s.SelectLayer(l2);
            s.ClearBoard();
            s.Undo();
            Check.That(s.Board.ActiveLayerId == l2, "Undo of Clear: the active layer stored with the step");
        }

        // ================= open gestures =================

        public static void V_UndoDuringAnOpenGestureInEveryToolCancelsItFirst()
        {
            var tools = new[] { ToolId.Line, ToolId.Circle, ToolId.SelectMove, ToolId.Move, ToolId.Scale, ToolId.Duplicate };
            foreach (var tool in tools)
                foreach (bool strokeStep in new[] { true, false })
                {
                    string w = $"{tool}/{(strokeStep ? "after a stroke" : "after Tiles with a selection")}";
                    var f = Fixture(snap: true);
                    EditorSession s = f.S;
                    string clean = Dump(s.Board);
                    EntityId x = EntityId.None;
                    bool transform = tool == ToolId.Move || tool == ToolId.Scale || tool == ToolId.Duplicate;
                    if (strokeStep)
                    {
                        Drag(s, V(0.0625, 0.4375), V(0.3125, 0.5625));   // step: Line X, empty selection
                        x = s.ActiveLayer.Shapes[s.ActiveLayer.Shapes.Count - 1].Id;
                    }
                    if (tool == ToolId.SelectMove) { s.SetTool(ToolId.SelectMove); s.SetSelection(IdsOf(f.LT)); }
                    else if (transform) Enter(s, tool, f.LT, f.CT);
                    else s.SetTool(tool);
                    if (!strokeStep) s.SetGrid(1);                     // step: Tiles, stored with the current selection
                    var storedSel = strokeStep ? new HashSet<EntityId>() : new HashSet<EntityId>(s.Selection.Ids);
                    string beforeGesture = DumpNA(s.Board);
                    Check.That(s.UndoCount == 1, $"{w}: one step");

                    // Open gesture.
                    if (tool == ToolId.SelectMove) DragNoUp(s, V(0.6, 0.6), V(0.95, 0.95));    // marquee around CT
                    else if (tool == ToolId.Scale) DragNoUp(s, V(0.8125, 0.75), V(0.9, 0.75));
                    else DragNoUp(s, V(0.45, 0.3), V(0.55, 0.38));
                    Check.That(s.GestureActive && s.Preview.Count > 0, $"{w}: precondition, gesture open with a preview");
                    if (tool == ToolId.Move || tool == ToolId.Scale)
                        Check.That(!HasCand(s, f.LT.A), $"{w}: precondition, own candidates excluded");

                    var r = new Rec(s);
                    s.Undo();
                    Check.That(!s.GestureActive, $"{w}: gesture closed");
                    Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, $"{w}: Preview empty, Cursor null");
                    Check.That(r.Previews >= 1, $"{w}: PreviewChanged for the removed ghost");
                    Check.That(r.Boards.Count == 1 && r.Boards[0] == EditorSession.HistoryChangeKind, $"{w}: one BoardChanged(History), got {r.B}");
                    Check.That(r.States == 1, $"{w}: exactly one StateChanged (the gesture's own is folded in), got {r.States}");
                    Check.That(r.Toasts.Count == 1 && r.Toasts[0] == "Undid: " + (strokeStep ? LLine : LTiles), $"{w}: toast {r.T}");
                    Check.That(s.UndoCount == 0, $"{w}: the previous action was undone");
                    if (strokeStep) Check.That(FindShape(s.Board, x) == null, $"{w}: X is gone");
                    else Check.That(s.Board.GridN == 3, $"{w}: Tiles back to 3");
                    SameDump(DumpNA(s.Board), Strip(clean, true, true), $"{w}: the model is the fixture again (nothing from the gesture)");
                    Check.That(HasCand(s, f.LT.A) && HasCand(s, f.LT.B) && HasCand(s, f.CT.Center), $"{w}: snap exclusion lifted");

                    if (tool == ToolId.Line || tool == ToolId.Circle)
                        Check.That(s.SelectionCount == 0 && s.ActiveTool == tool, $"{w}: tool kept, no selection");
                    else if (strokeStep)
                    {
                        Check.That(s.SelectionCount == 0, $"{w}: the stored (empty) selection");
                        Check.That(s.ActiveTool == ToolId.SelectMove, $"{w}: an empty selection falls back to Select, got {s.ActiveTool}");
                    }
                    else
                    {
                        Selected(s, $"{w}: the stored selection", storedSel);
                        Check.That(s.ActiveTool == tool, $"{w}: tool kept, got {s.ActiveTool}");
                    }

                    // A stray Up commits nothing.
                    string afterUndo = Dump(s.Board);
                    r.Reset();
                    Up(s, V(0.55, 0.38));
                    SameDump(Dump(s.Board), afterUndo, $"{w}: stray Up");
                    Check.That(r.Boards.Count == 0 && s.UndoCount == 0 && s.RedoCount == 1, $"{w}: stray Up commits nothing");
                    _ = beforeGesture;
                }
        }

        public static void V_UndoWithEmptyHistoryLeavesAnOpenGestureAlone()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            DragNoUp(s, V(0.2, 0.2), V(0.6, 0.4));
            int previews = s.Preview.Count;
            var r = new Rec(s);
            s.Undo();
            Check.That(r.Toasts.Count == 1 && r.Toasts[0] == NothingToUndo, $"toast {r.T}");
            Check.That(r.Boards.Count == 0 && r.States == 0 && r.Previews == 0, "no other event");
            Check.That(s.GestureActive && s.Preview.Count == previews, "the gesture is untouched");
            Up(s, V(0.6, 0.4));
            Check.That(ShapeCount(s.Board) == 1 && s.UndoCount == 1, "Up still commits the line");
        }

        public static void V_UndoClearsThePendingTapToConnectStart()
        {
            var s = new EditorSession();   // snapping on, GridN 2
            Drag(s, V(0.13, 0.17), V(0.41, 0.29));    // step: X
            Tap(s, V(0.5, 0.5));
            Check.That(s.PendingConnectStart.HasValue && s.PendingConnectStart.Value == V(0.5, 0.5) && HasPendingRing(s),
                "precondition: pending start with its ring");
            var r = new Rec(s);
            s.Undo();
            Check.That(!s.PendingConnectStart.HasValue, "pending start cleared");
            Check.That(!HasPendingRing(s) && s.Preview.Count == 0, "ring gone");
            Check.That(r.Previews >= 1, "PreviewChanged raised for the ring");
            Check.That(ShapeCount(s.Board) == 0, "X undone");
            Check.That(r.Log.IndexOf("P") < r.Log.IndexOf("B"), $"PreviewChanged before BoardChanged, log {string.Join(",", r.Log)}");
            Tap(s, V(0.5, 0.0));
            Check.That(ShapeCount(s.Board) == 0 && s.PendingConnectStart.HasValue && s.PendingConnectStart.Value == V(0.5, 0.0),
                "the next tap starts a new pair instead of connecting to the old start");

            // With nothing to undo, the pending start stays.
            var t = new EditorSession();
            Tap(t, V(0.5, 0.5));
            Check.That(t.PendingConnectStart.HasValue, "precondition");
            var rt = new Rec(t);
            t.Undo();
            Check.That(t.PendingConnectStart.HasValue && HasPendingRing(t) && rt.Previews == 0, "empty history: pending untouched");
        }

        // ================= cap =================

        public static void V_Exactly200StepsAreAllKept()
        {
            var s = new EditorSession();
            for (int i = 0; i < Cap; i++) s.SetGrid(s.Board.GridN % 4 + 1);
            Check.That(s.UndoCount == Cap, $"200 steps kept, got {s.UndoCount}");
            for (int i = 0; i < Cap; i++) s.Undo();
            Check.That(s.Board.GridN == 2 && !s.CanUndo && s.RedoCount == Cap, "all 200 undone to the start");
            var r = new Rec(s);
            OnlyToast(s, r, s.Undo, NothingToUndo, "nothing trimmed: the ordinary toast");
        }

        public static void V_CapKeepsTheLast200AndToastsTheLimit()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var states = new List<string> { DumpNA(s.Board) };
            for (int i = 0; i < 205; i++)
            {
                double x = 0.05 + 0.004 * i;
                Drag(s, V(x, 0.1), V(x, 0.9));
                Check.That(s.UndoCount == Math.Min(i + 1, Cap), $"after {i + 1} lines: {s.UndoCount} steps");
                states.Add(DumpNA(s.Board));
            }
            Check.That(ShapeCount(s.Board) == 205 && s.UndoCount == Cap, "205 lines, 200 steps");
            var r = new Rec(s);
            for (int i = 0; i < Cap; i++)
            {
                s.Undo();
                Check.That(s.UndoCount + s.RedoCount == Cap, "undo + redo <= 200");
            }
            SameDump(DumpNA(s.Board), states[5], "200 Undos reach the state after action 5");
            Check.That(ShapeCount(s.Board) == 5 && !s.CanUndo, "5 lines left");
            OnlyToast(s, r, s.Undo, HistoryLimit, "201st Undo");
            OnlyToast(s, r, s.Undo, HistoryLimit, "and again");

            for (int i = 0; i < Cap; i++) s.Redo();
            SameDump(DumpNA(s.Board), states[205], "Redo 200: all 205 lines");
            for (int i = 0; i < 10; i++) s.Undo();
            Drag(s, V(0.95, 0.1), V(0.95, 0.9));
            Check.That(s.UndoCount == 191 && s.RedoCount == 0, $"10 undone + a new line: {s.UndoCount}/{s.RedoCount}");

            s.LoadBoard(GoldenBoards.Empty());
            OnlyToast(s, r, s.Undo, NothingToUndo, "after LoadBoard the trimmed flag is reset");
        }

        // ================= non-steps =================

        public static void V_NonEditsNeverAddSteps()
        {
            var f = Fixture(snap: true);
            EditorSession s = f.S;
            s.SetGrid(2);
            s.SetGrid(4);
            s.Undo();   // one undo step (Tiles) and one redo step (Tiles)
            int u0 = s.UndoCount, r0 = s.RedoCount;
            Check.That(u0 == 1 && r0 == 1, "precondition: 1 undo + 1 redo");
            Board live = s.Board;
            string d0 = Dump(s.Board, nextId: true, active: false);

            void Same(string what)
            {
                Check.That(s.UndoCount == u0 && s.RedoCount == r0, $"{what}: steps {s.UndoCount}/{s.RedoCount}, expected {u0}/{r0}");
                Check.That(ReferenceEquals(s.Board, live), $"{what}: the board was swapped");
                SameDump(Dump(s.Board, nextId: true, active: false), d0, what + " (a non-step must not change the model or NextId)");
            }

            s.SelectLayer(f.Bottom.Id); Same("SelectLayer other");
            s.SelectLayer(f.Bottom.Id); Same("SelectLayer same");
            s.SelectLayer(new EntityId(999)); Same("SelectLayer unknown");
            s.SelectLayer(f.Top.Id); Same("SelectLayer back");
            s.SetTool(ToolId.Circle); Same("SetTool Circle");
            s.SetTool(ToolId.Rotate); Same("SetTool Rotate (coming soon)");
            s.SetTool(ToolId.Move); Same("SetTool Move with nothing selected");
            s.SetTool(ToolId.None); Same("SetTool None");
            s.SetTool(ToolId.Line); Same("SetTool Line");
            s.SetSnapEnabled(false); s.SetSnapEnabled(true); Same("Snap off/on");
            s.SetOrthoLock(true); s.SetOrthoLock(false); Same("H/V on/off");
            s.Style = s.Style.WithWidth(LineWidth.Thick); Same("Width");
            s.ToggleDottedLine(); s.ToggleDottedLine(); Same("Dotted");
            s.SetCursor(V(0.5, 0.5), true); s.SetCursor(V(0, 0), false); Same("SetCursor");
            s.PlaceSnapPoint(); s.ClickAtCursor(); s.LineBetweenSnapPoints(); s.SetStamp(StampKind.Hexagon); Same("quick menubar no-ops");

            // Taps that only set or clear the pending start.
            Tap(s, f.LT.A); Same("first tap (pending start)");
            Check.That(s.PendingConnectStart.HasValue, "pending set");
            Tap(s, f.LT.A); Same("same candidate again (toggle off)");
            Tap(s, f.LT.A); Tap(s, V(0.45, 0.42)); Same("tap off any candidate");
            s.SetOrthoLock(true); Tap(s, f.LT.A); Tap(s, f.CT.Center); s.SetOrthoLock(false); Same("H/V-refused tap pair");

            // Refused strokes.
            s.SelectLayer(f.Hidden.Id);
            Drag(s, V(0.45, 0.42), V(0.6, 0.6)); Same("Line on a hidden layer");
            s.SetTool(ToolId.Circle);
            Drag(s, V(0.45, 0.42), V(0.6, 0.6)); Same("Circle on a hidden layer");
            s.SelectLayer(f.Top.Id);
            Drag(s, V(0.45, 0.42), V(0.459, 0.42)); Same("too-small circle");
            s.SetTool(ToolId.Line);
            Drag(s, V(0.45, 0.42), V(0.459, 0.42)); Same("too-short line");

            // Cancelled strokes.
            DragNoUp(s, V(0.45, 0.42), V(0.6, 0.6)); Cancel(s); Same("Line + Cancel");
            DragNoUp(s, V(0.45, 0.42), V(0.6, 0.6)); s.SetTool(ToolId.Circle); Same("Line + tool switch");
            DragNoUp(s, V(0.45, 0.42), V(0.6, 0.6)); Cancel(s); Same("Circle + Cancel");
            s.SetTool(ToolId.Line);
            DragNoUp(s, V(0.45, 0.42), V(0.6, 0.6)); Down(s, V(0.45, 0.42)); Cancel(s); Same("lost Up");

            // Select: marquee, taps, API.
            s.SetTool(ToolId.SelectMove);
            Drag(s, V(0.05, 0.6), V(0.45, 0.95)); Same("marquee");
            Tap(s, V(0.84375, 0.75)); Same("tap-pick");
            Tap(s, V(0.45, 0.42)); Same("empty tap");
            DragNoUp(s, V(0.05, 0.6), V(0.45, 0.95)); Cancel(s); Same("marquee + Cancel");
            s.SetSelection(IdsOf(f.LT, f.CT)); s.ClearSelection(); s.SetSelection(IdsOf(f.LT)); Same("SetSelection/ClearSelection");

            // Transforms: identity drags, taps, cancels (snapping off so the identity is exact).
            s.SetSnapEnabled(false);
            s.SetTool(ToolId.Move);
            Down(s, V(0.5, 0.4)); MoveTo(s, V(0.6, 0.45)); MoveTo(s, V(0.5, 0.4)); Up(s, V(0.5, 0.4)); Same("identity Move");
            Tap(s, V(0.84375, 0.75)); Same("tap-pick in Move");
            DragNoUp(s, V(0.5, 0.4), V(0.6, 0.45)); Cancel(s); Same("Move + Cancel");
            DragNoUp(s, V(0.5, 0.4), V(0.6, 0.45)); s.SetTool(ToolId.Scale); Same("Move + switch to Scale");
            Down(s, V(0.9, 0.75)); MoveTo(s, V(0.95, 0.75)); MoveTo(s, V(0.9, 0.75)); Up(s, V(0.9, 0.75)); Same("identity Scale");
            DragNoUp(s, V(0.9, 0.75), V(0.95, 0.75)); Cancel(s); Same("Scale + Cancel");
            s.SetTool(ToolId.Duplicate);
            Down(s, V(0.5, 0.4)); MoveTo(s, V(0.6, 0.45)); MoveTo(s, V(0.5, 0.4)); Up(s, V(0.5, 0.4)); Same("Duplicate at t = 0");
            Tap(s, V(0.5, 0.4)); Same("tap in Duplicate");
            DragNoUp(s, V(0.5, 0.4), V(0.6, 0.45)); Cancel(s); Same("Duplicate + Cancel");

            // Unchanged values.
            s.SetGrid(s.Board.GridN); Same("SetGrid unchanged");
            s.SetGrid(99); s.SetGrid(4); Check.That(s.UndoCount > u0, "precondition: SetGrid(99) clamps to 4, a change");
        }

        public static void V_TransformCommitThatAppliesNothingPushesNoStep()
        {
            // Engine-only path (on device a second touch cancels first): the selection's layer is hidden mid-drag, so the
            // commit edits nothing; the captured step must be dropped, leaving only the Hide step.
            foreach (var tool in new[] { ToolId.Move, ToolId.Scale, ToolId.Duplicate })
            {
                var f = Fixture();
                EditorSession s = f.S;
                Enter(s, tool, f.LT, f.CT);
                DragNoUp(s, V(0.8125, 0.75), V(0.9, 0.7));
                s.SetLayerVisible(f.Top.Id, false);
                Check.That(s.UndoCount == 1 && s.NextUndoLabel == LHide, $"{tool}: precondition, the Hide step");
                string d = Dump(s.Board);
                Up(s, V(0.9, 0.7));
                Check.That(s.UndoCount == 1 && s.NextUndoLabel == LHide, $"{tool}: a commit that changed nothing is no step ({s.UndoCount}, '{s.NextUndoLabel}')");
                SameDump(Dump(s.Board), d, $"{tool}: nothing edited");
            }
        }

        public static void V_UnchangedOrRefusedLayerAndGridCallsAreNoSteps()
        {
            var f = Fixture();
            EditorSession s = f.S;
            string d0 = Dump(s.Board);
            var r = new Rec(s);
            s.SetGrid(3);
            s.SetGridKind(GridKind.Square);
            s.SetGridKind((GridKind)7);
            s.SetLayerOpacity(f.Top.Id, 1.0);
            s.SetLayerOpacity(f.Top.Id, 1.7);
            s.SetLayerOpacity(f.Top.Id, double.NaN);
            s.SetLayerOpacity(new EntityId(999), 0.5);
            s.SetLayerVisible(f.Top.Id, true);
            s.SetLayerVisible(f.Hidden.Id, false);
            s.SetLayerVisible(new EntityId(999), false);
            s.MoveLayer(f.Top.Id, 2);
            s.MoveLayer(f.Top.Id, 99);
            s.MoveLayer(new EntityId(999), 0);
            Check.That(s.UndoCount == 0 && r.Boards.Count == 0, $"unchanged values: no step, no BoardChanged ({s.UndoCount}, {r.B})");
            SameDump(Dump(s.Board), d0, "unchanged values");

            // Only layer; layer cap.
            var one = new EditorSession();
            one.DeleteBottomLayer();
            Check.That(one.UndoCount == 0 && one.Board.Layers.Count == 1, "only-layer delete: no step");
            var cap = new Board();
            for (int i = 0; i < EditorSession.MaxLayers; i++) cap.Layers.Add(new Layer(cap.NewId(), "L" + i));
            cap.ActiveLayerId = cap.Layers[0].Id;
            var full = new EditorSession(cap);
            int next = cap.NextId;
            Check.That(full.AddLayer().IsNone, "precondition: at the cap");
            Check.That(full.UndoCount == 0 && cap.NextId == next, "AddLayer at the cap: no step, no id used");
        }

        // ================= opacity coalescing =================

        public static void V_OpacityEditsCoalescePerPopupSession()
        {
            // 20 edits on one layer: one step; Undo restores the original exactly.
            var f = Fixture();
            EditorSession s = f.S;
            var r = new Rec(s);
            s.SetLayerOpacity(f.Bottom.Id, 0.75);   // unchanged: nothing, and no run starts
            Check.That(s.UndoCount == 0, "unchanged opacity: no step");
            for (int i = 1; i <= 20; i++) s.SetLayerOpacity(f.Bottom.Id, 0.75 - 0.03125 * i);
            Check.That(s.UndoCount == 1 && s.NextUndoLabel == LOpacity, $"20 edits: one step, got {s.UndoCount}");
            Check.That(r.Boards.Count == 20 && r.Boards.TrueForAll(k => k == BoardChangeKind.Layers) && r.States == 0,
                $"every edit still raises one BoardChanged(Layers) and no StateChanged ({r.Boards.Count}, {r.States})");
            s.Undo();
            Check.That(s.Board.FindLayer(f.Bottom.Id).Opacity == 0.75, "Undo: the original opacity exactly");
            Check.That(!s.CanUndo, "one Undo was enough");

            // SelectLayer (even on the already active layer) splits the run.
            var g = Fixture();
            g.S.SetLayerOpacity(g.Top.Id, 0.5);
            g.S.SetLayerOpacity(g.Top.Id, 0.4);
            g.S.SelectLayer(g.Top.Id);   // already active: a no-op that still ends the merge
            g.S.SetLayerOpacity(g.Top.Id, 0.3);
            g.S.SetLayerOpacity(g.Top.Id, 0.2);
            Check.That(g.S.UndoCount == 2, $"SelectLayer splits: 2 steps, got {g.S.UndoCount}");
            g.S.Undo();
            Check.That(g.S.Board.FindLayer(g.Top.Id).Opacity == 0.4, "first Undo: the end of the first session");
            g.S.Undo();
            Check.That(g.S.Board.FindLayer(g.Top.Id).Opacity == 1.0, "second Undo: the original");

            // SelectLayer of another layer splits too.
            var g2 = Fixture();
            g2.S.SetLayerOpacity(g2.Top.Id, 0.5);
            g2.S.SelectLayer(g2.Bottom.Id);
            g2.S.SetLayerOpacity(g2.Top.Id, 0.25);
            Check.That(g2.S.UndoCount == 2, $"SelectLayer(other) splits: got {g2.S.UndoCount}");

            // An edit on another layer splits; so does any other action.
            var h = Fixture();
            h.S.SetLayerOpacity(h.Top.Id, 0.5);
            h.S.SetLayerOpacity(h.Bottom.Id, 0.5);
            h.S.SetLayerOpacity(h.Top.Id, 0.25);
            Check.That(h.S.UndoCount == 3, $"L, M, L: 3 steps, got {h.S.UndoCount}");
            h.S.Undo();
            Check.That(h.S.Board.FindLayer(h.Top.Id).Opacity == 0.5, "undo L(2)");
            h.S.Undo();
            Check.That(h.S.Board.FindLayer(h.Bottom.Id).Opacity == 0.75, "undo M");
            h.S.Undo();
            Check.That(h.S.Board.FindLayer(h.Top.Id).Opacity == 1.0, "undo L(1)");

            var k2 = Fixture();
            k2.S.SetLayerOpacity(k2.Top.Id, 0.5);
            Drag(k2.S, V(0.1, 0.5), V(0.4, 0.5));
            k2.S.SetLayerOpacity(k2.Top.Id, 0.25);
            Check.That(k2.S.UndoCount == 3, $"opacity, line, opacity: 3 steps, got {k2.S.UndoCount}");
            var k3 = Fixture();
            k3.S.SetLayerOpacity(k3.Top.Id, 0.5);
            k3.S.SetGrid(2);
            k3.S.SetLayerOpacity(k3.Top.Id, 0.25);
            Check.That(k3.S.UndoCount == 3, $"opacity, tiles, opacity: 3 steps, got {k3.S.UndoCount}");

            // Undo (and Redo) end the merge: the next edit is a new step.
            var m = Fixture();
            m.S.SetLayerOpacity(m.Top.Id, 0.5);
            m.S.Undo();
            m.S.SetLayerOpacity(m.Top.Id, 0.25);
            Check.That(m.S.UndoCount == 1, "after Undo, a new step");
            m.S.Undo();
            Check.That(m.S.Board.FindLayer(m.Top.Id).Opacity == 1.0, "and it undoes to the original");
            var n = Fixture();
            n.S.SetLayerOpacity(n.Top.Id, 0.5);
            n.S.Undo();
            n.S.Redo();
            n.S.SetLayerOpacity(n.Top.Id, 0.25);
            Check.That(n.S.UndoCount == 2, $"after Redo, a new step: got {n.S.UndoCount}");
            n.S.Undo();
            Check.That(n.S.Board.FindLayer(n.Top.Id).Opacity == 0.5, "Undo returns to the redone value");

            // LoadBoard ends it: the loaded board's layer has the same id as before (#1).
            var o = new EditorSession();
            EntityId id = o.Board.Layers[0].Id;
            o.SetLayerOpacity(id, 0.5);
            o.LoadBoard(GoldenBoards.Empty());
            Check.That(o.Board.Layers[0].Id == id, "precondition: same layer id after Load");
            o.SetLayerOpacity(id, 0.25);
            Check.That(o.UndoCount == 1, "after LoadBoard the first opacity edit is a step");
            o.Undo();
            Check.That(o.Board.Layers[0].Opacity == 1.0, "and undoes to the loaded value");
        }

        // ================= snapshot isolation =================

        public static void V_SnapshotsAreNeverAliasedToTheLiveBoard()
        {
            // Move twice, Undo twice: the original doubles; Redo twice: the moved ones.
            var f = Fixture();
            EditorSession s = f.S;
            string orig = Dump(s.Board, nextId: false);
            Enter(s, ToolId.Move, f.LT);
            Drag(s, V(0.5, 0.5), V(0.53, 0.47));
            Drag(s, V(0.5, 0.5), V(0.47, 0.41));
            string twice = Dump(s.Board);
            s.Undo();
            s.Undo();
            SameDump(Dump(s.Board, nextId: false), orig, "Move, Move, Undo, Undo");
            s.Redo();
            s.Redo();
            SameDump(Dump(s.Board), twice, "Redo, Redo");

            // Move, Undo, Redo, Move again, Undo twice: the board that went live from a stack and was edited never
            // corrupted the step still stored.
            var g = Fixture();
            string g0 = Dump(g.S.Board, nextId: false);
            Enter(g.S, ToolId.Move, g.LT);
            Drag(g.S, V(0.5, 0.5), V(0.53, 0.47));
            string g1 = Dump(g.S.Board, nextId: false);
            g.S.Undo();
            g.S.Redo();
            Drag(g.S, V(0.5, 0.5), V(0.45, 0.47));
            g.S.Undo();
            SameDump(Dump(g.S.Board, nextId: false), g1, "first Undo: after the first Move");
            g.S.Undo();
            SameDump(Dump(g.S.Board, nextId: false), g0, "second Undo: the exact pre-first-Move doubles");

            // Edit the board that came off the undo stack: the new step is a clone of it, so Undo still restores it.
            var h = Fixture();
            string h0 = Dump(h.S.Board, nextId: false);
            Enter(h.S, ToolId.Move, h.LT);
            Drag(h.S, V(0.5, 0.5), V(0.53, 0.47));
            h.S.Undo();
            Board restored = h.S.Board;
            Drag(h.S, V(0.5, 0.5), V(0.4, 0.5));
            Check.That(ReferenceEquals(h.S.Board, restored), "an edit mutates the live board in place");
            h.S.Undo();
            SameDump(Dump(h.S.Board, nextId: false), h0, "Undo after editing the restored board");
            Check.That(!h.S.CanRedo || h.S.RedoCount == 1, "redo holds only the latest edit");
        }

        public static void V_UndoAndRedoNeverCloneTheLiveBoard()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Board b0 = s.Board;
            Drag(s, V(0.1, 0.1), V(0.5, 0.1));
            Check.That(ReferenceEquals(s.Board, b0), "a commit edits the live board in place");
            s.Undo();
            Board restored = s.Board;
            Check.That(!ReferenceEquals(restored, b0), "Undo: the stored snapshot became live");
            s.Redo();
            Check.That(ReferenceEquals(s.Board, b0), "Redo: the board Undo moved to the redo stack is live again (no clone)");
            s.Undo();
            Check.That(ReferenceEquals(s.Board, restored), "Undo again: the same snapshot instance (moved back, no clone)");
        }

        public static void V_DeepCloneSharesNothingAndKeepsNullEntries()
        {
            var f = Fixture();
            Board b = f.S.Board;
            b.Layers[0].Shapes.Add(null);
            b.Layers[0].SnapPoints.Add(null);
            b.Layers.Add(null);
            b.GridKind = GridKind.Hex;
            b.Layers[1].Name = "Hidden name";
            string d = Dump(b);

            Board c = b.DeepClone();
            SameDump(Dump(c), d, "DeepClone copies every field, in order, null entries kept");
            Check.That(!ReferenceEquals(c, b) && !ReferenceEquals(c.Layers, b.Layers), "new board and layer list");
            for (int i = 0; i < b.Layers.Count; i++)
            {
                Layer o = b.Layers[i], n = c.Layers[i];
                if (o == null) { Check.That(n == null, $"layer {i}: null kept"); continue; }
                Check.That(!ReferenceEquals(o, n) && !ReferenceEquals(o.Shapes, n.Shapes) && !ReferenceEquals(o.SnapPoints, n.SnapPoints),
                    $"layer {i}: new Layer, Shapes and SnapPoints");
                for (int k = 0; k < o.Shapes.Count; k++)
                    Check.That(o.Shapes[k] == null ? n.Shapes[k] == null : !ReferenceEquals(o.Shapes[k], n.Shapes[k]), $"layer {i} shape {k}: new instance");
                for (int k = 0; k < o.SnapPoints.Count; k++)
                    Check.That(o.SnapPoints[k] == null ? n.SnapPoints[k] == null : !ReferenceEquals(o.SnapPoints[k], n.SnapPoints[k]),
                        $"layer {i} snap point {k}: new instance");
            }

            // Mutating either side leaves the other alone.
            ((LineShape)c.Layers[0].Shapes[0]).A = V(0.9, 0.9);
            c.Layers[0].SnapPoints[0].Position = V(0.1, 0.1);
            c.Layers[2].Opacity = 0.1;
            c.Layers[2].Shapes.RemoveAt(0);
            c.NextId += 100;
            c.GridN = 1;
            SameDump(Dump(b), d, "editing the clone leaves the source unchanged");
            Board c2 = b.DeepClone();
            f.L1.B = V(0.0, 0.0);
            b.Layers[1].Visible = true;
            SameDump(Dump(c2), d, "editing the source leaves the clone unchanged");

            Layer lc = f.Top.DeepClone();
            Check.That(lc.Id == f.Top.Id && lc.Name == f.Top.Name && lc.Visible == f.Top.Visible && lc.Opacity == f.Top.Opacity
                       && lc.Shapes.Count == f.Top.Shapes.Count && !ReferenceEquals(lc.Shapes[0], f.Top.Shapes[0]),
                "Layer.DeepClone: same id, name, visibility, opacity; cloned shapes");
        }

        // ================= event counts of ordinary edits; CanUndo inside BoardChanged =================

        public static void V_EditsKeepTheirEventCountsAndThePushRaisesNothing()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            var r = new Rec(s);
            Drag(s, V(0.1, 0.1), V(0.5, 0.1));   // CanUndo flips false -> true here
            Check.That(r.Boards.Count == 1 && r.Boards[0] == BoardChangeKind.Shapes && r.States == 0 && r.Toasts.Count == 0,
                $"first Line commit: one BoardChanged(Shapes), no StateChanged, no toast; got {r.B} S {r.States} {r.T}");
            Check.That(r.AtBoard[0].canUndo && r.AtBoard[0].undo == 1, "CanUndo already true inside the commit's BoardChanged");
            r.Reset();
            Drag(s, V(0.1, 0.3), V(0.5, 0.3));
            Check.That(r.Boards.Count == 1 && r.States == 0, "second commit: the same");
            r.Reset();
            s.AddLayer();
            Check.That(r.Boards.Count == 1 && r.Boards[0] == BoardChangeKind.Layers && r.States == 1,
                $"AddLayer: one BoardChanged(Layers) + one StateChanged; got {r.B} S {r.States}");
            r.Reset();
            s.SelectLayer(s.Board.Layers[0].Id);
            Check.That(r.Boards.Count == 1 && r.States == 1 && s.UndoCount == 3, "SelectLayer: its old events, no step");

            // Undo inside its BoardChanged: the history has already moved.
            r.Reset();
            s.Undo();
            Check.That(r.AtBoard.Count == 1 && r.AtBoard[0].undo == 2 && r.AtBoard[0].redo == 1 && r.AtBoard[0].canRedo,
                "Undo: counts already updated inside BoardChanged");
        }

        // ================= allocation =================

        sealed class AffineMetric : ISnapMetric
        {
            public double Scale = 1000, Tol = 24, FailAboveY = double.PositiveInfinity;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                if (board.Y >= FailAboveY) { screenPx = default; return false; }
                screenPx = new Vec2(board.X * Scale + 11, board.Y * Scale - 7);
                return true;
            }
        }

        public static void V_MovePathStillAllocatesNothingWithAHistory()
        {
            foreach (bool withMetric in new[] { false, true })
                foreach (var tool in new[] { ToolId.Line, ToolId.Circle, ToolId.Move, ToolId.Scale, ToolId.Duplicate })
                {
                    string w = $"{tool}, metric {withMetric}";
                    var board = GoldenBoards.StarOnFullBoard();
                    var top = board.Layers[board.Layers.Count - 1];
                    EntityId topId = top.Id;
                    var ids = new List<EntityId>();
                    for (int i = 0; i < 12; i++)
                    {
                        ids.Add(AddLine(board, top, 0.05 + 0.07 * i, 0.05, 0.08 + 0.07 * i, 0.12).Id);
                        ids.Add(AddCircle(board, top, 0.08 + 0.07 * i, 0.2, 0.02).Id);
                    }
                    var s = new EditorSession(board);
                    if (withMetric) s.SnapMetric = new AffineMetric { Scale = 1000, Tol = 24 };
                    s.SetGrid(3);
                    s.SetGrid(4);
                    s.SetLayerOpacity(topId, 0.5);
                    s.Undo();   // the live board now came off the undo stack
                    Check.That(s.UndoCount == 2 && s.RedoCount == 1, $"{w}: precondition, history 2 + 1");
                    if (tool == ToolId.Line || tool == ToolId.Circle) s.SetTool(tool);
                    else
                    {
                        s.SetTool(ToolId.SelectMove);
                        s.SetSelection(ids);
                        s.SetTool(tool);
                    }
                    Check.That(s.ActiveTool == tool, $"{w}: tool");

                    var moves = new PointerEvent[200];
                    for (int i = 0; i < moves.Length; i++)
                    {
                        double t = i / 199.0;
                        moves[i] = new PointerEvent(PointerPhase.Move,
                            new Vec2(0.5 + 0.35 * Math.Sin(5 * t), 0.35 + 0.3 * Math.Abs(Math.Sin(3 * t + 0.4))), true, i);
                    }
                    var down = new PointerEvent(PointerPhase.Down, new Vec2(0.5, 0.35), true, 0);
                    var cancel = new PointerEvent(PointerPhase.Cancel, new Vec2(0.5, 0.35), false, 0);
                    var up = new PointerEvent(PointerPhase.Up, moves[moves.Length - 1].BoardPos, true, 300);

                    s.Pointer(down);   // warm-up: pools and candidate lists reach full size
                    for (int i = 0; i < moves.Length; i++) s.Pointer(moves[i]);
                    s.Pointer(cancel);
                    Check.That(s.UndoCount == 2 && s.RedoCount == 1, $"{w}: a cancelled gesture adds no step");

                    s.Pointer(down);
                    s.Pointer(moves[0]);
                    s.Pointer(moves[1]);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 2; i < moves.Length; i++) s.Pointer(moves[i]);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    Check.That(bytes == 0, $"{w}: 198 Moves allocated {bytes} bytes with a history");
                    Check.That(s.UndoCount == 2 && s.RedoCount == 1, $"{w}: no step before Up");
                    s.Pointer(up);
                    Check.That(s.UndoCount == 3 && s.RedoCount == 0, $"{w}: the Up committed one step ({s.UndoCount}/{s.RedoCount})");
                }
        }
    }
}
