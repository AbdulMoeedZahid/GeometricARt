using System;
using System.Collections.Generic;
using System.Globalization;
using GeoART.Contracts;
using UnityEngine;
using EntityId = GeoART.Contracts.EntityId;

namespace GeoART.App.Save
{
    /// <summary>
    /// One saved design as it sits on disk (JSON through JsonUtility). Public fields only, because JsonUtility reads and
    /// writes fields, not properties. Bump <see cref="DesignSerializer.Version"/> when the layout changes in a way old
    /// code cannot read.
    /// </summary>
    [Serializable]
    public sealed class DesignFile
    {
        public int version;
        public string name;
        public string savedUtc;   // ISO 8601, UTC, e.g. 2026-11-17T20:04:05.123Z
        public BoardData board;
    }

    [Serializable]
    public sealed class BoardData
    {
        public int gridN;
        public int gridKind;      // GridKind ordinal
        public int nextId;
        public int activeLayerId;
        public List<LayerData> layers = new List<LayerData>();   // index 0 = bottom, like Board.Layers
    }

    [Serializable]
    public sealed class LayerData
    {
        public int id;
        public string name;
        public bool visible;
        public string opacity;   // double, see DesignSerializer
        // Band treatment (LayerBands). Missing in older files: off, and every value reads through LayerBands.Sanitized().
        public bool bandOn;
        public int bandMode;            // BandMode ordinal
        public string bandWidth;        // double, see DesignSerializer
        public bool bandHideStructure, bandInner, bandFlip;
        public int bandR, bandG, bandB, bandA;
        public int bandLineWidth;       // LineWidth ordinal
        public List<ShapeData> shapes = new List<ShapeData>();
        public List<SnapPointData> snapPoints = new List<SnapPointData>();
    }

    /// <summary>
    /// Flat record for every shape kind. Line uses a/b, circle uses c + radius, polygon uses c + radius + rotation +
    /// sides. Fields a shape does not use stay 0.
    /// </summary>
    [Serializable]
    public sealed class ShapeData
    {
        public int kind;          // ShapeKind ordinal
        public int id;
        public int r, g, b, a;
        public int width;         // LineWidth ordinal
        public int dash;          // LineStyle ordinal
        public string ax, ay, bx, by, cx, cy, radius, rotation;   // doubles, see DesignSerializer
        public int sides;
    }

    [Serializable]
    public sealed class SnapPointData
    {
        public int id;
        public string x, y;      // doubles, see DesignSerializer
    }

    /// <summary>
    /// Board to JSON and back. <see cref="ToBoard"/> never trusts the file: bad values are clamped or the shape is
    /// dropped, so a hand-edited or damaged file still loads as far as it can. Ids, names and the order of layers,
    /// shapes and snap points are kept as they are, so a round trip gives the same board.
    /// Every double is stored as a string written with ToString("R", InvariantCulture). Snapping compares points with
    /// ==, so a reloaded point has to have exactly the same bits. JsonUtility's own number writing nearly gets there,
    /// but it reads 5e-324 (double.Epsilon) back as 0, so we do not rely on it. A missing or unreadable number reads
    /// as NaN, which drops the shape (or gives opacity 1).
    /// </summary>
    public static class DesignSerializer
    {
        public const int Version = 1;

        public static BoardData FromBoard(Board b)
        {
            if (b == null) throw new ArgumentNullException(nameof(b));
            var d = new BoardData
            {
                gridN = b.GridN,
                gridKind = (int)b.GridKind,
                nextId = b.NextId,
                activeLayerId = b.ActiveLayerId.Value,
            };
            foreach (var layer in b.Layers)
            {
                var ld = new LayerData { id = layer.Id.Value, name = layer.Name, visible = layer.Visible, opacity = D(layer.Opacity) };
                LayerBands bands = layer.Bands.Sanitized();
                ld.bandOn = bands.On;
                ld.bandMode = (int)bands.Mode;
                ld.bandWidth = D(bands.Width);
                ld.bandHideStructure = bands.HideStructure;
                ld.bandInner = bands.InnerLines;
                ld.bandFlip = bands.FlipWeave;
                ld.bandR = bands.OutlineColor.R; ld.bandG = bands.OutlineColor.G;
                ld.bandB = bands.OutlineColor.B; ld.bandA = bands.OutlineColor.A;
                ld.bandLineWidth = (int)bands.OutlineWidth;
                foreach (var shape in layer.Shapes)
                    if (shape != null) ld.shapes.Add(FromShape(shape));
                foreach (var p in layer.SnapPoints)
                    if (p != null) ld.snapPoints.Add(new SnapPointData { id = p.Id.Value, x = D(p.Position.X), y = D(p.Position.Y) });
                d.layers.Add(ld);
            }
            return d;
        }

        static ShapeData FromShape(Shape s)
        {
            var c = s.Style.Color;
            var d = new ShapeData
            {
                kind = (int)s.Kind, id = s.Id.Value,
                r = c.R, g = c.G, b = c.B, a = c.A,
                width = (int)s.Style.Width, dash = (int)s.Style.Dash,
                ax = Zero, ay = Zero, bx = Zero, by = Zero, cx = Zero, cy = Zero, radius = Zero, rotation = Zero,
            };
            switch (s)
            {
                case LineShape line:
                    d.ax = D(line.A.X); d.ay = D(line.A.Y); d.bx = D(line.B.X); d.by = D(line.B.Y);
                    break;
                case CircleShape circle:
                    d.cx = D(circle.Center.X); d.cy = D(circle.Center.Y); d.radius = D(circle.Radius);
                    break;
                case PolygonShape poly:
                    d.cx = D(poly.Center.X); d.cy = D(poly.Center.Y); d.radius = D(poly.Radius);
                    d.rotation = D(poly.RotationRad); d.sides = poly.Sides;
                    break;
            }
            return d;
        }

        /// <summary>Builds a board from file data, fixing or dropping bad values (see the class comment).</summary>
        public static Board ToBoard(BoardData d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var b = new Board
            {
                GridN = TileGrid.ClampN(d.gridN),
                GridKind = Enum.IsDefined(typeof(GridKind), d.gridKind) ? (GridKind)d.gridKind : GridKind.Square,
                ActiveLayerId = new EntityId(d.activeLayerId),
            };
            int maxId = Math.Max(0, d.activeLayerId);
            if (d.layers != null)
                foreach (var ld in d.layers)
                {
                    if (ld == null) continue;
                    double opacity = P(ld.opacity);
                    var layer = new Layer(new EntityId(ld.id), ld.name ?? "")
                    {
                        Visible = ld.visible,
                        Opacity = double.IsNaN(opacity) ? 1.0 : Math.Max(0.0, Math.Min(1.0, opacity)),
                        Bands = new LayerBands
                        {
                            On = ld.bandOn,
                            Mode = (BandMode)ld.bandMode,
                            Width = P(ld.bandWidth),   // missing -> NaN -> the default width
                            HideStructure = ld.bandHideStructure,
                            InnerLines = ld.bandInner,
                            FlipWeave = ld.bandFlip,
                            OutlineColor = new RgbaColor(Byte(ld.bandR), Byte(ld.bandG), Byte(ld.bandB), Byte(ld.bandA)),
                            OutlineWidth = (LineWidth)ld.bandLineWidth,
                        }.Sanitized(),
                    };
                    maxId = Math.Max(maxId, ld.id);
                    if (ld.shapes != null)
                        foreach (var sd in ld.shapes)
                        {
                            Shape s = sd == null ? null : ToShape(sd);
                            if (s == null) continue;
                            layer.Shapes.Add(s);
                            maxId = Math.Max(maxId, sd.id);
                        }
                    if (ld.snapPoints != null)
                        foreach (var pd in ld.snapPoints)
                        {
                            if (pd == null) continue;
                            double x = P(pd.x), y = P(pd.y);
                            if (!Finite(x) || !Finite(y)) continue;
                            layer.SnapPoints.Add(new SnapPoint(new EntityId(pd.id), new Vec2(x, y)));
                            maxId = Math.Max(maxId, pd.id);
                        }
                    b.Layers.Add(layer);
                }
            b.NextId = Math.Max(Math.Max(1, d.nextId), maxId == int.MaxValue ? maxId : maxId + 1);
            return b;
        }

        /// <summary>Null when the record is not a shape we can draw (unknown kind, NaN/infinity, radius &lt;= 0, &lt; 3 sides).</summary>
        static Shape ToShape(ShapeData d)
        {
            var color = new RgbaColor(Byte(d.r), Byte(d.g), Byte(d.b), Byte(d.a));
            var width = Enum.IsDefined(typeof(LineWidth), d.width) ? (LineWidth)d.width : LineWidth.Medium;
            var dash = Enum.IsDefined(typeof(LineStyle), d.dash) ? (LineStyle)d.dash : LineStyle.Solid;
            var style = new StrokeStyle(color, width, dash);
            var id = new EntityId(d.id);
            switch (d.kind)
            {
                case (int)ShapeKind.Line:
                {
                    double ax = P(d.ax), ay = P(d.ay), bx = P(d.bx), by = P(d.by);
                    if (!Finite(ax) || !Finite(ay) || !Finite(bx) || !Finite(by)) return null;
                    return new LineShape(id, new Vec2(ax, ay), new Vec2(bx, by), style);
                }
                case (int)ShapeKind.Circle:
                {
                    double cx = P(d.cx), cy = P(d.cy), r = P(d.radius);
                    if (!Finite(cx) || !Finite(cy) || !Finite(r) || r <= 0) return null;
                    return new CircleShape(id, new Vec2(cx, cy), r, style);
                }
                case (int)ShapeKind.Polygon:
                {
                    double cx = P(d.cx), cy = P(d.cy), r = P(d.radius), rot = P(d.rotation);
                    if (!Finite(cx) || !Finite(cy) || !Finite(r) || r <= 0 || !Finite(rot) || d.sides < 3) return null;
                    return new PolygonShape(id, new Vec2(cx, cy), d.sides, r, rot, style);
                }
                default:
                    return null;
            }
        }

        public static string ToJson(Board b, string name, DateTime savedUtc)
        {
            var file = new DesignFile
            {
                version = Version,
                name = name ?? "",
                savedUtc = FormatUtc(savedUtc),
                board = FromBoard(b),
            };
            return JsonUtility.ToJson(file, true);
        }

        /// <summary>
        /// Reads a design file. False (with a short message for the user) when the text is not JSON, has no version or
        /// board, or was written by a newer version of the app.
        /// </summary>
        public static bool TryParse(string json, out DesignFile file, out string error)
        {
            file = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The file is empty.";
                return false;
            }
            DesignFile parsed;
            try
            {
                parsed = JsonUtility.FromJson<DesignFile>(json);
            }
            catch (Exception e)
            {
                error = "The file is not valid JSON (" + e.Message + ").";
                return false;
            }
            if (parsed == null || parsed.version < 1)
            {
                error = "The file is not a saved design (no version).";
                return false;
            }
            if (parsed.version > Version)
            {
                error = $"The file was saved by a newer version of the app (format {parsed.version}, this app reads up to {Version}).";
                return false;
            }
            if (parsed.board == null)
            {
                error = "The file has no board.";
                return false;
            }
            file = parsed;
            return true;
        }

        public static string FormatUtc(DateTime t) =>
            t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        /// <summary>Parses <see cref="DesignFile.savedUtc"/>; false for a missing or broken date.</summary>
        public static bool TryParseUtc(string s, out DateTime utc)
        {
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
                return true;
            utc = default;
            return false;
        }

        const string Zero = "0";

        /// <summary>
        /// Double to text that parses back to the same bits. "R" is short and round-trips on Unity's runtime; the G17
        /// check is there because "R" is known to miss by one bit for a few values on some .NET runtimes.
        /// </summary>
        public static string D(double v)
        {
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            if (double.IsNaN(v) || BitConverter.DoubleToInt64Bits(P(s)) == BitConverter.DoubleToInt64Bits(v)) return s;
            return v.ToString("G17", CultureInfo.InvariantCulture);
        }

        /// <summary>Text to double; NaN when missing or unreadable.</summary>
        public static double P(string s) =>
            !string.IsNullOrEmpty(s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : double.NaN;

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        static byte Byte(int v) => (byte)Math.Max(0, Math.Min(255, v));
    }
}
