using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Accumulates flat, vertex-coloured triangles in a board's local XZ plane and writes them to one Mesh.
    /// Lines are ribbons of constant width (metres), so a whole board is a handful of draw calls.
    /// </summary>
    public sealed class BoardMeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>(1024);
        readonly List<Color32> _colors = new List<Color32>(1024);
        readonly List<int> _indices = new List<int>(2048);
        readonly List<Vector3> _scratch = new List<Vector3>(128);

        public int VertexCount => _vertices.Count;
        public int TriangleCount => _indices.Count / 3;

        public void Clear()
        {
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();
        }

        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_indices, 0, true);
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
            AddQuad(a - ext - half, a - ext + half, b + ext + half, b + ext - half, color);
        }

        /// <summary>Dashed ribbon; <paramref name="phase"/> carries the pattern across consecutive segments.</summary>
        public void AddDashedSegment(Vector3 a, Vector3 b, float width, Color32 color, float dash, float gap, ref float phase)
        {
            float len = Vector3.Distance(a, b);
            if (len < 1e-7f) return;
            float period = Mathf.Max(dash + gap, 1e-5f);
            float t = 0f;
            while (t < len)
            {
                float inPeriod = Mathf.Repeat(phase + t, period);
                if (inPeriod < dash)
                {
                    float end = Mathf.Min(len, t + (dash - inPeriod));
                    AddSegmentNoCaps(Vector3.Lerp(a, b, t / len), Vector3.Lerp(a, b, end / len), width, color);
                    t = end;
                }
                else
                {
                    t += period - inPeriod;
                }
            }
            phase = Mathf.Repeat(phase + len, period);
        }

        public void AddPolyline(IReadOnlyList<Vector3> points, bool closed, float width, Color32 color,
            bool dashed = false, float dash = 0f, float gap = 0f)
        {
            int n = points.Count;
            if (n < 2) return;
            int segments = closed ? n : n - 1;
            float phase = 0f;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % n];
                if (dashed) AddDashedSegment(a, b, width, color, dash, gap, ref phase);
                else AddSegment(a, b, width, color);
            }
        }

        public void AddCircle(Vector3 center, float radius, int segments, float width, Color32 color,
            bool dashed = false, float dash = 0f, float gap = 0f)
        {
            _scratch.Clear();
            segments = Mathf.Max(8, segments);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                _scratch.Add(center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            AddPolyline(_scratch, true, width, color, dashed, dash, gap);
        }

        public void AddDisc(Vector3 center, float radius, Color32 color, int segments = 14)
        {
            if (radius <= 0f) return;
            int c = _vertices.Count;
            _vertices.Add(center);
            _colors.Add(color);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                _vertices.Add(center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                _colors.Add(color);
            }
            for (int i = 0; i < segments; i++)
            {
                _indices.Add(c);
                _indices.Add(c + 1 + (i + 1) % segments);
                _indices.Add(c + 1 + i);
            }
        }

        /// <summary>Quad from four corners in order (either winding; the shader does not cull).</summary>
        public void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Color32 color)
        {
            int i = _vertices.Count;
            _vertices.Add(p0); _vertices.Add(p1); _vertices.Add(p2); _vertices.Add(p3);
            _colors.Add(color); _colors.Add(color); _colors.Add(color); _colors.Add(color);
            _indices.Add(i); _indices.Add(i + 1); _indices.Add(i + 2);
            _indices.Add(i); _indices.Add(i + 2); _indices.Add(i + 3);
        }

        void AddSegmentNoCaps(Vector3 a, Vector3 b, float width, Color32 color)
        {
            Vector3 d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 1e-7f) return;
            Vector3 half = new Vector3(-d.z, 0f, d.x) / len * (width * 0.5f);
            AddQuad(a - half, a + half, b + half, b - half, color);
        }
    }
}
