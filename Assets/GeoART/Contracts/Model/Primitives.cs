using System;

namespace GeoART.Contracts
{
    public readonly struct RgbaColor : IEquatable<RgbaColor>
    {
        public readonly byte R, G, B, A;

        public RgbaColor(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

        public bool Equals(RgbaColor o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object obj) => obj is RgbaColor o && Equals(o);
        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
    }

    /// <summary>Stable model ID. 0 is "none"; real IDs come from <see cref="Board.NewId"/>.</summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly int Value;
        public static readonly EntityId None = new EntityId(0);

        public EntityId(int value) { Value = value; }

        public bool IsNone => Value == 0;
        public bool Equals(EntityId o) => Value == o.Value;
        public override bool Equals(object obj) => obj is EntityId o && Equals(o);
        public override int GetHashCode() => Value;
        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
        public override string ToString() => $"#{Value}";
    }

    public enum LineStyle { Solid, Dotted }
    public enum LineWidth { Thin, Medium, Thick }
    public enum StampKind { Triangle = 3, Square = 4, Hexagon = 6 }

    public readonly struct StrokeStyle : IEquatable<StrokeStyle>
    {
        public readonly RgbaColor Color;
        public readonly LineWidth Width;
        public readonly LineStyle Dash;

        public StrokeStyle(RgbaColor color, LineWidth width, LineStyle dash)
        {
            Color = color; Width = width; Dash = dash;
        }

        /// <summary>D-08: black, Medium, solid. Use this rather than default(StrokeStyle), whose colour is transparent.</summary>
        public static StrokeStyle Default => new StrokeStyle(Palette.Black, LineWidth.Medium, LineStyle.Solid);

        public StrokeStyle WithDash(LineStyle dash) => new StrokeStyle(Color, Width, dash);
        public StrokeStyle WithWidth(LineWidth width) => new StrokeStyle(Color, width, Dash);
        public StrokeStyle WithColor(RgbaColor color) => new StrokeStyle(color, Width, Dash);

        public bool Equals(StrokeStyle o) => Color.Equals(o.Color) && Width == o.Width && Dash == o.Dash;
        public override bool Equals(object obj) => obj is StrokeStyle o && Equals(o);
        public override int GetHashCode() => (Color.GetHashCode() * 31 + (int)Width) * 31 + (int)Dash;
    }

    /// <summary>D-08 palette: black, white, red, orange, blue, green.</summary>
    public static class Palette
    {
        public static readonly RgbaColor Black = new RgbaColor(20, 20, 24);
        public static readonly RgbaColor White = new RgbaColor(250, 250, 250);
        public static readonly RgbaColor Red = new RgbaColor(214, 48, 49);
        public static readonly RgbaColor Orange = new RgbaColor(240, 140, 30);
        public static readonly RgbaColor Blue = new RgbaColor(40, 100, 210);
        public static readonly RgbaColor Green = new RgbaColor(30, 150, 80);

        public static readonly RgbaColor[] Default = { Black, White, Red, Orange, Blue, Green };
    }
}
