using System.Collections.Generic;

namespace GeoART.Contracts
{
    public enum ToolId { None, Line, SelectMove, Rotate, Circle, Snap, Scale, Stamps, Erase, Duplicate }

    /// <summary>Cancel = the gesture was interrupted (focus loss, re-placement, tracking loss, entering UI): abort unfinished work.</summary>
    public enum PointerPhase { Down, Move, Up, Cancel }

    public readonly struct PointerEvent
    {
        public readonly PointerPhase Phase;
        public readonly Vec2 BoardPos;
        public readonly bool OnBoard;
        public readonly double Time;

        public PointerEvent(PointerPhase phase, Vec2 boardPos, bool onBoard, double time)
        {
            Phase = phase; BoardPos = boardPos; OnBoard = onBoard; Time = time;
        }
    }

    public sealed class Selection
    {
        public readonly HashSet<EntityId> Ids = new HashSet<EntityId>();
    }

    public enum SnapKind { None, UserPoint, Endpoint, Center, CircleDivision, CellCorner, CellEdgeMid, CellCenter }

    public readonly struct SnapCandidate
    {
        public readonly Vec2 Position;
        public readonly SnapKind Kind;

        public SnapCandidate(Vec2 position, SnapKind kind) { Position = position; Kind = kind; }
    }
}
