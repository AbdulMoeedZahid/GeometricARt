using System;

namespace GeoART.Contracts
{
    /// <summary>
    /// Hand-coded rendering/test fixtures (MVP_PLAN 2.5). They are sample geometry, not evidence that the
    /// lesson or tiling works. Owned by B after Day 0.
    /// </summary>
    public static class GoldenBoards
    {
        public const double StarRadius = 0.35;
        public const double TileStarRadius = 0.25;
        public static readonly Vec2 BoardCenter = new Vec2(0.5, 0.5);

        public const string DottedLayerName = "Dotted lines";
        public const string CircleLayerName = "Circle";
        public const string StarLayerName = "8 pointed Star made w squares";

        static StrokeStyle GuideStyle => new StrokeStyle(Palette.Black, LineWidth.Thin, LineStyle.Dotted);
        static StrokeStyle CircleStyle => new StrokeStyle(Palette.Black, LineWidth.Thin, LineStyle.Solid);
        static StrokeStyle ArtStyle => new StrokeStyle(Palette.Black, LineWidth.Medium, LineStyle.Solid);
        static StrokeStyle TileStyle => new StrokeStyle(Palette.Black, LineWidth.Thick, LineStyle.Solid);

        /// <summary>One empty layer, default 2x2 grid.</summary>
        public static Board Empty()
        {
            var b = new Board();
            var layer = AddLayer(b, "Layer 1");
            b.ActiveLayerId = layer.Id;
            return b;
        }

        /// <summary>Point k of the eight circle divisions: center + r * (cos(k*pi/4), sin(k*pi/4)).</summary>
        public static Vec2 StarPoint(Vec2 center, double radius, int k) =>
            new Vec2(center.X + radius * Math.Cos(k * Math.PI / 4), center.Y + radius * Math.Sin(k * Math.PI / 4));

        /// <summary>Sketch 3: guides, circle and the eight-pointed star on the full board (layers bottom to top).</summary>
        public static Board StarOnFullBoard()
        {
            var b = new Board { GridN = 2 };
            var dotted = AddLayer(b, DottedLayerName);
            var circle = AddLayer(b, CircleLayerName);
            var star = AddLayer(b, StarLayerName);
            var c = BoardCenter;
            const double r = StarRadius;

            AddLine(b, dotted, new Vec2(c.X - r, c.Y), new Vec2(c.X + r, c.Y), GuideStyle);
            AddLine(b, dotted, new Vec2(c.X, c.Y - r), new Vec2(c.X, c.Y + r), GuideStyle);
            AddLine(b, dotted, new Vec2(0, 0), new Vec2(1, 1), GuideStyle);
            AddLine(b, dotted, new Vec2(0, 1), new Vec2(1, 0), GuideStyle);
            dotted.SnapPoints.Add(new SnapPoint(b.NewId(), c));

            circle.Shapes.Add(new CircleShape(b.NewId(), c, r, CircleStyle));
            for (int k = 0; k < 8; k++)
                circle.SnapPoints.Add(new SnapPoint(b.NewId(), StarPoint(c, r, k)));

            AddStarSquares(b, star, c, r, ArtStyle);
            b.ActiveLayerId = star.Id;
            return b;
        }

        /// <summary>Sketch 4: four stars of radius 0.25 at the 2x2 cell centers; cardinal tips meet at edge midpoints.</summary>
        public static Board Tiled2x2()
        {
            var b = new Board { GridN = 2 };
            var star = AddLayer(b, StarLayerName);
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                    AddStarSquares(b, star, new Vec2(0.25 + 0.5 * i, 0.25 + 0.5 * j), TileStarRadius, TileStyle);
            b.ActiveLayerId = star.Id;
            return b;
        }

        /// <summary>Squares joining point indices 0,2,4,6,0 and 1,3,5,7,1.</summary>
        static void AddStarSquares(Board b, Layer layer, Vec2 center, double radius, StrokeStyle style)
        {
            for (int start = 0; start < 2; start++)
                for (int i = 0; i < 4; i++)
                {
                    int k0 = start + 2 * i, k1 = start + 2 * ((i + 1) % 4);
                    AddLine(b, layer, StarPoint(center, radius, k0), StarPoint(center, radius, k1), style);
                }
        }

        static Layer AddLayer(Board b, string name)
        {
            var layer = new Layer(b.NewId(), name);
            b.Layers.Add(layer);
            return layer;
        }

        static void AddLine(Board b, Layer layer, Vec2 a, Vec2 c, StrokeStyle style) =>
            layer.Shapes.Add(new LineShape(b.NewId(), a, c, style));
    }
}
