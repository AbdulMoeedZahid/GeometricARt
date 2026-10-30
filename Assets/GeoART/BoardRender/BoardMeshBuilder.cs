using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Accumulates flat, vertex-coloured triangles in a board's local XZ plane and writes them to one Mesh.
    /// Lines are ribbons of constant width (metres), so a whole board is a handful of draw calls.
    ///
    /// Anti-aliasing: every ribbon vertex carries its stroke data in UV channel 1 (TEXCOORD1):
    ///   xyz = offset from the ribbon's centre line to this vertex (object space), w = side (+1/-1) * miter scale (&gt;= 1).
    /// The GeoART/BoardUnlit shader uses it to keep strokes at least ~1.5 px wide on screen and to fade the long edges over
    /// one pixel (fwidth). Solid fills (discs, quads) have w = 0 and are drawn as before. The vertex positions themselves are
    /// the exact ribbon edges, so the mesh is also correct without the shader's help.
    /// Solid polylines (circles, polygons) are one strip with mitred joins, so translucent strokes have no overlaps.
    /// </summary>
    public sealed class BoardMeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>(1024);
        readonly List<Color32> _colors = new List<Color32>(1024);
        readonly List<Vector4> _stroke = new List<Vector4>(1024);
        readonly List<int> _indices = new List<int>(2048);
        readonly List<Vector3> _scratch = new List<Vector3>(128);
        readonly List<Vector3> _clean = new List<Vector3>(128);

        /// <summary>Joins sharper than this miter scale (about 29 degrees) are clamped; the corner is then slightly thin.</summary>
        public const float MiterLimit = 4f;

        public int VertexCount => _vertices.Count;
        public int TriangleCount => _indices.Count / 3;

        public void Clear()
        {
            _vertices.Clear();
            _colors.Clear();
            _stroke.Clear();
            _indices.Clear();
        }

        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetUVs(1, _stroke);
            mesh.SetTriangles(_indices, 0, true);
        }

        /// <summary>
        /// Segment count for a circle of <paramref name="radius"/> metres so the chord error stays under
        /// <paramref name="maxError"/> metres (0.04 mm: invisible at phone distance), clamped to [24, 512].
        /// </summary>
        public static int CircleSegmentsFor(float radius, float maxError = 0.00004f)
        {
            if (!(radius > maxError)) return 24;
            float halfAngle = Mathf.Acos(1f - maxError / radius);
            if (!(halfAngle > 0f)) return 512;
            return Mathf.Clamp(Mathf.CeilToInt(Mathf.PI / halfAngle), 24, 512);
        }

        /// <summary>Solid ribbon from a to b; ends are extended by half the width so joints and corners close.</summary>
        public void AddSegment(Vector3 a, Vector3 b, float width, Color32 color)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 1e-7f || width <= 0f) return;
            Vector3 dir = d / len;
            Vector3 half = new Vector3(-dir.z, 0f, dir.x) * (width * 0.5f);
            Vector3 ext = dir * (width * 0.5f);
            AddRibbonQuad(a - ext, b + ext, half, half, 1f, 1f, color);
        }

        /// <summary>Upper bound on dashes per segment, so a degenerate pattern can never stall a frame.</summary>
        public const int MaxDashesPerSegment = 4096;

        /// <summary>Dashed ribbon; <paramref name="phase"/> carries the pattern across consecutive segments.</summary>
        /// <remarks>
        /// Dash k covers [k * period - offset, k * period - offset + dash] along the segment, computed from the integer k.
        /// Do not accumulate the position in float: with dash 8 mm / gap 6 mm, Mathf.Repeat returned 0.00799999945 at
        /// t = 0.022, the step t + (dash - inPeriod) rounded back to t, and the loop never ended (main-thread freeze on
        /// device as soon as the corner picker drew its dashed rubber band).
        /// </remarks>
        public void AddDashedSegment(Vector3 a, Vector3 b, float width, Color32 color, float dash, float gap, ref float phase)
        {
            float len = Vector3.Distance(a, b);
            if (!(len >= 1e-7f) || float.IsInfinity(len)) return;   // also rejects NaN
            float period = Mathf.Max(dash + gap, 1e-5f);
            dash = Mathf.Clamp(dash, 0f, period);
            float offset = Mathf.Repeat(phase, period);
            int count = Mathf.Min(Mathf.CeilToInt((offset + len) / period), MaxDashesPerSegment);
            for (int k = 0; k <= count; k++)
            {
                float start = Mathf.Max(0f, k * period - offset);
                float end = Mathf.Min(len, k * period - offset + dash);
                if (end > start)
                    AddSegmentNoCaps(Vector3.Lerp(a, b, start / len), Vector3.Lerp(a, b, end / len), width, color);
            }
            phase = Mathf.Repeat(phase + len, period);
        }

        /// <summary>
        /// Solid: one continuous strip with mitred joins (square caps on open ends). Dashed: per-segment dashes with the
        /// pattern carried across joints.
        /// </summary>
        public void AddPolyline(IReadOnlyList<Vector3> points, bool closed, float width, Color32 color,
            bool dashed = false, float dash = 0f, float gap = 0f)
        {
            int n = points.Count;
            if (n < 2 || width <= 0f) return;
            if (dashed)
            {
                int segments = closed ? n : n - 1;
                float phase = 0f;
                for (int i = 0; i < segments; i++)
                    AddDashedSegment(points[i], points[(i + 1) % n], width, color, dash, gap, ref phase);
                return;
            }
            AddStrip(points, closed, width, color);
        }

        public void AddCircle(Vector3 center, float radius, int segments, float width, Color32 color,
            bool dashed = false, float dash = 0f, float gap = 0f)
        {
            if (!(radius > 0f)) return;
            _scratch.Clear();
            segments = Mathf.Max(8, segments);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                _scratch.Add(center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            AddPolyline(_scratch, true, width, color, dashed, dash, gap);
        }

        /// <summary>Circle with <see cref="CircleSegmentsFor"/> segments.</summary>
        public void AddCircle(Vector3 center, float radius, float width, Color32 color,
            bool dashed = false, float dash = 0f, float gap = 0f) =>
            AddCircle(center, radius, CircleSegmentsFor(radius), width, color, dashed, dash, gap);

        public void AddDisc(Vector3 center, float radius, Color32 color, int segments = 14)
        {
            if (radius <= 0f) return;
            int c = _vertices.Count;
            AddVertex(center, color, Vector4.zero);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                AddVertex(center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius), color, Vector4.zero);
            }
            for (int i = 0; i < segments; i++)
            {
                _indices.Add(c);
                _indices.Add(c + 1 + (i + 1) % segments);
                _indices.Add(c + 1 + i);
            }
        }

        /// <summary>Solid quad from four corners in order (either winding; the shader does not cull). Not anti-aliased.</summary>
        public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color32 color)
        {
            int i = _vertices.Count;
            AddVertex(p0, color, Vector4.zero);
            AddVertex(p1, color, Vector4.zero);
            AddVertex(p2, color, Vector4.zero);
            AddVertex(p3, color, Vector4.zero);
            AddQuadIndices(i);
        }

        void AddSegmentNoCaps(Vector3 a, Vector3 b, float width, Color32 color)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 1e-7f) return;
            Vector3 half = new Vector3(-d.z, 0f, d.x) / len * (width * 0.5f);
            AddRibbonQuad(a, b, half, half, 1f, 1f, color);
        }

        /// <summary>Ribbon quad between centre points a and b with per-end offsets (half-width * miter) and miter scales.</summary>
        void AddRibbonQuad(Vector3 a, Vector3 b, Vector3 offA, Vector3 offB, float miterA, float miterB, Color32 color)
        {
            int i = _vertices.Count;
            AddVertex(a - offA, color, new Vector4(-offA.x, -offA.y, -offA.z, -miterA));
            AddVertex(a + offA, color, new Vector4(offA.x, offA.y, offA.z, miterA));
            AddVertex(b + offB, color, new Vector4(offB.x, offB.y, offB.z, miterB));
            AddVertex(b - offB, color, new Vector4(-offB.x, -offB.y, -offB.z, -miterB));
            AddQuadIndices(i);
        }

        void AddStrip(IReadOnlyList<Vector3> points, bool closed, float width, Color32 color)
        {
            // Drop repeated points (a zero-length piece has no direction).
            _clean.Clear();
            for (int i = 0; i < points.Count; i++)
                if (_clean.Count == 0 || FlatDistance(points[i], _clean[_clean.Count - 1]) >= 1e-7f) _clean.Add(points[i]);
            if (closed && _clean.Count > 2 && FlatDistance(_clean[0], _clean[_clean.Count - 1]) < 1e-7f)
                _clean.RemoveAt(_clean.Count - 1);
            int n = _clean.Count;
            if (n < 2) return;
            if (n == 2 || (closed && n < 3)) { AddSegment(_clean[0], _clean[1], width, color); return; }

            float half = width * 0.5f;
            int first = _vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = _clean[i];
                bool hasPrev = closed || i > 0, hasNext = closed || i < n - 1;
                Vector3 dIn = hasPrev ? FlatDir(_clean[(i - 1 + n) % n], c) : Vector3.zero;
                Vector3 dOut = hasNext ? FlatDir(c, _clean[(i + 1) % n]) : Vector3.zero;
                Vector3 offset;
                float miter = 1f;
                if (!hasPrev)
                {
                    offset = Perp(dOut) * half;
                    c -= dOut * half;   // square cap
                }
                else if (!hasNext)
                {
                    offset = Perp(dIn) * half;
                    c += dIn * half;
                }
                else
                {
                    Vector3 nIn = Perp(dIn), sum = nIn + Perp(dOut);
                    float len = sum.magnitude;
                    if (len < 1e-4f) offset = nIn * half;   // full reversal: no sensible miter
                    else
                    {
                        Vector3 m = sum / len;
                        miter = Mathf.Min(1f / Mathf.Max(Vector3.Dot(m, nIn), 1e-4f), MiterLimit);
                        offset = m * (half * miter);
                    }
                }
                AddVertex(c - offset, color, new Vector4(-offset.x, -offset.y, -offset.z, -miter));
                AddVertex(c + offset, color, new Vector4(offset.x, offset.y, offset.z, miter));
            }
            int pieces = closed ? n : n - 1;
            for (int i = 0; i < pieces; i++)
            {
                int a = first + 2 * i, b = first + 2 * ((i + 1) % n);
                _indices.Add(a); _indices.Add(a + 1); _indices.Add(b + 1);
                _indices.Add(a); _indices.Add(b + 1); _indices.Add(b);
            }
        }

        void AddVertex(Vector3 p, Color32 color, Vector4 stroke)
        {
            _vertices.Add(p);
            _colors.Add(color);
            _stroke.Add(stroke);
        }

        void AddQuadIndices(int i)
        {
            _indices.Add(i); _indices.Add(i + 1); _indices.Add(i + 2);
            _indices.Add(i); _indices.Add(i + 2); _indices.Add(i + 3);
        }

        static Vector3 Perp(Vector3 dir) => new Vector3(-dir.z, 0f, dir.x);

        static Vector3 FlatDir(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            float len = d.magnitude;
            return len > 1e-9f ? d / len : Vector3.zero;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
