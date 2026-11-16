using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.EngineCheck
{
    /// <summary>Circle tool: a circle never leaves the [0,1]^2 board (radius capped at the nearest edge), preview included.</summary>
    public static class CircleBoundsChecks
    {
        static PointerEvent Ev(PointerPhase phase, double x, double y) => new PointerEvent(phase, new Vec2(x, y), true, 0);

        static EditorSession CircleSession()
        {
            var s = new EditorSession();
            s.SetSnapEnabled(false);   // exact pointer positions
            s.SetTool(ToolId.Circle);
            return s;
        }

        static CircleShape LastCircle(EditorSession s)
        {
            var shapes = s.ActiveLayer.Shapes;
            return shapes.Count > 0 ? shapes[shapes.Count - 1] as CircleShape : null;
        }

        static CircleShape GhostCircle(EditorSession s)
        {
            foreach (var p in s.Preview)
                if (p is CircleShape c && c.Radius > 0.02) return c;   // not the small hover/pending rings
            return null;
        }

        static void AssertInside(CircleShape c, string label)
        {
            Check.That(c.Center.X - c.Radius >= -1e-12 && c.Center.X + c.Radius <= 1 + 1e-12, label + ": inside in X");
            Check.That(c.Center.Y - c.Radius >= -1e-12 && c.Center.Y + c.Radius <= 1 + 1e-12, label + ": inside in Y");
        }

        public static void MaxRadiusInBoard_IsDistanceToNearestEdge()
        {
            Check.Near(EditorSession.MaxRadiusInBoard(new Vec2(0.5, 0.5)), 0.5, "centre");
            Check.Near(EditorSession.MaxRadiusInBoard(new Vec2(0.2, 0.7)), 0.2, "near the left edge");
            Check.Near(EditorSession.MaxRadiusInBoard(new Vec2(0.6, 0.9)), 0.1, "near the top edge", 1e-12);
            Check.Near(EditorSession.MaxRadiusInBoard(new Vec2(0.0, 0.5)), 0.0, "on the edge");
        }

        public static void DragPastTheEdge_CommitsCappedCircle()
        {
            var s = CircleSession();
            s.Pointer(Ev(PointerPhase.Down, 0.3, 0.5));
            s.Pointer(Ev(PointerPhase.Move, 0.9, 0.5));   // drag 0.6: would reach x = -0.3
            CircleShape ghost = GhostCircle(s);
            Check.That(ghost != null, "ghost shown");
            Check.Near(ghost.Radius, 0.3, "ghost capped at the left edge", 1e-12);
            AssertInside(ghost, "ghost");
            s.Pointer(Ev(PointerPhase.Up, 0.9, 0.5));
            CircleShape c = LastCircle(s);
            Check.That(c != null, "circle committed");
            Check.Near(c.Radius, 0.3, "committed radius capped", 1e-12);
            AssertInside(c, "committed");
        }

        public static void CircleInsideTheBoard_IsUnchanged()
        {
            var s = CircleSession();
            s.Pointer(Ev(PointerPhase.Down, 0.5, 0.5));
            s.Pointer(Ev(PointerPhase.Move, 0.7, 0.5));
            s.Pointer(Ev(PointerPhase.Up, 0.7, 0.5));
            Check.Near(LastCircle(s).Radius, 0.2, "radius = drag distance", 1e-12);
        }

        public static void RadiusLineEndsOnTheCappedCircle()
        {
            var s = CircleSession();
            s.Pointer(Ev(PointerPhase.Down, 0.8, 0.8));
            s.Pointer(Ev(PointerPhase.Move, 0.2, 0.8));   // drag 0.6 left; cap 0.2 (top/right edges)
            LineShape radius = null;
            foreach (var p in s.Preview)
                if (p is LineShape l) radius = l;
            Check.That(radius != null, "radius line shown");
            Check.Near(Vec2.Distance(radius.A, radius.B), 0.2, "radius line has the capped length", 1e-12);
            Check.That(radius.B.X < 0.8, "and still points toward the pointer");
        }

        public static void CentreOnTheEdge_DrawsNothing()
        {
            var s = CircleSession();
            s.Pointer(Ev(PointerPhase.Down, 0.0, 0.5));
            s.Pointer(Ev(PointerPhase.Move, 0.4, 0.5));
            s.Pointer(Ev(PointerPhase.Up, 0.4, 0.5));
            Check.That(s.ActiveLayer.Shapes.Count == 0, "no zero-size circle");
        }
    }
}
