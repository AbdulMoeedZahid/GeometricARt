using System;

namespace GeoART.Contracts
{
    /// <summary>How a banded layer draws its bands. New values are appended so saved ordinals never change.</summary>
    public enum BandMode { Double, Interlace }

    /// <summary>
    /// Per-layer band treatment ("double lines", book p22-23): a non-destructive setting that draws the layer's lines and
    /// polygon edges as bands of equal width with mitred joins and open crossings, recomputed from the shapes after every
    /// edit. A value type, copied by assignment. Every field's zero/false value is the old behaviour, so
    /// default(LayerBands), old saves and old undo snapshots all mean "no bands"; read stored values through
    /// <see cref="Sanitized"/>. Geometry: Engine/Geometry/BandGraph.cs and BandOutline.cs (docs/BANDS.md).
    /// </summary>
    public struct LayerBands : IEquatable<LayerBands>
    {
        /// <summary>Default band width in board units (6 mm on the 40 cm board).</summary>
        public const double DefaultWidth = 0.015;
        /// <summary>Allowed band width range in board units (1.6 mm .. 20 mm on the 40 cm board).</summary>
        public const double MinWidth = 0.004, MaxWidth = 0.05;

        public bool On;                // false = no bands (default; old saves)
        public BandMode Mode;          // remembered while Off
        public double Width;           // band width (outer edge to outer edge), board units; NaN/<=0 -> DefaultWidth
        public bool HideStructure;     // false = the book's "bold line inside"; true = "stripped"
        public bool InnerLines;        // a second pair of lines inside each band
        public bool FlipWeave;         // swap every over/under
        public RgbaColor OutlineColor; // A == 0 -> Palette.Black
        public LineWidth OutlineWidth; // default Thin

        /// <summary>Bands off, Double, 6 mm, structure shown, no inner lines, black Thin outlines.</summary>
        public static LayerBands Default => new LayerBands
        {
            On = false,
            Mode = BandMode.Double,
            Width = DefaultWidth,
            OutlineColor = Palette.Black,
            OutlineWidth = LineWidth.Thin,
        };

        /// <summary>
        /// A safe copy: width NaN/infinite/&lt;=0 becomes <see cref="DefaultWidth"/>, otherwise clamped to
        /// [<see cref="MinWidth"/>, <see cref="MaxWidth"/>]; unknown enum values become Double / Thin; a transparent
        /// outline colour becomes black.
        /// </summary>
        public LayerBands Sanitized()
        {
            LayerBands s = this;
            if (double.IsNaN(s.Width) || double.IsInfinity(s.Width) || !(s.Width > 0)) s.Width = DefaultWidth;
            else s.Width = Math.Max(MinWidth, Math.Min(MaxWidth, s.Width));
            if (s.Mode != BandMode.Double && s.Mode != BandMode.Interlace) s.Mode = BandMode.Double;
            if (s.OutlineWidth != LineWidth.Thin && s.OutlineWidth != LineWidth.Medium && s.OutlineWidth != LineWidth.Thick)
                s.OutlineWidth = LineWidth.Thin;
            if (s.OutlineColor.A == 0) s.OutlineColor = Palette.Black;
            return s;
        }

        public bool Equals(LayerBands o) =>
            On == o.On && Mode == o.Mode && Width.Equals(o.Width) && HideStructure == o.HideStructure &&
            InnerLines == o.InnerLines && FlipWeave == o.FlipWeave && OutlineColor.Equals(o.OutlineColor) &&
            OutlineWidth == o.OutlineWidth;

        public override bool Equals(object obj) => obj is LayerBands o && Equals(o);

        public override int GetHashCode()
        {
            int h = On ? 1 : 0;
            h = h * 31 + (int)Mode;
            h = h * 31 + Width.GetHashCode();
            h = h * 31 + (HideStructure ? 1 : 0) + (InnerLines ? 2 : 0) + (FlipWeave ? 4 : 0);
            h = h * 31 + OutlineColor.GetHashCode();
            return h * 31 + (int)OutlineWidth;
        }

        public static bool operator ==(LayerBands a, LayerBands b) => a.Equals(b);
        public static bool operator !=(LayerBands a, LayerBands b) => !a.Equals(b);
    }
}
