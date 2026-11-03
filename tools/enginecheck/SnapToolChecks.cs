using System;
using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>E2: snapping in the Line/Circle tools, tap-to-connect, the Snap toggle, screen-space tolerances.</summary>
    public static class SnapToolChecks
    {
        static double s_T;
        static PointerEvent Ev(PointerPhase p, double x, double y, bool on = true) =>
            new PointerEvent(p, new Vec2(x, y), on, s_T += 0.016);

        static void Drag(EditorSession s, double ax, double ay, double bx, double by, int steps = 6)
        {
            s.Pointer(Ev(PointerPhase.Down, ax, ay));
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                s.Pointer(Ev(PointerPhase.Move, ax + (bx - ax) * t, ay + (by - ay) * t));
            }
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

        static LineShape OnlyLine(EditorSession s)
        {
            Check.That(ShapeCount(s) == 1, $"exactly one shape, got {ShapeCount(s)}");
            var line = s.ActiveLayer.Shapes[0] as LineShape;
            Check.That(line != null, "a LineShape");
            return line;
        }

        static string P(Vec2 v) => $"({v.X:R}, {v.Y:R})";

        /// <summary>Rings (Thin solid CircleShapes of HoverRingRadius) in Preview, in order.</summary>
        static List<CircleShape> Rings(EditorSession s)
        {
            var r = new List<CircleShape>();
            foreach (var sh in s.Preview)
                if (sh is CircleShape c && c.Radius == EditorSession.HoverRingRadius && c.Style.Width == LineWidth.Thin
                    && c.Style.Dash == LineStyle.Solid)
                    r.Add(c);
            return r;
        }

        /// <summary>screen = board * Scale + Offset (pixels); optionally unprojectable above <see cref="FailAboveY"/>.</summary>
        sealed class AffineMetric : ISnapMetric
        {
            public double Scale = 1000, OffX = 37, OffY = -12, Tol = 24, FailAboveY = double.PositiveInfinity;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                if (board.Y >= FailAboveY) { screenPx = default; return false; }
                screenPx = new Vec2(board.X * Scale + OffX, board.Y * Scale + OffY);
                return true;
            }
        }

        /// <summary>Perspective-like projective map: w = 1 + 0.8 y, screen = 1000 * (x, y) / w. Non-uniform and anisotropic.</summary>
        sealed class PerspectiveMetric : ISnapMetric
        {
            public double Tol = 12;
            public double TolerancePx => Tol;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                double w = 1 + 0.8 * board.Y;
                screenPx = new Vec2(1000 * board.X / w, 1000 * board.Y / w);
                return true;
            }
        }

        // ---------- drag snapping ----------

        public static void DragEndWithinToleranceCommitsCandidateExactly()
        {
            var s = new EditorSession();
            Drag(s, 0.1, 0.12, 0.503, 0.498);   // (0.5, 0.5) is a cell corner, 0.0036 away
            var line = OnlyLine(s);
            Check.That(line.A == new Vec2(0.1, 0.12), $"raw start kept (nothing in range), got {P(line.A)}");
            Check.That(line.B == new Vec2(0.5, 0.5), $"end is the corner exactly, got {P(line.B)}");
        }

        public static void DragEndOutsideToleranceCommitsRaw()
        {
            var s = new EditorSession();
            Drag(s, 0.1, 0.12, 0.53, 0.5);   // nearest candidate (0.5, 0.5) is 0.03 away > 0.02
            var line = OnlyLine(s);
            Check.That(line.B == new Vec2(0.53, 0.5), $"raw end, got {P(line.B)}");
        }

        public static void SnapOffCommitsRaw()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);
            Drag(s, 0.002, 0.003, 0.503, 0.498);
            var line = OnlyLine(s);
            Check.That(line.A == new Vec2(0.002, 0.003) && line.B == new Vec2(0.503, 0.498), $"raw both ends, got {P(line.A)} {P(line.B)}");
            Check.That(s.SnapCandidates.Count == 0, "no candidates while off");

            s.SetTool(ToolId.Circle);
            Drag(s, 0.252, 0.247, 0.497, 0.252);
            var c = (CircleShape)s.ActiveLayer.Shapes[1];
            Check.That(c.Center == new Vec2(0.252, 0.247), "raw centre");
            Check.That(c.Radius == Vec2.Distance(new Vec2(0.252, 0.247), new Vec2(0.497, 0.252)), "raw radius");

            s.SetSnapEnabled(true);
            Drag(s, 0.752, 0.747, 0.997, 0.752);
            var c2 = (CircleShape)s.ActiveLayer.Shapes[2];
            Check.That(c2.Center == new Vec2(0.75, 0.75) && c2.Radius == 0.25, "snapping back on snaps again");
        }

        public static void LineStartSnapsOnDown()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.503, 0.497));
            Check.That(s.Cursor == new Vec2(0.5, 0.5), $"cursor shows the snapped start, got {s.Cursor}");
            var rings = Rings(s);
            Check.That(rings.Count == 1 && rings[0].Center == new Vec2(0.5, 0.5), "hover ring on the start candidate");
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.62));
            s.Pointer(Ev(PointerPhase.Up, 0.9, 0.62));
            var line = OnlyLine(s);
            Check.That(line.A == new Vec2(0.5, 0.5) && line.B == new Vec2(0.9, 0.62), $"start snapped, end raw: {P(line.A)} {P(line.B)}");
        }

        public static void CircleCentreAndRadiusSnap()
        {
            var s = new EditorSession();
            s.SetTool(ToolId.Circle);
            s.Pointer(Ev(PointerPhase.Down, 0.252, 0.247));
            Check.That(s.Cursor == new Vec2(0.25, 0.25), "centre snaps on Down (cell centre)");
            Check.That(Rings(s).Count == 1, "hover ring on the centre");
            s.Pointer(Ev(PointerPhase.Move, 0.4, 0.3));
            s.Pointer(Ev(PointerPhase.Move, 0.497, 0.252));
            var ghost = (CircleShape)s.Preview[0];
            Check.That(ghost.Center == new Vec2(0.25, 0.25) && ghost.Radius == 0.25, "ghost uses the snapped radius point");
            s.Pointer(Ev(PointerPhase.Up, 0.497, 0.252));
            var c = (CircleShape)s.ActiveLayer.Shapes[0];
            Check.That(c.Center == new Vec2(0.25, 0.25), $"centre exact, got {P(c.Center)}");
            Check.That(c.Radius == 0.25, $"radius to the edge midpoint is exactly 0.25, got {c.Radius:R}");

            // Diagonal: radius == distance to the candidate, bit for bit.
            Drag(s, 0.248, 0.253, 0.503, 0.496);
            var d = (CircleShape)s.ActiveLayer.Shapes[1];
            Check.That(d.Center == new Vec2(0.25, 0.25), "second centre exact");
            Check.That(d.Radius == Vec2.Distance(new Vec2(0.25, 0.25), new Vec2(0.5, 0.5)), $"radius exact, got {d.Radius:R}");
        }

        public static void TieBreakIsDeterministic()
        {
            // Equidistant (dyadic values, so the distances are exactly equal): a user point beats a cell corner.
            var s = new EditorSession();
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.5, 0.5078125)));
            Drag(s, 0.1, 0.12, 0.5, 0.50390625);
            Check.That(OnlyLine(s).B == new Vec2(0.5, 0.5078125), $"priority wins a tie, got {P(OnlyLine(s).B)}");

            // Same priority, equal distance: the earlier candidate wins; swapping their order swaps the winner.
            for (int order = 0; order < 2; order++)
            {
                var t = new EditorSession();
                var u1 = new Vec2(0.375, 0.375);
                var u2 = new Vec2(0.375, 0.3828125);
                t.ActiveLayer.SnapPoints.Add(new SnapPoint(t.Board.NewId(), order == 0 ? u1 : u2));
                t.ActiveLayer.SnapPoints.Add(new SnapPoint(t.Board.NewId(), order == 0 ? u2 : u1));
                for (int rep = 0; rep < 3; rep++)
                    Drag(t, 0.1, 0.12 + 0.05 * rep, 0.375, 0.37890625);
                foreach (var sh in t.ActiveLayer.Shapes)
                    Check.That(((LineShape)sh).B == (order == 0 ? u1 : u2), $"order {order}: earlier candidate wins, every time");
            }

            // Nearest beats priority: a user point 0.015 away loses to a corner 0.005 away.
            var n = new EditorSession();
            n.ActiveLayer.SnapPoints.Add(new SnapPoint(n.Board.NewId(), new Vec2(0.5, 0.52)));
            Drag(n, 0.1, 0.12, 0.5, 0.505);
            Check.That(OnlyLine(n).B == new Vec2(0.5, 0.5), "nearer corner wins over a farther user point");
        }

        public static void GridIntersectionOnEmptyBoardSquareAndHex()
        {
            var s = new EditorSession();
            Drag(s, 0.003, 0.002, 0.748, 0.502);
            var line = OnlyLine(s);
            Check.That(line.A == new Vec2(0, 0), $"square start on corner (0,0), got {P(line.A)}");
            Check.That(line.B == new Vec2(0.75, 0.5), $"square end on edge midpoint (0.75, 0.5), got {P(line.B)}");

            var h = new EditorSession();
            h.SetGridKind(GridKind.Hex);
            var grid = new List<SnapCandidate>();
            TileGrid.SnapPoints(GridKind.Hex, h.Board.GridN, grid);
            var corners = new List<Vec2>();
            foreach (var c in grid)
                if (c.Kind == SnapKind.CellCorner && c.Position.X > 0.05 && c.Position.X < 0.95 && c.Position.Y > 0.05 && c.Position.Y < 0.95)
                    corners.Add(c.Position);
            Check.That(corners.Count >= 2, "hex has interior corners");
            Vec2 a = corners[0], b = corners[corners.Count - 1];
            Drag(h, a.X + 0.003, a.Y - 0.002, b.X - 0.002, b.Y + 0.003);
            var hl = OnlyLine(h);
            Check.That(hl.A == a && hl.B == b, $"hex corners exact: {P(hl.A)} vs {P(a)}, {P(hl.B)} vs {P(b)}");
        }

        public static void CandidatesRefreshAfterCommit()
        {
            var s = new EditorSession();
            Drag(s, 0.1, 0.4, 0.9, 0.4);
            foreach (var c in s.SnapCandidates) Check.That(c.Kind != SnapKind.Intersection, "no intersection with one line");
            Drag(s, 0.4, 0.1, 0.4, 0.9);
            Vec2? x = null;
            foreach (var c in s.SnapCandidates) if (c.Kind == SnapKind.Intersection) x = c.Position;
            Check.That(x.HasValue && Vec2.Distance(x.Value, new Vec2(0.4, 0.4)) < 1e-12, "crossing appears after the commit");
            Drag(s, 0.9, 0.9, 0.403, 0.398);
            var third = (LineShape)s.ActiveLayer.Shapes[2];
            Check.That(third.B == x.Value, $"third line ends on the new intersection exactly, got {P(third.B)}");
        }

        public static void AnyModelChangeRefreshesCandidates()
        {
            // An in-place edit keeps every count, so only the OnModelChanged -> MarkDirty hook can refresh the list.
            var s = new EditorSession();
            Drag(s, 0.1, 0.12, 0.9, 0.62);
            var line = (LineShape)s.ActiveLayer.Shapes[0];
            Check.That(HasEndpoint(s, new Vec2(0.9, 0.62)), "endpoint candidate after the commit");
            line.B = new Vec2(0.85, 0.3);   // moved, same counts
            s.SetLayerOpacity(s.ActiveLayer.Id, 0.5);   // any BoardChanged
            Check.That(HasEndpoint(s, new Vec2(0.85, 0.3)) && !HasEndpoint(s, new Vec2(0.9, 0.62)), "moved endpoint picked up");
            Drag(s, 0.1, 0.8, 0.853, 0.302);
            Check.That(((LineShape)s.ActiveLayer.Shapes[1]).B == new Vec2(0.85, 0.3), "snaps to the moved endpoint");
        }

        static bool HasEndpoint(EditorSession s, Vec2 p)
        {
            foreach (var c in s.SnapCandidates) if (c.Kind == SnapKind.Endpoint && c.Position == p) return true;
            return false;
        }

        // ---------- hover ring ----------

        public static void HoverRingOnlyWhileSnapped()
        {
            var s = new EditorSession();
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.12));
            Check.That(Rings(s).Count == 0, "no ring at a raw start");
            s.Pointer(Ev(PointerPhase.Move, 0.502, 0.503));
            var rings = Rings(s);
            Check.That(rings.Count == 1 && rings[0].Center == new Vec2(0.5, 0.5), "ring on the snapped end");
            Check.That(rings[0].Style.Color.Equals(s.Style.Color), "ring in the current colour");
            Check.That(s.Cursor == new Vec2(0.5, 0.5), "cursor on the snapped point");
            var ghost = (LineShape)s.Preview[0];
            Check.That(ghost.B == new Vec2(0.5, 0.5), "ghost ends on the candidate");
            s.Pointer(Ev(PointerPhase.Move, 0.4, 0.4));
            Check.That(Rings(s).Count == 0, "ring gone when nothing is in range");
            Check.That(s.Cursor == new Vec2(0.4, 0.4), "cursor raw again");
            s.Pointer(Ev(PointerPhase.Up, 0.4, 0.4));
            Check.That(s.Preview.Count == 0 && !s.Cursor.HasValue, "all cleared after the drag");
        }

        // ---------- tap-to-connect ----------

        public static void TapTapConnectsTwoCandidates()
        {
            var s = new EditorSession();
            var kinds = new List<BoardChangeKind>();
            s.BoardChanged += k => kinds.Add(k);
            Tap(s, 0.502, 0.497);
            Check.That(s.PendingConnectStart == new Vec2(0.5, 0.5), "first tap: pending start on the corner");
            Check.That(ShapeCount(s) == 0 && kinds.Count == 0, "nothing committed yet");
            Tap(s, 0.748, 0.253);
            var line = OnlyLine(s);
            Check.That(line.A == new Vec2(0.5, 0.5) && line.B == new Vec2(0.75, 0.25), $"exact candidate positions, got {P(line.A)} {P(line.B)}");
            Check.That(line.Style.Equals(s.Style) && !line.Id.IsNone, "current style, real id");
            Check.That(kinds.Count == 1 && kinds[0] == BoardChangeKind.Shapes, "BoardChanged(Shapes) once");
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "pending cleared, no ring");
        }

        public static void PendingRingPersistsAndClears()
        {
            var s = new EditorSession();
            int previews = 0;
            s.PreviewChanged += () => previews++;
            Tap(s, 0.502, 0.497);
            Check.That(!s.GestureActive && !s.Cursor.HasValue, "gesture over");
            var rings = Rings(s);
            Check.That(s.Preview.Count == 1 && rings.Count == 1 && rings[0].Center == new Vec2(0.5, 0.5), "pending ring stays after the tap");
            int before = previews;
            Tap(s, 0.4, 0.4);   // off any candidate
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "tap on empty clears pending and its ring");
            Check.That(previews > before, "PreviewChanged raised");
            Check.That(ShapeCount(s) == 0, "nothing drawn");

            Tap(s, 0.502, 0.497);
            Tap(s, 0.501, 0.499);   // same candidate again: toggles off, no zero-length line
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0 && ShapeCount(s) == 0, "same candidate toggles off");
        }

        public static void CancelToolSwitchBoardReplaceAndSnapOffClearPending()
        {
            var s = new EditorSession();
            Tap(s, 0.502, 0.497);
            s.Pointer(Ev(PointerPhase.Cancel, 0, 0, false));
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "Cancel without a gesture clears pending");

            Tap(s, 0.502, 0.497);
            s.Pointer(Ev(PointerPhase.Down, 0.748, 0.253));
            s.Pointer(Ev(PointerPhase.Cancel, 0.748, 0.253, false));
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0 && ShapeCount(s) == 0, "Cancel mid-tap clears pending");

            Tap(s, 0.502, 0.497);
            s.SetTool(ToolId.Circle);
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "tool switch clears pending");
            s.SetTool(ToolId.Line);

            Tap(s, 0.502, 0.497);
            s.ClearBoard();
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "ClearBoard clears pending");

            Tap(s, 0.502, 0.497);
            s.LoadBoard(GoldenBoards.Empty());
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "LoadBoard clears pending");

            Tap(s, 0.502, 0.497);
            int previews = 0;
            s.PreviewChanged += () => previews++;
            s.SetSnapEnabled(false);
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0 && previews == 1, "snap off clears pending (one PreviewChanged)");
            Tap(s, 0.502, 0.497);
            Check.That(!s.PendingConnectStart.HasValue && ShapeCount(s) == 0, "snap off: a tap only clears pending");

            s.SetSnapEnabled(true);
            Tap(s, 0.502, 0.497);
            s.Pointer(Ev(PointerPhase.Down, 1.3, 0.5, false));
            Check.That(!s.PendingConnectStart.HasValue, "a Down off the board is a tap off any candidate");
            Tap(s, 0.502, 0.497);
            s.Pointer(Ev(PointerPhase.Up, 0.748, 0.253));
            Check.That(s.PendingConnectStart.HasValue && ShapeCount(s) == 0, "a stray Up changes nothing");
        }

        public static void DragAfterPendingClearsPendingAndDrawsNormalLine()
        {
            var s = new EditorSession();
            Tap(s, 0.502, 0.497);
            s.Pointer(Ev(PointerPhase.Down, 0.1, 0.12));
            Check.That(s.PendingConnectStart.HasValue && Rings(s).Count == 1, "pending ring still shown at Down");
            s.Pointer(Ev(PointerPhase.Move, 0.3, 0.2));
            Check.That(!s.PendingConnectStart.HasValue && Rings(s).Count == 0, "pending dropped once it is a drag");
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.62));
            s.Pointer(Ev(PointerPhase.Up, 0.9, 0.62));
            var line = OnlyLine(s);
            Check.That(line.A == new Vec2(0.1, 0.12) && line.B == new Vec2(0.9, 0.62), "normal drag line, not connected to the pending start");
            Check.That(s.Preview.Count == 0, "nothing left in Preview");
        }

        public static void TapSlopUsesMaximumDistance()
        {
            var s = new EditorSession();   // fallback slop = 0.02 * 10/24 = 0.00833 board units
            s.Pointer(Ev(PointerPhase.Down, 0.5, 0.5));
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.509));
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.5));
            s.Pointer(Ev(PointerPhase.Up, 0.5, 0.5));
            Check.That(!s.PendingConnectStart.HasValue, "wandered out and back: a drag, not a tap");

            s.Pointer(Ev(PointerPhase.Down, 0.5, 0.5));
            s.Pointer(Ev(PointerPhase.Move, 0.5, 0.508));
            s.Pointer(Ev(PointerPhase.Up, 0.5, 0.505));
            Check.That(s.PendingConnectStart == new Vec2(0.5, 0.5), "stayed inside the slop: a tap");
            Check.That(ShapeCount(s) == 0, "never a zero-length commit");
        }

        public static void TapNeverCommitsTooShortLine()
        {
            var s = new EditorSession();
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.3, 0.3)));
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), new Vec2(0.305, 0.3)));
            Tap(s, 0.3, 0.3);
            Check.That(s.PendingConnectStart == new Vec2(0.3, 0.3), "pending on the first user point");
            Tap(s, 0.305, 0.3);
            Check.That(ShapeCount(s) == 0 && !s.PendingConnectStart.HasValue, "0.005 apart: nothing committed, pending cleared");
        }

        public static void CircleToolTapCommitsNothing()
        {
            var s = new EditorSession();
            s.SetTool(ToolId.Circle);
            int changes = 0;
            s.BoardChanged += _ => changes++;
            Tap(s, 0.502, 0.497);
            Check.That(!s.PendingConnectStart.HasValue, "no pending start in the Circle tool");
            Tap(s, 0.748, 0.253);
            Check.That(ShapeCount(s) == 0 && changes == 0 && s.Preview.Count == 0, "taps draw nothing in the Circle tool");

            // With a big screen metric a 0.012 board-unit wiggle is still a tap (12 px < 10/24 * 50 px): no circle.
            s.SnapMetric = new AffineMetric { Scale = 1000, Tol = 50 };
            s.Pointer(Ev(PointerPhase.Down, 0.4, 0.4));
            s.Pointer(Ev(PointerPhase.Move, 0.412, 0.4));
            s.Pointer(Ev(PointerPhase.Up, 0.412, 0.4));
            Check.That(ShapeCount(s) == 0, "a tap never commits a circle, even above MinStrokeLength");
        }

        public static void HiddenLayerOnConnectingTapToastsAndCommitsNothing()
        {
            var s = new EditorSession();
            var toasts = new List<string>();
            s.Toast += t => toasts.Add(t);
            Tap(s, 0.502, 0.497);
            s.ActiveLayer.Visible = false;
            Tap(s, 0.748, 0.253);
            Check.That(ShapeCount(s) == 0, "nothing committed");
            Check.That(toasts.Count == 1 && toasts[0] == EditorSession.HiddenLayerToast, "hidden-layer toast once");
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "pending cleared");

            // Hidden between the Down and the Up of the connecting tap.
            s.ActiveLayer.Visible = true;
            toasts.Clear();
            Tap(s, 0.502, 0.497);
            s.Pointer(Ev(PointerPhase.Down, 0.748, 0.253));
            s.ActiveLayer.Visible = false;
            s.Pointer(Ev(PointerPhase.Up, 0.748, 0.253));
            Check.That(ShapeCount(s) == 0, "nothing committed (hidden mid-tap)");
            Check.That(toasts.Count == 1 && toasts[0] == EditorSession.HiddenLayerToast, "toast once (hidden mid-tap)");
            Check.That(!s.PendingConnectStart.HasValue && s.Preview.Count == 0, "pending cleared (hidden mid-tap)");
        }

        // ---------- screen-space tolerance ----------

        public static void ScaleOffsetMetricToleranceIsInPixels()
        {
            // Scale 1000 px per board unit, 24 px tolerance = 0.024 board units (fallback would be 0.02).
            var s = new EditorSession { SnapMetric = new AffineMetric { Scale = 1000, Tol = 24 } };
            Drag(s, 0.1, 0.12, 0.5, 0.522);   // 22 px from (0.5, 0.5)
            Check.That(((LineShape)s.ActiveLayer.Shapes[0]).B == new Vec2(0.5, 0.5), "22 px snaps (0.022 board would not in fallback)");
            Drag(s, 0.1, 0.12, 0.5, 0.526);   // 26 px
            Check.That(((LineShape)s.ActiveLayer.Shapes[1]).B == new Vec2(0.5, 0.526), "26 px stays raw");

            // Scale 500: the same 24 px are 0.048 board units.
            var h = new EditorSession { SnapMetric = new AffineMetric { Scale = 500, OffX = -300, OffY = 999, Tol = 24 } };
            Drag(h, 0.1, 0.12, 0.5, 0.54);    // 0.04 board = 20 px
            Check.That(((LineShape)h.ActiveLayer.Shapes[0]).B == new Vec2(0.5, 0.5), "0.04 board units = 20 px snaps at scale 500");

            // Tap slop is in pixels too: at scale 2000, 0.008 board units = 16 px > 10 px slop, so it is a drag.
            var t = new EditorSession { SnapMetric = new AffineMetric { Scale = 2000, Tol = 24 } };
            t.Pointer(Ev(PointerPhase.Down, 0.5, 0.5));
            t.Pointer(Ev(PointerPhase.Up, 0.5, 0.508));
            Check.That(!t.PendingConnectStart.HasValue, "16 px movement is a drag (fallback would call 0.008 a tap)");
            t.Pointer(Ev(PointerPhase.Down, 0.5, 0.5));
            t.Pointer(Ev(PointerPhase.Up, 0.5, 0.504));
            Check.That(t.PendingConnectStart == new Vec2(0.5, 0.5), "8 px movement is a tap");
        }

        public static void PerspectiveMetricIsAnisotropic()
        {
            // At (0.5, 0.5): w = 1.4; dx -> 1000/1.4 px per unit, dy -> 1000/1.96 px per unit. 12 px tolerance.
            // 0.019 board units: along x 13.6 px (no snap), along y 9.7 px (snap). The fallback would snap both.
            var s = new EditorSession { SnapMetric = new PerspectiveMetric() };
            Drag(s, 0.1, 0.12, 0.519, 0.5);
            Check.That(((LineShape)s.ActiveLayer.Shapes[0]).B == new Vec2(0.519, 0.5), "x offset is too far on screen");
            Drag(s, 0.1, 0.12, 0.5, 0.519);
            Check.That(((LineShape)s.ActiveLayer.Shapes[1]).B == new Vec2(0.5, 0.5), "y offset is close on screen");

            // Depth: the same 0.015 x offset is 15 px near y = 0 (no snap) but 8.3 px near y = 1 (snap).
            Drag(s, 0.1, 0.12, 0.515, 0.0);
            Check.That(((LineShape)s.ActiveLayer.Shapes[2]).B == new Vec2(0.515, 0.0), "near edge: raw");
            Drag(s, 0.1, 0.12, 0.515, 1.0);
            Check.That(((LineShape)s.ActiveLayer.Shapes[3]).B == new Vec2(0.5, 1.0), "far edge: snapped");
        }

        public static void UnprojectableCandidatesAndPointerFallBack()
        {
            // Candidates the metric cannot project are skipped: (0.5, 1) is 15 px away but unprojectable.
            var s = new EditorSession { SnapMetric = new AffineMetric { Scale = 1000, Tol = 24, FailAboveY = 0.999 } };
            Drag(s, 0.1, 0.12, 0.5, 0.985);
            Check.That(((LineShape)s.ActiveLayer.Shapes[0]).B == new Vec2(0.5, 0.985), "unprojectable candidate skipped");

            // Pointer itself unprojectable: board units (0.02) with identity projection.
            var f = new EditorSession { SnapMetric = new AffineMetric { Scale = 1000, Tol = 1, FailAboveY = 0.9 } };
            Drag(f, 0.1, 0.12, 0.5, 0.985);
            Check.That(((LineShape)f.ActiveLayer.Shapes[0]).B == new Vec2(0.5, 1.0), "fallback tolerance when the pointer is not projectable");
        }

        // ---------- allocation ----------

        static int s_previews;

        public static void MovePathAllocatesNothing()
        {
            foreach (bool withMetric in new[] { false, true })
            {
                var s = new EditorSession(GoldenBoards.StarOnFullBoard());
                if (withMetric) s.SnapMetric = new PerspectiveMetric { Tol = 24 };
                s.PreviewChanged += () => s_previews++;
                foreach (var tool in new[] { ToolId.Line, ToolId.Circle })
                {
                    s.SetTool(tool);
                    Drag(s, 0.2, 0.3, 0.7, 0.6, 40);   // warm-up (rebuilds candidates)
                    Tap(s, 0.5, 0.5);                  // a pending ring too, in the Line tool
                    s.Pointer(Ev(PointerPhase.Down, 0.15, 0.2));
                    var moves = new PointerEvent[200];
                    for (int i = 0; i < moves.Length; i++)
                    {
                        // Sweeps across the star: in and out of candidates (ring on/off), ghost changing every time.
                        double t = i / 199.0;
                        moves[i] = new PointerEvent(PointerPhase.Move, new Vec2(0.1 + 0.8 * t, 0.5 + 0.35 * Math.Sin(7 * t)), true, i);
                    }
                    s.Pointer(moves[0]);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < moves.Length; i++) s.Pointer(moves[i]);
                    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    s.Pointer(Ev(PointerPhase.Up, 0.9, 0.5));
                    Check.That(bytes == 0, $"{tool}, metric {withMetric}: 200 Moves allocated {bytes} bytes");
                }
            }
        }
    }
}
