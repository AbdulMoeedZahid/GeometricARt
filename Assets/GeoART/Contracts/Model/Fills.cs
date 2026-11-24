namespace GeoART.Contracts
{
    /// <summary>
    /// A fill on a layer (decision 6, docs/FILL.md): the point the user tapped and the colour. It is a seed, not a
    /// polygon: the filled region is the closed region of the layer's shapes (or band outlines) that contains the point,
    /// found again after every edit, so a fill follows its shapes as they change. A seed whose point is no longer inside a
    /// closed region is kept but draws nothing. Value type, copied by assignment.
    /// </summary>
    public struct FillSeed
    {
        public Vec2 Point;
        public RgbaColor Color;

        public FillSeed(Vec2 point, RgbaColor color) { Point = point; Color = color; }
    }
}
