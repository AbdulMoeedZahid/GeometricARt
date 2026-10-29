using System;
using System.Collections.Generic;

namespace GeoART.Contracts
{
    public enum ShapeKind { Line, Circle, Polygon }

    /// <summary>
    /// Committed geometry. All fields are value types, so <see cref="Clone"/> is a full deep copy;
    /// commands must store clones, never shared instances (P2 4.2).
    /// </summary>
    public abstract class Shape
    {
        public EntityId Id;
        public StrokeStyle Style = StrokeStyle.Default;
        public abstract ShapeKind Kind { get; }
        public Shape Clone() => (Shape)MemberwiseClone();
    }

    public sealed class LineShape : Shape
    {
        public Vec2 A, B;
        public override ShapeKind Kind => ShapeKind.Line;

        public LineShape() { }
        public LineShape(EntityId id, Vec2 a, Vec2 b, StrokeStyle style) { Id = id; A = a; B = b; Style = style; }
    }

    public sealed class CircleShape : Shape
    {
        public Vec2 Center;
        public double Radius;
        public override ShapeKind Kind => ShapeKind.Circle;

        public CircleShape() { }
        public CircleShape(EntityId id, Vec2 center, double radius, StrokeStyle style)
        {
            Id = id; Center = center; Radius = radius; Style = style;
        }
    }

    /// <summary>Regular polygon (stamps). Vertex k is at Center + Radius * (cos, sin)(RotationRad + 2*pi*k/Sides).</summary>
    public sealed class PolygonShape : Shape
    {
        public Vec2 Center;
        public int Sides;
        public double Radius, RotationRad;
        public override ShapeKind Kind => ShapeKind.Polygon;

        public PolygonShape() { }
        public PolygonShape(EntityId id, Vec2 center, int sides, double radius, double rotationRad, StrokeStyle style)
        {
            Id = id; Center = center; Sides = sides; Radius = radius; RotationRad = rotationRad; Style = style;
        }
    }

    /// <summary>User-placed snap point; hidden and deleted together with its layer.</summary>
    public sealed class SnapPoint
    {
        public EntityId Id;
        public Vec2 Position;

        public SnapPoint() { }
        public SnapPoint(EntityId id, Vec2 position) { Id = id; Position = position; }
        public SnapPoint Clone() => new SnapPoint(Id, Position);
    }

    public sealed class Layer
    {
        public EntityId Id;
        public string Name;
        public bool Visible = true;
        public readonly List<Shape> Shapes = new List<Shape>();          // first = back
        public readonly List<SnapPoint> SnapPoints = new List<SnapPoint>();

        public Layer() { }
        public Layer(EntityId id, string name) { Id = id; Name = name; }
    }

    /// <summary>The design. Mutated only by the engine's commands; everyone else reads it.</summary>
    public sealed class Board
    {
        public const int MinGridN = 1;
        public const int MaxGridN = 4;

        public int GridN = 2;
        public double CellSide => 1.0 / GridN;
        public readonly List<Layer> Layers = new List<Layer>();          // index 0 = BOTTOM
        public EntityId ActiveLayerId;
        public int NextId = 1;

        public EntityId NewId() => new EntityId(NextId++);

        public Layer FindLayer(EntityId id)
        {
            for (int i = 0; i < Layers.Count; i++)
                if (Layers[i].Id == id) return Layers[i];
            return null;
        }
    }

    [Flags]
    public enum BoardChangeKind { None = 0, Shapes = 1, Layers = 2, SnapPoints = 4, Grid = 8, Reset = 16 }
}
