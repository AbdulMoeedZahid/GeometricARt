using System;
using System.Text.RegularExpressions;
using GeoART.App.Save;
using GeoART.Contracts;
using NUnit.Framework;

namespace GeoART.Tests.Verifier
{
    /// <summary>
    /// VERIFIER (ui-line-plan 3.5, P4): band fields through the real JSON path (ToJson -> TryParse -> ToBoard). Every
    /// field round-trips exactly (width as "R"); a file without band fields (written before P4) loads unbanded with the
    /// defaults; garbage values are sanitized; no version bump.
    /// </summary>
    public sealed class VerifierBandSaveTests
    {
        static LayerBands Full()
        {
            LayerBands b = LayerBands.Default;
            b.On = true;
            b.Mode = BandMode.Interlace;
            b.Width = 0.0234567891234567;
            b.HideStructure = true;
            b.InnerLines = true;
            b.FlipWeave = true;
            b.OutlineColor = new RgbaColor(12, 34, 56, 200);
            b.OutlineWidth = LineWidth.Thick;
            return b;
        }

        static Board ThreeLayers()
        {
            var b = GoldenBoards.StarOnFullBoard();
            b.Layers[2].Bands = Full();
            LayerBands remembered = LayerBands.Default;   // off, but remembers Interlace and a width
            remembered.Mode = BandMode.Interlace;
            remembered.Width = 0.04;
            remembered.OutlineWidth = LineWidth.Medium;
            remembered.OutlineColor = Palette.Red;
            b.Layers[1].Bands = remembered;
            return b;                                       // layer 0: default(LayerBands)
        }

        static Board Load(string json)
        {
            Assert.IsTrue(DesignSerializer.TryParse(json, out DesignFile f, out string err), err);
            return DesignSerializer.ToBoard(f.board);
        }

        static void AssertBands(LayerBands expected, LayerBands actual, string what)
        {
            Assert.AreEqual(expected.On, actual.On, what + ": On");
            Assert.AreEqual(expected.Mode, actual.Mode, what + ": Mode");
            Assert.IsTrue(expected.Width.Equals(actual.Width), $"{what}: Width {expected.Width:R} vs {actual.Width:R}");
            Assert.AreEqual(expected.HideStructure, actual.HideStructure, what + ": HideStructure");
            Assert.AreEqual(expected.InnerLines, actual.InnerLines, what + ": InnerLines");
            Assert.AreEqual(expected.FlipWeave, actual.FlipWeave, what + ": FlipWeave");
            Assert.AreEqual(expected.OutlineColor, actual.OutlineColor, what + ": OutlineColor");
            Assert.AreEqual(expected.OutlineWidth, actual.OutlineWidth, what + ": OutlineWidth");
        }

        [Test]
        public void EveryBandFieldRoundTripsExactly()
        {
            var b = ThreeLayers();
            string json = DesignSerializer.ToJson(b, "bands", DateTime.UtcNow);
            StringAssert.Contains("\"version\": 1", json, "no version bump");
            var back = Load(json);
            AssertBands(Full(), back.Layers[2].Bands, "all non-default");
            AssertBands(b.Layers[1].Bands.Sanitized(), back.Layers[1].Bands, "off but remembered");
            AssertBands(LayerBands.Default, back.Layers[0].Bands, "never banded: the defaults, off");
            string again = DesignSerializer.ToJson(back, "bands", DateTime.UtcNow);
            Assert.AreEqual(Strip(json), Strip(again), "save -> load -> save is stable");
        }

        static string Strip(string json) => Regex.Replace(json, "\"savedUtc\": \"[^\"]*\"", "");

        [Test]
        public void FileWithoutBandFieldsLoadsUnbanded()
        {
            string json = DesignSerializer.ToJson(ThreeLayers(), "old", DateTime.UtcNow);
            string old = Regex.Replace(json, "\\s*\"band[A-Za-z]*\": [^,\\n]*,?", "");
            StringAssert.DoesNotContain("band", old, "precondition: a pre-P4 file");
            var back = Load(old);
            foreach (var l in back.Layers) AssertBands(LayerBands.Default, l.Bands, "old file, layer " + l.Name);
            Assert.AreEqual(8, back.Layers[2].Shapes.Count, "shapes still load");
        }

        [Test]
        public void FillSeedsRoundTripAndOldFilesHaveNone()
        {
            var b = ThreeLayers();
            b.Layers[2].Fills.Add(new FillSeed(new Vec2(0.123456789012345, 0.987654321098765), new RgbaColor(1, 2, 3, 200)));
            b.Layers[2].Fills.Add(new FillSeed(new Vec2(0.5, 0.5), Palette.Red));
            b.Layers[0].Fills.Add(new FillSeed(new Vec2(1.0 / 3.0, 2.0 / 3.0), Palette.Blue));
            string json = DesignSerializer.ToJson(b, "seed test", DateTime.UtcNow);
            StringAssert.Contains("\"version\": 1", json, "no version bump");
            var back = Load(json);
            for (int li = 0; li < 3; li++)
            {
                Assert.AreEqual(b.Layers[li].Fills.Count, back.Layers[li].Fills.Count, $"layer {li}: fill count");
                for (int i = 0; i < b.Layers[li].Fills.Count; i++)
                {
                    Assert.IsTrue(b.Layers[li].Fills[i].Point == back.Layers[li].Fills[i].Point, $"layer {li} seed {i}: exact point");
                    Assert.AreEqual(b.Layers[li].Fills[i].Color, back.Layers[li].Fills[i].Color, $"layer {li} seed {i}: colour");
                }
            }
            string old = Regex.Replace(json, "\\s*\"fills\": \\[[^\\]]*\\],?", "");
            StringAssert.DoesNotContain("\"fills\"", old, "precondition: a pre-P6 file");
            foreach (var l in Load(old).Layers) Assert.AreEqual(0, l.Fills.Count, "old file: no fills");
            string bad = json.Replace("\"x\": \"0.5\"", "\"x\": \"abc\"");
            Assert.AreEqual(1, Load(bad).Layers[2].Fills.Count, "a seed with a broken coordinate is dropped, the rest load");
        }

        [Test]
        public void GarbageBandValuesAreSanitized()
        {
            string json = DesignSerializer.ToJson(ThreeLayers(), "garbage", DateTime.UtcNow);
            foreach (string width in new[] { "-3", "abc", "NaN", "Infinity", "0", "" })
            {
                string g = Regex.Replace(json, "\"bandMode\": \\d+", "\"bandMode\": 99");
                g = Regex.Replace(g, "\"bandWidth\": \"[^\"]*\"", "\"bandWidth\": \"" + width + "\"");
                g = Regex.Replace(g, "\"bandLineWidth\": \\d+", "\"bandLineWidth\": 7");
                g = Regex.Replace(g, "\"bandA\": \\d+", "\"bandA\": 0");
                var back = Load(g);
                foreach (var l in back.Layers)
                {
                    Assert.AreEqual(BandMode.Double, l.Bands.Mode, $"width '{width}': mode 99 -> Double");
                    Assert.AreEqual(LayerBands.DefaultWidth, l.Bands.Width, $"width '{width}' -> the default");
                    Assert.AreEqual(LineWidth.Thin, l.Bands.OutlineWidth, "line width 7 -> Thin");
                    Assert.AreEqual(Palette.Black, l.Bands.OutlineColor, "alpha 0 -> black");
                }
            }
            string big = Regex.Replace(json, "\"bandWidth\": \"[^\"]*\"", "\"bandWidth\": \"0.9\"");
            foreach (var l in Load(big).Layers) Assert.AreEqual(LayerBands.MaxWidth, l.Bands.Width, "0.9 -> clamped to 0.05");
        }
    }
}
