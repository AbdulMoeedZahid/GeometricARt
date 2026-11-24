using System.Collections.Generic;
using GeoART.Contracts;
using GeoART.Engine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Fill triangles per layer for <see cref="BoardView"/> (docs/FILL.md). For a layer with fills: its
    /// <see cref="FillRegions"/> and, per region holding a seed, the region's triangles with the colour of the LAST seed
    /// in it (so overlapping seeds never double-blend). Seeds outside every region draw nothing. Rebuilt for every layer
    /// after <see cref="MarkDirty"/> (BoardView calls it on BoardChanged with Shapes, Layers, Fills, Reset or History:
    /// Layers because band settings move the band outlines that bound a banded layer's regions). Entries of layers
    /// not drawn in a frame are dropped at <see cref="EndFrame"/>.
    /// </summary>
    public sealed class FillCache
    {
        /// <summary>Triangles (3 points each) and their colour.</summary>
        public sealed class Piece
        {
            public readonly List<Vec2> Triangles = new List<Vec2>();
            public RgbaColor Color;
            public int Seed;   // index of the seed in Layer.Fills (draw order)
        }

        sealed class Entry
        {
            public readonly List<Piece> Pieces = new List<Piece>();
            public bool Dirty = true, Seen;
        }

        readonly Dictionary<EntityId, Entry> _entries = new Dictionary<EntityId, Entry>();
        readonly List<EntityId> _drop = new List<EntityId>();
        readonly Dictionary<int, int> _lastSeed = new Dictionary<int, int>();

        public int BuildCount { get; private set; }

        public void MarkDirty()
        {
            foreach (var e in _entries.Values) e.Dirty = true;
        }

        public void BeginFrame()
        {
            foreach (var e in _entries.Values) e.Seen = false;
        }

        /// <summary>The fill pieces of <paramref name="layer"/> (empty when it has no fills or none resolve).</summary>
        public List<Piece> Get(Layer layer)
        {
            if (!_entries.TryGetValue(layer.Id, out Entry e)) _entries[layer.Id] = e = new Entry();
            e.Seen = true;
            if (!e.Dirty) return e.Pieces;
            e.Dirty = false;
            e.Pieces.Clear();
            if (layer.Fills.Count == 0) return e.Pieces;
            BuildCount++;
            var regions = FillRegions.Build(layer);
            _lastSeed.Clear();
            for (int i = 0; i < layer.Fills.Count; i++)
            {
                int face = regions.Locate(layer.Fills[i].Point);
                if (face >= 0) _lastSeed[face] = i;
            }
            foreach (var kv in _lastSeed)
            {
                var piece = new Piece { Color = layer.Fills[kv.Value].Color, Seed = kv.Value };
                regions.Triangulate(kv.Key, piece.Triangles);
                if (piece.Triangles.Count > 0) e.Pieces.Add(piece);
            }
            e.Pieces.Sort((a, b) => a.Seed.CompareTo(b.Seed));
            return e.Pieces;
        }

        public void EndFrame()
        {
            _drop.Clear();
            foreach (var kv in _entries)
                if (!kv.Value.Seen) _drop.Add(kv.Key);
            foreach (var id in _drop) _entries.Remove(id);
        }

        public void Clear() => _entries.Clear();
    }
}
