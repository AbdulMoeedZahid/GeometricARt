using System;
using System.Collections.Generic;
using GeoART.App.Save;
using GeoART.Contracts;
using GeoART.Engine;
using NUnit.Framework;
using EntityId = GeoART.Contracts.EntityId;

namespace GeoART.Tests.Save
{
    /// <summary>Exact board comparison shared by the save tests. Doubles are compared by their bits, not with a tolerance.</summary>
    public static class BoardAssert
    {
        public static void AreEqual(Board expected, Board actual)
        {
            Assert.That(actual, Is.Not.Null, "board");
            Assert.That(actual.GridN, Is.EqualTo(expected.GridN), "GridN");
            Assert.That(actual.GridKind, Is.EqualTo(expected.GridKind), "GridKind");
            Assert.That(actual.NextId, Is.EqualTo(expected.NextId), "NextId");
            Assert.That(actual.ActiveLayerId, Is.EqualTo(expected.ActiveLayerId), "ActiveLayerId");
            Assert.That(actual.Layers.Count, Is.EqualTo(expected.Layers.Count), "layer count");
            for (int i = 0; i < expected.Layers.Count; i++)
            {
                Layer e = expected.Layers[i], a = actual.Layers[i];
                string at = $"layer {i}";
                Assert.That(a.Id, Is.EqualTo(e.Id), at + " Id");
                Assert.That(a.Name, Is.EqualTo(e.Name), at + " Name");
                Assert.That(a.Visible, Is.EqualTo(e.Visible), at + " Visible");
                Same(e.Opacity, a.Opacity, at + " Opacity");
                // default(LayerBands) and its sanitized form mean the same thing ("no bands"); compare that meaning.
                LayerBands eb = e.Bands.Sanitized(), ab = a.Bands.Sanitized();
                Assert.That(ab == eb, Is.True, $"{at} Bands: expected {Describe(eb)} got {Describe(ab)}");
                Same(eb.Width, ab.Width, at + " Bands.Width");
                Assert.That(a.Shapes.Count, Is.EqualTo(e.Shapes.Count), at + " shape count");
                for (int k = 0; k < e.Shapes.Count; k++)
                    ShapesEqual(e.Shapes[k], a.Shapes[k], $"{at} shape {k}");
                Assert.That(a.SnapPoints.Count, Is.EqualTo(e.SnapPoints.Count), at + " snap point count");
                for (int k = 0; k < e.SnapPoints.Count; k++)
                {
                    Assert.That(a.SnapPoints[k].Id, Is.EqualTo(e.SnapPoints[k].Id), $"{at} snap {k} Id");
                    Same(e.SnapPoints[k].Position, a.SnapPoints[k].Position, $"{at} snap {k} Position");
                }
            }
        }

        static string Describe(LayerBands b) =>
            $"on {b.On} {b.Mode} w {b.Width:R} hide {b.HideStructure} inner {b.InnerLines} flip {b.FlipWeave} {b.OutlineColor} {b.OutlineWidth}";

        static void ShapesEqual(Shape e, Shape a, string at)
        {
            Assert.That(a.GetType(), Is.EqualTo(e.GetType()), at + " type");
            Assert.That(a.Id, Is.EqualTo(e.Id), at + " Id");
            Assert.That(a.Style.Equals(e.Style), Is.True, at + " Style");
            switch (e)
            {
                case LineShape el:
                    var al = (LineShape)a;
                    Same(el.A, al.A, at + " A");
                    Same(el.B, al.B, at + " B");
                    break;
                case CircleShape ec:
                    var ac = (CircleShape)a;
                    Same(ec.Center, ac.Center, at + " Center");
                    Same(ec.Radius, ac.Radius, at + " Radius");
                    break;
                case PolygonShape ep:
                    var ap = (PolygonShape)a;
                    Same(ep.Center, ap.Center, at + " Center");
                    Assert.That(ap.Sides, Is.EqualTo(ep.Sides), at + " Sides");
                    Same(ep.Radius, ap.Radius, at + " Radius");
                    Same(ep.RotationRad, ap.RotationRad, at + " RotationRad");
                    break;
            }
        }

        static void Same(Vec2 e, Vec2 a, string at)
        {
            Assert.That(a.Equals(e), Is.True, $"{at}: expected {e.X:R},{e.Y:R} got {a.X:R},{a.Y:R}");
            Same(e.X, a.X, at + ".X");
            Same(e.Y, a.Y, at + ".Y");
        }

        public static void Same(double e, double a, string at) =>
            Assert.That(BitConverter.DoubleToInt64Bits(a), Is.EqualTo(BitConverter.DoubleToInt64Bits(e)),
                $"{at}: expected {e:R} got {a:R}");
    }

    /// <summary>DesignSerializer: board -> JSON -> board keeps everything, and bad files are fixed or refused.</summary>
    public sealed class DesignSerializerTests
    {
        static readonly DateTime When = new DateTime(2026, 11, 17, 20, 4, 5, 123, DateTimeKind.Utc);

        static Board RoundTrip(Board b)
        {
            string json = DesignSerializer.ToJson(b, "Test", When);
            Assert.That(DesignSerializer.TryParse(json, out DesignFile file, out string error), Is.True, error);
            Assert.That(file.version, Is.EqualTo(DesignSerializer.Version));
            Assert.That(file.name, Is.EqualTo("Test"));
            Assert.That(file.savedUtc, Is.EqualTo("2026-11-17T20:04:05.123Z"));
            return DesignSerializer.ToBoard(file.board);
        }

        /// <summary>Three layers, every shape kind, Hex 3x3, a hidden half-see-through layer, the middle layer active.</summary>
        public static Board HandMade()
        {
            var b = new Board { GridN = 3, GridKind = GridKind.Hex };
            var red = new StrokeStyle(Palette.Red, LineWidth.Thick, LineStyle.Dotted);
            var bottom = new Layer(new EntityId(3), "Guides") { Visible = false, Opacity = 0.35 };
            bottom.Shapes.Add(new LineShape(new EntityId(10), new Vec2(0.1, 0.2), new Vec2(1.0 / 3.0, 0.9), red));
            bottom.SnapPoints.Add(new SnapPoint(new EntityId(11), new Vec2(0.5, 0.5)));
            bottom.SnapPoints.Add(new SnapPoint(new EntityId(12), new Vec2(TileGrid.HexRadius(3), CircleDivisions.Diagonal)));
            var middle = new Layer(new EntityId(20), "Circles");
            middle.Shapes.Add(new CircleShape(new EntityId(21), new Vec2(0.5, 0.5), 0.35, StrokeStyle.Default));
            middle.Shapes.Add(new PolygonShape(new EntityId(22), new Vec2(0.25, 0.75), 6, 0.125, Math.PI / 6.0,
                new StrokeStyle(new RgbaColor(1, 2, 3, 4), LineWidth.Thin, LineStyle.Solid)));
            var top = new Layer(new EntityId(40), "Top");
            top.Shapes.Add(new LineShape(new EntityId(41), CircleDivisions.Point(new Vec2(0.5, 0.5), 0.35, 3),
                new Vec2(0.0, 1.0), StrokeStyle.Default));
            b.Layers.Add(bottom);
            b.Layers.Add(middle);
            b.Layers.Add(top);
            b.ActiveLayerId = middle.Id;
            b.NextId = 57;
            return b;
        }

        [Test]
        public void StarOnFullBoardRoundTrips()
        {
            var b = GoldenBoards.StarOnFullBoard();
            BoardAssert.AreEqual(b, RoundTrip(b));
        }

        [Test]
        public void Tiled2x2RoundTrips()
        {
            var b = GoldenBoards.Tiled2x2();
            BoardAssert.AreEqual(b, RoundTrip(b));
        }

        [Test]
        public void HandMadeBoardRoundTrips()
        {
            var b = HandMade();
            var back = RoundTrip(b);
            BoardAssert.AreEqual(b, back);
            Assert.That(back.GridKind, Is.EqualTo(GridKind.Hex));
            Assert.That(back.Layers[0].Visible, Is.False);
            Assert.That(back.Layers[0].Shapes[0].Style.Color, Is.EqualTo(Palette.Red));
            Assert.That(back.NextId, Is.EqualTo(57));
        }

        [Test]
        public void JsonIsTheSameAfterARoundTrip()
        {
            var b = HandMade();
            string first = DesignSerializer.ToJson(b, "Test", When);
            Assert.That(DesignSerializer.ToJson(RoundTrip(b), "Test", When), Is.EqualTo(first));
        }

        static readonly double[] HardDoubles =
        {
            CircleDivisions.Diagonal, 1.0 / 3.0, 0.1, TileGrid.HexRadius(3), 1e-17, double.Epsilon,
            TileGrid.HexRadius(1), TileGrid.HexRadius(2), TileGrid.HexRadius(4), 0.5 + 0.35 * CircleDivisions.Diagonal,
            2.0 / 3.0, double.MaxValue, 0.30000000000000004,
        };

        [Test]
        public void DoublesComeBackWithTheSameBits()
        {
            foreach (double v in HardDoubles)
                BoardAssert.Same(v, DesignSerializer.P(DesignSerializer.D(v)), "text " + DesignSerializer.D(v));

            var b = new Board();
            var layer = new Layer(new EntityId(1), "L");
            b.Layers.Add(layer);
            int id = 2;
            foreach (double v in HardDoubles)
            {
                layer.SnapPoints.Add(new SnapPoint(new EntityId(id++), new Vec2(v, -v)));
                layer.Shapes.Add(new LineShape(new EntityId(id++), new Vec2(v, 1 - v), new Vec2(-v, v), StrokeStyle.Default));
                if (v > 0) layer.Shapes.Add(new CircleShape(new EntityId(id++), new Vec2(v, v), v, StrokeStyle.Default));
            }
            b.ActiveLayerId = layer.Id;
            b.NextId = id;
            BoardAssert.AreEqual(b, RoundTrip(b));
        }

        [Test]
        public void UnknownShapeKindIsSkipped()
        {
            var d = DesignSerializer.FromBoard(HandMade());
            d.layers[1].shapes[0].kind = 9;
            var b = DesignSerializer.ToBoard(d);
            Assert.That(b.Layers[1].Shapes.Count, Is.EqualTo(1));
            Assert.That(b.Layers[1].Shapes[0], Is.InstanceOf<PolygonShape>());
        }

        [Test]
        public void NaNCoordinateIsSkipped()
        {
            var d = DesignSerializer.FromBoard(HandMade());
            d.layers[0].shapes[0].ax = "NaN";
            d.layers[2].shapes[0].by = "not a number";
            d.layers[0].snapPoints[0].x = "Infinity";
            var b = DesignSerializer.ToBoard(d);
            Assert.That(b.Layers[0].Shapes.Count, Is.EqualTo(0));
            Assert.That(b.Layers[2].Shapes.Count, Is.EqualTo(0));
            Assert.That(b.Layers[0].SnapPoints.Count, Is.EqualTo(1));
        }

        [Test]
        public void BadRadiusAndSidesAreSkipped()
        {
            var d = DesignSerializer.FromBoard(HandMade());
            d.layers[1].shapes[0].radius = "0";
            d.layers[1].shapes[1].sides = 2;
            Assert.That(DesignSerializer.ToBoard(d).Layers[1].Shapes.Count, Is.EqualTo(0));
        }

        [Test]
        public void OutOfRangeValuesAreClamped()
        {
            var d = DesignSerializer.FromBoard(HandMade());
            d.gridN = 9;
            d.gridKind = 7;
            d.layers[0].opacity = "1.5";
            d.layers[1].opacity = "-2";
            d.layers[2].opacity = null;
            d.layers[0].shapes[0].r = 300;
            d.layers[0].shapes[0].g = -5;
            var b = DesignSerializer.ToBoard(d);
            Assert.That(b.GridN, Is.EqualTo(Board.MaxGridN));
            Assert.That(b.GridKind, Is.EqualTo(GridKind.Square));
            Assert.That(b.Layers[0].Opacity, Is.EqualTo(1.0));
            Assert.That(b.Layers[1].Opacity, Is.EqualTo(0.0));
            Assert.That(b.Layers[2].Opacity, Is.EqualTo(1.0));
            Assert.That(b.Layers[0].Shapes[0].Style.Color.R, Is.EqualTo(255));
            Assert.That(b.Layers[0].Shapes[0].Style.Color.G, Is.EqualTo(0));
        }

        [Test]
        public void GridNineBecomesFour()
        {
            var d = DesignSerializer.FromBoard(GoldenBoards.Tiled2x2());
            d.gridN = 9;
            Assert.That(DesignSerializer.ToBoard(d).GridN, Is.EqualTo(4));
        }

        [Test]
        public void NextIdTooLowIsRaised()
        {
            var d = DesignSerializer.FromBoard(HandMade());
            d.nextId = 5;
            var b = DesignSerializer.ToBoard(d);
            Assert.That(b.NextId, Is.EqualTo(42), "highest id used is 41");
            Assert.That(b.NewId().Value, Is.EqualTo(42));
        }

        [Test]
        public void GarbageJsonIsRefused()
        {
            foreach (string bad in new[] { "", "   ", "not json at all", "{ \"version\": ", "{}", "[1,2,3]" })
            {
                Assert.That(DesignSerializer.TryParse(bad, out DesignFile file, out string error), Is.False, bad);
                Assert.That(file, Is.Null, bad);
                Assert.That(error, Is.Not.Null.And.Not.Empty, bad);
            }
        }

        [Test]
        public void NewerVersionIsRefused()
        {
            string json = DesignSerializer.ToJson(HandMade(), "Test", When)
                .Replace("\"version\": 1,", "\"version\": 99,");
            Assert.That(json, Does.Contain("\"version\": 99,"));
            Assert.That(DesignSerializer.TryParse(json, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("newer"));
        }

        [Test]
        public void EmptyLayersStillLoadIntoTheSession()
        {
            string json = DesignSerializer.ToJson(new Board { GridN = 3 }, "Empty", When);
            Assert.That(DesignSerializer.TryParse(json, out DesignFile file, out string error), Is.True, error);
            var b = DesignSerializer.ToBoard(file.board);
            Assert.That(b.Layers.Count, Is.EqualTo(0));
            Assert.That(b.GridN, Is.EqualTo(3));

            var session = new EditorSession();
            session.LoadBoard(b);
            Assert.That(session.Board.Layers.Count, Is.EqualTo(1));
            Assert.That(session.Board.FindLayer(session.Board.ActiveLayerId), Is.Not.Null);
        }

        static LayerBands FullBands => new LayerBands
        {
            On = true, Mode = BandMode.Interlace, Width = 0.0123456789012345, HideStructure = true, InnerLines = true,
            FlipWeave = true, OutlineColor = new RgbaColor(12, 34, 56, 78), OutlineWidth = LineWidth.Thick,
        };

        [Test]
        public void EveryBandFieldRoundTrips()
        {
            var b = GoldenBoards.StarOnFullBoard();
            b.Layers[2].Bands = FullBands;
            var off = FullBands;
            off.On = false;   // remembered settings of a layer whose bands are off
            b.Layers[1].Bands = off;
            var back = RoundTrip(b);
            BoardAssert.AreEqual(b, back);
            Assert.That(back.Layers[2].Bands == FullBands, Is.True, "every field, the width bit-exact");
            Assert.That(back.Layers[1].Bands == off, Is.True, "an Off layer keeps its settings");
            Assert.That(back.Layers[0].Bands.On, Is.False, "an untouched layer stays unbanded");
        }

        [Test]
        public void TheWeaveIsTheSameAfterASaveRoundTrip()
        {
            var b = GoldenBoards.Tiled2x2();
            b.Layers[0].Bands = FullBands;
            var before = BandGraph.Build(b.Layers[0].Shapes);
            var after = BandGraph.Build(RoundTrip(b).Layers[0].Shapes);
            Assert.That(after.CrossingCount, Is.EqualTo(before.CrossingCount));
            for (int c = 0; c < before.CrossingCount; c++)
            {
                Assert.That(after.Graph.Node(after.CrossingNode(c)), Is.EqualTo(before.Graph.Node(before.CrossingNode(c))));
                Assert.That(after.OverPass(c), Is.EqualTo(before.OverPass(c)), $"crossing {c}: the weave never flips on reload");
            }
        }

        [Test]
        public void OldFileWithoutBandFieldsLoadsUnbanded()
        {
            var b = GoldenBoards.StarOnFullBoard();
            b.Layers[2].Bands = FullBands;
            string json = DesignSerializer.ToJson(b, "Old", When);
            string old = System.Text.RegularExpressions.Regex.Replace(json, "\\s*\"band[A-Za-z]*\": [^,\\n]*,", "");
            Assert.That(old, Does.Not.Contain("\"band"), "precondition: a version-1 file from before bands");
            Assert.That(DesignSerializer.TryParse(old, out DesignFile file, out string error), Is.True, error);
            var loaded = DesignSerializer.ToBoard(file.board);
            foreach (var layer in loaded.Layers)
            {
                Assert.That(layer.Bands.On, Is.False, layer.Name);
                Assert.That(layer.Bands == LayerBands.Default, Is.True, layer.Name + ": default settings");
            }
            Assert.That(loaded.Layers[2].Shapes.Count, Is.EqualTo(b.Layers[2].Shapes.Count), "shapes still load");
        }

        [Test]
        public void GarbageBandValuesAreSanitized()
        {
            foreach (string width in new[] { "-3", "abc", null, "NaN", "1e300" })
            {
                var d = new BoardData { gridN = 2, nextId = 5 };
                d.layers.Add(new LayerData
                {
                    id = 1, name = "A", visible = true, opacity = "1",
                    bandOn = true, bandMode = 99, bandWidth = width, bandLineWidth = -4, bandA = 0,
                });
                LayerBands lb = DesignSerializer.ToBoard(d).Layers[0].Bands;
                Assert.That(lb.On, Is.True, $"{width}: on kept");
                Assert.That(lb.Mode, Is.EqualTo(BandMode.Double), $"{width}: mode 99 -> Double");
                Assert.That(lb.Width, Is.EqualTo(width == "1e300" ? LayerBands.MaxWidth : LayerBands.DefaultWidth), $"{width}: width");
                Assert.That(lb.OutlineWidth, Is.EqualTo(LineWidth.Thin), $"{width}: outline width");
                Assert.That(lb.OutlineColor, Is.EqualTo(Palette.Black), $"{width}: transparent colour -> black");
            }
        }

        [Test]
        public void MissingListsAreTreatedAsEmpty()
        {
            var d = new BoardData { gridN = 2, nextId = 1, layers = null };
            Assert.That(DesignSerializer.ToBoard(d).Layers.Count, Is.EqualTo(0));
            d.layers = new List<LayerData> { new LayerData { id = 1, name = "A", visible = true, opacity = "1", shapes = null, snapPoints = null } };
            var b = DesignSerializer.ToBoard(d);
            Assert.That(b.Layers.Count, Is.EqualTo(1));
            Assert.That(b.Layers[0].Shapes.Count, Is.EqualTo(0));
            Assert.That(b.NextId, Is.EqualTo(2));
        }
    }
}
