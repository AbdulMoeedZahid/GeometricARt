using System;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>
    /// Snap feel (ui-line-plan 4.2-4.3): the sticky release radius (1.5 tol, BoardCap, 4/32 tol switch margin, reset per
    /// gesture, never on the gesture's own start, dropped by Snap off and by a vanished candidate), the tie window (grid
    /// fillers lose close ties; shape vs construction by distance), the 10 px tap slop at 32 px, SnapAcquired and the
    /// allocation-free Move path. A top-down metric of 1000 px per board unit makes 1 px = 0.001 board units.
    /// </summary>
    public static class SnapStickyChecks
    {
        sealed class TopDown : ISnapMetric
        {
            public double TolerancePx => 32;

            public bool TryToScreen(Vec2 board, out Vec2 screenPx)
            {
                screenPx = new Vec2(board.X * 1000, board.Y * 1000);
                return true;
            }
        }

        static Vec2 V(double x, double y) => new Vec2(x, y);
        static PointerEvent Ev(PointerPhase phase, Vec2 p) => new PointerEvent(phase, p, true, 0);

        /// <summary>One-tile grid (few grid points) and user points C (0.3, 0.3) and D (0.35, 0.3).</summary>
        static EditorSession Session(bool metric = true)
        {
            var s = new EditorSession { SnapMetric = metric ? new TopDown() : null };
            s.SetGrid(1);
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), V(0.3, 0.3)));
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), V(0.35, 0.3)));
            return s;
        }

        static void Down(EditorSession s, Vec2 p) => s.Pointer(Ev(PointerPhase.Down, p));
        static void Move(EditorSession s, Vec2 p) => s.Pointer(Ev(PointerPhase.Move, p));

        static Vec2 UpEnd(EditorSession s, Vec2 p)
        {
            int n = s.ActiveLayer.Shapes.Count;
            s.Pointer(Ev(PointerPhase.Up, p));
            Check.That(s.ActiveLayer.Shapes.Count == n + 1, "a line was committed");
            return ((LineShape)s.ActiveLayer.Shapes[n]).B;
        }

        static string P(Vec2 v) => $"({v.X:R}, {v.Y:R})";

        public static void Constants()
        {
            Check.Near(EditorSession.TapSlopFraction * 32, 10, "tap slop is still 10 px at 32 px", 1e-12);
            Check.That(EditorSession.StickyFactor == 1.5, "sticky radius 1.5 tol (48 px)");
            Check.Near(EditorSession.StickyMarginFraction * 32, 4, "switch margin 4 px", 1e-12);
            Check.Near(EditorSession.TieWindowFraction * 32, 6, "tie window 6 px", 1e-12);
        }

        public static void StickyHoldsUntil48PxAndUpCommitsItExactly()
        {
            var s = Session();
            Down(s, V(0.3, 0.6));
            Move(s, V(0.302, 0.303));                 // 3.6 px from C: snaps
            Move(s, V(0.3, 0.26));                    // 40 px from C: outside 32, inside 48
            Check.That(s.Cursor == V(0.3, 0.3), $"still on C at 40 px, got {s.Cursor}");
            Vec2 end = UpEnd(s, V(0.3, 0.255));       // 45 px
            Check.That(end == V(0.3, 0.3), $"Up commits the sticky candidate exactly, got {P(end)}");

            var raw = Session();
            Down(raw, V(0.3, 0.6));
            Move(raw, V(0.3, 0.26));
            Check.That(UpEnd(raw, V(0.3, 0.26)) == V(0.3, 0.26), "control: 40 px without a previous snap stays raw");

            var away = Session();
            Down(away, V(0.3, 0.6));
            Move(away, V(0.3, 0.301));
            Move(away, V(0.3, 0.25));                 // 50 px: released
            Check.That(UpEnd(away, V(0.3, 0.25)) == V(0.3, 0.25), "beyond 48 px the sticky lets go");
        }

        public static void StickySwitchesOnlyToACandidateNearerByMoreThan4Px()
        {
            var s = Session();
            Down(s, V(0.3, 0.6));
            Move(s, V(0.301, 0.3));                   // on C
            Move(s, V(0.3265, 0.3));                  // C 26.5 px, D 23.5 px: 3 px nearer, inside the margin
            Check.That(s.Cursor == V(0.3, 0.3), $"stays on C, got {s.Cursor}");
            Move(s, V(0.329, 0.3));                   // C 29 px, D 21 px: 8 px nearer
            Check.That(s.Cursor == V(0.35, 0.3), $"switches to D, got {s.Cursor}");
            Move(s, V(0.3265, 0.3));                  // now D is sticky: C is only 3 px nearer
            Check.That(UpEnd(s, V(0.3265, 0.3)) == V(0.35, 0.3), "and D holds the same way");
        }

        public static void StickyResetsPerGestureAndNeverHoldsTheStart()
        {
            var s = Session();
            Down(s, V(0.3, 0.6));
            Move(s, V(0.3, 0.301));
            UpEnd(s, V(0.3, 0.301));
            Down(s, V(0.6, 0.6));
            Move(s, V(0.3, 0.26));
            Check.That(UpEnd(s, V(0.3, 0.26)) == V(0.3, 0.26), "a new gesture starts without a sticky");

            var start = Session();
            Down(start, V(0.301, 0.301));             // snapped Down: the line starts on C
            Move(start, V(0.302, 0.31));              // still captured by C (10 px): the end sits on the start
            Move(start, V(0.3, 0.26));                // 40 px: the start is never sticky
            Vec2 end = UpEnd(start, V(0.3, 0.26));
            Check.That(end == V(0.3, 0.26), $"a short line from a snapped start can be drawn, got {P(end)}");
            Check.That(((LineShape)start.ActiveLayer.Shapes[0]).A == V(0.3, 0.3), "and it starts exactly on C");

            var cancel = Session();
            Down(cancel, V(0.3, 0.6));
            Move(cancel, V(0.3, 0.301));
            cancel.Pointer(new PointerEvent(PointerPhase.Cancel, V(0.3, 0.301), true, 0));
            Down(cancel, V(0.3, 0.6));
            Move(cancel, V(0.3, 0.26));
            Check.That(UpEnd(cancel, V(0.3, 0.26)) == V(0.3, 0.26), "Cancel resets the sticky");
        }

        public static void SnapOffMidDragDropsTheSticky()
        {
            var s = Session();
            Down(s, V(0.3, 0.6));
            Move(s, V(0.3, 0.301));
            s.SetSnapEnabled(false);
            Move(s, V(0.3, 0.29));                    // 10 px: would still be captured
            Check.That(UpEnd(s, V(0.3, 0.29)) == V(0.3, 0.29), "Snap off: the end is raw");
        }

        public static void StickyCandidateThatDisappearsIsDropped()
        {
            var s = Session();
            EntityId drawLayer = s.Board.ActiveLayerId;
            EntityId dots = s.AddLayer();             // E on its own layer
            s.ActiveLayer.SnapPoints.Add(new SnapPoint(s.Board.NewId(), V(0.6, 0.3)));
            s.SelectLayer(drawLayer);
            Down(s, V(0.6, 0.6));
            Move(s, V(0.6, 0.301));                   // on E
            s.SetLayerVisible(dots, false);           // E is no longer a candidate
            Move(s, V(0.6, 0.27));                    // 30 px: inside the sticky radius
            Check.That(s.Cursor == V(0.6, 0.27), $"a hidden dot is not held, got {s.Cursor}");

            var up = Session();
            drawLayer = up.Board.ActiveLayerId;
            dots = up.AddLayer();
            up.ActiveLayer.SnapPoints.Add(new SnapPoint(up.Board.NewId(), V(0.6, 0.3)));
            up.SelectLayer(drawLayer);
            Down(up, V(0.6, 0.6));
            Move(up, V(0.6, 0.301));
            Move(up, V(0.6, 0.27));                   // held at 30 px
            up.SetLayerVisible(dots, false);
            Check.That(UpEnd(up, V(0.6, 0.27)) == V(0.6, 0.27), "hidden just before Up: never committed onto it");
        }

        public static void StickyWithoutMetricUsesBoardUnits()
        {
            var s = Session(metric: false);           // tol 0.02, sticky 0.03, cap 0.03
            Down(s, V(0.3, 0.6));
            Move(s, V(0.3, 0.301));
            Move(s, V(0.3, 0.275));                   // 0.025
            Check.That(s.Cursor == V(0.3, 0.3), "0.025 board: held");
            Move(s, V(0.3, 0.265));                   // 0.035
            Check.That(UpEnd(s, V(0.3, 0.265)) == V(0.3, 0.265), "0.035 board: released");
        }

        public static void TieWindow_GridFillerLosesCloseTiesOnly()
        {
            // Edge midpoint (0.25, 0.5) of the 2x2 grid vs an intersection 10 px away at (0.26, 0.5).
            var s = new EditorSession { SnapMetric = new TopDown() };
            s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), V(0.26, 0.4), V(0.26, 0.6), StrokeStyle.Default));
            s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), V(0.2, 0.44), V(0.32, 0.56), StrokeStyle.Default));
            Down(s, V(0.25, 0.85));
            Check.That(UpEnd(s, V(0.2535, 0.5)) == V(0.26, 0.5), "midpoint 3 px nearer: the intersection wins (6 px window)");
            Down(s, V(0.25, 0.85));
            Check.That(UpEnd(s, V(0.249, 0.5)) == V(0.25, 0.5), "midpoint 10 px nearer: the midpoint wins");
        }

        public static void TieWindow_ShapeAndConstructionPointsGoByDistance()
        {
            // Circle division (0.7, 0.5) vs an intersection 5 px away at (0.705, 0.5) (steep lines that miss the circle).
            var s = new EditorSession { SnapMetric = new TopDown() };
            s.ActiveLayer.Shapes.Add(new CircleShape(s.Board.NewId(), V(0.5, 0.5), 0.2, StrokeStyle.Default));
            s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), V(0.685, 0.4), V(0.725, 0.6), StrokeStyle.Default));
            s.ActiveLayer.Shapes.Add(new LineShape(s.Board.NewId(), V(0.725, 0.4), V(0.685, 0.6), StrokeStyle.Default));
            Intersect.SegmentSegment(V(0.685, 0.4), V(0.725, 0.6), V(0.725, 0.4), V(0.685, 0.6), out Vec2 x);
            Check.That(Vec2.Distance(x, V(0.705, 0.5)) < 1e-12, "precondition: the lines cross at (0.705, 0.5)");
            Down(s, V(0.4, 0.85));
            Vec2 got = UpEnd(s, V(0.7035, 0.5));
            Check.That(Vec2.Distance(got, x) < 1e-12, $"intersection nearer: it wins, got {P(got)}");
            Down(s, V(0.4, 0.85));
            Check.That(UpEnd(s, V(0.7015, 0.5)) == V(0.7, 0.5), "division 2 px nearer: the division wins (no window)");
        }

        public static void TapSlopIsStillTenPixels()
        {
            var s = Session();
            Down(s, V(0.3, 0.3));
            s.Pointer(Ev(PointerPhase.Up, V(0.3, 0.309)));   // 9 px: a tap
            Check.That(s.PendingConnectStart == V(0.3, 0.3), "9 px is a tap");
            var d = Session();
            Down(d, V(0.3, 0.3));
            d.Pointer(Ev(PointerPhase.Up, V(0.3, 0.311)));   // 11 px: a drag that snaps back onto the start
            Check.That(!d.PendingConnectStart.HasValue && d.ActiveLayer.Shapes.Count == 0, "11 px is a drag (nothing committed)");
        }

        public static void SnapAcquired_OncePerNewAcquisition()
        {
            var s = Session();
            int n = 0;
            SnapCandidate last = default;
            s.SnapAcquired += c => { n++; last = c; };
            Down(s, V(0.6, 0.6));
            Check.That(n == 0, "raw Down: nothing");
            Move(s, V(0.301, 0.3));
            Check.That(n == 1 && last.Position == V(0.3, 0.3), "C acquired");
            Move(s, V(0.302, 0.301));
            Move(s, V(0.3, 0.27));                    // still C (sticky)
            Check.That(n == 1, "same candidate: once");
            Move(s, V(0.349, 0.3));
            Check.That(n == 2 && last.Position == V(0.35, 0.3), "D acquired");
            Move(s, V(0.5, 0.7));                     // leaves every candidate
            Move(s, V(0.351, 0.301));
            Check.That(n == 3, "coming back counts again");
            s.Pointer(Ev(PointerPhase.Up, V(0.351, 0.301)));
            Down(s, V(0.3005, 0.3));
            Check.That(n == 4, "a snapped Down");
            s.Pointer(Ev(PointerPhase.Cancel, V(0.3, 0.3)));

            s.SetTool(ToolId.SelectMove);
            Down(s, V(0.3, 0.3));
            Move(s, V(0.35, 0.3));
            s.Pointer(Ev(PointerPhase.Up, V(0.35, 0.3)));
            Check.That(n == 4, "Select never snaps");
            s.SetTool(ToolId.Line);
            s.SetSnapEnabled(false);
            Down(s, V(0.3, 0.3));
            Move(s, V(0.35, 0.3));
            s.Pointer(Ev(PointerPhase.Up, V(0.35, 0.3)));
            Check.That(n == 4, "snapping off: nothing");
        }

        public static void SnapAcquired_AlsoForMoveReferenceSnaps()
        {
            var s = Session();
            var line = new LineShape(s.Board.NewId(), V(0.6, 0.6), V(0.7, 0.62), StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(line);
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { line.Id });
            s.SetTool(ToolId.Move);
            int n = 0;
            s.SnapAcquired += _ => n++;
            Down(s, V(0.701, 0.62));
            Move(s, V(0.65, 0.5));
            Move(s, V(0.351, 0.301));                 // the B end (reference) lands on D
            Check.That(n == 1, $"Move reference snap acquired once (got {n})");
            s.Pointer(Ev(PointerPhase.Up, V(0.351, 0.301)));
            Check.That(line.B == V(0.35, 0.3), "and it lands exactly");
        }

        public static void SnapAcquired_AlsoForDuplicateReferenceSnaps()
        {
            var s = Session();
            var line = new LineShape(s.Board.NewId(), V(0.6, 0.6), V(0.7, 0.62), StrokeStyle.Default);
            s.ActiveLayer.Shapes.Add(line);
            s.SetTool(ToolId.SelectMove);
            s.SetSelection(new[] { line.Id });
            s.SetTool(ToolId.Duplicate);
            int n = 0;
            s.SnapAcquired += _ => n++;
            Down(s, V(0.701, 0.62));
            Move(s, V(0.65, 0.5));
            Move(s, V(0.351, 0.301));                 // the copy's B end lands on D
            Move(s, V(0.352, 0.3005));                // still D
            Check.That(n == 1, $"Duplicate reference snap acquired once (got {n})");
            s.Pointer(Ev(PointerPhase.Up, V(0.352, 0.3005)));
            Check.That(s.ActiveLayer.Shapes.Count == 2 && ((LineShape)s.ActiveLayer.Shapes[1]).B == V(0.35, 0.3),
                "the copy lands exactly on D and the original stays");
        }

        static int s_count;
        static void Count(SnapCandidate c) => s_count++;

        public static void StickyMovePathAllocatesNothing()
        {
            var s = Session();
            s.SnapAcquired += Count;
            Down(s, V(0.6, 0.6));
            Move(s, V(0.3, 0.301));
            for (int i = 0; i < 20; i++) Move(s, V(0.3 + 0.0005 * (i % 7), 0.27 + 0.001 * (i % 5)));   // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                Move(s, V(0.3 + 0.0005 * (i % 7), 0.27 + 0.001 * (i % 5)));   // held by the sticky
                Move(s, V(0.349 + 0.0003 * (i % 3), 0.3));                     // switches to D and back
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.That(bytes == 0, $"200 sticky Moves allocate nothing (got {bytes} bytes)");
            Check.That(s_count > 2, "and SnapAcquired fired along the way");
        }
    }
}
